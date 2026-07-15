namespace Restatify.SupportChat.Presentation;

public sealed partial class LoginPage : Page
{
	public LoginPage()
	{
		this.InitializeComponent();
		App.TraceStartup("LoginPage: constructor executed.");
	}

	private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
	{
		Frame?.Navigate(typeof(SettingsPage));
	}

	private void OnOpenLogsClick(object sender, RoutedEventArgs e)
	{
		Frame?.Navigate(typeof(LogsPage));
	}
}