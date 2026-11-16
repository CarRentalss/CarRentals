// Typy zgodne z docs/broker-api-contract.md (JSON camelCase, enumy jako stringi).
// Daty bez godziny (DateOnly) to string "2026-11-20", znaczniki czasu to ISO 8601 w UTC.

export type ProviderCode = 'our' | 'lecturer' | 'other-group' | (string & {})

export type SkipReason = 'Timeout' | 'Error' | 'Rejected'

export interface SkippedProvider {
  provider: ProviderCode
  providerName: string
  reason: SkipReason
  message: string | null
}

export interface CarListItem {
  provider: ProviderCode
  providerName: string
  carId: string
  make: string
  model: string
  year: number
  location: string
  available: boolean
}

export type CarDetails = CarListItem

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  skippedProviders: SkippedProvider[]
}

export interface MakeModels {
  make: string
  models: string[]
}

export interface CarSearchParams {
  make?: string
  model?: string
  year?: number
  location?: string
  page?: number
  pageSize?: number
}

export interface QuoteRequest {
  provider: ProviderCode
  carId: string
  startDate: string
  endDate: string
  age?: number | null
  licenseYears?: number | null
  location?: string | null
}

export interface ProviderOffer {
  quoteId: string
  provider: ProviderCode
  providerName: string
  offerId: string
  carId: string
  carLabel: string
  dailyPrice: number
  dailyInsurancePrice: number
  totalPrice: number
  currency: string
  startDate: string
  endDate: string
  days: number
  expiresAt: string
}

export interface QuoteResult {
  offers: ProviderOffer[]
  skippedProviders: SkippedProvider[]
}

export type AuthMode = 'Entra' | 'DevLogin' | 'None'

export interface MeResponse {
  isAuthenticated: boolean
  name: string | null
  email: string | null
  roles: string[]
  isEmployee: boolean
  profileComplete: boolean
  authMode: AuthMode
}

export type ProfileField =
  | 'firstName'
  | 'lastName'
  | 'email'
  | 'dateOfBirth'
  | 'licenseObtainedOn'
  | 'location'

export interface ProfileResponse {
  firstName: string | null
  lastName: string | null
  email: string | null
  dateOfBirth: string | null
  licenseObtainedOn: string | null
  location: string | null
  age: number | null
  licenseYears: number | null
  isComplete: boolean
  missingFields: ProfileField[]
  updatedAt: string | null
}

export interface UpdateProfileRequest {
  firstName: string
  lastName: string
  email: string
  dateOfBirth: string | null
  licenseObtainedOn: string | null
  location: string | null
}

export type RentalStatus = 'Pending' | 'Confirmed' | 'Rejected' | 'ReturnRequested' | 'Completed'

export interface EmployeeRentalListItem {
  rentalId: string
  carId: number
  car: string
  customerName: string
  email: string
  startDate: string
  endDate: string
  status: RentalStatus
  totalPrice: number
}

export interface EmployeeRentalDetails {
  rentalId: string
  carId: number
  car: string
  location: string
  firstName: string
  lastName: string
  email: string
  startDate: string
  endDate: string
  status: RentalStatus
  reason: string | null
  totalPrice: number
  brokerName: string
  createdAt: string
  returnRequestedAt: string | null
  completedAt: string | null
  completedBy: string | null
  returnNote: string | null
  hasAttachment: boolean
}

export interface RentalStatusChangedResponse {
  rentalId: string
  status: RentalStatus
}

/** ProblemDetails (RFC 9457) i ValidationProblemDetails z ASP.NET Core. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  traceId?: string
  errors?: Record<string, string[]>
}
