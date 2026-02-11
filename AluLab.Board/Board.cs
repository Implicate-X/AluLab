using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;
using Iot.Device.Mcp23xxx;
using AluLab.Board.InputOutputExpander;
using AluLab.Board.Alu;
using AluLab.Board.Display;
using AluLab.Board.Touch;
using AluLab.Board.Platform;

namespace AluLab.Board
{
	/// <summary>
	/// Central interface for accessing the hardware components of an AluLab board.
	/// </summary>
	/// <remarks>
	/// <para> The class encapsulates the initialization (one-time, thread-safe) and then provides type-safe
	/// access properties to the subsystems used (e.g., I2C, ALU, display, touch). </para>
	/// <para> Hardware resources (I2C/SPI/GPIO) are <b>not</b> created, managed, or disposed of by this class. 
	/// They are obtained exclusively via the injected <see cref="IBoardHardwareContext"/>. </para>
	/// <para> Initialization is performed via <see cref="Initialize"/>. 
	/// In case of errors, <see cref="LastInitializationException"/> is set and <see cref="Initialize"/> returns <see langword="false"/>. 
	/// Publicly exposed components should only be accessed after successful initialization; otherwise, <see cref="EnsureInitialized"/>
	/// throws an <see cref="InvalidOperationException"/>. </para>
	/// </remarks>
	/// <param name="hw"> Hardware context provided by the host (e.g., Workbench/Gateway) that provides buses/controllers.
	/// Must not be <see langword="null"/>. </param>
	public class Board( IBoardHardwareContext hw )
	{
		/// <summary>
		/// Hardware context provided by the host (e.g., Workbench/Gateway).
		/// </summary>
		/// <remarks>
		/// <para> This class does not own/create/manage any hardware resources itself. 
		/// Instead, I2C/SPI buses and GPIO controllers are injected via <see cref="IBoardHardwareContext"/> 
		/// and used exclusively via this field. </para>
		/// <para> The context must not be <see langword="null"/>; otherwise, an <see cref="ArgumentNullException"/> 
		/// is thrown when creating <see cref="Board"/>.</para>
		/// </remarks>
		private readonly IBoardHardwareContext _hw = hw ?? throw new ArgumentNullException( nameof( hw ) );

		private const int PIN_CS = 3;
		private const int PIN_DC = 4;
		private const int PIN_RESET = 5;
		private const int PIN_LED = 6;

		private const int PIN_V2_INTB = 4; // AD4
		private const int PIN_V2_INTA = 5; // AD5 (Touch)
		private const int PIN_V1_RESET = 6; // AD6
		private const int PIN_V2_RESET = 7; // AD7

		private const int TouchSpiClockFrequency = 2_000_000;
		private const int DisplaySpiClockFrequency = 24_000_000;
		private const SpiMode TouchSpiMode = SpiMode.Mode3;
		private const SpiMode DisplaySpiMode = SpiMode.Mode3;

		private I2cBus? _i2cBus;
		private V1SignalOutALU? _v1SignalOutALU;
		private V2SignalInpALU? _v2SignalInpALU;
		private AluController? _aluController;
		private St7796s? _display;
		private XPT2046Touch? touchController;

		private protected InputService _touchInputService = new();

		private volatile bool _isInitialized;
		private readonly object _initLock = new();

		/// <summary>
		/// Last exception that occurred during <see cref="Initialize"/>.
		/// </summary>
		/// <remarks>
		/// Set to <see langword="null"/> before a new initialization attempt.
		/// If an error occurs, it contains an <see cref="InvalidOperationException"/> with a meaningful
		/// error message (with inner exception, if applicable).
		/// </remarks>
		public Exception? LastInitializationException { get; private set; }

		/// <summary>
		/// Indicates whether the board instance was successfully initialized.
		/// </summary>
		public bool IsInitialized => _isInitialized;


		/// <summary>
		/// Provides the I2C bus used.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// If the board is not initialized (see <see cref="EnsureInitialized"/>).
		/// </exception>
		public I2cBus I2cBus { get { EnsureInitialized(); return _i2cBus!; } private set => _i2cBus = value; }

		/// <summary>
		/// I/O expander (V1) for the ALU output signals.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// If the board is not initialized (see <see cref="EnsureInitialized"/>).
		/// </exception>
		public V1SignalOutALU V1SignalOutALU { get { EnsureInitialized(); return _v1SignalOutALU!; } private set => _v1SignalOutALU = value; }

		/// <summary>
		/// I/O expander (V2) for the ALU input signals (including touch IRQ).
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// If the board is not initialized (see <see cref="EnsureInitialized"/>).
		/// </exception>
		public V2SignalInpALU V2SignalInpALU { get { EnsureInitialized(); return _v2SignalInpALU!; } private set => _v2SignalInpALU = value; }

		/// <summary>
		/// Controller for the ALU, built on the initialized expanders.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// If the board is not initialized (see <see cref="EnsureInitialized"/>).
		/// </exception>
		public AluController AluController { get { EnsureInitialized(); return _aluController!; } private set => _aluController = value; }

		/// <summary>
		/// Display controller (ST7796S) of the board.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// If the board is not initialized (see <see cref="EnsureInitialized"/>).
		/// </exception>
		public St7796s Display { get { EnsureInitialized(); return _display!; } private set => _display = value; }

		/// <summary>
		/// Touch controller (XPT2046) of the board.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// If the board is not initialized (see <see cref="EnsureInitialized"/>).
		/// </exception>
		public XPT2046Touch TouchController { get { EnsureInitialized(); return touchController!; } private set => touchController = value; }

		/// <summary>
		/// Service for processing/forwarding touch inputs.
		/// </summary>
		/// <remarks>
		/// Created internally and available after successful initialization.
		/// </remarks>
		/// <exception cref="InvalidOperationException">
		/// If the board is not initialized (see <see cref="EnsureInitialized"/>).
		/// </exception>
		public InputService TouchInputService { get { EnsureInitialized(); return _touchInputService; } }

		/// <summary>
		/// Initializes all expected subsystems of the board exactly once (thread-safe).
		/// </summary>
		/// <remarks>
		/// <para> Initialization is idempotent: If <see cref="IsInitialized"/> is already <see langword="true"/>,
		/// <see langword="true"/> is returned immediately. </para>
		/// <para> Procedure (with error handling per step):
		/// <list type="number">
		/// <item><description>Obtain I2C bus from <see cref="IBoardHardwareContext"/></description></item>
		/// <item><description>Check for presence of expected I2C devices (<see cref="FindI2cDevices"/>)</description></item>
		/// <item><description>Initialize input/output expanders (<see cref="InitInputOutputExpanders"/>)</description></item>
		/// <item><description>Create ALU controller (<see cref="InitAluController"/>)</description></item>
		/// <item><description>Initialize display and touch (<see cref="InitializeDisplayAndTouch"/>)</description></item>
		/// </list></para>
		/// <para> In case of error, <see cref="LastInitializationException"/> is set, <see cref="IsInitialized"/> remains
		/// <see langword="false"/> and the method returns <see langword="false"/>. </para>
		/// </remarks>
		/// <returns><see langword="true"/> if initialization is successful; otherwise <see langword="false"/>.</returns>
		public bool Initialize()
		{
			if( _isInitialized )
				return true;

			lock( _initLock )
			{
				if( _isInitialized )
					return true;

				LastInitializationException = null;

				I2cBus i2c;
				try
				{
					i2c = _hw.I2cBus;
					I2cBus = i2c;
				}
				catch( Exception ex )
				{
					LastInitializationException = new InvalidOperationException( "I2C bus is not available from host context.", ex );
					_isInitialized = false;
					return false;
				}

				try
				{
					if( !InitInputOutputExpanders( i2c ) )
					{
						LastInitializationException = new InvalidOperationException( "InitInputOutputExpanders() failed (returned false)." );
						_isInitialized = false;
						return false;
					}
				}
				catch( Exception ex )
				{
					LastInitializationException = new InvalidOperationException( "InitInputOutputExpanders() threw an exception.", ex );
					_isInitialized = false;
					return false;
				}

				try
				{
					if( !InitAluController() )
					{
						LastInitializationException = new InvalidOperationException( "InitAluController() failed (returned false)." );
						_isInitialized = false;
						return false;
					}
				}
				catch( Exception ex )
				{
					LastInitializationException = new InvalidOperationException( "InitAluController() threw an exception.", ex );
					_isInitialized = false;
					return false;
				}

				try
				{
					if( !InitializeDisplayAndTouch() )
					{
						LastInitializationException = new InvalidOperationException( "InitializeDisplayAndTouch() failed (returned false)." );
						_isInitialized = false;
						return false;
					}
				}
				catch( Exception ex )
				{
					LastInitializationException = new InvalidOperationException( "InitializeDisplayAndTouch() threw an exception.", ex );
					_isInitialized = false;
					return false;
				}

				_isInitialized = true;
				return true;
			}
		}

		/// <summary>
		/// Initializes all expected subsystems of the board exactly once (thread-safe).
		/// </summary>
		/// <remarks>
		/// <para> Initialization is idempotent: If <see cref="IsInitialized"/> is already <see langword="true"/>,
		/// <see langword="true"/> is returned immediately. </para>
		/// <para> Procedure (with error handling per step):
		/// <list type="number">
		/// <item><description>Obtain I2C bus from <see cref="IBoardHardwareContext"/></description></item>
		/// <item><description>Check for presence of expected I2C devices (<see cref="FindI2cDevices"/>)</description></item>
		/// <item><description>Initialize input/output expanders (<see cref="InitInputOutputExpanders"/>)</description></item>
		/// <item><description>Create ALU controller (<see cref="InitAluController"/>)</description></item>
		/// <item><description>Initialize display and touch (<see cref="InitializeDisplayAndTouch"/>)</description></item>
		/// </list></para>
		/// <para> In case of error, <see cref="LastInitializationException"/> is set, <see cref="IsInitialized"/> remains
		/// <see langword="false"/> and the method returns <see langword="false"/>. </para>
		/// </remarks>
		/// <returns><see langword="true"/> if initialization is successful; otherwise <see langword="false"/>.</returns>
		public void EnsureInitialized()
		{
			if( _isInitialized )
				return;

			lock( _initLock )
			{
				if( _isInitialized )
					return;

				throw new InvalidOperationException( "The board is not initialized. Check Board.Initialize() and LastInitializationException for details." );
			}
		}

		/// <summary>
		/// Initializes the board's I/O expanders and configures relevant pull-ups.
		/// </summary>
		/// <param name="i2cBus">The I2C bus provided by the host. </param>
		/// <remarks>
		/// <para> Uses GPIO lines from <see cref="IBoardHardwareContext.I2cLinesGpio"/> to control the reset pins 
		/// of the expanders and/or set their operating state. </para>
		/// <para> After <c>Initialize()</c> of the expanders, pull-ups are activated for relevant ports (including Touch-IRQ). </para>
		/// </remarks>
		/// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
		private bool InitInputOutputExpanders( I2cBus i2cBus )
		{
			GpioController i2cLinesController = _hw.I2cLinesGpio;

			// 1) Reset before the I2C check (sequence is crucial)
			TryResetExpanders( i2cLinesController );

			// 2) Classic probe instead of PerformBusScan (more robust for USB/I2C bridges)
			try
			{
				if( !ProbeExpectedExpanders( i2cBus ) )
				{
					LastInitializationException = new InvalidOperationException(
						"Expected I2C devices not found on the bus (probe failed for 0x21 and/or 0x23)." );
					_isInitialized = false;
					return false;
				}
			}
			catch( Exception ex )
			{
				LastInitializationException = new InvalidOperationException( "ProbeExpectedExpanders() threw an exception.", ex );
				_isInitialized = false;
				return false;
			}

			// 3) Only then create/initialize instances
			_v1SignalOutALU = new V1SignalOutALU( i2cBus, i2cLinesController, PIN_V1_RESET );
			_v2SignalInpALU = new V2SignalInpALU( i2cBus, i2cLinesController, PIN_V2_RESET, PIN_V2_INTA, PIN_V2_INTB );

			if( _v1SignalOutALU is null || _v2SignalInpALU is null )
				return false;

			_v1SignalOutALU.Initialize();
			_v2SignalInpALU.Initialize();

			_v1SignalOutALU.EnablePullUp( Port.PortA,
				V1SignalOutALU.PortA.A0,
				V1SignalOutALU.PortA.A1,
				V1SignalOutALU.PortA.A2,
				V1SignalOutALU.PortA.A3,
				V1SignalOutALU.PortA.B0,
				V1SignalOutALU.PortA.B1,
				V1SignalOutALU.PortA.B2,
				V1SignalOutALU.PortA.B3 );

			_v2SignalInpALU.EnablePullUp( Port.PortA, V2SignalInpALU.PortA.T_IRQ );

			return true;
		}

		/// <summary>
		/// Creates the <see cref="AluController"/> based on the initialized expanders.
		/// </summary>
		/// <remarks>
		/// This method requires that <see cref="InitInputOutputExpanders"/> has already been successfully executed.
		/// </remarks>
		/// <exception cref="InvalidOperationException">
		/// If the required expanders (<see cref="_v1SignalOutALU"/>/<see cref="_v2SignalInpALU"/>) are not initialized.
		/// </exception>
		/// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
		private bool InitAluController()
		{
			if( _v1SignalOutALU is null || _v2SignalInpALU is null )
				throw new InvalidOperationException( "Cannot create AluController: required expanders are not initialized." );

			_aluController = new AluController( _v1SignalOutALU, _v2SignalInpALU );
			return _aluController != null;
		}

		/// <summary>
		/// Initializes display and touch controller via SPI/GPIO resources provided by host.
		/// </summary>
		/// <remarks>
		/// Display: Initialization of ST7796S including DC/RESET/backlight pin.
		/// Touch: Creation of the XPT2046 controller based on the touch SPI.
		/// </remarks>
		/// <returns><see langword="true"/> if successful; otherwise <see langword="false"/>.</returns>
		private bool InitializeDisplayAndTouch()
		{
			_display = new( _hw.DisplaySpi, PIN_DC, PIN_RESET, backlightPin: PIN_LED, gpioController: _hw.DisplayGpio );
			_display.Initialize();

			touchController = new XPT2046Touch( _hw.TouchSpi );

			return _display != null && touchController != null;
		}

		/// <summary>
		/// Attempts to reset the connected I2C expander devices using the specified GPIO controller. If the required reset
		/// pins are unavailable, the operation is skipped without causing a failure.
		/// </summary>
		/// <remarks>This method is intended to assist with hardware initialization by pulsing the reset pins of I2C
		/// expanders. If the GPIO controller does not expose the necessary pins, the method completes silently without
		/// throwing exceptions.</remarks>
		/// <param name="i2cLinesController">The GPIO controller used to access the reset pins for the I2C expanders. Must provide access to the relevant GPIO
		/// lines; otherwise, the reset will not be performed.</param>
		private static void TryResetExpanders( GpioController i2cLinesController )
		{
			// Reset pins are connected to the I2C lines GPIOs provided by the host
			// If the pins are not available -> no hard fail, just no reset.
			try
			{
				// Pulse: High -> Low -> High (depending on board reset logic, may need to invert)
				using var v1Reset = i2cLinesController.OpenPin( PIN_V1_RESET, PinMode.Output );
				using var v2Reset = i2cLinesController.OpenPin( PIN_V2_RESET, PinMode.Output );

				v1Reset.Write( PinValue.High );
				v2Reset.Write( PinValue.High );

				Thread.Sleep( 2 );

				v1Reset.Write( PinValue.Low );
				v2Reset.Write( PinValue.Low );

				Thread.Sleep( 10 );

				v1Reset.Write( PinValue.High );
				v2Reset.Write( PinValue.High );

				Thread.Sleep( 10 );
			}
			catch
			{
				// Deliberate swallow: Reset is helpful,
				// but must not hard-fail init if the host cannot supply the pins.
			}
		}

		/// <summary>
		/// Checks whether the expected I2C expanders are present on the bus at addresses 0x21 and 0x23.
		/// </summary>
		/// <remarks>This method is typically used to verify that required expanders are connected and accessible
		/// before proceeding with further operations. It does not modify the state of the bus.</remarks>
		/// <param name="bus">The I2C bus to probe for the presence of expanders.</param>
		/// <returns>true if expanders are detected at both addresses; otherwise, false.</returns>
		private static bool ProbeExpectedExpanders( I2cBus bus )
		{
			var okV1 = ProbeAddress( bus, V1SignalOutALU.Address );
			var okV2 = ProbeAddress( bus, V2SignalInpALU.Address );

			return okV1 && okV2;
		}

		/// <summary>
		/// Attempts to communicate with a device at the specified I2C address to determine if it is present and responsive on
		/// the bus.
		/// </summary>
		/// <remarks>This method performs a minimal read operation to check for device presence without altering
		/// device state. It is intended for safe probing of devices such as MCP23017, where reading from the IOCON register
		/// is non-intrusive.</remarks>
		/// <param name="bus">The I2C bus instance used to probe the device address.</param>
		/// <param name="address">The address of the I2C device to probe. Must be a valid address supported by the bus.</param>
		/// <returns>true if the device at the specified address responds to the probe; otherwise, false.</returns>
		private static bool ProbeAddress( I2cBus bus, int address )
		{
			using var dev = bus.CreateDevice( address );

			Span<byte> write = stackalloc byte[ 1 ] { 0x0A }; // IOCON
			Span<byte> read = stackalloc byte[ 1 ];

			dev.WriteRead( write, read );
			return true;
		}
	}
}
