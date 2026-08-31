namespace ShowtimeBackend.Common;

// ============================================================
// 业务状态/类型枚举
// 枚举成员名称与数据库 CHECK 约束中的取值严格一致（如
// SHOW.STATUS、CK_SHOW_SESSION_STATUS、CHK_T_ORDER_STATUS 等），
// 通过 JsonStringEnumConverter 序列化为字符串后即与 DB 值对齐。
// 枚举进入 OpenAPI schema 后，前端 openapi-typescript 生成的
// union 类型可在编译期拦截非法取值。
// ============================================================

/// <summary>演出状态（SHOW.STATUS: DRAFT/PUBLISHED/UNPUBLISHED）</summary>
public enum ShowStatus
{
    DRAFT,
    PUBLISHED,
    UNPUBLISHED,
}

/// <summary>演出审核状态（SHOW.AUDIT_STATUS: PENDING/APPROVED/REJECTED）</summary>
public enum ShowAuditStatus
{
    PENDING,
    APPROVED,
    REJECTED,
}

/// <summary>场次状态（SHOW_SESSION.SESSION_STATUS）</summary>
public enum SessionStatus
{
    UPCOMING,
    PRESALE,
    ONSALE,
    SOLD_OUT,
    ENDED,
}

/// <summary>票价类型（PRICE_STRATEGY.PRICE_TYPE）</summary>
public enum PriceType
{
    EARLY_BIRD,
    PRESALE,
    STANDARD,
    VIP,
    MEMBER,
}

/// <summary>票价策略状态（PRICE_STRATEGY.STATUS）</summary>
public enum PriceStrategyStatus
{
    ENABLED,
    DISABLED,
}

/// <summary>订单状态（T_ORDER.ORDER_STATUS）</summary>
public enum OrderStatus
{
    PENDING_PAY,
    PAID,
    ISSUED,
    PART_REFUND,
    REFUNDED,
    CANCELLED,
}

/// <summary>订单类型（T_ORDER.ORDER_TYPE）</summary>
public enum OrderType
{
    NORMAL,
    SPLIT,
    MERGE,
    EXCHANGE,
}

/// <summary>订单明细状态（T_ORDER_ITEM.ITEM_STATUS）</summary>
public enum OrderItemStatus
{
    NORMAL,
    REFUNDING,
    REFUNDED,
    EXCHANGING,
    EXCHANGED,
}

/// <summary>支付状态（PAYMENT.PAY_STATUS）</summary>
public enum PaymentStatus
{
    PENDING,
    SUCCESS,
    FAIL,
    CLOSED,
}

/// <summary>支付渠道（PAYMENT.PAY_CHANNEL）</summary>
public enum PaymentChannel
{
    ALIPAY,
    WECHAT,
    UNIONPAY,
    BALANCE,
}

/// <summary>电子票状态（T_ETICKET.TICKET_STATUS）</summary>
public enum ETicketStatus
{
    UNUSED,
    REFUNDING,
    USED,
    REFUNDED,
    EXCHANGING,
    EXCHANGED,
}

/// <summary>退票类型（REFUND_REQUEST.REFUND_TYPE）</summary>
public enum RefundType
{
    FULL,
    PART,
}

/// <summary>退票审核状态（REFUND_REQUEST.APPROVE_STATUS）</summary>
public enum RefundApproveStatus
{
    PENDING,
    APPROVED,
    REJECTED,
}

/// <summary>退款处理状态（REFUND_REQUEST.REFUND_STATUS）</summary>
public enum RefundStatus
{
    PENDING,
    PROCESSING,
    COMPLETED,
    FAILED,
}

/// <summary>改签审核状态（EXCHANGE_REQUEST.APPROVE_STATUS）</summary>
public enum ExchangeApproveStatus
{
    PENDING,
    APPROVED,
    REJECTED,
}

/// <summary>改签执行状态（EXCHANGE_REQUEST.EXCHANGE_STATUS）</summary>
public enum ExchangeStatus
{
    PENDING,
    PROCESSING,
    COMPLETED,
    FAILED,
}
