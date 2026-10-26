import { fileURLToPath, URL } from 'node:url'

import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    vue(),
    vueDevTools(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    // The backend CORS policy is bound to exactly http://localhost:5173, so the
    // port is fixed and the dev server fails fast instead of drifting to 5174+.
    port: 5173,
    strictPort: true,
    proxy: {
      // Dev requests go through the Vite proxy; the backend origin is never
      // hard-coded in application code (production uses VITE_API_URL).
      '/api': 'http://localhost:5269',
    },
  },
})
