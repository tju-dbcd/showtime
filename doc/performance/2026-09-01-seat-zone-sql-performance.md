# Seat-zone SQL performance: Before baseline

Commit base: `e9f97366ace26b3defef36a338015f8636efb921`
Date: 2026-09-01
Status: **PARTIAL BASELINE — seat-map read measurements completed; no database-side optimisation is justified by the available evidence.**

## Safety preflight

- Only the environment-variable names `Oracle_UserId` and `Oracle_Password` were checked; no value or connection string was recorded.
- The protected connection verified that `SESSION_USER` and `CURRENT_SCHEMA` match the approved personal schema, not `APP_OWNER` or `DEPLOY_USER`.
- The required tables and the relevant existing indexes were confirmed before the run. A harmless `EXPLAIN PLAN` preflight succeeded; its generated `PLAN_TABLE` row was removed precisely afterwards.
- All runner source, raw logs and cleanup helpers remain in the system Temp directory. Nothing there is committed.

## Fixture validation

The final temporary runner used ODP.NET array binding in chunks of 500 rather than EF `SaveChanges`, because Oracle-provider `INSERT ... RETURNING` generation made a 10,000-seat fixture impractically slow. It wrote only high random IDs and one `PERF_SZ_...` audit tag, in a personal-schema transaction.

The following synthetic fixture was successfully inserted and verified before every measurement run:

| Table | Rows |
| --- | ---: |
| CATEGORY / SHOW / SYS_USER | 1 each |
| VENUE / SEAT_MAP / SHOW_SESSION | 3 each |
| SEAT_SECTION | 25 |
| SEAT | 12,200 |
| SEAT_LOCK / SEAT_RESERVATION | 2,000 / 1,000 |

After every attempt, cleanup used the tag as a bound parameter in child-to-parent order. The ten touched tables were then individually verified at zero tagged rows. No shared-schema operation, `DROP`, `TRUNCATE`, Redis flush, or key scan was performed.

## Measured session-seat-map read

Each request returned the stated number of seats, used six EF SQL commands, and was measured after ten warmups with three rounds of fifty samples. The reported timings include the remote Oracle connection and payload transfer, so they are end-to-end service timings rather than DB-only execution times.

| Seats | Round | P50 (ms) | P95 (ms) | SQL commands / 50 requests |
| ---: | ---: | ---: | ---: | ---: |
| 200 | 1 / 2 / 3 | 483 / 497 / 471 | 1123 / 906 / 577 | 300 |
| 2,000 | 1 / 2 / 3 | 964 / 969 / 977 | 1623 / 2034 / 1860 | 300 |
| 10,000 | 1 / 2 / 3 | 3313 / 3430 / 3301 | 3786 / 4099 / 4367 | 300 |

The command-count guard and the real Oracle run agree: the query count remains six as the seat count grows, so there is no N+1 regression. The large-map delay grows with the returned seat payload; a database index change would not reduce that DTO transfer cost.

## Constraints and diagnostic findings

1. Initial EF fixture seeding failed with `ORA-02291 (FK_SEAT_MAP_VENUE)`: `SeatMap` has no mapped EF navigation/relationship to `Venue`, so a single EF save cannot be trusted to order this database-only foreign key.
2. A later same-venue, multi-map fixture failed on `UK_SEAT_MAP_DEFAULT`. The actual function-based index is `(VENUE_ID, CASE WHEN IS_DEFAULT='Y' THEN 1 END)`; multiple rows with the same non-null venue key and the `NULL` expression are still not suitable for this fixture. The final fixture used three synthetic venues and explicitly supplied `IS_DEFAULT = 'N'`.
3. The personal-schema `USER_INDEXES` metadata read did not progress after fixture verification. A separate earlier `DBMS_XPLAN.DISPLAY` attempt also did not return. Both paths were excluded from the measurement runner after their processes were safely stopped and cleaned up.
4. The first complete runner appeared stalled only because it did not print progress during its 160 warmup/sample calls. A diagnostic run proved the 200-seat service path completes six EF commands and returns 200 seats; the full measurements above subsequently completed and cleaned up.

## Decision and next step

Do **not** add an index or rewrite a query based on this run: the completed Oracle measurements show fixed query counts and payload-dominated growth, while the unavailable plan metadata cannot support an index hypothesis. The portable SQLite command-count tests in this branch remain useful N+1 regression guards.

To resume this task, use one of these controlled paths:

1. ask the database owner to diagnose the blocked personal-schema metadata views before attempting plan-based index work; and
2. collect the same three-round baseline for lock, list and update scenarios in an isolated Oracle environment before making a write-path optimisation claim.
