using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;

namespace AluLab.Common.Services;

/// <summary>
/// Lightweight, thread-safe persistence layer for synchronization state shared across app components.
///
/// <para>
/// This store maintains:
/// <list type="bullet">
/// <item><description>A case-insensitive map of pin names to boolean states.</description></item>
/// <item><description>The last raw output byte observed/sent (<see cref="LastOutputsRaw"/>).</description></item>
/// </list>
/// </para>
///
/// <para>
/// State is persisted to disk as JSON under <c>App_Data/syncstate.json</c> rooted at
/// <see cref="IHostEnvironment.ContentRootPath"/>. Writes are debounced to coalesce rapid updates:
/// each call to a mutating method schedules a save after a short delay, canceling any pending save.
/// </para>
///
/// <para>
/// The implementation uses an atomic write pattern (<c>.tmp</c> file + move) to reduce the chance of
/// partially-written files.
/// </para>
/// </summary>
public sealed class SyncStateStore : IAsyncDisposable
{
	/// <summary>
	/// Synchronizes access to in-memory state and save scheduling.
	/// </summary>
	private readonly Lock _gate = new();

	/// <summary>
	/// Full path to the JSON persistence file (<c>.../App_Data/syncstate.json</c>).
	/// </summary>
	private readonly string _filePath;

	/// <summary>
	/// Debounce interval used to coalesce rapid updates into a single save.
	/// </summary>
	private readonly TimeSpan _debounce;

	/// <summary>
	/// In-memory pin state (case-insensitive keys).
	/// </summary>
	private Dictionary<string, bool> _pins = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Last raw outputs byte, if known.
	/// </summary>
	private byte? _lastOutputsRaw;

	/// <summary>
	/// Cancellation source for the currently scheduled save (if any).
	/// </summary>
	private CancellationTokenSource? _saveCts;

	/// <summary>
	/// Task for the currently scheduled/in-flight save (if any).
	/// </summary>
	private Task? _saveTask;

	/// <summary>
	/// Creates a new store that persists to <c>App_Data/syncstate.json</c> under the host content root.
	/// </summary>
	/// <param name="env">Provides the content root path used to compute the persistence location.</param>
	public SyncStateStore( IHostEnvironment env )
	{
		_filePath = Path.Combine( env.ContentRootPath, "App_Data", "syncstate.json" );
		_debounce = TimeSpan.FromMilliseconds( 250 );
	}

	/// <summary>
	/// Returns a point-in-time snapshot of the current state.
	/// </summary>
	/// <remarks>
	/// The returned dictionary is a copy and can be safely mutated by the caller.
	/// </remarks>
	/// <returns>
	/// A tuple containing a copied <c>Pins</c> dictionary and the current <c>LastOutputsRaw</c> value.
	/// </returns>
	public (Dictionary<string, bool> Pins, byte? LastOutputsRaw) GetSnapshot()
	{
		lock( _gate )
			return (new Dictionary<string, bool>( _pins, StringComparer.OrdinalIgnoreCase ), _lastOutputsRaw);
	}

	/// <summary>
	/// Sets the state of a named pin and schedules a debounced save to disk.
	/// </summary>
	/// <param name="pin">Pin identifier (treated case-insensitively).</param>
	/// <param name="state">The desired boolean state.</param>
	public void SetPin( string pin, bool state )
	{
		lock( _gate )
			_pins[ pin ] = state;

		RequestSave();
	}

	/// <summary>
	/// Sets the last raw outputs value and schedules a debounced save to disk.
	/// </summary>
	/// <param name="raw">Raw output byte.</param>
	public void SetLastOutputsRaw( byte raw )
	{
		lock( _gate )
			_lastOutputsRaw = raw;

		RequestSave();
	}

	/// <summary>
	/// Loads persisted state from disk, replacing any in-memory state currently held.
	/// </summary>
	/// <param name="ct">Cancellation token for the read/deserialize operation.</param>
	/// <remarks>
	/// If the file does not exist, this method is a no-op. If deserialization yields <see langword="null"/>,
	/// the method also becomes a no-op.
	/// </remarks>
	public async Task LoadAsync( CancellationToken ct = default )
	{
		if( !File.Exists( _filePath ) )
			return;

		await using var stream = File.OpenRead( _filePath );
		var state = await JsonSerializer.DeserializeAsync<PersistedSyncState>( stream, cancellationToken: ct );

		if( state is null )
			return;

		lock( _gate )
		{
			_pins = state.Pins is null
				? new Dictionary<string, bool>( StringComparer.OrdinalIgnoreCase )
				: new Dictionary<string, bool>( state.Pins, StringComparer.OrdinalIgnoreCase );

			_lastOutputsRaw = state.LastOutputsRaw;
		}
	}

	/// <summary>
	/// Creates an immutable snapshot suitable for serialization without holding the lock while writing to disk.
	/// </summary>
	private PersistedSyncState SnapshotForSave()
	{
		lock( _gate )
		{
			return new PersistedSyncState
			{
				Pins = new Dictionary<string, bool>( _pins, StringComparer.OrdinalIgnoreCase ),
				LastOutputsRaw = _lastOutputsRaw,
			};
		}
	}

	/// <summary>
	/// Schedules a debounced save by canceling any prior scheduled save and creating a new one.
	/// </summary>
	/// <remarks>
	/// This method is intentionally fire-and-forget; completion is observed during <see cref="DisposeAsync"/>.
	/// </remarks>
	private void RequestSave()
	{
		CancellationTokenSource? oldCts;

		lock( _gate )
		{
			oldCts = _saveCts;
			_saveCts = new CancellationTokenSource();
			_saveTask = DebouncedSaveAsync( _saveCts.Token );
		}

		oldCts?.Cancel();
		oldCts?.Dispose();
	}

	/// <summary>
	/// Performs the debounced save: waits for the debounce interval, then writes the snapshot to disk as JSON.
	/// </summary>
	/// <param name="ct">Cancellation token used to cancel the pending save when new updates arrive.</param>
	/// <remarks>
	/// Uses a temporary file and an overwrite move to reduce the risk of a partially written final file.
	/// Operation cancellation is expected during rapid updates and is swallowed.
	/// </remarks>
	private async Task DebouncedSaveAsync( CancellationToken ct )
	{
		try
		{
			await Task.Delay( _debounce, ct );

			var state = SnapshotForSave();

			Directory.CreateDirectory( Path.GetDirectoryName( _filePath )! );

			string tmp = _filePath + ".tmp";
			await using( var stream = File.Create( tmp ) )
			{
				await JsonSerializer.SerializeAsync( stream, state, cancellationToken: ct );
			}

			File.Move( tmp, _filePath, overwrite: true );
		}
		catch( OperationCanceledException )
		{
			// expected on rapid updates
		}
	}

	/// <summary>
	/// Cancels any pending debounced save and awaits the in-flight save task (if present) before releasing resources.
	/// </summary>
	/// <remarks>
	/// Ensures no background save continues after disposal. Canceled saves are expected and ignored.
	/// </remarks>
	public async ValueTask DisposeAsync()
	{
		Task? pending;

		lock( _gate )
		{
			_saveCts?.Cancel();
			pending = _saveTask;
		}

		if( pending is not null )
		{
			try { await pending; } catch( OperationCanceledException ) { }
		}

		lock( _gate )
		{
			_saveCts?.Dispose();
			_saveCts = null;
			_saveTask = null;
		}
	}

	/// <summary>
	/// JSON-serializable representation of the persisted sync state.
	/// </summary>
	private sealed record PersistedSyncState
	{
		/// <summary>
		/// Persisted pin states (case-insensitive at runtime; serialized as provided).
		/// </summary>
		public Dictionary<string, bool>? Pins { get; init; }

		/// <summary>
		/// Persisted last raw outputs value.
		/// </summary>
		public byte? LastOutputsRaw { get; init; }
	}
}