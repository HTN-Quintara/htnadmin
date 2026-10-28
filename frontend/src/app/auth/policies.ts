/** Policy names from .claude/skills/htn-domain/permissions.md used by the UI. The server enforces them. */
export const Policies = {
  PeopleView: 'People.View',
  PeopleManage: 'People.Manage',
  SettingsPersonal: 'Settings.Personal',
  SettingsManage: 'Settings.Manage',
  /** Board, or the finance assistant: "may know the finance module exists". Routes 404 to everyone else. */
  FinanceView: 'Finance.View',
  /** CFO only: post money in or out, transfer, void, reconcile, close a month, edit a budget, pay a request. */
  FinancePost: 'Finance.Post',
  /** CFO only: approve a pending entry, or decide an expense request. */
  FinanceApprove: 'Finance.Approve',
  /** Any board member: file an expense request. */
  FinanceRequest: 'Finance.Request',
  /** Any signed-in user: published events. Drafts stay manager-only on the server. */
  EventsView: 'Events.View',
  EventsManage: 'Events.Manage',
  /** Board only: manual registration, mark paid, cancel, revert, resend, export. */
  RegistrationsManage: 'Registrations.Manage',
  /** Any signed-in user: phone scanner and check-in desk. The event must be published. */
  CheckInScan: 'CheckIn.Scan',
  /**
   * Resource-scoped (Board, or the lead of that committee). The flat list only says "may be
   * allowed": screens gate on the per-resource flags the API sends (canManage, canCreateCommittee).
   */
  CommitteesManageMembers: 'Committees.ManageMembers',
  /** Resource-scoped: Board, or anyone in a committee of that event. */
  TasksViewBoard: 'Tasks.ViewBoard',
  /** Resource-scoped: Board, or the lead of the task's committee. */
  TasksManage: 'Tasks.Manage',
  /** Resource-scoped: Board, the committee lead, or someone assigned to the task. */
  TasksWork: 'Tasks.Work',
  /** Any signed-in user: the points leaderboard. Board members can read it but never appear on it. */
  LeaderboardView: 'Leaderboard.View',
  /** Board only: sponsors, tiers, partners and their group links. */
  SponsorsManage: 'Sponsors.Manage',
  /** Board only: sponsor and speaker leads. */
  LeadsManage: 'Leads.Manage',
  /** Board, or the lead of the event's Programs committee: speaker links, travel and the agenda. */
  SpeakersManage: 'Speakers.Manage',
  /** Board, or any committee lead anywhere: read the global directory of organizations and contacts. */
  DirectoryView: 'Directory.View',
  /** Board only: create/edit organizations, contacts and verification actions. */
  DirectoryManage: 'Directory.Manage',
  /** Board only: invitations per event. Unlike Directory, there is no committee-lead exception. */
  InvitationsManage: 'Invitations.Manage',
  /** Board, or any committee lead anywhere: read the global supplier catalog and per-event hires. */
  SuppliersView: 'Suppliers.View',
  /** Board only: create/edit suppliers, documents, hires, money and status. */
  SuppliersManage: 'Suppliers.Manage',
  /** Resource-scoped: board, or the lead of the hire's owning committee, once the event has ended. */
  SuppliersRate: 'Suppliers.Rate',
  /** Board only: merch catalog, variants and per-event discounts. */
  MerchManage: 'Merch.Manage',
  /** Resource-scoped: board, or any committee lead of that event: the merch dashboard and POS. */
  MerchSell: 'Merch.Sell',
} as const

export type Policy = (typeof Policies)[keyof typeof Policies] | (string & {})
