namespace Restatify.SupportChat.Services.Runtime;

public sealed partial class BackgroundChatRuntimeService : IBackgroundChatRuntimeService
{
	private readonly ILogger<BackgroundChatRuntimeService> _logger;
	private readonly SemaphoreSlim _sync = new(1, 1);
	private ChatRuntimeSession? _activeSession;

	public BackgroundChatRuntimeService(ILogger<BackgroundChatRuntimeService> logger)
	{
		_logger = logger;
	}

	public bool IsRunning => _activeSession is not null;

	public async Task StartAsync(ChatRuntimeSession session, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(session.BaseUrl) || string.IsNullOrWhiteSpace(session.ApiKey))
		{
			throw new ArgumentException("Chat runtime session requires base URL and API key.", nameof(session));
		}

		await _sync.WaitAsync(cancellationToken);
		try
		{
			if (_activeSession is not null
				&& string.Equals(_activeSession.BaseUrl, session.BaseUrl, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(_activeSession.ApiKey, session.ApiKey, StringComparison.Ordinal))
			{
				return;
			}

			_activeSession = session;
			await StartPlatformRuntimeAsync(session, cancellationToken);
			_logger.LogInformation("Background chat runtime started for endpoint {BaseUrl}.", session.BaseUrl);
		}
		finally
		{
			_sync.Release();
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken = default)
	{
		await _sync.WaitAsync(cancellationToken);
		try
		{
			if (_activeSession is null)
			{
				return;
			}

			await StopPlatformRuntimeAsync(_activeSession, cancellationToken);
			_logger.LogInformation("Background chat runtime stopped for endpoint {BaseUrl}.", _activeSession.BaseUrl);
			_activeSession = null;
		}
		finally
		{
			_sync.Release();
		}
	}

	private partial Task StartPlatformRuntimeAsync(ChatRuntimeSession session, CancellationToken cancellationToken);

	private partial Task StopPlatformRuntimeAsync(ChatRuntimeSession session, CancellationToken cancellationToken);
}
