import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { vi } from 'vitest'
import type { MeResponse } from '../api/types'
import { AuthProvider } from '../auth/AuthContext'

export function jsonResponse(body: unknown, status = 200, contentType = 'application/json'): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } })
}

type Handler = (url: URL, init: RequestInit) => Response | Promise<Response>

/** Podmienia globalny fetch. Handler dostaje URL (z query) i opcje żądania. */
export function mockFetch(handler: Handler) {
  const fn = vi.fn((input: RequestInfo | URL, init: RequestInit = {}) => {
    const raw = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    return Promise.resolve(handler(new URL(raw, 'http://localhost'), init))
  })
  vi.stubGlobal('fetch', fn)
  return fn
}

export const anonymousMe: MeResponse = {
  isAuthenticated: false,
  name: null,
  email: null,
  roles: [],
  isEmployee: false,
  profileComplete: false,
  authMode: 'Entra',
}

export function renderWithRouter(ui: ReactElement, { path = '/', route = '/' }: { path?: string; route?: string } = {}) {
  return render(
    <MemoryRouter initialEntries={[route]}>
      <AuthProvider>
        <Routes>
          <Route path={path} element={ui} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )
}
