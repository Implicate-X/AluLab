using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Iot.Device.Graphics;
using Iot.Device.Graphics.SkiaSharpAdapter;
using AluLab.Board.Platform;
using AluLab.Common.Views;

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
	/// Attaches the specified window to the display service, initializing rendering and event handling for the window.
	/// </summary>
	/// <remarks>If the window is already attached, this method has no effect. The method ensures that the image
	/// factory is registered before initializing rendering. When the attached window is closed, resources are
	/// automatically disposed.</remarks>
	/// <param name="window">The window to attach. This parameter must not be null and will be used for rendering output and managing window
	/// events.</param>
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
	/// Performs a single update cycle for the display service, including rendering and processing user interface input.
	/// </summary>
	/// <remarks>Call this method at regular intervals to ensure the display remains up to date and responsive to
	/// user interactions. This method is typically invoked by a timer or main application loop.</remarks>
	private void Tick()
	{
		RenderAndSend();
		PollTouchAndDispatchToUi();
	}

	/// <summary>
	/// Renders the housing view and transmits the resulting bitmap to the display board if all required components are
	/// initialized and available.
	/// </summary>
	/// <remarks>This method measures and arranges the housing view before rendering it to a bitmap and sending it
	/// to the display board. If the housing view, render target, or display board is not properly initialized, the method
	/// exits without performing any action. Any exceptions encountered during rendering or transmission are caught and
	/// logged as warnings. This method should be called only when the display infrastructure is fully
	/// initialized.</remarks>
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
	/// Polls the touch controller for touch events and dispatches corresponding actions to the UI thread when a touch is
	/// detected or released.
	/// </summary>
	/// <remarks>This method checks for touch input from the board's touch controller and, upon detecting a touch
	/// release, posts a UI update to process the touch event at the last known position. If the touch controller is not
	/// initialized or an error occurs while polling for touch status or position, the method logs the error and exits
	/// without further action. This method is intended to be called periodically to ensure timely UI updates in response
	/// to user touch interactions.</remarks>
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
	/// Maps the specified display coordinates to the corresponding point within the given HousingView.
	/// </summary>
	/// <remarks>This method calculates the position of the HousingView relative to the window and adjusts the
	/// provided display coordinates based on the effective area of the HousingView. If the window or the translation point
	/// is unavailable, the method returns the default Point.</remarks>
	/// <param name="hv">The HousingView instance that defines the target area for coordinate mapping. Cannot be null.</param>
	/// <param name="x">The x-coordinate in display space to be mapped to the HousingView.</param>
	/// <param name="y">The y-coordinate in display space to be mapped to the HousingView.</param>
	/// <returns>A Point representing the mapped coordinates within the HousingView. Returns the default Point if the mapping cannot
	/// be performed.</returns>
	private Point MapDisplayToHousingView( HousingView hv, int x, int y )
	{
		if( _window is null )
			return default;

		// Top left of the HousingView relative to the window
		var tl = hv.TranslatePoint( new Point( 0, 0 ), _window );
		if( !tl.HasValue )
			return default;

		// Effective area into which HousingView is rendered (DIPs)
		double targetWidth = Math.Max( 1.0, hv.Bounds.Width );
		double targetHeight = Math.Max( 1.0, hv.Bounds.Height );

		double mappedX = tl.Value.X + ( x * ( targetWidth / DisplayWidth ) );
		double mappedY = tl.Value.Y + ( y * ( targetHeight / DisplayHeight ) );

		return new Point( mappedX, mappedY );
	}

	/// <summary>
	/// Releases all resources used by the current instance of the class.
	/// </summary>
	/// <remarks>Call this method when the instance is no longer needed to free unmanaged resources and perform
	/// other cleanup operations. After calling this method, the instance should not be used.</remarks>
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