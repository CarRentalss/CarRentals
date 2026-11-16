import { describe, expect, it } from 'vitest'
import { jsonResponse, mockFetch } from '../test/utils'
import { ApiError, apiRequest, api } from './client'

function headersOf(init: RequestInit): Record<string, string> {
  return (init.headers ?? {}) as Record<string, string>
}

describe('client', () => {
  it('dodaje X-CSRF: 1 i credentials same-origin dla POST', async () => {
    const fetchMock = mockFetch(() => jsonResponse({ ok: true }))

    await api.post('/api/v1/offers', { carId: '1' })

    const init = fetchMock.mock.calls[0][1] as RequestInit
    expect(init.method).toBe('POST')
    expect(init.credentials).toBe('same-origin')
    expect(headersOf(init)['X-CSRF']).toBe('1')
    expect(headersOf(init)['Content-Type']).toBe('application/json')
    expect(init.body).toBe(JSON.stringify({ carId: '1' }))
  })

  it('nie dodaje X-CSRF dla GET', async () => {
    const fetchMock = mockFetch(() => jsonResponse([]))

    await api.get('/api/v1/cars/makes')

    const init = fetchMock.mock.calls[0][1] as RequestInit
    expect(headersOf(init)['X-CSRF']).toBeUndefined()
    expect(init.credentials).toBe('same-origin')
  })

  it('dodaje X-CSRF dla PUT i DELETE', async () => {
    const fetchMock = mockFetch(() => new Response(null, { status: 204 }))

    await api.put('/api/v1/profile', {})
    await api.delete('/api/v1/x')

    for (const call of fetchMock.mock.calls) {
      expect(headersOf(call[1] as RequestInit)['X-CSRF']).toBe('1')
    }
  })

  it('nie ustawia Content-Type dla FormData (boundary ustawia przeglądarka)', async () => {
    const fetchMock = mockFetch(() => jsonResponse({ rentalId: '1', status: 'Completed' }))
    const form = new FormData()
    form.append('note', 'ok')

    await api.post('/api/v1/employee/rentals/1/confirm-return', form)

    const init = fetchMock.mock.calls[0][1] as RequestInit
    expect(init.body).toBe(form)
    expect(headersOf(init)['Content-Type']).toBeUndefined()
    expect(headersOf(init)['X-CSRF']).toBe('1')
  })

  it('zamienia ProblemDetails na ApiError z tytułem', async () => {
    mockFetch(() =>
      jsonResponse({ type: 'about:blank', title: 'Nie znaleziono samochodu.', status: 404 }, 404, 'application/problem+json'),
    )

    const error = await apiRequest('/api/v1/cars/our/999').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(404)
    expect((error as ApiError).message).toBe('Nie znaleziono samochodu.')
  })

  it('zamienia ValidationProblemDetails na błędy pól po polsku', async () => {
    mockFetch(() =>
      jsonResponse(
        {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: { EndDate: ['Data zakończenia musi być późniejsza niż data rozpoczęcia.'] },
        },
        400,
        'application/problem+json',
      ),
    )

    const error = (await api.post('/api/v1/offers', {}).catch((e: unknown) => e)) as ApiError

    expect(error.status).toBe(400)
    expect(error.title).toBe('Popraw dane w formularzu.')
    expect(error.fieldError('endDate')).toBe('Data zakończenia musi być późniejsza niż data rozpoczęcia.')
    expect(error.message).toContain('Data zakończenia musi być późniejsza')
  })

  it('daje czytelny komunikat, gdy odpowiedź nie jest JSON-em', async () => {
    mockFetch(() => new Response('Forbidden', { status: 403, headers: { 'Content-Type': 'text/plain' } }))

    const error = (await api.get('/api/v1/employee/rentals').catch((e: unknown) => e)) as ApiError

    expect(error.status).toBe(403)
    expect(error.message).toBe('Nie masz uprawnień do tej strony.')
  })

  it('daje komunikat o braku połączenia, gdy fetch rzuci wyjątek', async () => {
    mockFetch(() => {
      throw new TypeError('Failed to fetch')
    })

    const error = (await api.get('/api/v1/cars').catch((e: unknown) => e)) as ApiError

    expect(error.status).toBe(0)
    expect(error.message).toContain('Brak połączenia')
  })

  it('zwraca undefined dla 204', async () => {
    mockFetch(() => new Response(null, { status: 204 }))
    await expect(api.post('/api/v1/auth/logout')).resolves.toBeUndefined()
  })
})
