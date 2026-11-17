import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { devLoginUrl, getMe, loginUrl, logout as logoutRequest } from '../api/broker'
import { errorMessage } from '../api/client'
import type { MeResponse } from '../api/types'
import { ANONYMOUS, AuthContext, type AuthContextValue } from './authContextValue'

// Konta do logowania deweloperskiego (tylko lokalnie, Broker w Development z Auth:UseDevLogin=true).
const DEV_USERS = {
  Customer: { name: 'jan.kowalski', role: null },
  Employee: { name: 'anna.nowak', role: 'Employee' },
} as const

function currentPath(): string {
  return window.location.pathname + window.location.search
}

interface LoadResult {
  me: MeResponse
  error: string | null
}

async function loadMe(): Promise<LoadResult> {
  try {
    const result = await getMe()
    return { me: { ...ANONYMOUS, ...result, roles: result.roles ?? [] }, error: null }
  } catch (err) {
    return { me: ANONYMOUS, error: errorMessage(err) }
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<MeResponse>(ANONYMOUS)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const apply = useCallback((result: LoadResult) => {
    setMe(result.me)
    setError(result.error)
    setLoading(false)
  }, [])

  const refresh = useCallback(async () => {
    apply(await loadMe())
  }, [apply])

  useEffect(() => {
    let cancelled = false
    void loadMe().then((result) => {
      if (!cancelled) apply(result)
    })
    return () => {
      cancelled = true
    }
  }, [apply])

  const login = useCallback((returnUrl?: string) => {
    window.location.assign(loginUrl(returnUrl ?? currentPath()))
  }, [])

  const devLogin = useCallback((role: 'Customer' | 'Employee', returnUrl?: string) => {
    const user = DEV_USERS[role]
    const target = returnUrl ?? (role === 'Employee' ? '/employee/rentals' : currentPath())
    window.location.assign(devLoginUrl(user.name, user.role, target))
  }, [])

  const logout = useCallback(async () => {
    try {
      await logoutRequest()
    } finally {
      await refresh()
    }
  }, [refresh])

  const value = useMemo<AuthContextValue>(
    () => ({ me, loading, error, refresh, login, devLogin, logout }),
    [me, loading, error, refresh, login, devLogin, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
