import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  const { API_TARGET = 'http://127.0.0.1:5000' } = loadEnv(mode, process.cwd(), '');
  const proxy = { '/api': { target: API_TARGET, changeOrigin: true } };

  return {
    plugins: [react()],
    server: { proxy },
    preview: { proxy },
  };
});
