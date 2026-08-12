

# SHOWTIME 系统总体架构

## 1. 系统架构概览

SHOWTIME 是一个面向演出活动的票务管理平台，系统采用前后端分离架构。前端基于 **React + TypeScript/JavaScript + Ant Design** 实现用户购票端和后台管理端；后端采用 **ASP.NET Core 10 + C#** 提供 RESTful API，并通过 **EF Core** 访问 **Oracle 21c** 数据库。README 中明确将用户权限、演出信息、场次、座位、订单、支付核销等作为核心业务领域；OpenAPI 则进一步体现了 Auth、ShowSession、Seat Locks、Seat Maps、Seat Rules、Seats、Orders、Payments 等 API 模块。

```mermaid
flowchart TB

    %% =========================
    %% Client Layer
    %% =========================
    subgraph CLIENT["客户端 / 表现层"]
        USER["普通用户<br/>购票 / 查票 / 订单"]
        ADMIN["管理人员<br/>演出 / 场次 / 座位 / 订单管理"]

        WEB["React Web Application<br/>React + TypeScript / JavaScript"]
        UI["Ant Design<br/>UI Component Library"]

        USER --> WEB
        ADMIN --> WEB
        WEB --> UI
    end

    %% =========================
    %% API Layer
    %% =========================
    subgraph API["后端应用层"]
        ASP["ASP.NET Core 10<br/>C#"]

        AUTH["用户与权限模块<br/>Auth / RBAC"]
        SHOW["演出信息模块<br/>Show Management"]
        SESSION["场次管理模块<br/>Show Session"]
        SEAT["座位管理模块<br/>Seat Management"]
        LOCK["座位锁定模块<br/>Seat Lock"]
        ORDER["订单处理模块<br/>Orders"]
        PAYMENT["支付模块<br/>Payments"]
        TICKET["电子票务 / 核销"]
        AUDIT["日志 / 审计 / 风控"]
    end

    %% =========================
    %% Data Access Layer
    %% =========================
    subgraph DAL["数据访问层"]
        EF["EF Core<br/>ORM / Data Access"]
    end

    %% =========================
    %% Database Layer
    %% =========================
    subgraph DB["数据层"]
        ORACLE["Oracle Database 21c"]
    end

    %% Client -> API
    WEB -->|"HTTP / REST API<br/>JSON"| ASP

    %% API Modules
    ASP --> AUTH
    ASP --> SHOW
    ASP --> SESSION
    ASP --> SEAT
    ASP --> LOCK
    ASP --> ORDER
    ASP --> PAYMENT
    ASP --> TICKET
    ASP --> AUDIT

    %% Business -> DAL
    AUTH --> EF
    SHOW --> EF
    SESSION --> EF
    SEAT --> EF
    LOCK --> EF
    ORDER --> EF
    PAYMENT --> EF
    TICKET --> EF
    AUDIT --> EF

    %% DAL -> DB
    EF -->|"SQL / ORM"| ORACLE
```

# 2. 技术栈架构

项目的核心技术栈可以分为四层：前端表现层、后端应用层、ORM 数据访问层和数据库层。

```mermaid
flowchart TB

    A["SHOWTIME 演出活动票务管理平台"]

    subgraph FRONT["前端"]
        R["React"]
        TS["TypeScript / JavaScript"]
        ANT["Ant Design"]
    end

    subgraph BACK["后端"]
        ASP["ASP.NET Core 10"]
        CS["C#"]
    end

    subgraph DATA_ACCESS["数据访问"]
        EF["Entity Framework Core"]
    end

    subgraph DATABASE["数据库"]
        ORACLE["Oracle 21c"]
    end

    A --> FRONT
    A --> BACK
    A --> DATA_ACCESS
    A --> DATABASE

    R --> TS
    TS --> ANT

    FRONT -->|"HTTP / REST API"| ASP
    ASP --> CS
    CS --> EF
    EF --> ORACLE
```

技术栈说明

| 层级     | 技术                    | 主要职责                 |
| -------- | ----------------------- | ------------------------ |
| 前端     | React                   | Web 页面与交互           |
| 前端语言 | TypeScript / JavaScript | 前端业务逻辑             |
| UI       | Ant Design              | 管理后台及业务组件       |
| 后端     | ASP.NET Core 10         | REST API、业务逻辑       |
| 后端语言 | C#                      | 后端业务实现             |
| ORM      | EF Core                 | 对象关系映射、数据库访问 |
| 数据库   | Oracle 21c              | 业务数据持久化           |



# 3. 后端模块划分

OpenAPI 中已经存在比较清晰的模块边界，例如 `Auth`、`ShowSessionClient`、`AdminShowSession`、`Seat Locks`、座位地图、座位规则、Seats、Orders 和 Payments 等

```mermaid
flowchart TB

    API["ASP.NET Core 10<br/>ShowtimeBackend"]

    API --> AUTH
    API --> SHOW
    API --> SESSION
    API --> SEAT
    API --> ORDER
    API --> PAYMENT
    API --> SUPPORT

    subgraph AUTH["① 用户与权限"]
        A1["注册 / 登录"]
        A2["JWT 身份认证"]
        A3["角色 / 权限"]
        A4["会话安全"]
    end

    subgraph SHOW["② 演出信息"]
        B1["演出信息"]
        B2["分类 / 标签"]
        B3["审核发布"]
        B4["推荐 / 热度"]
    end

    subgraph SESSION["③ 场次管理"]
        C1["场次创建"]
        C2["场次查询"]
        C3["动态调价"]
        C4["状态管理"]
        C5["限购策略"]
    end

    subgraph SEAT["④ 座位管理"]
        D1["Seat Map"]
        D2["Seat Section"]
        D3["Seat"]
        D4["Seat Rule"]
        D5["Seat Lock"]
    end

    subgraph ORDER["⑤ 订单处理"]
        E1["订单创建"]
        E2["订单生命周期"]
        E3["拆单 / 合并"]
        E4["退票 / 改签"]
        E5["电子票"]
    end

    subgraph PAYMENT["⑥ 支付"]
        F1["支付订单"]
        F2["支付状态"]
        F3["支付结果"]
        F4["核销"]
    end

    subgraph SUPPORT["⑦ 公共能力"]
        G1["统一 API Response"]
        G2["异常处理"]
        G3["JWT Bearer"]
        G4["日志 / 审计"]
    end
```

# 4. 业务模块关系

从业务角度来看，整个系统可以进一步抽象成下面的关系：

```mermaid
flowchart LR

    USER["用户"]

    AUTH["用户与权限"]
    SHOW["演出"]
    SESSION["场次"]
    SEATMAP["座位图"]
    SEAT["座位"]
    LOCK["座位锁定"]
    ORDER["订单"]
    PAYMENT["支付"]
    TICKET["电子票 / 核销"]

    USER --> AUTH
    AUTH --> SHOW

    SHOW --> SESSION
    SESSION --> SEATMAP
    SEATMAP --> SEAT

    SESSION --> LOCK
    SEAT --> LOCK

    LOCK --> ORDER
    SESSION --> ORDER
    SEAT --> ORDER

    ORDER --> PAYMENT
    PAYMENT --> TICKET

    ORDER -.->|"退票 / 改签"| SESSION
    ORDER -.->|"订单状态"| TICKET
```

这个关系尤其适合在架构图中突出**票务核心链路**：

> **用户 → 演出 → 场次 → 座位 → 锁座 → 订单 → 支付 → 电子票 / 核销**

其中 OpenAPI 已经明确存在场次查询、场次创建、调价、状态更新以及座位锁定/释放接口。例如客户端可以根据 `showId` 查询场次，管理员可以创建场次和配置价格策略，同时座位锁定模块提供锁定和释放接口。

# 5. 座位管理模块内部架构

座位部分是整个票务系统比较重要的模块，建议单独表现。

OpenAPI 中目前已经明确区分了：

- Seat Maps
- Seat Rules
- Seat Sections
- Seats
- Seat Locks

这些并不是一个简单的“座位表”，而是具有层次关系的座位管理体系

```mermaid
flowchart TB

    VENUE["场馆 / Venue"]

    MAP["Seat Map<br/>座位图"]

    SECTION["Seat Section<br/>座位区域"]

    SEAT["Seat<br/>具体座位"]

    RULE["Seat Rule<br/>座位规则"]

    LOCK["Seat Lock<br/>座位锁定"]

    SESSION["Show Session<br/>演出场次"]

    VENUE --> MAP
    MAP --> SECTION
    SECTION --> SEAT

    RULE --> SECTION
    RULE --> SEAT

    SESSION --> LOCK
    SEAT --> LOCK

    LOCK --> ORDER["Order"]
```

其中 API 已经体现了 `seatMap → seatSection → seat` 的管理关系，例如存在针对 Seat Map 的 CRUD 操作，以及通过 `seatSectionId` 查询座位的接口。

# 6. API 层架构

因此 API 层可以抽象成：

```mermaid
flowchart LR

    CLIENT["React Client"]

    API["ShowtimeBackend API<br/>ASP.NET Core 10"]

    AUTH["/api/auth/*"]

    CLIENT_API["Client APIs"]
    ADMIN_API["Admin APIs"]

    SESSION_API["Session APIs"]
    SEAT_API["Seat APIs"]
    ORDER_API["Order APIs"]
    PAYMENT_API["Payment APIs"]

    CLIENT --> API

    API --> AUTH
    API --> CLIENT_API
    API --> ADMIN_API
    API --> SESSION_API
    API --> SEAT_API
    API --> ORDER_API
    API --> PAYMENT_API
```

# 7. 部署形态

```mermaid
flowchart TB

    subgraph CLIENT["用户侧"]
        BROWSER["Web Browser"]
    end

    subgraph FRONT_SERVER["前端部署"]
        STATIC["React Build<br/>静态资源"]
    end

    subgraph APP_SERVER["应用服务器"]
        BACKEND["ShowtimeBackend<br/>ASP.NET Core 10"]
    end

    subgraph DATABASE_SERVER["数据库服务器"]
        ORACLE["Oracle 21c"]
    end

    BROWSER -->|"HTTPS"| STATIC
    BROWSER -->|"REST API / JSON"| BACKEND

    BACKEND -->|"EF Core"| ORACLE
```

(目前提供的文件没有明确说明 Nginx、Docker、Redis、消息队列、对象存储、Kubernetes、负载均衡等基础设施，因此架构图中不应该直接把这些技术画进去。)

# 8. 最终总架构图

```mermaid
flowchart TB

    %% ========== 用户层 ==========
    subgraph USER_LAYER["用户层"]
        CUSTOMER["普通用户"]
        OPERATOR["运营 / 管理人员"]
    end

    %% ========== 前端 ==========
    subgraph FRONTEND["前端表现层"]
        REACT["React Web"]
        TS["TypeScript / JavaScript"]
        ANT["Ant Design"]
    end

    %% ========== 后端 ==========
    subgraph BACKEND["后端应用层 · ASP.NET Core 10"]
        
        AUTH["用户与权限"]
        SHOW["演出信息"]
        SESSION["场次管理"]

        subgraph TICKETING["核心票务域"]
            SEATMAP["座位图"]
            SECTION["座位区域"]
            SEAT["座位"]
            LOCK["座位锁定"]
        end

        ORDER["订单处理"]
        PAYMENT["支付 / 核销"]

        COMMON["公共能力<br/>JWT / 异常 / 日志 / 审计"]
    end

    %% ========== 数据访问 ==========
    subgraph DATA["数据访问层"]
        EF["Entity Framework Core"]
    end

    %% ========== 数据库 ==========
    subgraph DATABASE["数据层"]
        ORACLE["Oracle 21c"]
    end

    %% ========== 用户到前端 ==========
    CUSTOMER --> REACT
    OPERATOR --> REACT

    REACT --> TS
    TS --> ANT

    %% ========== 前端到后端 ==========
    REACT -->|"HTTP / REST / JSON"| AUTH
    REACT -->|"HTTP / REST / JSON"| SHOW
    REACT -->|"HTTP / REST / JSON"| SESSION
    REACT -->|"HTTP / REST / JSON"| SEATMAP
    REACT -->|"HTTP / REST / JSON"| ORDER
    REACT -->|"HTTP / REST / JSON"| PAYMENT

    %% ========== 业务关系 ==========
    SHOW --> SESSION
    SESSION --> SEATMAP
    SEATMAP --> SECTION
    SECTION --> SEAT
    SESSION --> LOCK
    SEAT --> LOCK
    LOCK --> ORDER
    SESSION --> ORDER
    ORDER --> PAYMENT

    %% ========== 公共能力 ==========
    COMMON -.-> AUTH
    COMMON -.-> SHOW
    COMMON -.-> SESSION
    COMMON -.-> ORDER

    %% ========== 数据访问 ==========
    AUTH --> EF
    SHOW --> EF
    SESSION --> EF
    SEATMAP --> EF
    SECTION --> EF
    SEAT --> EF
    LOCK --> EF
    ORDER --> EF
    PAYMENT --> EF

    EF -->|"SQL"| ORACLE
```

## 9. 架构设计总结

整个系统可以概括为：

          				     ┌─────────────────────┐
                             │      用户 / 管理员    │
                             └──────────┬──────────┘
                                        │
                                        │ HTTPS
                                        ▼
                             ┌─────────────────────┐
                             │    React Web 前端    │
                             │ React + TS/JS + AntD│
                             └──────────┬──────────┘
                                        │
                                  REST API / JSON
                                        │
                                        ▼
                  ┌─────────────────────────────────────────┐
                  │          ASP.NET Core 10 API            │
                  │             ShowtimeBackend             │
                  │                                         │
                  │  Auth     Show      Session             │
                  │    │        │          │                │
                  │    └────────┼──────────┘                │
                  │             │                           │
                  │     Seat Map / Section / Seat           │
                  │             │                           │
                  │          Seat Lock                      │
                  │             │                           │
                  │           Order                         │
                  │             │                           │
                  │        Payment / Ticket                 │
                  └──────────────────┬──────────────────────┘
                                     │
                                  EF Core
                                     │
                                     ▼
                           ┌───────────────────┐
                           │    Oracle 21c     │
                           │   业务数据存储      │
                           └───────────────────┘

***最终架构定位***

| 维度                 | 当前项目定位                                  |
| -------------------- | --------------------------------------------- |
| 架构模式             | **前后端分离**                                |
| 后端模式             | **模块化单体**                                |
| 前端                 | React + TS/JS + Ant Design                    |
| 后端                 | ASP.NET Core 10 + C#                          |
| API                  | RESTful API / JSON                            |
| 认证                 | JWT Bearer                                    |
| ORM                  | EF Core                                       |
| 数据库               | Oracle 21c                                    |
| 核心业务域           | 用户权限、演出、场次、座位、订单、支付        |
| 核心票务链路         | 场次 → 座位 → 锁座 → 订单 → 支付              |
| 当前可确认部署       | Web 前端 + ASP.NET Core API + Oracle          |
| 暂不应在架构图中虚构 | Redis、MQ、Nginx、Docker、K8s、API Gateway 等 |
