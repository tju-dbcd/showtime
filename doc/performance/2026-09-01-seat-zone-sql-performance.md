# Seat-zone SQL performance

Commit base: `e9f97366ace26b3defef36a338015f8636efb921`
Measurement date: 2026-09-01 baseline and 2026-09-02 Oracle write/read runs
Status: **SET-BASED UPDATE VALIDATED — ExecuteUpdateAsync completes the 999-row update, while LockAsync remains a slow chunked insert.**

## Safety preflight

- Only the environment-variable names `Oracle_UserId` and `Oracle_Password` were checked; no value, password, connection string, lock token, or parameter value is recorded.
- The protected connection verified `SESSION_USER = CHENNAN`; the session switched `CURRENT_SCHEMA` to `APP_OWNER` for the approved read/write fixture. No `DEPLOY_USER` or shared-schema credentials were used.
- Required tables and relevant existing indexes were confirmed before the run. A harmless `EXPLAIN PLAN` preflight succeeded; its generated `PLAN_TABLE` row was removed precisely afterwards.
- All runner source, raw logs, and cleanup helpers remain in the system Temp directory. They are local evidence only and are not part of the PR.

## Fixture validation and cleanup

The temporary runner used ODP.NET array binding in chunks of 500 rather than EF `SaveChanges`, because Oracle-provider `INSERT ... RETURNING` generation made a 10,000-seat fixture impractically slow. It wrote only high random IDs and one 本地临时标签（已脱敏） in a personal-schema transaction.

The fixture inserted and verified:

| Table | Rows |
| --- | ---: |
| CATEGORY / SHOW / SYS_USER | 1 each |
| VENUE / SEAT_MAP / SHOW_SESSION | 3 each |
| SEAT_SECTION | 25 |
| SEAT | 12,200 |
| SEAT_LOCK / SEAT_RESERVATION | 2,000 / 1,000 |

After every attempt, cleanup used the tag as a bound parameter in child-to-parent order. The ten touched tables were individually verified at zero tagged rows. No shared-schema operation, `DROP`, `TRUNCATE`, Redis flush, or key scan was performed.

## Measured session-seat-map read baseline

Each request returned the stated number of seats, used six EF SQL commands, and was measured after ten warmups with three rounds of fifty samples. Timings include the remote Oracle connection and payload transfer, so they are end-to-end service timings rather than DB-only execution times.

| Seats | Round | P50 (ms) | P95 (ms) | SQL commands / 50 requests |
| ---: | ---: | ---: | ---: | ---: |
| 200 | 1 / 2 / 3 | 483 / 497 / 471 | 1123 / 906 / 577 | 300 |
| 2,000 | 1 / 2 / 3 | 964 / 969 / 977 | 1623 / 2034 / 1860 | 300 |
| 10,000 | 1 / 2 / 3 | 3313 / 3430 / 3301 | 3786 / 4099 / 4367 | 300 |

The command-count guard and real Oracle run agree: query count remains six as seat count grows, so there is no N+1 regression. Large-map delay grows with returned payload; an index change would not remove that DTO transfer cost.

## Real Oracle action measurements

All scenarios below used `LockDuration = 600s` (including non-lock actions for a consistent run configuration), ten warmups, and three measured rounds. `P50` and `P95` are milliseconds. The `Sql` column is the number of service action commands; `afterSample` release/delete cleanup commands are excluded.

### LockAsync

| Scenario | Round | P50 (ms) | P95 (ms) | Sql | Rows |
| --- | ---: | ---: | ---: | ---: | ---: |
| LockAsync_1 | 1 | 623.43 | 833.40 | 250 | 1 |
|  | 2 | 629.19 | 1272.20 | 250 | 1 |
|  | 3 | 593.34 | 912.34 | 250 | 1 |
| LockAsync_10 | 1 | 1392.18 | 2163.11 | 250 | 10 |
|  | 2 | 1358.11 | 1914.32 | 250 | 10 |
|  | 3 | 1437.48 | 2914.15 | 250 | 10 |
| LockAsync_100 | 1 | 9177.15 | 10691.23 | 350 | 100 |
|  | 2 | 8720.92 | 11194.44 | 350 | 100 |
|  | 3 | 8440.87 | 9489.98 | 350 | 100 |

Before the production change, `LockAsync_999` did not complete under the stagnation condition. After bounded persistence chunking (100 entities per `SaveChangesAsync` inside one transaction), the quick validation run completed all three samples:

| Scenario | Samples | P50 (ms) | P95 (ms) | Min / Max (ms) | Sql | Rows |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| LockAsync_999 (post-change) | 3 | 87,620.28 | 94,547.96 | 83,622.93 / 94,547.96 | 102 | 999 |

### ListSeats

| Scenario | Round | P50 (ms) | P95 (ms) | Sql | Rows |
| --- | ---: | ---: | ---: | ---: | ---: |
| ListSeats_20 | 1 | 227.66 | 291.90 | 150 | 20 |
|  | 2 | 234.93 | 306.42 | 150 | 20 |
|  | 3 | 234.76 | 284.61 | 150 | 20 |
| ListSeats_100 | 1 | 323.21 | 362.62 | 150 | 100 |
|  | 2 | 318.64 | 382.18 | 150 | 100 |
|  | 3 | 319.23 | 401.90 | 150 | 100 |
| ListSeats_1000 | 1 | 1315.76 | 1497.78 | 150 | 1000 |
|  | 2 | 1266.45 | 1538.45 | 150 | 1000 |
|  | 3 | 1388.45 | 1566.03 | 150 | 1000 |

### UpdateSeats

| Scenario | Round | P50 (ms) | P95 (ms) | Sql | Rows |
| --- | ---: | ---: | ---: | ---: | ---: |
| UpdateSeats_1 | 1 | 349.48 | 407.50 | 150 | 1 |
|  | 2 | 347.34 | 381.99 | 150 | 1 |
|  | 3 | 372.96 | 421.08 | 150 | 1 |
| UpdateSeats_100 | 1 | 7840.28 | 8784.94 | 250 | 100 |
|  | 2 | 7742.87 | 21571.17 | 250 | 100 |
|  | 3 | 13945.35 | 25322.71 | 250 | 100 |

Before the production change, `UpdateSeats_999` did not complete under the stagnation condition. The earlier bounded-persistence comparison (100 entities per `SaveChangesAsync` inside one transaction) completed all three samples:

| Scenario | Samples | P50 (ms) | P95 (ms) | Min / Max (ms) | Sql | Rows |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| UpdateSeats_999 (post-change) | 3 | 97,575.17 | 109,275.28 | 94,809.51 / 109,275.28 | 96 | 999 |

The current `ExecuteUpdateAsync` implementation was rechecked against real Oracle on 2026-09-02 using the personal `CHENNAN` schema with `PERF_QUICK=1` (one warmup and three samples); temporary data was cleaned up afterwards:

| Scenario | Samples | P50 (ms) | P95 (ms) | Min / Max (ms) | Sql | Rows |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| UpdateSeats_999 (`ExecuteUpdateAsync`) | 3 | 3,489.05 | 4,021.04 | 3,467.53 / 4,021.04 | 12 (4 / sample) | 999 |

The older chunked result above remains the comparison point: `UpdateSeats_999` P50 97,575.17 ms and P95 109,275.28 ms.

## Command-count and evidence interpretation

- For Lock/List/Update, `Sql` is the count of service action commands only. `afterSample` release/delete cleanup is not included.
- `CommandEvidence` is a de-duplicated set of SQL templates. Lock evidence includes the cleanup shape, so it must not be interpreted as the action command count.
- The current set-based update uses four commands per request: section-existence count, seat precheck, one `ExecuteUpdateAsync`, and response reload. `UpdateSeats_100`'s earlier five-command result is not the command count for the current implementation.

## Constraints and diagnostic findings

1. Initial EF fixture seeding failed with `ORA-02291 (FK_SEAT_MAP_VENUE)`: `SeatMap` has no mapped EF navigation/relationship to `Venue`, so a single EF save cannot be trusted to order this database-only foreign key.
2. A later same-venue, multi-map fixture failed on `UK_SEAT_MAP_DEFAULT`. The function-based index is `(VENUE_ID, CASE WHEN IS_DEFAULT='Y' THEN 1 END)`; the final fixture used three synthetic venues and explicitly supplied `IS_DEFAULT = 'N'`.
3. The personal-schema `USER_INDEXES` metadata read did not progress after fixture verification. A separate earlier `DBMS_XPLAN.DISPLAY` attempt also did not return. Both paths were excluded from the measurement runner after their processes were safely stopped and cleaned up; they were not repeated.
4. Existing Seat/Section/Map/Lock/Reservation indexes cover the principal predicates exercised here. Because plan metadata was unavailable, no index hypothesis is supported by this run.

## Decision

**ADOPT SET-BASED UPDATE.** The current `ExecuteUpdateAsync` implementation completes the 999-row update in the quick Oracle recheck at roughly 3.5–4.0 seconds end to end. `LockAsync` remains a separate, slow chunked insert path; its insertion optimization should be evaluated independently, including ODP.NET array binding, and must not be conflated with the set-based update decision. Do not create an index or change schema from this run.

## Graphical-editor backend acceptance

Coordinate fields `RowIndex`, `ColIndex`, `XCoord`, and `YCoord` are present in the backend contract. API boundary validation explicitly covers `pageSize=1000` and `seatIds=999`. The LockAsync 999-row scenario completes after chunking and the UpdateSeats 999-row scenario completes with the set-based update; the latency result is separate from API boundary/correctness validation.

## Verification record

The final regression commands and outcomes are recorded below after execution:

```text
dotnet test backend.Tests/ShowtimeBackend.Tests.csproj --configuration Release --filter "FullyQualifiedName~SeatZone" --logger "console;verbosity=normal"
dotnet build Showtime.sln --no-restore --configuration Release
git diff --check
git status --short
```

Results:

- SeatZone filter: **PASS**, 64/64 tests passed.
- Release solution build: **PASS**, 0 warnings and 0 errors (3.80 s).
- `git diff --check`: **PASS** (no output).
- Full test run: 747 passed, 17 skipped, 7 unrelated failures in other modules while deleting concurrently used temporary SQLite files (`IOException`); no SeatZone test failed.
- Real Oracle quick validation: `LockAsync_999` and `UpdateSeats_999` each completed 3/3 samples; tagged fixture cleanup reported zero rows in every touched table.
- `V$SESSION` and `V$SQL` were readable for the personal session; `V$SESSION_BLOCKERS` remained unavailable (`ORA-00942`) under the granted role. No active blocking session was observed in the captured `V$SESSION` sample.
- At that run's end, no files were pending commit or push; subsequent implementation, test, and report commits are recorded in git history, and the current branch has not been pushed. The local implementation plan, temporary runner, raw JSON, and raw logs remain outside the PR file set.
