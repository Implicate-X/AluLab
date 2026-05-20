using System;
using System.IO;
using System.Threading;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using Avalonia;
using Serilog;
using Serilog.Events;
using AluLab.Common;
using GHIElectronics.Endpoint.Core;
using GHIElectronics.Endpoint.Devices.Display;
using GHIElectronics.Endpoint.Drivers.Avalonia.Input;
using GHIElectronics.Endpoint.Drivers.FocalTech.FT5xx6;

using AluLab.Board.Services;
using AluLab.Board.Platform;
using AluLab.Gateway.Domino.Hardware;

namespace AluLab.Gateway.Domino;

internal static class Program
{
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


	[STAThread]
	static void Main( string[] args )
	{
		ConfigureLogging();

		try
		{
			Console.WriteLine( "Initializing display 2..." );

			// Framebuffer timing configuration for an 800x480 panel.
			// The *_start / *_end / *total values define the sync pulse and porch timings.
			var configuration = new FBDisplay.Configuration
			{
				Clock = 12000,
				Width = 800,
				Hsync_start = 800 + 2,
				Hsync_end = 800 + 2 + 41,
				Htotal = 800 + 2 + 41 + 2,
				Height = 480,
				Vsync_start = 480 + 2,
				Vsync_end = 480 + 2 + 10,
				Vtotal = 480 + 2 + 10 + 2,
			};

			// Create the framebuffer display device and attach a controller used by Avalonia
			// and by the on-screen keyboard integration.
			var fbDisplay = new FBDisplay( configuration );
			var displayController = new DisplayController( fbDisplay );

			Console.WriteLine( "Initializing backlight..." );

			// EPM815 GPIO pins are encoded as a single integer; split into:
			// - "port" (GPIO chip index) and
			// - "pin"  (line within that chip).
			// LibGpiodDriver is instantiated per port.
			var backlightPort = EPM815.Gpio.Pin.PD14 / 16;
			var backlightPin = EPM815.Gpio.Pin.PD14 % 16;
			var gpioBacklightController = new GpioController( PinNumberingScheme.Logical, new LibGpiodDriver( backlightPort ) );
			gpioBacklightController.OpenPin( backlightPin, PinMode.Output );

			// Drive backlight enable high.
			gpioBacklightController.Write( backlightPin, PinValue.High );

			Console.WriteLine( "Initializing touch..." );

			// Hardware reset of the touch controller via a dedicated reset pin.
			// Pulse low briefly, then release to high.
			var resetTouchPin = EPM815.Gpio.Pin.PF2 % 16;
			var resetTouchPort = EPM815.Gpio.Pin.PF2 / 16;
			var gpioTouchController = new GpioController( PinNumberingScheme.Logical, new LibGpiodDriver( resetTouchPort ) );

			gpioTouchController.OpenPin( resetTouchPin );
			gpioTouchController.Write( resetTouchPin, PinValue.Low );
			Thread.Sleep( 100 );
			gpioTouchController.Write( resetTouchPin, PinValue.High );

			// Initialize I2C bus and create the FT5xx6 touch controller.
			// The third argument is the IRQ pin used for touch events.

			EPM815.I2c.Initialize( EPM815.I2c.I2c5 );

			var touch = new FT5xx6Controller( EPM815.I2c.I2c5, EPM815.Gpio.Pin.PB11 );
			Console.WriteLine( "Touch Device Address: " + touch.DeviceAddress.ToString() );
			touch.GestureReceived += ( _, gesture ) => Console.WriteLine( $"Gesture: {gesture}" );
			touch.TouchDown += ( _, b ) => Console.WriteLine( $"Touch down: {b}" );
			touch.TouchUp += ( _, b ) => Console.WriteLine( $"Touch up: {b}" );
			touch.TouchMove += ( _, b ) => Console.WriteLine( $"Touch move: {b}" );
			touch.Width = 800;
			touch.Height = 480;
			// Avalonia input bridge:
			// - forwards FT5xx6 touch events to Avalonia touch events
			// - enables on-screen keyboard integration for the display controller.
			var input = new InputDevice();
			input.EnableOnscreenKeyboard( displayController );

			// Map hardware touch events to Avalonia touch events.
			//touch.TouchDown += ( _, b ) => input.UpdateTouchPoint( b.X, b.Y, TouchEvent.Pressed );
			//touch.TouchUp += ( _, b ) => input.UpdateTouchPoint( b.X, b.Y, TouchEvent.Released );

			Console.WriteLine( "Starting Avalonia..." );

			var builder = BuildAvaloniaApp();

			builder.ConfigureFonts( fontManager =>
			{
				fontManager.AddFontCollection( new FontCollection() );
			} );

			builder.StartLinuxFbDev( args, "/dev/fb0", 1, input );
		}
		catch( Exception ex )
		{
			Log.Fatal( ex, "Application terminated unexpectedly" );
		}
		finally
		{
			Log.CloseAndFlush();
		}

	}

	public static AppBuilder BuildAvaloniaApp()
		=> AppBuilder.Configure<App>()
					.UsePlatformDetect()
					.WithInterFont()
					.LogToTrace();
}
