using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Avalonia;
using Serilog;
using AluLab.Common;
using AluLab.Board.Services;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using AluLab.Common.Services;
using AluLab.Common.Relay;

namespace AluLab.Board.Platform;

/// <summary>
/// Creates and configures an Avalonia <see cref="AppBuilder"/> for the desktop “Board” application.
/// </summary>
/// <remarks>
/// This factory centralizes:
/// <list type="bullet">
/// <item><description>Avalonia platform initialization (<c>UsePlatformDetect</c>, fonts, trace logging).</description></item>
/// <item><description>Host/service registration via <see cref="IServiceCollection"/> (logging, board services, display mirroring).</description></item>
/// <item><description>Early board discovery/initialization and optional initial synchronization to hardware.</description></item>
/// <item><description>Attaching <see cref="DisplayService"/> once the main window is ready.</description></item>
/// </list>
/// </remarks>
public static class DesktopAppBuilderFactory
{
	/// <summary>
	/// Builds an Avalonia <see cref="AppBuilder"/> configured with DI services required to run the board UI,
	/// locate the board implementation, initialize hardware, and attach display mirroring.
	/// </summary>
	/// <typeparam name="TProgram">
	/// The program type used for creating the typed logger <see cref="ILogger{TCategoryName}"/>.
	/// Typically the entry point type (e.g., <c>Program</c>).
	/// </typeparam>
	/// <typeparam name="THardwareContext">
	/// The concrete hardware context to register as <see cref="IBoardHardwareContext"/>.
	/// </typeparam>
	/// <param name="configureExtraServices">
	/// Optional callback used to register additional services into the host container.
	/// Invoked after the default registrations for logging, board services, and <see cref="DisplayService"/>.
	/// </param>
	/// <param name="afterBoardInitialized">
	/// Optional callback intended to run after the board has been initialized.
	/// </param>
	/// <returns>
	/// A fully configured <see cref="AppBuilder"/>. The builder wires <c>AfterSetup</c> to configure the
	/// application’s host services, run hardware initialization after the container is built, and attach
	/// display mirroring when the main window becomes available.
	/// </returns>
	/// <remarks>
	/// Initialization flow (high level):
	/// <list type="number">
	/// <item>
	/// <description>
	/// Configure Avalonia (<c>UsePlatformDetect</c>, <c>WithInterFont</c>, <c>LogToTrace</c>).
	/// </description>
	/// </item>
	/// <item>
	/// <description>
	/// In <c>AfterSetup</c>, set <c>app.ConfigureHostServices</c> to register:
	/// <see cref="IBoardHardwareContext"/>, <see cref="IBoardProvider"/>, and <see cref="DisplayService"/>
	/// plus Serilog-backed logging.
	/// </description>
	/// </item>
	/// <item>
	/// <description>
	/// In <c>app.AfterHostServicesBuilt</c>, attempt to resolve <see cref="IBoardProvider"/>, obtain a board,
	/// and call <c>board.Initialize()</c>. On success, apply an initial <see cref="SyncState"/> snapshot
	/// and configure ongoing sync via the board’s ALU controller.
	/// </description>
	/// </item>
	/// <item>
	/// <description>
	/// In <c>app.MainWindowReady</c>, resolve <see cref="DisplayService"/> and attach it to the window.
	/// </description>
	/// </item>
	/// </list>
	/// </remarks>
	public static AppBuilder Create<TProgram, THardwareContext>(
		Action<IServiceCollection>? configureExtraServices = null,
		Action<IServiceProvider, ILogger>? afterBoardInitialized = null )
		where THardwareContext : class, IBoardHardwareContext
	{
		return AppBuilder.Configure<App>()
			.UsePlatformDetect()
			.WithInterFont()
			.LogToTrace()
			.AfterSetup( builder =>
			{
				// Ensure we are configuring the expected Avalonia App instance.
				if( builder.Instance is not App app )
					return;

				// Registers services into the app host container.
				// - Logging is wired to Serilog (providers cleared to avoid duplicates).
				// - Core board services are registered for hardware access and discovery.
				// - DisplayService is registered to enable mirroring/attachment to the main window.
				// - Callers may optionally register additional services.
				app.ConfigureHostServices = services =>
				{
					services.AddLogging( lb =>
					{
						lb.ClearProviders();
						lb.AddSerilog( Log.Logger, dispose: false );
					} );

					services.AddSingleton<IBoardHardwareContext, THardwareContext>();
					services.AddSingleton<IBoardProvider, BoardProvider>();
					services.AddSingleton<DisplayService>();

					configureExtraServices?.Invoke( services );
				};

				// Runs once the host service provider is built. Performs an “early check” to:
				// - Resolve IBoardProvider
				// - Locate the board instance
				// - Initialize the board hardware
				// - Apply initial SyncState snapshot (best-effort)
				// - Configure ongoing ALU sync (best-effort)
				//
				// All failures are logged and do not crash startup.
				app.AfterHostServicesBuilt = async sp =>
				{
					var logger = sp.GetRequiredService<ILogger<TProgram>>();

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

							// Best-effort: fetch current sync snapshot and apply it to the hardware state.
							try
							{
								var sync = new SyncService( "https://iot.homelabs.one/sync" );
								await sync.EnsureConnectedAsync().ConfigureAwait( false );

								SyncState snapshot = await sync.GetStateAsync().ConfigureAwait( false );
								board.AluController.ApplySyncStateToHardware( snapshot );

								logger.LogInformation( "Board: initial SyncState applied to hardware (pins={Count}).", snapshot?.Pins?.Count ?? 0 );
							}
							catch( Exception ex )
							{
								logger.LogWarning( ex, "Board: failed to apply initial SyncState snapshot." );
							}

							// Best-effort: configure ongoing sync on the controller.
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

				// When the main window is constructed and ready, attach the display mirror service.
				// Failures are logged but do not prevent the UI from running.
				app.MainWindowReady += window =>
				{
					try
					{
						var mirror = app.Services.GetRequiredService<DisplayService>();
						mirror.Attach( window );
					}
					catch( Exception ex )
					{
						var logger = app.Services.GetService<ILogger<TProgram>>();
						logger?.LogWarning( ex, "DisplayMirror: attach failed." );
					}
				};
			} );
	}
}