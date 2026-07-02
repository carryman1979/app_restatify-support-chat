namespace Restatify.SupportChat.Business.Contexts;

public sealed partial record ConversationDetailsContext(
	string BaseUrl,
	string ApiKey,
	Restatify.SupportChat.Business.Data.ConversationItem Conversation
);
