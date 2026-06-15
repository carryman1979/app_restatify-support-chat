namespace Restatify.SupportChat.Services.Settings;

public sealed record ConnectionSettings(string BaseUrl, string ApiKey, string LanguageCode, string LogoUrl);

public interface IConnectionSettingsStore
{
	ConnectionSettings Load();
	void Save(ConnectionSettings settings);
}
