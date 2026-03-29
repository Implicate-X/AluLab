namespace AluLab.Server.Api;

/// <summary>
/// Request model for simulating a 4-bit 74181-style ALU.
/// A and B are 4-bit operands (0..15). S is the 4-bit function select (0..15).
/// </summary>
public sealed record AluSimRequest(
	int A,
	int B,
	int S,
	bool ModeM,
	bool CarryInCn,
	bool ActiveLowData = false );

/// <summary>
/// Response model containing the simulated ALU outputs.
/// </summary>
public sealed record AluSimResponse(
	int F,
	bool P,
	bool G,
	bool CarryOutCn4,
	bool AeqB );
