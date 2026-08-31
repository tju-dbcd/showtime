import { Layout, Menu } from 'antd'
import { Outlet, useNavigate, useLocation } from 'react-router-dom'
import {
  UnorderedListOutlined,
  ScheduleOutlined,
  ShoppingCartOutlined,
  PlusCircleOutlined,
  AppstoreOutlined,
} from '@ant-design/icons'

const { Sider, Content } = Layout

const AdminLayout = () => {
  const navigate = useNavigate()
  const location = useLocation()

  // 侧边栏4个菜单项
  const menuItems = [
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
      key: '/admin/publish',
      icon: <PlusCircleOutlined />,
      label: '演出发布',
    },
    {
      key: '/admin/seat-map',
      icon: <AppstoreOutlined />,
      label: '座位图管理',
    },
  ]

  return (
    <Layout style={{ minHeight: '100vh' }}>
      {/* 左侧深色侧边栏 */}
      <Sider width={200} theme="dark">
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
          selectedKeys={[location.pathname]}
          items={menuItems}
          onClick={({ key }) => navigate(key)}
        />
      </Sider>

      {/* 右侧白色内容区 */}
      <Layout>
        <Content style={{ margin: 24, padding: 24, background: 'white', borderRadius: 4 }}>
          <Outlet /> {/* 子页面内容显示在这里 */}
        </Content>
      </Layout>
    </Layout>
  )
}

export default AdminLayout