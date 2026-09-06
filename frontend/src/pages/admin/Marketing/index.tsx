import { useState } from 'react';
import {
  Card,
  Form,
  Input,
  InputNumber,
  Select,
  DatePicker,
  Button,
  Radio,
  message,
  Space,
} from 'antd';
import dayjs from 'dayjs';
import {
  createMarketingContent,
  updateMarketingContent,
  type MarketingContentType,
  type MarketingContentStatus,
} from '../../../api/admin';

const CONTENT_TYPE_OPTIONS: { label: string; value: MarketingContentType }[] = [
  { label: '公告', value: 'NOTICE' },
  { label: '广告', value: 'AD' },
  { label: '推广', value: 'PROMOTION' },
];

const STATUS_OPTIONS: { label: string; value: MarketingContentStatus }[] = [
  { label: '启用', value: 'ENABLED' },
  { label: '禁用', value: 'DISABLED' },
];

const Marketing = () => {
  const [mode, setMode] = useState<'create' | 'edit'>('create');
  const [loading, setLoading] = useState(false);
  const [form] = Form.useForm();

  const handleSubmit = async (values: Record<string, unknown>) => {
    setLoading(true);
    try {
      const publishTime = values.publishTime
        ? (values.publishTime as dayjs.Dayjs).toISOString()
        : null;

      if (mode === 'create') {
        const res = await createMarketingContent({
          showId: Number(values.showId),
          contentType: values.contentType as MarketingContentType,
          title: values.title as string,
          contentText: (values.contentText as string) || null,
          imageUrl: (values.imageUrl as string) || null,
          sortOrder: values.sortOrder != null ? Number(values.sortOrder) : 0,
          status: values.status as MarketingContentStatus | undefined,
          publishTime,
        });
        if (res.error) {
          message.error((res.error as { message?: string })?.message || '创建失败');
          return;
        }
        message.success('创建成功');
        form.resetFields();
      } else {
        const contentId = Number(values.contentId);
        const res = await updateMarketingContent(contentId, {
          contentType: values.contentType as MarketingContentType,
          title: values.title as string,
          contentText: (values.contentText as string) || null,
          imageUrl: (values.imageUrl as string) || null,
          sortOrder: Number(values.sortOrder),
          status: values.status as MarketingContentStatus,
          publishTime,
        });
        if (res.error) {
          message.error((res.error as { message?: string })?.message || '更新失败');
          return;
        }
        message.success('更新成功');
      }
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) return;
      message.error((err as { message?: string })?.message || '操作失败');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div>
      <Card size="small" title="营销内容管理">
        <Radio.Group
          value={mode}
          onChange={e => {
            setMode(e.target.value);
            form.resetFields();
          }}
          style={{ marginBottom: 16 }}
        >
          <Radio.Button value="create">创建</Radio.Button>
          <Radio.Button value="edit">编辑</Radio.Button>
        </Radio.Group>

        <Form
          form={form}
          layout="vertical"
          onFinish={handleSubmit}
          initialValues={{ sortOrder: 0, status: 'ENABLED' }}
          style={{ maxWidth: 600 }}
        >
          {mode === 'edit' && (
            <Form.Item
              label="内容 ID"
              name="contentId"
              rules={[{ required: true, message: '请输入内容 ID' }]}
            >
              <InputNumber style={{ width: '100%' }} placeholder="请输入要编辑的 contentId" />
            </Form.Item>
          )}

          {mode === 'create' && (
            <Form.Item
              label="演出 ID"
              name="showId"
              rules={[{ required: true, message: '请输入演出 ID' }]}
            >
              <InputNumber style={{ width: '100%' }} placeholder="关联的演出 showId" />
            </Form.Item>
          )}

          <Form.Item
            label="内容类型"
            name="contentType"
            rules={[{ required: true, message: '请选择内容类型' }]}
          >
            <Select options={CONTENT_TYPE_OPTIONS} placeholder="选择内容类型" />
          </Form.Item>

          <Form.Item
            label="标题"
            name="title"
            rules={[{ required: true, message: '请输入标题' }]}
          >
            <Input placeholder="营销内容标题" />
          </Form.Item>

          <Form.Item label="正文内容" name="contentText">
            <Input.TextArea rows={3} placeholder="营销内容正文（可选）" />
          </Form.Item>

          <Form.Item label="图片 URL" name="imageUrl">
            <Input placeholder="图片地址（可选）" />
          </Form.Item>

          <Form.Item
            label="排序"
            name="sortOrder"
            rules={[{ required: mode === 'edit', message: '请输入排序值' }]}
          >
            <InputNumber style={{ width: '100%' }} placeholder="数字越小越靠前" />
          </Form.Item>

          <Form.Item
            label="状态"
            name="status"
            rules={[{ required: mode === 'edit', message: '请选择状态' }]}
          >
            <Select options={STATUS_OPTIONS} placeholder="选择状态" />
          </Form.Item>

          <Form.Item label="发布时间" name="publishTime">
            <DatePicker showTime style={{ width: '100%' }} placeholder="选择发布时间（可选）" />
          </Form.Item>

          <Form.Item>
            <Space>
              <Button type="primary" htmlType="submit" loading={loading}>
                {mode === 'create' ? '创建' : '更新'}
              </Button>
              <Button onClick={() => form.resetFields()}>重置</Button>
            </Space>
          </Form.Item>
        </Form>
      </Card>
    </div>
  );
};

export default Marketing;
