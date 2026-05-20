using GHIElectronics.TinyCLR.Pins;

namespace AluLab.Gateway.Controller
{
	internal partial class Board
	{
		/// <summary>
		/// GPIO pin mapping for the SC13048 (GHI TinyCLR) used by this board.
		/// </summary>
		/// <remarks>
		/// The nested types group related pins by function (encoder inputs, routing selection, bus enables).
		/// Many groups use a consistent A/B/C convention to refer to the three supported gateway targets:
		/// A = Desktop PC, B = GHI Endpoint Domino, C = Raspberry Pi 4B.
		/// </remarks>
		private class EncoderPin
		{
			/// <summary>Encoder input channel A.</summary>
			public const int A = SC13048.GpioPin.PA0;

			/// <summary>Encoder input channel B.</summary>
			public const int B = SC13048.GpioPin.PA1;

			/// <summary>Encoder input channel C.</summary>
			public const int C = SC13048.GpioPin.PA2;
		}

		/// <summary>
		/// GPIO pins used to select which gateway path (A/B/C) is active.
		/// </summary>
		private class SelectWayPin
		{
			/// <summary>Gateway Desktop PC selection.</summary>
			public const int A = SC13048.GpioPin.PB3;

			/// <summary>Gateway GHI Endpoint Domino selection.</summary>
			public const int B = SC13048.GpioPin.PB4;

			/// <summary>Gateway Raspberry Pi 4B selection.</summary>
			public const int C = SC13048.GpioPin.PB5;
		}

		/// <summary>
		/// GPIO pins that enable the I2C bus buffer (PCA9518) for each gateway target.
		/// </summary>
		private class I2cEnablePin
		{
			/// <summary>Gateway Desktop PC<br/>PCA9518 EN1.</summary>
			public const int A = SC13048.GpioPin.PA3;

			/// <summary>Gateway GHI Endpoint Domino<br/>PCA9518 EN2.</summary>
			public const int B = SC13048.GpioPin.PA4;

			/// <summary>Gateway Raspberry Pi 4B<br/>PCA9518 EN3.</summary>
			public const int C = SC13048.GpioPin.PA5;
		}

		/// <summary>
		/// GPIO pins that enable the peripheral bus for each gateway target.
		/// </summary>
		/// <remarks>
		/// Used for shared peripherals such as SPI Display and SPI Touch (GPIO V1+V2).
		/// </remarks>
		private class BusEnablePin
		{
			/// <summary>Gateway Desktop PC<br/>SPI Display, SPI Touch GPIO V1+V2.</summary>
			public const int A = SC13048.GpioPin.PB13;

			/// <summary>Gateway GHI Endpoint Domino<br/>SPI Display, SPI Touch GPIO V1+V2.</summary>
			public const int B = SC13048.GpioPin.PB14;

			/// <summary>Gateway Raspberry Pi 4B<br/>SPI Display, SPI Touch GPIO V1+V2.</summary>
			public const int C = SC13048.GpioPin.PB15;
		}
	}
}
