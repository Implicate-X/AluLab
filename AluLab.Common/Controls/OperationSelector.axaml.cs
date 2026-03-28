using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace AluLab.Common.Controls;

/// <summary>
/// UI control that lets the user pick one of the 16 ALU <c>S</c>-code operations (<c>S0..S3</c>) via a tile grid.
/// </summary>
public partial class OperationSelector : UserControl
{
	public static readonly StyledProperty<bool> ModeMProperty =
		AvaloniaProperty.Register<OperationSelector, bool>( nameof( ModeM ) );

	public static readonly StyledProperty<bool> CarryInCnProperty =
		AvaloniaProperty.Register<OperationSelector, bool>( nameof( CarryInCn ) );

	public static readonly StyledProperty<int> SelectedSCodeProperty =
		AvaloniaProperty.Register<OperationSelector, int>( nameof( SelectedSCode ), 0 );

	public event EventHandler<int>? SelectedSCodeChangedByUser;

	public bool ModeM
	{
		get => GetValue( ModeMProperty );
		set => SetValue( ModeMProperty, value );
	}

	public bool CarryInCn
	{
		get => GetValue( CarryInCnProperty );
		set => SetValue( CarryInCnProperty, value );
	}

	public int SelectedSCode
	{
		get => GetValue( SelectedSCodeProperty );
		set => SetValue( SelectedSCodeProperty, value );
	}

	private int _suppressSelectionChanged;

	private readonly Border?[] _tileBordersByS = new Border?[ 16 ];

	public OperationSelector()
	{
		InitializeComponent();

		PropertyChanged += ( _, e ) =>
		{
			if( e.Property == ModeMProperty || e.Property == CarryInCnProperty )
				RebuildTiles();
			else if( e.Property == SelectedSCodeProperty )
				UpdateTileSelectionVisuals();
		};

		RebuildTiles();
	}

	private UniformGrid? GetTilesGrid() => this.FindControl<UniformGrid>( "TilesGrid" );

	private void RebuildTiles()
	{
		var grid = GetTilesGrid();
		if( grid is null )
			return;

		grid.Children.Clear();
		Array.Clear( _tileBordersByS );

		var list = GetCurrentList();

		for( var s = 0; s < 16; s++ )
		{
			var sCode = s; // capture-safe copy

			var raw = list[ s ];

			// 1) drop "Logic 0000  " / "Arith(CN=L) 0101  "
			// 2) convert "~(X)" -> overline metadata
			var ( tileText, overStart, overLen ) = ParseOperationTileText( raw );

			var content = new OverlineText
			{
				Text = tileText,
				OverlineStart = overStart,
				OverlineLength = overLen,
				FontFamily = new FontFamily( "Consolas" ),
				FontSize = 9,
				Foreground = Brushes.Black,
				OverlineThickness = 0.5,
				OverlineOffset = 0.0,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness( 0,-4,0,0 )
			};

			var tile = new Border
			{
				Classes = { "op-tile" },
				Tag = s,
				Child = content,
			};

			_tileBordersByS[ s ] = tile;

			// Click/tap selects the tile.
			tile.PointerPressed += ( _, e ) =>
			{
				if( e.GetCurrentPoint( tile ).Properties.IsLeftButtonPressed == false )
					return;

				if( Volatile.Read( ref _suppressSelectionChanged ) != 0 )
					return;

				Interlocked.Exchange( ref _suppressSelectionChanged, 1 );
				try
				{
					SetCurrentValue( SelectedSCodeProperty, sCode );
					UpdateTileSelectionVisuals();
				}
				finally
				{
					Interlocked.Exchange( ref _suppressSelectionChanged, 0 );
				}

				SelectedSCodeChangedByUser?.Invoke( this, sCode );
				e.Handled = true;
			};

			grid.Children.Add( tile );
		}

		UpdateTileSelectionVisuals();
	}

	private void UpdateTileSelectionVisuals()
	{
		var idx = Math.Clamp( SelectedSCode, 0, 15 );

		for( var i = 0; i < _tileBordersByS.Length; i++ )
		{
			var tile = _tileBordersByS[ i ];
			if( tile is null )
				continue;

			// Use a class so it also works with theme/style changes.
			if( i == idx )
				tile.Classes.Add( "selected" );
			else
				tile.Classes.Remove( "selected" );
		}
	}

	private IReadOnlyList<string> GetCurrentList()
	{
		if( ModeM )
			return s_logic;

		return CarryInCn ? s_arithCnHigh : s_arithCnLow;
	}

	/// <summary>
	/// Produces tile display text without "Logic"/"Arith(...)" and without the 4-bit prefix.
	/// Also converts "~(X)" into (text, overline metadata).
	/// </summary>
	private static ( string Text, int OverlineStart, int OverlineLength ) ParseOperationTileText( string expression )
	{
		if( string.IsNullOrWhiteSpace( expression ) )
			return ( string.Empty, -1, 0 );

		// Expected formats:
		// "Logic 0101  ~(A AND B)"
		// "Arith(CN=L) 0101  A - B"
		//
		// We want only the operation part after the two spaces following the bit pattern.
		var s = expression.Trim();

		// Find the *second* double-space separator (after the 4-bit code).
		// Example: "Logic 0101  ~(A AND B)"
		//          ^----prefix----^ ^-op starts here
		var firstSep = s.IndexOf( ' ' );
		if( firstSep < 0 )
			return ParseSingleOverline( s );

		// find the 4-bit block start: after first space
		var afterPrefix = s.IndexOf( ' ', firstSep + 1 );
		if( afterPrefix < 0 )
			return ParseSingleOverline( s );

		// now look for the double-space that separates code and operation text
		var opSep = s.IndexOf( "  ", afterPrefix, StringComparison.Ordinal );
		if( opSep < 0 )
			return ParseSingleOverline( s );

		var opText = s[( opSep + 2 )..].Trim();
		return ParseSingleOverline( opText );
	}

	private static readonly string[] s_logic =
	{
		"Logic 0000  ~(A)", "Logic 0001  ~(B)", "Logic 0010  A XOR B", "Logic 0011  A OR B",
		"Logic 0100  A AND B", "Logic 0101  ~(A AND B)", "Logic 0110  A", "Logic 0111  B",
		"Logic 1000  0", "Logic 1001  1", "Logic 1010  ~(A OR B)", "Logic 1011  A NAND B",
		"Logic 1100  A NOR B", "Logic 1101  A XNOR B", "Logic 1110  A + B", "Logic 1111  (custom)"
	};

	private static readonly string[] s_arithCnLow =
	{
		"Arith(CN=L) 0000  A + B", "Arith(CN=L) 0001  A + ~(B)", "Arith(CN=L) 0010  A - 1", "Arith(CN=L) 0011  A",
		"Arith(CN=L) 0100  A + 1", "Arith(CN=L) 0101  A - B", "Arith(CN=L) 0110  B - A", "Arith(CN=L) 0111  B",
		"Arith(CN=L) 1000  A + B + 1", "Arith(CN=L) 1001  A + ~(B) + 1", "Arith(CN=L) 1010  (placeholder)", "Arith(CN=L) 1011  (placeholder)",
		"Arith(CN=L) 1100  0", "Arith(CN=L) 1101  1", "Arith(CN=L) 1110  (placeholder)", "Arith(CN=L) 1111  (placeholder)"
	};

	private static readonly string[] s_arithCnHigh =
	{
		"Arith(CN=H) 0000  A + B + 1", "Arith(CN=H) 0001  A + ~(B) + 1", "Arith(CN=H) 0010  A", "Arith(CN=H) 0011  A + 1",
		"Arith(CN=H) 0100  A - B - 1", "Arith(CN=H) 0101  A - B", "Arith(CN=H) 0110  B - A - 1", "Arith(CN=H) 0111  B - A",
		"Arith(CN=H) 1000  B", "Arith(CN=H) 1001  B + 1", "Arith(CN=H) 1010  (placeholder)", "Arith(CN=H) 1011  (placeholder)",
		"Arith(CN=H) 1100  0", "Arith(CN=H) 1101  1", "Arith(CN=H) 1110  (placeholder)", "Arith(CN=H) 1111  (placeholder)"
	};

	private static ( string Text, int OverlineStart, int OverlineLength ) ParseSingleOverline( string expression )
	{
		if( string.IsNullOrWhiteSpace( expression ) )
			return ( string.Empty, -1, 0 );

		var s = expression;

		var startMarker = s.IndexOf( "~(", StringComparison.Ordinal );
		if( startMarker < 0 )
			return ( s, -1, 0 );

		var innerStart = startMarker + 2;
		var endParen = s.IndexOf( ')', innerStart );
		if( endParen < 0 )
			return ( s, -1, 0 );

		var before = s[ ..startMarker ];
		var inner = s.Substring( innerStart, endParen - innerStart );
		var after = s[ ( endParen + 1 ).. ];

		return ( before + inner + after, before.Length, inner.Length );
	}
}
