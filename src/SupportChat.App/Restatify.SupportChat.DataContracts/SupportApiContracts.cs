using System.Text.Json.Serialization;

namespace Restatify.SupportChat.DataContracts;

public sealed record LoginRequestDto(
	[property: JsonPropertyName("email")] string Email,
	[property: JsonPropertyName("password")] string Password);

public sealed record LoginResponseDto(
	[property: JsonPropertyName("access_token")] string AccessToken,
	[property: JsonPropertyName("refresh_token")] string RefreshToken,
	[property: JsonPropertyName("mfa_required")] bool MfaRequired);

public sealed record ConversationSummaryDto(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("source_url")] string SourceUrl,
	[property: JsonPropertyName("updated_at_gmt")] string UpdatedAtGmt,
	[property: JsonPropertyName("unread_count")] int UnreadCount);

public sealed record ReplyRequestDto(
	[property: JsonPropertyName("message")] string Message);

public sealed record ReplyResponseDto(
	[property: JsonPropertyName("conversation_id")] string ConversationId,
	[property: JsonPropertyName("sender")] string Sender,
	[property: JsonPropertyName("message")] string Message,
	[property: JsonPropertyName("time_gmt")] string TimeGmt);

public sealed record ConversationMessageDto(
	[property: JsonPropertyName("message_id")] string MessageId,
	[property: JsonPropertyName("conversation_id")] string ConversationId,
	[property: JsonPropertyName("sender")] string Sender,
	[property: JsonPropertyName("message")] string Message,
	[property: JsonPropertyName("time_gmt")] string TimeGmt);

public sealed record ConversationMessagesPageDto(
	[property: JsonPropertyName("items")] IReadOnlyList<ConversationMessageDto>? Items,
	[property: JsonPropertyName("next_cursor")] string? NextCursor,
	[property: JsonPropertyName("has_more")] bool HasMore);

public sealed record GenerateApiKeyResponseDto(
	[property: JsonPropertyName("api_key")] string ApiKey);
