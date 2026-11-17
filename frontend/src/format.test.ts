import { describe, expect, it } from 'vitest'
import { daysLabel, formatDate, parseUtc } from './format'

describe('format', () => {
  it('formatuje datę bez godziny po polsku', () => {
    expect(formatDate('2026-11-20')).toBe('20.11.2026')
    expect(formatDate(null)).toBe('-')
  })

  it('traktuje znacznik czasu bez strefy jako UTC', () => {
    expect(parseUtc('2026-11-16T18:10:00').toISOString()).toBe('2026-11-16T18:10:00.000Z')
    expect(parseUtc('2026-11-16T18:10:00Z').toISOString()).toBe('2026-11-16T18:10:00.000Z')
    expect(parseUtc('2026-11-16T19:10:00+01:00').toISOString()).toBe('2026-11-16T18:10:00.000Z')
  })
})

describe('daysLabel', () => {
  it('odmienia dzień', () => {
    expect(daysLabel(1)).toBe('1 dzień')
    expect(daysLabel(3)).toBe('3 dni')
  })
})
