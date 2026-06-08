using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Restatify.SupportChat.Services.Support;

namespace Restatify.SupportChat.Presentation;

public sealed class MainModel : INotifyPropertyChanged
{
	private readonly ISupportChatApiClient _api;
	private readonly IConversationReplyStore _replyStore;
	private readonly INavigator _navigator;

	private string _baseUrl = "http://127.0.0.1:8000";
	private string _apiKey = "dev-support-api-key";
	private string _email = string.Empty;
	private string _password = string.Empty;
	private string _replyMessage = string.Empty;
	private string _status = "Ready.";
	private string _accessTokenPreview = "Not logged in.";
	private string _refreshTokenPreview = string.Empty;
	private string _selectedConversationDetails = "No conversation selected.";
	private string _sessionAccessToken = string.Empty;
	private string _sessionRefreshToken = string.Empty;
	private bool _isLoggedIn;
	private bool _isBusy;
	private ConversationItem? _selectedConversation;

	public MainModel(IStringLocalizer localizer, ISupportChatApiClient api, IConversationReplyStore replyStore, INavigator navigator)
	{
		_api = api;
		_replyStore = replyStore;
		_navigator = navigator;
		Title = $"Support Inbox - {localizer["ApplicationName"]}";
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public string Title { get; }
	public ObservableCollection<ConversationItem> Conversations { get; } = [];

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

	public string Status
	{
		get => _status;
		private set => SetProperty(ref _status, value);
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
		private set => SetProperty(ref _isLoggedIn, value);
	}

	public bool IsBusy
	{
		get => _isBusy;
		private set => SetProperty(ref _isBusy, value);
	}

	public ConversationItem? SelectedConversation
	{
		get => _selectedConversation;
		set
		{
			if (SetProperty(ref _selectedConversation, value))
			{
				SelectedConversationDetails = value is null
					? "No conversation selected."
					: $"Conversation: {value.Id}\nSource: {value.SourceUrl}\nUnread: {value.UnreadCount}\nUpdated (GMT): {value.UpdatedAtGmt}";
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
			Status = "Please enter email and password.";
			return;
		}

		await RunBusy(async () =>
		{
			var result = await _api.LoginAsync(BaseUrl, Email.Trim(), Password);
			_sessionAccessToken = result.AccessToken;
			_sessionRefreshToken = result.RefreshToken;
			IsLoggedIn = true;
			AccessTokenPreview = MaskToken(_sessionAccessToken, "access");
			RefreshTokenPreview = MaskToken(_sessionRefreshToken, "refresh");
			Password = string.Empty;
			Status = result.MfaRequired
				? "Login accepted. MFA required (stub API behavior)."
				: "Login successful.";
		});
	}

	public async Task LoadConversations()
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
			Status = "Please login first.";
			return;
		}

		if (string.IsNullOrWhiteSpace(ApiKey))
		{
			Status = "Please enter X-API-Key.";
			return;
		}

		await RunBusy(async () =>
		{
			var items = await _api.GetConversationsAsync(BaseUrl, ApiKey.Trim());
			Conversations.Clear();

			foreach (var item in items)
			{
				Conversations.Add(new ConversationItem(item.Id, item.SourceUrl, item.UpdatedAtGmt, item.UnreadCount));
			}

			SelectedConversation = Conversations.FirstOrDefault();
			Status = $"Loaded {Conversations.Count} conversation(s).";
		});
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
			Status = "Please login first.";
			return;
		}

		if (SelectedConversation is null)
		{
			Status = "Select a conversation first.";
			return;
		}

		var trimmedReply = ReplyMessage.Trim();

		if (string.IsNullOrWhiteSpace(trimmedReply))
		{
			Status = "Enter a reply message.";
			return;
		}

		if (trimmedReply.Length < 3)
		{
			Status = "Reply must be at least 3 characters.";
			return;
		}

		if (trimmedReply.Length > 2000)
		{
			Status = "Reply exceeds 2000 characters.";
			return;
		}

		await RunBusy(async () =>
		{
			var result = await _api.SendReplyAsync(BaseUrl, ApiKey.Trim(), SelectedConversation.Id, trimmedReply);
			_replyStore.Add(result.ConversationId, result.Sender, result.Message, result.TimeGmt);
			Status = $"Reply sent to {result.ConversationId} at {result.TimeGmt}.";
			ReplyMessage = string.Empty;
			SelectedConversationDetails = $"Conversation: {SelectedConversation.Id}\nSource: {SelectedConversation.SourceUrl}\nUnread: {SelectedConversation.UnreadCount}\nUpdated (GMT): {SelectedConversation.UpdatedAtGmt}\nLast reply: {result.TimeGmt}";
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
			Status = "Select a conversation first.";
			return;
		}

		if (string.IsNullOrWhiteSpace(ApiKey))
		{
			Status = "Please enter X-API-Key first.";
			return;
		}

		var context = new ConversationDetailsContext(BaseUrl.Trim(), ApiKey.Trim(), SelectedConversation);
		await _navigator.NavigateViewModelAsync<SecondModel>(this, data: context);
	}

	private bool TryValidateBaseUrl(out string error)
	{
		error = string.Empty;

		if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri))
		{
			error = "Base URL is invalid.";
			return false;
		}

		if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.Host is "127.0.0.1" or "localhost"))
		{
			error = "Use HTTPS (HTTP only allowed for localhost).";
			return false;
		}

		return true;
	}

	private static string MaskToken(string token, string label)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return $"{label}: <empty>";
		}

		var cleaned = token.Trim();
		if (cleaned.Length <= 12)
		{
			return $"{label}: {new string('*', cleaned.Length)}";
		}

		return $"{label}: {cleaned[..6]}...{cleaned[^6..]}";
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
