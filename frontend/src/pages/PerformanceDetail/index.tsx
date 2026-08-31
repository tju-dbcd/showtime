import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Button, message, Tag, Divider, Spin, Empty } from 'antd';
import { CalendarOutlined, ClockCircleOutlined } from '@ant-design/icons';
import { showAPI } from '@/api/requests';
import type { ShowDto } from '@/types/api';
import './PerformanceDetail.css';

const PerformanceDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [show, setShow] = useState<ShowDto | null>(null);
  const [loading, setLoading] = useState(true);

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
              className="detail-poster"
            />
          </div>

          {/* 右侧：信息 */}
          <div className="detail-right">
            <h1 className="detail-title">{show.showName}</h1>

            <div className="detail-meta">
              <div className="meta-item">
                <CalendarOutlined className="meta-icon" />
                <span>状态：{show.status || '未知'}</span>
              </div>
              <div className="meta-item">
                <ClockCircleOutlined className="meta-icon" />
                <span>时长：{formatDuration(show.durationMinutes)}</span>
              </div>
              <div className="meta-item">
                <Tag color={show.status === 'Published' ? 'green' : 'orange'}>
                  {show.status || '未发布'}
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
        <div className="detail-section">
          <h2>🎬 演出详情</h2>
          <Divider />
          <div className="detail-info-grid">
            <div className="info-item">
              <span className="label">演出名称</span>
              <span className="value">{show.showName}</span>
            </div>
            <div className="info-item">
              <span className="label">分类 ID</span>
              <span className="value">{show.categoryId || '未分类'}</span>
            </div>
            <div className="info-item">
              <span className="label">时长</span>
              <span className="value">{formatDuration(show.durationMinutes)}</span>
            </div>
            <div className="info-item">
              <span className="label">状态</span>
              <span className="value">{show.status || '未知'}</span>
            </div>
            <div className="info-item">
              <span className="label">审核状态</span>
              <span className="value">{show.auditStatus || '未审核'}</span>
            </div>
            <div className="info-item">
              <span className="label">创建时间</span>
              <span className="value">{new Date(show.createTime).toLocaleString('zh-CN')}</span>
            </div>
          </div>
        </div>

        {show.description && (
          <div className="detail-section">
            <h2>📖 详细介绍</h2>
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
