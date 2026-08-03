import { lazy, Suspense } from 'react';
import { createBrowserRouter } from 'react-router-dom';
import Layout from '../components/Layout';
import Login from '../pages/Login';
import Register from '../pages/Register';

import AdminLayout from '../pages/admin/Layout';
import Performance from '../pages/admin/Performance';
import Session from '../pages/admin/Session';
import AdminOrder from '../pages/admin/Order';
import Publish from '../pages/admin/Publish';

// ========== 客户端页面懒加载 ==========
const Home = lazy(() => import('../pages/Home'));
const Search = lazy(() => import('../pages/Search'));
const Order = lazy(() => import('../pages/Order'));
const PerformanceDetail = lazy(() => import('../pages/PerformanceDetail'));
const SeatSelection = lazy(() => import('../pages/SeatSelection'));
const UserCenter = lazy(() => import('../pages/UserCenter'));

// 加载中占位组件
const PageLoading = () => (
  <div style={{
    display: 'flex',
    justifyContent: 'center',
    alignItems: 'center',
    height: '100vh',
    fontSize: 16,
    color: '#999'
  }}>
    加载中...
  </div>
);

// 用 Suspense 包裹组件
const withSuspense = (Component: React.ComponentType) => (
  <Suspense fallback={<PageLoading />}>
    <Component />
  </Suspense>
);

const router = createBrowserRouter([
  // ========== 客户端路由：带顶部导航栏 ==========
  {
    path: '/',
    element: <Layout />,
    children: [
      { index: true, element: withSuspense(Home) },
      { path: 'search', element: withSuspense(Search) },
      { path: 'order', element: withSuspense(Order) },
      { path: 'performance/:id', element: withSuspense(PerformanceDetail) },
      { path: 'seat-selection/:eventId', element: withSuspense(SeatSelection) },
    ],
  },

  // ========== 客户端路由：无导航栏 ==========
  {
    path: '/login',
    element: <Login />,
  },
  {
    path: '/register',
    element: <Register />,
  },
  {
    path: '/usercenter',
    element: withSuspense(UserCenter),
  },

  // ========== 管理端路由 ==========
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
