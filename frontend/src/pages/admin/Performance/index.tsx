import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
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
  Drawer,
  Descriptions,
  Tabs,
  Empty,
  Spin,
} from 'antd'
import { PlusOutlined, EyeOutlined } from '@ant-design/icons'
import FileUploader from '../../../components/FileUploader'
import {
  getShowList,
  createShow,
  updateShow,
  updateShowAuditStatus,
  deleteShow,
  getCategories,
  getRefundPolicyList,
  getExchangePolicyList,
  getShowSessions,
  getSeatMapList,
  type ShowDto,
  type CreateShowRequest,
  type UpdateShowRequest,
  type ShowStatus,
  type CategoryResponse,
  type RefundPolicy,
  type ExchangePolicy,
  type ShowSessionDto,
  type SeatMapResponse,
} from '../../../api/admin'

const { TextArea } = Input

const statusMap: Record<ShowStatus, { text: string; color: string }> = {
  DRAFT: { text: '草稿', color: 'default' },
  PUBLISHED: { text: '已发布', color: 'success' },
  UNPUBLISHED: { text: '已下架', color: 'warning' },
}

const auditStatusMap: Record<string, { text: string; color: string }> = {
  PENDING: { text: '待审核', color: 'processing' },
  APPROVED: { text: '已通过', color: 'success' },
  REJECTED: { text: '未通过', color: 'error' },
}

const policyStatusMap: Record<number, { text: string; color: string }> = {
  0: { text: '停用', color: 'default' },
  1: { text: '启用', color: 'success' },
}

const sessionStatusMap: Record<string, { text: string; color: string }> = {
  UPCOMING: { text: '未开售', color: 'default' },
  PRESALE: { text: '预售', color: 'blue' },
  ONSALE: { text: '在售', color: 'success' },
  SOLD_OUT: { text: '售罄', color: 'orange' },
  ENDED: { text: '已结束', color: 'default' },
}

const Performance = () => {
  const navigate = useNavigate()
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

  // 演出详情抽屉：展示关联的退票策略/改签策略/场次座位图
  const [detailOpen, setDetailOpen] = useState(false)
  const [detailShow, setDetailShow] = useState<ShowDto | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)
  const [refundPolicies, setRefundPolicies] = useState<RefundPolicy[]>([])
  const [exchangePolicies, setExchangePolicies] = useState<ExchangePolicy[]>([])
  const [showSessions, setShowSessions] = useState<ShowSessionDto[]>([])
  const [seatMaps, setSeatMaps] = useState<SeatMapResponse[]>([])

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

  const handleViewDetail = async (record: ShowDto) => {
    setDetailShow(record)
    setDetailOpen(true)
    setDetailLoading(true)
    try {
      const showId = Number(record.showId)
      const [refundRes, exchangeRes, sessionRes, mapRes] = await Promise.all([
        getRefundPolicyList({ ShowId: showId, Page: 1, PageSize: 100 }),
        getExchangePolicyList({ ShowId: showId, Page: 1, PageSize: 100 }),
        getShowSessions(showId),
        getSeatMapList({ PageSize: 100 }),
      ])
      setRefundPolicies(refundRes.data?.data?.items || [])
      setExchangePolicies(exchangeRes.data?.data?.items || [])
      setShowSessions(sessionRes.data?.data || [])
      setSeatMaps(mapRes.data?.data?.items || [])
    } catch {
      message.error('加载演出详情失败')
    } finally {
      setDetailLoading(false)
    }
  }

  const seatMapNameOf = (seatMapId?: number | string | null): string => {
    if (seatMapId == null) return '未绑定座位图'
    const map = seatMaps.find(m => Number(m.seatMapId) === Number(seatMapId))
    return map ? `${map.mapName}（${map.venueName}）` : `座位图#${seatMapId}`
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

  // ========== 演出状态维护：发布 / 下架 ==========
  const changeShowStatus = async (record: ShowDto, status: ShowStatus) => {
    try {
      const res = await updateShow(Number(record.showId), {
        showName: record.showName,
        categoryId: Number(record.categoryId),
        status,
        description: record.description,
        durationMinutes: record.durationMinutes != null ? Number(record.durationMinutes) : null,
        posterUrl: record.posterUrl,
      })
      if (!res.response?.ok) {
        message.error(status === 'PUBLISHED' ? '发布失败，演出需先通过审核' : '下架失败')
        return
      }
      message.success(status === 'PUBLISHED' ? '演出已发布' : '演出已下架')
      loadData(pagination.current, pagination.pageSize)
    } catch {
      message.error('操作失败')
    }
  }

  // ========== 审核状态维护：通过 / 驳回 ==========
  const changeAuditStatus = async (record: ShowDto, auditStatus: ShowDto['auditStatus']) => {
    try {
      const res = await updateShowAuditStatus(Number(record.showId), { auditStatus })
      if (!res.response?.ok) {
        message.error('审核状态更新失败')
        return
      }
      message.success(auditStatus === 'APPROVED' ? '已审核通过' : '已驳回')
      loadData(pagination.current, pagination.pageSize)
    } catch {
      message.error('操作失败')
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
      title: '审核状态',
      dataIndex: 'auditStatus',
      key: 'auditStatus',
      width: 100,
      render: (audit: ShowDto['auditStatus']) => {
        const s = auditStatusMap[audit]
        return s ? <Tag color={s.color}>{s.text}</Tag> : audit
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
      width: 420,
      render: (_: unknown, record: ShowDto) => (
        <Space size={0} wrap>
          <Button type="link" size="small" icon={<EyeOutlined />} onClick={() => handleViewDetail(record)}>
            详情
          </Button>
          <Button type="link" size="small" onClick={() => handleEdit(record)}>
            编辑
          </Button>
          {record.auditStatus !== 'APPROVED' && (
            <Popconfirm
              title="确认审核通过该演出？"
              okText="确认"
              cancelText="取消"
              onConfirm={() => changeAuditStatus(record, 'APPROVED')}
            >
              <Button type="link" size="small">审核通过</Button>
            </Popconfirm>
          )}
          {record.auditStatus !== 'REJECTED' && (
            <Popconfirm
              title="驳回后已发布演出会自动下架，确认驳回？"
              okText="确认"
              cancelText="取消"
              onConfirm={() => changeAuditStatus(record, 'REJECTED')}
            >
              <Button type="link" size="small" danger>驳回</Button>
            </Popconfirm>
          )}
          {record.status !== 'PUBLISHED' && record.auditStatus === 'APPROVED' && (
            <Popconfirm
              title="确认发布该演出？"
              okText="确认"
              cancelText="取消"
              onConfirm={() => changeShowStatus(record, 'PUBLISHED')}
            >
              <Button type="link" size="small">发布</Button>
            </Popconfirm>
          )}
          {record.status !== 'PUBLISHED' && record.auditStatus !== 'APPROVED' && (
            <Button type="link" size="small" disabled title="需先审核通过后才能发布">发布</Button>
          )}
          {record.status === 'PUBLISHED' && (
            <Popconfirm
              title="确认下架该演出？"
              okText="确认"
              cancelText="取消"
              onConfirm={() => changeShowStatus(record, 'UNPUBLISHED')}
            >
              <Button type="link" size="small">下架</Button>
            </Popconfirm>
          )}
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
          <Form.Item
            label="演出海报"
            name="posterUrl"
            extra="支持 jpg/jpeg/png/webp/gif，不超过 5MB；上传后自动填写海报 URL"
          >
            <FileUploader folder="show" />
          </Form.Item>
          <Form.Item label="演出介绍" name="description">
            <TextArea rows={4} placeholder="请输入演出介绍" maxLength={1000} showCount />
          </Form.Item>
        </Form>
      </Modal>

      {/* 演出详情抽屉：直观查看退票策略/改签策略/场次座位图 */}
      <Drawer
        title={detailShow ? `演出详情：${detailShow.showName}` : '演出详情'}
        open={detailOpen}
        onClose={() => setDetailOpen(false)}
        width={880}
      >
        <Spin spinning={detailLoading}>
        <Tabs
          items={[
            {
              key: 'basic',
              label: '基本信息',
              children: detailShow ? (
                <Descriptions
                  bordered
                  size="small"
                  column={2}
                  style={{ marginTop: 8 }}
                  labelStyle={{ width: 120 }}
                >
                  <Descriptions.Item label="演出ID">{detailShow.showId}</Descriptions.Item>
                  <Descriptions.Item label="演出名称">{detailShow.showName}</Descriptions.Item>
                  <Descriptions.Item label="分类">
                    {(() => {
                      const cat = categories.find(c => Number(c.categoryId) === Number(detailShow.categoryId))
                      return cat ? cat.categoryName : detailShow.categoryId
                    })()}
                  </Descriptions.Item>
                  <Descriptions.Item label="时长">
                    {detailShow.durationMinutes ? `${detailShow.durationMinutes} 分钟` : '-'}
                  </Descriptions.Item>
                  <Descriptions.Item label="状态">
                    {(() => {
                      const s = statusMap[detailShow.status]
                      return s ? <Tag color={s.color}>{s.text}</Tag> : detailShow.status
                    })()}
                  </Descriptions.Item>
                  <Descriptions.Item label="创建时间">
                    {detailShow.createTime ? new Date(detailShow.createTime).toLocaleString('zh-CN') : '-'}
                  </Descriptions.Item>
                  <Descriptions.Item label="海报" span={2}>
                    {detailShow.posterUrl
                      ? <img src={detailShow.posterUrl} alt="海报" style={{ width: 140, borderRadius: 4 }} />
                      : '无海报'}
                  </Descriptions.Item>
                  <Descriptions.Item label="演出介绍" span={2}>
                    <div style={{ whiteSpace: 'pre-wrap' }}>{detailShow.description || '暂无介绍'}</div>
                  </Descriptions.Item>
                </Descriptions>
              ) : null,
            },
            {
              key: 'refund',
              label: `退票策略（${refundPolicies.length}）`,
              children: refundPolicies.length === 0 ? (
                <Empty description="该演出暂无退票策略" style={{ padding: 24 }} />
              ) : (
                <Table
                  size="small"
                  rowKey="policyId"
                  dataSource={refundPolicies}
                  pagination={false}
                  scroll={{ x: 640 }}
                  columns={[
                    { title: '策略名称', dataIndex: 'policyName', key: 'policyName' },
                    {
                      title: '截止小时',
                      dataIndex: 'refundDeadlineHour',
                      key: 'refundDeadlineHour',
                      render: (v: number | string) => `${v}h`,
                    },
                    {
                      title: '退款比例',
                      dataIndex: 'refundRate',
                      key: 'refundRate',
                      render: (v: number | string) => `${(Number(v) * 100).toFixed(0)}%`,
                    },
                    {
                      title: '服务费',
                      dataIndex: 'serviceFee',
                      key: 'serviceFee',
                      render: (v: number | string) => `¥${v}`,
                    },
                    { title: '优先级', dataIndex: 'priority', key: 'priority' },
                    {
                      title: '状态',
                      dataIndex: 'status',
                      key: 'status',
                      render: (s: number) => {
                        const m = policyStatusMap[s]
                        return m ? <Tag color={m.color}>{m.text}</Tag> : s
                      },
                    },
                  ]}
                />
              ),
            },
            {
              key: 'exchange',
              label: `改签策略（${exchangePolicies.length}）`,
              children: exchangePolicies.length === 0 ? (
                <Empty description="该演出暂无改签策略" style={{ padding: 24 }} />
              ) : (
                <Table
                  size="small"
                  rowKey="policyId"
                  dataSource={exchangePolicies}
                  pagination={false}
                  scroll={{ x: 640 }}
                  columns={[
                    { title: '策略名称', dataIndex: 'policyName', key: 'policyName' },
                    {
                      title: '截止小时',
                      dataIndex: 'exchangeDeadlineHour',
                      key: 'exchangeDeadlineHour',
                      render: (v: number | string) => `${v}h`,
                    },
                    {
                      title: '改签费',
                      dataIndex: 'exchangeFee',
                      key: 'exchangeFee',
                      render: (v: number | string) => `¥${v}`,
                    },
                    {
                      title: '跨场次',
                      dataIndex: 'allowCrossSession',
                      key: 'allowCrossSession',
                      render: (v: number | string) => (Number(v) === 1 ? '允许' : '不允许'),
                    },
                    { title: '优先级', dataIndex: 'priority', key: 'priority' },
                    {
                      title: '状态',
                      dataIndex: 'status',
                      key: 'status',
                      render: (s: number) => {
                        const m = policyStatusMap[s]
                        return m ? <Tag color={m.color}>{m.text}</Tag> : s
                      },
                    },
                  ]}
                />
              ),
            },
            {
              key: 'seatmap',
              label: `场次座位图（${showSessions.length}）`,
              children: showSessions.length === 0 ? (
                <Empty description="该演出暂无场次" style={{ padding: 24 }} />
              ) : (
                <Table
                  size="small"
                  rowKey="sessionId"
                  dataSource={showSessions}
                  pagination={false}
                  scroll={{ x: 640 }}
                  columns={[
                    {
                      title: '场次时间',
                      key: 'time',
                      render: (_: unknown, record: ShowSessionDto) => (
                        <Space direction="vertical" size={0}>
                          <span>开始：{new Date(record.startTime).toLocaleString('zh-CN')}</span>
                          <span>结束：{new Date(record.endTime).toLocaleString('zh-CN')}</span>
                        </Space>
                      ),
                    },
                    {
                      title: '场次状态',
                      dataIndex: 'sessionStatus',
                      key: 'sessionStatus',
                      render: (v: string) => {
                        const m = sessionStatusMap[v]
                        return m ? <Tag color={m.color}>{m.text}</Tag> : v
                      },
                    },
                    {
                      title: '座位图',
                      key: 'seatmap',
                      render: (_: unknown, record: ShowSessionDto) => seatMapNameOf(record.seatMapId),
                    },
                    {
                      title: '操作',
                      key: 'action',
                      render: () => (
                        <Button type="link" size="small" onClick={() => navigate('/admin/seat-map')}>
                          前往座位图管理
                        </Button>
                      ),
                    },
                  ]}
                />
              ),
            },
          ]}
        />
        </Spin>
      </Drawer>
    </div>
  )
}

export default Performance
