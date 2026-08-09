import { addSession, addPricingStrategy, updateSessionStatus } from '../../../api/admin'
import { Form, Input, Select, DatePicker, Upload, Button, Card, message } from 'antd'
import { UploadOutlined } from '@ant-design/icons'

const { TextArea } = Input
const { RangePicker } = DatePicker

const Publish = () => {
  const [form] = Form.useForm()

  // 点击发布按钮
 const handleSubmit = async () => {
  try {
    const values = await form.validateFields()

    const startTime = values.time?.[0]?.toISOString()
    const endTime = values.time?.[1]?.toISOString()

    console.log('=== 开始测试接口 ===')

    // 1. 测试加场次
    console.log('1. 测试加场次...')
    const sessionRes = await addSession(1, {
      startTime,
      endTime,
      saleStartTime: startTime,
      saleEndTime: endTime,
      seatMapId: 1,
    })
    console.log('加场次返回：', sessionRes)
    const sessionId = sessionRes?.data?.sessionId || 1 // 拿返回的场次ID，没有就先用1

    // 2. 测试加定价策略
    console.log('2. 测试加定价策略...')
    const priceRes = await addPricingStrategy(sessionId, {
      seatSectionId: null,
      strategyName: '默认票价',
      priceType: 'NORMAL',
      saleStartTime: startTime,
      saleEndTime: endTime,
      priority: 1,
      quota: null,
    })
    console.log('加定价返回：', priceRes)

    // 3. 测试更新状态
    console.log('3. 测试更新状态...')
    const statusRes = await updateSessionStatus(sessionId, 'ON_SALE')
    console.log('更新状态返回：', statusRes)

    console.log('=== 所有接口测试完成 ===')
    message.success('所有接口测试完成，看控制台')
    form.resetFields()
  } catch (error) {
    console.error('测试失败：', error)
  }
}

  return (
    <div>
      {/* 页面标题 */}
      <h2 style={{ margin: '0 0 24px 0', fontSize: 18 }}>演出发布</h2>

      <Card style={{ maxWidth: 700 }}>
        <Form
          form={form}
          layout="vertical"
          initialValues={{ status: 'onSale' }}
        >
          {/* 演出名称 */}
          <Form.Item
            label="演出名称"
            name="name"
            rules={[{ required: true, message: '请输入演出名称' }]}
          >
            <Input placeholder="请输入演出名称" size="large" />
          </Form.Item>

          {/* 演出类型 */}
          <Form.Item
            label="演出类型"
            name="type"
            rules={[{ required: true, message: '请选择演出类型' }]}
          >
            <Select placeholder="请选择演出类型" size="large">
              <Select.Option value="concert">演唱会</Select.Option>
              <Select.Option value="drama">话剧</Select.Option>
              <Select.Option value="crosstalk">相声</Select.Option>
              <Select.Option value="musical">音乐剧</Select.Option>
              <Select.Option value="dance">舞蹈</Select.Option>
            </Select>
          </Form.Item>

          {/* 演出时间 */}
          <Form.Item
            label="演出时间"
            name="time"
            rules={[{ required: true, message: '请选择演出时间' }]}
          >
            <RangePicker
              showTime
              style={{ width: '100%' }}
              size="large"
              placeholder={['开始时间', '结束时间']}
            />
          </Form.Item>

          {/* 演出场地 */}
          <Form.Item
            label="演出场地"
            name="venue"
            rules={[{ required: true, message: '请输入演出场地' }]}
          >
            <Input placeholder="请输入演出场地" size="large" />
          </Form.Item>

          {/* 票价 */}
          <Form.Item
            label="票价（元）"
            name="price"
            rules={[{ required: true, message: '请输入票价' }]}
          >
            <Input placeholder="请输入票价，多档用逗号分隔，如：180,280,380" size="large" />
          </Form.Item>

          {/* 演出介绍 */}
          <Form.Item
            label="演出介绍"
            name="description"
            rules={[{ required: true, message: '请输入演出介绍' }]}
          >
            <TextArea
              rows={6}
              placeholder="请输入演出介绍"
              maxLength={500}
              showCount
            />
          </Form.Item>

          {/* 封面图片 */}
          <Form.Item
            label="封面图片"
            name="cover"
            rules={[{ required: true, message: '请上传封面图片' }]}
          >
            <Upload>
              <Button icon={<UploadOutlined />}>点击上传</Button>
            </Upload>
          </Form.Item>

          {/* 发布状态 */}
          <Form.Item
            label="发布状态"
            name="status"
            rules={[{ required: true, message: '请选择发布状态' }]}
          >
            <Select placeholder="请选择发布状态" size="large">
              <Select.Option value="onSale">立即上架</Select.Option>
              <Select.Option value="draft">存为草稿</Select.Option>
            </Select>
          </Form.Item>

          {/* 底部按钮 */}
          <Form.Item style={{ marginTop: 32 }}>
            <Button type="primary" size="large" onClick={handleSubmit} style={{ width: 120 }}>
              发布
            </Button>
            <Button size="large" style={{ marginLeft: 16, width: 120 }} onClick={() => form.resetFields()}>
              重置
            </Button>
          </Form.Item>
        </Form>
      </Card>
    </div>
  )
}

export default Publish