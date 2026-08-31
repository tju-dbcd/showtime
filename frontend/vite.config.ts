import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

// 开发代理目标：优先取环境变量 VITE_DEV_PROXY_TARGET（本地/CI 可指向本地后端），
// 默认保持现状指向生产后端；E2E 测试用 page.route 全量 mock /api，不经过该代理。
const devProxyTarget = process.env.VITE_DEV_PROXY_TARGET || 'http://120.27.157.163:5146';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    proxy: {
      '/api': {
        target: devProxyTarget,
        changeOrigin: true,
        secure: false,
      },
    },
  },
});
