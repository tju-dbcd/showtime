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
  Popconfirm,
  Tag,
  Drawer,
} from 'antd'
import { PlusOutlined } from '@ant-design/icons'
import {
  getSeatRuleList,
  createSeatRule,
  updateSeatRule,
  deleteSeatRule,
  getSeatRuleScopes,
  createSeatRuleScope,
  deleteSeatRuleScope,
  getSeatMapList,
  getSeatSections,
  type SeatRule as SeatRuleDto,
  type SaveSeatRuleRequest,
  type SeatRuleScope,
  type SeatRuleScopeRequest,
  type SeatMapResponse,
  type SeatSectionResponse,
} from '../../../api/admin'

const { TextArea } = Input

const RULE_TYPES = [
  { value: 'CONTINUOUS', label: '连座规则', description: '要求所选座位连续' },
  { value: 'NO_SINGLE_LEFT', label: '禁单座规则', description: '不允许留下单个空位' },
  { value: 'LIMIT_COUNT', label: '限购数量', description: '限制单笔订单座位数量' },
  { value: 'SECTION_LIMIT', label: '分区规则', description: '分区维度限制/约束' },
]

const RULE_STATUSES = [
  { value: 'ENABLED', label: '启用' },
  { value: 'DISABLED', label: '停用' },
]

const SCOPE_TYPES = [
  { value: 'MAP', label: '座位图' },
  { value: 'SECTION', label: '票区' },
]

const SeatRule = () => {
  const [data, setData] = useState<SeatRuleDto[]>([])
  const [loading, setLoading] = useState(false)
  const [ruleTypeFilter, setRuleTypeFilter] = useState<string | undefined>()
  const [ruleStatusFilter, setRuleStatusFilter] = useState<string | undefined>()
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10, total: 0 })
  const [modalVisible, setModalVisible] = useState(false)
  const [editingItem, setEditingItem] = useState<SeatRuleDto | null>(null)
  const [saving, setSaving] = useState(false)
  const [form] = Form.useForm()

  // 作用域管理
  const [scopeRule, setScopeRule] = useState<SeatRuleDto | null>(null)
  const [scopes, setScopes] = useState<SeatRuleScope[]>([])
  const [scopesLoading, setScopesLoading] = useState(false)
  const [scopeAddVisible, setScopeAddVisible] = useState(false)
  const [scopeSaving, setScopeSaving] = useState(false)
  const [scopeForm] = Form.useForm()
  const [seatMaps, setSeatMaps] = useState<SeatMapResponse[]>([])
  const [scopeSections, setScopeSections] = useState<SeatSectionResponse[]>([])

  const loadData = async (page = 1, pageSize = 10) => {
    setLoading(true)
    try {
      const res = await getSeatRuleList({
        RuleType: ruleTypeFilter,
        RuleStatus: ruleStatusFilter,
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
      message.error('加载座位规则失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadData(1)
  }, [])

  useEffect(() => {
    getSeatMapList({ PageSize: 100 })
      .then(res => {
        if (res.data?.data?.items) setSeatMaps(res.data.data.items)
      })
      .catch(() => message.error('加载座位图列表失败'))
  }, [])

  const openCreate = () => {
    setEditingItem(null)
    form.resetFields()
    form.setFieldsValue({ priority: 0, ruleStatus: 'ENABLED', allowCrossRow: false, allowCrossSection: false })
    setModalVisible(true)
  }

  const openEdit = (item: SeatRuleDto) => {
    setEditingItem(item)
    form.resetFields()
    form.setFieldsValue({
      ruleCode: item.ruleCode,
      ruleName: item.ruleName,
      ruleType: item.ruleType,
      minSeatCount: Number(item.minSeatCount),
      maxSeatCount: Number(item.maxSeatCount),
      allowCrossRow: item.allowCrossRow,
      allowCrossSection: item.allowCrossSection,
      priority: Number(item.priority),
      ruleStatus: item.ruleStatus,
      remark: item.remark || undefined,
    })
    setModalVisible(true)
  }

  const handleSave = async () => {
    try {
      const values = await form.validateFields()
      const payload: SaveSeatRuleRequest = {
        ruleCode: values.ruleCode.trim(),
        ruleName: values.ruleName.trim(),
        ruleType: values.ruleType,
        minSeatCount: values.minSeatCount ?? 1,
        maxSeatCount: values.maxSeatCount ?? 9,
        allowCrossRow: values.allowCrossRow ?? false,
        allowCrossSection: values.allowCrossSection ?? false,
        priority: values.priority ?? 0,
        ruleStatus: values.ruleStatus,
        remark: values.remark?.trim() || null,
      }
      setSaving(true)
      const res = editingItem
        ? await updateSeatRule(Number(editingItem.seatRuleId), payload)
        : await createSeatRule(payload)
      if (res.error) {
        message.error('保存失败')
        return
      }
      message.success(editingItem ? '规则已更新' : '规则已创建')
      setModalVisible(false)
      loadData(pagination.current, pagination.pageSize)
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) return
      message.error('保存失败')
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async (seatRuleId: number) => {
    try {
      const res = await deleteSeatRule(seatRuleId)
      if (res.error) {
        message.error('删除失败')
        return
      }
      message.success('规则已删除')
      loadData(pagination.current, pagination.pageSize)
    } catch {
      message.error('删除失败')
    }
  }

  // ===== 作用域 =====
  const openScopes = async (rule: SeatRuleDto) => {
    setScopeRule(rule)
    setScopes([])
    setScopesLoading(true)
    try {
      const res = await getSeatRuleScopes(Number(rule.seatRuleId))
      if (res.data?.data) {
        setScopes(res.data.data || [])
      }
    } catch {
      message.error('加载作用域失败')
    } finally {
      setScopesLoading(false)
    }
  }

  const openScopeAdd = () => {
    scopeForm.resetFields()
    scopeForm.setFieldsValue({ scopeType: 'MAP', scopeStatus: 'ENABLED' })
    setScopeSections([])
    setScopeAddVisible(true)
  }

  const handleScopeTypeChange = (type: string) => {
    scopeForm.setFieldsValue({ seatMapId: undefined, seatSectionId: undefined })
    setScopeSections([])
    if (type === 'SECTION') {
      scopeForm.setFieldValue('seatMapId', undefined)
    }
  }

  const handleScopeMapChange = async (seatMapId: number) => {
    scopeForm.setFieldValue('seatSectionId', undefined)
    try {
      const res = await getSeatSections(seatMapId, { PageSize: 100 })
      if (res.data?.data) {
        setScopeSections(res.data.data.items || [])
      }
    } catch {
      setScopeSections([])
      message.error('加载票区失败')
    }
  }

  const handleScopeSave = async () => {
    if (!scopeRule) return
    try {
      const values = await scopeForm.validateFields()
      const payload: SeatRuleScopeRequest = {
        scopeType: values.scopeType,
        seatMapId: values.scopeType === 'MAP' ? values.seatMapId : null,
        seatSectionId: values.scopeType === 'SECTION' ? values.seatSectionId : null,
        scopeStatus: values.scopeStatus,
      }
      setScopeSaving(true)
      const res = await createSeatRuleScope(Number(scopeRule.seatRuleId), payload)
      if (res.error) {
        message.error('添加失败')
        return
      }
      message.success('作用域已添加')
      setScopeAddVisible(false)
      openScopes(scopeRule)
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) return
      message.error('添加失败')
    } finally {
      setScopeSaving(false)
    }
  }

  const handleScopeDelete = async (ruleScopeId: number) => {
    try {
      const res = await deleteSeatRuleScope(ruleScopeId)
      if (res.error) {
        message.error('删除失败')
        return
      }
      message.success('作用域已删除')
      if (scopeRule) openScopes(scopeRule)
    } catch {
      message.error('删除失败')
    }
  }

  const ruleTypeMap = Object.fromEntries(RULE_TYPES.map(t => [t.value, t.label]))
  const ruleStatusMap: Record<string, { text: string; color: string }> = {
    ENABLED: { text: '启用', color: 'success' },
    DISABLED: { text: '停用', color: 'default' },
  }
  const scopeTypeMap: Record<string, string> = Object.fromEntries(SCOPE_TYPES.map(t => [t.value, t.label]))

  const columns = [
    {
      title: '规则ID',
      dataIndex: 'seatRuleId',
      key: 'seatRuleId',
      width: 90,
    },
    {
      title: '规则编码',
      dataIndex: 'ruleCode',
      key: 'ruleCode',
      width: 120,
    },
    {
      title: '规则名称',
      dataIndex: 'ruleName',
      key: 'ruleName',
      width: 160,
    },
    {
      title: '规则类型',
      dataIndex: 'ruleType',
      key: 'ruleType',
      width: 130,
      render: (t: string) => ruleTypeMap[t] || t,
    },
    {
      title: '限购范围',
      key: 'count',
      width: 110,
      render: (_: unknown, record: SeatRuleDto) => `${record.minSeatCount}~${record.maxSeatCount}`,
    },
    {
      title: '允许跨排/跨区',
      key: 'cross',
      width: 120,
      render: (_: unknown, record: SeatRuleDto) =>
        `${record.allowCrossRow ? '跨排' : ''}${record.allowCrossRow && record.allowCrossSection ? '/' : ''}${record.allowCrossSection ? '跨区' : ''}` || '否',
    },
    {
      title: '优先级',
      dataIndex: 'priority',
      key: 'priority',
      width: 80,
    },
    {
      title: '状态',
      dataIndex: 'ruleStatus',
      key: 'ruleStatus',
      width: 90,
      render: (s: string) => {
        const m = ruleStatusMap[s]
        return m ? <Tag color={m.color}>{m.text}</Tag> : s
      },
    },
    {
      title: '操作',
      key: 'action',
      width: 220,
      render: (_: unknown, record: SeatRuleDto) => (
        <Space>
          <Button type="link" size="small" onClick={() => openScopes(record)}>
            作用域
          </Button>
          <Button type="link" size="small" onClick={() => openEdit(record)}>
            编辑
          </Button>
          <Popconfirm
            title="确定删除该规则吗？"
            onConfirm={() => handleDelete(Number(record.seatRuleId))}
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
      <div style={{ marginBottom: 16 }}>
        <Space wrap>
          <Button type="primary" icon={<PlusOutlined />} onClick={openCreate}>
            新建规则
          </Button>
          <span>规则类型：</span>
          <Select
            placeholder="全部"
            value={ruleTypeFilter}
            onChange={setRuleTypeFilter}
            style={{ width: 140 }}
            allowClear
          >
            {RULE_TYPES.map(t => (
              <Select.Option key={t.value} value={t.value}>{t.label}</Select.Option>
            ))}
          </Select>
          <span>状态：</span>
          <Select
            placeholder="全部"
            value={ruleStatusFilter}
            onChange={setRuleStatusFilter}
            style={{ width: 110 }}
            allowClear
          >
            {RULE_STATUSES.map(t => (
              <Select.Option key={t.value} value={t.value}>{t.label}</Select.Option>
            ))}
          </Select>
          <Button onClick={() => loadData(1)}>筛选</Button>
        </Space>
      </div>

      <Table
        columns={columns}
        dataSource={data}
        rowKey="seatRuleId"
        loading={loading}
        pagination={{
          ...pagination,
          showSizeChanger: true,
          showTotal: total => `共 ${total} 条`,
          onChange: (page, pageSize) => loadData(page, pageSize),
        }}
      />

      <Modal
        title={editingItem ? '编辑座位规则' : '新建座位规则'}
        open={modalVisible}
        onCancel={() => setModalVisible(false)}
        onOk={handleSave}
        confirmLoading={saving}
        okText="保存"
        width={600}
      >
        <Form form={form} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item name="ruleCode" label="规则编码" rules={[{ required: true, message: '请输入规则编码' }]}>
            <Input placeholder="如：CONTINUOUS_2" maxLength={30} />
          </Form.Item>
          <Form.Item name="ruleName" label="规则名称" rules={[{ required: true, message: '请输入规则名称' }]}>
            <Input placeholder="如：双人连座规则" maxLength={50} />
          </Form.Item>
          <Form.Item name="ruleType" label="规则类型" rules={[{ required: true, message: '请选择规则类型' }]}>
            <Select>
              {RULE_TYPES.map(t => (
                <Select.Option key={t.value} value={t.value}>{t.label}</Select.Option>
              ))}
            </Select>
          </Form.Item>
          <Space size="large">
            <Form.Item name="minSeatCount" label="最少座位数">
              <InputNumber min={1} max={100} style={{ width: 140 }} />
            </Form.Item>
            <Form.Item name="maxSeatCount" label="最多座位数">
              <InputNumber min={1} max={100} style={{ width: 140 }} />
            </Form.Item>
          </Space>
          <Space size="large">
            <Form.Item name="allowCrossRow" label="允许跨排" valuePropName="checked">
              <Switch />
            </Form.Item>
            <Form.Item name="allowCrossSection" label="允许跨区" valuePropName="checked">
              <Switch />
            </Form.Item>
            <Form.Item name="priority" label="优先级">
              <InputNumber min={0} style={{ width: 120 }} />
            </Form.Item>
          </Space>
          <Form.Item name="ruleStatus" label="状态" rules={[{ required: true, message: '请选择状态' }]}>
            <Select>
              {RULE_STATUSES.map(t => (
                <Select.Option key={t.value} value={t.value}>{t.label}</Select.Option>
              ))}
            </Select>
          </Form.Item>
          <Form.Item name="remark" label="备注">
            <TextArea rows={2} maxLength={200} />
          </Form.Item>
        </Form>
      </Modal>

      <Drawer
        title={scopeRule ? `作用域 - ${scopeRule.ruleName}` : '作用域'}
        open={!!scopeRule}
        onClose={() => setScopeRule(null)}
        width={520}
        extra={
          <Button type="primary" size="small" icon={<PlusOutlined />} onClick={openScopeAdd}>
            添加作用域
          </Button>
        }
      >
        <Table
          dataSource={scopes}
          rowKey="ruleScopeId"
          loading={scopesLoading}
          size="small"
          pagination={false}
          locale={{ emptyText: '暂无作用域' }}
          columns={[
            {
              title: '类型',
              dataIndex: 'scopeType',
              key: 'scopeType',
              render: (t: string) => scopeTypeMap[t] || t,
            },
            {
              title: '目标',
              key: 'target',
              render: (_: unknown, record: SeatRuleScope) =>
                record.scopeType === 'MAP'
                  ? `座位图#${record.seatMapId}`
                  : `票区#${record.seatSectionId}`,
            },
            {
              title: '状态',
              dataIndex: 'scopeStatus',
              key: 'scopeStatus',
              render: (s: string) => {
                const m = ruleStatusMap[s]
                return m ? <Tag color={m.color}>{m.text}</Tag> : s
              },
            },
            {
              title: '操作',
              key: 'action',
              render: (_: unknown, record: SeatRuleScope) => (
                <Popconfirm
                  title="确定删除该作用域吗？"
                  onConfirm={() => handleScopeDelete(Number(record.ruleScopeId))}
                  okText="确定"
                  cancelText="取消"
                >
                  <Button type="link" size="small" danger>
                    删除
                  </Button>
                </Popconfirm>
              ),
            },
          ]}
        />
      </Drawer>

      <Modal
        title="添加作用域"
        open={scopeAddVisible}
        onCancel={() => setScopeAddVisible(false)}
        onOk={handleScopeSave}
        confirmLoading={scopeSaving}
        okText="添加"
        width={480}
      >
        <Form form={scopeForm} layout="vertical" style={{ marginTop: 8 }}>
          <Form.Item name="scopeType" label="作用域类型" rules={[{ required: true, message: '请选择类型' }]}>
            <Select onChange={handleScopeTypeChange}>
              {SCOPE_TYPES.map(t => (
                <Select.Option key={t.value} value={t.value}>{t.label}</Select.Option>
              ))}
            </Select>
          </Form.Item>
          <Form.Item
            noStyle
            shouldUpdate={(prev, cur) => prev.scopeType !== cur.scopeType}
          >
            {({ getFieldValue }) =>
              getFieldValue('scopeType') === 'MAP' ? (
                <Form.Item name="seatMapId" label="座位图" rules={[{ required: true, message: '请选择座位图' }]}>
                  <Select
                    placeholder="请选择座位图"
                    showSearch
                    optionFilterProp="children"
                  >
                    {seatMaps.map(m => (
                      <Select.Option key={m.seatMapId} value={Number(m.seatMapId)}>
                        {m.mapName || `座位图#${m.seatMapId}`}
                      </Select.Option>
                    ))}
                  </Select>
                </Form.Item>
              ) : (
                <>
                  <Form.Item name="seatMapId" label="所属座位图" rules={[{ required: true, message: '请选择座位图' }]}>
                    <Select
                      placeholder="请选择座位图"
                      showSearch
                      optionFilterProp="children"
                      onChange={handleScopeMapChange}
                    >
                      {seatMaps.map(m => (
                        <Select.Option key={m.seatMapId} value={Number(m.seatMapId)}>
                          {m.mapName || `座位图#${m.seatMapId}`}
                        </Select.Option>
                      ))}
                    </Select>
                  </Form.Item>
                  <Form.Item name="seatSectionId" label="票区" rules={[{ required: true, message: '请选择票区' }]}>
                    <Select placeholder="请选择票区" showSearch optionFilterProp="children">
                      {scopeSections.map(s => (
                        <Select.Option key={s.seatSectionId} value={Number(s.seatSectionId)}>
                          {s.sectionName || `票区#${s.seatSectionId}`}
                        </Select.Option>
                      ))}
                    </Select>
                  </Form.Item>
                </>
              )
            }
          </Form.Item>
          <Form.Item name="scopeStatus" label="状态" rules={[{ required: true, message: '请选择状态' }]}>
            <Select>
              {RULE_STATUSES.map(t => (
                <Select.Option key={t.value} value={t.value}>{t.label}</Select.Option>
              ))}
            </Select>
          </Form.Item>
        </Form>
      </Modal>
    </div>
  )
}

export default SeatRule
