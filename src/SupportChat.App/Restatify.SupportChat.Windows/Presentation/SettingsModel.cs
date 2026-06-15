using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Restatify.SupportChat.Services.Settings;

namespace Restatify.SupportChat.Presentation;

public sealed class SettingsModel : INotifyPropertyChanged
{
	private const string FallbackLogoUri = "ms-appx:///Assets/Images/restatify_logo.png";
	private static readonly HttpClient LogoHttpClient = new();

	private readonly IConnectionSettingsStore _connectionSettingsStore;
	private readonly INavigator _navigator;
	private readonly IStringLocalizer _localizer;

	private string _baseUrl = string.Empty;
	private string _apiKey = string.Empty;
	private string _logoUrl = string.Empty;
	private string _logoPreviewUri = FallbackLogoUri;
	private int _previewRequestVersion;
	private string _selectedLanguageCode = "de";
	private string _status = "";

	public SettingsModel(string? from, IConnectionSettingsStore connectionSettingsStore, INavigator navigator, IStringLocalizer localizer)
	{
		_connectionSettingsStore = connectionSettingsStore;
		_navigator = navigator;
		_localizer = localizer;

		var current = _connectionSettingsStore.Load();
		_baseUrl = current.BaseUrl;
		_apiKey = current.ApiKey;
		_logoUrl = current.LogoUrl;
		_selectedLanguageCode = current.LanguageCode;
		SaveCommand = new AsyncRelayCommand(Save);
		_ = RefreshLogoPreviewAsync();
		Status = T("Status_SettingsLoaded", "Settings loaded.");
	}

	public event PropertyChangedEventHandler? PropertyChanged;
	public IAsyncRelayCommand SaveCommand { get; }

	public string BaseUrl
	{
		get => _baseUrl;
		set
		{
			if (SetProperty(ref _baseUrl, value))
			{
				_ = RefreshLogoPreviewAsync();
			}
		}
	}

	public string ApiKey
	{
		get => _apiKey;
		set => SetProperty(ref _apiKey, value);
	}

	public string LogoUrl
	{
		get => _logoUrl;
		set
		{
			if (SetProperty(ref _logoUrl, value))
			{
				_ = RefreshLogoPreviewAsync();
			}
		}
	}

	public string LogoPreviewUri
	{
		get => _logoPreviewUri;
		private set => SetProperty(ref _logoPreviewUri, value);
	}

public IReadOnlyList<LanguageOption> AvailableLanguages =>
	[
		new("de", T("LanguageName_de", "Deutsch")),
		new("en", T("LanguageName_en", "English")),
		new("fr", T("LanguageName_fr", "Francais")),
		new("es", T("LanguageName_es", "Espanol")),
		new("pt-BR", T("LanguageName_pt-BR", "Portugues (Brasil)"))
	];

	public string SelectedLanguageCode
	{
		get => _selectedLanguageCode;
		set => SetProperty(ref _selectedLanguageCode, value);
	}

	public string Status
	{
		get => _status;
		private set => SetProperty(ref _status, value);
	}

	public async Task Save()
	{
		if (string.IsNullOrWhiteSpace(BaseUrl))
		{
			Status = T("Error_BaseUrlRequired", "Base URL is required.");
			return;
		}

		_connectionSettingsStore.Save(new ConnectionSettings(BaseUrl, ApiKey, SelectedLanguageCode, LogoUrl));
		ConnectionSettingsStore.ApplyLanguage(SelectedLanguageCode);
		Status = T("Status_SettingsSaved", "Settings saved.");

		await _navigator.NavigateBackAsync(this);
	}

	private async Task RefreshLogoPreviewAsync()
	{
		var requestVersion = Interlocked.Increment(ref _previewRequestVersion);

		try
		{
			string previewUri;

			if (TryGetAbsoluteUri(LogoUrl, out var configuredLogoUri))
			{
				previewUri = configuredLogoUri;
			}
			else if (TryGetSiteRoot(BaseUrl, out var siteRoot))
			{
				previewUri = await TryResolveWordPressIconAsync(siteRoot) ?? $"{siteRoot}/favicon.ico";
			}
			else
			{
				previewUri = FallbackLogoUri;
			}

			if (requestVersion == _previewRequestVersion)
			{
				LogoPreviewUri = previewUri;
			}
		}
		catch
		{
			if (requestVersion == _previewRequestVersion)
			{
				LogoPreviewUri = FallbackLogoUri;
			}
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

	private string T(string key, string fallback)
	{
		var value = _localizer[key];
		return value.ResourceNotFound ? fallback : value.Value;
	}

	public sealed record LanguageOption(string Code, string DisplayName);
}
