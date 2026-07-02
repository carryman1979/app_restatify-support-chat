using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Restatify.SupportChat.Services.Authorization;
using Restatify.SupportChat.Services.Settings;
using Uno.Extensions.Authentication;
using Windows.Storage;
using Restatify.SupportChat.Presentation;

namespace Restatify.SupportChat.ViewModels;

public partial class LoginViewModel : ObservableObject
{
	private const string RememberLoginKey = "SupportChat.RememberLogin";
	private const string RememberedUsernameKey = "SupportChat.RememberedEmail";
	private const string RememberedPasswordKey = "SupportChat.RememberedPassword";
	private const string FallbackLogoUri = "ms-appx:///Assets/Images/restatify_logo.png";

	private readonly IAuthenticationService _authenticationService;
	private readonly IUserSettingsStore _userSettingsStore;
	private readonly INavigator _navigator;

	[ObservableProperty]
	private string? _baseUrl;

	[ObservableProperty]
	private string? _username;

	[ObservableProperty]
	private string? _password;

	[ObservableProperty]
	private bool _rememberLogin = true;

	[ObservableProperty]
	private bool _isBusy = false;

	[ObservableProperty]
	private string? _status = "Ready.";

	public LoginViewModel(
		IAuthenticationService authenticationService,
		IUserSettingsStore userSettingsStore,
		INavigator navigator)
	{
		App.TraceStartup("LoginViewModel: constructor start.");
		_authenticationService = authenticationService;
		_userSettingsStore = userSettingsStore;
		_navigator = navigator;
		var settings = _userSettingsStore.Load();
		BaseUrl = settings.BaseUrl;
		Login = new AsyncRelayCommand(DoLogin);
		LoadRememberedLogin();
		App.TraceStartup($"LoginViewModel: constructor completed. RememberLogin={RememberLogin}, UsernameHasValue={!string.IsNullOrWhiteSpace(Username)}");
	}

	public string Title { get; } = "Staff Login";
	public string LogoImageUri { get; } = FallbackLogoUri;

	public ICommand Login { get; }

	private async Task DoLogin()
	{
		if (IsBusy)
		{
			return;
		}

		if (BaseUrl == null)
		{
			Status = "Please configure the API base URL first.";
			return;
		}
		else if (!TryValidateBaseUrl(BaseUrl, out var baseUrlError))
		{
			Status = baseUrlError;
			return;
		}

		if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
		{
			Status = "Please enter username and password.";
			return;
		}

		try
		{
			IsBusy = true;
			Status = "Signing in...";

			var normalizedBaseUrl = BaseUrl.Trim();
			var credentials = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[SupportChatAuthenticationService.CredentialUsernameKey] = Username.Trim(),
				[SupportChatAuthenticationService.CredentialPasswordKey] = Password,
				[SupportChatAuthenticationService.CredentialBaseUrlKey] = normalizedBaseUrl,
			};

			var authenticated = await _authenticationService.LoginAsync(
				dispatcher: null,
				credentials: credentials,
				provider: SupportChatAuthenticationService.ProviderName,
				cancellationToken: CancellationToken.None);

			if (!authenticated)
			{
				Status = "Login failed.";
				return;
			}

			if (RememberLogin)
			{
				SaveRememberedLogin();
			}
			else
			{
				ClearRememberedLogin();
			}

			Password = string.Empty;
			Status = "Login successful.";
			await _navigator.NavigateViewModelAsync<MainViewModel>(this, qualifier: Qualifiers.ClearBackStack);
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

	private void LoadRememberedLogin()
	{
		var localSettings = ApplicationData.Current.LocalSettings;
		RememberLogin = localSettings.Values[RememberLoginKey] as bool? ?? false;

		if (!RememberLogin)
		{
			return;
		}

		Username = localSettings.Values[RememberedUsernameKey] as string ?? string.Empty;
		Password = localSettings.Values[RememberedPasswordKey] as string ?? string.Empty;
	}

	private void SaveRememberedLogin()
	{
		if (Username == null || Password == null)
		{
			Status = "Username or password cannot be null.";
			return;
		}

		var localSettings = ApplicationData.Current.LocalSettings;
		localSettings.Values[RememberLoginKey] = true;
		localSettings.Values[RememberedUsernameKey] = Username.Trim();
		localSettings.Values[RememberedPasswordKey] = Password;
	}

	private static void ClearRememberedLogin()
	{
		var localSettings = ApplicationData.Current.LocalSettings;
		localSettings.Values[RememberLoginKey] = false;
		localSettings.Values.Remove(RememberedUsernameKey);
		localSettings.Values.Remove(RememberedPasswordKey);
	}

	private static bool TryValidateBaseUrl(string candidate, out string error)
	{
		error = string.Empty;

		if (string.IsNullOrWhiteSpace(candidate))
		{
			error = "Please configure the API base URL first.";
			return false;
		}

		if (!Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri))
		{
			error = "The configured API base URL is invalid.";
			return false;
		}

		if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.Host is "127.0.0.1" or "localhost"))
		{
			error = "Use HTTPS (HTTP only allowed for localhost).";
			return false;
		}

		return true;
	}
}
