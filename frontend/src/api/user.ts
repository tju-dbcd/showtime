import apiClient from './client';
import type { ApiResponse } from '@/types/api';

// ========== 当前用户资料 ==========

/**
 * 更新当前用户头像（OSS 上传拿到公开 URL 后持久化到后端）。
 * 成功返回更新后的用户信息；失败抛 Error（消息来自后端 ApiResponse.message）。
 */
export const updateAvatar = async (avatarUrl: string): Promise<void> => {
  await apiClient.put<ApiResponse<unknown>>('/api/users/me/avatar', {
    avatarUrl,
  });
};