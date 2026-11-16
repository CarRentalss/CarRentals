/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Lokalnie front chodzi na 5173, a wszystkie wywołania /api i /health idą do Brokera na 5100.
// Dzięki temu ciasteczko sesji jest z tej samej domeny co front (tak samo jak w chmurze).
const brokerUrl = process.env.BROKER_URL ?? 'http://localhost:5100'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: brokerUrl, changeOrigin: false },
      '/health': { target: brokerUrl, changeOrigin: false },
    },
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
  },
})
