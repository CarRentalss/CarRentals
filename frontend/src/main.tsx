import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { App } from './App'
import { AuthProvider } from './auth/AuthContext'
// Fonty z paczek npm, serwowane z /assets (CSP: font-src 'self').
import '@fontsource-variable/bodoni-moda/opsz.css'
import '@fontsource-variable/public-sans/index.css'
import './index.css'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <AuthProvider>
        <App />
      </AuthProvider>
    </BrowserRouter>
  </StrictMode>,
)
