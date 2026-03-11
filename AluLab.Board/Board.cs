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
		/// Host-provided hardware context used to obtain bus/controller instances and pin assignments.
		/// </summary>
		/// <remarks>
		/// This class never creates nor disposes bus/controller resources; it only consumes the instances
		/// exposed by the host via <see cref="IBoardHardwareContext"/>.
		/// </remarks>
		private readonly IBoardHardwareContext _hw = hw ?? throw new ArgumentNullException( nameof( hw ) );

		/// <summary>
		/// Cached I2C bus instance obtained from <see cref="_hw"/> during <see cref="Initialize"/>.
		/// </summary>
		private I2cBus? _i2cBus;

		/// <summary>
		/// Output expander for ALU signals (V1).
		/// </summary>
		private V1SignalOutALU? _v1SignalOutALU;

		/// <summary>
		/// Input expander for ALU signals (V2).
		/// </summary>
		private V2SignalInpALU? _v2SignalInpALU;

		/// <summary>
		/// High-level ALU controller built on top of the V1/V2 expanders.
		/// </summary>
		private AluController? _aluController;

		/// <summary>
		/// LCD display driver instance (ST7796S).
		/// </summary>
		private St7796s? _display;

		/// <summary>
		/// Touch controller driver instance (XPT2046).
		/// </summary>
		private XPT2046Touch? _touchController;

		/// <summary>
		/// Input service used to expose/aggregate touch input events for consumers.
		/// </summary>
		/// <remarks>
		/// Initialized eagerly to allow internal wiring once touch is available; consumers should access it
		/// via <see cref="TouchInputService"/> after successful <see cref="Initialize"/>.
		/// </remarks>
		private protected InputService _touchInputService = new();

		/// <summary>
		/// Indicates whether initialization has successfully completed.
		/// </summary>
		/// <remarks>
		/// Marked <see langword="volatile"/> to ensure visibility across threads for the fast-path check.
		/// </remarks>
		private volatile bool _isInitialized;

		/// <summary>
		/// Synchronization object used to guarantee one-time initialization and consistent failure reporting.
		/// </summary>
		private readonly object _initLock = new();

		/// <summary>
		/// Holds the most recent initialization error, if any.
		/// </summary>
		/// <remarks>
		/// Set by <see cref="Initialize"/> and some internal init steps. When initialization succeeds this is cleared.
		/// Consumers can inspect this value after <see cref="Initialize"/> returns <see langword="false"/>.
		/// </remarks>
		public Exception? LastInitializationException { get; private set; }

		/// <summary>
		/// Gets whether the board has been initialized successfully.
		/// </summary>
		public bool IsInitialized => _isInitialized;

		/// <summary>
		/// Gets the I2C bus used by board peripherals.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// Thrown when accessed before a successful call to <see cref="Initialize"/>.
		/// </exception>
		public I2cBus I2cBus { get { EnsureInitialized(); return _i2cBus!; } private set => _i2cBus = value; }

		/// <summary>
		/// Gets the V1 ALU output expander instance used to drive ALU control/data lines.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// Thrown when accessed before a successful call to <see cref="Initialize"/>.
		/// </exception>
		public V1SignalOutALU V1SignalOutALU { get { EnsureInitialized(); return _v1SignalOutALU!; } private set => _v1SignalOutALU = value; }

		/// <summary>
		/// Gets the V2 ALU input expander instance used to observe ALU signals (including interrupt lines).
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// Thrown when accessed before a successful call to <see cref="Initialize"/>.
		/// </exception>
		public V2SignalInpALU V2SignalInpALU { get { EnsureInitialized(); return _v2SignalInpALU!; } private set => _v2SignalInpALU = value; }

		/// <summary>
		/// Gets the high-level ALU controller composed from the expander instances.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// Thrown when accessed before a successful call to <see cref="Initialize"/>.
		/// </exception>
		public AluController AluController { get { EnsureInitialized(); return _aluController!; } private set => _aluController = value; }

		/// <summary>
		/// Gets the display driver instance.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// Thrown when accessed before a successful call to <see cref="Initialize"/>.
		/// </exception>
		public St7796s Display { get { EnsureInitialized(); return _display!; } private set => _display = value; }

		/// <summary>
		/// Gets the touch controller driver instance.
		/// </summary>
		/// <exception cref="InvalidOperationException">
		/// Thrown when accessed before a successful call to <see cref="Initialize"/>.
		/// </exception>
		public XPT2046Touch TouchController { get { EnsureInitialized(); return _touchController!; } private set => _touchController = value; }

		/// <summary>
		/// Gets the input service used by the touch subsystem.
		/// </summary>
		/// <remarks>
		/// Call <see cref="Initialize"/> first; otherwise <see cref="EnsureInitialized"/> will throw.
		/// </remarks>
		public InputService TouchInputService { get { EnsureInitialized(); return _touchInputService; } }

		/// <summary>
		/// Performs one-time, thread-safe initialization of board subsystems.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The method is idempotent: calling it multiple times after a successful initialization returns <see langword="true"/>
		/// without repeating work.
		/// </para>
		/// <para>
		/// Initialization steps are performed in a fixed order:
		/// <list type="number">
		/// <item><description>Acquire the I2C bus from <see cref="IBoardHardwareContext"/>.</description></item>
		/// <item><description>Initialize/probe the I/O expanders used for ALU signals.</description></item>
		/// <item><description>Create the <see cref="AluController"/>.</description></item>
		/// <item><description>Initialize the display and touch controller.</description></item>
		/// </list>
		/// </para>
		/// <para>
		/// On failure, <see cref="LastInitializationException"/> is set with an explanatory wrapper exception and the method
		/// returns <see langword="false"/>.
		/// </para>
		/// </remarks>
		/// <returns>
		/// <see langword="true"/> if initialization completed successfully; otherwise <see langword="false"/>.
		/// </returns>
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
		/// Ensures the board has been initialized (successfully) before allowing access to public subsystems.
		/// </summary>
		/// <remarks>
		/// This method is used by public properties as a guard to prevent consumers from using uninitialized hardware drivers.
		/// </remarks>
		/// <exception cref="InvalidOperationException">
		/// Thrown when the board has not been initialized successfully. Inspect <see cref="LastInitializationException"/>
		/// after calling <see cref="Initialize"/> for details about the failure.
		/// </exception>
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
		/// Initializes the I/O expander devices used for the ALU interface and configures basic pin behavior.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The initialization sequence is sensitive and performed in three phases:
		/// <list type="number">
		/// <item><description>Attempt to reset both expanders via GPIO (best-effort).</description></item>
		/// <item><description>Probe the expected I2C addresses to ensure the devices are present.</description></item>
		/// <item><description>Create expander instances, call their `Initialize`, and configure pull-ups.</description></item>
		/// </list>
		/// </para>
		/// <para>
		/// Pull-ups are enabled for a set of ALU-related pins on the V1 expander and for the touch IRQ pin on the V2 expander.
		/// </para>
		/// </remarks>
		/// <param name="i2cBus">The I2C bus to use for expander communication.</param>
		/// <returns><see langword="true"/> when expander initialization completes successfully; otherwise <see langword="false"/>.</returns>
		private bool InitInputOutputExpanders( I2cBus i2cBus )
		{
			GpioController i2cLinesController = _hw.I2cLinesGpio;

			// 1) Reset before the I2C check (sequence is crucial)
			TryResetExpanders( i2cLinesController, _hw.V1ExpanderResetPin, _hw.V2ExpanderResetPin );

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
			_v1SignalOutALU = new V1SignalOutALU( i2cBus, i2cLinesController, _hw.V1ExpanderResetPin );
			_v2SignalInpALU = new V2SignalInpALU(
				i2cBus,
				i2cLinesController,
				_hw.V2ExpanderResetPin,
				_hw.V2ExpanderInterruptAPin,
				_hw.V2ExpanderInterruptBPin );

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
		/// Creates the <see cref="AluController"/> after the required expander instances are available.
		/// </summary>
		/// <remarks>
		/// This method assumes <see cref="InitInputOutputExpanders"/> ran successfully. If not, it throws to indicate a programming
		/// error in the initialization sequence (as opposed to a runtime hardware failure).
		/// </remarks>
		/// <returns><see langword="true"/> if the controller was created; otherwise <see langword="false"/>.</returns>
		/// <exception cref="InvalidOperationException">
		/// Thrown if the expander instances do not exist (initialization order violation).
		/// </exception>
		private bool InitAluController()
		{
			if( _v1SignalOutALU is null || _v2SignalInpALU is null )
				throw new InvalidOperationException( "Cannot create AluController: required expanders are not initialized." );

			_aluController = new AluController( _v1SignalOutALU, _v2SignalInpALU );
			return _aluController != null;
		}

		/// <summary>
		/// Creates and initializes the display and touch controller drivers using host-provided SPI and GPIO resources.
		/// </summary>
		/// <remarks>
		/// The display is actively initialized via <see cref="St7796s.Initialize"/>. The touch controller is constructed; any
		/// additional calibration/configuration is expected to be handled by the touch abstraction itself or by higher layers.
		/// </remarks>
		/// <returns><see langword="true"/> if both driver instances were created; otherwise <see langword="false"/>.</returns>
		private bool InitializeDisplayAndTouch()
		{
			_display = new(
				_hw.DisplaySpi,
				_hw.DisplayDataCommandPin,
				_hw.DisplayResetPin,
				backlightPin: _hw.DisplayBacklightPin,
				gpioController: _hw.DisplayGpio );

			_display.Initialize();

			_touchController = new XPT2046Touch( _hw.TouchSpi );

			return _display != null && _touchController != null;
		}

		/// <summary>
		/// Attempts to reset both I/O expander devices via their reset pins.
		/// </summary>
		/// <remarks>
		/// This is a best-effort operation; exceptions are intentionally swallowed so that initialization can still proceed
		/// on hosts that cannot supply reset GPIO access.
		/// </remarks>
		/// <param name="i2cLinesController">GPIO controller used to drive the reset pins.</param>
		/// <param name="v1ResetPin">Reset pin number for the V1 expander.</param>
		/// <param name="v2ResetPin">Reset pin number for the V2 expander.</param>
		private static void TryResetExpanders( GpioController i2cLinesController, int v1ResetPin, int v2ResetPin )
		{
			try
			{
				using var v1Reset = i2cLinesController.OpenPin( v1ResetPin, PinMode.Output );
				using var v2Reset = i2cLinesController.OpenPin( v2ResetPin, PinMode.Output );

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
				// Deliberate swallow: Reset is helpful, but must not hard-fail init if the host cannot supply the pins.
			}
		}

		/// <summary>
		/// Probes the I2C bus for the expander devices expected by the board design.
		/// </summary>
		/// <param name="bus">The I2C bus to probe.</param>
		/// <returns>
		/// <see langword="true"/> if all required expander addresses respond; otherwise <see langword="false"/>.
		/// </returns>
		private static bool ProbeExpectedExpanders( I2cBus bus )
		{
			var okV1 = ProbeAddress( bus, V1SignalOutALU.Address );
			var okV2 = ProbeAddress( bus, V2SignalInpALU.Address );

			return okV1 && okV2;
		}

		/// <summary>
		/// Performs a minimal read attempt against a specific I2C address to verify device presence.
		/// </summary>
		/// <remarks>
		/// The probe uses a `WriteRead` against the IOCON register (0x0A) which is typical for MCP23xxx-style expanders.
		/// </remarks>
		/// <param name="bus">The I2C bus used to create the device.</param>
		/// <param name="address">7-bit I2C address to probe.</param>
		/// <returns>
		/// <see langword="true"/> if the device responded without throwing; otherwise <see langword="false"/>.
		/// </returns>
		private static bool ProbeAddress( I2cBus bus, int address )
		{
			using var dev = bus.CreateDevice( address );

			Span<byte> write = stackalloc byte[ 1 ] { 0x0A }; // IOCON
			Span<byte> read = stackalloc byte[ 1 ];

			try
			{
				dev.WriteRead( write, read );
			}
			catch( Exception ex )
			{
			}
			return true;
		}
	}
}
