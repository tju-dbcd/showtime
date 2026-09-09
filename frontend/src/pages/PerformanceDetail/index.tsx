import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Button, message, Tag, Divider, Spin, Empty, Descriptions } from 'antd';
import { CalendarOutlined, ClockCircleOutlined } from '@ant-design/icons';
import { showAPI, marketingAPI } from '@/api/requests';
import type { ShowDto } from '@/types/api';
import type { components } from '@/api/types';
import './PerformanceDetail.css';

type MarketingContentDto = components['schemas']['MarketingContentDto'];
type MarketingContentType = components['schemas']['MarketingContentType'];

const marketingTypeText: Record<MarketingContentType, string> = {
  NOTICE: '公告',
  AD: '广告',
  PROMOTION: '推广',
};

const showStatusMeta: Record<string, { text: string; color: string }> = {
  DRAFT: { text: '草稿', color: 'default' },
  PUBLISHED: { text: '已发布', color: 'success' },
  UNPUBLISHED: { text: '已下架', color: 'warning' },
};

const auditStatusMeta: Record<string, { text: string; color: string }> = {
  PENDING: { text: '审核中', color: 'processing' },
  APPROVED: { text: '审核通过', color: 'success' },
  REJECTED: { text: '审核未通过', color: 'error' },
};

const PerformanceDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [show, setShow] = useState<ShowDto | null>(null);
  const [marketing, setMarketing] = useState<MarketingContentDto[]>([]);
  const [categoryNames, setCategoryNames] = useState<Map<number, string>>(new Map());
  const [loading, setLoading] = useState(true);

  // ========== 加载分类（用于详情页展示分类名称） ==========
  useEffect(() => {
    void showAPI.getCategories()
      .then(({ data, error }) => {
        if (!error && data?.success && Array.isArray(data.data)) {
          const map = new Map<number, string>();
          (data.data as Array<{ categoryId: number | string; categoryName?: string }>).forEach(item => {
            if (item.categoryId != null && item.categoryName) {
              map.set(Number(item.categoryId), item.categoryName);
            }
          });
          setCategoryNames(map);
        }
      })
      .catch(() => undefined);
  }, []);

  // ========== 获取演出详情 ==========
  useEffect(() => {
    const fetchDetail = async () => {
      if (!id) return;
      setLoading(true);
      try {
        const response: any = await showAPI.getShowDetail(Number(id));
        const result = response.data ? response.data : response;
        if (result.success && result.data) {
          setShow(result.data);
        } else {
          message.error(result.message || '获取演出详情失败');
        }
      } catch (error: any) {
        console.error('获取详情失败:', error);
        message.error(error.response?.data?.message || '获取演出详情失败');
      } finally {
        setLoading(false);
      }
    };
    fetchDetail();
  }, [id]);

  // ========== 获取演出关联的生效营销内容（公告/广告/推广） ==========
  useEffect(() => {
    const fetchMarketing = async () => {
      if (!id) return;
      try {
        const { data, error } = await marketingAPI.getShowMarketing(Number(id));
        if (!error && data?.success && Array.isArray(data.data)) {
          setMarketing(data.data as MarketingContentDto[]);
        } else {
          setMarketing([]);
        }
      } catch {
        setMarketing([]);
      }
    };
    void fetchMarketing();
  }, [id]);

  // ========== 加载中 ==========
  if (loading) {
    return (
      <div className="detail-container" style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '60vh' }}>
        <Spin size="large" tip="加载演出详情..." />
      </div>
    );
  }

  // ========== 未找到 ==========
  if (!show) {
    return (
      <div className="detail-container" style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '60vh', flexDirection: 'column' }}>
        <Empty description="演出不存在" />
        <Button onClick={() => navigate('/search')} style={{ marginTop: 16 }}>返回演出列表</Button>
      </div>
    );
  }

  // ========== 处理购票 ==========
  const handleBuyTicket = () => {
    navigate(`/seat-selection/${show.showId}`);
  };

  // ========== 格式化时长 ==========
  const formatDuration = (minutes: number | null): string => {
    if (!minutes) return '未知';
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    return hours > 0 ? `${hours}小时${mins > 0 ? ` ${mins}分钟` : ''}` : `${mins}分钟`;
  };

  // ========== 分类名称（无映射时回退到编号） ==========
  const getCategoryName = (categoryId: number | null | undefined): string => {
    if (categoryId == null) return '未分类';
    return categoryNames.get(Number(categoryId)) || `分类 #${categoryId}`;
  };

  return (
    <div className="detail-container">
      {/* ====== 顶部海报区 ====== */}
      <div
        className="detail-hero"
        style={{
          background: `linear-gradient(135deg, rgba(26,26,46,0.9) 0%, rgba(22,30,62,0.8) 100%), url(${show.posterUrl || 'https://picsum.photos/seed/fallback/1200/400'})`,
          backgroundSize: 'cover',
          backgroundPosition: 'center',
        }}
      >
        <div className="detail-hero-content">
          {/* 左侧：海报图 */}
          <div className="detail-left">
            <img
              src={show.posterUrl || 'https://picsum.photos/seed/fallback/300/400'}
              alt={show.showName}
              loading="lazy"
              decoding="async"
              className="detail-poster"
            />
          </div>

          {/* 右侧：信息 */}
          <div className="detail-right">
            <h1 className="detail-title">{show.showName}</h1>

            <div className="detail-meta">
              <div className="meta-item">
                <CalendarOutlined className="meta-icon" />
                <span>状态：{(showStatusMeta[show.status]?.text ?? show.status) || '未知'}</span>
              </div>
              <div className="meta-item">
                <ClockCircleOutlined className="meta-icon" />
                <span>时长：{formatDuration(show.durationMinutes)}</span>
              </div>
              <div className="meta-item">
                <Tag color={showStatusMeta[show.status]?.color ?? 'default'}>
                  {(showStatusMeta[show.status]?.text ?? show.status) || '未知'}
                </Tag>
              </div>
            </div>

            <div className="detail-description">
              <h3>演出简介</h3>
              <p>{show.description || '暂无简介'}</p>
            </div>
          </div>
        </div>
      </div>

      {/* ====== 下方内容区 ====== */}
      <div className="detail-body">
        {/* ====== 营销内容：公告/广告/推广 ====== */}
        {marketing.length > 0 && (
          <div className="detail-section">
            <h2>公告与活动</h2>
            <Divider />
            {marketing.map(item => (
              <div key={String(item.contentId)} className="marketing-item">
                {item.imageUrl && (
                  <img
                    className="marketing-img"
                    src={item.imageUrl}
                    alt={item.title}
                    loading="lazy"
                    decoding="async"
                  />
                )}
                <div className="marketing-body">
                  <div className="marketing-title">
                    <Tag
                      color={
                        item.contentType === 'NOTICE'
                          ? 'blue'
                          : item.contentType === 'AD'
                            ? 'gold'
                            : 'geekblue'
                      }
                    >
                      {marketingTypeText[item.contentType] || item.contentType}
                    </Tag>
                    <span>{item.title}</span>
                  </div>
                  {item.contentText && (
                    <p className="marketing-text">{item.contentText}</p>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}

        <div className="detail-section">
          <h2><img src="/show.png" alt="" />演出详情</h2>
          <Divider />
          <Descriptions
            className="show-info-descriptions"
            bordered
            size="middle"
            column={{ xs: 1, sm: 2 }}
          >
            <Descriptions.Item label="演出名称" span={2}>
              <span className="show-name-value">{show.showName}</span>
            </Descriptions.Item>
            <Descriptions.Item label="演出分类">
              {getCategoryName(show.categoryId)}
            </Descriptions.Item>
            <Descriptions.Item label="演出时长">
              {formatDuration(show.durationMinutes)}
            </Descriptions.Item>
            <Descriptions.Item label="演出状态">
              {(() => {
                const meta = showStatusMeta[show.status]
                return meta
                  ? <Tag color={meta.color}>{meta.text}</Tag>
                  : <Tag color="default">{show.status || '未知'}</Tag>
              })()}
            </Descriptions.Item>
            <Descriptions.Item label="审核状态">
              {(() => {
                const meta = auditStatusMeta[show.auditStatus]
                return meta
                  ? <Tag color={meta.color}>{meta.text}</Tag>
                  : <Tag color="default">{show.auditStatus || '未知'}</Tag>
              })()}
            </Descriptions.Item>
            <Descriptions.Item label="创建时间" span={2}>
              {new Date(show.createTime).toLocaleString('zh-CN')}
            </Descriptions.Item>
          </Descriptions>
        </div>

        {show.description && (
          <div className="detail-section">
            <h2><img src="/introduce.png" alt="" />详细介绍</h2>
            <Divider />
            <p style={{ whiteSpace: 'pre-wrap', lineHeight: 1.8 }}>{show.description}</p>
          </div>
        )}
      </div>

      {/* ====== 底部固定按钮 ====== */}
      <div className="detail-footer">
        <Button type="primary" size="large" className="buy-btn" onClick={handleBuyTicket}>
          立即抢票
        </Button>
      </div>
    </div>
  );
};

export default PerformanceDetail;
