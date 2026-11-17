import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Pagination } from './Pagination'

describe('Pagination', () => {
  it('nic nie pokazuje przy jednej stronie', () => {
    const { container } = render(<Pagination page={1} totalPages={1} onPageChange={() => {}} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('pokazuje strony, blokuje "Poprzednia" na pierwszej i zmienia stronę', async () => {
    const onPageChange = vi.fn()
    render(<Pagination page={1} totalPages={3} onPageChange={onPageChange} />)

    expect(screen.getByRole('button', { name: 'Poprzednia' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Strona 1' })).toHaveAttribute('aria-current', 'page')
    expect(screen.getByText('Strona 1 z 3')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Następna' }))
    expect(onPageChange).toHaveBeenLastCalledWith(2)

    await userEvent.click(screen.getByRole('button', { name: 'Strona 3' }))
    expect(onPageChange).toHaveBeenLastCalledWith(3)
  })

  it('blokuje "Następna" na ostatniej stronie i pokazuje maks. 5 numerów', () => {
    render(<Pagination page={10} totalPages={10} onPageChange={() => {}} />)

    expect(screen.getByRole('button', { name: 'Następna' })).toBeDisabled()
    const numbers = screen.getAllByRole('button', { name: /^Strona \d+$/ })
    expect(numbers.map((b) => b.textContent)).toEqual(['6', '7', '8', '9', '10'])
  })
})
