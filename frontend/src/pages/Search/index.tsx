import { useState, useEffect } from 'react';
import { Layout, Menu, Slider, Button, Input, Card, Row, Col, Typography, Checkbox, Divider } from 'antd';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { mockEvents } from '@/mock/events';
import type { Event } from '@/mock/events';
import './Search.css';

const { Sider, Content } = Layout;
const { Title } = Typography;
const { SubMenu } = Menu;

const categories = ['全部', '演唱会', '话剧', '音乐剧', '体育'];
const cities = ['全部', '北京', '上海', '广州', '成都', '杭州'];

const Search = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const initialQuery = searchParams.get('q') || '';

  // 状态管理
  const [searchText, setSearchText] = useState(initialQuery);
  const [selectedCategory, setSelectedCategory] = useState('全部');
  const [selectedCity, setSelectedCity] = useState('全部');
  const [priceRange, setPriceRange] = useState<[number, number]>([0, 1500]);
  const [filteredEvents, setFilteredEvents] = useState<Event[]>(mockEvents);

  // 模拟从主页带过来的搜索（*1 逻辑：自动搜索）
  useEffect(() => {
    handleSearch();
  }, []);

  const handleSearch = () => {
    let results = mockEvents;

    // 1. 搜索框文本过滤
    if (searchText.trim()) {
      results = results.filter(item =>
        item.name.includes(searchText.trim()) ||
        item.venue.includes(searchText.trim())
      );
    }

    // 2. 分类过滤
    if (selectedCategory !== '全部') {
      results = results.filter(item => item.category === selectedCategory);
    }

    // 3. 城市过滤
    if (selectedCity !== '全部') {
      results = results.filter(item => item.city === selectedCity);
    }

    // 4. 价格过滤
    results = results.filter(item => item.price >= priceRange[0] && item.price <= priceRange[1]);

    setFilteredEvents(results);
  };

  return (
    <Layout className="search-layout">
      {/* 左侧黑条 */}
      <Sider theme="dark" width={280} className="search-sider">
        <div style={{ padding: '20px 16px' }}>
          <Title level={4} style={{ color: 'white', marginBottom: 20 }}>筛选</Title>

          {/* 分类 */}
          <div className="filter-group">
            <div className="filter-label">分类</div>
            <Menu theme="dark" mode="inline" defaultSelectedKeys={['全部']} onClick={({ key }) => setSelectedCategory(key as string)}>
              {categories.map(cat => (
                <Menu.Item key={cat}>{cat}</Menu.Item>
              ))}
            </Menu>
          </div>

          <Divider style={{ borderColor: '#434343' }} />

          {/* 城市 */}
          <div className="filter-group">
            <div className="filter-label">地点</div>
            <Checkbox.Group
              className="city-checkbox"
              onChange={(values) => {
                // 这里简单处理，实际项目中一般只选一个或者多个，我们改为单选逻辑配合 Menu 风格，但为了演示，这里用简单的单选逻辑
                // 为了符合直觉，我们用 Menu 组件替代。为了演示，修改：使用下面的 Menu 代替上面的 Menu，但菜单已经用了分类。
                // 紧急修正：我们用一行按钮选择城市
              }}
            />
            {/* 因为上面用了 Menu 占位，这里快速改为按钮组 */}
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
              {cities.map(city => (
                <Button
                  key={city}
                  size="small"
                  type={selectedCity === city ? 'primary' : 'default'}
                  onClick={() => setSelectedCity(city)}
                  style={{ marginBottom: 4 }}
                >
                  {city}
                </Button>
              ))}
            </div>
          </div>

          <Divider style={{ borderColor: '#434343' }} />

          {/* 价位 */}
          <div className="filter-group">
            <div className="filter-label">价位范围</div>
            <Slider
              range
              min={0}
              max={2000}
              step={50}
              value={priceRange}
              onChange={(val) => setPriceRange(val as [number, number])}
              style={{ color: '#ff4d4f' }}
            />
            <div style={{ color: 'white' }}>¥{priceRange[0]} - ¥{priceRange[1]}</div>
          </div>

          <Divider style={{ borderColor: '#434343' }} />

          {/* 搜索按钮 */}
          <Button type="primary" block size="large" onClick={handleSearch} style={{ background: '#ff4d4f', border: 'none' }}>
            应用筛选 & 搜索
          </Button>
        </div>
      </Sider>

      {/* 右侧内容 */}
      <Content className="search-content">
        <div style={{ padding: '24px 16px' }}>
          <div className="search-result-header">
            <Title level={3}>演出列表</Title>
            {/* 自定义搜索框 */}
            <div className="search-input-wrapper">
              <input
                type="text"
                className="search-input-field"
                placeholder="搜索演出..."
                value={searchText}
                onChange={(e) => setSearchText(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    handleSearch();
                  }
                }}
              />
              <button className="search-btn" onClick={handleSearch}>
                🔍
              </button>
            </div>
          </div>

          <Divider />

          <div style={{ marginBottom: 12, color: '#888' }}>
            共找到 {filteredEvents.length} 个演出
          </div>

          <Row gutter={[24, 24]}>
            {filteredEvents.map((event) => (
              <Col key={event.id} xs={24} sm={12} md={8} lg={6}>
                <Card
                  hoverable
                  cover={<img alt={event.name} src={event.img} style={{ height: 180, objectFit: 'cover' }} />}
                  onClick={() => navigate(`/performance/${event.id}`)}
                >
                  <Card.Meta
                    title={event.name}
                    description={
                      <div>
                        <div>{event.venue}</div>
                        <div style={{ color: '#ff4d4f', fontWeight: 'bold' }}>¥{event.price} 起</div>
                      </div>
                    }
                  />
                </Card>
              </Col>
            ))}
          </Row>

          {filteredEvents.length === 0 && (
            <div style={{ textAlign: 'center', padding: 60, color: '#ccc', fontSize: 18 }}>
              没有找到符合条件的演出
            </div>
          )}
        </div>
      </Content>
    </Layout>
  );
};

export default Search;
