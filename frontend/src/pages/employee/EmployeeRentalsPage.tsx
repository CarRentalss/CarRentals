import { useCallback } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { getEmployeeRentals } from '../../api/broker'
import type { RentalStatus } from '../../api/types'
import { useApi } from '../../api/useApi'
import { ErrorMessage } from '../../components/ErrorMessage'
import { Spinner } from '../../components/Spinner'
import { STATUS_LABELS, formatDate, formatMoney } from '../../format'

const FILTERS: { label: string; status: RentalStatus | undefined }[] = [
  { label: 'Wszystkie', status: undefined },
  { label: 'Aktywne', status: 'Confirmed' },
  { label: 'Do odbioru', status: 'ReturnRequested' },
  { label: 'Zakończone', status: 'Completed' },
]

function parseStatus(value: string | null): RentalStatus | undefined {
  return FILTERS.find((f) => f.status === value)?.status
}

export function EmployeeRentalsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const status = parseStatus(searchParams.get('status'))

  const fetcher = useCallback((signal: AbortSignal) => getEmployeeRentals(status, signal), [status])
  const { data, loading, error, reload } = useApi(fetcher)

  return (
    <>
      <h1>Panel pracownika: wypożyczenia</h1>
      <p className="muted">Wypożyczenia naszej wypożyczalni (Car Rental API). Innych firm tu nie widać.</p>

      <div className="tabs" role="group" aria-label="Filtr statusu">
        {FILTERS.map((f) => (
          <button
            key={f.label}
            type="button"
            className={f.status === status ? 'btn btn--small btn--active' : 'btn btn--small'}
            aria-pressed={f.status === status}
            onClick={() => setSearchParams(f.status ? { status: f.status } : {})}
          >
            {f.label}
          </button>
        ))}
      </div>

      {loading && <Spinner label="Wczytujemy wypożyczenia..." />}
      <ErrorMessage message={error} onRetry={reload} />

      {data &&
        (data.length === 0 ? (
          <div className="alert alert--info">Brak wypożyczeń dla wybranego filtru.</div>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Auto</th>
                  <th>Klient</th>
                  <th>Termin</th>
                  <th>Status</th>
                  <th className="num">Kwota</th>
                  <th>
                    <span className="sr-only">Akcje</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.map((r) => (
                  <tr key={r.rentalId}>
                    <td data-label="Auto">{r.car}</td>
                    <td data-label="Klient">
                      {r.customerName}
                      <br />
                      <span className="muted small">{r.email}</span>
                    </td>
                    <td data-label="Termin">
                      {formatDate(r.startDate)} - {formatDate(r.endDate)}
                    </td>
                    <td data-label="Status">
                      <span className={`status status--${r.status}`}>{STATUS_LABELS[r.status] ?? r.status}</span>
                    </td>
                    <td data-label="Kwota" className="num">
                      {formatMoney(r.totalPrice)}
                    </td>
                    <td>
                      <Link to={`/employee/rentals/${encodeURIComponent(r.rentalId)}`}>Szczegóły</Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ))}
    </>
  )
}
