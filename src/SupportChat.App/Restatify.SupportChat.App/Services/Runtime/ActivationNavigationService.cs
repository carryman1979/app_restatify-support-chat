namespace Restatify.SupportChat.Services.Runtime;

public sealed class ConversationActivationRequestedEventArgs : EventArgs
{
	public ConversationActivationRequestedEventArgs(string conversationId)
	{
		ConversationId = conversationId;
	}

	public string ConversationId { get; }
}

public interface IActivationNavigationService
{
	event EventHandler<ConversationActivationRequestedEventArgs>? ConversationRequested;

	void RequestConversationNavigation(string conversationId);

	bool TryConsumePendingConversationId(out string? conversationId);
}

public sealed class ActivationNavigationService : IActivationNavigationService
{
	private readonly object _sync = new();
	private string? _pendingConversationId;

	public event EventHandler<ConversationActivationRequestedEventArgs>? ConversationRequested;

	public void RequestConversationNavigation(string conversationId)
	{
		if (string.IsNullOrWhiteSpace(conversationId))
		{
			return;
		}

		var trimmedConversationId = conversationId.Trim();
		lock (_sync)
		{
			_pendingConversationId = trimmedConversationId;
		}

		ConversationRequested?.Invoke(this, new ConversationActivationRequestedEventArgs(trimmedConversationId));
	}

	public bool TryConsumePendingConversationId(out string? conversationId)
	{
		lock (_sync)
		{
			conversationId = _pendingConversationId;
			_pendingConversationId = null;
		}

		return !string.IsNullOrWhiteSpace(conversationId);
	}
}
