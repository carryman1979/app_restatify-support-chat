using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Restatify.SupportChat.Windows;
using Windows.Graphics;
using Windows.Storage;
#if DESKTOP
using WinRT.Interop;
#endif

namespace Restatify.SupportChat;

public partial class App : Application
{
	private static Window? _window;
	private static DesktopWindowStateManager? _desktopWindowStateManager;
	private static bool _isShuttingDown;
	public static IHost? Host { get; private set; }

	public App()
	{
		InitializeComponent();
	}

	protected async override void OnLaunched(LaunchActivatedEventArgs args)
	{
		Host = await StartAsync(this, args);
	}

	public static async Task<IHost?> StartAsync(Application app, LaunchActivatedEventArgs args)
	{
		var startupSettingsStore = new Services.Settings.ConnectionSettingsStore();
		Services.Settings.ConnectionSettingsStore.ApplyLanguage(startupSettingsStore.Load().LanguageCode);

		var builder = app.CreateBuilder(args)

			// Add navigation support for toolkit controls such as TabBar and NavigationView
			.UseToolkitNavigation()
			.Configure(host => host
#if DEBUG
				// Switch to Development environment when running in DEBUG
				.UseEnvironment(Environments.Development)
#endif
				.UseLogging(configure: (context, logBuilder) =>
				{
					// Configure log levels for different categories of logging
					logBuilder.SetMinimumLevel(
						context.HostingEnvironment.IsDevelopment() ?
							LogLevel.Information :
							LogLevel.Warning);
				}, enableUnoLogging: true)
				.UseSerilog(consoleLoggingEnabled: true, fileLoggingEnabled: true)
				.UseConfiguration(configure: configBuilder =>
					configBuilder
						.EmbeddedSource<App>()
						.Section<AppConfig>()
				)
				// Enable localization (see appsettings.json for supported languages)
				.UseLocalization()
				.ConfigureServices((context, services) => {
					services.AddSingleton<Services.Support.ISupportChatApiClient, Services.Support.SupportChatApiClient>();
					services.AddSingleton<Services.Support.IConversationReplyStore, Services.Support.ConversationReplyStore>();
					services.AddSingleton<Services.Settings.IConnectionSettingsStore, Services.Settings.ConnectionSettingsStore>();
				})
				.UseNavigation(RegisterRoutes)
			);

		var host = await builder.NavigateAsync<Shell>();

		_window = builder.Window;
		if (_window is not null)
		{
			_window.Title = "Restatify Support Chat";
			ConfigureDesktopWindow(_window);
			TrySetNativeWindowIcon(_window);
			_window.Closed -= OnMainWindowClosed;
			_window.Closed += OnMainWindowClosed;
		}

		return host;
	}

	private static void ConfigureDesktopWindow(Window window)
	{
#if DESKTOP
		if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
		{
			return;
		}

		var appWindow = window.AppWindow;

		if (appWindow.Presenter is OverlappedPresenter overlappedPresenter)
		{
			overlappedPresenter.PreferredMinimumWidth = 640;
			overlappedPresenter.PreferredMinimumHeight = 560;
		}

		_desktopWindowStateManager = new DesktopWindowStateManager(appWindow);
		_desktopWindowStateManager.Restore();
#endif
	}

	private static void TrySetNativeWindowIcon(Window window)
	{
#if DESKTOP
		try
		{
			window.SetWindowIcon();

			var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "restatify_logo.ico");
			if (!File.Exists(iconPath))
			{
				return;
			}

			ApplyWindowIcon(window, iconPath);

			// Retry once on the UI queue in case the native handle wasn't fully ready on first attempt.
			window.DispatcherQueue?.TryEnqueue(() => ApplyWindowIcon(window, iconPath));
		}
		catch
		{
			// Keep startup resilient if a platform-specific icon API is unavailable.
		}
#endif
	}

#if DESKTOP
	private static void ApplyWindowIcon(Window window, string iconPath)
	{
		var hwnd = WindowNative.GetWindowHandle(window);
		if (hwnd == IntPtr.Zero)
		{
			return;
		}

		SetWin32WindowIcons(hwnd, iconPath);
	}

	private static void SetWin32WindowIcons(IntPtr hwnd, string iconPath)
	{
		var hIconSmall = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 16, 16, LrLoadFromFile);
		var hIconBig = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 32, 32, LrLoadFromFile);

		if (hIconSmall == IntPtr.Zero && hIconBig == IntPtr.Zero)
		{
			return;
		}

		if (hIconSmall != IntPtr.Zero)
		{
			SendMessage(hwnd, WmSetIcon, IconSmall, hIconSmall);
		}

		if (hIconBig != IntPtr.Zero)
		{
			SendMessage(hwnd, WmSetIcon, IconBig, hIconBig);
		}
	}

	private const uint ImageIcon = 1;
	private const uint LrLoadFromFile = 0x00000010;
	private const uint WmSetIcon = 0x0080;
	private static readonly IntPtr IconSmall = IntPtr.Zero;
	private static readonly IntPtr IconBig = new(1);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
#endif

	private static void OnMainWindowClosed(object sender, WindowEventArgs args)
	{
		if (_isShuttingDown)
		{
			return;
		}

		_isShuttingDown = true;

		try
		{
			Current?.Exit();
		}
		finally
		{
			// Fallback for host configurations where Exit does not terminate the process immediately.
			Environment.Exit(0);
		}
	}

	private sealed class DesktopWindowStateManager
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

	private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
	{
		views.Register(
			new ViewMap(ViewModel: typeof(ShellModel)),
			new ViewMap<MainPage, MainModel>(),
			new DataViewMap<SettingsPage, SettingsModel, string>(),
			new ViewMap<LogsPage, LogsModel>(),
			new DataViewMap<ConversationDetailsPage, ConversationDetailsModel, ConversationDetailsContext>()
		);

		routes.Register(
			new RouteMap("", View: views.FindByViewModel<ShellModel>(),
				Nested: new RouteMap[]
				{
					new RouteMap("Main", View: views.FindByViewModel<MainModel>()),
					new RouteMap("Settings", View: views.FindByViewModel<SettingsModel>()),
					new RouteMap("Logs", View: views.FindByViewModel<LogsModel>()),
					new RouteMap("ConversationDetails", View: views.FindByViewModel<ConversationDetailsModel>()),
				}
			)
		);
	}

	public static void InitializeLogging()
	{
#if DEBUG
		var factory = LoggerFactory.Create(builder =>
		{
			builder.AddConsole();
			builder.SetMinimumLevel(LogLevel.Information);
			builder.AddFilter("Uno", LogLevel.Warning);
			builder.AddFilter("Windows", LogLevel.Warning);
			builder.AddFilter("Microsoft", LogLevel.Warning);
		});

		global::Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;
#endif
	}
}
