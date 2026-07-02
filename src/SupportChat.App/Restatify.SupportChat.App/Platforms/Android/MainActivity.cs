#if ANDROID
using Android.App;
using Android.OS;
using Android.Content.PM;
using Android.Views;

namespace Restatify.SupportChat.Platforms.Android;

[Activity(
	MainLauncher = true,
	ConfigurationChanges = global::Uno.UI.ActivityHelper.AllConfigChanges,
	WindowSoftInputMode = SoftInput.AdjustNothing | SoftInput.StateHidden)]
public sealed class MainActivity : global::Microsoft.UI.Xaml.ApplicationActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		global::AndroidX.Core.SplashScreen.SplashScreen.InstallSplashScreen(this);

		base.OnCreate(savedInstanceState);
	}
}
#endif
