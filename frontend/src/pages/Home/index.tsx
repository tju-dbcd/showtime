import { useState, useEffect } from 'react';
import { Card, Row, Col, Button, Input, Typography, Spin, Empty, message } from 'antd';
import { SearchOutlined, FireOutlined, CalendarOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { showAPI } from '@/api/requests';
import type { ShowDto } from '@/types/api';
import './Home.css';

const { Title, Text } = Typography;

const Home = () => {
  const navigate = useNavigate();
  const [shows, setShows] = useState<ShowDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [hotShows, setHotShows] = useState<ShowDto[]>([]);
  const [upcomingShows, setUpcomingShows] = useState<ShowDto[]>([]);

  // ========== 获取演出列表 ==========
  const fetchShows = async () => {
    setLoading(true);
    try {
      const { data, error } = await showAPI.getShows({
        PageIndex: 1,
        PageSize: 20,
        Status: 'PUBLISHED',
      });

      if (error) {
        message.error('获取演出列表失败');
        return;
      }

      if (data?.success && data?.data) {
        const list = (data.data.items || []).map((item: any) => ({
          ...item,
          showId: Number(item.showId),
          categoryId: Number(item.categoryId),
          durationMinutes: item.durationMinutes !== null ? Number(item.durationMinutes) : null,
        }));
        setShows(list);
        setHotShows(list.slice(0, 3));
        setUpcomingShows(list.slice(3, 9));
      } else {
        message.error(data?.message || '获取演出列表失败');
      }
    } catch (error: any) {
      console.error('获取演出列表失败:', error);
      message.error(error.message || '获取演出列表失败');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchShows();
  }, []);

  // ========== 搜索处理 ==========
  const handleSearch = (value: string) => {
    if (value.trim()) {
      navigate(`/search?q=${encodeURIComponent(value.trim())}`);
    } else {
      navigate('/search');
    }
  };

  // ========== 获取海报图（带 fallback） ==========
  const getPoster = (show: ShowDto): string => {
    return show.posterUrl || 'https://picsum.photos/seed/fallback/300/200';
  };

  // ========== 加载中 ==========
  if (loading) {
    return (
      <div className="home-container" style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '60vh' }}>
        <Spin size="large" description="加载演出列表..." />
      </div>
    );
  }

  // ========== 空状态 ==========
  if (shows.length === 0) {
    return (
      <div className="home-container" style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '60vh', flexDirection: 'column' }}>
        <Empty description="暂无演出，敬请期待！" />
      </div>
    );
  }

  return (
    <div className="home-container">
      {/* ====== 推荐海报区（取第一个演出作为推荐） ====== */}
      <div
        className="hero-section"
        style={{
          background: `linear-gradient(135deg, rgba(26,26,46,0.85) 0%, rgba(22,30,62,0.75) 100%), url(${getPoster(shows[0])})`,
          backgroundSize: 'cover',
          backgroundPosition: 'center',
        }}
      >
        <div className="hero-content">
          <Title level={1} style={{ color: 'white', margin: 0 }}>
            {shows[0].showName}
          </Title>
          <Text style={{ color: 'rgba(255,255,255,0.8)', fontSize: 18 }}>
            {shows[0].description || '精彩演出，即将上演'}
          </Text>
          <Button
            type="primary"
            size="large"
            style={{ marginTop: 20 }}
            onClick={() => navigate(`/performance/${shows[0].showId}`)}
          >
            立即购票
          </Button>
        </div>
      </div>

      {/* ====== 搜索框 ====== */}
      <div className="search-wrapper">
        <Input.Search
          placeholder="搜索演出名称..."
          allowClear
          enterButton={<SearchOutlined />}
          size="large"
          onSearch={handleSearch}
          className="search-input"
        />
      </div>

      {/* ====== 内容区 ====== */}
      <div className="content-area">
        {/* ====== 热门演出 ====== */}
        <div className="section-header">
          <Title level={3}>
            <FireOutlined /> 热门演出
          </Title>
          <Button type="link" onClick={() => navigate('/search')}>
            查看全部 &gt;
          </Button>
        </div>

        {hotShows.length > 0 ? (
          <Row gutter={[24, 24]}>
            {hotShows.map((show) => (
              <Col key={show.showId} xs={24} sm={12} md={8}>
                <Card
                  hoverable
                  cover={
                    <img
                      alt={show.showName}
                      src={getPoster(show)}
                      style={{ height: 200, objectFit: 'cover' }}
                    />
                  }
                  onClick={() => navigate(`/performance/${show.showId}`)}
                >
                  <Card.Meta
                    title={show.showName}
                    description={show.description?.slice(0, 30) || ''}
                  />
                </Card>
              </Col>
            ))}
          </Row>
        ) : (
          <Empty description="暂无热门演出" />
        )}

        {/* ====== 近期演出 ====== */}
        <div className="section-header" style={{ marginTop: 48 }}>
          <Title level={3}>
            <CalendarOutlined /> 近期演出
          </Title>
          <Button type="link" onClick={() => navigate('/search')}>
            查看全部 &gt;
          </Button>
        </div>

        {upcomingShows.length > 0 ? (
          <Row gutter={[24, 24]}>
            {upcomingShows.map((show) => (
              <Col key={show.showId} xs={12} sm={12} md={8} lg={6}>
                <Card
                  hoverable
                  cover={
                    <img
                      alt={show.showName}
                      src={getPoster(show)}
                      style={{ height: 180, objectFit: 'cover' }}
                    />
                  }
                  onClick={() => navigate(`/performance/${show.showId}`)}
                >
                  <Card.Meta
                    title={show.showName}
                    description={show.description?.slice(0, 20) || ''}
                  />
                </Card>
              </Col>
            ))}
          </Row>
        ) : (
          <Empty description="暂无近期演出" />
        )}
      </div>
    </div>
  );
};

export default Home;
