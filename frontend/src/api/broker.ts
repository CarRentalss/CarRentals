import { api } from './client'
import type {
  CarDetails,
  CarListItem,
  CarSearchParams,
  CreateRentalRequest,
  EmployeeRentalDetails,
  EmployeeRentalListItem,
  MakeModels,
  MeResponse,
  PagedResult,
  ProfileResponse,
  QuoteRequest,
  QuoteResult,
  RentalResponse,
  RentalStatus,
  RentalStatusChangedResponse,
  UpdateProfileRequest,
} from './types'

// Jedna funkcja na każdy endpoint Brokera (docs/broker-api-contract.md).

const BASE = '/api/v1'

function query(params: Record<string, string | number | undefined | null>): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') search.set(key, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

// Katalog
export function getCars(params: CarSearchParams, signal?: AbortSignal) {
  return api.get<PagedResult<CarListItem>>(`${BASE}/cars${query({ ...params })}`, signal)
}

export function getMakes(signal?: AbortSignal) {
  return api.get<MakeModels[]>(`${BASE}/cars/makes`, signal)
}

export function getCar(provider: string, carId: string, signal?: AbortSignal) {
  return api.get<CarDetails>(`${BASE}/cars/${encodeURIComponent(provider)}/${encodeURIComponent(carId)}`, signal)
}

// Wycena
export function createQuote(request: QuoteRequest, signal?: AbortSignal) {
  return api.post<QuoteResult>(`${BASE}/offers`, request, signal)
}

// Wynajem (Moje wypożyczenia)
export function createRental(request: CreateRentalRequest) {
  return api.post<RentalResponse>(`${BASE}/rentals`, request)
}

export function getMyRentals(signal?: AbortSignal) {
  return api.get<RentalResponse[]>(`${BASE}/rentals`, signal)
}

export function getMyRental(rentalId: string, signal?: AbortSignal) {
  return api.get<RentalResponse>(`${BASE}/rentals/${encodeURIComponent(rentalId)}`, signal)
}

export function requestReturn(rentalId: string) {
  return api.post<RentalResponse>(`${BASE}/rentals/${encodeURIComponent(rentalId)}/return`)
}

// Logowanie
export function getMe(signal?: AbortSignal) {
  return api.get<MeResponse>(`${BASE}/auth/me`, signal)
}

export function logout() {
  return api.post<void>(`${BASE}/auth/logout`)
}

/** Adres do nawigacji przeglądarki (nie fetch). */
export function loginUrl(returnUrl: string): string {
  return `${BASE}/auth/login${query({ returnUrl: safeReturnUrl(returnUrl) })}`
}

/** Tylko Development: logowanie bez hasła jako zwykły użytkownik albo pracownik. */
export function devLoginUrl(name: string, role: string | null, returnUrl: string): string {
  return `${BASE}/auth/dev-login${query({ name, role, returnUrl: safeReturnUrl(returnUrl) })}`
}

function safeReturnUrl(url: string): string {
  return url.startsWith('/') && !url.startsWith('//') ? url : '/'
}

// Profil
export function getProfile(signal?: AbortSignal) {
  return api.get<ProfileResponse>(`${BASE}/profile`, signal)
}

export function updateProfile(request: UpdateProfileRequest) {
  return api.put<ProfileResponse>(`${BASE}/profile`, request)
}

// Panel pracownika
export function getEmployeeRentals(status: RentalStatus | undefined, signal?: AbortSignal) {
  return api.get<EmployeeRentalListItem[]>(`${BASE}/employee/rentals${query({ status })}`, signal)
}

export function getEmployeeRental(rentalId: string, signal?: AbortSignal) {
  return api.get<EmployeeRentalDetails>(`${BASE}/employee/rentals/${encodeURIComponent(rentalId)}`, signal)
}

export function confirmReturn(rentalId: string, note: string, file: File) {
  const form = new FormData()
  form.append('note', note)
  form.append('file', file)
  return api.post<RentalStatusChangedResponse>(
    `${BASE}/employee/rentals/${encodeURIComponent(rentalId)}/confirm-return`,
    form,
  )
}

/** Załącznik pobieramy zwykłym linkiem (GET, ciasteczko idzie samo). */
export function attachmentUrl(rentalId: string): string {
  return `${BASE}/employee/rentals/${encodeURIComponent(rentalId)}/attachment`
}
