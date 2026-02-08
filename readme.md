# AluLab

> **Note:**
>
> _The AluLab is still in the development phases._

## Overview

Desktop applications starts in the `HousingWindow`, whereas, Mobile/Web uses `HousingView`.

```cs
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

						services.AddSingleton<IBoardHardwareContext, WorkbenchHardwareContext>();
						services.AddSingleton<IBoardProvider, BoardProvider>();
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
									// Workbench is hardware host: from now on, PinToggled comes in from the server,
									// is written to MCP23017, and outputs are reported as AluOutputsChanged.
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
				}
			} );
```
