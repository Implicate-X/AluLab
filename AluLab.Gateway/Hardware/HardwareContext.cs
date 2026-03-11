using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;
using AluLab.Board.Platform;

namespace AluLab.Gateway.Hardware;

/// <summary>
/// Raspberry Pi specific hardware context for the Gateway application.
/// </summary>
/// <remarks>
/// This type centralizes the creation and configuration of bus devices (I2C/SPI) and GPIO controllers
/// as required by <see cref="IBoardHardwareContext"/>.
/// <para>
/// Bus and pin mappings in this implementation are for Raspberry Pi using BCM pin numbering.
/// </para>
/// <para>
/// SPI usage:
/// <list type="bullet">
/// <item><description>Display: SPI0, CE0</description></item>
/// <item><description>Touch: SPI1, CE2</description></item>
/// </list>
/// </para>
/// <para>
/// Resource lifetime:
/// The created <see cref="I2cBus"/>, <see cref="SpiDevice"/>, and <see cref="GpioController"/> instances are long-lived.
/// Consumers are expected to manage disposal if/when the owning component shuts down.
/// </para>
/// </remarks>
public sealed class HardwareContext : IBoardHardwareContext
{
	// Raspberry Pi: I2C1, SPI0 (CE0), SPI1 (CE2)
	/// <summary>
	/// Raspberry Pi I2C bus ID used by the Gateway hardware (I2C1).
	/// </summary>
	private const int I2cBusId = 1;

	/// <summary>
	/// SPI bus ID for the display panel (SPI0).
	/// </summary>
	private const int DisplaySpiBusId = 0;

	/// <summary>
	/// Chip select line for the display panel on <see cref="DisplaySpiBusId"/> (CE0).
	/// </summary>
	private const int DisplaySpiChipSelect = 0;

	/// <summary>
	/// SPI bus ID for the touch controller (SPI1).
	/// </summary>
	private const int TouchSpiBusId = 1;

	/// <summary>
	/// Chip select line for the touch controller on <see cref="TouchSpiBusId"/> (CE2).
	/// </summary>
	private const int TouchSpiChipSelect = 2;

	/// <summary>
	/// Initializes all buses and GPIO controllers required by the board.
	/// </summary>
	/// <remarks>
	/// Creates:
	/// <list type="bullet">
	/// <item><description><see cref="I2cBus"/> on <c>I2C1</c></description></item>
	/// <item><description><see cref="DisplaySpi"/> on <c>SPI0/CE0</c> (24 MHz, Mode3)</description></item>
	/// <item><description><see cref="TouchSpi"/> on <c>SPI1/CE2</c> (2 MHz, Mode3, 8-bit)</description></item>
	/// <item><description>Dedicated <see cref="GpioController"/> instances for display, I2C lines, and touch</description></item>
	/// </list>
	/// </remarks>
	public HardwareContext()
	{
		I2cBus = global::System.Device.I2c.I2cBus.Create( I2cBusId );

		DisplaySpi = SpiDevice.Create( new SpiConnectionSettings( DisplaySpiBusId, DisplaySpiChipSelect )
		{
			ClockFrequency = 24_000_000,
			Mode = SpiMode.Mode3
		} );

		TouchSpi = SpiDevice.Create( new SpiConnectionSettings( TouchSpiBusId, TouchSpiChipSelect )
		{
			ClockFrequency = 2_000_000,
			Mode = SpiMode.Mode3,
			DataBitLength = 8
		} );

		DisplayGpio = new GpioController();
		I2cLinesGpio = new GpioController();
		TouchGpio = new GpioController();
	}

	/// <summary>
	/// Shared I2C bus instance used to communicate with I2C peripherals (e.g. expanders/sensors).
	/// </summary>
	public I2cBus I2cBus { get; }

	/// <summary>
	/// GPIO controller reserved for bit-banged / auxiliary I2C-related lines (if needed by the platform).
	/// </summary>
	public GpioController I2cLinesGpio { get; }

	/// <summary>
	/// SPI device configured for the display interface.
	/// </summary>
	public SpiDevice DisplaySpi { get; }

	/// <summary>
	/// GPIO controller reserved for display-specific control pins (backlight, reset, D/C).
	/// </summary>
	public GpioController DisplayGpio { get; }

	/// <summary>
	/// SPI device configured for the touch controller interface.
	/// </summary>
	public SpiDevice TouchSpi { get; }

	/// <summary>
	/// GPIO controller reserved for touch-specific control/interrupt pins.
	/// </summary>
	public GpioController TouchGpio { get; }

	// Raspberry Pi BCM Pins (gemäß remarks in Gateway-HardwareContext)
	/// <summary>
	/// BCM pin used to control the display backlight (LED).
	/// </summary>
	public int DisplayBacklightPin => 22;     // LED

	/// <summary>
	/// BCM pin used as display Data/Command (DC/RS) selector.
	/// </summary>
	public int DisplayDataCommandPin => 23;   // DC/RS

	/// <summary>
	/// BCM pin used to reset the display controller.
	/// </summary>
	public int DisplayResetPin => 24;         // RESET

	/// <summary>
	/// BCM pin used to reset the V1 I/O expander.
	/// </summary>
	public int V1ExpanderResetPin => 5;       // RESET V1

	/// <summary>
	/// BCM pin connected to the V2 expander interrupt A line (INTA).
	/// </summary>
	public int V2ExpanderInterruptAPin => 6;  // INTA V2

	/// <summary>
	/// BCM pin connected to the V2 expander interrupt B line (INTB).
	/// </summary>
	public int V2ExpanderInterruptBPin => 12; // INTB V2

	/// <summary>
	/// BCM pin used to reset the V2 I/O expander.
	/// </summary>
	public int V2ExpanderResetPin => 13;      // RESET V2
}