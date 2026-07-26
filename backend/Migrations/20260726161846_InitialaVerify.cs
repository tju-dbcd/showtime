using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShowtimeBackend.Migrations
{
    /// <inheritdoc />
    public partial class InitialaVerify : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "APP_OWNER");

            migrationBuilder.CreateTable(
                name: "CATEGORY",
                schema: "APP_OWNER",
                columns: table => new
                {
                    CATEGORY_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    CATEGORY_NAME = table.Column<string>(type: "VARCHAR2(50)", maxLength: 50, nullable: false),
                    PARENT_ID = table.Column<long>(type: "NUMBER(19)", nullable: true),
                    SORT_ORDER = table.Column<short>(type: "NUMBER(5)", nullable: true, defaultValue: (short)0),
                    STATUS = table.Column<bool>(type: "NUMBER(1)", nullable: false, defaultValue: 1),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CATEGORY", x => x.CATEGORY_ID);
                    table.ForeignKey(
                        name: "FK_CATEGORY_CATEGORY_PARENT_ID",
                        column: x => x.PARENT_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "CATEGORY",
                        principalColumn: "CATEGORY_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ORG_STRUCTURE",
                schema: "APP_OWNER",
                columns: table => new
                {
                    ORG_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    PARENT_ID = table.Column<long>(type: "NUMBER(19)", nullable: true),
                    ORG_CODE = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: false),
                    ORG_NAME = table.Column<string>(type: "VARCHAR2(100)", unicode: false, maxLength: 100, nullable: false),
                    ORG_TYPE = table.Column<string>(type: "VARCHAR2(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "DEPT"),
                    SORT_ORDER = table.Column<short>(type: "NUMBER(5)", nullable: false, defaultValue: (short)0),
                    STATUS = table.Column<bool>(type: "NUMBER(1)", nullable: false, defaultValue: true),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ORG_STRUCTURE", x => x.ORG_ID);
                    table.CheckConstraint("CK_ORG_STRUCTURE_STATUS", "STATUS IN (0, 1)");
                    table.CheckConstraint("CK_ORG_STRUCTURE_TYPE", "ORG_TYPE IN ('COMPANY', 'DEPT', 'TEAM', 'OTHER')");
                    table.ForeignKey(
                        name: "FK_ORG_STRUCTURE_PARENT",
                        column: x => x.PARENT_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "ORG_STRUCTURE",
                        principalColumn: "ORG_ID");
                });

            migrationBuilder.CreateTable(
                name: "PERMISSION",
                schema: "APP_OWNER",
                columns: table => new
                {
                    PERMISSION_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    PERM_CODE = table.Column<string>(type: "VARCHAR2(100)", unicode: false, maxLength: 100, nullable: false),
                    PERM_NAME = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: false),
                    RESOURCE_TYPE = table.Column<string>(type: "VARCHAR2(20)", unicode: false, maxLength: 20, nullable: false),
                    PARENT_ID = table.Column<long>(type: "NUMBER(19)", nullable: true),
                    SORT_ORDER = table.Column<short>(type: "NUMBER(5)", nullable: false, defaultValue: (short)0),
                    STATUS = table.Column<bool>(type: "NUMBER(1)", nullable: false, defaultValue: true),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PERMISSION", x => x.PERMISSION_ID);
                    table.CheckConstraint("CK_PERMISSION_STATUS", "STATUS IN (0, 1)");
                    table.CheckConstraint("CK_PERMISSION_TYPE", "RESOURCE_TYPE IN ('MENU', 'BUTTON', 'API', 'DATA')");
                    table.ForeignKey(
                        name: "FK_PERMISSION_PARENT",
                        column: x => x.PARENT_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "PERMISSION",
                        principalColumn: "PERMISSION_ID");
                });

            migrationBuilder.CreateTable(
                name: "ROLE",
                schema: "APP_OWNER",
                columns: table => new
                {
                    ROLE_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    ROLE_CODE = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: false),
                    ROLE_NAME = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: false),
                    ROLE_DESC = table.Column<string>(type: "VARCHAR2(200)", unicode: false, maxLength: 200, nullable: true),
                    STATUS = table.Column<bool>(type: "NUMBER(1)", nullable: false, defaultValue: true),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ROLE", x => x.ROLE_ID);
                    table.CheckConstraint("CK_ROLE_STATUS", "STATUS IN (0, 1)");
                });

            migrationBuilder.CreateTable(
                name: "TAG",
                schema: "APP_OWNER",
                columns: table => new
                {
                    TAG_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    TAG_NAME = table.Column<string>(type: "VARCHAR2(50)", maxLength: 50, nullable: false),
                    COLOR = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: true),
                    STATUS = table.Column<bool>(type: "NUMBER(1)", nullable: false, defaultValue: 1),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TAG", x => x.TAG_ID);
                });

            migrationBuilder.CreateTable(
                name: "VENUE",
                schema: "APP_OWNER",
                columns: table => new
                {
                    VENUE_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    VENUE_NAME = table.Column<string>(type: "VARCHAR2(100)", maxLength: 100, nullable: false),
                    ADDRESS = table.Column<string>(type: "VARCHAR2(200)", maxLength: 200, nullable: true),
                    CONTACT_PHONE = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: true),
                    STATUS = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: false, defaultValue: "ENABLED"),
                    REMARK = table.Column<string>(type: "VARCHAR2(255)", maxLength: 255, nullable: true),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VENUE", x => x.VENUE_ID);
                });

            migrationBuilder.CreateTable(
                name: "SHOW",
                schema: "APP_OWNER",
                columns: table => new
                {
                    SHOW_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    SHOW_NAME = table.Column<string>(type: "VARCHAR2(200)", maxLength: 200, nullable: false),
                    CATEGORY_ID = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    DESCRIPTION = table.Column<string>(type: "VARCHAR2(2000)", maxLength: 2000, nullable: true),
                    DURATION_MINUTES = table.Column<short>(type: "NUMBER(5)", nullable: true),
                    POSTER_URL = table.Column<string>(type: "VARCHAR2(500)", maxLength: 500, nullable: true),
                    STATUS = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: false, defaultValue: "DRAFT"),
                    AUDIT_STATUS = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: false, defaultValue: "PENDING"),
                    AUDIT_BY = table.Column<string>(type: "VARCHAR2(50)", maxLength: 50, nullable: true),
                    AUDIT_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: true),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHOW", x => x.SHOW_ID);
                    table.ForeignKey(
                        name: "FK_SHOW_CATEGORY_CATEGORY_ID",
                        column: x => x.CATEGORY_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "CATEGORY",
                        principalColumn: "CATEGORY_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SYS_USER",
                schema: "APP_OWNER",
                columns: table => new
                {
                    USER_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    USER_NAME = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: false),
                    PASSWORD_HASH = table.Column<string>(type: "VARCHAR2(255)", unicode: false, maxLength: 255, nullable: false),
                    NICKNAME = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    PHONE = table.Column<string>(type: "VARCHAR2(20)", unicode: false, maxLength: 20, nullable: false),
                    EMAIL = table.Column<string>(type: "VARCHAR2(100)", unicode: false, maxLength: 100, nullable: true),
                    ORG_ID = table.Column<long>(type: "NUMBER(19)", nullable: true),
                    USER_TYPE = table.Column<string>(type: "VARCHAR2(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "NORMAL"),
                    STATUS = table.Column<bool>(type: "NUMBER(1)", nullable: false, defaultValue: (byte)1),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SYS_USER", x => x.USER_ID);
                    table.CheckConstraint("CK_SYS_USER_STATUS", "STATUS IN (0, 1, 2)");
                    table.CheckConstraint("CK_SYS_USER_TYPE", "USER_TYPE IN ('NORMAL', 'MEMBER', 'VIP')");
                    table.ForeignKey(
                        name: "FK_SYS_USER_ORG",
                        column: x => x.ORG_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "ORG_STRUCTURE",
                        principalColumn: "ORG_ID");
                });

            migrationBuilder.CreateTable(
                name: "ROLE_PERMISSION",
                schema: "APP_OWNER",
                columns: table => new
                {
                    ROLE_PERM_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    ROLE_ID = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    PERMISSION_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ROLE_PERMISSION", x => x.ROLE_PERM_ID);
                    table.ForeignKey(
                        name: "FK_RP_PERMISSION",
                        column: x => x.PERMISSION_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "PERMISSION",
                        principalColumn: "PERMISSION_ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RP_ROLE",
                        column: x => x.ROLE_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "ROLE",
                        principalColumn: "ROLE_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MARKETING_CONTENT",
                schema: "APP_OWNER",
                columns: table => new
                {
                    CONTENT_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    SHOW_ID = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    CONTENT_TYPE = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: false, defaultValue: "NOTICE"),
                    TITLE = table.Column<string>(type: "VARCHAR2(200)", maxLength: 200, nullable: false),
                    CONTENT_TEXT = table.Column<string>(type: "CLOB", nullable: true),
                    IMAGE_URL = table.Column<string>(type: "VARCHAR2(500)", maxLength: 500, nullable: true),
                    SORT_ORDER = table.Column<short>(type: "NUMBER(5)", nullable: false, defaultValue: (short)0),
                    STATUS = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: false, defaultValue: "ENABLED"),
                    PUBLISH_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: true),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MARKETING_CONTENT", x => x.CONTENT_ID);
                    table.ForeignKey(
                        name: "FK_MARKETING_CONTENT_SHOW_SHOW_ID",
                        column: x => x.SHOW_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "SHOW",
                        principalColumn: "SHOW_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SHOW_SESSION",
                schema: "APP_OWNER",
                columns: table => new
                {
                    SESSION_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    SHOW_ID = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    SEAT_MAP_ID = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    START_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false),
                    END_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false),
                    SALE_START_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false),
                    SALE_END_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false),
                    SESSION_STATUS = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: false, defaultValue: "UPCOMING"),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHOW_SESSION", x => x.SESSION_ID);
                    table.ForeignKey(
                        name: "FK_SHOW_SESSION_SHOW_SHOW_ID",
                        column: x => x.SHOW_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "SHOW",
                        principalColumn: "SHOW_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SHOW_TAG",
                schema: "APP_OWNER",
                columns: table => new
                {
                    SHOW_TAG_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    SHOW_ID = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    TAG_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHOW_TAG", x => x.SHOW_TAG_ID);
                    table.ForeignKey(
                        name: "FK_SHOW_TAG_SHOW",
                        column: x => x.SHOW_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "SHOW",
                        principalColumn: "SHOW_ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SHOW_TAG_TAG",
                        column: x => x.TAG_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "TAG",
                        principalColumn: "TAG_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OPERATION_LOG",
                schema: "APP_OWNER",
                columns: table => new
                {
                    LOG_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    USER_ID = table.Column<long>(type: "NUMBER(19)", nullable: true),
                    USER_NAME = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    SHOW_ID = table.Column<long>(type: "NUMBER(19)", nullable: true),
                    OPERATION_MODULE = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: false),
                    OPERATION_TYPE = table.Column<string>(type: "VARCHAR2(30)", unicode: false, maxLength: 30, nullable: false),
                    REQUEST_URL = table.Column<string>(type: "VARCHAR2(500)", unicode: false, maxLength: 500, nullable: true),
                    REQUEST_PARAMS = table.Column<string>(type: "CLOB", unicode: false, nullable: true),
                    RESPONSE_RESULT = table.Column<string>(type: "CLOB", unicode: false, nullable: true),
                    IP_ADDRESS = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    USER_AGENT = table.Column<string>(type: "VARCHAR2(500)", unicode: false, maxLength: 500, nullable: true),
                    COST_TIME = table.Column<int>(type: "NUMBER(10)", nullable: true),
                    STATUS = table.Column<bool>(type: "NUMBER(1)", nullable: false, defaultValue: true),
                    ERROR_MSG = table.Column<string>(type: "VARCHAR2(500)", unicode: false, maxLength: 500, nullable: true),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OPERATION_LOG", x => x.LOG_ID);
                    table.CheckConstraint("CK_OP_LOG_STATUS", "STATUS IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_OP_LOG_USER",
                        column: x => x.USER_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "SYS_USER",
                        principalColumn: "USER_ID");
                });

            migrationBuilder.CreateTable(
                name: "USER_BLACKLIST",
                schema: "APP_OWNER",
                columns: table => new
                {
                    BLACKLIST_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    USER_ID = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    SHOW_ID = table.Column<long>(type: "NUMBER(19)", nullable: true),
                    RISK_TYPE = table.Column<string>(type: "VARCHAR2(30)", unicode: false, maxLength: 30, nullable: false),
                    RISK_SCORE = table.Column<short>(type: "NUMBER(5)", nullable: false, defaultValue: (short)0),
                    START_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    END_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: true),
                    IS_PERMANENT = table.Column<bool>(type: "NUMBER(1)", nullable: false, defaultValue: false),
                    REASON = table.Column<string>(type: "VARCHAR2(200)", unicode: false, maxLength: 200, nullable: true),
                    STATUS = table.Column<bool>(type: "NUMBER(1)", nullable: false, defaultValue: true),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_BLACKLIST", x => x.BLACKLIST_ID);
                    table.CheckConstraint("CK_BLACKLIST_PERMANENT", "IS_PERMANENT IN (0, 1)");
                    table.CheckConstraint("CK_BLACKLIST_RISK_SCORE", "RISK_SCORE BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_BLACKLIST_STATUS", "STATUS IN (0, 1)");
                    table.CheckConstraint("CK_BLACKLIST_TIME", "END_TIME IS NULL OR END_TIME >= START_TIME");
                    table.ForeignKey(
                        name: "FK_BLACKLIST_USER",
                        column: x => x.USER_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "SYS_USER",
                        principalColumn: "USER_ID");
                });

            migrationBuilder.CreateTable(
                name: "USER_REAL_NAME",
                schema: "APP_OWNER",
                columns: table => new
                {
                    REAL_NAME_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    USER_ID = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    REAL_NAME = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: false),
                    ID_CARD_NO = table.Column<string>(type: "VARCHAR2(255)", unicode: false, maxLength: 255, nullable: false),
                    IS_DEFAULT = table.Column<bool>(type: "NUMBER(1)", nullable: false, defaultValue: false),
                    IS_VERIFIED = table.Column<bool>(type: "NUMBER(1)", nullable: false, defaultValue: false),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_REAL_NAME", x => x.REAL_NAME_ID);
                    table.CheckConstraint("CK_REAL_NAME_DEFAULT", "IS_DEFAULT IN (0, 1)");
                    table.CheckConstraint("CK_REAL_NAME_VERIFIED", "IS_VERIFIED IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_REAL_NAME_USER",
                        column: x => x.USER_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "SYS_USER",
                        principalColumn: "USER_ID");
                });

            migrationBuilder.CreateTable(
                name: "USER_ROLE",
                schema: "APP_OWNER",
                columns: table => new
                {
                    USER_ROLE_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    USER_ID = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    ROLE_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_ROLE", x => x.USER_ROLE_ID);
                    table.ForeignKey(
                        name: "FK_USER_ROLE_ROLE",
                        column: x => x.ROLE_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "ROLE",
                        principalColumn: "ROLE_ID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_USER_ROLE_USER",
                        column: x => x.USER_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "SYS_USER",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "USER_SESSION",
                schema: "APP_OWNER",
                columns: table => new
                {
                    USER_SESSION_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    USER_ID = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    SESSION_TOKEN = table.Column<string>(type: "VARCHAR2(128)", unicode: false, maxLength: 128, nullable: false),
                    LOGIN_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    EXPIRE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false),
                    LOGOUT_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: true),
                    IP_ADDRESS = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    USER_AGENT = table.Column<string>(type: "VARCHAR2(500)", unicode: false, maxLength: 500, nullable: true),
                    RISK_FLAG = table.Column<bool>(type: "NUMBER(1)", nullable: false, defaultValue: false),
                    STATUS = table.Column<string>(type: "VARCHAR2(20)", unicode: false, maxLength: 20, nullable: false, defaultValue: "ACTIVE"),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USER_SESSION", x => x.USER_SESSION_ID);
                    table.CheckConstraint("CK_USER_SESSION_RISK", "RISK_FLAG IN (0, 1)");
                    table.CheckConstraint("CK_USER_SESSION_STATUS", "STATUS IN ('ACTIVE', 'EXPIRED', 'LOGOUT', 'LOCKED')");
                    table.ForeignKey(
                        name: "FK_USER_SESSION_USER",
                        column: x => x.USER_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "SYS_USER",
                        principalColumn: "USER_ID");
                });

            migrationBuilder.CreateTable(
                name: "PRICE_STRATEGY",
                schema: "APP_OWNER",
                columns: table => new
                {
                    PRICE_STRATEGY_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    SESSION_ID = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    SEAT_SECTION_ID = table.Column<long>(type: "NUMBER(19)", nullable: false),
                    STRATEGY_NAME = table.Column<string>(type: "VARCHAR2(100)", maxLength: 100, nullable: false),
                    PRICE_TYPE = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: false, defaultValue: "STANDARD"),
                    PRICE = table.Column<decimal>(type: "NUMBER(10,2)", precision: 10, scale: 2, nullable: false, defaultValue: 0.00m),
                    SALE_START_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false),
                    SALE_END_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false),
                    PRIORITY = table.Column<short>(type: "NUMBER(5)", nullable: false, defaultValue: (short)0),
                    QUOTA = table.Column<int>(type: "NUMBER(10)", nullable: true),
                    STATUS = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: false, defaultValue: "ENABLED"),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRICE_STRATEGY", x => x.PRICE_STRATEGY_ID);
                    table.ForeignKey(
                        name: "FK_PRICE_STRATEGY_SHOW_SESSION_SESSION_ID",
                        column: x => x.SESSION_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "SHOW_SESSION",
                        principalColumn: "SESSION_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PURCHASE_LIMIT",
                schema: "APP_OWNER",
                columns: table => new
                {
                    LIMIT_ID = table.Column<long>(type: "NUMBER(19)", nullable: false)
                        .Annotation("Oracle:Identity", "START WITH 1 INCREMENT BY 1"),
                    LIMIT_NAME = table.Column<string>(type: "VARCHAR2(100)", maxLength: 100, nullable: false),
                    SHOW_ID = table.Column<long>(type: "NUMBER(19)", nullable: true),
                    SESSION_ID = table.Column<long>(type: "NUMBER(19)", nullable: true),
                    CHANNEL = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: true),
                    USER_TYPE = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: true),
                    MAX_BUY_COUNT = table.Column<short>(type: "NUMBER(5)", nullable: false, defaultValue: (short)1),
                    LIMIT_TYPE = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: false, defaultValue: "TICKET"),
                    START_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: true),
                    END_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: true),
                    STATUS = table.Column<string>(type: "VARCHAR2(20)", maxLength: 20, nullable: false, defaultValue: "ENABLED"),
                    CREATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UPDATE_TIME = table.Column<DateTime>(type: "TIMESTAMP(6)", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CREATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true),
                    UPDATE_BY = table.Column<string>(type: "VARCHAR2(50)", unicode: false, maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_LIMIT", x => x.LIMIT_ID);
                    table.ForeignKey(
                        name: "FK_PURCHASE_LIMIT_SHOW_SESSION_SESSION_ID",
                        column: x => x.SESSION_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "SHOW_SESSION",
                        principalColumn: "SESSION_ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PURCHASE_LIMIT_SHOW_SHOW_ID",
                        column: x => x.SHOW_ID,
                        principalSchema: "APP_OWNER",
                        principalTable: "SHOW",
                        principalColumn: "SHOW_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CATEGORY_PARENT_ID",
                schema: "APP_OWNER",
                table: "CATEGORY",
                column: "PARENT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_MARKETING_CONTENT_SHOW_ID",
                schema: "APP_OWNER",
                table: "MARKETING_CONTENT",
                column: "SHOW_ID");

            migrationBuilder.CreateIndex(
                name: "IDX_OP_LOG_SHOW",
                schema: "APP_OWNER",
                table: "OPERATION_LOG",
                column: "SHOW_ID");

            migrationBuilder.CreateIndex(
                name: "IDX_OP_LOG_TIME",
                schema: "APP_OWNER",
                table: "OPERATION_LOG",
                column: "CREATE_TIME");

            migrationBuilder.CreateIndex(
                name: "IDX_OP_LOG_TYPE",
                schema: "APP_OWNER",
                table: "OPERATION_LOG",
                column: "OPERATION_TYPE");

            migrationBuilder.CreateIndex(
                name: "IDX_OP_LOG_USER",
                schema: "APP_OWNER",
                table: "OPERATION_LOG",
                column: "USER_ID");

            migrationBuilder.CreateIndex(
                name: "IDX_ORG_PARENT",
                schema: "APP_OWNER",
                table: "ORG_STRUCTURE",
                column: "PARENT_ID");

            migrationBuilder.CreateIndex(
                name: "UK_ORG_STRUCTURE_CODE",
                schema: "APP_OWNER",
                table: "ORG_STRUCTURE",
                column: "ORG_CODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IDX_PERMISSION_PARENT",
                schema: "APP_OWNER",
                table: "PERMISSION",
                column: "PARENT_ID");

            migrationBuilder.CreateIndex(
                name: "UK_PERMISSION_CODE",
                schema: "APP_OWNER",
                table: "PERMISSION",
                column: "PERM_CODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRICE_STRATEGY_SESSION_ID",
                schema: "APP_OWNER",
                table: "PRICE_STRATEGY",
                column: "SESSION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_LIMIT_SESSION_ID",
                schema: "APP_OWNER",
                table: "PURCHASE_LIMIT",
                column: "SESSION_ID");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_LIMIT_SHOW_ID",
                schema: "APP_OWNER",
                table: "PURCHASE_LIMIT",
                column: "SHOW_ID");

            migrationBuilder.CreateIndex(
                name: "UK_ROLE_CODE",
                schema: "APP_OWNER",
                table: "ROLE",
                column: "ROLE_CODE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UK_ROLE_NAME",
                schema: "APP_OWNER",
                table: "ROLE",
                column: "ROLE_NAME",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IDX_RP_PERMISSION",
                schema: "APP_OWNER",
                table: "ROLE_PERMISSION",
                column: "PERMISSION_ID");

            migrationBuilder.CreateIndex(
                name: "UK_ROLE_PERMISSION",
                schema: "APP_OWNER",
                table: "ROLE_PERMISSION",
                columns: new[] { "ROLE_ID", "PERMISSION_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHOW_CATEGORY_ID",
                schema: "APP_OWNER",
                table: "SHOW",
                column: "CATEGORY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_SHOW_SESSION_SHOW_ID",
                schema: "APP_OWNER",
                table: "SHOW_SESSION",
                column: "SHOW_ID");

            migrationBuilder.CreateIndex(
                name: "IX_SHOW_TAG_TAG_ID",
                schema: "APP_OWNER",
                table: "SHOW_TAG",
                column: "TAG_ID");

            migrationBuilder.CreateIndex(
                name: "UK_SHOW_TAG",
                schema: "APP_OWNER",
                table: "SHOW_TAG",
                columns: new[] { "SHOW_ID", "TAG_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IDX_SYS_USER_ORG",
                schema: "APP_OWNER",
                table: "SYS_USER",
                column: "ORG_ID");

            migrationBuilder.CreateIndex(
                name: "IDX_SYS_USER_TYPE",
                schema: "APP_OWNER",
                table: "SYS_USER",
                column: "USER_TYPE");

            migrationBuilder.CreateIndex(
                name: "UK_SYS_USER_EMAIL",
                schema: "APP_OWNER",
                table: "SYS_USER",
                column: "EMAIL",
                unique: true,
                filter: "\"EMAIL\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UK_SYS_USER_NAME",
                schema: "APP_OWNER",
                table: "SYS_USER",
                column: "USER_NAME",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UK_SYS_USER_PHONE",
                schema: "APP_OWNER",
                table: "SYS_USER",
                column: "PHONE",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IDX_BLACKLIST_SHOW",
                schema: "APP_OWNER",
                table: "USER_BLACKLIST",
                column: "SHOW_ID");

            migrationBuilder.CreateIndex(
                name: "IDX_BLACKLIST_STATUS",
                schema: "APP_OWNER",
                table: "USER_BLACKLIST",
                column: "STATUS");

            migrationBuilder.CreateIndex(
                name: "IDX_BLACKLIST_USER",
                schema: "APP_OWNER",
                table: "USER_BLACKLIST",
                column: "USER_ID");

            migrationBuilder.CreateIndex(
                name: "IDX_REAL_NAME_USER",
                schema: "APP_OWNER",
                table: "USER_REAL_NAME",
                column: "USER_ID");

            migrationBuilder.CreateIndex(
                name: "IDX_USER_ROLE_ROLE",
                schema: "APP_OWNER",
                table: "USER_ROLE",
                column: "ROLE_ID");

            migrationBuilder.CreateIndex(
                name: "UK_USER_ROLE",
                schema: "APP_OWNER",
                table: "USER_ROLE",
                columns: new[] { "USER_ID", "ROLE_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IDX_USER_SESSION_EXPIRE",
                schema: "APP_OWNER",
                table: "USER_SESSION",
                column: "EXPIRE_TIME");

            migrationBuilder.CreateIndex(
                name: "IDX_USER_SESSION_STATUS",
                schema: "APP_OWNER",
                table: "USER_SESSION",
                column: "STATUS");

            migrationBuilder.CreateIndex(
                name: "IDX_USER_SESSION_USER",
                schema: "APP_OWNER",
                table: "USER_SESSION",
                column: "USER_ID");

            migrationBuilder.CreateIndex(
                name: "UK_USER_SESSION_TOKEN",
                schema: "APP_OWNER",
                table: "USER_SESSION",
                column: "SESSION_TOKEN",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MARKETING_CONTENT",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "OPERATION_LOG",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "PRICE_STRATEGY",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "PURCHASE_LIMIT",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "ROLE_PERMISSION",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "SHOW_TAG",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "USER_BLACKLIST",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "USER_REAL_NAME",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "USER_ROLE",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "USER_SESSION",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "VENUE",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "SHOW_SESSION",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "PERMISSION",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "TAG",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "ROLE",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "SYS_USER",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "SHOW",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "ORG_STRUCTURE",
                schema: "APP_OWNER");

            migrationBuilder.DropTable(
                name: "CATEGORY",
                schema: "APP_OWNER");
        }
    }
}
