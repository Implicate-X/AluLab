using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using AluLab.Common;

namespace AluLab.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
                .WithInterFont()
                .ConfigureFonts(fontManager =>
                {
                    fontManager.AddFontCollection(new FontCollection());
                });
        }
    }
}
