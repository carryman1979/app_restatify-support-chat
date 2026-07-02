#if !ANDROID && !IOS
namespace Restatify.SupportChat.Services.Runtime;

public sealed partial class BackgroundChatRuntimeService
{
	private partial Task StartPlatformRuntimeAsync(ChatRuntimeSession session, CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	private partial Task StopPlatformRuntimeAsync(ChatRuntimeSession session, CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}

#endif
