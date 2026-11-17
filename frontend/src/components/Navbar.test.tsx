import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import type { MeResponse } from '../api/types'
import { AuthProvider } from '../auth/AuthContext'
import { anonymousMe, jsonResponse, mockFetch } from '../test/utils'
import { Navbar } from './Navbar'

function renderNavbar(me: MeResponse) {
  mockFetch((url) => (url.pathname === '/api/v1/auth/me' ? jsonResponse(me) : jsonResponse({}, 404)))
  render(
    <MemoryRouter>
      <AuthProvider>
        <Navbar />
      </AuthProvider>
    </MemoryRouter>,
  )
}

const customer: MeResponse = {
  isAuthenticated: true,
  name: 'Jan Kowalski',
  email: 'jan@example.com',
  roles: [],
  isEmployee: false,
  profileComplete: true,
  authMode: 'Entra',
}

describe('Navbar i AuthContext', () => {
  it('pokazuje Panel pracownika tylko dla roli Employee', async () => {
    renderNavbar({ ...customer, name: 'Anna Nowak', roles: ['Employee'], isEmployee: true })

    expect(await screen.findByRole('link', { name: 'Panel pracownika' })).toHaveAttribute('href', '/employee/rentals')
    expect(screen.getByRole('button', { name: 'Wyloguj' })).toBeInTheDocument()
  })

  it('nie pokazuje Panelu pracownika zwykłemu klientowi', async () => {
    renderNavbar(customer)

    expect(await screen.findByText('Jan Kowalski')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Profil' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Panel pracownika' })).not.toBeInTheDocument()
  })

  it('zalogowany widzi link Moje wypożyczenia', async () => {
    renderNavbar(customer)

    expect(await screen.findByRole('link', { name: 'Moje wypożyczenia' })).toHaveAttribute('href', '/rentals')
  })

  it('niezalogowany widzi Zaloguj, bez Moich wypożyczeń i Profilu', async () => {
    renderNavbar(anonymousMe)

    expect(await screen.findByRole('button', { name: 'Zaloguj' })).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Moje wypożyczenia' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Panel pracownika' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Profil' })).not.toBeInTheDocument()
  })

  it('w trybie DevLogin daje wybór roli Klient albo Pracownik', async () => {
    renderNavbar({ ...anonymousMe, authMode: 'DevLogin' })

    expect(await screen.findByRole('button', { name: 'Klient' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Pracownik' })).toBeInTheDocument()
  })
})
