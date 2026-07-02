namespace Restatify.SupportChat.Services.Runtime;

public sealed record ChatRuntimeSession(string BaseUrl, string ApiKey);

public interface IBackgroundChatRuntimeService
{
	bool IsRunning { get; }

	Task StartAsync(ChatRuntimeSession session, CancellationToken cancellationToken = default);

	Task StopAsync(CancellationToken cancellationToken = default);
}
