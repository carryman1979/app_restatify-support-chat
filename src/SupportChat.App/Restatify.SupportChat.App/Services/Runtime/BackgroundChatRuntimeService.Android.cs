#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Restatify.SupportChat.Platforms.Android;

namespace Restatify.SupportChat.Services.Runtime;

public sealed partial class BackgroundChatRuntimeService
{
	private partial Task StartPlatformRuntimeAsync(ChatRuntimeSession session, CancellationToken cancellationToken)
	{
		var context = global::Android.App.Application.Context;
		if (context is null)
		{
			return Task.CompletedTask;
		}

		var intent = new Intent(context, typeof(SupportChatForegroundService));
		intent.SetAction(SupportChatForegroundService.ActionStart);
		intent.PutExtra(SupportChatForegroundService.ExtraConversationId, string.Empty);

		if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
		{
			context.StartForegroundService(intent);
		}
		else
		{
			context.StartService(intent);
		}

		return Task.CompletedTask;
	}

	private partial Task StopPlatformRuntimeAsync(ChatRuntimeSession session, CancellationToken cancellationToken)
	{
		var context = global::Android.App.Application.Context;
		if (context is null)
		{
			return Task.CompletedTask;
		}

		var intent = new Intent(context, typeof(SupportChatForegroundService));
		intent.SetAction(SupportChatForegroundService.ActionStop);
		context.StartService(intent);
		context.StopService(intent);
		return Task.CompletedTask;
	}
}
#endif
