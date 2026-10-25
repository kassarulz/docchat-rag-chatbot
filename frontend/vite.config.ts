import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The ASP.NET Core backend runs on :5080; proxying /api keeps the browser on one origin (no CORS).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
})
