using System.Device.Gpio;
using System.Device.I2c;
using Iot.Device.Mcp23xxx;

namespace AluLab.Board.InputOutputExpander
{
	/// <summary>
	/// Global I2C lock for all MCP23017 instances in the process.
	/// Serializes access to the shared I2C bus/device object and prevents
	/// simultaneous parallel I2C operations (cause of “Error writing device on byte 1”).
	/// </summary>
	internal static class I2cSync
	{
		/// <summary>
		/// Global lock for all MCP23017 I2C operations in the process.
		/// </summary>
		/// <remarks>
		/// This read-only object serves as a monitor for the C# keyword <c>lock</c>.
		/// Use <c>lock(I2cSync.Lock)</c> to serialize access to the shared
		/// I2C bus/device object and prevent parallel I2C operations
		/// (e.g., cause of “Error writing device on byte 1”).
		/// </remarks>
		public static readonly object Lock = new();
	}

	/// <summary>
	/// Common base for MCP23017-derived classes with safe (synchronized)
	/// read/write/enable helpers that serialize all I2C accesses via the process-wide lock
	/// and prevent double locks.
	/// In addition, the class includes a simple retry strategy with exponential backoff
	/// for temporary I2C/IO errors.
	/// </summary>
	public abstract class Mcp23017Base( 
			I2cDevice device, 
			int reset = -1, 
			int interruptA = -1, 
			int interruptB = -1, 
			GpioController? controller = null, 
			bool shouldDispose = true ) 
			: Mcp23017( device, reset, interruptA, interruptB, controller, shouldDispose )
	{
		private const int DefaultMaxAttempts = 3;
		private const int DefaultBaseDelayMs = 20;
		private static readonly Random s_rng = new();

		/// <summary>
		/// Performs initialization logic for the current instance. Override this method to provide custom initialization in
		/// derived classes.
		/// </summary>
		public virtual void Initialize()
		{
		}

		/// <summary>
		/// Sets the direction of the pins on the specified port.
		/// </summary>
		/// <param name="port">The port whose pin directions are to be configured.</param>
		/// <param name="mask">A bitmask specifying the direction for each pin. A bit value of 1 sets the corresponding pin as input; 0 sets it
		/// as output.</param>
		public void SetDir( Port port, byte mask )
		{
			WriteSafe( Register.IODIR, mask, port );
		}

		/// <summary>
		/// Sets the output state of the specified port by applying the provided bit values.
		/// </summary>
		/// <remarks>If multiple bit values are provided in the array, each value is combined with the current port
		/// state using a bitwise OR operation. If the array is empty, the method sets all bits on the port to high
		/// (0b11111111).</remarks>
		/// <param name="port">The port to set the output state for.</param>
		/// <param name="portBits">An array of bit values to apply to the port. If no values are provided, all bits on the port are set to high.</param>
		public void SetPort( Port port, params byte[] portBits )
		{
			byte portData;

			if( portBits.Length >= 1 )
			{
				portData = ReadSafe( Register.GPIO, port );

				for( int i = 0; i < portBits.Length; i++ )
				{
					portData |= portBits[ i ];
				}

				WriteSafe( Register.GPIO, portData, port );
			}
			else
			{
				WriteSafe( Register.GPIO, 0b11111111, port );
			}
		}

		/// <summary>
		/// Clears the specified bits on the given port, setting them to low. If no bits are specified, resets all bits on the
		/// port to low.
		/// </summary>
		/// <remarks>Use this method to reset individual or all bits on a port to a low state. This operation does not
		/// affect bits not specified in the bit masks unless no masks are provided, in which case the entire port is
		/// cleared.</remarks>
		/// <param name="port">The port on which to clear the specified bits.</param>
		/// <param name="portBits">An array of bit masks indicating which bits to clear. Each value should specify a bit position to set to low. If
		/// no values are provided, all bits on the port are cleared.</param>
		public void ResetPort( Port port, params byte[] portBits )
		{
			byte portData;

			if( portBits.Length >= 1 )
			{
				portData = ReadSafe( Register.GPIO, port );

				for( int i = 0; i < portBits.Length; i++ )
				{
					portData &= ( byte )~( portBits[ i ] );
				}

				WriteSafe( Register.GPIO, portData, port );
			}
			else
			{
				WriteSafe( Register.GPIO, 0b00000000, port );
			}
		}

		/// <summary>
		/// Enables the internal pull-up resistors on the specified port for the given port bits.
		/// </summary>
		/// <param name="port">The port on which to enable the pull-up resistors.</param>
		/// <param name="portBits">An array of bit masks specifying which pins on the port to enable pull-up resistors for. If no values are
		/// provided, pull-up resistors are enabled for all pins on the port.</param>
		public void EnablePullUp( Port port, params byte[] portBits )
		{
			byte reg;
			if( portBits.Length >= 1 )
			{
				reg = ReadSafe( Register.GPPU, port );
				for( int i = 0; i < portBits.Length; i++ )
				{
					reg |= portBits[ i ];
				}
			}
			else
			{
				reg = 0xFF;
			}

			WriteSafe( Register.GPPU, reg, port );
		}

		/// <summary>
		/// Disables the internal pull-up resistors for the specified port pins.
		/// </summary>
		/// <remarks>Disabling pull-up resistors may affect the input behavior of the specified pins, especially if
		/// they are left floating. Use this method when external pull-up or pull-down resistors are provided, or when the
		/// pins are driven by other sources.</remarks>
		/// <param name="port">The port on which to disable pull-up resistors.</param>
		/// <param name="portBits">An array of bit masks specifying the pins for which to disable pull-up resistors. If no values are provided,
		/// pull-up resistors are disabled for all pins on the port.</param>
		public void DisablePullUp( Port port, params byte[] portBits )
		{
			byte reg;
			if( portBits.Length >= 1 )
			{
				reg = ReadSafe( Register.GPPU, port );
				for( int i = 0; i < portBits.Length; i++ )
				{
					reg &= ( byte )~( portBits[ i ] );
				}
			}
			else
			{
				reg = 0x00;
			}

			WriteSafe( Register.GPPU, reg, port );
		}

		/// <summary>
		/// Reads a byte from the specified register and port, retrying the operation if it fails.
		/// </summary>
		/// <remarks>This method attempts to read a byte from the given register and port, automatically retrying the
		/// operation up to the specified number of attempts if failures occur. The delay between retries increases based on
		/// the base delay value. This can help improve reliability when transient errors are possible.</remarks>
		/// <param name="reg">The register from which to read the byte.</param>
		/// <param name="port">The port associated with the register.</param>
		/// <param name="maxAttempts">The maximum number of retry attempts to perform if the read operation fails. Must be greater than zero.</param>
		/// <param name="baseDelayMs">The base delay, in milliseconds, to wait between retry attempts. Must be zero or greater.</param>
		/// <returns>The byte value read from the specified register and port.</returns>
		protected byte ReadSafe( Register reg, Port port, int maxAttempts = DefaultMaxAttempts, int baseDelayMs = DefaultBaseDelayMs )
		{
			return ExecuteWithRetry( () => ReadByte( reg, port ), maxAttempts, baseDelayMs );
		}

		/// <summary>
		/// Writes a byte of data to the specified register and port, retrying the operation if it fails.
		/// </summary>
		/// <remarks>This method attempts to write the specified data to the register and port, automatically retrying
		/// the operation if an error occurs. The delay between retries increases with each attempt. This can be useful in
		/// scenarios where transient errors may occur during communication.</remarks>
		/// <param name="reg">The register to which the data will be written.</param>
		/// <param name="data">The byte value to write to the register.</param>
		/// <param name="port">The port through which the write operation is performed.</param>
		/// <param name="maxAttempts">The maximum number of retry attempts if the write operation fails. Must be greater than zero. The default is
		/// DefaultMaxAttempts.</param>
		/// <param name="baseDelayMs">The base delay, in milliseconds, between retry attempts. Must be zero or greater. The default is
		/// DefaultBaseDelayMs.</param>
		protected void WriteSafe( Register reg, byte data, Port port, int maxAttempts = DefaultMaxAttempts, int baseDelayMs = DefaultBaseDelayMs )
		{
			ExecuteWithRetry( () => { WriteByte( reg, data, port ); return true; }, maxAttempts, baseDelayMs );
		}

		/// <summary>
		/// Attempts to enable the component, retrying the operation if it fails due to transient errors.
		/// </summary>
		/// <remarks>This method is useful in scenarios where enabling the component may fail intermittently, such as
		/// due to temporary network issues. The method retries the enable operation up to the specified number of attempts,
		/// applying a delay between each attempt. If all attempts fail, an exception may be thrown by the underlying enable
		/// logic.</remarks>
		/// <param name="maxAttempts">The maximum number of retry attempts to perform if enabling fails. Must be greater than zero.</param>
		/// <param name="baseDelayMs">The initial delay, in milliseconds, between retry attempts. The delay may increase with each attempt. Must be
		/// non-negative.</param>
		protected void EnableSafe( int maxAttempts = DefaultMaxAttempts, int baseDelayMs = DefaultBaseDelayMs )
		{
			ExecuteWithRetry( () => { Enable(); return true; }, maxAttempts, baseDelayMs );
		}

		// Public wrappers that can be called from outside (e.g., AluController).
		// Visibility set to public because the classes are used in the same assembly.

		/// <summary>
		/// Reads a byte from the specified register and port with thread-safe access and automatic retry logic.
		/// </summary>
		/// <remarks>
		/// This method provides public access to the protected <see cref="ReadSafe"/> method, allowing external
		/// components (e.g., AluController) to safely read from MCP23017 registers. All I2C operations are
		/// synchronized via a process-wide lock and automatically retried on transient failures.
		/// </remarks>
		/// <param name="reg">The register from which to read the byte.</param>
		/// <param name="port">The port associated with the register.</param>
		/// <returns>The byte value read from the specified register and port.</returns>
		public byte ReadRegisterSafe( Register reg, Port port ) => ReadSafe( reg, port );

		/// <summary>
		/// Writes a byte of data to the specified register and port with thread-safe access and automatic retry logic.
		/// </summary>
		/// <remarks>
		/// This method provides public access to the protected <see cref="WriteSafe"/> method, allowing external
		/// components (e.g., AluController) to safely write to MCP23017 registers. All I2C operations are
		/// synchronized via a process-wide lock and automatically retried on transient failures.
		/// </remarks>
		/// <param name="reg">The register to which the data will be written.</param>
		/// <param name="data">The byte value to write to the register.</param>
		/// <param name="port">The port through which the write operation is performed.</param>
		public void WriteRegisterSafe( Register reg, byte data, Port port ) => WriteSafe( reg, data, port );

		/// <summary>
		/// Enables the component with thread-safe access and automatic retry logic.
		/// </summary>
		/// <remarks>
		/// This method provides public access to the protected <see cref="EnableSafe"/> method, allowing external
		/// components (e.g., AluController) to safely enable the MCP23017 device. The operation is synchronized
		/// via a process-wide lock and automatically retried on transient failures.
		/// </remarks>
		public void EnableRegisterSafe() => EnableSafe();
		/// <summary>
		/// Executes the specified action with automatic retries using exponential backoff in case of transient I/O failures.
		/// </summary>
		/// <remarks>If the action throws an IOException, the method retries the operation up to the specified number
		/// of attempts, waiting for an exponentially increasing delay with some random jitter between each attempt. If the
		/// action throws any other exception, or if all retry attempts are exhausted, the exception is propagated to the
		/// caller. The action is executed within a lock to ensure thread safety.</remarks>
		/// <typeparam name="T">The type of the value returned by the action.</typeparam>
		/// <param name="action">The function to execute. This delegate is invoked on each attempt and should contain the operation to be retried.</param>
		/// <param name="maxAttempts">The maximum number of retry attempts. If less than or equal to zero, a default value is used.</param>
		/// <param name="baseDelayMs">The base delay, in milliseconds, used for calculating the exponential backoff between retries. If less than or
		/// equal to zero, a default value is used.</param>
		/// <returns>The result returned by the action if it completes successfully.</returns>
		private T ExecuteWithRetry<T>( Func<T> action, int maxAttempts, int baseDelayMs )
		{
			if( maxAttempts <= 0 ) maxAttempts = DefaultMaxAttempts;
			if( baseDelayMs <= 0 ) baseDelayMs = DefaultBaseDelayMs;

			int attempt = 0;
			while( true )
			{
				attempt++;
				try
				{
					lock( I2cSync.Lock )
					{
						return action();
					}
				}
				catch( IOException ) when( attempt < maxAttempts )
				{
					// Exponential backoff with some jitter
					int delay = baseDelayMs * ( 1 << ( attempt - 1 ) );
					int jitter = s_rng.Next( 0, Math.Min( 50, delay ) );
					Thread.Sleep( delay + jitter );
					// retry
				}
				catch( Exception )
				{
					// Non-IOException or final error -> pass on
					throw;
				}
			}
		}
	}
}
