import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { CarListItem, PagedResult } from '../api/types'
import { anonymousMe, jsonResponse, mockFetch, renderWithRouter } from '../test/utils'
import { SearchPage } from './SearchPage'

const allCars: CarListItem[] = Array.from({ length: 7 }, (_, i) => ({
  provider: 'our',
  providerName: 'Car Rentals',
  carId: String(i + 1),
  make: 'Toyota',
  model: i < 4 ? 'Corolla' : 'Yaris',
  year: 2020 + (i % 4),
  location: 'Warszawa',
  available: true,
}))

function carsApi() {
  return mockFetch((url) => {
    if (url.pathname === '/api/v1/auth/me') return jsonResponse(anonymousMe)
    if (url.pathname === '/api/v1/cars/makes') return jsonResponse([{ make: 'Toyota', models: ['Corolla', 'Yaris'] }])
    if (url.pathname === '/api/v1/cars') {
      const make = url.searchParams.get('make')
      const page = Number(url.searchParams.get('page') ?? '1')
      const pageSize = Number(url.searchParams.get('pageSize') ?? '5')
      const filtered = allCars.filter((c) => !make || c.make === make)
      const body: PagedResult<CarListItem> = {
        items: filtered.slice((page - 1) * pageSize, page * pageSize),
        page,
        pageSize,
        totalCount: filtered.length,
        totalPages: Math.ceil(filtered.length / pageSize),
        skippedProviders: [],
      }
      return jsonResponse(body)
    }
    return jsonResponse({ title: 'Nie ma' }, 404)
  })
}

describe('SearchPage', () => {
  it('pokazuje wyniki z API i stronicuje po 5, zachowując filtry', async () => {
    const fetchMock = carsApi()
    renderWithRouter(<SearchPage />, { path: '/cars', route: '/cars?make=Toyota' })

    expect(await screen.findByText('Znaleziono aut: 7')).toBeInTheDocument()
    expect(screen.getAllByRole('link', { name: 'Szczegóły i ceny' })).toHaveLength(5)
    expect(screen.getByText('Strona 1 z 2')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Następna' }))

    expect(await screen.findByText('Strona 2 z 2')).toBeInTheDocument()
    await waitFor(() => expect(screen.getAllByRole('link', { name: 'Szczegóły i ceny' })).toHaveLength(2))

    const carCalls = fetchMock.mock.calls
      .map((c) => new URL(String(c[0]), 'http://localhost'))
      .filter((u) => u.pathname === '/api/v1/cars')
    const last = carCalls[carCalls.length - 1]
    expect(last.searchParams.get('page')).toBe('2')
    expect(last.searchParams.get('pageSize')).toBe('5')
    expect(last.searchParams.get('make')).toBe('Toyota')
  })

  it('pokazuje komunikat, gdy nie ma wyników', async () => {
    mockFetch((url) => {
      if (url.pathname === '/api/v1/auth/me') return jsonResponse(anonymousMe)
      if (url.pathname === '/api/v1/cars/makes') return jsonResponse([])
      return jsonResponse({ items: [], page: 1, pageSize: 5, totalCount: 0, totalPages: 0, skippedProviders: [] })
    })
    renderWithRouter(<SearchPage />, { path: '/cars', route: '/cars?make=Fiat' })

    expect(await screen.findByText(/Brak aut spełniających kryteria/)).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Stronicowanie' })).not.toBeInTheDocument()
  })
})
