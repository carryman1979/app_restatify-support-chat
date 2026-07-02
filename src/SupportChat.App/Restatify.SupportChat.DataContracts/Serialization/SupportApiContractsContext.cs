using System.Text.Json.Serialization;

namespace Restatify.SupportChat.DataContracts.Serialization;

[JsonSerializable(typeof(LoginRequestDto))]
[JsonSerializable(typeof(LoginResponseDto))]
[JsonSerializable(typeof(GenerateApiKeyResponseDto))]
[JsonSerializable(typeof(ConversationSummaryDto[]))]
[JsonSerializable(typeof(List<ConversationSummaryDto>))]
[JsonSerializable(typeof(ReplyRequestDto))]
[JsonSerializable(typeof(ReplyResponseDto))]
[JsonSerializable(typeof(ConversationMessageDto[]))]
[JsonSerializable(typeof(List<ConversationMessageDto>))]
[JsonSerializable(typeof(ConversationMessagesPageDto))]
[JsonSerializable(typeof(ConversationToolsDto))]
[JsonSerializable(typeof(SetConversationAiModeRequestDto))]
[JsonSerializable(typeof(SetConversationAiModeResponseDto))]
[JsonSerializable(typeof(DeleteConversationResponseDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class SupportApiContractsContext : JsonSerializerContext
{
}
