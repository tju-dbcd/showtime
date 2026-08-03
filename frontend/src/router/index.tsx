import { createBrowserRouter } from 'react-router-dom'
import Home from '../pages/Home'
import Order from '../pages/Order'
import PerformanceDetail from '../pages/PerformanceDetail'

const router = createBrowserRouter([
  {
    path: '/',
    element: <Home />,
  },
  {
    path: '/order',
    element: <Order />,
  },
  {
    path: '/performance/:id', // 假设带演出ID的详情页路由，后续可通过参数获取演出id
    element: <PerformanceDetail />,
  },
])

export default router