#!/usr/bin/env python3
"""为业务压测生成分配计划 CSV（sessionId,seatId,priceStrategyId,seatNo,section）。

通过匿名接口实时查询所有在售场次的可售座位，为每座匹配其所在票区的
ENABLED 定价策略（下单接口要求 priceStrategyId 与座位所在区一致），
按 sessionId、seatId 排序后写出前 --count 行。

已售/被锁座位不会出现在可售列表中，因此档间重新生成即可自动避开上一档
遗留占用；--exclude 可额外排除指定计划文件已用的座位。

用法:
    python3 generate_load_plan.py --count 500 --out out/plan_500.csv \
        [--base-url http://localhost:5146] [--exclude out/plan_smoke.csv ...]
"""
import argparse
import csv
import sys
import time

import requests


def fetch_json(session: requests.Session, url: str, retries: int = 6):
    last_exc: Exception | None = None
    for attempt in range(1, retries + 1):
        try:
            resp = session.get(url, timeout=15)
            resp.raise_for_status()
            return resp.json()["data"]
        except requests.RequestException as exc:
            last_exc = exc
            retry_after = 0
            if exc.response is not None:
                retry_after = int(exc.response.headers.get("Retry-After", 0) or 0)
            time.sleep(max(retry_after, 2 * attempt))
    print(f"  ⚠️ 放弃 {url}: {last_exc}")
    raise last_exc


def main() -> None:
    parser = argparse.ArgumentParser(description="生成压测座位分配计划")
    parser.add_argument("--count", type=int, required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--base-url", default="http://localhost:5146")
    parser.add_argument("--exclude", action="append", default=[], help="已用座位的计划 CSV（可多次）")
    args = parser.parse_args()
    base = args.base_url.rstrip("/")

    excluded: set[int] = set()
    for path in args.exclude:
        with open(path, newline="", encoding="utf-8") as f:
            excluded.update(int(r["seatId"]) for r in csv.DictReader(f))

    session = requests.Session()
    shows = fetch_json(session, f"{base}/api/client/shows")["items"]
    plan: list[dict] = []
    per_session: dict[int, int] = {}
    for show in shows:
        try:
            sessions = fetch_json(session, f"{base}/api/client/shows/{show['showId']}/sessions")
        except requests.RequestException:
            continue
        for se in sessions:
            if se.get("sessionStatus") != "ONSALE":
                continue
            sid = se["sessionId"]
            try:
                strategies = fetch_json(session, f"{base}/api/client/sessions/{sid}/pricing-strategies")
                seat_map = fetch_json(session, f"{base}/api/sessions/{sid}/seat-map")
            except (requests.RequestException, KeyError, TypeError) as exc:
                print(f"  ⚠️ 跳过场次 {sid}: {exc}")
                continue
            by_section: dict[int, int] = {}
            for st in strategies:
                if st.get("status") == "ENABLED" and st["seatSectionId"] not in by_section:
                    by_section[st["seatSectionId"]] = st["priceStrategyId"]
            for sec in seat_map["seatMap"]["sections"]:
                for seat in sec.get("seats") or []:
                    if seat.get("availabilityStatus") != "AVAILABLE":
                        continue
                    if not seat.get("isSellable") or seat.get("seatStatus") != "ENABLED":
                        continue
                    if seat["seatId"] in excluded:
                        continue
                    strategy = by_section.get(sec["seatSectionId"])
                    if strategy is None:
                        continue
                    plan.append({
                        "sessionId": sid,
                        "seatId": seat["seatId"],
                        "priceStrategyId": strategy,
                        "seatNo": seat.get("seatNo", ""),
                        "section": sec.get("sectionName", ""),
                    })
            per_session[sid] = len(seat_map["seatMap"]["sections"])

    plan.sort(key=lambda r: (r["sessionId"], r["seatId"]))
    per_session_alloc: dict[int, int] = {}
    for r in plan:
        per_session_alloc[r["sessionId"]] = per_session_alloc.get(r["sessionId"], 0) + 1
    print(f"各场次可售座位: {per_session_alloc}")
    if len(plan) < args.count:
        print(f"❌ 可分配座位仅 {len(plan)}，不足 {args.count}；排除座位 {len(excluded)} 个")
        sys.exit(1)
    rows = plan[: args.count]
    with open(args.out, "w", newline="", encoding="utf-8") as f:
        writer = csv.DictWriter(f, fieldnames=["sessionId", "seatId", "priceStrategyId", "seatNo", "section"])
        writer.writeheader()
        writer.writerows(rows)
    sessions_used: dict[int, int] = {}
    for r in rows:
        sessions_used[r["sessionId"]] = sessions_used.get(r["sessionId"], 0) + 1
    print(f"✅ 分配 {len(rows)} 座 → {args.out}；场次分布 {sessions_used}；"
          f"全库剩余可售（未含本档）{len(plan) - len(rows)}")


if __name__ == "__main__":
    main()
