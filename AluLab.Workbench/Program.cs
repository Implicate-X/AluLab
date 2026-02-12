using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Avalonia;
using Serilog;
using Serilog.Events;
using AluLab.Common;
using AluLab.Board.Platform;
using AluLab.Workbench.Hardware;
using AluLab.Workbench.Services;

namespace AluLab.Workbench;

/// <summary>
/// Entry point for the Workbench application.
/// </summary>
/// <remarks>
/// Responsibilities:
/// <list type="bullet">
/// <item><description>Initializes Serilog (file/debug/Seq) before starting the UI.</description></item>
/// <item><description>Creates and configures the Avalonia-<see cref="AppBuilder"/>.</description></item>
/// <item><description>Registers host/DI services and performs an early hardware/board sanity check.</description></item>
/// </list>
/// </remarks>
sealed class Program
{
	/// <summary>
	/// Configures application-wide Serilog logging and assigns the logger to the global
	/// <see cref="Log.Logger"/> instance.
	/// </summary>
	/// <remarks>
	/// The configuration is deliberately set up before the UI starts so that even very early
	/// initialization errors are captured.
	/// <para>
	/// Included sinks and settings:
	/// <list type="bullet">
	/// <item><description>Global minimum: <see cref="LogEventLevel.Debug"/>.</description></item>
	/// <item><description>Overwrites levels for <c>Microsoft</c> and <c>System</c> to <see cref="LogEventLevel.Information"/> to reduce noise.</description></item>
	/// <item><description>Enrichment: fixed properties <c>Application</c> and <c>Environment</c> for better correlation.</description></item>
	/// <item><description>Debug sink for output in the debugger/output window.</description></item>
	/// <item><description>File sink <c>workbench.log</c> in <see cref="AppContext.BaseDirectory"/> with daily rolling and 14 days of retention.</description></item>
	/// <item><description>Seq sink on <c>http://localhost:5341</c> for structured central logs (if accessible).</description></item>
	/// </list>
	/// </para>
	/// </remarks>
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


	/// <summary>
	/// Creates and configures the Avalonia-<see cref="AppBuilder"/> for the Workbench application.
	/// </summary>
	/// <remarks>
	/// This factory method encapsulates the UI initialization and the app-specific host/DI configuration.
	/// Two central hooks are set on the <see cref="App"/> via <c>.AfterSetup(...)</c>:
	/// <list type="bullet">
	/// <item><description> <c>ConfigureHostServices</c>: Registers the required services in the DI container, in particular:
	/// <list type="bullet">
	/// <item><description>Logging via <c>Microsoft.Extensions.Logging</c> with Serilog as the provider (providers are cleaned up beforehand).</description></item>
	/// <item><description><see cref="IBoardHardwareContext"/> → <see cref="HardwareContext"/> (singleton).</description></item>
	/// <item><description><see cref="IBoardProvider"/> → <see cref="BoardProvider"/> (singleton).</description></item>
	/// </list>
	/// </description></item>
	/// <item><description><c>AfterHostServicesBuilt</c>: Performs an early hardware/board sanity check after the ServiceProvider has been built:
	/// attempts to obtain and initialize a board and logs errors/warnings via <see cref="ILogger{TCategoryName}"/>.
	/// UI initialization is not aborted; errors are simply logged. </description></item>
	/// </list>
	/// </remarks>
	/// <returns>The configured <see cref="AppBuilder"/>, which can then be started.</returns>
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

	[STAThread]
	public static void Main( string[] args )
	{
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
