namespace Restatify.SupportChat.Services.Support;

public sealed class CursorExpiredException : Exception
{
	public CursorExpiredException(string? message = null)
		: base(message ?? "Cursor expired.")
	{
	}
}
