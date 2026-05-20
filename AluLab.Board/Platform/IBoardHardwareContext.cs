using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;

namespace AluLab.Board.Platform;

/// <summary>
/// Provides access to the board's hardware resources (I2C/SPI/GPIO) provided by host projects (e.g., Workbench/Gateway).
/// </summary>
/// <remarks>
/// <para>
/// This interface does not create or manage any hardware resources and, in particular, does not
/// <see cref="System.IDisposable.Dispose"/> any of these resources. The lifetime (creation,
/// configuration, release) remains entirely with the host; <c>AluLab.Board</c> only consumes the dependencies via DI.
/// </para>
/// <para>
/// Pin assignments are host-specific and must be provided by the host implementation (Workbench: FTDI pin indices,
/// Gateway: Raspberry Pi BCM pins).
/// </para>
/// </remarks>
public interface IBoardHardwareContext
{
	/// <summary>
	/// Shared I2C bus for I2C peripherals connected to the board.
	/// </summary>
	I2cBus I2cBus { get; }

	/// <summary>
	/// GPIO controller for I2C-related lines (expander reset/interrupt lines).
	/// </summary>
	GpioController I2cLinesGpio { get; }

	/// <summary>
	/// SPI device for display communication (data/command transfers).
	/// </summary>
	SpiDevice DisplaySpi { get; }

	/// <summary>
	/// GPIO controller for display-related signals (D/C, reset, backlight).
	/// </summary>
	GpioController DisplayGpio { get; }

	/// <summary>
	/// SPI device for touch controller communication.
	/// </summary>
	SpiDevice TouchSpi { get; }

	/// <summary>
	/// GPIO controller for touch-related signals (currently optional).
	/// </summary>
	GpioController TouchGpio { get; }

	// -------- Pin assignments (host-specific) --------

	/// <summary>GPIO pin used for display D/C (data/command).</summary>
	int DisplayDataCommandPin { get; }

	/// <summary>GPIO pin used for display reset.</summary>
	int DisplayResetPin { get; }

	/// <summary>GPIO pin used for display backlight/LED.</summary>
	int DisplayBacklightPin { get; }

	/// <summary>GPIO pin used for resetting the V1 I/O expander.</summary>
	int V1ExpanderResetPin { get; }

	/// <summary>GPIO pin used for resetting the V2 I/O expander.</summary>
	int V2ExpanderResetPin { get; }

	/// <summary>GPIO pin connected to V2 expander INTA.</summary>
	int V2ExpanderInterruptAPin { get; }

	/// <summary>GPIO pin connected to V2 expander INTB.</summary>
	int V2ExpanderInterruptBPin { get; }
}