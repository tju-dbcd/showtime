import { lazy, Suspense } from 'react';
import { createBrowserRouter, Navigate } from 'react-router-dom';
import type { ReactNode } from 'react';
import Layout from '../components/Layout';
import Login from '../pages/Login';
import Register from '../pages/Register';

import AdminLayout from '../pages/admin/Layout';
import Performance from '../pages/admin/Performance';
import Session from '../pages/admin/Session';
import AdminOrder from '../pages/admin/Order';
import OrderDetail from '../pages/OrderDetail';
import Publish from '../pages/admin/Publish';
import SeatMapEditor from '../pages/admin/SeatMap';
import Dashboard from '../pages/admin/Dashboard';
import Marketing from '../pages/admin/Marketing';
import Refund from '../pages/admin/Refund';
import Exchange from '../pages/admin/Exchange';
import RefundPolicy from '../pages/admin/RefundPolicy';
import ExchangePolicy from '../pages/admin/ExchangePolicy';
import SeatRule from '../pages/admin/SeatRule';
import Redeem from '../pages/admin/Redeem';
import AdminUser from '../pages/admin/User';

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

const RequireAuth = ({ children }: { children: ReactNode }) => (
  localStorage.getItem('accessToken')
    ? children
    : <Navigate to="/login" replace />
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
      { path: 'order/:id', element: <OrderDetail /> },
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
    element: <RequireAuth>{withSuspense(UserCenter)}</RequireAuth>,
  },

  // ========== 管理端路由 ==========
  {
    path: '/admin',
    element: <AdminLayout />,
    children: [
      { index: true, element: <Dashboard /> },
      { path: 'dashboard', element: <Dashboard /> },
      { path: 'user', element: <AdminUser /> },
      { path: 'performance', element: <Performance /> },
      { path: 'session', element: <Session /> },
      { path: 'order', element: <AdminOrder /> },
      { path: 'refund', element: <Refund /> },
      { path: 'exchange', element: <Exchange /> },
      { path: 'refund-policy', element: <RefundPolicy /> },
      { path: 'exchange-policy', element: <ExchangePolicy /> },
      { path: 'seat-rule', element: <SeatRule /> },
      { path: 'redeem', element: <Redeem /> },
      { path: 'publish', element: <Publish /> },
      { path: 'seat-map', element: <SeatMapEditor /> },
      { path: 'marketing', element: <Marketing /> },
    ],
  },
]);

export default router;
