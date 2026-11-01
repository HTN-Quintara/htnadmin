using Htn.Infrastructure.Email;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Htn.Api.Hosting;

/// <summary>
/// Outside Development, reports Unhealthy when no Mailtrap token resolves (none saved in Settings, none in the
/// environment), since invites, resets and tickets would stay in the outbox. With Email:AllowLoggingSender=true the
/// operator chose logging explicitly, so it stays Healthy. A saved token that can't be decrypted is also reported.
/// </summary>
public sealed class EmailDeliveryHealthCheck(IMailtrapConfigProvider config, IHostEnvironment env, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (env.IsDevelopment()) return HealthCheckResult.Healthy("Development: email may be logged.");
        var effective = await config.GetAsync(cancellationToken);
        if (effective.Source != EmailConfigSource.None)
            return effective.TokenUnreadable
                ? HealthCheckResult.Degraded("The saved Mailtrap token can't be decrypted; using MAILTRAP_API_TOKEN.")
                : HealthCheckResult.Healthy($"Mailtrap token from {effective.Source}.");
        var message = effective.TokenUnreadable
            ? "The saved Mailtrap token can't be decrypted and no matching MAILTRAP_API_TOKEN is set."
            : "No Mailtrap token is configured (Settings → Email delivery or MAILTRAP_API_TOKEN).";
        return configuration.GetValue<bool>(HostingSetup.EmailAllowLoggingSender)
            ? HealthCheckResult.Healthy(message + " Email:AllowLoggingSender is on, so mail is logged.")
            : HealthCheckResult.Unhealthy(message);
    }
}
