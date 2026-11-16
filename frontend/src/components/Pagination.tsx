interface PaginationProps {
  page: number
  totalPages: number
  onPageChange: (page: number) => void
}

// Pokazujemy maks. 5 numerów stron wokół bieżącej, plus Poprzednia/Następna.
function visiblePages(page: number, totalPages: number): number[] {
  const size = Math.min(5, totalPages)
  let start = Math.max(1, page - 2)
  start = Math.min(start, totalPages - size + 1)
  return Array.from({ length: size }, (_, i) => start + i)
}

export function Pagination({ page, totalPages, onPageChange }: PaginationProps) {
  if (totalPages <= 1) return null

  return (
    <nav className="pagination" aria-label="Stronicowanie">
      <button type="button" className="btn btn--small" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
        Poprzednia
      </button>
      <ul>
        {visiblePages(page, totalPages).map((p) => (
          <li key={p}>
            <button
              type="button"
              className={p === page ? 'btn btn--small btn--active' : 'btn btn--small'}
              aria-current={p === page ? 'page' : undefined}
              aria-label={`Strona ${p}`}
              onClick={() => onPageChange(p)}
            >
              {p}
            </button>
          </li>
        ))}
      </ul>
      <button
        type="button"
        className="btn btn--small"
        disabled={page >= totalPages}
        onClick={() => onPageChange(page + 1)}
      >
        Następna
      </button>
      <span className="pagination__info">
        Strona {page} z {totalPages}
      </span>
    </nav>
  )
}
