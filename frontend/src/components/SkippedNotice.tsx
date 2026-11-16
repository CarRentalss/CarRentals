import type { SkippedProvider } from '../api/types'

function reasonText(reason: SkippedProvider['reason']): string {
  switch (reason) {
    case 'Timeout':
      return 'brak odpowiedzi w ciągu 15 s'
    case 'Rejected':
      return 'odmówiła oferty'
    default:
      return 'błąd po stronie wypożyczalni'
  }
}

interface SkippedNoticeProps {
  skipped: SkippedProvider[] | undefined
  title?: string
}

/** Lista wypożyczalni pominiętych przez Brokera (limit 15 s, błąd albo odmowa). */
export function SkippedNotice({ skipped, title = 'Nie wszystkie wypożyczalnie odpowiedziały:' }: SkippedNoticeProps) {
  if (!skipped || skipped.length === 0) return null
  return (
    <div className="alert alert--warn">
      <strong>{title}</strong>
      <ul>
        {skipped.map((s) => (
          <li key={s.provider}>
            {s.providerName}: {s.message || reasonText(s.reason)}
          </li>
        ))}
      </ul>
    </div>
  )
}
