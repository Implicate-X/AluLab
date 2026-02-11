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

public sealed class DisplayMirrorService : IDisposable
{
	private const int DisplayWidth = 480;
	private const int DisplayHeight = 320;

	private static bool s_imageFactoryRegistered;

	private readonly IBoardProvider _boardProvider;
	private readonly ILogger<DisplayMirrorService> _logger;

	private Window? _window;
	private RenderTargetBitmap? _rtb;
	private DispatcherTimer? _timer;
	private bool _isAttached;

	private bool _wasTouched;
	private int _lastX = -1;
	private int _lastY = -1;

	public DisplayMirrorService( IBoardProvider boardProvider, ILogger<DisplayMirrorService> logger )
	{
		_boardProvider = boardProvider;
		_logger = logger;
	}

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

		_rtb = new RenderTargetBitmap( new PixelSize( DisplayWidth, DisplayHeight ), new Vector( 96, 96 ) );

		_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds( 10 ) };
		_timer.Tick += ( _, _ ) => Tick();
		_timer.Start();

		_logger.LogInformation( "DisplayMirror: attached (render {Width}x{Height}).", DisplayWidth, DisplayHeight );
	}

	private void Tick()
	{
		RenderAndSend();
		PollTouchAndDispatchToUi();
	}

	private void RenderAndSend()
	{
		if( _window is null || _rtb is null )
			return;

		if( !_boardProvider.TryGetBoard( out var board, out _ ) || board is null || !board.IsInitialized )
			return;

		try
		{
			var hv = _window.FindControl<HousingView>( "Root" ) ?? _window.Content as HousingView;
			var renderRoot = (Control?)hv ?? _window;

			_rtb.Render( renderRoot );

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