import { Link, Outlet, Route, Routes } from 'react-router-dom'
import { useAuth } from './auth/useAuth'
import { CookieBanner } from './components/CookieBanner'
import { Navbar } from './components/Navbar'
import { RequireAuth } from './components/RequireAuth'
import { CarDetailsPage } from './pages/CarDetailsPage'
import { EmployeeRentalDetailsPage } from './pages/employee/EmployeeRentalDetailsPage'
import { EmployeeRentalsPage } from './pages/employee/EmployeeRentalsPage'
import { LandingPage } from './pages/LandingPage'
import { NotFound } from './pages/NotFound'
import { PrivacyPage } from './pages/PrivacyPage'
import { ProfilePage } from './pages/ProfilePage'
import { SearchPage } from './pages/SearchPage'

function Layout() {
  const { error } = useAuth()
  return (
    <div className="layout">
      <Navbar />
      <main className="container">
        {error && (
          <div className="alert alert--warn">Nie udało się sprawdzić logowania: {error}</div>
        )}
        <Outlet />
      </main>
      <footer className="footer">
        <div className="container">
          <span>
            Car Rentals, projekt zespołowy 2026
            {import.meta.env.VITE_APP_VERSION && <span className="muted"> · wersja {import.meta.env.VITE_APP_VERSION}</span>}
          </span>
          <Link to="/privacy">Polityka prywatności i ciasteczka</Link>
        </div>
      </footer>
      <CookieBanner />
    </div>
  )
}

export function App() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<LandingPage />} />
        <Route path="cars" element={<SearchPage />} />
        <Route path="cars/:provider/:carId" element={<CarDetailsPage />} />
        <Route
          path="profile"
          element={
            <RequireAuth>
              <ProfilePage />
            </RequireAuth>
          }
        />
        <Route path="privacy" element={<PrivacyPage />} />
        <Route
          path="employee/rentals"
          element={
            <RequireAuth employee>
              <EmployeeRentalsPage />
            </RequireAuth>
          }
        />
        <Route
          path="employee/rentals/:rentalId"
          element={
            <RequireAuth employee>
              <EmployeeRentalDetailsPage />
            </RequireAuth>
          }
        />
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  )
}
