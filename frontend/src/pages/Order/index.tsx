import { useState } from 'react';
import { Table, Tag, Typography, Empty, Modal, Button, message } from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useLocation } from 'react-router-dom';
import { mockOrders } from '@/mock/orders';
import './Order.css';

const { Title } = Typography;

// 定义订单数据类型
interface Order {
  id: string;
  eventName: string;
  venue: string;
  seats: string;
  amount: number;
  status: 'paid' | 'pending' | 'cancelled';
  date: string;
}

// 模拟生成二维码图片（实际项目用真实的二维码库）
const generateQRCode = () => {
  return `data:image/svg+xml,${encodeURIComponent(`
    <svg xmlns="http://www.w3.org/2000/svg" width="200" height="200" viewBox="0 0 200 200">
      <rect width="200" height="200" fill="white"/>
      <rect x="20" y="20" width="40" height="40" fill="black"/>
      <rect x="70" y="20" width="20" height="20" fill="black"/>
      <rect x="100" y="20" width="40" height="40" fill="black"/>
      <rect x="20" y="70" width="20" height="40" fill="black"/>
      <rect x="50" y="70" width="20" height="20" fill="black"/>
      <rect x="80" y="70" width="20" height="40" fill="black"/>
      <rect x="110" y="70" width="20" height="20" fill="black"/>
      <rect x="140" y="70" width="40" height="40" fill="black"/>
      <rect x="20" y="120" width="40" height="40" fill="black"/>
      <rect x="70" y="120" width="40" height="20" fill="black"/>
      <rect x="120" y="120" width="20" height="20" fill="black"/>
      <rect x="150" y="120" width="30" height="40" fill="black"/>
      <rect x="20" y="170" width="20" height="10" fill="black"/>
      <rect x="50" y="170" width="30" height="20" fill="black"/>
      <rect x="90" y="170" width="20" height="10" fill="black"/>
      <rect x="120" y="170" width="60" height="20" fill="black"/>
      <rect x="90" y="90" width="20" height="20" fill="black"/>
      <rect x="140" y="20" width="10" height="10" fill="black"/>
      <rect x="160" y="40" width="10" height="10" fill="black"/>
      <rect x="40" y="160" width="10" height="10" fill="black"/>
      <rect x="160" y="160" width="10" height="10" fill="black"/>
      <rect x="180" y="20" width="10" height="10" fill="black"/>
    </svg>
  `)}`;
};

const Order = () => {
  const location = useLocation();
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [selectedOrder, setSelectedOrder] = useState<Order | null>(null);

  const state = location.state as { selectedSeats?: string[]; eventId?: string } | null;

  const orders: Order[] = mockOrders as Order[];

  const statusMap = {
    paid: { color: 'green', text: '已支付' },
    pending: { color: 'orange', text: '待支付' },
    cancelled: { color: 'red', text: '已取消' },
  };

  const columns: ColumnsType<Order> = [
    {
      title: '订单号',
      dataIndex: 'id',
      key: 'id',
      width: 180,
      render: (id: string) => <span style={{ fontFamily: 'monospace' }}>{id}</span>,
    },
    {
      title: '演出名称',
      dataIndex: 'eventName',
      key: 'eventName',
      width: 220,
    },
    {
      title: '场馆',
      dataIndex: 'venue',
      key: 'venue',
      width: 140,
    },
    {
      title: '座位',
      dataIndex: 'seats',
      key: 'seats',
      width: 160,
    },
    {
      title: '金额',
      dataIndex: 'amount',
      key: 'amount',
      width: 100,
      render: (amount: number) => <span style={{ color: '#ff4d4f', fontWeight: 600 }}>¥{amount}</span>,
    },
    {
      title: '状态',
      dataIndex: 'status',
      key: 'status',
      width: 100,
      render: (status: 'paid' | 'pending' | 'cancelled') => (
        <Tag color={statusMap[status].color}>{statusMap[status].text}</Tag>
      ),
    },
    {
      title: '下单时间',
      dataIndex: 'date',
      key: 'date',
      width: 160,
    },
    {
      title: '操作',
      key: 'action',
      width: 100,
      render: (_: any, record: Order) => {
        if (record.status === 'pending') {
          return (
            <Button
              type="primary"
              size="small"
              onClick={() => {
                setSelectedOrder(record);
                setIsModalOpen(true);
              }}
            >
              去支付
            </Button>
          );
        }
        return <span style={{ color: '#ccc' }}>--</span>;
      },
    },
  ];

  const handlePay = () => {
    if (!selectedOrder) {
      message.warning('请选择要支付的订单');
      return;
    }
    setIsModalOpen(true);
  };

  const handleCloseModal = () => {
    setIsModalOpen(false);
    setSelectedOrder(null);
  };

  const handleConfirmPay = () => {
    message.success('支付成功！');
    setIsModalOpen(false);
    setSelectedOrder(null);
  };

  const showSelectedInfo = state?.selectedSeats && state.selectedSeats.length > 0;

  const pendingOrders = orders.filter(o => o.status === 'pending');
  const totalPendingAmount = pendingOrders.reduce((sum, o) => sum + o.amount, 0);

  return (
    <>
      <div className="order-container">
        <div className="order-content">
          <Title level={2} style={{ marginBottom: 8 }}>我的订单</Title>
          <p style={{ color: '#888', marginBottom: 24 }}>
            共 {orders.length} 笔订单
            {showSelectedInfo && (
              <span style={{ marginLeft: 16, color: '#1890ff' }}>
                刚刚选择了 {state.selectedSeats?.length} 个座位，待支付
              </span>
            )}
          </p>

          <Table<Order>
            dataSource={orders}
            columns={columns}
            rowKey="id"
            pagination={{ pageSize: 10, showSizeChanger: true }}
            bordered
            style={{ background: '#fff', borderRadius: 12 }}
            locale={{
              emptyText: <Empty description="暂无订单，快去抢票吧！" />,
            }}
          />
        </div>

        <div className="order-footer">
          <div className="footer-info">
            <span className="footer-label">待支付：</span>
            <span className="footer-count">{pendingOrders.length} 笔</span>
            <span className="footer-total">合计：¥{totalPendingAmount}</span>
          </div>
          <Button
            type="primary"
            size="large"
            className="pay-btn"
            disabled={pendingOrders.length === 0}
            onClick={handlePay}
          >
            立即付款
          </Button>
        </div>
      </div>

      <Modal
        title="扫码支付"
        open={isModalOpen}
        onCancel={handleCloseModal}
        footer={[
          <Button key="cancel" onClick={handleCloseModal}>
            取消
          </Button>,
          <Button key="confirm" type="primary" onClick={handleConfirmPay}>
            我已支付
          </Button>,
        ]}
        centered
        mask={{ closable: true }}
        className="payment-modal"
        width={420}
      >
        <div className="payment-content">
          <div className="qr-code-wrapper">
            <img
              src={selectedOrder ? generateQRCode() : ''}
              alt="付款二维码"
              className="qr-code"
            />
          </div>
          <div className="payment-info">
            <p className="payment-amount">
              支付金额：<span className="amount">¥{selectedOrder?.amount || 0}</span>
            </p>
            <p className="payment-order">
              订单号：<span className="order-id">{selectedOrder?.id || ''}</span>
            </p>
            <p className="payment-tip">
              请使用微信 / 支付宝扫码支付，付款后点击"我已支付"
            </p>
          </div>
        </div>
      </Modal>
    </>
  );
};

export default Order;
