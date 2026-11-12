import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Build goes straight into the API's wwwroot; in dev, API calls are proxied to the backend.
export default defineConfig({
  plugins: [react()],
  build: { outDir: '../src/Api/wwwroot', emptyOutDir: true },
  server: { proxy: { '/runs': 'http://localhost:8080', '/alerts': 'http://localhost:8080', '/merchants': 'http://localhost:8080', '/health': 'http://localhost:8080' } },
})
