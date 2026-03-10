using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;
using AluLab.Board.Platform;
using Iot.Device.Board;

namespace AluLab.Gateway.Hardware;

/// <summary>
/// Provides access to the hardware communication interfaces required for operating devices on the gateway, including
/// I2C and SPI buses and their associated GPIO controllers.
/// </summary>
/// <remarks>
/// I²C (V1 + V2)
/// 	GPIO 2 (SDA1)
/// 	GPIO 3 (SCL1)
///
/// SPI0 (DISPLAY)
/// 	GPIO 8 – CE0
/// 	GPIO 9 – MISO
/// 	GPIO 10 – MOSI
/// 	GPIO 11 – SCLK
///
/// GPIO (DISPLAY)
///		GPIO 22 – LED
///		GPIO 23 – DC/RS
///		GPIO 24 – RESET
///
/// SPI1 (TOUCH)
/// 	GPIO 16 – CE2
/// 	GPIO 19 – MISO
/// 	GPIO 20 – MOSI
/// 	GPIO 21 – SCLK
///
/// GPIO (V1 + V2)
///		GPIO 5 – RESET V1
///		GPIO 6 – INTA V2
///		GPIO 12 – INTB V2
///		GPIO 13 – RESET V2 
/// </remarks>
public sealed class HardwareContext : IBoardHardwareContext
{
	RaspberryPiBoard _board;

	/// <summary>
	/// Initializes a new instance of the Gateway.HardwareContext class and sets up the hardware communication interfaces
	/// required for device operation.
	/// </summary>
	/// <remarks>This constructor configures I2C and SPI buses, as well as associated GPIO controllers, for display
	/// and touch hardware. All hardware interfaces are initialized and ready for use after construction.
	/// </remarks>
	public HardwareContext()
	{
		_board = new RaspberryPiBoard();

		_board.ConfigurationFile = "boardconfig.json";
		//if( !_serialBus.Initialize() )
		//	throw new InvalidOperationException( "FTDI SerialBus initialization failed." );

		//var i2c = _serialBus.ChannelI2cInOut.CreateOrGetI2cBus( _serialBus.ChannelI2cInOut.GetDefaultI2cBusNumber() );

		//I2cBus = i2c;
		//I2cLinesGpio = _serialBus.ChannelI2cInOut.CreateGpioController();

		//DisplaySpi = _serialBus.ChannelSpiDispl.CreateSpiDevice( new SpiConnectionSettings( 0 )
		//{
		//	ChipSelectLine = 3,
		//	ClockFrequency = 24_000_000,
		//	Mode = SpiMode.Mode3
		//} );

		//DisplayGpio = _serialBus.ChannelSpiDispl.CreateGpioController();

		//TouchSpi = _serialBus.ChannelSpiTouch.CreateSpiDevice( new SpiConnectionSettings( 0 )
		//{
		//	ChipSelectLine = 3,
		//	ClockFrequency = 2_000_000,
		//	Mode = SpiMode.Mode3,
		//	DataBitLength = 8
		//} );

		//TouchGpio = _serialBus.ChannelSpiTouch.CreateGpioController();
	}

	/// <summary>
	/// Gets the I2C bus interface used to communicate with connected I2C devices.
	/// </summary>
	/// <remarks>Use this property to access the I2C bus for data transfer operations. Ensure that the bus is
	/// properly initialized before performing any communication. The returned interface provides methods for interacting
	/// with devices on the I2C bus.</remarks>
	public I2cBus I2cBus { get; }
	
	/// <summary>
	/// Gets the GPIO controller used to manage the I2C lines.
	/// </summary>
	/// <remarks>Use this property to access the underlying GPIO controller responsible for configuring and
	/// controlling the I2C communication lines. Ensure that the controller is properly initialized before performing I2C
	/// operations.</remarks>
	public GpioController I2cLinesGpio { get; }
	
	/// <summary>
	/// Gets the SPI device used for display operations.
	/// </summary>
	/// <remarks>This property provides access to the underlying SPI device that is responsible for handling display
	/// communications. Ensure that the device is properly initialized before use.</remarks>
	public SpiDevice DisplaySpi { get; }
	
	/// <summary>
	/// Gets the GPIO controller used for display operations.
	/// </summary>
	/// <remarks>This property provides access to the GPIO controller that manages the display's GPIO pins, allowing
	/// for configuration and control of display-related functionalities.</remarks>
	public GpioController DisplayGpio { get; }
	
	/// <summary>
	/// Gets the SPI device used for communication with the touch sensor.
	/// </summary>
	/// <remarks>Access this property to interact directly with the underlying SPI device responsible for touch
	/// input operations. Ensure that the device is properly initialized before use.</remarks>
	public SpiDevice TouchSpi { get; }
	
	/// <summary>
	/// Gets the GPIO controller used for touch input operations.
	/// </summary>
	/// <remarks>This property provides access to the underlying GPIO controller, which is essential for managing
	/// touch input devices. Ensure that the GPIO controller is properly initialized before use.</remarks>
	public GpioController TouchGpio { get; }
}