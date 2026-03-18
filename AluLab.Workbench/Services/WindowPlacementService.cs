using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AluLab.Workbench.Services;

public sealed class WindowPlacementService
{
	private readonly string _filePath;

	public WindowPlacementService()
	{
		var dir = Path.Combine(
			Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ),
			"AluLab",
			"Workbench" );

		Directory.CreateDirectory( dir );
		_filePath = Path.Combine( dir, "window-placement.json" );
	}

	public void Attach( Window window )
	{
		Restore( window );

		// Speichern, wenn das Fenster wirklich geschlossen wird.
		window.Closing += ( _, _ ) => Save( window );

		// Optional zusätzlich beim Verschieben/Resizen speichern (debounced).
		var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds( 350 ) };
		debounce.Tick += ( _, _ ) =>
		{
			debounce.Stop();
			Save( window );
		};

		window.PositionChanged += ( _, _ ) => RestartDebounce( debounce );
		window.Resized += ( _, _ ) => RestartDebounce( debounce );
	}

	private static void RestartDebounce( DispatcherTimer timer )
	{
		timer.Stop();
		timer.Start();
	}

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

			// Nicht wiederherstellen, wenn maximiert/minimiert – sonst “kämpft” das mit dem WM.
			window.WindowState = WindowState.Normal;

			window.Position = new PixelPoint( data.X, data.Y );
			window.Width = Math.Max( 200, data.Width );
			window.Height = Math.Max( 200, data.Height );
		}
		catch
		{
			// Best-effort: keine Startup-Crashes wegen kaputter Settings.
		}
	}

	private void Save( Window window )
	{
		try
		{
			// Bei Maximized verwenden wir die Normal-Bounds, damit Restore sinnvoll ist.
			var pos = window.Position;
			var w = (int)Math.Round( window.Width );
			var h = (int)Math.Round( window.Height );

			if( window.WindowState == WindowState.Maximized )
			{
				var nb = window.Bounds; // falls NormalBounds verfügbar wären, wäre das besser; best-effort hier.
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

	private sealed record Data( int X, int Y, int Width, int Height );
}