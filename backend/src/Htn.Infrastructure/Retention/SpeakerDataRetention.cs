using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Htn.Infrastructure.Retention;

public sealed class RetentionOptions
{
    public const string Section = "Retention";

    /// <summary>Run the background sweep. Tests turn it off and call <see cref="SpeakerDataRetention.RunOnceAsync"/> directly.</summary>
    public bool Enabled { get; set; } = true;

    public int PollMinutes { get; set; } = 60;

    /// <summary>Days after an event ends before its speakers' flight details and ID names are purged.</summary>
    public int SpeakerTravelDays { get; set; } = 30;
}

/// <summary>
/// Nulls a speaker's flight details and government-ID name 30 days after their event ends — the one hard-delete
/// rule in the system. A set-based sweep rather than an outbox message: the outbox is for side effects queued by a
/// specific business event, and nobody queues "30 days from now, forget this" — it must run even though no request
/// triggered it. Idempotent (the <c>purged_at IS NULL</c> guard), and <c>ck_speaker_travel_purged</c> plus the
/// <c>speaker_travel_guard</c> trigger make a half-purge, or re-writing a purged field, impossible even from here.
/// </summary>
public sealed class SpeakerDataRetention(HtnDbContext db, IAuditLog audit, TimeProvider clock, IOptions<RetentionOptions> options)
{
    private const string LockKey = "htn:speaker_retention";

    /// <summary>Runs one sweep. Returns the number of speakers purged. Safe to call from multiple replicas (advisory lock).</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var locked = await db.Database.SqlQuery<bool>(
            $"SELECT pg_try_advisory_xact_lock(hashtext({LockKey})) AS \"Value\"").SingleAsync(ct);
        if (!locked) { await tx.RollbackAsync(ct); return 0; }

        var now = clock.GetUtcNow();
        var cutoffDays = options.Value.SpeakerTravelDays;
        var due = await (
            from t in db.SpeakerTravel
            join sp in db.Speakers on t.SpeakerId equals sp.Id
            join e in db.Events on t.EventId equals e.Id
            where t.PurgedAt == null
                  && (t.IdFullName != null || t.FlyingFrom != null || t.PreferredArrivalTime != null)
                  && e.EndsAt < now.AddDays(-cutoffDays)
            select t).ToListAsync(ct);

        foreach (var travel in due)
        {
            var (hadId, hadFlying, hadArrival) = travel.Purge(now);
            audit.Record(null, "speaker.travel_purged", nameof(Htn.Domain.Speakers.Speaker), travel.SpeakerId.ToString(),
                before: new { cleared = FieldNames(hadId, hadFlying, hadArrival), hadId, hadFlying, hadArrival },
                after: new { purgedAt = now, eventId = travel.EventId },
                reason: $"{cutoffDays}-day retention after the event");
        }
        if (due.Count > 0) await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return due.Count;
    }

    private static string[] FieldNames(bool hadId, bool hadFlying, bool hadArrival)
    {
        var names = new List<string>(3);
        if (hadId) names.Add("idFullName");
        if (hadFlying) names.Add("flyingFrom");
        if (hadArrival) names.Add("preferredArrivalTime");
        return names.ToArray();
    }
}

/// <summary>Polls hourly (configurable) and sweeps expired speaker travel data.</summary>
public sealed class SpeakerDataRetentionWorker(
    IServiceScopeFactory scopes,
    IOptions<RetentionOptions> options,
    ILogger<SpeakerDataRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Speaker data retention worker disabled");
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.PollMinutes)));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var purged = await scope.ServiceProvider
                    .GetRequiredService<SpeakerDataRetention>().RunOnceAsync(stoppingToken);
                if (purged > 0) logger.LogInformation("Purged speaker travel data for {Count} speakers", purged);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Speaker retention sweep failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
