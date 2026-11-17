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
        <h1>Porównaj oferty wynajmu aut z kilku wypożyczalni naraz</h1>
        <p className="lead">
          Wybierz auto i termin, a my zapytamy wypożyczalnie o cenę. Każda oferta pokazuje cenę za dzień, koszt
          ubezpieczenia i kwotę łączną. Szukać możesz bez konta, logowanie jest potrzebne dopiero do wynajmu.
        </p>
      </section>

      <section className="panel search-panel" aria-labelledby="search-title">
        <h2 id="search-title">Jakiego auta szukasz?</h2>
        {loading && <Spinner label="Wczytujemy marki…" />}
        <ErrorMessage message={error} onRetry={reload} />
        <form className="form form--inline" onSubmit={onSubmit}>
          <label>
            Marka
            <select
              name="make"
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
            <select name="model" value={model} onChange={(e) => setModel(e.target.value)} disabled={!make}>
              <option value="">{make ? 'Wszystkie modele' : 'Najpierw wybierz markę'}</option>
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

      <section className="band steps-band" aria-labelledby="steps-title">
        <h2 id="steps-title">Jak to działa</h2>
        <ol className="steps">
          <li className="step">
            <h3>Wybierz auto</h3>
            <p>Przeglądasz auta ze wszystkich podłączonych wypożyczalni i zawężasz listę po marce, modelu i roku.</p>
          </li>
          <li className="step">
            <h3>Sprawdź cenę</h3>
            <p>
              Podajesz termin, wiek, staż prawa jazdy i miasto. Odpowiedzi zbieramy najdłużej 15 sekund, a każda oferta
              jest ważna co najmniej 10 minut.
            </p>
          </li>
          <li className="step">
            <h3>Wynajmij</h3>
            <p>Wybierasz ofertę i składasz wniosek. Status i zgłoszenie zwrotu znajdziesz w Moich wypożyczeniach.</p>
          </li>
        </ol>
      </section>
    </>
  )
}
