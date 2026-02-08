using System;
using Avalonia;
using AluLab.Common;

namespace AluLab.Gateway;

/// <summary>
/// Provides the application entry point and configuration methods for initializing and starting the Avalonia
/// application.
/// </summary>
/// <remarks>
/// This class is not intended to be instantiated. It contains static methods for setting up the Avalonia
/// application with recommended defaults and launching it with a classic desktop lifetime. The class is sealed to
/// prevent inheritance.
/// Remote start on Raspberry Oi 4B:
/// ssh user@raspberry-iot-node "DISPLAY=:0 nohup /home/user/.dotnet/dotnet /opt/alulab/gateway/AluLab.Gateway.dll"
/// </remarks>
sealed class Program
{
	public static AppBuilder BuildAvaloniaApp()
		=> AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.WithInterFont()
			.LogToTrace();

	/// <summary>
	/// Initializes and starts the application with a classic desktop lifetime using the specified command-line arguments.
	/// </summary>
	/// <param name="args">An array of command-line arguments to pass to the application on startup.</param>
	[STAThread]
	public static void Main( string[] args )
	{
		BuildAvaloniaApp().StartWithClassicDesktopLifetime( args );
	}
}
