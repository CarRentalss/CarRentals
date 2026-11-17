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
import { MyRentalsPage } from './pages/rentals/MyRentalsPage'
import { RentalStatusPage } from './pages/rentals/RentalStatusPage'
import { SearchPage } from './pages/SearchPage'

function Layout() {
  const { error } = useAuth()
  return (
    <div className="layout">
      <a className="skip-link" href="#tresc">
        Przejdź do treści
      </a>
      <Navbar />
      <main className="container" id="tresc" tabIndex={-1}>
        {error && <div className="alert alert--warn">Nie udało się sprawdzić logowania: {error}</div>}
        <Outlet />
      </main>
      <footer className="footer">
        <div className="container">
          <span>
            <span className="footer__brand">Car Rentals</span>
            Porównanie ofert wynajmu aut, projekt zespołowy 2026
            {import.meta.env.VITE_APP_VERSION && <>, wersja {import.meta.env.VITE_APP_VERSION}</>}
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
        <Route
          path="rentals"
          element={
            <RequireAuth>
              <MyRentalsPage />
            </RequireAuth>
          }
        />
        <Route
          path="rentals/:rentalId"
          element={
            <RequireAuth>
              <RentalStatusPage />
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
