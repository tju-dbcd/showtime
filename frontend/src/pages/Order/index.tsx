import { useState, useEffect, useRef } from 'react';
import { Tag, Typography, Empty, Modal, Button, message, Spin, Divider, notification, Tabs, Card } from 'antd';
import { useNavigate } from 'react-router-dom';
import { List as VirtualList } from 'react-window';
import { orderAPI, paymentAPI } from '@/api/requests';
import type { components } from '@/api/types';
import type { OrderSummaryResponse, PaymentResponse } from '@/types/api';
import {
  ensureRealtimeConnection,
  subscribeOrderCreated,
  subscribeRefundStatusChanged,
  type RefundStatusChangedEvent,
} from '@/realtime/orderNotifications';
import './Order.css';

const { Title, Text } = Typography;

type PaymentChannel = components['schemas']['PaymentChannel'];

// 订单状态映射
const STATUS_MAP: Record<string, { color: string; text: string }> = {
  PENDING_PAY: { color: 'orange', text: '待支付' },
  PAID: { color: 'green', text: '已支付' },
  ISSUED: { color: 'blue', text: '已出票' },
  PART_REFUND: { color: 'purple', text: '部分退款' },
  REFUNDED: { color: 'red', text: '已退款' },
  CANCELLED: { color: 'red', text: '已取消' },
};
// 支付状态映射
const PAYMENT_STATUS_MAP: Record<string, { color: string; text: string }> = {
  PENDING: { color: 'orange', text: '支付中' },
  SUCCESS: { color: 'green', text: '支付成功' },
  FAIL: { color: 'red', text: '支付失败' },
  CLOSED: { color: 'default', text: '已关闭' },
};

const REFUND_STATUS_TEXT: Record<string, string> = {
  PROCESSING: '退款处理中，请耐心等待',
  COMPLETED: '退款已完成，款项将原路退回',
  FAILED: '退款失败，请联系客服',
};

const refundStatusText = (event: RefundStatusChangedEvent) =>
  REFUND_STATUS_TEXT[event.refundStatus] || `退款状态：${event.refundStatus}`;

type OrderRowData = {
  orders: OrderSummaryResponse[];
  onOpenPayment: (order: OrderSummaryResponse) => void;
  onCancel: (orderId: number) => void;
  onOpenDetail: (orderId: number) => void;
};

const OrderRow = ({ index, style, ariaAttributes, orders, onOpenPayment, onCancel, onOpenDetail }: OrderRowData & {
  index: number;
  style: React.CSSProperties;
  ariaAttributes: React.HTMLAttributes<HTMLDivElement>;
}) => {
  const order = orders[index];
  const status = STATUS_MAP[order.orderStatus] || { color: 'default', text: order.orderStatus };

  return (
    <div {...ariaAttributes} style={{ ...style, padding: '0 4px 12px' }}>
      <Card size="small" className="order-row-card">
        <div className="order-row-main">
          <div>
            <div className="order-row-number">{order.orderNo}</div>
            <Typography.Text type="secondary">
              {order.ticketCount} 张票 · {new Date(order.createTime).toLocaleString('zh-CN')}
            </Typography.Text>
          </div>
          <div className="order-row-amount">¥{order.totalAmount.toFixed(2)}</div>
          <Tag color={status.color}>{status.text}</Tag>
          <div className="order-row-actions">
            {order.orderStatus === 'PENDING_PAY' && (
              <>
                <Button type="primary" size="small" onClick={() => onOpenPayment(order)}>去支付</Button>
                <Button size="small" danger onClick={() => onCancel(order.orderId)}>取消</Button>
              </>
            )}
            <Button type="link" size="small" onClick={() => onOpenDetail(order.orderId)}>查看详情</Button>
          </div>
        </div>
      </Card>
    </div>
  );
};

const Order = () => {
  const navigate = useNavigate();
  const [orders, setOrders] = useState<OrderSummaryResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);
  const [statusFilter, setStatusFilter] = useState<string>('ALL');

  // 弹窗相关
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [selectedOrderId, setSelectedOrderId] = useState<number | null>(null);
  const [selectedOrderNo, setSelectedOrderNo] = useState<string>('');
  const [selectedAmount, setSelectedAmount] = useState<number>(0);
  const [payments, setPayments] = useState<PaymentResponse[]>([]);
  const [paying, setPaying] = useState(false);
  const [loadingPayments, setLoadingPayments] = useState(false);
  const statusFilterRef = useRef(statusFilter);
  const pageRef = useRef(page);
  const pageSizeRef = useRef(pageSize);
  const [rowHeight, setRowHeight] = useState(92);

  useEffect(() => {
    statusFilterRef.current = statusFilter;
    pageRef.current = page;
    pageSizeRef.current = pageSize;
  }, [statusFilter, page, pageSize]);

  useEffect(() => {
    const updateRowHeight = () => setRowHeight(window.innerWidth <= 768 ? 116 : 92);
    updateRowHeight();
    window.addEventListener('resize', updateRowHeight);
    return () => window.removeEventListener('resize', updateRowHeight);
  }, []);

  // ========== 获取订单列表 ==========
  const fetchOrders = async (currentPage: number = page, currentPageSize: number = pageSize) => {
    setLoading(true);
    try {
      const { data, error } = await orderAPI.getOrders({
        Status: statusFilterRef.current === 'ALL' ? undefined : statusFilterRef.current as any,
        Page: currentPage,
        PageSize: currentPageSize,
      });

      if (error) {
        message.error('获取订单列表失败');
        setLoading(false);
        return;
      }

      if (data?.success && data?.data) {
        const items = data.data.items.map((item: any) => ({
          ...item,
          orderId: Number(item.orderId),
          sessionId: Number(item.sessionId),
          totalAmount: Number(item.totalAmount),
          discountAmount: Number(item.discountAmount),
          ticketCount: Number(item.ticketCount),
          parentOrderId: item.parentOrderId ? Number(item.parentOrderId) : null,
        }));
        setOrders(items);
        setTotalCount(Number(data.data.totalCount) || 0);
      } else {
        message.error(data?.message || '获取订单列表失败');
      }
    } catch (error: any) {
      console.error('获取订单失败:', error);
      message.error(error.message || '获取订单列表失败');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchOrders(1, pageSize);
    setPage(1);
  }, [statusFilter]);

  // ========== 实时通知（下单成功 / 退款状态变化） ==========
  useEffect(() => {
    void ensureRealtimeConnection();
    const unsubscribeCreated = subscribeOrderCreated((event) => {
      notification.success({
        message: '新订单创建成功',
        description: `订单号 ${event.orderNo}，共 ${event.ticketCount} 张票`,
      });
      fetchOrders(pageRef.current, pageSizeRef.current);
    });
    const unsubscribeRefund = subscribeRefundStatusChanged((event) => {
      notification.info({
        message: `退款单 ${event.refundNo} 状态更新`,
        description: refundStatusText(event),
      });
      fetchOrders(pageRef.current, pageSizeRef.current);
    });
    return () => {
      unsubscribeCreated();
      unsubscribeRefund();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ========== 分页变化 ==========
  const handlePageChange = (newPage: number, newPageSize: number) => {
    setPage(newPage);
    setPageSize(newPageSize);
    fetchOrders(newPage, newPageSize);
  };

  const handleStatusChange = (key: string) => {
    setStatusFilter(key);
  };

  // ========== 打开支付弹窗 ==========
  const handleOpenPaymentModal = async (orderId: number, orderNo: string, amount: number) => {
    setSelectedOrderId(orderId);
    setSelectedOrderNo(orderNo);
    setSelectedAmount(amount);
    setIsModalOpen(true);
    setLoadingPayments(true);

    try {
      const { data, error } = await paymentAPI.getPayments(orderId);
      if (error || !data?.success || !data.data) {
        setPayments([]);
      } else {
        setPayments(data.data as unknown as PaymentResponse[]);
      }
    } catch (error) {
      console.error('获取支付记录失败:', error);
      setPayments([]);
    } finally {
      setLoadingPayments(false);
    }
  };

  // ========== 模拟支付 ==========
  const handleMockPayment = async (channel: PaymentChannel = 'WECHAT') => {
    if (!selectedOrderId) return;
    setPaying(true);
    try {
      const { data, error } = await paymentAPI.mockPayment(selectedOrderId, {
        payChannel: channel,
        result: 'SUCCESS',
      });

      if (error) {
        message.error('支付失败');
        setPaying(false);
        return;
      }

      if (data?.success && data?.data) {
        message.success('支付成功！');
        setIsModalOpen(false);
        fetchOrders();
      } else {
        message.error(data?.message || '支付失败');
      }
    } catch (error: any) {
      console.error('支付失败:', error);
      message.error(error.message || '支付失败，请重试');
    } finally {
      setPaying(false);
    }
  };

  // ========== 取消订单 ==========
  const handleCancelOrder = (orderId: number) => {
    Modal.confirm({
      title: '确认取消',
      content: '确定要取消该订单吗？取消后无法恢复。',
      onOk: async () => {
        try {
          const { data, error } = await orderAPI.cancelOrder(orderId);
          if (error || !data?.success) {
            message.error(data?.message || '取消订单失败');
            return;
          }
          message.success('订单已取消');
          fetchOrders(page, pageSize);
        } catch (error: any) {
          message.error(error.message || '取消订单失败');
        }
      },
    });
  };

  const handleCloseModal = () => {
    setIsModalOpen(false);
    setSelectedOrderId(null);
    setPayments([]);
  };

  // ========== 待支付统计 ==========
  const pendingOrders = orders.filter((o) => o.orderStatus === 'PENDING_PAY');
  const totalPendingAmount = pendingOrders.reduce((sum, o) => sum + o.totalAmount, 0);

  return (
    <>
      <div className="order-container">
        <div className="order-content">
          <Title level={2} style={{ marginBottom: 8 }}>我的订单</Title>
          <Text type="secondary" style={{ marginBottom: 24, display: 'block' }}>
            共 {totalCount} 笔订单
            {pendingOrders.length > 0 && (
              <span style={{ marginLeft: 16, color: '#ff4d4f' }}>
                待支付 {pendingOrders.length} 笔
              </span>
            )}
          </Text>

          <Tabs
            activeKey={statusFilter}
            onChange={handleStatusChange}
            items={[{ key: 'ALL', label: '全部' }, ...Object.entries(STATUS_MAP).map(([key, value]) => ({ key, label: value.text }))]}
          />

          <Spin spinning={loading}>
            {orders.length > 0 ? (
              <VirtualList<OrderRowData>
                key={`${statusFilter}-${page}-${pageSize}`}
                rowCount={orders.length}
                rowHeight={rowHeight}
                overscanCount={5}
                rowComponent={OrderRow}
                rowProps={{
                  orders,
                  onOpenPayment: (order) => handleOpenPaymentModal(order.orderId, order.orderNo, order.totalAmount),
                  onCancel: handleCancelOrder,
                  onOpenDetail: (orderId) => navigate(`/order/${orderId}`),
                }}
                style={{ height: Math.min(600, orders.length * rowHeight), width: '100%' }}
              />
            ) : (
              <Empty description="当前状态暂无订单" />
            )}
          </Spin>

          {totalCount > 0 && (
            <div className="order-pagination">
              <Button disabled={page <= 1} onClick={() => handlePageChange(page - 1, pageSize)}>上一页</Button>
              <span>第 {page} 页，共 {totalCount} 笔</span>
              <Button disabled={page * pageSize >= totalCount} onClick={() => handlePageChange(page + 1, pageSize)}>下一页</Button>
            </div>
          )}
        </div>

        {/* ====== 底部固定黑条 ====== */}
        <div className="order-footer">
          <div className="footer-info">
            <span className="footer-label">待支付：</span>
            <span className="footer-count">{pendingOrders.length} 笔</span>
            <span className="footer-total">合计：¥{totalPendingAmount.toFixed(2)}</span>
          </div>
          <Button
            type="primary"
            size="large"
            className="pay-btn"
            disabled={pendingOrders.length === 0}
            onClick={() => {
              const first = pendingOrders[0];
              handleOpenPaymentModal(first.orderId, first.orderNo, first.totalAmount);
            }}
          >
            立即付款
          </Button>
        </div>
      </div>

      {/* ====== 支付弹窗 ====== */}
      <Modal
        title="扫码支付"
        open={isModalOpen}
        onCancel={handleCloseModal}
        footer={[
          <Button key="cancel" onClick={handleCloseModal}>
            取消
          </Button>,
          <Button key="wechat" type="primary" loading={paying} onClick={() => handleMockPayment('WECHAT')}>
            微信支付
          </Button>,
          <Button key="alipay" type="primary" loading={paying} onClick={() => handleMockPayment('ALIPAY')}>
            支付宝
          </Button>,
        ]}
        centered
        mask={{ closable: true }}
        className="payment-modal"
        width={460}
        destroyOnClose
      >
        <div className="payment-content">
          <div className="payment-info" style={{ width: '100%' }}>
            <p className="payment-amount">
              支付金额：<span className="amount">¥{selectedAmount.toFixed(2)}</span>
            </p>
            <p className="payment-order">
              订单号：<span className="order-id">{selectedOrderNo}</span>
            </p>

            <Divider />

            <div style={{ textAlign: 'center', marginTop: 8 }}>
              <Text type="secondary" style={{ fontSize: 13 }}>选择支付方式完成支付</Text>
            </div>

            {loadingPayments ? (
              <div style={{ textAlign: 'center', padding: 16 }}>
                <Spin size="small" /> 加载支付记录...
              </div>
            ) : payments.length > 0 ? (
              <div style={{ marginTop: 12 }}>
                <Text type="secondary" style={{ fontSize: 12 }}>已有 {payments.length} 条支付记录</Text>
                {payments.map((p) => (
                  <div
                    key={p.paymentId}
                    style={{
                      display: 'flex',
                      justifyContent: 'space-between',
                      padding: '4px 0',
                      fontSize: 13,
                      borderBottom: '1px solid #f5f5f5',
                    }}
                  >
                    <span>{p.payChannel}</span>
                    <span>¥{p.payAmount.toFixed(2)}</span>
                    <Tag color={PAYMENT_STATUS_MAP[p.payStatus]?.color || 'default'}>
                      {PAYMENT_STATUS_MAP[p.payStatus]?.text || p.payStatus}
                    </Tag>
                  </div>
                ))}
              </div>
            ) : null}

            <div className="payment-tip" style={{ marginTop: 16 }}>
              请选择支付方式，付款后订单状态会自动更新
            </div>
          </div>
        </div>
      </Modal>
    </>
  );
};

export default Order;
