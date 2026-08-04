#!/bin/bash
#===============================================================================
# Oracle 21c 自动备份脚本
# 功能：RMAN 全库备份 + 归档日志备份 + 过期备份自动清理
# 用法：./oracle_backup.sh [full|incremental|archivelog]
# 建议通过 crontab 定时执行
#===============================================================================

set -euo pipefail

#---------------------------------------
# 配置区域（根据实际环境修改）
#---------------------------------------

# Oracle 环境变量
export ORACLE_SID="${ORACLE_SID:-orcl}"
export ORACLE_HOME="${ORACLE_HOME:-/u01/app/oracle/product/21.0.0/dbhome_1}"
export PATH="$ORACLE_HOME/bin:$PATH"

# RMAN 连接串
RMAN_TARGET="${RMAN_TARGET:-/}"                          # 本地连接：/ ；远程：sys/password@tnsname
RMAN_CATALOG="${RMAN_CATALOG:-}"                         # 恢复目录（可选），格式：rman_user/rman_pass@catdb

# 备份存储路径
BACKUP_ROOT="${BACKUP_ROOT:-/backup/oracle}"             # 备份根目录
BACKUP_TAG="${BACKUP_TAG:-AUTO_BACKUP}"                  # 备份标签前缀

# 保留策略（天数）
RETENTION_DAYS="${RETENTION_DAYS:-14}"                   # 备份保留天数

# 并行度
PARALLELISM="${PARALLELISM:-4}"                          # RMAN 通道数

# 压缩
COMPRESSION="${COMPRESSION:-BASIC}"                      # BASIC | LOW | MEDIUM | HIGH

# 日志
LOG_DIR="${LOG_DIR:-$BACKUP_ROOT/log}"
LOG_RETENTION_DAYS="${LOG_RETENTION_DAYS:-30}"

#---------------------------------------
# 运行时变量
#---------------------------------------

TIMESTAMP=$(date +%Y%m%d_%H%M%S)
DAY_OF_WEEK=$(date +%u)                                  # 1=Mon, 7=Sun
BACKUP_TYPE="${1:-full}"
LOG_FILE="$LOG_DIR/backup_${BACKUP_TYPE}_${TIMESTAMP}.log"
RMAN_SCRIPT=$(mktemp /tmp/rman_script.XXXXXX.rman)

# 退出码
EXIT_SUCCESS=0
EXIT_CONFIG_ERROR=1
EXIT_RMAN_ERROR=2
EXIT_ARCHIVE_ERROR=3
EXIT_CLEANUP_ERROR=4

#---------------------------------------
# 函数：输出和日志
#---------------------------------------

log() {
    local msg="[$(date '+%Y-%m-%d %H:%M:%S')] $*"
    echo "$msg" | tee -a "$LOG_FILE"
}

log_error() {
    log "ERROR: $*"
}

log_info() {
    log "INFO: $*"
}

#---------------------------------------
# 函数：初始化检查
#---------------------------------------

init_check() {
    # 创建必要目录
    mkdir -p "$BACKUP_ROOT"/{full,incremental,archivelog,log}

    # 检查 ORACLE_HOME
    if [[ ! -d "$ORACLE_HOME" ]]; then
        log_error "ORACLE_HOME 不存在: $ORACLE_HOME"
        exit $EXIT_CONFIG_ERROR
    fi

    # 检查 RMAN
    if [[ ! -x "$ORACLE_HOME/bin/rman" ]]; then
        log_error "rman 不可执行: $ORACLE_HOME/bin/rman"
        exit $EXIT_CONFIG_ERROR
    fi

    # 检查备份目录是否可写
    if [[ ! -w "$BACKUP_ROOT" ]]; then
        log_error "备份目录不可写: $BACKUP_ROOT"
        exit $EXIT_CONFIG_ERROR
    fi

    log_info "========================================="
    log_info "Oracle 21c 自动备份开始"
    log_info "========================================="
    log_info "备份类型: $BACKUP_TYPE"
    log_info "ORACLE_SID: $ORACLE_SID"
    log_info "ORACLE_HOME: $ORACLE_HOME"
    log_info "备份根目录: $BACKUP_ROOT"
    log_info "保留天数: $RETENTION_DAYS"
    log_info "并行度: $PARALLELISM"
    log_info "压缩级别: $COMPRESSION"
    log_info "日志文件: $LOG_FILE"
    log_info "========================================="
}

#---------------------------------------
# 函数：检查数据库状态
#---------------------------------------

check_db_status() {
    log_info "检查数据库状态..."

    local status
    status=$(
        sqlplus -S / as sysdba <<'SQL'
SET PAGESIZE 0 FEEDBACK OFF VERIFY OFF HEADING OFF ECHO OFF
SELECT status FROM v$instance;
EXIT;
SQL
    )

    if echo "$status" | grep -qi "OPEN"; then
        log_info "数据库状态: OPEN — 正常"
        return 0
    else
        log_error "数据库状态异常: $status"
        exit $EXIT_RMAN_ERROR
    fi
}

#---------------------------------------
# 函数：生成 RMAN 脚本
#---------------------------------------

generate_rman_script() {
    local script_file="$1"
    local backup_type="$2"

    # RMAN 头部配置
    cat > "$script_file" <<EOF
--=============================================================================
-- Oracle 21c RMAN 自动备份脚本
-- 生成时间: $(date '+%Y-%m-%d %H:%M:%S')
-- 备份类型: ${backup_type}
--=============================================================================

CONFIGURE RETENTION POLICY TO RECOVERY WINDOW OF ${RETENTION_DAYS} DAYS;
CONFIGURE BACKUP OPTIMIZATION ON;
CONFIGURE DEFAULT DEVICE TYPE TO DISK;
CONFIGURE DEVICE TYPE DISK PARALLELISM ${PARALLELISM} BACKUP TYPE TO COMPRESSED BACKUPSET;
CONFIGURE COMPRESSION ALGORITHM '${COMPRESSION}';
CONFIGURE CONTROLFILE AUTOBACKUP ON;
CONFIGURE CONTROLFILE AUTOBACKUP FORMAT FOR DEVICE TYPE DISK TO '${BACKUP_ROOT}/controlfile/%F';
CONFIGURE CHANNEL DEVICE TYPE DISK MAXPIECESIZE 10G;
CONFIGURE SNAPSHOT CONTROLFILE NAME TO '${ORACLE_HOME}/dbs/snapcf_${ORACLE_SID}.f';

EOF

    # 根据备份类型生成对应语句
    case "$backup_type" in
        full)
            cat >> "$script_file" <<EOF
RUN {
    ALLOCATE CHANNEL c1 DEVICE TYPE DISK FORMAT '${BACKUP_ROOT}/full/full_%d_%T_%s_%p.bkp';
    ALLOCATE CHANNEL c2 DEVICE TYPE DISK FORMAT '${BACKUP_ROOT}/full/full_%d_%T_%s_%p.bkp';
    ALLOCATE CHANNEL c3 DEVICE TYPE DISK FORMAT '${BACKUP_ROOT}/full/full_%d_%T_%s_%p.bkp';
    ALLOCATE CHANNEL c4 DEVICE TYPE DISK FORMAT '${BACKUP_ROOT}/full/full_%d_%T_%s_%p.bkp';

    BACKUP AS COMPRESSED BACKUPSET
        TAG '${BACKUP_TAG}_FULL_${TIMESTAMP}'
        DATABASE
        PLUS ARCHIVELOG DELETE INPUT;

    BACKUP AS COMPRESSED BACKUPSET
        TAG '${BACKUP_TAG}_SPFILE_${TIMESTAMP}'
        SPFILE;

    BACKUP AS COMPRESSED BACKUPSET
        TAG '${BACKUP_TAG}_CONTROLFILE_${TIMESTAMP}'
        CURRENT CONTROLFILE;

    RELEASE CHANNEL c1;
    RELEASE CHANNEL c2;
    RELEASE CHANNEL c3;
    RELEASE CHANNEL c4;
}

-- 删除超过保留策略的过期备份
DELETE NOPROMPT OBSOLETE;

-- 交叉检查备份
CROSSCHECK BACKUP;
CROSSCHECK ARCHIVELOG ALL;

-- 删除失效的备份记录
DELETE NOPROMPT EXPIRED BACKUP;
DELETE NOPROMPT EXPIRED ARCHIVELOG ALL;
EOF
            ;;

        incremental)
            cat >> "$script_file" <<EOF
RUN {
    ALLOCATE CHANNEL c1 DEVICE TYPE DISK FORMAT '${BACKUP_ROOT}/incremental/incr_%d_%T_%s_%p.bkp';
    ALLOCATE CHANNEL c2 DEVICE TYPE DISK FORMAT '${BACKUP_ROOT}/incremental/incr_%d_%T_%s_%p.bkp';
    ALLOCATE CHANNEL c3 DEVICE TYPE DISK FORMAT '${BACKUP_ROOT}/incremental/incr_%d_%T_%s_%p.bkp';
    ALLOCATE CHANNEL c4 DEVICE TYPE DISK FORMAT '${BACKUP_ROOT}/incremental/incr_%d_%T_%s_%p.bkp';

    BACKUP INCREMENTAL LEVEL 1
        AS COMPRESSED BACKUPSET
        TAG '${BACKUP_TAG}_INCR_${TIMESTAMP}'
        DATABASE;

    BACKUP AS COMPRESSED BACKUPSET
        TAG '${BACKUP_TAG}_ARCH_${TIMESTAMP}'
        ARCHIVELOG ALL DELETE INPUT;

    RELEASE CHANNEL c1;
    RELEASE CHANNEL c2;
    RELEASE CHANNEL c3;
    RELEASE CHANNEL c4;
}

DELETE NOPROMPT OBSOLETE;
CROSSCHECK BACKUP;
DELETE NOPROMPT EXPIRED BACKUP;
EOF
            ;;

        archivelog_only)
            cat >> "$script_file" <<EOF
RUN {
    ALLOCATE CHANNEL c1 DEVICE TYPE DISK FORMAT '${BACKUP_ROOT}/archivelog/arch_%d_%T_%s_%p.bkp';

    BACKUP AS COMPRESSED BACKUPSET
        TAG '${BACKUP_TAG}_ARCH_${TIMESTAMP}'
        ARCHIVELOG ALL DELETE INPUT;

    RELEASE CHANNEL c1;
}

DELETE NOPROMPT OBSOLETE;
CROSSCHECK ARCHIVELOG ALL;
DELETE NOPROMPT EXPIRED ARCHIVELOG ALL;
EOF
            ;;

        *)
            log_error "不支持的备份类型: $backup_type（支持: full | incremental | archivelog_only）"
            rm -f "$script_file"
            exit $EXIT_CONFIG_ERROR
            ;;
    esac

    # 追加末尾退出
    echo "EXIT;" >> "$script_file"

    log_info "RMAN 脚本已生成: $script_file"
}

#---------------------------------------
# 函数：执行 RMAN 备份
#---------------------------------------

run_rman_backup() {
    local script_file="$1"

    log_info "开始执行 RMAN 备份..."

    # 构建 RMAN 命令
    local rman_cmd="rman target $RMAN_TARGET"
    if [[ -n "$RMAN_CATALOG" ]]; then
        rman_cmd="$rman_cmd catalog $RMAN_CATALOG"
    fi
    rman_cmd="$rman_cmd cmdfile='$script_file'"

    # 执行备份
    if $rman_cmd >> "$LOG_FILE" 2>&1; then
        log_info "RMAN 备份执行成功"
        return $EXIT_SUCCESS
    else
        log_error "RMAN 备份执行失败，请检查日志: $LOG_FILE"
        return $EXIT_RMAN_ERROR
    fi
}

#---------------------------------------
# 函数：清理旧备份文件（文件系统层面）
#---------------------------------------

cleanup_old_backups() {
    log_info "开始清理超过 ${RETENTION_DAYS} 天的备份文件..."

    local cleaned=0

    # 清理全备份
    if [[ -d "$BACKUP_ROOT/full" ]]; then
        local count
        count=$(find "$BACKUP_ROOT/full" -name "*.bkp" -type f -mtime +"$RETENTION_DAYS" | wc -l)
        if [[ "$count" -gt 0 ]]; then
            find "$BACKUP_ROOT/full" -name "*.bkp" -type f -mtime +"$RETENTION_DAYS" -delete
            log_info "清理了 $count 个过期全备份文件"
            cleaned=$((cleaned + count))
        fi
    fi

    # 清理增量备份
    if [[ -d "$BACKUP_ROOT/incremental" ]]; then
        local count
        count=$(find "$BACKUP_ROOT/incremental" -name "*.bkp" -type f -mtime +"$RETENTION_DAYS" | wc -l)
        if [[ "$count" -gt 0 ]]; then
            find "$BACKUP_ROOT/incremental" -name "*.bkp" -type f -mtime +"$RETENTION_DAYS" -delete
            log_info "清理了 $count 个过期增量备份文件"
            cleaned=$((cleaned + count))
        fi
    fi

    # 清理归档日志备份
    if [[ -d "$BACKUP_ROOT/archivelog" ]]; then
        local count
        count=$(find "$BACKUP_ROOT/archivelog" -name "*.bkp" -type f -mtime +"$RETENTION_DAYS" | wc -l)
        if [[ "$count" -gt 0 ]]; then
            find "$BACKUP_ROOT/archivelog" -name "*.bkp" -type f -mtime +"$RETENTION_DAYS" -delete
            log_info "清理了 $count 个过期归档日志备份文件"
            cleaned=$((cleaned + count))
        fi
    fi

    # 清理控制文件自动备份
    if [[ -d "$BACKUP_ROOT/controlfile" ]]; then
        local count
        count=$(find "$BACKUP_ROOT/controlfile" -type f -mtime +"$RETENTION_DAYS" | wc -l)
        if [[ "$count" -gt 0 ]]; then
            find "$BACKUP_ROOT/controlfile" -type f -mtime +"$RETENTION_DAYS" -delete
            log_info "清理了 $count 个过期控制文件备份"
            cleaned=$((cleaned + count))
        fi
    fi

    if [[ "$cleaned" -gt 0 ]]; then
        log_info "文件系统清理完成，共清理 $cleaned 个过期文件"
    else
        log_info "没有需要清理的过期备份文件"
    fi
}

#---------------------------------------
# 函数：清理旧日志
#---------------------------------------

cleanup_old_logs() {
    if [[ -d "$LOG_DIR" ]]; then
        local count
        count=$(find "$LOG_DIR" -name "backup_*.log" -type f -mtime +"$LOG_RETENTION_DAYS" | wc -l)
        if [[ "$count" -gt 0 ]]; then
            find "$LOG_DIR" -name "backup_*.log" -type f -mtime +"$LOG_RETENTION_DAYS" -delete
            log_info "清理了 $count 个过期日志文件"
        fi
    fi
}

#---------------------------------------
# 函数：备份摘要
#---------------------------------------

print_summary() {
    log_info "========================================="
    log_info "备份任务完成"

    # 磁盘使用情况
    if [[ -d "$BACKUP_ROOT" ]]; then
        local usage
        usage=$(du -sh "$BACKUP_ROOT" 2>/dev/null | cut -f1)
        log_info "备份目录总大小: $usage"
    fi

    # 最近备份文件
    log_info "最近备份文件:"
    find "$BACKUP_ROOT" -name "*.bkp" -type f -mtime -1 -ls 2>/dev/null | \
        awk '{printf "  %s  %s  %s\n", $7, $8, $11}' | tail -20 | tee -a "$LOG_FILE"

    log_info "日志文件: $LOG_FILE"
    log_info "========================================="
}

#---------------------------------------
# 函数：发送通知（可选）
#---------------------------------------

send_notification() {
    local status="$1"

    # 如果配置了邮件通知
    if [[ -n "${NOTIFY_EMAIL:-}" ]]; then
        {
            echo "Subject: [Oracle Backup] ${status} - ${ORACLE_SID} - ${BACKUP_TYPE}"
            echo "Content-Type: text/plain; charset=utf-8"
            echo ""
            cat "$LOG_FILE"
        } | /usr/sbin/sendmail -t "${NOTIFY_EMAIL}" 2>/dev/null || true
    fi

    # 如果配置了 Webhook 通知（钉钉/企业微信等）
    if [[ -n "${NOTIFY_WEBHOOK:-}" ]]; then
        local msg
        msg="Oracle备份${status} - 实例:${ORACLE_SID} - 类型:${BACKUP_TYPE} - 时间:$(date '+%Y-%m-%d %H:%M:%S')"
        curl -s -X POST "${NOTIFY_WEBHOOK}" \
            -H "Content-Type: application/json" \
            -d "{\"msgtype\":\"text\",\"text\":{\"content\":\"${msg}\"}}" \
            /dev/null 2>&1 || true
    fi
}

#---------------------------------------
# 主流程
#---------------------------------------

main() {
    local exit_code=$EXIT_SUCCESS

    # 初始化
    init_check

    # 检查数据库状态
    check_db_status

    # 生成 RMAN 脚本
    generate_rman_script "$RMAN_SCRIPT" "$BACKUP_TYPE"

    # 执行备份
    if ! run_rman_backup "$RMAN_SCRIPT"; then
        exit_code=$EXIT_RMAN_ERROR
    fi

    # 清理临时脚本
    rm -f "$RMAN_SCRIPT"

    # 清理过期备份
    if [[ "$exit_code" -eq $EXIT_SUCCESS ]]; then
        cleanup_old_backups
        cleanup_old_logs
    fi

    # 打印摘要
    print_summary

    # 发送通知
    if [[ "$exit_code" -eq $EXIT_SUCCESS ]]; then
        send_notification "成功"
    else
        send_notification "失败"
    fi

    log_info "脚本退出，状态码: $exit_code"
    exit $exit_code
}

# 执行主函数
main
