/**
 * 座位图画布排版（纯计算，无 DOM/React 依赖，便于单测回归）。
 *
 * 背景：座位按管理端保存的 x/y 坐标绝对定位绘制，而“舞台”和“票区标注”
 * （A区/B区…）是前端叠加在坐标画布上的装饰层，管理端坐标并不会为它们
 * 预留空间。若直接按原始坐标绘制，会出现：
 *   1. 最前排座位压住顶部“舞台”；
 *   2. 票区标注被座位图标盖住（绘制顺序在座位下方，看不清 A/B/C）。
 *
 * 处理方式：
 *   1. 整体下移 dy，把全部座位推到“舞台 + 标注带”下方；
 *   2. 若相邻票区之间没有足够空隙容纳本区标注，自动在两个区块之间补一段空白；
 *   3. 每个票区标注固定放在“本区第一排上方”的空隙中，不与任何座位图标相交。
 */

export const SEAT_SIZE = 30;
/** 舞台图形顶边/高度，需与 SeatSelection.css 中 .seat-map-stage 保持一致 */
export const STAGE_TOP = 10;
export const STAGE_HEIGHT = 44;
export const STAGE_BOTTOM = STAGE_TOP + STAGE_HEIGHT;
/** 票区标注高度，需与 .seat-map-section-label 保持一致 */
export const SECTION_LABEL_HEIGHT = 20;
/** 舞台下沿与最上方标注之间的留白 */
export const STAGE_TO_LABEL_GAP = 10;
/** 标注下沿与本区第一排座位之间的留白 */
export const LABEL_TO_SEAT_GAP = 6;
/** 标注与上方相邻区块座位之间的额外留白 */
export const LABEL_BAND_GAP = 4;
/** 标注允许出现的最低 y（舞台下沿 + 留白） */
export const LABEL_MIN_TOP = STAGE_BOTTOM + STAGE_TO_LABEL_GAP;
/** 标注顶部到本区第一排座位顶部的高度（标注高度 + 下留白） */
export const LABEL_ABOVE = SECTION_LABEL_HEIGHT + LABEL_TO_SEAT_GAP;
/** 第一排座位允许出现的最低 y */
export const CONTENT_MIN_TOP = LABEL_MIN_TOP + LABEL_ABOVE;
/** 画布兜底尺寸与边缘留白 */
export const CANVAS_MIN_WIDTH = 640;
export const CANVAS_MIN_HEIGHT = 320;
export const CANVAS_PADDING = 60;
/** 票区边界框相对座位图标的外扩留白（上侧额外小，避免压到区名标注） */
export const BOUNDARY_PAD_X = 8;
export const BOUNDARY_PAD_TOP = 4;
export const BOUNDARY_PAD_BOTTOM = 8;

/** 参与排版的最小座位结构（只关心坐标） */
export interface CoordSeat {
  xCoord?: number | string | null;
  yCoord?: number | string | null;
}

/** 参与排版的最小票区结构 */
export interface CoordSection<S extends CoordSeat> {
  seatSectionId?: number | string | null;
  sectionName?: string | null;
  sectionColor?: string | null;
  seats?: S[] | null;
}

export interface LayoutSeat<S extends CoordSeat> {
  /** 原始座位对象，供调用方读取座位号/状态等 */
  seat: S;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface LayoutLabel {
  seatSectionId: number;
  text: string;
  color: string | null;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface LayoutBoundary {
  seatSectionId: number;
  /** 票区主题色（可能为空，由调用方兜底） */
  color: string | null;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface SeatMapLayout<S extends CoordSeat> {
  seats: LayoutSeat<S>[];
  labels: LayoutLabel[];
  /** 每个票区的彩色边界框（围住本区座位图标，不含上方的区名标注） */
  boundaries: LayoutBoundary[];
  canvasWidth: number;
  canvasHeight: number;
}

const toNum = (v: number | string | null | undefined): number => Number(v) || 0;

export function computeSeatMapLayout<S extends CoordSeat>(
  sections: CoordSection<S>[] | null | undefined,
  mapWidth?: number | string | null,
  mapHeight?: number | string | null,
): SeatMapLayout<S> | null {
  interface Group {
    sectionId: number;
    sectionName: string;
    sectionColor: string | null;
    rawLeft: number;
    rawTop: number;
  }

  const all: Array<{ seat: S; rawX: number; rawY: number; sectionId: number }> = [];
  const groups: Group[] = [];

  (sections ?? []).forEach((section) => {
    const items = (section?.seats ?? []).filter((s): s is S => !!s);
    if (items.length === 0) return;
    const sectionId = Number(section?.seatSectionId) || 0;
    let rawLeft = Infinity;
    let rawTop = Infinity;
    for (const seat of items) {
      const rawX = toNum(seat.xCoord);
      const rawY = toNum(seat.yCoord);
      all.push({ seat, rawX, rawY, sectionId });
      if (rawX < rawLeft) rawLeft = rawX;
      if (rawY < rawTop) rawTop = rawY;
    }
    groups.push({
      sectionId,
      sectionName: section?.sectionName ?? '',
      sectionColor: section?.sectionColor ?? null,
      rawLeft,
      rawTop,
    });
  });

  if (all.length === 0 || groups.length === 0) return null;

  const globalMinY = Math.min(...all.map((s) => s.rawY));
  // 整体下移：保证最前排座位（含最上方票区标注）都位于舞台下方。
  const dy = Math.max(0, CONTENT_MIN_TOP - globalMinY);

  // 后续为容纳票区标注而插入的“区块间距”。pos 为原始 y 坐标，rawY >= pos 的座位都受影响。
  const gaps: Array<{ pos: number; size: number }> = [];
  const shiftFor = (rawY: number): number => {
    let s = dy;
    for (const gap of gaps) {
      if (gap.pos <= rawY) s += gap.size;
    }
    return s;
  };

  // 自上而下处理票区，保证“上方区块”先完成标注排布。
  const orderedGroups = [...groups].sort(
    (a, b) => a.rawTop - b.rawTop || a.rawLeft - b.rawLeft,
  );

  const labels: LayoutLabel[] = [];
  for (const g of orderedGroups) {
    let effTop = g.rawTop + shiftFor(g.rawTop);

    // 标注水平位置贴近本区最左一排，宽度按文字粗略估算（含内边距/边框，宁多勿少）。
    const labelX = Math.max(4, g.rawLeft);
    const labelW = g.sectionName.length * 14 + 16;

    // 找会与本区标注相撞的“上方座位”：图标横跨标注水平范围、且完全位于本区第一排之上。
    let maxObstacleBottom = -Infinity;
    for (const s of all) {
      const iconTop = s.rawY + shiftFor(s.rawY);
      if (iconTop >= effTop - 1) continue; // 未完全位于本区上方（含本区自身 / 区块相互压叠）
      const xOverlap = s.rawX < labelX + labelW && s.rawX + SEAT_SIZE > labelX;
      if (!xOverlap) continue;
      const iconBottom = iconTop + SEAT_SIZE;
      if (iconBottom > maxObstacleBottom) maxObstacleBottom = iconBottom;
    }

    if (maxObstacleBottom > -Infinity && effTop - maxObstacleBottom < LABEL_ABOVE + LABEL_BAND_GAP) {
      const need = LABEL_ABOVE + LABEL_BAND_GAP - (effTop - maxObstacleBottom);
      gaps.push({ pos: g.rawTop, size: need });
      effTop += need;
    }

    const labelY = Math.max(LABEL_MIN_TOP, effTop - LABEL_ABOVE);
    labels.push({
      seatSectionId: g.sectionId,
      text: g.sectionName,
      color: g.sectionColor,
      x: labelX,
      y: labelY,
      w: labelW,
      h: SECTION_LABEL_HEIGHT,
    });
  }

  const seats: LayoutSeat<S>[] = all.map((s) => ({
    seat: s.seat,
    x: s.rawX,
    y: s.rawY + shiftFor(s.rawY),
    w: SEAT_SIZE,
    h: SEAT_SIZE,
  }));

  // 票区边界框：按最终有效坐标围住本区全部座位图标。
  const bySection = new Map<number, Array<{ rawX: number; rawY: number }>>();
  for (const s of all) {
    const arr = bySection.get(s.sectionId);
    if (arr) {
      arr.push(s);
    } else {
      bySection.set(s.sectionId, [s]);
    }
  }
  const boundaries: LayoutBoundary[] = [];
  for (const g of groups) {
    const items = bySection.get(g.sectionId);
    if (!items || items.length === 0) continue;
    let minX = Infinity;
    let maxX = -Infinity;
    let minYEff = Infinity;
    let maxYEff = -Infinity;
    for (const it of items) {
      const yEff = it.rawY + shiftFor(it.rawY);
      if (it.rawX < minX) minX = it.rawX;
      if (it.rawX + SEAT_SIZE > maxX) maxX = it.rawX + SEAT_SIZE;
      if (yEff < minYEff) minYEff = yEff;
      if (yEff + SEAT_SIZE > maxYEff) maxYEff = yEff + SEAT_SIZE;
    }
    boundaries.push({
      seatSectionId: g.sectionId,
      color: g.sectionColor,
      x: minX - BOUNDARY_PAD_X,
      y: minYEff - BOUNDARY_PAD_TOP,
      w: maxX - minX + BOUNDARY_PAD_X * 2,
      h: maxYEff - minYEff + BOUNDARY_PAD_TOP + BOUNDARY_PAD_BOTTOM,
    });
  }

  const maxRight = Math.max(...seats.map((s) => s.x + s.w), 0);
  const maxBottom = Math.max(...seats.map((s) => s.y + s.h), 0);
  const canvasWidth = Math.max(toNum(mapWidth), Math.ceil(maxRight + CANVAS_PADDING), CANVAS_MIN_WIDTH);
  const canvasHeight = Math.max(toNum(mapHeight), Math.ceil(maxBottom + CANVAS_PADDING), CANVAS_MIN_HEIGHT);

  return { seats, labels, boundaries, canvasWidth, canvasHeight };
}
