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

sealed class Program
{
	private static readonly TimeSpan DefaultDebuggerWaitTimeout = TimeSpan.FromSeconds( 60 );

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
