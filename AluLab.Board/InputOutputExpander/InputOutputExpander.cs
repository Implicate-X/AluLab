using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Threading;
using Iot.Device.Gpio;
using Iot.Device.Mcp23xxx;

namespace AluLab.Board.InputOutputExpander
{
	/// <summary>
	/// Provides an interface to a 16-channel I/O expander (MCP23017) configured for controlling and monitoring the operand
	/// and control inputs of a 74181 arithmetic logic unit (ALU).
	/// </summary>
	/// <remarks>This class maps the MCP23017's ports to the operand and control inputs of a 74181 ALU, enabling
	/// binary logic emulation or hardware interfacing scenarios. Port A is used for operand inputs (A0–A3 and B0–B3),
	/// while port B is used for select inputs (S0–S3), carry input, and mode control. Use the nested PortA and PortB
	/// classes for bit mask constants corresponding to each input line.</remarks>
	/// <param name="i2cBus">The I2C bus used to communicate with the MCP23017 device.</param>
	/// <param name="controller">An optional GPIO controller for managing reset and interrupt pins. If null, GPIO functionality is disabled.</param>
	/// <param name="reset">The GPIO pin number used for the device reset line, or -1 to disable hardware reset.</param>
	/// <param name="interruptA">The GPIO pin number for the interrupt output from port A, or -1 if not used.</param>
	/// <param name="interruptB">The GPIO pin number for the interrupt output from port B, or -1 if not used.</param>
	/// <param name="shouldDispose">true to dispose the I2C device and GPIO controller when this instance is disposed; otherwise, false.</param>
	public partial class V1SignalOutALU(
			I2cBus i2cBus,
			GpioController? controller = null,
			int reset = -1,
			int interruptA = -1,
			int interruptB = -1,
			bool shouldDispose = true ) 
			: Mcp23017Base( i2cBus.CreateDevice( address ), reset, interruptA, interruptB, controller, shouldDispose )
	{
		/// <summary>The I2C address <see cref="AddressDocs_V1.address"/>.</summary>
		private const int address = 0x21;

		public static int Address => address;

		/// <summary>
		/// Provides constant values representing the operand input bit masks for the A and B inputs of the 74181 arithmetic
		/// logic unit (ALU).
		/// </summary>
		/// <remarks>Use these constants to identify or manipulate individual operand input lines (A0–A3 and B0–B3)
		/// when working with the 74181 ALU or compatible logic. Each constant corresponds to a single bit position for the
		/// respective input.</remarks>
		public class PortA
		{
			/// <summary>
			/// Operand input A0 of the 74181 ALU.
			/// </summary>
			public const byte A0 = 0b00000001;
			/// <summary>
			/// Operand input A1 of the 74181 ALU.
			/// </summary>
			public const byte A1 = 0b00000010;
			/// <summary>
			/// Operand input A2 of the 74181 ALU.
			/// </summary>
			public const byte A2 = 0b00000100;
			/// <summary>
			/// Operand input A3 of the 74181 ALU.
			/// </summary>
			public const byte A3 = 0b00001000;
			/// <summary>
			/// Operand input B0 of the 74181 ALU.
			/// </summary>
			public const byte B0 = 0b00010000;
			/// <summary>
			/// Operand input B1 of the 74181 ALU.
			/// </summary>
			public const byte B1 = 0b00100000;
			/// <summary>
			/// Operand input B2 of the 74181 ALU.
			/// </summary>
			public const byte B2 = 0b01000000;
			/// <summary>
			/// Operand input B3 of the 74181 ALU.
			/// </summary>
			public const byte B3 = 0b10000000;
		}

		/// <summary>
		/// Provides constant values representing the control and select input bit masks for the 74181 arithmetic logic unit
		/// (ALU) port B interface.
		/// </summary>
		/// <remarks>These constants correspond to the bit positions for the S0–S3 select inputs, carry input (CN),
		/// and mode control (M) of the 74181 ALU. They are intended for use when constructing or interpreting control bytes
		/// for interfacing with the ALU in binary logic or emulation scenarios.</remarks>
		public class PortB
		{
			/// <summary>
			/// Select input S0 of the 74181 ALU.
			/// </summary>
			public const byte S0 = 0b00000001;
			/// <summary>
			/// Select input S1 of the 74181 ALU.
			/// </summary>
			public const byte S1 = 0b00000010;
			/// <summary>
			/// Select input S2 of the 74181 ALU.
			/// </summary>
			public const byte S2 = 0b00000100;
			/// <summary>
			/// Select input S3 of the 74181 ALU.
			/// </summary>
			public const byte S3 = 0b00001000;
			/// <summary>
			/// Carry input of the 74181 ALU.
			/// </summary>
			public const byte CN = 0b01000000;
			/// <summary>
			/// Mode control input of the 74181 ALU.
			/// </summary>
			public const byte M = 0b10000000;
		}

		/// <summary>
		/// Initializes the device to a safe default state, configuring all ports and settings as required for startup.
		/// </summary>
		/// <remarks>Call this method before performing any operations that require the device to be in a known, safe
		/// state. This method should typically be invoked once during application startup or device reset. Subsequent calls
		/// will reapply the default configuration.</remarks>
		public override void Initialize()
		{
			EnableSafe();

			SetDir( Port.PortA, 0b00000000 );
			SetDir( Port.PortB, 0b00000000 );

			SetPort( Port.PortA );
			ResetPort( Port.PortB );
		}
	}

	/// <summary>
	/// Represents a specialized MCP23017-based I/O expander for interfacing with the control and output lines of a 74181
	/// arithmetic logic unit (ALU) and related signals. Provides access to input port A for control lines and input port B
	/// for function outputs, comparator output, and carry state outputs.
	/// </summary>
	/// <remarks>This class is intended for use in hardware control scenarios involving the 74181 ALU or similar
	/// devices. It exposes constants and methods for interacting with specific ALU signals via the MCP23017 expander.
	/// Thread safety is not guaranteed; callers should ensure appropriate synchronization if accessing from multiple
	/// threads.</remarks>
	/// <param name="i2cBus">The I2C bus used to communicate with the MCP23017 device. Must not be null.</param>
	/// <param name="controller">An optional GPIO controller used for managing reset and interrupt pins. If null, GPIO functionality for these pins
	/// is disabled.</param>
	/// <param name="reset">The GPIO pin number used to reset the device. Specify -1 to disable hardware reset.</param>
	/// <param name="interruptA">The GPIO pin number connected to the interrupt output for port A. Specify -1 if not used.</param>
	/// <param name="interruptB">The GPIO pin number connected to the interrupt output for port B. Specify -1 if not used.</param>
	/// <param name="shouldDispose">true to dispose the I2C device and GPIO controller when this instance is disposed; otherwise, false.</param>
	public partial class V2SignalInpALU(
			I2cBus i2cBus,
			GpioController? controller = null,
			int reset = -1,
			int interruptA = -1,
			int interruptB = -1,
			bool shouldDispose = true ) 
			: Mcp23017Base( i2cBus.CreateDevice( address ), reset, interruptA, interruptB, controller, shouldDispose )
	{
		private const int address = 0x23;
		public static int Address => address;

		/// <summary>
		/// Provides constants for interacting with Port A hardware registers.
		/// </summary>
		/// <remarks>This class defines bitmask values used to identify specific signals or flags on Port A. It is
		/// typically used in low-level hardware access scenarios, such as embedded systems or device drivers.</remarks>
		public class PortA
		{
			public const byte T_IRQ = 0b10000000;
		}

		/// <summary>
		/// Defines bitmask constants for the output signals of the 74181 arithmetic logic unit (ALU) port B.
		/// </summary>
		/// <remarks>These constants represent individual output lines from the 74181 ALU, such as function outputs
		/// (F0–F3), carry propagate and generate signals, comparator output, and carry out. They can be used to identify or
		/// manipulate specific output bits when interfacing with the ALU in emulation or hardware control
		/// scenarios.</remarks>
		public class PortB
		{
			/// <summary>
			/// Function output F0 of the 74181 ALU.
			/// </summary>
			public const byte F0 = 0b00000001;
			/// <summary>
			/// Function output F1 of the 74181 ALU.
			/// </summary>
			public const byte F1 = 0b00000010;
			/// <summary>
			/// Function output F2 of the 74181 ALU.
			/// </summary>
			public const byte F2 = 0b00000100;
			/// <summary>
			/// Function output F3 of the 74181 ALU.
			/// </summary>
			public const byte F3 = 0b00001000;
			/// <summary>
			/// Carry propagate output of the 74181 ALU.
			/// </summary>
			public const byte P = 0b00010000;
			/// <summary>
			/// Carry generate output of the 74181 ALU.
			/// </summary>
			public const byte G = 0b00100000;
			/// <summary>
			/// Comparator output of the 74181 ALU.
			/// </summary>
			public const byte A_EQ_B = 0b01000000;
			/// <summary>
			/// Carry output of the 74181 ALU.
			/// </summary>
			public const byte CN_4 = 0b10000000;
		}

		/// <summary>
		/// Initializes the device by configuring ports and enabling safe operation mode.
		/// </summary>
		/// <remarks>Call this method before performing any operations that require the device ports to be set up.
		/// This method prepares the device for use by setting port directions and resetting port states. Repeated calls will
		/// reinitialize the ports to their default state.</remarks>
		public override void Initialize()
		{
			EnableSafe();

			SetDir( Port.PortA, 0b11111111 );
			SetDir( Port.PortB, 0b11111111 );

			ResetPort( Port.PortA );
			ResetPort( Port.PortB );
		}
	}
}
