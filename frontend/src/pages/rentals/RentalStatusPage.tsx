import { useCallback } from 'react'
import { Link, useParams } from 'react-router-dom'
import { getMyRental } from '../../api/broker'
import { usePolledApi } from '../../api/usePolledApi'
import { ErrorMessage } from '../../components/ErrorMessage'
import { LoginAgain } from '../../components/LoginAgain'
import { Spinner } from '../../components/Spinner'
import { CUSTOMER_STATUS_LABELS, formatDate, formatMoney } from '../../format'
import { POLL_INTERVAL_MS, isPending } from './rentalPolling'

// Ekran po złożeniu wniosku. Wypożyczalnia potwierdza go w tle, więc póki jest Pending, pytamy co 3 s.
export function RentalStatusPage() {
  const { rentalId = '' } = useParams()
  const fetcher = useCallback((signal: AbortSignal) => getMyRental(rentalId, signal), [rentalId])
  const { data: rental, loading, error, errorStatus, reload } = usePolledApi(fetcher, POLL_INTERVAL_MS, isPending)

  return (
    <>
      <Link to="/rentals" className="back-link">
        Moje wypożyczenia
      </Link>
      <header className="page-head">
        <h1>Status wypożyczenia</h1>
      </header>
      {loading && <Spinner label="Wczytujemy wypożyczenie…" />}
      <ErrorMessage message={error} onRetry={errorStatus === 404 ? undefined : reload} />
      <LoginAgain status={errorStatus} />
      {rental && (
        <section className="panel narrow">
          <div className="status-head">
            <div>
              <h2>{rental.carLabel}</h2>
              <span className={`status status--large status--${rental.status}`}>
                {CUSTOMER_STATUS_LABELS[rental.status] ?? rental.status}
              </span>
            </div>
            <dl className="price-figure">
              <dt>Kwota</dt>
              <dd>{formatMoney(rental.totalPrice, rental.currency)}</dd>
            </dl>
          </div>
          <dl className="details">
            <dt>Wypożyczalnia</dt>
            <dd>{rental.providerName}</dd>
            <dt>Termin</dt>
            <dd>
              {formatDate(rental.startDate)} – {formatDate(rental.endDate)}
            </dd>
            <dt>Numer u wypożyczalni</dt>
            <dd className="mono">{rental.providerRentalId}</dd>
          </dl>
          {rental.status === 'Pending' && (
            <Spinner label="Wypożyczalnia sprawdza wniosek. Status odświeża się sam co kilka sekund…" />
          )}
          {rental.status === 'Confirmed' && (
            <div className="alert alert--success" role="status">
              Wypożyczalnia potwierdziła wynajem. Auto czeka na Ciebie w podanym terminie.
            </div>
          )}
          {rental.status === 'Rejected' && (
            <div className="alert alert--error">
              Wypożyczalnia odrzuciła wniosek.{rental.reason && <> Powód: {rental.reason}</>}
            </div>
          )}
          <div className="actions">
            <Link to="/rentals" className="btn btn--quiet">
              Przejdź do moich wypożyczeń
            </Link>
          </div>
        </section>
      )}
    </>
  )
}
