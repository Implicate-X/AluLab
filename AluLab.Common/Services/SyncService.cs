using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using AluLab.Common.Relay;

namespace AluLab.Common.Services
{
	/// <summary>
	/// Encapsulates a SignalR client connection to the sync hub and provides a simple API for
	/// connect, send, and subscribe to hub events.
	/// </summary>
	/// <remarks>
	/// The class manages a <see cref="HubConnection"/> including automatic reconnection.
	/// In addition, a <see cref="SemaphoreSlim"/> ensures that parallel connection attempts
	/// (e.g., due to competing calls) are serialized.
	/// </remarks>
	public sealed partial class SyncService : IAsyncDisposable
	{
		/// <summary>
		/// The underlying SignalR hub connection.
		/// </summary>
		private readonly HubConnection _connection;

		/// <summary>
		/// Gate for serializing connection establishment to avoid concurrent <see cref="EnsureConnectedAsync"/> calls.
		/// </summary>
		private readonly SemaphoreSlim _connectGate = new( 1, 1 );

		/// <summary>
		/// Indicates whether the hub connection is currently in the <see cref="HubConnectionState.Connected"/> state.
		/// </summary>
		public bool IsConnected => _connection?.State == HubConnectionState.Connected;

		/// <summary>
		/// Initializes the service and configures the SignalR connection.
		/// </summary>
		/// <param name="url">The URL of the sync hub. Default: <c>https://iot.homelabs.one/sync</c>.</param>
		/// <remarks>
		/// Enables automatic reconnection (<c>WithAutomaticReconnect</c>) and attempts to restart after a
		/// <c>Closed</c> event.
		/// </remarks>
		public SyncService( string url = "https://iot.homelabs.one/sync" )
		{
			_connection = new HubConnectionBuilder()
			.WithUrl( url )
			.WithAutomaticReconnect()
			.Build();

			_connection.Reconnecting += error => Task.CompletedTask;
			_connection.Reconnected += connectionId => Task.CompletedTask;

			_connection.Closed += async error =>
			{
				await Task.Delay( TimeSpan.FromSeconds( 5 ) );
				try { await _connection.StartAsync(); } catch { }
			};
		}

		/// <summary>
		/// Starts the hub connection.
		/// </summary>
		/// <returns>A task that completes when the connection has been started. </returns>
		public Task StartAsync() => _connection.StartAsync();

		/// <summary>
		/// Stops the hub connection.
		/// </summary>
		/// <returns>A task that completes when the connection has been stopped.</returns>
		public Task StopAsync() => _connection.StopAsync();

		/// <summary>
		/// Ensures that the hub connection is established.
		/// </summary>
		/// <remarks>
		/// If already connected, returns immediately. Otherwise, the connection establishment is serialized
		/// via <see cref="_connectGate"/> to prevent redundant start attempts.
		/// </remarks>
		public async Task EnsureConnectedAsync()
		{
			if( IsConnected )
				return;

			await _connectGate.WaitAsync().ConfigureAwait( false );
			try
			{
				if( IsConnected )
					return;

				await _connection.StartAsync().ConfigureAwait( false );
			}
			finally
			{
				_connectGate.Release();
			}
		}

		/// <summary>
		/// Sends a message to the hub (fire-and-forget, no return value from the hub).
		/// </summary>
		/// <param name="methodName">Name of the hub method.
		/// <param name="arg">Optional argument passed to the hub method.
		/// <returns>A task representing the send operation. </returns>
		public Task SendAsync( string methodName, object? arg = null ) =>
			_connection.SendAsync( methodName, arg );

		/// <summary>
		/// Subscribes to a hub event with one parameter.
		/// </summary>
		/// <typeparam name="T">Type of the event payload.
		/// <param name="methodName">Name of the hub event or client method.
		/// <param name="handler">Handler that is called when the event is received.
		/// <returns>
		/// An <see cref="IDisposable"/> that must be disposed of to unsubscribe/deactivate the handler.
		/// </returns>
		public IDisposable Subscribe<T>( string methodName, Action<T> handler ) =>
			_connection.On( methodName, handler );

		/// <summary>
		/// Subscribes to a hub event with two parameters.
		/// </summary>
		/// <typeparam name="T1">Type of the first parameter.
		/// <typeparam name="T2">Type of the second parameter.</typeparam>
		/// <param name="methodName">Name of the hub event or client method.</param>
		/// <param name="handler">Handler that is called when the event is received.</param>
		/// <returns>
		/// An <see cref="IDisposable"/> that must be disposed of to unsubscribe/deactivate the handler.
		/// </returns>
		public IDisposable Subscribe<T1, T2>( string methodName, Action<T1, T2> handler ) =>
			_connection.On<T1, T2>( methodName, handler );

		/// <summary>
		/// Subscribes to a hub event with three parameters.
		/// </summary>
		/// <typeparam name="T1">Type of the first parameter.
		/// <typeparam name="T2">Type of the second parameter.
		/// <typeparam name="T3">Type of the third parameter.
		/// <param name="methodName">Name of the hub event or client method.</param>
		/// <param name="handler">Handler that is called when the event is received.</param>
		/// <returns>
		/// An <see cref="IDisposable"/> that must be disposed of to unsubscribe/deactivate the handler.
		/// </returns>
		public IDisposable Subscribe<T1, T2, T3>( string methodName, Action<T1, T2, T3> handler ) =>
			_connection.On<T1, T2, T3>( methodName, handler );

		/// <summary>
		/// Subscribes to a hub event that delivers a <see cref="string"/>.
		/// </summary>
		/// <param name="methodName">Name of the hub event or client method.</param>
		/// <param name="handler">Handler that is called when the event is received.
		/// <returns>
		/// An <see cref="IDisposable"/> that must be disposed of to unsubscribe/deactivate the handler.
		/// </returns>
		public IDisposable Subscribe( string methodName, Action<string> handler ) =>
			_connection.On( methodName, handler );

		/// <summary>
		/// Notifies the hub that a pin has been switched.
		/// </summary>
		/// <param name="pin">Pin identifier.
		/// <param name="state">New state.
		/// <returns>A task representing the hub invoke.
		public Task SendPinToggledAsync( string pin, bool state ) =>
			_connection.InvokeAsync( "PinToggled", pin, state );

		/// <summary>
		/// Asynchronously releases the SignalR connection and cleans up internal resources.
		/// </summary>
		public async ValueTask DisposeAsync()
		{
			if( _connection is not null )
			{
				await _connection.DisposeAsync();
			}

			_connectGate.Dispose();
		}

		/// <summary>
		/// Retrieves the current sync state from the hub.
		/// </summary>
		/// <returns>The current <see cref="SyncState"/>.
		public Task<SyncState> GetStateAsync() =>
			_connection.InvokeAsync<SyncState>( "GetState" );

		/// <summary>
		/// Retrieves the last known output state from the hub.
		/// </summary>
		/// <returns>A DTO with output information or <c>null</c> if not available.</returns>
		public Task<SyncHub.AluOutputsDto?> GetLastOutputsAsync() =>
			_connection.InvokeAsync<SyncHub.AluOutputsDto?>( "GetLastOutputsState" );
	}
}
