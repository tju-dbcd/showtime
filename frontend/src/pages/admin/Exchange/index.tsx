import { useEffect, useState } from 'react'
import {
  Table,
  Select,
  Button,
  Space,
  Tag,
  Modal,
  Descriptions,
  Input,
  message,
} from 'antd'
import {
  getExchangeList,
  getExchangeDetail,
  approveExchange,
  rejectExchange,
  type ExchangeSummary,
  type ExchangeDetail,
  type ExchangeApproveStatus,
  type ExchangeStatus,
} from '../../../api/admin'

const approveStatusMap: Record<ExchangeApproveStatus, { text: string; color: string }> = {
  PENDING: { text: '待审核', color: 'warning' },
  APPROVED: { text: '已通过', color: 'success' },
  REJECTED: { text: '已驳回', color: 'error' },
}

const exchangeStatusMap: Record<ExchangeStatus, { text: string; color: string }> = {
  PENDING: { text: '申请中', color: 'default' },
  PROCESSING: { text: '换票中', color: 'processing' },
  COMPLETED: { text: '已完成', color: 'success' },
  FAILED: { text: '失败', color: 'error' },
}

const Exchange = () => {
  const [data, setData] = useState<ExchangeSummary[]>([])
  const [loading, setLoading] = useState(false)
  const [approveFilter, setApproveFilter] = useState<ExchangeApproveStatus | undefined>()
  const [exchangeStatusFilter, setExchangeStatusFilter] = useState<ExchangeStatus | undefined>()
  const [exchangeNo, setExchangeNo] = useState('')
  const [orderId, setOrderId] = useState('')
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10, total: 0 })
  const [detailVisible, setDetailVisible] = useState(false)
  const [detail, setDetail] = useState<ExchangeDetail | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)
  // 审核弹窗：action = 'approve' | 'reject'
  const [reviewVisible, setReviewVisible] = useState(false)
  const [reviewAction, setReviewAction] = useState<'approve' | 'reject'>('approve')
  const [reviewTargetId, setReviewTargetId] = useState(0)
  const [reviewRemark, setReviewRemark] = useState('')
  const [reviewLoading, setReviewLoading] = useState(false)

  const loadData = async (page = 1, pageSize = 10) => {
    setLoading(true)
    try {
      const res = await getExchangeList({
        ApproveStatus: approveFilter,
        ExchangeStatus: exchangeStatusFilter,
        ExchangeNo: exchangeNo || undefined,
        OriginalOrderId: orderId ? Number(orderId) : undefined,
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
      message.error('加载改签申请失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadData(1)
  }, [])

  const handleViewDetail = async (exchangeId: number) => {
    setDetailVisible(true)
    setDetailLoading(true)
    setDetail(null)
    try {
      const res = await getExchangeDetail(exchangeId)
      if (res.data?.data) {
        setDetail(res.data.data)
      }
    } catch {
      message.error('加载改签详情失败')
    } finally {
      setDetailLoading(false)
    }
  }

  const openReview = (action: 'approve' | 'reject', exchangeId: number) => {
    setReviewAction(action)
    setReviewTargetId(exchangeId)
    setReviewRemark('')
    setReviewVisible(true)
  }

  const handleReview = async () => {
    if (reviewAction === 'reject' && !reviewRemark.trim()) {
      message.warning('驳回时必须填写备注')
      return
    }
    setReviewLoading(true)
    try {
      const res =
        reviewAction === 'approve'
          ? await approveExchange(reviewTargetId, reviewRemark || null)
          : await rejectExchange(reviewTargetId, reviewRemark.trim())
      if (res.error) {
        message.error('操作失败')
        return
      }
      message.success(reviewAction === 'approve' ? '改签申请已通过' : '改签申请已驳回')
      setReviewVisible(false)
      loadData(pagination.current, pagination.pageSize)
      if (detailVisible && detail) {
        handleViewDetail(Number(detail.exchangeId))
      }
    } catch {
      message.error(reviewAction === 'approve' ? '通过失败' : '驳回失败')
    } finally {
      setReviewLoading(false)
    }
  }

  const columns = [
    {
      title: '改签单号',
      dataIndex: 'exchangeNo',
      key: 'exchangeNo',
      width: 180,
    },
    {
      title: '原订单ID',
      dataIndex: 'originalOrderId',
      key: 'originalOrderId',
      width: 100,
    },
    {
      title: '新订单ID',
      dataIndex: 'childOrderId',
      key: 'childOrderId',
      width: 100,
      render: (v: number | string) => (v || '-'),
    },
    {
      title: '应付差价',
      dataIndex: 'amountDue',
      key: 'amountDue',
      width: 100,
      render: (v: number | string) => `¥${v}`,
    },
    {
      title: '审核状态',
      dataIndex: 'approveStatus',
      key: 'approveStatus',
      width: 100,
      render: (s: ExchangeApproveStatus) => {
        const m = approveStatusMap[s]
        return m ? <Tag color={m.color}>{m.text}</Tag> : s
      },
    },
    {
      title: '换票状态',
      dataIndex: 'exchangeStatus',
      key: 'exchangeStatus',
      width: 100,
      render: (s: ExchangeStatus) => {
        const m = exchangeStatusMap[s]
        return m ? <Tag color={m.color}>{m.text}</Tag> : s
      },
    },
    {
      title: '申请时间',
      dataIndex: 'createTime',
      key: 'createTime',
      width: 170,
      render: (t: string) => (t ? new Date(t).toLocaleString('zh-CN') : '-'),
    },
    {
      title: '完成时间',
      dataIndex: 'completeTime',
      key: 'completeTime',
      width: 170,
      render: (t: string | null) => (t ? new Date(t).toLocaleString('zh-CN') : '-'),
    },
    {
      title: '操作',
      key: 'action',
      width: 180,
      render: (_: unknown, record: ExchangeSummary) => (
        <Space>
          <Button type="link" size="small" onClick={() => handleViewDetail(Number(record.exchangeId))}>
            详情
          </Button>
          {record.approveStatus === 'PENDING' && (
            <>
              <Button type="link" size="small" onClick={() => openReview('approve', Number(record.exchangeId))}>
                通过
              </Button>
              <Button type="link" size="small" danger onClick={() => openReview('reject', Number(record.exchangeId))}>
                驳回
              </Button>
            </>
          )}
        </Space>
      ),
    },
  ]

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Space wrap>
          <span>审核状态：</span>
          <Select
            placeholder="全部"
            value={approveFilter}
            onChange={setApproveFilter}
            style={{ width: 120 }}
            allowClear
          >
            {Object.entries(approveStatusMap).map(([key, val]) => (
              <Select.Option key={key} value={key}>{val.text}</Select.Option>
            ))}
          </Select>
          <span>换票状态：</span>
          <Select
            placeholder="全部"
            value={exchangeStatusFilter}
            onChange={setExchangeStatusFilter}
            style={{ width: 120 }}
            allowClear
          >
            {Object.entries(exchangeStatusMap).map(([key, val]) => (
              <Select.Option key={key} value={key}>{val.text}</Select.Option>
            ))}
          </Select>
          <Input
            placeholder="改签单号"
            value={exchangeNo}
            onChange={e => setExchangeNo(e.target.value)}
            style={{ width: 180 }}
            allowClear
            onPressEnter={() => loadData(1)}
          />
          <Input
            placeholder="原订单ID"
            value={orderId}
            onChange={e => setOrderId(e.target.value)}
            style={{ width: 120 }}
            allowClear
            onPressEnter={() => loadData(1)}
          />
          <Button type="primary" onClick={() => loadData(1)}>
            筛选
          </Button>
        </Space>
      </div>

      <Table
        columns={columns}
        dataSource={data}
        rowKey="exchangeId"
        loading={loading}
        pagination={{
          ...pagination,
          showSizeChanger: true,
          showTotal: total => `共 ${total} 条`,
          onChange: (page, pageSize) => loadData(page, pageSize),
        }}
      />

      <Modal
        title="改签申请详情"
        open={detailVisible}
        onCancel={() => setDetailVisible(false)}
        width={760}
        footer={[
          detail?.approveStatus === 'PENDING' && (
            <>
              <Button key="reject" danger onClick={() => openReview('reject', Number(detail.exchangeId))}>
                驳回
              </Button>
              <Button key="approve" type="primary" onClick={() => openReview('approve', Number(detail.exchangeId))}>
                通过
              </Button>
            </>
          ),
          <Button key="close" onClick={() => setDetailVisible(false)}>
            关闭
          </Button>,
        ].filter(Boolean)}
      >
        {detailLoading ? (
          <div style={{ textAlign: 'center', padding: 40 }}>加载中...</div>
        ) : detail ? (
          <Descriptions column={2} bordered size="small">
            <Descriptions.Item label="改签单号">{detail.exchangeNo}</Descriptions.Item>
            <Descriptions.Item label="用户ID">{detail.userId}</Descriptions.Item>
            <Descriptions.Item label="原订单ID">{detail.originalOrderId}</Descriptions.Item>
            <Descriptions.Item label="新订单ID">{detail.childOrderId || '-'}</Descriptions.Item>
            <Descriptions.Item label="原场次ID">{detail.origSessionId}</Descriptions.Item>
            <Descriptions.Item label="目标场次ID">{detail.targetSessionId}</Descriptions.Item>
            <Descriptions.Item label="申请原因">{detail.reason || '-'}</Descriptions.Item>
            <Descriptions.Item label="适用策略">{detail.policyName || '-'}</Descriptions.Item>
            <Descriptions.Item label="原票抵扣">¥{detail.origDeduction}</Descriptions.Item>
            <Descriptions.Item label="目标金额">¥{detail.targetAmount}</Descriptions.Item>
            <Descriptions.Item label="差价">{Number(detail.priceDiff) >= 0 ? '+' : ''}¥{detail.priceDiff}</Descriptions.Item>
            <Descriptions.Item label="改签费">¥{detail.exchangeFee}</Descriptions.Item>
            <Descriptions.Item label="应付金额">¥{detail.amountDue}</Descriptions.Item>
            <Descriptions.Item label="审核状态">
              {(() => {
                const m = approveStatusMap[detail.approveStatus]
                return m ? <Tag color={m.color}>{m.text}</Tag> : detail.approveStatus
              })()}
            </Descriptions.Item>
            <Descriptions.Item label="换票状态">
              {(() => {
                const m = exchangeStatusMap[detail.exchangeStatus]
                return m ? <Tag color={m.color}>{m.text}</Tag> : detail.exchangeStatus
              })()}
            </Descriptions.Item>
            <Descriptions.Item label="审核人">{detail.reviewBy || '-'}</Descriptions.Item>
            <Descriptions.Item label="审核时间">
              {detail.reviewTime ? new Date(detail.reviewTime).toLocaleString('zh-CN') : '-'}
            </Descriptions.Item>
            <Descriptions.Item label="审核备注" span={2}>{detail.reviewRemark || '-'}</Descriptions.Item>
            <Descriptions.Item label="申请时间">
              {detail.createTime ? new Date(detail.createTime).toLocaleString('zh-CN') : '-'}
            </Descriptions.Item>
            <Descriptions.Item label="完成时间">
              {detail.completeTime ? new Date(detail.completeTime).toLocaleString('zh-CN') : '-'}
            </Descriptions.Item>
          </Descriptions>
        ) : null}
      </Modal>

      <Modal
        title={reviewAction === 'approve' ? '通过改签申请' : '驳回改签申请'}
        open={reviewVisible}
        onCancel={() => setReviewVisible(false)}
        onOk={handleReview}
        confirmLoading={reviewLoading}
        okText={reviewAction === 'approve' ? '确认通过' : '确认驳回'}
        okButtonProps={{ danger: reviewAction === 'reject' }}
      >
        <p style={{ marginTop: 0 }}>
          {reviewAction === 'approve'
            ? '确认通过该改签申请？系统将按策略生成新订单并换票。'
            : '驳回改签申请必须填写备注，用于告知用户原因。'}
        </p>
        <Input.TextArea
          placeholder="备注（驳回时必填）"
          value={reviewRemark}
          onChange={e => setReviewRemark(e.target.value)}
          rows={3}
        />
      </Modal>
    </div>
  )
}

export default Exchange
