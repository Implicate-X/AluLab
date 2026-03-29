using System.ComponentModel.DataAnnotations;

namespace AluLab.Server.Api;

/// <summary>
/// Helper methods for validating ALU simulation requests and decoding raw ALU output signals
/// into logical values.
/// </summary>
/// <remarks>
/// This type is internal and stateless; it centralizes common validation/decoding logic used by the API layer.
/// </remarks>
internal static class AluSim
{
	/// <summary>
	/// Validates an <see cref="AluSimRequest"/> to ensure all ALU inputs are within 4-bit range.
	/// </summary>
	/// <param name="req">The request containing 4-bit ALU input values.</param>
	/// <exception cref="ValidationException">
	/// Thrown when any of <c>A</c>, <c>B</c>, or <c>S</c> is outside the inclusive range 0..15.
	/// </exception>
	public static void ValidateRequest( AluSimRequest req )
	{
		Validate4Bit( req.A, nameof( req.A ) );
		Validate4Bit( req.B, nameof( req.B ) );
		Validate4Bit( req.S, nameof( req.S ) );
	}

	/// <summary>
	/// Decodes a packed byte of ALU output signal levels into a logical <see cref="AluSimResponse"/>.
	/// </summary>
	/// <param name="raw">
	/// Packed output signal levels (not logical values). Bit layout:
	/// <list type="table">
	/// <listheader>
	/// <term>Bit</term>
	/// <description>Signal</description>
	/// </listheader>
	/// <item><term>0..3</term><description><c>F0..F3</c> (function/result nibble)</description></item>
	/// <item><term>4</term><description><c>P</c> (propagate)</description></item>
	/// <item><term>5</term><description><c>G</c> (generate)</description></item>
	/// <item><term>6</term><description><c>AEqualsB</c> (equality)</description></item>
	/// <item><term>7</term><description><c>CN4</c> (carry out)</description></item>
	/// </list>
	/// </param>
	/// <param name="activeLowData">
	/// If <see langword="true"/>, the physical level is active-low (a high level means logical 0, and a low level means logical 1).
	/// If <see langword="false"/>, the physical level matches the logical value.
	/// </param>
	/// <returns>
	/// A decoded response where <c>F</c> is the 4-bit logical value (0..15) and the remaining flags are decoded booleans.
	/// </returns>
	public static AluSimResponse DecodeOutputsRawToLogical( byte raw, bool activeLowData )
	{
		// raw is interpreted as signal levels:
		// F0..F3 (bits 0..3), P (4), G (5), AEqualsB (6), CN4 (7)
		// For ActiveLowData=true, the logical value is inverted from the level.
		static bool ToLogical( bool level, bool ald ) => ald ? !level : level;

		var f0 = ToLogical( ( raw & 0b0000_0001 ) != 0, activeLowData );
		var f1 = ToLogical( ( raw & 0b0000_0010 ) != 0, activeLowData );
		var f2 = ToLogical( ( raw & 0b0000_0100 ) != 0, activeLowData );
		var f3 = ToLogical( ( raw & 0b0000_1000 ) != 0, activeLowData );

		var f = ( f0 ? 1 : 0 )
			| ( f1 ? 2 : 0 )
			| ( f2 ? 4 : 0 )
			| ( f3 ? 8 : 0 );

		var p = ToLogical( ( raw & 0b0001_0000 ) != 0, activeLowData );
		var g = ToLogical( ( raw & 0b0010_0000 ) != 0, activeLowData );
		var aeqb = ToLogical( ( raw & 0b0100_0000 ) != 0, activeLowData );
		var cn4 = ToLogical( ( raw & 0b1000_0000 ) != 0, activeLowData );

		return new AluSimResponse(
			F: f & 0xF,
			P: p,
			G: g,
			CarryOutCn4: cn4,
			AeqB: aeqb );
	}

	/// <summary>
	/// Validates that an integer fits within an unsigned 4-bit value (a single nibble).
	/// </summary>
	/// <param name="value">The value to validate.</param>
	/// <param name="paramName">
	/// The parameter name to include in the exception message (typically produced via <see cref="nameof"/>).
	/// </param>
	/// <exception cref="ValidationException">
	/// Thrown when <paramref name="value"/> is outside the inclusive range 0..15.
	/// </exception>
	private static void Validate4Bit( int value, string paramName )
	{
		if( value is < 0 or > 15 )
			throw new ValidationException( $"{paramName} must be in range 0..15 (4-bit)." );
	}
}
