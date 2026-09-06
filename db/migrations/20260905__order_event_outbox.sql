SET SQLBLANKLINES ON
WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK

-- 正式环境仅允许 DEPLOY_USER 修改 APP_OWNER；个人演练仅允许修改自己的 Schema。
DECLARE
    v_session_user VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'SESSION_USER'));
    v_owner        VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
BEGIN
    IF v_session_user = 'DEPLOY_USER' THEN
        EXECUTE IMMEDIATE 'ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER';
    ELSIF v_session_user <> v_owner OR v_owner IN ('APP_OWNER', 'DEPLOY_USER') THEN
        RAISE_APPLICATION_ERROR(-20400, 'Unsupported migration owner');
    END IF;
END;
/

-- 若表已存在，先 fail-closed 校验全部列，禁止在未知同名对象上继续修改。
DECLARE
    v_owner       VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
    v_table_count NUMBER;
    v_valid_count NUMBER;
    v_definition  VARCHAR2(4000);
    v_same_name   NUMBER;
BEGIN
    SELECT COUNT(*) INTO v_table_count
      FROM ALL_TABLES
     WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX';

    -- 防止 ORA-00955：目标 Schema 存在同名对象（任意类型/任意大小写）但不是
    -- 规范大写表时，fail-closed 报明确错误，禁止在未知对象上继续修改或盲建。
    SELECT COUNT(*) INTO v_same_name
      FROM ALL_OBJECTS
     WHERE OWNER = v_owner AND UPPER(OBJECT_NAME) = 'T_ORDER_EVENT_OUTBOX';
    IF v_table_count = 0 AND v_same_name > 0 THEN
        RAISE_APPLICATION_ERROR(-20416, 'T_ORDER_EVENT_OUTBOX exists as a non-canonical object (wrong case or non-table type); resolve it before rerunning');
    END IF;

    -- 正式部署（APP_OWNER）时用 DBA_OBJECTS 兜底：若对象由 APP_OWNER 直接创建而
    -- 未授权给 DEPLOY_USER，ALL_* 视图不可见，但名字仍会占用，需先处理归属再重跑。
    -- 无 DBA 字典权限时静默跳过，仍由上面的 ALL_OBJECTS 检查兜底。
    IF v_owner = 'APP_OWNER' THEN
        v_same_name := 0;
        BEGIN
            EXECUTE IMMEDIATE
                'SELECT COUNT(*) FROM DBA_OBJECTS WHERE OWNER = ''APP_OWNER'' AND UPPER(OBJECT_NAME) = ''T_ORDER_EVENT_OUTBOX'''
                INTO v_same_name;
        EXCEPTION
            WHEN OTHERS THEN
                v_same_name := -1;
        END;
        IF v_same_name > 0 AND v_table_count = 0 THEN
            RAISE_APPLICATION_ERROR(-20417, 'T_ORDER_EVENT_OUTBOX exists in APP_OWNER but is invisible here (likely owned by APP_OWNER without grants); reconcile ownership (drop or grant) before rerunning');
        END IF;
    END IF;

    IF v_table_count = 1 THEN
        SELECT COUNT(*) INTO v_valid_count
          FROM ALL_TAB_COLUMNS
         WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
           AND (
                (COLUMN_NAME = 'EVENT_ID' AND DATA_TYPE = 'CHAR' AND CHAR_LENGTH = 36 AND CHAR_USED = 'C' AND NULLABLE = 'N') OR
                (COLUMN_NAME = 'EVENT_TYPE' AND DATA_TYPE = 'VARCHAR2' AND CHAR_LENGTH = 50 AND CHAR_USED = 'C' AND NULLABLE = 'N') OR
                (COLUMN_NAME = 'ROUTING_KEY' AND DATA_TYPE = 'VARCHAR2' AND CHAR_LENGTH = 100 AND CHAR_USED = 'C' AND NULLABLE = 'N') OR
                (COLUMN_NAME IN ('AGGREGATE_ID', 'USER_ID') AND DATA_TYPE = 'NUMBER' AND DATA_PRECISION = 19 AND DATA_SCALE = 0 AND NULLABLE = 'N') OR
                (COLUMN_NAME = 'PAYLOAD' AND DATA_TYPE = 'CLOB' AND NULLABLE = 'N') OR
                (COLUMN_NAME IN ('OCCURRED_AT', 'NEXT_ATTEMPT_AT', 'CREATE_TIME', 'UPDATE_TIME') AND DATA_TYPE = 'TIMESTAMP(6)' AND NULLABLE = 'N') OR
                (COLUMN_NAME IN ('LOCKED_UNTIL', 'PUBLISHED_AT') AND DATA_TYPE = 'TIMESTAMP(6)' AND NULLABLE = 'Y') OR
                (COLUMN_NAME = 'STATUS' AND DATA_TYPE = 'VARCHAR2' AND CHAR_LENGTH = 20 AND CHAR_USED = 'C' AND NULLABLE = 'N') OR
                (COLUMN_NAME = 'ATTEMPT_COUNT' AND DATA_TYPE = 'NUMBER' AND DATA_PRECISION = 5 AND DATA_SCALE = 0 AND NULLABLE = 'N') OR
                (COLUMN_NAME = 'LOCK_OWNER' AND DATA_TYPE = 'VARCHAR2' AND CHAR_LENGTH = 100 AND CHAR_USED = 'C' AND NULLABLE = 'Y') OR
                (COLUMN_NAME = 'LAST_ERROR' AND DATA_TYPE = 'VARCHAR2' AND CHAR_LENGTH = 1000 AND CHAR_USED = 'C' AND NULLABLE = 'Y') OR
                (COLUMN_NAME IN ('CREATE_BY', 'UPDATE_BY') AND DATA_TYPE = 'VARCHAR2' AND CHAR_LENGTH = 50 AND CHAR_USED = 'C' AND NULLABLE = 'Y')
           );
        IF v_valid_count <> 18 THEN
            RAISE_APPLICATION_ERROR(-20401, 'T_ORDER_EVENT_OUTBOX has an unexpected column definition');
        END IF;

        SELECT COUNT(*) INTO v_valid_count
          FROM ALL_TAB_COLUMNS
         WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX';
        IF v_valid_count <> 18 THEN
            RAISE_APPLICATION_ERROR(-20402, 'T_ORDER_EVENT_OUTBOX has unexpected extra columns');
        END IF;

        SELECT COUNT(*) INTO v_valid_count FROM ALL_CONSTRAINTS
         WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
           AND CONSTRAINT_NAME = 'PK_ORDER_EVENT_OUTBOX';
        IF v_valid_count = 1 THEN
            SELECT COUNT(*) INTO v_valid_count
              FROM ALL_CONSTRAINTS c
              JOIN ALL_CONS_COLUMNS cc
                ON cc.OWNER = c.OWNER AND cc.CONSTRAINT_NAME = c.CONSTRAINT_NAME
             WHERE c.OWNER = v_owner AND c.TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
               AND c.CONSTRAINT_NAME = 'PK_ORDER_EVENT_OUTBOX' AND c.CONSTRAINT_TYPE = 'P'
               AND cc.COLUMN_NAME = 'EVENT_ID' AND cc.POSITION = 1;
            IF v_valid_count <> 1 THEN
                RAISE_APPLICATION_ERROR(-20403, 'PK_ORDER_EVENT_OUTBOX has an unexpected definition');
            END IF;
        END IF;

        SELECT COUNT(*) INTO v_valid_count FROM ALL_CONSTRAINTS
         WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
           AND CONSTRAINT_NAME = 'CHK_ORDER_OUTBOX_STATUS';
        IF v_valid_count = 1 THEN
            SELECT REGEXP_REPLACE(UPPER(REPLACE(SEARCH_CONDITION_VC, '"', '')), '[[:space:]]', '')
              INTO v_definition FROM ALL_CONSTRAINTS
             WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
               AND CONSTRAINT_NAME = 'CHK_ORDER_OUTBOX_STATUS' AND CONSTRAINT_TYPE = 'C';
            IF v_definition <> 'STATUSIN(''PENDING'',''PROCESSING'',''PUBLISHED'',''FAILED'')' THEN
                RAISE_APPLICATION_ERROR(-20404, 'CHK_ORDER_OUTBOX_STATUS has an unexpected definition');
            END IF;
        END IF;

        SELECT COUNT(*) INTO v_valid_count FROM ALL_INDEXES
         WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
           AND INDEX_NAME = 'IDX_ORDER_OUTBOX_RETRY';
        IF v_valid_count = 1 THEN
            SELECT COUNT(*) INTO v_valid_count FROM ALL_IND_COLUMNS
             WHERE INDEX_OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
               AND INDEX_NAME = 'IDX_ORDER_OUTBOX_RETRY';
            IF v_valid_count <> 3 THEN
                RAISE_APPLICATION_ERROR(-20405, 'IDX_ORDER_OUTBOX_RETRY has an unexpected column count');
            END IF;
            SELECT COUNT(*) INTO v_valid_count FROM ALL_IND_COLUMNS
             WHERE INDEX_OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
               AND INDEX_NAME = 'IDX_ORDER_OUTBOX_RETRY'
               AND ((COLUMN_POSITION = 1 AND COLUMN_NAME = 'STATUS')
                 OR (COLUMN_POSITION = 2 AND COLUMN_NAME = 'NEXT_ATTEMPT_AT')
                 OR (COLUMN_POSITION = 3 AND COLUMN_NAME = 'EVENT_ID'));
            IF v_valid_count <> 3 THEN
                RAISE_APPLICATION_ERROR(-20405, 'IDX_ORDER_OUTBOX_RETRY has an unexpected definition');
            END IF;
        END IF;

        SELECT COUNT(*) INTO v_valid_count FROM ALL_INDEXES
         WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
           AND INDEX_NAME = 'IDX_ORDER_OUTBOX_AGGREGATE';
        IF v_valid_count = 1 THEN
            SELECT COUNT(*) INTO v_valid_count FROM ALL_IND_COLUMNS
             WHERE INDEX_OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
               AND INDEX_NAME = 'IDX_ORDER_OUTBOX_AGGREGATE';
            IF v_valid_count <> 2 THEN
                RAISE_APPLICATION_ERROR(-20406, 'IDX_ORDER_OUTBOX_AGGREGATE has an unexpected column count');
            END IF;
            SELECT COUNT(*) INTO v_valid_count FROM ALL_IND_COLUMNS
             WHERE INDEX_OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
               AND INDEX_NAME = 'IDX_ORDER_OUTBOX_AGGREGATE'
               AND ((COLUMN_POSITION = 1 AND COLUMN_NAME = 'AGGREGATE_ID')
                 OR (COLUMN_POSITION = 2 AND COLUMN_NAME = 'EVENT_TYPE'));
            IF v_valid_count <> 2 THEN
                RAISE_APPLICATION_ERROR(-20406, 'IDX_ORDER_OUTBOX_AGGREGATE has an unexpected definition');
            END IF;
        END IF;
    END IF;
END;
/

DECLARE
    v_owner VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
    v_count NUMBER;
BEGIN
    SELECT COUNT(*) INTO v_count FROM ALL_TABLES
     WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX';
    IF v_count = 0 THEN
        EXECUTE IMMEDIATE q'[
            CREATE TABLE T_ORDER_EVENT_OUTBOX (
                EVENT_ID       CHAR(36 CHAR) NOT NULL,
                EVENT_TYPE     VARCHAR2(50 CHAR) NOT NULL,
                ROUTING_KEY    VARCHAR2(100 CHAR) NOT NULL,
                AGGREGATE_ID   NUMBER(19) NOT NULL,
                USER_ID        NUMBER(19) NOT NULL,
                PAYLOAD        CLOB NOT NULL,
                OCCURRED_AT    TIMESTAMP(6) NOT NULL,
                STATUS         VARCHAR2(20 CHAR) DEFAULT 'PENDING' NOT NULL,
                ATTEMPT_COUNT  NUMBER(5) DEFAULT 0 NOT NULL,
                NEXT_ATTEMPT_AT TIMESTAMP(6) NOT NULL,
                LOCKED_UNTIL   TIMESTAMP(6),
                LOCK_OWNER     VARCHAR2(100 CHAR),
                PUBLISHED_AT   TIMESTAMP(6),
                LAST_ERROR     VARCHAR2(1000 CHAR),
                CREATE_TIME    TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP NOT NULL,
                UPDATE_TIME    TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP NOT NULL,
                CREATE_BY      VARCHAR2(50 CHAR),
                UPDATE_BY      VARCHAR2(50 CHAR),
                CONSTRAINT PK_ORDER_EVENT_OUTBOX PRIMARY KEY (EVENT_ID),
                CONSTRAINT CHK_ORDER_OUTBOX_STATUS CHECK (
                    STATUS IN ('PENDING', 'PROCESSING', 'PUBLISHED', 'FAILED'))
            )]';
    END IF;

    SELECT COUNT(*) INTO v_count FROM ALL_CONSTRAINTS
     WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
       AND CONSTRAINT_NAME = 'PK_ORDER_EVENT_OUTBOX' AND CONSTRAINT_TYPE = 'P';
    IF v_count = 0 THEN
        -- 主键按规范名收敛：表上可能已存在其他名字的单列 EVENT_ID 主键
        -- （如历史建表产生的 PK_T_ORDER_EVENT_OUTBOX / SYS_C 系统名），
        -- 先校验其定义为单列 EVENT_ID 主键，再删除并重建为规范名，
        -- 避免直接 ADD PRIMARY KEY 触发 ORA-02260；定义异常则 fail-closed。
        SELECT COUNT(*) INTO v_count FROM ALL_CONSTRAINTS
         WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
           AND CONSTRAINT_TYPE = 'P';
        IF v_count > 0 THEN
            SELECT COUNT(*) INTO v_count
              FROM ALL_CONSTRAINTS c
              JOIN ALL_CONS_COLUMNS cc
                ON cc.OWNER = c.OWNER AND cc.CONSTRAINT_NAME = c.CONSTRAINT_NAME
             WHERE c.OWNER = v_owner AND c.TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
               AND c.CONSTRAINT_TYPE = 'P' AND c.STATUS = 'ENABLED'
               AND cc.COLUMN_NAME = 'EVENT_ID' AND cc.POSITION = 1;
            IF v_count <> 1 THEN
                RAISE_APPLICATION_ERROR(-20415, 'T_ORDER_EVENT_OUTBOX has an unexpected primary key definition');
            END IF;

            SELECT COUNT(*) INTO v_count
              FROM ALL_CONS_COLUMNS cc
              JOIN ALL_CONSTRAINTS c
                ON c.OWNER = cc.OWNER AND c.CONSTRAINT_NAME = cc.CONSTRAINT_NAME
             WHERE c.OWNER = v_owner AND c.TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
               AND c.CONSTRAINT_TYPE = 'P';
            IF v_count <> 1 THEN
                RAISE_APPLICATION_ERROR(-20415, 'T_ORDER_EVENT_OUTBOX has an unexpected primary key definition');
            END IF;

            EXECUTE IMMEDIATE 'ALTER TABLE T_ORDER_EVENT_OUTBOX DROP PRIMARY KEY';
        END IF;
        EXECUTE IMMEDIATE 'ALTER TABLE T_ORDER_EVENT_OUTBOX ADD CONSTRAINT PK_ORDER_EVENT_OUTBOX PRIMARY KEY (EVENT_ID)';
    END IF;

    SELECT COUNT(*) INTO v_count FROM ALL_CONSTRAINTS
     WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
       AND CONSTRAINT_NAME = 'CHK_ORDER_OUTBOX_STATUS';
    IF v_count = 0 THEN
        EXECUTE IMMEDIATE q'[ALTER TABLE T_ORDER_EVENT_OUTBOX ADD CONSTRAINT CHK_ORDER_OUTBOX_STATUS CHECK (
            STATUS IN ('PENDING', 'PROCESSING', 'PUBLISHED', 'FAILED')) ENABLE VALIDATE]';
    END IF;

    SELECT COUNT(*) INTO v_count FROM ALL_INDEXES
     WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
       AND INDEX_NAME = 'IDX_ORDER_OUTBOX_RETRY';
    IF v_count = 0 THEN
        EXECUTE IMMEDIATE 'CREATE INDEX IDX_ORDER_OUTBOX_RETRY ON T_ORDER_EVENT_OUTBOX (STATUS, NEXT_ATTEMPT_AT, EVENT_ID)';
    END IF;

    SELECT COUNT(*) INTO v_count FROM ALL_INDEXES
     WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
       AND INDEX_NAME = 'IDX_ORDER_OUTBOX_AGGREGATE';
    IF v_count = 0 THEN
        EXECUTE IMMEDIATE 'CREATE INDEX IDX_ORDER_OUTBOX_AGGREGATE ON T_ORDER_EVENT_OUTBOX (AGGREGATE_ID, EVENT_TYPE)';
    END IF;
END;
/

-- 终态校验约束类型、定义与索引列顺序；重复执行必须得到同一终态。
DECLARE
    v_owner      VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
    v_count      NUMBER;
    v_definition VARCHAR2(4000);
BEGIN
    SELECT COUNT(*) INTO v_count FROM ALL_CONSTRAINTS
     WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
       AND CONSTRAINT_NAME = 'PK_ORDER_EVENT_OUTBOX' AND CONSTRAINT_TYPE = 'P' AND STATUS = 'ENABLED';
    IF v_count <> 1 THEN
        RAISE_APPLICATION_ERROR(-20410, 'Outbox primary key verification failed');
    END IF;

    SELECT REGEXP_REPLACE(UPPER(REPLACE(SEARCH_CONDITION_VC, '"', '')), '[[:space:]]', '')
      INTO v_definition FROM ALL_CONSTRAINTS
     WHERE OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
       AND CONSTRAINT_NAME = 'CHK_ORDER_OUTBOX_STATUS' AND CONSTRAINT_TYPE = 'C';
    IF v_definition <> 'STATUSIN(''PENDING'',''PROCESSING'',''PUBLISHED'',''FAILED'')' THEN
        RAISE_APPLICATION_ERROR(-20411, 'Outbox status constraint verification failed');
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM ALL_IND_COLUMNS
     WHERE INDEX_OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
       AND INDEX_NAME = 'IDX_ORDER_OUTBOX_RETRY'
       AND ((COLUMN_POSITION = 1 AND COLUMN_NAME = 'STATUS')
         OR (COLUMN_POSITION = 2 AND COLUMN_NAME = 'NEXT_ATTEMPT_AT')
         OR (COLUMN_POSITION = 3 AND COLUMN_NAME = 'EVENT_ID'));
    IF v_count <> 3 THEN
        RAISE_APPLICATION_ERROR(-20412, 'Outbox retry index verification failed');
    END IF;

    SELECT COUNT(*) INTO v_count
      FROM ALL_IND_COLUMNS
     WHERE INDEX_OWNER = v_owner AND TABLE_NAME = 'T_ORDER_EVENT_OUTBOX'
       AND INDEX_NAME = 'IDX_ORDER_OUTBOX_AGGREGATE'
       AND ((COLUMN_POSITION = 1 AND COLUMN_NAME = 'AGGREGATE_ID')
         OR (COLUMN_POSITION = 2 AND COLUMN_NAME = 'EVENT_TYPE'));
    IF v_count <> 2 THEN
        RAISE_APPLICATION_ERROR(-20413, 'Outbox aggregate index verification failed');
    END IF;
END;
/
