using System;
using System.Threading;
using GHIElectronics.TinyCLR.Devices.Gpio;

namespace AluLab.Gateway.Controller
{
	/// <summary>
	/// Robust rotary encoder (quadrature) decoder using timer polling + a lookup-table (LUT) state machine.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The decoder samples the two encoder channels (A/B) periodically and translates valid quadrature transitions
	/// into direction deltas using a 4x4 transition LUT. Deltas are accumulated until a configured number of
	/// transitions constitutes one logical step (typically one detent).
	/// </para>
	/// <para>
	/// Polling is used intentionally (instead of GPIO interrupts) to improve stability on some TinyCLR targets.
	/// </para>
	/// <para>
	/// Thread-safety: sampling and event emission are serialized via an internal lock. Disposing stops the timer;
	/// no further events are emitted after disposal is observed.
	/// </para>
	/// </remarks>
	internal sealed class RotaryEncoder : IDisposable
	{
		/// <summary>
		/// Handler for <see cref="Stepped"/>.
		/// </summary>
		/// <param name="step">
		/// Logical direction step: <c>+1</c> for clockwise/right, <c>-1</c> for counter-clockwise/left.
		/// </param>
		public delegate void StepHandler( int step );

		/// <summary>
		/// Quadrature transition lookup table.
		/// </summary>
		/// <remarks>
		/// The index is built from the previous 2-bit state and the current 2-bit state: <c>(last &lt;&lt; 2) | current</c>.
		/// Each entry yields:
		/// <list type="bullet">
		/// <item><description><c>+1</c>: valid forward transition</description></item>
		/// <item><description><c>-1</c>: valid reverse transition</description></item>
		/// <item><description><c>0</c>: invalid/noise/bounce transition (ignored)</description></item>
		/// </list>
		/// </remarks>
		private static readonly sbyte[] Lut =
		{
			 0, +1, -1,  0,
			-1,  0,  0, +1,
			+1,  0,  0, -1,
			 0, -1, +1,  0
		};

		private readonly GpioPin pinA_;
		private readonly GpioPin pinB_;
		private readonly Timer pollTimer_;
		private readonly int transitionsPerStep_;
		private readonly object sync_ = new();

		private byte lastState_;
		private int accum_;
		private bool disposed_;

		/// <summary>
		/// Raised when a logical step is detected.
		/// </summary>
		/// <remarks>
		/// The event argument is <c>+1</c> (right/clockwise) or <c>-1</c> (left/counter-clockwise).
		/// A step is emitted when the accumulated valid transitions reach <see cref="transitionsPerStep_"/>.
		/// </remarks>
		public event StepHandler? Stepped;

		/// <summary>
		/// Creates a new rotary encoder decoder that polls two GPIO pins (A/B).
		/// </summary>
		/// <param name="pinA">GPIO pin connected to encoder channel A.</param>
		/// <param name="pinB">GPIO pin connected to encoder channel B.</param>
		/// <param name="transitionsPerStep">
		/// Number of valid quadrature transitions required to emit one logical step (detent).
		/// Common values are <c>1</c>, <c>2</c> (default), or <c>4</c> depending on the encoder and desired sensitivity.
		/// </param>
		/// <param name="pollPeriodMs">
		/// Polling period in milliseconds. Smaller values react faster but use more CPU. Default is <c>2</c> ms.
		/// </param>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="pinA"/> or <paramref name="pinB"/> is <see langword="null"/>.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="transitionsPerStep"/> or <paramref name="pollPeriodMs"/> is non-positive.</exception>
		public RotaryEncoder(
			GpioPin pinA,
			GpioPin pinB,
			int transitionsPerStep = 2,
			int pollPeriodMs = 2 )
		{
			if( transitionsPerStep <= 0 )
				throw new ArgumentOutOfRangeException( nameof( transitionsPerStep ) );
			if( pollPeriodMs <= 0 )
				throw new ArgumentOutOfRangeException( nameof( pollPeriodMs ) );

			pinA_ = pinA ?? throw new ArgumentNullException( nameof( pinA ) );
			pinB_ = pinB ?? throw new ArgumentNullException( nameof( pinB ) );

			transitionsPerStep_ = transitionsPerStep;

			// Initialize with the current physical state to avoid a synthetic transition on the first poll.
			lastState_ = ReadState();

			// Polling is intentionally used (more stable than edge interrupts on some TinyCLR targets)
			pollTimer_ = new Timer( _ => Poll(), null, dueTime: 0, period: pollPeriodMs );
		}

		/// <summary>
		/// Stops polling and releases the underlying timer resources.
		/// </summary>
		/// <remarks>
		/// After disposal is observed, subsequent polls return immediately and no further <see cref="Stepped"/> events are raised.
		/// </remarks>
		public void Dispose()
		{
			lock( sync_ )
			{
				if( disposed_ )
					return;

				disposed_ = true;
			}

			pollTimer_.Dispose();
		}

		/// <summary>
		/// Reads the current 2-bit quadrature state from pins A and B.
		/// </summary>
		/// <returns>
		/// A packed state where bit 0 is channel A and bit 1 is channel B (<c>0</c>=Low, <c>1</c>=High).
		/// </returns>
		private byte ReadState()
		{
			// Bit0 = A, Bit1 = B (0 = Low, 1 = High)
			int a = pinA_.Read() == GpioPinValue.High ? 1 : 0;
			int b = pinB_.Read() == GpioPinValue.High ? 1 : 0;
			return ( byte )( a | ( b << 1 ) );
		}

		/// <summary>
		/// Polls the GPIO pins, updates the state machine, and emits <see cref="Stepped"/> when a full step is detected.
		/// </summary>
		/// <remarks>
		/// Algorithm:
		/// <list type="number">
		/// <item><description>Read current A/B state.</description></item>
		/// <item><description>Compute LUT index from last/current state.</description></item>
		/// <item><description>Map transition to delta (+1/-1/0) and accumulate.</description></item>
		/// <item><description>When the accumulator magnitude reaches <see cref="transitionsPerStep_"/>, emit one step and reset.</description></item>
		/// </list>
		/// Invalid transitions (often caused by contact bounce) are ignored via <c>delta == 0</c>.
		/// </remarks>
		private void Poll()
		{
			lock( sync_ )
			{
				if( disposed_ )
					return;

				byte state = ReadState();
				if( state == lastState_ )
					return;

				int idx = ( lastState_ << 2 ) | state;
				lastState_ = state;

				int delta = Lut[ idx ];
				if( delta == 0 )
					return;

				accum_ += delta;

				if( accum_ >= transitionsPerStep_ )
				{
					accum_ = 0;
					Stepped?.Invoke( +1 );
				}
				else if( accum_ <= -transitionsPerStep_ )
				{
					accum_ = 0;
					Stepped?.Invoke( -1 );
				}
			}
		}
	}
}