import { useState } from 'react'

interface CarImageProps {
  src?: string | null
  alt: string
  width: number
  height: number
  className?: string
}

/**
 * Zdjęcie auta z Azure Blob Storage. Stałe wymiary (bez skakania układu) i lazy loading.
 * Gdy wypożyczalnia nie podaje zdjęcia albo plik się nie wczyta, pokazujemy zaślepkę.
 */
export function CarImage({ src, alt, width, height, className }: CarImageProps) {
  const [failed, setFailed] = useState(false)
  const classes = ['car-image', className].filter(Boolean).join(' ')

  if (!src || failed) {
    return (
      <div className={`${classes} car-image--empty`} style={{ aspectRatio: `${width} / ${height}` }} role="img" aria-label={`${alt} (brak zdjęcia)`}>
        <span aria-hidden="true">Brak zdjęcia</span>
      </div>
    )
  }

  return (
    <img
      className={classes}
      src={src}
      alt={alt}
      width={width}
      height={height}
      loading="lazy"
      decoding="async"
      onError={() => setFailed(true)}
    />
  )
}
