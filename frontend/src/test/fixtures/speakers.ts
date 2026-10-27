// API fixtures for the speakers slice (frame 22/23).

export const speakerRow = (over: Record<string, unknown> = {}) => ({
  id: 'sp1',
  name: 'Dr. Lara Gomez',
  session: 'Keynote · 9:15 AM',
  photoState: 'Uploaded · approve for website',
  linkState: 'Opened',
  linkStateSub: 'Code verified Oct 25',
  dates: 'Nov 21 → Nov 23',
  travel: 'Plane · from Davao',
  plusOne: 'Yes · voucher sent',
  ...over,
})

export const speakersList = (over: Record<string, unknown> = {}) => ({
  travelMode: 'Plane',
  kpis: [
    { label: 'Confirmed speakers', value: '6' },
    { label: 'Links sent', value: '5' },
    { label: 'Forms submitted', value: '1' },
    { label: 'Plus-one vouchers', value: '0 issued' },
  ],
  speakers: [speakerRow()],
  ...over,
})

export const speakerDetail = (over: Record<string, unknown> = {}) => ({
  id: 'sp1',
  name: 'Dr. Lara Gomez',
  email: 'l•••••@dict.gov.ph',
  affiliation: 'DICT',
  status: 'Confirmed',
  session: 'Keynote · 9:15 AM',
  photoState: 'Uploaded · approve for website',
  canApprovePhoto: true,
  travel: {
    arrivalDate: '2026-11-21', departureDate: '2026-11-23',
    flyingFrom: 'Davao', preferredArrivalTime: 'Afternoon', hasIdFullName: true,
    busFrom: null, busEta: null, needsTerminalPickup: false, roomNote: null, needsNotes: null,
  },
  plusOne: { id: 'po1', name: 'Maria Gomez', email: 'm•••@example.com', sharesRoom: true, voucherCode: 'SPK-GOMEZ-7Q4X', registered: false },
  log: [
    { at: '2026-10-25T09:20:00+08:00', what: 'Form submitted' },
    { at: '2026-10-25T09:12:00+08:00', what: 'Code verified' },
    { at: '2026-10-24T15:00:00+08:00', what: 'Link sent by Rafael Tan' },
  ],
  canSend: false,
  sent: true,
  ...over,
})

export const speakerLanding = (over: Record<string, unknown> = {}) => ({
  eventName: 'HTN Con 2026',
  eventStartsAt: '2026-11-22T09:00:00+08:00',
  speakerFirstName: 'Lara',
  maskedEmail: 'l•••••@dict.gov.ph',
  travelMode: 'Plane',
  expiresAt: '2026-11-05T00:00:00+08:00',
  state: 'NeedsCode',
  ...over,
})

export const speakerFormState = (over: Record<string, unknown> = {}) => ({
  speakerName: 'Dr. Lara Gomez',
  session: 'Keynote · 9:15 AM',
  travelMode: 'Plane',
  photoFileId: null,
  photoApproved: false,
  arrivalDate: null,
  departureDate: null,
  flyingFrom: null,
  preferredArrivalTime: null,
  busFrom: null,
  busEta: null,
  needsTerminalPickup: false,
  needsNotes: null,
  hasPlusOne: false,
  plusOneName: null,
  plusOneEmail: null,
  plusOneSharesRoom: false,
  submitted: false,
  ...over,
})
