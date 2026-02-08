using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace AluLab.Common.Relay;

/// <summary>
/// Provides a client for connecting to a SignalR hub and synchronizing pin state changes in real time.
/// </summary>
/// <remarks>The SyncClient class manages the connection lifecycle and event handling for a SignalR hub that
/// broadcasts pin toggle events. It exposes events for connection state changes and remote pin updates, and provides
/// methods to start, stop, and dispose of the connection. This class is not thread-safe; callers should ensure that its
/// methods are not called concurrently from multiple threads.</remarks>
public sealed class SyncClient : IAsyncDisposable
{
	private readonly HubConnection _conn;
	private readonly ILogger? _logger;

	/// <summary>
	/// Occurs when the remote pin state is toggled.
	/// </summary>
	/// <remarks>Subscribers receive the identifier of the remote pin and a value indicating the new state. The
	/// event is typically raised when a user or system action changes the pin state remotely.</remarks>
	public event Action<string, bool>? RemotePinToggled;
	public event Action<Exception?>? ConnectionClosed;
	public event Action? Reconnecting;
	public event Action? Reconnected;

	/// <summary>
	/// Initializes a new instance of the SyncClient class and establishes a connection to the specified SignalR hub.
	/// </summary>
	/// <remarks>The constructor sets up automatic reconnection and subscribes to connection lifecycle events,
	/// allowing the client to respond to connection changes and remote pin toggle events. Event handlers such as
	/// Reconnecting, Reconnected, and ConnectionClosed can be used to monitor connection status.</remarks>
	/// <param name="hubUrl">The URL of the SignalR hub to connect to. Cannot be null or empty.</param>
	/// <param name="logger">An optional logger used to record connection events and errors. If null, no logging is performed.</param>
	public SyncClient( string hubUrl, ILogger? logger = null )
	{
		_logger = logger;
		_conn = new HubConnectionBuilder()
			.WithUrl( hubUrl )
			.WithAutomaticReconnect()
			.Build();

		_conn.On<string, bool>( "PinToggled", ( pin, state ) => RemotePinToggled?.Invoke( pin, state ) );

		_conn.Reconnecting += ex =>
		{
			_logger?.LogWarning( ex, "SignalR reconnecting: {Message}", ex?.Message );
			Reconnecting?.Invoke();
			return Task.CompletedTask;
		};

		_conn.Reconnected += id =>
		{
			_logger?.LogInformation( "SignalR reconnected. ConnectionId={Id}", id );
			Reconnected?.Invoke();
			return Task.CompletedTask;
		};

		_conn.Closed += ex =>
		{
			_logger?.LogWarning( ex, "SignalR closed: {Message}", ex?.Message );
			ConnectionClosed?.Invoke( ex );
			return Task.CompletedTask;
		};
	}

	/// <summary>
	/// Asynchronously initiates the connection process.
	/// </summary>
	/// <returns>A task that represents the asynchronous start operation.</returns>
	public Task StartAsync() => _conn.StartAsync();

	/// <summary>
	/// Asynchronously stops the current connection and releases associated resources.
	/// </summary>
	/// <returns>A task that represents the asynchronous stop operation.</returns>
	public Task StopAsync() => _conn.StopAsync();

	/// <summary>
	/// Asynchronously notifies the server that the specified pin has been toggled to a new state.
	/// </summary>
	/// <param name="pin">The identifier of the pin that was toggled. Cannot be null or empty.</param>
	/// <param name="state">The new state of the pin. Set to <see langword="true"/> if the pin is enabled; otherwise, <see langword="false"/>.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	public Task SendPinToggledAsync( string pin, bool state ) => _conn.InvokeAsync( "PinToggled", pin, state );

	/// <summary>
	/// Asynchronously reports the ALU outputs to the server.
	/// </summary>
	/// <param name="raw">The raw byte value of the ALU outputs.</param>
	/// <param name="binary">The binary string representation of the ALU outputs.</param>
	/// <param name="hex">The hexadecimal string representation of the ALU outputs.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	public Task ReportAluOutputsAsync( byte raw, string binary, string hex ) =>
		_conn.InvokeAsync( "ReportAluOutputs", raw, binary, hex );

	/// <summary>
	/// Asynchronously releases the unmanaged resources used by the object.
	/// </summary>
	/// <returns>A ValueTask that represents the asynchronous dispose operation.</returns>
	public ValueTask DisposeAsync() => _conn.DisposeAsync();
}