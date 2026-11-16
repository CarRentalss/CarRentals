import type { ProblemDetails } from './types'

// Wspólny klient HTTP do Brokera. Front woła tylko /api/v1 na tym samym originie,
// sesja jest w ciasteczku HttpOnly, więc żadnych tokenów w JS.

const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

const STATUS_MESSAGES: Record<number, string> = {
  400: 'Nieprawidłowe dane.',
  401: 'Musisz się zalogować.',
  403: 'Nie masz uprawnień do tej strony.',
  404: 'Nie znaleziono.',
  409: 'Operacja nie jest teraz możliwa.',
  500: 'Błąd serwera. Spróbuj ponownie za chwilę.',
  502: 'Serwer nie odpowiada. Spróbuj ponownie za chwilę.',
  503: 'Usługa chwilowo niedostępna. Spróbuj ponownie za chwilę.',
  504: 'Serwer nie odpowiedział na czas.',
}

// ASP.NET Core daje ten tytuł po angielsku przy walidacji modelu.
const DEFAULT_VALIDATION_TITLE = 'One or more validation errors occurred.'

export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly fieldErrors: Record<string, string[]>
  readonly traceId?: string

  constructor(status: number, title: string, fieldErrors: Record<string, string[]> = {}, traceId?: string) {
    super(ApiError.buildMessage(title, fieldErrors))
    this.name = 'ApiError'
    this.status = status
    this.title = title
    this.fieldErrors = fieldErrors
    this.traceId = traceId
  }

  /** Pierwszy komunikat dla pola (klucze w camelCase, tak jak w JSON). */
  fieldError(field: string): string | undefined {
    const key = Object.keys(this.fieldErrors).find((k) => k.toLowerCase() === field.toLowerCase())
    return key ? this.fieldErrors[key][0] : undefined
  }

  private static buildMessage(title: string, fieldErrors: Record<string, string[]>): string {
    const details = Object.values(fieldErrors).flat().filter(Boolean)
    if (details.length === 0) return title
    return `${title} ${details.join(' ')}`
  }
}

/** Zamienia odpowiedź z błędem (ProblemDetails albo cokolwiek innego) na ApiError z czytelnym tekstem. */
export async function toApiError(response: Response): Promise<ApiError> {
  const fallback = STATUS_MESSAGES[response.status] ?? `Błąd ${response.status}.`
  let problem: ProblemDetails | null = null

  const contentType = response.headers.get('content-type') ?? ''
  if (contentType.includes('json')) {
    try {
      problem = (await response.json()) as ProblemDetails
    } catch {
      problem = null
    }
  }

  if (!problem) return new ApiError(response.status, fallback)

  const errors = normalizeErrors(problem.errors)
  let title = problem.detail?.trim() || problem.title?.trim() || fallback
  if (title === DEFAULT_VALIDATION_TITLE || (problem.title === DEFAULT_VALIDATION_TITLE && !problem.detail)) {
    title = 'Popraw dane w formularzu.'
  }
  return new ApiError(response.status, title, errors, problem.traceId)
}

function normalizeErrors(errors: ProblemDetails['errors']): Record<string, string[]> {
  if (!errors || typeof errors !== 'object') return {}
  const result: Record<string, string[]> = {}
  for (const [key, value] of Object.entries(errors)) {
    // Klucze z ASP.NET bywają "$.endDate" albo "EndDate", normalizujemy do camelCase.
    const clean = key.replace(/^\$\./, '')
    const camel = clean.length > 0 ? clean[0].toLowerCase() + clean.slice(1) : clean
    result[camel] = Array.isArray(value) ? value.map(String) : [String(value)]
  }
  return result
}

export interface RequestOptions {
  method?: string
  body?: unknown
  signal?: AbortSignal
}

export async function apiRequest<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const method = (options.method ?? 'GET').toUpperCase()
  const headers: Record<string, string> = { Accept: 'application/json' }

  let body: BodyInit | undefined
  if (options.body instanceof FormData) {
    // Content-Type z boundary ustawia przeglądarka.
    body = options.body
  } else if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
    body = JSON.stringify(options.body)
  }

  if (UNSAFE_METHODS.has(method)) {
    headers['X-CSRF'] = '1'
  }

  let response: Response
  try {
    response = await fetch(path, {
      method,
      headers,
      body,
      credentials: 'same-origin',
      signal: options.signal,
    })
  } catch (err) {
    if (err instanceof DOMException && err.name === 'AbortError') throw err
    throw new ApiError(0, 'Brak połączenia z serwerem. Sprawdź internet i spróbuj ponownie.')
  }

  if (!response.ok) {
    throw await toApiError(response)
  }

  if (response.status === 204) return undefined as T

  const contentType = response.headers.get('content-type') ?? ''
  if (!contentType.includes('json')) return undefined as T
  return (await response.json()) as T
}

export const api = {
  get: <T>(path: string, signal?: AbortSignal) => apiRequest<T>(path, { signal }),
  post: <T>(path: string, body?: unknown, signal?: AbortSignal) => apiRequest<T>(path, { method: 'POST', body, signal }),
  put: <T>(path: string, body?: unknown, signal?: AbortSignal) => apiRequest<T>(path, { method: 'PUT', body, signal }),
  delete: <T>(path: string, signal?: AbortSignal) => apiRequest<T>(path, { method: 'DELETE', signal }),
}

/** Tekst błędu do pokazania użytkownikowi. */
export function errorMessage(err: unknown): string {
  if (err instanceof ApiError) return err.message
  if (err instanceof Error && err.message) return err.message
  return 'Coś poszło nie tak.'
}

export function isAbortError(err: unknown): boolean {
  return err instanceof DOMException && err.name === 'AbortError'
}
