namespace Restatify.SupportChat.Services.Support;

public interface ISupportChatApiClient
{
	Task<LoginResult> LoginAsync(string baseUrl, string username, string password, CancellationToken cancellationToken = default);
	Task<string> GenerateApiKeyAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default);
	Task SubscribeSupportEventsAsync(
		string baseUrl,
		string apiKey,
		string? conversationId,
		Func<SupportLiveEvent, Task> onEvent,
		CancellationToken cancellationToken = default);
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
	Task<ConversationToolsResult> GetConversationToolsAsync(string baseUrl, string apiKey, string conversationId, CancellationToken cancellationToken = default);
	Task<ConversationToolsResult> SetConversationAiModeAsync(string baseUrl, string apiKey, string conversationId, string aiMode, CancellationToken cancellationToken = default);
	Task<DeleteConversationResult> DeleteConversationAsync(string baseUrl, string apiKey, string conversationId, CancellationToken cancellationToken = default);
	Task<ReplyResult> OpenBookingOverlayAsync(string baseUrl, string apiKey, string conversationId, CancellationToken cancellationToken = default);
}

public sealed partial record LoginResult(string AccessToken, string RefreshToken, bool MfaRequired);

public sealed partial record ConversationSummary(string Id, string SourceUrl, string UpdatedAtGmt, int UnreadCount);

public sealed partial record ConversationMessage(string MessageId, string ConversationId, string Sender, string Message, string TimeGmt)
{
	public string Display => $"[{TimeGmt}] {Sender}: {Message}";
}

public sealed partial record ConversationMessagesPage(IReadOnlyList<ConversationMessage> Items, string? NextCursor, bool HasMore);

public sealed partial record ReplyResult(string ConversationId, string Sender, string Message, string TimeGmt);

public sealed partial record ConversationToolsResult(string ConversationId, string AiMode, bool BookingOverlayAvailable);

public sealed partial record DeleteConversationResult(bool Deleted, bool AlreadyGone);

public sealed partial record SupportLiveEvent(
	string Type,
	string? ConversationId,
	string? Sender,
	string? Message,
	string? TimeGmt);
