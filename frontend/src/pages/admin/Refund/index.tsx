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
  getRefundList,
  getRefundDetail,
  approveRefund,
  rejectRefund,
  type RefundSummary,
  type RefundDetail,
  type RefundType,
  type RefundApproveStatus,
  type RefundStatus,
} from '../../../api/admin'

const refundTypeMap: Record<RefundType, { text: string; color: string }> = {
  FULL: { text: '全额退', color: 'error' },
  PART: { text: '部分退', color: 'warning' },
}

const approveStatusMap: Record<RefundApproveStatus, { text: string; color: string }> = {
  PENDING: { text: '待审核', color: 'warning' },
  APPROVED: { text: '已通过', color: 'success' },
  REJECTED: { text: '已驳回', color: 'error' },
}

const refundStatusMap: Record<RefundStatus, { text: string; color: string }> = {
  PENDING: { text: '申请中', color: 'default' },
  PROCESSING: { text: '退款中', color: 'processing' },
  COMPLETED: { text: '已完成', color: 'success' },
  FAILED: { text: '失败', color: 'error' },
}

const Refund = () => {
  const [data, setData] = useState<RefundSummary[]>([])
  const [loading, setLoading] = useState(false)
  const [approveFilter, setApproveFilter] = useState<RefundApproveStatus | undefined>()
  const [refundStatusFilter, setRefundStatusFilter] = useState<RefundStatus | undefined>()
  const [refundNo, setRefundNo] = useState('')
  const [orderId, setOrderId] = useState('')
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10, total: 0 })
  const [detailVisible, setDetailVisible] = useState(false)
  const [detail, setDetail] = useState<RefundDetail | null>(null)
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
      const res = await getRefundList({
        ApproveStatus: approveFilter,
        RefundStatus: refundStatusFilter,
        RefundNo: refundNo || undefined,
        OrderId: orderId ? Number(orderId) : undefined,
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
      message.error('加载退票申请失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadData(1)
  }, [])

  const handleViewDetail = async (refundId: number) => {
    setDetailVisible(true)
    setDetailLoading(true)
    setDetail(null)
    try {
      const res = await getRefundDetail(refundId)
      if (res.data?.data) {
        setDetail(res.data.data)
      }
    } catch {
      message.error('加载退票详情失败')
    } finally {
      setDetailLoading(false)
    }
  }

  const openReview = (action: 'approve' | 'reject', refundId: number) => {
    setReviewAction(action)
    setReviewTargetId(refundId)
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
          ? await approveRefund(reviewTargetId, reviewRemark || null)
          : await rejectRefund(reviewTargetId, reviewRemark.trim())
      if (res.error) {
        message.error('操作失败')
        return
      }
      message.success(reviewAction === 'approve' ? '退票申请已通过' : '退票申请已驳回')
      setReviewVisible(false)
      loadData(pagination.current, pagination.pageSize)
      if (detailVisible && detail) {
        handleViewDetail(Number(detail.refundId))
      }
    } catch {
      message.error(reviewAction === 'approve' ? '通过失败' : '驳回失败')
    } finally {
      setReviewLoading(false)
    }
  }

  const columns = [
    {
      title: '退票单号',
      dataIndex: 'refundNo',
      key: 'refundNo',
      width: 180,
    },
    {
      title: '订单ID',
      dataIndex: 'orderId',
      key: 'orderId',
      width: 90,
    },
    {
      title: '退票类型',
      dataIndex: 'refundType',
      key: 'refundType',
      width: 100,
      render: (t: RefundType) => {
        const m = refundTypeMap[t]
        return m ? <Tag color={m.color}>{m.text}</Tag> : t
      },
    },
    {
      title: '实退金额',
      dataIndex: 'actualRefund',
      key: 'actualRefund',
      width: 100,
      render: (v: number | string | null) => (v == null ? '-' : `¥${v}`),
    },
    {
      title: '审核状态',
      dataIndex: 'approveStatus',
      key: 'approveStatus',
      width: 100,
      render: (s: RefundApproveStatus) => {
        const m = approveStatusMap[s]
        return m ? <Tag color={m.color}>{m.text}</Tag> : s
      },
    },
    {
      title: '退款状态',
      dataIndex: 'refundStatus',
      key: 'refundStatus',
      width: 100,
      render: (s: RefundStatus) => {
        const m = refundStatusMap[s]
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
      render: (_: unknown, record: RefundSummary) => (
        <Space>
          <Button type="link" size="small" onClick={() => handleViewDetail(Number(record.refundId))}>
            详情
          </Button>
          {record.approveStatus === 'PENDING' && (
            <>
              <Button type="link" size="small" onClick={() => openReview('approve', Number(record.refundId))}>
                通过
              </Button>
              <Button type="link" size="small" danger onClick={() => openReview('reject', Number(record.refundId))}>
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
          <span>退款状态：</span>
          <Select
            placeholder="全部"
            value={refundStatusFilter}
            onChange={setRefundStatusFilter}
            style={{ width: 120 }}
            allowClear
          >
            {Object.entries(refundStatusMap).map(([key, val]) => (
              <Select.Option key={key} value={key}>{val.text}</Select.Option>
            ))}
          </Select>
          <Input
            placeholder="退票单号"
            value={refundNo}
            onChange={e => setRefundNo(e.target.value)}
            style={{ width: 180 }}
            allowClear
            onPressEnter={() => loadData(1)}
          />
          <Input
            placeholder="订单ID"
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
        rowKey="refundId"
        loading={loading}
        pagination={{
          ...pagination,
          showSizeChanger: true,
          showTotal: total => `共 ${total} 条`,
          onChange: (page, pageSize) => loadData(page, pageSize),
        }}
      />

      <Modal
        title="退票申请详情"
        open={detailVisible}
        onCancel={() => setDetailVisible(false)}
        width={760}
        footer={[
          detail?.approveStatus === 'PENDING' && (
            <>
              <Button key="reject" danger onClick={() => openReview('reject', Number(detail.refundId))}>
                驳回
              </Button>
              <Button key="approve" type="primary" onClick={() => openReview('approve', Number(detail.refundId))}>
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
            <Descriptions.Item label="退票单号">{detail.refundNo}</Descriptions.Item>
            <Descriptions.Item label="订单ID">{detail.orderId}</Descriptions.Item>
            <Descriptions.Item label="用户ID">{detail.userId}</Descriptions.Item>
            <Descriptions.Item label="退票类型">
              {(() => {
                const m = refundTypeMap[detail.refundType]
                return m ? <Tag color={m.color}>{m.text}</Tag> : detail.refundType
              })()}
            </Descriptions.Item>
            <Descriptions.Item label="申请原因">{detail.refundReason || '-'}</Descriptions.Item>
            <Descriptions.Item label="适用策略">{detail.policyName || '-'}</Descriptions.Item>
            <Descriptions.Item label="退款金额">¥{detail.refundAmount}</Descriptions.Item>
            <Descriptions.Item label="手续费率">{(Number(detail.feeRate) * 100).toFixed(0)}%</Descriptions.Item>
            <Descriptions.Item label="服务费">¥{detail.appliedServiceFee}</Descriptions.Item>
            <Descriptions.Item label="实际退款">{detail.actualRefund == null ? '-' : `¥${detail.actualRefund}`}</Descriptions.Item>
            <Descriptions.Item label="审核状态">
              {(() => {
                const m = approveStatusMap[detail.approveStatus]
                return m ? <Tag color={m.color}>{m.text}</Tag> : detail.approveStatus
              })()}
            </Descriptions.Item>
            <Descriptions.Item label="退款状态">
              {(() => {
                const m = refundStatusMap[detail.refundStatus]
                return m ? <Tag color={m.color}>{m.text}</Tag> : detail.refundStatus
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
        title={reviewAction === 'approve' ? '通过退票申请' : '驳回退票申请'}
        open={reviewVisible}
        onCancel={() => setReviewVisible(false)}
        onOk={handleReview}
        confirmLoading={reviewLoading}
        okText={reviewAction === 'approve' ? '确认通过' : '确认驳回'}
        okButtonProps={{ danger: reviewAction === 'reject' }}
      >
        <p style={{ marginTop: 0 }}>
          {reviewAction === 'approve'
            ? '确认通过该退票申请？系统将按策略执行退款。'
            : '驳回退票申请必须填写备注，用于告知用户原因。'}
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

export default Refund
