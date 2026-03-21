using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AluLab.Common.Controls;

/// <summary>
/// Renders a single line of text and optionally draws an overline over a contiguous character range.
/// </summary>
/// <remarks>
/// <para>
/// This control is intended for simple, fast rendering scenarios. Overline placement is computed using a
/// monospace glyph-width approximation (see <c>MeasureOverride</c> / <c>Render</c>), which is a good fit
/// for the default <see cref="FontFamily"/> (<c>Consolas</c>), but may be visually inaccurate for proportional fonts.
/// </para>
/// <para>
/// Overline is enabled when <see cref="OverlineStart"/> is non-negative and <see cref="OverlineLength"/> is greater than 0.
/// The overline is rendered using <see cref="OverlineBrush"/> when provided, otherwise it falls back to
/// <see cref="Foreground"/> (and then <see cref="Brushes.Black"/>).
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
	public static readonly StyledProperty<double> OverlineOffsetProperty =
		AvaloniaProperty.Register<OverlineText, double>( nameof( OverlineOffset ), 2 );

	/// <summary>
	/// Gets or sets the text to render.
	/// </summary>
	public string? Text
	{
		get => GetValue( TextProperty );
		set => SetValue( TextProperty, value );
	}

	/// <summary>
	/// Gets or sets the start index (0-based) within <see cref="Text"/> where the overline begins.
	/// </summary>
	/// <remarks>
	/// Set to -1 to disable rendering the overline.
	/// </remarks>
	public int OverlineStart
	{
		get => GetValue( OverlineStartProperty );
		set => SetValue( OverlineStartProperty, value );
	}

	/// <summary>
	/// Gets or sets the number of characters to overline starting at <see cref="OverlineStart"/>.
	/// </summary>
	/// <remarks>
	/// Values less than or equal to 0 disable the overline.
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
	/// When null, falls back to <see cref="Foreground"/>.
	/// </remarks>
	public IBrush? OverlineBrush
	{
		get => GetValue( OverlineBrushProperty );
		set => SetValue( OverlineBrushProperty, value );
	}

	/// <summary>
	/// Gets or sets the thickness (stroke width) of the overline.
	/// </summary>
	public double OverlineThickness
	{
		get => GetValue( OverlineThicknessProperty );
		set => SetValue( OverlineThicknessProperty, value );
	}

	/// <summary>
	/// Gets or sets the distance (in device-independent pixels) from the top of the text
	/// to where the overline is drawn.
	/// </summary>
	public double OverlineOffset
	{
		get => GetValue( OverlineOffsetProperty );
		set => SetValue( OverlineOffsetProperty, value );
	}

	/// <summary>
	/// Registers properties that affect measurement and rendering, ensuring the control re-measures
	/// and/or re-renders when they change.
	/// </summary>
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
	/// Computes the desired size of the control using a monospace glyph-width approximation.
	/// </summary>
	/// <param name="availableSize">The size available from the layout system.</param>
	/// <returns>The desired size, constrained by <paramref name="availableSize"/>.</returns>
	/// <remarks>
	/// The width is approximated as <c>text.Length * (FontSize * 0.62)</c> and the height as <c>FontSize * 1.35</c>.
	/// This avoids more expensive text layout during measurement but may not match actual glyph metrics.
	/// </remarks>
	protected override Size MeasureOverride( Size availableSize )
	{
		var text = Text ?? string.Empty;

		// v1: monospace approximation (Consolas default in this control).
		var glyphW = Math.Max( 0.0, FontSize * 0.62 );
		var w = glyphW * text.Length;
		var h = Math.Max( 0.0, FontSize * 1.35 );

		return new Size( Math.Min( w, availableSize.Width ), Math.Min( h, availableSize.Height ) );
	}

	/// <summary>
	/// Renders the text and, when enabled, an overline covering the configured character range.
	/// </summary>
	/// <param name="context">Drawing context used by Avalonia to render primitives.</param>
	/// <remarks>
	/// The overline's horizontal coordinates are computed from the configured start/length and the same
	/// glyph-width approximation used by measurement. The character range is clamped to the text bounds.
	/// </remarks>
	public override void Render( DrawingContext context )
	{
		base.Render( context );

		var text = Text ?? string.Empty;
		if( text.Length == 0 )
			return;

		var typeface = new Typeface( FontFamily, FontStyle, FontWeight );
		var ft = new FormattedText(
			text,
			CultureInfo.CurrentUICulture,
			FlowDirection.LeftToRight,
			typeface,
			FontSize,
			Foreground ?? Brushes.Black );

		context.DrawText( ft, new Point( 0, 0 ) );

		if( OverlineStart < 0 || OverlineLength <= 0 )
			return;

		var start = Math.Clamp( OverlineStart, 0, text.Length );
		var end = Math.Clamp( OverlineStart + OverlineLength, start, text.Length );

		var glyphW = Math.Max( 0.0, FontSize * 0.62 );
		var x1 = start * glyphW;
		var x2 = end * glyphW;

		var y = OverlineOffset;

		var penBrush = OverlineBrush ?? Foreground ?? Brushes.Black;
		var pen = new Pen( penBrush, OverlineThickness );
		context.DrawLine( pen, new Point( x1, y ), new Point( x2, y ) );
	}
}