using Htn.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Htn.Api.Auth;

/// <summary>Every capability from <c>.claude/skills/htn-domain/permissions.md</c> as a named policy.</summary>
public static class Policies
{
    public const string PeopleManage = "People.Manage";
    public const string PeopleView = "People.View";
    public const string EventsManage = "Events.Manage";
    public const string EventsView = "Events.View";
    public const string CommitteesManageMembers = "Committees.ManageMembers";
    public const string TasksManage = "Tasks.Manage";
    public const string TasksWork = "Tasks.Work";
    public const string TasksViewBoard = "Tasks.ViewBoard";
    public const string LeaderboardView = "Leaderboard.View";
    public const string RegistrationsManage = "Registrations.Manage";
    public const string CheckInScan = "CheckIn.Scan";
    public const string SponsorsManage = "Sponsors.Manage";
    public const string LeadsManage = "Leads.Manage";
    public const string InvitationsManage = "Invitations.Manage";
    public const string DirectoryManage = "Directory.Manage";
    public const string DirectoryView = "Directory.View";
    public const string SpeakersManage = "Speakers.Manage";
    public const string SuppliersManage = "Suppliers.Manage";
    public const string SuppliersView = "Suppliers.View";
    public const string SuppliersRate = "Suppliers.Rate";
    public const string MerchManage = "Merch.Manage";
    public const string MerchSell = "Merch.Sell";
    public const string PopUpsManage = "PopUps.Manage";
    public const string PopUpsSell = "PopUps.Sell";
    public const string FinanceView = "Finance.View";
    public const string FinancePost = "Finance.Post";
    public const string FinanceApprove = "Finance.Approve";
    public const string FinanceRequest = "Finance.Request";
    public const string SettingsManage = "Settings.Manage";
    public const string SettingsPersonal = "Settings.Personal";

    public static readonly IReadOnlyList<string> All =
    [
        PeopleManage, PeopleView, EventsManage, EventsView, CommitteesManageMembers, TasksManage, TasksWork, TasksViewBoard, LeaderboardView,
        RegistrationsManage, CheckInScan, SponsorsManage, LeadsManage, InvitationsManage, DirectoryManage, DirectoryView,
        SpeakersManage, SuppliersManage, SuppliersView, SuppliersRate, MerchManage, MerchSell, PopUpsManage, PopUpsSell,
        FinanceView, FinancePost, FinanceApprove, FinanceRequest, SettingsManage, SettingsPersonal,
    ];

    /// <summary>Board-only, for routes that need a second, coarser gate next to a resource-scoped policy.</summary>
    public static readonly AuthorizationPolicy BoardOnly =
        new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireRole(Roles.Board).Build();

    /// <summary>
    /// Phase 1 implements People.* and Settings.Personal; Phase 3 adds Events.View and CheckIn.Scan (any signed-in
    /// Board/Goon/Apprentice); Phase 4A adds the four resource-scoped committee/task policies; Phase 4C adds Leaderboard.View. Everything else is
    /// registered as Board-only (Finance.Post/Approve as CFO-only) until its slice adds the scoped handlers.
    /// </summary>
    public static AuthorizationBuilder AddHtnPolicies(this AuthorizationBuilder auth)
    {
        auth.SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        auth.AddPolicy(PeopleView, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon, Roles.Apprentice));
        auth.AddPolicy(PeopleManage, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board));
        auth.AddPolicy(SettingsPersonal, p => p.RequireAuthenticatedUser());
        auth.AddPolicy(EventsView, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon, Roles.Apprentice));
        // Every staff role works the check-in desk (committee leads are goons).
        auth.AddPolicy(CheckInScan, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon, Roles.Apprentice));
        // The leaderboard and "My tasks" are organization-wide screens in the sidebar, so they are not event-scoped:
        // any signed-in staff member may read them (the leaderboard lists only goons and apprentices either way).
        auth.AddPolicy(LeaderboardView, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon, Roles.Apprentice));

        // Finance.View is "may know the finance module exists": the board, plus the single finance assistant, who is a
        // goon and may add ledger entries that stay Pending. Everyone else gets 404 from the finance gate, not 403.
        auth.AddPolicy(FinanceView, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
            ctx.User.IsInRole(Roles.Board) || ctx.User.HasClaim(HtnClaimTypes.FinanceAssistant, "true")));
        auth.AddPolicy(FinanceRequest, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board));
        auth.AddPolicy(FinancePost, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board).RequireClaim(HtnClaimTypes.Cfo, "true"));
        auth.AddPolicy(FinanceApprove, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board).RequireClaim(HtnClaimTypes.Cfo, "true"));

        // Resource-scoped (see CommitteeScopedHandler). The role list is the coarse gate: Committees.ManageMembers and
        // Tasks.Manage can only ever be held by the board or a committee lead, who is always a goon. The requirement
        // then decides per committee / event / task, and with no resource it answers "may be allowed" for
        // /api/auth/me, which is why the payloads carry explicit canManage flags.
        auth.AddPolicy(CommitteesManageMembers, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon)
            .AddRequirements(new CommitteeScopedRequirement(CommitteeAccess.ManageMembers)));
        auth.AddPolicy(TasksManage, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon)
            .AddRequirements(new CommitteeScopedRequirement(CommitteeAccess.TasksManage)));
        auth.AddPolicy(TasksWork, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon, Roles.Apprentice)
            .AddRequirements(new CommitteeScopedRequirement(CommitteeAccess.TasksWork)));
        auth.AddPolicy(TasksViewBoard, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon, Roles.Apprentice)
            .AddRequirements(new CommitteeScopedRequirement(CommitteeAccess.ViewBoard)));
        // Speakers.Manage (and agenda writes): board, or the lead of the event's Programs committee. A Programs lead
        // is a goon, so the coarse role gate is Board/Goon; the handler decides per event, and endpoints that know the
        // event re-run the policy with an EventScope. (Agenda sits in the events area but, per the agreed scope, its
        // writes use this policy rather than the board-only Events.Manage.)
        auth.AddPolicy(SpeakersManage, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon)
            .AddRequirements(new CommitteeScopedRequirement(CommitteeAccess.ProgramsManage)));

        // Directory.Manage (Phase 7A): board only, like Sponsors.Manage and Leads.Manage.
        auth.AddPolicy(DirectoryManage, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board));
        // Directory.View: board, or any committee lead of any event — a lead chases its own org's contacts, not just
        // its own event's, so this is deliberately not event-scoped (unlike Tasks.ViewBoard). ICommitteeContext
        // already has exactly "leads something, anywhere" as LeadsAnythingAsync (the same check the resource-less
        // branch of CommitteeScopedHandler uses), so Directory.View is a plain RequireAssertion rather than a new
        // CommitteeScopedRequirement/resource type.
        auth.AddPolicy(DirectoryView, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board, Roles.Goon)
            .RequireAssertion(async ctx => ctx.User.IsInRole(Roles.Board)
                || await (ctx.Resource switch
                {
                    HttpContext http => http.RequestServices.GetRequiredService<ICommitteeContext>().LeadsAnythingAsync(),
                    _ => Task.FromResult(false),
                })));

        string[] implemented =
        [
            PeopleView, PeopleManage, SettingsPersonal, EventsView, CheckInScan,
            FinanceView, FinanceRequest, FinancePost, FinanceApprove,
            CommitteesManageMembers, TasksManage, TasksWork, TasksViewBoard, LeaderboardView, SpeakersManage,
            DirectoryManage, DirectoryView,
        ];
        foreach (var name in All.Except(implemented))
            auth.AddPolicy(name, p => p.RequireAuthenticatedUser().RequireRole(Roles.Board));

        return auth;
    }
}
