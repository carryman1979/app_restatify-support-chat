using Microsoft.Extensions.Options;
using Restatify.SupportChat.Business.Contexts;
using Restatify.SupportChat.Infrastructure.Activation;
using Restatify.SupportChat.Presentation;
using Restatify.SupportChat.Services.Settings;
using Restatify.SupportChat.ViewModels;
using Restatify.SupportChat.Services.Authorization;
using Serilog;
using Serilog.Events;
using Restatify.SupportChat.Services.Support;
using Restatify.SupportChat.Services.Runtime;

namespace Restatify.SupportChat;

public partial class App : Application
{
    private const int MaxLogFileBytes = 512 * 1024;
    private static string LocalLogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "supportchat.log");

    public static Window? MainWindow { get; private set; }
	public static Microsoft.UI.Dispatching.DispatcherQueue? UiDispatcherQueue => MainWindow?.DispatcherQueue;
	public static IHost? Host { get; private set; }

	public static void TraceStartup(string message)
	{
#if DEBUG
#if ANDROID
		global::Android.Util.Log.Info("RestatifyStartup", message);
#endif
		System.Diagnostics.Debug.WriteLine($"[Startup] {message}");
		Console.WriteLine($"[Startup] {message}");
		Log.Information("{Message}", message);
#endif
	}

	public App()
	{
		this.InitializeComponent();

		AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.CloseAndFlush();
	}

	protected async override void OnLaunched(LaunchActivatedEventArgs args)
	{
		Host = await StartAsync(this, args);
	}

	public static async Task<IHost?> StartAsync(Application app, LaunchActivatedEventArgs args)
	{
		TraceStartup("App startup: entering StartAsync.");
		var startupSettingsStore = new UserSettingsStore();
		var startupSettings = startupSettingsStore.Load();
		UserSettingsStore.ApplyLanguage(startupSettings.LanguageCode);

        TraceStartup("App startup: creating builder.");
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
							LogLevel.Warning)
						.CoreLogLevel(LogLevel.Warning);
				}, enableUnoLogging: true)
				.UseSerilog(false, false, loggerConfiguration =>
				{ 
                    loggerConfiguration
						.MinimumLevel.Information()
						.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
						.MinimumLevel.Override("Uno", LogEventLevel.Warning)
						.WriteTo.Console()
						.WriteTo.File(
							LocalLogPath,
							rollingInterval: RollingInterval.Infinite,
							rollOnFileSizeLimit: true,
							fileSizeLimitBytes: MaxLogFileBytes,
							retainedFileCountLimit: 1,
							shared: true,
							flushToDiskInterval: TimeSpan.FromSeconds(1));
				})
				.UseConfiguration(configure: configBuilder =>
					configBuilder
						.EmbeddedSource<App>()
						.Section<AppConfig>()
				)
				// Enable localization; selected language/theme are persisted in supportchat.settings.json
				.UseLocalization()
                .ConfigureServices(services => {
					services.AddSingleton<IAuthenticationService, SupportChatAuthenticationService>();
					services.AddSingleton<ISupportChatApiClient, SupportChatApiClient>();
					services.AddSingleton<IConversationReplyStore, ConversationReplyStore>();
					services.AddSingleton<IUserSettingsStore>(startupSettingsStore);
					services.AddSingleton<IBackgroundChatRuntimeService, BackgroundChatRuntimeService>();
					services.AddSingleton<IActivationNavigationService, ActivationNavigationService>();
					services.AddSingleton<INotificationService, NotificationService>();
				})
				.UseNavigation((views, routes) => RegisterRoutes(views, routes, startupSettingsStore))
			);

		TraceStartup("App startup: builder created.");
		MainWindow = builder.Window;

		TraceStartup("App startup: navigating to shell.");
		var host = await builder.NavigateAsync<Shell>
            (initialNavigate: async (services, navigator) =>
            {
                var auth = services.GetRequiredService<IAuthenticationService>();
                var authenticated = await auth.RefreshAsync();
                if (authenticated)
                {
                    await navigator.NavigateViewModelAsync<MainViewModel>(app, qualifier: Qualifiers.Nested);
                }
                else
                {
                    await navigator.NavigateViewModelAsync<LoginViewModel>(app, qualifier: Qualifiers.Nested);
                }
            });
        TraceStartup("App startup: shell navigation completed.");

		if (host.Services.GetService<IRouteNotifier>() is { } routeNotifier)
		{
			routeNotifier.RouteChanged += (_, e) =>
			{
				var regionName = e.Region?.Name ?? "<no-region>";
				TraceStartup($"Navigation route changed: region={regionName}");
			};
		}

		if (MainWindow is not null)
		{
			MainWindow.Title = host.Services.GetRequiredService<IOptions<AppConfig>>()?.Value?.Title ?? "Unknown";
			MainWindow.Activate();
			await UserSettingsStore.ApplyTheme(startupSettings.ThemeMode);
		}

		var conversationId = LaunchActivationParser.TryExtractConversationId(args.Arguments);
		if (!string.IsNullOrWhiteSpace(conversationId))
		{
			QueueConversationActivation(conversationId);
		}

		return host;
	}

	public static void QueueConversationActivation(string conversationId)
	{
		if (string.IsNullOrWhiteSpace(conversationId))
		{
			return;
		}

		if (Host?.Services.GetService<IActivationNavigationService>() is not { } activationService)
		{
			return;
		}

		activationService.RequestConversationNavigation(conversationId);
	}

	private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes, UserSettingsStore startupSettings)
	{
		var hasSavedApiKey = !string.IsNullOrWhiteSpace(startupSettings.Load().ApiKey);

		views.Register(
			new ViewMap(ViewModel: typeof(ShellViewModel)),
			new ViewMap<LoginPage, LoginViewModel>(),
			new ViewMap<MainPage, MainViewModel>(),
			new DataViewMap<SettingsPage, SettingsViewModel, string>(),
			new ViewMap<LogsPage, LogsViewModel>(),
			new DataViewMap<ConversationDetailsPage, ConversationDetailsViewModel, ConversationDetailsContext>()
		);

		routes.Register(
			new RouteMap("", View: views.FindByViewModel<ShellViewModel>(),
                Nested:
                [
					new RouteMap("Login", View: views.FindByViewModel<LoginViewModel>(), IsDefault: !hasSavedApiKey),
					new RouteMap("Main", View: views.FindByViewModel<MainViewModel>(), IsDefault: hasSavedApiKey),
					new RouteMap("Settings", View: views.FindByViewModel<SettingsViewModel>()),
					new RouteMap("Logs", View: views.FindByViewModel<LogsViewModel>()),
					new RouteMap("ConversationDetails", View: views.FindByViewModel<ConversationDetailsViewModel>()),
				]
			)
		);
	}
}
