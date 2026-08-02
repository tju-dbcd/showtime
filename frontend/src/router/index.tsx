import { createBrowserRouter } from 'react-router-dom'
import Home from '../pages/Home'
import Login from '../pages/Login'        // 新增
import Register from '../pages/Register'  // 新增
import Order from '../pages/Order'
import PerformanceDetail from '../pages/PerformanceDetail'
// 下面这几行是新增的
import AdminLayout from '../pages/admin/Layout'
import Performance from '../pages/admin/Performance'
import Session from '../pages/admin/Session'
import AdminOrder from '../pages/admin/Order'
import Publish from '../pages/admin/Publish'

const router = createBrowserRouter([
  // 客户端路由
  {
    path: '/',
    element: <Home />,
  },
  //用户端新增
    {
    path: '/login',
    element: <Login />,
  },
  {
    path: '/register',
    element: <Register />,
  },
  //用户端新增结束
  {
    path: '/order',
    element: <Order />,
  },
  {
    path: '/performance/:id',
    element: <PerformanceDetail />,
  },
  // 下面整个管理端新增的
  {
    path: '/admin',
    element: <AdminLayout />,
    children: [
      {
        index: true,
        element: <Performance />,
      },
      {
        path: 'performance',
        element: <Performance />,
      },
      {
        path: 'session',
        element: <Session />,
      },
      {
        path: 'order',
        element: <AdminOrder />,
      },
      {
        path: 'publish',
        element: <Publish />,
      },
    ],
  },
])

export default router