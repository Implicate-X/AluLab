using System;
using System.Collections;
using ImplicateX.Display;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Devices.I2c;
using GHIElectronics.TinyCLR.Drivers.BasicGraphics;
using System.Threading;

namespace AluLab.Gateway.Controller
{
	/// <summary>
	/// Board-level hardware abstraction for the gateway controller.
	/// </summary>
	/// <remarks>
	/// Responsibilities:
	/// <list type="bullet">
	/// <item><description>Initialize GPIO and I2C peripherals (TinyCLR).</description></item>
	/// <item><description>Drive an SSD1306 OLED to display the currently selected gateway.</description></item>
	/// <item><description>Read user input via a rotary encoder and three dedicated selection inputs.</description></item>
	/// <item><description>Select one of three "gateway" paths by enabling/disabling the corresponding I2C/bus lines.</description></item>
	/// </list>
	/// Pin identifiers such as <c>I2cEnablePin.A</c>, <c>BusEnablePin.B</c>, <c>SelectWayPin.C</c>, and <c>EncoderPin.A</c>
	/// are expected to be defined in another part of this <c>partial</c> class (or related types).
	/// </remarks>
	internal partial class Board : IDisposable
	{
		/// <summary>Default GPIO controller instance.</summary>
		private GpioController gpioController_;

		/// <summary>I2C controller instance used to communicate with the OLED display.</summary>
		private I2cController i2cController_;

		/// <summary>SSD1306 display driver instance.</summary>
		private SD1306 displayController_;

		/// <summary>GPIO pins used to enable I2C routing for gateway paths A/B/C.</summary>
		private GpioPin i2cEnablePinA_;
		private GpioPin i2cEnablePinB_;
		private GpioPin i2cEnablePinC_;

		/// <summary>GPIO pins used to enable/disable the main bus for gateway paths A/B/C.</summary>
		private GpioPin busEnablePinA_;
		private GpioPin busEnablePinB_;
		private GpioPin busEnablePinC_;

		/// <summary>GPIO inputs used as direct "select gateway" buttons (A/B/C).</summary>
		private GpioPin selectWayPinA_;
		private GpioPin selectWayPinB_;
		private GpioPin selectWayPinC_;

		/// <summary>Rotary encoder input pins (quadrature A/B).</summary>
		private GpioPin encoderPinA_;
		private GpioPin encoderPinB_;

		/// <summary>Rotary encoder helper for decoding A/B transitions into steps.</summary>
		private RotaryEncoder encoder_;

		/// <summary>
		/// Index of the currently selected gateway (0..2).
		/// </summary>
		private byte gatewayIndex_ = 1;

		/// <summary>Convenience constant for drawing in 1bpp graphics buffer.</summary>
		private const uint COLOR_WHITE = 0x00ffffffU;

		/// <summary>Human-readable names shown on the OLED for each gateway index.</summary>
		private readonly string[] GATEWAY_NAME = { "Desktop", "GHI Domino", "Raspberry" };

		/// <summary>
		/// Backing graphics buffer for the OLED display.
		/// </summary>
		/// <remarks>
		/// The buffer is rendered to the OLED via <see cref="SD1306.DrawBufferNative(byte[])"/>.
		/// </remarks>
		private BasicGraphics graphics_;

		/// <summary>
		/// Synchronizes concurrent draw/select operations triggered by encoder events.
		/// </summary>
		private readonly object drawLock_ = new();

		/// <summary>
		/// Initializes GPIO, rotary encoder input, I2C bus, OLED controller, and renders the initial UI.
		/// </summary>
		/// <remarks>
		/// Initialization flow:
		/// <list type="number">
		/// <item><description>Open all required GPIO pins and set drive modes.</description></item>
		/// <item><description>Configure selection inputs (pull-up + falling edge interrupt).</description></item>
		/// <item><description>Configure and start the rotary encoder step decoder.</description></item>
		/// <item><description>Open I2C and create the SSD1306 display driver.</description></item>
		/// <item><description>Render initial selection state and apply routing to the selected gateway.</description></item>
		/// </list>
		/// </remarks>
		public void Initialize()
		{
			gpioController_ = GpioController.GetDefault();

			i2cEnablePinA_ = gpioController_.OpenPin( I2cEnablePin.A );
			i2cEnablePinB_ = gpioController_.OpenPin( I2cEnablePin.B );
			i2cEnablePinC_ = gpioController_.OpenPin( I2cEnablePin.C );

			busEnablePinA_ = gpioController_.OpenPin( BusEnablePin.A );
			busEnablePinB_ = gpioController_.OpenPin( BusEnablePin.B );
			busEnablePinC_ = gpioController_.OpenPin( BusEnablePin.C );

			selectWayPinA_ = gpioController_.OpenPin( SelectWayPin.A );
			selectWayPinB_ = gpioController_.OpenPin( SelectWayPin.B );
			selectWayPinC_ = gpioController_.OpenPin( SelectWayPin.C );

			i2cEnablePinA_.SetDriveMode( GpioPinDriveMode.Output );
			i2cEnablePinB_.SetDriveMode( GpioPinDriveMode.Output );
			i2cEnablePinC_.SetDriveMode( GpioPinDriveMode.Output );

			busEnablePinA_.SetDriveMode( GpioPinDriveMode.Output );
			busEnablePinB_.SetDriveMode( GpioPinDriveMode.Output );
			busEnablePinC_.SetDriveMode( GpioPinDriveMode.Output );

			// Start with I2C routing disabled for all paths.
			i2cEnablePinA_.Write( GpioPinValue.Low );
			i2cEnablePinB_.Write( GpioPinValue.Low );
			i2cEnablePinC_.Write( GpioPinValue.Low );

			// Start with bus disabled (active-low) for all paths.
			busEnablePinA_.Write( GpioPinValue.High );
			busEnablePinB_.Write( GpioPinValue.High );
			busEnablePinC_.Write( GpioPinValue.High );

			// Direct selection inputs (buttons/switches) are pull-ups; falling edge indicates activation.
			selectWayPinA_.SetDriveMode( GpioPinDriveMode.InputPullUp );
			selectWayPinB_.SetDriveMode( GpioPinDriveMode.InputPullUp );
			selectWayPinC_.SetDriveMode( GpioPinDriveMode.InputPullUp );

			selectWayPinA_.ValueChangedEdge = GpioPinEdge.FallingEdge;
			selectWayPinB_.ValueChangedEdge = GpioPinEdge.FallingEdge;
			selectWayPinC_.ValueChangedEdge = GpioPinEdge.FallingEdge;

			selectWayPinA_.ValueChanged += SelectWayPinA__ValueChanged;
			selectWayPinB_.ValueChanged += SelectWayPinB__ValueChanged;
			selectWayPinC_.ValueChanged += SelectWayPinC__ValueChanged;

			encoderPinA_ = gpioController_.OpenPin( EncoderPin.A );
			encoderPinB_ = gpioController_.OpenPin( EncoderPin.B );

			encoderPinA_.SetDriveMode( GpioPinDriveMode.InputPullUp );
			encoderPinB_.SetDriveMode( GpioPinDriveMode.InputPullUp );

			// Encoder is polled by the helper; keep GPIO debounce disabled to preserve responsiveness.
			encoderPinA_.DebounceTimeout = TimeSpan.Zero;
			encoderPinB_.DebounceTimeout = TimeSpan.Zero;

			encoder_ = new RotaryEncoder( encoderPinA_, encoderPinB_, transitionsPerStep: 2, pollPeriodMs: 2 );
			encoder_.Stepped += Encoder__Stepped;

			i2cController_ = I2cController.FromName( SC13048.I2cBus.I2c1 );

			displayController_ =
				new SD1306( i2cController_.GetDevice( SD1306.GetConnectionSettings() ) );

			graphics_ = new BasicGraphics( 128, 32, ColorFormat.OneBpp );

			DrawInfo();
			SelectGateway();
		}

		/// <summary>
		/// Applies the current <see cref="gatewayIndex_"/> to the hardware routing pins.
		/// </summary>
		/// <remarks>
		/// Behavior:
		/// <list type="bullet">
		/// <item><description>First disables all I2C paths and disables all buses.</description></item>
		/// <item><description>Waits briefly to allow signals to settle.</description></item>
		/// <item><description>Enables the selected path's I2C line and enables the selected bus (active-low).</description></item>
		/// </list>
		/// </remarks>
		private void SelectGateway()
		{
			i2cEnablePinA_.Write( GpioPinValue.Low );
			i2cEnablePinB_.Write( GpioPinValue.Low );
			i2cEnablePinC_.Write( GpioPinValue.Low );

			busEnablePinA_.Write( GpioPinValue.High );
			busEnablePinB_.Write( GpioPinValue.High );
			busEnablePinC_.Write( GpioPinValue.High );

			// Allow hardware mux/bus lines to settle before enabling the selected route.
			Thread.Sleep( 500 );

			switch( gatewayIndex_ )
			{
				case 0:
					i2cEnablePinA_.Write( GpioPinValue.High );
					busEnablePinA_.Write( GpioPinValue.Low );
					break;

				case 1:
					i2cEnablePinB_.Write( GpioPinValue.High );
					busEnablePinB_.Write( GpioPinValue.Low );
					break;

				case 2:
					i2cEnablePinC_.Write( GpioPinValue.High );
					busEnablePinC_.Write( GpioPinValue.Low );
					break;
			}
		}

		/// <summary>
		/// Renders the current gateway selection to the OLED display.
		/// </summary>
		/// <remarks>
		/// UI layout:
		/// <list type="bullet">
		/// <item><description>Gateway name near the top.</description></item>
		/// <item><description>Selection indicator rectangle at the bottom, spaced in 3 columns (0..2).</description></item>
		/// </list>
		/// </remarks>
		private void DrawInfo()
		{
			graphics_.Clear();
			graphics_.DrawString( GATEWAY_NAME[ gatewayIndex_ ], COLOR_WHITE, 0, 4, 2, 2 );
			graphics_.DrawRectangle( COLOR_WHITE, gatewayIndex_ * 42, 24, 42, 8 );
			displayController_.DrawBufferNative( graphics_.Buffer );
		}

		/// <summary>
		/// Handles rotary encoder steps by cycling through gateway options and updating both UI and hardware routing.
		/// </summary>
		/// <param name="step">
		/// Encoder step delta. The sign indicates direction; magnitude indicates number of steps.
		/// </param>
		/// <remarks>
		/// This method uses <see cref="drawLock_"/> to ensure the OLED update and routing update stay consistent.
		/// </remarks>
		private void Encoder__Stepped( int step )
		{
			lock( drawLock_ )
			{
				int len = GATEWAY_NAME.Length;

				// Wrap index within [0..len-1]. Subtracting step makes direction dependent on encoder wiring.
				gatewayIndex_ = ( byte )( ( gatewayIndex_ + len - step ) % len );

				// Defensive clamp (note: gatewayIndex_ is a byte, so "< 0" cannot be true).
				gatewayIndex_ = ( byte )( gatewayIndex_ < 0 ? 0 : gatewayIndex_ > 2 ? 2 : gatewayIndex_ );

				DrawInfo();
				SelectGateway();
			}
		}

		/// <summary>
		/// Direct selection input for gateway A ("Desktop").
		/// </summary>
		private void SelectWayPinA__ValueChanged( GpioPin sender, GpioPinValueChangedEventArgs e )
		{
			gatewayIndex_ = 0;

			DrawInfo();
			SelectGateway();
		}

		/// <summary>
		/// Direct selection input for gateway B ("GHI Domino").
		/// </summary>
		private void SelectWayPinB__ValueChanged( GpioPin sender, GpioPinValueChangedEventArgs e )
		{
			gatewayIndex_ = 1;

			DrawInfo();
			SelectGateway();
		}

		/// <summary>
		/// Direct selection input for gateway C ("Raspberry").
		/// </summary>
		private void SelectWayPinC__ValueChanged( GpioPin sender, GpioPinValueChangedEventArgs e )
		{
			gatewayIndex_ = 2;

			DrawInfo();
			SelectGateway();
		}

		/// <summary>
		/// Releases all board resources (encoder, display, controllers, pins).
		/// </summary>
		virtual public void Dispose()
		{
			Terminate();
		}

		/// <summary>
		/// Internal cleanup routine invoked by <see cref="Dispose"/>.
		/// </summary>
		private void Terminate()
		{
			encoder_.Stepped -= Encoder__Stepped;
			encoder_.Dispose();
			displayController_.Dispose();
			i2cController_.Dispose();
			i2cEnablePinA_.Dispose();
			i2cEnablePinB_.Dispose();
			i2cEnablePinC_.Dispose();
			busEnablePinA_.Dispose();
			busEnablePinB_.Dispose();
			busEnablePinC_.Dispose();
			encoderPinA_.Dispose();
			encoderPinB_.Dispose();
		}
	}
}
