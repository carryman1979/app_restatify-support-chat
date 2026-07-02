using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Uno.Extensions;
using Uno.Extensions.Toolkit;
using Windows.Globalization;

namespace Restatify.SupportChat.Services.Settings;

public sealed partial class UserSettingsStore : IUserSettingsStore
{
	#if ANDROID
	private const string DefaultHttpApiHost = "10.0.2.2";
	#else
	private const string DefaultHttpApiHost = "127.0.0.1";
	#endif
	public const int DefaultHttpApiPort = 8089;
	private static readonly string DefaultBaseUrl = $"http://{DefaultHttpApiHost}:{DefaultHttpApiPort}";
	private const string SettingsFileName = "supportchat.settings.json";
	private const string DefaultApiKey = "";
	private const string DefaultLanguageCode = "de";
	private const string DefaultThemeMode = ThemeModes.System;
	private static readonly HashSet<string> SupportedLanguageCodes = new(StringComparer.OrdinalIgnoreCase)
	{
		"de",
		"en",
		"fr",
		"es",
		"pt-BR",
	};

	public UserSettings Load()
	{
		var persisted = LoadPersistedSettings();

		return new UserSettings(
			NormalizeBaseUrl(persisted?.BaseUrl),
			string.IsNullOrWhiteSpace(persisted?.ApiKey) ? DefaultApiKey : persisted.ApiKey.Trim(),
			NormalizeLanguageCode(persisted?.LanguageCode),
			string.IsNullOrWhiteSpace(persisted?.LogoUrl) ? string.Empty : persisted.LogoUrl.Trim(),
			NormalizeThemeMode(persisted?.ThemeMode)
		);
	}

	public void Save(UserSettings settings)
	{
		var persisted = new PersistedConnectionSettings
		{
			BaseUrl = NormalizeBaseUrl(settings.BaseUrl),
			ApiKey = settings.ApiKey.Trim(),
			LanguageCode = NormalizeLanguageCode(settings.LanguageCode),
			LogoUrl = settings.LogoUrl.Trim(),
			ThemeMode = NormalizeThemeMode(settings.ThemeMode),
		};

		SavePersistedSettings(persisted);
	}

	public static string GetSettingsFilePath()
	{
		var directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		return Path.Combine(directory, SettingsFileName);
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

	public static async Task ApplyTheme(string themeMode)
	{
		var normalizedThemeMode = NormalizeThemeMode(themeMode);
		var appTheme = normalizedThemeMode switch
		{
			ThemeModes.Light => AppTheme.Light,
			ThemeModes.Dark => AppTheme.Dark,
			_ => AppTheme.System,
		};

		if (App.MainWindow is { } window)
		{
			try
			{
				var themeService = window.GetThemeService();
				await themeService.SetThemeAsync(appTheme);
				return;
			}
			catch (NotSupportedException)
			{
				// Fall back to direct element theming when theme service is not available.
			}
			catch (InvalidOperationException)
			{
				// Fall back to direct element theming when app/window state is not ready.
			}
		}

		if (Application.Current is not { } app)
		{
			return;
		}

		try
		{
			if (normalizedThemeMode == ThemeModes.Light)
			{
				app.RequestedTheme = ApplicationTheme.Light;
			}
			else if (normalizedThemeMode == ThemeModes.Dark)
			{
				app.RequestedTheme = ApplicationTheme.Dark;
			}
		}
		catch (NotSupportedException)
		{
			// Some Uno/WinUI targets do not support setting Application.RequestedTheme at runtime.
		}
		catch (InvalidOperationException)
		{
			// Keep startup resilient when platform theme APIs are not available in current app state.
		}

		if (App.MainWindow?.Content is FrameworkElement rootContent)
		{
			rootContent.RequestedTheme = normalizedThemeMode switch
			{
				ThemeModes.Light => ElementTheme.Light,
				ThemeModes.Dark => ElementTheme.Dark,
				_ => ElementTheme.Default,
			};
		}
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

	private static string NormalizeThemeMode(string? themeMode)
	{
		if (string.IsNullOrWhiteSpace(themeMode))
		{
			return DefaultThemeMode;
		}

		return themeMode.Trim().ToLowerInvariant() switch
		{
			ThemeModes.Light => ThemeModes.Light,
			ThemeModes.Dark => ThemeModes.Dark,
			ThemeModes.System => ThemeModes.System,
			_ => DefaultThemeMode,
		};
	}

	private static string NormalizeBaseUrl(string? baseUrl)
	{
		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			return DefaultBaseUrl;
		}

		var trimmed = baseUrl.Trim();
		if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
		{
			return trimmed;
		}

		if (uri.Scheme == Uri.UriSchemeHttp && !HasExplicitPort(trimmed))
		{
			var builder = new UriBuilder(uri)
			{
				Port = DefaultHttpApiPort,
			};

			return builder.Uri.AbsoluteUri.TrimEnd('/');
		}

		return trimmed;
	}

	private static bool HasExplicitPort(string absoluteUri)
	{
		var schemeSeparatorIndex = absoluteUri.IndexOf("://", StringComparison.Ordinal);
		if (schemeSeparatorIndex < 0)
		{
			return false;
		}

		var authorityStart = schemeSeparatorIndex + 3;
		var authorityEnd = absoluteUri.IndexOfAny(['/', '?', '#'], authorityStart);
		if (authorityEnd < 0)
		{
			authorityEnd = absoluteUri.Length;
		}

		var authority = absoluteUri[authorityStart..authorityEnd];
		var atIndex = authority.LastIndexOf('@');
		if (atIndex >= 0)
		{
			authority = authority[(atIndex + 1)..];
		}

		if (authority.StartsWith('['))
		{
			var bracketEnd = authority.IndexOf(']');
			return bracketEnd >= 0
				&& bracketEnd + 1 < authority.Length
				&& authority[bracketEnd + 1] == ':';
		}

		return authority.Contains(':');
	}

	private static PersistedConnectionSettings? LoadPersistedSettings()
	{
		var path = GetSettingsFilePath();
		if (!File.Exists(path))
		{
			return null;
		}

		try
		{
			var json = File.ReadAllText(path);
			if (string.IsNullOrWhiteSpace(json))
			{
				return null;
			}

			return JsonSerializer.Deserialize(
				json,
				UserSettingsStoreJsonContext.Default.PersistedConnectionSettings);
		}
		catch (JsonException)
		{
			return null;
		}
		catch (IOException)
		{
			return null;
		}
	}

	private static void SavePersistedSettings(PersistedConnectionSettings settings)
	{
		var path = GetSettingsFilePath();
		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		var json = JsonSerializer.Serialize(
			settings,
			UserSettingsStoreJsonContext.Default.PersistedConnectionSettings);
		File.WriteAllText(path, json);
	}

	private sealed class PersistedConnectionSettings
	{
		public string? BaseUrl { get; init; }
		public string? ApiKey { get; init; }
		public string? LanguageCode { get; init; }
		public string? LogoUrl { get; init; }
		public string? ThemeMode { get; init; }
	}

	[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
	[JsonSerializable(typeof(PersistedConnectionSettings))]
	private sealed partial class UserSettingsStoreJsonContext : JsonSerializerContext
	{
	}

}
