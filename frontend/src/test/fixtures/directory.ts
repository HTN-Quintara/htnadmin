// API fixtures for the directory slice (frame 18), shaped like DirectoryOrgDto / DirectoryOrgDetailDto.
// Mirrors the three orgs DevSeeder.EnsureDirectoryAsync seeds, for consistency with the backend sample data.

export const contact = (over: Record<string, unknown> = {}) => ({
  id: 'c1',
  orgId: 'o1',
  position: 'Undersecretary',
  holderName: 'Dr. Lara Gomez',
  email: 'lara.gomez@dict.gov.ph',
  phone: null,
  isPrimary: true,
  verifiedAt: '2026-10-25T00:00:00Z',
  needsCheck: false,
  ccEmails: [],
  ...over,
})

export const historyEntry = (over: Record<string, unknown> = {}) => ({
  id: 'h1',
  contactId: 'c1',
  kind: 'StillCorrect',
  at: '2026-10-25T00:00:00Z',
  byId: 'u1',
  note: null,
  priorPosition: null,
  priorHolderName: null,
  ...over,
})

export const org = (over: Record<string, unknown> = {}) => ({
  id: 'o1',
  kind: 'Government',
  name: 'Department of Information and Communications Technology',
  nameKey: 'department-of-information-and-communications-technology',
  addresseeSalutation: 'The Secretary',
  websiteUrl: 'https://dict.gov.ph',
  logoFileId: null,
  notes: 'National ICT policy agency.',
  isActive: true,
  needsCheck: false,
  ...over,
})

export const cloudcorpOrg = (over: Record<string, unknown> = {}) =>
  org({
    id: 'o2',
    kind: 'Company',
    name: 'CloudCorp PH',
    nameKey: 'cloudcorp-ph',
    addresseeSalutation: null,
    websiteUrl: 'https://cloudcorp.ph',
    notes: null,
    ...over,
  })

export const universityOrg = (over: Record<string, unknown> = {}) =>
  org({
    id: 'o3',
    kind: 'University',
    name: 'University of the Philippines Diliman',
    nameKey: 'university-of-the-philippines-diliman',
    addresseeSalutation: 'The Chancellor',
    websiteUrl: 'https://upd.edu.ph',
    notes: 'Partner campus for recruitment drives.',
    needsCheck: true,
    ...over,
  })

export const orgsList = () => [org(), cloudcorpOrg(), universityOrg()]

export const cloudcorpContact = (over: Record<string, unknown> = {}) =>
  contact({
    id: 'c2',
    orgId: 'o2',
    position: 'Partnerships',
    holderName: 'Kai Cruz',
    email: 'kai.cruz@cloudcorp.ph',
    phone: '+639171234567',
    verifiedAt: '2026-10-22T00:00:00Z',
    ccEmails: ['finance@cloudcorp.ph'],
    ...over,
  })

export const universityContact = (over: Record<string, unknown> = {}) =>
  contact({
    id: 'c3',
    orgId: 'o3',
    position: 'Director, Student Affairs',
    holderName: 'Prof. Ana Reyes',
    email: 'ana.reyes@upd.edu.ph',
    phone: null,
    verifiedAt: '2026-06-30T00:00:00Z',
    needsCheck: true,
    ...over,
  })

export const orgDetail = (over: Record<string, unknown> = {}) => ({
  org: org(),
  contacts: [contact()],
  history: [historyEntry()],
  ...over,
})
