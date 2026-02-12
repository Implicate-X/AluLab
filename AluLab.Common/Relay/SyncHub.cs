using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace AluLab.Common.Relay;

/// <summary>
/// SignalR hub for synchronizing pin states and ALU outputs between clients in real-time.
/// Maintains a log of recent pin toggle events, tracks the current state of input pins,
/// and broadcasts changes to connected clients.
/// </summary>
public class SyncHub : Hub
{
	/// <summary>
	/// Represents a synchronization event, such as a pin toggle or client action.
	/// </summary>
	/// <param name="TimestampMillis">The event timestamp in milliseconds since Unix epoch.</param>
	/// <param name="ConnectionId">The SignalR connection ID of the client.</param>
	/// <param name="Pin">The pin identifier or event type.</param>
	/// <param name="State">The state associated with the event (e.g., pin high/low).</param>
	public sealed record SyncEvent( long TimestampMillis, string ConnectionId, string Pin, bool State );

	/// <summary>
	/// Data transfer object representing ALU output values in multiple formats.
	/// </summary>
	/// <param name="Raw">Raw byte value of the ALU output.</param>
	/// <param name="Binary">Binary string representation of the output.</param>
	/// <param name="Hex">Hexadecimal string representation of the output.</param>
	public sealed record AluOutputsDto( byte Raw, string Binary, string Hex );

	private static readonly ConcurrentQueue<SyncEvent> s_events = new();
	private const int MaxLogEntries = 1000;

	private static readonly ConcurrentDictionary<string, bool> s_pinStates = new();
	private static AluOutputsDto? s_lastOutputs;

	/// <summary>
	/// Set of recognized input pin names (case-insensitive).
	/// </summary>
	private static readonly HashSet<string> s_inputPins = new( StringComparer.OrdinalIgnoreCase )
	{
		"A0","A1","A2","A3",
		"B0","B1","B2","B3",
		"S0","S1","S2","S3",
		"CN","M"
	};

	/// <summary>
	/// Adds a synchronization event to the event log, maintaining a maximum number of entries.
	/// </summary>
	/// <param name="ev">The event to enqueue.</param>
	public static void EnqueueEvent( SyncEvent ev )
	{
		s_events.Enqueue( ev );
		while( s_events.Count > MaxLogEntries )
			s_events.TryDequeue( out _ );
	}

	/// <summary>
	/// Retrieves a list of recent synchronization events as formatted strings.
	/// </summary>
	/// <param name="maxEntries">Maximum number of entries to return (default: 100).</param>
	/// <returns>List of event descriptions, most recent first.</returns>
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
	/// Asynchronously retrieves the current state of all tracked pins.
	/// </summary>
	/// <returns>A <see cref="SyncState"/> containing the pin states.</returns>
	public Task<SyncState> GetState()
	{
		try
		{
			var copy = s_pinStates.ToDictionary( kvp => kvp.Key, kvp => kvp.Value );
			return Task.FromResult( new SyncState( copy ) );
		}
		catch
		{
			return Task.FromResult( new SyncState() );
		}
	}

	/// <summary>
	/// Returns a snapshot of the current pin states.
	/// </summary>
	/// <returns>Dictionary mapping pin names to their states.</returns>
	public static Dictionary<string, bool> GetSnapshot() =>
		s_pinStates.ToDictionary( kvp => kvp.Key, kvp => kvp.Value );

	/// <summary>
	/// Gets the most recently reported ALU outputs, if available.
	/// </summary>
	/// <returns>The last <see cref="AluOutputsDto"/> or null.</returns>
	public static AluOutputsDto? GetLastOutputs() => s_lastOutputs;

	/// <summary>
	/// Asynchronously retrieves the most recent ALU outputs.
	/// </summary>
	/// <returns>The last <see cref="AluOutputsDto"/> or null.</returns>
	public Task<AluOutputsDto?> GetLastOutputsState()
	{
		try { return Task.FromResult( s_lastOutputs ); }
		catch { return Task.FromResult<AluOutputsDto?>( null ); }
	}

	/// <summary>
	/// Handles a pin toggle event from a client, updates state, and notifies other clients.
	/// </summary>
	/// <param name="pin">The pin identifier.</param>
	/// <param name="state">The new state of the pin.</param>
	public async Task PinToggled( string pin, bool state )
	{
		var id = Context?.ConnectionId ?? "unknown";
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		EnqueueEvent( new SyncEvent( now, id, pin, state ) );

		if( s_inputPins.Contains( pin ) )
			s_pinStates[ pin ] = state;

		await Clients.Others.SendAsync( "PinToggled", pin, state );
	}

	/// <summary>
	/// Reports new ALU output values and broadcasts them to all clients.
	/// </summary>
	/// <param name="raw">Raw byte value.</param>
	/// <param name="binary">Binary string representation.</param>
	/// <param name="hex">Hexadecimal string representation.</param>
	public async Task ReportAluOutputs( byte raw, string binary, string hex )
	{
		var dto = new AluOutputsDto( raw, binary, hex );
		s_lastOutputs = dto;

		await Clients.All.SendAsync( "AluOutputsChanged", dto.Raw, dto.Binary, dto.Hex );
	}

	/// <summary>
	/// Called when a client connects. Logs the connection event.
	/// </summary>
	public override async Task OnConnectedAsync()
	{
		await base.OnConnectedAsync();

		var id = Context?.ConnectionId ?? "unknown";
		EnqueueEvent( new SyncEvent( DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), id, "OnConnected", true ) );

		// Kein Snapshot hier.
	}

	/// <summary>
	/// Handles client readiness, sending the current pin and ALU output states to the caller.
	/// </summary>
	/// <param name="token">Client authentication or session token.</param>
	public async Task ClientReady( string token )
	{
		var id = Context?.ConnectionId ?? "unknown";
		EnqueueEvent( new SyncEvent( DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), id, "ClientReady", true ) );

		var copy = s_pinStates.ToDictionary( kvp => kvp.Key, kvp => kvp.Value );

		await Clients.Caller.SendAsync( "SnapshotPins", copy );

		byte? raw = s_lastOutputs?.Raw;
		await Clients.Caller.SendAsync( "SnapshotOutputsRaw", raw );

		EnqueueEvent( new SyncEvent( DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), id, "ClientReadySent", true ) );
	}

	/// <summary>
	/// Handles a snapshot request from a client, sending current pin and ALU output states.
	/// </summary>
	public async Task RequestSnapshot()
	{
		var id = Context?.ConnectionId ?? "unknown";
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		EnqueueEvent( new SyncEvent( now, id, "RequestSnapshot", true ) );

		var copy = s_pinStates.ToDictionary( kvp => kvp.Key, kvp => kvp.Value );

		await Clients.Caller.SendAsync( "SnapshotPins", copy );

		byte? raw = s_lastOutputs?.Raw;
		await Clients.Caller.SendAsync( "SnapshotOutputsRaw", raw );

		EnqueueEvent( new SyncEvent( DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), id, "RequestSnapshotSent", true ) );
	}

	/// <summary>
	/// Sends a test event to the calling client for connectivity or diagnostics.
	/// </summary>
	public Task TestClientEvent()
		=> Clients.Caller.SendAsync( "TestClientEvent", "ok" );
}