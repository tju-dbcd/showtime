import { useState, useEffect, useRef } from 'react';  // ← 加 useRef
import { useUser } from '@/context/UserContext';     // ← 新增
import { useNavigate } from 'react-router-dom';
import { Layout, Menu, Avatar, Typography, Button, Form, Input, Modal, message, Table, Tag, Divider, Card } from 'antd';
import {
  UserOutlined,
  LockOutlined,
  IdcardOutlined,
  UnorderedListOutlined,
  HomeOutlined,
  LogoutOutlined,
  EditOutlined,
  PlusOutlined,
  DeleteOutlined,
  CheckCircleOutlined,
  CloseCircleOutlined,
} from '@ant-design/icons';
import type { Address } from '@/mock/user';
import { mockOrders } from '@/mock/orders';
import './UserCenter.css';

const { Sider, Content } = Layout;
const { Title, Text } = Typography;

// 菜单项配置
const menuItems = [
  { key: 'profile', icon: <UserOutlined />, label: '个人资料' },
  { key: 'security', icon: <LockOutlined />, label: '账号安全' },
  { key: 'verify', icon: <IdcardOutlined />, label: '实名认证' },
  { key: 'orders', icon: <UnorderedListOutlined  />, label: '我的订单' },
  { key: 'address', icon: <HomeOutlined />, label: '收货地址' },
  { key: 'logout', icon: <LogoutOutlined />, label: '退出登录', danger: true },
];

const UserCenter = () => {
  const navigate = useNavigate();
  const [selectedKey, setSelectedKey] = useState('profile');
  const { user, updateUser } = useUser();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [isAddressModalOpen, setIsAddressModalOpen] = useState(false);
  const [editingAddress, setEditingAddress] = useState<Address | null>(null);
  const [form] = Form.useForm();
  const [addressForm] = Form.useForm();

  // 检查登录状态
  useEffect(() => {
    const token = localStorage.getItem('accessToken');
    if (!token) {
      navigate('/login');
    }
  }, [navigate]);

  const handleAvatarUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    if (file.size > 2 * 1024 * 1024) {
      message.warning('图片大小不能超过 2MB');
      return;
    }

    const reader = new FileReader();
    reader.onload = (event) => {
      const dataUrl = event.target?.result as string;
      updateUser({ avatar: dataUrl });
      message.success('头像更新成功！');
    };
    reader.onerror = () => {
      message.error('读取文件失败，请重试');
    };
    reader.readAsDataURL(file);
    e.target.value = '';
  };

  // 渲染个人资料
  const renderProfile = () => (
    <div className="uc-content">
      <Title level={3}>个人资料</Title>
      <Divider />
      <div className="profile-info">
        <div className="profile-avatar">
          <Avatar src={user.avatar} size={100} />
          <Button
            icon={<EditOutlined />}
            size="small"
            style={{ marginTop: 12 }}
            onClick={() => fileInputRef.current?.click()}
          >
            更换头像
          </Button>
          <input
            ref={fileInputRef}
            type="file"
            accept="image/*"
            style={{ display: 'none' }}
            onChange={handleAvatarUpload}
          />
        </div>
        <div className="profile-details">
          <div className="detail-row">
            <span className="label">昵称</span>
            <span className="value">{user.nickname || user.username || '用户'}</span>
            <Button type="link" size="small" onClick={() => setIsEditModalOpen(true)}>
              编辑
            </Button>
          </div>
          <div className="detail-row">
            <span className="label">用户名</span>
            <span className="value">{user.username}</span>
          </div>
          <div className="detail-row">
            <span className="label">手机号</span>
            <span className="value">{user.phone}</span>
            <Button type="link" size="small" onClick={() => setIsEditModalOpen(true)}>
              修改
            </Button>
          </div>
          <div className="detail-row">
            <span className="label">邮箱</span>
            <span className="value">{user.email}</span>
            <Button type="link" size="small" onClick={() => setIsEditModalOpen(true)}>
              修改
            </Button>
          </div>
        </div>
      </div>
    </div>
  );

  // 渲染账号安全
  const renderSecurity = () => (
    <div className="uc-content">
      <Title level={3}>账号安全</Title>
      <Divider />
      <Card title="修改密码" className="security-card">
        <Form layout="vertical">
          <Form.Item label="当前密码" required>
            <Input.Password placeholder="请输入当前密码" />
          </Form.Item>
          <Form.Item label="新密码" required>
            <Input.Password placeholder="请输入新密码（至少6位）" />
          </Form.Item>
          <Form.Item label="确认新密码" required>
            <Input.Password placeholder="请再次输入新密码" />
          </Form.Item>
          <Form.Item>
            <Button type="primary">确认修改</Button>
          </Form.Item>
        </Form>
      </Card>
      <Card title="绑定手机" className="security-card">
        <div className="bind-info">
          <Text>已绑定手机：{user.phone}</Text>
          <Button type="link">更换绑定</Button>
        </div>
      </Card>
      <Card title="绑定邮箱" className="security-card">
        <div className="bind-info">
          <Text>已绑定邮箱：{user.email}</Text>
          <Button type="link">更换绑定</Button>
        </div>
      </Card>
    </div>
  );

  // 渲染实名认证
  const renderVerify = () => (
    <div className="uc-content">
      <Title level={3}>实名认证</Title>
      <Divider />
      <Card className="verify-card">
        <div className="verify-status">
          <div className="status-icon">
            {user.isVerified ? (
              <CheckCircleOutlined style={{ color: '#52c41a', fontSize: 48 }} />
            ) : (
              <CloseCircleOutlined style={{ color: '#ff4d4f', fontSize: 48 }} />
            )}
          </div>
          <div className="status-text">
            <Title level={4}>
              {user.isVerified ? '已实名认证' : '未实名认证'}
            </Title>
            <Text type="secondary">
              {user.isVerified
                ? '您已完成实名认证，可使用全部功能'
                : '请完善实名信息以使用更多功能'}
            </Text>
          </div>
        </div>
        <Divider />
        <div className="verify-info">
          <div className="detail-row">
            <span className="label">真实姓名</span>
            <span className="value">{user.isVerified ? user.realName : '****'}</span>
          </div>
          <div className="detail-row">
            <span className="label">身份证号</span>
            <span className="value">
              {user.isVerified
                ? `${user.idCard.slice(0, 6)}********${user.idCard.slice(-4)}`
                : '******************'}
            </span>
          </div>
        </div>
        {!user.isVerified && (
          <Button type="primary" block style={{ marginTop: 16 }}>
            去认证
          </Button>
        )}
      </Card>
    </div>
  );

  // 渲染我的订单
  const renderOrders = () => (
    <div className="uc-content">
      <div className="orders-header">
        <Title level={3}>我的订单</Title>
        <Button type="link" onClick={() => navigate('/order')}>
          查看全部 &gt;
        </Button>
      </div>
      <Divider />
      <Table
        dataSource={mockOrders}
        columns={[
          { title: '订单号', dataIndex: 'id', key: 'id', width: 160 },
          { title: '演出名称', dataIndex: 'eventName', key: 'eventName', width: 200 },
          { title: '座位', dataIndex: 'seats', key: 'seats', width: 140 },
          { title: '金额', dataIndex: 'amount', key: 'amount', render: (v) => `¥${v}` },
          {
            title: '状态',
            dataIndex: 'status',
            key: 'status',
            render: (s) => {
              const map = { paid: '已支付', pending: '待支付', cancelled: '已取消' };
              const color = { paid: 'green', pending: 'orange', cancelled: 'red' };
              return <Tag color={color[s as keyof typeof color]}>{map[s as keyof typeof map]}</Tag>;
            },
          },
        ]}
        rowKey="id"
        pagination={false}
        size="small"
      />
    </div>
  );

  // 渲染收货地址
  const renderAddress = () => (
    <div className="uc-content">
      <div className="address-header">
        <Title level={3}>收货地址</Title>
        <Button
          type="primary"
          icon={<PlusOutlined />}
          onClick={() => {
            setEditingAddress(null);
            addressForm.resetFields();
            setIsAddressModalOpen(true);
          }}
        >
          新增地址
        </Button>
      </div>
      <Divider />
      <div className="address-list">
        {user.addressList.map((addr) => (
          <Card key={addr.id} className="address-card" size="small">
            <div className="address-item">
              <div className="addr-info">
                <Text strong>{addr.name}</Text>
                <Text style={{ marginLeft: 12 }}>{addr.phone}</Text>
                {addr.isDefault && <Tag color="blue" style={{ marginLeft: 12 }}>默认</Tag>}
                <div style={{ marginTop: 4, color: '#666' }}>
                  {addr.province} {addr.city} {addr.district} {addr.detail}
                </div>
              </div>
              <div className="addr-actions">
                <Button
                  type="link"
                  size="small"
                  onClick={() => {
                    setEditingAddress(addr);
                    addressForm.setFieldsValue(addr);
                    setIsAddressModalOpen(true);
                  }}
                >
                  编辑
                </Button>
                <Button type="link" size="small" danger icon={<DeleteOutlined />}>
                  删除
                </Button>
              </div>
            </div>
          </Card>
        ))}
      </div>
    </div>
  );

  // 退出登录
  const handleLogout = () => {
    Modal.confirm({
      title: '确认退出',
      content: '确定要退出登录吗？',
      onOk: () => {
        localStorage.removeItem('token');
        message.success('已退出登录');
        navigate('/login');
      },
    });
  };

  // 编辑个人资料弹窗
  const handleEditProfile = (values: any) => {
    updateUser({
      ...user,
      nickname: values.nickname,
      phone: values.phone,
      email: values.email,
    });
    setIsEditModalOpen(false);
    message.success('个人信息已更新');
  };

  // 地址弹窗确认
  const handleAddressSubmit = (values: any) => {
    if (editingAddress) {
      // 编辑
      const updated = user.addressList.map((a) =>
        a.id === editingAddress.id ? { ...a, ...values } : a
      );
      updateUser({ ...user, addressList: updated });
      message.success('地址已更新');
    } else {
      // 新增
      const newAddress: Address = {
        id: Date.now(),
        ...values,
        isDefault: false,
      };
      updateUser({ ...user, addressList: [...user.addressList, newAddress] });
      message.success('地址已添加');
    }
    setIsAddressModalOpen(false);
    addressForm.resetFields();
  };

  // 根据选中菜单渲染内容
  const renderContent = () => {
    switch (selectedKey) {
      case 'profile':
        return renderProfile();
      case 'security':
        return renderSecurity();
      case 'verify':
        return renderVerify();
      case 'orders':
        return renderOrders();
      case 'address':
        return renderAddress();
      case 'logout':
        handleLogout();
        return null;
      default:
        return renderProfile();
    }
  };

  return (
    <div className="uc-container">
      {/* 顶部返回栏（取代导航栏） */}
      <div className="uc-topbar">
        <div className="uc-topbar-content">
          <span className="uc-logo" onClick={() => navigate('/')}>
            🎫 ShowTime
          </span>
          <span className="uc-back" onClick={() => navigate('/')}>
            ← 返回首页
          </span>
        </div>
      </div>

      {/* 主体：左侧菜单 + 右侧内容 */}
      <Layout className="uc-layout">
        <Sider theme="dark" width={240} className="uc-sider">
          <div className="uc-user-info">
            <Avatar src={user.avatar} size={56} />
            <div className="uc-user-name">{user.nickname || user.username || '用户'}</div>
            <div className="uc-user-id">ID: {user.username}</div>
          </div>
          <Menu
            theme="dark"
            mode="inline"
            selectedKeys={[selectedKey]}
            items={menuItems.map((item) => ({
              key: item.key,
              icon: item.icon,
              label: item.label,
              danger: item.danger,
              onClick: () => {
                if (item.key === 'logout') {
                  handleLogout();
                } else {
                  setSelectedKey(item.key);
                }
              },
            }))}
            className="uc-menu"
          />
        </Sider>

        <Content className="uc-content-area">
          {selectedKey !== 'logout' && renderContent()}
        </Content>
      </Layout>

      {/* 编辑个人资料弹窗 */}
      <Modal
        title="编辑个人资料"
        open={isEditModalOpen}
        onCancel={() => setIsEditModalOpen(false)}
        footer={null}
        destroyOnClose
      >
        <Form
          form={form}
          layout="vertical"
          initialValues={{
            nickname: user.nickname,
            phone: user.phone,
            email: user.email,
          }}
          onFinish={handleEditProfile}
        >
          <Form.Item
            label="昵称"
            name="nickname"
            rules={[{ required: true, message: '请输入昵称' }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            label="手机号"
            name="phone"
            rules={[
              { required: true, message: '请输入手机号' },
              { pattern: /^1\d{10}$/, message: '请输入正确的手机号' },
            ]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            label="邮箱"
            name="email"
            rules={[
              { required: true, message: '请输入邮箱' },
              { type: 'email', message: '请输入正确的邮箱格式' },
            ]}
          >
            <Input />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" block>
              保存修改
            </Button>
          </Form.Item>
        </Form>
      </Modal>

      {/* 地址弹窗 */}
      <Modal
        title={editingAddress ? '编辑地址' : '新增地址'}
        open={isAddressModalOpen}
        onCancel={() => setIsAddressModalOpen(false)}
        footer={null}
        destroyOnClose
      >
        <Form
          form={addressForm}
          layout="vertical"
          onFinish={handleAddressSubmit}
        >
          <Form.Item label="收货人" name="name" rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item label="手机号" name="phone" rules={[{ required: true, pattern: /^1\d{10}$/ }]}>
            <Input />
          </Form.Item>
          <Form.Item label="省份" name="province" rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item label="城市" name="city" rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item label="区/县" name="district" rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item label="详细地址" name="detail" rules={[{ required: true }]}>
            <Input.TextArea rows={2} />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" block>
              {editingAddress ? '更新地址' : '添加地址'}
            </Button>
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default UserCenter;
