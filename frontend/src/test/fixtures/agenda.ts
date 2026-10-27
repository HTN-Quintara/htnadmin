// API fixtures for the agenda slice (frame 16), shaped like the Agenda DTO.

export const room = (over: Record<string, unknown> = {}) => ({
  id: 'rm1', name: 'Main Hall', colorHex: '#B71C1C', sortOrder: 0, isActive: true, ...over,
})

export const session = (over: Record<string, unknown> = {}) => ({
  id: 'se1',
  title: 'The state of Philippine cybersecurity',
  kind: 'Keynote',
  startsAt: '2026-11-22T09:15:00+08:00',
  durationMinutes: 45,
  time: '9:15 AM',
  ownerCommitteeId: null,
  isPublic: true,
  notes: null,
  rooms: [room()],
  speakers: [{ speakerId: 'sp1', name: 'Dr. Lara Gomez', role: 'Speaker', sortOrder: 0, tentative: false }],
  tentative: false,
  ...over,
})

export const agenda = (over: Record<string, unknown> = {}) => ({
  eventId: 'e1',
  eventName: 'HTN Con 2026',
  rooms: [room(), room({ id: 'rm2', name: 'Workshop Room A', colorHex: '#0D9488', sortOrder: 1 })],
  days: [
    {
      date: '2026-11-22',
      heading: 'Saturday, Nov 22',
      sessionCount: 2,
      tentativeSpeakers: 1,
      slots: [
        { time: '9:15 AM', sessions: [session()] },
        { time: '10:15 AM', sessions: [session({ id: 'se2', title: 'AI for the blue team', kind: 'Talk', time: '10:15 AM', tentative: true, speakers: [{ speakerId: 'sp2', name: '[Speaker TBC]', role: 'Speaker', sortOrder: 0, tentative: true }] })] },
      ],
    },
  ],
  canManage: true,
  ...over,
})
