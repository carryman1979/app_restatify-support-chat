using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using CommunityToolkit.Mvvm.Input;
using Restatify.SupportChat.Services.Settings;
using Restatify.SupportChat.Services.Support;
using Windows.Storage;

namespace Restatify.SupportChat.Presentation;

public sealed class MainModel : INotifyPropertyChanged
{
	private const string RememberLoginKey = "SupportChat.RememberLogin";
	private const string RememberedEmailKey = "SupportChat.RememberedEmail";
	private const string RememberedPasswordKey = "SupportChat.RememberedPassword";
	private const string FallbackLogoUri = "ms-appx:///Assets/Images/restatify_logo.png";
	private static readonly HttpClient LogoHttpClient = new();

	private readonly ISupportChatApiClient _api;
	private readonly IConversationReplyStore _replyStore;
	private readonly INavigator _navigator;
	private readonly IConnectionSettingsStore _connectionSettingsStore;
	private readonly IStringLocalizer _localizer;

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
	private int _loadConversationsRequestVersion;

	public MainModel(
		IStringLocalizer localizer,
		ISupportChatApiClient api,
		IConversationReplyStore replyStore,
		INavigator navigator,
		IConnectionSettingsStore connectionSettingsStore)
	{
		_api = api;
		_replyStore = replyStore;
		_navigator = navigator;
		_connectionSettingsStore = connectionSettingsStore;
		_localizer = localizer;

		var connectionSettings = _connectionSettingsStore.Load();
		_baseUrl = connectionSettings.BaseUrl;
		_apiKey = connectionSettings.ApiKey;
		_logoUrl = connectionSettings.LogoUrl;
		LoginCommand = new AsyncRelayCommand(Login);
		LoadConversationsCommand = new AsyncRelayCommand(LoadConversations, AsyncRelayCommandOptions.AllowConcurrentExecutions);
		LogoutCommand = new AsyncRelayCommand(Logout);
		OpenConversationDetailsCommand = new AsyncRelayCommand(OpenConversationDetails);
		LoadRememberedLogin();
		_ = RefreshLogoUriAsync();
		Title = string.Format(T("MainPage_TitleTemplate", "Support Inbox - {0}"), localizer["ApplicationName"]);
		_status = T("Status_Ready", "Ready.");
		_accessTokenPreview = T("Status_NotLoggedIn", "Not logged in.");
		_selectedConversationDetails = T("Status_NoConversationSelected", "No conversation selected.");
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
			Status = baseUrlError;
			return;
		}

		if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
		{
			Status = T("Error_EnterEmailPassword", "Please enter email and password.");
			return;
		}

		await RunBusy(async () =>
		{
			Status = T("Status_LoggingIn", "Signing in...");
			var result = await _api.LoginAsync(BaseUrl, Email.Trim(), Password);

			if (RememberLogin)
			{
				SaveRememberedLogin();
			}
			else
			{
				ClearRememberedLogin();
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

			if (RememberLogin && !string.IsNullOrWhiteSpace(_sessionAccessToken))
			{
				await GenerateAndSaveApiKeyAsync();
			}

			await LoadConversationsCore();
		});
	}

	public async Task LoadConversations()
	{
		if (!TryValidateBaseUrl(out var baseUrlError))
		{
			Status = baseUrlError;
			return;
		}

		if (!IsLoggedIn)
		{
			Status = T("Error_LoginFirst", "Please login first.");
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
				Status = ex.Message;
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

	public Task Logout()
	{
		Conversations.Clear();
		SelectedConversation = null;
		_sessionAccessToken = string.Empty;
		_sessionRefreshToken = string.Empty;
		IsLoggedIn = false;
		AccessTokenPreview = T("Status_NotLoggedIn", "Not logged in.");
		RefreshTokenPreview = string.Empty;
		Status = T("Status_LoggedOut", "Logged out.");
		return Task.CompletedTask;
	}

	public async Task SendReply()
	{
		if (IsBusy)
		{
			return;
		}

		if (!TryValidateBaseUrl(out var baseUrlError))
		{
			Status = baseUrlError;
			return;
		}

		if (!IsLoggedIn)
		{
			Status = T("Error_LoginFirst", "Please login first.");
			return;
		}

		if (SelectedConversation is null)
		{
			Status = T("Error_SelectConversationFirst", "Select a conversation first.");
			return;
		}

		var trimmedReply = ReplyMessage.Trim();

		if (string.IsNullOrWhiteSpace(trimmedReply))
		{
			Status = T("Error_EnterReplyMessage", "Enter a reply message.");
			return;
		}

		if (trimmedReply.Length > 2000)
		{
			Status = T("Error_ReplyMaxLength", "Reply exceeds 2000 characters.");
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
			Status = T("Error_SelectConversationFirst", "Select a conversation first.");
			return;
		}

		var context = new ConversationDetailsContext(BaseUrl.Trim(), ApiKey.Trim(), SelectedConversation);
		await _navigator.NavigateViewModelAsync<ConversationDetailsModel>(this, data: context);
	}

	public async Task OpenSettings()
	{
		await _navigator.NavigateViewModelAsync<SettingsModel>(this, data: "main");
	}

	public async Task OpenLogs()
	{
		await _navigator.NavigateViewModelAsync<LogsModel>(this);
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
		var connectionSettings = _connectionSettingsStore.Load();
		var previousBaseUrl = _baseUrl;
		var previousLogoUrl = _logoUrl;

		BaseUrl = connectionSettings.BaseUrl;
		ApiKey = connectionSettings.ApiKey;
		_logoUrl = connectionSettings.LogoUrl;

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
		var items = await _api.GetConversationsAsync(BaseUrl, ApiKey.Trim(), cancellationToken);

		var refreshed = new ObservableCollection<ConversationItem>(
			items.Select(item => new ConversationItem(item.Id, item.SourceUrl, item.UpdatedAtGmt, item.UnreadCount))
		);

		SelectedConversation = null;
		Conversations = refreshed;

		SelectedConversation = Conversations.FirstOrDefault();
		Status = Conversations.Count switch
		{
			1 => "1 aktiver Chat geladen.",
			_ => $"{Conversations.Count} aktive Chats geladen.",
		};
	}

	private async Task GenerateAndSaveApiKeyAsync()
	{
		try
		{
			Status = T("Status_GeneratingApiKey", "Schlüsselpaar wird generiert...");
			var newKey = await _api.GenerateApiKeyAsync(BaseUrl, _sessionAccessToken);
			if (!string.IsNullOrWhiteSpace(newKey))
			{
				ApiKey = newKey;
				var current = _connectionSettingsStore.Load();
				_connectionSettingsStore.Save(new Restatify.SupportChat.Services.Settings.ConnectionSettings(
					current.BaseUrl,
					newKey,
					current.LanguageCode,
					current.LogoUrl));
				Status = T("Status_ApiKeyGenerated", "Schlüssel gespeichert. Zugangsdaten sind hinterlegt.");
			}
		}
		catch (Exception ex)
		{
			Status = string.Format(T("Status_ApiKeyGenerationFailed", "Schlüssel konnte nicht generiert werden: {0}"), ex.Message);
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
			Status = ex.Message;
		}
		finally
		{
			IsBusy = false;
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
