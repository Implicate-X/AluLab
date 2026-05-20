using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AluLab.Workbench.Services;

/// <summary>
/// Persists and restores an Avalonia <see cref="Window"/>'s position and size between application runs.
/// </summary>
/// <remarks>
/// <para>
/// The placement is stored as JSON under the current user's local application data folder:
/// <c>%LOCALAPPDATA%\AluLab\Workbench\window-placement.json</c>.
/// </para>
/// <para>
/// This service is intentionally <em>best-effort</em>: all I/O and deserialization errors are swallowed
/// to avoid startup/shutdown crashes due to corrupt or inaccessible settings.
/// </para>
/// <para>
/// To reduce disk writes while the user drags/resizes the window, saves triggered by move/resize are
/// debounced using a <see cref="DispatcherTimer"/>.
/// </para>
/// </remarks>
public sealed class WindowPlacementService
{
	/// <summary>
	/// Full path to the persisted placement file (JSON).
	/// </summary>
	private readonly string _filePath;

	/// <summary>
	/// Initializes the service and ensures the placement storage directory exists.
	/// </summary>
	public WindowPlacementService()
	{
		var dir = Path.Combine(
			Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ),
			"AluLab",
			"Workbench" );

		Directory.CreateDirectory( dir );
		_filePath = Path.Combine( dir, "window-placement.json" );
	}

	/// <summary>
	/// Attaches placement persistence to the provided <paramref name="window"/>.
	/// </summary>
	/// <remarks>
	/// On attach, the service attempts to restore the last persisted placement.
	/// Afterwards it saves placement:
	/// <list type="bullet">
	/// <item><description>when the window is closing, and</description></item>
	/// <item><description>after the user moves or resizes the window (debounced).</description></item>
	/// </list>
	/// </remarks>
	/// <param name="window">The window whose placement should be persisted.</param>
	public void Attach( Window window )
	{
		Restore( window );

		// Save when the window is actually closed.
		window.Closing += ( _, _ ) => Save( window );

		// Optionally, also save when moving/resizing (debounced).
		var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds( 350 ) };
		debounce.Tick += ( _, _ ) =>
		{
			debounce.Stop();
			Save( window );
		};

		window.PositionChanged += ( _, _ ) => RestartDebounce( debounce );
		window.Resized += ( _, _ ) => RestartDebounce( debounce );
	}

	/// <summary>
	/// Restarts a debounce timer by stopping and starting it.
	/// </summary>
	/// <param name="timer">The timer used to debounce frequent events (move/resize).</param>
	private static void RestartDebounce( DispatcherTimer timer )
	{
		timer.Stop();
		timer.Start();
	}

	/// <summary>
	/// Attempts to restore the window placement from the JSON file.
	/// </summary>
	/// <remarks>
	/// If the file does not exist or cannot be read/parsed, the method does nothing.
	/// The method forces <see cref="Window.WindowState"/> to <see cref="WindowState.Normal"/> before applying
	/// position and size to avoid fighting the window manager when the previously persisted state was
	/// maximized/minimized.
	/// </remarks>
	/// <param name="window">The window to apply the stored placement to.</param>
	private void Restore( Window window )
	{
		try
		{
			if( !File.Exists( _filePath ) )
				return;

			var json = File.ReadAllText( _filePath );
			var data = JsonSerializer.Deserialize<Data>( json );
			if( data is null )
				return;

			// Do not restore if maximized/minimized – otherwise it will “conflict” with the WM.
			window.WindowState = WindowState.Normal;

			window.Position = new PixelPoint( data.X, data.Y );
			window.Width = Math.Max( 200, data.Width );
			window.Height = Math.Max( 200, data.Height );
		}
		catch
		{
			// Best-effort: No startup crashes due to broken settings.
		}
	}

	/// <summary>
	/// Persists the current window placement to the JSON file.
	/// </summary>
	/// <remarks>
	/// When the window is maximized, the method attempts to save bounds that represent a sensible "normal"
	/// restore size rather than the maximized size. This is implemented as best-effort using
	/// <see cref="Window.Bounds"/>.
	/// </remarks>
	/// <param name="window">The window whose current placement should be saved.</param>
	private void Save( Window window )
	{
		try
		{
			// At Maximized, we use the normal bounds so that Restore makes sense.
			var pos = window.Position;
			var w = (int)Math.Round( window.Width );
			var h = (int)Math.Round( window.Height );

			if( window.WindowState == WindowState.Maximized )
			{
				// If NormalBounds were available, that would be better; best-effort here.
				var nb = window.Bounds;
				w = (int)Math.Round( nb.Width );
				h = (int)Math.Round( nb.Height );
			}

			var data = new Data( pos.X, pos.Y, w, h );

			var json = JsonSerializer.Serialize( data, new JsonSerializerOptions { WriteIndented = true } );
			File.WriteAllText( _filePath, json );
		}
		catch
		{
			// Best-effort.
		}
	}

	/// <summary>
	/// Serializable DTO for persisted window placement.
	/// </summary>
	/// <param name="X">Window X position in pixels.</param>
	/// <param name="Y">Window Y position in pixels.</param>
	/// <param name="Width">Window width in pixels.</param>
	/// <param name="Height">Window height in pixels.</param>
	private sealed record Data( int X, int Y, int Width, int Height );
}