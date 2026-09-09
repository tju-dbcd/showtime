/**
 * 纯 Node 冒烟回归：验证 seatMapLayout 排版后：
 *  1) 没有座位与“舞台”矩形相交；
 *  2) 没有票区标注与座位图标相交；
 *  3) 相邻且压得很近的票区会被自动拉开，保证标注可见。
 *
 * 运行：node --experimental-strip-types scripts/seatMapLayout.check.ts
 */
import {
  computeSeatMapLayout,
  STAGE_TOP,
  STAGE_HEIGHT,
  type CoordSection,
  type CoordSeat,
} from '../src/pages/SeatSelection/seatMapLayout.ts';

const round = (n: number) => Math.round(n);

function stageRect(canvasWidth: number) {
  const width = Math.max(canvasWidth * 0.4, 180);
  const left = (canvasWidth - width) / 2;
  return { left, top: STAGE_TOP, right: left + width, bottom: STAGE_TOP + STAGE_HEIGHT };
}

function intersect(
  a: { l: number; t: number; r: number; b: number },
  b: { l: number; t: number; r: number; b: number },
) {
  return a.l < b.r && a.r > b.l && a.t < b.b && a.b > b.t;
}

interface MarkedSeat extends CoordSeat {
  _sectionId: number;
}

function makeSection(
  id: number,
  name: string,
  x0: number,
  y0: number,
  rows: number,
  cols: number,
  pitch = 40,
): CoordSection<MarkedSeat> {
  const seats: MarkedSeat[] = [];
  for (let row = 0; row < rows; row++) {
    for (let col = 0; col < cols; col++) {
      seats.push({ xCoord: x0 + col * pitch, yCoord: y0 + row * pitch, _sectionId: id });
    }
  }
  return { seatSectionId: id, sectionName: name, sectionColor: '#333', seats };
}

let failures = 0;
function check(name: string, cond: boolean, detail = '') {
  if (!cond) {
    failures++;
    console.error(`FAIL ${name}${detail ? ` — ${detail}` : ''}`);
  } else {
    console.log(`ok   ${name}`);
  }
}

function verifyScenario(label: string, sections: CoordSection<MarkedSeat>[], mapWidth: number, mapHeight: number) {
  const layout = computeSeatMapLayout(sections, mapWidth, mapHeight);
  if (!layout) throw new Error(`${label}: 排版结果为空`);
  const stage = stageRect(layout.canvasWidth);
  let seatStageHits = 0;
  for (const s of layout.seats) {
    if (intersect({ l: s.x, t: s.y, r: s.x + s.w, b: s.y + s.h }, stage)) seatStageHits++;
  }
  check(`${label}: 座位不与舞台相交`, seatStageHits === 0, `hits=${seatStageHits}`);

  let labelSeatHits = 0;
  let worst = '';
  for (const lb of layout.labels) {
    const labelRect = { l: lb.x, t: lb.y, r: lb.x + lb.w, b: lb.y + lb.h };
    for (const s of layout.seats) {
      const rect = { l: s.x, t: s.y, r: s.x + s.w, b: s.y + s.h };
      if (intersect(labelRect, rect)) {
        labelSeatHits++;
        worst = `${lb.text}@(${lb.x},${lb.y}) vs seat@(${s.x},${s.y})`;
      }
    }
  }
  check(`${label}: 票区标注不与座位相交`, labelSeatHits === 0, `hits=${labelSeatHits} ${worst}`);

  const minSeatTop = Math.min(...layout.seats.map((s) => s.y));
  check(`${label}: 最前排座位在舞台之下`, minSeatTop >= STAGE_TOP + STAGE_HEIGHT + 10, `top=${minSeatTop}`);

  // 票区边界框：完整包含本区座位，且不与其它票区座位/边界框相交。
  check(`${label}: 边界框数量正确`, layout.boundaries.length === sections.length, `got=${layout.boundaries.length}`);
  for (const b of layout.boundaries) {
    const own = layout.seats.filter((s) => (s.seat as MarkedSeat)._sectionId === b.seatSectionId);
    const others = layout.seats.filter((s) => (s.seat as MarkedSeat)._sectionId !== b.seatSectionId);
    const box = { l: b.x, t: b.y, r: b.x + b.w, b: b.y + b.h };
    const allInside = own.every(
      (s) => s.x >= b.x && s.y >= b.y && s.x + s.w <= b.x + b.w && s.y + s.h <= b.y + b.h,
    );
    check(
      `${label}: 边界框#${b.seatSectionId} 包含本区全部座位`,
      own.length > 0 && allInside,
      `own=${own.length}`,
    );
    const outsideHits = others.filter((s) =>
      intersect(box, { l: s.x, t: s.y, r: s.x + s.w, b: s.y + s.h }),
    ).length;
    check(`${label}: 边界框#${b.seatSectionId} 不压其它票区座位`, outsideHits === 0, `hits=${outsideHits}`);
  }
  for (let i = 0; i < layout.boundaries.length; i++) {
    for (let j = i + 1; j < layout.boundaries.length; j++) {
      const a = layout.boundaries[i];
      const c = layout.boundaries[j];
      check(
        `${label}: 边界框#${a.seatSectionId}/#${c.seatSectionId} 互不重叠`,
        !intersect(
          { l: a.x, t: a.y, r: a.x + a.w, b: a.y + a.h },
          { l: c.x, t: c.y, r: c.x + c.w, b: c.y + c.h },
        ),
      );
    }
  }
  return layout;
}

// 场景 1：单票区、第一排画在 y=50（未修复时会压住舞台、盖住标注）
verifyScenario('单票区紧贴顶部', [makeSection(1001, 'A区', 50, 50, 5, 8)], 1000, 700);

// 场景 2：普通剧院上下堆叠（A→B→C，区块间几乎零空隙，需要自动补白）
const s2 = verifyScenario(
  '上下堆叠 A/B/C',
  [
    makeSection(1001, 'A区', 60, 50, 6, 8),
    makeSection(1002, 'B区', 60, 50 + 6 * 40 - 4, 6, 8),
    makeSection(1003, 'C区', 60, 50 + 12 * 40 - 8, 6, 8),
  ],
  1000,
  900,
);

// 场景 3：左右并排（同一起始排，互不遮挡，不应被拉开）
const s3 = verifyScenario(
  '左右并排 A/B',
  [
    makeSection(1001, 'A区', 60, 50, 6, 8),
    makeSection(1002, 'B区', 430, 50, 6, 8),
  ],
  1000,
  700,
);

// 场景 4：混合（前区 A，中区 B/C 并排，后区 D）
verifyScenario(
  '混合布局 A / B+C / D',
  [
    makeSection(1001, 'A区', 60, 50, 4, 6),
    makeSection(1002, 'B区', 60, 50 + 4 * 40 - 2, 4, 6),
    makeSection(1003, 'C区', 340, 50 + 4 * 40 - 2, 4, 6),
    makeSection(1004, 'D区', 60, 50 + 8 * 40 - 6, 4, 12),
  ],
  1000,
  900,
);

console.log('场景2 标注位置：', s2.labels.map((l) => `${l.text} y=${round(l.y)}..${round(l.y + l.h)}`).join('  '));
console.log('场景2 边界框：', s2.boundaries.map((b) => `#${b.seatSectionId} (${round(b.x)},${round(b.y)}) ${b.w}x${b.h}`).join('  '));
console.log('场景3 标注位置：', s3.labels.map((l) => `${l.text} y=${round(l.y)}..${round(l.y + l.h)}`).join('  '));

if (failures > 0) {
  console.error(`\n${failures} 项检查失败`);
  process.exit(1);
}
console.log('\n全部检查通过');
