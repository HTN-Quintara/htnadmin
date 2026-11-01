using Microsoft.AspNetCore.Diagnostics;
using Npgsql;

namespace Htn.Api.Common;

/// <summary>
/// The database's own guards are the backstop under the endpoints: append-only triggers, check constraints and the
/// deferred constraint triggers that judge a transfer pair or a closed period only at commit. When one of them fires
/// the endpoint logic has usually already refused the same thing with a proper message, so reaching here means
/// something slipped past — a race, or a path that forgot its check.
/// <para>
/// Either way it is a conflict, not a server fault: the caller is told to reload rather than shown a 500 with a trace
/// id. The underlying message is deliberately not forwarded, because it names tables and columns.
/// </para>
/// </summary>
public sealed class DatabaseGuardExceptionHandler(ILogger<DatabaseGuardExceptionHandler> logger) : IExceptionHandler
{
    public const string Code = "database_guard";

    /// <summary>Raised by the plpgsql guards and the check constraints respectively.</summary>
    private static readonly string[] GuardStates = [PostgresErrorCodes.CheckViolation, PostgresErrorCodes.InsufficientPrivilege];

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var postgres = exception as PostgresException ?? exception.InnerException as PostgresException;
        if (postgres is null || !GuardStates.Contains(postgres.SqlState)) return false;

        // Worth a log line every time: in normal operation this should never happen.
        logger.LogError(exception, "A database guard refused a write that the endpoint allowed: {SqlState} {Constraint} {Table}",
            postgres.SqlState, postgres.ConstraintName, postgres.TableName);

        await Problems.WriteAsync(httpContext, StatusCodes.Status409Conflict, "That cannot be done right now",
            "Something changed while you were working. Reload and try again.", Code);
        return true;
    }
}
