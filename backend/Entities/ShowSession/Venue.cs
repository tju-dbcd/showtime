//namespace ShowtimeBackend.Entities.Session // 由base派生出session类管理演出场次相关数据

using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.ShowSessions
{
	/// <summary>
	/// 场馆信息实体
	/// </summary>
	public class Venue : AuditableEntity
	{
		/// <summary>
		/// 场馆唯一标识，主键，自增
		/// </summary>
		public long VenueId { get; set; }

		/// <summary>
		/// 场馆名称
		/// </summary>
		public string VenueName { get; set; } = string.Empty;

		/// <summary>
		/// 场馆地址
		/// </summary>
		public string? Address { get; set; }

		/// <summary>
		/// 联系电话
		/// </summary>
		public string? ContactPhone { get; set; }

		/// <summary>
		/// 状态：ENABLED-启用，DISABLED-停用
		/// </summary>
		public string Status { get; set; } = "ENABLED";

		/// <summary>
		/// 备注
		/// </summary>
		public string? Remark { get; set; }

	}
}
