using System.Globalization;
using Windows.Globalization;
using Windows.Storage;

namespace Restatify.SupportChat.Services.Settings;

public sealed class ConnectionSettingsStore : IConnectionSettingsStore
{
	private const string BaseUrlKey = "SupportChat.BaseUrl";
	private const string ApiKeyKey = "SupportChat.ApiKey";
	private const string LanguageCodeKey = "SupportChat.LanguageCode";
	private const string LogoUrlKey = "SupportChat.LogoUrl";
	private const string DefaultBaseUrl = "http://127.0.0.1:8089";
	private const string DefaultApiKey = "";
	private const string DefaultLanguageCode = "de";
	private static readonly HashSet<string> SupportedLanguageCodes = new(StringComparer.OrdinalIgnoreCase)
	{
		"de",
		"en",
		"fr",
		"es",
		"pt-BR",
	};

	public ConnectionSettings Load()
	{
		var localSettings = ApplicationData.Current.LocalSettings;

		var baseUrl = localSettings.Values[BaseUrlKey] as string;
		var apiKey = localSettings.Values[ApiKeyKey] as string;
		var languageCode = localSettings.Values[LanguageCodeKey] as string;
		var logoUrl = localSettings.Values[LogoUrlKey] as string;

		return new ConnectionSettings(
			string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl,
			string.IsNullOrWhiteSpace(apiKey) ? DefaultApiKey : apiKey,
			NormalizeLanguageCode(languageCode),
			string.IsNullOrWhiteSpace(logoUrl) ? string.Empty : logoUrl.Trim()
		);
	}

	public void Save(ConnectionSettings settings)
	{
		var localSettings = ApplicationData.Current.LocalSettings;
		localSettings.Values[BaseUrlKey] = settings.BaseUrl.Trim();
		localSettings.Values[ApiKeyKey] = settings.ApiKey.Trim();
		localSettings.Values[LanguageCodeKey] = NormalizeLanguageCode(settings.LanguageCode);
		localSettings.Values[LogoUrlKey] = settings.LogoUrl.Trim();
	}

	public static void ApplyLanguage(string languageCode)
	{
		var normalized = NormalizeLanguageCode(languageCode);
		ApplicationLanguages.PrimaryLanguageOverride = normalized;

		var culture = CultureInfo.GetCultureInfo(normalized);
		CultureInfo.CurrentCulture = culture;
		CultureInfo.CurrentUICulture = culture;
		CultureInfo.DefaultThreadCurrentCulture = culture;
		CultureInfo.DefaultThreadCurrentUICulture = culture;
	}

	private static string NormalizeLanguageCode(string? languageCode)
	{
		if (string.IsNullOrWhiteSpace(languageCode))
		{
			return DefaultLanguageCode;
		}

		var trimmed = languageCode.Trim();
		return SupportedLanguageCodes.Contains(trimmed)
			? trimmed
			: DefaultLanguageCode;
	}
}
