import { useEffect, useState } from 'react'
import {
  Table,
  Select,
  Button,
  Space,
  Tag,
  Modal,
  Descriptions,
  Popconfirm,
  message,
} from 'antd'
import {
  getOrderList,
  getOrderDetail,
  cancelOrder,
} from '../../../api/admin'

const orderStatusMap: Record<string, { text: string; color: string }> = {
  PENDING_PAY: { text: '待支付', color: 'warning' },
  PAID: { text: '已支付', color: 'processing' },
  ISSUED: { text: '已出票', color: 'success' },
  PART_REFUND: { text: '部分退款', color: 'warning' },
  REFUNDED: { text: '已退款', color: 'default' },
  CANCELLED: { text: '已取消', color: 'error' },
}

const itemStatusMap: Record<string, { text: string; color: string }> = {
  PENDING: { text: '待处理', color: 'default' },
  PAID: { text: '已支付', color: 'processing' },
  ISSUED: { text: '已出票', color: 'success' },
  REFUNDED: { text: '已退款', color: 'default' },
  CANCELLED: { text: '已取消', color: 'error' },
}

const Order = () => {
  const [data, setData] = useState<unknown[]>([])
  const [loading, setLoading] = useState(false)
  const [statusFilter, setStatusFilter] = useState<string | undefined>()
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10, total: 0 })
  const [detailVisible, setDetailVisible] = useState(false)
  const [orderDetail, setOrderDetail] = useState<Record<string, unknown> | null>(null)
  const [detailLoading, setDetailLoading] = useState(false)

  const loadData = async (page = 1, pageSize = 10) => {
    setLoading(true)
    try {
      const res = await getOrderList({
        Status: statusFilter,
        Page: page,
        PageSize: pageSize,
      })
      if (res.data) {
        const result = (res.data as Record<string, unknown>).data || res.data
        const r = result as Record<string, unknown>
        setData((r?.items as unknown[]) || (Array.isArray(result) ? result : []) || [])
        setPagination({
          current: page,
          pageSize,
          total: (r?.totalCount as number) || 0,
        })
      }
    } catch {
      message.error('加载订单列表失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadData(1)
  }, [])

  const handleViewDetail = async (orderId: number) => {
    setDetailVisible(true)
    setDetailLoading(true)
    try {
      const res = await getOrderDetail(orderId)
      if (res.data) {
        setOrderDetail(((res.data as Record<string, unknown>).data || res.data) as Record<string, unknown>)
      }
    } catch {
      message.error('加载订单详情失败')
    } finally {
      setDetailLoading(false)
    }
  }

  const handleCancel = async (orderId: number) => {
    try {
      const res = await cancelOrder(orderId)
      if (res.error) {
        message.error('取消订单失败')
        return
      }
      message.success('订单已取消')
      loadData(pagination.current, pagination.pageSize)
    } catch {
      message.error('取消订单失败')
    }
  }

  const columns = [
    {
      title: '订单号',
      dataIndex: 'orderNo',
      key: 'orderNo',
      width: 200,
    },
    {
      title: '场次ID',
      dataIndex: 'sessionId',
      key: 'sessionId',
      width: 100,
    },
    {
      title: '票数',
      dataIndex: 'ticketCount',
      key: 'ticketCount',
      width: 80,
    },
    {
      title: '总金额',
      dataIndex: 'totalAmount',
      key: 'totalAmount',
      width: 100,
      render: (amount: number) => `¥${amount}`,
    },
    {
      title: '优惠金额',
      dataIndex: 'discountAmount',
      key: 'discountAmount',
      width: 100,
      render: (amount: number) => amount ? `¥${amount}` : '-',
    },
    {
      title: '订单状态',
      dataIndex: 'orderStatus',
      key: 'orderStatus',
      width: 100,
      render: (status: string) => {
        const s = orderStatusMap[status]
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
      render: (_: unknown, record: Record<string, unknown>) => (
        <Space>
          <Button type="link" size="small" onClick={() => handleViewDetail(Number(record.orderId))}>
            详情
          </Button>
          {record.orderStatus === 'PENDING_PAY' && (
            <Popconfirm
              title="确定取消该订单吗？"
              onConfirm={() => handleCancel(Number(record.orderId))}
              okText="确定"
              cancelText="取消"
            >
              <Button type="link" size="small" danger>
                取消
              </Button>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ]

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Space>
          <span>订单状态：</span>
          <Select
            placeholder="全部状态"
            value={statusFilter}
            onChange={setStatusFilter}
            style={{ width: 150 }}
            allowClear
          >
            {Object.entries(orderStatusMap).map(([key, val]) => (
              <Select.Option key={key} value={key}>{val.text}</Select.Option>
            ))}
          </Select>
          <Button type="primary" onClick={() => loadData(1)}>
            筛选
          </Button>
        </Space>
      </div>

      <Table
        columns={columns}
        dataSource={data as Record<string, unknown>[]}
        rowKey="orderId"
        loading={loading}
        pagination={{
          ...pagination,
          showSizeChanger: true,
          showTotal: total => `共 ${total} 条`,
          onChange: (page, pageSize) => loadData(page, pageSize),
        }}
      />

      <Modal
        title="订单详情"
        open={detailVisible}
        onCancel={() => setDetailVisible(false)}
        width={700}
        footer={[
          <Button key="close" onClick={() => setDetailVisible(false)}>
            关闭
          </Button>,
        ]}
      >
        {detailLoading ? (
          <div style={{ textAlign: 'center', padding: 40 }}>加载中...</div>
        ) : orderDetail ? (
          <div>
            <Descriptions column={2} bordered size="small">
              <Descriptions.Item label="订单ID">{orderDetail.orderId as string}</Descriptions.Item>
              <Descriptions.Item label="订单号">{orderDetail.orderNo as string}</Descriptions.Item>
              <Descriptions.Item label="场次ID">{orderDetail.sessionId as string}</Descriptions.Item>
              <Descriptions.Item label="票数">{orderDetail.ticketCount as string}</Descriptions.Item>
              <Descriptions.Item label="总金额">¥{orderDetail.totalAmount as string}</Descriptions.Item>
              <Descriptions.Item label="优惠金额">
                {orderDetail.discountAmount ? `¥${orderDetail.discountAmount}` : '-'}
              </Descriptions.Item>
              <Descriptions.Item label="订单状态">
                {(() => {
                  const s = orderStatusMap[orderDetail.orderStatus as string]
                  return s ? <Tag color={s.color}>{s.text}</Tag> : (orderDetail.orderStatus as string)
                })()}
              </Descriptions.Item>
              <Descriptions.Item label="来源">{(orderDetail.source as string) || '-'}</Descriptions.Item>
              <Descriptions.Item label="创建时间">
                {orderDetail.createTime ? new Date(orderDetail.createTime as string).toLocaleString('zh-CN') : '-'}
              </Descriptions.Item>
              <Descriptions.Item label="支付时间">
                {orderDetail.payTime ? new Date(orderDetail.payTime as string).toLocaleString('zh-CN') : '-'}
              </Descriptions.Item>
              <Descriptions.Item label="取消时间">
                {orderDetail.cancelTime ? new Date(orderDetail.cancelTime as string).toLocaleString('zh-CN') : '-'}
              </Descriptions.Item>
              <Descriptions.Item label="过期时间">
                {orderDetail.expireTime ? new Date(orderDetail.expireTime as string).toLocaleString('zh-CN') : '-'}
              </Descriptions.Item>
              {Boolean(orderDetail.remark) && (
                <Descriptions.Item label="备注" span={2}>{orderDetail.remark as string}</Descriptions.Item>
              )}
            </Descriptions>

            {Boolean(orderDetail.items) && (orderDetail.items as unknown[]).length > 0 && (
              <div style={{ marginTop: 16 }}>
                <h4>票品明细</h4>
                <Table
                  dataSource={orderDetail.items as Record<string, unknown>[]}
                  rowKey="orderItemId"
                  size="small"
                  pagination={false}
                  columns={[
                    { title: '明细ID', dataIndex: 'orderItemId', key: 'orderItemId', width: 100 },
                    { title: '座位ID', dataIndex: 'seatId', key: 'seatId', width: 100 },
                    { title: '定价策略ID', dataIndex: 'priceStrategyId', key: 'priceStrategyId', width: 110 },
                    { title: '单价', dataIndex: 'unitPrice', key: 'unitPrice', width: 90, render: (p: number) => `¥${p}` },
                    {
                      title: '状态',
                      dataIndex: 'itemStatus',
                      key: 'itemStatus',
                      render: (status: string) => {
                        const s = itemStatusMap[status]
                        return s ? <Tag color={s.color}>{s.text}</Tag> : status
                      },
                    },
                  ]}
                />
              </div>
            )}
          </div>
        ) : null}
      </Modal>
    </div>
  )
}

export default Order
