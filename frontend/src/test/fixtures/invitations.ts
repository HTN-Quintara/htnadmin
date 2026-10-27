// API fixtures for the invitations slice (frame 19), shaped like InvitationListItemDto / EventInvitationTemplateDto.

export const invitation = (over: Record<string, unknown> = {}) => ({
  id: 'inv1',
  directoryOrgId: 'o1',
  orgName: 'Department of Information and Communications Technology',
  orgKind: 'Government',
  status: 'NotSent',
  sentAt: null,
  lastStatusAt: null,
  primaryContactVerified: true,
  ...over,
})

export const unverifiedInvitation = (over: Record<string, unknown> = {}) =>
  invitation({
    id: 'inv2',
    directoryOrgId: 'o3',
    orgName: 'University of the Philippines Diliman',
    orgKind: 'University',
    primaryContactVerified: false,
    ...over,
  })

export const sentInvitation = (over: Record<string, unknown> = {}) =>
  invitation({
    id: 'inv3',
    directoryOrgId: 'o2',
    orgName: 'CloudCorp PH',
    orgKind: 'Company',
    status: 'Sent',
    sentAt: '2026-10-20T00:00:00Z',
    lastStatusAt: '2026-10-20T00:00:00Z',
    ...over,
  })

export const invitationsList = () => [invitation(), unverifiedInvitation(), sentInvitation()]

export const invitationTemplate = (over: Record<string, unknown> = {}) => ({
  eventId: 'e1',
  subject: 'Invitation to [date]',
  body: 'Dear [addressee.salutation] [addressee.name],\n\nWe invite you.',
  updatedAt: '2026-10-01T00:00:00Z',
  ...over,
})

export const invitationDetail = (over: Record<string, unknown> = {}) => ({
  id: 'inv1',
  eventId: 'e1',
  directoryOrgId: 'o1',
  orgName: 'Department of Information and Communications Technology',
  status: 'Sent',
  contactId: 'c1',
  sentAt: '2026-10-27T00:00:00Z',
  lastStatusAt: '2026-10-27T00:00:00Z',
  letterSubjectSnapshot: 'Invitation to October 27, 2026',
  letterBodySnapshot: 'Dear The Secretary Dr. Lara Gomez,\n\nWe invite you.',
  history: [{ id: 'ev1', kind: 'Sent', at: '2026-10-27T00:00:00Z', byId: 'u1', note: null }],
  ...over,
})
