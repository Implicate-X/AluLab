using System.Device.Spi;
using AluLab.Board.Display;

namespace AluLab.Board.Touch;

/// <summary>
/// Provides access to an XPT2046 touch controller via SPI, enabling touch detection and position retrieval mapped to
/// screen coordinates.
/// </summary>
/// <remarks>This class provides methods to detect touch events, retrieve raw and mapped touch positions, and
/// manage device resources. Screen dimensions and orientation affect how touch coordinates are interpreted. Instances
/// should be disposed when no longer needed to release underlying SPI resources.</remarks>
/// <param name="spiDevice">The SPI device used to communicate with the XPT2046 touch controller. Cannot be null.</param>
/// <param name="screenWidth">The width of the screen, in pixels, used to map touch coordinates. Must be greater than zero.</param>
/// <param name="screenHeight">The height of the screen, in pixels, used to map touch coordinates. Must be greater than zero.</param>
/// <param name="orientation">The screen orientation used to adjust touch coordinate mapping. Determines how raw touch data is translated to
/// screen coordinates.</param>
public class XPT2046Touch(
		SpiDevice spiDevice,
		int screenWidth = 480,
		int screenHeight = 320,
		St7796s.Orientation orientation = St7796s.Orientation.LandscapeFlipped ) : IDisposable
{
	private const byte CMD_X_READ = 0xD0;
	private const byte CMD_Y_READ = 0x90;
	private const byte CMD_Z1_READ = 0xB0;
	private const byte CMD_Z2_READ = 0xC0;

	// Fallback-Rohbereich (typische Werte)
	private const int RAW_MIN = 100;
	private const int RAW_MAX = 4000;

	private bool _disposed;

	/// <summary>
	/// Represents the SPI device used for communication with peripheral hardware.
	/// </summary>
	private readonly SpiDevice _spiDevice = spiDevice ?? throw new ArgumentNullException( nameof( spiDevice ) );

	public int ScreenWidth { get; set; } = screenWidth;
	public int ScreenHeight { get; set; } = screenHeight;
	public St7796s.Orientation Orientation { get; set; } = orientation;

	/// <summary>
	/// Represents the default number of samples used for the median filter. The value is an odd number to ensure proper
	/// median calculation.
	/// </summary>
	private const int DefaultSampleCount = 5;

	/// <summary>
	/// Returns true if the touch is currently pressed (simple Z1/Z2 threshold).
	/// </summary>
	public bool IsTouched()
	{
		int z1 = ReadChannel( CMD_Z1_READ );
		int z2 = ReadChannel( CMD_Z2_READ );

		// leichte Heuristik; passt an, wenn nötig
		return ( z1 > 50 && z2 > 50 );
	}

	/// <summary>
	/// Gets the current touch position on the screen, mapped to screen coordinates.
	/// </summary>
	/// <remarks>The returned coordinates are adjusted based on the current screen orientation. If no valid touch is
	/// detected, both coordinates will be -1.</remarks>
	/// <returns>A tuple containing the X and Y coordinates of the touch position in screen pixels. Returns (-1, -1) if the position
	/// is invalid or outside the detectable range.</returns>
	public (int x, int y) GetPosition()
	{
		var raw = ReadMedianRaw( DefaultSampleCount );
		int rawX = raw.x;
		int rawY = raw.y;

		if( rawX < RAW_MIN || rawX > RAW_MAX || rawY < RAW_MIN || rawY > RAW_MAX )
			return (-1, -1);

		int x, y;
		if( Orientation == St7796s.Orientation.LandscapeNormal ||
			Orientation == St7796s.Orientation.LandscapeFlipped )
		{
			x = ( rawY - RAW_MIN ) * ScreenWidth / ( RAW_MAX - RAW_MIN );
			y = ( rawX - RAW_MIN ) * ScreenHeight / ( RAW_MAX - RAW_MIN );
		}
		else
		{
			x = ( rawX - RAW_MIN ) * ScreenWidth / ( RAW_MAX - RAW_MIN );
			y = ( rawY - RAW_MIN ) * ScreenHeight / ( RAW_MAX - RAW_MIN );
		}

		x = Math.Clamp( x, 0, ScreenWidth - 1 );
		y = Math.Clamp( y, 0, ScreenHeight - 1 );

		return (x, y);
	}

	/// <summary>
	/// Retrieves the raw X and Y position values from the input device.
	/// </summary>
	/// <remarks>The returned values represent unprocessed readings directly from the device channels. These values
	/// may require calibration or transformation before use in higher-level coordinate systems.</remarks>
	/// <returns>A tuple containing the raw X and Y position values as integers.</returns>
	public (int x, int y) GetRawPosition()
	{
		int rawX = ReadChannel( CMD_X_READ );
		int rawY = ReadChannel( CMD_Y_READ );
		return (rawX, rawY);
	}

	/// <summary>
	/// Reads the current values from the Z1 and Z2 channels and returns them as a tuple.
	/// </summary>
	/// <returns>A tuple containing the values read from the Z1 and Z2 channels. The first item is the Z1 value; the second item is
	/// the Z2 value.</returns>
	public (int z1, int z2) ReadZ()
	{
		int z1 = ReadChannel( CMD_Z1_READ );
		int z2 = ReadChannel( CMD_Z2_READ );
		return (z1, z2);
	}

	/// <summary>
	/// Reads a specified number of samples from the X and Y channels and returns the results as arrays.
	/// </summary>
	/// <param name="count">The number of samples to read from each channel. Must be greater than zero.</param>
	/// <returns>A tuple containing two arrays: the first array holds the X channel samples, and the second array holds the Y
	/// channel samples. Each array has a length equal to the specified count.</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is less than or equal to zero.</exception>
	public (int[] xs, int[] ys) ReadSamples( int count )
	{
		if( count <= 0 ) throw new ArgumentOutOfRangeException( nameof( count ) );
		int[] xs = new int[count];
		int[] ys = new int[count];

		for( int i = 0; i < count; i++ )
		{
			xs[i] = ReadChannel( CMD_X_READ );
			ys[i] = ReadChannel( CMD_Y_READ );
		}

		return (xs, ys);
	}

	/// <summary>
	/// Reads the specified number of samples from the X and Y channels and returns the median values as raw integer
	/// coordinates.
	/// </summary>
	/// <remarks>This method performs multiple readings from each channel and calculates the median to help reduce
	/// the impact of noise or outliers in the raw data. The returned values are not scaled or calibrated.</remarks>
	/// <param name="count">The number of samples to read from each channel. Must be greater than zero.</param>
	/// <returns>A tuple containing the median X and Y values, each as an integer. The tuple is in the form (x, y).</returns>
	/// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is less than or equal to zero.</exception>
	public (int x, int y) ReadMedianRaw( int count )
	{
		if( count <= 0 ) throw new ArgumentOutOfRangeException( nameof( count ) );
		int[] xs = new int[count];
		int[] ys = new int[count];

		for( int i = 0; i < count; i++ )
		{
			xs[i] = ReadChannel( CMD_X_READ );
			ys[i] = ReadChannel( CMD_Y_READ );
		}

		Array.Sort( xs );
		Array.Sort( ys );

		int median = count / 2;
		return (xs[median], ys[median]);
	}

	/// <summary>
	/// Reads a 12-bit value from the SPI device using the specified command byte.
	/// </summary>
	/// <remarks>This method performs a full-duplex SPI transfer, sending the command and reading the response. The
	/// returned value is typically used to represent analog or sensor data from the device.</remarks>
	/// <param name="command">The command byte to send to the SPI device. Determines which channel or register is read.</param>
	/// <returns>An integer representing the 12-bit value read from the SPI device. The value is masked to ensure only the lower 12
	/// bits are returned.</returns>
	private int ReadChannel( byte command )
	{
		byte[] write = new byte[3];
		byte[] read = new byte[3];

		write[0] = command;
		write[1] = 0;
		write[2] = 0;

		_spiDevice.TransferFullDuplex( write, read );

		int value = ( ( read[1] << 8 ) | read[2] ) >> 3;
		return value & 0x0FFF;
	}

	/// <summary>
	/// Releases all resources used by the current instance of the class.
	/// </summary>
	/// <remarks>Call this method when you are finished using the instance to free unmanaged resources and perform
	/// other cleanup operations. After calling <see cref="Dispose"/>, the instance should not be used.</remarks>
	public void Dispose()
	{
		if( !_disposed )
		{
			_spiDevice?.Dispose();
			_disposed = true;
		}
	}
}