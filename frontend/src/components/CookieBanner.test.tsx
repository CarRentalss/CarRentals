import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { CookieBanner } from './CookieBanner'

describe('CookieBanner', () => {
  it('chowa się po kliknięciu i pamięta to w localStorage', async () => {
    const { unmount } = render(
      <MemoryRouter>
        <CookieBanner />
      </MemoryRouter>,
    )
    expect(screen.getByRole('link', { name: 'polityce prywatności' })).toHaveAttribute('href', '/privacy')

    await userEvent.click(screen.getByRole('button', { name: 'Rozumiem' }))
    expect(screen.queryByRole('button', { name: 'Rozumiem' })).not.toBeInTheDocument()
    unmount()

    render(
      <MemoryRouter>
        <CookieBanner />
      </MemoryRouter>,
    )
    expect(screen.queryByRole('button', { name: 'Rozumiem' })).not.toBeInTheDocument()
  })
})
