using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Restatify.SupportChat.Business.Contexts;
using Restatify.SupportChat.Business.Data;
using Restatify.SupportChat.Services.Runtime;
using Restatify.SupportChat.Services.Settings;
using Restatify.SupportChat.Services.Support;
using Restatify.SupportChat.ViewModels;
using Windows.Storage;

namespace Restatify.SupportChat.Presentation;

public sealed class MainViewModel : INotifyPropertyChanged
{
	private const string ErrorStatusPrefix = "ERROR: ";
	private const string RememberLoginKey = "SupportChat.RememberLogin";
	private const string RememberedEmailKey = "SupportChat.RememberedEmail";
	private const string RememberedPasswordKey = "SupportChat.RememberedPassword";
	private const string FallbackLogoUri = "ms-appx:///Assets/Images/restatify_logo.png";
	private static readonly HttpClient LogoHttpClient = new();

	private readonly ISupportChatApiClient _api;
	private readonly IConversationReplyStore _replyStore;
	private readonly INavigator _navigator;
	private readonly IUserSettingsStore _userSettingsStore;
	private readonly IBackgroundChatRuntimeService _backgroundChatRuntimeService;
	private readonly INotificationService _notificationService;
	private readonly IStringLocalizer _localizer;
	private readonly ILogger<MainViewModel> _logger;
	private readonly DispatcherQueue? _dispatcherQueue;

	private string _baseUrl = string.Empty;
	private string _apiKey = string.Empty;
	private string _logoUrl = string.Empty;
	private string _email = string.Empty;
	private string _password = string.Empty;
	private string _replyMessage = string.Empty;
	private string _status = string.Empty;
	private string _accessTokenPreview = string.Empty;
	private string _refreshTokenPreview = string.Empty;
	private string _selectedConversationDetails = string.Empty;
	private string _sessionAccessToken = string.Empty;
	private string _sessionRefreshToken = string.Empty;
	private bool _isLoggedIn;
	private bool _isBusy;
	private bool _rememberLogin;
	private string _logoImageUri = FallbackLogoUri;
	private string _statusBar = string.Empty;
	private ConversationItem? _selectedConversation;
	private ObservableCollection<ConversationItem> _conversations = [];
	private bool _isRefreshingConversations;
	private readonly object _refreshSync = new();
	private CancellationTokenSource? _loadConversationsCts;
	private CancellationTokenSource? _liveUpdatesCts;
	private int _loadConversationsRequestVersion;
	private readonly SemaphoreSlim _eventRefreshLock = new(1, 1);
	private static readonly TimeSpan MainFallbackPollingInterval = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan NotificationCooldown = TimeSpan.FromSeconds(10);
	private bool _isLiveUpdatesConnected;
	private bool _isLiveUpdatesConnecting;
	private bool _isFallbackPollingActive;
	private int _sessionRedirectInProgress;
	private readonly Dictionary<string, int> _conversationUnreadSnapshot = new(StringComparer.Ordinal);
	private readonly Dictionary<string, DateTimeOffset> _lastNotificationByConversation = new(StringComparer.Ordinal);

	public MainViewModel(
		IStringLocalizer localizer,
		ISupportChatApiClient api,
		IConversationReplyStore replyStore,
		INavigator navigator,
		IUserSettingsStore userSettingsStore,
		IBackgroundChatRuntimeService backgroundChatRuntimeService,
		INotificationService notificationService,
		ILogger<MainViewModel> logger)
	{
		_api = api;
		_replyStore = replyStore;
		_navigator = navigator;
		_userSettingsStore = userSettingsStore;
		_backgroundChatRuntimeService = backgroundChatRuntimeService;
		_notificationService = notificationService;
		_localizer = localizer;
		_logger = logger;
		_dispatcherQueue = Restatify.SupportChat.App.UiDispatcherQueue ?? DispatcherQueue.GetForCurrentThread();

		var userSettings = _userSettingsStore.Load();
		_baseUrl = userSettings.BaseUrl;
		_apiKey = userSettings.ApiKey;
		_logoUrl = userSettings.LogoUrl;
		LoginCommand = new AsyncRelayCommand(Login);
		LoadConversationsCommand = new AsyncRelayCommand(LoadConversations, AsyncRelayCommandOptions.AllowConcurrentExecutions);
		LogoutCommand = new AsyncRelayCommand(Logout);
		OpenConversationDetailsCommand = new AsyncRelayCommand(OpenConversationDetails);
		OpenSettingsCommand = new AsyncRelayCommand(OpenSettings);
		OpenLogsCommand = new AsyncRelayCommand(OpenLogs);
		LoadRememberedLogin();
		_ = RefreshLogoUriAsync();
		Title = "Support Chat";
		_status = T("Status_Ready", "Ready.");
		_accessTokenPreview = T("Status_NotLoggedIn", "Not logged in.");
		_selectedConversationDetails = T("Status_NoConversationSelected", "No conversation selected.");
		if (!string.IsNullOrWhiteSpace(_apiKey))
		{
			IsLoggedIn = true;
			_status = T("Status_SessionRestored", "Saved session restored.");
			_accessTokenPreview = T("Status_ApiKeyReady", "Saved API key ready.");
			_ = InitializeRestoredSessionAsync();
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public string Title { get; }
	public ObservableCollection<ConversationItem> Conversations
	{
		get => _conversations;
		private set => SetProperty(ref _conversations, value);
	}
	public IAsyncRelayCommand LoginCommand { get; }
	public IAsyncRelayCommand LoadConversationsCommand { get; }
	public IAsyncRelayCommand LogoutCommand { get; }
	public IAsyncRelayCommand OpenConversationDetailsCommand { get; }
	public IAsyncRelayCommand OpenSettingsCommand { get; }
	public IAsyncRelayCommand OpenLogsCommand { get; }

	public bool IsRefreshingConversations
	{
		get => _isRefreshingConversations;
		private set
		{
			if (SetProperty(ref _isRefreshingConversations, value))
			{
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRefreshConversations)));
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LoadConversationsButtonContent)));
			}
		}
	}

	public bool CanRefreshConversations => !IsRefreshingConversations;

	public string LoadConversationsButtonContent => IsRefreshingConversations
		? T("MainPage_LoadConversationsButton.Loading", "Lade...")
		: T("MainPage_LoadConversationsButton.Content", "Laden");

	public string BaseUrl
	{
		get => _baseUrl;
		set => SetProperty(ref _baseUrl, value);
	}

	public string ApiKey
	{
		get => _apiKey;
		set => SetProperty(ref _apiKey, value);
	}

	public string Email
	{
		get => _email;
		set => SetProperty(ref _email, value);
	}

	public string Password
	{
		get => _password;
		set => SetProperty(ref _password, value);
	}

	public string ReplyMessage
	{
		get => _replyMessage;
		set => SetProperty(ref _replyMessage, value);
	}

	public string LogoImageUri
	{
		get => _logoImageUri;
		private set => SetProperty(ref _logoImageUri, value);
	}

	public string Status
	{
		get => _status;
		private set
		{
			if (SetProperty(ref _status, value))
			{
				UpdateStatusBar();
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
			: IsFallbackPollingActive
				? T("Status_LiveUpdatesFallbackPolling", "Fallback polling active.")
				: T("Status_LiveUpdatesIdle", "Live updates idle.");

	public Brush LiveUpdatesStatusBrush => IsLiveUpdatesConnected
		? new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 46, 125, 50))
		: IsLiveUpdatesConnecting
			? new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 176, 125, 0))
			: new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 107, 114, 128));

	public string AccessTokenPreview
	{
		get => _accessTokenPreview;
		private set => SetProperty(ref _accessTokenPreview, value);
	}

	public string RefreshTokenPreview
	{
		get => _refreshTokenPreview;
		private set => SetProperty(ref _refreshTokenPreview, value);
	}

	public string SelectedConversationDetails
	{
		get => _selectedConversationDetails;
		private set => SetProperty(ref _selectedConversationDetails, value);
	}

	public bool IsLoggedIn
	{
		get => _isLoggedIn;
		private set
		{
			if (SetProperty(ref _isLoggedIn, value))
			{
				PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoggedOut)));
				UpdateStatusBar();
			}
		}
	}

	public bool IsLoggedOut => !IsLoggedIn;

	public string StatusBar
	{
		get => _statusBar;
		private set => SetProperty(ref _statusBar, value);
	}

	public bool IsBusy
	{
		get => _isBusy;
		private set => SetProperty(ref _isBusy, value);
	}

	public bool RememberLogin
	{
		get => _rememberLogin;
		set
		{
			if (!SetProperty(ref _rememberLogin, value))
			{
				return;
			}

			if (!value)
			{
				ClearRememberedLogin();
			}
		}
	}

	public ConversationItem? SelectedConversation
	{
		get => _selectedConversation;
		set
		{
			if (SetProperty(ref _selectedConversation, value))
			{
				SelectedConversationDetails = value is null
					? T("Status_NoConversationSelected", "No conversation selected.")
					: string.Format(
						T("Status_ConversationDetailsTemplate", "Conversation: {0}\\nSource: {1}\\nUnread: {2}\\nUpdated (GMT): {3}"),
						value.Id,
						value.SourceUrl,
						value.UnreadCount,
						value.UpdatedAtGmt);
			}
		}
	}

	public async Task Login()
	{
		if (IsBusy)
		{
			return;
		}

		if (!TryValidateBaseUrl(out var baseUrlError))
		{
			SetErrorStatus(baseUrlError, "Login.BaseUrlValidation");
			return;
		}

		if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
		{
			SetErrorStatus(T("Error_EnterEmailPassword", "Please enter username and password."), "Login.CredentialsValidation");
			return;
		}

		await RunBusy(async () =>
		{
			// Ensure a previous user's key cannot leak into a fresh login flow.
			ApiKey = string.Empty;
			Status = T("Status_LoggingIn", "Signing in...");
			var result = await _api.LoginAsync(BaseUrl, Email.Trim(), Password);

			if (RememberLogin)
			{
				SaveRememberedLogin();
			}
			else
			{
				ClearRememberedLogin();
				// Do not keep an old persisted key when the user chooses session-only login.
				PersistApiKey(string.Empty);
			}

			_sessionAccessToken = result.AccessToken;
			_sessionRefreshToken = result.RefreshToken;
			IsLoggedIn = true;
			AccessTokenPreview = MaskToken(_sessionAccessToken, T("TokenLabel_Access", "access"));
			RefreshTokenPreview = MaskToken(_sessionRefreshToken, T("TokenLabel_Refresh", "refresh"));
			Password = string.Empty;
			Status = result.MfaRequired
				? T("Status_LoginAcceptedMfaRequired", "Login accepted. MFA required (stub API behavior).")
				: T("Status_LoginSuccessful", "Login successful.");

			if (!string.IsNullOrWhiteSpace(_sessionAccessToken))
			{
				await GenerateAndSaveApiKeyAsync(persistToSettings: RememberLogin);
			}

			await LoadConversationsCore();
			EnsureLiveUpdatesStarted();
			_ = EnsureBackgroundRuntimeStartedAsync();
		});
	}

	public void EnsureLiveUpdatesStarted()
	{
		if (!IsLoggedIn || string.IsNullOrWhiteSpace(ApiKey))
		{
			return;
		}

		if (_liveUpdatesCts is { IsCancellationRequested: false })
		{
			return;
		}

		_liveUpdatesCts?.Dispose();
		_liveUpdatesCts = new CancellationTokenSource();
		_ = LiveUpdatesLoop(_liveUpdatesCts.Token);
		_ = EnsureBackgroundRuntimeStartedAsync();
	}

	public async Task LoadConversations()
	{
		if (!TryValidateBaseUrl(out var baseUrlError))
		{
			SetErrorStatus(baseUrlError, "LoadConversations.BaseUrlValidation");
			return;
		}

		if (!IsLoggedIn)
		{
			SetErrorStatus(T("Error_LoginFirst", "Please login first."), "LoadConversations.AuthValidation");
			return;
		}

		CancellationTokenSource refreshCts;
		lock (_refreshSync)
		{
			_loadConversationsCts?.Cancel();
			_loadConversationsCts?.Dispose();
			_loadConversationsCts = new CancellationTokenSource();
			refreshCts = _loadConversationsCts;
		}

		var requestVersion = Interlocked.Increment(ref _loadConversationsRequestVersion);
		try
		{
			IsBusy = true;
			IsRefreshingConversations = true;
			Status = T("Status_LoadingConversations", "Loading conversations...");

			// Debounce burst-click refreshes so only the latest request reaches the API.
			await Task.Delay(TimeSpan.FromMilliseconds(250), refreshCts.Token);
			await LoadConversationsCore(refreshCts.Token);
		}
		catch (OperationCanceledException)
		{
			if (requestVersion == _loadConversationsRequestVersion)
			{
				Status = T("Status_RefreshCanceled", "Refresh canceled.");
			}
		}
		catch (Exception ex)
		{
			if (requestVersion == _loadConversationsRequestVersion)
			{
				_logger.LogError(ex, "Loading conversations failed for endpoint {BaseUrl}.", BaseUrl);
				SetErrorStatus(ex.Message, "LoadConversations.Exception", alreadyLogged: true);
			}
		}
		finally
		{
			if (requestVersion == _loadConversationsRequestVersion)
			{
				IsRefreshingConversations = false;
				IsBusy = false;
			}
		}
	}

	public async Task Logout()
	{
		StopLiveUpdates();
		await _backgroundChatRuntimeService.StopAsync();
		Conversations.Clear();
		SelectedConversation = null;
		_sessionAccessToken = string.Empty;
		_sessionRefreshToken = string.Empty;
		ApiKey = string.Empty;
		_conversationUnreadSnapshot.Clear();
		_lastNotificationByConversation.Clear();
		IsLoggedIn = false;
		PersistApiKey(string.Empty);
		AccessTokenPreview = T("Status_NotLoggedIn", "Not logged in.");
		RefreshTokenPreview = string.Empty;
		Status = T("Status_LoggedOut", "Logged out.");
		await _navigator.NavigateViewModelAsync<LoginViewModel>(this, qualifier: Qualifiers.ClearBackStack);
	}

	private async Task EnsureBackgroundRuntimeStartedAsync()
	{
		if (!IsLoggedIn || string.IsNullOrWhiteSpace(ApiKey))
		{
			return;
		}

		try
		{
			await _backgroundChatRuntimeService.StartAsync(new ChatRuntimeSession(BaseUrl.Trim(), ApiKey.Trim()));
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Starting background chat runtime failed for {BaseUrl}.", BaseUrl);
		}
	}

	private void StopLiveUpdates()
	{
		if (_liveUpdatesCts is null)
		{
			return;
		}

		_liveUpdatesCts.Cancel();
		_liveUpdatesCts.Dispose();
		_liveUpdatesCts = null;
		_ = RunOnUiThreadAsync(() =>
		{
			IsLiveUpdatesConnected = false;
			IsLiveUpdatesConnecting = false;
			IsFallbackPollingActive = false;
		});
	}

	private async Task LiveUpdatesLoop(CancellationToken token)
	{
		var retryDelay = TimeSpan.FromSeconds(3);
		while (!token.IsCancellationRequested)
		{
			await RunOnUiThreadAsync(() =>
			{
				IsLiveUpdatesConnecting = true;
				IsFallbackPollingActive = !IsLiveUpdatesConnected;
			});

			try
			{
				await _api.SubscribeSupportEventsAsync(BaseUrl, ApiKey.Trim(), conversationId: null, OnSupportEventAsync, token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			catch (Exception ex)
			{
				_logger.LogWarning(ex, "Conversation live-updates disconnected. Fallback to manual/poll refresh until reconnect. BaseUrl={BaseUrl}", BaseUrl);
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

			await RunMainFallbackRefreshAsync(token);

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
		if (string.Equals(evt.Type, "connected", StringComparison.Ordinal))
		{
			await RunOnUiThreadAsync(() =>
			{
				IsLiveUpdatesConnected = true;
				IsLiveUpdatesConnecting = false;
				IsFallbackPollingActive = false;
			});
			return;
		}

		if (!string.Equals(evt.Type, "message_added", StringComparison.Ordinal)
			&& !string.Equals(evt.Type, "conversation_deleted", StringComparison.Ordinal))
		{
			return;
		}

		var isMessageAdded = string.Equals(evt.Type, "message_added", StringComparison.Ordinal);
		var eventConversationId = evt.ConversationId?.Trim();
		var previousUnreadCount = isMessageAdded
			? GetUnreadCountFromSnapshot(eventConversationId)
			: 0;

		if (!await _eventRefreshLock.WaitAsync(0))
		{
			return;
		}

		try
		{
			var items = await _api.GetConversationsAsync(BaseUrl, ApiKey.Trim());
			var currentUnreadCount = isMessageAdded
				? items.FirstOrDefault(item => string.Equals(item.Id, eventConversationId, StringComparison.Ordinal))?.UnreadCount ?? 0
				: 0;
			await RunOnUiThreadAsync(() => ApplyConversations(items, updateStatus: false));

			if (isMessageAdded
				&& ShouldNotifyMessageAdded(eventConversationId, previousUnreadCount, currentUnreadCount))
			{
				await NotifyForIncomingMessageAsync(evt, eventConversationId!, previousUnreadCount, currentUnreadCount);
			}
		}
		catch (HttpRequestException ex) when (IsInvalidApiKeyError(ex))
		{
			await RedirectToLoginForExpiredSessionAsync("OnSupportEventAsync.InvalidApiKey");
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Event-driven conversation refresh failed. BaseUrl={BaseUrl}", BaseUrl);
		}
		finally
		{
			_eventRefreshLock.Release();
		}
	}

	private async Task RunMainFallbackRefreshAsync(CancellationToken token)
	{
		if (token.IsCancellationRequested || !IsFallbackPollingActive)
		{
			return;
		}

		try
		{
			await Task.Delay(MainFallbackPollingInterval, token);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		if (token.IsCancellationRequested || !IsFallbackPollingActive)
		{
			return;
		}

		if (!await _eventRefreshLock.WaitAsync(0, token))
		{
			return;
		}

		try
		{
			var items = await _api.GetConversationsAsync(BaseUrl, ApiKey.Trim(), token);
			await RunOnUiThreadAsync(() => ApplyConversations(items, updateStatus: false));
		}
		catch (HttpRequestException ex) when (IsInvalidApiKeyError(ex))
		{
			await RedirectToLoginForExpiredSessionAsync("RunMainFallbackRefreshAsync.InvalidApiKey");
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Fallback polling refresh failed. BaseUrl={BaseUrl}", BaseUrl);
		}
		finally
		{
			_eventRefreshLock.Release();
		}
	}


	public async Task SendReply()
	{
		if (IsBusy)
		{
			return;
		}

		if (!TryValidateBaseUrl(out var baseUrlError))
		{
			SetErrorStatus(baseUrlError, "SendReply.BaseUrlValidation");
			return;
		}

		if (!IsLoggedIn)
		{
			SetErrorStatus(T("Error_LoginFirst", "Please login first."), "SendReply.AuthValidation");
			return;
		}

		if (SelectedConversation is null)
		{
			SetErrorStatus(T("Error_SelectConversationFirst", "Select a conversation first."), "SendReply.SelectionValidation");
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

		await RunBusy(async () =>
		{
			Status = T("Status_SendingReply", "Sending reply...");
			var result = await _api.SendReplyAsync(BaseUrl, ApiKey.Trim(), SelectedConversation.Id, trimmedReply);
			_replyStore.Add(result.ConversationId, result.Sender, result.Message, result.TimeGmt);
			Status = string.Format(T("Status_ReplySent", "Reply sent to {0} at {1}."), result.ConversationId, result.TimeGmt);
			ReplyMessage = string.Empty;
			SelectedConversationDetails = string.Format(
				T("Status_ConversationDetailsWithReplyTemplate", "Conversation: {0}\\nSource: {1}\\nUnread: {2}\\nUpdated (GMT): {3}\\nLast reply: {4}"),
				SelectedConversation.Id,
				SelectedConversation.SourceUrl,
				SelectedConversation.UnreadCount,
				SelectedConversation.UpdatedAtGmt,
				result.TimeGmt);
		});
	}

	public async Task OpenConversationDetails()
	{
		if (IsBusy)
		{
			return;
		}

		if (SelectedConversation is null)
		{
			SetErrorStatus(T("Error_SelectConversationFirst", "Select a conversation first."), "OpenConversationDetails.SelectionValidation");
			return;
		}

		var context = new ConversationDetailsContext(BaseUrl.Trim(), ApiKey.Trim(), SelectedConversation);
		await _navigator.NavigateViewModelAsync<ConversationDetailsViewModel>(this, data: context);
	}

	public async Task OpenSettings()
	{
		await _navigator.NavigateViewModelAsync<SettingsViewModel>(this, data: "main");
	}

	public async Task OpenLogs()
	{
		await _navigator.NavigateViewModelAsync<LogsViewModel>(this);
	}

	private bool TryValidateBaseUrl(out string error)
	{
		ReloadConnectionSettings();

		error = string.Empty;

		if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri))
		{
			error = T("Error_BaseUrlInvalid", "Base URL is invalid.");
			return false;
		}

		if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.Host is "127.0.0.1" or "localhost"))
		{
			error = T("Error_UseHttpsOrLocalhostHttp", "Use HTTPS (HTTP only allowed for localhost).");
			return false;
		}

		return true;
	}


	private void ReloadConnectionSettings()
	{
		var userSettings = _userSettingsStore.Load();
		var previousBaseUrl = _baseUrl;
		var previousLogoUrl = _logoUrl;

		BaseUrl = userSettings.BaseUrl;
		if (string.IsNullOrWhiteSpace(_sessionAccessToken))
		{
			ApiKey = userSettings.ApiKey;
		}
		_logoUrl = userSettings.LogoUrl;

		if (!string.Equals(previousBaseUrl, _baseUrl, StringComparison.OrdinalIgnoreCase)
			|| !string.Equals(previousLogoUrl, _logoUrl, StringComparison.Ordinal))
		{
			_ = RefreshLogoUriAsync();
		}
	}

	private async Task RefreshLogoUriAsync()
	{
		try
		{
			if (TryGetAbsoluteUri(_logoUrl, out var configuredLogoUri))
			{
				LogoImageUri = configuredLogoUri;
				return;
			}

			if (!TryGetSiteRoot(_baseUrl, out var siteRoot))
			{
				LogoImageUri = FallbackLogoUri;
				return;
			}

			var wpIconUri = await TryResolveWordPressIconAsync(siteRoot);
			LogoImageUri = wpIconUri ?? $"{siteRoot}/favicon.ico";
		}
		catch
		{
			LogoImageUri = FallbackLogoUri;
		}
	}

	private static bool TryGetAbsoluteUri(string? input, out string uri)
	{
		uri = string.Empty;
		if (string.IsNullOrWhiteSpace(input))
		{
			return false;
		}

		if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var absoluteUri))
		{
			return false;
		}

		if (absoluteUri.Scheme != Uri.UriSchemeHttps && absoluteUri.Scheme != Uri.UriSchemeHttp)
		{
			return false;
		}

		uri = absoluteUri.ToString();
		return true;
	}

	private static bool TryGetSiteRoot(string? baseUrl, out string siteRoot)
	{
		siteRoot = string.Empty;
		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
		{
			return false;
		}

		siteRoot = uri.GetLeftPart(UriPartial.Authority);
		return true;
	}

	private static async Task<string?> TryResolveWordPressIconAsync(string siteRoot)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, $"{siteRoot}/wp-json");
		using var response = await LogoHttpClient.SendAsync(request);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}

		await using var contentStream = await response.Content.ReadAsStreamAsync();
		using var json = await JsonDocument.ParseAsync(contentStream);

		if (json.RootElement.TryGetProperty("site_icon_url", out var siteIconProperty)
			&& siteIconProperty.ValueKind == JsonValueKind.String)
		{
			var siteIcon = siteIconProperty.GetString();
			if (TryGetAbsoluteUri(siteIcon, out var iconUri))
			{
				return iconUri;
			}
		}

		return null;
	}

	private async Task LoadConversationsCore(CancellationToken cancellationToken = default)
	{
		Status = T("Status_LoadingConversations", "Loading conversations...");

		if (string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(_sessionAccessToken))
		{
			Status = T("Status_RegeneratingApiKey", "API key invalid. Generating a fresh key...");
			await GenerateAndSaveApiKeyAsync(persistToSettings: RememberLogin);
		}

		IReadOnlyList<ConversationSummary> items;
		try
		{
			items = await _api.GetConversationsAsync(BaseUrl, ApiKey.Trim(), cancellationToken);
		}
		catch (HttpRequestException ex) when (IsInvalidApiKeyError(ex) && !string.IsNullOrWhiteSpace(_sessionAccessToken))
		{
			_logger.LogDebug(ex, "Invalid API key detected while loading conversations. Regenerating key for current session. BaseUrl={BaseUrl}", BaseUrl);
			Status = T("Status_RegeneratingApiKey", "API key invalid. Generating a fresh key...");
			await GenerateAndSaveApiKeyAsync(persistToSettings: RememberLogin);
			items = await _api.GetConversationsAsync(BaseUrl, ApiKey.Trim(), cancellationToken);
		}
		catch (HttpRequestException ex) when (IsInvalidApiKeyError(ex))
		{
			await RedirectToLoginForExpiredSessionAsync("LoadConversationsCore.InvalidApiKey");
			return;
		}

		await RunOnUiThreadAsync(() => ApplyConversations(items, updateStatus: true));
	}

	private async Task InitializeRestoredSessionAsync()
	{
		try
		{
			await LoadConversationsCore();
			EnsureLiveUpdatesStarted();
			await EnsureBackgroundRuntimeStartedAsync();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Restored session initialization failed. BaseUrl={BaseUrl}", BaseUrl);
		}
	}

		private async Task RedirectToLoginForExpiredSessionAsync(string reason)
		{
			if (Interlocked.Exchange(ref _sessionRedirectInProgress, 1) == 1)
			{
				return;
			}

			try
			{
				_logger.LogWarning("Session is no longer valid ({Reason}). Redirecting to login.", reason);

				await RunOnUiThreadAsync(() =>
				{
					StopLiveUpdates();
					Conversations.Clear();
					SelectedConversation = null;
					_sessionAccessToken = string.Empty;
					_sessionRefreshToken = string.Empty;
					ApiKey = string.Empty;
					PersistApiKey(string.Empty);
					_conversationUnreadSnapshot.Clear();
					_lastNotificationByConversation.Clear();
					IsLoggedIn = false;
					AccessTokenPreview = T("Status_NotLoggedIn", "Not logged in.");
					RefreshTokenPreview = string.Empty;
					Status = T("Status_SessionExpiredRelogin", "Session expired. Please sign in again.");
				});

				await _backgroundChatRuntimeService.StopAsync();
				await _navigator.NavigateViewModelAsync<LoginViewModel>(this, qualifier: Qualifiers.ClearBackStack);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to redirect to login after session invalidation.");
			}
			finally
			{
				Interlocked.Exchange(ref _sessionRedirectInProgress, 0);
			}
		}

	private void ApplyConversations(IReadOnlyList<ConversationSummary> items, bool updateStatus)
	{
		UpdateUnreadSnapshot(items);

		var selectedId = SelectedConversation?.Id;
		var refreshed = new ObservableCollection<ConversationItem>(
			items.Select(item => new ConversationItem(item.Id, item.SourceUrl, item.UpdatedAtGmt, item.UnreadCount))
		);

		Conversations = refreshed;
		SelectedConversation = !string.IsNullOrWhiteSpace(selectedId)
			? Conversations.FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.Ordinal))
			: null;

		if (SelectedConversation is null)
		{
			SelectedConversation = Conversations.FirstOrDefault();
		}

		if (updateStatus)
		{
			Status = Conversations.Count switch
			{
				1 => "1 aktiver Chat geladen.",
				_ => $"{Conversations.Count} aktive Chats geladen.",
			};
		}
	}

	private int GetUnreadCountFromSnapshot(string? conversationId)
	{
		if (string.IsNullOrWhiteSpace(conversationId))
		{
			return 0;
		}

		return _conversationUnreadSnapshot.TryGetValue(conversationId, out var unreadCount)
			? unreadCount
			: 0;
	}

	private bool ShouldNotifyMessageAdded(string? conversationId, int previousUnreadCount, int currentUnreadCount)
	{
		if (string.IsNullOrWhiteSpace(conversationId))
		{
			return false;
		}

		if (string.Equals(SelectedConversation?.Id, conversationId, StringComparison.Ordinal))
		{
			return false;
		}

		return currentUnreadCount > previousUnreadCount;
	}

	private async Task NotifyForIncomingMessageAsync(SupportLiveEvent evt, string conversationId, int previousUnreadCount, int currentUnreadCount)
	{
		try
		{
			if (!ShouldEmitNotificationNow(conversationId, DateTimeOffset.UtcNow))
			{
				return;
			}

			var unreadDelta = Math.Max(1, currentUnreadCount - previousUnreadCount);
			var title = T("Notification_NewMessageTitle", "New support message");
			var senderPrefix = string.IsNullOrWhiteSpace(evt.Sender) ? string.Empty : $"{evt.Sender}: ";
			var body = unreadDelta > 1
				? string.Format(
					T("Notification_NewMessageBurstBody", "Conversation {0} has {1} new messages."),
					conversationId,
					unreadDelta)
				: !string.IsNullOrWhiteSpace(evt.Message)
				? $"{senderPrefix}{evt.Message}"
				: string.Format(T("Notification_NewMessageFallbackBody", "Conversation {0} has a new message."), conversationId);

			await _notificationService.ShowNewMessageAsync(new ChatNotificationPayload(conversationId, title, body));
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Notification dispatch failed for conversation {ConversationId}.", conversationId);
		}
	}

	private bool ShouldEmitNotificationNow(string conversationId, DateTimeOffset now)
	{
		if (_lastNotificationByConversation.TryGetValue(conversationId, out var previousNotificationAt)
			&& now - previousNotificationAt < NotificationCooldown)
		{
			return false;
		}

		_lastNotificationByConversation[conversationId] = now;
		return true;
	}

	private void UpdateUnreadSnapshot(IReadOnlyList<ConversationSummary> items)
	{
		_conversationUnreadSnapshot.Clear();

		foreach (var item in items)
		{
			if (string.IsNullOrWhiteSpace(item.Id))
			{
				continue;
			}

			_conversationUnreadSnapshot[item.Id] = Math.Max(0, item.UnreadCount);
		}
	}

	private static bool IsInvalidApiKeyError(HttpRequestException ex)
	{
		var message = ex.Message;
		if (ex.StatusCode == HttpStatusCode.Unauthorized)
		{
			return message.Contains("Invalid API key", StringComparison.OrdinalIgnoreCase);
		}

		return false;
	}

	private async Task GenerateAndSaveApiKeyAsync(bool persistToSettings)
	{
		Status = T("Status_GeneratingApiKey", "Schlüsselpaar wird generiert...");
		var newKey = await _api.GenerateApiKeyAsync(BaseUrl, _sessionAccessToken);
		if (string.IsNullOrWhiteSpace(newKey))
		{
			throw new InvalidOperationException(T("Error_ApiKeyEmpty", "API key generation returned an empty key."));
		}

		ApiKey = newKey;

		if (persistToSettings)
		{
			PersistApiKey(newKey);
			Status = T("Status_ApiKeyGenerated", "Schlüssel gespeichert. Zugangsdaten sind hinterlegt.");
		}
		else
		{
			Status = T("Status_ApiKeyGeneratedSession", "API key generated for current session.");
		}
	}

	private void LoadRememberedLogin()
	{
		var localSettings = ApplicationData.Current.LocalSettings;
		RememberLogin = localSettings.Values[RememberLoginKey] as bool? ?? false;

		if (!RememberLogin)
		{
			return;
		}

		Email = localSettings.Values[RememberedEmailKey] as string ?? string.Empty;
		Password = localSettings.Values[RememberedPasswordKey] as string ?? string.Empty;
	}

	private void SaveRememberedLogin()
	{
		var localSettings = ApplicationData.Current.LocalSettings;
		localSettings.Values[RememberLoginKey] = true;
		localSettings.Values[RememberedEmailKey] = Email.Trim();
		localSettings.Values[RememberedPasswordKey] = Password;
	}

	private void ClearRememberedLogin()
	{
		var localSettings = ApplicationData.Current.LocalSettings;
		localSettings.Values[RememberLoginKey] = false;
		localSettings.Values.Remove(RememberedEmailKey);
		localSettings.Values.Remove(RememberedPasswordKey);
	}

	private void PersistApiKey(string apiKey)
	{
		var current = _userSettingsStore.Load();
		_userSettingsStore.Save(new UserSettings(
			current.BaseUrl,
			apiKey,
			current.LanguageCode,
			current.LogoUrl,
			current.ThemeMode));
	}

	private string MaskToken(string token, string label)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return string.Format(T("TokenMask_Empty", "{0}: <empty>"), label);
		}

		var cleaned = token.Trim();
		if (cleaned.Length <= 12)
		{
			return string.Format(T("TokenMask_Short", "{0}: {1}"), label, new string('*', cleaned.Length));
		}

		return string.Format(T("TokenMask_Long", "{0}: {1}...{2}"), label, cleaned[..6], cleaned[^6..]);
	}

	private string T(string key, string fallback)
	{
		var value = _localizer[key];
		return value.ResourceNotFound ? fallback : value.Value;
	}

	private void UpdateStatusBar()
	{
		if (!IsLoggedIn)
		{
			StatusBar = _status;
			return;
		}

		var count = Conversations.Count;
		var chatText = count == 1 ? "1 aktiver Chat" : $"{count} Chats";
		StatusBar = string.IsNullOrEmpty(_status) ? chatText : $"{chatText}  ·  {_status}";
	}

	private async Task RunBusy(Func<Task> action)
	{
		try
		{
			IsBusy = true;
			await action();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Operation failed in MainViewModel. BaseUrl={BaseUrl}", BaseUrl);
			SetErrorStatus(ex.Message, "RunBusy.Exception", alreadyLogged: true);
		}
		finally
		{
			IsBusy = false;
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

	private void SetErrorStatus(string message, string context, bool alreadyLogged = false)
	{
		if (!alreadyLogged)
		{
			_logger.LogWarning("UI error status in {Context}. BaseUrl={BaseUrl}. Message={Message}", context, BaseUrl, message);
		}

		Status = message.StartsWith(ErrorStatusPrefix, StringComparison.OrdinalIgnoreCase)
			? message
			: $"{ErrorStatusPrefix}{message}";
	}
}


