import { useEffect, useState } from 'react'
import {
  Table,
  Select,
  Button,
  Space,
  Tag,
  Modal,
  Descriptions,
  Form,
  Input,
  InputNumber,
  DatePicker,
  message,
  Popconfirm,
  Switch,
} from 'antd'
import dayjs from 'dayjs'
import {
  getShowList,
  getShowSessions,
  addSession,
  updateSession,
  updateSessionStatus,
  getSeatSections,
  getSeatMapList,
  configureDynamicPricingRules,
  getAdminPricingStrategies,
  addPricingStrategies,
  type ShowDto,
  type ShowSessionDto,
  type SeatMapResponse,
  type SessionStatus,
  type PriceType,
  type CreateDynamicPricingRuleRequest,
} from '../../../api/admin'

const priceTypeOptions: { value: PriceType; label: string }[] = [
  { value: 'EARLY_BIRD', label: '早鸟票' },
  { value: 'PRESALE', label: '预售票' },
  { value: 'STANDARD', label: '标准票' },
  { value: 'VIP', label: 'VIP票' },
  { value: 'MEMBER', label: '会员票' },
]

interface PriceStrategyRow {
  priceStrategyId?: number
  seatSectionId?: number
  priceType: PriceType
  price: number
  strategyName?: string
  saleWindow: [dayjs.Dayjs | null, dayjs.Dayjs | null] | null
  enabled: boolean
  priority: number
}

const sessionStatusMap: Record<SessionStatus, { text: string; color: string }> = {
  UPCOMING: { text: '待上架', color: 'default' },
  PRESALE: { text: '预售中', color: 'processing' },
  ONSALE: { text: '售卖中', color: 'success' },
  SOLD_OUT: { text: '已售罄', color: 'warning' },
  ENDED: { text: '已结束', color: 'default' },
}

const Session = () => {
  const [shows, setShows] = useState<ShowDto[]>([])
  const [selectedShowId, setSelectedShowId] = useState<number | undefined>()
  const [sessions, setSessions] = useState<ShowSessionDto[]>([])
  const [loading, setLoading] = useState(false)
  const [detailVisible, setDetailVisible] = useState(false)
  const [currentSession, setCurrentSession] = useState<ShowSessionDto | null>(null)
  // 新增/编辑场次
  const [seatMaps, setSeatMaps] = useState<SeatMapResponse[]>([])
  const [editorVisible, setEditorVisible] = useState(false)
  const [editingSession, setEditingSession] = useState<ShowSessionDto | null>(null)
  const [editorSaving, setEditorSaving] = useState(false)
  const [editorForm] = Form.useForm()
  // 动态定价配置
  const [pricingVisible, setPricingVisible] = useState(false)
  const [pricingSections, setPricingSections] = useState<{ seatSectionId: number | string; sectionName: string }[]>([])
  const [pricingSaving, setPricingSaving] = useState(false)
  const [pricingForm] = Form.useForm()
  // 基础票价策略维护
  const [strategyVisible, setStrategyVisible] = useState(false)
  const [strategySession, setStrategySession] = useState<ShowSessionDto | null>(null)
  const [strategySections, setStrategySections] = useState<{ seatSectionId: number | string; sectionName: string }[]>([])
  const [strategyRows, setStrategyRows] = useState<PriceStrategyRow[]>([])
  const [strategySaving, setStrategySaving] = useState(false)

  // 加载演出列表
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
    loadShows()
  }, [])

  // 加载座位图（新增/编辑场次选择用）
  useEffect(() => {
    const loadSeatMaps = async () => {
      try {
        const res = await getSeatMapList({ PageSize: 100 })
        if (res.data?.data) {
          setSeatMaps(res.data.data.items || [])
        }
      } catch {
        message.error('加载座位图失败')
      }
    }
    loadSeatMaps()
  }, [])

  // 选择演出后加载场次
  const loadSessions = async (showId: number) => {
    setLoading(true)
    try {
      const res = await getShowSessions(showId)
      if (res.data?.data) {
        setSessions(res.data.data || [])
      }
    } catch {
      message.error('加载场次失败')
    } finally {
      setLoading(false)
    }
  }

  const handleShowChange = (value: number) => {
    setSelectedShowId(value)
    loadSessions(value)
  }

  const handleStatusChange = async (sessionId: number, status: SessionStatus) => {
    try {
      const res = await updateSessionStatus(sessionId, { status })
      if (res.error) {
        message.error('操作失败')
        return
      }
      message.success('状态更新成功')
      if (selectedShowId) {
        loadSessions(selectedShowId)
      }
    } catch {
      message.error('操作失败')
    }
  }

  const handleViewDetail = (session: ShowSessionDto) => {
    setCurrentSession(session)
    setDetailVisible(true)
  }

  // ========== 新增 / 编辑场次 ==========
  const openCreateEditor = () => {
    if (!selectedShowId) {
      message.warning('请先选择演出')
      return
    }
    setEditingSession(null)
    editorForm.resetFields()
    setEditorVisible(true)
  }

  const openEditEditor = (session: ShowSessionDto) => {
    setEditingSession(session)
    editorForm.setFieldsValue({
      seatMapId: Number(session.seatMapId),
      time: [dayjs(session.startTime), dayjs(session.endTime)],
      saleTime: [dayjs(session.saleStartTime), dayjs(session.saleEndTime)],
    })
    setEditorVisible(true)
  }

  const handleEditorSubmit = async () => {
    try {
      const values = await editorForm.validateFields()
      const payload = {
        startTime: (values.time[0] as dayjs.Dayjs).toISOString(),
        endTime: (values.time[1] as dayjs.Dayjs).toISOString(),
        saleStartTime: (values.saleTime[0] as dayjs.Dayjs).toISOString(),
        saleEndTime: (values.saleTime[1] as dayjs.Dayjs).toISOString(),
        seatMapId: Number(values.seatMapId),
      }
      setEditorSaving(true)
      const res = editingSession
        ? await updateSession(Number(editingSession.sessionId), payload)
        : selectedShowId
          ? await addSession(Number(selectedShowId), payload)
          : null
      // 用 HTTP 状态判断成败：openapi-fetch 对无 JSON 体的 404 会把 error 置为空字符串，
      // 若只判 res.error 会漏判并误报“成功”，故这里看 res.response.ok
      if (!res || !res.response?.ok) {
        message.error(editingSession ? '编辑失败' : '新增失败')
        return
      }
      message.success(editingSession ? '场次更新成功' : '场次创建成功')
      setEditorVisible(false)
      if (selectedShowId) {
        loadSessions(selectedShowId)
      }
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) return
      message.error('保存失败')
    } finally {
      setEditorSaving(false)
    }
  }

  const openPricing = async (session: ShowSessionDto) => {
    setCurrentSession(session)
    pricingForm.resetFields()
    setPricingVisible(true)
    setPricingSections([])
    try {
      const res = await getSeatSections(Number(session.seatMapId), { PageSize: 100 })
      if (res.data?.data) {
        setPricingSections(
          (res.data.data.items || []).map(s => ({
            seatSectionId: s.seatSectionId,
            sectionName: s.sectionName || `票区#${s.seatSectionId}`,
          })),
        )
      }
    } catch {
      message.error('加载票区失败')
    }
  }

  const handleSavePricing = async () => {
    if (!currentSession) return
    try {
      const values = await pricingForm.validateFields()
      const rules: CreateDynamicPricingRuleRequest[] = (values.rules || []).map((r: Record<string, unknown>) => ({
        ruleName: String(r.ruleName).trim(),
        triggerType: String(r.triggerType),
        startOffsetMinutes: r.startOffsetMinutes == null || r.startOffsetMinutes === '' ? null : Number(r.startOffsetMinutes),
        endOffsetMinutes: r.endOffsetMinutes == null || r.endOffsetMinutes === '' ? null : Number(r.endOffsetMinutes),
        adjustmentType: String(r.adjustmentType),
        adjustmentValue: Number(r.adjustmentValue),
        priority: Number(r.priority ?? 0),
        seatSectionId: r.seatSectionId == null ? null : Number(r.seatSectionId),
      }))
      // 对齐后端校验：时间窗口为“开演前分钟数”，须满足起始 >= 结束
      const invalid = rules.find(
        r => r.startOffsetMinutes != null && r.endOffsetMinutes != null && r.startOffsetMinutes < r.endOffsetMinutes,
      )
      if (invalid) {
        message.error(`规则「${invalid.ruleName}」时间窗口无效：起始偏移(${invalid.startOffsetMinutes}) 必须大于等于结束偏移(${invalid.endOffsetMinutes})`)
        return
      }
      setPricingSaving(true)
      const res = await configureDynamicPricingRules(Number(currentSession.sessionId), rules)
      if (res.error) {
        message.error('保存失败')
        return
      }
      message.success('动态定价规则已保存（整批覆盖）')
      setPricingVisible(false)
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) return
      message.error('保存失败')
    } finally {
      setPricingSaving(false)
    }
  }

  const handleClearPricing = async () => {
    if (!currentSession) return
    setPricingSaving(true)
    try {
      const res = await configureDynamicPricingRules(Number(currentSession.sessionId), [])
      if (res.error) {
        message.error('清空失败')
        return
      }
      message.success('已清空该场次动态定价规则')
      setPricingVisible(false)
    } catch {
      message.error('清空失败')
    } finally {
      setPricingSaving(false)
    }
  }

  // ========== 基础票价策略维护 ==========
  const openPriceStrategies = async (session: ShowSessionDto) => {
    setStrategySession(session)
    setStrategyVisible(true)
    setStrategySaving(false)
    try {
      const [sectionRes, listRes] = await Promise.all([
        getSeatSections(Number(session.seatMapId), { PageSize: 100 }),
        getAdminPricingStrategies(Number(session.sessionId)),
      ])
      const sections = sectionRes.data?.data?.items || []
      const sectionIds = new Set(sections.map(s => Number(s.seatSectionId)))
      setStrategySections(sections.map(s => ({ seatSectionId: s.seatSectionId, sectionName: s.sectionName || `票区#${s.seatSectionId}` })))
      const list = listRes.data?.data || []
      // 只加载“票区属于该场次当前座位图”的档位；历史订单引用的旧票区档位（其他座位图）
      // 已不属于本场次，不参与编辑/回存，避免把脏数据再次提交给后端。
      const rows: PriceStrategyRow[] = list
        .filter(item => sectionIds.has(Number(item.seatSectionId)))
        .map(item => ({
        priceStrategyId: Number(item.priceStrategyId),
        seatSectionId: Number(item.seatSectionId),
        priceType: item.priceType as PriceType,
        price: Number(item.price),
        strategyName: item.strategyName,
        saleWindow: [item.saleStartTime ? dayjs(item.saleStartTime) : null, item.saleEndTime ? dayjs(item.saleEndTime) : null],
        enabled: item.status === 'ENABLED',
        priority: Number(item.priority || 0),
      }))
      setStrategyRows(rows.length > 0
        ? rows
        : [{ priceType: 'STANDARD', price: 0, saleWindow: null, enabled: true, priority: 0 }])
    } catch {
      message.error('加载票价策略失败')
      setStrategyRows([{ priceType: 'STANDARD', price: 0, saleWindow: null, enabled: true, priority: 0 }])
    }
  }

  const updateStrategyRow = <K extends keyof PriceStrategyRow>(index: number, key: K, value: PriceStrategyRow[K]) => {
    setStrategyRows(rows => rows.map((row, i) => (i === index ? { ...row, [key]: value } : row)))
  }

  const handleSavePriceStrategies = async () => {
    if (!strategySession) return
    if (strategyRows.some(row => !row.seatSectionId || row.priceType == null || row.price == null)) {
      message.warning('请为每个票价档位选择票区、票种并填写价格')
      return
    }
    setStrategySaving(true)
    try {
      const payload = strategyRows.map(row => ({
        seatSectionId: Number(row.seatSectionId),
        priceType: row.priceType,
        price: Number(row.price),
        strategyName: row.strategyName?.trim() || `${row.priceType}-${row.price}`,
        saleStartTime: row.saleWindow?.[0] ? row.saleWindow[0].toISOString() : null,
        saleEndTime: row.saleWindow?.[1] ? row.saleWindow[1].toISOString() : null,
        priority: Number(row.priority || 0),
        status: (row.enabled ? 'ENABLED' : 'DISABLED') as 'ENABLED' | 'DISABLED',
      }))
      const res = await addPricingStrategies(Number(strategySession.sessionId), payload)
      if (!res.response?.ok) {
        message.error('保存票价策略失败')
        return
      }
      message.success('票价策略已保存（整批覆盖）')
      setStrategyVisible(false)
    } catch {
      message.error('保存票价策略失败')
    } finally {
      setStrategySaving(false)
    }
  }

  const columns = [
    {
      title: '场次ID',
      dataIndex: 'sessionId',
      key: 'sessionId',
      width: 100,
    },
    {
      title: '演出开始时间',
      dataIndex: 'startTime',
      key: 'startTime',
      width: 180,
      render: (time: string) => time ? new Date(time).toLocaleString('zh-CN') : '-',
    },
    {
      title: '演出结束时间',
      dataIndex: 'endTime',
      key: 'endTime',
      width: 180,
      render: (time: string) => time ? new Date(time).toLocaleString('zh-CN') : '-',
    },
    {
      title: '售票开始时间',
      dataIndex: 'saleStartTime',
      key: 'saleStartTime',
      width: 180,
      render: (time: string) => time ? new Date(time).toLocaleString('zh-CN') : '-',
    },
    {
      title: '售票结束时间',
      dataIndex: 'saleEndTime',
      key: 'saleEndTime',
      width: 180,
      render: (time: string) => time ? new Date(time).toLocaleString('zh-CN') : '-',
    },
    {
      title: '座位图ID',
      dataIndex: 'seatMapId',
      key: 'seatMapId',
      width: 100,
    },
    {
      title: '状态',
      dataIndex: 'sessionStatus',
      key: 'sessionStatus',
      width: 100,
      render: (status: SessionStatus) => {
        const s = sessionStatusMap[status]
        return s ? <Tag color={s.color}>{s.text}</Tag> : status
      },
    },
    {
      title: '操作',
      key: 'action',
      width: 200,
      render: (_: unknown, record: ShowSessionDto) => (
        <Space>
          <Button type="link" size="small" onClick={() => openEditEditor(record)}>
            编辑
          </Button>
          <Button type="link" size="small" onClick={() => handleViewDetail(record)}>
            详情
          </Button>
          <Button type="link" size="small" onClick={() => openPriceStrategies(record)}>
            票价策略
          </Button>
          <Button type="link" size="small" onClick={() => openPricing(record)}>
            动态定价
          </Button>
          {(record.sessionStatus === 'UPCOMING' || record.sessionStatus === 'PRESALE') && (
            <Button
              type="link"
              size="small"
              onClick={() => handleStatusChange(Number(record.sessionId), 'ONSALE')}
            >
              上架
            </Button>
          )}
          {record.sessionStatus === 'ONSALE' && (
            <Button
              type="link"
              size="small"
              danger
              onClick={() => handleStatusChange(Number(record.sessionId), 'ENDED')}
            >
              结束
            </Button>
          )}
        </Space>
      ),
    },
  ]

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Space>
          <span>选择演出：</span>
          <Select
            placeholder="请选择演出"
            value={selectedShowId}
            onChange={handleShowChange}
            style={{ width: 300 }}
            showSearch
            optionFilterProp="children"
          >
            {shows.map(show => (
              <Select.Option key={show.showId} value={Number(show.showId)}>
                {show.showName}
              </Select.Option>
            ))}
          </Select>
          <Button type="primary" onClick={openCreateEditor} disabled={!selectedShowId}>
            新增场次
          </Button>
        </Space>
      </div>

      <Table
        columns={columns}
        dataSource={sessions}
        rowKey="sessionId"
        loading={loading}
        pagination={false}
        locale={{ emptyText: selectedShowId ? '暂无场次数据' : '请先选择演出' }}
      />

      <Modal
        title={editingSession ? `编辑场次（ID: ${editingSession.sessionId}）` : '新增场次'}
        open={editorVisible}
        onCancel={() => setEditorVisible(false)}
        onOk={handleEditorSubmit}
        confirmLoading={editorSaving}
        okText="保存"
        cancelText="取消"
        width={600}
      >
        <Form form={editorForm} layout="vertical" style={{ marginTop: 16 }}>
          <Form.Item
            label="座位图"
            name="seatMapId"
            rules={[{ required: true, message: '请选择座位图' }]}
            tooltip="选择场馆座位图（同一座位图在同一时段不可重复排期）"
          >
            <Select
              showSearch
              optionFilterProp="children"
              placeholder="请选择座位图"
            >
              {seatMaps.map(map => (
                <Select.Option key={map.seatMapId} value={Number(map.seatMapId)}>
                  {map.venueName} / {map.mapName}
                </Select.Option>
              ))}
            </Select>
          </Form.Item>
          <Form.Item
            label="演出时间"
            name="time"
            rules={[{ required: true, message: '请选择演出开始和结束时间' }]}
          >
            <DatePicker.RangePicker
              showTime
              style={{ width: '100%' }}
              placeholder={['演出开始时间', '演出结束时间']}
              format="YYYY-MM-DD HH:mm"
            />
          </Form.Item>
          <Form.Item
            label="售票时间"
            name="saleTime"
            rules={[{ required: true, message: '请选择售票开始和结束时间' }]}
          >
            <DatePicker.RangePicker
              showTime
              style={{ width: '100%' }}
              placeholder={['售票开始时间', '售票结束时间']}
              format="YYYY-MM-DD HH:mm"
            />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="场次详情"
        open={detailVisible}
        onCancel={() => setDetailVisible(false)}
        footer={[
          <Button key="close" onClick={() => setDetailVisible(false)}>
            关闭
          </Button>,
        ]}
      >
        {currentSession && (
          <Descriptions column={1} bordered size="small">
            <Descriptions.Item label="场次ID">{currentSession.sessionId}</Descriptions.Item>
            <Descriptions.Item label="演出ID">{currentSession.showId}</Descriptions.Item>
            <Descriptions.Item label="开始时间">
              {currentSession.startTime ? new Date(currentSession.startTime).toLocaleString('zh-CN') : '-'}
            </Descriptions.Item>
            <Descriptions.Item label="结束时间">
              {currentSession.endTime ? new Date(currentSession.endTime).toLocaleString('zh-CN') : '-'}
            </Descriptions.Item>
            <Descriptions.Item label="售票开始时间">
              {currentSession.saleStartTime ? new Date(currentSession.saleStartTime).toLocaleString('zh-CN') : '-'}
            </Descriptions.Item>
            <Descriptions.Item label="售票结束时间">
              {currentSession.saleEndTime ? new Date(currentSession.saleEndTime).toLocaleString('zh-CN') : '-'}
            </Descriptions.Item>
            <Descriptions.Item label="座位图ID">{currentSession.seatMapId}</Descriptions.Item>
            <Descriptions.Item label="状态">
              {(() => {
                const s = sessionStatusMap[currentSession.sessionStatus]
                return s ? <Tag color={s.color}>{s.text}</Tag> : currentSession.sessionStatus
              })()}
            </Descriptions.Item>
          </Descriptions>
        )}
      </Modal>

      <Modal
        title="动态定价规则"
        open={pricingVisible}
        onCancel={() => setPricingVisible(false)}
        width={880}
        footer={
          <Space>
            <Popconfirm
              title="确定清空该场次的全部动态定价规则吗？"
              onConfirm={handleClearPricing}
              okText="确定"
              cancelText="取消"
            >
              <Button danger loading={pricingSaving}>
                清空全部规则
              </Button>
            </Popconfirm>
            <Button onClick={() => setPricingVisible(false)}>取消</Button>
            <Button type="primary" loading={pricingSaving} onClick={handleSavePricing}>
              保存（整批覆盖）
            </Button>
          </Space>
        }
      >
        <div style={{ marginBottom: 12 }}>
          <Tag color="gold">提示</Tag> 本接口为整批覆盖：保存时将替换该场次全部动态定价规则（传空数组即清空）。请先填写需要保留的全部规则。
          <br />
          <Tag color="blue">时间窗口</Tag> 偏移为“距开演前分钟数”，须满足 起始偏移 ≥ 结束偏移，例：开演前 120~30 分钟 → 起始 120、结束 30（120=开场前 2 小时）。
          <br />
          <Tag color="warning">INVENTORY_RATE</Tag> 触发类型当前版本评估恒为 false，建议使用 <Tag color="processing">TIME_WINDOW</Tag>。
        </div>
        <Form form={pricingForm}>
          <Form.List name="rules">
            {(fields, { add, remove }) => (
              <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                {fields.map(field => (
                  <div
                    key={field.key}
                    style={{ display: 'flex', gap: 8, alignItems: 'flex-start', flexWrap: 'wrap', border: '1px solid #f0f0f0', padding: 8, borderRadius: 4 }}
                  >
                    <Form.Item name={[field.name, 'ruleName']} rules={[{ required: true, message: '规则名必填' }]} style={{ marginBottom: 0, width: 140 }}>
                      <Input placeholder="规则名" />
                    </Form.Item>
                    <Form.Item name={[field.name, 'triggerType']} rules={[{ required: true, message: '必选' }]} style={{ marginBottom: 0, width: 120 }}>
                      <Select
                        placeholder="触发类型"
                        options={[
                          { value: 'TIME_WINDOW', label: '时间窗口' },
                          { value: 'INVENTORY_RATE', label: '库存比例(未生效)' },
                        ]}
                      />
                    </Form.Item>
                    <Form.Item name={[field.name, 'startOffsetMinutes']} style={{ marginBottom: 0, width: 110 }}>
                      <InputNumber placeholder="起始偏移(分)" min={-10080} max={10080} style={{ width: '100%' }} />
                    </Form.Item>
                    <Form.Item name={[field.name, 'endOffsetMinutes']} style={{ marginBottom: 0, width: 110 }}>
                      <InputNumber placeholder="结束偏移(分)" min={-10080} max={10080} style={{ width: '100%' }} />
                    </Form.Item>
                    <Form.Item name={[field.name, 'adjustmentType']} rules={[{ required: true, message: '必选' }]} style={{ marginBottom: 0, width: 120 }}>
                      <Select
                        placeholder="调整方式"
                        options={[
                          { value: 'DISCOUNT_RATE', label: '折扣率' },
                          { value: 'AMOUNT_OFF', label: '立减金额' },
                          { value: 'FIXED_PRICE', label: '固定价' },
                        ]}
                      />
                    </Form.Item>
                    <Form.Item name={[field.name, 'adjustmentValue']} rules={[{ required: true, message: '必填' }]} style={{ marginBottom: 0, width: 100 }}>
                      <InputNumber placeholder="调整值" style={{ width: '100%' }} />
                    </Form.Item>
                    <Form.Item name={[field.name, 'priority']} style={{ marginBottom: 0, width: 80 }}>
                      <InputNumber placeholder="优先级" min={0} style={{ width: '100%' }} />
                    </Form.Item>
                    <Form.Item name={[field.name, 'seatSectionId']} style={{ marginBottom: 0, width: 160 }}>
                      <Select placeholder="适用票区(全部)" allowClear options={pricingSections.map(s => ({ value: Number(s.seatSectionId), label: s.sectionName }))} />
                    </Form.Item>
                    <Button type="text" danger onClick={() => remove(field.name)}>
                      删除
                    </Button>
                  </div>
                ))}
                <Button type="dashed" onClick={() => add({})} block>
                  + 添加规则
                </Button>
              </div>
            )}
          </Form.List>
        </Form>
      </Modal>

      <Modal
        title={strategySession ? `基础票价策略（场次 ID: ${strategySession.sessionId}）` : '基础票价策略'}
        open={strategyVisible}
        onCancel={() => setStrategyVisible(false)}
        width={1180}
        footer={
          <Space>
            <Button onClick={() => setStrategyVisible(false)}>取消</Button>
            <Button type="primary" loading={strategySaving} onClick={handleSavePriceStrategies}>
              保存（整批覆盖）
            </Button>
          </Space>
        }
      >
        <div style={{ marginBottom: 12 }}>
          <Tag color="gold">提示</Tag> 保存将替换该场次的全部基础票价档位。<Tag color="blue">时间票种</Tag>：同一票区同一时刻只生效一档——档位填写售票时间后按时间自动切换（早鸟→预售→标准），不填则整场生效；重叠时按优先级与票种序取档。
        </div>
        {strategyRows.map((row, index) => (
          <div
            key={index}
            style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap', border: '1px solid #f0f0f0', padding: 8, borderRadius: 4, marginBottom: 8 }}
          >
            <Select
              placeholder="票区"
              value={row.seatSectionId}
              onChange={v => updateStrategyRow(index, 'seatSectionId', v)}
              style={{ width: 160 }}
              options={strategySections.map(s => ({ value: Number(s.seatSectionId), label: s.sectionName }))}
            />
            <Select
              placeholder="票种"
              value={row.priceType}
              onChange={v => updateStrategyRow(index, 'priceType', v)}
              style={{ width: 120 }}
              options={priceTypeOptions}
            />
            <InputNumber
              placeholder="价格"
              min={0}
              value={row.price}
              onChange={v => updateStrategyRow(index, 'price', v ?? 0)}
              style={{ width: 120 }}
              prefix="¥"
            />
            <Input
              placeholder="策略名（可选）"
              value={row.strategyName}
              onChange={e => updateStrategyRow(index, 'strategyName', e.target.value)}
              style={{ width: 160 }}
            />
            <DatePicker.RangePicker
              showTime
              value={row.saleWindow || undefined}
              onChange={dates => updateStrategyRow(index, 'saleWindow', dates as PriceStrategyRow['saleWindow'])}
              placeholder={['档位开售', '档位截止']}
              style={{ width: 300 }}
              format="YYYY-MM-DD HH:mm"
              allowEmpty={[true, true]}
            />
            <Space size={4}>
              <Switch checked={row.enabled} onChange={v => updateStrategyRow(index, 'enabled', v)} />
              <span style={{ color: '#888', fontSize: 13 }}>{row.enabled ? '启用' : '停用'}</span>
            </Space>
            <Button type="text" danger disabled={strategyRows.length <= 1} onClick={() => setStrategyRows(rows => rows.filter((_, i) => i !== index))}>
              删除
            </Button>
          </div>
        ))}
        <Button
          type="dashed"
          block
          onClick={() => setStrategyRows(rows => [...rows, { priceType: 'STANDARD', price: 0, saleWindow: null, enabled: true, priority: 0 }])}
        >
          + 添加票价档
        </Button>
      </Modal>
    </div>
  )
}

export default Session
