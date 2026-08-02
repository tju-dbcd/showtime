import { createBrowserRouter } from 'react-router-dom';
// 客户端页面
import Layout from '../components/Layout';
import Home from '../pages/Home';
import Login from '../pages/Login';
import Register from '../pages/Register';
import Order from '../pages/Order';
import PerformanceDetail from '../pages/PerformanceDetail';
import SeatSelection from '../pages/SeatSelection';
import Search from '../pages/Search';
// 管理端
import AdminLayout from '../pages/admin/Layout';
import Performance from '../pages/admin/Performance';
import Session from '../pages/admin/Session';
import AdminOrder from '../pages/admin/Order';
import Publish from '../pages/admin/Publish';

const router = createBrowserRouter([
  //客户端路由:带顶部导航栏
  {
    path: '/',
    element: <Layout />,
    children: [
      { index: true, element: <Home /> },
      { path: 'search', element: <Search /> },
      { path: 'order', element: <Order /> },
      { path: 'performance/:id', element: <PerformanceDetail /> },
      { path: 'seat-selection/:eventId', element: <SeatSelection /> },
    ],
  },

  //客户端路由:无导航栏
  {
    path: '/login',
    element: <Login />,
  },
  {
    path: '/register',
    element: <Register />,
  },

  //管理端路由
  {
    path: '/admin',
    element: <AdminLayout />,
    children: [
      { index: true, element: <Performance /> },
      { path: 'performance', element: <Performance /> },
      { path: 'session', element: <Session /> },
      { path: 'order', element: <AdminOrder /> },
      { path: 'publish', element: <Publish /> },
    ],
  },
]);

export default router;
