namespace Restatify.SupportChat.Presentation;

public sealed partial record ConversationDetailsContext(
	string BaseUrl,
	string ApiKey,
	ConversationItem Conversation
);
