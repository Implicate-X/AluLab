using System;
using Iot.Device.Mcp23xxx;

namespace AluLab.Board.InputOutputExpander;

/// <summary>
/// Provides utility methods for configuring and accessing individual pins on an MCP23017 I/O expander device.
/// </summary>
/// <remarks>This static class offers helper functions to simplify common pin operations, such as setting pin
/// direction, reading pin state, and writing output values. All methods require a reference to an initialized Mcp23017
/// instance. Pin numbers are specified in the range 0–15, where 0–7 correspond to Port A and 8–15 to Port B. These
/// utilities are intended to streamline direct register manipulation when working with the MCP23017 device.</remarks>
public static class Mcp23017PinUtility
{
    /// <summary>
    /// Configures the direction of a specified pin on the MCP23017 device as either input or output.
    /// </summary>
    /// <remarks>Changing the direction of a pin may affect its current state and behavior. Ensure that no
    /// conflicting operations are performed on the pin while its direction is being changed.</remarks>
    /// <param name="mcp">The MCP23017 device on which to set the pin direction.</param>
    /// <param name="pinNumber">The zero-based pin number to configure. Must be in the valid range for the device.</param>
    /// <param name="isInput">true to configure the pin as an input; otherwise, false to configure it as an output.</param>
	public static void SetDirection(Mcp23017 mcp, int pinNumber, bool isInput)
    {
        var (port, bit) = GetPortAndBit(pinNumber);
        var (iodir, _) = ReadDirAndGpio(mcp, port);
        if (isInput)
            iodir |= (byte)(1 << bit);
        else
            iodir &= (byte)~(1 << bit);
        mcp.WriteRegisterSafe(Iot.Device.Mcp23xxx.Register.IODIR, iodir, port);
    }

    /// <summary>
    /// Sets the output value of the specified pin on the given MCP23017 device.
    /// </summary>
    /// <remarks>The specified pin must be configured as an output before calling this method. Writing to a
    /// pin configured as an input has no effect.</remarks>
    /// <param name="mcp">The MCP23017 device instance to write the pin value to. Cannot be null.</param>
    /// <param name="pinNumber">The zero-based pin number to set. Must be within the valid range for the device (0–15).</param>
    /// <param name="value">The value to set for the pin. Set to <see langword="true"/> to drive the pin high; otherwise, <see
    /// langword="false"/> to drive it low.</param>
    public static void WritePin(Mcp23017 mcp, int pinNumber, bool value)
    {
        var (port, bit) = GetPortAndBit(pinNumber);
        var gpio = mcp.ReadRegisterSafe(Iot.Device.Mcp23xxx.Register.GPIO, port);
        if (value)
            gpio |= (byte)(1 << bit);
        else
            gpio &= (byte)~(1 << bit);
        mcp.WriteRegisterSafe(Iot.Device.Mcp23xxx.Register.GPIO, gpio, port);
    }

    /// <summary>
    /// Reads the current logic level of the specified pin on the given MCP23017 device.
    /// </summary>
    /// <remarks>This method does not modify the state of the pin. Ensure the pin is configured as an input
    /// before calling this method to obtain valid results.</remarks>
    /// <param name="mcp">The MCP23017 device instance from which to read the pin state. Cannot be null.</param>
    /// <param name="pinNumber">The zero-based pin number to read. Must be in the valid range for the device (0–15).</param>
    /// <returns>true if the specified pin is high; otherwise, false.</returns>
	public static bool ReadPin(Mcp23017 mcp, int pinNumber)
    {
        var (port, bit) = GetPortAndBit(pinNumber);
        var gpio = mcp.ReadRegisterSafe(Iot.Device.Mcp23xxx.Register.GPIO, port);
        return (gpio & (1 << bit)) != 0;
    }

    /// <summary>
    /// Reads the I/O direction and GPIO register values for the specified port from the MCP23017 device.
    /// </summary>
    /// <param name="mcp">The MCP23017 device instance from which to read register values. Cannot be null.</param>
    /// <param name="port">The port (A or B) for which to read the IODIR and GPIO register values.</param>
    /// <returns>A tuple containing the IODIR (I/O direction) register value and the GPIO register value for the specified port.</returns>
	public static (byte iodir, byte gpio) ReadDirAndGpio(Mcp23017 mcp, Port port)
    {
        var iodir = mcp.ReadRegisterSafe(Iot.Device.Mcp23xxx.Register.IODIR, port);
        var gpio = mcp.ReadRegisterSafe(Iot.Device.Mcp23xxx.Register.GPIO, port);
        return (iodir, gpio);
    }

    /// <summary>
    /// Determines the port and bit position corresponding to the specified pin number.
    /// </summary>
    /// <param name="pinNumber">The pin number to map to a port and bit position. Must be in the range 0 to 15.</param>
    /// <returns>A tuple containing the port and the bit position within that port that correspond to the specified pin number.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if pinNumber is less than 0 or greater than 15.</exception>
	private static (Port port, int bit) GetPortAndBit(int pinNumber)
    {
        if (pinNumber < 0 || pinNumber > 15)
            throw new ArgumentOutOfRangeException(nameof(pinNumber));
        return pinNumber < 8
            ? (Port.PortA, pinNumber)
            : (Port.PortB, pinNumber - 8);
    }
}