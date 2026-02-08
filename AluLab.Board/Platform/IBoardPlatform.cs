using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;

namespace AluLab.Board.Platform;

/// <summary>
/// Abstraction of platform-specific hardware creation for the board.
/// </summary>
/// <remarks>
/// This interface encapsulates the creation of the I2C/SPI/GPIO accesses required for the board,
/// so that different host environments (e.g., Workbench/Gateway) can provide their respective platform implementations.
/// <para> Implementations should consistently manage the lifetime (open/close/dispose) of the resources created. 
/// Callers should not rely on multiple calls returning the same instance unless this is guaranteed by the concrete implementation. </para>
/// </remarks>
public interface IBoardPlatform : IDisposable
{
	/// <summary>
	/// Creates an <see cref="I2cBus"/> for communication with the board's I2C peripherals.
	/// </summary>
	/// <returns>An initialized <see cref="I2cBus"/>.</returns>
	I2cBus CreateI2cBus();

	/// <summary>
	/// Creates a <see cref="GpioController"/> for GPIO lines provided via an I2C GPIO expander.
	/// </summary>
	/// <returns>A <see cref="GpioController"/> that maps the expander lines.</returns>
	GpioController CreateGpioControllerForI2cExpanderLines();

	/// <summary>
	/// Creates a <see cref="SpiDevice"/> for the display using the specified connection settings.
	/// </summary>
	/// <param name="settings">SPI connection settings for display communication.</param>
	/// <returns>An initialized <see cref="SpiDevice"/> for the display. </returns>
	SpiDevice CreateDisplaySpiDevice( SpiConnectionSettings settings );

	/// <summary>
	/// Creates a <see cref="GpioController"/> for display-specific GPIO signals
	/// (e.g., reset, data/command, backlight), depending on the platform.
	/// </summary>
	/// <returns>A <see cref="GpioController"/> for display GPIO.</returns>
	GpioController CreateDisplayGpioController();

	/// <summary>
	/// Creates a <see cref="SpiDevice"/> for the touch controller using the specified connection settings.
	/// </summary>
	/// <param name="settings">SPI connection settings for touch communication.</param>
	/// <returns>An initialized <see cref="SpiDevice"/> for the touch controller.</returns>
	SpiDevice CreateTouchSpiDevice( SpiConnectionSettings settings );

	/// <summary>
	/// Creates a <see cref="GpioController"/> for touch-specific GPIO signals
	/// (e.g., interrupt/IRQ, reset), depending on the platform.
	/// </summary>
	/// <returns>A <see cref="GpioController"/> for touch GPIO. </returns>
	GpioController CreateTouchGpioController();
}