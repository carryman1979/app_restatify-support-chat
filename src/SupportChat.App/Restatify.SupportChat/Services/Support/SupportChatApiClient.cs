using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Restatify.SupportChat.Services.Support;

public sealed class SupportChatApiClient : ISupportChatApiClient
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	public async Task<LoginResult> LoginAsync(string baseUrl, string email, string password, CancellationToken cancellationToken = default)
	{
		using var client = CreateClient(baseUrl);
		var payload = new LoginRequest(email, password);
		using var response = await client.PostAsJsonAsync("/v1/auth/login", payload, JsonOptions, cancellationToken);
		await EnsureSuccess(response, cancellationToken);

		var result = await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions, cancellationToken)
			?? throw new InvalidOperationException("Login response was empty.");

		return new LoginResult(result.AccessToken, result.RefreshToken, result.MfaRequired);
	}

	public async Task<IReadOnlyList<ConversationSummary>> GetConversationsAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default)
	{
		using var client = CreateClient(baseUrl, apiKey);
		using var response = await client.GetAsync("/v1/support/conversations", cancellationToken);
		await EnsureSuccess(response, cancellationToken);

		var result = await response.Content.ReadFromJsonAsync<List<ConversationResponse>>(JsonOptions, cancellationToken)
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

		var result = await response.Content.ReadFromJsonAsync<ConversationMessagesPageResponse>(JsonOptions, cancellationToken)
			?? new ConversationMessagesPageResponse();

		var items = (result.Items ?? [])
			.Select(item => new ConversationMessage(item.MessageId, item.ConversationId, item.Sender, item.Message, item.TimeGmt))
			.ToArray();

		return new ConversationMessagesPage(items, result.NextCursor, result.HasMore);
	}

	public async Task<ReplyResult> SendReplyAsync(string baseUrl, string apiKey, string conversationId, string message, CancellationToken cancellationToken = default)
	{
		using var client = CreateClient(baseUrl, apiKey);
		var payload = new ReplyRequest(message);
		using var response = await client.PostAsJsonAsync($"/v1/support/conversations/{conversationId}/reply", payload, JsonOptions, cancellationToken);
		await EnsureSuccess(response, cancellationToken);

		var result = await response.Content.ReadFromJsonAsync<ReplyResponse>(JsonOptions, cancellationToken)
			?? throw new InvalidOperationException("Reply response was empty.");

		return new ReplyResult(result.ConversationId, result.Sender, result.Message, result.TimeGmt);
	}

	private static HttpClient CreateClient(string baseUrl, string? apiKey = null)
	{
		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
		{
			throw new InvalidOperationException("Base URL is invalid.");
		}

		var client = new HttpClient
		{
			BaseAddress = uri,
		};

		if (!string.IsNullOrWhiteSpace(apiKey))
		{
			client.DefaultRequestHeaders.Add("X-API-Key", apiKey.Trim());
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

	private sealed partial record LoginRequest(string Email, string Password);
	private sealed partial record ReplyRequest(string Message);

	private sealed class LoginResponse
	{
		[JsonPropertyName("access_token")]
		public string AccessToken { get; set; } = string.Empty;

		[JsonPropertyName("refresh_token")]
		public string RefreshToken { get; set; } = string.Empty;

		[JsonPropertyName("mfa_required")]
		public bool MfaRequired { get; set; }
	}

	private sealed class ConversationResponse
	{
		[JsonPropertyName("id")]
		public string Id { get; set; } = string.Empty;

		[JsonPropertyName("source_url")]
		public string SourceUrl { get; set; } = string.Empty;

		[JsonPropertyName("updated_at_gmt")]
		public string UpdatedAtGmt { get; set; } = string.Empty;

		[JsonPropertyName("unread_count")]
		public int UnreadCount { get; set; }
	}

	private sealed class ReplyResponse
	{
		[JsonPropertyName("conversation_id")]
		public string ConversationId { get; set; } = string.Empty;

		[JsonPropertyName("sender")]
		public string Sender { get; set; } = string.Empty;

		[JsonPropertyName("message")]
		public string Message { get; set; } = string.Empty;

		[JsonPropertyName("time_gmt")]
		public string TimeGmt { get; set; } = string.Empty;
	}

	private sealed class ConversationMessageResponse
	{
		[JsonPropertyName("message_id")]
		public string MessageId { get; set; } = string.Empty;

		[JsonPropertyName("conversation_id")]
		public string ConversationId { get; set; } = string.Empty;

		[JsonPropertyName("sender")]
		public string Sender { get; set; } = string.Empty;

		[JsonPropertyName("message")]
		public string Message { get; set; } = string.Empty;

		[JsonPropertyName("time_gmt")]
		public string TimeGmt { get; set; } = string.Empty;
	}

	private sealed class ConversationMessagesPageResponse
	{
		[JsonPropertyName("items")]
		public List<ConversationMessageResponse>? Items { get; set; }

		[JsonPropertyName("next_cursor")]
		public string? NextCursor { get; set; }

		[JsonPropertyName("has_more")]
		public bool HasMore { get; set; }
	}
}
