using System.ComponentModel;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Windows.Storage;

namespace Restatify.SupportChat.Presentation;

public sealed class LogsModel : INotifyPropertyChanged
{
	private readonly IStringLocalizer _localizer;
	private string _status = string.Empty;
	private string _logContent = string.Empty;
	private string _logFilePath = string.Empty;

	public LogsModel(IStringLocalizer localizer)
	{
		_localizer = localizer;
		Title = T("LogsPage_Title", "Logs");
		RefreshCommand = new AsyncRelayCommand(Refresh);
		_ = Refresh();
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public string Title { get; }

	public IAsyncRelayCommand RefreshCommand { get; }

	public string Status
	{
		get => _status;
		private set => SetProperty(ref _status, value);
	}

	public string LogContent
	{
		get => _logContent;
		private set => SetProperty(ref _logContent, value);
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
			var latest = directory.GetFiles("*.log", SearchOption.TopDirectoryOnly)
				.OrderByDescending(file => file.LastWriteTimeUtc)
				.FirstOrDefault();

			if (latest is null)
			{
				LogFilePath = string.Empty;
				LogContent = T("LogsPage_NoLogs", "No log files found.");
				Status = T("LogsPage_StatusNoLogs", "No logs available.");
				return;
			}

			LogFilePath = latest.FullName;
			var lines = await File.ReadAllLinesAsync(latest.FullName);
			var tail = lines.Length > 500 ? lines[^500..] : lines;
			LogContent = string.Join(Environment.NewLine, tail);
			Status = string.Format(T("LogsPage_StatusLoadedTemplate", "Loaded {0} log lines."), tail.Length);
		}
		catch (Exception ex)
		{
			Status = ex.Message;
		}
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
}
