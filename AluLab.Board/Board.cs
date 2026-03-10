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
		private readonly IBoardHardwareContext _hw = hw ?? throw new ArgumentNullException( nameof( hw ) );

		private I2cBus? _i2cBus;
		private V1SignalOutALU? _v1SignalOutALU;
		private V2SignalInpALU? _v2SignalInpALU;
		private AluController? _aluController;
		private St7796s? _display;
		private XPT2046Touch? _touchController;

		private protected InputService _touchInputService = new();

		private volatile bool _isInitialized;
		private readonly object _initLock = new();

		public Exception? LastInitializationException { get; private set; }

		public bool IsInitialized => _isInitialized;

		public I2cBus I2cBus { get { EnsureInitialized(); return _i2cBus!; } private set => _i2cBus = value; }

		public V1SignalOutALU V1SignalOutALU { get { EnsureInitialized(); return _v1SignalOutALU!; } private set => _v1SignalOutALU = value; }

		public V2SignalInpALU V2SignalInpALU { get { EnsureInitialized(); return _v2SignalInpALU!; } private set => _v2SignalInpALU = value; }

		public AluController AluController { get { EnsureInitialized(); return _aluController!; } private set => _aluController = value; }

		public St7796s Display { get { EnsureInitialized(); return _display!; } private set => _display = value; }

		public XPT2046Touch TouchController { get { EnsureInitialized(); return _touchController!; } private set => _touchController = value; }

		public InputService TouchInputService { get { EnsureInitialized(); return _touchInputService; } }

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

		private bool InitAluController()
		{
			if( _v1SignalOutALU is null || _v2SignalInpALU is null )
				throw new InvalidOperationException( "Cannot create AluController: required expanders are not initialized." );

			_aluController = new AluController( _v1SignalOutALU, _v2SignalInpALU );
			return _aluController != null;
		}

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

		private static bool ProbeExpectedExpanders( I2cBus bus )
		{
			var okV1 = ProbeAddress( bus, V1SignalOutALU.Address );
			var okV2 = ProbeAddress( bus, V2SignalInpALU.Address );

			return okV1 && okV2;
		}

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
