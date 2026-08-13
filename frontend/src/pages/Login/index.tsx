import { Form, Input, Button, Card, message } from 'antd';
import { UserOutlined, LockOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { useState } from 'react';
import { client } from '../../api/request';

const Login = () => {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(false);

  const onFinish = async (values: { username: string; password: string }) => {
    setLoading(true);
    try {
      const { data, error } = await client.POST('/api/auth/login', {
        body: {
          account: values.username,
          password: values.password,
        },
      });

      if (error || !data?.success || !data.data?.accessToken) {
        message.error(data?.message || '登录失败，请检查用户名或密码');
        return;
      }

      localStorage.setItem('token', data.data.accessToken);
      message.success('登录成功！');
      setTimeout(() => {
        navigate('/');
      }, 500);
    } catch {
      message.error('登录失败，请检查网络连接');
    } finally {
      setLoading(false);
    }
  };

  const onFinishFailed = () => {
    message.error('请检查用户名或密码');
  };

  return (
    <div style={{
      display: 'flex',
      justifyContent: 'center',
      alignItems: 'center',
      height: '100vh',
      background: 'linear-gradient(135deg, #667eea 0%, #764ba2 100%)'
    }}>
      <Card
        title="🎫 票务系统登录"
        style={{ width: 400, boxShadow: '0 10px 25px rgba(0,0,0,0.1)' }}
        styles={{ header: { textAlign: 'center', fontSize: '20px' } }}
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
              登 录
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
