#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace Restatify.SupportChat.Services.Runtime;

public sealed partial class NotificationService
{
	private const string MessageChannelId = "supportchat.messages";
	private const string MessageChannelName = "Support Chat Messages";

	private partial Task ShowPlatformNotificationAsync(ChatNotificationPayload payload, CancellationToken cancellationToken)
	{
		var context = global::Android.App.Application.Context;
		if (context is null)
		{
			return Task.CompletedTask;
		}

		EnsureMessageChannel(context);

		var packageName = context.PackageName;
		if (string.IsNullOrWhiteSpace(packageName))
		{
			return Task.CompletedTask;
		}

		var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(packageName);
		if (launchIntent is not null)
		{
			launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop | ActivityFlags.NewTask);
			launchIntent.PutExtra("conversationId", payload.ConversationId);
			launchIntent.PutExtra("args", $"conversationId={Uri.EscapeDataString(payload.ConversationId)}");
		}

		var pendingIntentFlags = PendingIntentFlags.UpdateCurrent;
		if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
		{
			pendingIntentFlags |= PendingIntentFlags.Immutable;
		}

		var pendingIntent = launchIntent is null
			? null
			: PendingIntent.GetActivity(context, payload.ConversationId.GetHashCode(), launchIntent, pendingIntentFlags);

		var builder = new NotificationCompat.Builder(context, MessageChannelId);
		builder.SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo);
		builder.SetContentTitle(payload.Title);
		builder.SetContentText(payload.Body);
		builder.SetPriority(NotificationCompat.PriorityHigh);
		builder.SetAutoCancel(true);
		builder.SetVisibility((int)NotificationCompat.VisibilityPrivate);
		builder.SetCategory(NotificationCompat.CategoryMessage);

		if (pendingIntent is not null)
		{
			builder.SetContentIntent(pendingIntent);
		}

		var notification = builder.Build();
		if (notification is null)
		{
			return Task.CompletedTask;
		}

		var notificationManager = NotificationManagerCompat.From(context);
		if (notificationManager is null)
		{
			return Task.CompletedTask;
		}

		notificationManager.Notify(payload.ConversationId, payload.ConversationId.GetHashCode(), notification);
		return Task.CompletedTask;
	}

	private static void EnsureMessageChannel(Context context)
	{
		if (Build.VERSION.SdkInt < BuildVersionCodes.O)
		{
			return;
		}

		if (context.GetSystemService(Context.NotificationService) is not NotificationManager manager)
		{
			return;
		}

		if (manager.GetNotificationChannel(MessageChannelId) is not null)
		{
			return;
		}

		var channel = new NotificationChannel(MessageChannelId, MessageChannelName, NotificationImportance.High)
		{
			Description = "Support chat notifications",
		};

		manager.CreateNotificationChannel(channel);
	}
}
#endif
