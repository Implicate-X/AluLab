namespace AluLab.Board.Platform;

/// <summary>
/// Provides an abstraction for obtaining a <see cref="Board"/> instance from the current environment
/// (e.g., from a platform/hardware configuration) without necessarily throwing exceptions.
/// </summary>
/// <remarks>
/// The pattern corresponds to a classic <c>Try*</c> call: The return value signals success or failure,
/// while details are returned via <paramref name="board"/> or <paramref name="error"/>.
/// </remarks>
public interface IBoardProvider
{
	/// <summary>
	/// Attempts to provide a <see cref="Board"/> instance.
	/// </summary>
	/// <param name="board"> If the call is successful, this parameter contains the referenced <see cref="Board"/> instance;
	/// otherwise <see langword="null"/>. </param>
	/// <param name="error"> If the call fails, this parameter contains a descriptive error message;
	/// otherwise <see langword="null"/>. </param>
	/// <returns>
	/// <see langword="true"/> if a <see cref="Board"/> instance was successfully obtained; otherwise <see langword="false"/>.
	/// </returns>
	bool TryGetBoard( out Board? board, out string? error );
}