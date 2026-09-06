SET SQLBLANKLINES ON
WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK

-- 正式环境仅允许 DEPLOY_USER 修改 APP_OWNER；个人演练仅允许修改自己的 Schema。
DECLARE
    v_session_user VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'SESSION_USER'));
    v_owner        VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
BEGIN
    IF v_session_user = 'DEPLOY_USER' THEN
        EXECUTE IMMEDIATE 'ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER';
    ELSIF v_session_user <> v_owner OR v_owner = 'APP_OWNER' THEN
        RAISE_APPLICATION_ERROR(-20200, 'Unsupported migration owner');
    END IF;
END;
/

-- 对已存在对象先做 fail-closed 元数据校验，允许完整脚本安全重复执行。
-- 说明：唯一性不建普通 UNIQUE 约束，而建“忽略 NULL 幂等键”的函数唯一索引：
--   历史订单/改签子订单的 IDEMPOTENCY_KEY 为 NULL，同一用户可有多条；
--   只有非空幂等键才参与同用户唯一性校验（Oracle 复合唯一约束会把
--   (USER_ID, NULL) 判为重复，无法直接用于含大量 NULL 键的存量表）。
DECLARE
    v_owner              VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
    v_count              NUMBER;
    v_check_count        NUMBER;
    v_uk_constraint_count NUMBER;
    v_uk_index_count     NUMBER;
BEGIN
    SELECT COUNT(*) INTO v_count
    FROM ALL_TABLES
    WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER';
    IF v_count <> 1 THEN
        RAISE_APPLICATION_ERROR(-20201, 'T_ORDER is missing');
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND COLUMN_NAME = 'IDEMPOTENCY_KEY';
    IF v_count = 1 THEN
        SELECT COUNT(*) INTO v_count
        FROM ALL_TAB_COLUMNS
        WHERE OWNER = v_owner
          AND TABLE_NAME = 'T_ORDER'
          AND COLUMN_NAME = 'IDEMPOTENCY_KEY'
          AND DATA_TYPE = 'VARCHAR2'
          AND CHAR_LENGTH = 64
          AND CHAR_USED = 'C'
          AND NULLABLE = 'Y';
        IF v_count <> 1 THEN
            RAISE_APPLICATION_ERROR(-20202, 'IDEMPOTENCY_KEY has an unexpected definition');
        END IF;
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND COLUMN_NAME = 'IDEMPOTENCY_REQUEST_HASH';
    IF v_count = 1 THEN
        SELECT COUNT(*) INTO v_count
        FROM ALL_TAB_COLUMNS
        WHERE OWNER = v_owner
          AND TABLE_NAME = 'T_ORDER'
          AND COLUMN_NAME = 'IDEMPOTENCY_REQUEST_HASH'
          AND DATA_TYPE = 'CHAR'
          AND CHAR_LENGTH = 64
          AND CHAR_USED = 'C'
          AND NULLABLE = 'Y';
        IF v_count <> 1 THEN
            RAISE_APPLICATION_ERROR(-20203, 'IDEMPOTENCY_REQUEST_HASH has an unexpected definition');
        END IF;
    END IF;

    SELECT COUNT(*) INTO v_check_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND CONSTRAINT_NAME = 'CHK_T_ORDER_IDEMPOTENCY_PAIR';
    IF v_check_count NOT IN (0, 1) THEN
        RAISE_APPLICATION_ERROR(-20204, 'Idempotency check constraint metadata is ambiguous');
    END IF;

    -- 兼容旧版迁移：早期版本可能已建成同名的普通 UNIQUE 约束，需先拆除再替换。
    SELECT COUNT(*) INTO v_uk_constraint_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND CONSTRAINT_NAME = 'UK_T_ORDER_USER_IDEMPOTENCY'
      AND CONSTRAINT_TYPE = 'U';
    IF v_uk_constraint_count NOT IN (0, 1) THEN
        RAISE_APPLICATION_ERROR(-20205, 'Idempotency unique constraint metadata is ambiguous');
    END IF;

    SELECT COUNT(*) INTO v_uk_index_count
    FROM ALL_INDEXES
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND INDEX_NAME = 'UK_T_ORDER_USER_IDEMPOTENCY';
    IF v_uk_index_count NOT IN (0, 1) THEN
        RAISE_APPLICATION_ERROR(-20206, 'Idempotency unique index metadata is ambiguous');
    END IF;
END;
/

DECLARE
    v_owner VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
    v_count NUMBER;
BEGIN
    SELECT COUNT(*) INTO v_count
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND COLUMN_NAME = 'IDEMPOTENCY_KEY';
    IF v_count = 0 THEN
        EXECUTE IMMEDIATE
            'ALTER TABLE T_ORDER ADD (IDEMPOTENCY_KEY VARCHAR2(64 CHAR))';
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND COLUMN_NAME = 'IDEMPOTENCY_REQUEST_HASH';
    IF v_count = 0 THEN
        EXECUTE IMMEDIATE
            'ALTER TABLE T_ORDER ADD (IDEMPOTENCY_REQUEST_HASH CHAR(64 CHAR))';
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND CONSTRAINT_NAME = 'CHK_T_ORDER_IDEMPOTENCY_PAIR';
    IF v_count = 0 THEN
        EXECUTE IMMEDIATE q'[
            ALTER TABLE T_ORDER ADD CONSTRAINT CHK_T_ORDER_IDEMPOTENCY_PAIR CHECK (
                (IDEMPOTENCY_KEY IS NULL AND IDEMPOTENCY_REQUEST_HASH IS NULL) OR
                (IDEMPOTENCY_KEY IS NOT NULL AND IDEMPOTENCY_REQUEST_HASH IS NOT NULL)) ENABLE VALIDATE]';
    END IF;

    -- 旧版迁移若已建成同名普通 UNIQUE 约束，先拆除（其隐式索引会一并删除）。
    SELECT COUNT(*) INTO v_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND CONSTRAINT_NAME = 'UK_T_ORDER_USER_IDEMPOTENCY'
      AND CONSTRAINT_TYPE = 'U';
    IF v_count = 1 THEN
        EXECUTE IMMEDIATE
            'ALTER TABLE T_ORDER DROP CONSTRAINT UK_T_ORDER_USER_IDEMPOTENCY';
    END IF;

    -- 若已存在同名但非“函数唯一索引”的普通索引，拆除后重建。
    SELECT COUNT(*) INTO v_count
    FROM ALL_INDEXES
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND INDEX_NAME = 'UK_T_ORDER_USER_IDEMPOTENCY'
      AND (INDEX_TYPE <> 'FUNCTION-BASED NORMAL'
           OR UNIQUENESS <> 'UNIQUE'
           OR STATUS <> 'VALID');
    IF v_count = 1 THEN
        EXECUTE IMMEDIATE 'DROP INDEX UK_T_ORDER_USER_IDEMPOTENCY';
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_INDEXES
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND INDEX_NAME = 'UK_T_ORDER_USER_IDEMPOTENCY';
    IF v_count = 0 THEN
        EXECUTE IMMEDIATE q'[
            CREATE UNIQUE INDEX UK_T_ORDER_USER_IDEMPOTENCY ON T_ORDER (
                CASE WHEN IDEMPOTENCY_KEY IS NOT NULL THEN USER_ID END,
                CASE WHEN IDEMPOTENCY_KEY IS NOT NULL THEN IDEMPOTENCY_KEY END)]';
    END IF;

    EXECUTE IMMEDIATE
        'COMMENT ON COLUMN T_ORDER.IDEMPOTENCY_KEY IS ''普通订单创建幂等键（同一用户内唯一；NULL 表示非幂等订单，不参与唯一性）''';
    EXECUTE IMMEDIATE
        'COMMENT ON COLUMN T_ORDER.IDEMPOTENCY_REQUEST_HASH IS ''普通订单创建请求的 SHA-256 摘要''';
END;
/

-- 终态断言：既验证首次迁移，也验证重复执行没有漂移。
DECLARE
    v_owner      VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
    v_count      NUMBER;
    v_definition VARCHAR2(4000);
BEGIN
    SELECT COUNT(*) INTO v_count
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND ((COLUMN_NAME = 'IDEMPOTENCY_KEY'
            AND DATA_TYPE = 'VARCHAR2' AND CHAR_LENGTH = 64 AND CHAR_USED = 'C' AND NULLABLE = 'Y')
        OR (COLUMN_NAME = 'IDEMPOTENCY_REQUEST_HASH'
            AND DATA_TYPE = 'CHAR' AND CHAR_LENGTH = 64 AND CHAR_USED = 'C' AND NULLABLE = 'Y'));
    IF v_count <> 2 THEN
        RAISE_APPLICATION_ERROR(-20210, 'Order idempotency columns verification failed');
    END IF;

    SELECT REGEXP_REPLACE(UPPER(REPLACE(SEARCH_CONDITION_VC, '"', '')), '[[:space:]]', '')
      INTO v_definition
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND CONSTRAINT_NAME = 'CHK_T_ORDER_IDEMPOTENCY_PAIR'
      AND CONSTRAINT_TYPE = 'C';
    IF v_definition <>
       '(IDEMPOTENCY_KEYISNULLANDIDEMPOTENCY_REQUEST_HASHISNULL)OR(IDEMPOTENCY_KEYISNOTNULLANDIDEMPOTENCY_REQUEST_HASHISNOTNULL)' THEN
        RAISE_APPLICATION_ERROR(-20211, 'Idempotency pair constraint verification failed');
    END IF;

    -- 必须是函数唯一索引：忽略 NULL 幂等键，同用户内仅对非空幂等键去重。
    -- 仅做计数断言，避免在 SQL*Plus/驱动里读取 LONG 类型的 COLUMN_EXPRESSION。
    SELECT COUNT(*) INTO v_count
    FROM ALL_INDEXES i
    WHERE i.OWNER = v_owner
      AND i.TABLE_NAME = 'T_ORDER'
      AND i.INDEX_NAME = 'UK_T_ORDER_USER_IDEMPOTENCY'
      AND i.INDEX_TYPE = 'FUNCTION-BASED NORMAL'
      AND i.UNIQUENESS = 'UNIQUE'
      AND i.STATUS = 'VALID'
      AND (SELECT COUNT(*)
           FROM ALL_IND_EXPRESSIONS e
           WHERE e.INDEX_OWNER = i.OWNER
             AND e.INDEX_NAME = i.INDEX_NAME) = 2;
    IF v_count <> 1 THEN
        RAISE_APPLICATION_ERROR(-20212, 'Idempotency unique index verification failed');
    END IF;

    -- 不允许残留旧版普通 UNIQUE 约束（避免与函数索引语义冲突）。
    SELECT COUNT(*) INTO v_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'T_ORDER'
      AND CONSTRAINT_NAME = 'UK_T_ORDER_USER_IDEMPOTENCY'
      AND CONSTRAINT_TYPE = 'U';
    IF v_count <> 0 THEN
        RAISE_APPLICATION_ERROR(-20213, 'Legacy idempotency unique constraint still exists');
    END IF;
END;
/

SELECT SYS_CONTEXT('USERENV', 'SESSION_USER') AS SESSION_USER,
       SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') AS CURRENT_SCHEMA
FROM DUAL;
