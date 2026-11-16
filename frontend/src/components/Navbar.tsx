import { useState } from 'react'
import { Link, NavLink } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'

export function Navbar() {
  const { me, loading, login, devLogin, logout } = useAuth()
  const [open, setOpen] = useState(false)
  const close = () => setOpen(false)

  const linkClass = ({ isActive }: { isActive: boolean }) => (isActive ? 'nav__link nav__link--active' : 'nav__link')

  return (
    <header className="navbar">
      <div className="navbar__inner">
        <Link to="/" className="navbar__brand" onClick={close}>
          Car Rentals
        </Link>
        <button
          type="button"
          className="navbar__toggle"
          aria-expanded={open}
          aria-controls="main-menu"
          onClick={() => setOpen((v) => !v)}
        >
          Menu
        </button>
        <nav id="main-menu" className={open ? 'nav nav--open' : 'nav'} aria-label="Menu główne">
          <NavLink to="/cars" className={linkClass} onClick={close}>
            Wyszukiwarka
          </NavLink>
          <span className="nav__link nav__link--disabled" aria-disabled="true" title="Będzie w KM3">
            Moje wypożyczenia <small>(KM3)</small>
          </span>
          {me.isAuthenticated && (
            <NavLink to="/profile" className={linkClass} onClick={close}>
              Profil{!me.profileComplete && <span className="badge" title="Profil niekompletny">!</span>}
            </NavLink>
          )}
          {me.isAuthenticated && me.isEmployee && (
            <NavLink to="/employee/rentals" className={linkClass} onClick={close}>
              Panel pracownika
            </NavLink>
          )}
          <div className="nav__auth">
            {loading ? null : me.isAuthenticated ? (
              <>
                <span className="nav__user">{me.name ?? me.email}</span>
                <button type="button" className="btn btn--small" onClick={() => void logout()}>
                  Wyloguj
                </button>
              </>
            ) : me.authMode === 'None' ? (
              <button type="button" className="btn btn--small" disabled title="Logowanie nie jest skonfigurowane">
                Zaloguj
              </button>
            ) : me.authMode === 'DevLogin' ? (
              // Lokalnie bez Entra: wybór roli, Broker ustawia ciasteczko bez hasła.
              <span className="nav__dev" title="Logowanie deweloperskie, tylko lokalnie">
                <small>Zaloguj (dev):</small>
                <button type="button" className="btn btn--small btn--primary" onClick={() => devLogin('Customer')}>
                  Klient
                </button>
                <button type="button" className="btn btn--small" onClick={() => devLogin('Employee')}>
                  Pracownik
                </button>
              </span>
            ) : (
              <button type="button" className="btn btn--small btn--primary" onClick={() => login()}>
                Zaloguj
              </button>
            )}
          </div>
        </nav>
      </div>
    </header>
  )
}
