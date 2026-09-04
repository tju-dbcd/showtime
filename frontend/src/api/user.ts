import { client } from './request';

/**
 * 更新当前用户头像（OSS 上传拿到公开 URL 后持久化到后端）。
 * 成功返回更新后的用户信息；失败抛 Error（消息来自后端 ApiResponse.message）。
 */
export const updateAvatar = async (avatarUrl: string): Promise<void> => {
  const { data, error } = await client.PUT('/api/users/me/avatar', {
    body: { avatarUrl },
  });

  if (error) {
    throw new Error(error.message || '更新头像失败');
  }

  if (!data?.success) {
    throw new Error(data?.message || '更新头像失败');
  }
};
