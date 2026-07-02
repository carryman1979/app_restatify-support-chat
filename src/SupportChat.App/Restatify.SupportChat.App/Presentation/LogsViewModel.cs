using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Restatify.SupportChat.Presentation;

public sealed class LogsViewModel : INotifyPropertyChanged
{
	private const string ErrorStatusPrefix = "ERROR: ";
	private const int MaxLogLines = 500;
	private const int MaxLogFileBytes = 512 * 1024;
	private const string ManagedLogFileName = "supportchat.log";

	private readonly IStringLocalizer _localizer;
	private readonly ILogger<LogsViewModel> _logger;
	private string _status = string.Empty;
	private string _logContent = string.Empty;
	private string _logFilePath = string.Empty;

	public LogsViewModel(IStringLocalizer localizer, ILogger<LogsViewModel> logger)
	{
		_localizer = localizer;
		_logger = logger;
		Title = T("LogsPage_Title", "Logs");
		RefreshCommand = new AsyncRelayCommand(Refresh);
		CopyCommand = new AsyncRelayCommand(CopyLogsToClipboardAsync, CanCopyLogs);
		ClearCommand = new AsyncRelayCommand(ClearLogsAsync, CanClearLogs);
		_ = Refresh();
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public string Title { get; }

	public IAsyncRelayCommand RefreshCommand { get; }

	public IAsyncRelayCommand CopyCommand { get; }

	public IAsyncRelayCommand ClearCommand { get; }

	public string Status
	{
		get => _status;
		private set => SetProperty(ref _status, value);
	}

	public string LogContent
	{
		get => _logContent;
		private set
		{
			if (SetProperty(ref _logContent, value))
			{
				CopyCommand.NotifyCanExecuteChanged();
				ClearCommand.NotifyCanExecuteChanged();
			}
		}
	}

	public string LogFilePath
	{
		get => _logFilePath;
		private set => SetProperty(ref _logFilePath, value);
	}

	public async Task Refresh()
	{
		try
		{
			Status = T("LogsPage_StatusLoading", "Loading logs...");

			var localPath = ApplicationData.Current.LocalFolder.Path;
			var directory = new DirectoryInfo(localPath);
			var health = CheckManagedLogFileHealth(directory);
			if (!health.IsHealthy)
			{
				LogFilePath = health.LogFilePath;
				LogContent = string.Empty;
				SetErrorStatus(string.Format(
					T("LogsPage_StatusWriteProbeFailedTemplate", "Logging may be inactive. Cannot access {0}: {1}"),
					health.LogFilePath,
					health.ErrorMessage ?? "unknown error"),
					"Logs.Refresh.HealthCheck");
				return;
			}

			var candidates = await NormalizeLogFilesAsync(directory);

			if (candidates.Length == 0)
			{
				LogFilePath = string.Empty;
				LogContent = T("LogsPage_NoLogs", "No log files found.");
				Status = T("LogsPage_StatusNoLogs", "No logs available.");
				return;
			}

			Exception? lastReadError = null;
			for (var index = 0; index < candidates.Length; index++)
			{
				var file = candidates[index];
				try
				{
					var lines = await ReadAllLinesSharedAsync(file.FullName);
					var tail = lines.Length > MaxLogLines ? lines[^MaxLogLines..] : lines;
					LogFilePath = file.FullName;
					LogContent = string.Join(Environment.NewLine, tail);

					Status = index == 0
						? string.Format(T("LogsPage_StatusLoadedTemplate", "Loaded {0} log lines."), tail.Length)
						: string.Format(
							T("LogsPage_StatusLoadedFallbackTemplate", "Current log file is locked. Loaded {0} lines from previous file."),
							tail.Length);
					return;
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
					_logger.LogWarning(ex, "Reading log file failed for {LogFilePath}.", file.FullName);
					lastReadError = ex;
				}
			}

			LogFilePath = candidates[0].FullName;
			LogContent = string.Empty;
			SetErrorStatus(string.Format(
				T("LogsPage_StatusLockedTemplate", "Log files are currently locked: {0}"),
				lastReadError?.Message ?? "unknown lock error"),
				"Logs.Refresh.AllCandidatesLocked");
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Refreshing logs failed.");
			SetErrorStatus(ex.Message, "Logs.Refresh.Exception", alreadyLogged: true);
		}
	}

	private LogFileHealth CheckManagedLogFileHealth(DirectoryInfo directory)
	{
		var managedPath = Path.Combine(directory.FullName, ManagedLogFileName);

		try
		{
			using var stream = new FileStream(
				managedPath,
				FileMode.OpenOrCreate,
				FileAccess.ReadWrite,
				FileShare.ReadWrite | FileShare.Delete);
			stream.Flush();
			return LogFileHealth.Healthy(managedPath);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.LogError(ex, "Log file health check failed for {LogFilePath}.", managedPath);
			return LogFileHealth.Unhealthy(managedPath, ex.Message);
		}
	}

	private bool CanCopyLogs()
	{
		return !string.IsNullOrWhiteSpace(LogContent);
	}

	private async Task CopyLogsToClipboardAsync()
	{
		if (!CanCopyLogs())
		{
			return;
		}

		var package = new DataPackage();
		package.SetText(LogContent);
		Clipboard.SetContent(package);
		Clipboard.Flush();
		Status = T("LogsPage_StatusCopied", "Log content copied to clipboard.");
		await Task.CompletedTask;
	}

	private bool CanClearLogs()
	{
		return !string.IsNullOrWhiteSpace(LogContent);
	}

	private async Task ClearLogsAsync()
	{
		try
		{
			Status = T("LogsPage_StatusClearing", "Clearing logs...");

			var localPath = ApplicationData.Current.LocalFolder.Path;
			var directory = new DirectoryInfo(localPath);
			var files = GetManagedLogFiles(directory);

			if (files.Length == 0)
			{
				LogFilePath = string.Empty;
				LogContent = T("LogsPage_NoLogs", "No log files found.");
				Status = T("LogsPage_StatusNoLogs", "No logs available.");
				return;
			}

			var target = ResolveActiveLogFile(files, directory);

			using (var stream = new FileStream(target.FullName, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
			{
				await stream.FlushAsync();
			}

			LogContent = string.Empty;
			LogFilePath = target.FullName;
			Status = T("LogsPage_StatusClearedTemplate", "Cleared {0} log file(s).", 1);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Clearing logs failed.");
			SetErrorStatus(ex.Message, "Logs.Clear.Exception", alreadyLogged: true);
		}
	}

	private async Task<FileInfo[]> NormalizeLogFilesAsync(DirectoryInfo directory)
	{
		var files = GetManagedLogFiles(directory)
			.OrderByDescending(file => file.LastWriteTimeUtc)
			.ToArray();

		if (files.Length == 0)
		{
			return files;
		}

		var primary = ResolveActiveLogFile(files, directory);
		await TrimFileToMaxSizeAsync(primary.FullName, MaxLogFileBytes);

		return [new FileInfo(primary.FullName)];
	}

	private static FileInfo ResolveActiveLogFile(FileInfo[] files, DirectoryInfo directory)
	{
		var primaryPath = Path.Combine(directory.FullName, ManagedLogFileName);
		var explicitPrimary = files.FirstOrDefault(file => string.Equals(file.FullName, primaryPath, StringComparison.OrdinalIgnoreCase));
		if (explicitPrimary is not null)
		{
			return explicitPrimary;
		}

		return files[0];
	}

	private static FileInfo[] GetManagedLogFiles(DirectoryInfo directory)
	{
		var managed = directory.GetFiles("supportchat*.log*", SearchOption.TopDirectoryOnly);
		if (managed.Length > 0)
		{
			return managed;
		}

		// Compatibility fallback for previous log names.
		return directory.GetFiles("*.log*", SearchOption.TopDirectoryOnly);
	}

	private static async Task TrimFileToMaxSizeAsync(string path, int maxBytes)
	{
		await using var readStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		if (readStream.Length <= maxBytes)
		{
			return;
		}

		var start = Math.Max(0, readStream.Length - maxBytes);
		readStream.Seek(start, SeekOrigin.Begin);

		var buffer = new byte[readStream.Length - start];
		var totalRead = 0;
		while (totalRead < buffer.Length)
		{
			var read = await readStream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead));
			if (read == 0)
			{
				break;
			}

			totalRead += read;
		}

		var effectiveLength = totalRead;
		var writeOffset = 0;
		for (var i = 0; i < effectiveLength; i++)
		{
			if (buffer[i] == (byte)'\n')
			{
				writeOffset = i + 1;
				break;
			}
		}

		var writeLength = Math.Max(0, effectiveLength - writeOffset);
		var output = new byte[writeLength];
		if (writeLength > 0)
		{
			Array.Copy(buffer, writeOffset, output, 0, writeLength);
		}

		await using var writeStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
		await writeStream.WriteAsync(output);
		await writeStream.FlushAsync();
	}

	private static async Task<string[]> ReadAllLinesSharedAsync(string path)
	{
		var lines = new List<string>();
		using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
		using var reader = new StreamReader(stream);

		while (true)
		{
			var line = await reader.ReadLineAsync();
			if (line is null)
			{
				break;
			}

			lines.Add(line);
		}

		return lines.ToArray();
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

	private string T(string key, string fallback, params object[] args)
	{
		var template = T(key, fallback);
		return string.Format(template, args);
	}

	private void SetErrorStatus(string message, string context, bool alreadyLogged = false)
	{
		if (!alreadyLogged)
		{
			_logger.LogWarning("UI error status in {Context}. LogFilePath={LogFilePath}. Message={Message}", context, LogFilePath, message);
		}

		Status = message.StartsWith(ErrorStatusPrefix, StringComparison.OrdinalIgnoreCase)
			? message
			: $"{ErrorStatusPrefix}{message}";
	}

	private readonly record struct LogFileHealth(bool IsHealthy, string LogFilePath, string? ErrorMessage)
	{
		public static LogFileHealth Healthy(string path) => new(true, path, null);

		public static LogFileHealth Unhealthy(string path, string errorMessage) => new(false, path, errorMessage);
	}
}
