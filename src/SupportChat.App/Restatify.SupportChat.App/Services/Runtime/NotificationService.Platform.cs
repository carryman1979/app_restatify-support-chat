#if !ANDROID && !IOS
namespace Restatify.SupportChat.Services.Runtime;

public sealed partial class NotificationService
{
	private partial Task ShowPlatformNotificationAsync(ChatNotificationPayload payload, CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}

#endif
