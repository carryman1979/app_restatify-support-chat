using System.Text.Json.Serialization;

namespace Restatify.SupportChat.DataContracts.Serialization;

[JsonSerializable(typeof(LoginRequestDto))]
[JsonSerializable(typeof(LoginResponseDto))]
[JsonSerializable(typeof(ConversationSummaryDto[]))]
[JsonSerializable(typeof(List<ConversationSummaryDto>))]
[JsonSerializable(typeof(ReplyRequestDto))]
[JsonSerializable(typeof(ReplyResponseDto))]
[JsonSerializable(typeof(ConversationMessageDto[]))]
[JsonSerializable(typeof(List<ConversationMessageDto>))]
[JsonSerializable(typeof(ConversationMessagesPageDto))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class SupportApiContractsContext : JsonSerializerContext
{
}
