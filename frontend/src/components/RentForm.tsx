import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { createRental } from '../api/broker'
import { ApiError, errorMessage } from '../api/client'
import type { ProfileResponse, ProviderOffer } from '../api/types'
import { formatMoney } from '../format'
import { ErrorMessage } from './ErrorMessage'

interface DriverForm {
  firstName: string
  lastName: string
  email: string
}

interface RentFormProps {
  offer: ProviderOffer
  profile: ProfileResponse | null
  onCancel: () => void
}

const EMAIL_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

// Dane kierowcy idą do wypożyczalni razem z wnioskiem. Wypełniamy je z profilu, można je zmienić.
export function RentForm({ offer, profile, onCancel }: RentFormProps) {
  const navigate = useNavigate()
  const [form, setForm] = useState<DriverForm>(() => ({
    firstName: profile?.firstName ?? '',
    lastName: profile?.lastName ?? '',
    email: profile?.email ?? '',
  }))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<ApiError | string | null>(null)
  const [localErrors, setLocalErrors] = useState<Partial<Record<keyof DriverForm, string>>>({})

  const update = (field: keyof DriverForm) => (value: string) => setForm((f) => ({ ...f, [field]: value }))

  function validate(): Partial<Record<keyof DriverForm, string>> {
    const errors: Partial<Record<keyof DriverForm, string>> = {}
    if (!form.firstName.trim()) errors.firstName = 'Podaj imię.'
    if (!form.lastName.trim()) errors.lastName = 'Podaj nazwisko.'
    if (!form.email.trim()) errors.email = 'Podaj email.'
    else if (!EMAIL_REGEX.test(form.email.trim())) errors.email = 'Podaj poprawny email.'
    return errors
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors = validate()
    setLocalErrors(errors)
    if (Object.keys(errors).length > 0) return

    setBusy(true)
    setError(null)
    try {
      const rental = await createRental({
        quoteId: offer.quoteId,
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        email: form.email.trim(),
      })
      navigate(`/rentals/${encodeURIComponent(rental.rentalId)}`)
    } catch (err) {
      setError(err instanceof ApiError ? err : errorMessage(err))
      setBusy(false)
    }
  }

  const fieldError = (field: keyof DriverForm) =>
    localErrors[field] ?? (error instanceof ApiError ? error.fieldError(field) : undefined)

  const autoComplete: Record<keyof DriverForm, string> = {
    firstName: 'given-name',
    lastName: 'family-name',
    email: 'email',
  }
  const field = (name: keyof DriverForm, label: string, type: string, maxLength: number) => {
    const message = fieldError(name)
    return (
      <label className={message ? 'field field--error' : 'field'}>
        <span>{label}</span>
        <input
          type={type}
          name={name}
          autoComplete={autoComplete[name]}
          spellCheck={type === 'email' ? false : undefined}
          maxLength={maxLength}
          value={form[name]}
          onChange={(e) => update(name)(e.target.value)}
        />
        {message && (
          <span className="field__error" role="alert">
            {message}
          </span>
        )}
      </label>
    )
  }

  return (
    <form className="form form--grid rent-form" onSubmit={onSubmit} noValidate aria-label="Dane kierowcy do wynajmu">
      <div className="full">
        <h3>Dane kierowcy</h3>
        <p className="muted">
          Wynajmujesz {offer.carLabel} w {offer.providerName} za{' '}
          <strong className="money">{formatMoney(offer.totalPrice, offer.currency)}</strong>. Dane wypełniliśmy z
          profilu, przekażemy je wypożyczalni razem z wnioskiem.
        </p>
      </div>
      {field('firstName', 'Imię', 'text', 100)}
      {field('lastName', 'Nazwisko', 'text', 100)}
      {field('email', 'Email', 'email', 254)}
      <div className="actions">
        <button type="submit" className="btn btn--primary" disabled={busy}>
          {busy ? 'Wysyłamy wniosek…' : 'Potwierdź wynajem'}
        </button>
        <button type="button" className="btn btn--quiet" onClick={onCancel} disabled={busy}>
          Anuluj
        </button>
      </div>
      {error && (
        <div className="full">
          <ErrorMessage message={typeof error === 'string' ? error : error.message} />
        </div>
      )}
    </form>
  )
}
