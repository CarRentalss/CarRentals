import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import type { CarDetails, MeResponse, ProfileResponse, QuoteResult, RentalResponse } from '../api/types'
import { AuthProvider } from '../auth/AuthContext'
import { CarDetailsPage } from '../pages/CarDetailsPage'
import { jsonResponse, mockFetch } from '../test/utils'

const customerMe: MeResponse = {
  isAuthenticated: true,
  name: 'Jan Kowalski',
  email: 'jan@example.com',
  roles: [],
  isEmployee: false,
  profileComplete: true,
  authMode: 'Entra',
}

const car: CarDetails = {
  provider: 'our',
  providerName: 'Car Rentals',
  carId: '1',
  make: 'Toyota',
  model: 'Corolla',
  year: 2022,
  location: 'Warszawa',
  available: true,
}

const profile: ProfileResponse = {
  firstName: 'Jan',
  lastName: 'Kowalski',
  email: 'jan@example.com',
  dateOfBirth: '1996-05-14',
  licenseObtainedOn: '2018-06-01',
  location: 'Warszawa',
  age: 30,
  licenseYears: 8,
  isComplete: true,
  missingFields: [],
  updatedAt: '2026-11-16T18:00:00Z',
}

const quote: QuoteResult = {
  offers: [
    {
      quoteId: 'q1',
      provider: 'our',
      providerName: 'Car Rentals',
      offerId: 'o1',
      carId: '1',
      carLabel: 'Toyota Corolla 2022',
      dailyPrice: 180,
      dailyInsurancePrice: 35,
      totalPrice: 645,
      currency: 'PLN',
      startDate: '2026-11-20',
      endDate: '2026-11-23',
      days: 3,
      expiresAt: '2026-11-16T18:10:00Z',
    },
  ],
  skippedProviders: [],
}

const pendingRental: RentalResponse = {
  rentalId: 'r1',
  provider: 'our',
  providerName: 'Car Rentals',
  providerRentalId: 'abc',
  carId: '1',
  carLabel: 'Toyota Corolla 2022',
  startDate: '2026-11-20',
  endDate: '2026-11-23',
  totalPrice: 645,
  currency: 'PLN',
  status: 'Pending',
  reason: null,
  canRequestReturn: false,
  createdAt: '2026-11-16T18:01:00Z',
  updatedAt: '2026-11-16T18:01:00Z',
}

function setup(respondToRental: () => Response) {
  const fetchMock = mockFetch((url, init) => {
    if (url.pathname === '/api/v1/auth/me') return jsonResponse(customerMe)
    if (url.pathname === '/api/v1/profile') return jsonResponse(profile)
    if (url.pathname === '/api/v1/cars/our/1') return jsonResponse(car)
    if (url.pathname === '/api/v1/offers' && init.method === 'POST') return jsonResponse(quote)
    if (url.pathname === '/api/v1/rentals' && init.method === 'POST') return respondToRental()
    return jsonResponse({ title: 'Nie ma' }, 404)
  })
  render(
    <MemoryRouter initialEntries={['/cars/our/1']}>
      <AuthProvider>
        <Routes>
          <Route path="/cars/:provider/:carId" element={<CarDetailsPage />} />
          <Route path="/rentals/:rentalId" element={<h1>Ekran statusu</h1>} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )
  return fetchMock
}

async function openRentForm() {
  await userEvent.click(await screen.findByRole('button', { name: 'Sprawdź ceny' }))
  await userEvent.click(await screen.findByRole('button', { name: 'Wynajmij: Car Rentals' }))
}

describe('Wynajem z oferty', () => {
  it('wypełnia dane kierowcy z profilu, wysyła wniosek i przechodzi do statusu', async () => {
    const fetchMock = setup(() => jsonResponse(pendingRental, 202))

    await openRentForm()
    expect(screen.getByLabelText('Imię')).toHaveValue('Jan')
    expect(screen.getByLabelText('Nazwisko')).toHaveValue('Kowalski')
    expect(screen.getByLabelText('Email')).toHaveValue('jan@example.com')

    await userEvent.click(screen.getByRole('button', { name: 'Potwierdź wynajem' }))

    expect(await screen.findByRole('heading', { name: 'Ekran statusu' })).toBeInTheDocument()
    const call = fetchMock.mock.calls.find((c) => String(c[0]) === '/api/v1/rentals')!
    const init = call[1] as RequestInit
    expect((init.headers as Record<string, string>)['X-CSRF']).toBe('1')
    expect(JSON.parse(String(init.body))).toEqual({
      quoteId: 'q1',
      firstName: 'Jan',
      lastName: 'Kowalski',
      email: 'jan@example.com',
    })
  })

  it('nie wysyła wniosku bez emaila', async () => {
    const fetchMock = setup(() => jsonResponse(pendingRental, 202))

    await openRentForm()
    await userEvent.clear(screen.getByLabelText('Email'))
    await userEvent.click(screen.getByRole('button', { name: 'Potwierdź wynajem' }))

    expect(screen.getByText('Podaj email.')).toBeInTheDocument()
    expect(fetchMock.mock.calls.some((c) => String(c[0]) === '/api/v1/rentals')).toBe(false)
  })

  it('pokazuje komunikat, gdy oferta wygasła (410)', async () => {
    setup(() =>
      jsonResponse({ title: 'Oferta wygasła. Sprawdź cenę jeszcze raz.', status: 410 }, 410, 'application/problem+json'),
    )

    await openRentForm()
    await userEvent.click(screen.getByRole('button', { name: 'Potwierdź wynajem' }))

    expect(await screen.findByText('Oferta wygasła. Sprawdź cenę jeszcze raz.')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Potwierdź wynajem' })).toBeEnabled())
  })
})
