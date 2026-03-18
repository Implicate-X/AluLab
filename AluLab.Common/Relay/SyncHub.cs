using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AluLab.Common.Services;
using Microsoft.AspNetCore.SignalR;

namespace AluLab.Common.Relay;

/// <summary>
/// SignalR hub used to synchronize ALU/relay UI state between connected clients.
/// </summary>
/// <remarks>
/// <para>
/// The hub maintains a server-side snapshot of pin states and the latest ALU output byte via
/// <see cref="SyncStateStore"/>. Clients can push changes (e.g. pin toggles) and receive updates from
/// other clients in near real-time.
/// </para>
/// <para>
/// A small in-memory event log is kept (static, process-local) to aid debugging and inspection.
/// This log is bounded to <see cref="MaxLogEntries"/> entries.
/// </para>
/// <para>
/// Client-facing SignalR method names used by this hub:
/// <list type="bullet">
/// <item><description><c>PinToggled</c> (broadcast to others)</description></item>
/// <item><description><c>AluOutputsChanged</c> (broadcast to all)</description></item>
/// <item><description><c>SnapshotPins</c> (sent to caller)</description></item>
/// <item><description><c>SnapshotOutputsRaw</c> (sent to caller)</description></item>
/// <item><description><c>TestClientEvent</c> (sent to caller)</description></item>
/// </list>
/// </para>
/// </remarks>
public class SyncHub : Hub
{
	/// <summary>
	/// A single event record stored in the in-memory hub log.
	/// </summary>
	/// <param name="TimestampMillis">UTC Unix timestamp in milliseconds.</param>
	/// <param name="ConnectionId">SignalR connection id that produced the event.</param>
	/// <param name="Pin">Pin name (or a pseudo pin such as <c>OnConnected</c>).</param>
	/// <param name="State">Associated state value (for pin toggles).</param>
	public sealed record SyncEvent( long TimestampMillis, string ConnectionId, string Pin, bool State );

	/// <summary>
	/// DTO for exposing the latest ALU outputs in multiple representations.
	/// </summary>
	/// <param name="Raw">Raw output byte.</param>
	/// <param name="Binary">Binary output representation (8-bit, left padded with 0s).</param>
	/// <param name="Hex">Hex output representation (2 characters).</param>
	public sealed record AluOutputsDto( byte Raw, string Binary, string Hex );

	/// <summary>
	/// Process-wide bounded event queue used for recent activity inspection.
	/// </summary>
	private static readonly ConcurrentQueue<SyncEvent> s_events = new();

	/// <summary>
	/// Maximum number of entries retained in <see cref="s_events"/>.
	/// </summary>
	private const int MaxLogEntries = 1000;

	private readonly SyncStateStore _store;

	/// <summary>
	/// Creates a new hub instance using the provided shared state store.
	/// </summary>
	/// <param name="store">Shared state store holding pin snapshot and last ALU output byte.</param>
	public SyncHub( SyncStateStore store ) => _store = store;

	/// <summary>
	/// Pins treated as "inputs" for snapshot purposes. Only these pins are persisted into the store
	/// when received via <see cref="PinToggled(string, bool)"/>.
	/// </summary>
	private static readonly HashSet<string> s_inputPins = new( StringComparer.OrdinalIgnoreCase )
	{
		"A0","A1","A2","A3",
		"B0","B1","B2","B3",
		"S0","S1","S2","S3",
		"CN","M"
	};

	/// <summary>
	/// Adds an event to the in-memory log and trims older entries to enforce the max size.
	/// </summary>
	/// <param name="ev">Event to enqueue.</param>
	public static void EnqueueEvent( SyncEvent ev )
	{
		s_events.Enqueue( ev );
		while( s_events.Count > MaxLogEntries )
			s_events.TryDequeue( out _ );
	}

	/// <summary>
	/// Returns a human-readable list of the most recent hub events (newest first).
	/// </summary>
	/// <param name="maxEntries">
	/// Maximum number of entries to return. Values &lt;= 0 default to 100.
	/// </param>
	/// <returns>Formatted event lines suitable for diagnostics/log display.</returns>
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
	/// Returns the current pin snapshot from the shared store.
	/// </summary>
	/// <returns>A <see cref="SyncState"/> containing the latest known pin states.</returns>
	public Task<SyncState> GetState()
	{
		var (pins, _) = _store.GetSnapshot();
		return Task.FromResult( new SyncState( pins ) );
	}

	/// <summary>
	/// Gets the last reported ALU output byte from the shared store, if available.
	/// </summary>
	/// <returns>
	/// An <see cref="AluOutputsDto"/> containing raw/binary/hex forms of the last output,
	/// or <see langword="null"/> if no outputs have been reported yet.
	/// </returns>
	public Task<AluOutputsDto?> GetLastOutputsState()
	{
		var (_, raw) = _store.GetSnapshot();
		if( raw is null )
			return Task.FromResult<AluOutputsDto?>( null );

		var dto = new AluOutputsDto(
			raw.Value,
			Convert.ToString( raw.Value, 2 ).PadLeft( 8, '0' ),
			raw.Value.ToString( "X2" ) );

		return Task.FromResult<AluOutputsDto?>( dto );
	}

	/// <summary>
	/// Called by a client to report that a pin was toggled.
	/// </summary>
	/// <remarks>
	/// The event is logged. If <paramref name="pin"/> is in <see cref="s_inputPins"/>, the new state is
	/// persisted into <see cref="SyncStateStore"/>. The change is then broadcast to all other clients
	/// via the <c>PinToggled</c> SignalR message.
	/// </remarks>
	/// <param name="pin">Pin identifier (e.g. <c>A0</c>, <c>S3</c>, <c>CN</c>).</param>
	/// <param name="state">New pin state.</param>
	public async Task PinToggled( string pin, bool state )
	{
		var id = Context?.ConnectionId ?? "unknown";
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		EnqueueEvent( new SyncEvent( now, id, pin, state ) );

		if( s_inputPins.Contains( pin ) )
			_store.SetPin( pin, state );

		await Clients.Others.SendAsync( "PinToggled", pin, state );
	}

	/// <summary>
	/// Called by a client to report the current ALU outputs.
	/// </summary>
	/// <remarks>
	/// The raw byte is persisted into <see cref="SyncStateStore"/>, and the change is broadcast to all
	/// clients via the <c>AluOutputsChanged</c> message.
	/// </remarks>
	/// <param name="raw">Raw output byte.</param>
	/// <param name="binary">Binary representation provided by the client.</param>
	/// <param name="hex">Hex representation provided by the client.</param>
	public async Task ReportAluOutputs( byte raw, string binary, string hex )
	{
		_store.SetLastOutputsRaw( raw );
		await Clients.All.SendAsync( "AluOutputsChanged", raw, binary, hex );
	}

	/// <summary>
	/// SignalR lifecycle hook invoked when a new connection is established.
	/// </summary>
	/// <remarks>
	/// The connection is logged as an event for diagnostics.
	/// </remarks>
	public override async Task OnConnectedAsync()
	{
		await base.OnConnectedAsync();

		var id = Context?.ConnectionId ?? "unknown";
		EnqueueEvent( new SyncEvent( DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), id, "OnConnected", true ) );
	}

	/// <summary>
	/// Called by a client to indicate it is ready to receive the current snapshot.
	/// </summary>
	/// <remarks>
	/// The <paramref name="token"/> parameter is currently not validated or used; it is accepted
	/// to support future authentication/handshake needs.
	/// The caller receives <c>SnapshotPins</c> and <c>SnapshotOutputsRaw</c> messages.
	/// </remarks>
	/// <param name="token">Client-provided token (reserved for future use).</param>
	public async Task ClientReady( string token )
	{
		var id = Context?.ConnectionId ?? "unknown";
		EnqueueEvent( new SyncEvent( DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), id, "ClientReady", true ) );

		var (pins, raw) = _store.GetSnapshot();

		await Clients.Caller.SendAsync( "SnapshotPins", pins );
		await Clients.Caller.SendAsync( "SnapshotOutputsRaw", raw );

		EnqueueEvent( new SyncEvent( DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), id, "ClientReadySent", true ) );
	}

	/// <summary>
	/// Called by a client to request a fresh snapshot of the current server state.
	/// </summary>
	/// <remarks>
	/// The caller receives <c>SnapshotPins</c> and <c>SnapshotOutputsRaw</c> messages.
	/// </remarks>
	public async Task RequestSnapshot()
	{
		var id = Context?.ConnectionId ?? "unknown";
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		EnqueueEvent( new SyncEvent( now, id, "RequestSnapshot", true ) );

		var (pins, raw) = _store.GetSnapshot();

		await Clients.Caller.SendAsync( "SnapshotPins", pins );
		await Clients.Caller.SendAsync( "SnapshotOutputsRaw", raw );

		EnqueueEvent( new SyncEvent( DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), id, "RequestSnapshotSent", true ) );
	}

	/// <summary>
	/// Sends a simple test message to the caller to validate connectivity and client handlers.
	/// </summary>
	public Task TestClientEvent()
		=> Clients.Caller.SendAsync( "TestClientEvent", "ok" );
}