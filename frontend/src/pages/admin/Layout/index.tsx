import { Layout, Menu } from 'antd'
import { Outlet, useNavigate, useLocation } from 'react-router-dom'
import {
  UnorderedListOutlined,
  ScheduleOutlined,
  ShoppingCartOutlined,
  PlusCircleOutlined,
  AppstoreOutlined,
  DashboardOutlined,
  NotificationOutlined,
  RollbackOutlined,
  SwapOutlined,
  QrcodeOutlined,
  TeamOutlined,
} from '@ant-design/icons'

const { Sider, Content } = Layout

const AdminLayout = () => {
  const navigate = useNavigate()
  const location = useLocation()

  // /admin（含 /admin/）默认渲染数据看板，此时菜单需高亮数据看板项
  const pathname = location.pathname.replace(/\/+$/, '') || '/'
  const selectedKey = pathname === '/admin' ? '/admin/dashboard' : pathname

  const menuItems = [
    {
      key: '/admin/dashboard',
      icon: <DashboardOutlined />,
      label: '数据看板',
    },
    {
      key: '/admin/user',
      icon: <TeamOutlined />,
      label: '用户管理',
    },
    {
      key: '/admin/performance',
      icon: <UnorderedListOutlined />,
      label: '演出管理',
    },
    {
      key: '/admin/session',
      icon: <ScheduleOutlined />,
      label: '场次管理',
    },
    {
      key: '/admin/order',
      icon: <ShoppingCartOutlined />,
      label: '订单管理',
    },
    {
      key: '/admin/refund-group',
      icon: <RollbackOutlined />,
      label: '退票管理',
      children: [
        { key: '/admin/refund', label: '退票审核' },
        { key: '/admin/refund-policy', label: '退票策略' },
      ],
    },
    {
      key: '/admin/exchange-group',
      icon: <SwapOutlined />,
      label: '改签管理',
      children: [
        { key: '/admin/exchange', label: '改签审核' },
        { key: '/admin/exchange-policy', label: '改签策略' },
      ],
    },
    {
      key: '/admin/redeem',
      icon: <QrcodeOutlined />,
      label: '电子票核销',
    },
    {
      key: '/admin/publish',
      icon: <PlusCircleOutlined />,
      label: '演出发布',
    },
    {
      key: '/admin/seat-map',
      icon: <AppstoreOutlined />,
      label: '座位图管理',
    },
    {
      key: '/admin/marketing',
      icon: <NotificationOutlined />,
      label: '营销配置',
    },
  ]

  return (
    <Layout style={{ height: '100vh', minWidth: 1280 }}>
      {/* 左侧深色侧边栏：随外层布局占满整屏，滚动只发生在右侧内容区 */}
      <Sider width={200} theme="dark" style={{ height: '100%', overflow: 'auto' }}>
        <div style={{
          height: 64,
          color: 'white',
          textAlign: 'center',
          lineHeight: '64px',
          fontSize: 16,
          fontWeight: 'bold',
        }}>
          管理后台
        </div>
        <Menu
          theme="dark"
          mode="inline"
          selectedKeys={[selectedKey]}
          defaultOpenKeys={['/admin/refund-group', '/admin/exchange-group']}
          items={menuItems}
          onClick={({ key }) => {
            // 分组父节点（退票管理/改签管理）只做展开收起，不导航
            if (key !== '/admin/refund-group' && key !== '/admin/exchange-group') {
              navigate(key)
            }
          }}
        />
      </Sider>

      {/* 右侧白色内容区：页面级滚动收敛到内容区，避免撑大整页导致侧边栏留白 */}
      <Layout style={{ overflow: 'auto' }}>
        {/* flex:none + minHeight 保证白色内容卡随内容伸展，不被 antd 的 min-height:0 压缩成两层 */}
        <Content style={{
          margin: 24,
          padding: 24,
          background: 'white',
          borderRadius: 4,
          flex: 'none',
          minHeight: 'calc(100vh - 48px)',
        }}>
          <Outlet /> {/* 子页面内容显示在这里 */}
        </Content>
      </Layout>
    </Layout>
  )
}

export default AdminLayout