namespace Restatify.SupportChat.Services.Runtime;

public sealed record ChatNotificationPayload(string ConversationId, string Title, string Body);

public interface INotificationService
{
	Task ShowNewMessageAsync(ChatNotificationPayload payload, CancellationToken cancellationToken = default);
}

public sealed partial class NotificationService : INotificationService
{
	private readonly ILogger<NotificationService> _logger;

	public NotificationService(ILogger<NotificationService> logger)
	{
		_logger = logger;
	}

	public Task ShowNewMessageAsync(ChatNotificationPayload payload, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(payload.ConversationId))
		{
			return Task.CompletedTask;
		}

		_logger.LogInformation("Notification requested for conversation {ConversationId}.", payload.ConversationId);
		return ShowPlatformNotificationAsync(payload, cancellationToken);
	}

	private partial Task ShowPlatformNotificationAsync(ChatNotificationPayload payload, CancellationToken cancellationToken);
}
