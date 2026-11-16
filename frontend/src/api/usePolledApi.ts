import { useCallback, useEffect, useState } from 'react'
import { ApiError, errorMessage, isAbortError } from './client'

type Fetcher<T> = (signal: AbortSignal) => Promise<T>

interface State<T> {
  source: Fetcher<T> | null
  data: T | null
  error: string | null
  status: number | null
}

export interface PolledApiState<T> {
  data: T | null
  error: string | null
  errorStatus: number | null
  /** Tylko pierwsze wczytanie, kolejne odświeżenia nie chowają danych. */
  loading: boolean
  /** Podmiana danych po akcji użytkownika (np. po zgłoszeniu zwrotu). */
  setData: (data: T) => void
  reload: () => void
}

/**
 * Jak useApi, ale po każdej odpowiedzi pyta jeszcze raz co intervalMs, dopóki keepPolling(data) zwraca true
 * (np. wynajem ma status Pending). Fetcher i keepPolling muszą być stałe (useCallback albo funkcja z modułu).
 */
export function usePolledApi<T>(fetcher: Fetcher<T>, intervalMs: number, keepPolling: (data: T) => boolean): PolledApiState<T> {
  const [nonce, setNonce] = useState(0)
  const [state, setState] = useState<State<T>>({ source: null, data: null, error: null, status: null })

  useEffect(() => {
    const controller = new AbortController()
    let timer: number | undefined

    const run = () => {
      fetcher(controller.signal).then(
        (data) => {
          setState({ source: fetcher, data, error: null, status: null })
          if (keepPolling(data)) timer = window.setTimeout(run, intervalMs)
        },
        (err: unknown) => {
          if (isAbortError(err)) return
          const status = err instanceof ApiError ? err.status : null
          // Ostatnie dane zostają na ekranie, pokazujemy tylko błąd
          setState((prev) => ({ source: fetcher, data: prev.source === fetcher ? prev.data : null, error: errorMessage(err), status }))
        },
      )
    }

    run()
    return () => {
      controller.abort()
      window.clearTimeout(timer)
    }
  }, [fetcher, intervalMs, keepPolling, nonce])

  const setData = useCallback((data: T) => setState({ source: fetcher, data, error: null, status: null }), [fetcher])
  const reload = useCallback(() => setNonce((n) => n + 1), [])
  const current = state.source === fetcher

  return {
    data: current ? state.data : null,
    error: current ? state.error : null,
    errorStatus: current ? state.status : null,
    loading: !current,
    setData,
    reload,
  }
}
