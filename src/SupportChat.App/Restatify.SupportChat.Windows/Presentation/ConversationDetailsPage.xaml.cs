namespace Restatify.SupportChat.Presentation;

public sealed partial class ConversationDetailsPage : Page
{
	public ConversationDetailsPage()
	{
		this.InitializeComponent();
	}

	private void OnBackClick(object sender, RoutedEventArgs e)
	{
		if (Frame?.CanGoBack is true)
		{
			Frame.GoBack();
		}
	}
}
