import { Form, Input, Select, DatePicker, Button, Card, message, InputNumber, Space, Divider } from 'antd'
import { PlusOutlined, DeleteOutlined } from '@ant-design/icons'
import { useState, useEffect } from 'react'
import {
  createShow,
  addSession,
  addPricingStrategies,
  updateSessionStatus,
  getSeatMapList,
  getSeatSections,
  type SeatMapResponse,
  type SeatSectionResponse,
} from '../../../api/admin'

const { TextArea } = Input

interface PriceItem {
  seatSectionId: number | undefined
  price: number
  priceType: string
}

const Publish = () => {
  const [form] = Form.useForm()
  const [loading, setLoading] = useState(false)
  const [seatMaps, setSeatMaps] = useState<SeatMapResponse[]>([])
  const [sections, setSections] = useState<SeatSectionResponse[]>([])
  const [selectedSeatMapId, setSelectedSeatMapId] = useState<number | undefined>()
  const [priceList, setPriceList] = useState<PriceItem[]>([
    { seatSectionId: undefined, price: 180, priceType: 'normal' }
  ])

  // 加载座位图列表
  useEffect(() => {
    const loadSeatMaps = async () => {
      try {
        const res = await getSeatMapList({ PageSize: 100 })
        if (res.data) {
          const result = (res.data as any).data || (res.data as any)
          setSeatMaps(result?.items || result || [])
        }
      } catch (err) {
        message.error('加载座位图失败')
      }
    }
    loadSeatMaps()
  }, [])

  // 选择座位图后加载票区
  const handleSeatMapChange = async (value: number) => {
    setSelectedSeatMapId(value)
    form.setFieldValue('seatMapId', value)
    try {
      const res = await getSeatSections(value, { PageSize: 100 })
      if (res.data) {
        const result = (res.data as any).data || (res.data as any)
        setSections(result?.items || result || [])
      }
    } catch (err) {
      message.error('加载票区失败')
    }
    // 重置已选的票区
    setPriceList(priceList.map(item => ({ ...item, seatSectionId: undefined })))
  }

  // 添加票价
  const addPrice = () => {
    setPriceList([...priceList, { seatSectionId: undefined, price: 180, priceType: 'normal' }])
  }

  // 删除票价
  const removePrice = (index: number) => {
    if (priceList.length === 1) {
      message.warning('至少保留一个票价')
      return
    }
    setPriceList(priceList.filter((_, i) => i !== index))
  }

  // 更新票价项
  const updatePrice = (index: number, field: keyof PriceItem, value: any) => {
    const newList = [...priceList]
    newList[index] = { ...newList[index], [field]: value }
    setPriceList(newList)
  }

  // 提交发布
  const handleSubmit = async () => {
    try {
      const values = await form.validateFields()

      // 校验票区都选了
      const invalidPrice = priceList.find(item => !item.seatSectionId)
      if (invalidPrice) {
        message.warning('请为每个票价选择票区')
        return
      }

      setLoading(true)

      // 1. 创建演出
      message.loading({ content: '正在创建演出...', key: 'publish' })
      const showRes = await createShow({
        showName: values.showName,
        categoryId: values.categoryId,
        description: values.description || null,
        durationMinutes: values.durationMinutes || null,
        posterUrl: values.posterUrl || null,
      })

      if (showRes.error) {
        throw new Error('创建演出失败')
      }

      const showId = (showRes.data as any)?.data?.showId
      if (!showId) {
        throw new Error('创建演出失败：未返回showId')
      }
      message.success({ content: '演出创建成功', key: 'publish' })

      // 2. 创建场次
      message.loading({ content: '正在创建场次...', key: 'session' })
      const sessionRes = await addSession(Number(showId), {
        sessionId: values.sessionId || Date.now(),
        startTime: values.time[0].toISOString(),
        endTime: values.time[1].toISOString(),
        saleStartTime: values.saleTime[0].toISOString(),
        saleEndTime: values.saleTime[1].toISOString(),
        seatMapId: values.seatMapId,
      })

      if (sessionRes.error) {
        throw new Error('创建场次失败')
      }

      const sessionId = (sessionRes.data as any)?.data?.sessionId || values.sessionId
      message.success({ content: '场次创建成功', key: 'session' })

      // 3. 添加定价策略
      message.loading({ content: '正在设置票价...', key: 'price' })
      const priceData = priceList.map(item => ({
        seatSectionId: item.seatSectionId as number,
        price: item.price,
        priceType: item.priceType,
        strategyName: `${item.priceType}-${item.price}`,
        saleStartTime: values.saleTime[0].toISOString(),
        saleEndTime: values.saleTime[1].toISOString(),
        priority: 0,
      }))

      const priceRes = await addPricingStrategies(Number(sessionId), priceData as any)
      if (priceRes.error) {
        throw new Error('设置票价失败')
      }
      message.success({ content: '票价设置成功', key: 'price' })

      // 4. 更新场次状态为上架
      message.loading({ content: '正在上架...', key: 'status' })
      const statusRes = await updateSessionStatus(Number(sessionId), { status: 'OnSale' })
      if (statusRes.error) {
        throw new Error('上架失败')
      }

      message.success({ content: '发布成功！', key: 'status' })
      form.resetFields()
      setPriceList([{ seatSectionId: undefined, price: 180, priceType: 'normal' }])
      setSelectedSeatMapId(undefined)
      setSections([])
    } catch (err: any) {
      message.error(err.message || '发布失败')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div>
      <h2 style={{ margin: '0 0 24px 0', fontSize: 18 }}>演出发布</h2>

      <Card style={{ maxWidth: 800 }}>
        <Form
          form={form}
          layout="vertical"
          initialValues={{
            categoryId: 1,
            durationMinutes: 120,
          }}
        >
          <Divider>演出基本信息</Divider>

          <Form.Item
            label="演出名称"
            name="showName"
            rules={[{ required: true, message: '请输入演出名称' }]}
          >
            <Input placeholder="请输入演出名称" size="large" />
          </Form.Item>

          <Space size="large" style={{ width: '100%' }}>
            <Form.Item
              label="演出分类"
              name="categoryId"
              rules={[{ required: true, message: '请选择分类' }]}
              style={{ width: 200 }}
            >
              <Select size="large">
                <Select.Option value={1}>演唱会</Select.Option>
                <Select.Option value={2}>话剧音乐剧</Select.Option>
                <Select.Option value={3}>曲苑杂坛</Select.Option>
                <Select.Option value={4}>体育赛事</Select.Option>
                <Select.Option value={5}>展览休闲</Select.Option>
              </Select>
            </Form.Item>

            <Form.Item
              label="演出时长（分钟）"
              name="durationMinutes"
              rules={[{ required: true, message: '请输入时长' }]}
            >
              <InputNumber min={1} size="large" style={{ width: 160 }} />
            </Form.Item>
          </Space>

          <Form.Item
            label="海报图片URL"
            name="posterUrl"
          >
            <Input placeholder="请输入海报图片链接（可选）" size="large" />
          </Form.Item>

          <Form.Item
            label="演出介绍"
            name="description"
          >
            <TextArea
              rows={4}
              placeholder="请输入演出介绍（可选）"
              maxLength={1000}
              showCount
            />
          </Form.Item>

          <Divider>场次信息</Divider>

          <Space size="large" style={{ width: '100%' }}>
            <Form.Item
              label="场次编号"
              name="sessionId"
              rules={[{ required: true, message: '请输入场次编号' }]}
              tooltip="场次唯一ID，可使用数字编号"
            >
              <InputNumber min={1} size="large" style={{ width: 160 }} />
            </Form.Item>

            <Form.Item
              label="座位图"
              name="seatMapId"
              rules={[{ required: true, message: '请选择座位图' }]}
              tooltip="选择场馆和座位图"
            >
              <Select
                size="large"
                style={{ width: 300 }}
                placeholder="请选择座位图"
                onChange={handleSeatMapChange}
                showSearch
                optionFilterProp="children"
              >
                {seatMaps.map(map => (
                  <Select.Option key={map.seatMapId} value={Number(map.seatMapId)}>
                    {map.venueName} / {map.mapName}
                  </Select.Option>
                ))}
              </Select>
            </Form.Item>
          </Space>

          <Form.Item
            label="演出时间"
            name="time"
            rules={[{ required: true, message: '请选择演出开始和结束时间' }]}
          >
            <DatePicker.RangePicker
              showTime
              style={{ width: '100%' }}
              size="large"
              placeholder={['演出开始时间', '演出结束时间']}
              format="YYYY-MM-DD HH:mm"
            />
          </Form.Item>

          <Form.Item
            label="售票时间"
            name="saleTime"
            rules={[{ required: true, message: '请选择售票开始和结束时间' }]}
          >
            <DatePicker.RangePicker
              showTime
              style={{ width: '100%' }}
              size="large"
              placeholder={['售票开始时间', '售票结束时间']}
              format="YYYY-MM-DD HH:mm"
            />
          </Form.Item>

          <Divider>票价设置</Divider>

          {priceList.map((item, index) => (
            <Space key={index} size="middle" align="start" style={{ display: 'flex', marginBottom: 16 }} wrap>
              <Form.Item label="票区" required>
                <Select
                  placeholder="请选择票区"
                  value={item.seatSectionId}
                  onChange={v => updatePrice(index, 'seatSectionId', v)}
                  size="large"
                  style={{ width: 180 }}
                  disabled={!selectedSeatMapId}
                  showSearch
                  optionFilterProp="children"
                >
                  {sections
                    .filter(s => s.isSellable)
                    .map(section => (
                      <Select.Option key={section.seatSectionId} value={Number(section.seatSectionId)}>
                        {section.sectionName}
                      </Select.Option>
                    ))}
                </Select>
              </Form.Item>
              <Form.Item label="价格（元）" required>
                <InputNumber
                  min={0}
                  step={10}
                  value={item.price}
                  onChange={v => updatePrice(index, 'price', v || 0)}
                  size="large"
                  style={{ width: 140 }}
                  prefix="¥"
                />
              </Form.Item>
              <Form.Item label="价格类型" required>
                <Select
                  value={item.priceType}
                  onChange={v => updatePrice(index, 'priceType', v)}
                  size="large"
                  style={{ width: 140 }}
                >
                  <Select.Option value="normal">普通票</Select.Option>
                  <Select.Option value="earlyBird">早鸟票</Select.Option>
                  <Select.Option value="vip">VIP票</Select.Option>
                  <Select.Option value="student">学生票</Select.Option>
                </Select>
              </Form.Item>
              <Button
                type="text"
                danger
                icon={<DeleteOutlined />}
                onClick={() => removePrice(index)}
                style={{ marginTop: 30 }}
              />
            </Space>
          ))}

          <Button
            type="dashed"
            onClick={addPrice}
            icon={<PlusOutlined />}
            style={{ width: '100%', marginBottom: 24 }}
            disabled={!selectedSeatMapId}
          >
            添加票价档位
          </Button>

          <Form.Item style={{ marginTop: 32 }}>
            <Button
              type="primary"
              size="large"
              onClick={handleSubmit}
              loading={loading}
              style={{ width: 140 }}
            >
              发布演出
            </Button>
            <Button
              size="large"
              style={{ marginLeft: 16, width: 140 }}
              onClick={() => {
                form.resetFields()
                setPriceList([{ seatSectionId: undefined, price: 180, priceType: 'normal' }])
                setSelectedSeatMapId(undefined)
                setSections([])
              }}
            >
              重置
            </Button>
          </Form.Item>
        </Form>
      </Card>
    </div>
  )
}

export default Publish
