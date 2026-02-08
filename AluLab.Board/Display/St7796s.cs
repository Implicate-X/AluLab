using System.Device.Gpio;
using System.Device.Spi;
using System.Drawing;
using Iot.Device.Graphics;

namespace AluLab.Board.Display
{
	/// <summary>
	/// Represents a driver for the ST7796S-based SPI graphic display, providing methods to initialize, control, and render
	/// graphics to the screen.
	/// </summary>
	/// <remarks>This class provides high-level graphics operations and low-level display control for ST7796S-based
	/// screens. It manages SPI and GPIO resources, supports orientation changes, and offers methods for clearing, filling,
	/// and updating the display. Thread safety is not guaranteed; callers should ensure appropriate synchronization if
	/// accessing from multiple threads.</remarks>
	/// <param name="spiDevice">The SPI device used for communication with the display controller. Must be initialized and configured for the
	/// display's requirements.</param>
	/// <param name="dataCommandPin">The GPIO pin number used to switch between data and command modes for the display.</param>
	/// <param name="resetPin">The GPIO pin number used to reset the display. Set to a negative value if no reset pin is connected.</param>
	/// <param name="backlightPin">The GPIO pin number used to control the display's backlight. Set to -1 if backlight control is not required.</param>
	/// <param name="spiBufferSize">The maximum number of bytes to send in a single SPI transfer. Must be greater than 0.</param>
	/// <param name="gpioController">The GPIO controller instance to use for pin operations. If null, a new controller will be created and managed by
	/// the driver.</param>
	/// <param name="orientation">The initial display orientation. Determines the default rotation and layout of the screen.</param>
	/// <param name="shouldDispose">true to dispose the provided GPIO controller when the display is disposed; otherwise, false. If a controller is
	/// created internally, it will always be disposed.</param>
	public partial class St7796s(
			SpiDevice spiDevice,
			int dataCommandPin,
			int resetPin,
			int backlightPin = -1,
			int spiBufferSize = St7796s.DefaultSPIBufferSize,
			GpioController? gpioController = null,
			St7796s.Orientation orientation = St7796s.Orientation.LandscapeFlipped,
			bool shouldDispose = true ) : GraphicDisplay
	{
		private const int DefaultSPIBufferSize = 0x1000;

		internal const int SX = 320;
		internal const int SY = 480;

		/// <summary>
		/// Represents the memory access control value for the normal portrait display orientation using MX and BGR flags.
		/// </summary>
		/// <remarks>This constant is intended for internal use when configuring display orientation and color order.
		/// It combines the MX (row address order) and BGR (color order) flags to specify the standard portrait
		/// mode.</remarks>
		internal const byte PortraitNormal =
			( byte )( MemoryAccessControlFlag.MX | MemoryAccessControlFlag.BGR );
		internal const byte PortraitFlipped =
			( byte )( MemoryAccessControlFlag.MY | MemoryAccessControlFlag.BGR );
		internal const byte LandscapeNormal =
			( byte )( MemoryAccessControlFlag.MV | MemoryAccessControlFlag.BGR );
		internal const byte LandscapeFlipped =
			( byte )( MemoryAccessControlFlag.MV | MemoryAccessControlFlag.BGR | MemoryAccessControlFlag.MY | MemoryAccessControlFlag.MX );

		private readonly int _dcPinId = dataCommandPin;
		private readonly int _resetPinId = resetPin;
		private readonly int _backlightPin = backlightPin;
		private readonly int _spiBufferSize = spiBufferSize;
		private readonly bool _shouldDispose = shouldDispose || gpioController is null;

		private SpiDevice? _spiDevice = spiDevice;
		private GpioController _gpioDevice = gpioController ?? new GpioController();

		private protected Rgb565[]? _screenBuffer;
		private protected Rgb565[]? _previousBuffer;

		private double _fps = 0;
		private DateTimeOffset _lastUpdate = DateTimeOffset.UtcNow;

		private protected Orientation _orientation = orientation;

		/// <summary>
		/// Initializes the display controller and prepares the device for operation.
		/// </summary>
		/// <remarks>This method must be called before performing any display operations. It configures the necessary
		/// GPIO pins, resets the display, and allocates internal buffers. Calling this method multiple times may reinitialize
		/// the device and overwrite any existing display state.</remarks>
		/// <exception cref="ArgumentException">Thrown if the configured SPI buffer size is less than or equal to zero.</exception>
		public void Initialize()
		{
			if( _spiBufferSize <= 0 )
			{
				throw new ArgumentException( "Buffer size must be larger than 0.", nameof( spiBufferSize ) );
			}

			_gpioDevice.OpenPin( _dcPinId, PinMode.Output );
			if( _resetPinId >= 0 )
			{
				_gpioDevice.OpenPin( _resetPinId, PinMode.Output );
			}

			if( _backlightPin != -1 )
			{
				_gpioDevice.OpenPin( _backlightPin, PinMode.Output );
				TurnBacklightOn();
			}

			ResetDisplayAsync().Wait();

			InitDisplayParameters();

			_screenBuffer = new Rgb565[ ScreenWidth * ScreenHeight ];
			_previousBuffer = new Rgb565[ ScreenWidth * ScreenHeight ];

			SendFrame( true );
		}

		/// <summary>
		/// Specifies the display orientation for devices or graphical interfaces.
		/// </summary>
		/// <remarks>Use this enumeration to indicate the desired rotation of the display. The values correspond to
		/// standard portrait and landscape orientations, including their rotated variants. Orientation values may affect
		/// rendering, input handling, and layout calculations depending on the device or framework.</remarks>
		public enum Orientation : byte
		{
			/// <summary> Default or normal orientation. </summary>
			PortraitNormal = St7796s.PortraitNormal,

			/// <summary> Rotated 90 degrees clockwise. </summary>
			LandscapeNormal = St7796s.LandscapeNormal,

			/// <summary> Rotated 180 degrees clockwise. </summary>
			PortraitFlipped = St7796s.PortraitFlipped,

			/// <summary> Rotated 270 degrees clockwise. </summary>
			LandscapeFlipped = St7796s.LandscapeFlipped
		}


		/// <summary>
		/// Gets the width of the screen, in pixels, based on the current orientation.
		/// </summary>
		/// <remarks>The returned value reflects the horizontal pixel count for the active screen orientation. For
		/// landscape orientations, this may differ from portrait orientations. This property is read-only.</remarks>
		public override int ScreenWidth => _orientation switch
		{
			Orientation.PortraitNormal => SX,
			Orientation.LandscapeNormal => SY,
			Orientation.PortraitFlipped => SX,
			Orientation.LandscapeFlipped => SY,
			_ => SX
		};

		/// <summary>
		/// Gets the height of the screen, in pixels, based on the current orientation.
		/// </summary>
		/// <remarks>The returned value reflects the vertical dimension of the display for the active orientation. For
		/// landscape orientations, this may correspond to the physical width of the device.</remarks>
		public override int ScreenHeight => _orientation switch
		{
			Orientation.PortraitNormal => SY,
			Orientation.LandscapeNormal => SX,
			Orientation.PortraitFlipped => SY,
			Orientation.LandscapeFlipped => SX,
			_ => SY
		};

		/// <inheritdoc />
		public override PixelFormat NativePixelFormat => PixelFormat.Format16bppRgb565;

		/// <summary>
		/// Returns the last FPS value (frames per second).
		/// The value is unfiltered.
		/// </summary>
		public double Fps => _fps;

		/// <summary>
		/// Configure memory and orientation parameters
		/// </summary>
		protected virtual void InitDisplayParameters()
		{
			SendCommand( St7796sCommand.SoftwareReset );
			Thread.Sleep( 150 );
			SendCommand( St7796sCommand.SleepOut );
			Thread.Sleep( 150 );

			SendCommand( St7796sCommand.CommandSetControl, 0xC3 );
			SendCommand( St7796sCommand.CommandSetControl, 0x96 );

			SendCommand( St7796sCommand.MemoryAccessControl, ( byte )_orientation );
			SendCommand( St7796sCommand.ColModPixelFormatSet, 0x55 );

			SendCommand( St7796sCommand.DisplayInversionControl, 0x01 );
			SendCommand( St7796sCommand.EntryModeSet, 0xC6 );

			SendCommand( St7796sCommand.ColumnAddressSet, 0x00, 0x00, ( byte )( ( ScreenWidth - 1 ) >> 8 ), ( byte )( ( ScreenWidth - 1 ) & 0xFF ) );
			SendCommand( St7796sCommand.PageAddressSet, 0x00, 0x00, ( byte )( ( ScreenHeight - 1 ) >> 8 ), ( byte )( ( ScreenHeight - 1 ) & 0xFF ) );

			SendCommand( St7796sCommand.PowerControl2, 0x15 );
			SendCommand( St7796sCommand.PowerControl3, 0xAF );
			SendCommand( St7796sCommand.VcomControl1, 0x22 );
			SendCommand( St7796sCommand.VcomOffsetRegister, 0x00 );
			SendCommand( St7796sCommand.DisplayOutputCtrlAdjust, 0x40, 0x8A, 0x00, 0x00, 0x29, 0x19, 0xA5, 0x33 );

			SendCommand( St7796sCommand.PositiveGammaCorrection, 0xF0, 0x04, 0x08, 0x09, 0x08, 0x15, 0x2F, 0x42, 0x46, 0x28, 0x15, 0x16, 0x29, 0x2D );
			SendCommand( St7796sCommand.NegativeGammaCorrection, 0xF0, 0x04, 0x09, 0x09, 0x08, 0x15, 0x2E, 0x46, 0x46, 0x28, 0x15, 0x15, 0x29, 0x2D );

			SendCommand( St7796sCommand.NormalDisplayModeOn );
			SendCommand( St7796sCommand.WriteControlDisplay, 0x24 );

			SendCommand( St7796sCommand.CommandSetControl, 0x3C );
			SendCommand( St7796sCommand.CommandSetControl, 0x69 );
			SendCommand( St7796sCommand.DisplayOn );
			Thread.Sleep( 150 );
		}

		/// <summary>
		/// Send a command to the the display controller along with associated parameters.
		/// </summary>
		/// <param name="command">Command to send.</param>
		/// <param name="commandParameters">parameteters for the command to be sent</param>
		internal void SendCommand( St7796sCommand command, params byte[] commandParameters )
		{
			SendCommand( command, commandParameters.AsSpan() );
		}

		/// <summary>
		/// Send a command to the the display controller along with parameters.
		/// </summary>
		/// <param name="command">Command to send.</param>
		/// <param name="data">Span to send as parameters for the command.</param>
		internal void SendCommand( St7796sCommand command, Span<byte> data )
		{
			Span<byte> commandSpan = stackalloc byte[]
			{
				(byte)command
			};

			SendSPI( commandSpan, true );

			if( !data.IsEmpty && data.Length > 0 )
			{
				SendSPI( data );
			}
		}

		/// <summary>
		/// Send data to the display controller.
		/// </summary>
		/// <param name="data">The data to send to the display controller.</param>
		private void SendData( Span<byte> data )
		{
			SendSPI( data, blnIsCommand: false );
		}

		/// <summary>
		/// Write a block of data to the SPI device
		/// </summary>
		/// <param name="data">The data to be sent to the SPI device</param>
		/// <param name="blnIsCommand">A flag indicating that the data is really a command when true or data when false.</param>
		private void SendSPI( Span<byte> data, bool blnIsCommand = false )
		{
			int index = 0;
			int len;

			if ( _spiDevice == null )
			{
				throw new InvalidOperationException( "SPI device not set" );
			}

			if( _gpioDevice == null )
			{
				throw new InvalidOperationException( "GPIO device not set" ); 
			}

			// set the DC pin to indicate if the data being sent to the display is DATA or COMMAND bytes.
			_gpioDevice.Write( _dcPinId, blnIsCommand ? PinValue.Low : PinValue.High );

			// write the array of bytes to the display. (in chunks of SPI Buffer Size)
			do
			{
				// calculate the amount of spi data to send in this chunk
				len = Math.Min( data.Length - index, _spiBufferSize );
				// send the slice of data off set by the index and of length len.
				_spiDevice.Write( data.Slice( index, len ) );
				// add the length just sent to the index
				index += len;
			}
			while( index < data.Length ); // repeat until all data sent.
		}

		/// <summary>
		/// Sets the active window (region) on the display for subsequent memory writes.
		/// </summary>
		/// <param name="x0">Start column (x coordinate).</param>
		/// <param name="y0">Start page (y coordinate).</param>
		/// <param name="x1">End column (x coordinate).</param>
		/// <param name="y1">End page (y coordinate).</param>
		private void SetWindow( int x0, int y0, int x1, int y1 )
		{
			SendCommand( St7796sCommand.ColumnAddressSet );
			Span<byte> data = stackalloc byte[ 4 ]
			{
				(byte)(x0 >> 8),
				(byte)x0,
				(byte)(x1 >> 8),
				(byte)x1,
			};
			SendData( data );
			SendCommand( St7796sCommand.PageAddressSet );
			Span<byte> data2 = stackalloc byte[ 4 ]
			{
				(byte)(y0 >> 8),
				(byte)y0,
				(byte)(y1 >> 8),
				(byte)y1,
			};
			SendData( data2 );
			SendCommand( St7796sCommand.MemoryWrite );
		}

		/// <summary>
		/// Fill rectangle to the specified color
		/// </summary>
		/// <param name="color">The color to fill the rectangle with.</param>
		/// <param name="x">The x co-ordinate of the point to start the rectangle at in pixels.</param>
		/// <param name="y">The y co-ordinate of the point to start the rectangle at in pixels.</param>
		/// <param name="w">The width of the rectangle in pixels.</param>
		/// <param name="h">The height of the rectangle in pixels.</param>
		public void FillRect( Color color, int x, int y, int w, int h )
		{
			FillRect( color, x, y, w, h, false );
		}

		/// <summary>
		/// Fill rectangle to the specified color
		/// </summary>
		/// <param name="color">The color to fill the rectangle with.</param>
		/// <param name="x">The x co-ordinate of the point to start the rectangle at in pixels.</param>
		/// <param name="y">The y co-ordinate of the point to start the rectangle at in pixels.</param>
		/// <param name="w">The width of the rectangle in pixels.</param>
		/// <param name="h">The height of the rectangle in pixels.</param>
		/// <param name="doRefresh">True to immediately update the screen, false to only update the back buffer</param>
		private void FillRect( Color color, int x, int y, int w, int h, bool doRefresh )
		{
			if ( _screenBuffer == null )
			{
				throw new InvalidOperationException( "Screen buffer not initialized" );
			}

			// Begrenzung der Parameter auf gültige Werte
			if (x < 0) x = 0;
			if (y < 0) y = 0;
			if (w < 0) w = 0;
			if (h < 0) h = 0;
			if (x + w > ScreenWidth) w = ScreenWidth - x;
			if (y + h > ScreenHeight) h = ScreenHeight - y;

			Span<byte> colourBytes = stackalloc byte[ 2 ]; // create a short span that holds the colour data to be sent to the display

			// set the colourbyte array to represent the fill colour
			var c = Rgb565.FromRgba32( color );

			// set the pixels in the array representing the raw data to be sent to the display
			// to the fill color
			for( int j = y; j < y + h; j++ )
			{
				for( int i = x; i < x + w; i++ )
				{
					_screenBuffer[ i + j * ScreenWidth ] = c;
				}
			}

			if( doRefresh )
			{
				SendFrame( false );
			}
		}

		/// <summary>
		/// Clears the screen to a specific color
		/// </summary>
		/// <param name="color">The color to clear the screen to</param>
		/// <param name="doRefresh">Immediately force an update of the screen. If false, only the backbuffer is cleared.</param>
		public void ClearScreen( Color color, bool doRefresh )
		{
			FillRect( color, 0, 0, ScreenWidth, ScreenHeight, doRefresh );
		}

		/// <summary>
		/// Clears the screen to black
		/// </summary>
		/// <param name="doRefresh">Immediately force an update of the screen. If false, only the backbuffer is cleared.</param>
		public void ClearScreen( bool doRefresh )
		{
			FillRect( Color.FromArgb( 0, 0, 0 ), 0, 0, ScreenWidth, ScreenHeight, doRefresh );
		}

		/// <summary>
		/// Immediately clears the screen to black.
		/// </summary>
		public override void ClearScreen()
		{
			ClearScreen( true );
		}

		/// <summary>
		/// Resets the display.
		/// </summary>
		public async Task ResetDisplayAsync()
		{
			if (_gpioDevice == null)
			{
				throw new InvalidOperationException( "GPIO device not set" );
			}

			if( _resetPinId < 0 )
			{
				return;
			}

			_gpioDevice.Write( _resetPinId, PinValue.High );
			await Task.Delay( 120 ).ConfigureAwait( false );
			_gpioDevice.Write( _resetPinId, PinValue.Low );
			await Task.Delay( 120 ).ConfigureAwait( false );
			_gpioDevice.Write( _resetPinId, PinValue.High );
			await Task.Delay( 120 ).ConfigureAwait( false );
		}

		/// <summary>
		/// This command turns the backlight panel off.
		/// </summary>
		public void TurnBacklightOn()
		{
			if (_gpioDevice == null )
			{
				throw new InvalidOperationException( "GPIO device not set" );
			}

			if( _backlightPin == -1 )
			{
				throw new InvalidOperationException( "Backlight pin not set" );
			}

			_gpioDevice.Write( _backlightPin, PinValue.High );
		}

		/// <summary>
		/// This command turns the backlight panel off.
		/// </summary>
		public void TurnBacklightOff()
		{
			if( _gpioDevice == null )
			{
				throw new InvalidOperationException( "GPIO device not set" );
			}

			if( _backlightPin == -1 )
			{
				throw new InvalidOperationException( "Backlight pin not set" );
			}

			_gpioDevice.Write( _backlightPin, PinValue.Low );
		}

		/// <summary>
		/// Reads 8 bits of data from ST7796S configuration memory (not RAM).
		/// This is undocumented and not officially supported, but can be useful for debugging.
		/// </summary>
		/// <param name="commandByte">The command register to read data from.</param>
		/// <param name="index">The byte index into the command to read from.</param>
		/// <returns>Unsigned 8-bit data read from ST7796S register.</returns>
		public byte ReadCommand8(byte commandByte, byte index)
		{
			// Set Index Register (0xFB) to desired offset
			Span<byte> setIndex = stackalloc byte[1] { (byte)(0x10 + index) };
			SendCommand(St7796sCommand.SpiReadControl, setIndex);

			// Read the command register (using SPI)
			byte ret = ReadRegister(commandByte);

			// Reset Index Register (0xFB) to 0
			Span<byte> resetIndex = stackalloc byte[1] { 0x00 };
			SendCommand(St7796sCommand.SpiReadControl, resetIndex);

			return ret;
		}

		/// <summary>
		/// Reads a single byte from a register using SPI.
		/// </summary>
		private byte ReadRegister(byte command)
		{
			if (_spiDevice == null || _gpioDevice == null)
				throw new InvalidOperationException("SPI or GPIO device not set");

			// Set DC low for command
			_gpioDevice.Write(_dcPinId, PinValue.Low);
			Span<byte> cmd = stackalloc byte[1] { command };
			_spiDevice.Write(cmd);

			// Set DC high for data
			_gpioDevice.Write(_dcPinId, PinValue.High);
			Span<byte> readBuffer = stackalloc byte[1];
			_spiDevice.Read(readBuffer);

			return readBuffer[0];
		}

		/// <inheritdoc />
		protected override void Dispose( bool disposing )
		{
			if( disposing )
			{
				if( _gpioDevice != null )
				{
					if( _resetPinId >= 0 )
					{
						_gpioDevice.ClosePin( _resetPinId );
					}

					if( _backlightPin >= 0 )
					{
						_gpioDevice.ClosePin( _backlightPin );
					}

					if( _dcPinId >= 0 )
					{
						_gpioDevice.ClosePin( _dcPinId );
					}

					if( _shouldDispose )
					{
						_gpioDevice?.Dispose();
					}

					_gpioDevice = null!;
				}

				_spiDevice?.Dispose();
				_spiDevice = null!;
			}
		}
	}
}
