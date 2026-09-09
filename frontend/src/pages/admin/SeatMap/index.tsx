import { useEffect, useState, useCallback, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
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
  AppstoreOutlined,
} from '@ant-design/icons';
import {
  getSeatMapList,
  createSeatMap,
  createSeatSection,
  getSeatSections,
  getSeats,
  createSeat,
  deleteSeat,
  batchUpdateSeats,
  updateSeat,
  getSeatRuleList,
  getSeatRuleScopes,
  getVenues,
  type SeatMapResponse,
  type SeatSectionResponse,
  type SeatResponse,
  type SeatRequest,
  type SeatRuleScope,
  type VenueResponse,
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
  const navigate = useNavigate();
  const [seatMaps, setSeatMaps] = useState<SeatMapResponse[]>([]);
  const [selectedMapId, setSelectedMapId] = useState<number | null>(null);
  const [sections, setSections] = useState<SeatSectionResponse[]>([]);
  const [selectedSectionId, setSelectedSectionId] = useState<number | null>(null);
  const [seats, setSeats] = useState<SeatResponse[]>([]);
  const [loadingSeats, setLoadingSeats] = useState(false);
  const [selectedSeatIds, setSelectedSeatIds] = useState<Set<number>>(new Set());
  const [appliedRuleNames, setAppliedRuleNames] = useState<string[]>([]);

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

  // 新建座位图 / 新建票区
  const [venues, setVenues] = useState<VenueResponse[]>([]);
  const [mapModalVisible, setMapModalVisible] = useState(false);
  const [mapModalLoading, setMapModalLoading] = useState(false);
  const [mapForm] = Form.useForm();
  const [sectionModalVisible, setSectionModalVisible] = useState(false);
  const [sectionModalLoading, setSectionModalLoading] = useState(false);
  const [sectionForm] = Form.useForm();

  useEffect(() => {
    getVenues().then(res => {
      if (res.data?.data) setVenues(res.data.data);
    }).catch(() => message.error('加载场馆列表失败'));
  }, []);

  const loadSeatMaps = useCallback(async () => {
    try {
      const res = await getSeatMapList({ PageSize: 100 });
      if (res.data?.data?.items) setSeatMaps(res.data.data.items);
    } catch {
      message.error('加载座位图列表失败');
    }
  }, []);

  useEffect(() => {
    loadSeatMaps();
  }, [loadSeatMaps]);

  const loadAppliedRules = useCallback(async (mapId: number | null, sectionId: number | null) => {
    if (!mapId) {
      setAppliedRuleNames([]);
      return;
    }
    try {
      const rulesRes = await getSeatRuleList({ RuleStatus: 'ENABLED', PageSize: 100 });
      const rules = rulesRes.data?.data?.items || [];
      const names: string[] = [];
      for (const rule of rules) {
        const scopesRes = await getSeatRuleScopes(Number(rule.seatRuleId));
        const scopes: SeatRuleScope[] = scopesRes.data?.data || [];
        const hit = scopes.some(scope =>
          (scope.scopeType === 'MAP' && Number(scope.seatMapId) === mapId) ||
          (scope.scopeType === 'SECTION' && sectionId != null && Number(scope.seatSectionId) === sectionId),
        );
        if (hit) names.push(rule.ruleName);
      }
      setAppliedRuleNames(names);
    } catch {
      setAppliedRuleNames([]);
    }
  }, []);

  const handleMapChange = useCallback((mapId: number) => {
    setSelectedMapId(mapId);
    setSelectedSectionId(null);
    setSeats([]);
    setSelectedSeatIds(new Set());
    setModifiedCoords(new Map());
    setAppliedRuleNames([]);
    getSeatSections(mapId, { PageSize: 100 }).then(res => {
      if (res.data?.data?.items) setSections(res.data.data.items);
    }).catch(() => message.error('加载票区列表失败'));
    loadAppliedRules(mapId, null);
  }, [loadAppliedRules]);

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
    loadAppliedRules(selectedMapId, sectionId || null);
  }, [loadSeats, loadAppliedRules, selectedMapId]);

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

  const handleCreateMap = async () => {
    try {
      const values = await mapForm.validateFields();
      setMapModalLoading(true);
      const res = await createSeatMap({
        venueId: values.venueId,
        mapCode: values.mapCode.trim(),
        mapName: values.mapName.trim(),
        mapVersion: values.mapVersion.trim() || 'V1',
        isDefault: values.isDefault || false,
        mapWidth: values.mapWidth ?? null,
        mapHeight: values.mapHeight ?? null,
        mapStatus: values.mapStatus || 'DRAFT',
        remark: values.remark || null,
      });
      const mapId = Number(res.data?.data?.seatMapId);
      if (!mapId) {
        message.error('新建座位图失败');
        return;
      }
      message.success('座位图创建成功');
      setMapModalVisible(false);
      mapForm.resetFields();
      await loadSeatMaps();
      handleMapChange(mapId);
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) return;
      const errMsg = (err as { message?: string })?.message || '新建座位图失败';
      message.error(errMsg);
    } finally {
      setMapModalLoading(false);
    }
  };

  const handleCreateSection = async () => {
    if (!selectedMapId) return;
    try {
      const values = await sectionForm.validateFields();
      setSectionModalLoading(true);
      const res = await createSeatSection(selectedMapId, {
        sectionCode: values.sectionCode.trim(),
        sectionName: values.sectionName.trim(),
        sectionType: values.sectionType || 'NORMAL',
        sectionColor: values.sectionColor || null,
        floorNo: values.floorNo || null,
        isSellable: values.isSellable !== false,
        displayOrder: values.displayOrder || 0,
        remark: values.remark || null,
      });
      const sectionId = Number(res.data?.data?.seatSectionId);
      if (!sectionId) {
        message.error('新建票区失败');
        return;
      }
      message.success('票区创建成功');
      setSectionModalVisible(false);
      sectionForm.resetFields();
      if (selectedMapId) {
        getSeatSections(selectedMapId, { PageSize: 100 }).then(r => {
          if (r.data?.data?.items) setSections(r.data.data.items);
        });
      }
    } catch (err) {
      if (err && typeof err === 'object' && 'errorFields' in err) return;
      const errMsg = (err as { message?: string })?.message || '新建票区失败';
      message.error(errMsg);
    } finally {
      setSectionModalLoading(false);
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
          <Button type="primary" icon={<PlusOutlined />} onClick={() => { mapForm.resetFields(); setMapModalVisible(true); }}>
            新建座位图
          </Button>
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
          <Button icon={<PlusOutlined />} disabled={!selectedMapId} onClick={() => { sectionForm.resetFields(); setSectionModalVisible(true); }}>
            新建票区
          </Button>
          <Button icon={<AppstoreOutlined />} onClick={() => navigate('/admin/seat-rule')}>
            座位规则
          </Button>
        </Space>
      </Card>

      {selectedSectionId && (
        <Card size="small" style={{ marginBottom: 16 }}>
          <Space>
            <span style={{ color: '#666' }}>命中规则：</span>
            {appliedRuleNames.length > 0 ? (
              appliedRuleNames.map(name => <Tag key={name} color="blue">{name}</Tag>)
            ) : (
              <span style={{ color: '#999' }}>无（该座位图/票区未绑定座位规则）</span>
            )}
            <Button type="link" size="small" icon={<AppstoreOutlined />} onClick={() => navigate('/admin/seat-rule')}>
              管理座位规则
            </Button>
          </Space>
        </Card>
      )}

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

      {/* 新建座位图弹窗 */}
      <Modal
        title="新建座位图"
        open={mapModalVisible}
        onCancel={() => setMapModalVisible(false)}
        onOk={handleCreateMap}
        confirmLoading={mapModalLoading}
        okText="确定"
        cancelText="取消"
        width={560}
      >
        <Form form={mapForm} layout="vertical" style={{ marginTop: 8 }} initialValues={{ mapVersion: 'V1', mapStatus: 'DRAFT', isDefault: false }}>
          <Form.Item label="场馆" name="venueId" rules={[{ required: true, message: '请选择场馆' }]}>
            <Select placeholder="请选择场馆" showSearch optionFilterProp="children">
              {venues.map(venue => (
                <Select.Option key={venue.venueId} value={Number(venue.venueId)}>
                  {venue.venueName}
                </Select.Option>
              ))}
            </Select>
          </Form.Item>
          <Space size="large" style={{ width: '100%' }}>
            <Form.Item label="座位图名称" name="mapName" rules={[{ required: true, message: '请输入座位图名称' }]} style={{ width: 220 }}>
              <Input maxLength={100} />
            </Form.Item>
            <Form.Item label="座位图编码" name="mapCode" rules={[{ required: true, message: '请输入座位图编码' }]} style={{ width: 220 }}>
              <Input maxLength={50} placeholder="如 MAP_A" />
            </Form.Item>
          </Space>
          <Space size="large" style={{ width: '100%' }}>
            <Form.Item label="版本" name="mapVersion" style={{ width: 140 }}>
              <Input maxLength={20} />
            </Form.Item>
            <Form.Item label="地图宽度" name="mapWidth" style={{ width: 160 }}>
              <InputNumber min={0} precision={0} style={{ width: '100%' }} placeholder="如 1000" />
            </Form.Item>
            <Form.Item label="地图高度" name="mapHeight" style={{ width: 160 }}>
              <InputNumber min={0} precision={0} style={{ width: '100%' }} placeholder="如 700" />
            </Form.Item>
          </Space>
          <Space size="large" style={{ width: '100%' }}>
            <Form.Item label="状态" name="mapStatus" style={{ width: 160 }}>
              <Select options={[
                { value: 'DRAFT', label: '草稿' },
                { value: 'ENABLED', label: '启用' },
                { value: 'DISABLED', label: '停用' },
              ]} />
            </Form.Item>
            <Form.Item label="设为默认" name="isDefault" valuePropName="checked" style={{ width: 160, marginTop: 6 }}>
              <Switch checkedChildren="是" unCheckedChildren="否" />
            </Form.Item>
          </Space>
          <Form.Item label="备注" name="remark">
            <Input maxLength={255} />
          </Form.Item>
        </Form>
      </Modal>

      {/* 新建票区弹窗 */}
      <Modal
        title="新建票区"
        open={sectionModalVisible}
        onCancel={() => setSectionModalVisible(false)}
        onOk={handleCreateSection}
        confirmLoading={sectionModalLoading}
        okText="确定"
        cancelText="取消"
        width={520}
      >
        <Form form={sectionForm} layout="vertical" style={{ marginTop: 8 }} initialValues={{ sectionType: 'NORMAL', sectionColor: '#1677ff', isSellable: true, displayOrder: 0 }}>
          <Space size="large" style={{ width: '100%' }}>
            <Form.Item label="票区名称" name="sectionName" rules={[{ required: true, message: '请输入票区名称' }]} style={{ width: 200 }}>
              <Input maxLength={50} placeholder="如 A区" />
            </Form.Item>
            <Form.Item label="票区编码" name="sectionCode" rules={[{ required: true, message: '请输入票区编码' }]} style={{ width: 200 }}>
              <Input maxLength={50} placeholder="如 SEC_A" />
            </Form.Item>
          </Space>
          <Space size="large" style={{ width: '100%' }}>
            <Form.Item label="票区类型" name="sectionType" style={{ width: 180 }}>
              <Select options={[
                { value: 'NORMAL', label: '普通区' },
                { value: 'VIP', label: 'VIP区' },
                { value: 'ACCESSIBLE', label: '无障碍区' },
                { value: 'STANDING', label: '站票区' },
              ]} />
            </Form.Item>
            <Form.Item label="显示颜色" name="sectionColor" style={{ width: 180 }}>
              <Select showSearch allowClear>
                {[
                  { value: '#1677ff', label: '蓝色 #1677ff' },
                  { value: '#3498DB', label: '天蓝 #3498DB' },
                  { value: '#2ECC71', label: '绿色 #2ECC71' },
                  { value: '#F39C12', label: '橙色 #F39C12' },
                  { value: '#9B59B6', label: '紫色 #9B59B6' },
                  { value: '#E74C3C', label: '红色 #E74C3C' },
                  { value: '#1ABC9C', label: '青色 #1ABC9C' },
                ].map(c => (
                  <Select.Option key={c.value} value={c.value}>{c.label}</Select.Option>
                ))}
              </Select>
            </Form.Item>
          </Space>
          <Space size="large" style={{ width: '100%' }}>
            <Form.Item label="楼层" name="floorNo" style={{ width: 180 }}>
              <Input maxLength={20} placeholder="如 1F" />
            </Form.Item>
            <Form.Item label="排序" name="displayOrder" style={{ width: 160 }}>
              <InputNumber min={0} precision={0} style={{ width: '100%' }} />
            </Form.Item>
            <Form.Item label="可售" name="isSellable" valuePropName="checked" style={{ width: 120, marginTop: 6 }}>
              <Switch checkedChildren="是" unCheckedChildren="否" />
            </Form.Item>
          </Space>
          <Form.Item label="备注" name="remark">
            <Input maxLength={255} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default SeatMapEditor;
