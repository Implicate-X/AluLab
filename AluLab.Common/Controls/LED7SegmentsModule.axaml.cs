using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace AluLab.Common.Controls;

public partial class LED7SegmentsModule : UserControl
{
	public IBrush OnBrush { get; set; } = Brushes.DodgerBlue;
	public IBrush OffBrush { get; set; } = Brushes.Gainsboro;

	private Shape? _segA, _segB, _segC, _segD, _segE, _segF, _segG;

	public int Value
	{
		get => GetValue( ValueProperty );
		set => SetValue( ValueProperty, value );
	}

	public string Color
	{
		get => GetValue( ColorProperty );
		set => SetValue( ColorProperty, value );
	}

	public static readonly StyledProperty<byte> SegmentsProperty =
		AvaloniaProperty.Register<LED7SegmentsModule, byte>( nameof( Segments ) );

	public byte Segments
	{
		get => GetValue( SegmentsProperty );
		set => SetValue( SegmentsProperty, value );
	}

	public static readonly StyledProperty<int> ValueProperty =
		AvaloniaProperty.Register<LED7SegmentsModule, int>( nameof( Value ), -1 );

	public static readonly StyledProperty<string> ColorProperty =
		AvaloniaProperty.Register<LED7SegmentsModule, string>( nameof( Color ), "LimeGreen" );

	public LED7SegmentsModule()
	{
		SegmentsProperty.Changed.AddClassHandler<LED7SegmentsModule>( ( x, _ ) => x.UpdateVisualSegments() );
		ColorProperty.Changed.AddClassHandler<LED7SegmentsModule>( ( x, _ ) => x.UpdateOnBrushFromColor() );

		InitializeComponent();

		UpdateOnBrushFromColor();
	}

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

			UpdateOnBrushFromColor();
			UpdateVisualSegments();
		}
	}

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
