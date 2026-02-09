using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace AluLab.Common.Relay;

public class SyncHub : Hub
{
	public sealed record SyncEvent( long TimestampMillis, string ConnectionId, string Pin, bool State );
	public sealed record AluOutputsDto( byte Raw, string Binary, string Hex );

	private static readonly ConcurrentQueue<SyncEvent> s_events = new();
	private const int MaxLogEntries = 1000;

	private static readonly ConcurrentDictionary<string, bool> s_pinStates = new();
	private static AluOutputsDto? s_lastOutputs;

	private static readonly HashSet<string> s_inputPins = new( StringComparer.OrdinalIgnoreCase )
	{
		"A0","A1","A2","A3",
		"B0","B1","B2","B3",
		"S0","S1","S2","S3",
		"CN","M"
	};

	public static void EnqueueEvent( SyncEvent ev )
	{
		s_events.Enqueue( ev );
		while( s_events.Count > MaxLogEntries )
			s_events.TryDequeue( out _ );
	}

	public static IReadOnlyList<string> GetRecent( int maxEntries = 100 )
	{
		var arr = s_events.ToArray();
		if( maxEntries <= 0 ) maxEntries = 100;
		var take = Math.Min( maxEntries, arr.Length );
		return arr.Reverse().Take( take )
			.Select( e => $"{DateTimeOffset.FromUnixTimeMilliseconds( e.TimestampMillis ):O} | {e.ConnectionId} | PinToggled | {e.Pin} => {e.State}" )
			.ToList();
	}

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

	public static Dictionary<string, bool> GetSnapshot() =>
		s_pinStates.ToDictionary( kvp => kvp.Key, kvp => kvp.Value );

	public static AluOutputsDto? GetLastOutputs() => s_lastOutputs;

	public Task<AluOutputsDto?> GetLastOutputsState()
	{
		try { return Task.FromResult( s_lastOutputs ); }
		catch { return Task.FromResult<AluOutputsDto?>( null ); }
	}

	public async Task PinToggled( string pin, bool state )
	{
		var id = Context?.ConnectionId ?? "unknown";
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		EnqueueEvent( new SyncEvent( now, id, pin, state ) );

		if( s_inputPins.Contains( pin ) )
			s_pinStates[ pin ] = state;

		await Clients.Others.SendAsync( "PinToggled", pin, state );
	}

	public async Task ReportAluOutputs( byte raw, string binary, string hex )
	{
		var dto = new AluOutputsDto( raw, binary, hex );
		s_lastOutputs = dto;

		await Clients.All.SendAsync( "AluOutputsChanged", dto.Raw, dto.Binary, dto.Hex );
	}

	public override async Task OnConnectedAsync()
	{
		await base.OnConnectedAsync();

		var id = Context?.ConnectionId ?? "unknown";
		EnqueueEvent( new SyncEvent( DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), id, "OnConnected", true ) );

		// Kein Snapshot hier.
	}

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

	public Task TestClientEvent()
		=> Clients.Caller.SendAsync( "TestClientEvent", "ok" );
}