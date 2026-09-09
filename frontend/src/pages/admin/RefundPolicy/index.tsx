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
  Switch,
  message,
  Tag,
} from 'antd'
import { PlusOutlined } from '@ant-design/icons'
import {
  getRefundPolicyList,
  createRefundPolicy,
  updateRefundPolicy,
  updateRefundPolicyStatus,
  getShowList,
  type RefundPolicy as RefundPolicyDto,
  type SaveRefundPolicyRequest,
  type ShowDto,
} from '../../../api/admin'

const { TextArea } = Input

const RefundPolicy = () => {
  const [data, setData] = useState<RefundPolicyDto[]>([])
  const [shows, setShows] = useState<ShowDto[]>([])
  const [loading, setLoading] = useState(false)
  const [modalVisible, setModalVisible] = useState(false)
  const [editingItem, setEditingItem] = useState<RefundPolicyDto | null>(null)
  const [saving, setSaving] = useState(false)
  const [form] = Form.useForm()
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10, total: 0 })
  const [showFilter, setShowFilter] = useState<number | undefined>()
  const [statusFilter, setStatusFilter] = useState<number | undefined>()

  const loadData = async (page = 1, pageSize = 10) => {
    setLoading(true)
    try {
      const res = await getRefundPolicyList({
        ShowId: showFilter,
        Status: statusFilter,
        Page: page,
        PageSize: pageSize,
      })
      if (res.data?.data) {
        const r = res.data.data
        setData(r.items || [])
        setPagination({
          current: page,
          pageSize,
          total: Number(r.totalCount) || 0,
        })
      }
    } catch {
      message.error('加载退票策略失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadData(1)
  }, [])

  // 加载演出列表，用于选择策略适用的演出（可选）
  useEffect(() => {
    getShowList({ PageSize: 100 })
      .then(res => {
        if (res.data?.data) setShows(res.data.data.items || [])
      })
      .catch(() => message.error('加载演出列表失败'))
  }, [])

  const openCreate = () => {
    setEditingItem(null)
    form.resetFields()
    form.setFieldsValue({ refundRate: 0.8, serviceFee: 0, priority: 0, status: 1 })
    setModalVisible(true)
  }

  const openEdit = (item: RefundPolicyDto) => {
    setEditingItem(item)
    form.resetFields()
    form.setFieldsValue({
      showId: item.showId != null ? Number(item.showId) : undefined,
      policyName: item.policyName,
      refundDeadlineHour: Number(item.refundDeadlineHour),
      refundRate: Number(item.refundRate),
      serviceFee: Number(item.serviceFee),
      priority: Number(item.priority),
      remark: item.remark || undefined,
    })
    setModalVisible(true)
  }

  const handleSave = async () => {
    try {
      const values = await form.validateFields()
      const payload: SaveRefundPolicyRequest = {
        showId: values.showId ?? null,
        policyName: values.policyName.trim(),
        refundDeadlineHour: values.refundDeadlineHour,
        refundRate: values.refundRate,
        serviceFee: values.serviceFee ?? 0,
        priority: values.priority ?? 0,
        remark: values.remark?.trim() || null,
      }
      setSaving(true)
      const res = editingItem
        ? await updateRefundPolicy(Number(editingItem.policyId), payload)
        : await createRefundPolicy(payload)
      if (res.error) {
        message.error('保存失败')
        return
      }
      message.success(editingItem ? '策略已更新' : '策略已创建')
      setModalVisible(false)
      loadData(pagination.current, pagination.pageSize)
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) return
      message.error('保存失败')
    } finally {
      setSaving(false)
    }
  }

  const handleToggleStatus = async (item: RefundPolicyDto, status: number) => {
    try {
      const res = await updateRefundPolicyStatus(Number(item.policyId), { status })
      if (res.error) {
        message.error('操作失败')
        return
      }
      message.success(status === 1 ? '策略已启用' : '策略已停用')
      loadData(pagination.current, pagination.pageSize)
    } catch {
      message.error('操作失败')
    }
  }

  const policyStatusMap: Record<number, { text: string; color: string }> = {
    1: { text: '启用', color: 'success' },
    0: { text: '停用', color: 'default' },
  }

  const columns = [
    {
      title: '策略ID',
      dataIndex: 'policyId',
      key: 'policyId',
      width: 90,
    },
    {
      title: '策略名称',
      dataIndex: 'policyName',
      key: 'policyName',
      width: 180,
    },
    {
      title: '适用演出',
      key: 'show',
      width: 220,
      render: (_: unknown, record: RefundPolicyDto) => {
        if (record.showId == null) return <Tag>全局</Tag>
        const show = shows.find(s => Number(s.showId) === Number(record.showId))
        return <span>{show ? show.showName : `演出#${record.showId}`}</span>
      },
    },
    {
      title: '截止小时',
      dataIndex: 'refundDeadlineHour',
      key: 'refundDeadlineHour',
      width: 90,
      render: (v: number | string) => `${v}h`,
    },
    {
      title: '退款比例',
      dataIndex: 'refundRate',
      key: 'refundRate',
      width: 100,
      render: (v: number | string) => `${(Number(v) * 100).toFixed(0)}%`,
    },
    {
      title: '服务费',
      dataIndex: 'serviceFee',
      key: 'serviceFee',
      width: 90,
      render: (v: number | string) => `¥${v}`,
    },
    {
      title: '优先级',
      dataIndex: 'priority',
      key: 'priority',
      width: 80,
    },
    {
      title: '状态',
      dataIndex: 'status',
      key: 'status',
      width: 90,
      render: (s: number) => {
        const m = policyStatusMap[s]
        return m ? <Tag color={m.color}>{m.text}</Tag> : s
      },
    },
    {
      title: '操作',
      key: 'action',
      width: 150,
      render: (_: unknown, record: RefundPolicyDto) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEdit(record)}>
            编辑
          </Button>
          <Switch
            checked={Number(record.status) === 1}
            size="small"
            checkedChildren="启"
            unCheckedChildren="停"
            onChange={checked => handleToggleStatus(record, checked ? 1 : 0)}
          />
        </Space>
      ),
    },
  ]

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Space wrap>
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            新建策略
          </Button>
          <span>适用演出：</span>
          <Select
            placeholder="全部"
            value={showFilter}
            onChange={setShowFilter}
            style={{ width: 220 }}
            allowClear
            showSearch
            optionFilterProp="children"
          >
            {shows.map(show => (
              <Select.Option key={show.showId} value={Number(show.showId)}>
                {show.showName}
              </Select.Option>
            ))}
          </Select>
          <span>状态：</span>
          <Select
            placeholder="全部"
            value={statusFilter}
            onChange={setStatusFilter}
            style={{ width: 110 }}
            allowClear
          >
            <Select.Option value={1}>启用</Select.Option>
            <Select.Option value={0}>停用</Select.Option>
          </Select>
          <Button onClick={() => loadData(1)}>筛选</Button>
        </Space>
      </div>

      <Table
        columns={columns}
        dataSource={data}
        rowKey="policyId"
        loading={loading}
        pagination={{
          ...pagination,
          showSizeChanger: true,
          showTotal: total => `共 ${total} 条`,
          onChange: (page, pageSize) => loadData(page, pageSize),
        }}
      />

      <Modal
        title={editingItem ? '编辑退票策略' : '新建退票策略'}
        open={modalVisible}
        onCancel={() => setModalVisible(false)}
        onOk={handleSave}
        confirmLoading={saving}
        okText="保存"
        width={560}
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item name="policyName" label="策略名称" rules={[{ required: true, message: '请输入策略名称' }]}>
            <Input placeholder="如：开场前2小时退款80%" maxLength={50} />
          </Form.Item>
          <Form.Item name="showId" label="适用演出（留空为全局）">
            <Select
              placeholder="全部演出"
              allowClear
              showSearch
              optionFilterProp="children"
            >
              {shows.map(show => (
                <Select.Option key={show.showId} value={Number(show.showId)}>
                  {show.showName}
                </Select.Option>
              ))}
            </Select>
          </Form.Item>
          <Form.Item
            name="refundDeadlineHour"
            label="退票截止（距演出开始小时数）"
            rules={[{ required: true, message: '请输入截止小时数' }]}
          >
            <InputNumber min={0} max={8760} style={{ width: '100%' }} addonAfter="小时" />
          </Form.Item>
          <Form.Item
            name="refundRate"
            label="退款比例（0~1，如 0.8 表示退 80%）"
            rules={[{ required: true, message: '请输入退款比例' }]}
          >
            <InputNumber min={0} max={1} step={0.05} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="serviceFee" label="固定服务费（元）">
            <InputNumber min={0} step={0.5} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="priority" label="优先级（数字越小越优先）">
            <InputNumber min={0} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="remark" label="备注">
            <TextArea rows={2} maxLength={200} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  )
}

export default RefundPolicy
