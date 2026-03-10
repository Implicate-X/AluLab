using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;
using AluLab.Board.Platform;

namespace AluLab.Gateway.Hardware;

public sealed class HardwareContext : IBoardHardwareContext
{
	// Raspberry Pi: I2C1, SPI0 (CE0), SPI1 (CE2)
	private const int I2cBusId = 1;

	private const int DisplaySpiBusId = 0;
	private const int DisplaySpiChipSelect = 0;

	private const int TouchSpiBusId = 1;
	private const int TouchSpiChipSelect = 2;

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

	public I2cBus I2cBus { get; }
	public GpioController I2cLinesGpio { get; }
	public SpiDevice DisplaySpi { get; }
	public GpioController DisplayGpio { get; }
	public SpiDevice TouchSpi { get; }
	public GpioController TouchGpio { get; }

	// Raspberry Pi BCM Pins (gemäß remarks in Gateway-HardwareContext)
	public int DisplayBacklightPin => 22;     // LED
	public int DisplayDataCommandPin => 23;   // DC/RS
	public int DisplayResetPin => 24;         // RESET

	public int V1ExpanderResetPin => 5;       // RESET V1
	public int V2ExpanderInterruptAPin => 6;  // INTA V2
	public int V2ExpanderInterruptBPin => 12; // INTB V2
	public int V2ExpanderResetPin => 13;      // RESET V2
}