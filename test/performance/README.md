# 性能回归验证

## 生成 JTL

先用 JMeter 跑同一份压测脚本、同一组参数得到基线，代码/配置变更后再跑一次：

```bash
cd test/jmeter
# 基线（保存为 baseline.jtl）
jmeter -n -t showtime_load_test.jmx -JTHREADS=50 -l baseline.jtl

# 变更后（保存为 current.jtl）
jmeter -n -t showtime_load_test.jmx -JTHREADS=50 -l current.jtl
```

## 对比

```bash
cd test/performance
python3 compare_results.py ../jmeter/baseline.jtl ../jmeter/current.jtl
```

## 判定阈值

- 成功率 ≥ 95% 且不下降；
- 平均 / P50 / P90 / P99 / 最大响应时延相对基线涨幅 < 20%。

> 两次对比必须使用相同场景、线程数与循环数，否则结果无意义。
