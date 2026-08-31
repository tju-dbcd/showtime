import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Descriptions, Tag, Spin, Button, message, Typography, Card, Divider } from 'antd';
import { orderAPI } from '@/api/requests';
import type { OrderResponse } from '@/types/api';
import './OrderDetail.css';

const { Title } = Typography;

const STATUS_MAP: Record<string, { color: string; text: string }> = {
  PENDING_PAY: { color: 'orange', text: '待支付' },
  PAID: { color: 'green', text: '已支付' },
  ISSUED: { color: 'blue', text: '已出票' },
  PART_REFUND: { color: 'purple', text: '部分退款' },
  REFUNDED: { color: 'red', text: '已退款' },
  CANCELLED: { color: 'red', text: '已取消' },
};

const OrderDetail = () => {
  const { orderId } = useParams<{ orderId: string }>();
  const navigate = useNavigate();
  const [order, setOrder] = useState<OrderResponse | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchOrder = async () => {
      if (!orderId) return;
      setLoading(true);
      try {
        const { data, error } = await orderAPI.getOrder(Number(orderId));
        if (error) {
          message.error('获取订单详情失败');
          setLoading(false);
          return;
        }
        if (data?.success && data?.data) {
          // 转换所有数字字段为 number
          const orderData = {
            ...data.data,
            orderId: Number(data.data.orderId),
            sessionId: Number(data.data.sessionId),
            ticketCount: Number(data.data.ticketCount),
            totalAmount: Number(data.data.totalAmount),
            discountAmount: Number(data.data.discountAmount),
            parentOrderId: data.data.parentOrderId ? Number(data.data.parentOrderId) : null,
            items: data.data.items?.map((item: any) => ({
              ...item,
              orderItemId: Number(item.orderItemId),
              seatId: Number(item.seatId),
              priceStrategyId: Number(item.priceStrategyId),
              unitPrice: Number(item.unitPrice),
              realNameId: item.realNameId ? Number(item.realNameId) : null,
            })) || [],
            payments: data.data.payments?.map((payment: any) => ({
              ...payment,
              paymentId: Number(payment.paymentId),
              orderId: Number(payment.orderId),
              payAmount: Number(payment.payAmount),
            })) || [],
            tickets: data.data.tickets?.map((ticket: any) => ({
              ...ticket,
              eTicketId: Number(ticket.eTicketId),
              orderItemId: Number(ticket.orderItemId),
            })) || [],
          };
          setOrder(orderData);
        } else {
          message.error(data?.message || '获取订单详情失败');
        }
      } catch (error: any) {
        console.error('获取订单详情失败:', error);
        message.error(error.message || '获取订单详情失败');
      } finally {
        setLoading(false);
      }
    };
    fetchOrder();
  }, [orderId]);

  if (loading) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '60vh' }}>
        <Spin size="large" tip="加载订单详情..." />
      </div>
    );
  }

  if (!order) {
    return (
      <div style={{ textAlign: 'center', padding: 60 }}>
        <Title level={4}>订单不存在</Title>
        <Button onClick={() => navigate('/order')}>返回订单列表</Button>
      </div>
    );
  }

  const statusInfo = STATUS_MAP[order.orderStatus] || { color: 'default', text: order.orderStatus };

  return (
    <div className="order-detail-container">
      <Card>
        <div className="order-detail-header">
          <Title level={3}>订单详情</Title>
          <Tag color={statusInfo.color}>{statusInfo.text}</Tag>
        </div>

        <Divider />

        <Descriptions bordered column={2}>
          <Descriptions.Item label="订单号">{order.orderNo}</Descriptions.Item>
          <Descriptions.Item label="订单ID">{order.orderId}</Descriptions.Item>
          <Descriptions.Item label="场次ID">{order.sessionId}</Descriptions.Item>
          <Descriptions.Item label="票数">{order.ticketCount}</Descriptions.Item>
          <Descriptions.Item label="总金额">¥{order.totalAmount}</Descriptions.Item>
          <Descriptions.Item label="优惠金额">¥{order.discountAmount}</Descriptions.Item>
          <Descriptions.Item label="订单状态">
            <Tag color={statusInfo.color}>{statusInfo.text}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="来源">{order.source || '-'}</Descriptions.Item>
          <Descriptions.Item label="创建时间">
            {new Date(order.createTime).toLocaleString('zh-CN')}
          </Descriptions.Item>
          <Descriptions.Item label="过期时间">
            {new Date(order.expireTime).toLocaleString('zh-CN')}
          </Descriptions.Item>
          <Descriptions.Item label="支付时间">
            {order.payTime ? new Date(order.payTime).toLocaleString('zh-CN') : '-'}
          </Descriptions.Item>
          <Descriptions.Item label="出票时间">
            {order.issueTime ? new Date(order.issueTime).toLocaleString('zh-CN') : '-'}
          </Descriptions.Item>
          <Descriptions.Item label="取消时间">
            {order.cancelTime ? new Date(order.cancelTime).toLocaleString('zh-CN') : '-'}
          </Descriptions.Item>
          <Descriptions.Item label="备注" span={2}>
            {order.remark || '-'}
          </Descriptions.Item>
        </Descriptions>

        {order.items && order.items.length > 0 && (
          <>
            <Divider />
            <Title level={5}>票品明细</Title>
            <table className="order-items-table">
              <thead>
                <tr>
                  <th>明细ID</th>
                  <th>座位ID</th>
                  <th>单价</th>
                  <th>状态</th>
                </tr>
              </thead>
              <tbody>
                {order.items.map((item) => (
                  <tr key={item.orderItemId}>
                    <td>{item.orderItemId}</td>
                    <td>{item.seatId}</td>
                    <td>¥{item.unitPrice}</td>
                    <td>
                      <Tag color="default">{item.itemStatus}</Tag>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}

        {/* ====== 新增：电子票展示 ====== */}
        {order.tickets && order.tickets.length > 0 && (
          <>
            <Divider />
            <Title level={5}>电子票</Title>
            <div className="ticket-list">
              {order.tickets.map((ticket) => (
                <div key={ticket.eTicketId} className="ticket-item">
                  <div className="ticket-info">
                    <span>票号：{ticket.eTicketNo}</span>
                    <Tag color={ticket.ticketStatus === 'UNUSED' ? 'green' : 'default'}>
                      {ticket.ticketStatus}
                    </Tag>
                  </div>
                  {ticket.qrCode && (
                    <img src={ticket.qrCode} alt="二维码" className="ticket-qr" />
                  )}
                </div>
              ))}
            </div>
          </>
        )}

        <Divider />

        <Button type="primary" onClick={() => navigate('/order')}>
          返回订单列表
        </Button>
      </Card>
    </div>
  );
};

export default OrderDetail;
