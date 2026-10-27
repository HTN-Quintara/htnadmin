using System.Security.Cryptography;
using Htn.Api.Auth;
using Htn.Api.Common;
using Htn.Domain.Common;
using Htn.Domain.Files;
using Htn.Domain.Finance;
using Htn.Infrastructure.Audit;
using Htn.Infrastructure.Data;
using Htn.Infrastructure.Outbox;
using Htn.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Htn.Api.Features.Finance;

/// <summary>
/// Frame 26: non-event spending. A board member files a request, the CFO approves or rejects it with a reason, then
/// marks it paid — which posts an entry to the Organization fund.
/// <para>
/// The CFO never decides their own request. They do not need to: holding <c>Finance.Post</c>, they post the expense
/// directly, so requests only ever come from the board members who cannot. That keeps a second pair of eyes on every
/// request without creating a dead end.
/// </para>
/// </summary>
public static class ExpenseRequestEndpoints
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public static RouteGroupBuilder MapExpenseRequests(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/finance/requests").WithTags("Finance");

        g.MapGet("/", List).RequireFinance(FinanceAccess.Board);
        g.MapGet("/{id:guid}", Detail).RequireFinance(FinanceAccess.Board);
        g.MapPost("/", Create).RequireFinance(FinanceAccess.Request)
            .RequireRateLimiting(FinanceRateLimiting.Write).Validate<CreateExpenseRequestRequest>();
        g.MapPost("/{id:guid}/approve", Approve).RequireFinance(FinanceAccess.Approve)
            .RequireRateLimiting(FinanceRateLimiting.Write).ValidateOptional<DecideExpenseRequestRequest>();
        g.MapPost("/{id:guid}/reject", Reject).RequireFinance(FinanceAccess.Approve)
            .RequireRateLimiting(FinanceRateLimiting.Write).Validate<FinanceReasonRequest>();
        g.MapPost("/{id:guid}/pay", Pay).RequireFinance(FinanceAccess.Post)
            .RequireRateLimiting(FinanceRateLimiting.Write);
        // The requester withdraws their own; the CFO may withdraw anyone's. Checked in the handler.
        g.MapPost("/{id:guid}/cancel", Cancel).RequireFinance(FinanceAccess.Request)
            .RequireRateLimiting(FinanceRateLimiting.Write).Validate<FinanceReasonRequest>();

        return g;
    }

    private static async Task<IResult> List(
        HtnDbContext db, ICurrentUser me, ExpenseRequestStatus? status, int? page, int? pageSize, CancellationToken ct)
    {
        var (p, size) = (Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
        var query = db.ExpenseRequests.AsNoTracking().AsQueryable();
        if (status is { } s) query = query.Where(r => r.Status == s);

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(r => r.RequestedAt).ThenByDescending(r => r.Id)
            .Skip((p - 1) * size).Take(size).ToListAsync(ct);
        var items = await ProjectAsync(db, rows, me, ct);
        return TypedResults.Ok(new ExpenseRequestPageDto(items, p, size, total));
    }

    private static async Task<IResult> Detail(Guid id, HtnDbContext db, ICurrentUser me, CancellationToken ct)
    {
        var request = await db.ExpenseRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return TypedResults.NotFound();
        var items = await ProjectAsync(db, [request], me, ct);
        return TypedResults.Ok(items[0]);
    }

    private static async Task<IResult> Create(
        CreateExpenseRequestRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock,
        CancellationToken ct)
    {
        if (await FinanceOps.CategoryAsync(db, req.CategoryId!.Value, "categoryId", ct) is (null, { } error))
            return error;
        if (me.PersonId is not { } personId)
            return Problems.Result(StatusCodes.Status403Forbidden, "You don't have access to this.",
                "Your account is not linked to a person record.", ScopedAuthorization.ForbiddenCode);

        var now = clock.GetUtcNow();
        var request = ExpenseRequest.Create(req.Title!, req.Description, req.Amount!.Value, req.CategoryId!.Value,
            req.NeededBy, personId, now);
        db.ExpenseRequests.Add(request);
        audit.Record(me.Id, FinanceAuditActions.RequestCreate, nameof(ExpenseRequest), request.Id.ToString(),
            after: new { request.Title, request.Amount, request.CategoryId, request.NeededBy });
        await db.SaveChangesAsync(ct);
        return await OkAsync(db, request.Id, me, ct);
    }

    private static async Task<IResult> Approve(
        Guid id, DecideExpenseRequestRequest? req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct) =>
        await DecideAsync(id, db, audit, me, clock, ct, approve: true, req?.Reason);

    private static async Task<IResult> Reject(
        Guid id, FinanceReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct) =>
        await DecideAsync(id, db, audit, me, clock, ct, approve: false, req.Reason);

    private static async Task<IResult> DecideAsync(
        Guid id, HtnDbContext db, IAuditLog audit, ICurrentUser me, TimeProvider clock, CancellationToken ct,
        bool approve, string? reason)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var request = await db.ExpenseRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return TypedResults.NotFound();
        if (request.Status != ExpenseRequestStatus.Pending)
            return FinanceOps.Conflict(
                request.Status == ExpenseRequestStatus.Cancelled
                    ? "This request was withdrawn."
                    : "This request has already been decided.",
                FinanceErrorCodes.RequestDecided);
        // The CFO does not decide their own request; they post the expense directly instead.
        if (request.RequestedById == me.PersonId)
            return FinanceOps.Conflict("Someone else has to decide a request you filed.", FinanceErrorCodes.SelfApproval);

        var now = clock.GetUtcNow();
        var before = new { status = request.Status.ToString() };
        if (approve) request.Approve(me.Id!.Value, reason, now);
        else request.Reject(reason!, me.Id!.Value, now);

        audit.Record(me.Id, approve ? FinanceAuditActions.RequestApprove : FinanceAuditActions.RequestReject,
            nameof(ExpenseRequest), request.Id.ToString(),
            before, new { status = request.Status.ToString(), request.Amount }, reason);
        db.Outbox.Add(OutboxProcessor.ExpenseRequestDecisionEmail(request.Id, now));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await OkAsync(db, request.Id, me, ct);
    }

    /// <summary>
    /// Marks an approved request paid, with the receipt for the money that left. Multipart, so the fields are checked
    /// by hand; the ledger entry it posts is the request's only effect on any balance.
    /// </summary>
    private static async Task<IResult> Pay(
        Guid id, HttpRequest httpRequest, HtnDbContext db, AutoPost autoPost, IFileStorage storage, IAuditLog audit,
        ICurrentUser me, TimeProvider clock, CancellationToken ct)
    {
        var (file, uploadError) = await Uploads.ReadSingleFileAsync(httpRequest, "receipt",
            StoredFilePurposes.FinanceReceiptMaxBytes, "The receipt must be 5 MB or smaller.", ct);
        if (uploadError is not null) return uploadError;
        if (FileSignatures.DetectAttachment(file!.Bytes) is not { } detected)
            return Problems.Field("receipt", "Upload a PNG, JPG or PDF.", FinanceErrorCodes.UnsupportedType);

        var form = httpRequest.Form;
        if (!Guid.TryParse(form["paymentAccountId"], out var accountId) || accountId == Guid.Empty)
            return Problems.Field("paymentAccountId", "An account is required.");
        if (await FinanceOps.AccountErrorAsync(db, accountId, "paymentAccountId", ct) is { } accountError)
            return accountError;

        var now = clock.GetUtcNow();
        var date = DateOnly.TryParse(form["entryDate"], System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : ManilaTime.ManilaDate(now);
        if (date.Year is < 2020 or > 2100) return Problems.Field("entryDate", "Pick a date in this decade.");
        if (await FinanceOps.IsClosedAsync(db, date, ct))
            return FinanceOps.Conflict("That month is closed. Pick a date in an open month, or reopen it first.",
                FinanceErrorCodes.MonthClosed);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await FinanceOps.LockPeriodsAsync(db, ct);
        var request = await db.ExpenseRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return TypedResults.NotFound();
        if (request.Status == ExpenseRequestStatus.Paid)
            return FinanceOps.Conflict("This request is already paid.", FinanceErrorCodes.RequestAlreadyPaid);
        if (request.Status != ExpenseRequestStatus.Approved)
            return FinanceOps.Conflict("Approve the request before marking it paid.", FinanceErrorCodes.RequestNotApproved);

        var (contentType, bytes) = detected;
        var stored = StoredFile.Create(contentType, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)),
            StoredFilePurposes.FinanceReceipt, file.FileName, now, me.Id);
        await storage.SaveAsync(stored.Id, bytes, ct);
        db.StoredFiles.Add(stored);

        var entry = await autoPost.ExpenseRequestPaidAsync(request, accountId, stored.Id, date, me.Id!.Value, now, ct);
        var before = new { status = request.Status.ToString() };
        request.MarkPaid(entry.Id, me.Id!.Value, now);

        audit.Record(me.Id, FinanceAuditActions.RequestPay, nameof(ExpenseRequest), request.Id.ToString(),
            before, new { status = request.Status.ToString(), entryId = entry.Id, request.Amount, entryDate = date });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await OkAsync(db, request.Id, me, ct);
    }

    private static async Task<IResult> Cancel(
        Guid id, FinanceReasonRequest req, HtnDbContext db, IAuditLog audit, ICurrentUser me,
        TimeProvider clock, CancellationToken ct)
    {
        var request = await db.ExpenseRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return TypedResults.NotFound();
        // Your own request, or the CFO's call.
        if (request.RequestedById != me.PersonId && !me.IsCfo)
            return Problems.Result(StatusCodes.Status403Forbidden, "You don't have access to this.",
                "Only the person who filed the request, or the CFO, can withdraw it.", ScopedAuthorization.ForbiddenCode);
        if (request.Status != ExpenseRequestStatus.Pending)
            return FinanceOps.Conflict("This request has already been decided.", FinanceErrorCodes.RequestDecided);

        var before = new { status = request.Status.ToString() };
        request.Cancel(req.Reason!, me.Id!.Value, clock.GetUtcNow());
        audit.Record(me.Id, FinanceAuditActions.RequestCancel, nameof(ExpenseRequest), request.Id.ToString(),
            before, new { status = request.Status.ToString() }, req.Reason);
        await db.SaveChangesAsync(ct);
        return await OkAsync(db, request.Id, me, ct);
    }

    // ---------- Helpers ----------

    private static async Task<IResult> OkAsync(HtnDbContext db, Guid id, ICurrentUser me, CancellationToken ct)
    {
        var request = await db.ExpenseRequests.AsNoTracking().FirstAsync(r => r.Id == id, ct);
        var items = await ProjectAsync(db, [request], me, ct);
        return TypedResults.Ok(items[0]);
    }

    private static async Task<List<ExpenseRequestDto>> ProjectAsync(
        HtnDbContext db, IReadOnlyList<ExpenseRequest> requests, ICurrentUser me, CancellationToken ct)
    {
        if (requests.Count == 0) return [];
        var categoryIds = requests.Select(r => r.CategoryId).Distinct().ToList();
        var categories = await db.LedgerCategories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        // The requester is a person; the decider is a user, like every other finance actor.
        var personIds = requests.Select(r => r.RequestedById).Distinct().ToList();
        var personNames = await db.People.AsNoTracking().Where(p => personIds.Contains(p.Id))
            .Select(p => new { p.Id, Name = p.FirstName + " " + p.LastName })
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var userNames = await FinanceOps.ActorNamesAsync(db,
            [.. requests.Where(r => r.DecidedById is not null).Select(r => r.DecidedById!.Value)], ct);
        var names = new Dictionary<Guid, string>(personNames);
        foreach (var (id, name) in userNames) names[id] = name;

        return [.. requests.Select(r => FinanceViews.Request(r, categories.GetValueOrDefault(r.CategoryId, "—"),
            names, me.IsCfo, me.PersonId))];
    }
}
