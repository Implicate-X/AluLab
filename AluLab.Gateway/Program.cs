using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
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
/// Application entry point for the Gateway desktop app.
/// </summary>
/// <remarks>
/// Responsibilities:
/// <list type="bullet">
/// <item><description>Configure Serilog sinks and log levels.</description></item>
/// <item><description>Optionally wait for a debugger attach in DEBUG builds.</description></item>
/// <item><description>Build and start the Avalonia app, including DI registrations and early hardware initialization checks.</description></item>
/// </list>
/// </remarks>
sealed class Program
{
	/// <summary>
	/// Default timeout used when waiting for a debugger attach is enabled but no explicit timeout is specified.
	/// </summary>
	private static readonly TimeSpan DefaultDebuggerWaitTimeout = TimeSpan.FromSeconds( 60 );

	/// <summary>
	/// Configures the global Serilog logger used by the application.
	/// </summary>
	/// <remarks>
	/// Logging is written to:
	/// <list type="bullet">
	/// <item><description>Debug output.</description></item>
	/// <item><description>A rolling log file at <c>&lt;baseDirectory&gt;/gateway.log</c>.</description></item>
	/// <item><description>A local Seq instance at <c>http://localhost:5341</c>.</description></item>
	/// </list>
	/// The configuration also enriches log events with <c>Application</c> and <c>Environment</c> properties.
	/// </remarks>
	static void ConfigureLogging()
	{
		var logPath = Path.Combine( AppContext.BaseDirectory, "gateway.log" );

		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Debug()
			.MinimumLevel.Override( "Microsoft", LogEventLevel.Information )
			.MinimumLevel.Override( "System", LogEventLevel.Information )
			.Enrich.WithProperty( "Application", "AluLab.Gateway" )
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
	/// In DEBUG builds, optionally pauses startup until a debugger attaches, then breaks into it.
	/// </summary>
	/// <param name="args">Command line arguments used to detect whether and how long to wait.</param>
	/// <remarks>
	/// Enabled via either:
	/// <list type="bullet">
	/// <item><description><c>--wait-for-debugger</c> (uses the default timeout), or <c>--wait-for-debugger=&lt;seconds|infinite&gt;</c></description></item>
	/// <item><description>Environment variable <c>ALULAB_WAIT_FOR_DEBUGGER</c> (<c>1</c> for default timeout, or <c>&lt;seconds|infinite&gt;</c>)</description></item>
	/// </list>
	/// Special values for infinite waiting: <c>infinite</c>, <c>inf</c>, <c>none</c>, <c>0</c>.
	/// </remarks>
	static void WaitForDebuggerIfRequested( string[] args )
	{
#if DEBUG
		if( !TryGetDebuggerWaitTimeout( args, out var timeout ) )
			return;

		// Wenn bereits attached (z.B. per Reattach), direkt breaken.
		if( Debugger.IsAttached )
		{
			Debugger.Break();
			return;
		}

		Log.Information(
			"Waiting for debugger attach... PID={Pid} Timeout={Timeout} (use --wait-for-debugger[=seconds] or ALULAB_WAIT_FOR_DEBUGGER=[1|seconds])",
			Environment.ProcessId,
			timeout == Timeout.InfiniteTimeSpan ? "infinite" : timeout );

		var sw = Stopwatch.StartNew();
		while( !Debugger.IsAttached )
		{
			if( timeout != Timeout.InfiniteTimeSpan && sw.Elapsed >= timeout )
			{
				Log.Warning( "Debugger wait timed out after {Timeout}. Continuing startup.", timeout );
				return;
			}

			Thread.Sleep( 200 );
		}

		Log.Information( "Debugger attached." );
		Debugger.Break();
#endif
	}

	/// <summary>
	/// Determines whether debugger-wait behavior is requested and returns the effective timeout.
	/// </summary>
	/// <param name="args">Command line arguments.</param>
	/// <param name="timeout">
	/// The parsed timeout. Can be <see cref="Timeout.InfiniteTimeSpan"/> for infinite waiting.
	/// </param>
	/// <returns><see langword="true"/> if waiting is enabled; otherwise <see langword="false"/>.</returns>
	/// <remarks>
	/// Precedence:
	/// <list type="number">
	/// <item><description>Command line argument <c>--wait-for-debugger</c> / <c>--wait-for-debugger=...</c></description></item>
	/// <item><description>Environment variable <c>ALULAB_WAIT_FOR_DEBUGGER</c></description></item>
	/// </list>
	/// If an invalid value is provided via args, the default timeout is used (and a warning is logged).
	/// If an invalid value is provided via environment variable, it is ignored (and a warning is logged).
	/// </remarks>
	static bool TryGetDebuggerWaitTimeout( string[] args, out TimeSpan timeout )
	{
		timeout = default;

		// Arg: --wait-for-debugger OR --wait-for-debugger=60
		foreach( var a in args )
		{
			if( string.Equals( a, "--wait-for-debugger", StringComparison.OrdinalIgnoreCase ) )
			{
				timeout = DefaultDebuggerWaitTimeout;
				return true;
			}

			const string prefix = "--wait-for-debugger=";
			if( a.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
			{
				var value = a.Substring( prefix.Length ).Trim();

				if( string.IsNullOrWhiteSpace( value ) )
				{
					timeout = DefaultDebuggerWaitTimeout;
					return true;
				}

				if( TryParseTimeoutSeconds( value, out timeout ) )
					return true;

				Log.Warning( "Invalid value for {Arg}. Using default timeout {Timeout}.", a, DefaultDebuggerWaitTimeout );
				timeout = DefaultDebuggerWaitTimeout;
				return true;
			}
		}

		// Env: ALULAB_WAIT_FOR_DEBUGGER=1 OR =60
		var env = Environment.GetEnvironmentVariable( "ALULAB_WAIT_FOR_DEBUGGER" )?.Trim();
		if( string.IsNullOrWhiteSpace( env ) )
			return false;

		if( string.Equals( env, "1", StringComparison.OrdinalIgnoreCase ) )
		{
			timeout = DefaultDebuggerWaitTimeout;
			return true;
		}

		if( TryParseTimeoutSeconds( env, out timeout ) )
			return true;

		Log.Warning( "Invalid value for ALULAB_WAIT_FOR_DEBUGGER='{Value}'. Ignoring.", env );
		return false;
	}

	/// <summary>
	/// Parses a debugger-wait timeout expressed in seconds or as an "infinite" token.
	/// </summary>
	/// <param name="value">The raw string value to parse.</param>
	/// <param name="timeout">The resulting timeout value if parsing succeeds.</param>
	/// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
	/// <remarks>
	/// Accepted infinite tokens: <c>infinite</c>, <c>inf</c>, <c>none</c>, <c>0</c>.
	/// Otherwise the value must be a non-negative integer number of seconds.
	/// </remarks>
	static bool TryParseTimeoutSeconds( string value, out TimeSpan timeout )
	{
		timeout = default;

		if( string.Equals( value, "infinite", StringComparison.OrdinalIgnoreCase ) ||
			string.Equals( value, "inf", StringComparison.OrdinalIgnoreCase ) ||
			string.Equals( value, "none", StringComparison.OrdinalIgnoreCase ) ||
			string.Equals( value, "0", StringComparison.OrdinalIgnoreCase ) )
		{
			timeout = Timeout.InfiniteTimeSpan;
			return true;
		}

		if( !int.TryParse( value, out int seconds ) || seconds < 0 )
			return false;

		timeout = TimeSpan.FromSeconds( seconds );
		return true;
	}

	/// <summary>
	/// Creates and configures the Avalonia <see cref="AppBuilder"/> for the Gateway.
	/// </summary>
	/// <returns>The configured <see cref="AppBuilder"/>.</returns>
	/// <remarks>
	/// Configuration includes:
	/// <list type="bullet">
	/// <item><description>Platform detection and font setup.</description></item>
	/// <item><description>Integration of Serilog into Microsoft.Extensions.Logging.</description></item>
	/// <item><description>DI registrations for board/hardware services and <c>DisplayService</c>.</description></item>
	/// <item><description>An early board availability/initialization check after host services are built.</description></item>
	/// <item><description>Attaching <c>DisplayService</c> to the main window when ready.</description></item>
	/// </list>
	/// Any exceptions during the early board check or display attach are caught and logged to avoid breaking app startup.
	/// </remarks>
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
	/// Application entry point.
	/// </summary>
	/// <param name="args">Command line arguments forwarded to Avalonia's desktop lifetime.</param>
	/// <remarks>
	/// Ensures logs are flushed by calling <see cref="Log.CloseAndFlush"/> in a <c>finally</c> block.
	/// </remarks>
	[STAThread]
	public static void Main( string[] args )
	{
		ConfigureLogging();
		WaitForDebuggerIfRequested( args );

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
