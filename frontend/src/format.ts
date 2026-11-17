import type { ProfileField, RentalStatus } from './api/types'

// Formatowanie dat i kwot po polsku.

/** Zamienia "2026-11-20" na "20.11.2026" (bez strefy czasowej, to data bez godziny). */
export function formatDate(value: string | null | undefined): string {
  if (!value) return '-'
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value)
  if (!match) return value
  return `${match[3]}.${match[2]}.${match[1]}`
}

/**
 * Znaczniki czasu z Brokera są w UTC. Gdyby przyszły bez strefy (DateTime z Kind=Unspecified),
 * traktujemy je jako UTC, a nie czas lokalny.
 */
export function parseUtc(value: string): Date {
  const hasZone = /([zZ]|[+-]\d{2}:?\d{2})$/.test(value)
  return new Date(hasZone || !value.includes('T') ? value : `${value}Z`)
}

/** Znacznik czasu UTC jako "HH:MM" w czasie lokalnym. */
export function formatTime(value: string): string {
  const date = parseUtc(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleTimeString('pl-PL', { hour: '2-digit', minute: '2-digit' })
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '-'
  const date = parseUtc(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString('pl-PL', { dateStyle: 'short', timeStyle: 'short' })
}

export function formatMoney(amount: number, currency = 'PLN'): string {
  try {
    return new Intl.NumberFormat('pl-PL', { style: 'currency', currency }).format(amount)
  } catch {
    return `${amount.toFixed(2)} ${currency}`
  }
}

/** Dzisiejsza data jako "YYYY-MM-DD" w czasie lokalnym. */
export function todayIso(offsetDays = 0): string {
  const date = new Date()
  date.setDate(date.getDate() + offsetDays)
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

/** "1 dzień", "3 dni". */
export function daysLabel(days: number): string {
  return days === 1 ? '1 dzień' : `${days} dni`
}

export const STATUS_LABELS: Record<RentalStatus, string> = {
  Pending: 'Oczekuje na potwierdzenie',
  Confirmed: 'Aktywne',
  Rejected: 'Odrzucone',
  ReturnRequested: 'Do odbioru',
  Completed: 'Zakończone',
}

/** Statusy z punktu widzenia klienta (Moje wypożyczenia). */
export const CUSTOMER_STATUS_LABELS: Record<RentalStatus, string> = {
  Pending: 'Czeka na potwierdzenie',
  Confirmed: 'Potwierdzone',
  Rejected: 'Odrzucone',
  ReturnRequested: 'Zwrot zgłoszony',
  Completed: 'Zakończone',
}

export const PROFILE_FIELD_LABELS: Record<ProfileField, string> = {
  firstName: 'imię',
  lastName: 'nazwisko',
  email: 'email',
  dateOfBirth: 'data urodzenia',
  licenseObtainedOn: 'data uzyskania prawa jazdy',
  location: 'lokalizacja',
}
