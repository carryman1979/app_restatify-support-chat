namespace Restatify.SupportChat.Services.Settings;

public static class ThemeModes
{
	public const string System = "system";
	public const string Light = "light";
	public const string Dark = "dark";
}

public sealed record UserSettings(string BaseUrl, string ApiKey, string LanguageCode, string LogoUrl, string ThemeMode);

public interface IUserSettingsStore
{
	UserSettings Load();
	void Save(UserSettings settings);
}
