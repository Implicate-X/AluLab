using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AluLab.Common.Relay;

/// <summary>
/// Provides a high-level interface for synchronizing pin state changes with a remote IoT hub using asynchronous
/// operations.
/// </summary>
/// <remarks>SyncAgent manages the connection to the remote synchronization hub and exposes events and methods for
/// sending and receiving pin state changes. This class is intended for use in IoT scenarios where device pin states
/// need to be kept in sync across multiple clients. SyncAgent is not thread-safe; callers should ensure that its
/// methods are not accessed concurrently from multiple threads.</remarks>
public sealed class SyncAgent : IAsyncDisposable
{
	private readonly SyncClient _client;

	/// <summary>
	/// Occurs when the state of a remote pin is toggled.
	/// </summary>
	/// <remarks>Subscribe to this event to be notified when a remote pin changes its state. The event provides the
	/// pin identifier and the new state as parameters. Event handlers are invoked on the thread that raises the event;
	/// ensure thread safety when updating UI elements or shared resources.</remarks>
	public event Action<string, bool>? RemotePinToggled
	{
		add => _client.RemotePinToggled += value;
		remove => _client.RemotePinToggled -= value;
	}

	/// <summary>
	/// Initializes a new instance of the SyncAgent class with the specified synchronization hub URL and optional logger.
	/// </summary>
	/// <param name="hubUrl">The URL of the synchronization hub to connect to. If not specified, defaults to "https://iot.homelabs.one/sync".</param>
	/// <param name="logger">An optional logger used to record diagnostic and operational messages. May be null if logging is not required.</param>
	public SyncAgent( string hubUrl = "https://iot.homelabs.one/sync", ILogger? logger = null )
	{
		_client = new SyncClient( hubUrl, logger );
	}

	/// <summary>
	/// Starts the client asynchronously.
	/// </summary>
	/// <returns>A task that represents the asynchronous start operation.</returns>
	public Task StartAsync() => _client.StartAsync();

	/// <summary>
	/// Asynchronously stops the client and releases any associated resources.
	/// </summary>
	/// <returns>A task that represents the asynchronous stop operation.</returns>
	public Task StopAsync() => _client.StopAsync();
	
	/// <summary>
	/// Asynchronously sends a command to toggle the specified pin to the given state.
	/// </summary>
	/// <param name="pin">The identifier of the pin to be toggled. Cannot be null or empty.</param>
	/// <param name="state">The desired state of the pin. Set to <see langword="true"/> to turn the pin on; otherwise, <see langword="false"/>
	/// to turn it off.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	public Task SendPinToggledAsync( string pin, bool state ) => _client.SendPinToggledAsync( pin, state );

	/// <summary>
	/// Asynchronously releases the unmanaged resources used by the client and optionally releases the managed resources.
	/// </summary>
	/// <remarks>Call this method when you are finished using the client to ensure that all resources are released
	/// properly. After calling this method, the client instance should not be used.</remarks>
	/// <returns>A task that represents the asynchronous dispose operation.</returns>
	public ValueTask DisposeAsync() => _client.DisposeAsync();
}
