import { useState, useEffect } from 'react';
import { Table, Tag, Typography, Empty, Modal, Button, message, Spin, Divider } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useNavigate } from 'react-router-dom';
import { orderAPI, paymentAPI } from '@/api/requests';
import type { OrderSummaryResponse, PaymentResponse } from '@/types/api';
import './Order.css';

const { Title, Text } = Typography;

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

const Order = () => {
  const navigate = useNavigate();
  const [orders, setOrders] = useState<OrderSummaryResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  // 弹窗相关
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [selectedOrderId, setSelectedOrderId] = useState<number | null>(null);
  const [selectedOrderNo, setSelectedOrderNo] = useState<string>('');
  const [selectedAmount, setSelectedAmount] = useState<number>(0);
  const [payments, setPayments] = useState<PaymentResponse[]>([]);
  const [paying, setPaying] = useState(false);
  const [loadingPayments, setLoadingPayments] = useState(false);

  // ========== 获取订单列表 ==========
  const fetchOrders = async (currentPage: number = page, currentPageSize: number = pageSize) => {
    setLoading(true);
    try {
      const { data, error } = await orderAPI.getOrders({
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
    fetchOrders();
  }, []);

  // ========== 分页变化 ==========
  const handlePageChange = (newPage: number, newPageSize: number) => {
    setPage(newPage);
    setPageSize(newPageSize);
    fetchOrders(newPage, newPageSize);
  };

  // ========== 打开支付弹窗 ==========
  const handleOpenPaymentModal = async (orderId: number, orderNo: string, amount: number) => {
    setSelectedOrderId(orderId);
    setSelectedOrderNo(orderNo);
    setSelectedAmount(amount);
    setIsModalOpen(true);
    setLoadingPayments(true);

    try {
      const response: any = await paymentAPI.getPayments(orderId);
      const result = response.data ? response.data : response;
      if (result.success && result.data) {
        setPayments(result.data);
      } else {
        setPayments([]);
      }
    } catch (error) {
      console.error('获取支付记录失败:', error);
      setPayments([]);
    } finally {
      setLoadingPayments(false);
    }
  };

  // ========== 模拟支付 ==========
  const handleMockPayment = async (channel: string = 'WeChat') => {
    if (!selectedOrderId) return;
    setPaying(true);
    try {
      const { data, error } = await paymentAPI.mockPayment(selectedOrderId, {
        payChannel: channel,
        result: 'Success',
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
          if (error) {
            message.error('取消订单失败');
            return;
          }
          if (data?.success) {
            message.success('订单已取消');
            fetchOrders();
          } else {
            message.error(data?.message || '取消订单失败');
          }
        } catch (error: any) {
          console.error('取消订单失败:', error);
          message.error(error.message || '取消订单失败');
        }
      },
    });
  };

  // ========== 关闭弹窗 ==========
  const handleCloseModal = () => {
    setIsModalOpen(false);
    setSelectedOrderId(null);
    setPayments([]);
  };

  // ========== 表格列定义 ==========
  const columns: ColumnsType<OrderSummaryResponse> = [
    {
      title: '订单号',
      dataIndex: 'orderNo',
      key: 'orderNo',
      width: 180,
      render: (text: string) => <span style={{ fontFamily: 'monospace' }}>{text}</span>,
    },
    {
      title: '票数',
      dataIndex: 'ticketCount',
      key: 'ticketCount',
      width: 80,
      align: 'center',
    },
    {
      title: '金额',
      dataIndex: 'totalAmount',
      key: 'totalAmount',
      width: 120,
      render: (amount: number) => (
        <span style={{ color: '#ff4d4f', fontWeight: 600 }}>¥{amount.toFixed(2)}</span>
      ),
    },
    {
      title: '状态',
      dataIndex: 'orderStatus',
      key: 'orderStatus',
      width: 100,
      render: (status: string) => {
        const info = STATUS_MAP[status] || { color: 'default', text: status };
        return <Tag color={info.color}>{info.text}</Tag>;
      },
    },
    {
      title: '下单时间',
      dataIndex: 'createTime',
      key: 'createTime',
      width: 180,
      render: (time: string) => new Date(time).toLocaleString('zh-CN'),
    },
    {
      title: '过期时间',
      dataIndex: 'expireTime',
      key: 'expireTime',
      width: 180,
      render: (time: string) => new Date(time).toLocaleString('zh-CN'),
    },
    {
      title: '操作',
      key: 'action',
      width: 200,
      fixed: 'right',
      render: (_: any, record: OrderSummaryResponse) => {
        const status = record.orderStatus;

        if (status === 'PENDING_PAY') {
          return (
            <div style={{ display: 'flex', gap: 8 }}>
              <Button
                type="primary"
                size="small"
                onClick={() => handleOpenPaymentModal(record.orderId, record.orderNo, record.totalAmount)}
              >
                去支付
              </Button>
              <Button type="link" size="small" danger onClick={() => handleCancelOrder(record.orderId)}>
                取消
              </Button>
            </div>
          );
        }

        if (status === 'PAID' || status === 'ISSUED') {
          return (
            <Button type="link" size="small" onClick={() => navigate(`/order/${record.orderId}`)}>
              查看详情
            </Button>
          );
        }

        return <span style={{ color: '#ccc' }}>--</span>;
      },
    },
  ];

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

          <Spin spinning={loading}>
            <Table<OrderSummaryResponse>
              dataSource={orders}
              columns={columns}
              rowKey="orderId"
              pagination={{
                current: page,
                pageSize: pageSize,
                total: totalCount,
                showSizeChanger: true,
                showTotal: (total) => `共 ${total} 笔订单`,
                onChange: handlePageChange,
                onShowSizeChange: handlePageChange,
              }}
              bordered
              style={{ background: '#fff', borderRadius: 12 }}
              locale={{ emptyText: <Empty description="暂无订单，快去抢票吧！" /> }}
              scroll={{ x: 900 }}
            />
          </Spin>
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
          <Button key="wechat" type="primary" loading={paying} onClick={() => handleMockPayment('WeChat')}>
            微信支付
          </Button>,
          <Button key="alipay" type="primary" loading={paying} onClick={() => handleMockPayment('Alipay')}>
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
