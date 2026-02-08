namespace AluLab.Board.Platform;

/// <summary>
/// Provides exactly one <see cref="Board"/> instance per process (lazy) and encapsulates the
/// creation/error logic behind a try pattern.
/// </summary>
/// <remarks>
/// <para> This provider implements a process-wide single-instance policy: Once a <see cref="Board"/> 
/// has been successfully created anywhere in the process, any further creation (even via another
/// <see cref="BoardProvider"/> instance) will deterministically fail. </para>
/// <para> The provider does not own or manage any hardware resources (I2C/SPI/GPIO) itself, 
/// but only uses the injected <see cref="IBoardHardwareContext"/>. </para>
/// <para> Thread safety: Process-wide single creation is secured using <see cref="Interlocked.Exchange(ref int, int)"/>. 
/// The reference to the created <see cref="Board"/> and the persisted error state are then cached in the instance state. </para>
/// </remarks>
public sealed class BoardProvider( IBoardHardwareContext hw ) : IBoardProvider
{
	/// <summary>
	/// Process-wide flag indicating whether a <see cref="Board"/> instance has already been created (0 = no, 1 = yes).
	/// </summary>
	private static int _created;

	/// <summary>
	/// The externally provided hardware context used to create the <see cref="Board"/> instance.
	/// </summary>
	/// <remarks>
	/// <para> The provider does not own or manage any hardware resources (I2C/SPI/GPIO).
	/// Instead, only the injected <see cref="IBoardHardwareContext"/> is used. </para>
	/// <para> The parameter <paramref name="hw"/> is mandatory; if <c>null</c>, an
	/// <see cref="ArgumentNullException"/> is triggered to detect configuration/DI errors early on. </para>
	/// </remarks>
	private readonly IBoardHardwareContext _hw = hw ?? throw new ArgumentNullException( nameof(hw ) );

	/// <summary>
	/// Cached, successfully created <see cref="Board"/> instance (per provider instance).
	/// </summary>
	private Board? _board;

	/// <summary>
	/// Cached error text if creation ultimately failed (per provider instance).
	/// </summary>
	private string? _error;

	/// <summary>
	/// Returns a <see cref="Board"/> instance, if available or can be created; otherwise, an error text.
	/// </summary>
	/// <param name="board"> If successful, the provided (possibly previously cached) <see cref="Board"/> instance; otherwise <c>null</c>. </param>
	/// <param name="error"> If unsuccessful, a description of the problem; otherwise <c>null</c>. </param>
	/// <returns> <c>true</c> if a <see cref="Board"/> could be delivered; otherwise <c>false</c>. </returns>
	/// <remarks>
	/// <para> Success case: If an instance is already cached in <see cref="_board"/>, it is returned. </para>
	/// <para> Error case: If an error is already cached in <see cref="_error"/>, it is returned again. </para>
	/// <para> One-time creation: If no instance exists yet and no error is cached, an atomic check is performed
	/// to see if a <see cref="Board"/> has already been created in the process. If so, the error
	/// <c>“Only one Board instance is allowed per process.”</c> is set and returned. </para>
	/// </remarks>
	public bool TryGetBoard( out Board? board, out string? error )
	{
		if( _board != null )
		{
			board = _board;
			error = null;
			return true;
		}

		if( _error != null )
		{
			board = null;
			error = _error;
			return false;
		}

		if( Interlocked.Exchange( ref _created, 1 ) == 1 )
		{
			_error = "Only one Board instance is allowed per process.";
			board = null;
			error = _error;
			return false;
		}

		_board = new Board( _hw );
		board = _board;
		error = null;
		return true;
	}
}