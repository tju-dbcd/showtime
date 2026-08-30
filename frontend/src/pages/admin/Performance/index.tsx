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
  message,
  Popconfirm,
  Tag,
} from 'antd'
import { PlusOutlined } from '@ant-design/icons'
import {
  getShowList,
  createShow,
  updateShow,
  deleteShow,
  getCategories,
  type ShowDto,
  type CreateShowRequest,
  type UpdateShowRequest,
  type ShowStatus,
  type CategoryResponse,
} from '../../../api/admin'

const { TextArea } = Input

const statusMap: Record<ShowStatus, { text: string; color: string }> = {
  DRAFT: { text: '草稿', color: 'default' },
  PUBLISHED: { text: '已发布', color: 'success' },
  UNPUBLISHED: { text: '已下架', color: 'warning' },
}

const Performance = () => {
  const [data, setData] = useState<ShowDto[]>([])
  const [loading, setLoading] = useState(false)
  const [modalVisible, setModalVisible] = useState(false)
  const [editingShow, setEditingShow] = useState<ShowDto | null>(null)
  const [form] = Form.useForm()
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10, total: 0 })
  const [searchName, setSearchName] = useState('')
  const [searchCategory, setSearchCategory] = useState<number | undefined>()
  const [searchStatus, setSearchStatus] = useState<ShowStatus | undefined>()
  const [categories, setCategories] = useState<CategoryResponse[]>([])

  const loadData = async (page = 1, pageSize = 10) => {
    setLoading(true)
    try {
      const res = await getShowList({
        PageIndex: page,
        PageSize: pageSize,
        Keyword: searchName || undefined,
        CategoryId: searchCategory,
        Status: searchStatus,
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
    } catch (err) {
      message.error('加载演出列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    getCategories().then(res => {
      if (res.data?.data) setCategories(res.data.data)
    }).catch(() => {})
    loadData(1)
  }, [])

  const handleAdd = () => {
    setEditingShow(null)
    form.resetFields()
    setModalVisible(true)
  }

  const handleEdit = (record: ShowDto) => {
    setEditingShow(record)
    form.setFieldsValue({
      showName: record.showName,
      categoryId: record.categoryId,
      durationMinutes: record.durationMinutes,
      posterUrl: record.posterUrl,
      description: record.description,
    })
    setModalVisible(true)
  }

  const handleDelete = async (showId: number) => {
    try {
      const res = await deleteShow(showId)
      if (res.error) {
        message.error('删除失败')
        return
      }
      message.success('删除成功')
      loadData(pagination.current, pagination.pageSize)
    } catch (err) {
      message.error('删除失败')
    }
  }

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields()
      if (editingShow) {
        const updateData: UpdateShowRequest = {
          showName: values.showName,
          categoryId: values.categoryId,
          status: editingShow.status || 'DRAFT',
          description: values.description || null,
          durationMinutes: values.durationMinutes || null,
          posterUrl: values.posterUrl || null,
        }
        const res = await updateShow(Number(editingShow.showId), updateData)
        if (res.error) {
          message.error('更新失败')
          return
        }
        message.success('更新成功')
      } else {
        const createData: CreateShowRequest = {
          showName: values.showName,
          categoryId: values.categoryId,
          description: values.description || null,
          durationMinutes: values.durationMinutes || null,
          posterUrl: values.posterUrl || null,
        }
        const res = await createShow(createData)
        if (res.error) {
          message.error('创建失败')
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
      dataIndex: 'showId',
      key: 'showId',
      width: 80,
    },
    {
      title: '演出名称',
      dataIndex: 'showName',
      key: 'showName',
    },
    {
      title: '分类',
      dataIndex: 'categoryId',
      key: 'categoryId',
      width: 100,
      render: (id: number | string) => {
        const cat = categories.find(c => c.categoryId === id)
        return cat ? cat.categoryName : id
      },
    },
    {
      title: '时长(分钟)',
      dataIndex: 'durationMinutes',
      key: 'durationMinutes',
      width: 100,
    },
    {
      title: '状态',
      dataIndex: 'status',
      key: 'status',
      width: 100,
      render: (status: ShowStatus) => {
        const s = statusMap[status]
        return s ? <Tag color={s.color}>{s.text}</Tag> : status
      },
    },
    {
      title: '创建时间',
      dataIndex: 'createTime',
      key: 'createTime',
      width: 180,
      render: (time: string) => time ? new Date(time).toLocaleString('zh-CN') : '-',
    },
    {
      title: '操作',
      key: 'action',
      width: 150,
      render: (_: unknown, record: ShowDto) => (
        <Space>
          <Button type="link" size="small" onClick={() => handleEdit(record)}>
            编辑
          </Button>
          <Popconfirm
            title="确定删除该演出吗？"
            onConfirm={() => handleDelete(Number(record.showId))}
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
            placeholder="搜索演出名称"
            value={searchName}
            onChange={e => setSearchName(e.target.value)}
            style={{ width: 200 }}
            onPressEnter={() => loadData(1)}
            allowClear
          />
          <Select
            placeholder="分类"
            value={searchCategory}
            onChange={setSearchCategory}
            style={{ width: 120 }}
            allowClear
          >
            {categories.map(cat => (
              <Select.Option key={cat.categoryId} value={Number(cat.categoryId)}>{cat.categoryName}</Select.Option>
            ))}
          </Select>
          <Select
            placeholder="状态"
            value={searchStatus}
            onChange={setSearchStatus}
            style={{ width: 120 }}
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
          新增演出
        </Button>
      </div>

      <Table
        columns={columns}
        dataSource={data}
        rowKey="showId"
        loading={loading}
        pagination={{
          ...pagination,
          showSizeChanger: true,
          showTotal: total => `共 ${total} 条`,
          onChange: (page, pageSize) => loadData(page, pageSize),
        }}
      />

      <Modal
        title={editingShow ? '编辑演出' : '新增演出'}
        open={modalVisible}
        onOk={handleSubmit}
        onCancel={() => setModalVisible(false)}
        width={600}
        okText="确定"
        cancelText="取消"
      >
        <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
          <Form.Item
            label="演出名称"
            name="showName"
            rules={[{ required: true, message: '请输入演出名称' }]}
          >
            <Input placeholder="请输入演出名称" />
          </Form.Item>
          <Space size="large" style={{ width: '100%' }}>
            <Form.Item
              label="分类"
              name="categoryId"
              rules={[{ required: true, message: '请选择分类' }]}
              style={{ width: 200 }}
            >
              <Select>
                {categories.map(cat => (
                  <Select.Option key={cat.categoryId} value={Number(cat.categoryId)}>{cat.categoryName}</Select.Option>
                ))}
              </Select>
            </Form.Item>
            <Form.Item
              label="时长（分钟）"
              name="durationMinutes"
              style={{ width: 200 }}
            >
              <InputNumber min={1} style={{ width: '100%' }} />
            </Form.Item>
          </Space>
          <Form.Item label="海报URL" name="posterUrl">
            <Input placeholder="请输入海报图片链接" />
          </Form.Item>
          <Form.Item label="演出介绍" name="description">
            <TextArea rows={4} placeholder="请输入演出介绍" maxLength={1000} showCount />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  )
}

export default Performance
