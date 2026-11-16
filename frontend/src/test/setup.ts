import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach, vi } from 'vitest'

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.unstubAllGlobals()
  try {
    window.localStorage.clear()
  } catch {
    // jsdom bez localStorage
  }
})

// jsdom nie ma scrollTo, a SearchPage przewija do góry przy zmianie strony.
Object.defineProperty(window, 'scrollTo', { value: () => {}, writable: true })
