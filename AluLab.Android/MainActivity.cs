using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using AluLab.Common;

namespace AluLab.Android;

/// <summary>
/// Represents the main activity for the AluLab.Android application, serving as the entry point when the app is launched
/// on an Android device.
/// </summary>
/// <remarks>This activity is configured as the main launcher and applies a custom theme without an action bar. It
/// inherits from AvaloniaMainActivity and is responsible for initializing the Avalonia application with
/// platform-specific settings. The activity handles orientation, screen size, and UI mode configuration changes to
/// ensure proper behavior across device rotations and UI modes.</remarks>
[Activity(
  Label = "AluLab.Android",
  Theme = "@style/MyTheme.NoActionBar",
  Icon = "@drawable/icon",
  MainLauncher = true,
  ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode )]
public class MainActivity : AvaloniaMainActivity<App>
{
	/// <summary>
	/// Configures the specified application builder with additional settings or middleware before the application is
	/// built.
	/// </summary>
	/// <param name="builder">The application builder to customize.</param>
	/// <returns>The customized application builder instance.</returns>
	protected override AppBuilder CustomizeAppBuilder( AppBuilder builder )
	{
		return base.CustomizeAppBuilder( builder )
		  .WithInterFont();
	}
}
