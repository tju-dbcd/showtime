import { Form, Input, Button, Card, message } from 'antd';
import { UserOutlined, LockOutlined, MailOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';

const Register = () => {
  const navigate = useNavigate();

  const onFinish = (values: any) => {
    // 1. 打印注册数据（后续替换为 axios 请求）
    console.log('注册数据:', values);

    // 2. 假装注册成功
    localStorage.setItem('token', 'fake-token-456');
    message.success('注册成功！请登录 🎉');

    // 3. 延迟 0.5 秒后跳转到登录页
    setTimeout(() => {
      navigate('/login');
    }, 500);
  };

  const onFinishFailed = (errorInfo: any) => {
    message.error('请检查表单填写是否正确');
    console.log('注册失败:', errorInfo);
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
        title="🎫 用户注册"
        style={{ width: 420, boxShadow: '0 10px 25px rgba(0,0,0,0.1)' }}
        styles={{ header: { textAlign: 'center', fontSize: '20px' } }}
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
            rules={[{ required: true, message: '请输入用户名' }]}
          >
            <Input prefix={<UserOutlined />} placeholder="用户名" />
          </Form.Item>

          {/* 邮箱（可选，但加上显得更真实） */}
          <Form.Item
            name="email"
            rules={[{ type: 'email', message: '请输入有效的邮箱地址' }]}
          >
            <Input prefix={<MailOutlined />} placeholder="邮箱（选填）" />
          </Form.Item>

          {/* 密码 */}
          <Form.Item
            name="password"
            rules={[
              { required: true, message: '请输入密码' },
              { min: 6, message: '密码至少 6 位' }
            ]}
          >
            <Input.Password prefix={<LockOutlined />} placeholder="密码（至少6位）" />
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
            <Button type="primary" htmlType="submit" block>
              注 册
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
