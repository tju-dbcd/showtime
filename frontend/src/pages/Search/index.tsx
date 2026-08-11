import { useState, useEffect } from 'react';
import { Layout, Menu, Slider, Button, Input, Card, Row, Col, Typography, Divider, Spin, Empty, message, Tag } from 'antd';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { showAPI } from '@/api/requests';
import type { ShowDto } from '@/types/api';
import './Search.css';

const { Sider, Content } = Layout;

// 分类列表（硬编码，后续可以从接口获取）
const categories = [
  { id: 0, name: '全部' },
  { id: 1, name: '演唱会' },
  { id: 2, name: '话剧' },
  { id: 3, name: '音乐剧' },
  { id: 4, name: '体育' },
];

// 城市列表（硬编码，后续可以从接口获取）
const cities = ['全部', '北京', '上海', '广州', '成都', '杭州'];

const Search = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const initialQuery = searchParams.get('q') || '';

  // 状态
  const [searchText, setSearchText] = useState(initialQuery);
  const [selectedCategory, setSelectedCategory] = useState(0);
  const [selectedCity, setSelectedCity] = useState('全部');
  const [priceRange, setPriceRange] = useState<[number, number]>([0, 2000]);
  const [shows, setShows] = useState<ShowDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const pageSize = 12;

  // ========== 搜索演出 ==========
  const fetchShows = async (keyword?: string) => {
    setLoading(true);
    try {
      const params: any = {
        PageIndex: page,
        PageSize: pageSize,
        Status: 'Published',
      };

      // 关键词
      if (keyword || searchText) {
        params.Keyword = keyword || searchText;
      }

      // 分类（选中的分类ID，0表示全部）
      if (selectedCategory !== 0) {
        params.CategoryId = selectedCategory;
      }

      // 城市和价格目前无法筛选（后端接口暂无这些参数，可后续补充）
      // 暂时忽略 city 和 priceRange

      const response: any = await showAPI.getShows(params);
      const result = response.data ? response.data : response;
      if (result.success && result.data) {
        setShows(result.data.items || []);
        setTotal(result.data.totalCount || 0);
      } else {
        message.error(result.message || '搜索失败');
      }
    } catch (error: any) {
      console.error('搜索失败:', error);
      message.error(error.response?.data?.message || '搜索失败');
    } finally {
      setLoading(false);
    }
  };

  // ========== 初始加载 ==========
  useEffect(() => {
    fetchShows(initialQuery);
  }, [page, selectedCategory]);

  // ========== 搜索按钮 ==========
  const handleSearch = () => {
    setPage(1);
    fetchShows(searchText);
  };

  // ========== 回车搜索 ==========
  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') {
      handleSearch();
    }
  };

  // ========== 重置 ==========
  const handleReset = () => {
    setSelectedCategory(0);
    setSelectedCity('全部');
    setPriceRange([0, 2000]);
    setSearchText('');
    setPage(1);
    fetchShows('');
  };

  // ========== 获取海报图 ==========
  const getPoster = (show: ShowDto): string => {
    return show.posterUrl || 'https://picsum.photos/seed/fallback/300/200';
  };

  return (
    <Layout className="search-layout">
      {/* 左侧筛选栏 */}
      <Sider theme="dark" width={280} className="search-sider">
        <div style={{ padding: '20px 16px' }}>
          <Typography.Title level={4} style={{ color: 'white', marginBottom: 20 }}>筛选</Typography.Title>

          {/* 分类 */}
          <div className="filter-group">
            <div className="filter-label">分类</div>
            <Menu theme="dark" mode="inline" selectedKeys={[String(selectedCategory)]} onClick={({ key }) => setSelectedCategory(Number(key))}>
              {categories.map(cat => (
                <Menu.Item key={String(cat.id)}>{cat.name}</Menu.Item>
              ))}
            </Menu>
          </div>

          <Divider style={{ borderColor: '#434343' }} />

          {/* 城市（暂时只做UI） */}
          <div className="filter-group">
            <div className="filter-label">地点</div>
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

          {/* 价位（暂时只做UI） */}
          <div className="filter-group">
            <div className="filter-label">价位范围</div>
            <Slider
              range
              min={0}
              max={2000}
              step={50}
              value={priceRange}
              onChange={(val) => setPriceRange(val as [number, number])}
            />
            <div style={{ color: 'white' }}>¥{priceRange[0]} - ¥{priceRange[1]}</div>
          </div>

          <Divider style={{ borderColor: '#434343' }} />

          <Button type="primary" block size="large" onClick={handleSearch} style={{ background: '#ff4d4f', border: 'none' }}>
            应用筛选 & 搜索
          </Button>
          <Button block size="large" onClick={handleReset} style={{ marginTop: 8 }}>
            重置
          </Button>
        </div>
      </Sider>

      {/* 右侧内容 */}
      <Content className="search-content">
        <div style={{ padding: '24px 32px' }}>
          <div className="search-result-header">
            <Typography.Title level={3}>演出列表</Typography.Title>
            <Input.Search
              placeholder="搜索演出..."
              value={searchText}
              onChange={(e) => setSearchText(e.target.value)}
              onSearch={handleSearch}
              onKeyDown={handleKeyDown}
              style={{ width: 300 }}
              allowClear
            />
          </div>

          <Divider />

          <div style={{ marginBottom: 12, color: '#888' }}>
            共找到 {total} 个演出
          </div>

          <Spin spinning={loading}>
            {shows.length > 0 ? (
              <Row gutter={[24, 24]}>
                {shows.map((show) => (
                  <Col key={show.showId} xs={24} sm={12} md={8} lg={6}>
                    <Card
                      hoverable
                      cover={<img alt={show.showName} src={getPoster(show)} style={{ height: 180, objectFit: 'cover' }} />}
                      onClick={() => navigate(`/performance/${show.showId}`)}
                    >
                      <Card.Meta
                        title={show.showName}
                        description={
                          <div>
                            <div>{show.description?.slice(0, 30) || '暂无简介'}</div>
                            <div style={{ marginTop: 8 }}>
                              <Tag color={show.status === 'Published' ? 'green' : 'orange'}>
                                {show.status || '未知'}
                              </Tag>
                            </div>
                          </div>
                        }
                      />
                    </Card>
                  </Col>
                ))}
              </Row>
            ) : (
              !loading && <Empty description="没有找到符合条件的演出" />
            )}
          </Spin>

          {/* 简单分页（加载更多） */}
          {total > pageSize && (
            <div style={{ textAlign: 'center', marginTop: 24 }}>
              <Button
                type="primary"
                onClick={() => setPage(page + 1)}
                disabled={page * pageSize >= total}
              >
                {page * pageSize >= total ? '没有更多了' : '加载更多'}
              </Button>
            </div>
          )}
        </div>
      </Content>
    </Layout>
  );
};

export default Search;
