using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Avalonia;
using Serilog;
using Serilog.Events;
using AluLab.Common;
using AluLab.Common.Services;
using AluLab.Common.Relay;
using AluLab.Board.Services;
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
	/// Creates the Avalonia <see cref="AppBuilder"/> pre-configured for the Workbench application.
	/// </summary>
	/// <remarks>
	/// This method centralizes UI host creation so it can be customized (e.g., platform options,
	/// DI/host wiring, or hardware context initialization) without changing <see cref="Main(string[])"/>.
	/// The underlying factory is expected to:
	/// <list type="bullet">
	/// <item><description>Construct the application and wire any required services.</description></item>
	/// <item><description>Initialize the <see cref="HardwareContext"/> used by the Workbench runtime.</description></item>
	/// </list>
	/// </remarks>
	/// <returns>A configured <see cref="AppBuilder"/> ready to start the classic desktop lifetime.</returns>
	public static AppBuilder BuildAvaloniaApp()
		=> DesktopAppBuilderFactory.Create<Program, HardwareContext>(
			configureExtraServices: services =>
			{
				services.AddSingleton<WindowPlacementService>();
			},
			afterBoardInitialized: ( sp, logger ) =>
			{
				// aktuell nicht benötigt, aber bleibt als Hook
			} );

	/// <summary>
	/// Application entry point.
	/// </summary>
	/// <param name="args">Command line arguments forwarded to Avalonia's desktop lifetime.</param>
	/// <remarks>
	/// Startup sequence:
	/// <list type="number">
	/// <item><description>Configure Serilog early so startup failures are captured.</description></item>
	/// <item><description>Build and start Avalonia using the classic desktop lifetime.</description></item>
	/// <item><description>Always flush and close logs before process exit.</description></item>
	/// </list>
	/// The <see cref="STAThreadAttribute"/> is required for many desktop UI features that rely on a
	/// single-threaded COM apartment (common on Windows).
	/// </remarks>
	[STAThread]
	public static void Main( string[] args )
	{
		ConfigureLogging();

		try
		{
			var builder = BuildAvaloniaApp();

			// Workbench-spezifisch: Fensterposition/-größe persistieren.
			builder.AfterSetup( b =>
			{
				if( b.Instance is not App app )
					return;

				app.MainWindowReady += window =>
				{
					var placement = app.Services.GetRequiredService<WindowPlacementService>();
					placement.Attach( window );
				};
			} );

			builder.StartWithClassicDesktopLifetime( args );
		}
		finally
		{
			Log.CloseAndFlush();
		}
	}
}
