import { isApiError } from './api'

export const RATE_LIMITED_MESSAGE = 'Too many attempts. Please wait a minute and try again.'
export const INVITE_INVALID_MESSAGE = 'This invite link is invalid or has expired. Ask an admin to resend it.'
export const RESET_INVALID_MESSAGE = 'This reset link is invalid or has expired.'
/** Nobody signs off their own work: the only Lead on a task can't approve it. */
export const NEEDS_OTHER_APPROVER_MESSAGE = 'Another committee lead or a board member has to approve this.'

/** Human messages for the `code` extension the API puts on ProblemDetails. */
const codeMessages: Record<string, string> = {
  rate_limited: RATE_LIMITED_MESSAGE,
  invite_invalid: INVITE_INVALID_MESSAGE,
  reset_invalid: RESET_INVALID_MESSAGE,
  antiforgery: 'Your session security token expired. Refresh the page and try again.',
  already_active: 'This account is already active, so there is no invite to resend. Send a password reset link instead.',
  no_login: 'This person has no login. Members are roster only.',
  not_active: "This account isn't active yet. Resend the invite instead.",
  person_deactivated: 'This person is deactivated.',
  self_deactivate: "You can't deactivate your own account.",
  '2fa_already_enabled': 'Two-step verification is already on.',
  invalid_promotion: 'People move one step at a time: Member → Apprentice → Goon. Anyone can be seated on the Board.',
  email_locked: 'The email can only change before the person has activated their login.',
  duplicate_email: 'Someone with this email is already in the organization.',
  import_has_errors: 'Some rows have errors. Fix the file and preview it again.',
  self_change: "You can't change your own role. Ask another board member.",
  wrong_level: "This person's level doesn't fit that role.",
  low_contrast: 'This color is too light for white text. Pick a darker shade (contrast 4.5:1 or more).',
  no_tickets: 'Add at least one active ticket type before publishing.',
  duplicate_slug: 'Another event already uses this page link. Pick a different one.',
  slug_locked: 'The page link is locked once the event has been published.',
  price_locked: "The price can't change after tickets have been sold.",
  duplicate_code: 'This voucher code is already used for this event.',
  duplicate_main_event: 'Another event is already the main event for that membership year.',
  registration_closed: 'Registration for this event is closed.',
  group_size: 'Fill in the details for every member of the group.',
  not_pending: 'This registration is no longer waiting for payment.',
  not_paid: 'This registration is not paid yet.',
  too_many_uploads: 'Too many uploads for this registration. Reply to the billing email instead.',
  invalid_payment_account: "That payment account doesn't match the payment method.",
  // Speakers (frame 22/23).
  code_invalid: "That code is not right.",
  code_expired: 'That code expired. Request a new code.',
  code_locked: 'Too many attempts. Try again later.',
  code_resend_limit: 'Too many requests. Try again later.',
  link_expired: 'This link no longer works. Ask the Programs team for a new one.',
  session_required: 'Your session has expired. Request a new code.',
  event_closed: 'The confirmation window for this event has closed.',
  no_live_link: 'There is no live link to act on.',
  no_photo: 'There is no photo to approve.',
  voucher_code_exhausted: 'Could not generate a voucher code. Try again.',
  unknown_committee: 'That committee is not part of this event.',
  // Leads (frame 17).
  invalid_transition: "That move isn't allowed from the lead's current stage.",
  lead_closed: 'This lead is closed. Reopen it to edit the details.',
  reason_required: 'Add the speaker\'s email before confirming, so the link can be sent.',
  // Sponsors & partners (frame 13).
  already_received: "This sponsor's cash was already recorded.",
  not_received: "This sponsor's cash was not recorded as received.",
  cash_posted: "This sponsor's cash is in the ledger. Reverse the receipt first.",
  period_closed_use_finance: "This sponsor's earlier cash sits in a closed month. Book the correction from Finance.",
  no_cash: 'This sponsor has no cash contribution to record.',
  not_confirmed: 'Confirm the sponsor first.',
  unknown_tier: 'That tier no longer exists. Pick another one.',
  threshold_not_reached: "This partner hasn't reached the threshold yet.",
  already_tagged: 'That organization is already a partner.',
  // Finance (frames 24-26).
  month_closed: 'This month is closed. Reopen it, or post a correcting entry dated today.',
  month_has_pending: 'Some entries are still waiting for approval. Approve or void them first.',
  month_unbalanced: 'Some accounts are not balanced yet. Reconcile them before closing the month.',
  previous_month_open: 'An earlier month is still open. Close months in order.',
  month_already_closed: 'This month is already closed.',
  month_not_closed: 'This month is not closed.',
  reopen_latest_only: 'Only the most recently closed month can be reopened.',
  entry_not_pending: 'This entry is no longer waiting for approval.',
  entry_voided: 'This entry was voided.',
  already_posted: 'This entry is already posted.',
  self_approval: 'Someone else has to approve this.',
  receipt_required: 'Attach the receipt. Every entry you add needs one.',
  transfer_same_fund: 'Pick two different funds for a transfer.',
  fund_closed: 'This fund is closed.',
  no_surplus: 'This event fund has nothing left to transfer.',
  sweep_not_voidable: 'A surplus transfer cannot be voided. Record a transfer back to the event fund instead.',
  database_guard: 'Something changed while you were working. Reload and try again.',
  not_reconcilable: 'This account is not reconciled against a statement.',
  unknown_category: 'That category no longer exists. Pick another one.',
  category_inactive: 'That category is no longer in use. Pick another one.',
  unknown_account: 'That account no longer exists.',
  account_inactive: 'That account is deactivated. Pick another one.',
  unknown_fund: 'That fund no longer exists.',
  request_decided: 'This request has already been decided.',
  request_not_approved: 'Approve the request before marking it paid.',
  request_already_paid: 'This request is already paid.',

  unknown_payment_account: 'One of the payment accounts no longer exists.',
  no_email: 'Your account has no email address for the test.',
  rejected: "We couldn't submit the form. Please try again.",
  sold_out: 'This ticket just sold out. Pick another ticket.',
  ticket_unavailable: "This ticket isn't available anymore. Pick another ticket.",
  invalid_voucher: "This voucher code can't be used.",
  invalid_status: 'This registration changed in the meantime. Refresh and try again.',
  already_checked_in: "This attendee has already checked in, so the payment can't be reverted.",
  capacity_exceeded: 'This event or ticket is full now. Add a reason to mark it paid anyway.',
  check_in_closed: 'Check-in is open from a day before the event until a day after it ends.',
  no_check_in: 'Only people who have checked in at an event can be Members.',
  proof_invalid: 'This upload link is invalid or has expired.',
  duplicate_name: 'Another committee in this event already uses this name.',
  has_open_tasks: 'This committee still has open tasks. Move or finish them first.',
  already_lead: 'This goon already leads another committee in this event.',
  already_in_committee: 'This person is already in another committee for this event.',
  is_lead: 'This person leads the committee. Change the lead first.',
  needs_approval: 'Only the committee lead or an admin can finish a task. Move it to Review and ask them to approve it.',
  needs_other_approver: NEEDS_OTHER_APPROVER_MESSAGE,
  reopen_first: 'This task still has live points awards. Reopen it first.',
  no_lead: 'Add a lead before approving. A task needs someone in the Lead role so points can be awarded.',
  task_done: 'This task is done. Reopen it before changing points, assignees or details.',
  volunteer_role: 'Volunteers help as Assistant only, and never earn points.',
  already_assigned: 'This person is already on the task.',
  not_author: 'Only the author can edit a comment.',
  too_many_attachments: 'This task already has 10 attachments. Remove one before adding another.',
  task_archived: 'This task is archived. Restore it before changing anything.',
  not_archived: 'This task is not archived.',
  not_done: 'This task is not done, so there is nothing to reopen.',
  too_many_items: 'This checklist is full. Remove an item before adding another.',
  committee_inactive: 'That committee is no longer active. Move the task to an active committee.',
  unsupported_type: 'Attach a PNG, JPG or PDF file.',
  file_too_large: 'This file is larger than 5 MB. Attach a smaller file.',
}

/** Message for a known API error code, if any. */
export function messageForCode(code: string | undefined): string | undefined {
  return code ? codeMessages[code] : undefined
}

/**
 * Human message for a failed request. Known `code`s win; then `byStatus` overrides; then defaults.
 */
export function errorMessage(error: unknown, byStatus: Partial<Record<number, string>> = {}): string {
  if (isApiError(error)) {
    const byCode = messageForCode(error.code)
    if (byCode) return byCode
    if (error.status === 429) return RATE_LIMITED_MESSAGE
    const override = byStatus[error.status]
    if (override) return override
    if (error.status === 403) return 'You do not have access to do that.'
    if (error.status >= 500) return 'Something went wrong on our side. Try again in a moment.'
    return error.problem.detail ?? error.problem.title ?? 'The request could not be completed.'
  }
  return 'We could not reach HTN Admin. Check your connection and try again.'
}
