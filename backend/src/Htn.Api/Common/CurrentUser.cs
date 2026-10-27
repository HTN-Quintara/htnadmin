using System.Security.Claims;
using Htn.Infrastructure.Identity;

namespace Htn.Api.Common;

public interface ICurrentUser
{
    Guid? Id { get; }
    Guid? PersonId { get; }

    /// <summary>The one board member who holds the CFO flag. In finance, this is what separates posting from viewing.</summary>
    bool IsCfo { get; }

    /// <summary>The one goon who may add ledger entries, which stay Pending until the CFO approves them.</summary>
    bool IsFinanceAssistant { get; }
}

public sealed class CurrentUser(IHttpContextAccessor http) : ICurrentUser
{
    public Guid? Id => Parse(ClaimTypes.NameIdentifier);
    public Guid? PersonId => Parse(HtnClaimTypes.PersonId);
    public bool IsCfo => Flag(HtnClaimTypes.Cfo);
    public bool IsFinanceAssistant => Flag(HtnClaimTypes.FinanceAssistant);

    private Guid? Parse(string type) =>
        Guid.TryParse(http.HttpContext?.User.FindFirstValue(type), out var id) ? id : null;

    private bool Flag(string type) => http.HttpContext?.User.HasClaim(type, "true") ?? false;
}
