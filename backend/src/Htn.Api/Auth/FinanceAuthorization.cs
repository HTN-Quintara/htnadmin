using Htn.Api.Common;
using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Htn.Api.Auth;

/// <summary>
/// What a caller must be to use one finance route. <see cref="Policies"/> holds the four named policies from the
/// permissions matrix; this is the per-route right the gate checks once the caller is known to be allowed into the
/// module at all.
/// </summary>
public enum FinanceAccess
{
    /// <summary>Any board member: the reads (overview, ledger export, balancing, budgets, requests, audit).</summary>
    Board,

    /// <summary>Board or the finance assistant: the ledger list, an entry, its receipt, the funds and categories.</summary>
    Ledger,

    /// <summary>The CFO (posts straight away) or the finance assistant (lands Pending): adding an entry.</summary>
    Create,

    /// <summary>CFO only: post, transfer, void, reconcile, close or reopen a month, sweep a surplus, edit a budget.</summary>
    Post,

    /// <summary>CFO only: approve a pending entry, decide an expense request.</summary>
    Approve,

    /// <summary>Any board member: file an expense request.</summary>
    Request,
}

internal sealed record FinanceAccessMetadata(FinanceAccess Access);

public static class FinanceGate
{
    /// <summary>
    /// Guards one finance route. Finance routes carry this <b>instead of</b> <c>RequireAuthorization</c>, because the
    /// module is hidden rather than merely forbidden: <c>permissions.md</c> requires a <b>404</b> for anyone who is
    /// not board and not the finance assistant, so the existence of finance is not revealed, and
    /// <c>RequireAuthorization</c> can only ever produce a 403. Anonymous callers still get 401 from the global
    /// fallback policy, and board two-factor is still enforced by the two-factor middleware.
    /// </summary>
    public static TBuilder RequireFinance<TBuilder>(this TBuilder builder, FinanceAccess access)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new FinanceAccessMetadata(access));
        builder.AddEndpointFilter<TBuilder, FinanceGateFilter>();
        return builder;
    }
}

/// <summary>
/// Two questions in order: may this caller know the finance module exists (404 if not), and may they do this
/// particular thing (403 if not). Inside the module, denials are honest.
/// </summary>
internal sealed class FinanceGateFilter(IAuthorizationService auth, ICurrentUser me) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        // Finance.View is "may know this module exists": the board, plus the one finance assistant.
        if (!await auth.AllowsAsync(http, null, Policies.FinanceView)) return TypedResults.NotFound();

        var access = http.GetEndpoint()?.Metadata.GetMetadata<FinanceAccessMetadata>()?.Access
            ?? throw new InvalidOperationException("A finance route must declare its FinanceAccess.");

        var allowed = access switch
        {
            // Everyone who got past the gate: the board and the assistant both read the ledger.
            FinanceAccess.Ledger => true,
            FinanceAccess.Board => http.User.IsInRole(Roles.Board),
            FinanceAccess.Request => await auth.AllowsAsync(http, null, Policies.FinanceRequest),
            FinanceAccess.Approve => await auth.AllowsAsync(http, null, Policies.FinanceApprove),
            FinanceAccess.Post => await auth.AllowsAsync(http, null, Policies.FinancePost),
            // The assistant may add an entry; the handler forces it to Pending. The CFO goes through the named
            // policy rather than the flag alone, so this branch carries the same Board check as Finance.Post.
            FinanceAccess.Create => me.IsFinanceAssistant || await auth.AllowsAsync(http, null, Policies.FinancePost),
            _ => false,
        };

        return allowed
            ? await next(context)
            : Problems.Result(StatusCodes.Status403Forbidden, "You don't have access to this.",
                Detail(access), ScopedAuthorization.ForbiddenCode);
    }

    private static string Detail(FinanceAccess access) => access switch
    {
        FinanceAccess.Board => "Only the board can see this.",
        FinanceAccess.Request => "Only the board can file an expense request.",
        FinanceAccess.Create => "Only the CFO and the finance assistant can add entries.",
        _ => "Only the CFO can do this.",
    };
}
