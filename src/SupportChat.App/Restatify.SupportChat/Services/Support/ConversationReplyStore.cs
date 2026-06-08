using System.Collections.Concurrent;

namespace Restatify.SupportChat.Services.Support;

public sealed class ConversationReplyStore : IConversationReplyStore
{
	private readonly ConcurrentDictionary<string, List<ReplyHistoryItem>> _items = new();
	private readonly object _sync = new();

	public void Add(string conversationId, string sender, string message, string timeGmt)
	{
		if (string.IsNullOrWhiteSpace(conversationId))
		{
			return;
		}

		lock (_sync)
		{
			var list = _items.GetOrAdd(conversationId, static _ => []);
			list.Add(new ReplyHistoryItem(sender, message, timeGmt));
		}
	}

	public IReadOnlyList<ReplyHistoryItem> GetForConversation(string conversationId)
	{
		if (string.IsNullOrWhiteSpace(conversationId))
		{
			return [];
		}

		lock (_sync)
		{
			if (!_items.TryGetValue(conversationId, out var list))
			{
				return [];
			}

			return list.ToArray();
		}
	}
}
