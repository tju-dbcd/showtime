import { useEffect, useState } from 'react'
import {
  Table,
  Button,
  Space,
  Modal,
  Form,
  Input,
  Select,
  Tag,
  message,
  Popconfirm,
} from 'antd'
import { PlusOutlined, TeamOutlined } from '@ant-design/icons'
import {
  getAdminUserList,
  createAdminUser,
  deleteAdminUser,
  type AdminUser,
} from '../../../api/admin'

const statusMap: Record<number, { text: string; color: string }> = {
  0: { text: '禁用', color: 'default' },
  1: { text: '正常', color: 'success' },
  2: { text: '锁定', color: 'warning' },
}

const User = () => {
  const [data, setData] = useState<AdminUser[]>([])
  const [loading, setLoading] = useState(false)
  const [modalVisible, setModalVisible] = useState(false)
  const [form] = Form.useForm()
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10, total: 0 })
  const [searchKeyword, setSearchKeyword] = useState('')
  const [searchStatus, setSearchStatus] = useState<number | undefined>()

  const loadData = async (page = 1, pageSize = 10) => {
    setLoading(true)
    try {
      const res = await getAdminUserList({
        PageIndex: page,
        PageSize: pageSize,
        Keyword: searchKeyword || undefined,
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
    } catch {
      message.error('加载用户列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadData(1)
  }, [])

  const handleAdd = () => {
    form.resetFields()
    setModalVisible(true)
  }

  const handleDelete = async (userId: number) => {
    try {
      const res = await deleteAdminUser(userId)
      if (res.error) {
        message.error((res.error as { message?: string })?.message || '删除失败')
        return
      }
      message.success('删除成功')
      // 当前页删空时回退到前一页
      const nextPage = data.length === 1 && pagination.current > 1
        ? pagination.current - 1
        : pagination.current
      loadData(nextPage, pagination.pageSize)
    } catch {
      message.error('删除失败')
    }
  }

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields()
      const res = await createAdminUser({
        userName: values.userName as string,
        password: values.password as string,
        phone: values.phone as string,
        nickname: (values.nickname as string) || null,
        email: (values.email as string) || null,
      })
      if (res.error) {
        message.error((res.error as { message?: string })?.message || '创建失败')
        return
      }
      message.success('创建成功')
      setModalVisible(false)
      loadData(1, pagination.pageSize)
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) return
      message.error(err instanceof Error ? err.message : '操作失败')
    }
  }

  const columns = [
    {
      title: 'ID',
      dataIndex: 'userId',
      key: 'userId',
      width: 80,
    },
    {
      title: '用户名',
      dataIndex: 'userName',
      key: 'userName',
    },
    {
      title: '昵称',
      dataIndex: 'nickname',
      key: 'nickname',
      render: (nickname: string | null) => nickname || '-',
    },
    {
      title: '手机号',
      dataIndex: 'phone',
      key: 'phone',
    },
    {
      title: '邮箱',
      dataIndex: 'email',
      key: 'email',
      render: (email: string | null) => email || '-',
    },
    {
      title: '角色',
      dataIndex: 'roles',
      key: 'roles',
      width: 140,
      render: (roles: string[]) => (
        <Space size={4} wrap>
          {roles.map(role => (
            <Tag key={role} color={role === 'Admin' ? 'gold' : 'blue'}>
              {role}
            </Tag>
          ))}
        </Space>
      ),
    },
    {
      title: '状态',
      dataIndex: 'status',
      key: 'status',
      width: 90,
      render: (status: number) => {
        const item = statusMap[Number(status)]
        return item ? <Tag color={item.color}>{item.text}</Tag> : status
      },
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
      width: 100,
      render: (_: unknown, record: AdminUser) => (
        <Popconfirm
          title="确定删除该用户吗？"
          description="仅当用户没有订单、票务、会话等关联数据时才能删除"
          onConfirm={() => handleDelete(Number(record.userId))}
          okText="确定"
          cancelText="取消"
        >
          <Button type="link" size="small" danger>
            删除
          </Button>
        </Popconfirm>
      ),
    },
  ]

  return (
    <div>
      <div style={{ marginBottom: 16, display: 'flex', justifyContent: 'space-between' }}>
        <Space>
          <Input
            placeholder="搜索用户名/昵称/手机号/邮箱"
            value={searchKeyword}
            onChange={e => setSearchKeyword(e.target.value)}
            style={{ width: 220 }}
            onPressEnter={() => loadData(1)}
            allowClear
          />
          <Select
            placeholder="状态"
            value={searchStatus}
            onChange={setSearchStatus}
            style={{ width: 100 }}
            allowClear
          >
            {Object.entries(statusMap).map(([key, val]) => (
              <Select.Option key={key} value={Number(key)}>{val.text}</Select.Option>
            ))}
          </Select>
          <Button type="primary" onClick={() => loadData(1)}>
            搜索
          </Button>
        </Space>
        <Button type="primary" icon={<PlusOutlined />} onClick={handleAdd}>
          新增用户
        </Button>
      </div>

      <Table
        columns={columns}
        dataSource={data}
        rowKey="userId"
        loading={loading}
        pagination={{
          ...pagination,
          showSizeChanger: true,
          showTotal: total => `共 ${total} 条`,
          onChange: (page, pageSize) => loadData(page, pageSize),
        }}
      />

      <Modal
        title={
          <Space>
            <TeamOutlined />
            新增用户
          </Space>
        }
        open={modalVisible}
        onOk={handleSubmit}
        onCancel={() => setModalVisible(false)}
        width={520}
        okText="确定"
        cancelText="取消"
      >
        <Form form={form} layout="vertical" style={{ marginTop: 16 }}>
          <Form.Item
            label="用户名"
            name="userName"
            rules={[
              { required: true, message: '请输入用户名' },
              { min: 3, max: 50, message: '用户名长度为 3-50 个字符' },
            ]}
          >
            <Input placeholder="以字母开头，可含字母/数字/下划线" />
          </Form.Item>
          <Form.Item
            label="密码"
            name="password"
            rules={[
              { required: true, message: '请输入密码' },
              { min: 8, max: 128, message: '密码长度为 8-128 个字符' },
            ]}
            extra="至少包含一个字母和一个数字"
          >
            <Input.Password placeholder="设置登录密码" />
          </Form.Item>
          <Form.Item
            label="手机号"
            name="phone"
            rules={[{ required: true, message: '请输入手机号' }]}
          >
            <Input placeholder="6-20 位数字，可加国际区号前缀" />
          </Form.Item>
          <Form.Item label="昵称" name="nickname">
            <Input placeholder="选填" maxLength={50} />
          </Form.Item>
          <Form.Item
            label="邮箱"
            name="email"
            rules={[{ type: 'email', message: '邮箱格式不正确' }]}
          >
            <Input placeholder="选填" maxLength={100} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  )
}

export default User
