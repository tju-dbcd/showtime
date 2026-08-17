import axios, { type AxiosInstance, type InternalAxiosRequestConfig, type AxiosError } from 'axios';
import { message } from 'antd';

// API 基础地址
const BASE_URL = import.meta.env.VITE_API_BASE_URL || '';

console.log('BASE_URL:', BASE_URL);
console.log('import.meta.env.VITE_API_BASE_URL:', import.meta.env.VITE_API_BASE_URL);

// 创建 axios 实例
const apiClient: AxiosInstance = axios.create({
  baseURL: BASE_URL,
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json',
  },
});

// 请求拦截器：添加 token
apiClient.interceptors.request.use(
  (config: InternalAxiosRequestConfig) => {
    const token = localStorage.getItem('accessToken');
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// 响应拦截器：统一处理错误
apiClient.interceptors.response.use(
  (response) => {
    // 如果响应是 ApiResponse 格式，检查 success 字段
    if (response.data && typeof response.data === 'object' && 'success' in response.data) {
      if (!response.data.success) {
        // 不在这里弹窗，让调用方自己处理
        return Promise.reject(new Error(response.data.message || '请求失败'));
      }
      // 返回整个 response，让调用方取 response.data
      return response;
    }
    return response;
  },
  (error: AxiosError) => {
    if (error.response) {
      const status = error.response.status;
      const data = error.response.data as any;
      const config = error.config;

      // 如果是登录或注册接口，不自动弹窗，让前端自己处理
      const isAuthApi =
        config?.url?.includes('/api/auth/login') ||
        config?.url?.includes('/api/auth/register');

      switch (status) {
        case 401:
          if (isAuthApi) {
            return Promise.reject(error);
          }
          message.error('登录已过期，请重新登录');
          localStorage.removeItem('accessToken');
          window.location.href = '/login';
          break;
        case 409: // Conflict（注册重复）
          if (isAuthApi) {
            return Promise.reject(error);
          }
          message.error(data?.message || '数据冲突');
          break;
        case 400:
          if (isAuthApi) {
            return Promise.reject(error);
          }
          message.error(data?.message || '请求参数错误');
          break;
        case 403:
          message.error('没有权限访问该资源');
          break;
        case 404:
          message.error('请求的资源不存在');
          break;
        case 500:
          message.error('服务器内部错误，请稍后重试');
          break;
        default:
          message.error(data?.message || `请求失败 (${status})`);
      }
    } else if (error.request) {
      message.error('网络连接失败，请检查网络');
    } else {
      message.error(error.message || '请求失败');
    }
    return Promise.reject(error);
  }
);

export default apiClient;
