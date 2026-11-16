/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Numer builda z pipeline'u (VITE_APP_VERSION=$(Build.BuildNumber)), lokalnie pusty. */
  readonly VITE_APP_VERSION?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
