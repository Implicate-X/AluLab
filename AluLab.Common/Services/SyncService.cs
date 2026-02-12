using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using AluLab.Common.Relay;
using System.Collections.Generic;

namespace AluLab.Common.Services
{
	/// <summary>
	/// Provides a service for synchronizing state and events with a remote SignalR hub.
	/// Handles connection management, event subscriptions, and state snapshot retrieval for ALU lab devices.
	/// </summary>
	public sealed partial class SyncService : IAsyncDisposable
	{
		/// <summary>
		/// The underlying SignalR hub connection.
		/// </summary>
		private readonly HubConnection _connection;

		/// <summary>
		/// Semaphore to ensure thread-safe connection attempts.
		/// </summary>
		private readonly SemaphoreSlim _connectGate = new( 1, 1 );

		/// <summary>
		/// Indicates whether the initial synchronization has been performed (0 = not done, 1 = done).
		/// </summary>
		private int _initialSyncDone;

		/// <summary>
		/// Unique token sent to the server to indicate client readiness.
		/// </summary>
		private readonly string _readyToken = Guid.NewGuid().ToString( "N" );

		/// <summary>
		/// Gets a value indicating whether the service is currently connected to the SignalR hub.
		/// </summary>
		public bool IsConnected => _connection?.State == HubConnectionState.Connected;

		/// <summary>
		/// Event triggered when a pin is toggled.
		/// </summary>
		public event Action<string, bool>? PinToggled;

		/// <summary>
		/// Event triggered when ALU outputs change.
		/// </summary>
		public event Action<byte, string, string>? AluOutputsChanged;

		/// <summary>
		/// Event for logging messages.
		/// </summary>
		public event Action<string>? Log;

		/// <summary>
		/// Event triggered when a snapshot of the pin state is received.
		/// </summary>
		public event Action<SyncState>? SnapshotStateReceived;

		/// <summary>
		/// Event triggered when a snapshot of the ALU outputs is received.
		/// </summary>
		public event Action<SyncHub.AluOutputsDto?>? SnapshotOutputsReceived;

		/// <summary>
		/// Initializes a new instance of the <see cref="SyncService"/> class and sets up SignalR event handlers.
		/// </summary>
		/// <param name="url">The URL of the SignalR hub to connect to.</param>
		public SyncService( string url = "https://iot.homelabs.one/sync" )
		{
			_connection = new HubConnectionBuilder()
				.WithUrl( url )
				.WithAutomaticReconnect()
				.Build();

			// Handle reconnection logic
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

			// Register handlers for incoming hub events
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

			_connection.On<byte?>( "SnapshotOutputsRaw", raw =>
			{
				Log?.Invoke( $"Sync: SnapshotOutputsRaw received: {( raw is null ? "null" : $"0x{raw.Value:X2}" )}" );

				// Reuse legacy event (HousingView can already do dto->raw)
				var dto = raw is null
					? null
					: new SyncHub.AluOutputsDto( raw.Value, Convert.ToString( raw.Value, 2 ).PadLeft( 8, '0' ), raw.Value.ToString( "X2" ) );

				SnapshotOutputsReceived?.Invoke( dto );
			} );
		}

		/// <summary>
		/// Ensures the service is connected to the SignalR hub and performs initial synchronization if needed.
		/// </summary>
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

				Log?.Invoke( $"Sync: Sending ClientReady token (len={_readyToken.Length})" );
				await _connection.SendAsync( "ClientReady", _readyToken ).ConfigureAwait( false );
			}
			finally
			{
				_connectGate.Release();
			}

			await TryRunInitialSyncAsync().ConfigureAwait( false );
		}

		/// <summary>
		/// Attempts to perform the initial synchronization with the server.
		/// Ensures this is only done once per connection.
		/// </summary>
		private Task TryRunInitialSyncAsync()
		{
			if( Interlocked.Exchange( ref _initialSyncDone, 1 ) == 1 )
				return Task.CompletedTask;

			Log?.Invoke( "Sync: Warte auf SnapshotPins nach ClientReady." );
			return Task.CompletedTask;
		}

		// -----------------------------------------------------------------------
		// Backward-compatible API (legacy; do not use for start snapshot in WASM)
		// -----------------------------------------------------------------------

		/// <summary>
		/// Subscribes to a SignalR hub method with two parameters.
		/// </summary>
		/// <typeparam name="T1">Type of the first parameter.</typeparam>
		/// <typeparam name="T2">Type of the second parameter.</typeparam>
		/// <param name="methodName">The name of the hub method.</param>
		/// <param name="handler">The handler to invoke when the method is called.</param>
		/// <returns>An <see cref="IDisposable"/> to unsubscribe.</returns>
		public IDisposable Subscribe<T1, T2>( string methodName, Action<T1, T2> handler )
			=> _connection.On( methodName, handler );

		/// <summary>
		/// Subscribes to a SignalR hub method with three parameters.
		/// </summary>
		/// <typeparam name="T1">Type of the first parameter.</typeparam>
		/// <typeparam name="T2">Type of the second parameter.</typeparam>
		/// <typeparam name="T3">Type of the third parameter.</typeparam>
		/// <param name="methodName">The name of the hub method.</param>
		/// <param name="handler">The handler to invoke when the method is called.</param>
		/// <returns>An <see cref="IDisposable"/> to unsubscribe.</returns>
		public IDisposable Subscribe<T1, T2, T3>( string methodName, Action<T1, T2, T3> handler )
			=> _connection.On( methodName, handler );

		/// <summary>
		/// Sends a pin toggle event to the server.
		/// </summary>
		/// <param name="pin">The pin identifier.</param>
		/// <param name="state">The new state of the pin.</param>
		public Task SendPinToggledAsync( string pin, bool state )
			=> _connection.SendAsync( "PinToggled", pin, state );

		/// <summary>
		/// Requests the current state snapshot from the server.
		/// </summary>
		/// <returns>A <see cref="SyncState"/> representing the current state.</returns>
		public Task<SyncState> GetStateAsync()
			=> _connection.InvokeAsync<SyncState>( "GetState" );

		/// <summary>
		/// Requests the last known ALU outputs from the server.
		/// </summary>
		/// <returns>An <see cref="SyncHub.AluOutputsDto"/> representing the last outputs, or null if unavailable.</returns>
		public Task<SyncHub.AluOutputsDto?> GetLastOutputsAsync()
			=> _connection.InvokeAsync<SyncHub.AluOutputsDto?>( "GetLastOutputsState" );

		/// <summary>
		/// Disposes the service and releases all resources.
		/// </summary>
		public async ValueTask DisposeAsync()
		{
			await _connection.DisposeAsync().ConfigureAwait( false );
			_connectGate.Dispose();
		}
	}
}
