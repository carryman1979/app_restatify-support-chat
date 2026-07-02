#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;

namespace Restatify.SupportChat.Platforms.Android;

[Service(Exported = false, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeDataSync)]
public sealed class SupportChatForegroundService : Service
{
	public const string ActionStart = "tech.restatify.supportchat.runtime.START";
	public const string ActionStop = "tech.restatify.supportchat.runtime.STOP";
	public const string ExtraConversationId = "conversationId";

	private const int ForegroundNotificationId = 99320;
	private const string ForegroundChannelId = "supportchat.runtime";
	private const string ForegroundChannelName = "Support Chat Runtime";

	public override IBinder? OnBind(Intent? intent)
	{
		return null;
	}

	public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
	{
		var action = intent?.Action;
		if (string.Equals(action, ActionStop, StringComparison.Ordinal))
		{
			StopForeground(StopForegroundFlags.Remove);
			StopSelf();
			return StartCommandResult.NotSticky;
		}

		EnsureChannel();
		var notification = BuildForegroundNotification(intent?.GetStringExtra(ExtraConversationId));
		StartForeground(ForegroundNotificationId, notification);
		return StartCommandResult.Sticky;
	}

	private Notification BuildForegroundNotification(string? conversationId)
	{
		var packageName = PackageName;
		var launchIntent = string.IsNullOrWhiteSpace(packageName)
			? null
			: PackageManager?.GetLaunchIntentForPackage(packageName);
		PendingIntent? pendingIntent = null;

		if (launchIntent is not null)
		{
			launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop | ActivityFlags.NewTask);
			if (!string.IsNullOrWhiteSpace(conversationId))
			{
				launchIntent.PutExtra(ExtraConversationId, conversationId);
				launchIntent.PutExtra("args", $"conversationId={Uri.EscapeDataString(conversationId)}");
			}

			var pendingFlags = PendingIntentFlags.UpdateCurrent;
			if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
			{
				pendingFlags |= PendingIntentFlags.Immutable;
			}

			pendingIntent = PendingIntent.GetActivity(this, 9011, launchIntent, pendingFlags);
		}

		var builder = new Notification.Builder(this, ForegroundChannelId)
			.SetContentTitle("Restatify Support Chat")
			.SetContentText("Background runtime active")
			.SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
			.SetOngoing(true);

		if (pendingIntent is not null)
		{
			builder.SetContentIntent(pendingIntent);
		}

		return builder.Build();
	}

	private void EnsureChannel()
	{
		if (Build.VERSION.SdkInt < BuildVersionCodes.O)
		{
			return;
		}

		if (GetSystemService(NotificationService) is not NotificationManager manager)
		{
			return;
		}

		if (manager.GetNotificationChannel(ForegroundChannelId) is not null)
		{
			return;
		}

		var channel = new NotificationChannel(ForegroundChannelId, ForegroundChannelName, NotificationImportance.Low)
		{
			Description = "Runs support chat in background",
		};

		manager.CreateNotificationChannel(channel);
	}
}
#endif
