using System;
using Iot.Device.Mcp23xxx;

namespace AluLab.Board.InputOutputExpander
{
	public static class Mcp23017Extensions
	{
		/// <summary>
		/// Reads a register of the MCP23017 safely.
		/// </summary>
		/// <remarks>
		/// If the device is a safe implementation (`Mcp23017SafeBase`), its own safe method is used.
		/// Otherwise, a global I2C lock is used before directly calling `ReadByte` to prevent concurrent access.
		/// Exceptions are not caught and are passed to the caller.
		/// </remarks>
		/// <param name="mcp">The MCP23017 device.</param>
		/// <param name="reg">The register to be read.</param>
		/// <param name="port">The port (A or B) to read from.</param>
		/// <returns>The byte value of the register that was read.</returns>
		public static byte ReadRegisterSafe( this Mcp23017 mcp, Iot.Device.Mcp23xxx.Register reg, Port port )
		{
			// If the device has a special “safe” implementation, use its own logic.
			if (mcp is Mcp23017Base safe)
				return safe.ReadRegisterSafe(reg, port);

			// If no special implementation is available, ensure thread safety
			// using a shared I2C lock and read the register directly.
			lock( I2cSync.Lock )
			{
				return mcp.ReadByte(reg, port);
			}
		}

		/// <summary>
		/// Writes safely to a register of the MCP23017.
		/// </summary>
		/// <remarks>
		/// If the passed device is a special safe implementation
		/// (`Mcp23017SafeBase`), its own synchronization/retry logic
		/// is used via `WriteRegisterSafe`. Otherwise, before directly
		/// calling `WriteByte`, a global I2C lock (`I2cSync.Lock`) is used
		/// to serialize concurrent I2C accesses.
		/// Exceptions are not caught, but passed on to the caller.
		/// </remarks>
		/// <param name="mcp">The MCP23017 device.</param>
		/// <param name="reg">The register to write to.</param>
		/// <param name="data">The byte to be written.</param>
		/// <param name="port">The port (`Port.A` or `Port.B`).</param>
		/// <exception cref="System.Exception">I/O errors when accessing the device are passed on to the caller.</exception>
		public static void WriteRegisterSafe( this Mcp23017 mcp, Iot.Device.Mcp23xxx.Register reg, byte data, Port port )
		{
			if (mcp is Mcp23017Base safe)
			{
				safe.WriteRegisterSafe(reg, data, port);
				return;
			}

			lock (I2cSync.Lock)
			{
				mcp.WriteByte(reg, data, port);
			}
		}
	}
}
