//namespace ShowtimeBackend.Entities.Session // 由base派生出session类管理演出场次相关数据

using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.ShowSessions
{
	/// <summary>
	/// 演出信息实体
	/// </summary>
	public class Show : AuditableEntity
	{
		/// <summary>
		/// 演出唯一标识，主键，自增
		/// </summary>
		public long ShowId { get; set; }

		/// <summary>
		/// 演出名称
		/// </summary>
		public string ShowName { get; set; } = string.Empty;

		/// <summary>
		/// 所属分类 ID（外键）
		/// </summary>
		public long CategoryId { get; set; }

		/// <summary>
		/// 演出简介
		/// </summary>
		public string? Description { get; set; }

		/// <summary>
		/// 演出时长（分钟）
		/// </summary>
		public int? DurationMinutes { get; set; }

		/// <summary>
		/// 海报图片地址
		/// </summary>
		public string? PosterUrl { get; set; }

		/// <summary>
		/// 发布状态：DRAFT-草稿，PUBLISHED-已上架，UNPUBLISHED-已下架
		/// </summary>
		public string Status { get; set; } = "DRAFT";

		/// <summary>
		/// 审核状态：PENDING-待审核，APPROVED-已通过，REJECTED-已驳回
		/// </summary>
		public string AuditStatus { get; set; } = "PENDING";

		/// <summary>
		/// 审核人
		/// </summary>
		public string? AuditBy { get; set; }

		/// <summary>
		/// 审核时间
		/// </summary>
		public DateTime? AuditTime { get; set; }

		#region 导航属性 (Navigation Property)

		/// <summary>
		/// 所属分类导航属性
		/// </summary>
		public virtual Category Category { get; set; } = null!;

        public virtual ICollection<ShowTag> ShowTags { get; set; } = new List<ShowTag>();

        #endregion
    }
}
