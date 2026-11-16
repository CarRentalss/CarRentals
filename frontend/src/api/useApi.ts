import { useCallback, useEffect, useState } from 'react'
import { ApiError, errorMessage, isAbortError } from './client'

type Fetcher<T> = (signal: AbortSignal) => Promise<T>

interface State<T> {
  source: Fetcher<T> | null
  nonce: number
  data: T | null
  error: string | null
  status: number | null
}

export interface ApiState<T> {
  data: T | null
  error: string | null
  /** Kod HTTP błędu (np. 401, 403), 0 = brak połączenia. */
  errorStatus: number | null
  loading: boolean
  reload: () => void
}

/**
 * Pobiera dane przy zmianie fetchera (fetcher musi być z useCallback).
 * null = nic nie pobieraj. Poprzednie żądanie jest przerywane przez AbortController.
 */
export function useApi<T>(fetcher: Fetcher<T> | null): ApiState<T> {
  const [nonce, setNonce] = useState(0)
  const [state, setState] = useState<State<T>>({ source: null, nonce: -1, data: null, error: null, status: null })

  useEffect(() => {
    if (!fetcher) return
    const controller = new AbortController()
    fetcher(controller.signal).then(
      (data) => setState({ source: fetcher, nonce, data, error: null, status: null }),
      (err: unknown) => {
        if (isAbortError(err)) return
        const status = err instanceof ApiError ? err.status : null
        setState({ source: fetcher, nonce, data: null, error: errorMessage(err), status })
      },
    )
    return () => controller.abort()
  }, [fetcher, nonce])

  const reload = useCallback(() => setNonce((n) => n + 1), [])
  const settled = fetcher !== null && state.source === fetcher && state.nonce === nonce

  return {
    data: settled ? state.data : null,
    error: settled ? state.error : null,
    errorStatus: settled ? state.status : null,
    loading: fetcher !== null && !settled,
    reload,
  }
}
