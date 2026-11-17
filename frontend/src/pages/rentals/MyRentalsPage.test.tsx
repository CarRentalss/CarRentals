import { act, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { MeResponse, RentalResponse } from '../../api/types'
import { jsonResponse, mockFetch, renderWithRouter } from '../../test/utils'
import { MyRentalsPage } from './MyRentalsPage'
import { RentalStatusPage } from './RentalStatusPage'

const customerMe: MeResponse = {
  isAuthenticated: true,
  name: 'Jan Kowalski',
  email: 'jan@example.com',
  roles: [],
  isEmployee: false,
  profileComplete: true,
  authMode: 'Entra',
}

function rental(overrides: Partial<RentalResponse>): RentalResponse {
  return {
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
    status: 'Confirmed',
    reason: null,
    canRequestReturn: true,
    createdAt: '2026-11-16T18:01:00Z',
    updatedAt: '2026-11-16T18:01:00Z',
    ...overrides,
  }
}

afterEach(() => {
  vi.useRealTimers()
})

describe('Moje wypożyczenia', () => {
  it('pokazuje listę ze statusami i przycisk zwrotu tylko dla aktywnego', async () => {
    mockFetch((url) => {
      if (url.pathname === '/api/v1/auth/me') return jsonResponse(customerMe)
      if (url.pathname === '/api/v1/rentals')
        return jsonResponse([
          rental({}),
          rental({ rentalId: 'r2', carLabel: 'Skoda Octavia 2021', status: 'Completed', canRequestReturn: false }),
        ])
      return jsonResponse({ title: 'Nie ma' }, 404)
    })
    renderWithRouter(<MyRentalsPage />, { path: '/rentals', route: '/rentals' })

    expect(await screen.findByText('Toyota Corolla 2022')).toBeInTheDocument()
    expect(screen.getByText('Potwierdzone')).toBeInTheDocument()
    expect(screen.getByText('Zakończone')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Zgłoś zwrot: Toyota Corolla 2022' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Zgłoś zwrot: Skoda Octavia 2021' })).not.toBeInTheDocument()
  })

  it('zgłoszenie zwrotu wysyła POST i zmienia status na liście', async () => {
    const fetchMock = mockFetch((url, init) => {
      if (url.pathname === '/api/v1/auth/me') return jsonResponse(customerMe)
      if (url.pathname === '/api/v1/rentals') return jsonResponse([rental({})])
      if (url.pathname === '/api/v1/rentals/r1/return' && init.method === 'POST')
        return jsonResponse(rental({ status: 'ReturnRequested', canRequestReturn: false }))
      return jsonResponse({ title: 'Nie ma' }, 404)
    })
    renderWithRouter(<MyRentalsPage />, { path: '/rentals', route: '/rentals' })

    await userEvent.click(await screen.findByRole('button', { name: 'Zgłoś zwrot: Toyota Corolla 2022' }))

    expect(await screen.findByText('Zwrot zgłoszony')).toBeInTheDocument()
    expect(screen.getByText(/Pracownik wypożyczalni potwierdzi odbiór auta/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Zgłoś zwrot/ })).not.toBeInTheDocument()
    const call = fetchMock.mock.calls.find((c) => String(c[0]) === '/api/v1/rentals/r1/return')!
    expect(((call[1] as RequestInit).headers as Record<string, string>)['X-CSRF']).toBe('1')
  })

  it('pusta lista pokazuje link do wyszukiwarki', async () => {
    mockFetch((url) => {
      if (url.pathname === '/api/v1/auth/me') return jsonResponse(customerMe)
      if (url.pathname === '/api/v1/rentals') return jsonResponse([])
      return jsonResponse({ title: 'Nie ma' }, 404)
    })
    renderWithRouter(<MyRentalsPage />, { path: '/rentals', route: '/rentals' })

    expect(await screen.findByText(/Nie masz jeszcze wypożyczeń/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Znajdź auto' })).toHaveAttribute('href', '/cars')
  })
})

describe('Status wypożyczenia', () => {
  it('odświeża status co kilka sekund, aż wypożyczalnia potwierdzi', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    let calls = 0
    mockFetch((url) => {
      if (url.pathname === '/api/v1/auth/me') return jsonResponse(customerMe)
      if (url.pathname === '/api/v1/rentals/r1') {
        calls++
        return jsonResponse(rental(calls < 2 ? { status: 'Pending', canRequestReturn: false } : {}))
      }
      return jsonResponse({ title: 'Nie ma' }, 404)
    })
    renderWithRouter(<RentalStatusPage />, { path: '/rentals/:rentalId', route: '/rentals/r1' })

    expect(await screen.findByText('Czeka na potwierdzenie')).toBeInTheDocument()
    expect(screen.getByText(/Status odświeża się sam/)).toBeInTheDocument()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(3000)
    })

    expect(await screen.findByText('Potwierdzone')).toBeInTheDocument()
    expect(screen.getByText(/Wypożyczalnia potwierdziła wynajem/)).toBeInTheDocument()
    expect(calls).toBe(2)
  })
})
