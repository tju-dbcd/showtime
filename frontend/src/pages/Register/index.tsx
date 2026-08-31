import { useState } from 'react';
import { Form, Input, Button, Card, message } from 'antd';
import { UserOutlined, LockOutlined, MailOutlined, PhoneOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { authAPI } from '@/api/requests';

const Register = () => {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(false);

  const onFinish = async (values: {
    username: string;
    password: string;
    confirmPassword: string;
    phone: string;
    email?: string;
    nickname?: string;
  }) => {
    setLoading(true);
    try {
      const response: any = await authAPI.register({
        userName: values.username,
        password: values.password,
        phone: values.phone,
        email: values.email || null,
        nickname: values.nickname || null,
      });

      console.log('注册响应:', response);

      // 处理可能被拦截器解包或未解包的情况
      const result = response.data ? response.data : response;
      console.log('result:', result);

      // 判断成功：success 为 true 且有 data
      if (result.success === true && result.data) {
        message.success('注册成功！请登录 🎉');
        setTimeout(() => {
          navigate('/login');
        }, 500);
      } else {
        // 如果 success 为 false 或 data 为空，显示错误
        message.error(result.message || '注册失败，请重试');
      }
    } catch (error: any) {
      console.error('注册异常:', error);
      // 从 error.response 中提取后端返回的错误消息
      const msg = error.response?.data?.message || error.message || '注册失败';
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
        title="🎫 用户注册"
        style={{ width: 420, boxShadow: '0 10px 25px rgba(0,0,0,0.1)' }}
        headStyle={{ textAlign: 'center', fontSize: '20px' }}
      >
        <Form
          name="register"
          onFinish={onFinish}
          onFinishFailed={onFinishFailed}
          autoComplete="off"
          size="large"
        >
          {/* 用户名 */}
          <Form.Item
            name="username"
            rules={[
              { required: true, message: '请输入用户名' },
              { min: 3, message: '用户名至少 3 位' },
              { max: 50, message: '用户名最多 50 位' },
              { pattern: /^[A-Za-z][A-Za-z0-9_]{2,49}$/, message: '用户名以字母开头，仅支持字母、数字、下划线' },
            ]}
          >
            <Input prefix={<UserOutlined />} placeholder="用户名（3-50位，字母开头）" />
          </Form.Item>

          {/* 昵称（选填） */}
          <Form.Item name="nickname" rules={[{ max: 50, message: '昵称最多 50 位' }]}>
            <Input prefix={<UserOutlined />} placeholder="昵称（选填）" />
          </Form.Item>

          {/* 手机号 */}
          <Form.Item
            name="phone"
            rules={[
              { required: true, message: '请输入手机号' },
              { pattern: /^(?:\+?[0-9]{6,19}|[0-9]{20})$/, message: '请输入正确的手机号' },
            ]}
          >
            <Input prefix={<PhoneOutlined />} placeholder="手机号" />
          </Form.Item>

          {/* 邮箱（选填） */}
          <Form.Item name="email" rules={[{ type: 'email', message: '请输入有效的邮箱地址' }]}>
            <Input prefix={<MailOutlined />} placeholder="邮箱（选填）" />
          </Form.Item>

          {/* 密码 */}
          <Form.Item
            name="password"
            rules={[
              { required: true, message: '请输入密码' },
              { min: 8, message: '密码至少 8 位' },
              { max: 128, message: '密码最多 128 位' },
              { pattern: /^(?=.*[A-Za-z])(?=.*[0-9])[^\r\n]{8,128}$/, message: '密码必须包含字母和数字' },
            ]}
          >
            <Input.Password prefix={<LockOutlined />} placeholder="密码（至少8位，含字母和数字）" />
          </Form.Item>

          {/* 确认密码 */}
          <Form.Item
            name="confirmPassword"
            dependencies={['password']}
            rules={[
              { required: true, message: '请再次输入密码' },
              ({ getFieldValue }) => ({
                validator(_, value) {
                  if (!value || getFieldValue('password') === value) {
                    return Promise.resolve();
                  }
                  return Promise.reject(new Error('两次输入的密码不一致！'));
                },
              }),
            ]}
          >
            <Input.Password prefix={<LockOutlined />} placeholder="确认密码" />
          </Form.Item>

          {/* 注册按钮 */}
          <Form.Item>
            <Button type="primary" htmlType="submit" block loading={loading}>
              {loading ? '注册中...' : '注 册'}
            </Button>
          </Form.Item>

          {/* 跳转到登录 */}
          <div style={{ textAlign: 'center' }}>
            已有账号？<a onClick={() => navigate('/login')}>去登录</a>
          </div>
        </Form>
      </Card>
    </div>
  );
};

export default Register;
