import { useState, useEffect } from 'react';
import { useParams, useNavigate, useLocation } from 'react-router-dom';
import { Button, message, Typography, Spin, Radio } from 'antd';
import { sessionAPI, showSessionAPI, orderAPI, seatLockAPI } from '@/api/requests';
import type { SessionSeatMapDto, SessionSeatMapSeatDto, PricingStrategyDto } from '@/types/api';
import './SeatSelection.css';

const { Title, Text } = Typography;

// 座位状态映射（后端返回 -> 前端显示）
const SEAT_STATUS_MAP: Record<string, { label: string; className: string }> = {
  AVAILABLE: { label: '可选', className: 'available' },
  LOCKED: { label: '锁定中', className: 'locked' },
  SOLD: { label: '已售', className: 'sold' },
  UNAVAILABLE: { label: '不可用', className: 'unavailable' },
};

// 场次信息
interface SessionInfo {
  sessionId: number;
  startTime: string;
  endTime: string;
  sessionStatus: string;
}

const SeatSelection = () => {
  const { eventId } = useParams<{ eventId: string }>();
  const navigate = useNavigate();
  const location = useLocation();

  // ========== 检测是否来自改签 ==========
  const fromExchange = location.state?.fromExchange || false;
  const exchangeOrderId = location.state?.orderId || null;
  const preSelectedSessionId = location.state?.preSelectedSessionId || null;
  // 原订单票品明细（来自订单详情页），用于改签 1:1 映射：每个目标座位对应一张原票
  const exchangeOriginalItems: Array<{ orderItemId: number; seatId: number; unitPrice?: number }> =
    location.state?.originalItems || null;
  const exchangeOriginalCount = exchangeOriginalItems?.length ?? 0;

  const [loading, setLoading] = useState(true);
  const [sessions, setSessions] = useState<SessionInfo[]>([]);
  const [selectedSessionId, setSelectedSessionId] = useState<number | null>(
    preSelectedSessionId ? Number(preSelectedSessionId) : null
  );
  const [seatMap, setSeatMap] = useState<SessionSeatMapDto | null>(null);
  const [pricingStrategies, setPricingStrategies] = useState<PricingStrategyDto[]>([]);

  // 座位矩阵
  const [seats, setSeats] = useState<SessionSeatMapSeatDto[]>([]);
  const [selectedSeats, setSelectedSeats] = useState<number[]>([]);

  const [submitting, setSubmitting] = useState(false);

  // ========== 获取场次列表 ==========
  const fetchSessions = async () => {
    try {
      const { data, error } = await showSessionAPI.getShowSessions(Number(eventId));
      if (error) {
        message.error('获取场次失败');
        return;
      }
      if (data?.success && data?.data) {
        const sessions = data.data.map((s: any) => ({
          ...s,
          sessionId: Number(s.sessionId),
        }));
        setSessions(sessions);

        // 如果有预选场次，检查是否在列表中
        if (preSelectedSessionId) {
          const exists = sessions.some((s) => s.sessionId === Number(preSelectedSessionId));
          if (!exists) {
            message.warning('目标场次已不可用，请重新选择');
            if (sessions.length > 0) {
              setSelectedSessionId(sessions[0].sessionId);
            }
          }
        } else if (sessions.length > 0) {
          // 没有预选场次，默认选中第一个
          setSelectedSessionId(sessions[0].sessionId);
        } else {
          message.warning('该演出暂无场次');
        }
      } else {
        message.error(data?.message || '获取场次失败');
      }
    } catch (error: any) {
      console.error('获取场次失败:', error);
      message.error(error.message || '获取场次失败');
    }
  };

  // ========== 获取座位图 ==========
  const fetchSeatMap = async (sessionId: number) => {
    setLoading(true);
    try {
      const { data, error } = await sessionAPI.getSessionSeatMap(sessionId);
      if (error) {
        message.error('获取座位图失败');
        setLoading(false);
        return;
      }
      if (data?.success && data?.data) {
        const seatMapData = {
          ...data.data,
          sessionId: Number(data.data.sessionId),
          showId: Number(data.data.showId),
          seatMapId: Number(data.data.seatMapId),
        };
        setSeatMap(seatMapData as any);

        const allSeats: SessionSeatMapSeatDto[] = [];
        data.data.seatMap?.sections?.forEach((section: any) => {
          section.seats?.forEach((seat: SessionSeatMapSeatDto) => {
            allSeats.push({
              ...seat,
              sectionName: section.sectionName,
              sectionColor: section.sectionColor,
            });
          });
        });
        setSeats(allSeats);
        setSelectedSeats([]);
      } else {
        message.error(data?.message || '获取座位图失败');
      }
    } catch (error: any) {
      console.error('获取座位图失败:', error);
      message.error(error.message || '获取座位图失败');
    } finally {
      setLoading(false);
    }
  };

  // ========== 获取定价策略 ==========
  const fetchPricingStrategies = async (sessionId: number) => {
    try {
      const { data, error } = await showSessionAPI.getPricingStrategies(sessionId);
      if (error) {
        console.error('获取定价策略失败:', error);
        message.error('票价信息加载失败，请稍后重试或联系客服');
        return;
      }
      if (data?.success && data?.data) {
        const strategies = data.data.map((s: any) => ({
          ...s,
          priceStrategyId: Number(s.priceStrategyId),
          seatSectionId: Number(s.seatSectionId),
          price: Number(s.price),
        }));
        setPricingStrategies(strategies);
      } else {
        message.warning('该场次暂无票价配置，请选择其他场次');
      }
    } catch (error) {
      console.error('获取定价策略失败:', error);
      message.error('票价信息加载失败，请稍后重试');
    }
  };

  // ========== 初始化加载 ==========
  useEffect(() => {
    if (eventId) {
      fetchSessions();
    }
  }, [eventId]);

  // ========== 场次变化时加载座位图 ==========
  useEffect(() => {
    if (selectedSessionId) {
      fetchSeatMap(selectedSessionId);
      fetchPricingStrategies(selectedSessionId);
    }
  }, [selectedSessionId]);

  // ========== 座位点击处理 ==========
  const handleSeatClick = (seat: SessionSeatMapSeatDto) => {
    if (!seat.isSellable) {
      message.warning('该座位暂不可售');
      return;
    }

    const status = (seat.availabilityStatus || '').toUpperCase();
    if (status !== 'AVAILABLE') {
      if (status === 'SOLD') {
        message.warning('该座位已被选走');
      } else if (status === 'LOCKED') {
        message.warning('该座位已被锁定');
      } else {
        message.warning('该座位不可用');
      }
      return;
    }

    const seatId = seat.seatId;
    if (selectedSeats.includes(seatId)) {
      setSelectedSeats(selectedSeats.filter((id) => id !== seatId));
      return;
    }
    // 改签模式：目标座位数量必须与原票一致（后端强制 1:1 映射）
    if (fromExchange && exchangeOriginalCount > 0 && selectedSeats.length >= exchangeOriginalCount) {
      message.warning(`改签目标座位数量需与原票一致（${exchangeOriginalCount} 个）`);
      return;
    }
    setSelectedSeats([...selectedSeats, seatId]);
  };

  // ========== 获取座位价格 ==========
  const getSeatPrice = (seat: SessionSeatMapSeatDto): number => {
    const strategy = pricingStrategies.find(
      (s) => s.seatSectionId === seat.seatSectionId
    );
    return strategy?.price || 0;
  };

  // ========== 计算总价 ==========
  const getTotalPrice = () => {
    let total = 0;
    selectedSeats.forEach((seatId) => {
      const seat = seats.find((s) => s.seatId === seatId);
      if (seat) {
        total += getSeatPrice(seat);
      }
    });
    return total;
  };

  // ========== 确认选座 ==========
  const handleConfirm = async () => {
    const uniqueSeatIds = Array.from(new Set(selectedSeats.map((id) => Number(id))));
    if (uniqueSeatIds.length === 0) {
      message.warning('请至少选择一个座位');
      return;
    }

    if (!selectedSessionId) {
      message.warning('请选择场次');
      return;
    }

    setSubmitting(true);
    try {
      // 1. 锁座
      const { data: lockData, error: lockError } = await seatLockAPI.lockSeats(
        selectedSessionId,
        uniqueSeatIds
      );

      if (lockError) {
        message.error('锁定座位失败，请重试');
        setSubmitting(false);
        return;
      }

      if (!lockData?.success || !lockData?.data) {
        message.error(lockData?.message || '锁定座位失败，请重试');
        setSubmitting(false);
        return;
      }

      // 获取锁座令牌映射
      const lockMap: Record<number, string> = {};
      lockData.data.locks.forEach((item: any) => {
        lockMap[Number(item.seatId)] = item.lockToken;
      });

      // 改签模式：跳转回订单详情页，不创建订单
      if (fromExchange && exchangeOrderId) {
        // 1:1 约束：换几张票必须选几个座位，每个目标座位按顺序对应一张原票明细
        if (exchangeOriginalCount !== uniqueSeatIds.length) {
          message.error(`改签需选择 ${exchangeOriginalCount || '与订单票数相同'} 个目标座位（与原票一一对应）`);
          const tokens = lockData.data.locks.map((item: any) => item.lockToken);
          await seatLockAPI.releaseSeats(selectedSessionId, tokens);
          setSubmitting(false);
          return;
        }
        const targetSeats = uniqueSeatIds.map((seatId, idx) => {
          const seat = seats.find((s) => s.seatId === seatId);
          const strategy = pricingStrategies.find(
            (s) => s.seatSectionId === seat?.seatSectionId
          );
          return {
            seatId: seatId,
            rowCode: seat?.rowCode,
            colIndex: seat?.colIndex,
            // 关键修复：携带原票明细 ID（原实现恒为 null，订单详情页只能危险兜底）
            originalOrderItemId: exchangeOriginalItems?.[idx]?.orderItemId ?? null,
            priceStrategyId: strategy?.priceStrategyId || 0,
            lockToken: lockMap[seatId] || '',
          };
        });

        const hasEmptyLock = targetSeats.some((item) => !item.lockToken);
        if (hasEmptyLock) {
          message.error('部分座位未锁定，请重新选择');
          const tokens = lockData.data.locks.map((item: any) => item.lockToken);
          await seatLockAPI.releaseSeats(selectedSessionId, tokens);
          setSubmitting(false);
          return;
        }

        message.success('座位已锁定，返回改签申请');
        navigate(`/order/${exchangeOrderId}`, {
          state: {
            fromExchange: true,
            targetSeats: targetSeats,
            targetSessionId: selectedSessionId,
          },
        });
        return;
      }

      // 正常下单
      const orderItems: Array<{
        seatId: number;
        priceStrategyId: number;
        realNameId: null;
        lockToken: string;
      }> = [];

      for (const seatId of uniqueSeatIds) {
        const seat = seats.find((s) => s.seatId === seatId);
        if (!seat) {
          message.error(`座位 ${seatId} 不存在`);
          const tokens = lockData.data.locks.map((item: any) => item.lockToken);
          await seatLockAPI.releaseSeats(selectedSessionId, tokens);
          setSubmitting(false);
          return;
        }

        const strategy = pricingStrategies.find(
          (s) => s.seatSectionId === seat.seatSectionId
        );

        if (!strategy || !strategy.priceStrategyId) {
          const seatLabel = seat.seatNo || `${seat.rowCode}${seat.colIndex}`;
          message.error(`座位 ${seatLabel} 所在区域未配置票价，请联系管理员`);
          const tokens = lockData.data.locks.map((item: any) => item.lockToken);
          await seatLockAPI.releaseSeats(selectedSessionId, tokens);
          setSubmitting(false);
          return;
        }

        const lockToken = lockMap[seatId] || '';
        if (!lockToken) {
          message.error(`座位 ${seat.seatNo || `${seat.rowCode}${seat.colIndex}`} 未锁定，请重新选择`);
          const tokens = lockData.data.locks.map((item: any) => item.lockToken);
          await seatLockAPI.releaseSeats(selectedSessionId, tokens);
          setSubmitting(false);
          return;
        }

        orderItems.push({
          seatId: seatId,
          priceStrategyId: strategy.priceStrategyId,
          realNameId: null,
          lockToken: lockToken,
        });
      }

      // 每次下单尝试生成唯一幂等键：服务端按 (用户, 幂等键, 请求摘要) 做安全重放与并发去重。
      // 优先 crypto.randomUUID()；非安全上下文（http 非 localhost）不支持时退化为时间戳+随机串。
      const idempotencyKey =
        typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
          ? crypto.randomUUID()
          : `ik-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}-${Math.random().toString(36).slice(2, 10)}`;
      const { data: orderData, error: orderError } = await orderAPI.createOrder(
        {
          sessionId: selectedSessionId,
          items: orderItems,
          remark: null,
        },
        idempotencyKey,
      );

      if (orderError) {
        const tokens = lockData.data.locks.map((item: any) => item.lockToken);
        await seatLockAPI.releaseSeats(selectedSessionId, tokens);
        message.error('创建订单失败');
        setSubmitting(false);
        return;
      }

      if (orderData?.success && orderData?.data) {
        message.success('订单创建成功！');
        navigate('/order');
      } else {
        const tokens = lockData.data.locks.map((item: any) => item.lockToken);
        await seatLockAPI.releaseSeats(selectedSessionId, tokens);
        message.error(orderData?.message || '创建订单失败');
      }
    } catch (error: any) {
      console.error('操作失败:', error);
      message.error(error.message || '操作失败，请重试');
    } finally {
      setSubmitting(false);
    }
  };

  // ========== 渲染场次选择器 ==========
  const renderSessionSelector = () => {
    // 改签模式下，隐藏场次选择器（因为已经在弹窗中选过了）
    if (fromExchange) {
      return (
        <div className="session-selector" style={{ textAlign: 'center', padding: '8px 0' }}>
          <Text type="secondary">
            {seatMap
              ? `改签目标场次：${new Date(seatMap.startTime).toLocaleString('zh-CN')}`
              : '加载场次信息...'}
          </Text>
        </div>
      );
    }

    if (sessions.length <= 1) {
      return null;
    }

    return (
      <div className="session-selector">
        <Text strong>选择场次：</Text>
        <Radio.Group
          value={selectedSessionId}
          onChange={(e) => setSelectedSessionId(e.target.value)}
          buttonStyle="solid"
        >
          {sessions.map((session) => (
            <Radio.Button key={session.sessionId} value={session.sessionId}>
              {new Date(session.startTime).toLocaleString('zh-CN')}
            </Radio.Button>
          ))}
        </Radio.Group>
      </div>
    );
  };

  // ========== 渲染座位矩阵（按票区） ==========
  const renderSeats = () => {
    if (!seatMap?.seatMap) return null;

    const sections = seatMap.seatMap.sections || [];

    return (
      <div className="seat-grid-wrapper">
        {sections.map((section) => {
          const sectionSeats = section.seats || [];
          if (sectionSeats.length === 0) return null;

          const rows: Record<string, SessionSeatMapSeatDto[]> = {};
          sectionSeats.forEach((seat) => {
            const key = seat.rowCode;
            if (!rows[key]) rows[key] = [];
            rows[key].push(seat);
          });

          Object.keys(rows).forEach((key) => {
            rows[key].sort((a, b) => a.colIndex - b.colIndex);
          });

          const rowKeys = Object.keys(rows).sort();

          return (
            <div key={section.seatSectionId} className="seat-section">
              <div
                className="section-header"
                style={{
                  backgroundColor: section.sectionColor || '#e8e8e8',
                  color: '#fff',
                  padding: '4px 12px',
                  borderRadius: '4px',
                  marginBottom: '8px',
                  fontWeight: 'bold',
                  fontSize: '14px',
                }}
              >
                {section.sectionName}
              </div>
              <div className="seat-grid">
                {rowKeys.map((rowKey) => (
                  <div key={rowKey} className="seat-row">
                    <span className="row-label">{rowKey}</span>
                    {rows[rowKey].map((seat) => {
                      const isSelected = selectedSeats.includes(seat.seatId);
                      const statusKey = (seat.availabilityStatus || seat.seatStatus || '').toUpperCase();
                      let statusInfo;
                      if (seat.isSellable && statusKey === 'AVAILABLE') {
                        statusInfo = SEAT_STATUS_MAP['AVAILABLE'];
                      } else if (!seat.isSellable) {
                        statusInfo = { label: '不可售', className: 'unavailable' };
                      } else {
                        statusInfo = SEAT_STATUS_MAP[statusKey] || { label: '未知', className: 'unknown' };
                      }
                      const price = getSeatPrice(seat);

                      const seatLabel = seat.seatNo || `${seat.rowCode}${seat.colIndex}`;

                      return (
                        <div
                          key={seat.seatId}
                          className={`seat ${statusInfo.className} ${isSelected ? 'selected' : ''}`}
                          onClick={() => handleSeatClick(seat)}
                          title={`${seatLabel} - ${statusInfo.label}${price > 0 ? ` ¥${price}` : ''}`}
                        >
                          <span className="seat-number">{seatLabel}</span>
                          {price > 0 && <span className="seat-price">{price}</span>}
                        </div>
                      );
                    })}
                  </div>
                ))}
              </div>
            </div>
          );
        })}
      </div>
    );
  };

  // ========== 渲染图例 ==========
  const renderLegend = () => (
    <div className="seat-legend">
      <span>
        <span className="legend-box available"></span> 可选
      </span>
      <span>
        <span className="legend-box selected"></span> 已选
      </span>
      <span>
        <span className="legend-box locked"></span> 锁定中
      </span>
      <span>
        <span className="legend-box sold"></span> 已售
      </span>
      <span>
        <span className="legend-box unavailable"></span> 不可用
      </span>
    </div>
  );

  // ========== 加载中 ==========
  if (loading) {
    return (
      <div
        className="seat-selection-container"
        style={{
          display: 'flex',
          justifyContent: 'center',
          alignItems: 'center',
          height: '60vh',
        }}
      >
        <Spin size="large" tip="加载座位图..." />
      </div>
    );
  }

  // ========== 无场次 ==========
  if (sessions.length === 0) {
    return (
      <div className="seat-selection-container" style={{ textAlign: 'center', padding: 60 }}>
        <Title level={4}>该演出暂无场次</Title>
        <Button onClick={() => navigate(-1)}>返回</Button>
      </div>
    );
  }

  // ========== 主渲染 ==========
  return (
    <div className="seat-selection-container">
      <Title level={2} style={{ textAlign: 'center', marginBottom: 8 }}>
        选择座位
      </Title>
      <Text type="secondary" style={{ display: 'block', textAlign: 'center', marginBottom: 16 }}>
        演出 ID: {eventId}
        {seatMap && (
          <span style={{ marginLeft: 16 }}>
            场次: {new Date(seatMap.startTime).toLocaleString('zh-CN')}
          </span>
        )}
        {fromExchange && (
          <span style={{ marginLeft: 16, color: '#ff4d4f' }}>（改签模式）</span>
        )}
      </Text>

      {/* 场次选择器（改签模式下隐藏） */}
      {renderSessionSelector()}

      {/* 座位网格 */}
      {seats.length > 0 ? (
        <>
          {renderSeats()}
          {renderLegend()}
        </>
      ) : (
        <div style={{ textAlign: 'center', padding: 40, color: '#999' }}>
          暂无座位数据
        </div>
      )}

      {/* 操作栏 */}
      <div className="seat-actions">
        <div className="seat-summary">
          <span>
            已选 <strong>{selectedSeats.length}</strong> 个座位
          </span>
          <span className="total-price">合计：¥{getTotalPrice().toFixed(2)}</span>
        </div>
        <div className="seat-buttons">
          <Button size="large" onClick={() => navigate(-1)}>
            返回
          </Button>
          <Button
            type="primary"
            size="large"
            loading={submitting}
            disabled={selectedSeats.length === 0}
            onClick={handleConfirm}
          >
            {fromExchange ? '确认改签座位' : '确认选座'}
          </Button>
        </div>
      </div>
    </div>
  );
};

export default SeatSelection;
