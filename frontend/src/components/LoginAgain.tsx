import { useAuth } from '../auth/useAuth'

// Broker daje 401, gdy ciasteczko wygasło albo (z Entra) po restarcie nie ma tokena do Car Rental API.
// Wtedy /auth/me z początku sesji jest nieaktualne, więc dajemy przycisk do ponownego logowania.
export function LoginAgain({ status }: { status: number | null }) {
  const { me, login, devLogin } = useAuth()
  if (status !== 401) return null

  return (
    <div className="actions">
      <button
        type="button"
        className="btn btn--primary"
        onClick={() =>
          me.authMode === 'DevLogin'
            ? devLogin(me.isEmployee ? 'Employee' : 'Customer', window.location.pathname + window.location.search)
            : login()
        }
      >
        Zaloguj się ponownie
      </button>
    </div>
  )
}
