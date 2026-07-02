namespace Restatify.SupportChat.Presentation;

public sealed partial class Shell : UserControl, IContentControlProvider
{
	public Shell()
	{
		this.InitializeComponent();
		App.TraceStartup("Shell: initialized.");
	}

	public ContentControl ContentControl => Splash;
}
