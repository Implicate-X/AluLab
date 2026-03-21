using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace AluLab.Common.Controls;

/// <summary>
/// Avalonia <see cref="UserControl"/> that renders a single 7-segment LED digit using seven named
/// <see cref="Shape"/> elements (<c>SegA</c> .. <c>SegG</c>) defined in the corresponding AXAML.
/// </summary>
/// <remarks>
/// Segment bit mapping for <see cref="Segments"/>:
/// <list type="table">
/// <listheader>
/// <term>Bit</term>
/// <description>Segment</description>
/// </listheader>
/// <item><term>0</term><description>A</description></item>
/// <item><term>1</term><description>B</description></item>
/// <item><term>2</term><description>C</description></item>
/// <item><term>3</term><description>D</description></item>
/// <item><term>4</term><description>E</description></item>
/// <item><term>5</term><description>F</description></item>
/// <item><term>6</term><description>G</description></item>
/// </list>
/// </remarks>
public partial class LED7SegmentsModule : UserControl
{
	/// <summary>
	/// Brush used when a segment is turned on.
	/// </summary>
	/// <remarks>
	/// This is updated by <see cref="UpdateOnBrushFromColor"/> when <see cref="Color"/> changes.
	/// </remarks>
	public IBrush OnBrush { get; set; } = Brushes.DodgerBlue;

	/// <summary>
	/// Brush used when a segment is turned off.
	/// </summary>
	public IBrush OffBrush { get; set; } = Brushes.Gainsboro;

	/// <summary>
	/// Cached references to segment shapes resolved from the visual tree.
	/// </summary>
	private Shape? _segA, _segB, _segC, _segD, _segE, _segF, _segG;

	/// <summary>
	/// Display value for the digit.
	/// </summary>
	/// <remarks>
	/// When this property changes, <see cref="Segments"/> is updated using a built-in lookup table
	/// for values <c>-1</c> (blank), <c>0..9</c> and selected hex-like characters (<c>A</c>, <c>b</c>,
	/// <c>C</c>, <c>d</c>, <c>E</c>, <c>F</c>, <c>H</c>, <c>L</c>). Unknown values blank the display.
	/// </remarks>
	public int Value
	{
		get => GetValue( ValueProperty );
		set => SetValue( ValueProperty, value );
	}

	/// <summary>
	/// String representation of the segment "on" color, parsed via <see cref="Avalonia.Media.Color.Parse(string)"/>.
	/// </summary>
	/// <remarks>
	/// If parsing fails, the current <see cref="OnBrush"/> is left unchanged.
	/// This value is also used for the glow/shadow effect color in <see cref="SetShape(Shape?, bool)"/>.
	/// </remarks>
	public string Color
	{
		get => GetValue( ColorProperty );
		set => SetValue( ColorProperty, value );
	}

	/// <summary>
	/// Avalonia styled property backing store for <see cref="Segments"/>.
	/// </summary>
	public static readonly StyledProperty<byte> SegmentsProperty =
		AvaloniaProperty.Register<LED7SegmentsModule, byte>( nameof( Segments ) );

	/// <summary>
	/// Bit mask controlling which of the seven segments are lit.
	/// </summary>
	/// <remarks>
	/// This can be set directly to drive custom patterns. Changes trigger a visual refresh.
	/// </remarks>
	public byte Segments
	{
		get => GetValue( SegmentsProperty );
		set => SetValue( SegmentsProperty, value );
	}

	/// <summary>
	/// Avalonia styled property backing store for <see cref="Value"/>.
	/// </summary>
	public static readonly StyledProperty<int> ValueProperty =
		AvaloniaProperty.Register<LED7SegmentsModule, int>( nameof( Value ), -1 );

	/// <summary>
	/// Avalonia styled property backing store for <see cref="Color"/>.
	/// </summary>
	public static readonly StyledProperty<string> ColorProperty =
		AvaloniaProperty.Register<LED7SegmentsModule, string>( nameof( Color ), "LimeGreen" );

	/// <summary>
	/// Initializes the control, wires property change handlers, and applies the initial <see cref="Color"/>.
	/// </summary>
	public LED7SegmentsModule()
	{
		// Refresh segment fills when the bitmask changes.
		SegmentsProperty.Changed.AddClassHandler<LED7SegmentsModule>( ( x, _ ) => x.UpdateVisualSegments() );

		// Recompute the "on" brush when the color string changes.
		ColorProperty.Changed.AddClassHandler<LED7SegmentsModule>( ( x, _ ) => x.UpdateOnBrushFromColor() );

		InitializeComponent();

		UpdateOnBrushFromColor();
	}

	/// <summary>
	/// Resolves segment shapes from the visual tree after the control is attached, then renders the current state.
	/// </summary>
	protected override void OnAttachedToVisualTree( VisualTreeAttachmentEventArgs e )
	{
		base.OnAttachedToVisualTree( e );

		_segA = this.FindControl<Shape>( "SegA" );
		_segB = this.FindControl<Shape>( "SegB" );
		_segC = this.FindControl<Shape>( "SegC" );
		_segD = this.FindControl<Shape>( "SegD" );
		_segE = this.FindControl<Shape>( "SegE" );
		_segF = this.FindControl<Shape>( "SegF" );
		_segG = this.FindControl<Shape>( "SegG" );

		UpdateVisualSegments();
	}

	/// <summary>
	/// Handles changes to <see cref="Value"/> and maps it to a 7-segment bit pattern in <see cref="Segments"/>.
	/// </summary>
	/// <param name="change">Property change notification.</param>
	protected override void OnPropertyChanged( AvaloniaPropertyChangedEventArgs change )
	{
		base.OnPropertyChanged( change );

		if( change.Property == ValueProperty )
		{
			// Bit: 6 5 4 3 2 1 0
			// Seg: g f e d c b a
			Segments = Value switch
			{
				-1 => 0b0000_0000,
				 0 => 0b0011_1111, // 0
				 1 => 0b0000_0110, // 1
				 2 => 0b0101_1011, // 2
				 3 => 0b0100_1111, // 3
				 4 => 0b0110_0110, // 4
				 5 => 0b0110_1101, // 5
				 6 => 0b0111_1101, // 6
				 7 => 0b0000_0111, // 7
				 8 => 0b0111_1111, // 8
				 9 => 0b0110_1111, // 9
				10 => 0b0111_0111, // A
				11 => 0b0111_1100, // b
				12 => 0b0011_1001, // C
				13 => 0b0101_1110, // d
				14 => 0b0111_1001, // E
				15 => 0b0111_0001, // F
				16 => 0b0111_0110, // H
				17 => 0b0011_1000, // L
				 _ => 0b0000_0000
			};

			// Keep brushes and visuals in sync after changing the pattern.
			UpdateOnBrushFromColor();
			UpdateVisualSegments();
		}
	}

	/// <summary>
	/// Parses <see cref="Color"/> to update <see cref="OnBrush"/> and refresh the segments.
	/// </summary>
	/// <remarks>
	/// Parsing exceptions are swallowed to allow invalid user input without breaking rendering.
	/// </remarks>
	private void UpdateOnBrushFromColor()
	{
		if( string.IsNullOrWhiteSpace( Color ) )
			return;

		try
		{
			var parsed = Avalonia.Media.Color.Parse( Color );
			OnBrush = new SolidColorBrush( parsed );
			UpdateVisualSegments();
		}
		catch
		{
			// Ungültiger Color-String -> OnBrush unverändert lassen
		}
	}

	/// <summary>
	/// Applies the current <see cref="Segments"/> mask to the cached segment shapes.
	/// </summary>
	private void UpdateVisualSegments()
	{
		var segs = Segments;

		SetShape( _segA, ( segs & ( 1 << 0 ) ) != 0 );
		SetShape( _segB, ( segs & ( 1 << 1 ) ) != 0 );
		SetShape( _segC, ( segs & ( 1 << 2 ) ) != 0 );
		SetShape( _segD, ( segs & ( 1 << 3 ) ) != 0 );
		SetShape( _segE, ( segs & ( 1 << 4 ) ) != 0 );
		SetShape( _segF, ( segs & ( 1 << 5 ) ) != 0 );
		SetShape( _segG, ( segs & ( 1 << 6 ) ) != 0 );
	}

	/// <summary>
	/// Sets the fill and optional glow effect for a single segment.
	/// </summary>
	/// <param name="shape">The segment <see cref="Shape"/> (may be <see langword="null"/> if not found).</param>
	/// <param name="on">Whether the segment should be lit.</param>
	private void SetShape( Shape? shape, bool on )
	{
		if( shape is not null )
		{
			shape.Fill = on ? OnBrush : OffBrush;
			shape.Effect = on ? new DropShadowEffect
			{
				Color = Avalonia.Media.Color.Parse( Color ),
				BlurRadius = 24,
				OffsetX = 0,
				OffsetY = 0,
				Opacity = 1,
			} : null;
		}
	}
}
