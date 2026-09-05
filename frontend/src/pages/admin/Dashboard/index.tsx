import { useEffect, useRef, useState } from 'react';
import { Card, Row, Col, Statistic, Spin, message } from 'antd';
import {
  ShoppingCartOutlined,
  DollarOutlined,
  FundOutlined,
  FileTextOutlined,
} from '@ant-design/icons';
import * as echarts from 'echarts';
import { getAdminOrderList, getShowList } from '../../../api/admin';

const Dashboard = () => {
  const [loading, setLoading] = useState(true);
  const [stats, setStats] = useState({ totalOrders: 0, totalAmount: 0, totalTickets: 0, totalShows: 0 });

  const trendChartRef = useRef<HTMLDivElement>(null);
  const statusChartRef = useRef<HTMLDivElement>(null);
  const showChartRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const loadData = async () => {
      setLoading(true);
      try {
        const [orderRes, showRes] = await Promise.all([
          getAdminOrderList({ PageSize: 500 }),
          getShowList({ PageSize: 100 }),
        ]);

        const orders = orderRes.data?.data?.items || [];
        const shows = showRes.data?.data?.items || [];

        const paidOrders = orders.filter((o: { orderStatus: string }) =>
          o.orderStatus === 'PAID' || o.orderStatus === 'ISSUED'
        );

        const totalAmount = paidOrders.reduce((sum: number, o: { totalAmount?: string | number }) =>
          sum + (Number(o.totalAmount) || 0), 0);
        const totalTickets = paidOrders.reduce((sum: number, o: { ticketCount?: string | number }) =>
          sum + (Number(o.ticketCount) || 0), 0);

        setStats({
          totalOrders: orders.length,
          totalAmount,
          totalTickets,
          totalShows: shows.length,
        });

        // 订单趋势（按支付日期）
        const dateMap = new Map<string, { count: number; amount: number }>();
        paidOrders.forEach((o: { payTime?: string | null; totalAmount?: string | number }) => {
          if (!o.payTime) return;
          const date = o.payTime.slice(0, 10);
          const cur = dateMap.get(date) || { count: 0, amount: 0 };
          cur.count += 1;
          cur.amount += Number(o.totalAmount) || 0;
          dateMap.set(date, cur);
        });
        const sortedDates = Array.from(dateMap.keys()).sort();
        const trendCounts = sortedDates.map(d => dateMap.get(d)!.count);
        const trendAmounts = sortedDates.map(d => dateMap.get(d)!.amount);

        if (trendChartRef.current) {
          const chart = echarts.init(trendChartRef.current);
          chart.setOption({
            tooltip: { trigger: 'axis' },
            legend: { data: ['订单数', '销售额'] },
            grid: { left: 50, right: 50, top: 40, bottom: 30 },
            xAxis: { type: 'category', data: sortedDates },
            yAxis: [
              { type: 'value', name: '订单数' },
              { type: 'value', name: '销售额(元)' },
            ],
            series: [
              { name: '订单数', type: 'line', data: trendCounts, smooth: true, itemStyle: { color: '#1677ff' } },
              { name: '销售额', type: 'line', yAxisIndex: 1, data: trendAmounts, smooth: true, itemStyle: { color: '#52c41a' } },
            ],
          });
        }

        // 订单状态分布
        const statusMap = new Map<string, number>();
        orders.forEach(o => {
          statusMap.set(o.orderStatus, (statusMap.get(o.orderStatus) || 0) + 1);
        });
        const statusLabels: Record<string, string> = {
          PENDING_PAY: '待支付', PAID: '已支付', ISSUED: '已出票',
          PART_REFUND: '部分退款', REFUNDED: '已退款', CANCELLED: '已取消',
        };
        const statusData = Array.from(statusMap.entries()).map(([k, v]) => ({
          name: statusLabels[k] || k, value: v,
        }));

        if (statusChartRef.current) {
          const chart = echarts.init(statusChartRef.current);
          chart.setOption({
            tooltip: { trigger: 'item' },
            legend: { bottom: 0 },
            series: [{
              type: 'pie', radius: ['40%', '70%'],
              data: statusData,
              label: { formatter: '{b}: {c} ({d}%)' },
            }],
          });
        }

        // 演出销量排行（按演出列表展示，票数暂为0待后端关联接口）
        const showTicketMap = new Map<number, { name: string; tickets: number }>();
        shows.forEach((s: { showId: string | number; showName: string }) => {
          showTicketMap.set(Number(s.showId), { name: s.showName || `演出${s.showId}`, tickets: 0 });
        });
        const showRankData = Array.from(showTicketMap.values())
          .sort((a, b) => b.tickets - a.tickets)
          .slice(0, 10);

        if (showChartRef.current) {
          const chart = echarts.init(showChartRef.current);
          chart.setOption({
            tooltip: { trigger: 'axis' },
            grid: { left: 120, right: 30, top: 20, bottom: 30 },
            xAxis: { type: 'value', name: '售票数' },
            yAxis: { type: 'category', data: showRankData.map(s => s.name).reverse() },
            series: [{
              type: 'bar',
              data: showRankData.map(s => s.tickets).reverse(),
              itemStyle: { color: '#722ed1' },
              label: { show: true, position: 'right' },
            }],
          });
        }
      } catch {
        message.error('加载看板数据失败');
      } finally {
        setLoading(false);
      }
    };

    loadData();

    const handleResize = () => {
      [trendChartRef.current, statusChartRef.current, showChartRef.current].forEach(el => {
        if (el) echarts.getInstanceByDom(el)?.resize();
      });
    };
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  return (
    <Spin spinning={loading}>
      <Row gutter={16} style={{ marginBottom: 16 }}>
        <Col span={6}>
          <Card>
            <Statistic title="总订单数" value={stats.totalOrders} prefix={<ShoppingCartOutlined />} />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic title="总销售额(元)" value={stats.totalAmount} precision={2} prefix={<DollarOutlined />} />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic title="总售票数" value={stats.totalTickets} prefix={<FileTextOutlined />} />
          </Card>
        </Col>
        <Col span={6}>
          <Card>
            <Statistic title="演出数" value={stats.totalShows} prefix={<FundOutlined />} />
          </Card>
        </Col>
      </Row>

      <Row gutter={16}>
        <Col span={16}>
          <Card title="订单与销售趋势" size="small">
            <div ref={trendChartRef} style={{ height: 320 }} />
          </Card>
        </Col>
        <Col span={8}>
          <Card title="订单状态分布" size="small">
            <div ref={statusChartRef} style={{ height: 320 }} />
          </Card>
        </Col>
      </Row>

      <Row gutter={16} style={{ marginTop: 16 }}>
        <Col span={24}>
          <Card title="演出销量排行" size="small">
            <div ref={showChartRef} style={{ height: 320 }} />
          </Card>
        </Col>
      </Row>
    </Spin>
  );
};

export default Dashboard;
