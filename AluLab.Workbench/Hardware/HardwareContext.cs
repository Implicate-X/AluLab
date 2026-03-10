using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;
using AluLab.Board.Platform;

namespace AluLab.Workbench.Hardware;

public sealed class HardwareContext : IBoardHardwareContext
{
	private readonly SerialBus _serialBus = new();

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

	public I2cBus I2cBus { get; }
	public GpioController I2cLinesGpio { get; }
	public SpiDevice DisplaySpi { get; }
	public GpioController DisplayGpio { get; }
	public SpiDevice TouchSpi { get; }
	public GpioController TouchGpio { get; }

	// FTDI-Pin-Indexe (Ist-Stand wie zuvor hardcodiert in Board)
	public int DisplayDataCommandPin => 4;
	public int DisplayResetPin => 5;
	public int DisplayBacklightPin => 6;

	public int V2ExpanderInterruptBPin => 4;
	public int V2ExpanderInterruptAPin => 5;
	public int V1ExpanderResetPin => 6;
	public int V2ExpanderResetPin => 7;
}