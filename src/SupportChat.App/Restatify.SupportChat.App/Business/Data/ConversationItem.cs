namespace Restatify.SupportChat.Business.Data;

public sealed partial record ConversationItem(string Id, string SourceUrl, string UpdatedAtGmt, int UnreadCount)
{
	public string Display => $"{Id} | unread: {UnreadCount} | {SourceUrl} | {UpdatedAtGmt}";
}
