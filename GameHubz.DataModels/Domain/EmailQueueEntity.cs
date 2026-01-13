using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
	public class EmailQueueEntity : BaseEntity
	{
		public EmailQueueEntity()
		{ }

		public string To { get; set; } = string.Empty;

		public string? Message { get; set; }

		public EmailQueueStatus Status { get; set; }

		public string? Error { get; set; }

		public string? Cc { get; set; }

		public string? Subject { get; set; }

		public bool IsMessageHtml { get; set; }
	}
}
