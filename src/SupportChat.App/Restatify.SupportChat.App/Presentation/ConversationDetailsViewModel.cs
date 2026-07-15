using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Restatify.SupportChat.Business.Contexts;
using Restatify.SupportChat.Services.Support;

namespace Restatify.SupportChat.Presentation;

public sealed class ConversationDetailsViewModel : INotifyPropertyChanged
{
	private const string ErrorStatusPrefix = "ERROR: ";
	private const int FallbackPollingIntervalSeconds = 5;
	private static readonly HashSet<string> AllowedAiModes = ["off", "visitor", "support", "both"];
	private readonly IConversationReplyStore _replyStore;
	private readonly ISupportChatApiClient _api;
	private readonly IStringLocalizer _localizer;
	private readonly ILogger<ConversationDetailsViewModel> _logger;
	private readonly string _baseUrl;
	private readonly string _apiKey;
	private string _serverStatus = string.Empty;
	private bool _autoRefreshEnabled;
	private bool _isLiveUpdatesConnected;
	private bool _isLiveUpdatesConnecting;
	private bool _isFallbackPollingActive;
	private bool _hasMoreServerMessages;
	private bool _isLoadingMore;
	private bool _isSendingReply;
	private bool _isSavingAiMode;
	private bool _isOpeningBookingOverlay;
	private string _selectedAiMode = "both";
	private bool _isBookingOverlayAvailable;
	private bool _isConversationDeleted;
	private string _replyMessage = string.Empty;
	private string? _nextCursor;
	private CancellationTokenSource? _autoRefreshCts;
	private readonly DispatcherQueue? _dispatcherQueue;

	public ConversationDetailsViewModel(
		ConversationDetailsContext context,
		IConversationReplyStore replyStore,
		ISupportChatApiClient api,
		IStringLocalizer localizer,
		ILogger<ConversationDetailsViewModel> logger)
	{
		_replyStore = replyStore;
		_api = api;
		_localizer = localizer;
		_logger = logger;
		_dispatcherQueue = Restatify.SupportChat.App.UiDispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
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
		SaveAiModeCommand = new AsyncRelayCommand(SaveAiModeAsync);
		OpenBookingOverlayCommand = new AsyncRelayCommand(OpenBookingOverlayAsync);
		AiModeOptions = [
			new AiModeOption("off", T("ConversationDetails_AiMode_Off", "AI off (temporary)")),
			new AiModeOption("visitor", T("ConversationDetails_AiMode_Visitor", "AI replies to visitor only")),
			new AiModeOption("support", T("ConversationDetails_AiMode_Support", "AI replies to support only")),
			new AiModeOption("both", T("ConversationDetails_AiMode_Both", "AI replies to both sides")),
		];

		RefreshHistory();
		_ = LoadConversationToolsAsync();
		_ = RefreshServerMessages();
		AutoRefreshEnabled = true;
	}

	public event PropertyChangedEventHandler? PropertyChanged;
	public IAsyncRelayCommand RefreshServerMessagesCommand { get; }
	public IAsyncRelayCommand LoadMoreServerMessagesCommand { get; }
	public IAsyncRelayCommand SendReplyCommand { get; }
	public IAsyncRelayCommand SaveAiModeCommand { get; }
	public IAsyncRelayCommand OpenBookingOverlayCommand { get; }

	public string ConversationId { get; }
	public string SourceUrl { get; }
	public string UpdatedAtGmt { get; }
	public int UnreadCount { get; }
	public string UnreadText => string.Format(T("ConversationDetails_UnreadTemplate", "Unread: {0}"), UnreadCount);
	public int AutoRefreshIntervalSeconds => FallbackPollingIntervalSeconds;
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

	public bool IsLiveUpdatesConnected
	{
		get => _isLiveUpdatesConnected;
		private set
		{
			if (SetProperty(ref _isLiveUpdatesConnected, value))
			{
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LiveUpdatesStatusText)));
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LiveUpdatesStatusBrush)));
			}
		}
	}

	public bool IsLiveUpdatesConnecting
	{
		get => _isLiveUpdatesConnecting;
		private set
		{
			if (SetProperty(ref _isLiveUpdatesConnecting, value))
			{
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LiveUpdatesStatusText)));
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LiveUpdatesStatusBrush)));
			}
		}
	}

	public bool IsFallbackPollingActive
	{
		get => _isFallbackPollingActive;
		private set
		{
			if (SetProperty(ref _isFallbackPollingActive, value))
			{
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LiveUpdatesStatusText)));
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LiveUpdatesStatusBrush)));
			}
		}
	}

	public string LiveUpdatesStatusText => IsLiveUpdatesConnected
		? T("Status_LiveUpdatesConnected", "Live updates connected.")
		: IsLiveUpdatesConnecting
			? T("Status_LiveUpdatesReconnecting", "Live updates reconnecting...")
			: T("Status_LiveUpdatesFallbackPolling", "Fallback polling active.");

	public Brush LiveUpdatesStatusBrush => IsLiveUpdatesConnected
		? new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 46, 125, 50))
		: IsLiveUpdatesConnecting
			? new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 176, 125, 0))
			: new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 107, 114, 128));

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

	public bool IsSavingAiMode
	{
		get => _isSavingAiMode;
		private set => SetProperty(ref _isSavingAiMode, value);
	}

	public bool IsOpeningBookingOverlay
	{
		get => _isOpeningBookingOverlay;
		private set => SetProperty(ref _isOpeningBookingOverlay, value);
	}

	public string SelectedAiMode
	{
		get => _selectedAiMode;
		set => SetProperty(ref _selectedAiMode, NormalizeAiMode(value));
	}

	public bool IsBookingOverlayAvailable
	{
		get => _isBookingOverlayAvailable;
		private set => SetProperty(ref _isBookingOverlayAvailable, value);
	}

	public bool IsConversationDeleted
	{
		get => _isConversationDeleted;
		private set
		{
			if (SetProperty(ref _isConversationDeleted, value))
			{
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanInteractWithConversation)));
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConversationDeletedBannerText)));
			}
		}
	}

	public bool CanInteractWithConversation => !IsConversationDeleted;

	public string ConversationDeletedBannerText => T("ConversationDetails_ConversationDeletedBanner", "This conversation was deleted in another session.");

	public string ReplyMessage
	{
		get => _replyMessage;
		set => SetProperty(ref _replyMessage, value);
	}

	public string DeleteDialogTitle => T("ConversationDetails_DeleteDialog_Title", "Delete this conversation?");
	public string DeleteDialogContent => T("ConversationDetails_DeleteDialog_Content", "This action cannot be undone.");
	public string DeleteDialogConfirm => T("ConversationDetails_DeleteDialog_Confirm", "Delete");
	public string DeleteDialogCancel => T("ConversationDetails_DeleteDialog_Cancel", "Cancel");

	public ObservableCollection<ReplyHistoryItem> Replies { get; } = [];
	public ObservableCollection<ConversationMessage> ServerMessages { get; } = [];
	public ObservableCollection<AiModeOption> AiModeOptions { get; }

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
			await RunOnUiThreadAsync(() =>
			{
				ServerMessages.Clear();
				foreach (var item in page.Items)
				{
					ServerMessages.Add(item);
				}

				_nextCursor = page.NextCursor;
				HasMoreServerMessages = page.HasMore;
				ServerStatus = string.Format(T("Status_LoadedMessagesFromApi", "Loaded {0} message(s) from API."), ServerMessages.Count);
			});
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Refreshing conversation messages failed for {ConversationId} against {BaseUrl}.", ConversationId, _baseUrl);
			await RunOnUiThreadAsync(() => SetErrorStatus(ex.Message, "RefreshServerMessages.Exception", alreadyLogged: true));
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
			await RunOnUiThreadAsync(() => ServerStatus = T("Status_NoMoreMessagesAvailable", "No more messages available."));
			return;
		}

		try
		{
			await RunOnUiThreadAsync(() =>
			{
				IsLoadingMore = true;
				ServerStatus = T("Status_LoadingMoreMessages", "Loading more messages...");
			});

			var page = await _api.GetConversationMessagesAsync(_baseUrl, _apiKey, ConversationId, cursor: _nextCursor, limit: 30, newestFirst: false);
			await RunOnUiThreadAsync(() =>
			{
				foreach (var item in page.Items)
				{
					ServerMessages.Add(item);
				}

				_nextCursor = page.NextCursor;
				HasMoreServerMessages = page.HasMore;
				ServerStatus = string.Format(T("Status_LoadedTotalMessages", "Loaded {0} total message(s)."), ServerMessages.Count);
			});
		}
		catch (CursorExpiredException)
		{
			_logger.LogWarning("Cursor expired while loading more messages for {ConversationId}. Triggering refresh.", ConversationId);
			await RunOnUiThreadAsync(() => SetErrorStatus(T("Error_CursorExpiredReloading", "Cursor expired. Reloading latest messages..."), "LoadMoreServerMessages.CursorExpired", alreadyLogged: true));
			await RefreshServerMessages();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Loading more conversation messages failed for {ConversationId}.", ConversationId);
			await RunOnUiThreadAsync(() => SetErrorStatus(ex.Message, "LoadMoreServerMessages.Exception", alreadyLogged: true));
		}
		finally
		{
			await RunOnUiThreadAsync(() => IsLoadingMore = false);
		}
	}

	public async Task SendReply()
	{
		if (IsSendingReply)
		{
			return;
		}

		if (IsConversationDeleted)
		{
			SetErrorStatus(T("Status_ConversationDeleted", "Conversation deleted."), "SendReply.ConversationDeleted");
			return;
		}

		var trimmedReply = ReplyMessage.Trim();

		if (string.IsNullOrWhiteSpace(trimmedReply))
		{
			SetErrorStatus(T("Error_EnterReplyMessage", "Enter a reply message."), "SendReply.ContentValidation");
			return;
		}

		if (trimmedReply.Length > 2000)
		{
			SetErrorStatus(T("Error_ReplyMaxLength", "Reply exceeds 2000 characters."), "SendReply.LengthValidation");
			return;
		}

		try
		{
			await RunOnUiThreadAsync(() => IsSendingReply = true);
			var result = await _api.SendReplyAsync(_baseUrl, _apiKey, ConversationId, trimmedReply);
			await RunOnUiThreadAsync(() =>
			{
				_replyStore.Add(result.ConversationId, result.Sender, result.Message, result.TimeGmt);
				RefreshHistory();
				ReplyMessage = string.Empty;
				ServerStatus = string.Format(T("Status_ReplySent", "Reply sent to {0} at {1}."), result.ConversationId, result.TimeGmt);
			});
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Sending reply failed for {ConversationId}.", ConversationId);
			await RunOnUiThreadAsync(() => SetErrorStatus(ex.Message, "SendReply.Exception", alreadyLogged: true));
		}
		finally
		{
			await RunOnUiThreadAsync(() => IsSendingReply = false);
		}
	}

	public async Task<bool> DeleteConversationAsync()
	{
		try
		{
			var result = await _api.DeleteConversationAsync(_baseUrl, _apiKey, ConversationId);
			StopAutoRefresh();
			await RunOnUiThreadAsync(() =>
			{
				IsConversationDeleted = true;
				ServerStatus = result.AlreadyGone
					? T("Status_ConversationAlreadyDeleted", "Conversation was already deleted.")
					: T("Status_ConversationDeleted", "Conversation deleted.");
			});
			return result.Deleted;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Deleting conversation failed for {ConversationId}.", ConversationId);
			await RunOnUiThreadAsync(() => SetErrorStatus(ex.Message, "DeleteConversation.Exception", alreadyLogged: true));
			return false;
		}
	}

	private async Task LoadConversationToolsAsync()
	{
		try
		{
			var tools = await _api.GetConversationToolsAsync(_baseUrl, _apiKey, ConversationId);
			await RunOnUiThreadAsync(() =>
			{
				SelectedAiMode = NormalizeAiMode(tools.AiMode);
				IsBookingOverlayAvailable = tools.BookingOverlayAvailable;
			});
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Loading conversation tools failed for {ConversationId}.", ConversationId);
		}
	}

	private async Task SaveAiModeAsync()
	{
		if (IsSavingAiMode)
		{
			return;
		}

		if (IsConversationDeleted)
		{
			return;
		}

		try
		{
			await RunOnUiThreadAsync(() => IsSavingAiMode = true);
			var result = await _api.SetConversationAiModeAsync(_baseUrl, _apiKey, ConversationId, SelectedAiMode);
			await RunOnUiThreadAsync(() =>
			{
				SelectedAiMode = NormalizeAiMode(result.AiMode);
				ServerStatus = T("Status_AiModeSaved", "AI mode saved.");
			});
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Saving AI mode failed for {ConversationId}.", ConversationId);
			await RunOnUiThreadAsync(() => SetErrorStatus(ex.Message, "SaveAiMode.Exception", alreadyLogged: true));
		}
		finally
		{
			await RunOnUiThreadAsync(() => IsSavingAiMode = false);
		}
	}

	private async Task OpenBookingOverlayAsync()
	{
		if (IsOpeningBookingOverlay || !IsBookingOverlayAvailable)
		{
			return;
		}

		if (IsConversationDeleted)
		{
			return;
		}

		try
		{
			await RunOnUiThreadAsync(() => IsOpeningBookingOverlay = true);
			var result = await _api.OpenBookingOverlayAsync(_baseUrl, _apiKey, ConversationId);
			await RunOnUiThreadAsync(() =>
			{
				_replyStore.Add(result.ConversationId, result.Sender, result.Message, result.TimeGmt);
				RefreshHistory();
				ServerStatus = T("Status_BookingOverlayTriggered", "Booking overlay triggered for visitor.");
			});
			await RefreshServerMessages();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Opening booking overlay failed for {ConversationId}.", ConversationId);
			await RunOnUiThreadAsync(() => SetErrorStatus(ex.Message, "OpenBookingOverlay.Exception", alreadyLogged: true));
		}
		finally
		{
			await RunOnUiThreadAsync(() => IsOpeningBookingOverlay = false);
		}
	}

	private Task RunOnUiThreadAsync(Action action)
	{
		if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
		{
			action();
			return Task.CompletedTask;
		}

		var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var enqueued = _dispatcherQueue.TryEnqueue(() =>
		{
			try
			{
				action();
				tcs.TrySetResult(null);
			}
			catch (Exception ex)
			{
				tcs.TrySetException(ex);
			}
		});

		if (!enqueued)
		{
			tcs.TrySetException(new InvalidOperationException("Failed to enqueue UI update."));
		}

		return tcs.Task;
	}

	private void StartAutoRefresh()
	{
		StopAutoRefresh();
		_autoRefreshCts = new CancellationTokenSource();
		_ = LiveUpdatesLoop(_autoRefreshCts.Token);
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
		_ = RunOnUiThreadAsync(() =>
		{
			IsLiveUpdatesConnected = false;
			IsLiveUpdatesConnecting = false;
			IsFallbackPollingActive = false;
		});
	}

	private async Task LiveUpdatesLoop(CancellationToken token)
	{
		var retryDelay = TimeSpan.FromSeconds(4);
		while (!token.IsCancellationRequested)
		{
			await RunOnUiThreadAsync(() =>
			{
				IsLiveUpdatesConnecting = true;
				IsFallbackPollingActive = !IsLiveUpdatesConnected;
			});

			try
			{
				await _api.SubscribeSupportEventsAsync(
					_baseUrl,
					_apiKey,
					ConversationId,
					OnSupportEventAsync,
					token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Live updates channel disconnected for conversation {ConversationId}. Falling back to polling.", ConversationId);
				await RunOnUiThreadAsync(() =>
				{
					SetErrorStatus(
						$"Live updates unavailable ({ex.GetBaseException().Message}). Fallback polling active.",
						"LiveUpdatesLoop.Disconnected",
						alreadyLogged: true);
				});
			}

			await RunOnUiThreadAsync(() =>
			{
				IsLiveUpdatesConnected = false;
				IsLiveUpdatesConnecting = true;
				IsFallbackPollingActive = true;
			});

			try
			{
				await Task.Delay(retryDelay, token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
		}
	}

	private async Task OnSupportEventAsync(SupportLiveEvent evt)
	{
		if (evt.Type == "connected")
		{
			await RunOnUiThreadAsync(() =>
			{
				IsLiveUpdatesConnected = true;
				IsLiveUpdatesConnecting = false;
				IsFallbackPollingActive = false;
				ServerStatus = T("Status_LiveUpdatesConnected", "Live updates connected.");
			});
			return;
		}

		if (evt.Type == "message_added"
			&& string.Equals(evt.ConversationId, ConversationId, StringComparison.Ordinal))
		{
			await RefreshServerMessages();
			return;
		}

		if (evt.Type == "conversation_deleted"
			&& string.Equals(evt.ConversationId, ConversationId, StringComparison.Ordinal))
		{
			StopAutoRefresh();
			await RunOnUiThreadAsync(() =>
			{
				IsConversationDeleted = true;
				ServerStatus = T("Status_ConversationDeleted", "Conversation deleted.");
			});
		}
	}

	private async Task AutoRefreshLoop(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			try
			{
				if (!IsFallbackPollingActive)
				{
					await Task.Delay(TimeSpan.FromSeconds(1), token);
					continue;
				}

				await Task.Delay(TimeSpan.FromSeconds(FallbackPollingIntervalSeconds), token);
				if (token.IsCancellationRequested)
				{
					return;
				}

				if (!IsFallbackPollingActive)
				{
					continue;
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

	private void SetErrorStatus(string message, string context, bool alreadyLogged = false)
	{
		if (!alreadyLogged)
		{
			_logger.LogWarning("Conversation details error status in {Context}. ConversationId={ConversationId}. Message={Message}", context, ConversationId, message);
		}

		ServerStatus = message.StartsWith(ErrorStatusPrefix, StringComparison.OrdinalIgnoreCase)
			? message
			: $"{ErrorStatusPrefix}{message}";
	}

	private static string NormalizeAiMode(string? aiMode)
	{
		var normalized = (aiMode ?? string.Empty).Trim().ToLowerInvariant();
		return AllowedAiModes.Contains(normalized) ? normalized : "both";
	}

}

public sealed record AiModeOption(string Value, string Label);
