using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AluLab.Common.Controls;

public sealed class OverlineText : Control
{
	public static readonly StyledProperty<string?> TextProperty =
		AvaloniaProperty.Register<OverlineText, string?>( nameof( Text ) );

	public static readonly StyledProperty<int> OverlineStartProperty =
		AvaloniaProperty.Register<OverlineText, int>( nameof( OverlineStart ), -1 );

	public static readonly StyledProperty<int> OverlineLengthProperty =
		AvaloniaProperty.Register<OverlineText, int>( nameof( OverlineLength ), 0 );

	public static readonly StyledProperty<IBrush?> ForegroundProperty =
		AvaloniaProperty.Register<OverlineText, IBrush?>( nameof( Foreground ), Brushes.Black );

	public static readonly StyledProperty<FontFamily> FontFamilyProperty =
		AvaloniaProperty.Register<OverlineText, FontFamily>( nameof( FontFamily ), new FontFamily( "Consolas" ) );

	public static readonly StyledProperty<double> FontSizeProperty =
		AvaloniaProperty.Register<OverlineText, double>( nameof( FontSize ), 14 );

	public static readonly StyledProperty<FontStyle> FontStyleProperty =
		AvaloniaProperty.Register<OverlineText, FontStyle>( nameof( FontStyle ), FontStyle.Normal );

	public static readonly StyledProperty<FontWeight> FontWeightProperty =
		AvaloniaProperty.Register<OverlineText, FontWeight>( nameof( FontWeight ), FontWeight.Normal );

	public static readonly StyledProperty<IBrush?> OverlineBrushProperty =
		AvaloniaProperty.Register<OverlineText, IBrush?>( nameof( OverlineBrush ), null );

	public static readonly StyledProperty<double> OverlineThicknessProperty =
		AvaloniaProperty.Register<OverlineText, double>( nameof( OverlineThickness ), 1 );

	public static readonly StyledProperty<double> OverlineOffsetProperty =
		AvaloniaProperty.Register<OverlineText, double>( nameof( OverlineOffset ), 2 );

	public string? Text
	{
		get => GetValue( TextProperty );
		set => SetValue( TextProperty, value );
	}

	public int OverlineStart
	{
		get => GetValue( OverlineStartProperty );
		set => SetValue( OverlineStartProperty, value );
	}

	public int OverlineLength
	{
		get => GetValue( OverlineLengthProperty );
		set => SetValue( OverlineLengthProperty, value );
	}

	public IBrush? Foreground
	{
		get => GetValue( ForegroundProperty );
		set => SetValue( ForegroundProperty, value );
	}

	public FontFamily FontFamily
	{
		get => GetValue( FontFamilyProperty );
		set => SetValue( FontFamilyProperty, value );
	}

	public double FontSize
	{
		get => GetValue( FontSizeProperty );
		set => SetValue( FontSizeProperty, value );
	}

	public FontStyle FontStyle
	{
		get => GetValue( FontStyleProperty );
		set => SetValue( FontStyleProperty, value );
	}

	public FontWeight FontWeight
	{
		get => GetValue( FontWeightProperty );
		set => SetValue( FontWeightProperty, value );
	}

	public IBrush? OverlineBrush
	{
		get => GetValue( OverlineBrushProperty );
		set => SetValue( OverlineBrushProperty, value );
	}

	public double OverlineThickness
	{
		get => GetValue( OverlineThicknessProperty );
		set => SetValue( OverlineThicknessProperty, value );
	}

	public double OverlineOffset
	{
		get => GetValue( OverlineOffsetProperty );
		set => SetValue( OverlineOffsetProperty, value );
	}

	static OverlineText()
	{
		AffectsMeasure<OverlineText>(
			TextProperty,
			FontFamilyProperty,
			FontSizeProperty,
			FontWeightProperty,
			FontStyleProperty );

		AffectsRender<OverlineText>(
			TextProperty,
			ForegroundProperty,
			FontFamilyProperty,
			FontSizeProperty,
			FontWeightProperty,
			FontStyleProperty,
			OverlineStartProperty,
			OverlineLengthProperty,
			OverlineBrushProperty,
			OverlineThicknessProperty,
			OverlineOffsetProperty );
	}

	private FormattedText CreateFormattedText( string text ) =>
		new(
			text,
			CultureInfo.CurrentUICulture,
			FlowDirection.LeftToRight,
			new Typeface( FontFamily, FontStyle, FontWeight ),
			FontSize,
			Foreground ?? Brushes.Black );

	protected override Size MeasureOverride( Size availableSize )
	{
		var text = Text ?? string.Empty;
		if( text.Length == 0 )
			return default;

		var ft = CreateFormattedText( text );

		// Keep the returned size within the available bounds (layout will clip if needed).
		var w = Math.Min( ft.Width, availableSize.Width );
		var h = Math.Min( ft.Height, availableSize.Height );
		return new Size( w, h );
	}

	public override void Render( DrawingContext context )
	{
		base.Render( context );

		var text = Text ?? string.Empty;
		if( text.Length == 0 )
			return;

		var ft = CreateFormattedText( text );
		context.DrawText( ft, new Point( 0, 0 ) );

		if( OverlineStart < 0 || OverlineLength <= 0 )
			return;

		var start = Math.Clamp( OverlineStart, 0, text.Length );
		var end = Math.Clamp( OverlineStart + OverlineLength, start, text.Length );
		var len = end - start;
		if( len <= 0 )
			return;

		// Get actual glyph-range bounds from the text layout instead of a monospace approximation.
		var geometry = ft.BuildHighlightGeometry( new Point( 0, 0 ), start, len );
		if( geometry is null )
			return;

		var bounds = geometry.Bounds;
		var x1 = bounds.X;
		var x2 = bounds.Right;

		// Overline position relative to the real laid-out text box.
		var y = bounds.Y + OverlineOffset;

		var penBrush = OverlineBrush ?? Foreground ?? Brushes.Black;
		var pen = new Pen( penBrush, OverlineThickness );

		// Align to pixel grid for less blur on some renderers.
		if( OverlineThickness > 0 )
			y = Math.Round( y ) + ( OverlineThickness % 2 ) * 0.5;

		context.DrawLine( pen, new Point( x1, y ), new Point( x2, y ) );
	}
}