using System.Threading;

namespace AluLab.Gateway.Controller
{
	/// <summary>
	/// Application entry point for the AluLab Gateway Controller host.
	/// </summary>
	/// <remarks>
	/// This host is responsible for creating a <see cref="Board"/> instance, initializing attached
	/// hardware subsystems, and then keeping the process alive so the board can continue operating.
	/// </remarks>
	internal class Program
	{
		/// <summary>
		/// Creates and initializes the board, then blocks the main thread indefinitely.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The board initialization typically wires up I/O busses and device drivers. If initialization fails,
		/// the current implementation will still block; consider adding error handling/logging if the host
		/// should terminate on failure.
		/// </para>
		/// <para>
		/// The infinite sleep acts as a simple "run loop" for a headless process that relies on background
		/// threads/timers/events to do the actual work.
		/// </para>
		/// </remarks>
		static void Main()
		{
			// Create the board facade that provides access to hardware subsystems (I2C/SPI/GPIO, display, touch, ALU, ...).
			Board board_ = new();

			// Perform one-time initialization of board subsystems/drivers.
			board_.Initialize();

			// Keep the process alive indefinitely (this host does not currently implement a shutdown mechanism).
			Thread.Sleep( Timeout.Infinite );
		}
	}
}
