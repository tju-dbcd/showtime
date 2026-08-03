// 生成模拟座位数据：rows 行，cols 列
export const generateMockSeats = (rows: number = 8, cols: number = 12): string[][] => {
  const seatMap: string[][] = [];
  for (let r = 0; r < rows; r++) {
    const row: string[] = [];
    for (let c = 0; c < cols; c++) {
      const rand = Math.random();
      // 70% 可用，20% 已售，10% 预留（不可选）
      if (rand < 0.7) row.push('available');
      else if (rand < 0.9) row.push('sold');
      else row.push('unavailable');
    }
    seatMap.push(row);
  }
  return seatMap;
};
