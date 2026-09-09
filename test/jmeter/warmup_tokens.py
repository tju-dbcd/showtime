#!/usr/bin/env python3
"""JMeter 压测 token 预热工具（对齐 seed_users.py 风格）。

三种模式（互斥）：

1) 登录预热（默认）: 读 test_users.csv，逐账号 POST /api/auth/login，
   写出 out/tokens.csv（account,accessToken,refreshToken,expiresAtUtc）。
   login 限流 5/min/IP → 节流 4/min（16s 间隔）；账号不存在(401)时先
   register 补建（register 限流 3/min/IP → 22s 间隔）再登录。
   结束时解码 JWT exp 校验剩余有效期。

2) --refresh: 读已有 out/tokens.csv，逐账号 POST /api/auth/refresh
   （refresh 限流 10/min/IP → 节流 8/min）。refresh 会轮换 refreshToken，
   每次轮换后立即原子写回，中断后重跑自动恢复。

3) --status: 只读校验 tokens.csv 的数量与 exp 剩余时间，不发登录/刷新请求。

用法:
    python3 warmup_tokens.py [--users test_users.csv] [--out out/tokens.csv]
                             [--base-url http://localhost:5146]
                             [--login-interval 16] [--refresh-interval 8]
                             [--register-interval 22]
                             [--refresh | --status] [--count 100]
"""
import argparse
import base64
import csv
import json
import os
import sys
import tempfile
import time

import requests


def egress_note() -> None:
    print("提示: login/register/refresh 限流按后端所见来源 IP 计数（本机公网出口）。")


def post_with_retry(
    session: requests.Session, url: str, payload: dict, interval: float, label: str
) -> requests.Response | None:
    """带限流退避的 POST；返回最终响应（None=网络持续失败）。"""
    for _ in range(120):
        try:
            resp = session.post(url, json=payload, timeout=15)
        except requests.RequestException as exc:
            print(f"  ⚠️ {label} 网络错误: {exc}")
            time.sleep(interval)
            continue
        if resp.status_code == 429:
            retry_after = int(resp.headers.get("Retry-After", "60") or 60)
            print(f"  ⏳ {label} 429，{retry_after}s 后重试")
            time.sleep(max(retry_after, interval))
            continue
        return resp
    return None


def register_user(session: requests.Session, base_url: str, account: str, password: str, interval: float) -> bool:
    payload = {
        "username": account,
        "password": password,
        "phone": f"138{10000000 + int(account.rsplit('_', 1)[1]):08d}",
        "email": f"{account}@example.com",
    }
    resp = post_with_retry(session, f"{base_url}/api/auth/register", payload, interval, f"register {account}")
    if resp is None:
        return False
    if resp.status_code in (201, 409):
        return True
    print(f"  ⚠️ register {account}: {resp.status_code} {resp.text[:120]}")
    return False


def login_one(session: requests.Session, base_url: str, account: str, password: str, interval: float) -> tuple[str, str, str] | None:
    """登录单账号，401 时先补建再试一次；成功返回 (accessToken, refreshToken, expiresAtUtc)。"""
    for attempt in (1, 2):
        resp = post_with_retry(session, f"{base_url}/api/auth/login", {"account": account, "password": password}, interval, f"login {account}")
        if resp is None:
            return None
        if resp.status_code == 200:
            data = resp.json()["data"]
            return data["accessToken"], data["refreshToken"], data["expiresAtUtc"]
        if resp.status_code == 401 and attempt == 1:
            print(f"  ℹ️ {account} 不存在(401)，先补建")
            if not register_user(session, base_url, account, password, 22.0):
                return None
            time.sleep(interval)
            continue
        print(f"  ⚠️ login {account}: {resp.status_code} {resp.text[:120]}")
        return None
    return None


def refresh_one(session: requests.Session, base_url: str, row: dict, interval: float) -> dict | None:
    resp = post_with_retry(session, f"{base_url}/api/auth/refresh", {"refreshToken": row["refreshToken"]}, interval, f"refresh {row['account']}")
    if resp is None:
        return None
    if resp.status_code == 200:
        data = resp.json()["data"]
        return {
            "account": row["account"],
            "accessToken": data["accessToken"],
            "refreshToken": data["refreshToken"],
            "expiresAtUtc": data["expiresAtUtc"],
        }
    print(f"  ⚠️ refresh {row['account']}: {resp.status_code} {resp.text[:120]}")
    return None


def atomic_write_csv(path: str, fieldnames: list[str], rows: list[dict]) -> None:
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    fd, tmp = tempfile.mkstemp(dir=os.path.dirname(path) or ".", suffix=".tmp")
    with os.fdopen(fd, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(rows)
    os.replace(tmp, path)


def jwt_exp_epoch(token: str) -> int | None:
    """解码 JWT payload 取 exp（不校验签名，仅本地核对）。"""
    try:
        part = token.split(".")[1]
        part += "=" * (-len(part) % 4)
        return json.loads(base64.urlsafe_b64decode(part)).get("exp")
    except Exception:
        return None


def report_status(rows: list[dict], now: float) -> None:
    exps = [jwt_exp_epoch(r["accessToken"]) for r in rows]
    missing = [r["account"] for r, e in zip(rows, exps) if e is None]
    valid = [e for e in exps if e is not None]
    print(f"token 总数 {len(rows)}，可解码 {len(valid)}，损坏 {len(missing)}")
    if valid:
        remain = [e - now for e in valid]
        print(f"距过期剩余: min {min(remain):.0f}s / max {max(remain):.0f}s")
    if missing:
        print(f"损坏账号: {missing[:5]}{'...' if len(missing) > 5 else ''}")


def main() -> None:
    parser = argparse.ArgumentParser(description="JMeter token 预热工具")
    parser.add_argument("--users", default="test_users.csv", help="账号 CSV（account,password）")
    parser.add_argument("--out", default="out/tokens.csv", help="token 输出 CSV")
    parser.add_argument("--base-url", default="http://localhost:5146")
    parser.add_argument("--count", type=int, default=100)
    parser.add_argument("--login-interval", type=float, default=16.0, help="登录间隔秒（4/min，限流 5）")
    parser.add_argument("--refresh-interval", type=float, default=7.6, help="刷新间隔秒（≈8/min，限流 10）")
    parser.add_argument("--refresh", action="store_true", help="模式2: 滚动刷新已有 token")
    parser.add_argument("--status", action="store_true", help="模式3: 仅校验 token 状态")
    args = parser.parse_args()
    base_url = args.base_url.rstrip("/")

    if args.status:
        with open(args.out, newline="", encoding="utf-8") as f:
            report_status(list(csv.DictReader(f)), time.time())
        return

    fieldnames = ["account", "accessToken", "refreshToken", "expiresAtUtc"]
    session = requests.Session()
    started = time.monotonic()

    if args.refresh:
        with open(args.out, newline="", encoding="utf-8") as f:
            rows = list(csv.DictReader(f))
        print(f"滚动刷新 {len(rows)} 个 token（refresh 限流 10/min/IP，间隔 {args.refresh_interval}s）")
        ok = fail = 0
        for i, row in enumerate(rows, 1):
            fresh = refresh_one(session, base_url, row, args.refresh_interval)
            if fresh:
                rows[i - 1] = fresh
                ok += 1
                atomic_write_csv(args.out, fieldnames, rows)  # 轮换后立即落盘
            else:
                fail += 1
            if i % 10 == 0 or i == len(rows):
                print(f"  进度 {i}/{len(rows)}，成功 {ok}，失败 {fail}")
            time.sleep(args.refresh_interval)
        atomic_write_csv(args.out, fieldnames, rows)
        report_status(rows, time.time())
        print(f"完成：刷新 {ok}，失败 {fail}，耗时 {(time.monotonic() - started) / 60:.1f} 分钟")
        return

    # 模式1: 登录预热
    egress_note()
    with open(args.users, newline="", encoding="utf-8") as f:
        users = [(r["account"], r["password"]) for r in csv.DictReader(f)]
    users = users[: args.count]
    print(f"登录预热 {len(users)} 个账号（login 限流 5/min/IP，间隔 {args.login_interval}s）")
    rows: list[dict] = []
    failed_accounts: list[str] = []
    for i, (account, password) in enumerate(users, 1):
        got = login_one(session, base_url, account, password, args.login_interval)
        if got:
            at, rt, exp = got
            rows.append({"account": account, "accessToken": at, "refreshToken": rt, "expiresAtUtc": exp})
            print(f"✅ [{i}/{len(users)}] {account} 登录成功")
        else:
            failed_accounts.append(account)
            print(f"❌ [{i}/{len(users)}] {account} 放弃")
        if i % 10 == 0 or i == len(users):
            atomic_write_csv(args.out, fieldnames, rows)
        time.sleep(args.login_interval)
    atomic_write_csv(args.out, fieldnames, rows)
    report_status(rows, time.time())
    print(f"完成：成功 {len(rows)}，失败 {len(failed_accounts)} {failed_accounts or ''}，耗时 {(time.monotonic() - started) / 60:.1f} 分钟")
    if failed_accounts:
        sys.exit(1)


if __name__ == "__main__":
    main()
