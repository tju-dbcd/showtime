import { useEffect, useState } from 'react'
import {
  Table,
  Select,
  Button,
  Space,
  Tag,
  Modal,
  Descriptions,
  message,
} from 'antd'
import {
  getShowList,
  getShowSessions,
  updateSessionStatus,
  type ShowDto,
  type ShowSessionDto,
} from '../../../api/admin'

const sessionStatusMap: Record<string, { text: string; color: string }> = {
  UPCOMING: { text: '待上架', color: 'default' },
  PRESALE: { text: '预售中', color: 'processing' },
  ONSALE: { text: '售卖中', color: 'success' },
  SOLD_OUT: { text: '已售罄', color: 'warning' },
  ENDED: { text: '已结束', color: 'default' },
}

const Session = () => {
  const [shows, setShows] = useState<ShowDto[]>([])
  const [selectedShowId, setSelectedShowId] = useState<number | undefined>()
  const [sessions, setSessions] = useState<ShowSessionDto[]>([])
  const [loading, setLoading] = useState(false)
  const [detailVisible, setDetailVisible] = useState(false)
  const [currentSession, setCurrentSession] = useState<ShowSessionDto | null>(null)

  // 加载演出列表
  useEffect(() => {
    const loadShows = async () => {
      try {
        const res = await getShowList({ PageSize: 100 })
        if (res.data) {
          const result = (res.data as any).data || (res.data as any)
          setShows(result?.items || result || [])
        }
      } catch {
        message.error('加载演出列表失败')
      }
    }
    loadShows()
  }, [])

  // 选择演出后加载场次
  const loadSessions = async (showId: number) => {
    setLoading(true)
    try {
      const res = await getShowSessions(showId)
      if (res.data) {
        const result = (res.data as any).data || (res.data as any)
        setSessions(Array.isArray(result) ? result : (result?.items || []))
      }
    } catch {
      message.error('加载场次失败')
    } finally {
      setLoading(false)
    }
  }

  const handleShowChange = (value: number) => {
    setSelectedShowId(value)
    loadSessions(value)
  }

  const handleStatusChange = async (sessionId: number, status: string) => {
    try {
      const res = await updateSessionStatus(sessionId, { status })
      if (res.error) {
        message.error('操作失败')
        return
      }
      message.success('状态更新成功')
      if (selectedShowId) {
        loadSessions(selectedShowId)
      }
    } catch {
      message.error('操作失败')
    }
  }

  const handleViewDetail = (session: ShowSessionDto) => {
    setCurrentSession(session)
    setDetailVisible(true)
  }

  const columns = [
    {
      title: '场次ID',
      dataIndex: 'sessionId',
      key: 'sessionId',
      width: 100,
    },
    {
      title: '演出开始时间',
      dataIndex: 'startTime',
      key: 'startTime',
      width: 180,
      render: (time: string) => time ? new Date(time).toLocaleString('zh-CN') : '-',
    },
    {
      title: '演出结束时间',
      dataIndex: 'endTime',
      key: 'endTime',
      width: 180,
      render: (time: string) => time ? new Date(time).toLocaleString('zh-CN') : '-',
    },
    {
      title: '售票开始时间',
      dataIndex: 'saleStartTime',
      key: 'saleStartTime',
      width: 180,
      render: (time: string) => time ? new Date(time).toLocaleString('zh-CN') : '-',
    },
    {
      title: '座位图ID',
      dataIndex: 'seatMapId',
      key: 'seatMapId',
      width: 100,
    },
    {
      title: '状态',
      dataIndex: 'sessionStatus',
      key: 'sessionStatus',
      width: 100,
      render: (status: string) => {
        const s = sessionStatusMap[status]
        return s ? <Tag color={s.color}>{s.text}</Tag> : status
      },
    },
    {
      title: '操作',
      key: 'action',
      width: 200,
      render: (_: unknown, record: ShowSessionDto) => (
        <Space>
          <Button type="link" size="small" onClick={() => handleViewDetail(record)}>
            详情
          </Button>
          {(record.sessionStatus === 'UPCOMING' || record.sessionStatus === 'PRESALE') && (
            <Button
              type="link"
              size="small"
              onClick={() => handleStatusChange(Number(record.sessionId), 'ONSALE')}
            >
              上架
            </Button>
          )}
          {record.sessionStatus === 'ONSALE' && (
            <Button
              type="link"
              size="small"
              danger
              onClick={() => handleStatusChange(Number(record.sessionId), 'ENDED')}
            >
              结束
            </Button>
          )}
        </Space>
      ),
    },
  ]

  return (
    <div>
      <div style={{ marginBottom: 16 }}>
        <Space>
          <span>选择演出：</span>
          <Select
            placeholder="请选择演出"
            value={selectedShowId}
            onChange={handleShowChange}
            style={{ width: 300 }}
            showSearch
            optionFilterProp="children"
          >
            {shows.map(show => (
              <Select.Option key={show.showId} value={Number(show.showId)}>
                {show.showName}
              </Select.Option>
            ))}
          </Select>
        </Space>
      </div>

      <Table
        columns={columns}
        dataSource={sessions}
        rowKey="sessionId"
        loading={loading}
        pagination={false}
        locale={{ emptyText: selectedShowId ? '暂无场次数据' : '请先选择演出' }}
      />

      <Modal
        title="场次详情"
        open={detailVisible}
        onCancel={() => setDetailVisible(false)}
        footer={[
          <Button key="close" onClick={() => setDetailVisible(false)}>
            关闭
          </Button>,
        ]}
      >
        {currentSession && (
          <Descriptions column={1} bordered size="small">
            <Descriptions.Item label="场次ID">{currentSession.sessionId}</Descriptions.Item>
            <Descriptions.Item label="演出ID">{currentSession.showId}</Descriptions.Item>
            <Descriptions.Item label="开始时间">
              {currentSession.startTime ? new Date(currentSession.startTime).toLocaleString('zh-CN') : '-'}
            </Descriptions.Item>
            <Descriptions.Item label="结束时间">
              {currentSession.endTime ? new Date(currentSession.endTime).toLocaleString('zh-CN') : '-'}
            </Descriptions.Item>
            <Descriptions.Item label="售票开始时间">
              {currentSession.saleStartTime ? new Date(currentSession.saleStartTime).toLocaleString('zh-CN') : '-'}
            </Descriptions.Item>
            <Descriptions.Item label="座位图ID">{currentSession.seatMapId}</Descriptions.Item>
            <Descriptions.Item label="状态">
              {(() => {
                const s = sessionStatusMap[currentSession.sessionStatus]
                return s ? <Tag color={s.color}>{s.text}</Tag> : currentSession.sessionStatus
              })()}
            </Descriptions.Item>
          </Descriptions>
        )}
      </Modal>
    </div>
  )
}

export default Session
