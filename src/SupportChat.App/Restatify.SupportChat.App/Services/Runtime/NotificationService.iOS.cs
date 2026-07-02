#if IOS
using Foundation;
using UserNotifications;

namespace Restatify.SupportChat.Services.Runtime;

public sealed partial class NotificationService
{
	private const string ConversationIdKey = "conversation_id";
	private static readonly SupportChatNotificationCenterDelegate NotificationDelegate = new();
	private static bool _configured;

	private partial async Task ShowPlatformNotificationAsync(ChatNotificationPayload payload, CancellationToken cancellationToken)
	{
		EnsureNotificationCenterConfigured();

		var (granted, _) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
			UNAuthorizationOptions.Alert | UNAuthorizationOptions.Badge | UNAuthorizationOptions.Sound);

		if (!granted)
		{
			return;
		}

		var content = new UNMutableNotificationContent
		{
			Title = payload.Title,
			Body = payload.Body,
			Sound = UNNotificationSound.Default,
			UserInfo = NSDictionary<NSString, NSObject>.FromObjectAndKey(
				new NSString(payload.ConversationId),
				new NSString(ConversationIdKey)),
		};

		var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(0.25, false);
		var request = UNNotificationRequest.FromIdentifier(
			$"supportchat-{payload.ConversationId}",
			content,
			trigger);

		await UNUserNotificationCenter.Current.AddNotificationRequestAsync(request);
	}

	private static void EnsureNotificationCenterConfigured()
	{
		if (_configured)
		{
			return;
		}

		UNUserNotificationCenter.Current.Delegate = NotificationDelegate;
		_configured = true;
	}

	private sealed class SupportChatNotificationCenterDelegate : UNUserNotificationCenterDelegate
	{
		public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
		{
			try
			{
				if (response.Notification.Request.Content.UserInfo is NSDictionary userInfo
					&& userInfo[new NSString(ConversationIdKey)] is NSString conversationId
					&& !string.IsNullOrWhiteSpace(conversationId.ToString()))
				{
					App.QueueConversationActivation(conversationId.ToString());
				}
			}
			finally
			{
				completionHandler();
			}
		}
	}
}
#endif
