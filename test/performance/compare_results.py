#!/usr/bin/env python3
"""JMeter JTL 性能回归对比脚本。

用法:
    python3 compare_results.py <baseline.jtl> <current.jtl>

说明:
    - JTL 为 `jmeter -n -l results.jtl` 生成的 CSV（含 timeStamp 表头），
      字段含逗号/引号时按 CSV 规则解析；
    - 两次对比请使用相同场景、线程数与循环数，仅比较同一链路时延才有意义；
    - 判定阈值: 成功率 >= 95% 且不下降；平均/P50/P90/P99/最大时延
      相对基线涨幅 < 20% 判 PASS，否则 FAIL。
"""
import csv
import os
import sys
from datetime import datetime

SUCCESS_RATE_MIN = 95.0
LATENCY_REGRESSION_PERCENT = 20.0


def parse_jtl(file_path: str) -> list[dict]:
    """解析 JMeter JTL（CSV），返回样本列表。"""
    results: list[dict] = []
    with open(file_path, "r", encoding="utf-8", errors="replace") as f:
        reader = csv.reader(f)
        for row in reader:
            if not row or row[0].strip() == "timeStamp":
                continue
            if len(row) < 8:
                continue
            try:
                results.append(
                    {
                        "elapsed": int(row[1]),
                        "responseCode": row[3],
                        "success": row[7].strip().lower() == "true",
                    }
                )
            except ValueError:
                continue
    return results


def calculate_metrics(results: list[dict]) -> dict:
    """计算成功率与分位时延等指标。"""
    total = len(results)
    success_count = sum(1 for r in results if r["success"])
    fail_count = total - success_count
    elapsed = sorted(r["elapsed"] for r in results if r["success"])

    if not elapsed:
        return {
            "total": total,
            "success_count": success_count,
            "fail_count": fail_count,
            "avg": 0.0,
            "p50": 0,
            "p90": 0,
            "p99": 0,
            "max": 0,
            "min": 0,
            "success_rate": 0.0,
        }

    def percentile(p: float) -> int:
        index = min(len(elapsed) - 1, int(len(elapsed) * p))
        return elapsed[index]

    return {
        "total": total,
        "success_count": success_count,
        "fail_count": fail_count,
        "avg": sum(elapsed) / len(elapsed),
        "p50": percentile(0.50),
        "p90": percentile(0.90),
        "p99": percentile(0.99),
        "max": max(elapsed),
        "min": min(elapsed),
        "success_rate": success_count / total * 100 if total > 0 else 0.0,
    }


def main() -> None:
    if len(sys.argv) != 3:
        print("Usage: python compare_results.py <baseline.jtl> <current.jtl>")
        sys.exit(1)

    baseline_file, current_file = sys.argv[1], sys.argv[2]
    for path in (baseline_file, current_file):
        if not os.path.exists(path):
            print(f"❌ 文件不存在: {path}")
            sys.exit(1)

    baseline = calculate_metrics(parse_jtl(baseline_file))
    current = calculate_metrics(parse_jtl(current_file))

    print("=" * 72)
    print("性能回归验证报告")
    print("=" * 72)
    print(f"基线文件: {baseline_file}")
    print(f"当前文件: {current_file}")
    print(f"时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print()

    print(f"{'指标':<18}{'基线':>12}{'当前':>12}{'变化':>12}  状态")
    print("-" * 72)

    for key, label, unit in [
        ("success_rate", "成功率", "%"),
        ("avg", "平均响应", "ms"),
        ("p50", "P50", "ms"),
        ("p90", "P90", "ms"),
        ("p99", "P99", "ms"),
        ("max", "最大响应", "ms"),
    ]:
        base_value = baseline[key]
        current_value = current[key]

        if key == "success_rate":
            change = current_value - base_value
            passed = current_value >= SUCCESS_RATE_MIN and change >= 0
            print(f"{label:<18}{base_value:>10.2f}{current_value:>10.2f}{change:>+10.2f}  "
                  f"{'✅ PASS' if passed else '❌ FAIL'} ({unit})")
        elif base_value > 0:
            change = (current_value - base_value) / base_value * 100
            passed = change < LATENCY_REGRESSION_PERCENT
            print(f"{label:<18}{base_value:>10.1f}{current_value:>10.1f}{change:>+9.1f}%  "
                  f"{'✅ PASS' if passed else '❌ FAIL'} ({unit})")
        else:
            print(f"{label:<18}{base_value:>10.1f}{current_value:>10.1f}{'N/A':>12}  N/A")

    print("-" * 72)
    print(f"总请求数: 基线={baseline['total']}, 当前={current['total']}")
    print(f"成功数:   基线={baseline['success_count']}, 当前={current['success_count']}")
    print(f"失败数:   基线={baseline['fail_count']}, 当前={current['fail_count']}")

    if current["success_rate"] < SUCCESS_RATE_MIN:
        print(f"\n❌ 警告: 成功率低于 {SUCCESS_RATE_MIN:g}%，需要检查!")
        sys.exit(1)
    print("\n✅ 成功率达标")


if __name__ == "__main__":
    main()
