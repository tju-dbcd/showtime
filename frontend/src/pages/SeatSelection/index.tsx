import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
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

  const [loading, setLoading] = useState(true);
  const [sessions, setSessions] = useState<SessionInfo[]>([]);
  const [selectedSessionId, setSelectedSessionId] = useState<number | null>(null);
  const [seatMap, setSeatMap] = useState<SessionSeatMapDto | null>(null);
  const [pricingStrategies, setPricingStrategies] = useState<PricingStrategyDto[]>([]);

  // 座位矩阵（展平后的座位列表，方便渲染）
  const [seats, setSeats] = useState<SessionSeatMapSeatDto[]>([]);
  const [selectedSeats, setSelectedSeats] = useState<number[]>([]); // 选中座位的 seatId

  const [submitting, setSubmitting] = useState(false);

  // 获取场次列表
  const fetchSessions = async () => {
    try {
      const response: any = await showSessionAPI.getShowSessions(Number(eventId));
      const result = response.data ? response.data : response;
      if (result.success && result.data) {
        setSessions(result.data);
        // 默认选中第一个场次
        if (result.data.length > 0) {
          setSelectedSessionId(result.data[0].sessionId);
        } else {
          message.warning('该演出暂无场次');
        }
      } else {
        message.error(result.message || '获取场次失败');
      }
    } catch (error: any) {
      console.error('获取场次失败:', error);
      message.error(error.response?.data?.message || '获取场次失败');
    }
  };

  // 获取座位图
  const fetchSeatMap = async (sessionId: number) => {
    setLoading(true);
    try {
      const response: any = await sessionAPI.getSessionSeatMap(sessionId);
      const result = response.data ? response.data : response;
      if (result.success && result.data) {
        setSeatMap(result.data);
        // 展平所有座位
        const allSeats: SessionSeatMapSeatDto[] = [];
        result.data.seatMap?.sections?.forEach((section: any) => {
          section.seats?.forEach((seat: SessionSeatMapSeatDto) => {
            allSeats.push({
              ...seat,
              // 补充 section 信息
              sectionName: section.sectionName,
              sectionColor: section.sectionColor,
            });
          });
        });
        setSeats(allSeats);
        setSelectedSeats([]);
      } else {
        message.error(result.message || '获取座位图失败');
      }
    } catch (error: any) {
      console.error('获取座位图失败:', error);
      message.error(error.response?.data?.message || '获取座位图失败');
    } finally {
      setLoading(false);
    }
  };

  // 获取定价策略
  const fetchPricingStrategies = async (sessionId: number) => {
    try {
      const response: any = await showSessionAPI.getPricingStrategies(sessionId);
      const result = response.data ? response.data : response;
      if (result.success && result.data) {
        setPricingStrategies(result.data);
      }
    } catch (error) {
      console.error('获取定价策略失败:', error);
    }
  };

  // 场次变化时重新加载
  useEffect(() => {
    if (eventId) {
      fetchSessions();
    }
  }, [eventId]);

  useEffect(() => {
    if (selectedSessionId) {
      fetchSeatMap(selectedSessionId);
      fetchPricingStrategies(selectedSessionId);
    }
  }, [selectedSessionId]);

  const handleSeatClick = (seat: SessionSeatMapSeatDto) => {
    // 如果不可售，直接提示并返回
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
    } else {
      setSelectedSeats([...selectedSeats, seatId]);
    }
  };

  // 获取座位价格
  const getSeatPrice = (seat: SessionSeatMapSeatDto): number => {
    // 根据 seatSectionId 查找定价策略
    const strategy = pricingStrategies.find(
      (s) => s.seatSectionId === seat.seatSectionId
    );
    return strategy?.price || 0;
  };

  // 计算总价
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

  // 确认选座并创建订单
  const handleConfirm = async () => {
    if (selectedSeats.length === 0) {
      message.warning('请至少选择一个座位');
      return;
    }

    if (!selectedSessionId) {
      message.warning('请选择场次');
      return;
    }

    setSubmitting(true);
    try {
      // 1. 先锁座
      const lockResponse: any = await seatLockAPI.lockSeats(selectedSessionId, selectedSeats);
      const lockResult = lockResponse.data ? lockResponse.data : lockResponse;

      if (!lockResult.success || !lockResult.data) {
        message.error(lockResult.message || '锁定座位失败，请重试');
        return;
      }

      // 获取锁座令牌映射
      const lockMap: Record<number, string> = {};
      lockResult.data.locks.forEach((item: any) => {
        lockMap[item.seatId] = item.lockToken;
      });

      // 2. 构建订单请求（带上 lockToken）
      const orderItems = selectedSeats.map((seatId) => {
        const seat = seats.find((s) => s.seatId === seatId);
        const strategy = pricingStrategies.find(
          (s) => s.seatSectionId === seat?.seatSectionId
        );
        return {
          seatId: seatId,
          priceStrategyId: strategy?.priceStrategyId || 0,
          realNameId: null,
          lockToken: lockMap[seatId] || '',
        };
      });

      const orderResponse: any = await orderAPI.createOrder({
        sessionId: selectedSessionId,
        items: orderItems,
        remark: null,
      });

      const orderResult = orderResponse.data ? orderResponse.data : orderResponse;
      if (orderResult.success && orderResult.data) {
        message.success('订单创建成功！');
        navigate('/order');
      } else {
        // 创建订单失败，释放锁
        const tokens = lockResult.data.locks.map((item: any) => item.lockToken);
        await seatLockAPI.releaseSeats(selectedSessionId, tokens);
        message.error(orderResult.message || '创建订单失败');
      }
    } catch (error: any) {
      console.error('操作失败:', error);
      message.error(error.response?.data?.message || '操作失败，请重试');
    } finally {
      setSubmitting(false);
    }
  };

  // 获取座位显示标签
  const getSeatLabel = (seat: SessionSeatMapSeatDto): string => {
    return `${seat.rowCode}${seat.colIndex}`;
  };

  // 渲染场次选择
  const renderSessionSelector = () => {
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

  // 渲染座位矩阵
  const renderSeats = () => {
    if (!seatMap) return null;
  console.log('=== 座位数据 ===', seats);
  console.log('第一个座位:', seats[0]);
    // 按行分组
    const rows: Record<string, SessionSeatMapSeatDto[]> = {};
    seats.forEach((seat) => {
      const key = seat.rowCode;
      if (!rows[key]) rows[key] = [];
      rows[key].push(seat);
    });

    // 按列排序
    Object.keys(rows).forEach((key) => {
      rows[key].sort((a, b) => a.colIndex - b.colIndex);
    });

    const rowKeys = Object.keys(rows).sort();

    return (
      <div className="seat-grid-wrapper">
        <div className="seat-grid">
          {rowKeys.map((rowKey) => (
            <div key={rowKey} className="seat-row">
              <span className="row-label">{rowKey}</span>
              {rows[rowKey].map((seat) => {
                const isSelected = selectedSeats.includes(seat.seatId);
                const seatStatus = seat.availabilityStatus || seat.seatStatus || '';
                const statusKey = seatStatus.toUpperCase();
                let statusInfo;

                if (seatStatus === 'AVAILABLE' && seat.isSellable) {
                  statusInfo = SEAT_STATUS_MAP['AVAILABLE'];
                } else if (seatStatus === 'AVAILABLE' && !seat.isSellable) {
                  statusInfo = { label: '不可售', className: 'unavailable' };
                } else {
                  statusInfo = SEAT_STATUS_MAP[statusKey] || { label: '未知', className: 'unknown' };
                }
                const price = getSeatPrice(seat);

                return (
                  <div
                    key={seat.seatId}
                    className={`seat ${statusInfo.className} ${isSelected ? 'selected' : ''}`}
                    onClick={() => handleSeatClick(seat)}
                    title={`${getSeatLabel(seat)} - ${statusInfo.label}${price > 0 ? ` ¥${price}` : ''}`}
                  >
                    <span className="seat-number">{getSeatLabel(seat)}</span>
                    {price > 0 && <span className="seat-price">{price}</span>}
                  </div>
                );
              })}
            </div>
          ))}
        </div>
      </div>
    );
  };

  // 渲染图例
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

  // 加载中
  if (loading) {
    return (
      <div className="seat-selection-container" style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '60vh' }}>
        <Spin size="large" tip="加载座位图..." />
      </div>
    );
  }

  // 无场次
  if (sessions.length === 0) {
    return (
      <div className="seat-selection-container" style={{ textAlign: 'center', padding: 60 }}>
        <Title level={4}>该演出暂无场次</Title>
        <Button onClick={() => navigate(-1)}>返回</Button>
      </div>
    );
  }

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
      </Text>

      {/* 场次选择 */}
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
          <span>已选 <strong>{selectedSeats.length}</strong> 个座位</span>
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
            确认选座
          </Button>
        </div>
      </div>
    </div>
  );
};

export default SeatSelection;
