using Microsoft.UI.Xaml.Media.Imaging;

namespace Restatify.SupportChat.Presentation;

public sealed partial class MainPage : Page
{
	private const string FallbackLogoUri = "ms-appx:///Assets/Images/restatify_logo.png";

	public MainPage()
	{
		this.InitializeComponent();
	}

	private async void OnSettingsClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is MainModel model)
		{
			await model.OpenSettings();
		}
	}

	private async void OnLogsClick(object sender, RoutedEventArgs e)
	{
		if (DataContext is MainModel model)
		{
			await model.OpenLogs();
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
