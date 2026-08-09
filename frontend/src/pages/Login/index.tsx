// src/pages/Login/index.tsx
import { useState } from 'react';
import { Form, Input, Button, Card, message } from 'antd';
import { UserOutlined, LockOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { authAPI } from '@/api/requests';
import { useUser } from '@/context/UserContext';

const Login = () => {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(false);
  const { updateUser } = useUser();

  const onFinish = async (values: { username: string; password: string }) => {
    setLoading(true);
    try {
      const response: any = await authAPI.login({
        account: values.username,
        password: values.password,
      });

      console.log('登录响应:', response);

      // 处理可能被拦截器解包或未解包的情况
      const result = response.data ? response.data : response;
      console.log('result:', result);

      if (result.success === true && result.data) {
        const loginData = result.data;
          const user = {
            ...loginData.user,
            username: loginData.user.userName, // 把 userName 映射到 username
          };
        localStorage.setItem('accessToken', loginData.accessToken);
        localStorage.setItem('user', JSON.stringify(loginData.user));
        updateUser(loginData.user);
        updateUser(user);
        console.log('token 存入成功:', localStorage.getItem('accessToken'));
        message.success('登录成功！');
        setTimeout(() => {
          window.location.href = '/';
        }, 500);
      } else {
        message.error(result.message || '登录失败，请检查账号密码');
      }
    } catch (error: any) {
      console.error('登录异常:', error);
      const msg = error.response?.data?.message || error.message || '登录失败';
      message.error(msg);
    } finally {
      setLoading(false);
    }
  };

  const onFinishFailed = (errorInfo: any) => {
    console.log('表单校验失败:', errorInfo);
  };

  return (
    <div
      style={{
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'center',
        height: '100vh',
        background: 'linear-gradient(135deg, #667eea 0%, #764ba2 100%)',
      }}
    >
      <Card
        title="票务系统登录"
        style={{ width: 400, boxShadow: '0 10px 25px rgba(0,0,0,0.1)' }}
        headStyle={{ textAlign: 'center', fontSize: '20px' }}
      >
        <Form
          name="login"
          onFinish={onFinish}
          onFinishFailed={onFinishFailed}
          autoComplete="off"
          size="large"
        >
          <Form.Item
            name="username"
            rules={[{ required: true, message: '请输入用户名' }]}
          >
            <Input prefix={<UserOutlined />} placeholder="用户名" />
          </Form.Item>

          <Form.Item
            name="password"
            rules={[{ required: true, message: '请输入密码' }]}
          >
            <Input.Password prefix={<LockOutlined />} placeholder="密码" />
          </Form.Item>

          <Form.Item>
            <Button type="primary" htmlType="submit" block loading={loading}>
              {loading ? '登录中...' : '登 录'}
            </Button>
          </Form.Item>

          <div style={{ textAlign: 'center' }}>
            还没有账号？<a onClick={() => navigate('/register')}>立即注册</a>
          </div>
        </Form>
      </Card>
    </div>
  );
};

export default Login;
