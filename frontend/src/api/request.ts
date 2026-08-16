import createClient, { type Middleware } from 'openapi-fetch';
import type { components, paths } from './types';
import { message } from 'antd';

const client = createClient<paths>({
  baseUrl: import.meta.env.VITE_API_BASE_URL || '',
  headers: {
    'Content-Type': 'application/json',
  },
});

// 请求拦截器：加token
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = localStorage.getItem('token');
    if (token) {
      request.headers.set('Authorization', `Bearer ${token}`);
    }
    return request;
  },
  async onResponse({ response }) {
    if (!response.ok) {
      try {
        const data = await response.clone().json();
        const msg = data?.message || data?.title || `请求失败 (${response.status})`;
        message.error(msg);
      } catch {
        message.error(`请求失败 (${response.status})`);
      }
    }
    return response;
  },
};

client.use(authMiddleware);

export { client };
export type { components, paths };
