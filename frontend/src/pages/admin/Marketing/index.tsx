import { Card, Empty, Tabs } from 'antd';

const Marketing = () => {
  return (
    <div>
      <Card size="small">
        <Tabs
          items={[
            {
              key: 'recommend',
              label: '推荐位管理',
              children: (
                <Empty
                  description="待后端 MarketingContent API，暂未实现"
                  style={{ padding: '60px 0' }}
                />
              ),
            },
            {
              key: 'banner',
              label: '广告 Banner',
              children: (
                <Empty
                  description="待后端 MarketingContent API，暂未实现"
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
