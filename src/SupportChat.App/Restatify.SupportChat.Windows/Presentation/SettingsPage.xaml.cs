using Microsoft.UI.Xaml.Media.Imaging;

namespace Restatify.SupportChat.Presentation;

public sealed partial class SettingsPage : Page
{
	private const string FallbackLogoUri = "ms-appx:///Assets/Images/restatify_logo.png";

	public SettingsPage()
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

	private void OnLogoImageFailed(object sender, ExceptionRoutedEventArgs e)
	{
		if (sender is Image image)
		{
			image.Source = new BitmapImage(new Uri(FallbackLogoUri));
		}
	}
}
