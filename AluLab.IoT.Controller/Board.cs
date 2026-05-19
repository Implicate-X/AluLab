using System;
using System.Diagnostics;
using System.Collections;
using ImplicateX.Display;
using GHIElectronics.TinyCLR.Pins;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Devices.I2c;
using GHIElectronics.TinyCLR.Drivers.BasicGraphics;
using GHIElectronics.TinyCLR.Drivers.Encoder;

namespace AluLab.IoT.Controller
{
	internal class Board
	{
		private GpioController gpioController;
		private I2cController i2cController;
		private SD1306 displayController;
		private byte hostIndex = 1;
		private const int SCREEN_WIDTH = 128;
		private const int SCREEN_HEIGHT = 32;
		private const uint COLOR_WHITE = 0x00ffffffU;
		private string[] GATEWAY_NAME = { "Desktop", "GHI Domino", "Raspberry" };
		private BasicGraphics graphics;
		private Hashtable keyTable;

		public void Initialize()
		{
			i2cController = I2cController.FromName( SC13048.I2cBus.I2c1 );

			displayController =
				new SD1306( i2cController.GetDevice( SD1306.GetConnectionSettings() ) );

			graphics = new BasicGraphics( 128, 32, ColorFormat.OneBpp );

			DrawInfo();
		}

		private void DrawInfo()
		{
			graphics.Clear();
			graphics.DrawString( GATEWAY_NAME[ hostIndex ], COLOR_WHITE, 0, 12, 2, 2 );
			//graphics.DrawRectangle( COLOR_WHITE, SCREEN_WIDTH - 4, ( 4 - hostIndex ) * 4, 4, 4 );

			displayController.DrawBufferNative( graphics.Buffer );

			var gpioController = GpioController.GetDefault();

			var pinA = gpioController.OpenPin( SC13048.GpioPin.PA0 );
			var pinB = gpioController.OpenPin( SC13048.GpioPin.PA1 );

			var encoder = new EncoderController( pinA, pinB );

			encoder.OnCounterChangedEvent += static ( counter ) =>
			{
				Debug.WriteLine( "Counter = " + counter );
			};

		}

	}
}
