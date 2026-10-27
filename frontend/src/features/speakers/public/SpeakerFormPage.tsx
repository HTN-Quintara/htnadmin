import { useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router'
import { isApiError } from '@/app/api'
import { errorMessage } from '@/app/errors'
import { Alert } from '@/components/ui/Alert'
import { Button } from '@/components/ui/Button'
import { Checkbox, Input, Select, Textarea } from '@/components/ui/Input'
import { PageSpinner } from '@/components/ui/Spinner'
import { getLanding, getForm, requestCode, submitForm, uploadPhoto, verifyCode } from './api'
import type { SpeakerFormState, SpeakerLanding } from '../schemas'

type Step = 'loading' | 'verify' | 'form' | 'done' | 'dead'

/** Frame 23: the public speaker confirmation form. No app shell; a private, token-gated page. */
export function SpeakerFormPage() {
  const { token = '' } = useParams()
  const [step, setStep] = useState<Step>('loading')
  const [landing, setLanding] = useState<SpeakerLanding | null>(null)
  const [sessionToken, setSessionToken] = useState<string | null>(null)
  const [form, setForm] = useState<SpeakerFormState | null>(null)
  const [voucherCode, setVoucherCode] = useState<string | null>(null)

  useEffect(() => {
    getLanding(token)
      .then((l) => { setLanding(l); setStep(l.state === 'Submitted' ? 'form' : 'verify') })
      .catch(() => setStep('dead'))
  }, [token])

  if (step === 'loading') return <PageSpinner label="Loading" />
  if (step === 'dead' || !landing) {
    return (
      <main className="mx-auto flex max-w-sm flex-col gap-4 p-6 text-center">
        <h1 className="text-xl font-semibold">This link no longer works</h1>
        <p className="text-sm text-gray-600">Ask the Programs team for a new one.</p>
      </main>
    )
  }

  return (
    <main className="mx-auto flex min-h-screen max-w-sm flex-col bg-white">
      <header className="flex items-center gap-3 border-b border-gray-200 p-5">
        <div aria-hidden className="flex h-9 w-9 items-center justify-center rounded-md bg-gray-900 text-[0.6875rem] font-bold text-white">HTN</div>
        <div className="flex flex-col">
          <span className="text-[0.9375rem] font-semibold">Speaker confirmation</span>
          <span className="text-[0.8125rem] text-gray-600">{landing.eventName}</span>
        </div>
        <span className="ml-auto flex items-center gap-1 text-xs text-gray-600">Private</span>
      </header>

      {step === 'verify' && (
        <VerifyStep
          token={token}
          landing={landing}
          onVerified={(sess, f) => { setSessionToken(sess); setForm(f); setStep('form') }}
        />
      )}
      {step === 'form' && (
        <FormStep
          token={token}
          landing={landing}
          sessionToken={sessionToken}
          initial={form}
          onSubmitted={(code) => { setVoucherCode(code); setStep('done') }}
          onNeedsCode={() => setStep('verify')}
        />
      )}
      {step === 'done' && <DoneStep voucherCode={voucherCode} onRestart={() => setStep('verify')} />}
    </main>
  )
}

function VerifyStep({ token, landing, onVerified }: { token: string; landing: SpeakerLanding; onVerified: (sessionToken: string, form: import('../schemas').SpeakerFormState) => void }) {
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [sending, setSending] = useState(false)
  const [verifying, setVerifying] = useState(false)
  const [sentOnce, setSentOnce] = useState(false)

  useEffect(() => { void sendCode() }, []) // eslint-disable-line react-hooks/exhaustive-deps

  async function sendCode() {
    setSending(true)
    try { await requestCode(token); setSentOnce(true) }
    catch (e) { setError(errorMessage(e)) }
    finally { setSending(false) }
  }

  async function verify() {
    if (verifying || code.length !== 6) return
    setVerifying(true)
    setError(null)
    try {
      const result = await verifyCode(token, code)
      onVerified(result.sessionToken, result.form)
    } catch (e) {
      setError(errorMessage(e))
    } finally {
      setVerifying(false)
    }
  }

  return (
    <div className="flex flex-col gap-5 p-5">
      <div className="flex flex-col gap-2">
        <h1 className="text-[1.375rem] font-semibold">Hi {landing.speakerFirstName}, let's confirm it's you</h1>
        <p className="text-[0.9375rem] leading-relaxed text-gray-600">
          We sent a 6-digit code to <strong className="font-medium text-gray-900">{landing.maskedEmail}</strong>. It expires in 10 minutes.
        </p>
      </div>
      <label className="flex flex-col gap-1.5 text-sm font-medium text-gray-700">
        Verification code
        <Input
          inputMode="numeric"
          autoComplete="one-time-code"
          maxLength={6}
          value={code}
          onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
          className="h-14 text-center text-2xl tracking-[0.4em] tabular-nums"
        />
      </label>
      {error && <Alert tone="red">{error}</Alert>}
      <Button className="h-13" loading={verifying} disabled={code.length !== 6} onClick={verify}>Verify and continue</Button>
      <Button variant="ghost" loading={sending} onClick={sendCode}>{sentOnce ? 'Resend code' : 'Send code'}</Button>
      <p className="text-[0.8125rem] leading-relaxed text-gray-600">
        This link is only for you. If you didn't expect it, ignore this page; nothing is shared until you verify.
      </p>
    </div>
  )
}

function FormStep({
  token, landing, sessionToken, initial, onSubmitted, onNeedsCode,
}: {
  token: string; landing: SpeakerLanding; sessionToken: string | null; initial: SpeakerFormState | null
  onSubmitted: (voucherCode: string | null) => void; onNeedsCode: () => void
}) {
  const [form, setForm] = useState<SpeakerFormState | null>(initial)
  const [plusOne, setPlusOne] = useState(initial?.hasPlusOne ?? false)
  const [plusOneName, setPlusOneName] = useState(initial?.plusOneName ?? '')
  const [plusOneEmail, setPlusOneEmail] = useState(initial?.plusOneEmail ?? '')
  const [arrival, setArrival] = useState(initial?.arrivalDate ?? '')
  const [departure, setDeparture] = useState(initial?.departureDate ?? '')
  const [flyingFrom, setFlyingFrom] = useState(initial?.flyingFrom ?? '')
  const [preferredTime, setPreferredTime] = useState(initial?.preferredArrivalTime ?? 'Afternoon (12 PM – 5 PM)')
  const [idFullName, setIdFullName] = useState('')
  const [busFrom, setBusFrom] = useState(initial?.busFrom ?? '')
  const [busEta, setBusEta] = useState(initial?.busEta ?? '')
  const [pickup, setPickup] = useState(initial?.needsTerminalPickup ?? false)
  const [notes, setNotes] = useState(initial?.needsNotes ?? '')
  const [consent, setConsent] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const fileRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    if (!form && sessionToken) getForm(token, sessionToken).then(setForm).catch(() => onNeedsCode())
  }, [form, sessionToken, token, onNeedsCode])

  if (!form) return <PageSpinner label="Loading your details" />

  const isPlane = landing.travelMode === 'Plane' || landing.travelMode === 'PlaneOrBus'
  const isBus = landing.travelMode === 'Bus' || landing.travelMode === 'PlaneOrBus'

  async function onUploadPhoto(file: File) {
    if (!sessionToken) return
    try { await uploadPhoto(token, sessionToken, file) }
    catch (e) { setError(isApiError(e) ? e.message : 'Could not upload the photo.') }
  }

  async function submit() {
    if (submitting || !sessionToken) return
    if (!consent) { setError('Please allow HackTheNorth.PH to use these details to arrange your travel.'); return }
    if (plusOne && (!plusOneName.trim() || !plusOneEmail.trim())) { setError("Give your plus-one's name and email."); return }
    setSubmitting(true)
    setError(null)
    try {
      const result = await submitForm(token, sessionToken, {
        arrivalDate: arrival || null, departureDate: departure || null,
        flyingFrom: isPlane ? flyingFrom || null : null,
        preferredArrivalTime: isPlane ? preferredTime || null : null,
        idFullName: isPlane ? idFullName || null : null,
        busFrom: isBus ? busFrom || null : null, busEta: isBus ? busEta || null : null,
        needsTerminalPickup: isBus ? pickup : false,
        needsNotes: notes || null, consent, hasPlusOne: plusOne,
        plusOneName: plusOne ? plusOneName : null, plusOneEmail: plusOne ? plusOneEmail : null, plusOneSharesRoom: plusOne,
      })
      onSubmitted(result.plusOneVoucherCode)
    } catch (e) {
      setError(errorMessage(e))
      if (isApiError(e) && e.status === 401) onNeedsCode()
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form className="flex flex-col gap-7 p-5 pb-8" onSubmit={(e) => { e.preventDefault(); void submit() }}>
      <div className="flex flex-col gap-1.5">
        <h1 className="text-[1.375rem] font-semibold">Thank you for speaking, {landing.speakerFirstName}</h1>
        <p className="text-[0.9375rem] leading-relaxed text-gray-600">{form.session}. Please tell us how you're getting here.</p>
      </div>

      <fieldset className="flex flex-col gap-3 border-0 p-0">
        <legend className="mb-3 text-base font-semibold">Your photo</legend>
        <div className="flex items-center gap-4">
          <div aria-hidden className="flex h-22 w-22 items-center justify-center rounded-full border border-dashed border-gray-400 bg-gray-50" />
          <div className="flex flex-col gap-1.5">
            <Button type="button" variant="secondary" onClick={() => fileRef.current?.click()}>Upload photo</Button>
            <input ref={fileRef} type="file" accept="image/jpeg,image/png" className="hidden"
              onChange={(e) => { const f = e.target.files?.[0]; e.target.value = ''; if (f) void onUploadPhoto(f) }} />
            <span className="text-xs text-gray-600">JPG or PNG, square, at least 800px. Used on the agenda and social posts.</span>
          </div>
        </div>
      </fieldset>

      <fieldset className="flex flex-col gap-3.5 border-0 p-0">
        <legend className="mb-3.5 text-base font-semibold">Arrival &amp; departure</legend>
        <div className="grid grid-cols-2 gap-3">
          <label className="flex flex-col gap-1.5 text-sm font-medium text-gray-700">Arrival date<Input type="date" value={arrival} onChange={(e) => setArrival(e.target.value)} /></label>
          <label className="flex flex-col gap-1.5 text-sm font-medium text-gray-700">Departure date<Input type="date" value={departure} onChange={(e) => setDeparture(e.target.value)} /></label>
        </div>
      </fieldset>

      {landing.travelMode !== 'None' && (
        <fieldset className="flex flex-col gap-3.5 border-0 p-0">
          <legend className="mb-1 text-base font-semibold">{isPlane ? 'Your flight' : 'Your bus trip'}</legend>
          {isPlane && (
            <div className="flex flex-col gap-3.5">
              <label className="flex flex-col gap-1.5 text-sm font-medium text-gray-700">Flying from
                <Select value={flyingFrom} onChange={(e) => setFlyingFrom(e.target.value)}>
                  <option value="">Select</option>
                  <option>Davao (DVO)</option><option>Cebu (CEB)</option><option>Iloilo (ILO)</option><option>Other</option>
                </Select>
              </label>
              <label className="flex flex-col gap-1.5 text-sm font-medium text-gray-700">Preferred arrival time
                <Select value={preferredTime} onChange={(e) => setPreferredTime(e.target.value)}>
                  <option>Afternoon (12 PM – 5 PM)</option><option>Morning</option><option>Evening</option>
                </Select>
              </label>
              <label className="flex flex-col gap-1.5 text-sm font-medium text-gray-700">Full name as on your government ID
                <Input autoComplete="name" value={idFullName} onChange={(e) => setIdFullName(e.target.value)} />
                <span className="text-xs font-normal text-gray-600">Needed by the airline. Deleted 30 days after the event.</span>
              </label>
            </div>
          )}
          {isBus && (
            <div className="flex flex-col gap-3.5">
              <label className="flex flex-col gap-1.5 text-sm font-medium text-gray-700">Departing from (city / terminal)
                <Input placeholder="e.g. Baguio · Victory Liner" value={busFrom} onChange={(e) => setBusFrom(e.target.value)} />
              </label>
              <label className="flex flex-col gap-1.5 text-sm font-medium text-gray-700">Estimated arrival in Manila
                <Input type="time" value={busEta} onChange={(e) => setBusEta(e.target.value)} />
              </label>
              <label className="flex min-h-11 items-center gap-2.5 text-sm"><Checkbox checked={pickup} onChange={(e) => setPickup(e.target.checked)} />I need pickup from the bus terminal</label>
            </div>
          )}
        </fieldset>
      )}

      <fieldset className="flex flex-col gap-3 border-0 p-0">
        <legend className="mb-3 text-base font-semibold">Bringing a plus-one?</legend>
        <div role="radiogroup" aria-label="Plus-one" className="grid grid-cols-2 gap-2">
          <button type="button" role="radio" aria-checked={!plusOne} onClick={() => setPlusOne(false)}
            className={`h-12 rounded-md border text-[0.9375rem] font-medium ${!plusOne ? 'border-brand-600 bg-red-50' : 'border-gray-300 bg-white'}`}>No, just me</button>
          <button type="button" role="radio" aria-checked={plusOne} onClick={() => setPlusOne(true)}
            className={`h-12 rounded-md border text-[0.9375rem] font-medium ${plusOne ? 'border-brand-600 bg-red-50' : 'border-gray-300 bg-white'}`}>Yes</button>
        </div>
        {plusOne && (
          <div className="flex flex-col gap-3">
            <div className="rounded-md bg-red-50 p-3.5 text-[0.8125rem] leading-relaxed text-red-900">
              <strong>Free:</strong> your plus-one stays with you in your room.<br />
              <strong>Not included:</strong> their event ticket and travel. We'll email them a personal <strong>50% off</strong> voucher so they can register.
            </div>
            <label className="flex flex-col gap-1.5 text-sm font-medium text-gray-700">Plus-one full name<Input value={plusOneName} onChange={(e) => setPlusOneName(e.target.value)} /></label>
            <label className="flex flex-col gap-1.5 text-sm font-medium text-gray-700">Plus-one email
              <Input type="email" autoComplete="off" value={plusOneEmail} onChange={(e) => setPlusOneEmail(e.target.value)} />
              <span className="text-xs font-normal text-gray-600">We send their voucher code and registration link here.</span>
            </label>
          </div>
        )}
      </fieldset>

      <label className="flex flex-col gap-1.5 text-sm font-medium text-gray-700">Dietary needs or accessibility requests
        <Textarea rows={2} placeholder="Optional" value={notes} onChange={(e) => setNotes(e.target.value)} />
      </label>

      <label className="flex items-start gap-2.5 text-[0.8125rem] leading-relaxed text-gray-600">
        <Checkbox checked={consent} onChange={(e) => setConsent(e.target.checked)} />
        I allow HackTheNorth.PH to use these details only to arrange my travel and stay, as allowed by the Data Privacy Act of 2012.
      </label>

      {error && <Alert tone="red">{error}</Alert>}
      <Button type="submit" className="h-13" loading={submitting}>Submit details</Button>
    </form>
  )
}

function DoneStep({ voucherCode, onRestart }: { voucherCode: string | null; onRestart: () => void }) {
  return (
    <div role="status" className="flex flex-col items-center gap-3.5 p-12 text-center">
      <div aria-hidden className="flex h-14 w-14 items-center justify-center rounded-full bg-green-50 text-2xl text-green-700">✓</div>
      <h1 className="text-[1.375rem] font-semibold">Details received</h1>
      <p className="text-[0.9375rem] leading-relaxed text-gray-600">
        Our Programs team will email your travel itinerary and hotel booking.
        {voucherCode && <> Your plus-one's voucher (<strong>{voucherCode}</strong>) has been emailed to them.</>}
        {' '}Need to change something? Use the same link; you'll get a new code.
      </p>
      <Button variant="secondary" onClick={onRestart}>Update my details</Button>
    </div>
  )
}
