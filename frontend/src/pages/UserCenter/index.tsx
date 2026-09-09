import { useState, useEffect } from 'react';
import { useUser } from '@/context/UserContext';
import { useNavigate } from 'react-router-dom';
import { Layout, Menu, Avatar, Typography, Button, Form, Input, Modal, message, Divider, Card, Empty } from 'antd';
import {
  UserOutlined,
  LockOutlined,
  IdcardOutlined,
  LogoutOutlined,
  CheckCircleOutlined,
  CloseCircleOutlined,
  SettingOutlined,
} from '@ant-design/icons';
import FileUploader from '@/components/FileUploader';
import { updateAvatar } from '@/api/user';
import { userAPI } from '@/api/requests';
import type { components } from '@/api/types';
import './UserCenter.css';

const { Sider, Content } = Layout;
const { Title, Text } = Typography;
type RealName = components['schemas']['UserRealNameResponse'];

// 菜单项配置
const baseMenuItems = [
  { key: 'profile', icon: <UserOutlined />, label: '个人资料' },
  { key: 'security', icon: <LockOutlined />, label: '账号安全' },
  { key: 'verify', icon: <IdcardOutlined />, label: '实名认证' },
  { key: 'logout', icon: <LogoutOutlined />, label: '退出登录', danger: true },
];

const UserCenter = () => {
  const navigate = useNavigate();
  const [selectedKey, setSelectedKey] = useState('profile');
  const { user, updateUser } = useUser();
  // 管理端入口：仅 Admin 角色可见
  const isAdmin = Array.isArray(user.roles) && user.roles.includes('Admin');
  const menuItems = [
    ...baseMenuItems.slice(0, 3),
    ...(isAdmin ? [{ key: 'admin', icon: <SettingOutlined />, label: '管理后台' }] : []),
    ...baseMenuItems.slice(3),
  ];
  const [avatarUpdating, setAvatarUpdating] = useState(false);
  const [realNames, setRealNames] = useState<RealName[]>([]);
  const [realNameModalOpen, setRealNameModalOpen] = useState(false);
  const [editingRealName, setEditingRealName] = useState<RealName | null>(null);
  const [realNameForm] = Form.useForm();

  // 检查登录状态
  useEffect(() => {
    const token = localStorage.getItem('accessToken');
    if (!token) {
      navigate('/login');
    }
  }, [navigate]);

  useEffect(() => {
    const loadRealNames = async () => {
      const { data, error } = await userAPI.listRealNames();
      if (error || !data?.success) {
        message.error(data?.message || '实名认证信息加载失败');
        return;
      }
      setRealNames((data.data || []) as RealName[]);
    };
    void loadRealNames();
  }, []);

  // 头像经 OSS 上传后先调后端持久化（AVATAR_URL），成功再更新本地 context/localStorage，刷新后仍显示。
  const handleAvatarChange = async (url?: string) => {
    if (!url) return;
    try {
      setAvatarUpdating(true);
      await updateAvatar(url);
      updateUser({ avatar: url });
      message.success('头像更新成功！');
    } catch (err) {
      message.error(err instanceof Error ? err.message : '头像保存失败，请重试');
    } finally {
      setAvatarUpdating(false);
    }
  };

  const openRealNameModal = (realName: RealName | null) => {
    setEditingRealName(realName);
    realNameForm.setFieldsValue(realName ? { realName: realName.realName, idCardNo: '' } : {});
    setRealNameModalOpen(true);
  };

  const refreshRealNames = async () => {
    const { data } = await userAPI.listRealNames();
    if (data?.success) setRealNames((data.data || []) as RealName[]);
  };

  const handleRealNameSubmit = async (values: { realName: string; idCardNo: string }) => {
    const result = editingRealName
      ? await userAPI.updateRealName(Number(editingRealName.realNameId), values)
      : await userAPI.createRealName({ ...values, isDefault: realNames.length === 0 });
    if (result.error || !result.data?.success) {
      message.error(result.data?.message || '实名认证保存失败');
      return;
    }
    message.success(editingRealName ? '实名认证已更新' : '实名认证已添加');
    setRealNameModalOpen(false);
    realNameForm.resetFields();
    await refreshRealNames();
  };

  const setDefaultRealName = async (realNameId: number) => {
    const { data, error } = await userAPI.setDefaultRealName(realNameId);
    if (error || !data?.success) {
      message.error(data?.message || '设置默认实名失败');
      return;
    }
    await refreshRealNames();
  };

  const deleteRealName = async (realNameId: number) => {
    const confirmed = await new Promise<boolean>((resolve) => {
      Modal.confirm({
        title: '确认删除实名认证？',
        content: '删除后如需使用实名购票，需要重新添加实名信息。',
        onOk: () => resolve(true),
        onCancel: () => resolve(false),
      });
    });
    if (!confirmed) return;
    const { data, error } = await userAPI.deleteRealName(realNameId);
    if (error || !data?.success) {
      message.error(data?.message || '删除实名认证失败');
      return;
    }
    message.success('实名认证已删除');
    await refreshRealNames();
  };

  // 渲染个人资料
  const renderProfile = () => (
    <div className="uc-content">
      <Title level={3}>个人资料</Title>
      <Divider />
      <div className="profile-info">
        <div className="profile-avatar">
          <FileUploader
            folder="avatar"
            value={user.avatar}
            onChange={handleAvatarChange}
            showUploadList={false}
          >
            <Avatar src={user.avatar} size={100} />
          </FileUploader>
          <div style={{ marginTop: 12, color: '#999', fontSize: 12 }}>
            {avatarUpdating ? '正在保存...' : '点击头像更换'}
          </div>
        </div>
        <div className="profile-details">
          <div className="detail-row">
            <span className="label">昵称</span>
            <span className="value">{user.nickname || user.username || '用户'}</span>
          </div>
          <div className="detail-row">
            <span className="label">用户名</span>
            <span className="value">{user.username}</span>
          </div>
          <div className="detail-row">
            <span className="label">手机号</span>
            <span className="value">{user.phone}</span>
          </div>
          <div className="detail-row">
            <span className="label">邮箱</span>
            <span className="value">{user.email}</span>
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
      <Card title="绑定手机" className="security-card">
        <div className="bind-info">
          <Text>已绑定手机：{user.phone}</Text>
          <Button type="link" disabled>更换绑定</Button>
        </div>
      </Card>
      <Card title="绑定邮箱" className="security-card">
        <div className="bind-info">
          <Text>已绑定邮箱：{user.email}</Text>
          <Button type="link" disabled>更换绑定</Button>
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
            {realNames.some((item) => item.isVerified) ? (
              <CheckCircleOutlined style={{ color: '#52c41a', fontSize: 48 }} />
            ) : (
              <CloseCircleOutlined style={{ color: '#ff4d4f', fontSize: 48 }} />
            )}
          </div>
          <div className="status-text">
            <Title level={4}>
              {realNames.some((item) => item.isVerified) ? '已实名认证' : '未实名认证'}
            </Title>
            <Text type="secondary">
              {realNames.some((item) => item.isVerified)
                ? '您已完成实名认证，可使用全部功能'
                : '请完善实名信息以使用更多功能'}
            </Text>
          </div>
        </div>
        <Divider />
        <div className="verify-info">
          {realNames.length === 0 && <Empty description="暂无实名信息" />}
          {realNames.map((item) => (
            <div className="detail-row" key={String(item.realNameId)}>
              <span className="label">{item.realName}{item.isDefault ? '（默认）' : ''}</span>
              <span className="value">{item.maskedIdCardNo}</span>
              <Button type="link" size="small" onClick={() => void setDefaultRealName(Number(item.realNameId))} disabled={item.isDefault}>设为默认</Button>
              {!item.isVerified && <Button type="link" size="small" onClick={() => openRealNameModal(item)}>编辑</Button>}
              <Button type="link" size="small" danger onClick={() => void deleteRealName(Number(item.realNameId))}>删除</Button>
            </div>
          ))}
        </div>
        <Button type="primary" block style={{ marginTop: 16 }} onClick={() => openRealNameModal(null)}>新增实名认证</Button>
      </Card>
    </div>
  );

  // 退出登录
  const handleLogout = () => {
    Modal.confirm({
      title: '确认退出',
      content: '确定要退出登录吗？',
      onOk: () => {
        localStorage.removeItem('accessToken');
        localStorage.removeItem('user');
        message.success('已退出登录');
        navigate('/login');
      },
    });
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
          <img className="uc-logo" src="/logo.png" alt="ShowTime" onClick={() => navigate('/')} />
          <span className="uc-back" onClick={() => navigate('/')}>
            <img className="uc-back-icon" src="/return.png" alt="" />
            返回首页
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
                if (item.key === 'admin') {
                  navigate('/admin');
                } else if (item.key === 'logout') {
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

      {/* 实名认证弹窗 */}
      <Modal
        title={editingRealName ? '编辑实名认证' : '新增实名认证'}
        open={realNameModalOpen}
        onCancel={() => setRealNameModalOpen(false)}
        footer={null}
        destroyOnClose
      >
        <Form
          form={realNameForm}
          layout="vertical"
          onFinish={handleRealNameSubmit}
        >
          <Form.Item label="真实姓名" name="realName" rules={[{ required: true, message: '请输入真实姓名' }]}>
            <Input />
          </Form.Item>
          <Form.Item label="身份证号" name="idCardNo" rules={[{ required: true, message: '请输入身份证号' }]}>
            <Input />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" block>
              保存实名认证
            </Button>
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default UserCenter;
