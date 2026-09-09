import { useState, useEffect, useRef } from 'react'
import {
  Card,
  Descriptions,
  Form,
  Input,
  Button,
  Space,
  Tag,
  message,
  Result,
  Alert,
} from 'antd'
import { ScanOutlined, CameraOutlined } from '@ant-design/icons'
import { Html5Qrcode } from 'html5-qrcode'
import { redeemTicket, type TicketRedemption } from '../../../api/admin'

const ticketStatusMap: Record<string, { text: string; color: string }> = {
  UNUSED: { text: '未使用', color: 'default' },
  USED: { text: '已核销', color: 'success' },
  REFUNDING: { text: '退款中', color: 'processing' },
  REFUNDED: { text: '已退款', color: 'default' },
  EXCHANGING: { text: '换票中', color: 'warning' },
  EXCHANGED: { text: '已换票', color: 'default' },
}

const Redeem = () => {
  const [form] = Form.useForm()
  const [submitting, setSubmitting] = useState(false)
  const [result, setResult] = useState<TicketRedemption | null>(null)
  const [errorMsg, setErrorMsg] = useState('')
  // 摄像头扫码状态
  const [scanning, setScanning] = useState(false)
  const [scanError, setScanError] = useState('')
  const scannerRef = useRef<Html5Qrcode | null>(null)

  // 扫码生命周期：scanning=true 时挂载扫码区并开启摄像头，关闭/组件卸载时释放摄像头
  useEffect(() => {
    if (!scanning) return
    let disposed = false
    const scanner = new Html5Qrcode('redeem-qr-reader', { verbose: false })
    scannerRef.current = scanner
    setScanError('')

    ;(async () => {
      try {
        await scanner.start(
          { facingMode: 'environment' },
          { fps: 10, qrbox: { width: 240, height: 240 } },
          (decodedText) => {
            if (disposed) return
            form.setFieldValue('qrCode', decodedText)
            setScanning(false)
            message.success('已识别电子票二维码内容，请点击“核销”确认')
          },
          () => undefined,
        )
      } catch {
        if (disposed) return
        setScanError('无法启动摄像头：请确认已授权摄像头，并通过 HTTPS 或 localhost 访问本页面。也可以直接粘贴二维码内容。')
        setScanning(false)
      }
    })()

    return () => {
      disposed = true
      scannerRef.current = null
      // start 失败/未运行等场景下 stop 可能同步抛异常，需分别兜底
      try {
        scanner.stop()
          .then(() => scanner.clear())
          .catch(() => undefined)
      } catch {
        try {
          scanner.clear()
        } catch {
          /* ignore */
        }
      }
    }
  }, [scanning, form])

  const handleRedeem = async () => {
    try {
      const values = await form.validateFields()
      setSubmitting(true)
      setErrorMsg('')
      setResult(null)
      try {
        // 未填写的可选字段（如核销设备）antd 不会放入 validateFields 结果，取值必须判空
        const qrCode = String(values.qrCode ?? '').trim()
        const checkDevice = String(values.checkDevice ?? '').trim() || 'admin-console'
        const res = await redeemTicket({ qrCode, checkDevice })
        if (res.error) {
          setErrorMsg(res.error.message || '核销失败')
          message.error('核销失败')
          return
        }
        if (res.data?.data) {
          const ticket = res.data.data
          setResult(ticket)
          const m = ticketStatusMap[ticket.ticketStatus]
          message.success(`核销成功：${ticket.eTicketNo}（${m ? m.text : ticket.ticketStatus}）`)
          form.setFieldValue('qrCode', '')
        } else {
          setErrorMsg('核销接口未返回数据')
          message.error('核销失败')
        }
      } catch {
        setErrorMsg('核销请求异常')
        message.error('核销请求异常')
      } finally {
        setSubmitting(false)
      }
    } catch {
      // 表单校验失败
    }
  }

  const renderResult = () => {
    if (errorMsg) {
      return (
        <Result
          status="error"
          title="核销失败"
          subTitle={errorMsg}
          style={{ padding: '24px 0' }}
        />
      )
    }
    if (!result) return null
    const m = ticketStatusMap[result.ticketStatus]
    return (
      <Card title="核销结果" style={{ marginTop: 16 }}>
        <Descriptions column={2} bordered size="small">
          <Descriptions.Item label="电子票ID">{result.eTicketId}</Descriptions.Item>
          <Descriptions.Item label="电子票号">{result.eTicketNo}</Descriptions.Item>
          <Descriptions.Item label="订单ID">{result.orderId}</Descriptions.Item>
          <Descriptions.Item label="订单明细ID">{result.orderItemId}</Descriptions.Item>
          <Descriptions.Item label="场次ID">{result.sessionId}</Descriptions.Item>
          <Descriptions.Item label="票状态">
            <Tag color={m ? m.color : 'default'}>{m ? m.text : result.ticketStatus}</Tag>
          </Descriptions.Item>
          <Descriptions.Item label="核销时间">
            {result.checkTime ? new Date(result.checkTime).toLocaleString('zh-CN') : '-'}
          </Descriptions.Item>
          <Descriptions.Item label="核销设备">{result.checkDevice}</Descriptions.Item>
          <Descriptions.Item label="核销人">{result.checkBy}</Descriptions.Item>
        </Descriptions>
      </Card>
    )
  }

  return (
    <div style={{ maxWidth: 680 }}>
      <Card title="电子票核销">
        <Form form={form} layout="vertical" onFinish={handleRedeem}>
          <Form.Item
            name="qrCode"
            label="电子票二维码内容"
            extra="需电子票的完整二维码内容（含票号与签名），输入纯票号无法核销"
            rules={[{ required: true, message: '请扫码或粘贴电子票完整二维码内容' }]}
          >
            <Input
              placeholder="扫码或粘贴电子票完整二维码内容"
              size="large"
              prefix={<ScanOutlined />}
              allowClear
            />
          </Form.Item>

          {scanning && (
            <div
              style={{
                marginBottom: 16,
                padding: 12,
                background: '#fafafa',
                border: '1px solid #f0f0f0',
                borderRadius: 8,
                textAlign: 'center',
              }}
            >
              <div
                id="redeem-qr-reader"
                style={{
                  width: 'min(100%, 320px)',
                  margin: '0 auto',
                }}
              />
              <div style={{ color: '#888', marginTop: 8 }}>将电子票二维码对准摄像头，识别后自动填入</div>
            </div>
          )}
          {scanError && (
            <Alert type="warning" showIcon title={scanError} style={{ marginBottom: 16 }} />
          )}

          <Form.Item
            name="checkDevice"
            label="核销设备（选填，默认 admin-console）"
          >
            <Input placeholder="如：闸机01 / 检票口A" maxLength={50} />
          </Form.Item>
          <Space wrap>
            <Button type="primary" htmlType="submit" loading={submitting} icon={<ScanOutlined />}>
              核销
            </Button>
            {!scanning ? (
              <Button icon={<CameraOutlined />} onClick={() => setScanning(true)}>
                摄像头扫码
              </Button>
            ) : (
              <Button danger onClick={() => setScanning(false)}>
                停止扫码
              </Button>
            )}
          </Space>
        </Form>
      </Card>
      {renderResult()}
    </div>
  )
}

export default Redeem
