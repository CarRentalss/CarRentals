import type { ReactNode } from 'react'
import { useAuth } from '../auth/useAuth'
import { Spinner } from './Spinner'

interface RequireAuthProps {
  children: ReactNode
  /** Strona tylko dla roli Employee. */
  employee?: boolean
}

// Ukrywanie w UI to tylko wygoda. Prawdziwe sprawdzenie robi Broker (401/403).
export function RequireAuth({ children, employee = false }: RequireAuthProps) {
  const { me, loading, login, devLogin } = useAuth()

  if (loading) return <Spinner label="Sprawdzamy logowanie..." />

  if (!me.isAuthenticated) {
    return (
      <section className="card">
        <h1>Wymagane logowanie</h1>
        <p>Ta strona jest dostępna po zalogowaniu.</p>
        {me.authMode === 'DevLogin' ? (
          <div className="actions">
            <button type="button" className="btn btn--primary" onClick={() => devLogin(employee ? 'Employee' : 'Customer', window.location.pathname)}>
              Zaloguj (dev) jako {employee ? 'pracownik' : 'klient'}
            </button>
          </div>
        ) : me.authMode === 'None' ? (
          <p className="muted">Logowanie nie jest teraz skonfigurowane.</p>
        ) : (
          <button type="button" className="btn btn--primary" onClick={() => login()}>
            Zaloguj
          </button>
        )}
      </section>
    )
  }

  if (employee && !me.isEmployee) {
    return (
      <section className="card">
        <h1>Brak dostępu</h1>
        <p>Panel pracownika jest tylko dla pracowników naszej wypożyczalni.</p>
      </section>
    )
  }

  return <>{children}</>
}
