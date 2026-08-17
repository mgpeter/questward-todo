import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// The production bundle is written straight into the API's wwwroot, so `npm run build`
// followed by `dotnet run` serves the whole app from one origin - the same thing the
// Docker image does, just without the container.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    outDir: '../src/TodoApp.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },
})
