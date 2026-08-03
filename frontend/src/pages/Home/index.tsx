import { Card, Row, Col, Button, Typography } from 'antd';
import { FireOutlined, CalendarOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { mockEvents } from '@/mock/events';
import './Home.css';

const { Title, Text } = Typography;

const Home = () => {
  const navigate = useNavigate();

  // 1. 获取热门演出（假设第一个最热，取前3个）
  const hotEvents = mockEvents.slice(0, 3);
  // 2. 获取近期演出（假设后6个）
  const upcomingEvents = mockEvents.slice(2, 8);

  // 搜索处理
  // const handleSearch = (value: string) => {
  //   if (value.trim()) {
  //     navigate(`/search?q=${encodeURIComponent(value.trim())}`);
  //   } else {
  //     navigate('/search');
  //   }
  // };

  return (
    <div className="home-container">
      {/* 1. 推荐海报区 (带背景图) */}
      <div className="hero-section">
        <div className="hero-content">
          <Title level={1} style={{ color: 'white', margin: 0 }}>
            {mockEvents[0].name}
          </Title>
          <Text style={{ color: 'rgba(255,255,255,0.8)', fontSize: 18 }}>
            {mockEvents[0].venue} | {mockEvents[0].date}
          </Text>
          <Button
            type="primary"
            size="large"
            style={{ marginTop: 20 }}
            onClick={() => navigate(`/performance/${mockEvents[0].id}`)}
          >
            立即购票
          </Button>
        </div>
      </div>

      {/* 搜索框 */}
      <div className="search-wrapper">
        <div className="custom-search">
          <input
            type="text"
            className="search-input-field"
            placeholder="搜索演出名称、艺人、场馆..."
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                const value = (e.target as HTMLInputElement).value;
                if (value.trim()) {
                  navigate(`/search?q=${encodeURIComponent(value.trim())}`);
                } else {
                  navigate('/search');
                }
              }
            }}
          />
          <button
            className="search-btn"
            onClick={(e) => {
              const input = (e.target as HTMLButtonElement).previousElementSibling as HTMLInputElement;
              const value = input?.value || '';
              if (value.trim()) {
                navigate(`/search?q=${encodeURIComponent(value.trim())}`);
              } else {
                navigate('/search');
              }
            }}
          >
            🔍
          </button>
        </div>
      </div>

      {/* 内容区 */}
      <div className="content-area">
        {/* 热门演出 */}
        <div className="section-header">
          <h3><FireOutlined /> 热门演出</h3>
          <button className="link-btn" onClick={() => navigate('/search')}>查看全部 &gt;</button>
        </div>
        <Row gutter={[24, 24]}>
          {hotEvents.map((event) => (
            <Col key={event.id} xs={24} sm={12} md={8}>
              <Card
                hoverable
                cover={<img alt={event.name} src={event.img} style={{ height: 200, objectFit: 'cover' }} />}
                onClick={() => navigate(`/performance/${event.id}`)}
              >
                <Card.Meta title={event.name} description={`${event.venue}`} />
                <div style={{ marginTop: 12, color: '#ff4d4f', fontWeight: 'bold' }}>¥{event.price} 起</div>
              </Card>
            </Col>
          ))}
        </Row>

        {/* 近期演出 */}
        <div className="section-header" style={{ marginTop: 48 }}>
          <h3><CalendarOutlined /> 近期演出</h3>
          <button className="link-btn" onClick={() => navigate('/search')}>查看全部 &gt;</button>
        </div>
        <Row gutter={[24, 24]}>
          {upcomingEvents.map((event) => (
            <Col key={event.id} xs={12} sm={12} md={8} lg={6}>
              <Card
                hoverable
                cover={<img alt={event.name} src={event.img} style={{ height: 180, objectFit: 'cover' }} />}
                onClick={() => navigate(`/performance/${event.id}`)}
              >
                <Card.Meta title={event.name} description={event.date} />
                <div style={{ marginTop: 12, color: '#ff4d4f', fontWeight: 'bold' }}>¥{event.price} 起</div>
              </Card>
            </Col>
          ))}
        </Row>
      </div>
    </div>
  );
};

export default Home;
