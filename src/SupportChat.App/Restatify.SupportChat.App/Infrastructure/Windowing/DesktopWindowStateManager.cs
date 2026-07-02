using Microsoft.UI.Windowing;
using Windows.Graphics;
using Windows.Storage;

namespace Restatify.SupportChat.Infrastructure.Windowing;

internal sealed class DesktopWindowStateManager
{
	private const string MaximizedKey = "SupportChat.Window.Maximized";
	private const string PositionXKey = "SupportChat.Window.X";
	private const string PositionYKey = "SupportChat.Window.Y";
	private const string WidthKey = "SupportChat.Window.Width";
	private const string HeightKey = "SupportChat.Window.Height";
	private const int DefaultWidth = 1180;
	private const int DefaultHeight = 820;

	private readonly AppWindow _appWindow;
	private RectInt32? _restoredBounds;

	public DesktopWindowStateManager(AppWindow appWindow)
	{
		_appWindow = appWindow;
		_appWindow.Changed += OnChanged;
		_appWindow.Closing += OnClosing;
	}

	public void Restore()
	{
		var localSettings = ApplicationData.Current.LocalSettings;
		var bounds = LoadBounds(localSettings);
		var shouldMaximize = localSettings.Values[MaximizedKey] as bool? ?? false;

		if (bounds is { Width: > 0, Height: > 0 })
		{
			_restoredBounds = bounds.Value;
			_appWindow.MoveAndResize(bounds.Value);
		}
		else
		{
			_appWindow.Resize(new SizeInt32 { Width = DefaultWidth, Height = DefaultHeight });
			CaptureCurrentBounds();
		}

		if (shouldMaximize && _appWindow.Presenter is OverlappedPresenter overlappedPresenter)
		{
			overlappedPresenter.Maximize();
		}
	}

	private void OnChanged(AppWindow sender, AppWindowChangedEventArgs args)
	{
		if (!args.DidPositionChange && !args.DidSizeChange && !args.DidPresenterChange)
		{
			return;
		}

		if (_appWindow.Presenter is OverlappedPresenter overlappedPresenter
			&& overlappedPresenter.State == OverlappedPresenterState.Restored)
		{
			CaptureCurrentBounds();
		}
	}

	private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
	{
		Save();
	}

	private void Save()
	{
		if (_appWindow.Presenter is OverlappedPresenter overlappedPresenter
			&& overlappedPresenter.State == OverlappedPresenterState.Restored)
		{
			CaptureCurrentBounds();
		}

		if (_restoredBounds is not { Width: > 0, Height: > 0 } bounds)
		{
			return;
		}

		var localSettings = ApplicationData.Current.LocalSettings;
		var isMaximized = _appWindow.Presenter is OverlappedPresenter activePresenter
			&& activePresenter.State == OverlappedPresenterState.Maximized;
		localSettings.Values[MaximizedKey] = isMaximized;
		localSettings.Values[PositionXKey] = bounds.X;
		localSettings.Values[PositionYKey] = bounds.Y;
		localSettings.Values[WidthKey] = bounds.Width;
		localSettings.Values[HeightKey] = bounds.Height;
	}

	private void CaptureCurrentBounds()
	{
		_restoredBounds = new RectInt32
		{
			X = _appWindow.Position.X,
			Y = _appWindow.Position.Y,
			Width = _appWindow.Size.Width,
			Height = _appWindow.Size.Height,
		};
	}

	private static RectInt32? LoadBounds(ApplicationDataContainer localSettings)
	{
		if (localSettings.Values[PositionXKey] is not int x
			|| localSettings.Values[PositionYKey] is not int y
			|| localSettings.Values[WidthKey] is not int width
			|| localSettings.Values[HeightKey] is not int height
			|| width <= 0
			|| height <= 0)
		{
			return null;
		}

		return new RectInt32
		{
			X = x,
			Y = y,
			Width = width,
			Height = height,
		};
	}
}
