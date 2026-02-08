using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;

namespace AluLab.Board.Platform;

/// <summary>
/// Provides access to the board's hardware resources (I2C/SPI/GPIO) provided by host projects (e.g., Workbench/Gateway).
/// </summary>
/// <remarks>
/// <para> This interface does not create or manage any hardware resources and, in particular, does not
/// This interface does not create or manage any hardware resources and, in particular, does not
/// <see cref="System.IDisposable.Dispose"/> any of these resources. The lifetime (creation,
/// configuration, release) remains entirely with the host; <c>AluLab.Board</c> only consumes the dependencies via DI. </para>
/// <para> The individual controllers/devices are typically preconfigured 
/// so that they are assigned to the respective bus/GPIO lines for display and touch. </para>
/// </remarks>
public interface IBoardHardwareContext
{
	/// <summary>
	/// Shared I2C bus for I2C peripherals connected to the board.
	/// </summary>
	I2cBus I2cBus { get; }

	/// <summary>
	/// GPIO controller for I2C-related lines (e.g., reset/power/enable or multiplexing),
	/// unless these are mapped exclusively via the <see cref="I2cBus"/>.
	/// </summary>
	GpioController I2cLinesGpio { get; }

	/// <summary>
	/// SPI device for display communication (data/command transfers).
	/// </summary>
	SpiDevice DisplaySpi { get; }

	/// <summary>
	/// GPIO controller for display-related signals (e.g., D/C, reset, backlight, chip select),
	/// unless covered by <see cref="DisplaySpi"/>.
	/// </summary>
	GpioController DisplayGpio { get; }

	/// <summary>
	/// SPI device for touch controller communication.
	/// </summary>
	SpiDevice TouchSpi { get; }

	/// <summary>
	/// GPIO controller for touch-related signals (e.g., IRQ, reset, chip select),
	/// unless covered by <see cref="TouchSpi"/>.
	/// </summary>
	GpioController TouchGpio { get; }
}