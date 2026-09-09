import { useState, useEffect, useRef } from 'react';
import { Layout, Button, Input, InputNumber, Card, Typography, Divider, Spin, Empty, message, Tag, Select } from 'antd';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Grid } from 'react-window';
import { showAPI } from '@/api/requests';
import { showSessionAPI } from '@/api/requests';
import type { ShowDto, PricingStrategyDto } from '@/types/api';
import './Search.css';

const { Sider, Content } = Layout;

type ShowGridCellProps = {
  shows: ShowDto[];
  getPoster: (show: ShowDto) => string;
  navigate: ReturnType<typeof useNavigate>;
  columnCount: number;
};

const ShowGridCell = ({
  columnIndex,
  rowIndex,
  style,
  ariaAttributes,
  shows,
  getPoster,
  navigate,
  columnCount,
}: ShowGridCellProps & {
  columnIndex: number;
  rowIndex: number;
  style: React.CSSProperties;
  ariaAttributes: React.HTMLAttributes<HTMLDivElement>;
}) => {
  const show = shows[rowIndex * columnCount + columnIndex];
  if (!show) return null;

  return (
    <div {...ariaAttributes} style={{ ...style, padding: '0 8px 16px' }}>
      <Card
        hoverable
        onClick={() => navigate(`/performance/${show.showId}`)}
        cover={<img className="show-poster" loading="lazy" decoding="async" alt={show.showName} src={getPoster(show)} />}
      >
        <Card.Meta
          title={show.showName}
          description={<><div className="show-card-description">{show.description?.slice(0, 80) || '暂无简介'}</div><Tag color="green">最低 ¥{Number((show as ShowDto & { minPrice?: number }).minPrice ?? 0).toFixed(0)}</Tag></>}
        />
      </Card>
    </div>
  );
};

const Search = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const initialQuery = searchParams.get('q') || '';

  // 状态
  const [searchText, setSearchText] = useState(initialQuery);
  const [appliedKeyword, setAppliedKeyword] = useState(initialQuery);
  const [selectedCategory, setSelectedCategory] = useState(0);
  const [categories, setCategories] = useState<Array<{ id: number; name: string }>>([{ id: 0, name: '全部' }]);
  const [priceRange, setPriceRange] = useState<[number, number]>([0, 2000]);
  const [appliedPriceRange, setAppliedPriceRange] = useState<[number, number]>([0, 2000]);
  const [shows, setShows] = useState<ShowDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [columnCount, setColumnCount] = useState(4);
  const requestVersion = useRef(0);
  const pageSize = 100;
  const gridRowHeight = columnCount === 4 ? 316 : 376;

  useEffect(() => {
    const updateColumnCount = () => setColumnCount(window.innerWidth <= 768 ? 2 : 4);
    updateColumnCount();
    window.addEventListener('resize', updateColumnCount);
    return () => window.removeEventListener('resize', updateColumnCount);
  }, []);

  useEffect(() => {
    void showAPI.getCategories().then(({ data, error }) => {
      if (!error && data?.success) {
        setCategories([{ id: 0, name: '全部' }, ...(data.data || []).map((item) => ({ id: Number(item.categoryId), name: item.categoryName }))]);
      }
    });
  }, []);

  // ========== 搜索演出 ==========
  const fetchShows = async (keyword?: string) => {
    const currentRequest = ++requestVersion.current;
    setLoading(true);
    try {
      const params: any = {
        PageIndex: page,
        PageSize: pageSize,
        Status: 'PUBLISHED',
      };

      if (keyword || searchText) {
        params.Keyword = keyword || searchText;
      }
      if (selectedCategory !== 0) {
        params.CategoryId = selectedCategory;
      }

      const { data, error } = await showAPI.getShows(params);

      if (error) {
        message.error('搜索失败');
        setLoading(false);
        return;
      }

      if (data?.success && data?.data) {
        const rawItems = (data.data.items || []).map((item: any) => ({
          ...item,
          showId: Number(item.showId),
          categoryId: Number(item.categoryId),
          durationMinutes: item.durationMinutes !== null ? Number(item.durationMinutes) : null,
        }));
        const items = await Promise.all(rawItems.map(async (item: ShowDto) => {
          const sessions = await showSessionAPI.getShowSessions(item.showId);
          const prices = await Promise.all((sessions.data?.success ? sessions.data.data || [] : []).map(async (session: any) => {
            const result = await showSessionAPI.getPricingStrategies(Number(session.sessionId));
            return result.data?.success ? (result.data.data || []) as PricingStrategyDto[] : [];
          }));
          const minPrice = prices.flat().reduce((min, strategy) => Math.min(min, Number(strategy.price)), Number.POSITIVE_INFINITY);
          return { ...item, minPrice: Number.isFinite(minPrice) ? minPrice : null };
        }));
        const filtered = items.filter((item: any) => item.minPrice !== null && item.minPrice >= appliedPriceRange[0] && item.minPrice <= appliedPriceRange[1]);
        if (currentRequest !== requestVersion.current) return;
        setShows((current) => page > 1 ? [...current, ...filtered] : filtered);
        setTotal(Number(data.data.totalCount || 0));
        if (items.length > 0) {
          const maxPrice = Math.max(2000, ...items.map((item: any) => Number(item.minPrice || 0)));
          setPriceRange((current) => {
            const nextMax = Math.max(current[1], maxPrice);
            return current[1] === nextMax ? current : [current[0], nextMax];
          });
        }
        setPage(Number(data.data.page || 1));
      } else {
        message.error(data?.message || '搜索失败');
      }
    } catch (error: any) {
      console.error('搜索失败:', error);
      message.error(error.message || '搜索失败');
    } finally {
      setLoading(false);
    }
  };

  // ========== 初始加载 ==========
  useEffect(() => {
    fetchShows(appliedKeyword);
  }, [page, selectedCategory, appliedKeyword, appliedPriceRange]);

  // ========== 搜索按钮 ==========
  const handleSearch = () => {
    if (priceRange[0] < 0 || priceRange[1] < 0 || priceRange[0] > priceRange[1]) {
      message.warning('请输入有效的票价范围');
      return;
    }
    setPage(1);
    setAppliedKeyword(searchText.trim());
    setAppliedPriceRange(priceRange);
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
    setPriceRange([0, 2000]);
    setAppliedPriceRange([0, 2000]);
    setSearchText('');
    setAppliedKeyword('');
    setPage(1);
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
            <div className="filter-label">演出类型</div>
            <Select
              className="filter-select"
              value={selectedCategory}
              onChange={(value) => { setSelectedCategory(value); setPage(1); }}
              options={categories.map((category) => ({ value: category.id, label: category.name }))}
            />
          </div>

          <Divider style={{ borderColor: '#434343' }} />

          {/* 价位 */}
          <div className="filter-group">
            <div className="filter-label">最低票价范围</div>
            <div className="price-inputs">
              <InputNumber
                min={0}
                precision={0}
                value={priceRange[0]}
                onChange={(value) => setPriceRange([value ?? 0, priceRange[1]])}
                prefix="¥"
                placeholder="最低"
              />
              <span className="price-separator">至</span>
              <InputNumber
                min={0}
                precision={0}
                value={priceRange[1]}
                onChange={(value) => setPriceRange([priceRange[0], value ?? 0])}
                prefix="¥"
                placeholder="最高"
              />
            </div>
          </div>

          <Divider style={{ borderColor: '#434343' }} />

          <Button type="primary" block size="large" onClick={handleSearch} style={{ background: '#ff4d4f', border: 'none' }}>
            应用筛选
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
              <Grid<ShowGridCellProps>
                columnCount={columnCount}
                columnWidth={`${100 / columnCount}%`}
                rowCount={Math.ceil(shows.length / columnCount)}
                  rowHeight={gridRowHeight}
                overscanCount={2}
                cellComponent={ShowGridCell}
                cellProps={{ shows, getPoster, navigate, columnCount }}
                defaultHeight={Math.min(720, Math.max(gridRowHeight, Math.ceil(shows.length / columnCount) * gridRowHeight))}
                style={{ height: Math.min(720, Math.max(gridRowHeight, Math.ceil(shows.length / columnCount) * gridRowHeight)), width: '100%' }}
                className="show-grid"
              />
            ) : (
              !loading && <Empty description="没有找到符合条件的演出" />
            )}
          </Spin>

          {/* 简单分页 */}
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
