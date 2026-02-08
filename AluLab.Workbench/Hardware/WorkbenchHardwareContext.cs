using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;
using AluLab.Board.Platform;

namespace AluLab.Workbench.Hardware;

/// <summary>
/// Provides access to the hardware communication interfaces required for operating devices on the workbench, including
/// I2C and SPI buses and their associated GPIO controllers.
/// </summary>
/// <remarks>This context initializes and exposes the hardware interfaces necessary for display and touch device
/// communication. All interfaces are ready for use after construction. If the required hardware is not connected or the
/// FTDI SerialBus cannot be initialized, an exception is thrown during instantiation.</remarks>
public sealed class WorkbenchHardwareContext : IBoardHardwareContext
{
	private readonly SerialBus _serialBus = new();

	/// <summary>
	/// Initializes a new instance of the WorkbenchHardwareContext class and sets up the hardware communication interfaces
	/// required for device operation.
	/// </summary>
	/// <remarks>This constructor configures I2C and SPI buses, as well as associated GPIO controllers, for display
	/// and touch hardware. All hardware interfaces are initialized and ready for use after construction. If the hardware is
	/// not connected or the FTDI SerialBus cannot be initialized, construction will fail with an exception.</remarks>
	/// <exception cref="InvalidOperationException">Thrown if the underlying FTDI SerialBus fails to initialize.</exception>
	public WorkbenchHardwareContext()
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

	public I2cBus I2cBus { get; }
	public GpioController I2cLinesGpio { get; }
	public SpiDevice DisplaySpi { get; }
	public GpioController DisplayGpio { get; }
	public SpiDevice TouchSpi { get; }
	public GpioController TouchGpio { get; }
}