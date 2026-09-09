#!/usr/bin/env python3
"""批量创建 JMeter 压测账号（与 test_users.csv 账号一一对应）。

后端 register 接口限流 3 次/分钟/IP，本脚本在每次请求后等待 --interval 秒
（默认 21 秒，保证稳定低于限流阈值），遇到 429 时按 Retry-After 等待后重试。
因此创建 100 个账号约需 35 分钟；本地冒烟可先用 --count 10 建少量账号。

用法:
    python3 seed_users.py [--count 100] [--base-url http://localhost:5146]

账号规则: e2e_test_001 ~ e2e_test_{count:03d} / Test123456
"""
import argparse
import sys
import time

import requests


def create_users(count: int, base_url: str, interval: float) -> None:
    created = existed = failed = 0
    started = time.monotonic()

    for i in range(1, count + 1):
        username = f"e2e_test_{i:03d}"
        payload = {
            "username": username,
            "password": "Test123456",
            "phone": f"138{10000000 + i:08d}",
            "email": f"{username}@example.com",
        }

        resp: requests.Response | None = None
        for _ in range(120):  # 容忍长时间限流（120 次重试）
            try:
                resp = requests.post(f"{base_url}/api/auth/register", json=payload, timeout=10)
            except requests.RequestException as exc:
                print(f"❌ {username} 网络错误: {exc}")
                time.sleep(interval)
                continue
            if resp.status_code == 429:
                retry_after = int(resp.headers.get("Retry-After", "60") or 60)
                print(f"⏳ {username} 触发限流，{retry_after}s 后重试 ...")
                time.sleep(max(retry_after, interval))
                continue
            break

        if resp is None:
            failed += 1
            print(f"❌ {username} 重试次数用尽，放弃")
            continue

        if resp.status_code == 201:
            created += 1
            print(f"✅ {username} created (201)")
        elif resp.status_code == 409:
            existed += 1
            print(f"ℹ️  {username} 已存在 (409)")
        else:
            failed += 1
            print(f"⚠️ {username} failed: {resp.status_code} - {resp.text[:120]}")

        time.sleep(interval)

    elapsed = time.monotonic() - started
    print("\n" + "=" * 60)
    print(f"完成！新建 {created}，已存在 {existed}，失败 {failed}，耗时 {elapsed / 60:.1f} 分钟")
    print("=" * 60)


def main() -> None:
    parser = argparse.ArgumentParser(description="批量创建 JMeter 压测账号")
    parser.add_argument("--count", type=int, default=100, help="创建账号数量（默认 100）")
    parser.add_argument("--base-url", default="http://localhost:5146", help="后端地址")
    parser.add_argument("--interval", type=float, default=21.0, help="每次请求间隔秒数（默认 21）")
    args = parser.parse_args()

    if args.count <= 0 or args.count > 999:
        print("❌ count 需在 1~999 之间")
        sys.exit(1)

    create_users(args.count, args.base_url.rstrip("/"), args.interval)


if __name__ == "__main__":
    main()
