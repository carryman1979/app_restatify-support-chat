using System.Net.Http.Json;
using System.Text.Json;

namespace Restatify.SupportChat.Services.Support;

public sealed class SupportChatApiClient : ISupportChatApiClient
{
	private const string LocalDevApiKeyFallback = "dev-support-api-key";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	private string? _accessToken;

	public async Task<LoginResult> LoginAsync(string baseUrl, string email, string password, CancellationToken cancellationToken = default)
	{
		using var client = CreateClient(baseUrl);
		var payload = new LoginRequestDto(email, password);
		using var response = await client.PostAsJsonAsync("/v1/auth/login", payload, JsonOptions, cancellationToken);
		await EnsureSuccess(response, cancellationToken);

		var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>(JsonOptions, cancellationToken)
			?? throw new InvalidOperationException("Login response was empty.");

		_accessToken = result.AccessToken;

		return new LoginResult(result.AccessToken, result.RefreshToken, result.MfaRequired);
	}

	public async Task<string> GenerateApiKeyAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
	{
		using var client = CreateClient(baseUrl);
		client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
		using var response = await client.PostAsync("/v1/auth/generate-api-key", content: null, cancellationToken);
		await EnsureSuccess(response, cancellationToken);

		var result = await response.Content.ReadFromJsonAsync<GenerateApiKeyResponseDto>(JsonOptions, cancellationToken)
			?? throw new InvalidOperationException("Generate API key response was empty.");

		return result.ApiKey;
	}

	public async Task<IReadOnlyList<ConversationSummary>> GetConversationsAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default)
	{
		using var client = CreateClient(baseUrl, apiKey);
		using var response = await client.GetAsync("/v1/support/conversations", cancellationToken);
		await EnsureSuccess(response, cancellationToken);

		var result = await response.Content.ReadFromJsonAsync<List<ConversationSummaryDto>>(JsonOptions, cancellationToken)
			?? [];

		return result
			.Select(item => new ConversationSummary(item.Id, item.SourceUrl, item.UpdatedAtGmt, item.UnreadCount))
			.ToArray();
	}

	public async Task<ConversationMessagesPage> GetConversationMessagesAsync(
		string baseUrl,
		string apiKey,
		string conversationId,
		string? cursor = null,
		int limit = 50,
		bool newestFirst = true,
		CancellationToken cancellationToken = default)
	{
		using var client = CreateClient(baseUrl, apiKey);
		var order = newestFirst ? "desc" : "asc";
		var cursorSegment = string.IsNullOrWhiteSpace(cursor)
			? string.Empty
			: $"&cursor={Uri.EscapeDataString(cursor)}";
		using var response = await client.GetAsync($"/v1/support/conversations/{conversationId}/messages?limit={limit}&order={order}{cursorSegment}", cancellationToken);
		await EnsureSuccess(response, cancellationToken);

		var result = await response.Content.ReadFromJsonAsync<ConversationMessagesPageDto>(JsonOptions, cancellationToken)
			?? new ConversationMessagesPageDto([], null, false);

		var items = (result.Items ?? [])
			.Select(item => new ConversationMessage(item.MessageId, item.ConversationId, item.Sender, item.Message, item.TimeGmt))
			.ToArray();

		return new ConversationMessagesPage(items, result.NextCursor, result.HasMore);
	}

	public async Task<ReplyResult> SendReplyAsync(string baseUrl, string apiKey, string conversationId, string message, CancellationToken cancellationToken = default)
	{
		using var client = CreateClient(baseUrl, apiKey);
		var payload = new ReplyRequestDto(message);
		using var response = await client.PostAsJsonAsync($"/v1/support/conversations/{conversationId}/reply", payload, JsonOptions, cancellationToken);
		await EnsureSuccess(response, cancellationToken);

		var result = await response.Content.ReadFromJsonAsync<ReplyResponseDto>(JsonOptions, cancellationToken)
			?? throw new InvalidOperationException("Reply response was empty.");

		return new ReplyResult(result.ConversationId, result.Sender, result.Message, result.TimeGmt);
	}

	private HttpClient CreateClient(string baseUrl, string? apiKey = null)
	{
		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
		{
			throw new InvalidOperationException("Base URL is invalid.");
		}

		var client = new HttpClient
		{
			BaseAddress = uri,
		};

		if (!string.IsNullOrWhiteSpace(_accessToken))
		{
			client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
		}

		var effectiveApiKey = apiKey?.Trim();
		if (string.IsNullOrWhiteSpace(effectiveApiKey)
			&& uri.Host is "localhost" or "127.0.0.1")
		{
			effectiveApiKey = LocalDevApiKeyFallback;
		}

		if (!string.IsNullOrWhiteSpace(effectiveApiKey))
		{
			client.DefaultRequestHeaders.Add("X-API-Key", effectiveApiKey);
		}

		return client;
	}

	private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}

		var body = await response.Content.ReadAsStringAsync(cancellationToken);

			try
			{
				using var document = JsonDocument.Parse(body);
				if (document.RootElement.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.Object)
				{
					var code = detailElement.TryGetProperty("code", out var codeElement)
						? codeElement.GetString()
						: null;
					var message = detailElement.TryGetProperty("message", out var messageElement)
						? messageElement.GetString()
						: null;

					if (code == "cursor_expired")
					{
						throw new CursorExpiredException(message);
					}
				}
			}
			catch (JsonException)
			{
				// Ignore parsing issues and fall back to generic HTTP exception.
			}

		throw new HttpRequestException($"Request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}");
	}

}
