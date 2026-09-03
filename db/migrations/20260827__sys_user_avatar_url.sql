WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;
ALTER SESSION SET CURRENT_SCHEMA = APP_OWNER;

-- 前置守卫：目标列已存在视为部分/历史部署残留，停止并人工核对后再重跑
DECLARE
    v_existing NUMBER;
BEGIN
    SELECT COUNT(*) INTO v_existing
    FROM ALL_TAB_COLUMNS
    WHERE OWNER = 'APP_OWNER'
      AND TABLE_NAME = 'SYS_USER'
      AND COLUMN_NAME = 'AVATAR_URL';

    IF v_existing > 0 THEN
        RAISE_APPLICATION_ERROR(
            -20001,
            'SYS_USER.AVATAR_URL already exists. A partial or prior deployment was detected; manually restore the pre-migration schema before rerunning.');
    END IF;
END;
/

-- 用户头像公开访问 URL（OSS 上传 /api/files/upload 后由 PUT /api/users/me/avatar 写入）
ALTER TABLE SYS_USER ADD (
    AVATAR_URL VARCHAR2(500 CHAR)
);

COMMENT ON COLUMN SYS_USER.AVATAR_URL IS '用户头像公开访问 URL（OSS 上传后写入）';

SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, NULLABLE
FROM ALL_TAB_COLUMNS
WHERE OWNER = 'APP_OWNER'
  AND TABLE_NAME = 'SYS_USER'
  AND COLUMN_NAME = 'AVATAR_URL';