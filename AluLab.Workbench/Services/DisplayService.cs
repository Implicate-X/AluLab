using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using AluLab.Board.Platform;
using AluLab.Common.Views;
using Iot.Device.Graphics;
using Iot.Device.Graphics.SkiaSharpAdapter;

namespace AluLab.Workbench.Services;

/// <summary>
/// Provides a service for mirroring the UI display to a hardware board display and handling touch input.
/// </summary>
/// <param name="boardProvider">Provider for accessing the hardware board.</param>
/// <param name="logger">Logger for diagnostic messages.</param>
/// <remarks>
/// <para>
/// The <see cref="DisplayService"/> manages rendering a <see cref="HousingView"/> to a bitmap, sending it to the board's display,
/// and polling for touch input from the board's touch controller. It maps touch coordinates to the UI and dispatches them accordingly.
/// </para>
/// <para>
/// - Uses Avalonia for UI rendering.
/// - Integrates with hardware via <see cref="IBoardProvider"/>.
/// - Handles display updates and touch events at a 10ms interval.
/// - Ensures image factory registration for bitmap conversion.
/// </para>
/// </remarks>
public sealed class DisplayService( IBoardProvider boardProvider, ILogger<DisplayService> logger ) : IDisposable
{
	private const int DisplayWidth = 480;
	private const int DisplayHeight = 320;

	private static bool s_imageFactoryRegistered;

	private readonly IBoardProvider _boardProvider = boardProvider;
	private readonly ILogger<DisplayService> _logger = logger;

	private Window? _window;
	private RenderTargetBitmap? _rtb;
	private DispatcherTimer? _timer;
	private bool _isAttached;

	private bool _wasTouched;
	private int _lastX = -1;
	private int _lastY = -1;

	private HousingView? _housingView;

	/// <summary>
	/// Attaches the service to a window, sets up rendering and touch polling.
	/// </summary>
	/// <param name="window">The Avalonia window to attach to.</param>
	public void Attach( Window window )
	{
		if( _isAttached )
			return;

		_isAttached = true;

		if( !s_imageFactoryRegistered )
		{
			BitmapImage.RegisterImageFactory( new SkiaSharpImageFactory() );
			s_imageFactoryRegistered = true;
		}

		_window = window;
		_window.Closed += ( _, _ ) => Dispose();
		_window.SizeChanged += OnWindowSizeChanged;

		_housingView = _window.FindControl<HousingView>( "HousingControl" ) ?? _window.Content as HousingView;

		if( _housingView is null )
			return;

		_rtb = new RenderTargetBitmap( new PixelSize( DisplayWidth, DisplayHeight ), new Vector( 96, 96 ) );

		_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds( 10 ) };
		_timer.Tick += ( _, _ ) => Tick();
		_timer.Start();

		_logger.LogInformation( "DisplayMirror: attached (render {Width}x{Height}).", DisplayWidth, DisplayHeight );
	}

	/// <summary>
	/// Timer callback for rendering and touch polling.
	/// </summary>
	private void Tick()
	{
		RenderAndSend();
		PollTouchAndDispatchToUi();
	}

	/// <summary>
	/// Renders the <see cref="HousingView"/> and sends the bitmap to the board's display.
	/// </summary>
	private void RenderAndSend()
	{
		if( _housingView is null || _rtb is null )
			return;

		if( !_boardProvider.TryGetBoard( out var board, out _ ) || board is null || !board.IsInitialized )
			return;

		try
		{
			_housingView.Measure( new Size( DisplayWidth, DisplayHeight ) );
			_housingView.Arrange( new Rect( 0, 0, DisplayWidth, DisplayHeight ) );

			_rtb.Render( _housingView );

			using var stream = new MemoryStream();
			_rtb.Save( stream );
			stream.Position = 0;

			var image = BitmapImage.CreateFromStream( stream );

			board.Display.DrawBitmap( image );
			board.Display.SendFrame( true );
		}
		catch( Exception ex )
		{
			_logger.LogWarning( ex, "DisplayMirror: RenderAndSend failed." );
		}
	}

	/// <summary>
	/// Handles window size changes. Currently a placeholder.
	/// </summary>
	private void OnWindowSizeChanged( object? sender, SizeChangedEventArgs e )
	{
	}

	/// <summary>
	/// Polls the board's touch controller and dispatches touch events to the UI.
	/// </summary>
	private void PollTouchAndDispatchToUi()
	{
		if( _window is null )
			return;

		if( !_boardProvider.TryGetBoard( out var board, out _ ) || board is null || !board.IsInitialized )
			return;

		bool isTouched;
		try
		{
			isTouched = board.TouchController.IsTouched();
		}
		catch( Exception ex )
		{
			_logger.LogDebug( ex, "Touch: IsTouched failed." );
			return;
		}

		if( isTouched )
		{
			(int x, int y) pos;
			try
			{
				pos = board.TouchController.GetPosition();
			}
			catch( Exception ex )
			{
				_logger.LogDebug( ex, "Touch: GetPosition failed." );
				return;
			}

			if( pos.x < 0 || pos.y < 0 )
				return;

			_lastX = pos.x;
			_lastY = pos.y;
			_wasTouched = true;
			return;
		}

		if( _wasTouched )
		{
			_wasTouched = false;

			int x = _lastX >= 0 ? _lastX : 0;
			int y = _lastY >= 0 ? _lastY : 0;

			Dispatcher.UIThread.Post( () =>
			{
				if( _window is null )
					return;

				var hv = _window.FindControl<HousingView>( "Root" ) ?? _window.FindControl<HousingView>( "HousingView" );
				hv ??= _window.Content as HousingView;

				if( hv is null )
					return;

				var mapped = MapDisplayToHousingView( hv, x, y );
				hv.ProcessTouch( mapped.X, mapped.Y );
			} );
		}
	}

	/// <summary>
	/// Maps display coordinates to the <see cref="HousingView"/> coordinate space.
	/// </summary>
	/// <param name="hv">The housing view to map to.</param>
	/// <param name="x">X coordinate from the display.</param>
	/// <param name="y">Y coordinate from the display.</param>
	/// <returns>Mapped <see cref="Point"/> in the housing view.</returns>
	private Point MapDisplayToHousingView( HousingView hv, int x, int y )
	{
		if( _window is null )
			return default;

		// Top-left des HousingView relativ zum Window
		var tl = hv.TranslatePoint( new Point( 0, 0 ), _window );
		if( !tl.HasValue )
			return default;

		// Effektive Fläche, in die das HousingView gerendert wird (DIPs)
		double targetWidth = Math.Max( 1.0, hv.Bounds.Width );
		double targetHeight = Math.Max( 1.0, hv.Bounds.Height );

		double mappedX = tl.Value.X + ( x * ( targetWidth / DisplayWidth ) );
		double mappedY = tl.Value.Y + ( y * ( targetHeight / DisplayHeight ) );

		return new Point( mappedX, mappedY );
	}

	/// <summary>
	/// Cleans up resources and detaches from the window.
	/// </summary>
	public void Dispose()
	{
		try { _timer?.Stop(); } catch { }
		_timer = null;

		_rtb?.Dispose();
		_rtb = null;

		_window = null;
		_isAttached = false;

		_wasTouched = false;
		_lastX = -1;
		_lastY = -1;
	}
}