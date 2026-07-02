namespace Restatify.SupportChat.Presentation;

public sealed partial class LogsPage : Page
{
	public LogsPage()
	{
		InitializeComponent();
	}

	private void OnBackClick(object sender, RoutedEventArgs e)
	{
		if (Frame?.CanGoBack is true)
		{
			Frame.GoBack();
		}
	}
}
