import { useState, useEffect } from 'react';
import { useParams, useNavigate, useLocation } from 'react-router-dom';
import {
  Descriptions,
  Tag,
  Spin,
  Button,
  message,
  notification,
  Typography,
  Card,
  Divider,
  Modal,
  Input,
  Empty,
  Select,
} from 'antd';
import { orderAPI, refundAPI, exchangeAPI, showAPI, showSessionAPI, sessionAPI } from '@/api/requests';
import type { components } from '@/api/types';
import type { OrderResponse } from '@/types/api';
import {
  ensureRealtimeConnection,
  subscribeRefundStatusChanged,
} from '@/realtime/orderNotifications';
import './OrderDetail.css';

const { Title, Text } = Typography;

// ========== 生成类型（openapi.json） ==========
type ShowDto = components['schemas']['ShowDto'];
type ShowSessionDto = components['schemas']['ShowSessionDto'];
type RefundQuoteResponse = components['schemas']['RefundQuoteResponse'];
type ExchangeQuoteResponse = components['schemas']['ExchangeQuoteResponse'];
type ExchangeSummaryResponse = components['schemas']['ExchangeSummaryResponse'];

const STATUS_MAP: Record<string, { color: string; text: string }> = {
  PENDING_PAY: { color: 'orange', text: '待支付' },
  PAID: { color: 'green', text: '已支付' },
  ISSUED: { color: 'blue', text: '已出票' },
  PART_REFUND: { color: 'purple', text: '部分退款' },
  REFUNDED: { color: 'red', text: '已退款' },
  CANCELLED: { color: 'red', text: '已取消' },
};

const ITEM_STATUS_MAP: Record<string, { color: string; text: string }> = {
  NORMAL: { color: 'green', text: '正常' },
  REFUNDING: { color: 'orange', text: '退款中' },
  REFUNDED: { color: 'default', text: '已退款' },
  EXCHANGING: { color: 'warning', text: '换票中' },
  EXCHANGED: { color: 'default', text: '已换票' },
};

const EXCHANGE_APPROVE_STATUS_MAP: Record<string, { color: string; text: string }> = {
  PENDING: { color: 'orange', text: '待审核' },
  APPROVED: { color: 'green', text: '审核通过' },
  REJECTED: { color: 'red', text: '已驳回' },
};

const EXCHANGE_STATUS_MAP: Record<string, { color: string; text: string }> = {
  PENDING: { color: 'orange', text: '待处理' },
  PROCESSING: { color: 'blue', text: '处理中' },
  COMPLETED: { color: 'green', text: '已完成' },
  FAILED: { color: 'red', text: '已失败' },
};

interface SessionDetail {
  sessionId: number;
  showId: number;
  startTime: string;
  endTime: string;
  sessionStatus: string;
  seatMapId: number;
}

/** 从选座页带回的目标座位（含与原票明细的 1:1 映射） */
interface ExchangeTargetSeat {
  seatId: number;
  rowCode?: string;
  colIndex?: number;
  originalOrderItemId: number | null;
  priceStrategyId: number;
  lockToken: string;
}

const OrderDetail = () => {
  const { orderId } = useParams<{ orderId: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const [order, setOrder] = useState<OrderResponse | null>(null);
  const [loading, setLoading] = useState(true);

  // 场次和演出详情
  const [sessionDetail, setSessionDetail] = useState<SessionDetail | null>(null);
  const [showDetail, setShowDetail] = useState<ShowDto | null>(null);
  const [loadingDetail, setLoadingDetail] = useState(false);

  // ========== 退票相关 ==========
  const [refundModalVisible, setRefundModalVisible] = useState(false);
  const [refundQuote, setRefundQuote] = useState<RefundQuoteResponse | null>(null);
  const [refundReason, setRefundReason] = useState('');
  const [refundLoading, setRefundLoading] = useState(false);
  const [refundSubmitting, setRefundSubmitting] = useState(false);

  // ========== 改签相关 ==========
  const [exchangeModalVisible, setExchangeModalVisible] = useState(false);
  const [exchangeQuote, setExchangeQuote] = useState<ExchangeQuoteResponse | null>(null);
  const [exchangeLoading, setExchangeLoading] = useState(false);
  const [exchangeSubmitting, setExchangeSubmitting] = useState(false);
  const [exchangeReason, setExchangeReason] = useState('');
  // 改签目标选择（后端契约：仅支持同演出换场次，1:1 换票）
  const [targetShows, setTargetShows] = useState<ShowDto[]>([]);
  const [targetShowId, setTargetShowId] = useState<number | null>(null);
  const [targetSessions, setTargetSessions] = useState<ShowSessionDto[]>([]);
  const [targetSessionId, setTargetSessionId] = useState<number | null>(null);
  const [selectedTargetSeats, setSelectedTargetSeats] = useState<ExchangeTargetSeat[]>([]);
  const [loadingShows, setLoadingShows] = useState(false);
  const [loadingSessions, setLoadingSessions] = useState(false);
  // 从选座页返回后自动触发报价（用状态标记，避免首帧闭包问题）
  const [pendingAutoQuote, setPendingAutoQuote] = useState(false);

  // ========== 改签申请列表（待审核 / 待支付差价链路） ==========
  const [exchangeRequests, setExchangeRequests] = useState<ExchangeSummaryResponse[]>([]);
  const [exchangeListLoading, setExchangeListLoading] = useState(false);
  const [payingExchange, setPayingExchange] = useState(false);

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
        fetchExchangeRequests();
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

  // ========== 实时退款状态（审核通过/完成/拒绝） ==========
  useEffect(() => {
    void ensureRealtimeConnection();
    const unsubscribe = subscribeRefundStatusChanged((event) => {
      if (String(event.orderId) !== orderId) return;
      const texts: Record<string, string> = {
        PROCESSING: '退款处理中，请耐心等待',
        COMPLETED: '退款已完成，款项将原路退回',
        FAILED: '退款失败，请联系客服',
      };
      notification.info({
        message: `退款单 ${event.refundNo} 状态更新`,
        description: texts[event.refundStatus] || `退款状态：${event.refundStatus}`,
      });
      fetchOrder();
    });
    return unsubscribe;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [orderId]);

  // ========== 检测从选座页返回的改签数据 ==========
  useEffect(() => {
    if (location.state?.fromExchange) {
      const { targetSeats, targetSessionId: sessionId } = location.state;
      if (targetSeats && targetSeats.length > 0 && sessionId) {
        setSelectedTargetSeats(targetSeats);
        setTargetSessionId(sessionId);
        // 自动重新打开改签弹窗，展示报价并可直接提交
        setExchangeModalVisible(true);
        // 先置标记，待 order/targetSessionId/selectedTargetSeats 全部就绪后再自动报价
        setPendingAutoQuote(true);
      }
      // 清除 state，防止刷新页面后重复触发
      window.history.replaceState({}, document.title);
    }
  }, [location.state]);

  // ========== 自动获取改签报价 ==========
  // 修复：setTimeout 里捕获的是首帧 render 闭包（order/targetSessionId 恒为 null），
  // 改为依赖最新 state 的 effect，state 就绪后只会触发一次报价。
  useEffect(() => {
    if (pendingAutoQuote && order && targetSessionId && selectedTargetSeats.length > 0) {
      setPendingAutoQuote(false);
      handleGetExchangeQuote();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pendingAutoQuote, order, targetSessionId, selectedTargetSeats]);

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
      // 后端契约：EXCHANGE_CROSS_SHOW_NOT_ALLOWED —— 改签仅支持同演出换场次，
      // 目标演出固定为原订单演出，不再让用户选择跨演出。
      let showId = sessionDetail?.showId;
      if (!showId && order?.sessionId) {
        const { data } = await sessionAPI.getSessionSeatMap(order.sessionId);
        showId = Number(data?.data?.showId) || undefined;
      }
      if (!showId) {
        setTargetShows([]);
        return;
      }
      const { data, error } = await showAPI.getShowDetail(showId);
      if (error) {
        message.error('获取演出信息失败');
        return;
      }
      if (data?.success && data?.data) {
        setTargetShows([data.data]);
        setTargetShowId(Number(data.data.showId));
        fetchTargetSessions(Number(data.data.showId));
      } else {
        message.error(data?.message || '获取演出信息失败');
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
        // 仅保留在售场次，并排除原场次（改签意义为换到其他场次）
        const sessions = data.data
          .filter((s: any) => s.sessionStatus === 'ONSALE' && Number(s.sessionId) !== order?.sessionId);
        setTargetSessions(sessions);
        // 保留从选座页带回的目标场次（若仍在售），避免自动报价被打断
        setTargetSessionId((prev) =>
          prev && sessions.some((s) => Number(s.sessionId) === prev) ? prev : null
        );
        if (sessions.length === 0) {
          message.warning('该演出暂无其他可选场次');
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

  /**
   * 组装改签 targetItems，强制 1:1 映射：
   * 后端契约（ExchangeApplicationService.QuoteAsync）：
   *  - originalItems.Count != originalItemIds.Length → EXCHANGE_ITEM_NOT_ELIGIBLE
   *  - 同一 OrderItemId 重复 → ToDictionaryAsync 抛异常（500）
   * 因此：换几张票必须选几个座位，且每个目标座位必须携带唯一的原票明细 ID。
   */
  const buildExchangeTargetItems = (): { originalOrderItemId: number; seatId: number; priceStrategyId: number; lockToken: string }[] | null => {
    if (!order) return null;
    if (selectedTargetSeats.length !== order.items.length) {
      message.warning(`改签需要选择 ${order.items.length} 个目标座位（与原票一一对应），当前已选 ${selectedTargetSeats.length} 个`);
      return null;
    }
    const missing = selectedTargetSeats.filter((seat) => !seat.originalOrderItemId);
    if (missing.length > 0) {
      message.warning('部分目标座位缺少原票明细映射，请重新选择目标座位');
      return null;
    }
    const seen = new Set<number>();
    const items = selectedTargetSeats.map((seat) => ({
      originalOrderItemId: seat.originalOrderItemId as number,
      seatId: seat.seatId,
      priceStrategyId: seat.priceStrategyId,
      lockToken: seat.lockToken,
    }));
    for (const item of items) {
      if (seen.has(item.originalOrderItemId)) {
        message.warning('存在重复的原票明细映射，请重新选择目标座位');
        return null;
      }
      seen.add(item.originalOrderItemId);
    }
    return items;
  };

  const handleGetExchangeQuote = async () => {
    if (!order || !targetSessionId) {
      message.warning('请先选择目标场次');
      return;
    }
    const targetItems = buildExchangeTargetItems();
    if (!targetItems) return;
    setExchangeLoading(true);
    try {
      const { data, error } = await exchangeAPI.getExchangeQuote(Number(orderId), {
        targetSessionId,
        targetItems,
      });
      if (error) {
        handleExchangeError(error, '获取改签报价失败');
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
    if (!order || !targetSessionId) {
      message.warning('请选择完整的目标场次和座位');
      return;
    }
    const targetItems = buildExchangeTargetItems();
    if (!targetItems) return;
    setExchangeSubmitting(true);
    try {
      const { data, error } = await exchangeAPI.applyExchange(Number(orderId), {
        targetSessionId,
        targetItems,
        reason: exchangeReason || null,
      });
      if (error) {
        handleExchangeError(error, '提交改签申请失败');
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
        // 刷新订单并加载改签申请列表（进入「待审核」状态面板）
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

  /** 统一处理改签相关错误（锁座 TTL 过期等场景引导重选） */
  const handleExchangeError = (error: any, fallback: string) => {
    const code = error?.code || error?.data?.code;
    if (code === 'EXCHANGE_SEAT_LOCK_INVALID' || code === 'EXCHANGE_TARGET_SEAT_UNAVAILABLE') {
      message.error('目标座位锁定已失效（锁定期 600 秒）或不可用，请重新选择目标座位');
      setSelectedTargetSeats([]);
      setExchangeQuote(null);
    } else {
      message.error(error?.message || fallback);
    }
  };

  // ========== 改签申请列表与差价支付 ==========
  const fetchExchangeRequests = async () => {
    if (!orderId) return;
    setExchangeListLoading(true);
    try {
      const { data, error } = await exchangeAPI.getExchangeList(Number(orderId), {
        Page: 1,
        PageSize: 20,
      });
      if (error) return;
      if (data?.success && data?.data) {
        const items = (data.data.items || []).map((item: any) => ({
          ...item,
          exchangeId: Number(item.exchangeId),
          originalOrderId: Number(item.originalOrderId),
          childOrderId: Number(item.childOrderId),
          amountDue: Number(item.amountDue),
        }));
        setExchangeRequests(items);
      }
    } catch (error) {
      console.error('获取改签申请列表失败:', error);
    } finally {
      setExchangeListLoading(false);
    }
  };

  // 存在未终结的改签申请时，轮询改签列表（审核通过后可支付差价）
  useEffect(() => {
    if (!order || exchangeRequests.length === 0) return;
    const hasActive = exchangeRequests.some(
      (ex) => ex.approveStatus === 'PENDING' || ex.exchangeStatus === 'PROCESSING',
    );
    if (!hasActive) return;
    const timer = setInterval(() => void fetchExchangeRequests(), 10_000);
    return () => clearInterval(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [order, exchangeRequests]);

  const handlePayExchange = async (exchange: ExchangeSummaryResponse) => {
    setPayingExchange(true);
    try {
      const { data, error } = await exchangeAPI.payExchange(Number(exchange.exchangeId), {
        payChannel: 'WECHAT',
        result: 'SUCCESS',
      });
      if (error) {
        message.error(error?.message || '差价支付失败');
        return;
      }
      if (data?.success && data?.data) {
        message.success('差价支付成功，改签完成');
        fetchExchangeRequests();
        fetchOrder();
      } else {
        message.error(data?.message || '差价支付失败');
      }
    } catch (error) {
      console.error('差价支付失败:', error);
      message.error('差价支付失败');
    } finally {
      setPayingExchange(false);
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
                      <Tag color={ITEM_STATUS_MAP[item.itemStatus]?.color || 'default'}>
                        {ITEM_STATUS_MAP[item.itemStatus]?.text || item.itemStatus}
                      </Tag>
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

        {/* ========== 改签申请状态面板 ========== */}
        {exchangeRequests.length > 0 && (
          <>
            <Divider />
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
              <Title level={5} style={{ margin: 0 }}>改签申请</Title>
              {exchangeListLoading && <Spin size="small" />}
            </div>
            <div style={{ marginTop: 12, display: 'flex', flexDirection: 'column', gap: 12 }}>
              {exchangeRequests.map((ex) => {
                const approveInfo = EXCHANGE_APPROVE_STATUS_MAP[ex.approveStatus] || {
                  color: 'default',
                  text: ex.approveStatus,
                };
                const execInfo = EXCHANGE_STATUS_MAP[ex.exchangeStatus] || {
                  color: 'default',
                  text: ex.exchangeStatus,
                };
                const canPay =
                  ex.approveStatus === 'APPROVED' && ex.exchangeStatus === 'PROCESSING';
                return (
                  <div
                    key={ex.exchangeId}
                    style={{
                      border: '1px solid #f0f0f0',
                      borderRadius: 8,
                      padding: '12px 16px',
                      background: '#fafafa',
                    }}
                  >
                    <div
                      style={{
                        display: 'flex',
                        justifyContent: 'space-between',
                        alignItems: 'center',
                        flexWrap: 'wrap',
                        gap: 8,
                      }}
                    >
                      <div>
                        <span style={{ fontWeight: 600 }}>改签单号：{ex.exchangeNo}</span>
                        <Tag color={approveInfo.color} style={{ marginLeft: 8 }}>
                          {approveInfo.text}
                        </Tag>
                        <Tag color={execInfo.color}>{execInfo.text}</Tag>
                      </div>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                        <span>
                          需补差价：
                          <b style={{ color: '#ff4d4f' }}>¥{ex.amountDue}</b>
                        </span>
                        {canPay && (
                          <Button
                            type="primary"
                            size="small"
                            loading={payingExchange}
                            onClick={() => handlePayExchange(ex)}
                          >
                            支付差价
                          </Button>
                        )}
                      </div>
                    </div>
                    <div style={{ color: '#888', fontSize: 13, marginTop: 6 }}>
                      申请时间：{new Date(ex.createTime).toLocaleString('zh-CN')}
                      {ex.approveStatus === 'PENDING' && ' · 管理员审核中，请耐心等待'}
                      {canPay && ' · 审核已通过，请支付差价完成改签'}
                      {ex.exchangeStatus === 'COMPLETED' && ' · 改签已完成'}
                      {ex.approveStatus === 'REJECTED' && ' · 改签申请未通过'}
                    </div>
                  </div>
                );
              })}
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
              {(Number(refundQuote.feeRate) * 100).toFixed(1)}%
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
            disabled={
              !targetSessionId ||
              selectedTargetSeats.length !== order.items.length ||
              !exchangeQuote
            }
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
              <strong>目标演出</strong>
            </p>
            {loadingShows ? (
              <Spin size="small" />
            ) : (
              <>
                {targetShows.length > 0 && (
                  <div style={{ padding: '8px 12px', background: '#fafafa', borderRadius: 8 }}>
                    {targetShows[0].showName}
                    <div style={{ color: '#888', fontSize: 13, marginTop: 2 }}>
                      改签仅支持同场演出换场次（跨演出改签暂不支持）
                    </div>
                  </div>
                )}
              </>
            )}
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
                  <Select.Option key={session.sessionId} value={Number(session.sessionId)}>
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
                <Text type="secondary" style={{ fontSize: 13, marginLeft: 8 }}>
                  需选择 {order.items.length} 个座位（与原票一一对应）
                </Text>
              </p>
              <Button
                type="dashed"
                block
                onClick={() => {
                  // 跳转时带上目标场次 ID 与原票明细映射，标记为改签模式
                  navigate(`/seat-selection/${targetShowId}`, {
                    state: {
                      fromExchange: true,
                      orderId: order.orderId,
                      preSelectedSessionId: targetSessionId,
                      originalItems: order.items.map((item) => ({
                        orderItemId: item.orderItemId,
                        seatId: item.seatId,
                        unitPrice: item.unitPrice,
                      })),
                    },
                  });
                }}
              >
                {selectedTargetSeats.length > 0
                  ? `已选 ${selectedTargetSeats.length}/${order.items.length} 个座位（点击重新选择）`
                  : `点击选择目标座位（需选 ${order.items.length} 个）`}
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
                <span style={{ color: Number(exchangeQuote.priceDiff) > 0 ? '#ff4d4f' : '#52c41a' }}>
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
          ) : targetSessionId && selectedTargetSeats.length === order.items.length ? (
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