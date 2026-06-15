namespace Restatify.SupportChat.Tests;

public class AppInfoTests
{
	[SetUp]
	public void Setup()
	{
	}

	[Test]
	public void AppInfoCreation()
	{
		var appInfo = new { Title = "Test" };

		appInfo.Should().NotBeNull();
		appInfo.Title.Should().Be("Test");
	}
}
