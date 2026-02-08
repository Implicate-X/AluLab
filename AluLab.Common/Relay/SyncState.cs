using System.Collections.Generic;

namespace AluLab.Common.Relay;

/// <summary>
/// Represents the synchronization state of pins, mapping pin identifiers to their synchronization status.
/// </summary>
/// <remarks>This record is immutable and is intended to capture the current state of pin synchronization at a
/// specific point in time. The mapping associates each pin's unique identifier with a Boolean value indicating whether
/// the pin is synchronized (<see langword="true"/>) or not (<see langword="false"/>).</remarks>
public sealed record SyncState
{
	/// <summary>
	/// Gets the collection of pin states, indexed by pin name.
	/// </summary>
	public Dictionary<string, bool> Pins { get; init; }

	/// <summary>
	/// Initializes a new instance of the SyncState class with an empty set of synchronization states.
	/// </summary>
	public SyncState() : this( new Dictionary<string, bool>() ) { }

	/// <summary>
	/// Initializes a new instance of the SyncState class with the specified pin states.
	/// </summary>
	/// <param name="pins">A dictionary that maps pin names to their synchronization states. Each key is the name of a pin, and the
	/// corresponding value indicates whether the pin is synchronized (<see langword="true"/>) or not (<see
	/// langword="false"/>). If null, an empty dictionary is used.</param>
	public SyncState( Dictionary<string, bool> pins ) => Pins = pins ?? new Dictionary<string, bool>();
}