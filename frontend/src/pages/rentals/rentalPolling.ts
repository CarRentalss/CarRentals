import type { RentalResponse } from '../../api/types'

/** Co ile odświeżamy status, gdy wypożyczalnia jeszcze nie potwierdziła wniosku. */
export const POLL_INTERVAL_MS = 3000

export function isPending(rental: RentalResponse): boolean {
  return rental.status === 'Pending'
}

export function anyPending(rentals: RentalResponse[]): boolean {
  return rentals.some(isPending)
}
