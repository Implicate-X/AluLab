using System;
using System.Collections;
using ImplicateX.Display;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Devices.I2c;
using GHIElectronics.TinyCLR.Drivers.BasicGraphics;
using System.Threading;
using System.Diagnostics;

#nullable enable

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
        private GpioController? gpioController_ = null;

        /// <summary>I2C controller instance used to communicate with the OLED display.</summary>
        private I2cController? i2cController_ = null;

        /// <summary>SSD1306 display driver instance.</summary>
        private SD1306? displayController_;

        private GpioPin? octalSwitchesShutdownPin_;

        private GpioPin[] busEnablePins = new GpioPin[ 3 ];

		private GpioPin[] selectWayPins = new GpioPin[ 3 ];

        /// <summary>Rotary encoder input pins (quadrature A/B).</summary>
        private GpioPin? encoderPinA_;
        private GpioPin? encoderPinB_;

        /// <summary>Rotary encoder helper for decoding A/B transitions into steps.</summary>
        private RotaryEncoder? encoder_;

        private OctalSwitchV4? octalSwitchV4_;
        private OctalSwitchV5? octalSwitchV5_;
        private OctalSwitchV6? octalSwitchV6_;
        private OctalSwitchV7? octalSwitchV7_;

        /// <summary>
        /// Index of the currently selected gateway (0..2).
        /// </summary>
        private byte gatewayIndex_ = 2;

        /// <summary>Convenience constant for drawing in 1bpp graphics buffer.</summary>
        private const uint COLOR_WHITE = 0x00ffffffU;

        /// <summary>Human-readable names shown on the OLED for each gateway index.</summary>
        private readonly string[] GATEWAY_NAME = { "Desktop", "Raspberry", "GHI Domino" };

        /// <summary>
        /// Backing graphics buffer for the OLED display.
        /// </summary>
        /// <remarks>
        /// The buffer is rendered to the OLED via <see cref="SD1306.DrawBufferNative(byte[])"/>.
        /// </remarks>
        private BasicGraphics? graphics_;

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
            if ( !InitializePins() )
			{
				Debug.WriteLine( "GPIO pins initialization failed!" );
				return;
			}

			if( !InitializeI2cDevices() )
            {
                Debug.WriteLine( "I2C devices initialization failed!" );

                return;
            }

            DrawInfo();
            SelectGateway();
        }

        public bool InitializePins()
		{
			try
			{
				gpioController_ = GpioController.GetDefault();

				octalSwitchesShutdownPin_ = gpioController_.OpenPin( OctalSwitchesControlPin.Shutdown );
				octalSwitchesShutdownPin_.SetDriveMode( GpioPinDriveMode.Output );
                octalSwitchesShutdownPin_.Write( GpioPinValue.Low );

				encoderPinA_ = gpioController_.OpenPin( EncoderPin.A );
				encoderPinB_ = gpioController_.OpenPin( EncoderPin.B );

				encoderPinA_.SetDriveMode( GpioPinDriveMode.InputPullUp );
				encoderPinB_.SetDriveMode( GpioPinDriveMode.InputPullUp );

				// Encoder is polled by the helper; keep GPIO debounce disabled to preserve responsiveness.
				encoderPinA_.DebounceTimeout = TimeSpan.Zero;
				encoderPinB_.DebounceTimeout = TimeSpan.Zero;

				encoder_ = new RotaryEncoder( encoderPinA_, encoderPinB_, transitionsPerStep: 2, pollPeriodMs: 2 );
				encoder_.Stepped += Encoder__Stepped;

				if( !gpioController_.TryOpenPins( out busEnablePins, BusEnablePin.A, BusEnablePin.B, BusEnablePin.C ))
                {
					Debug.WriteLine( "Failed to open bus enable pins!" );
					return false;
                }

				if( !gpioController_.TryOpenPins( out selectWayPins, SelectWayPin.A, SelectWayPin.B, SelectWayPin.C ) )
				{
					Debug.WriteLine( "Failed to open select way pins!" );
					return false;
				}

				encoderPinA_ = gpioController_.OpenPin( EncoderPin.A );
				encoderPinB_ = gpioController_.OpenPin( EncoderPin.B );

                for( int i = 0; i < busEnablePins.Length; i++ )
                {
				    busEnablePins[i].SetDriveMode( GpioPinDriveMode.Output );
					busEnablePins[i].Write( GpioPinValue.Low );
                }

				for( int i = 0; i < selectWayPins.Length; i++ )
				{
					selectWayPins[ i ].SetDriveMode( GpioPinDriveMode.InputPullUp );
					selectWayPins[ i ].ValueChangedEdge = GpioPinEdge.FallingEdge;
					selectWayPins[ i ].ValueChanged += SelectWay_ValueChanged;
				}

				octalSwitchesShutdownPin_.Write( GpioPinValue.High );
				return true;
			}
			catch( Exception ex )
			{
				Debug.WriteLine( $"GPIO pin initialization failed: {ex.Message}" );
				return false;
			}
		}


		public bool InitializeI2cDevices()
        {
            i2cController_ = I2cController.FromName( SC13048.I2cBus.I2c1 );

            octalSwitchV4_ =
                new OctalSwitchV4(
                    i2cController_.GetDevice(
                        OctalSwitchV4.GetConnectionSettings( OctalSwitchV4.Address ) ) );

            if( !octalSwitchV4_.Initialize() )
            {
                Debug.WriteLine( "Initializing of MAX14662 (V4) failed!" );
                return false;
            }

            octalSwitchV5_ =
                new OctalSwitchV5(
                    i2cController_.GetDevice(
                        OctalSwitchV5.GetConnectionSettings( OctalSwitchV5.Address ) ) );

            if( !octalSwitchV5_.Initialize() )
            {
                Debug.WriteLine( "Initializing of MAX14662 (V5) failed!" );
                return false;
            }

            octalSwitchV6_ =
                new OctalSwitchV6(
                    i2cController_.GetDevice(
                        OctalSwitchV6.GetConnectionSettings( OctalSwitchV6.Address ) ) );

            if( !octalSwitchV6_.Initialize() )
            {
                Debug.WriteLine( "Initializing of MAX14662 (V6) failed!" );
                return false;
            }

            octalSwitchV7_ =
                new OctalSwitchV7(
                    i2cController_.GetDevice(
                        OctalSwitchV7.GetConnectionSettings( OctalSwitchV7.Address ) ) );

            if( !octalSwitchV7_.Initialize() )
            {
                Debug.WriteLine( "Initializing of MAX14662 (V7) failed!" );
                return false;
            }

            if( !ProbeAddress( SD1306.GetConnectionSettings().SlaveAddress ) )
            {
                Debug.WriteLine( "Probing SSD1306 OLED failed!" );
                return false;
            }

            try
            {
                displayController_ =
                    new SD1306( i2cController_.GetDevice( SD1306.GetConnectionSettings() ) );

                graphics_ = new BasicGraphics( 128, 32, ColorFormat.OneBpp );
            }
            catch( Exception ex )
            {
                Debug.WriteLine( $"I2C probe for SSD1306 OLED failed: {ex.Message}" );
                return false;	
            }

            return true;
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
            for ( int i = 0; i < busEnablePins.Length; i++ )
			{
				busEnablePins[ i ].Write( GpioPinValue.Low );
			}

            Thread.Sleep( 500 );

            busEnablePins[ gatewayIndex_ ].Write( GpioPinValue.High );
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
            if( graphics_ == null || displayController_ == null )
			{
				Debug.WriteLine( "Graphics or display controller not initialized!" );
				return;
			}

			graphics_.Clear();
            graphics_.DrawString( GATEWAY_NAME[ gatewayIndex_ ], COLOR_WHITE, 0, 4, 2, 2 );
            graphics_.DrawRectangle( COLOR_WHITE, gatewayIndex_ * 42, 24, 42, 8 );
            displayController_.DrawBufferNative( graphics_.Buffer );
        }

        private bool ProbeAddress( int address )
        {
            bool result = false;

            if ( i2cController_ == null )
			{
				Debug.WriteLine( "I2C controller not initialized!" );
				return false;
			}

			I2cDevice dev = 
                i2cController_.GetDevice( 
                    new I2cConnectionSettings( address, I2cMode.Master, I2cAddressFormat.SevenBit, 100_000U ) );

            byte[] rBuf = new byte[ 1 ];

            try
            {
                if( dev.ReadPartial( rBuf ).Status != I2cTransferStatus.SlaveAddressNotAcknowledged )
                {
                    result = true;
                }
            }
            catch( Exception ex )
            {
                Debug.WriteLine( $"I2C probe failed for address 0x{address:X2}: {ex.Message}" );
                result = false;	
            }

            dev.Dispose();

            return result;
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

		private void SelectWay_ValueChanged( GpioPin sender, GpioPinValueChangedEventArgs e )
		{
		    gatewayIndex_ = sender.PinNumber == SelectWayPin.A ? ( byte )0 :
							sender.PinNumber == SelectWayPin.B ? ( byte )1 :
							sender.PinNumber == SelectWayPin.C ? ( byte )2 : gatewayIndex_;
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
            if( encoder_ != null )
			{
				encoder_.Stepped -= Encoder__Stepped;
                encoder_?.Dispose();
            }
            displayController_?.Dispose();
            i2cController_?.Dispose();
            encoderPinA_?.Dispose();
            encoderPinB_?.Dispose();
        }
    }
}
