using Restatify.SupportChat.Services.Settings;
using Restatify.SupportChat.Services.Support;
using Uno.Extensions.Authentication;

namespace Restatify.SupportChat.Services.Authorization;

public sealed class SupportChatAuthenticationService : IAuthenticationService
{
	public const string ProviderName = "supportchat";
	public const string CredentialUsernameKey = "username";
	public const string CredentialPasswordKey = "password";
	public const string CredentialBaseUrlKey = "baseUrl";

	private readonly ISupportChatApiClient _api;
	private readonly IUserSettingsStore _userSettingsStore;
	private readonly ILogger<SupportChatAuthenticationService> _logger;

	public SupportChatAuthenticationService(
		ISupportChatApiClient api,
		IUserSettingsStore userSettingsStore,
		ILogger<SupportChatAuthenticationService> logger)
	{
		_api = api;
		_userSettingsStore = userSettingsStore;
		_logger = logger;
	}

	public string[] Providers { get; } = [ProviderName];

	public event EventHandler? LoggedOut;

	public async ValueTask<bool> LoginAsync(
		IDispatcher? dispatcher,
		IDictionary<string, string>? credentials,
		string? provider,
		CancellationToken? cancellationToken = null)
	{
		var token = cancellationToken ?? CancellationToken.None;
		var activeProvider = string.IsNullOrWhiteSpace(provider) ? ProviderName : provider;
		if (!string.Equals(activeProvider, ProviderName, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (credentials is null)
		{
			return false;
		}

		if (!credentials.TryGetValue(CredentialUsernameKey, out var username) || string.IsNullOrWhiteSpace(username)
			|| !credentials.TryGetValue(CredentialPasswordKey, out var password) || string.IsNullOrWhiteSpace(password))
		{
			return false;
		}

		var currentSettings = _userSettingsStore.Load();
		var baseUrl = currentSettings.BaseUrl.Trim();
		if (!credentials.TryGetValue(CredentialBaseUrlKey, out var baseUrlOverride) || string.IsNullOrWhiteSpace(baseUrlOverride))
		{
			baseUrlOverride = baseUrl;
		}
		else
		{
			baseUrlOverride = baseUrlOverride.Trim();
		}

		if (!Uri.TryCreate(baseUrlOverride, UriKind.Absolute, out _))
		{
			return false;
		}

		try
		{
			var loginResult = await _api.LoginAsync(baseUrlOverride, username.Trim(), password, token);
			var apiKey = await _api.GenerateApiKeyAsync(baseUrlOverride, loginResult.AccessToken, token);

			_userSettingsStore.Save(currentSettings with
			{
				BaseUrl = baseUrlOverride,
				ApiKey = apiKey,
			});

			return true;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Authentication login failed for endpoint {BaseUrl}.", baseUrlOverride);
			return false;
		}
	}

	public async ValueTask<bool> RefreshAsync(CancellationToken? cancellationToken = null)
	{
		var token = cancellationToken ?? CancellationToken.None;
		var settings = _userSettingsStore.Load();
		var normalizedBaseUrl = settings.BaseUrl.Trim();
		var hasBaseUrl = !string.IsNullOrWhiteSpace(normalizedBaseUrl)
			&& Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out _);
		var hasApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);
		if (!hasBaseUrl || !hasApiKey)
		{
			return false;
		}

		try
		{
			await _api.GetConversationsAsync(normalizedBaseUrl, settings.ApiKey.Trim(), token);
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogInformation(ex, "Authentication refresh check failed for endpoint {BaseUrl}.", normalizedBaseUrl);
			return false;
		}
	}

	public ValueTask<bool> IsAuthenticated(CancellationToken? cancellationToken = null)
		=> RefreshAsync(cancellationToken);

	public ValueTask<bool> LogoutAsync(IDispatcher? dispatcher, CancellationToken? cancellationToken = null)
	{
		try
		{
			var settings = _userSettingsStore.Load();
			_userSettingsStore.Save(settings with { ApiKey = string.Empty });
			LoggedOut?.Invoke(this, EventArgs.Empty);
			return ValueTask.FromResult(true);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Authentication logout failed.");
			return ValueTask.FromResult(false);
		}
	}
}
