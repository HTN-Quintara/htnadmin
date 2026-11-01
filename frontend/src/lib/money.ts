/**
 * Money parsing shared across the app. Pesos are always typed into a text field and parsed here — never a
 * `type="number"` input, which would let a browser hand us a float it rounded on its own.
 */
const amountRe = /^\d{1,10}(\.\d{1,2})?$/
const signedAmountRe = /^[+-]?\d{1,10}(\.\d{1,2})?$/
const intRe = /^\d{1,7}$/

/** Pesos as typed ("1500", "1,500.50"); commas are allowed for grouping. Null when it is not money. */
export function parseAmount(v: string): number | null {
  const clean = v.trim().replace(/,/g, '')
  return amountRe.test(clean) ? Number(clean) : null
}

/**
 * Pesos that may be negative: a bank statement balance can be overdrawn, and a counted cash box can come up short.
 * Accepts a leading "+" or "-", and the Unicode minus the UI prints — someone reconciling will copy the figure the
 * screen showed them, signs and all.
 */
export function parseSignedAmount(v: string): number | null {
  const clean = v.trim().replace(/,/g, '').replace(/−/g, '-')
  return signedAmountRe.test(clean) ? Number(clean) : null
}

export function parseInteger(v: string): number | null {
  const clean = v.trim().replace(/,/g, '')
  return intRe.test(clean) ? Number(clean) : null
}

export const AMOUNT_HINT = 'Enter an amount like 1500 or 1500.50.'
export const SIGNED_AMOUNT_HINT = 'Enter an amount like 1500, 1500.50 or -250 for an overdrawn account.'
