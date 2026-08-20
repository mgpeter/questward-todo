import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

// Aspire's JavaScript hosting allocates this resource's port and passes it both as a
// `--port` argument and as PORT. The argument is what actually wins, so this line is a
// belt-and-braces fallback rather than the mechanism; it costs nothing and means a change of
// heart upstream cannot leave strictPort below fighting an allocated port.
//
// This is not the VITE_* pattern web/src/lib/config.ts rejects. That rejection is about
// import.meta.env, which Vite inlines into the browser bundle at build time; vite.config.ts
// runs in Node at dev-server startup and process.env never reaches the browser.
const port = Number(process.env.PORT) || 5173

// The production bundle is written into the gateway's wwwroot, so `npm run build` followed by
// running the gateway serves the whole app from one origin - the same thing the Docker image
// does, just without the container.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    outDir: '../src/TodoApp.Gateway/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port,
    // Kept, and worth more now than before: with a port that moves, this turns "Aspire said
    // 54312 and something else already had it" into an immediate failure rather than Vite
    // quietly taking 54313 while the gateway keeps proxying to 54312.
    strictPort: true,
    proxy: {
      // Only used when the browser is pointed straight at Vite on 5173. The supported
      // development URL is the gateway on 5080, which proxies /api itself.
      '/api': {
        target: 'http://localhost:5081',
        changeOrigin: true,
      },
    },
  },
})
