using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Restatify.SupportChat.Services.Support;

namespace Restatify.SupportChat.Presentation;

public sealed class ConversationDetailsModel : INotifyPropertyChanged
{
	private readonly IConversationReplyStore _replyStore;
	private readonly ISupportChatApiClient _api;
	private readonly IStringLocalizer _localizer;
	private readonly string _baseUrl;
	private readonly string _apiKey;
	private string _serverStatus = string.Empty;
	private bool _autoRefreshEnabled;
	private bool _hasMoreServerMessages;
	private bool _isLoadingMore;
	private bool _isSendingReply;
	private string _replyMessage = string.Empty;
	private string? _nextCursor;
	private CancellationTokenSource? _autoRefreshCts;

	public ConversationDetailsModel(ConversationDetailsContext context, IConversationReplyStore replyStore, ISupportChatApiClient api, IStringLocalizer localizer)
	{
		_replyStore = replyStore;
		_api = api;
		_localizer = localizer;
		_baseUrl = context.BaseUrl;
		_apiKey = context.ApiKey;
		ConversationId = context.Conversation.Id;
		SourceUrl = context.Conversation.SourceUrl;
		UpdatedAtGmt = context.Conversation.UpdatedAtGmt;
		UnreadCount = context.Conversation.UnreadCount;
		_serverStatus = T("Status_LoadingConversationMessages", "Loading conversation messages...");
		RefreshServerMessagesCommand = new AsyncRelayCommand(RefreshServerMessages);
		LoadMoreServerMessagesCommand = new AsyncRelayCommand(LoadMoreServerMessages);
		SendReplyCommand = new AsyncRelayCommand(SendReply);

		RefreshHistory();
		_ = RefreshServerMessages();
		AutoRefreshEnabled = true;
	}

	public event PropertyChangedEventHandler? PropertyChanged;
	public IAsyncRelayCommand RefreshServerMessagesCommand { get; }
	public IAsyncRelayCommand LoadMoreServerMessagesCommand { get; }
	public IAsyncRelayCommand SendReplyCommand { get; }

	public string ConversationId { get; }
	public string SourceUrl { get; }
	public string UpdatedAtGmt { get; }
	public int UnreadCount { get; }
	public string UnreadText => string.Format(T("ConversationDetails_UnreadTemplate", "Unread: {0}"), UnreadCount);
	public int AutoRefreshIntervalSeconds => 10;
	public string AutoRefreshIntervalText => string.Format(T("ConversationDetails_AutoRefreshIntervalTemplate", "Interval: {0}s"), AutoRefreshIntervalSeconds);
	public string LoadMoreButtonText
	{
		get
		{
			if (IsLoadingMore)
			{
				return T("ConversationDetails_Loading", "Loading...");
			}

			return HasMoreServerMessages
				? T("ConversationDetails_LoadMore", "Load More")
				: T("ConversationDetails_NoMoreMessages", "No More Messages");
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

	public bool IsSendingReply
	{
		get => _isSendingReply;
		private set => SetProperty(ref _isSendingReply, value);
	}

	public string ReplyMessage
	{
		get => _replyMessage;
		set => SetProperty(ref _replyMessage, value);
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

			ServerStatus = string.Format(T("Status_LoadedMessagesFromApi", "Loaded {0} message(s) from API."), ServerMessages.Count);
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
			ServerStatus = T("Status_NoMoreMessagesAvailable", "No more messages available.");
			return;
		}

		try
		{
			IsLoadingMore = true;
			ServerStatus = T("Status_LoadingMoreMessages", "Loading more messages...");

			var page = await _api.GetConversationMessagesAsync(_baseUrl, _apiKey, ConversationId, cursor: _nextCursor, limit: 30, newestFirst: false);
			foreach (var item in page.Items)
			{
				ServerMessages.Add(item);
			}

			_nextCursor = page.NextCursor;
			HasMoreServerMessages = page.HasMore;
			ServerStatus = string.Format(T("Status_LoadedTotalMessages", "Loaded {0} total message(s)."), ServerMessages.Count);
		}
		catch (CursorExpiredException)
		{
			ServerStatus = T("Error_CursorExpiredReloading", "Cursor expired. Reloading latest messages...");
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

	public async Task SendReply()
	{
		if (IsSendingReply)
		{
			return;
		}

		var trimmedReply = ReplyMessage.Trim();

		if (string.IsNullOrWhiteSpace(trimmedReply))
		{
			ServerStatus = T("Error_EnterReplyMessage", "Enter a reply message.");
			return;
		}

		if (trimmedReply.Length > 2000)
		{
			ServerStatus = T("Error_ReplyMaxLength", "Reply exceeds 2000 characters.");
			return;
		}

		try
		{
			IsSendingReply = true;
			var result = await _api.SendReplyAsync(_baseUrl, _apiKey, ConversationId, trimmedReply);
			_replyStore.Add(result.ConversationId, result.Sender, result.Message, result.TimeGmt);
			RefreshHistory();
			ReplyMessage = string.Empty;
			ServerStatus = string.Format(T("Status_ReplySent", "Reply sent to {0} at {1}."), result.ConversationId, result.TimeGmt);
		}
		catch (Exception ex)
		{
			ServerStatus = ex.Message;
		}
		finally
		{
			IsSendingReply = false;
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

	private string T(string key, string fallback)
	{
		var value = _localizer[key];
		return value.ResourceNotFound ? fallback : value.Value;
	}
}
