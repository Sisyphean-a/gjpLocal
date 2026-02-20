import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'
import fs from 'node:fs'
import path from 'node:path'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const certDir = path.resolve(__dirname, '../backend/src/SwcsScanner.Api/certs')
  const keyPath = path.join(certDir, 'frontend-localhost-key.pem')
  const certPath = path.join(certDir, 'frontend-localhost-cert.pem')

  const useHttps = fs.existsSync(keyPath) && fs.existsSync(certPath)
  const proxyTarget = env.VITE_PROXY_TARGET ?? 'https://localhost:5001'

  return {
    plugins: [vue()],
    server: {
      host: '0.0.0.0',
      port: 5173,
      https: useHttps
        ? {
            key: fs.readFileSync(keyPath),
            cert: fs.readFileSync(certPath),
          }
        : undefined,
      proxy: {
        '/api': {
          target: proxyTarget ?? 'https://localhost:5001',
          changeOrigin: true,
          secure: false,
        },
      },
    },
  }
})
