import { useCallback, useState } from 'react'
import { Link } from 'react-router-dom'
import { getMyRentals, requestReturn } from '../../api/broker'
import { errorMessage } from '../../api/client'
import type { RentalResponse } from '../../api/types'
import { usePolledApi } from '../../api/usePolledApi'
import { ErrorMessage } from '../../components/ErrorMessage'
import { LoginAgain } from '../../components/LoginAgain'
import { Spinner } from '../../components/Spinner'
import { CUSTOMER_STATUS_LABELS, formatDate, formatDateTime, formatMoney } from '../../format'
import { POLL_INTERVAL_MS, anyPending } from './rentalPolling'

export function MyRentalsPage() {
  const fetcher = useCallback((signal: AbortSignal) => getMyRentals(signal), [])
  const { data, loading, error, errorStatus, reload, setData } = usePolledApi(fetcher, POLL_INTERVAL_MS, anyPending)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  async function onReturn(rental: RentalResponse) {
    setBusyId(rental.rentalId)
    setActionError(null)
    setMessage(null)
    try {
      const updated = await requestReturn(rental.rentalId)
      setData((data ?? []).map((r) => (r.rentalId === updated.rentalId ? updated : r)))
      setMessage(`Zgłosiliśmy zwrot: ${updated.carLabel}. Pracownik wypożyczalni potwierdzi odbiór auta.`)
    } catch (err) {
      setActionError(errorMessage(err))
    } finally {
      setBusyId(null)
    }
  }

  return (
    <>
      <header className="page-head">
        <h1>Moje wypożyczenia</h1>
        <p>Wynajmy złożone przez nasz serwis. Statusy pobieramy na bieżąco z wypożyczalni.</p>
      </header>

      {loading && <Spinner label="Wczytujemy wypożyczenia…" />}
      <ErrorMessage message={error} onRetry={reload} />
      <LoginAgain status={errorStatus} />
      <ErrorMessage message={actionError} />
      {message && (
        <div className="alert alert--success" role="status">
          {message}
        </div>
      )}

      {data &&
        (data.length === 0 ? (
          <div className="empty">
            <h2>Nie masz jeszcze wypożyczeń</h2>
            <p>Wybierz auto, sprawdź cenę i złóż wniosek, a pojawi się tutaj.</p>
            <Link to="/cars" className="btn btn--primary">
              Znajdź auto
            </Link>
          </div>
        ) : (
          <ul className="rent-list" aria-label="Lista wypożyczeń">
            {data.map((r) => (
              <li key={r.rentalId} className="rent-item">
                <div>
                  <h2 className="rent-item__car">{r.carLabel}</h2>
                  <span className="muted small">
                    {r.providerName}, złożono {formatDateTime(r.createdAt)}
                  </span>
                </div>
                <div className="rent-item__dates">
                  {formatDate(r.startDate)} – {formatDate(r.endDate)}
                </div>
                <div className="rent-item__price">{formatMoney(r.totalPrice, r.currency)}</div>
                <div className="rent-item__side">
                  <span className={`status status--${r.status}`}>{CUSTOMER_STATUS_LABELS[r.status] ?? r.status}</span>
                  {r.status === 'Rejected' && r.reason && <span className="small">{r.reason}</span>}
                  {r.canRequestReturn && (
                    <button
                      type="button"
                      className="btn btn--small btn--quiet"
                      disabled={busyId !== null}
                      onClick={() => void onReturn(r)}
                      aria-label={`Zgłoś zwrot: ${r.carLabel}`}
                    >
                      {busyId === r.rentalId ? 'Zgłaszamy…' : 'Zgłoś zwrot'}
                    </button>
                  )}
                  <Link to={`/rentals/${encodeURIComponent(r.rentalId)}`} className="small">
                    Szczegóły
                  </Link>
                </div>
              </li>
            ))}
          </ul>
        ))}
    </>
  )
}
