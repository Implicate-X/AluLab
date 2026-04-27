using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace AluLab.Android;

[Activity(
	Label = "AluLab.Android",
	Theme = "@style/MyTheme.NoActionBar",
	Icon = "@drawable/implicatex",
	MainLauncher = true,
	ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode )]
public class MainActivity : AvaloniaMainActivity
{
}
