import { useState } from 'react';
import { Button, message, Typography } from 'antd';
import { useParams, useNavigate } from 'react-router-dom';
import { generateMockSeats } from '@/mock/seats';
import './index.css'; // 等下创建这个 CSS 文件

const { Title, Text } = Typography;

const SeatSelection = () => {
  const { eventId } = useParams<{ eventId: string }>();
  const navigate = useNavigate();

  // 生成座位矩阵（8行 × 12列）
  const [seats, setSeats] = useState<string[][]>(() => generateMockSeats(8, 12));
  // 存储已选座位的坐标，格式：['0-3', '1-5', ...]
  const [selectedSeats, setSelectedSeats] = useState<string[]>([]);

  // 处理座位点击
  const handleSeatClick = (rowIdx: number, colIdx: number) => {
    const status = seats[rowIdx][colIdx];
    const key = `${rowIdx}-${colIdx}`;

    // 已售或不可用 -> 不可操作
    if (status === 'sold' || status === 'unavailable') {
      if (status === 'sold') message.warning('该座位已被选走');
      else message.warning('该座位不可用');
      return;
    }

    // 如果已选中 -> 取消选中
    if (status === 'selected') {
      const newSeats = [...seats];
      newSeats[rowIdx][colIdx] = 'available';
      setSeats(newSeats);
      setSelectedSeats(selectedSeats.filter(s => s !== key));
      return;
    }

    // 如果可用 -> 选中
    if (status === 'available') {
      const newSeats = [...seats];
      newSeats[rowIdx][colIdx] = 'selected';
      setSeats(newSeats);
      setSelectedSeats([...selectedSeats, key]);
    }
  };

  // 确认选座
  const handleConfirm = () => {
    if (selectedSeats.length === 0) {
      message.warning('请至少选择一个座位');
      return;
    }
    message.success(`成功选择 ${selectedSeats.length} 个座位，即将跳转订单`);
    // 将选中的座位信息传到订单页（通过 state 或 URL 参数）
    setTimeout(() => {
      navigate('/order', { state: { selectedSeats, eventId } });
    }, 500);
  };

  // 获取行标签（A, B, C, ...）
  const getRowLabel = (idx: number) => String.fromCharCode(65 + idx);

  return (
    <div className="seat-selection-container">
      <Title level={2} style={{ textAlign: 'center', marginBottom: 8 }}>
        🎫 选择座位
      </Title>
      <Text type="secondary" style={{ display: 'block', textAlign: 'center', marginBottom: 24 }}>
        演出 ID: {eventId} | 灰色不可选，点击可选座位
      </Text>

      {/* 座位网格 */}
      <div className="seat-grid-wrapper">
        <div className="seat-grid">
          {seats.map((row, ri) => (
            <div key={ri} className="seat-row">
              <span className="row-label">{getRowLabel(ri)}</span>
              {row.map((status, ci) => (
                <div
                  key={ci}
                  className={`seat ${status}`}
                  onClick={() => handleSeatClick(ri, ci)}
                  title={`${getRowLabel(ri)}-${ci + 1}`}
                >
                  <span className="seat-number">{ci + 1}</span>
                </div>
              ))}
            </div>
          ))}
        </div>
      </div>

      {/* 图例 & 操作按钮 */}
      <div className="seat-legend">
        <span><span className="legend-box available"></span> 可选</span>
        <span><span className="legend-box selected"></span> 已选</span>
        <span><span className="legend-box sold"></span> 已售</span>
        <span><span className="legend-box unavailable"></span> 不可用</span>
      </div>

      <div className="seat-actions">
        <Button size="large" onClick={() => navigate(-1)}>
          返回
        </Button>
        <Button type="primary" size="large" onClick={handleConfirm}>
          确认选座 ({selectedSeats.length} 个)
        </Button>
      </div>
    </div>
  );
};

export default SeatSelection;
