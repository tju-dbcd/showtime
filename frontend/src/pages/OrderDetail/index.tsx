import { useState, useEffect } from 'react';
import { useParams, useNavigate, useLocation } from 'react-router-dom';
import {
  Descriptions,
  Tag,
  Spin,
  Button,
  message,
  Typography,
  Card,
  Divider,
  Modal,
  Input,
  Empty,
  Select,
} from 'antd';
import { orderAPI, refundAPI, exchangeAPI, showAPI, showSessionAPI, sessionAPI } from '@/api/requests';
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

interface SessionDetail {
  sessionId: number;
  showId: number;
  startTime: string;
  endTime: string;
  sessionStatus: string;
  seatMapId: number;
}

const OrderDetail = () => {
  const { orderId } = useParams<{ orderId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const [order, setOrder] = useState<OrderResponse | null>(null);
  const [loading, setLoading] = useState(true);

  // 场次和演出详情
  const [sessionDetail, setSessionDetail] = useState<SessionDetail | null>(null);
  const [showDetail, setShowDetail] = useState<any>(null);
  const [loadingDetail, setLoadingDetail] = useState(false);

  // ========== 退票相关 ==========
  const [refundModalVisible, setRefundModalVisible] = useState(false);
  const [refundQuote, setRefundQuote] = useState<any>(null);
  const [refundReason, setRefundReason] = useState('');
  const [refundLoading, setRefundLoading] = useState(false);
  const [refundSubmitting, setRefundSubmitting] = useState(false);

  // ========== 改签相关 ==========
  const [exchangeModalVisible, setExchangeModalVisible] = useState(false);
  const [exchangeQuote, setExchangeQuote] = useState<any>(null);
  const [exchangeLoading, setExchangeLoading] = useState(false);
  const [exchangeSubmitting, setExchangeSubmitting] = useState(false);
  const [exchangeReason, setExchangeReason] = useState('');
  // 改签目标选择
  const [targetShows, setTargetShows] = useState<any[]>([]);
  const [targetShowId, setTargetShowId] = useState<number | null>(null);
  const [targetSessions, setTargetSessions] = useState<any[]>([]);
  const [targetSessionId, setTargetSessionId] = useState<number | null>(null);
  const [selectedTargetSeats, setSelectedTargetSeats] = useState<any[]>([]);
  const [loadingShows, setLoadingShows] = useState(false);
  const [loadingSessions, setLoadingSessions] = useState(false);

  // ========== 获取订单详情 ==========
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

        // 获取场次和演出详情
        if (orderData.sessionId) {
          fetchSessionAndShow(orderData.sessionId);
        }
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

    // ========== 获取场次和演出详情 ==========
  const fetchSessionAndShow = async (sessionId: number) => {
    setLoadingDetail(true);
    try {
      // 1. 获取场次详情（座位图接口包含场次信息）
      const { data: sessionData, error: sessionError } = await sessionAPI.getSessionSeatMap(sessionId);
      if (sessionError) {
        console.warn('获取场次详情失败:', sessionError);
        setLoadingDetail(false);
        return;
      }
      if (sessionData?.success && sessionData?.data) {
        const detail = {
          sessionId: Number(sessionData.data.sessionId),
          showId: Number(sessionData.data.showId),
          startTime: sessionData.data.startTime,
          endTime: sessionData.data.endTime,
          sessionStatus: sessionData.data.sessionStatus,
          seatMapId: Number(sessionData.data.seatMapId),
        };
        setSessionDetail(detail);

        // 2. 获取演出详情
        if (detail.showId) {
          const { data: showData, error: showError } = await showAPI.getShowDetail(detail.showId);
          if (showError) {
            console.warn('获取演出详情失败:', showError);
          } else if (showData?.success && showData?.data) {
            setShowDetail(showData.data);
          }
        }
      }
    } catch (error) {
      console.error('获取场次/演出详情失败:', error);
    } finally {
      setLoadingDetail(false);
    }
  };

  useEffect(() => {
    fetchOrder();
  }, [orderId]);

  // ========== 检测从选座页返回的改签数据 ==========
  useEffect(() => {
    if (location.state?.fromExchange) {
      const { targetSeats, targetSessionId: sessionId } = location.state;
      if (targetSeats && targetSeats.length > 0 && sessionId) {
        setSelectedTargetSeats(targetSeats);
        setTargetSessionId(sessionId);
        // 自动获取改签报价
        setTimeout(() => {
          handleGetExchangeQuote();
        }, 300);
        // 清除 state，防止刷新页面后重复触发
        window.history.replaceState({}, document.title);
      }
    }
  }, [location.state]);

  // ========== 退票功能 ==========
  const fetchRefundQuote = async () => {
    if (!order) return;
    setRefundLoading(true);
    try {
      const { data, error } = await refundAPI.getRefundQuote(Number(orderId), {
        orderItemIds: order.items.map((item) => item.orderItemId),
      });
      if (error) {
        message.error('获取退票报价失败');
        return;
      }
      if (data?.success && data?.data) {
        setRefundQuote(data.data);
      } else {
        message.error(data?.message || '获取退票报价失败');
      }
    } catch (error) {
      console.error('获取退票报价失败:', error);
      message.error('获取退票报价失败');
    } finally {
      setRefundLoading(false);
    }
  };

  const handleSubmitRefund = async () => {
    if (!order) return;
    if (!refundReason.trim()) {
      message.warning('请填写退票原因');
      return;
    }
    setRefundSubmitting(true);
    try {
      const { data, error } = await refundAPI.applyRefund(Number(orderId), {
        orderItemIds: order.items.map((item) => item.orderItemId),
        reason: refundReason,
      });
      if (error) {
        message.error('提交退票申请失败');
        return;
      }
      if (data?.success && data?.data) {
        message.success('退票申请已提交，请等待审核');
        setRefundModalVisible(false);
        setRefundReason('');
        fetchOrder();
      } else {
        message.error(data?.message || '提交退票申请失败');
      }
    } catch (error) {
      console.error('提交退票申请失败:', error);
      message.error('提交退票申请失败');
    } finally {
      setRefundSubmitting(false);
    }
  };

  // ========== 改签功能 ==========
  const fetchTargetShows = async () => {
    setLoadingShows(true);
    try {
      const { data, error } = await showAPI.getShows({
        PageSize: 50,
        Status: 'PUBLISHED',
      });
      if (error) {
        message.error('获取演出列表失败');
        return;
      }
      if (data?.success && data?.data) {
        setTargetShows(data.data.items || []);
      } else {
        message.error(data?.message || '获取演出列表失败');
      }
    } catch (error) {
      console.error('获取演出列表失败:', error);
      message.error('获取演出列表失败');
    } finally {
      setLoadingShows(false);
    }
  };

  const fetchTargetSessions = async (showId: number) => {
    setLoadingSessions(true);
    setTargetSessions([]);
    setTargetSessionId(null);
    try {
      const { data, error } = await showSessionAPI.getShowSessions(showId);
      if (error) {
        message.error('获取场次失败');
        return;
      }
      if (data?.success && data?.data) {
        const sessions = data.data.filter((s: any) => s.sessionStatus === 'ONSALE');
        setTargetSessions(sessions);
        if (sessions.length === 0) {
          message.warning('该演出暂无可用场次');
        }
      } else {
        message.error(data?.message || '获取场次失败');
      }
    } catch (error) {
      console.error('获取场次失败:', error);
      message.error('获取场次失败');
    } finally {
      setLoadingSessions(false);
    }
  };

  const handleGetExchangeQuote = async () => {
    if (!order || !targetSessionId) {
      message.warning('请先选择目标场次');
      return;
    }
    if (selectedTargetSeats.length === 0) {
      message.warning('请先选择目标座位');
      return;
    }
    setExchangeLoading(true);
    try {
      const { data, error } = await exchangeAPI.getExchangeQuote(Number(orderId), {
        targetSessionId: targetSessionId,
        targetItems: selectedTargetSeats.map((seat) => ({
          originalOrderItemId: seat.originalOrderItemId || order.items[0]?.orderItemId || 0,
          seatId: seat.seatId,
          priceStrategyId: seat.priceStrategyId || 0,
          lockToken: seat.lockToken || '',
        })),
      });
      if (error) {
        message.error('获取改签报价失败');
        return;
      }
      if (data?.success && data?.data) {
        setExchangeQuote(data.data);
        message.success('改签报价获取成功');
      } else {
        message.error(data?.message || '获取改签报价失败');
      }
    } catch (error) {
      console.error('获取改签报价失败:', error);
      message.error('获取改签报价失败');
    } finally {
      setExchangeLoading(false);
    }
  };

  const handleSubmitExchange = async () => {
    if (!order || !targetSessionId || selectedTargetSeats.length === 0) {
      message.warning('请选择完整的目标场次和座位');
      return;
    }
    setExchangeSubmitting(true);
    try {
      const { data, error } = await exchangeAPI.applyExchange(Number(orderId), {
        targetSessionId: targetSessionId,
        targetItems: selectedTargetSeats.map((seat) => ({
          originalOrderItemId: seat.originalOrderItemId || order.items[0]?.orderItemId || 0,
          seatId: seat.seatId,
          priceStrategyId: seat.priceStrategyId || 0,
          lockToken: seat.lockToken || '',
        })),
        reason: exchangeReason || null,
      });
      if (error) {
        message.error('提交改签申请失败');
        return;
      }
      if (data?.success && data?.data) {
        message.success('改签申请已提交，请等待审核');
        setExchangeModalVisible(false);
        setExchangeReason('');
        setTargetShowId(null);
        setTargetSessionId(null);
        setSelectedTargetSeats([]);
        setExchangeQuote(null);
        fetchOrder();
      } else {
        message.error(data?.message || '提交改签申请失败');
      }
    } catch (error) {
      console.error('提交改签申请失败:', error);
      message.error('提交改签申请失败');
    } finally {
      setExchangeSubmitting(false);
    }
  };

  // 弹窗打开时自动加载数据
  useEffect(() => {
    if (refundModalVisible) {
      fetchRefundQuote();
    }
  }, [refundModalVisible]);

  useEffect(() => {
    if (exchangeModalVisible) {
      fetchTargetShows();
    }
  }, [exchangeModalVisible]);

  // ========== 渲染 ==========
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
  const canRefund = order.orderStatus === 'PAID' || order.orderStatus === 'ISSUED';

  return (
    <div className="order-detail-container">
      <Card>
        <div className="order-detail-header">
          <Title level={3}>订单详情</Title>
          <Tag color={statusInfo.color}>{statusInfo.text}</Tag>
        </div>

        {loadingDetail ? (
          <div style={{ textAlign: 'center', padding: 12 }}>
            <Spin size="small" /> 加载演出信息...
          </div>
        ) : showDetail ? (
          <div style={{ marginBottom: 16, padding: '12px 16px', background: '#fafafa', borderRadius: 8 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 16 }}>
              {showDetail.posterUrl && (
                <img
                  src={showDetail.posterUrl}
                  alt={showDetail.showName}
                  style={{ width: 80, height: 80, objectFit: 'cover', borderRadius: 8 }}
                />
              )}
              <div>
                <div style={{ fontSize: 18, fontWeight: 'bold' }}>{showDetail.showName}</div>
                <div style={{ color: '#888', marginTop: 4 }}>
                  {sessionDetail && (
                    <>
                      <div>场次：{new Date(sessionDetail.startTime).toLocaleString('zh-CN')}</div>
                      <div>状态：<Tag color={sessionDetail.sessionStatus === 'ONSALE' ? 'green' : 'default'}>
                        {sessionDetail.sessionStatus}
                      </Tag></div>
                    </>
                  )}
                </div>
              </div>
            </div>
          </div>
        ) : (
          <div style={{ color: '#999', marginBottom: 16 }}>场次信息：ID {order.sessionId}</div>
        )}

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

        <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
          <Button type="primary" onClick={() => navigate('/order')}>
            返回订单列表
          </Button>
          {canRefund && (
            <>
              <Button danger onClick={() => setRefundModalVisible(true)}>
                申请退票
              </Button>
              <Button type="primary" onClick={() => setExchangeModalVisible(true)}>
                申请改签
              </Button>
            </>
          )}
        </div>
      </Card>

      {/* ========== 退票弹窗 ========== */}
      <Modal
        title="申请退票"
        open={refundModalVisible}
        onCancel={() => {
          setRefundModalVisible(false);
          setRefundReason('');
        }}
        footer={[
          <Button key="cancel" onClick={() => setRefundModalVisible(false)}>
            取消
          </Button>,
          <Button key="submit" type="primary" danger loading={refundSubmitting} onClick={handleSubmitRefund}>
            提交退票申请
          </Button>,
        ]}
      >
        {refundLoading ? (
          <div style={{ textAlign: 'center', padding: 20 }}>
            <Spin />
          </div>
        ) : refundQuote ? (
          <div>
            <p>
              <strong>可退金额：</strong>
              <span style={{ color: '#ff4d4f', fontSize: 20, fontWeight: 'bold' }}>
                ¥{refundQuote.actualRefund}
              </span>
            </p>
            <p>
              <strong>退票费率：</strong>
              {(refundQuote.feeRate * 100).toFixed(1)}%
            </p>
            <p>
              <strong>服务费：</strong>¥{refundQuote.appliedServiceFee}
            </p>
            <p style={{ color: '#888', fontSize: 14 }}>
              退票申请提交后需要管理员审核，审核通过后退款将原路返回。
            </p>
            <Input.TextArea
              placeholder="请填写退票原因（必填）"
              value={refundReason}
              onChange={(e) => setRefundReason(e.target.value)}
              rows={3}
              maxLength={200}
              showCount
            />
          </div>
        ) : (
          <Empty description="获取退票信息失败，请稍后重试" />
        )}
      </Modal>

      {/* ========== 改签弹窗 ========== */}
      <Modal
        title="申请改签"
        open={exchangeModalVisible}
        onCancel={() => {
          setExchangeModalVisible(false);
          setTargetShowId(null);
          setTargetSessionId(null);
          setSelectedTargetSeats([]);
          setExchangeQuote(null);
          setExchangeReason('');
        }}
        width={700}
        footer={[
          <Button key="cancel" onClick={() => setExchangeModalVisible(false)}>
            取消
          </Button>,
          <Button
            key="submit"
            type="primary"
            loading={exchangeSubmitting}
            disabled={!targetSessionId || selectedTargetSeats.length === 0}
            onClick={handleSubmitExchange}
          >
            提交改签申请
          </Button>,
        ]}
      >
        <div>
          <p style={{ marginBottom: 12 }}>
            <strong>原订单：</strong>
            订单号 {order.orderNo} | 票数 {order.ticketCount}
          </p>
          <Divider />

          <div style={{ marginBottom: 16 }}>
            <p>
              <strong>选择目标演出</strong>
            </p>
            <Select
              style={{ width: '100%' }}
              placeholder="请选择目标演出"
              value={targetShowId}
              onChange={(value) => {
                setTargetShowId(value);
                fetchTargetSessions(value);
              }}
              loading={loadingShows}
              showSearch
              optionFilterProp="children"
            >
              {targetShows.map((show) => (
                <Select.Option key={show.showId} value={show.showId}>
                  {show.showName}
                </Select.Option>
              ))}
            </Select>
          </div>

          {targetSessions.length > 0 && (
            <div style={{ marginBottom: 16 }}>
              <p>
                <strong>选择目标场次</strong>
              </p>
              <Select
                style={{ width: '100%' }}
                placeholder="请选择目标场次"
                value={targetSessionId}
                onChange={(value) => {
                  setTargetSessionId(value);
                  setSelectedTargetSeats([]);
                  setExchangeQuote(null);
                }}
                loading={loadingSessions}
              >
                {targetSessions.map((session) => (
                  <Select.Option key={session.sessionId} value={session.sessionId}>
                    {new Date(session.startTime).toLocaleString('zh-CN')}
                  </Select.Option>
                ))}
              </Select>
            </div>
          )}

          {targetSessionId && (
            <div style={{ marginBottom: 16 }}>
              <p>
                <strong>选择目标座位</strong>
              </p>
              <Button
                type="dashed"
                block
                onClick={() => {
                  // 跳转时带上目标场次 ID，并标记为改签模式
                  navigate(`/seat-selection/${targetShowId}`, {
                    state: {
                      fromExchange: true,
                      orderId: order.orderId,
                      preSelectedSessionId: targetSessionId,  // ← 关键：预选场次 ID
                    },
                  });
                }}
              >
                {selectedTargetSeats.length > 0
                  ? `已选 ${selectedTargetSeats.length} 个座位（点击重新选择）`
                  : '点击选择目标座位'}
              </Button>
              {selectedTargetSeats.length > 0 && (
                <div style={{ marginTop: 8, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                  {selectedTargetSeats.map((seat, idx) => (
                    <Tag
                      key={idx}
                      closable
                      onClose={() => {
                        setSelectedTargetSeats((prev) => prev.filter((_, i) => i !== idx));
                        setExchangeQuote(null);
                      }}
                    >
                      {seat.rowCode || seat.seatId}
                    </Tag>
                  ))}
                </div>
              )}
            </div>
          )}

          {exchangeLoading ? (
            <div style={{ textAlign: 'center', padding: 20 }}>
              <Spin />
            </div>
          ) : exchangeQuote ? (
            <div style={{ background: '#f5f5f5', padding: 16, borderRadius: 8 }}>
              <p>
                <strong>原票价：</strong>¥{exchangeQuote.origDeduction}
              </p>
              <p>
                <strong>新票价：</strong>¥{exchangeQuote.targetAmount}
              </p>
              <p>
                <strong>差价：</strong>
                <span style={{ color: exchangeQuote.priceDiff > 0 ? '#ff4d4f' : '#52c41a' }}>
                  ¥{exchangeQuote.priceDiff}
                </span>
              </p>
              <p>
                <strong>改签费：</strong>¥{exchangeQuote.exchangeFee}
              </p>
              <p style={{ color: '#ff4d4f', fontWeight: 'bold', fontSize: 16 }}>
                需补差价：¥{exchangeQuote.amountDue}
              </p>
            </div>
          ) : targetSessionId && selectedTargetSeats.length > 0 ? (
            <Button type="primary" onClick={handleGetExchangeQuote} loading={exchangeLoading}>
              获取改签报价
            </Button>
          ) : null}

          <Input.TextArea
            placeholder="改签原因（选填）"
            value={exchangeReason}
            onChange={(e) => setExchangeReason(e.target.value)}
            rows={2}
            style={{ marginTop: 16 }}
            maxLength={200}
            showCount
          />
        </div>
      </Modal>
    </div>
  );
};

export default OrderDetail;
