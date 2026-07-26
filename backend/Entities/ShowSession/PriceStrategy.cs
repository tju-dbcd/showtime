//namespace ShowtimeBackend.Entities.Session // 由base派生出session类管理演出场次相关数据

using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities
{
	/// <summary>
	/// 票价策略实体
	/// </summary>
	public class PriceStrategy : AuditableEntity
	{
		/// <summary>
		/// 票价策略唯一标识，主键，自增
		/// </summary>
		public long PriceStrategyId { get; set; }

		/// <summary>
		/// 所属场次 ID（外键）
		/// </summary>
		public long SessionId { get; set; }

		/// <summary>
		/// 适用座位区域 ID（外键）
		/// </summary>
		public long SeatSectionId { get; set; }

		/// <summary>
		/// 策略名称（如“早鸟票”、“正常票”）
		/// </summary>
		public string StrategyName { get; set; } = string.Empty;

		/// <summary>
		/// 价格类型：EARLY_BIRD-早鸟，PRESALE-预售，STANDARD-普通，VIP-会员等
		/// </summary>
		public string PriceType { get; set; } = "STANDARD";

		/// <summary>
		/// 销售票价（元，精度 10,2）
		/// </summary>
		public decimal Price { get; set; } = 0.00m;

		/// <summary>
		/// 该票价开始销售时间
		/// </summary>
		public DateTime SaleStartTime { get; set; }

		/// <summary>
		/// 该票价结束销售时间
		/// </summary>
		public DateTime SaleEndTime { get; set; }

		/// <summary>
		/// 优先级，数值越小优先级越高（当多策略时间重叠时选择）
		/// </summary>
		public int Priority { get; set; } = 0;

		/// <summary>
		/// 限售数量，为 null 表示不限量
		/// </summary>
		public int? Quota { get; set; }

		/// <summary>
		/// 状态：ENABLED-启用，DISABLED-停用
		/// </summary>
		public string Status { get; set; } = "ENABLED";

		#region 外键导航属性 (Navigation Properties)

		/// <summary>
		/// 所属场次导航属性
		/// </summary>
		public virtual ShowSession? Session { get; set; }

        /// <summary>
        /// TODO: 等其他小组合并 SeatSection 实体后解封
        /// 适用座位区域导航属性
        /// </summary>
        //public virtual SeatSection? SeatSection { get; set; }

		#endregion
	}
}
