namespace Restatify.SupportChat.Services.Support;

public interface ISupportChatApiClient
{
	Task<LoginResult> LoginAsync(string baseUrl, string email, string password, CancellationToken cancellationToken = default);
	Task<IReadOnlyList<ConversationSummary>> GetConversationsAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default);
	Task<ConversationMessagesPage> GetConversationMessagesAsync(
		string baseUrl,
		string apiKey,
		string conversationId,
		string? cursor = null,
		int limit = 50,
		bool newestFirst = true,
		CancellationToken cancellationToken = default);
	Task<ReplyResult> SendReplyAsync(string baseUrl, string apiKey, string conversationId, string message, CancellationToken cancellationToken = default);
}

public sealed partial record LoginResult(string AccessToken, string RefreshToken, bool MfaRequired);

public sealed partial record ConversationSummary(string Id, string SourceUrl, string UpdatedAtGmt, int UnreadCount);

public sealed partial record ConversationMessage(string MessageId, string ConversationId, string Sender, string Message, string TimeGmt)
{
	public string Display => $"[{TimeGmt}] {Sender}: {Message}";
}

public sealed partial record ConversationMessagesPage(IReadOnlyList<ConversationMessage> Items, string? NextCursor, bool HasMore);

public sealed partial record ReplyResult(string ConversationId, string Sender, string Message, string TimeGmt);
