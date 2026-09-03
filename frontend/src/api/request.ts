import createClient, { type Middleware } from 'openapi-fetch';
import type { components, paths } from './types';
import { message } from 'antd';

// 注意：不要设置全局默认 Content-Type: application/json。
// openapi-fetch 对 JSON body 会自动加 Content-Type，而 FormData 上传（multipart/form-data）
// 需要浏览器自动生成 boundary；若此处写死 application/json 会覆盖 FormData 的 Content-Type，
// 导致后端 [Consumes("multipart/form-data")] 返回 415。
const client = createClient<paths>({
  baseUrl: import.meta.env.VITE_API_BASE_URL || '',
});

// 请求拦截器：加 token
const authMiddleware: Middleware = {
  async onRequest({ request }) {
    const token = localStorage.getItem('accessToken');
    if (token) {
      request.headers.set('Authorization', `Bearer ${token}`);
    }
    return request;
  },
  async onResponse({ response }) {
    // 401：会话过期，清理登录态并跳转登录页（对齐旧 client.ts 行为）
    if (response.status === 401) {
      // 登录/注册接口的 401（如密码错误）由页面自行提示，不跳转
      const isAuthApi = /\/api\/auth\/(login|register)(\/|$)/.test(response.url);
      if (!isAuthApi) {
        localStorage.removeItem('accessToken');
        message.error('登录已过期，请重新登录');
        if (!window.location.pathname.startsWith('/login')) {
          window.location.href = '/login';
        }
      }
    } else if (!response.ok) {
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