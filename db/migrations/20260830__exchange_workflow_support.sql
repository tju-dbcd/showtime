SET SQLBLANKLINES ON
WHENEVER SQLERROR EXIT SQL.SQLCODE

-- The same file is used for personal-schema rehearsal and formal deployment.
-- Personal: SESSION_USER = CURRENT_SCHEMA = LEIKAI.
-- Formal:   SESSION_USER = DEPLOY_USER; this block selects APP_OWNER explicitly.
DECLARE
    v_session_user  VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'SESSION_USER'));
    v_current_owner VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
BEGIN
    IF v_session_user = 'DEPLOY_USER' THEN
        EXECUTE IMMEDIATE 'ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER';
        v_current_owner := UPPER(SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
        IF v_current_owner <> 'APP_OWNER' THEN
            RAISE_APPLICATION_ERROR(-20100, 'Formal deployment must target APP_OWNER');
        END IF;
    ELSIF v_session_user = 'LEIKAI'
       AND v_current_owner = 'LEIKAI' THEN
        NULL;
    ELSE
        RAISE_APPLICATION_ERROR(-20101, 'Unsupported SESSION_USER/CURRENT_SCHEMA combination');
    END IF;
END;
/

-- Complete fail-closed preflight. No schema object is changed above or in this block.
DECLARE
    v_owner              VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
    v_count              NUMBER;
    v_definition         VARCHAR2(4000);
    v_eticket_old        NUMBER := 0;
    v_eticket_target     NUMBER := 0;
    v_eticket_temp       NUMBER := 0;
    v_column_count       NUMBER;
    v_fk_count           NUMBER;
    v_policy_index_count NUMBER;
    v_combo_count        NUMBER;
    v_unique_count       NUMBER;
    v_item_index_count   NUMBER;
    v_default_value      VARCHAR2(4000);

    FUNCTION normalized_condition(p_condition VARCHAR2) RETURN VARCHAR2 IS
    BEGIN
        RETURN REGEXP_REPLACE(UPPER(REPLACE(p_condition, '"', '')), '[[:space:]]', '');
    END;

    PROCEDURE require_table(p_table_name VARCHAR2) IS
        v_table_count NUMBER;
    BEGIN
        SELECT COUNT(*) INTO v_table_count
        FROM ALL_TABLES
        WHERE OWNER = v_owner
          AND TABLE_NAME = p_table_name;
        IF v_table_count <> 1 THEN
            RAISE_APPLICATION_ERROR(-20102, 'Missing target table ' || p_table_name || ' for owner ' || v_owner);
        END IF;
    END;
BEGIN
    IF v_owner = 'APP_OWNER' THEN
        IF UPPER(SYS_CONTEXT('USERENV', 'SESSION_USER')) <> 'DEPLOY_USER' THEN
            RAISE_APPLICATION_ERROR(-20103, 'APP_OWNER may only be targeted by DEPLOY_USER');
        END IF;
    ELSIF UPPER(SYS_CONTEXT('USERENV', 'SESSION_USER')) <> 'LEIKAI'
       OR v_owner <> 'LEIKAI' THEN
        RAISE_APPLICATION_ERROR(-20104, 'Personal rehearsal must target the session user');
    END IF;

    require_table('E_TICKET');
    require_table('EXCHANGE_REQUEST');
    require_table('EXCHANGE_ITEM');
    require_table('EXCHANGE_POLICY');

    SELECT COUNT(*) INTO v_count
    FROM ALL_SYNONYMS
    WHERE OWNER IN (v_owner, 'PUBLIC')
      AND SYNONYM_NAME IN ('E_TICKET', 'EXCHANGE_REQUEST', 'EXCHANGE_ITEM', 'EXCHANGE_POLICY');
    IF v_count > 0 THEN
        RAISE_APPLICATION_ERROR(-20105, 'Synonyms are not accepted for exchange migration targets');
    END IF;

    FOR c IN (
        SELECT CONSTRAINT_NAME, SEARCH_CONDITION_VC
        FROM ALL_CONSTRAINTS
        WHERE OWNER = v_owner
          AND TABLE_NAME = 'E_TICKET'
          AND CONSTRAINT_NAME IN ('CHK_ETICKET_STATUS', 'CHK_ETICKET_STATUS_NEW')
          AND CONSTRAINT_TYPE = 'C'
    ) LOOP
        v_definition := normalized_condition(c.SEARCH_CONDITION_VC);
        IF c.CONSTRAINT_NAME = 'CHK_ETICKET_STATUS' THEN
            IF v_definition = 'TICKET_STATUSIN(''UNUSED'',''REFUNDING'',''USED'',''REFUNDED'',''EXCHANGED'')' THEN
                v_eticket_old := 1;
            ELSIF v_definition = 'TICKET_STATUSIN(''UNUSED'',''REFUNDING'',''EXCHANGING'',''USED'',''REFUNDED'',''EXCHANGED'')' THEN
                v_eticket_target := 1;
            ELSE
                RAISE_APPLICATION_ERROR(-20106, 'CHK_ETICKET_STATUS has an unexpected definition');
            END IF;
        ELSIF v_definition = 'TICKET_STATUSIN(''UNUSED'',''REFUNDING'',''EXCHANGING'',''USED'',''REFUNDED'',''EXCHANGED'')' THEN
            v_eticket_temp := 1;
        ELSE
            RAISE_APPLICATION_ERROR(-20107, 'CHK_ETICKET_STATUS_NEW has an unexpected definition');
        END IF;
    END LOOP;
    IF v_eticket_old + v_eticket_target + v_eticket_temp = 0
       OR v_eticket_target + v_eticket_temp > 1
       OR (v_eticket_target = 1 AND v_eticket_old = 1) THEN
        RAISE_APPLICATION_ERROR(-20108, 'E_TICKET status constraints are missing or ambiguous');
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM E_TICKET
    WHERE TICKET_STATUS NOT IN ('UNUSED', 'REFUNDING', 'EXCHANGING', 'USED', 'REFUNDED', 'EXCHANGED');
    IF v_count > 0 THEN
        RAISE_APPLICATION_ERROR(-20109, 'E_TICKET contains unsupported status values');
    END IF;

    SELECT COUNT(*) INTO v_column_count
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_REQUEST'
      AND COLUMN_NAME = 'APPLIED_POLICY_ID';
    IF v_column_count = 1 THEN
        SELECT COUNT(*) INTO v_count
        FROM ALL_TAB_COLUMNS
        WHERE OWNER = v_owner
          AND TABLE_NAME = 'EXCHANGE_REQUEST'
          AND COLUMN_NAME = 'APPLIED_POLICY_ID'
          AND DATA_TYPE = 'NUMBER'
          AND DATA_PRECISION = 19
          AND NVL(DATA_SCALE, 0) = 0
          AND NULLABLE = 'Y';
        IF v_count <> 1 THEN
            RAISE_APPLICATION_ERROR(-20110, 'APPLIED_POLICY_ID has an unexpected definition');
        END IF;
    ELSIF v_column_count <> 0 THEN
        RAISE_APPLICATION_ERROR(-20111, 'APPLIED_POLICY_ID metadata is ambiguous');
    END IF;

    SELECT COUNT(*) INTO v_fk_count
    FROM ALL_CONSTRAINTS fk
    JOIN ALL_CONS_COLUMNS fkc
      ON fkc.OWNER = fk.OWNER
     AND fkc.CONSTRAINT_NAME = fk.CONSTRAINT_NAME
     AND fkc.TABLE_NAME = fk.TABLE_NAME
    JOIN ALL_CONSTRAINTS pk
      ON pk.OWNER = fk.R_OWNER
     AND pk.CONSTRAINT_NAME = fk.R_CONSTRAINT_NAME
    JOIN ALL_CONS_COLUMNS pkc
      ON pkc.OWNER = pk.OWNER
     AND pkc.CONSTRAINT_NAME = pk.CONSTRAINT_NAME
     AND pkc.TABLE_NAME = pk.TABLE_NAME
     AND pkc.POSITION = fkc.POSITION
    WHERE fk.OWNER = v_owner
      AND fk.TABLE_NAME = 'EXCHANGE_REQUEST'
      AND fk.CONSTRAINT_NAME = 'FK_EXCHANGE_APPLIED_POLICY'
      AND fk.CONSTRAINT_TYPE = 'R'
      AND fkc.COLUMN_NAME = 'APPLIED_POLICY_ID'
      AND fkc.POSITION = 1
      AND pk.OWNER = v_owner
      AND pk.TABLE_NAME = 'EXCHANGE_POLICY'
      AND pkc.COLUMN_NAME = 'POLICY_ID';
    SELECT COUNT(*) INTO v_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_REQUEST'
      AND CONSTRAINT_NAME = 'FK_EXCHANGE_APPLIED_POLICY';
    IF v_count <> v_fk_count OR v_fk_count NOT IN (0, 1) OR (v_fk_count = 1 AND v_column_count = 0) THEN
        RAISE_APPLICATION_ERROR(-20112, 'FK_EXCHANGE_APPLIED_POLICY is missing its column or has drifted');
    END IF;

    SELECT COUNT(*) INTO v_policy_index_count
    FROM ALL_INDEXES i
    WHERE i.OWNER = v_owner
      AND i.TABLE_OWNER = v_owner
      AND i.TABLE_NAME = 'EXCHANGE_REQUEST'
      AND i.INDEX_NAME = 'IDX_EXCHANGE_APPLIED_POLICY'
      AND i.UNIQUENESS = 'NONUNIQUE'
      AND (SELECT COUNT(*)
           FROM ALL_IND_COLUMNS ic
           WHERE ic.INDEX_OWNER = v_owner
             AND ic.TABLE_OWNER = v_owner
             AND ic.INDEX_NAME = i.INDEX_NAME
             AND ic.TABLE_NAME = i.TABLE_NAME) = 1
      AND EXISTS (
          SELECT 1
          FROM ALL_IND_COLUMNS ic
          WHERE ic.INDEX_OWNER = v_owner
            AND ic.TABLE_OWNER = v_owner
            AND ic.INDEX_NAME = i.INDEX_NAME
            AND ic.TABLE_NAME = i.TABLE_NAME
            AND ic.COLUMN_POSITION = 1
            AND ic.COLUMN_NAME = 'APPLIED_POLICY_ID');
    SELECT COUNT(*) INTO v_count
    FROM ALL_INDEXES
    WHERE OWNER = v_owner
      AND INDEX_NAME = 'IDX_EXCHANGE_APPLIED_POLICY';
    IF v_count <> v_policy_index_count OR v_policy_index_count NOT IN (0, 1)
       OR (v_policy_index_count = 1 AND v_column_count = 0) THEN
        RAISE_APPLICATION_ERROR(-20113, 'IDX_EXCHANGE_APPLIED_POLICY has drifted');
    END IF;

    SELECT COUNT(*) INTO v_combo_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_REQUEST'
      AND CONSTRAINT_NAME = 'CHK_EXCHANGE_STATE_COMBO'
      AND CONSTRAINT_TYPE = 'C'
      AND REGEXP_REPLACE(UPPER(REPLACE(SEARCH_CONDITION_VC, '"', '')), '[[:space:]]', '') =
          '(APPROVE_STATUS=''PENDING''ANDEXCHANGE_STATUS=''PENDING'')OR(APPROVE_STATUS=''APPROVED''ANDEXCHANGE_STATUSIN(''PROCESSING'',''COMPLETED'',''FAILED''))OR(APPROVE_STATUS=''REJECTED''ANDEXCHANGE_STATUS=''FAILED'')';
    SELECT COUNT(*) INTO v_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_REQUEST'
      AND CONSTRAINT_NAME = 'CHK_EXCHANGE_STATE_COMBO';
    IF v_count <> v_combo_count OR v_combo_count NOT IN (0, 1) THEN
        RAISE_APPLICATION_ERROR(-20114, 'CHK_EXCHANGE_STATE_COMBO has drifted');
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM EXCHANGE_REQUEST
    WHERE NOT (
        (APPROVE_STATUS = 'PENDING' AND EXCHANGE_STATUS = 'PENDING') OR
        (APPROVE_STATUS = 'APPROVED' AND EXCHANGE_STATUS IN ('PROCESSING', 'COMPLETED', 'FAILED')) OR
        (APPROVE_STATUS = 'REJECTED' AND EXCHANGE_STATUS = 'FAILED'));
    IF v_count > 0 THEN
        RAISE_APPLICATION_ERROR(-20115, 'EXCHANGE_REQUEST contains an unsupported state combination');
    END IF;

    SELECT COUNT(*) INTO v_unique_count
    FROM ALL_CONSTRAINTS c
    WHERE c.OWNER = v_owner
      AND c.TABLE_NAME = 'EXCHANGE_ITEM'
      AND c.CONSTRAINT_NAME = 'UK_EXCHANGE_ORDER_ITEM'
      AND c.CONSTRAINT_TYPE = 'U'
      AND (SELECT COUNT(*)
           FROM ALL_CONS_COLUMNS cc
           WHERE cc.OWNER = v_owner
             AND cc.TABLE_NAME = c.TABLE_NAME
             AND cc.CONSTRAINT_NAME = c.CONSTRAINT_NAME) = 1
      AND EXISTS (
          SELECT 1
          FROM ALL_CONS_COLUMNS cc
          WHERE cc.OWNER = v_owner
            AND cc.TABLE_NAME = c.TABLE_NAME
            AND cc.CONSTRAINT_NAME = c.CONSTRAINT_NAME
            AND cc.POSITION = 1
            AND cc.COLUMN_NAME = 'ORDER_ITEM_ID');
    SELECT COUNT(*) INTO v_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_ITEM'
      AND CONSTRAINT_NAME = 'UK_EXCHANGE_ORDER_ITEM';
    IF v_count <> v_unique_count OR v_unique_count NOT IN (0, 1) THEN
        RAISE_APPLICATION_ERROR(-20116, 'UK_EXCHANGE_ORDER_ITEM has drifted');
    END IF;

    SELECT COUNT(*) INTO v_item_index_count
    FROM ALL_INDEXES i
    WHERE i.OWNER = v_owner
      AND i.TABLE_OWNER = v_owner
      AND i.TABLE_NAME = 'EXCHANGE_ITEM'
      AND i.INDEX_NAME = 'IDX_EXCHANGE_ITEM_ORDER'
      AND i.UNIQUENESS = 'NONUNIQUE'
      AND (SELECT COUNT(*)
           FROM ALL_IND_COLUMNS ic
           WHERE ic.INDEX_OWNER = v_owner
             AND ic.TABLE_OWNER = v_owner
             AND ic.INDEX_NAME = i.INDEX_NAME
             AND ic.TABLE_NAME = i.TABLE_NAME) = 1
      AND EXISTS (
          SELECT 1
          FROM ALL_IND_COLUMNS ic
          WHERE ic.INDEX_OWNER = v_owner
            AND ic.TABLE_OWNER = v_owner
            AND ic.INDEX_NAME = i.INDEX_NAME
            AND ic.TABLE_NAME = i.TABLE_NAME
            AND ic.COLUMN_POSITION = 1
            AND ic.COLUMN_NAME = 'ORDER_ITEM_ID');
    SELECT COUNT(*) INTO v_count
    FROM ALL_INDEXES
    WHERE OWNER = v_owner
      AND INDEX_NAME = 'IDX_EXCHANGE_ITEM_ORDER';
    IF v_count <> v_item_index_count OR v_item_index_count NOT IN (0, 1) THEN
        RAISE_APPLICATION_ERROR(-20117, 'IDX_EXCHANGE_ITEM_ORDER has drifted');
    END IF;

    FOR c IN (
        SELECT COLUMN_NAME, DATA_TYPE, DATA_PRECISION, DATA_SCALE, NULLABLE, DEFAULT_LENGTH
        FROM ALL_TAB_COLUMNS
        WHERE OWNER = v_owner
          AND TABLE_NAME = 'EXCHANGE_POLICY'
          AND COLUMN_NAME IN ('ALLOW_CROSS_SESSION', 'STATUS')
    ) LOOP
        IF c.DATA_TYPE <> 'NUMBER'
           OR c.DATA_PRECISION NOT IN (1, 3)
           OR NVL(c.DATA_SCALE, 0) <> 0
           OR c.NULLABLE <> 'N'
           OR NVL(c.DEFAULT_LENGTH, 0) = 0 THEN
            RAISE_APPLICATION_ERROR(-20118, c.COLUMN_NAME || ' has an unexpected definition');
        END IF;
    END LOOP;
    SELECT COUNT(*) INTO v_count
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_POLICY'
      AND COLUMN_NAME IN ('ALLOW_CROSS_SESSION', 'STATUS');
    IF v_count <> 2 THEN
        RAISE_APPLICATION_ERROR(-20119, 'Exchange policy flag columns are missing');
    END IF;
    SELECT DATA_DEFAULT INTO v_default_value
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_POLICY'
      AND COLUMN_NAME = 'ALLOW_CROSS_SESSION';
    IF TRIM(v_default_value) <> '1' THEN
        RAISE_APPLICATION_ERROR(-20121, 'ALLOW_CROSS_SESSION must keep DEFAULT 1');
    END IF;
    SELECT DATA_DEFAULT INTO v_default_value
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_POLICY'
      AND COLUMN_NAME = 'STATUS';
    IF TRIM(v_default_value) <> '1' THEN
        RAISE_APPLICATION_ERROR(-20122, 'STATUS must keep DEFAULT 1');
    END IF;
    SELECT COUNT(*) INTO v_count
    FROM EXCHANGE_POLICY
    WHERE ALLOW_CROSS_SESSION NOT IN (0, 1)
       OR STATUS NOT IN (0, 1);
    IF v_count > 0 THEN
        RAISE_APPLICATION_ERROR(-20120, 'Exchange policy flags contain values outside 0/1');
    END IF;
END;
/

-- Forward-repair every recognized interruption boundary.
DECLARE
    v_owner          VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
    v_count          NUMBER;
    v_definition     VARCHAR2(4000);
    v_old            NUMBER := 0;
    v_target         NUMBER := 0;
    v_temp           NUMBER := 0;
    v_precision_done NUMBER;

    FUNCTION normalized_condition(p_condition VARCHAR2) RETURN VARCHAR2 IS
    BEGIN
        RETURN REGEXP_REPLACE(UPPER(REPLACE(p_condition, '"', '')), '[[:space:]]', '');
    END;
BEGIN
    FOR c IN (
        SELECT CONSTRAINT_NAME, SEARCH_CONDITION_VC
        FROM ALL_CONSTRAINTS
        WHERE OWNER = v_owner
          AND TABLE_NAME = 'E_TICKET'
          AND CONSTRAINT_NAME IN ('CHK_ETICKET_STATUS', 'CHK_ETICKET_STATUS_NEW')
    ) LOOP
        v_definition := normalized_condition(c.SEARCH_CONDITION_VC);
        IF c.CONSTRAINT_NAME = 'CHK_ETICKET_STATUS'
           AND INSTR(v_definition, '''EXCHANGING''') = 0 THEN
            v_old := 1;
        ELSIF c.CONSTRAINT_NAME = 'CHK_ETICKET_STATUS' THEN
            v_target := 1;
        ELSE
            v_temp := 1;
        END IF;
    END LOOP;
    IF v_target = 0 THEN
        IF v_temp = 0 THEN
            EXECUTE IMMEDIATE q'[
                ALTER TABLE E_TICKET ADD CONSTRAINT CHK_ETICKET_STATUS_NEW
                CHECK (TICKET_STATUS IN ('UNUSED','REFUNDING','EXCHANGING','USED','REFUNDED','EXCHANGED')) ENABLE VALIDATE]';
            v_temp := 1;
        END IF;
        IF v_old = 1 THEN
            EXECUTE IMMEDIATE 'ALTER TABLE E_TICKET DROP CONSTRAINT CHK_ETICKET_STATUS';
        END IF;
        EXECUTE IMMEDIATE 'ALTER TABLE E_TICKET RENAME CONSTRAINT CHK_ETICKET_STATUS_NEW TO CHK_ETICKET_STATUS';
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_REQUEST'
      AND COLUMN_NAME = 'APPLIED_POLICY_ID';
    IF v_count = 0 THEN
        EXECUTE IMMEDIATE 'ALTER TABLE EXCHANGE_REQUEST ADD (APPLIED_POLICY_ID NUMBER(19))';
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_REQUEST'
      AND CONSTRAINT_NAME = 'FK_EXCHANGE_APPLIED_POLICY';
    IF v_count = 0 THEN
        EXECUTE IMMEDIATE q'[
            ALTER TABLE EXCHANGE_REQUEST ADD CONSTRAINT FK_EXCHANGE_APPLIED_POLICY
            FOREIGN KEY (APPLIED_POLICY_ID) REFERENCES EXCHANGE_POLICY(POLICY_ID) ENABLE VALIDATE]';
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_INDEXES
    WHERE OWNER = v_owner
      AND TABLE_OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_REQUEST'
      AND INDEX_NAME = 'IDX_EXCHANGE_APPLIED_POLICY';
    IF v_count = 0 THEN
        EXECUTE IMMEDIATE 'CREATE INDEX IDX_EXCHANGE_APPLIED_POLICY ON EXCHANGE_REQUEST(APPLIED_POLICY_ID)';
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_REQUEST'
      AND CONSTRAINT_NAME = 'CHK_EXCHANGE_STATE_COMBO';
    IF v_count = 0 THEN
        EXECUTE IMMEDIATE q'[
            ALTER TABLE EXCHANGE_REQUEST ADD CONSTRAINT CHK_EXCHANGE_STATE_COMBO CHECK (
                (APPROVE_STATUS = 'PENDING' AND EXCHANGE_STATUS = 'PENDING') OR
                (APPROVE_STATUS = 'APPROVED' AND EXCHANGE_STATUS IN ('PROCESSING','COMPLETED','FAILED')) OR
                (APPROVE_STATUS = 'REJECTED' AND EXCHANGE_STATUS = 'FAILED')) ENABLE VALIDATE]';
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_ITEM'
      AND CONSTRAINT_NAME = 'UK_EXCHANGE_ORDER_ITEM';
    IF v_count = 1 THEN
        EXECUTE IMMEDIATE 'ALTER TABLE EXCHANGE_ITEM DROP CONSTRAINT UK_EXCHANGE_ORDER_ITEM';
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_INDEXES
    WHERE OWNER = v_owner
      AND TABLE_OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_ITEM'
      AND INDEX_NAME = 'IDX_EXCHANGE_ITEM_ORDER';
    IF v_count = 0 THEN
        EXECUTE IMMEDIATE 'CREATE INDEX IDX_EXCHANGE_ITEM_ORDER ON EXCHANGE_ITEM(ORDER_ITEM_ID)';
    END IF;

    SELECT COUNT(*) INTO v_precision_done
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_POLICY'
      AND COLUMN_NAME IN ('ALLOW_CROSS_SESSION', 'STATUS')
      AND DATA_TYPE = 'NUMBER'
      AND DATA_PRECISION = 3
      AND NVL(DATA_SCALE, 0) = 0
      AND NULLABLE = 'N';
    IF v_precision_done <> 2 THEN
        EXECUTE IMMEDIATE q'[
            ALTER TABLE EXCHANGE_POLICY MODIFY (
                ALLOW_CROSS_SESSION NUMBER(3) DEFAULT 1,
                STATUS NUMBER(3) DEFAULT 1)]';
    END IF;

    EXECUTE IMMEDIATE 'COMMENT ON COLUMN EXCHANGE_REQUEST.APPLIED_POLICY_ID IS ''申请时命中的改签策略ID''';
END;
/

-- Terminal assertions also make a second execution a meaningful idempotency check.
DECLARE
    v_owner      VARCHAR2(128) := UPPER(SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA'));
    v_count      NUMBER;
    v_definition VARCHAR2(4000);
    v_default_value VARCHAR2(4000);
BEGIN
    IF v_owner = 'APP_OWNER' THEN
        IF UPPER(SYS_CONTEXT('USERENV', 'SESSION_USER')) <> 'DEPLOY_USER' THEN
            RAISE_APPLICATION_ERROR(-20130, 'Terminal owner assertion failed');
        END IF;
    ELSIF UPPER(SYS_CONTEXT('USERENV', 'SESSION_USER')) <> 'LEIKAI'
       OR v_owner <> 'LEIKAI' THEN
        RAISE_APPLICATION_ERROR(-20131, 'Terminal personal owner assertion failed');
    END IF;

    SELECT REGEXP_REPLACE(UPPER(REPLACE(SEARCH_CONDITION_VC, '"', '')), '[[:space:]]', '')
      INTO v_definition
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'E_TICKET'
      AND CONSTRAINT_NAME = 'CHK_ETICKET_STATUS';
    IF v_definition <> 'TICKET_STATUSIN(''UNUSED'',''REFUNDING'',''EXCHANGING'',''USED'',''REFUNDED'',''EXCHANGED'')' THEN
        RAISE_APPLICATION_ERROR(-20132, 'Terminal E_TICKET status constraint verification failed');
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_REQUEST'
      AND COLUMN_NAME = 'APPLIED_POLICY_ID'
      AND DATA_TYPE = 'NUMBER'
      AND DATA_PRECISION = 19
      AND NULLABLE = 'Y';
    IF v_count <> 1 THEN
        RAISE_APPLICATION_ERROR(-20133, 'Terminal APPLIED_POLICY_ID verification failed');
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND CONSTRAINT_NAME IN ('FK_EXCHANGE_APPLIED_POLICY', 'CHK_EXCHANGE_STATE_COMBO');
    IF v_count <> 2 THEN
        RAISE_APPLICATION_ERROR(-20134, 'Terminal exchange request constraints verification failed');
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_CONSTRAINTS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_ITEM'
      AND CONSTRAINT_NAME = 'UK_EXCHANGE_ORDER_ITEM';
    IF v_count <> 0 THEN
        RAISE_APPLICATION_ERROR(-20135, 'Terminal exchange item uniqueness verification failed');
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_INDEXES i
    WHERE i.OWNER = v_owner
      AND i.TABLE_OWNER = v_owner
      AND i.TABLE_NAME = 'EXCHANGE_ITEM'
      AND i.INDEX_NAME = 'IDX_EXCHANGE_ITEM_ORDER'
      AND i.UNIQUENESS = 'NONUNIQUE'
      AND EXISTS (
          SELECT 1 FROM ALL_IND_COLUMNS ic
          WHERE ic.INDEX_OWNER = v_owner
            AND ic.TABLE_OWNER = v_owner
            AND ic.INDEX_NAME = i.INDEX_NAME
            AND ic.TABLE_NAME = i.TABLE_NAME
            AND ic.COLUMN_POSITION = 1
            AND ic.COLUMN_NAME = 'ORDER_ITEM_ID');
    IF v_count <> 1 THEN
        RAISE_APPLICATION_ERROR(-20136, 'Terminal exchange item index verification failed');
    END IF;

    SELECT COUNT(*) INTO v_count
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_POLICY'
      AND COLUMN_NAME IN ('ALLOW_CROSS_SESSION', 'STATUS')
      AND DATA_TYPE = 'NUMBER'
      AND DATA_PRECISION = 3
      AND NVL(DATA_SCALE, 0) = 0
      AND NULLABLE = 'N';
    IF v_count <> 2 THEN
        RAISE_APPLICATION_ERROR(-20137, 'Terminal exchange policy precision verification failed');
    END IF;
    SELECT DATA_DEFAULT INTO v_default_value
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_POLICY'
      AND COLUMN_NAME = 'ALLOW_CROSS_SESSION';
    IF TRIM(v_default_value) <> '1' THEN
        RAISE_APPLICATION_ERROR(-20138, 'Terminal ALLOW_CROSS_SESSION default verification failed');
    END IF;
    SELECT DATA_DEFAULT INTO v_default_value
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = v_owner
      AND TABLE_NAME = 'EXCHANGE_POLICY'
      AND COLUMN_NAME = 'STATUS';
    IF TRIM(v_default_value) <> '1' THEN
        RAISE_APPLICATION_ERROR(-20139, 'Terminal STATUS default verification failed');
    END IF;
END;
/

SELECT SYS_CONTEXT('USERENV', 'SESSION_USER') AS SESSION_USER,
       SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA') AS CURRENT_SCHEMA
FROM DUAL;
