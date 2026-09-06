import { useEffect, useState, useCallback, useRef } from 'react';
import {
  Select,
  Card,
  Button,
  InputNumber,
  Input,
  Space,
  Table,
  Tag,
  message,
  Modal,
  Form,
  Switch,
  Popconfirm,
  Empty,
  Radio,
} from 'antd';
import {
  PlusOutlined,
  EditOutlined,
  ReloadOutlined,
  SaveOutlined,
} from '@ant-design/icons';
import {
  getSeatMapList,
  getSeatSections,
  getSeats,
  createSeat,
  deleteSeat,
  batchUpdateSeats,
  updateSeat,
  type SeatMapResponse,
  type SeatSectionResponse,
  type SeatResponse,
  type SeatRequest,
} from '../../../api/admin';

const SEAT_TYPES = [
  { value: 'NORMAL', label: '普通座' },
  { value: 'COUPLE', label: '情侣座' },
  { value: 'ACCESSIBLE', label: '无障碍座' },
  { value: 'COMPANION', label: '陪同座' },
];

const SEAT_STATUSES = [
  { value: 'ENABLED', label: '启用' },
  { value: 'DISABLED', label: '禁用' },
  { value: 'MAINTENANCE', label: '维护中' },
];

const SeatMapEditor = () => {
  const [seatMaps, setSeatMaps] = useState<SeatMapResponse[]>([]);
  const [selectedMapId, setSelectedMapId] = useState<number | null>(null);
  const [sections, setSections] = useState<SeatSectionResponse[]>([]);
  const [selectedSectionId, setSelectedSectionId] = useState<number | null>(null);
  const [seats, setSeats] = useState<SeatResponse[]>([]);
  const [loadingSeats, setLoadingSeats] = useState(false);
  const [selectedSeatIds, setSelectedSeatIds] = useState<Set<number>>(new Set());

  // 图形视图相关
  const [viewMode, setViewMode] = useState<'table' | 'canvas'>('table');
  const [modifiedCoords, setModifiedCoords] = useState<Map<number, { xCoord: number; yCoord: number }>>(new Map());
  const [savingLayout, setSavingLayout] = useState(false);
  const canvasRef = useRef<HTMLDivElement>(null);
  const dragState = useRef<{ seatId: number; offsetX: number; offsetY: number } | null>(null);

  const [addVisible, setAddVisible] = useState(false);
  const [addLoading, setAddLoading] = useState(false);
  const [addForm] = Form.useForm();

  const [editVisible, setEditVisible] = useState(false);
  const [editLoading, setEditLoading] = useState(false);
  const [editForm] = Form.useForm();

  useEffect(() => {
    getSeatMapList({ PageSize: 100 }).then(res => {
      if (res.data?.data?.items) setSeatMaps(res.data.data.items);
    }).catch(() => message.error('加载座位图列表失败'));
  }, []);

  const handleMapChange = useCallback((mapId: number) => {
    setSelectedMapId(mapId);
    setSelectedSectionId(null);
    setSeats([]);
    setSelectedSeatIds(new Set());
    setModifiedCoords(new Map());
    getSeatSections(mapId, { PageSize: 100 }).then(res => {
      if (res.data?.data?.items) setSections(res.data.data.items);
    }).catch(() => message.error('加载票区列表失败'));
  }, []);

  const loadSeats = useCallback((sectionId: number) => {
    setLoadingSeats(true);
    getSeats(sectionId, { PageSize: 1000 }).then(res => {
      if (res.data?.data?.items) setSeats(res.data.data.items);
    }).catch(() => message.error('加载座位列表失败'))
      .finally(() => setLoadingSeats(false));
  }, []);

  const handleSectionChange = useCallback((sectionId: number) => {
    setSelectedSectionId(sectionId);
    setSelectedSeatIds(new Set());
    setModifiedCoords(new Map());
    if (sectionId) loadSeats(sectionId);
  }, [loadSeats]);

  const handleAdd = async () => {
    if (!selectedSectionId) return;
    try {
      const values = await addForm.validateFields();
      setAddLoading(true);
      const data: SeatRequest = {
        rowCode: values.rowCode,
        seatNo: values.seatNo,
        rowIndex: values.rowIndex,
        colIndex: values.colIndex,
        xCoord: values.xCoord || 0,
        yCoord: values.yCoord || 0,
        seatType: values.seatType || 'NORMAL',
        seatStatus: values.seatStatus || 'ENABLED',
        isAisleSide: values.isAisleSide || false,
        isSellable: values.isSellable !== false,
        remark: values.remark || null,
      };
      const res = await createSeat(selectedSectionId, data);
      if (res.error) {
        message.error('新增失败');
        return;
      }
      message.success('新增成功');
      setAddVisible(false);
      addForm.resetFields();
      loadSeats(selectedSectionId);
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) return;
      message.error('新增失败');
    } finally {
      setAddLoading(false);
    }
  };

  const handleBatchEdit = async () => {
    if (!selectedSectionId || selectedSeatIds.size === 0) return;
    try {
      const values = await editForm.validateFields();
      setEditLoading(true);
      const res = await batchUpdateSeats(selectedSectionId, {
        seatIds: Array.from(selectedSeatIds).map(Number),
        seatType: values.seatType ?? null,
        seatStatus: values.seatStatus ?? null,
        isAisleSide: values.isAisleSide === 'yes' ? true : values.isAisleSide === 'no' ? false : null,
        isSellable: values.isSellable === 'yes' ? true : values.isSellable === 'no' ? false : null,
      });
      if (res.error) {
        const errMsg = (res.error as { message?: string })?.message || '批量编辑失败';
        message.error(errMsg);
        return;
      }
      message.success(`成功更新 ${res.data?.data?.updatedCount || 0} 个座位`);
      setEditVisible(false);
      editForm.resetFields();
      setSelectedSeatIds(new Set());
      loadSeats(selectedSectionId);
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) return;
      const errMsg = (err as { message?: string })?.message || '批量编辑失败';
      message.error(errMsg);
    } finally {
      setEditLoading(false);
    }
  };

  const handleDelete = async (seatId: number) => {
    try {
      const res = await deleteSeat(seatId);
      if (res.error) {
        message.error('删除失败');
        return;
      }
      message.success('删除成功');
      if (selectedSectionId) loadSeats(selectedSectionId);
    } catch {
      message.error('删除失败');
    }
  };

  const toggleSelectAll = () => {
    if (selectedSeatIds.size === seats.length) {
      setSelectedSeatIds(new Set());
    } else {
      setSelectedSeatIds(new Set(seats.map(s => Number(s.seatId))));
    }
  };

  // 图形视图：获取座位当前坐标（优先用修改后的，否则用原始值）
  const getSeatCoord = (seat: SeatResponse) => {
    const modified = modifiedCoords.get(Number(seat.seatId));
    if (modified) return modified;
    return { xCoord: Number(seat.xCoord) || 0, yCoord: Number(seat.yCoord) || 0 };
  };

  // 图形视图：开始拖拽
  const handleSeatMouseDown = (e: React.MouseEvent, seatId: number) => {
    e.preventDefault();
    const seat = seats.find(s => Number(s.seatId) === seatId);
    if (!seat || !canvasRef.current) return;
    const coord = getSeatCoord(seat);
    const rect = canvasRef.current.getBoundingClientRect();
    dragState.current = {
      seatId,
      offsetX: e.clientX - rect.left - coord.xCoord,
      offsetY: e.clientY - rect.top - coord.yCoord,
    };
  };

  // 图形视图：拖拽中
  const handleCanvasMouseMove = useCallback((e: React.MouseEvent) => {
    if (!dragState.current || !canvasRef.current) return;
    const rect = canvasRef.current.getBoundingClientRect();
    const newX = Math.max(0, Math.round(e.clientX - rect.left - dragState.current.offsetX));
    const newY = Math.max(0, Math.round(e.clientY - rect.top - dragState.current.offsetY));
    const seatId = dragState.current.seatId;
    setModifiedCoords(prev => {
      const next = new Map(prev);
      next.set(seatId, { xCoord: newX, yCoord: newY });
      return next;
    });
  }, []);

  // 图形视图：结束拖拽
  const handleCanvasMouseUp = useCallback(() => {
    dragState.current = null;
  }, []);

  // 图形视图：保存布局（逐个更新座位坐标）
  const handleSaveLayout = async () => {
    if (modifiedCoords.size === 0) {
      message.info('没有修改需要保存');
      return;
    }
    setSavingLayout(true);
    try {
      let success = 0;
      let failed = 0;
      for (const [seatId, coord] of modifiedCoords) {
        const seat = seats.find(s => Number(s.seatId) === seatId);
        if (!seat) continue;
        const data: SeatRequest = {
          rowCode: seat.rowCode,
          seatNo: seat.seatNo,
          rowIndex: seat.rowIndex ?? 0,
          colIndex: seat.colIndex ?? 0,
          xCoord: coord.xCoord,
          yCoord: coord.yCoord,
          seatType: seat.seatType,
          seatStatus: seat.seatStatus,
          isAisleSide: seat.isAisleSide ?? false,
          isSellable: seat.isSellable ?? true,
          remark: seat.remark ?? null,
        };
        const res = await updateSeat(seatId, data);
        if (res.error) {
          failed++;
        } else {
          success++;
        }
      }
      if (failed > 0) {
        message.warning(`保存完成：成功 ${success} 个，失败 ${failed} 个`);
      } else {
        message.success(`成功保存 ${success} 个座位的布局`);
      }
      setModifiedCoords(new Map());
      if (selectedSectionId) loadSeats(selectedSectionId);
    } catch {
      message.error('保存布局失败');
    } finally {
      setSavingLayout(false);
    }
  };

  const columns = [
    { title: '行', dataIndex: 'rowCode', key: 'rowCode', width: 80 },
    { title: '座号', dataIndex: 'seatNo', key: 'seatNo', width: 80 },
    {
      title: '类型', dataIndex: 'seatType', key: 'seatType', width: 100,
      render: (type: string) => {
        const found = SEAT_TYPES.find(t => t.value === type);
        return <Tag color="blue">{found ? found.label : type}</Tag>;
      },
    },
    {
      title: '状态', dataIndex: 'seatStatus', key: 'seatStatus', width: 100,
      render: (status: string) => {
        const found = SEAT_STATUSES.find(s => s.value === status);
        const color = status === 'ENABLED' ? 'green' : status === 'DISABLED' ? 'red' : 'orange';
        return <Tag color={color}>{found ? found.label : status}</Tag>;
      },
    },
    {
      title: '过道侧', dataIndex: 'isAisleSide', key: 'isAisleSide', width: 80,
      render: (v: boolean) => v ? <Tag color="orange">是</Tag> : '否',
    },
    {
      title: '可售', dataIndex: 'isSellable', key: 'isSellable', width: 80,
      render: (v: boolean) => v ? <Tag color="green">是</Tag> : <Tag color="red">否</Tag>,
    },
    {
      title: '坐标', key: 'coord', width: 120,
      render: (_: unknown, record: SeatResponse) => `(${record.xCoord}, ${record.yCoord})`,
    },
    {
      title: '操作', key: 'action', width: 80,
      render: (_: unknown, record: SeatResponse) => (
        <Popconfirm title="确定删除该座位吗？" onConfirm={() => handleDelete(Number(record.seatId))} okText="确定" cancelText="取消">
          <Button type="link" size="small" danger>删除</Button>
        </Popconfirm>
      ),
    },
  ];

  return (
    <div>
      <Card size="small" style={{ marginBottom: 16 }}>
        <Space size="large">
          <div>
            <span style={{ marginRight: 8 }}>座位图：</span>
            <Select
              placeholder="请选择座位图"
              value={selectedMapId ?? undefined}
              onChange={handleMapChange}
              style={{ width: 280 }}
              showSearch
              optionFilterProp="children"
            >
              {seatMaps.map(map => (
                <Select.Option key={map.seatMapId} value={Number(map.seatMapId)}>
                  {map.mapName}（{map.venueName}）
                </Select.Option>
              ))}
            </Select>
          </div>
          <div>
            <span style={{ marginRight: 8 }}>票区：</span>
            <Select
              placeholder="请选择票区"
              value={selectedSectionId ?? undefined}
              onChange={handleSectionChange}
              style={{ width: 240 }}
              disabled={!selectedMapId}
              showSearch
              optionFilterProp="children"
            >
              {sections.map(sec => (
                <Select.Option key={sec.seatSectionId} value={Number(sec.seatSectionId)}>
                  {sec.sectionName}（{sec.sectionCode}）
                </Select.Option>
              ))}
            </Select>
          </div>
          <Button icon={<ReloadOutlined />} onClick={() => selectedSectionId && loadSeats(selectedSectionId)} disabled={!selectedSectionId}>
            刷新
          </Button>
        </Space>
      </Card>

      {selectedSectionId && (
        <Card size="small" style={{ marginBottom: 16 }}>
          <Space>
            <Button type="primary" icon={<PlusOutlined />} onClick={() => { addForm.resetFields(); setAddVisible(true); }}>
              新增座位
            </Button>
            <Button
              icon={<EditOutlined />}
              disabled={selectedSeatIds.size === 0}
              onClick={() => { editForm.resetFields(); setEditVisible(true); }}
            >
              批量编辑（{selectedSeatIds.size}）
            </Button>
            <Button onClick={toggleSelectAll}>
              {selectedSeatIds.size === seats.length && seats.length > 0 ? '取消全选' : '全选'}
            </Button>
            <span style={{ color: '#999' }}>共 {seats.length} 个座位，已选 {selectedSeatIds.size} 个</span>
            <div style={{ marginLeft: 'auto' }}>
              <Radio.Group value={viewMode} onChange={e => setViewMode(e.target.value)} size="small">
                <Radio.Button value="table">表格视图</Radio.Button>
                <Radio.Button value="canvas">图形视图</Radio.Button>
              </Radio.Group>
            </div>
          </Space>
        </Card>
      )}

      <Card size="small">
        {!selectedSectionId ? (
          <Empty description="请先选择座位图和票区" />
        ) : viewMode === 'table' ? (
          <Table
            columns={columns}
            dataSource={seats}
            rowKey="seatId"
            loading={loadingSeats}
            size="small"
            pagination={{ pageSize: 50, showSizeChanger: true, showTotal: t => `共 ${t} 个座位` }}
            rowSelection={{
              selectedRowKeys: Array.from(selectedSeatIds),
              onChange: keys => setSelectedSeatIds(new Set(keys.map(Number))),
            }}
          />
        ) : (
          <div>
            <Space style={{ marginBottom: 12 }}>
              <Button
                type="primary"
                icon={<SaveOutlined />}
                onClick={handleSaveLayout}
                loading={savingLayout}
                disabled={modifiedCoords.size === 0}
              >
                保存布局（{modifiedCoords.size} 项修改）
              </Button>
              <Button onClick={() => setModifiedCoords(new Map())} disabled={modifiedCoords.size === 0}>
                撤销修改
              </Button>
              <span style={{ color: '#999', fontSize: 12 }}>拖动座位调整位置，点击"保存布局"批量更新坐标</span>
            </Space>
            <div
              ref={canvasRef}
              onMouseMove={handleCanvasMouseMove}
              onMouseUp={handleCanvasMouseUp}
              onMouseLeave={handleCanvasMouseUp}
              style={{
                position: 'relative',
                width: '100%',
                height: 500,
                border: '1px solid #d9d9d9',
                borderRadius: 4,
                background: '#fafafa',
                overflow: 'auto',
                cursor: 'default',
              }}
            >
              {seats.map(seat => {
                const coord = getSeatCoord(seat);
                const isModified = modifiedCoords.has(Number(seat.seatId));
                const typeFound = SEAT_TYPES.find(t => t.value === seat.seatType);
                const bgColor = seat.seatStatus === 'DISABLED' ? '#ff4d4f'
                  : seat.seatStatus === 'MAINTENANCE' ? '#faad14'
                  : seat.seatType === 'COUPLE' ? '#722ed1'
                  : seat.seatType === 'ACCESSIBLE' ? '#13c2c2'
                  : seat.seatType === 'COMPANION' ? '#eb2f96'
                  : '#1677ff';
                return (
                  <div
                    key={seat.seatId}
                    onMouseDown={e => handleSeatMouseDown(e, Number(seat.seatId))}
                    title={`${seat.rowCode}排${seat.seatNo}座 (${typeFound?.label || seat.seatType})`}
                    style={{
                      position: 'absolute',
                      left: coord.xCoord,
                      top: coord.yCoord,
                      width: 28,
                      height: 28,
                      borderRadius: 4,
                      background: bgColor,
                      color: '#fff',
                      fontSize: 10,
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      cursor: 'move',
                      userSelect: 'none',
                      border: isModified ? '2px solid #faad14' : 'none',
                      boxShadow: isModified ? '0 0 4px #faad14' : 'none',
                    }}
                  >
                    {seat.seatNo}
                  </div>
                );
              })}
            </div>
          </div>
        )}
      </Card>

      {/* 新增座位弹窗 */}
      <Modal
        title="新增座位"
        open={addVisible}
        onCancel={() => setAddVisible(false)}
        onOk={handleAdd}
        confirmLoading={addLoading}
        okText="确定"
        cancelText="取消"
        width={520}
      >
        <Form form={addForm} layout="vertical" initialValues={{ seatType: 'NORMAL', seatStatus: 'ENABLED', isAisleSide: false, isSellable: true }}>
          <Space size="large" style={{ width: '100%' }}>
            <Form.Item label="排号" name="rowCode" rules={[{ required: true, message: '请输入排号' }]} style={{ width: 120 }}>
              <Input maxLength={5} />
            </Form.Item>
            <Form.Item label="座号" name="seatNo" rules={[{ required: true, message: '请输入座号' }]} style={{ width: 120 }}>
              <Input maxLength={10} />
            </Form.Item>
          </Space>
          <Space size="large" style={{ width: '100%' }}>
            <Form.Item label="行索引" name="rowIndex" rules={[{ required: true, message: '请输入行索引' }]} style={{ width: 120 }}>
              <InputNumber min={0} style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item label="列索引" name="colIndex" rules={[{ required: true, message: '请输入列索引' }]} style={{ width: 120 }}>
              <InputNumber min={0} style={{ width: '100%' }} />
            </Form.Item>
          </Space>
          <Space size="large" style={{ width: '100%' }}>
            <Form.Item label="X坐标" name="xCoord" style={{ width: 120 }}>
              <InputNumber style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item label="Y坐标" name="yCoord" style={{ width: 120 }}>
              <InputNumber style={{ width: '100%' }} />
            </Form.Item>
          </Space>
          <Space size="large" style={{ width: '100%' }}>
            <Form.Item label="座位类型" name="seatType" style={{ width: 160 }}>
              <Select options={SEAT_TYPES} />
            </Form.Item>
            <Form.Item label="座位状态" name="seatStatus" style={{ width: 160 }}>
              <Select options={SEAT_STATUSES} />
            </Form.Item>
          </Space>
          <Space size="large" style={{ width: '100%' }}>
            <Form.Item label="过道侧" name="isAisleSide" valuePropName="checked" style={{ width: 120 }}>
              <Switch checkedChildren="是" unCheckedChildren="否" />
            </Form.Item>
            <Form.Item label="可售" name="isSellable" valuePropName="checked" style={{ width: 120 }}>
              <Switch checkedChildren="是" unCheckedChildren="否" />
            </Form.Item>
          </Space>
          <Form.Item label="备注" name="remark">
            <Input maxLength={200} />
          </Form.Item>
        </Form>
      </Modal>

      {/* 批量编辑弹窗 */}
      <Modal
        title={`批量编辑座位（${selectedSeatIds.size} 个）`}
        open={editVisible}
        onCancel={() => setEditVisible(false)}
        onOk={handleBatchEdit}
        confirmLoading={editLoading}
        okText="应用"
        cancelText="取消"
        width={480}
      >
        <Form form={editForm} layout="vertical" initialValues={{ isAisleSide: 'unchanged', isSellable: 'unchanged' }}>
          <Form.Item label="座位类型（不选则不改）" name="seatType">
            <Select allowClear options={SEAT_TYPES} placeholder="选择座位类型" />
          </Form.Item>
          <Form.Item label="座位状态（不选则不改）" name="seatStatus">
            <Select allowClear options={SEAT_STATUSES} placeholder="选择座位状态" />
          </Form.Item>
          <Form.Item label="过道侧" name="isAisleSide">
            <Radio.Group>
              <Radio value="yes">是</Radio>
              <Radio value="no">否</Radio>
              <Radio value="unchanged">不修改</Radio>
            </Radio.Group>
          </Form.Item>
          <Form.Item label="可售" name="isSellable">
            <Radio.Group>
              <Radio value="yes">是</Radio>
              <Radio value="no">否</Radio>
              <Radio value="unchanged">不修改</Radio>
            </Radio.Group>
          </Form.Item>
          <div style={{ color: '#999', fontSize: 12 }}>未设置的字段将保持原值不变。</div>
        </Form>
      </Modal>
    </div>
  );
};

export default SeatMapEditor;
