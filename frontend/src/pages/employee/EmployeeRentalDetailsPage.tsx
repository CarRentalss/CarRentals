import { useCallback, useRef, useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { attachmentUrl, confirmReturn, getEmployeeRental } from '../../api/broker'
import { ApiError, errorMessage } from '../../api/client'
import type { EmployeeRentalDetails } from '../../api/types'
import { useApi } from '../../api/useApi'
import { ErrorMessage } from '../../components/ErrorMessage'
import { LoginAgain } from '../../components/LoginAgain'
import { Spinner } from '../../components/Spinner'
import { STATUS_LABELS, formatDate, formatDateTime, formatMoney } from '../../format'

const MAX_FILE_BYTES = 10 * 1024 * 1024
const ALLOWED_EXTENSIONS = ['.jpg', '.jpeg', '.png', '.pdf']

export function EmployeeRentalDetailsPage() {
  const { rentalId = '' } = useParams()
  const fetcher = useCallback((signal: AbortSignal) => getEmployeeRental(rentalId, signal), [rentalId])
  const { data, loading, error, errorStatus, reload } = useApi(fetcher)
  const [confirmed, setConfirmed] = useState(false)

  return (
    <>
      <Link to="/employee/rentals" className="back-link">
        Wróć do listy
      </Link>
      {loading && <Spinner label="Wczytujemy wypożyczenie…" />}
      <ErrorMessage message={error} onRetry={errorStatus === 404 ? undefined : reload} />
      <LoginAgain status={errorStatus} />
      {confirmed && (
        <div className="alert alert--success" role="status">
          Zwrot potwierdzony. Wypożyczenie jest zakończone.
        </div>
      )}
      {data && (
        <>
          <RentalInfo rental={data} />
          {data.status === 'ReturnRequested' ? (
            <ConfirmReturnForm
              rentalId={data.rentalId}
              onDone={() => {
                setConfirmed(true)
                reload()
              }}
            />
          ) : (
            data.status !== 'Completed' && (
              <p className="muted">Zwrot można potwierdzić, gdy klient go zgłosi (status „Do odbioru”).</p>
            )
          )}
        </>
      )}
    </>
  )
}

function RentalInfo({ rental }: { rental: EmployeeRentalDetails }) {
  return (
    <section className="narrow">
      <header className="status-head">
        <div>
          <h1>Wypożyczenie: {rental.car}</h1>
          <span className={`status status--large status--${rental.status}`}>
            {STATUS_LABELS[rental.status] ?? rental.status}
          </span>
        </div>
        <dl className="price-figure">
          <dt>Kwota</dt>
          <dd>{formatMoney(rental.totalPrice)}</dd>
        </dl>
      </header>
      <dl className="details">
        <dt>Numer</dt>
        <dd className="mono">{rental.rentalId}</dd>
        <dt>Klient</dt>
        <dd>
          {rental.firstName} {rental.lastName} ({rental.email})
        </dd>
        <dt>Termin</dt>
        <dd>
          {formatDate(rental.startDate)} – {formatDate(rental.endDate)}
        </dd>
        <dt>Miejsce</dt>
        <dd>{rental.location}</dd>
        <dt>Broker</dt>
        <dd>{rental.brokerName}</dd>
        <dt>Utworzone</dt>
        <dd>{formatDateTime(rental.createdAt)}</dd>
        <dt>Zgłoszenie zwrotu</dt>
        <dd>{formatDateTime(rental.returnRequestedAt)}</dd>
        {rental.reason && (
          <>
            <dt>Powód</dt>
            <dd>{rental.reason}</dd>
          </>
        )}
        {rental.completedAt && (
          <>
            <dt>Zwrot potwierdzony</dt>
            <dd>
              {formatDateTime(rental.completedAt)}
              {rental.completedBy && <> przez {rental.completedBy}</>}
            </dd>
          </>
        )}
        {rental.returnNote && (
          <>
            <dt>Notatka o stanie auta</dt>
            <dd className="pre">{rental.returnNote}</dd>
          </>
        )}
        {rental.hasAttachment && (
          <>
            <dt>Załącznik</dt>
            <dd>
              <a href={attachmentUrl(rental.rentalId)}>Pobierz załącznik</a>
            </dd>
          </>
        )}
      </dl>
    </section>
  )
}

function validateFile(file: File | null): string | null {
  if (!file) return 'Dodaj zdjęcie (jpg, png) albo PDF.'
  const name = file.name.toLowerCase()
  if (!ALLOWED_EXTENSIONS.some((ext) => name.endsWith(ext))) return 'Dozwolone są tylko pliki jpg, png i pdf.'
  if (file.size === 0) return 'Plik jest pusty.'
  if (file.size > MAX_FILE_BYTES) return 'Plik może mieć najwyżej 10 MB.'
  return null
}

function ConfirmReturnForm({ rentalId, onDone }: { rentalId: string; onDone: () => void }) {
  const [note, setNote] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<{ note?: string; file?: string }>({})
  const fileInput = useRef<HTMLInputElement>(null)

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors: { note?: string; file?: string } = {}
    const trimmed = note.trim()
    if (trimmed.length < 3 || trimmed.length > 1000) errors.note = 'Notatka musi mieć od 3 do 1000 znaków.'
    const fileError = validateFile(file)
    if (fileError) errors.file = fileError
    setFieldErrors(errors)
    if (Object.keys(errors).length > 0 || !file) return

    setBusy(true)
    setError(null)
    try {
      await confirmReturn(rentalId, trimmed, file)
      setNote('')
      setFile(null)
      if (fileInput.current) fileInput.current.value = ''
      onDone()
    } catch (err) {
      if (err instanceof ApiError) {
        setFieldErrors({ note: err.fieldError('note'), file: err.fieldError('file') })
      }
      setError(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="panel confirm-panel">
      <h2>Potwierdzenie zwrotu</h2>
      <p className="muted">Dodaj krótką notatkę o stanie auta i zdjęcie albo PDF (jpg, png, pdf, do 10 MB).</p>
      <form className="form" onSubmit={onSubmit} noValidate aria-label="Potwierdzenie zwrotu">
        <label className={fieldErrors.note ? 'field field--error' : 'field'}>
          <span>Notatka o stanie auta</span>
          <textarea rows={4} maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} />
          {fieldErrors.note && <span className="field__error">{fieldErrors.note}</span>}
        </label>
        <label className={fieldErrors.file ? 'field field--error' : 'field'}>
          <span>Zdjęcie albo PDF</span>
          <input
            ref={fileInput}
            type="file"
            accept=".jpg,.jpeg,.png,.pdf,image/jpeg,image/png,application/pdf"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          />
          {fieldErrors.file && <span className="field__error">{fieldErrors.file}</span>}
        </label>
        <ErrorMessage message={error} />
        <div className="actions">
          <button type="submit" className="btn btn--primary" disabled={busy}>
            {busy ? 'Wysyłamy…' : 'Potwierdź zwrot'}
          </button>
        </div>
      </form>
    </section>
  )
}
