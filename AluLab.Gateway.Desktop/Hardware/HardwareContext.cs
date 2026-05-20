using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;
using AluLab.Board.Platform;

namespace AluLab.Workbench.Hardware;

/// <summary>
/// Provides a concrete <see cref="IBoardHardwareContext"/> backed by an FTDI-based <see cref="SerialBus"/>.
/// </summary>
/// <remarks>
/// <para>
/// This context centralizes creation and ownership of the hardware communication primitives used by the
/// workbench application:
/// </para>
/// <list type="bullet">
/// <item><description>An <see cref="I2cBus"/> for I2C peripherals.</description></item>
/// <item><description><see cref="GpioController"/> instances for GPIO lines on the respective FTDI channels.</description></item>
/// <item><description><see cref="SpiDevice"/> instances for the display and touch controllers.</description></item>
/// </list>
///
/// <para>
/// The FTDI pin indices exposed as properties are the current "as wired" mapping (historically hardcoded
/// in the board implementation). These values are indices in the FTDI GPIO space, not MCU pin numbers.
/// </para>
/// </remarks>
public sealed class HardwareContext : IBoardHardwareContext
{
	private readonly SerialBus _serialBus = new();

	/// <summary>
	/// Initializes the FTDI serial bus and creates the I2C/SPI/GPIO primitives used by the application.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the underlying FTDI <see cref="SerialBus"/> cannot be initialized.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Initialization configures:
	/// </para>
	/// <list type="bullet">
	/// <item><description><see cref="I2cBus"/> using the default bus number from <c>ChannelI2cInOut</c>.</description></item>
	/// <item><description><see cref="DisplaySpi"/> on <c>ChannelSpiDispl</c> using SPI mode 3 at 24 MHz.</description></item>
	/// <item><description><see cref="TouchSpi"/> on <c>ChannelSpiTouch</c> using SPI mode 3 at 2 MHz (8-bit transfers).</description></item>
	/// <item><description>GPIO controllers on each channel for driving auxiliary lines (reset, D/C, interrupts, etc.).</description></item>
	/// </list>
	/// </remarks>
	public HardwareContext()
	{
		if( !_serialBus.Initialize() )
			throw new InvalidOperationException( "FTDI SerialBus initialization failed." );

		var i2c = _serialBus.ChannelI2cInOut.CreateOrGetI2cBus( _serialBus.ChannelI2cInOut.GetDefaultI2cBusNumber() );

		I2cBus = i2c;
		I2cLinesGpio = _serialBus.ChannelI2cInOut.CreateGpioController();

		DisplaySpi = _serialBus.ChannelSpiDispl.CreateSpiDevice( new SpiConnectionSettings( 0 )
		{
			ChipSelectLine = 3,
			ClockFrequency = 24_000_000,
			Mode = SpiMode.Mode3
		} );

		DisplayGpio = _serialBus.ChannelSpiDispl.CreateGpioController();

		TouchSpi = _serialBus.ChannelSpiTouch.CreateSpiDevice( new SpiConnectionSettings( 0 )
		{
			ChipSelectLine = 3,
			ClockFrequency = 2_000_000,
			Mode = SpiMode.Mode3,
			DataBitLength = 8
		} );

		TouchGpio = _serialBus.ChannelSpiTouch.CreateGpioController();
	}

	/// <summary>
	/// Gets the I2C bus used for board peripherals connected to the FTDI I2C channel.
	/// </summary>
	public I2cBus I2cBus { get; }

	/// <summary>
	/// Gets the GPIO controller associated with the I2C channel, used for any additional lines routed with I2C.
	/// </summary>
	public GpioController I2cLinesGpio { get; }

	/// <summary>
	/// Gets the SPI device used to communicate with the display controller.
	/// </summary>
	public SpiDevice DisplaySpi { get; }

	/// <summary>
	/// Gets the GPIO controller associated with the display SPI channel (e.g., D/C, reset, backlight).
	/// </summary>
	public GpioController DisplayGpio { get; }

	/// <summary>
	/// Gets the SPI device used to communicate with the touch controller.
	/// </summary>
	public SpiDevice TouchSpi { get; }

	/// <summary>
	/// Gets the GPIO controller associated with the touch SPI channel (e.g., interrupt/reset lines).
	/// </summary>
	public GpioController TouchGpio { get; }

	// FTDI-Pin-Indexe (Ist-Stand wie zuvor hardcodiert in Board)

	/// <summary>
	/// Gets the FTDI GPIO pin index used as the display Data/Command (D/C) select line.
	/// </summary>
	public int DisplayDataCommandPin => 4;

	/// <summary>
	/// Gets the FTDI GPIO pin index used to reset the display controller.
	/// </summary>
	public int DisplayResetPin => 5;

	/// <summary>
	/// Gets the FTDI GPIO pin index used to control the display backlight enable/PWM line (as wired).
	/// </summary>
	public int DisplayBacklightPin => 6;

	/// <summary>
	/// Gets the FTDI GPIO pin index used for expander interrupt line B (board revision V2).
	/// </summary>
	public int V2ExpanderInterruptBPin => 4;

	/// <summary>
	/// Gets the FTDI GPIO pin index used for expander interrupt line A (board revision V2).
	/// </summary>
	public int V2ExpanderInterruptAPin => 5;

	/// <summary>
	/// Gets the FTDI GPIO pin index used to reset the GPIO expander (board revision V1).
	/// </summary>
	public int V1ExpanderResetPin => 6;

	/// <summary>
	/// Gets the FTDI GPIO pin index used to reset the GPIO expander (board revision V2).
	/// </summary>
	public int V2ExpanderResetPin => 7;
}