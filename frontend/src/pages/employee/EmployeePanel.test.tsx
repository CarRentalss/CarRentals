import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import type { EmployeeRentalDetails, EmployeeRentalListItem, MeResponse } from '../../api/types'
import { jsonResponse, mockFetch, renderWithRouter } from '../../test/utils'
import { EmployeeRentalDetailsPage } from './EmployeeRentalDetailsPage'
import { EmployeeRentalsPage } from './EmployeeRentalsPage'

const employeeMe: MeResponse = {
  isAuthenticated: true,
  name: 'anna.nowak',
  email: 'anna.nowak@dev.local',
  roles: ['Employee'],
  isEmployee: true,
  profileComplete: true,
  authMode: 'DevLogin',
}

const rentalId = '8f6c1c1e-4c43-4b8e-9b1a-1d2f3a4b5c6d'

const listItem: EmployeeRentalListItem = {
  rentalId,
  carId: 1,
  car: 'Toyota Corolla (2022)',
  customerName: 'Jan Kowalski',
  email: 'jan.kowalski@example.com',
  startDate: '2026-11-20',
  endDate: '2026-11-23',
  status: 'ReturnRequested',
  totalPrice: 645,
}

const details: EmployeeRentalDetails = {
  rentalId,
  carId: 1,
  car: 'Toyota Corolla (2022)',
  location: 'Warszawa',
  firstName: 'Jan',
  lastName: 'Kowalski',
  email: 'jan.kowalski@example.com',
  startDate: '2026-11-20',
  endDate: '2026-11-23',
  status: 'ReturnRequested',
  reason: null,
  totalPrice: 645,
  brokerName: 'OurBroker',
  createdAt: '2026-11-16T17:00:00Z',
  returnRequestedAt: '2026-11-16T17:30:00Z',
  completedAt: null,
  completedBy: null,
  returnNote: null,
  hasAttachment: false,
}

describe('Panel pracownika', () => {
  it('lista: filtr "Do odbioru" wysyła status=ReturnRequested', async () => {
    const fetchMock = mockFetch((url) => {
      if (url.pathname === '/api/v1/auth/me') return jsonResponse(employeeMe)
      if (url.pathname === '/api/v1/employee/rentals') return jsonResponse([listItem])
      return jsonResponse({ title: 'Nie ma' }, 404)
    })
    renderWithRouter(<EmployeeRentalsPage />, { path: '/employee/rentals', route: '/employee/rentals' })

    expect(await screen.findByText('Jan Kowalski')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Do odbioru' }))

    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([u]) => String(u) === '/api/v1/employee/rentals?status=ReturnRequested')).toBe(
        true,
      ),
    )
  })

  it('lista: 401 z Brokera pokazuje przycisk ponownego logowania', async () => {
    mockFetch((url) => {
      if (url.pathname === '/api/v1/auth/me') return jsonResponse(employeeMe)
      return jsonResponse(
        { title: 'Sesja wygasła. Zaloguj się ponownie.', status: 401 },
        401,
        'application/problem+json',
      )
    })
    renderWithRouter(<EmployeeRentalsPage />, { path: '/employee/rentals', route: '/employee/rentals' })

    expect(await screen.findByText('Sesja wygasła. Zaloguj się ponownie.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Zaloguj się ponownie' })).toBeInTheDocument()
  })

  it('szczegóły: potwierdzenie zwrotu idzie jako multipart z X-CSRF i odświeża status', async () => {
    let current = details
    const fetchMock = mockFetch((url, init) => {
      if (url.pathname === '/api/v1/auth/me') return jsonResponse(employeeMe)
      if (url.pathname === `/api/v1/employee/rentals/${rentalId}/confirm-return` && init.method === 'POST') {
        current = {
          ...details,
          status: 'Completed',
          returnNote: 'Auto czyste.',
          hasAttachment: true,
          completedBy: 'anna.nowak',
        }
        return jsonResponse({ rentalId, status: 'Completed' })
      }
      if (url.pathname === `/api/v1/employee/rentals/${rentalId}`) return jsonResponse(current)
      return jsonResponse({ title: 'Nie ma' }, 404)
    })
    renderWithRouter(<EmployeeRentalDetailsPage />, {
      path: '/employee/rentals/:rentalId',
      route: `/employee/rentals/${rentalId}`,
    })

    await userEvent.type(await screen.findByLabelText('Notatka o stanie auta'), 'Auto czyste.')
    const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], 'zwrot.png', { type: 'image/png' })
    await userEvent.upload(screen.getByLabelText('Zdjęcie albo PDF'), file)
    await userEvent.click(screen.getByRole('button', { name: 'Potwierdź zwrot' }))

    expect(await screen.findByText('Zwrot potwierdzony. Wypożyczenie jest zakończone.')).toBeInTheDocument()
    expect(await screen.findByRole('link', { name: 'Pobierz załącznik' })).toHaveAttribute(
      'href',
      `/api/v1/employee/rentals/${rentalId}/attachment`,
    )

    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!
    const init = post[1]!
    expect((init.headers as Record<string, string>)['X-CSRF']).toBe('1')
    expect((init.headers as Record<string, string>)['Content-Type']).toBeUndefined()
    const form = init.body as FormData
    expect(form.get('note')).toBe('Auto czyste.')
    expect((form.get('file') as File).name).toBe('zwrot.png')
  })

  it('szczegóły: za mała notatka i brak pliku blokują wysyłkę', async () => {
    const fetchMock = mockFetch((url) => {
      if (url.pathname === '/api/v1/auth/me') return jsonResponse(employeeMe)
      if (url.pathname === `/api/v1/employee/rentals/${rentalId}`) return jsonResponse(details)
      return jsonResponse({ title: 'Nie ma' }, 404)
    })
    renderWithRouter(<EmployeeRentalDetailsPage />, {
      path: '/employee/rentals/:rentalId',
      route: `/employee/rentals/${rentalId}`,
    })

    await userEvent.type(await screen.findByLabelText('Notatka o stanie auta'), 'ok')
    await userEvent.click(screen.getByRole('button', { name: 'Potwierdź zwrot' }))

    expect(screen.getByText('Notatka musi mieć od 3 do 1000 znaków.')).toBeInTheDocument()
    expect(screen.getByText('Dodaj zdjęcie (jpg, png) albo PDF.')).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false)
  })
})
