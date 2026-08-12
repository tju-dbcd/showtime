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
  PendingPayment: { text: '待支付', color: 'warning' },
  Paid: { text: '已支付', color: 'success' },
  Cancelled: { text: '已取消', color: 'error' },
  Refunded: { text: '已退款', color: 'default' },
  Completed: { text: '已完成', color: 'success' },
}

const Order = () => {
  const [data, setData] = useState<any[]>([])
  const [loading, setLoading] = useState(false)
  const [statusFilter, setStatusFilter] = useState<string | undefined>()
  const [pagination, setPagination] = useState({ current: 1, pageSize: 10, total: 0 })
  const [detailVisible, setDetailVisible] = useState(false)
  const [orderDetail, setOrderDetail] = useState<any>(null)
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
        const result = (res.data as any).data || (res.data as any)
        setData(result?.items || result || [])
        setPagination({
          current: page,
          pageSize,
          total: result?.totalCount || 0,
        })
      }
    } catch (err) {
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
        setOrderDetail((res.data as any).data || res.data)
      }
    } catch (err) {
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
    } catch (err) {
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
      render: (_: any, record: any) => (
        <Space>
          <Button type="link" size="small" onClick={() => handleViewDetail(Number(record.orderId))}>
            详情
          </Button>
          {record.orderStatus === 'PendingPayment' && (
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
        dataSource={data}
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
              <Descriptions.Item label="订单ID">{orderDetail.orderId}</Descriptions.Item>
              <Descriptions.Item label="订单号">{orderDetail.orderNo}</Descriptions.Item>
              <Descriptions.Item label="场次ID">{orderDetail.sessionId}</Descriptions.Item>
              <Descriptions.Item label="票数">{orderDetail.ticketCount}</Descriptions.Item>
              <Descriptions.Item label="总金额">¥{orderDetail.totalAmount}</Descriptions.Item>
              <Descriptions.Item label="优惠金额">
                {orderDetail.discountAmount ? `¥${orderDetail.discountAmount}` : '-'}
              </Descriptions.Item>
              <Descriptions.Item label="订单状态">
                {(() => {
                  const s = orderStatusMap[orderDetail.orderStatus]
                  return s ? <Tag color={s.color}>{s.text}</Tag> : orderDetail.orderStatus
                })()}
              </Descriptions.Item>
              <Descriptions.Item label="来源">{orderDetail.source || '-'}</Descriptions.Item>
              <Descriptions.Item label="创建时间">
                {orderDetail.createTime ? new Date(orderDetail.createTime).toLocaleString('zh-CN') : '-'}
              </Descriptions.Item>
              <Descriptions.Item label="支付时间">
                {orderDetail.payTime ? new Date(orderDetail.payTime).toLocaleString('zh-CN') : '-'}
              </Descriptions.Item>
              <Descriptions.Item label="取消时间">
                {orderDetail.cancelTime ? new Date(orderDetail.cancelTime).toLocaleString('zh-CN') : '-'}
              </Descriptions.Item>
              <Descriptions.Item label="过期时间">
                {orderDetail.expireTime ? new Date(orderDetail.expireTime).toLocaleString('zh-CN') : '-'}
              </Descriptions.Item>
              {orderDetail.remark && (
                <Descriptions.Item label="备注" span={2}>{orderDetail.remark}</Descriptions.Item>
              )}
            </Descriptions>

            {orderDetail.items && orderDetail.items.length > 0 && (
              <div style={{ marginTop: 16 }}>
                <h4>票品明细</h4>
                <Table
                  dataSource={orderDetail.items}
                  rowKey="orderItemId"
                  size="small"
                  pagination={false}
                  columns={[
                    { title: '票品ID', dataIndex: 'ticketId', key: 'ticketId' },
                    { title: '票区ID', dataIndex: 'seatSectionId', key: 'seatSectionId' },
                    { title: '单价', dataIndex: 'unitPrice', key: 'unitPrice', render: (p: number) => `¥${p}` },
                    { title: '数量', dataIndex: 'quantity', key: 'quantity' },
                    { title: '小计', dataIndex: 'subtotal', key: 'subtotal', render: (p: number) => `¥${p}` },
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
