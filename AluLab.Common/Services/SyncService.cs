using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using AluLab.Common.Relay;
using System.Collections.Generic;

namespace AluLab.Common.Services
{
	public sealed partial class SyncService : IAsyncDisposable
	{
		private readonly HubConnection _connection;
		private readonly SemaphoreSlim _connectGate = new( 1, 1 );
		private int _initialSyncDone;

		private readonly string _readyToken = Guid.NewGuid().ToString( "N" );

		public bool IsConnected => _connection?.State == HubConnectionState.Connected;

		public event Action<string, bool>? PinToggled;
		public event Action<byte, string, string>? AluOutputsChanged;
		public event Action<string>? Log;

		public event Action<SyncState>? SnapshotStateReceived;
		public event Action<SyncHub.AluOutputsDto?>? SnapshotOutputsReceived;

		public SyncService( string url = "https://iot.homelabs.one/sync" )
		{
			_connection = new HubConnectionBuilder()
				.WithUrl( url )
				.WithAutomaticReconnect()
				.Build();

			_connection.Reconnecting += error => Task.CompletedTask;

			_connection.Reconnected += connectionId =>
			{
				Interlocked.Exchange( ref _initialSyncDone, 0 );
				_ = TryRunInitialSyncAsync();
				return Task.CompletedTask;
			};

			_connection.Closed += async error =>
			{
				await Task.Delay( TimeSpan.FromSeconds( 5 ) );
				try { await _connection.StartAsync(); } catch { }
			};

			_connection.On<string, bool>( "PinToggled", ( pin, state ) => PinToggled?.Invoke( pin, state ) );
			_connection.On<byte, string, string>( "AluOutputsChanged", ( raw, binary, hex ) => AluOutputsChanged?.Invoke( raw, binary, hex ) );

			_connection.On<Dictionary<string, bool>>( "SnapshotPins", pins =>
			{
				Log?.Invoke( $"Sync: SnapshotPins received: pins={pins?.Count ?? -1}" );
				if( pins is null )
					return;

				SnapshotStateReceived?.Invoke( new SyncState( pins ) );
			} );

			_connection.On<SyncHub.AluOutputsDto?>( "SnapshotOutputs", dto =>
			{
				Log?.Invoke( $"Sync: SnapshotOutputs received: {( dto is null ? "null" : $"raw=0x{dto.Hex}" )}" );
				SnapshotOutputsReceived?.Invoke( dto );
			} );
		}

		public async Task EnsureConnectedAsync()
		{
			if( IsConnected )
			{
				await TryRunInitialSyncAsync().ConfigureAwait( false );
				return;
			}

			await _connectGate.WaitAsync().ConfigureAwait( false );
			try
			{
				if( IsConnected )
					return;

				await _connection.StartAsync().ConfigureAwait( false );

				// Wichtig: erst jetzt den "Ready"-Handshake senden
				Log?.Invoke( $"Sync: Sending ClientReady token (len={_readyToken.Length})" );
				await _connection.SendAsync( "ClientReady", _readyToken ).ConfigureAwait( false );
			}
			finally
			{
				_connectGate.Release();
			}

			await TryRunInitialSyncAsync().ConfigureAwait( false );
		}

		private Task TryRunInitialSyncAsync()
		{
			if( Interlocked.Exchange( ref _initialSyncDone, 1 ) == 1 )
				return Task.CompletedTask;

			Log?.Invoke( "Sync: Initial snapshot wird durch Server nach ClientReady gepusht." );
			return Task.CompletedTask;
		}

		// --------------------------------------------------------------------
		// Backward-compatible API (für HousingView.axaml.cs)
		// --------------------------------------------------------------------

		public IDisposable Subscribe<T1, T2>( string methodName, Action<T1, T2> handler )
			=> _connection.On( methodName, handler );

		public IDisposable Subscribe<T1, T2, T3>( string methodName, Action<T1, T2, T3> handler )
			=> _connection.On( methodName, handler );

		public Task SendPinToggledAsync( string pin, bool state )
			=> _connection.SendAsync( "PinToggled", pin, state );

		public Task<SyncState> GetStateAsync()
			=> _connection.InvokeAsync<SyncState>( "GetState" );

		public Task<SyncHub.AluOutputsDto?> GetLastOutputsAsync()
			=> _connection.InvokeAsync<SyncHub.AluOutputsDto?>( "GetLastOutputsState" );

		public async ValueTask DisposeAsync()
		{
			await _connection.DisposeAsync().ConfigureAwait( false );
			_connectGate.Dispose();
		}
	}
}
