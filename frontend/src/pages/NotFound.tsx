import { Link } from 'react-router-dom'

export function NotFound() {
  return (
    <section className="card">
      <h1>Nie ma takiej strony</h1>
      <p>Sprawdź adres albo wróć na stronę główną.</p>
      <Link to="/" className="btn btn--primary">
        Strona główna
      </Link>
    </section>
  )
}
