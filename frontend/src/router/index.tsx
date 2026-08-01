import { createBrowserRouter } from 'react-router-dom'
import Home from '../pages/Home'
import Order from '../pages/Order'
import PerformanceDetail from '../pages/PerformanceDetail'
import AdminLayout from '../pages/admin/Layout'
import Performance from '../pages/admin/Performance'
import Session from '../pages/admin/Session'
import AdminOrder from '../pages/admin/Order'
import Publish from '../pages/admin/Publish'

const router = createBrowserRouter([
  // 客户端页面
  {
    path: '/',
    element: <Home />,
  },
  {
    path: '/order',
    element: <Order />,
  },
  {
    path: '/performance/:id',
    element: <PerformanceDetail />,
  },
  // 管理端页面
  {
    path: '/admin',
    element: <AdminLayout />,
    children: [
      {
        index: true, // 默认跳转到演出管理
        element: <Performance />,
      },
      {
        path: 'performance', // 演出管理（占位）
        element: <Performance />,
      },
      {
        path: 'session', // 场次管理（占位）
        element: <Session />,
      },
      {
        path: 'order', // 订单管理（占位）
        element: <AdminOrder />,
      },
      {
        path: 'publish', // 演出发布（具体写）
        element: <Publish />,
      },
    ],
  },
])

export default router