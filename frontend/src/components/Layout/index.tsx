import { Outlet, useNavigate, useLocation } from 'react-router-dom';
import { Button, Avatar } from 'antd';
import { useUser } from '@/context/UserContext';
import './Layout.css';

const Layout = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const { user } = useUser();
  const isLoggedIn = !!localStorage.getItem('token');

  return (
    <div className="layout-container">
      <header className="header-nav">
        <div className="nav-content">
          <div className="logo" onClick={() => navigate('/')}>🎫 ShowTime</div>
          <div className="nav-links">
            <span
              className={location.pathname === '/' ? 'active' : ''}
              onClick={() => navigate('/')}
            >
              首页
            </span>
            <span
              className={location.pathname === '/search' ? 'active' : ''}
              onClick={() => navigate('/search')}
            >
              演出列表
            </span>
            <span
              className={location.pathname.startsWith('/performance/') ? 'active' : ''}
              onClick={() => navigate('/search')}
            >
              演出详情
            </span>
            <span
              className={location.pathname === '/order' ? 'active' : ''}
              onClick={() => navigate('/order')}
            >
              我的订单
            </span>
          </div>
          <div className="nav-right">
            {isLoggedIn ? (
              <div className="user-info" onClick={() => navigate('/usercenter')}>
                <Avatar src={user.avatar} size={32} />
                <span className="username">{user.nickname}</span>
              </div>
            ) : (
              <Button type="primary" ghost onClick={() => navigate('/login')}>
                登录
              </Button>
            )}
          </div>
        </div>
      </header>
      <main className="layout-content">
        <Outlet />
      </main>
    </div>
  );
};

export default Layout;
