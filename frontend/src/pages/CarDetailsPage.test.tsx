import { act, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { CarDetails, QuoteResult } from '../api/types'
import { anonymousMe, jsonResponse, mockFetch, renderWithRouter } from '../test/utils'
import { CarDetailsPage } from './CarDetailsPage'

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

function setup(respondToQuote: () => Promise<Response>) {
  const fetchMock = mockFetch((url, init) => {
    if (url.pathname === '/api/v1/auth/me') return jsonResponse(anonymousMe)
    if (url.pathname === '/api/v1/cars/our/1') return jsonResponse(car)
    if (url.pathname === '/api/v1/offers' && init.method === 'POST') return respondToQuote()
    return jsonResponse({ title: 'Nie ma' }, 404)
  })
  renderWithRouter(<CarDetailsPage />, { path: '/cars/:provider/:carId', route: '/cars/our/1' })
  return fetchMock
}

async function fillDriverData() {
  await userEvent.type(await screen.findByLabelText('Wiek kierowcy'), '30')
  await userEvent.type(screen.getByLabelText('Staż prawa jazdy (lata)'), '8')
}

describe('CarDetailsPage', () => {
  it('pokazuje spinner w trakcie wyceny i komunikat, gdy nie ma ofert', async () => {
    let resolveQuote: (r: Response) => void = () => {}
    const fetchMock = setup(() => new Promise<Response>((resolve) => (resolveQuote = resolve)))

    expect(await screen.findByRole('heading', { name: 'Toyota Corolla' })).toBeInTheDocument()
    await fillDriverData()
    await userEvent.click(screen.getByRole('button', { name: 'Sprawdź ceny' }))

    expect(screen.getByText(/Pytamy wypożyczalnie o ceny/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Szukamy ofert…' })).toBeDisabled()

    const empty: QuoteResult = {
      offers: [],
      skippedProviders: [
        { provider: 'our', providerName: 'Car Rentals', reason: 'Rejected', message: 'Samochód jest zajęty w wybranym terminie.' },
      ],
    }
    await act(async () => resolveQuote(jsonResponse(empty)))

    expect(await screen.findByText(/Brak ofert dla podanego terminu/)).toBeInTheDocument()
    expect(screen.getByText(/Samochód jest zajęty w wybranym terminie/)).toBeInTheDocument()
    expect(screen.queryByText(/Pytamy wypożyczalnie o ceny/)).not.toBeInTheDocument()

    const quoteCall = fetchMock.mock.calls.find((c) => String(c[0]) === '/api/v1/offers')!
    const body = JSON.parse(String((quoteCall[1] as RequestInit).body))
    expect(body).toMatchObject({ provider: 'our', carId: '1', age: 30, licenseYears: 8, location: 'Warszawa' })
  })

  it('pokazuje oferty z ceną za dzień, ubezpieczeniem i ważnością', async () => {
    const result: QuoteResult = {
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
    setup(() => Promise.resolve(jsonResponse(result)))

    await fillDriverData()
    await userEvent.click(screen.getByRole('button', { name: 'Sprawdź ceny' }))

    expect(await screen.findByText('Cena za dzień')).toBeInTheDocument()
    expect(screen.getByText('Ubezpieczenie za dzień')).toBeInTheDocument()
    expect(screen.getByText(/Ważna do \d{2}:\d{2}/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Wynajmij/ })).toBeEnabled()
    expect(screen.getByText(/Wynajem wymaga logowania/)).toBeInTheDocument()
  })

  it('nie wysyła wyceny anonimowej bez danych kierowcy', async () => {
    const fetchMock = setup(() => Promise.resolve(jsonResponse({ offers: [], skippedProviders: [] })))

    await screen.findByRole('heading', { name: 'Toyota Corolla' })
    await userEvent.click(screen.getByRole('button', { name: 'Sprawdź ceny' }))

    expect(screen.getByText('Podaj wiek.')).toBeInTheDocument()
    expect(fetchMock.mock.calls.some((c) => String(c[0]) === '/api/v1/offers')).toBe(false)
  })
})
