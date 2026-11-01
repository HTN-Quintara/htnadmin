import { z } from 'zod'
import { contrastWithWhite, isHexColor, MIN_CONTRAST } from '@/app/theme/brand'

// ---- Org settings (GET /api/settings/org) -----------------------------------

export const presetsSchema = z.object({ small: z.number(), medium: z.number(), large: z.number(), xl: z.number() })

export const pointsSettingsSchema = z.object({
  leadMultiplier: z.number(),
  collabMultiplier: z.number(),
  assistantMultiplier: z.number(),
  presets: presetsSchema,
  requireTaskApproval: z.boolean(),
})
export type PointsSettings = z.infer<typeof pointsSettingsSchema>

export const brandingSchema = z.object({
  brandColor: z.string(),
  logoLightUrl: z.string().nullable(),
  logoDarkUrl: z.string().nullable(),
  paymentDeadlineHours: z.number(),
})
export type Branding = z.infer<typeof brandingSchema>

export const orgSettingsSchema = z.object({ points: pointsSettingsSchema, branding: brandingSchema })
export type OrgSettings = z.infer<typeof orgSettingsSchema>

/** Domain defaults (htn-domain: Lead 1.0, Collab 0.7, Assistant 0.3). */
export const defaultPoints: PointsSettings = {
  leadMultiplier: 1,
  collabMultiplier: 0.7,
  assistantMultiplier: 0.3,
  presets: { small: 10, medium: 20, large: 40, xl: 80 },
  requireTaskApproval: true,
}

/** Base points used for the "40-pt task → X pts" previews. */
export const PREVIEW_BASE = 40

/** "28 pts" for a 40-point task at multiplier 0.7. */
export function previewPoints(multiplier: number): string {
  if (!Number.isFinite(multiplier)) return '—'
  const pts = Number((PREVIEW_BASE * multiplier).toFixed(1))
  return `${pts} ${pts === 1 ? 'pt' : 'pts'}`
}

// ---- Points form --------------------------------------------------------------

const multiplier = z
  .number({ error: 'Enter a number from 0 to 2.' })
  .min(0, 'Use 0 to 2.')
  .max(2, 'Use 0 to 2.')
  .refine((n) => Math.abs(n * 10 - Math.round(n * 10)) < 1e-9, 'Use at most one decimal place.')

const preset = z.number({ error: 'Enter a whole number.' }).int('Enter a whole number.').min(1, 'Use 1 to 1000.').max(1000, 'Use 1 to 1000.')

export const pointsFormSchema = z
  .object({
    leadMultiplier: multiplier,
    collabMultiplier: multiplier,
    assistantMultiplier: multiplier,
    presets: z.object({ small: preset, medium: preset, large: preset, xl: preset }),
    requireTaskApproval: z.boolean(),
  })
  .superRefine((v, ctx) => {
    const { small, medium, large, xl } = v.presets
    if (!(medium > small)) ctx.addIssue({ code: 'custom', path: ['presets', 'medium'], message: 'Must be more than Small.' })
    if (!(large > medium)) ctx.addIssue({ code: 'custom', path: ['presets', 'large'], message: 'Must be more than Medium.' })
    if (!(xl > large)) ctx.addIssue({ code: 'custom', path: ['presets', 'xl'], message: 'Must be more than Large.' })
  })

// ---- Branding form -----------------------------------------------------------

export const brandingFormSchema = z.object({
  brandColor: z
    .string()
    .trim()
    .refine(isHexColor, 'Enter a hex color like #B71C1C.')
    .refine((v) => !isHexColor(v) || contrastWithWhite(v) >= MIN_CONTRAST, 'Pick a darker color: white text needs a contrast of 4.5:1 or more.'),
  paymentDeadlineHours: z
    .number({ error: 'Enter a number of hours.' })
    .int('Enter whole hours.')
    .min(1, 'Use 1 to 336 hours (14 days).')
    .max(336, 'Use 1 to 336 hours (14 days).'),
})
export type BrandingFormValues = z.infer<typeof brandingFormSchema>

// ---- Payment accounts ----------------------------------------------------------

export const paymentKindSchema = z.enum(['GCash', 'Bank', 'Cash'])
export type PaymentKind = z.infer<typeof paymentKindSchema>

export const paymentAccountSchema = z.object({
  id: z.string(),
  kind: paymentKindSchema,
  label: z.string(),
  bankName: z.string().nullable(),
  accountName: z.string(),
  /** Full number for Board callers only; null otherwise. */
  accountNumber: z.string().nullable(),
  accountNumberMasked: z.string().nullable(),
  isDefault: z.boolean(),
  isActive: z.boolean(),
})
export type PaymentAccount = z.infer<typeof paymentAccountSchema>

const text = (label: string, max: number) =>
  z.string().trim().min(1, `Enter ${label}.`).max(max, `Use ${max} characters or fewer.`)

export const paymentAccountFormSchema = z
  .object({
    kind: paymentKindSchema,
    label: text('a label', 80),
    bankName: z.string().trim().max(80, 'Use 80 characters or fewer.'),
    accountName: text('the account name', 120),
    // Checked below: only GCash and Bank accounts have a number (hidden fields keep their value).
    accountNumber: z.string().trim(),
    isDefault: z.boolean(),
  })
  .superRefine((v, ctx) => {
    if (v.kind === 'Bank' && !v.bankName) ctx.addIssue({ code: 'custom', path: ['bankName'], message: 'Enter the bank name.' })
    if (v.kind === 'Cash') return
    const message = !v.accountNumber
      ? 'Enter the account number.'
      : v.accountNumber.length > 40
        ? 'Use 40 characters or fewer.'
        : !/^[0-9 -]+$/.test(v.accountNumber)
          ? 'Use digits, spaces and dashes only.'
          : null
    if (message) ctx.addIssue({ code: 'custom', path: ['accountNumber'], message })
  })
  .transform((v) => ({
    kind: v.kind,
    label: v.label,
    bankName: v.kind === 'Bank' ? v.bankName : null,
    accountName: v.accountName,
    accountNumber: v.kind === 'Cash' ? null : v.accountNumber,
    isDefault: v.isDefault,
  }))
export type PaymentAccountFormInput = z.input<typeof paymentAccountFormSchema>
export type PaymentAccountBody = z.output<typeof paymentAccountFormSchema>

export const reasonSchema = z.object({
  reason: z.string().trim().min(1, 'Enter a reason.').max(500, 'Use 500 characters or fewer.'),
})
export type ReasonValues = z.infer<typeof reasonSchema>

// ---- Email delivery (Mailtrap) ------------------------------------------------

export const emailModeSchema = z.enum(['Sandbox', 'Live'])
export type EmailMode = z.infer<typeof emailModeSchema>

export const emailSettingsSchema = z.object({
  mode: emailModeSchema,
  inboxId: z.string().nullable(),
  fromEmail: z.string(),
  fromName: z.string(),
  replyTo: z.string().nullable(),
  tokenSet: z.boolean(),
  tokenLast4: z.string().nullable(),
  tokenUnreadable: z.boolean().default(false),
  source: z.enum(['Database', 'Environment', 'None']),
  updatedAt: z.string().nullable(),
})
export type EmailSettings = z.infer<typeof emailSettingsSchema>

const emailField = z.string().trim().min(1, 'Enter an email address.').pipe(z.email('Enter a valid email address.'))

export const emailSettingsFormSchema = z
  .object({
    mode: emailModeSchema,
    inboxId: z.string().trim(),
    fromName: z.string().trim().min(1, 'Enter a sender name.').max(100, 'Keep it under 100 characters.'),
    fromEmail: emailField,
    replyTo: z.string().trim().refine((v) => v === '' || z.email().safeParse(v).success, 'Enter a valid email address.'),
    apiToken: z.string().trim(),
  })
  .superRefine((v, ctx) => {
    if (v.mode === 'Sandbox' && v.inboxId === '') ctx.addIssue({ code: 'custom', path: ['inboxId'], message: 'Enter the sandbox inbox ID.' })
  })
export type EmailSettingsFormValues = z.infer<typeof emailSettingsFormSchema>

export type EmailSettingsBody = {
  mode: EmailMode
  inboxId: string | null
  fromEmail: string
  fromName: string
  replyTo: string | null
  apiToken: string | null
  clearToken: boolean
}

export const testEmailFormSchema = z.object({ to: emailField })
export type TestEmailFormValues = z.infer<typeof testEmailFormSchema>

export const testEmailResultSchema = z.object({ to: z.string() })
