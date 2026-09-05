import { Card, Empty, Tabs } from 'antd';

const Marketing = () => {
  return (
    <div>
      <Card size="small" style={{ marginBottom: 16 }}>
        <span style={{ color: '#999' }}>演出营销配置：推荐位管理、广告 Banner 管理</span>
      </Card>

      <Card size="small">
        <Tabs
          items={[
            {
              key: 'recommend',
              label: '推荐位管理',
              children: (
                <Empty
                  description="等待后端 MarketingContent API 接口"
                  style={{ padding: '60px 0' }}
                />
              ),
            },
            {
              key: 'banner',
              label: '广告 Banner',
              children: (
                <Empty
                  description="等待后端 MarketingContent API 接口"
                  style={{ padding: '60px 0' }}
                />
              ),
            },
          ]}
        />
      </Card>
    </div>
  );
};

export default Marketing;
