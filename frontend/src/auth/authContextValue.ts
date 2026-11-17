import { createContext } from 'react'
import type { MeResponse } from '../api/types'

export interface AuthContextValue {
  me: MeResponse
  loading: boolean
  /** Błąd przy pobieraniu /auth/me (np. Broker nie działa). */
  error: string | null
  refresh: () => Promise<void>
  /** Przekierowanie na logowanie Brokera, po powrocie wracamy na returnUrl. */
  login: (returnUrl?: string) => void
  /** Tylko Development (authMode = DevLogin). */
  devLogin: (role: 'Customer' | 'Employee', returnUrl?: string) => void
  logout: () => Promise<void>
}

export const ANONYMOUS: MeResponse = {
  isAuthenticated: false,
  name: null,
  email: null,
  roles: [],
  isEmployee: false,
  profileComplete: false,
  authMode: 'Entra',
}

export const AuthContext = createContext<AuthContextValue | null>(null)
