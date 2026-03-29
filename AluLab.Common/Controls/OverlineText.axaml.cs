using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AluLab.Common.Controls;

/// <summary>
/// Avalonia <see cref="Control"/> that renders a single line of text and can draw an overline
/// (a horizontal line above a specified character range).
/// </summary>
/// <remarks>
/// <para>
/// The overline position/width is derived from the actual text layout by using
/// <see cref="FormattedText.BuildHighlightGeometry(Avalonia.Point, int, int)"/>. This avoids
/// approximating character widths (even when using a monospace font).
/// </para>
/// <para>
/// Setting <see cref="OverlineStart"/> to a negative value (default) or <see cref="OverlineLength"/>
/// to zero disables the overline.
/// </para>
/// </remarks>
public sealed class OverlineText : Control
{
	/// <summary>
	/// Styled property backing <see cref="Text"/>.
	/// </summary>
	public static readonly StyledProperty<string?> TextProperty =
		AvaloniaProperty.Register<OverlineText, string?>( nameof( Text ) );

	/// <summary>
	/// Styled property backing <see cref="OverlineStart"/>.
	/// </summary>
	/// <remarks>Default is <c>-1</c> (disabled).</remarks>
	public static readonly StyledProperty<int> OverlineStartProperty =
		AvaloniaProperty.Register<OverlineText, int>( nameof( OverlineStart ), -1 );

	/// <summary>
	/// Styled property backing <see cref="OverlineLength"/>.
	/// </summary>
	public static readonly StyledProperty<int> OverlineLengthProperty =
		AvaloniaProperty.Register<OverlineText, int>( nameof( OverlineLength ), 0 );

	/// <summary>
	/// Styled property backing <see cref="Foreground"/>.
	/// </summary>
	public static readonly StyledProperty<IBrush?> ForegroundProperty =
		AvaloniaProperty.Register<OverlineText, IBrush?>( nameof( Foreground ), Brushes.Black );

	/// <summary>
	/// Styled property backing <see cref="FontFamily"/>.
	/// </summary>
	public static readonly StyledProperty<FontFamily> FontFamilyProperty =
		AvaloniaProperty.Register<OverlineText, FontFamily>( nameof( FontFamily ), new FontFamily( "Consolas" ) );

	/// <summary>
	/// Styled property backing <see cref="FontSize"/>.
	/// </summary>
	public static readonly StyledProperty<double> FontSizeProperty =
		AvaloniaProperty.Register<OverlineText, double>( nameof( FontSize ), 14 );

	/// <summary>
	/// Styled property backing <see cref="FontStyle"/>.
	/// </summary>
	public static readonly StyledProperty<FontStyle> FontStyleProperty =
		AvaloniaProperty.Register<OverlineText, FontStyle>( nameof( FontStyle ), FontStyle.Normal );

	/// <summary>
	/// Styled property backing <see cref="FontWeight"/>.
	/// </summary>
	public static readonly StyledProperty<FontWeight> FontWeightProperty =
		AvaloniaProperty.Register<OverlineText, FontWeight>( nameof( FontWeight ), FontWeight.Normal );

	/// <summary>
	/// Styled property backing <see cref="OverlineBrush"/>.
	/// </summary>
	/// <remarks>
	/// When <see langword="null"/>, the control falls back to <see cref="Foreground"/> (and finally to black).
	/// </remarks>
	public static readonly StyledProperty<IBrush?> OverlineBrushProperty =
		AvaloniaProperty.Register<OverlineText, IBrush?>( nameof( OverlineBrush ), null );

	/// <summary>
	/// Styled property backing <see cref="OverlineThickness"/>.
	/// </summary>
	public static readonly StyledProperty<double> OverlineThicknessProperty =
		AvaloniaProperty.Register<OverlineText, double>( nameof( OverlineThickness ), 1 );

	/// <summary>
	/// Styled property backing <see cref="OverlineOffset"/>.
	/// </summary>
	/// <remarks>
	/// Offset is applied relative to the highlight geometry bounds Y coordinate.
	/// Positive values move the line downward.
	/// </remarks>
	public static readonly StyledProperty<double> OverlineOffsetProperty =
		AvaloniaProperty.Register<OverlineText, double>( nameof( OverlineOffset ), 2 );

	/// <summary>
	/// Gets or sets the text to draw. When <see langword="null"/> or empty, nothing is rendered/measured.
	/// </summary>
	public string? Text
	{
		get => GetValue( TextProperty );
		set => SetValue( TextProperty, value );
	}

	/// <summary>
	/// Gets or sets the start character index (0-based) for the overlined range.
	/// </summary>
	/// <remarks>
	/// Values are clamped to the text length. A negative value disables the overline.
	/// </remarks>
	public int OverlineStart
	{
		get => GetValue( OverlineStartProperty );
		set => SetValue( OverlineStartProperty, value );
	}

	/// <summary>
	/// Gets or sets the number of characters to overline.
	/// </summary>
	/// <remarks>
	/// The range end is clamped to the text length. Values less than or equal to zero disable the overline.
	/// </remarks>
	public int OverlineLength
	{
		get => GetValue( OverlineLengthProperty );
		set => SetValue( OverlineLengthProperty, value );
	}

	/// <summary>
	/// Gets or sets the brush used to render the text.
	/// </summary>
	public IBrush? Foreground
	{
		get => GetValue( ForegroundProperty );
		set => SetValue( ForegroundProperty, value );
	}

	/// <summary>
	/// Gets or sets the font family used to render the text.
	/// </summary>
	public FontFamily FontFamily
	{
		get => GetValue( FontFamilyProperty );
		set => SetValue( FontFamilyProperty, value );
	}

	/// <summary>
	/// Gets or sets the font size used to render the text.
	/// </summary>
	public double FontSize
	{
		get => GetValue( FontSizeProperty );
		set => SetValue( FontSizeProperty, value );
	}

	/// <summary>
	/// Gets or sets the font style used to render the text.
	/// </summary>
	public FontStyle FontStyle
	{
		get => GetValue( FontStyleProperty );
		set => SetValue( FontStyleProperty, value );
	}

	/// <summary>
	/// Gets or sets the font weight used to render the text.
	/// </summary>
	public FontWeight FontWeight
	{
		get => GetValue( FontWeightProperty );
		set => SetValue( FontWeightProperty, value );
	}

	/// <summary>
	/// Gets or sets the brush used to render the overline.
	/// </summary>
	/// <remarks>
	/// When <see langword="null"/>, the control uses <see cref="Foreground"/>.
	/// </remarks>
	public IBrush? OverlineBrush
	{
		get => GetValue( OverlineBrushProperty );
		set => SetValue( OverlineBrushProperty, value );
	}

	/// <summary>
	/// Gets or sets the thickness of the overline stroke.
	/// </summary>
	public double OverlineThickness
	{
		get => GetValue( OverlineThicknessProperty );
		set => SetValue( OverlineThicknessProperty, value );
	}

	/// <summary>
	/// Gets or sets the Y offset applied to the computed overline position.
	/// </summary>
	public double OverlineOffset
	{
		get => GetValue( OverlineOffsetProperty );
		set => SetValue( OverlineOffsetProperty, value );
	}

	/// <summary>
	/// Initializes property change notifications for layout and rendering.
	/// </summary>
	/// <remarks>
	/// Text and font-related changes affect measuring; overline-related changes affect rendering.
	/// </remarks>
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

	/// <summary>
	/// Creates a <see cref="FormattedText"/> instance using the current font and <see cref="Foreground"/>.
	/// </summary>
	/// <param name="text">Text to format.</param>
	/// <returns>A formatted text layout object used for measuring and rendering.</returns>
	private FormattedText CreateFormattedText( string text ) =>
		new(
			text,
			CultureInfo.CurrentUICulture,
			FlowDirection.LeftToRight,
			new Typeface( FontFamily, FontStyle, FontWeight ),
			FontSize,
			Foreground ?? Brushes.Black );

	/// <summary>
	/// Measures the desired size of the control based on the formatted text.
	/// </summary>
	/// <param name="availableSize">The available size for the control.</param>
	/// <returns>
	/// The measured size clamped to <paramref name="availableSize"/>. Returns default when there is no text.
	/// </returns>
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

	/// <summary>
	/// Renders the text and (optionally) an overline over a character span.
	/// </summary>
	/// <param name="context">Drawing context provided by Avalonia.</param>
	/// <remarks>
	/// The overline span is clamped to the actual text length. The Y coordinate is optionally aligned to the
	/// pixel grid to reduce blur on some renderers.
	/// </remarks>
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