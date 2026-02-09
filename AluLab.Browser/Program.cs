using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using AluLab.Common;

/// <summary>
/// dotnet publish .\AluLab.Browser\AluLab.Browser.csproj -c Debug /p:DebugType=portable /p:DebugSymbols=true
/// dotnet publish .\AluLab.Browser\AluLab.Browser.csproj --configuration Release
/// The main entry point for the Avalonia browser application.
/// </summary>
internal sealed partial class Program
{
	/// <summary>
	/// Initializes and starts the Avalonia browser application asynchronously.
	/// </summary>
	/// <remarks>This entry point is intended for use when running the application in a WebAssembly-enabled browser
	/// environment. The method is marked with the SupportedOSPlatform attribute to indicate browser support.</remarks>
	/// <param name="args">An array of command-line arguments supplied to the application. This parameter is not used in browser environments.</param>
	/// <returns>A task that represents the asynchronous operation of starting the browser application.</returns>
	[SupportedOSPlatform("browser")]
	private static Task Main(string[] args) => BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");

	/// <summary>
	/// Configures and returns an Avalonia application builder for the App class.
	/// </summary>
	/// <returns>An AppBuilder instance configured for the App class. Use this builder to further configure and start the Avalonia
	/// application.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
