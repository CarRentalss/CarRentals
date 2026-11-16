import { useCallback, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { getCars, getMakes } from '../api/broker'
import type { MakeModels } from '../api/types'
import { useApi } from '../api/useApi'
import { ErrorMessage } from '../components/ErrorMessage'
import { Pagination } from '../components/Pagination'
import { SkippedNotice } from '../components/SkippedNotice'
import { Spinner } from '../components/Spinner'

const PAGE_SIZE = 5

function parsePositiveInt(value: string | null): number | undefined {
  if (!value) return undefined
  const n = Number.parseInt(value, 10)
  return Number.isFinite(n) && n > 0 ? n : undefined
}

export function SearchPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const make = searchParams.get('make') ?? ''
  const model = searchParams.get('model') ?? ''
  const year = parsePositiveInt(searchParams.get('year'))
  const page = parsePositiveInt(searchParams.get('page')) ?? 1

  const makesFetcher = useCallback((signal: AbortSignal) => getMakes(signal), [])
  const makes = useApi(makesFetcher)

  const carsFetcher = useCallback(
    (signal: AbortSignal) =>
      getCars({ make: make || undefined, model: model || undefined, year, page, pageSize: PAGE_SIZE }, signal),
    [make, model, year, page],
  )
  const cars = useApi(carsFetcher)

  function applyFilters(filters: { make: string; model: string; year: string }) {
    const next = new URLSearchParams()
    if (filters.make) next.set('make', filters.make)
    if (filters.model) next.set('model', filters.model)
    if (filters.year) next.set('year', filters.year)
    setSearchParams(next)
  }

  function changePage(newPage: number) {
    // Filtry zostają w URL, zmieniamy tylko numer strony.
    const next = new URLSearchParams(searchParams)
    next.set('page', String(newPage))
    setSearchParams(next)
    window.scrollTo?.({ top: 0 })
  }

  const result = cars.data

  return (
    <>
      <h1>Wyszukiwarka aut</h1>
      <Filters
        // Nowy klucz przy zmianie URL, żeby formularz wziął wartości z adresu.
        key={`${make}|${model}|${year ?? ''}`}
        makes={makes.data ?? []}
        initial={{ make, model, year: year ? String(year) : '' }}
        onApply={applyFilters}
      />

      {cars.loading && <Spinner label="Wczytujemy auta..." />}
      <ErrorMessage message={cars.error} onRetry={cars.reload} />

      {result && (
        <>
          <SkippedNotice skipped={result.skippedProviders} />
          {result.items.length === 0 ? (
            <div className="alert alert--info">
              {result.totalCount === 0
                ? 'Brak aut spełniających kryteria. Zmień albo wyczyść filtry.'
                : 'Na tej stronie nie ma już wyników.'}
            </div>
          ) : (
            <>
              <p className="muted">Znaleziono aut: {result.totalCount}</p>
              <ul className="car-list">
                {result.items.map((car) => (
                  <li key={`${car.provider}-${car.carId}`} className="card car-card">
                    <div>
                      <h2 className="car-card__title">
                        {car.make} {car.model}
                      </h2>
                      <p className="muted">
                        Rok {car.year} · {car.location} · {car.providerName}
                      </p>
                      {!car.available && <span className="tag tag--warn">Chwilowo niedostępne</span>}
                    </div>
                    <Link
                      className="btn btn--primary"
                      to={`/cars/${encodeURIComponent(car.provider)}/${encodeURIComponent(car.carId)}`}
                    >
                      Szczegóły i ceny
                    </Link>
                  </li>
                ))}
              </ul>
            </>
          )}
          <Pagination page={result.page} totalPages={result.totalPages} onPageChange={changePage} />
        </>
      )}
    </>
  )
}

interface FiltersProps {
  makes: MakeModels[]
  initial: { make: string; model: string; year: string }
  onApply: (filters: { make: string; model: string; year: string }) => void
}

function Filters({ makes, initial, onApply }: FiltersProps) {
  const [make, setMake] = useState(initial.make)
  const [model, setModel] = useState(initial.model)
  const [year, setYear] = useState(initial.year)
  const models = makes.find((m) => m.make.toLowerCase() === make.toLowerCase())?.models ?? []
  const maxYear = new Date().getFullYear() + 1

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    onApply({ make, model, year })
  }

  return (
    <form className="card form form--inline" onSubmit={onSubmit} aria-label="Filtry">
      <label>
        Marka
        <select
          value={make}
          onChange={(e) => {
            setMake(e.target.value)
            setModel('')
          }}
        >
          <option value="">Wszystkie</option>
          {/* Marka z URL może nie być jeszcze na liście (lista się wczytuje). */}
          {make && !makes.some((m) => m.make.toLowerCase() === make.toLowerCase()) && (
            <option value={make}>{make}</option>
          )}
          {makes.map((m) => (
            <option key={m.make} value={m.make}>
              {m.make}
            </option>
          ))}
        </select>
      </label>
      <label>
        Model
        <select value={model} onChange={(e) => setModel(e.target.value)} disabled={!make}>
          <option value="">Wszystkie</option>
          {model && !models.some((m) => m.toLowerCase() === model.toLowerCase()) && (
            <option value={model}>{model}</option>
          )}
          {models.map((m) => (
            <option key={m} value={m}>
              {m}
            </option>
          ))}
        </select>
      </label>
      <label>
        Rok produkcji
        <input
          type="number"
          inputMode="numeric"
          min={1990}
          max={maxYear}
          placeholder="np. 2022"
          value={year}
          onChange={(e) => setYear(e.target.value)}
        />
      </label>
      <div className="actions">
        <button type="submit" className="btn btn--primary">
          Filtruj
        </button>
        <button
          type="button"
          className="btn"
          onClick={() => {
            setMake('')
            setModel('')
            setYear('')
            onApply({ make: '', model: '', year: '' })
          }}
        >
          Wyczyść
        </button>
      </div>
    </form>
  )
}
