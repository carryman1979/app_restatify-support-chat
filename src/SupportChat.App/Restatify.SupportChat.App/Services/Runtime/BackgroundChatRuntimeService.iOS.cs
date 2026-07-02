#if IOS
namespace Restatify.SupportChat.Services.Runtime;

public sealed partial class BackgroundChatRuntimeService
{
    private partial Task StartPlatformRuntimeAsync(ChatRuntimeSession session, CancellationToken cancellationToken)
    {
        // iOS background tasks would be implemented here
        // For now, return completed task as iOS background processing requires different approach
        return Task.CompletedTask;
    }

    private partial Task StopPlatformRuntimeAsync(ChatRuntimeSession session, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
#endif
