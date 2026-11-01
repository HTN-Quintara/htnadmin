using Htn.Api.Hosting;
using Htn.Api.Tests.Infrastructure;
using Htn.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Htn.Api.Tests.Hosting;

public sealed class EmailDeliveryHealthCheckTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private static Task<HealthCheckResult> Check(MailtrapOptions o, string env, IConfiguration? config = null) =>
        new EmailDeliveryHealthCheck(new FixedMailtrapConfig(o), new FakeEnvironment(env), config ?? Config())
            .CheckHealthAsync(new HealthCheckContext());

    [Fact]
    public async Task Unhealthy_outside_development_when_no_token_resolves()
    {
        Assert.Equal(HealthStatus.Unhealthy, (await Check(new MailtrapOptions(), "Production")).Status);
        Assert.Equal(HealthStatus.Healthy,
            (await Check(new MailtrapOptions(), "Production", Config((HostingSetup.EmailAllowLoggingSender, "true")))).Status);
        Assert.Equal(HealthStatus.Healthy, (await Check(new MailtrapOptions(), "Development")).Status);
        Assert.Equal(HealthStatus.Healthy, (await Check(new MailtrapOptions { ApiToken = "x", Mode = "live" }, "Production")).Status);
    }
}
