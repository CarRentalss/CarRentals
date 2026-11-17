import { useCallback, useState, type FormEvent, type ReactNode } from 'react'
import { getProfile, updateProfile } from '../api/broker'
import { ApiError, errorMessage } from '../api/client'
import type { ProfileResponse, UpdateProfileRequest } from '../api/types'
import { useApi } from '../api/useApi'
import { useAuth } from '../auth/useAuth'
import { ErrorMessage } from '../components/ErrorMessage'
import { LoginAgain } from '../components/LoginAgain'
import { Spinner } from '../components/Spinner'
import { PROFILE_FIELD_LABELS, formatDateTime, todayIso } from '../format'

export function ProfilePage() {
  const { refresh } = useAuth()
  const fetcher = useCallback((signal: AbortSignal) => getProfile(signal), [])
  const loaded = useApi(fetcher)
  const [saved, setSaved] = useState<ProfileResponse | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  const profile = saved ?? loaded.data

  return (
    <>
      <header className="page-head">
        <h1>Mój profil</h1>
        <p>Dane kierowcy, z których wypełniamy wycenę i wniosek o wynajem.</p>
      </header>
      {loaded.loading && <Spinner label="Wczytujemy profil…" />}
      <ErrorMessage message={loaded.error} onRetry={loaded.reload} />
      <LoginAgain status={loaded.errorStatus} />
      {profile && (
        <>
          {profile.isComplete ? (
            <div className="alert alert--success">
              Profil jest kompletny. Dane wypełnimy automatycznie przy wycenie.
            </div>
          ) : (
            <div className="alert alert--warn">
              Uzupełnij brakujące dane: {profile.missingFields.map((f) => PROFILE_FIELD_LABELS[f] ?? f).join(', ')}. Są
              potrzebne do wyceny i do wynajmu.
            </div>
          )}
          {message && (
            <div className="alert alert--success" role="status">
              {message}
            </div>
          )}
          <ProfileForm
            key={profile.updatedAt ?? 'new'}
            profile={profile}
            onSaved={(p) => {
              setSaved(p)
              setMessage('Zapisano zmiany w profilu.')
              void refresh()
            }}
            onEdit={() => setMessage(null)}
          />
        </>
      )}
    </>
  )
}

interface ProfileFormProps {
  profile: ProfileResponse
  onSaved: (profile: ProfileResponse) => void
  onEdit: () => void
}

type FormState = Record<keyof UpdateProfileRequest, string>

function ProfileForm({ profile, onSaved, onEdit }: ProfileFormProps) {
  const [form, setForm] = useState<FormState>({
    firstName: profile.firstName ?? '',
    lastName: profile.lastName ?? '',
    email: profile.email ?? '',
    dateOfBirth: profile.dateOfBirth ?? '',
    licenseObtainedOn: profile.licenseObtainedOn ?? '',
    location: profile.location ?? '',
  })
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<ApiError | string | null>(null)

  const missing = new Set<string>(profile.missingFields)
  const set = (field: keyof FormState) => (value: string) => {
    setForm((f) => ({ ...f, [field]: value }))
    onEdit()
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      const request: UpdateProfileRequest = {
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        email: form.email.trim(),
        dateOfBirth: form.dateOfBirth || null,
        licenseObtainedOn: form.licenseObtainedOn || null,
        location: form.location.trim() || null,
      }
      onSaved(await updateProfile(request))
    } catch (err) {
      setError(err instanceof ApiError ? err : errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  const fieldError = (field: keyof FormState) => (error instanceof ApiError ? error.fieldError(field) : undefined)
  const today = todayIso()

  return (
    <form className="panel form form--grid profile-form" onSubmit={onSubmit} aria-label="Dane profilu">
      <Field label="Imię" missing={missing.has('firstName')} error={fieldError('firstName')}>
        <input
          type="text"
          name="firstName"
          autoComplete="given-name"
          required
          maxLength={100}
          value={form.firstName}
          onChange={(e) => set('firstName')(e.target.value)}
        />
      </Field>
      <Field label="Nazwisko" missing={missing.has('lastName')} error={fieldError('lastName')}>
        <input
          type="text"
          name="lastName"
          autoComplete="family-name"
          required
          maxLength={100}
          value={form.lastName}
          onChange={(e) => set('lastName')(e.target.value)}
        />
      </Field>
      <Field label="Email" missing={missing.has('email')} error={fieldError('email')}>
        <input
          type="email"
          name="email"
          autoComplete="email"
          spellCheck={false}
          required
          maxLength={254}
          value={form.email}
          onChange={(e) => set('email')(e.target.value)}
        />
      </Field>
      <Field
        label="Data urodzenia"
        missing={missing.has('dateOfBirth')}
        error={fieldError('dateOfBirth')}
        hint={profile.age != null ? `Wiek: ${profile.age} lat` : undefined}
      >
        <input
          type="date"
          name="dateOfBirth"
          autoComplete="bday"
          max={today}
          value={form.dateOfBirth}
          onChange={(e) => set('dateOfBirth')(e.target.value)}
        />
      </Field>
      <Field
        label="Data uzyskania prawa jazdy"
        missing={missing.has('licenseObtainedOn')}
        error={fieldError('licenseObtainedOn')}
        hint={profile.licenseYears != null ? `Staż: ${profile.licenseYears} lat` : undefined}
      >
        <input
          type="date"
          max={today}
          value={form.licenseObtainedOn}
          onChange={(e) => set('licenseObtainedOn')(e.target.value)}
        />
      </Field>
      <Field label="Lokalizacja (miasto)" missing={missing.has('location')} error={fieldError('location')}>
        <input
          type="text"
          name="location"
          autoComplete="address-level2"
          maxLength={100}
          value={form.location}
          onChange={(e) => set('location')(e.target.value)}
        />
      </Field>
      <p className="muted small full">
        Trzymamy datę urodzenia i datę prawa jazdy, a wiek i staż liczymy na dziś, bo zmieniają się z czasem.
        {profile.updatedAt && <> Ostatnia zmiana: {formatDateTime(profile.updatedAt)}.</>}
      </p>
      {error && (
        <div className="full">
          <ErrorMessage message={typeof error === 'string' ? error : error.message} />
        </div>
      )}
      <div className="actions">
        <button type="submit" className="btn btn--primary" disabled={busy}>
          {busy ? 'Zapisujemy…' : 'Zapisz'}
        </button>
      </div>
    </form>
  )
}

interface FieldProps {
  label: string
  missing?: boolean
  error?: string
  hint?: string
  children: ReactNode
}

function Field({ label, missing, error, hint, children }: FieldProps) {
  const className = ['field', error ? 'field--error' : '', missing ? 'field--missing' : ''].filter(Boolean).join(' ')
  return (
    <label className={className}>
      <span>
        {label}
        {missing && <small className="field__missing"> (brakuje)</small>}
      </span>
      {children}
      {hint && <span className="field__hint">{hint}</span>}
      {error && <span className="field__error">{error}</span>}
    </label>
  )
}
