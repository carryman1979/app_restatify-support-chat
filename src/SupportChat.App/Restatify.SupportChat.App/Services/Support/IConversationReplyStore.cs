namespace Restatify.SupportChat.Services.Support;

public interface IConversationReplyStore
{
	void Add(string conversationId, string sender, string message, string timeGmt);
	IReadOnlyList<ReplyHistoryItem> GetForConversation(string conversationId);
}

public sealed record ReplyHistoryItem(string Sender, string Message, string TimeGmt)
{
	public string Display => $"[{TimeGmt}] {Sender}: {Message}";
}
