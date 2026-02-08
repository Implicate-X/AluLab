using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace AluLab.Common.Relay;

/// <summary>
/// SignalR hub for synchronizing ALU/board states between multiple clients.
/// </summary>
/// <remarks>
/// <para> The hub distributes state changes (e.g., pin toggles) to connected clients in real time and
/// maintains snapshots on the server side so that newly joining clients (late joiners) can retrieve the current status. </para>
/// <para> There are two types of states:</para>
/// <list type="bullet">
/// <item><description><b>Inputs</b>: are stored as snapshots in <see cref="s_pinStates"/>. </description></item>
/// <item><description><b>Outputs</b>: are stored as the last reported DTO in <see cref="s_lastOutputs"/> 
/// and distributed separately.</description></item>
/// </list>
/// <para> In addition, a limited event log (<see cref="s_events"/>, max. <see cref="MaxLogEntries"/>) 
/// is maintained to track the most recent changes. </para>
/// <para> Note: The snapshots and the event log are <c>static</c> and therefore global per server process.
/// Scale-out (multiple server instances) would require backplane/shared state for this. </para>
/// </remarks>
public class SyncHub : Hub
{
	/// <summary>
	/// Represents a logged synchronization event (pin change).
	/// </summary>
	/// <param name="TimestampMillis">UTC timestamp in milliseconds since Unix epoch. </param>
	/// <param name="ConnectionId">SignalR-ConnectionId of the triggering client. </param>
	/// <param name="Pin">Name/identifier of the pin. </param>
	/// <param name="State">New (logical) state of the pin. </param>
	public sealed record SyncEvent( long TimestampMillis, string ConnectionId, string Pin, bool State );

	/// <summary>
	/// DTO for the current ALU output state.
	/// </summary>
	/// <param name="Raw">Raw byte of the outputs. </param>
	/// <param name="Binary">Binary representation of the raw byte (string).</param>
	/// <param name="Hex">Hex representation of the raw byte (string).</param>
	public sealed record AluOutputsDto( byte Raw, string Binary, string Hex );

	/// <summary>
	/// Thread-safe, limited event log of the last synchronization events.
	/// </summary>
	private static readonly ConcurrentQueue<SyncEvent> s_events = new();

	/// <summary>
	/// Maximum number of log entries stored in <see cref="s_events"/>.
	/// Older entries are discarded when enqueued.
	/// </summary>
	private const int MaxLogEntries = 1000;

	/// <summary>
	/// Thread-safe snapshot of the last known <b>Input</b> pin states (for late joiners).
	/// </summary>
	private static readonly ConcurrentDictionary<string, bool> s_pinStates = new();

	/// <summary>
	/// Last output state reported by the hardware host (can be <c>null</c> if never reported).
	/// </summary>
	private static AluOutputsDto? s_lastOutputs;

	/// <summary>
	/// Whitelist of pins that are considered <b>inputs</b> and stored in <see cref="s_pinStates"/>.
	/// Output pins are instead synchronized via <see cref="ReportAluOutputs(byte, string, string)"/>.
	/// </summary>
	private static readonly HashSet<string> s_inputPins = new( StringComparer.OrdinalIgnoreCase )
	{
		"A0","A1","A2","A3",
		"B0","B1","B2","B3",
		"S0","S1","S2","S3",
		"CN","M"
	};

	/// <summary>
	/// Adds an event to the server-side event log and ensures that the log is limited to
	/// <see cref="MaxLogEntries"/> entries.
	/// </summary>
	/// <param name="ev">The event to be logged. </param>
	public static void EnqueueEvent( SyncEvent ev )
	{
		s_events.Enqueue( ev );
		while( s_events.Count > MaxLogEntries )
			s_events.TryDequeue( out _ );
	}

	/// <summary>
	/// Returns the most recently logged events in text format (most recent first).
	/// </summary>
	/// <param name="maxEntries">Maximum number of entries to return (default: 100).</param>
	/// <returns>List of formatted log lines.</returns>
	public static IReadOnlyList<string> GetRecent( int maxEntries = 100 )
	{
		var arr = s_events.ToArray();
		if( maxEntries <= 0 ) maxEntries = 100;
		var take = Math.Min( maxEntries, arr.Length );
		return arr.Reverse().Take( take )
		.Select( e => $"{DateTimeOffset.FromUnixTimeMilliseconds( e.TimestampMillis ):O} | {e.ConnectionId} | PinToggled | {e.Pin} => {e.State}" )
		.ToList();
	}

	/// <summary>
	/// Returns the current input snapshot as <see cref="SyncState"/> (for clients that are initially synchronizing).
	/// </summary>
	/// <returns>A <see cref="SyncState"/> with a copy of the current input states.</returns>
	public Task<SyncState> GetState()
	{
		var copy = s_pinStates.ToDictionary( kvp => kvp.Key, kvp => kvp.Value );
		return Task.FromResult( new SyncState( copy ) );
	}

	/// <summary>
	/// Returns a copy of the current input snapshot as a dictionary.
	/// </summary>
	/// <returns>Dictionary with pin &gt; state for input pins. </returns>
	public static Dictionary<string, bool> GetSnapshot() =>
		s_pinStates.ToDictionary( kvp => kvp.Key, kvp => kvp.Value );

	/// <summary>
	/// Returns the last known ALU output state (may be null).
	/// </summary>
	public static AluOutputsDto? GetLastOutputs() => s_lastOutputs;

	/// <summary>
	/// Returns the last known ALU output state (may be null if never reported).
	/// </summary>
	public Task<AluOutputsDto?> GetLastOutputsState() =>
		Task.FromResult( s_lastOutputs );

	/// <summary>
	/// Called by a client when a pin state has changed.
	/// </summary>
	/// <remarks>
	/// The event is logged. If it is an input pin (see <see cref="s_inputPins"/>),
	/// the snapshot in <see cref="s_pinStates"/> is also updated. The change is then forwarded to all
	/// other clients (excluding the sender) via the SignalR event <c>“PinToggled”</c>.
	/// </remarks>
	/// <param name="pin">Pin identifier.</param>
	/// <param name="state">New state.</param>
	public async Task PinToggled( string pin, bool state )
	{
		var id = Context?.ConnectionId ?? "unknown";
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		EnqueueEvent( new SyncEvent( now, id, pin, state ) );

		// Only inputs are saved as snapshots (outputs come via ReportAluOutputs)
		if( s_inputPins.Contains( pin ) )
			s_pinStates[ pin ] = state;

		await Clients.Others.SendAsync( "PinToggled", pin, state );
	}

	/// <summary>
	/// Hardware host reports new ALU outputs; server distributes to all clients.
	/// </summary>
	/// <remarks>
	/// Stores the last reported output state in <see cref="s_lastOutputs"/> and sends the SignalR event
	/// <c>“AluOutputsChanged”</c> to all clients (including sender).
	/// </remarks>
	/// <param name="raw">Raw byte of the outputs.</param>
	/// <param name="binary">Binary representation of the raw byte. </param>
	/// <param name="hex">Hex representation of the raw byte.</param>
	public async Task ReportAluOutputs( byte raw, string binary, string hex )
	{
		var dto = new AluOutputsDto( raw, binary, hex );
		s_lastOutputs = dto;

		await Clients.All.SendAsync( "AluOutputsChanged", dto.Raw, dto.Binary, dto.Hex );
	}
}