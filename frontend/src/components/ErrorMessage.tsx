interface ErrorMessageProps {
  message: string | null | undefined
  onRetry?: () => void
}

export function ErrorMessage({ message, onRetry }: ErrorMessageProps) {
  if (!message) return null
  return (
    <div className="alert alert--error" role="alert">
      <span>{message}</span>
      {onRetry && (
        <button type="button" className="btn btn--small" onClick={onRetry}>
          Spróbuj ponownie
        </button>
      )}
    </div>
  )
}
