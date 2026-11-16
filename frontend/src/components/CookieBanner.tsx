import { useState } from 'react'
import { Link } from 'react-router-dom'

const STORAGE_KEY = 'carrentals.cookieNotice'

// localStorage może rzucić wyjątek (tryb prywatny, zablokowane dane strony), wtedy po prostu pokazujemy baner.
function readAccepted(): boolean {
  try {
    return window.localStorage.getItem(STORAGE_KEY) === 'accepted'
  } catch {
    return false
  }
}

function saveAccepted(): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, 'accepted')
  } catch {
    // bez zapisu baner wróci przy następnym wejściu, to nie problem
  }
}

export function CookieBanner() {
  const [visible, setVisible] = useState(() => !readAccepted())
  if (!visible) return null

  return (
    <div className="cookie-banner" role="region" aria-label="Informacja o ciasteczkach">
      <p>
        Używamy tylko niezbędnych ciasteczek: sesja logowania i, na czas logowania, ciasteczka Microsoft. Nie mamy
        ciasteczek reklamowych ani analitycznych. Szczegóły w <Link to="/privacy">polityce prywatności</Link>.
      </p>
      <button
        type="button"
        className="btn btn--primary"
        onClick={() => {
          saveAccepted()
          setVisible(false)
        }}
      >
        Rozumiem
      </button>
    </div>
  )
}
