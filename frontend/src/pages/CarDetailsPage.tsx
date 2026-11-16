import { useCallback, useState, type FormEvent, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { createQuote, getCar, getProfile } from '../api/broker'
import { ApiError, errorMessage, isAbortError } from '../api/client'
import type { CarDetails, ProfileResponse, QuoteRequest, QuoteResult } from '../api/types'
import { useApi } from '../api/useApi'
import { useAuth } from '../auth/useAuth'
import { ErrorMessage } from '../components/ErrorMessage'
import { SkippedNotice } from '../components/SkippedNotice'
import { Spinner } from '../components/Spinner'
import { formatDate, formatMoney, formatTime, todayIso } from '../format'

// Broker kończy wycenę po 15 s, my czekamy chwilę dłużej na odpowiedź z listą pominiętych.
const CLIENT_TIMEOUT_MS = 20_000

export function CarDetailsPage() {
  const { provider = '', carId = '' } = useParams()
  const { me, loading: authLoading } = useAuth()

  const carFetcher = useCallback((signal: AbortSignal) => getCar(provider, carId, signal), [provider, carId])
  const car = useApi(carFetcher)

  const profileFetcher = useCallback((signal: AbortSignal) => getProfile(signal), [])
  const profile = useApi(me.isAuthenticated ? profileFetcher : null)

  return (
    <>
      <p>
        <Link to="/cars">Wróć do wyszukiwarki</Link>
      </p>
      {car.loading && <Spinner label="Wczytujemy dane auta..." />}
      <ErrorMessage message={car.error} onRetry={car.errorStatus === 404 ? undefined : car.reload} />
      {car.data && (
        <>
          <CarInfo car={car.data} />
          {authLoading || profile.loading ? (
            <Spinner label="Wczytujemy dane z profilu..." />
          ) : (
            <QuoteSection
              // Formularz startuje z danymi z profilu, gdy już są.
              key={profile.data ? 'profile' : 'anonymous'}
              car={car.data}
              profile={profile.data}
              isAuthenticated={me.isAuthenticated}
            />
          )}
        </>
      )}
    </>
  )
}

function CarInfo({ car }: { car: CarDetails }) {
  return (
    <section className="card">
      <h1>
        {car.make} {car.model}
      </h1>
      <dl className="details">
        <dt>Rok produkcji</dt>
        <dd>{car.year}</dd>
        <dt>Lokalizacja</dt>
        <dd>{car.location}</dd>
        <dt>Wypożyczalnia</dt>
        <dd>{car.providerName}</dd>
        <dt>Dostępność</dt>
        <dd>{car.available ? 'Dostępne (wg katalogu)' : 'Chwilowo niedostępne'}</dd>
      </dl>
      <p className="muted small">Dostępność w wybranym terminie sprawdzamy dopiero przy wycenie.</p>
    </section>
  )
}

interface QuoteForm {
  startDate: string
  endDate: string
  age: string
  licenseYears: string
  location: string
}

interface QuoteSectionProps {
  car: CarDetails
  profile: ProfileResponse | null
  isAuthenticated: boolean
}

function QuoteSection({ car, profile, isAuthenticated }: QuoteSectionProps) {
  const [form, setForm] = useState<QuoteForm>(() => ({
    startDate: todayIso(1),
    endDate: todayIso(4),
    age: profile?.age != null ? String(profile.age) : '',
    licenseYears: profile?.licenseYears != null ? String(profile.licenseYears) : '',
    location: profile?.location ?? car.location ?? '',
  }))
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<QuoteResult | null>(null)
  const [error, setError] = useState<ApiError | string | null>(null)
  const [localErrors, setLocalErrors] = useState<Partial<Record<keyof QuoteForm, string>>>({})

  const update = (field: keyof QuoteForm) => (value: string) => setForm((f) => ({ ...f, [field]: value }))

  function validate(): Partial<Record<keyof QuoteForm, string>> {
    const errors: Partial<Record<keyof QuoteForm, string>> = {}
    if (!form.startDate) errors.startDate = 'Podaj datę rozpoczęcia.'
    if (!form.endDate) errors.endDate = 'Podaj datę zakończenia.'
    if (form.startDate && form.startDate < todayIso()) errors.startDate = 'Data rozpoczęcia nie może być w przeszłości.'
    if (form.startDate && form.endDate && form.endDate <= form.startDate)
      errors.endDate = 'Data zakończenia musi być późniejsza niż data rozpoczęcia.'
    // Anonim musi podać wszystko, zalogowanemu brakujące dane Broker weźmie z profilu.
    if (!isAuthenticated) {
      if (!form.age) errors.age = 'Podaj wiek.'
      if (!form.licenseYears) errors.licenseYears = 'Podaj staż prawa jazdy.'
      if (!form.location.trim()) errors.location = 'Podaj miasto odbioru.'
    }
    const age = form.age ? Number(form.age) : null
    const years = form.licenseYears ? Number(form.licenseYears) : null
    if (age !== null && (age < 18 || age > 99)) errors.age = 'Wiek musi być w zakresie 18-99.'
    if (years !== null && (years < 0 || years > 81)) errors.licenseYears = 'Staż musi być w zakresie 0-81.'
    if (age !== null && years !== null && years > age - 18)
      errors.licenseYears = 'Staż nie może być dłuższy niż wiek minus 18 lat.'
    if (form.location.trim() && form.location.trim().length < 2) errors.location = 'Podaj co najmniej 2 znaki.'
    return errors
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors = validate()
    setLocalErrors(errors)
    if (Object.keys(errors).length > 0) return

    const request: QuoteRequest = {
      provider: car.provider,
      carId: car.carId,
      startDate: form.startDate,
      endDate: form.endDate,
      age: form.age ? Number(form.age) : null,
      licenseYears: form.licenseYears ? Number(form.licenseYears) : null,
      location: form.location.trim() || null,
    }

    const controller = new AbortController()
    const timer = window.setTimeout(() => controller.abort(), CLIENT_TIMEOUT_MS)
    setBusy(true)
    setError(null)
    setResult(null)
    try {
      setResult(await createQuote(request, controller.signal))
    } catch (err) {
      if (isAbortError(err)) {
        setError('Wypożyczalnie nie odpowiedziały na czas. Spróbuj ponownie za chwilę.')
      } else {
        setError(err instanceof ApiError ? err : errorMessage(err))
      }
    } finally {
      window.clearTimeout(timer)
      setBusy(false)
    }
  }

  const fieldError = (field: keyof QuoteForm) =>
    localErrors[field] ?? (error instanceof ApiError ? error.fieldError(field) : undefined)

  return (
    <section className="card">
      <h2>Sprawdź cenę</h2>
      {isAuthenticated ? (
        <p className="muted small">
          Dane kierowcy wypełniliśmy z profilu. Możesz je zmienić dla tej wyceny, a puste pola uzupełnimy z profilu.
          {profile && !profile.isComplete && (
            <>
              {' '}
              <Link to="/profile">Uzupełnij profil</Link>, żeby nie wpisywać ich za każdym razem.
            </>
          )}
        </p>
      ) : (
        <p className="muted small">Bez logowania podaj wszystkie dane. Po zalogowaniu wypełnimy je z profilu.</p>
      )}

      <form className="form form--grid" onSubmit={onSubmit} noValidate aria-label="Formularz wyceny">
        <Field label="Od" error={fieldError('startDate')}>
          <input type="date" value={form.startDate} min={todayIso()} onChange={(e) => update('startDate')(e.target.value)} />
        </Field>
        <Field label="Do" error={fieldError('endDate')}>
          <input type="date" value={form.endDate} min={form.startDate || todayIso()} onChange={(e) => update('endDate')(e.target.value)} />
        </Field>
        <Field label="Wiek kierowcy" error={fieldError('age')}>
          <input type="number" inputMode="numeric" min={18} max={99} value={form.age} onChange={(e) => update('age')(e.target.value)} />
        </Field>
        <Field label="Staż prawa jazdy (lata)" error={fieldError('licenseYears')}>
          <input
            type="number"
            inputMode="numeric"
            min={0}
            max={81}
            value={form.licenseYears}
            onChange={(e) => update('licenseYears')(e.target.value)}
          />
        </Field>
        <Field label="Miasto odbioru" error={fieldError('location')}>
          <input type="text" maxLength={100} value={form.location} onChange={(e) => update('location')(e.target.value)} />
        </Field>
        <div className="actions">
          <button type="submit" className="btn btn--primary" disabled={busy}>
            {busy ? 'Szukamy ofert...' : 'Sprawdź ceny'}
          </button>
        </div>
      </form>

      {busy && <Spinner label="Pytamy wypożyczalnie o ceny, to trwa najwyżej 15 sekund..." />}
      {error && <ErrorMessage message={typeof error === 'string' ? error : error.message} />}
      {result && <Offers result={result} />}
    </section>
  )
}

function Field({ label, error, children }: { label: string; error?: string; children: ReactNode }) {
  return (
    <label className={error ? 'field field--error' : 'field'}>
      <span>{label}</span>
      {children}
      {error && <span className="field__error">{error}</span>}
    </label>
  )
}

function Offers({ result }: { result: QuoteResult }) {
  return (
    <div className="offers">
      <h3>Oferty</h3>
      {result.offers.length === 0 ? (
        <div className="alert alert--info">
          Brak ofert dla podanego terminu i danych kierowcy. Zmień daty albo miasto i spróbuj ponownie.
        </div>
      ) : (
        <ul className="offer-list">
          {result.offers.map((offer) => (
            <li key={offer.quoteId} className="offer">
              <div className="offer__main">
                <strong>{offer.providerName}</strong>
                <span className="muted small">{offer.carLabel}</span>
                <span className="small">
                  {formatDate(offer.startDate)} - {formatDate(offer.endDate)} ({offer.days} dni)
                </span>
              </div>
              <dl className="offer__prices">
                <dt>Cena za dzień</dt>
                <dd>{formatMoney(offer.dailyPrice, offer.currency)}</dd>
                <dt>Ubezpieczenie za dzień</dt>
                <dd>{formatMoney(offer.dailyInsurancePrice, offer.currency)}</dd>
                <dt>Razem</dt>
                <dd className="offer__total">{formatMoney(offer.totalPrice, offer.currency)}</dd>
              </dl>
              <div className="offer__actions">
                <span className="small">Ważna do {formatTime(offer.expiresAt)}</span>
                <button type="button" className="btn" disabled title="Wynajem będzie w KM3">
                  Wynajmij <small>(KM3)</small>
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
      <SkippedNotice skipped={result.skippedProviders} title="Pominięte wypożyczalnie:" />
    </div>
  )
}
