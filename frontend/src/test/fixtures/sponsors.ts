// API fixtures for the sponsors slice (frame 13), shaped like the SponsorsPage / Partners DTOs.

export const tier = (over: Record<string, unknown> = {}) => ({
  id: 't1',
  class: 'Gold',
  name: 'Lanyard & ID',
  priceText: '₱80,000',
  slots: 3,
  slotsTaken: 1,
  compedSeats: 3,
  perks: ['Logo on lanyard', 'Booth (2 × 2 m)'],
  isActive: true,
  ...over,
})

export const sponsor = (over: Record<string, unknown> = {}) => ({
  id: 's1',
  kind: 'Organization',
  name: 'CloudCorp PH',
  displayName: null,
  isAnonymous: false,
  contactName: 'Kai Cruz',
  contactRole: 'Partnerships',
  contactEmail: null,
  contactMobile: null,
  status: 'Confirmed',
  tierId: 't1',
  tierClass: 'Gold',
  tierName: 'Lanyard & ID',
  contributionType: 'Cash',
  cashAmount: 80000,
  inKindDescription: null,
  inKindItemCount: null,
  amountText: '₱80,000',
  logoFileId: null,
  websiteUrl: null,
  needsAssets: true,
  compsUsed: 0,
  compsEntitlement: 3,
  perksDelivered: 1,
  perksTotal: 2,
  cashReceived: false,
  cashReceivedAt: null,
  cashReference: null,
  ...over,
})

export const sponsorsPage = (over: Record<string, unknown> = {}) => ({
  eventId: 'e1',
  eventName: 'HTN Con 2026',
  kpis: [
    { label: 'Cash committed', value: '₱485,000', sub: '₱345,000 received · ₱35,000 from individuals' },
    { label: 'In-kind sponsors', value: '2', sub: 'Confirmed in-kind contributions' },
    { label: 'Comped seats', value: '0 / 3', sub: 'Linked to registrations' },
    { label: 'Missing logo or website', value: '1', sub: 'Confirmed organizations to follow up' },
  ],
  tiers: [tier()],
  sponsors: [sponsor()],
  ...over,
})

export const sponsorDetail = (over: Record<string, unknown> = {}) => ({
  sponsor: sponsor(),
  perks: [
    { id: 'pk1', text: 'Logo on lanyard', delivered: true, removed: false, sortOrder: 0 },
    { id: 'pk2', text: 'Booth (2 × 2 m)', delivered: false, removed: false, sortOrder: 1 },
  ],
  ...over,
})

export const partner = (over: Record<string, unknown> = {}) => ({
  id: 'ep1',
  nameKey: 'dict',
  displayName: 'DICT',
  orgType: 'Government agency',
  registered: 34,
  paid: 31,
  checkedIn: 0,
  countForBasis: 31,
  status: 'Partner',
  remaining: 0,
  isPartner: true,
  isTagged: true,
  assets: 'Logo & website ✓',
  logoFileId: null,
  websiteUrl: null,
  hasGroupLink: false,
  groupJoined: 31,
  ...over,
})

export const partnersPage = (over: Record<string, unknown> = {}) => ({
  settings: { threshold: 20, countBasis: 'PaidOnly', groupLinkEnabled: false },
  partners: [partner()],
  unmatched: [{ nameKey: 'mapua', sampleName: 'Mapúa University', registered: 18, paid: 15 }],
  partnerPerks: ['Logo on the partners wall and website', 'Certificate of partnership'],
  ...over,
})
