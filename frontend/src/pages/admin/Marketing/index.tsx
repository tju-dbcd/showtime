import { useEffect, useState } from 'react'
import {
  Table,
  Button,
  Space,
  Modal,
  Form,
  Input,
  Select,
  InputNumber,
  DatePicker,
  message,
  Popconfirm,
  Tag,
} from 'antd'
import { PlusOutlined } from '@ant-design/icons'
import dayjs from 'dayjs'
import FileUploader from '../../../components/FileUploader'
import {
  getMarketingContentList,
  createMarketingContent,
  updateMarketingContent,
  deleteMarketingContent,
  getShowList,
  type MarketingContentDto,
  type MarketingContentType,
  type MarketingContentStatus,
  type ShowDto,
} from '../../../api/admin'

const { TextArea } = Input

const contentTypeMap: Record<MarketingContentType, string> = {
  NOTICE: '公告',
  AD: '广告',
  PROMOTION: '推广',
}

const statusMap: Record<MarketingContentStatus, { text: string; color: string }> = {
  ENABLED: { text: '启用', color: 'success' },
  DISABLED: { text: '禁用', color: 'default' },
}

const Marketing = () => {
  const [data, setData] = useState<MarketingContentDto[]>([])
  const [loading, setLoading] = useState(false)
  const [modalVisible, setModalVisible] = useState(false)
  const [editingItem, setEditingItem] = useState<MarketingContentDto | null>(null)
  const [form] = Form.useForm()
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10, total: 0 })
  const [searchKeyword, setSearchKeyword] = useState('')
  const [searchContentType, setSearchContentType] = useState<MarketingContentType | undefined>()
  const [searchStatus, setSearchStatus] = useState<MarketingContentStatus | undefined>()
  const [searchShowId, setSearchShowId] = useState<number | undefined>()
  const [shows, setShows] = useState<ShowDto[]>([])

  const loadData = async (page = 1, pageSize = 10) => {
    setLoading(true)
    try {
      const res = await getMarketingContentList({
        PageIndex: page,
        PageSize: pageSize,
        Keyword: searchKeyword || undefined,
        ContentType: searchContentType,
        Status: searchStatus,
        ShowId: searchShowId,
      })
      if (res.data?.data) {
        const result = res.data.data
        setData(result.items || [])
        setPagination({
          current: page,
          pageSize,
          total: Number(result.totalCount) || 0,
        })
      }
    } catch {
      message.error('加载营销内容列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadData(1)
  }, [])

  // 加载演出列表，用于选择/展示关联演出名称
  useEffect(() => {
    const loadShows = async () => {
      try {
        const res = await getShowList({ PageSize: 100 })
        if (res.data?.data) {
          setShows(res.data.data.items || [])
        }
      } catch {
        message.error('加载演出列表失败')
      }
    }
    void loadShows()
  }, [])

  const findShow = (showId: number | string | undefined) =>
    shows.find(s => Number(s.showId) === Number(showId))

  const handleAdd = () => {
    setEditingItem(null)
    form.resetFields()
    setModalVisible(true)
  }

  const handleEdit = (record: MarketingContentDto) => {
    setEditingItem(record)
    form.setFieldsValue({
      contentType: record.contentType,
      title: record.title,
      contentText: record.contentText,
      imageUrl: record.imageUrl,
      sortOrder: Number(record.sortOrder),
      status: record.status,
      publishTime: record.publishTime ? dayjs(record.publishTime) : null,
    })
    setModalVisible(true)
  }

  const handleDelete = async (contentId: number) => {
    try {
      const res = await deleteMarketingContent(contentId)
      if (res.error) {
        message.error((res.error as { message?: string })?.message || '删除失败')
        return
      }
      message.success('删除成功')
      loadData(pagination.current, pagination.pageSize)
    } catch {
      message.error('删除失败')
    }
  }

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields()
      const publishTime = values.publishTime
        ? (values.publishTime as dayjs.Dayjs).toISOString()
        : null

      if (editingItem) {
        const res = await updateMarketingContent(Number(editingItem.contentId), {
          contentType: values.contentType as MarketingContentType,
          title: values.title as string,
          contentText: (values.contentText as string) || null,
          imageUrl: (values.imageUrl as string) || null,
          sortOrder: Number(values.sortOrder),
          status: values.status as MarketingContentStatus,
          publishTime,
        })
        if (res.error) {
          message.error((res.error as { message?: string })?.message || '更新失败')
          return
        }
        message.success('更新成功')
      } else {
        const res = await createMarketingContent({
          showId: Number(values.showId),
          contentType: values.contentType as MarketingContentType,
          title: values.title as string,
          contentText: (values.contentText as string) || null,
          imageUrl: (values.imageUrl as string) || null,
          sortOrder: values.sortOrder != null ? Number(values.sortOrder) : 0,
          status: values.status as MarketingContentStatus | undefined,
          publishTime,
        })
        if (res.error) {
          message.error((res.error as { message?: string })?.message || '创建失败')
          return
        }
        message.success('创建成功')
      }
      setModalVisible(false)
      loadData(pagination.current, pagination.pageSize)
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) return
      message.error(err instanceof Error ? err.message : '操作失败')
    }
  }

  const columns = [
    {
      title: 'ID',
      dataIndex: 'contentId',
      key: 'contentId',
      width: 80,
    },
    {
      title: '关联演出',
      dataIndex: 'showId',
      key: 'showId',
      width: 220,
      render: (id: number) => {
        const show = findShow(id)
        return (
          <div>
            <div>{show?.showName || `演出 #${id}`}</div>
            {show && <div style={{ color: '#999', fontSize: 12 }}>ID: {id}</div>}
          </div>
        )
      },
    },
    {
      title: '类型',
      dataIndex: 'contentType',
      key: 'contentType',
      width: 90,
      render: (type: MarketingContentType) => contentTypeMap[type] || type,
    },
    {
      title: '标题',
      dataIndex: 'title',
      key: 'title',
    },
    {
      title: '排序',
      dataIndex: 'sortOrder',
      key: 'sortOrder',
      width: 80,
    },
    {
      title: '状态',
      dataIndex: 'status',
      key: 'status',
      width: 90,
      render: (status: MarketingContentStatus) => {
        const s = statusMap[status]
        return s ? <Tag color={s.color}>{s.text}</Tag> : status
      },
    },
    {
      title: '发布时间',
      dataIndex: 'publishTime',
      key: 'publishTime',
      width: 170,
      render: (time: string) => time ? new Date(time).toLocaleString('zh-CN') : '-',
    },
    {
      title: '创建时间',
      dataIndex: 'createTime',
      key: 'createTime',
      width: 170,
      render: (time: string) => time ? new Date(time).toLocaleString('zh-CN') : '-',
    },
    {
      title: '操作',
      key: 'action',
      width: 140,
      render: (_: unknown, record: MarketingContentDto) => (
        <Space>
          <Button type="link" size="small" onClick={() => handleEdit(record)}>
            编辑
          </Button>
          <Popconfirm
            title="确定删除该营销内容吗？"
            onConfirm={() => handleDelete(Number(record.contentId))}
            okText="确定"
            cancelText="取消"
          >
            <Button type="link" size="small" danger>
              删除
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  return (
    <div>
      <div style={{ marginBottom: 16, display: 'flex', justifyContent: 'space-between' }}>
        <Space>
          <Input
            placeholder="搜索标题关键词"
            value={searchKeyword}
            onChange={e => setSearchKeyword(e.target.value)}
            style={{ width: 180 }}
            onPressEnter={() => loadData(1)}
            allowClear
          />
          <Select
            placeholder="选择演出"
            value={searchShowId}
            onChange={v => setSearchShowId(v ?? undefined)}
            style={{ width: 200 }}
            allowClear
            showSearch
            optionFilterProp="children"
          >
            {shows.map(show => (
              <Select.Option key={show.showId} value={Number(show.showId)}>
                {show.showName}（ID: {show.showId}）
              </Select.Option>
            ))}
          </Select>
          <Select
            placeholder="类型"
            value={searchContentType}
            onChange={setSearchContentType}
            style={{ width: 100 }}
            allowClear
          >
            {Object.entries(contentTypeMap).map(([key, val]) => (
              <Select.Option key={key} value={key}>{val}</Select.Option>
            ))}
          </Select>
          <Select
            placeholder="状态"
            value={searchStatus}
            onChange={setSearchStatus}
            style={{ width: 100 }}
            allowClear
          >
            {Object.entries(statusMap).map(([key, val]) => (
              <Select.Option key={key} value={key}>{val.text}</Select.Option>
            ))}
          </Select>
          <Button type="primary" onClick={() => loadData(1)}>
            搜索
          </Button>
        </Space>
        <Button type="primary" icon={<PlusOutlined />} onClick={handleAdd}>
          新增营销内容
        </Button>
      </div>

      <Table
        columns={columns}
        dataSource={data}
        rowKey="contentId"
        loading={loading}
        pagination={{
          ...pagination,
          showSizeChanger: true,
          showTotal: total => `共 ${total} 条`,
          onChange: (page, pageSize) => loadData(page, pageSize),
        }}
      />

      <Modal
        title={editingItem ? '编辑营销内容' : '新增营销内容'}
        open={modalVisible}
        onOk={handleSubmit}
        onCancel={() => setModalVisible(false)}
        width={600}
        okText="确定"
        cancelText="取消"
      >
        <Form form={form} layout="vertical" style={{ marginTop: 16 }} initialValues={{ sortOrder: 0, status: 'ENABLED' }}>
          {!editingItem && (
            <Form.Item
              label="关联演出"
              name="showId"
              rules={[{ required: true, message: '请选择关联演出' }]}
            >
              <Select
                placeholder="搜索并选择演出"
                showSearch
                optionFilterProp="children"
              >
                {shows.map(show => (
                  <Select.Option key={show.showId} value={Number(show.showId)}>
                    {show.showName}（ID: {show.showId}）
                  </Select.Option>
                ))}
              </Select>
            </Form.Item>
          )}
          <Form.Item
            label="内容类型"
            name="contentType"
            rules={[{ required: true, message: '请选择内容类型' }]}
          >
            <Select
              options={Object.entries(contentTypeMap).map(([value, label]) => ({ value, label }))}
              placeholder="选择内容类型"
            />
          </Form.Item>
          <Form.Item
            label="标题"
            name="title"
            rules={[{ required: true, message: '请输入标题' }]}
          >
            <Input placeholder="营销内容标题" />
          </Form.Item>
          <Form.Item label="正文内容" name="contentText">
            <TextArea rows={3} placeholder="营销内容正文（可选）" />
          </Form.Item>
          <Form.Item
            label="图片"
            name="imageUrl"
            extra="支持 jpg/jpeg/png/webp/gif，不超过 5MB"
          >
            <FileUploader folder="marketing" />
          </Form.Item>
          <Space size="large" style={{ width: '100%' }}>
            <Form.Item label="排序" name="sortOrder" style={{ width: 150 }}>
              <InputNumber style={{ width: '100%' }} placeholder="数字越小越靠前" />
            </Form.Item>
            <Form.Item label="状态" name="status" style={{ width: 150 }}>
              <Select
                options={Object.entries(statusMap).map(([value, val]) => ({ value, label: val.text }))}
                placeholder="选择状态"
              />
            </Form.Item>
          </Space>
          <Form.Item label="发布时间" name="publishTime">
            <DatePicker showTime style={{ width: '100%' }} placeholder="选择发布时间（可选）" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  )
}

export default Marketing
