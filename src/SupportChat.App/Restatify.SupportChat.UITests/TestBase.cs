namespace Restatify.SupportChat.UITests;

public class TestBase
{
	private IApp? _app;

	static TestBase()
	{
		AppInitializer.TestEnvironment.AndroidAppName = Constants.AndroidAppName;
		AppInitializer.TestEnvironment.WebAssemblyDefaultUri = Constants.WebAssemblyDefaultUri;
		AppInitializer.TestEnvironment.iOSAppName = Constants.IosAppName;
		AppInitializer.TestEnvironment.AndroidAppName = Constants.AndroidAppName;
		AppInitializer.TestEnvironment.iOSDeviceNameOrId = Constants.IosDeviceNameOrId;
		AppInitializer.TestEnvironment.CurrentPlatform = Constants.CurrentPlatform;
		AppInitializer.TestEnvironment.WebAssemblyBrowser = Constants.WebAssemblyBrowser;

#if DEBUG
		AppInitializer.TestEnvironment.WebAssemblyHeadless = false;
#endif

		AppInitializer.ColdStartApp();
	}

	protected IApp App
	{
		get => _app!;
		private set
		{
			_app = value;
			Helpers.App = value;
		}
	}

	[SetUp]
	public void SetUpTest()
	{
		App = AppInitializer.AttachToApp();
	}

	[TearDown]
	public void TearDownTest()
	{
		TakeScreenshot("teardown");
	}

	public FileInfo TakeScreenshot(string stepName)
	{
		var title = $"{TestContext.CurrentContext.Test.Name}_{stepName}"
			.Replace(" ", "_")
			.Replace(".", "_");

		var fileInfo = App.Screenshot(title);

		var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileInfo.Name);
		if (fileNameWithoutExt != title && fileInfo.DirectoryName is not null)
		{
			var destFileName = Path.Combine(fileInfo.DirectoryName, title + Path.GetExtension(fileInfo.Name));

			if (File.Exists(destFileName))
			{
				File.Delete(destFileName);
			}

			File.Move(fileInfo.FullName, destFileName);
			TestContext.AddTestAttachment(destFileName, stepName);
			return new FileInfo(destFileName);
		}

		TestContext.AddTestAttachment(fileInfo.FullName, stepName);
		return fileInfo;
	}
}