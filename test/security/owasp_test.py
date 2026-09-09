#!/usr/bin/env python3
"""OWASP Top 10 安全冒烟测试（Showtime 后端）。

前置条件：
- 后端已在 BASE_URL（默认 http://localhost:5146）运行；
- 已用 test/jmeter/seed_users.py 创建测试账号，或通过环境变量
  TEST_ACCOUNT / TEST_PASSWORD 指定一个真实可登录账号。

覆盖项与不适用说明：
- A01 失效访问控制：匿名访问受保护接口应 401；普通用户访问 /api/admin/** 应 403（垂直越权）；
- A03 SQL 注入 / XSS：通过 /api/client/shows?keyword= 做反射冒烟（后端为参数化查询，
  黑盒仅能做冒烟，不能证明绝对安全）；
- A05 敏感信息泄露：错误响应不应携带堆栈/内部异常信息；
- A07 爆破与限流：login 限流 5 次/分钟/IP，连续错误密码必须触发 429；
- CSRF：本项目为纯 Bearer JWT、无 Cookie 会话，CSRF 攻击面不适用，故不执行
  （若未来引入 Cookie 会话需补测）。
"""
import os
import sys
from urllib.parse import quote

import requests

BASE_URL = os.environ.get("SHOWTIME_BASE_URL", "http://localhost:5146")
TEST_ACCOUNT = os.environ.get("TEST_ACCOUNT", "e2e_test_001")
TEST_PASSWORD = os.environ.get("TEST_PASSWORD", "Test123456")
LEAK_MARKERS = ("stacktrace", "inner exception", "at showtimebackend.", "at system.")

FAILED: list[str] = []


def check(name: str, condition: bool, detail: str = "") -> None:
    status = "✅" if condition else "❌"
    print(f"  {status} {name}" + (f" ({detail})" if detail else ""))
    if not condition:
        FAILED.append(name)


def main() -> None:
    print("=" * 60)
    print("OWASP Top 10 安全冒烟测试 - Showtime")
    print(f"目标: {BASE_URL}")
    print("=" * 60)

    # ---- 1. A01: 匿名访问受保护接口 ----
    print("\n[1] A01: 失效的访问控制（匿名访问）")
    resp = requests.get(f"{BASE_URL}/api/orders", timeout=10)
    check("无 token 访问 /api/orders -> 401", resp.status_code == 401, f"HTTP {resp.status_code}")
    resp = requests.get(f"{BASE_URL}/api/auth/sessions", timeout=10)
    check("无 token 访问 /api/auth/sessions -> 401", resp.status_code == 401, f"HTTP {resp.status_code}")

    # ---- 前置: 登录（login 限流 5 次/分钟/IP，本脚本全程只登录 1 次 + 末段爆破用例）----
    print("\n[前置] 登录测试账号")
    login_resp = requests.post(
        f"{BASE_URL}/api/auth/login",
        json={"account": TEST_ACCOUNT, "password": TEST_PASSWORD},
        timeout=10,
    )
    if login_resp.status_code != 200:
        print(f"  ❌ 登录失败: HTTP {login_resp.status_code} {login_resp.text[:200]}")
        print("  请先运行 test/jmeter/seed_users.py 建号，或用 TEST_ACCOUNT/TEST_PASSWORD 指定账号。")
        sys.exit(2)
    token = login_resp.json()["data"]["accessToken"]
    auth_headers = {"Authorization": f"Bearer {token}"}
    print("  ✅ 登录成功")

    # ---- 2. A01: 垂直越权（普通用户访问管理端）----
    print("\n[2] A01: 垂直越权（普通用户访问 /api/admin/orders）")
    resp = requests.get(f"{BASE_URL}/api/admin/orders", headers=auth_headers, timeout=10)
    check("普通用户 -> 403", resp.status_code == 403, f"HTTP {resp.status_code}")
    print("  说明: 订单/锁座/实名等资源均由 JWT sub 归属、按当前用户过滤，接口不接收跨用户 ID，"
          "黑盒无法构造水平越权；若新增可指定归属者的接口需补测。")

    # ---- 3. A03: SQL 注入反射冒烟 ----
    print("\n[3] A03: SQL 注入（keyword 反射冒烟）")
    for payload in ["' OR '1'='1", "admin'--", "'; DROP TABLE sys_user; --"]:
        resp = requests.get(f"{BASE_URL}/api/client/shows?keyword={quote(payload)}", timeout=10)
        leaked = resp.status_code >= 500 or "ORA-" in resp.text or "SQL" in resp.text.upper()
        check(f"payload 未导致错误/回显: {payload[:18]}", not leaked, f"HTTP {resp.status_code}")

    # ---- 4. XSS 反射冒烟 ----
    print("\n[4] XSS（keyword 反射冒烟）")
    for payload in ["<script>alert('XSS')</script>", "<img src=x onerror=alert('XSS')>"]:
        resp = requests.get(f"{BASE_URL}/api/client/shows?keyword={quote(payload)}", timeout=10)
        reflected = resp.status_code == 200 and payload in resp.text
        check(f"payload 未反射: {payload[:24]}", not reflected, f"HTTP {resp.status_code}")

    # ---- 5. A05: 敏感信息泄露 ----
    print("\n[5] A05: 敏感信息泄露")
    resp = requests.post(f"{BASE_URL}/api/auth/login", json={"account": "", "password": ""}, timeout=10)
    leaked = any(marker in resp.text.lower() for marker in LEAK_MARKERS)
    check("空凭据错误响应无堆栈泄露", not leaked, f"HTTP {resp.status_code}")
    resp = requests.get(f"{BASE_URL}/api/definitely-not-exist", timeout=10)
    leaked = any(marker in resp.text.lower() for marker in LEAK_MARKERS)
    check("404 响应无堆栈泄露", not leaked, f"HTTP {resp.status_code}")

    # ---- 6. A07: 爆破防护与限流（放最后: 触发 429 后不再登录）----
    print("\n[6] A07: 爆破防护与限流")
    success_count = 0
    rate_limited = False
    for i in range(10):
        resp = requests.post(
            f"{BASE_URL}/api/auth/login",
            json={"account": TEST_ACCOUNT, "password": f"wrong_{i}"},
            timeout=10,
        )
        if resp.status_code == 200:
            success_count += 1
        elif resp.status_code == 429:
            rate_limited = True
    check("错误密码无成功登录", success_count == 0, f"成功 {success_count} 次")
    check("连续错误密码触发 429 限流", rate_limited, "未触发 429")

    print("\n" + "=" * 60)
    if FAILED:
        print(f"❌ 失败 {len(FAILED)} 项: {', '.join(FAILED)}")
        sys.exit(1)
    print("✅ 安全冒烟测试全部通过")
    print("=" * 60)


if __name__ == "__main__":
    try:
        main()
    except requests.RequestException as exc:
        print(f"❌ 无法连接后端 {BASE_URL}: {exc}")
        print("请确认后端已启动，或通过 SHOWTIME_BASE_URL 指定正确地址。")
        sys.exit(2)
