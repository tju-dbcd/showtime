import { client } from './request';
//import type { paths } from './types';

/** folder 取值白名单，与后端 FileStorageFolders 保持一致 */
export type FileUploadFolder = 'show' | 'marketing' | 'avatar' | 'tmp';

/** 上传结果：公开 URL（业务表直接存该值）与对象键（供删除/清理使用） */
export interface FileUploadResult {
  url: string;
  objectKey: string;
}

/**
 * 上传文件到 OSS，返回公开 URL 与对象键。
 * 成功：返回 { url, objectKey }；失败：抛 Error，由调用方提示。
 */
export const uploadFile = async (
  file: File,
  folder: FileUploadFolder = 'tmp',
): Promise<FileUploadResult> => {
  const form = new FormData();
  form.append('file', file);
  form.append('folder', folder);

  const { data, error } = await client.POST('/api/files/upload', {
    body: form as any,
  });

  if (error) {
    throw new Error(error.message || '上传失败，请重试');
  }

  const result = data?.data;
  if (!result) {
    throw new Error(data?.message || '上传失败，请重试');
  }
  return result;
};
