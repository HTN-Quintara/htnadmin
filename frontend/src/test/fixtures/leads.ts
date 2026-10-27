// API fixtures for the leads slice (frame 17), shaped like the LeadsBoard DTO.

export const card = (over: Record<string, unknown> = {}) => ({
  id: 'l1',
  kind: 'Sponsor',
  status: 'Contacted',
  subject: 'Globe Business',
  contactName: 'Rina Cruz',
  contactRole: 'Partnerships Mgr',
  contactEmail: null,
  contactMobile: null,
  askTierId: null,
  askTierName: null,
  askAmount: 100000,
  askNote: 'Gold',
  ask: 'Ask: Gold · ₱100,000',
  ownerPersonId: 'p9',
  ownerName: 'Kai Cruz',
  ownerInitials: 'KC',
  nextStep: 'Follow up',
  nextStepDue: '2026-10-24',
  overdue: true,
  fromDirectory: false,
  sponsorId: null,
  speakerId: null,
  ...over,
})

const emptyColumn = (status: string, name: string) => ({ status, name, count: 0, cards: [] as unknown[] })

export const leadsBoard = (over: Record<string, unknown> = {}) => ({
  kind: 'Sponsor',
  stats: [
    { label: 'In pipeline', value: '₱580,000' },
    { label: 'Confirmed', value: '₱450,000' },
    { label: 'Overdue follow-ups', value: '1' },
  ],
  columns: [
    emptyColumn('Prospect', 'Prospect'),
    { status: 'Contacted', name: 'Contacted', count: 1, cards: [card()] },
    emptyColumn('InTalks', 'In talks'),
    { status: 'Confirmed', name: 'Confirmed', count: 1, cards: [card({ id: 'l2', subject: 'ToteWorks', status: 'Confirmed', sponsorId: 's9', overdue: false, nextStep: null })] },
    emptyColumn('Declined', 'Declined'),
  ],
  ...over,
})
