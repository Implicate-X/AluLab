using Android.App;
using Android.Content;
using Android.OS;
using Android.Graphics.Drawables;
using Android.Widget;
using AndroidX.AppCompat.App;

namespace AluLab.Android;

[Activity(
	Theme = "@style/MyTheme.Splash",
	MainLauncher = true,
	NoHistory = true,
	Exported = true)]
public sealed class SplashActivity : AppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);

		SetContentView(Resource.Layout.splash);

		var image = FindViewById<ImageView>(Resource.Id.splashImage);
		if (image?.Drawable is AnimatedVectorDrawable avd)
		{
			avd.Start();
		}

		StartActivity(new Intent(this, typeof(MainActivity)));
		Finish();
	}
}