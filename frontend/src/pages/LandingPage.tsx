import { useCallback, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { getMakes } from '../api/broker'
import { useApi } from '../api/useApi'
import { ErrorMessage } from '../components/ErrorMessage'
import { Spinner } from '../components/Spinner'

export function LandingPage() {
  const navigate = useNavigate()
  const makesFetcher = useCallback((signal: AbortSignal) => getMakes(signal), [])
  const { data: makes, loading, error, reload } = useApi(makesFetcher)
  const [make, setMake] = useState('')
  const [model, setModel] = useState('')

  const models = makes?.find((m) => m.make === make)?.models ?? []

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    const params = new URLSearchParams()
    if (make) params.set('make', make)
    if (model) params.set('model', model)
    const query = params.toString()
    navigate(query ? `/cars?${query}` : '/cars')
  }

  return (
    <>
      <section className="hero">
        <h1>Porównaj oferty wynajmu aut w jednym miejscu</h1>
        <p>
          Wybierasz auto, podajesz termin, wiek, staż prawa jazdy i miasto, a my pytamy wypożyczalnie o cenę. Wyniki
          przychodzą najpóźniej po 15 sekundach. Każda oferta pokazuje cenę za dzień, cenę ubezpieczenia i do kiedy jest
          ważna (co najmniej 10 minut). Szukać można bez logowania, konto jest potrzebne dopiero do wynajmu.
        </p>
      </section>

      <section className="card">
        <h2>Zacznij wyszukiwanie</h2>
        {loading && <Spinner label="Wczytujemy marki..." />}
        <ErrorMessage message={error} onRetry={reload} />
        <form className="form form--inline" onSubmit={onSubmit}>
          <label>
            Marka
            <select
              value={make}
              onChange={(e) => {
                setMake(e.target.value)
                setModel('')
              }}
            >
              <option value="">Wszystkie marki</option>
              {makes?.map((m) => (
                <option key={m.make} value={m.make}>
                  {m.make}
                </option>
              ))}
            </select>
          </label>
          <label>
            Model
            <select value={model} onChange={(e) => setModel(e.target.value)} disabled={!make}>
              <option value="">Wszystkie modele</option>
              {models.map((m) => (
                <option key={m} value={m}>
                  {m}
                </option>
              ))}
            </select>
          </label>
          <button type="submit" className="btn btn--primary">
            Szukaj
          </button>
        </form>
      </section>

      <section className="steps">
        <div className="card">
          <h3>1. Wybierz auto</h3>
          <p>Lista aut ze wszystkich podłączonych wypożyczalni, z filtrem po marce, modelu i roku.</p>
        </div>
        <div className="card">
          <h3>2. Sprawdź ceny</h3>
          <p>Na stronie auta podajesz dane do wyceny. Po zalogowaniu wypełniamy je z profilu.</p>
        </div>
        <div className="card">
          <h3>3. Wynajmij</h3>
          <p>Wybierasz ofertę i składasz wniosek. Ta część będzie gotowa w kolejnym etapie (KM3).</p>
        </div>
      </section>
    </>
  )
}
