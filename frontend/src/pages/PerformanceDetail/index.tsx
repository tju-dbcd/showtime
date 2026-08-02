import { useParams, useNavigate } from 'react-router-dom';
import { Button, message, Tag, Divider, Carousel, Empty } from 'antd';
import { CalendarOutlined, EnvironmentOutlined, DollarOutlined, ClockCircleOutlined, LeftOutlined } from '@ant-design/icons';
import { mockEvents } from '@/mock/events';
import './PerformanceDetail.css';

const PerformanceDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  // 根据 ID 查找演出
  const event = mockEvents.find((e) => e.id === Number(id));

  // 如果没找到演出，显示空状态
  if (!event) {
    return (
      <div className="detail-container">
        <Empty description="演出不存在" />
        <Button onClick={() => navigate('/search')}>返回演出列表</Button>
      </div>
    );
  }

  // 处理购票按钮
  const handleBuyTicket = () => {
    navigate(`/seat-selection/${event.id}`);
  };

  // 处理分类标签颜色
  const categoryColorMap: Record<string, string> = {
    '演唱会': '#ff4d4f',
    '话剧': '#1890ff',
    '音乐剧': '#52c41a',
    '体育': '#faad14',
  };

  return (
    <div className="detail-container">
      {/* ====== 顶部海报区 ====== */}
      <div className="detail-hero">
        <div className="detail-hero-content">
          {/* 左侧：图片 + 名称 */}
          <div className="detail-left">
            <img
              src={event.img}
              alt={event.name}
              className="detail-poster"
            />
            <h1 className="detail-title">{event.name}</h1>
          </div>

          {/* 右侧：详细信息 */}
          <div className="detail-right">
            <div className="detail-meta">
              <div className="meta-item">
                <EnvironmentOutlined className="meta-icon" />
                <span>{event.venue}</span>
              </div>
              <div className="meta-item">
                <CalendarOutlined className="meta-icon" />
                <span>{event.date}</span>
              </div>
              <div className="meta-item">
                <ClockCircleOutlined className="meta-icon" />
                <span>{event.duration}</span>
              </div>
              <div className="meta-item">
                <DollarOutlined className="meta-icon" />
                <span className="detail-price">¥{event.price} 起</span>
              </div>
              <div className="meta-item">
                <Tag color={categoryColorMap[event.category] || '#888'}>
                  {event.category}
                </Tag>
              </div>
            </div>

            <div className="detail-description">
              <h3>演出简介</h3>
              <p>{event.description}</p>
            </div>
          </div>
        </div>
      </div>

      {/* ====== 下方介绍区 ====== */}
      <div className="detail-body">
        {/* 演出介绍（带图片） */}
        <div className="detail-section">
          <h2>🎬 精彩瞬间</h2>
          <Divider />
          <div className="detail-gallery">
            <Carousel autoplay arrows dotPosition="bottom">
              {event.images.map((img, index) => (
                <div key={index}>
                  <img src={img} alt={`${event.name} 宣传图 ${index + 1}`} />
                </div>
              ))}
            </Carousel>
          </div>
        </div>

        {/* 购票须知 */}
        <div className="detail-section">
          <h2>📋 购票须知</h2>
          <Divider />
          <div className="detail-notice">
            {event.notice.split('\n').map((line, index) => (
              <p key={index}>{line}</p>
            ))}
          </div>
        </div>

        {/* 场馆信息（占位） */}
        <div className="detail-section">
          <h2>📍 场馆信息</h2>
          <Divider />
          <div className="detail-venue">
            <p><strong>{event.venue}</strong></p>
            <p style={{ color: '#888', fontSize: 14 }}>
              具体地址请查看地图导航，建议提前规划出行路线。
            </p>
          </div>
        </div>
      </div>

      {/* ====== 底部固定按钮 ====== */}
      <div className="detail-footer">
        <Button
          type="primary"
          size="large"
          className="buy-btn"
          onClick={handleBuyTicket}
        >
          立即抢票
        </Button>
      </div>
    </div>
  );
};

export default PerformanceDetail;
