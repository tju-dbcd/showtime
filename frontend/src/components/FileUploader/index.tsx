import { useEffect, useState } from 'react';
import { Upload, message } from 'antd';
import type { UploadFile, UploadProps } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { uploadFile, type FileUploadFolder } from '@/api/upload';

const DEFAULT_ALLOWED_EXTENSIONS = ['.jpg', '.jpeg', '.png', '.webp', '.gif'];
const DEFAULT_MAX_SIZE_BYTES = 5 * 1024 * 1024;

interface FileUploaderProps {
  /** 当前已上传的图片 URL（受控，可直接配合 Form.Item 使用） */
  value?: string;
  /** URL 变化回调（删除时传 undefined） */
  onChange?: (url?: string) => void;
  /** 业务目录：show / marketing / avatar / tmp */
  folder: FileUploadFolder;
  /** 扩展名白名单（小写含点），默认 jpg/jpeg/png/webp/gif */
  allowedExtensions?: string[];
  /** 单文件大小上限（字节），默认 5MB，与后端 OssOptions.MaxFileSizeBytes 一致 */
  maxSizeBytes?: number;
  maxCount?: number;
  listType?: 'text' | 'picture' | 'picture-card' | 'picture-circle';
  /** false 时不展示已上传文件列表（配合 children 自定义触发器，如头像） */
  showUploadList?: boolean;
  /** 自定义上传触发器（头像等场景）；缺省渲染"点击上传"卡片 */
  children?: React.ReactNode;
}

/**
 * OSS 图片上传组件：选择/拖拽上传，内置格式与大小校验，
 * 经后端 POST /api/files/upload 代理上传，返回公开 URL 并回显。
 */
const FileUploader = ({
  value,
  onChange,
  folder,
  allowedExtensions = DEFAULT_ALLOWED_EXTENSIONS,
  maxSizeBytes = DEFAULT_MAX_SIZE_BYTES,
  maxCount = 1,
  listType = 'picture-card',
  showUploadList = true,
  children,
}: FileUploaderProps) => {
  const [fileList, setFileList] = useState<UploadFile[]>([]);

  // 受控同步：外部 value（URL）变化 → 同步文件列表用于回显
  useEffect(() => {
    setFileList(
      value
        ? [
            {
              uid: '-1',
              name: value.slice(value.lastIndexOf('/') + 1) || 'image',
              status: 'done',
              url: value,
            },
          ]
        : [],
    );
  }, [value]);

  const beforeUpload: UploadProps['beforeUpload'] = (file) => {
    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
    if (!allowedExtensions.includes(extension)) {
      message.error(`仅支持 ${allowedExtensions.join(' / ')} 格式`);
      return Upload.LIST_IGNORE;
    }
    if (file.size > maxSizeBytes) {
      message.error(`图片大小不能超过 ${Math.floor(maxSizeBytes / 1024 / 1024)}MB`);
      return Upload.LIST_IGNORE;
    }
    return true;
  };

  const customRequest: UploadProps['customRequest'] = async (options) => {
    const { file, onSuccess, onError } = options;
    try {
      const result = await uploadFile(file as File, folder);
      onChange?.(result.url);
      onSuccess?.(result as never);
    } catch (error) {
      const err = error instanceof Error ? error : new Error('上传失败，请重试');
      message.error(err.message);
      // 失败时不清空已存在的值（受控 value 保持不变）：
      // 发布页表单中已填的海报 URL 不应因一次失败上传而被抹掉。
      onError?.(err as never);
    }
  };

  const handleRemove = () => {
    onChange?.(undefined);
    return true;
  };

  const hideTrigger = !children && fileList.length >= maxCount;

  return (
    <Upload
      listType={listType}
      maxCount={maxCount}
      accept={allowedExtensions.join(',')}
      fileList={fileList}
      beforeUpload={beforeUpload}
      customRequest={customRequest}
      onRemove={handleRemove}
      showUploadList={showUploadList}
    >
      {children ?? (!hideTrigger &&
        <div style={{ padding: 12 }}>
          <PlusOutlined style={{ fontSize: 20, color: '#999' }} />
          <div style={{ marginTop: 8, color: '#999' }}>点击/拖拽上传</div>
        </div>
      )}
    </Upload>
  );
};

export default FileUploader;