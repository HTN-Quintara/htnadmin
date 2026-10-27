const dateFmt = new Intl.DateTimeFormat('en-PH', { month: 'short', day: 'numeric', year: 'numeric' })
const pesoFmt = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP', maximumFractionDigits: 0, minimumFractionDigits: 0 })
const pesoCentsFmt = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP', maximumFractionDigits: 2, minimumFractionDigits: 2 })

export function formatDate(value: string | Date | null | undefined): string {
  if (!value) return ''
  const d = typeof value === 'string' ? new Date(value) : value
  return Number.isNaN(d.getTime()) ? '' : dateFmt.format(d)
}

/** ₱1,500 for whole pesos, ₱1,500.50 when there are centavos (en-PH grouping). */
export function formatPeso(n: number): string {
  return Number.isInteger(n) ? pesoFmt.format(n) : pesoCentsFmt.format(n)
}

/**
 * −₱1,200 / +₱3,400, with a real minus sign (U+2212) so it reads as money rather than a hyphenated range. For a net
 * figure or a variance, where the sign is the point.
 */
export function formatPesoSigned(n: number): string {
  if (n === 0) return formatPeso(0)
  return `${n < 0 ? '\u2212' : '+'}${formatPeso(Math.abs(n))}`
}

/**
 * ₱1.2M / ₱450k / ₱980 — for chart axis labels and stat tiles only. Never for a figure someone reconciles against:
 * it rounds.
 */
export function formatPesoCompact(n: number): string {
  const sign = n < 0 ? '\u2212' : ''
  const abs = Math.abs(n)
  if (abs >= 1_000_000) return `${sign}\u20B1${trim(abs / 1_000_000)}M`
  if (abs >= 1_000) return `${sign}\u20B1${trim(abs / 1_000)}k`
  return `${sign}${formatPeso(abs)}`
}

function trim(n: number): string {
  return (Math.round(n * 10) / 10).toString()
}

export function initials(first: string, last: string): string {
  return `${first.trim().charAt(0)}${last.trim().charAt(0)}`.toUpperCase()
}

/** Only allow same-origin relative paths as post-login redirects. */
export function safeNext(next: string | null | undefined): string {
  if (!next || !next.startsWith('/') || next.startsWith('//') || next.startsWith('/\\')) return '/'
  return next
}
