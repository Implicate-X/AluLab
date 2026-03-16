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
	/// <remarks>
	/// Used when the user enables debugger waiting (via CLI arg or environment variable) without providing a numeric timeout.
	/// </remarks>
	private static readonly TimeSpan DefaultDebuggerWaitTimeout = TimeSpan.FromSeconds( 60 );

	/// <summary>
	/// Configures the global Serilog logger used by the application.
	/// </summary>
	/// <remarks>
	/// This method initializes <see cref="Log.Logger"/> with a sink configuration appropriate for local development:
	/// <list type="bullet">
	/// <item><description>Debug sink (Visual Studio Output window when debugging).</description></item>
	/// <item><description>Rolling file sink writing to <c>&lt;baseDirectory&gt;/gateway.log</c> (daily rollover, 14 retained files).</description></item>
	/// <item><description>Seq sink pointing to <c>http://localhost:5341</c>.</description></item>
	/// </list>
	/// Additionally, log events are enriched with constant properties (<c>Application</c> and <c>Environment</c>) to simplify filtering in Seq/file logs.
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
	/// This is a developer convenience for early-startup debugging (before UI initialization).
	/// When enabled, the process repeatedly polls <see cref="Debugger.IsAttached"/> until either a debugger attaches
	/// (then calls <see cref="Debugger.Break"/>), or the timeout elapses.
	/// <para/>
	/// Enable waiting using either:
	/// <list type="bullet">
	/// <item><description>
	/// CLI: <c>--wait-for-debugger</c> (uses <see cref="DefaultDebuggerWaitTimeout"/>)
	/// or <c>--wait-for-debugger=&lt;seconds|infinite&gt;</c>.
	/// </description></item>
	/// <item><description>
	/// Environment: <c>ALULAB_WAIT_FOR_DEBUGGER</c> (<c>1</c> for default timeout, or <c>&lt;seconds|infinite&gt;</c>).
	/// </description></item>
	/// </list>
	/// Special values for infinite waiting: <c>infinite</c>, <c>inf</c>, <c>none</c>, <c>0</c>.
	/// <para/>
	/// In non-DEBUG builds this method does nothing.
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
	/// This method centralizes the enablement rules and parsing logic used by <see cref="WaitForDebuggerIfRequested"/>.
	/// <para/>
	/// Precedence:
	/// <list type="number">
	/// <item><description>Command line argument <c>--wait-for-debugger</c> / <c>--wait-for-debugger=...</c></description></item>
	/// <item><description>Environment variable <c>ALULAB_WAIT_FOR_DEBUGGER</c></description></item>
	/// </list>
	/// <para/>
	/// Validation behavior:
	/// <list type="bullet">
	/// <item><description>
	/// Invalid CLI values still enable waiting, but fall back to <see cref="DefaultDebuggerWaitTimeout"/> (and log a warning).
	/// </description></item>
	/// <item><description>
	/// Invalid environment variable values are ignored (and log a warning).
	/// </description></item>
	/// </list>
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
	/// Accepted formats:
	/// <list type="bullet">
	/// <item><description>
	/// Infinite: <c>infinite</c>, <c>inf</c>, <c>none</c>, or <c>0</c> (maps to <see cref="Timeout.InfiniteTimeSpan"/>).
	/// </description></item>
	/// <item><description>
	/// Finite: a non-negative integer number of seconds (maps to <see cref="TimeSpan.FromSeconds(double)"/>).
	/// </description></item>
	/// </list>
	/// Any other value (including negative numbers and non-integers) fails parsing.
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
	/// Creates and configures the Avalonia <see cref="AppBuilder"/> for the Gateway desktop app.
	/// </summary>
	/// <returns>An <see cref="AppBuilder"/> preconfigured for desktop hosting.</returns>
	/// <remarks>
	/// This delegates to <c>DesktopAppBuilderFactory.Create&lt;TProgram, THardwareContext&gt;()</c> to apply shared
	/// application defaults (e.g., platform detection, dependency injection, and hardware context wiring) consistently
	/// across AluLab desktop applications.
	/// </remarks>
	public static AppBuilder BuildAvaloniaApp()
		=> DesktopAppBuilderFactory.Create<Program, HardwareContext>();

	/// <summary>
	/// Application entry point.
	/// </summary>
	/// <param name="args">Command line arguments forwarded to Avalonia's desktop lifetime.</param>
	/// <remarks>
	/// Startup flow:
	/// <list type="number">
	/// <item><description>Configure logging via <see cref="ConfigureLogging"/>.</description></item>
	/// <item><description>Optionally wait for debugger attach via <see cref="WaitForDebuggerIfRequested"/> (DEBUG only).</description></item>
	/// <item><description>Create the Avalonia app builder via <see cref="BuildAvaloniaApp"/> and start the desktop lifetime.</description></item>
	/// </list>
	/// Serilog is always flushed on exit by calling <see cref="Log.CloseAndFlush"/> in a <c>finally</c> block.
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
