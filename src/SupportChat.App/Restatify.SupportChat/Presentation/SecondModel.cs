using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Restatify.SupportChat.Services.Support;

namespace Restatify.SupportChat.Presentation;

public sealed class SecondModel : INotifyPropertyChanged
{
	private readonly IConversationReplyStore _replyStore;
	private readonly ISupportChatApiClient _api;
	private readonly string _baseUrl;
	private readonly string _apiKey;
	private string _serverStatus = "Loading conversation messages...";
	private bool _autoRefreshEnabled;
	private bool _hasMoreServerMessages;
	private bool _isLoadingMore;
	private string? _nextCursor;
	private CancellationTokenSource? _autoRefreshCts;

	public SecondModel(ConversationDetailsContext context, IConversationReplyStore replyStore, ISupportChatApiClient api)
	{
		_replyStore = replyStore;
		_api = api;
		_baseUrl = context.BaseUrl;
		_apiKey = context.ApiKey;
		ConversationId = context.Conversation.Id;
		SourceUrl = context.Conversation.SourceUrl;
		UpdatedAtGmt = context.Conversation.UpdatedAtGmt;
		UnreadCount = context.Conversation.UnreadCount;

		RefreshHistory();
		_ = RefreshServerMessages();
		AutoRefreshEnabled = true;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public string ConversationId { get; }
	public string SourceUrl { get; }
	public string UpdatedAtGmt { get; }
	public int UnreadCount { get; }
	public string UnreadText => $"Unread: {UnreadCount}";
	public int AutoRefreshIntervalSeconds => 10;
	public string AutoRefreshIntervalText => $"Interval: {AutoRefreshIntervalSeconds}s";
	public string LoadMoreButtonText
	{
		get
		{
			if (IsLoadingMore)
			{
				return "Loading...";
			}

			return HasMoreServerMessages ? "Load More" : "No More Messages";
		}
	}

	public string ServerStatus
	{
		get => _serverStatus;
		private set => SetProperty(ref _serverStatus, value);
	}

	public bool AutoRefreshEnabled
	{
		get => _autoRefreshEnabled;
		set
		{
			if (!SetProperty(ref _autoRefreshEnabled, value))
			{
				return;
			}

			if (value)
			{
				StartAutoRefresh();
			}
			else
			{
				StopAutoRefresh();
			}
		}
	}

	public bool HasMoreServerMessages
	{
		get => _hasMoreServerMessages;
		private set
		{
			if (SetProperty(ref _hasMoreServerMessages, value))
			{
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoadMoreButtonText)));
			}
		}
	}

	public bool IsLoadingMore
	{
		get => _isLoadingMore;
		private set
		{
			if (SetProperty(ref _isLoadingMore, value))
			{
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoadMoreButtonText)));
			}
		}
	}

	public ObservableCollection<ReplyHistoryItem> Replies { get; } = [];
	public ObservableCollection<ConversationMessage> ServerMessages { get; } = [];

	public void RefreshHistory()
	{
		Replies.Clear();
		foreach (var item in _replyStore.GetForConversation(ConversationId))
		{
			Replies.Add(item);
		}
	}

	public async Task RefreshServerMessages()
	{
		try
		{
			var page = await _api.GetConversationMessagesAsync(_baseUrl, _apiKey, ConversationId, cursor: null, limit: 30, newestFirst: false);
			ServerMessages.Clear();
			foreach (var item in page.Items)
			{
				ServerMessages.Add(item);
			}

			_nextCursor = page.NextCursor;
			HasMoreServerMessages = page.HasMore;

			ServerStatus = $"Loaded {ServerMessages.Count} message(s) from API.";
		}
		catch (Exception ex)
		{
			ServerStatus = ex.Message;
		}
	}

	public async Task LoadMoreServerMessages()
	{
		if (IsLoadingMore)
		{
			return;
		}

		if (!HasMoreServerMessages || string.IsNullOrWhiteSpace(_nextCursor))
		{
			ServerStatus = "No more messages available.";
			return;
		}

		try
		{
			IsLoadingMore = true;
			ServerStatus = "Loading more messages...";

			var page = await _api.GetConversationMessagesAsync(_baseUrl, _apiKey, ConversationId, cursor: _nextCursor, limit: 30, newestFirst: false);
			foreach (var item in page.Items)
			{
				ServerMessages.Add(item);
			}

			_nextCursor = page.NextCursor;
			HasMoreServerMessages = page.HasMore;
			ServerStatus = $"Loaded {ServerMessages.Count} total message(s).";
		}
		catch (CursorExpiredException)
		{
			ServerStatus = "Cursor expired. Reloading latest messages...";
			await RefreshServerMessages();
		}
		catch (Exception ex)
		{
			ServerStatus = ex.Message;
		}
		finally
		{
			IsLoadingMore = false;
		}
	}

	private void StartAutoRefresh()
	{
		StopAutoRefresh();
		_autoRefreshCts = new CancellationTokenSource();
		_ = AutoRefreshLoop(_autoRefreshCts.Token);
	}

	private void StopAutoRefresh()
	{
		if (_autoRefreshCts is null)
		{
			return;
		}

		_autoRefreshCts.Cancel();
		_autoRefreshCts.Dispose();
		_autoRefreshCts = null;
	}

	private async Task AutoRefreshLoop(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(AutoRefreshIntervalSeconds), token);
				if (token.IsCancellationRequested)
				{
					return;
				}

				await RefreshServerMessages();
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value))
		{
			return false;
		}

		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		return true;
	}
}
