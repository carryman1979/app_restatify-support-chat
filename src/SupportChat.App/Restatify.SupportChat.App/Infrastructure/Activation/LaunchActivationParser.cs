namespace Restatify.SupportChat.Infrastructure.Activation;

internal static class LaunchActivationParser
{
	public static string? TryExtractConversationId(string? launchArguments)
	{
		if (string.IsNullOrWhiteSpace(launchArguments))
		{
			return null;
		}

		var directValue = TryExtractConversationIdFromFlatArgs(launchArguments);
		if (!string.IsNullOrWhiteSpace(directValue))
		{
			return directValue;
		}

		var decoded = Uri.UnescapeDataString(launchArguments);
		if (!string.Equals(decoded, launchArguments, StringComparison.Ordinal))
		{
			directValue = TryExtractConversationIdFromFlatArgs(decoded);
			if (!string.IsNullOrWhiteSpace(directValue))
			{
				return directValue;
			}
		}

		return null;
	}

	private static string? TryExtractConversationIdFromFlatArgs(string flatArgs)
	{
		if (string.IsNullOrWhiteSpace(flatArgs))
		{
			return null;
		}

		foreach (var segment in flatArgs.Split(['&', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
		{
			const string argsPrefix = "args=";
			if (segment.StartsWith(argsPrefix, StringComparison.OrdinalIgnoreCase))
			{
				var nestedRaw = segment[argsPrefix.Length..].Trim();
				var nestedDecoded = Uri.UnescapeDataString(nestedRaw);
				var nestedConversationId = TryExtractConversationIdFromFlatArgs(nestedDecoded);
				if (!string.IsNullOrWhiteSpace(nestedConversationId))
				{
					return nestedConversationId;
				}
			}

			const string conversationPrefix = "conversationId=";
			if (!segment.StartsWith(conversationPrefix, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var rawValue = segment[conversationPrefix.Length..].Trim();
			return string.IsNullOrWhiteSpace(rawValue)
				? null
				: Uri.UnescapeDataString(rawValue);
		}

		return null;
	}
}
