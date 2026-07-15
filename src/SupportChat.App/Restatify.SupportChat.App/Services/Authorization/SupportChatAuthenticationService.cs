using Restatify.SupportChat.Services.Settings;
using Restatify.SupportChat.Services.Support;
using System.Net;
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

	public string? LastLoginError { get; private set; }

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
		LastLoginError = null;

		var token = cancellationToken ?? CancellationToken.None;
		var activeProvider = string.IsNullOrWhiteSpace(provider) ? ProviderName : provider;
		if (!string.Equals(activeProvider, ProviderName, StringComparison.OrdinalIgnoreCase))
		{
			LastLoginError = "Anmeldung für den gewählten Auth-Provider ist nicht verfügbar.";
			return false;
		}

		if (credentials is null)
		{
			LastLoginError = "Anmeldedaten fehlen.";
			return false;
		}

		if (!credentials.TryGetValue(CredentialUsernameKey, out var username) || string.IsNullOrWhiteSpace(username))
		{
			LastLoginError = "Bitte Benutzernamen eingeben.";
			return false;
		}

		if (!credentials.TryGetValue(CredentialPasswordKey, out var password) || string.IsNullOrWhiteSpace(password))
		{
			LastLoginError = "Bitte Passwort eingeben.";
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
			LastLoginError = "Die API-Base-URL ist ungültig.";
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
			LastLoginError = BuildFriendlyLoginError(ex, baseUrlOverride);
			return false;
		}
	}

	private static string BuildFriendlyLoginError(Exception ex, string baseUrl)
	{
		var endpointHint = BuildEndpointHint(baseUrl);

		if (ex is HttpRequestException httpEx)
		{
			if (httpEx.StatusCode == HttpStatusCode.Unauthorized)
			{
				return "Login fehlgeschlagen (401 Unauthorized). Bitte Benutzername und Passwort prüfen.";
			}

			if (httpEx.StatusCode == HttpStatusCode.Forbidden)
			{
				return "Login fehlgeschlagen (403 Forbidden). Zugriff wurde vom Server verweigert.";
			}

			if (httpEx.StatusCode is not null)
			{
				return $"Login fehlgeschlagen (HTTP {(int)httpEx.StatusCode} {httpEx.StatusCode}).";
			}

			return $"Verbindung zur API fehlgeschlagen ({baseUrl}). {httpEx.Message}{endpointHint}";
		}

		return $"Login fehlgeschlagen: {ex.Message}{endpointHint}";
	}

	private static string BuildEndpointHint(string baseUrl)
	{
		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
		{
			return string.Empty;
		}

		if (uri.Host == "10.0.0.2")
		{
			return " Hinweis: Im Android-Emulator ist die Host-Maschine unter 10.0.2.2 erreichbar (nicht 10.0.0.2).";
		}

		if (uri.Scheme == Uri.UriSchemeHttps && uri.Host is "10.0.2.2" or "127.0.0.1" or "localhost")
		{
			return $" Hinweis: Lokale Development-APIs laufen häufig ohne TLS. Versuche http://{uri.Host}:{uri.Port}.";
		}

		return string.Empty;
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
