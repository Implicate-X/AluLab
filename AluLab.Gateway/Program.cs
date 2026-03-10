using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Avalonia;
using Serilog;
using Serilog.Events;
using AluLab.Common;
using AluLab.Board.Services;
using AluLab.Board.Platform;
using AluLab.Gateway.Hardware;

namespace AluLab.Gateway;

/// <summary>
/// Provides the application entry point and configuration methods for initializing and starting the Avalonia
/// application.
/// </summary>
/// <remarks>
/// This class is not intended to be instantiated. It contains static methods for setting up the Avalonia
/// application with recommended defaults and launching it with a classic desktop lifetime. The class is sealed to
/// prevent inheritance.
/// Remote start on Raspberry Pi 4B:
/// ssh iotuser@iot-gateway "DISPLAY=:0 nohup /opt/dotnet/dotnet Projects/AluLab/AluLab.Gateway.dll"
/// 
/// </remarks>
sealed class Program
{
	static void ConfigureLogging()
	{
		var logPath = Path.Combine( AppContext.BaseDirectory, "workbench.log" );

		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Debug()
			.MinimumLevel.Override( "Microsoft", LogEventLevel.Information )
			.MinimumLevel.Override( "System", LogEventLevel.Information )
			.Enrich.WithProperty( "Application", "AluLab.Workbench" )
			.Enrich.WithProperty( "Environment", "Development" )
			.WriteTo.Debug()
			.WriteTo.File(
				logPath,
				rollingInterval: RollingInterval.Day,
				retainedFileCountLimit: 14,
				shared: true,
				flushToDiskInterval: TimeSpan.FromSeconds( 1 ) )
			.WriteTo.Seq( "http://localhost:5341" )
			.CreateLogger();
	}

	[DllImport( "libgtk-3.so.0" )]
	private static extern void gtk_init( ref int argc, ref IntPtr argv );

	[DllImport( "libgtk-3.so.0" )]
	private static extern IntPtr gtk_message_dialog_new(
		IntPtr parent,
		uint flags,
		int type,
		int buttons,
		string message
	);

	[DllImport( "libgtk-3.so.0" )]
	private static extern int gtk_dialog_run( IntPtr raw );

	[DllImport( "libgtk-3.so.0" )]
	private static extern void gtk_widget_destroy( IntPtr widget );


	public static AppBuilder BuildAvaloniaApp()
		=> AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.WithInterFont()
			.LogToTrace()
			.AfterSetup( builder =>
			{
				if( builder.Instance is App app )
				{
					app.ConfigureHostServices = services =>
					{
						services.AddLogging( lb =>
						{
							lb.ClearProviders();
							lb.AddSerilog( Log.Logger, dispose: false );
						} );

						services.AddSingleton<IBoardHardwareContext, HardwareContext>();
						services.AddSingleton<IBoardProvider, BoardProvider>();
						services.AddSingleton<DisplayService>();
					};

					app.AfterHostServicesBuilt = sp =>
					{
						var logger = sp.GetRequiredService<ILogger<Program>>();

						try
						{
							var provider = sp.GetService<IBoardProvider>();
							if( provider is null )
							{
								logger.LogError( "Board: IBoardProvider not registered." );
								return;
							}

							if( !provider.TryGetBoard( out var board, out var error ) || board is null )
							{
								logger.LogWarning( "Board: not available: {Error}", error );
								return;
							}

							if( board.Initialize() )
							{
								logger.LogInformation( "Board: initialized." );

								try
								{
									board.AluController.ConfigureSync( "https://iot.homelabs.one/sync", logger );
									logger.LogInformation( "Board: ALU sync configured." );
								}
								catch( Exception ex )
								{
									logger.LogWarning( ex, "Board: failed to configure ALU sync." );
								}

								return;
							}

							logger.LogError(
								"Board: initialization failed: {Reason}",
								board.LastInitializationException?.ToString() ?? "Unknown error" );
						}
						catch( Exception ex )
						{
							logger.LogError( ex, "Board: early check exception" );
						}
					};

					app.MainWindowReady += window =>
					{
						try
						{
							var mirror = app.Services.GetRequiredService<DisplayService>();
							mirror.Attach( window );
						}
						catch( Exception ex )
						{
							var logger = app.Services.GetService<ILogger<Program>>();
							logger?.LogWarning( ex, "DisplayMirror: attach failed." );
						}
					};
				}
			} );

	/// <summary>
	/// Initializes and starts the application with a classic desktop lifetime using the specified command-line arguments.
	/// </summary>
	/// <param name="args">An array of command-line arguments to pass to the application on startup.</param>
	[STAThread]
	public static void Main( string[] args )
	{
#if DEBUG
		int argc = 0;
		IntPtr argv = IntPtr.Zero;

		gtk_init( ref argc, ref argv );

		IntPtr dialog = gtk_message_dialog_new(
			IntPtr.Zero,
			0,
			0, // GTK_MESSAGE_INFO
			1, // GTK_BUTTONS_OK
			"Attach Debugger in Visual Studio ..."
		);

		gtk_dialog_run( dialog );
		gtk_widget_destroy( dialog );
#endif

		ConfigureLogging();

		try
		{
			BuildAvaloniaApp().StartWithClassicDesktopLifetime( args );
		}
		finally
		{
			Log.CloseAndFlush();
		}
	}
}
