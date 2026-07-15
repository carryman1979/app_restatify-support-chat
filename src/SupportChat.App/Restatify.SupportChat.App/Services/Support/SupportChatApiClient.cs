using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Restatify.SupportChat.DataContracts.Serialization;

namespace Restatify.SupportChat.Services.Support;

public sealed class SupportChatApiClient : ISupportChatApiClient
{
	private const string LocalDevApiKeyFallback = "dev-support-api-key";
	private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(20);
	private static readonly TimeSpan ConnectionRetryDelay = TimeSpan.FromMilliseconds(350);
	private readonly ILogger<SupportChatApiClient> _logger;

	private string? _accessToken;

	public SupportChatApiClient(ILogger<SupportChatApiClient> logger)
	{
		_logger = logger;
	}

	public async Task<LoginResult> LoginAsync(string baseUrl, string username, string password, CancellationToken cancellationToken = default)
	{
		var payload = new LoginRequestDto(username, password);
		var result = await ExecuteAgainstConfiguredEndpointAsync(
			baseUrl,
			async client =>
			{
				using var response = await client.PostAsJsonAsync("v1/auth/login", payload, SupportApiContractsContext.Default.LoginRequestDto, cancellationToken);
				await EnsureSuccess(response, cancellationToken);

				return await response.Content.ReadFromJsonAsync(SupportApiContractsContext.Default.LoginResponseDto, cancellationToken)
					?? throw new InvalidOperationException("Login response was empty.");
			},
			apiKey: null,
			cancellationToken);

		_accessToken = result.AccessToken;

		return new LoginResult(result.AccessToken, result.RefreshToken, result.MfaRequired);
	}

	public async Task<string> GenerateApiKeyAsync(string baseUrl, string bearerToken, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteAgainstConfiguredEndpointAsync(
			baseUrl,
			async client =>
			{
				client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
				using var response = await client.PostAsync("v1/auth/generate-api-key", content: null, cancellationToken);
				await EnsureSuccess(response, cancellationToken);

				return await response.Content.ReadFromJsonAsync(SupportApiContractsContext.Default.GenerateApiKeyResponseDto, cancellationToken)
					?? throw new InvalidOperationException("Generate API key response was empty.");
			},
			apiKey: null,
			cancellationToken);

		return result.ApiKey;
	}

	public async Task<IReadOnlyList<ConversationSummary>> GetConversationsAsync(string baseUrl, string apiKey, CancellationToken cancellationToken = default)
	{
		return await ExecuteAgainstConfiguredEndpointAsync(
			baseUrl,
			async client =>
			{
				using var response = await client.GetAsync("v1/support/conversations", cancellationToken);
				await EnsureSuccess(response, cancellationToken);

				var result = await response.Content.ReadFromJsonAsync(SupportApiContractsContext.Default.ListConversationSummaryDto, cancellationToken)
					?? [];

				return result
					.Select(item => new ConversationSummary(item.Id, item.SourceUrl, item.UpdatedAtGmt, item.UnreadCount))
					.ToArray();
			},
			apiKey,
			cancellationToken);
	}

	public async Task SubscribeSupportEventsAsync(
		string baseUrl,
		string apiKey,
		string? conversationId,
		Func<SupportLiveEvent, Task> onEvent,
		CancellationToken cancellationToken = default)
	{
		if (onEvent is null)
		{
			throw new ArgumentNullException(nameof(onEvent));
		}

		var effectiveApiKey = ResolveEffectiveApiKey(baseUrl, apiKey);
		var wsUri = BuildSupportUpdatesWebSocketUri(baseUrl, effectiveApiKey ?? string.Empty, conversationId);
		using var socket = new ClientWebSocket();
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
		if (!string.IsNullOrWhiteSpace(effectiveApiKey))
		{
			socket.Options.SetRequestHeader("X-API-Key", effectiveApiKey);
		}

		await socket.ConnectAsync(wsUri, cancellationToken);

		var buffer = new byte[8192];
		while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
		{
			using var memory = new MemoryStream();
			WebSocketReceiveResult receiveResult;
			do
			{
				receiveResult = await socket.ReceiveAsync(buffer, cancellationToken);
				if (receiveResult.MessageType == WebSocketMessageType.Close)
				{
					return;
				}

				if (receiveResult.Count > 0)
				{
					memory.Write(buffer, 0, receiveResult.Count);
				}
			} while (!receiveResult.EndOfMessage);

			if (receiveResult.MessageType != WebSocketMessageType.Text)
			{
				continue;
			}

			var payload = Encoding.UTF8.GetString(memory.ToArray());
			var evt = TryParseSupportLiveEvent(payload);
			if (evt is not null)
			{
				await onEvent(evt);
			}
		}
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
		var order = newestFirst ? "desc" : "asc";
		var cursorSegment = string.IsNullOrWhiteSpace(cursor)
			? string.Empty
			: $"&cursor={Uri.EscapeDataString(cursor)}";

		return await ExecuteAgainstConfiguredEndpointAsync(
			baseUrl,
			async client =>
			{
				using var response = await client.GetAsync($"v1/support/conversations/{conversationId}/messages?limit={limit}&order={order}{cursorSegment}", cancellationToken);
				await EnsureSuccess(response, cancellationToken);

				var result = await response.Content.ReadFromJsonAsync(SupportApiContractsContext.Default.ConversationMessagesPageDto, cancellationToken)
					?? new ConversationMessagesPageDto([], null, false);

				var items = (result.Items ?? [])
					.Select(item => new ConversationMessage(item.MessageId, item.ConversationId, item.Sender, item.Message, item.TimeGmt))
					.ToArray();

				return new ConversationMessagesPage(items, result.NextCursor, result.HasMore);
			},
			apiKey,
			cancellationToken);
	}

	public async Task<ReplyResult> SendReplyAsync(string baseUrl, string apiKey, string conversationId, string message, CancellationToken cancellationToken = default)
	{
		var payload = new ReplyRequestDto(message);
		var result = await ExecuteAgainstConfiguredEndpointAsync(
			baseUrl,
			async client =>
			{
				using var response = await client.PostAsJsonAsync($"v1/support/conversations/{conversationId}/reply", payload, SupportApiContractsContext.Default.ReplyRequestDto, cancellationToken);
				await EnsureSuccess(response, cancellationToken);

				return await response.Content.ReadFromJsonAsync(SupportApiContractsContext.Default.ReplyResponseDto, cancellationToken)
					?? throw new InvalidOperationException("Reply response was empty.");
			},
			apiKey,
			cancellationToken);

		return new ReplyResult(result.ConversationId, result.Sender, result.Message, result.TimeGmt);
	}

	public async Task<ConversationToolsResult> GetConversationToolsAsync(string baseUrl, string apiKey, string conversationId, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteAgainstConfiguredEndpointAsync(
			baseUrl,
			async client =>
			{
				using var response = await client.GetAsync($"v1/support/conversations/{conversationId}/tools", cancellationToken);
				await EnsureSuccess(response, cancellationToken);

				return await response.Content.ReadFromJsonAsync(SupportApiContractsContext.Default.ConversationToolsDto, cancellationToken)
					?? throw new InvalidOperationException("Conversation tools response was empty.");
			},
			apiKey,
			cancellationToken);

		return new ConversationToolsResult(result.ConversationId, result.AiMode, result.BookingOverlayAvailable);
	}

	public async Task<ConversationToolsResult> SetConversationAiModeAsync(string baseUrl, string apiKey, string conversationId, string aiMode, CancellationToken cancellationToken = default)
	{
		var payload = new SetConversationAiModeRequestDto(aiMode);
		var result = await ExecuteAgainstConfiguredEndpointAsync(
			baseUrl,
			async client =>
			{
				using var response = await client.PutAsJsonAsync($"v1/support/conversations/{conversationId}/ai-mode", payload, SupportApiContractsContext.Default.SetConversationAiModeRequestDto, cancellationToken);
				await EnsureSuccess(response, cancellationToken);

				return await response.Content.ReadFromJsonAsync(SupportApiContractsContext.Default.SetConversationAiModeResponseDto, cancellationToken)
					?? throw new InvalidOperationException("Set AI mode response was empty.");
			},
			apiKey,
			cancellationToken);

		return new ConversationToolsResult(result.ConversationId, result.AiMode, false);
	}

	public async Task<DeleteConversationResult> DeleteConversationAsync(string baseUrl, string apiKey, string conversationId, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteAgainstConfiguredEndpointAsync(
			baseUrl,
			async client =>
			{
				using var response = await client.DeleteAsync($"v1/support/conversations/{conversationId}", cancellationToken);
				await EnsureSuccess(response, cancellationToken);

				return await response.Content.ReadFromJsonAsync(SupportApiContractsContext.Default.DeleteConversationResponseDto, cancellationToken)
					?? throw new InvalidOperationException("Delete conversation response was empty.");
			},
			apiKey,
			cancellationToken);

		return new DeleteConversationResult(result.Deleted, result.AlreadyGone);
	}

	public async Task<ReplyResult> OpenBookingOverlayAsync(string baseUrl, string apiKey, string conversationId, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteAgainstConfiguredEndpointAsync(
			baseUrl,
			async client =>
			{
				using var response = await client.PostAsync($"v1/support/conversations/{conversationId}/open-booking-overlay", content: null, cancellationToken);
				await EnsureSuccess(response, cancellationToken);

				return await response.Content.ReadFromJsonAsync(SupportApiContractsContext.Default.ReplyResponseDto, cancellationToken)
					?? throw new InvalidOperationException("Open booking overlay response was empty.");
			},
			apiKey,
			cancellationToken);

		return new ReplyResult(result.ConversationId, result.Sender, result.Message, result.TimeGmt);
	}

	private async Task<T> ExecuteAgainstConfiguredEndpointAsync<T>(
		string baseUrl,
		Func<HttpClient, Task<T>> action,
		string? apiKey,
		CancellationToken cancellationToken)
	{
		var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);

		async Task<T> ExecuteOnceAsync()
		{
			using var client = CreateClient(normalizedBaseUrl, apiKey);
			return await action(client);
		}

		try
		{
			return await ExecuteOnceAsync();
		}
		catch (Exception ex) when (IsConnectionSetupError(ex) && !cancellationToken.IsCancellationRequested)
		{
			_logger.LogWarning(ex, "Transient connection issue for endpoint {Endpoint}. Retrying once.", normalizedBaseUrl);
			await Task.Delay(ConnectionRetryDelay, cancellationToken);

			try
			{
				return await ExecuteOnceAsync();
			}
			catch (Exception retryEx) when (IsConnectionSetupError(retryEx) && !cancellationToken.IsCancellationRequested)
			{
				_logger.LogError(retryEx, "Connection setup failed for endpoint {Endpoint} after retry.", normalizedBaseUrl);
				throw CreateConnectionFailedException(retryEx, normalizedBaseUrl);
			}
		}
	}

	private static bool IsConnectionSetupError(Exception ex)
	{
		if (ex is HttpRequestException httpRequestException)
		{
			if (httpRequestException.InnerException is SocketException socketException)
			{
				return socketException.SocketErrorCode is SocketError.ConnectionRefused
					or SocketError.ConnectionReset
					or SocketError.HostNotFound
					or SocketError.HostUnreachable
					or SocketError.NetworkDown
					or SocketError.NetworkUnreachable
					or SocketError.TimedOut;
			}

			return false;
		}

		return ex is TaskCanceledException;
	}

	private static HttpRequestException CreateConnectionFailedException(Exception ex, string endpoint)
	{
		var hint = TryGetLegacyLocalPortHint(endpoint);
		var details = string.IsNullOrWhiteSpace(hint)
			? string.Empty
			: $" Hinweis: {hint}";

		return new HttpRequestException($"Verbindung zur Support-API fehlgeschlagen ({endpoint}). Prüfe, ob API/Container läuft und der konfigurierte Port stimmt.{details} Letzter Fehler: {ex.Message}", ex);
	}

	private static string? TryGetLegacyLocalPortHint(string endpoint)
	{
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
		{
			return null;
		}

		if (uri.Scheme == Uri.UriSchemeHttp
			&& uri.Host is "127.0.0.1" or "localhost"
			&& uri.Port != 8089)
		{
			return "Die lokale API in diesem Workspace verwendet standardmäßig Port 8089 (z. B. http://127.0.0.1:8089).";
		}

		return null;
	}

	private static string NormalizeBaseUrl(string baseUrl)
	{
		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var inputUri))
		{
			throw new InvalidOperationException("Base URL is invalid.");
		}

		var builder = new UriBuilder(inputUri)
		{
			Path = NormalizePathRoot(inputUri.AbsolutePath),
			Query = string.Empty,
			Fragment = string.Empty,
		};

		return builder.Uri.AbsoluteUri.TrimEnd('/');
	}

	private static string NormalizePathRoot(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || path == "/")
		{
			return "/";
		}

		var trimmed = path.Trim();
		if (!trimmed.StartsWith('/'))
		{
			trimmed = "/" + trimmed;
		}

		return trimmed.TrimEnd('/');
	}

	private static Uri BuildSupportUpdatesWebSocketUri(string baseUrl, string apiKey, string? conversationId)
	{
		var normalizedBaseUrl = NormalizeBaseUrl(baseUrl);
		if (!Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var httpUri))
		{
			throw new InvalidOperationException("Base URL is invalid.");
		}

		var scheme = httpUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws";
		var query = $"api_key={Uri.EscapeDataString(apiKey?.Trim() ?? string.Empty)}";
		if (!string.IsNullOrWhiteSpace(conversationId))
		{
			query += $"&conversation_id={Uri.EscapeDataString(conversationId.Trim())}";
		}

		var builder = new UriBuilder(httpUri)
		{
			Scheme = scheme,
			Path = CombinePath(httpUri.AbsolutePath, "v1/support/ws/updates"),
			Query = query,
		};

		return builder.Uri;
	}

	private static SupportLiveEvent? TryParseSupportLiveEvent(string payload)
	{
		try
		{
			using var document = JsonDocument.Parse(payload);
			var root = document.RootElement;

			var type = root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
				? typeElement.GetString()
				: null;

			if (string.IsNullOrWhiteSpace(type))
			{
				return null;
			}

			var conversationId = root.TryGetProperty("conversation_id", out var conversationIdElement) && conversationIdElement.ValueKind == JsonValueKind.String
				? conversationIdElement.GetString()
				: null;
			var sender = root.TryGetProperty("sender", out var senderElement) && senderElement.ValueKind == JsonValueKind.String
				? senderElement.GetString()
				: null;
			var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
				? messageElement.GetString()
				: null;
			var timeGmt = root.TryGetProperty("time_gmt", out var timeElement) && timeElement.ValueKind == JsonValueKind.String
				? timeElement.GetString()
				: null;

			return new SupportLiveEvent(type, conversationId, sender, message, timeGmt);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private HttpClient CreateClient(string baseUrl, string? apiKey = null)
	{
		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
		{
			throw new InvalidOperationException("Base URL is invalid.");
		}

		var baseAddress = uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
			? uri
			: new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);

		var client = new HttpClient
		{
			BaseAddress = baseAddress,
			Timeout = DefaultRequestTimeout,
		};

		if (!string.IsNullOrWhiteSpace(_accessToken))
		{
			client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
		}

		var effectiveApiKey = ResolveEffectiveApiKey(baseUrl, apiKey);

		if (!string.IsNullOrWhiteSpace(effectiveApiKey))
		{
			client.DefaultRequestHeaders.Add("X-API-Key", effectiveApiKey);
		}

		return client;
	}

	private static string? ResolveEffectiveApiKey(string baseUrl, string? apiKey)
	{
		var effectiveApiKey = apiKey?.Trim();
		if (!string.IsNullOrWhiteSpace(effectiveApiKey))
		{
			return effectiveApiKey;
		}

		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
		{
			return effectiveApiKey;
		}

		if (uri.Host is "localhost" or "127.0.0.1")
		{
			return LocalDevApiKeyFallback;
		}

		return effectiveApiKey;
	}

	private static string CombinePath(string basePath, string relativePath)
	{
		var safeBase = string.IsNullOrWhiteSpace(basePath) ? "/" : basePath;
		if (!safeBase.StartsWith('/'))
		{
			safeBase = "/" + safeBase;
		}

		safeBase = safeBase.TrimEnd('/');
		var safeRelative = relativePath.TrimStart('/');
		return string.IsNullOrEmpty(safeBase)
			? "/" + safeRelative
			: $"{safeBase}/{safeRelative}";
	}

	private async Task EnsureSuccess(HttpResponseMessage response, CancellationToken cancellationToken)
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

		if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
			&& body.Contains("Invalid API key", StringComparison.OrdinalIgnoreCase))
		{
			// This can be transient right after login while the session key is being refreshed.
			_logger.LogInformation("Support API returned 401 Invalid API key. Body: {Body}", body);
		}
		else
		{
			_logger.LogWarning("Support API request failed with status {StatusCode} {ReasonPhrase}. Body: {Body}", (int)response.StatusCode, response.ReasonPhrase, body);
		}

		throw new HttpRequestException($"Request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}", null, response.StatusCode);
	}

}
