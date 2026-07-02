namespace Restatify.SupportChat.UITests;

public class Given_MainPage : TestBase
{
	[Test]
	public async Task When_AppStarts_ThenTakeScreenshot()
	{
		await Task.Delay(5000);
		TakeScreenshot("after_startup");
	}
}