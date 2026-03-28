using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace AluLab.Common.Controls;

public partial class OperationSelector : UserControl
{
	public static readonly StyledProperty<bool> ModeMProperty =
		AvaloniaProperty.Register<OperationSelector, bool>( nameof( ModeM ) );

	public static readonly StyledProperty<bool> CarryInCnProperty =
		AvaloniaProperty.Register<OperationSelector, bool>( nameof( CarryInCn ) );

	/// <summary>
	/// When true, interpret A/B inputs as active-low (“ACTIVE-LOW DATA” table).
	/// When false, use active-high table.
	/// </summary>
	public static readonly StyledProperty<bool> ActiveLowDataProperty =
		AvaloniaProperty.Register<OperationSelector, bool>( nameof( ActiveLowData ), false );

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

	public bool ActiveLowData
	{
		get => GetValue( ActiveLowDataProperty );
		set => SetValue( ActiveLowDataProperty, value );
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
			if( e.Property == ModeMProperty || e.Property == CarryInCnProperty || e.Property == ActiveLowDataProperty )
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
				Margin = new Thickness( 0, -4, 0, 0 )
			};

			var tile = new Border
			{
				Classes = { "op-tile" },
				Tag = sCode,
				Child = content,
				Margin = new Thickness( 2 )
			};

			_tileBordersByS[ sCode ] = tile;

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

			if( i == idx )
				tile.Classes.Add( "selected" );
			else
				tile.Classes.Remove( "selected" );
		}
	}

	private IReadOnlyList<string> GetCurrentList()
	{
		// M=H => logic table
		if( ModeM )
			return ActiveLowData ? s_logicActiveLow : s_logicActiveHigh;

		// M=L => arithmetic table depends on CN and active-high/low mapping
		if( ActiveLowData )
		{
			// ACTIVE-LOW DATA image: Cn=L => no carry, Cn=H => with carry
			return CarryInCn ? s_arithActiveLow_WithCarry_CnH : s_arithActiveLow_NoCarry_CnL;
		}

		// ACTIVE-HIGH DATA image: Cn=H => no carry, Cn=L => with carry
		return CarryInCn ? s_arithActiveHigh_NoCarry_CnH : s_arithActiveHigh_WithCarry_CnL;
	}

	/// <summary>
	/// Removes "Logic/Arith(...)" and the 4-bit code prefix; keeps "~(X)" supported.
	/// </summary>
	private static ( string Text, int OverlineStart, int OverlineLength ) ParseOperationTileText( string expression )
	{
		if( string.IsNullOrWhiteSpace( expression ) )
			return ( string.Empty, -1, 0 );

		var s = expression.Trim();

		var firstSep = s.IndexOf( ' ' );
		if( firstSep < 0 )
			return ParseSingleOverline( s );

		var afterPrefix = s.IndexOf( ' ', firstSep + 1 );
		if( afterPrefix < 0 )
			return ParseSingleOverline( s );

		var opSep = s.IndexOf( "  ", afterPrefix, StringComparison.Ordinal );
		if( opSep < 0 )
			return ParseSingleOverline( s );

		var opText = s[( opSep + 2 )..].Trim();
		return ParseSingleOverline( opText );
	}

	/// <summary>
	/// Supports exactly one segment in the form "~(X)".
	/// The overline will span exactly the text X (after removing "~(" and ")").
	/// </summary>
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

	// ----------------------------
	// Tables (6×16) from Operation Tables.txt
	// ----------------------------

	private static readonly string[] s_logicActiveLow =
	{
		"Logic 0000  ~(A)",
		"Logic 0001  ~(AB)",
		"Logic 0010  ~(A) + B",
		"Logic 0011  1",
		"Logic 0100  ~(A + B)",
		"Logic 0101  B",
		"Logic 0110  ~(A + B)",
		"Logic 0111  A + B",
		"Logic 1000  ~(A)B",
		"Logic 1001  A + B",
		"Logic 1010  B",
		"Logic 1011  A + B",
		"Logic 1100  0",
		"Logic 1101  A~(B)",
		"Logic 1110  AB",
		"Logic 1111  A",
	};

	private static readonly string[] s_arithActiveLow_NoCarry_CnL =
	{
		"Arith(CN=L) 0000  A MINUS 1",
		"Arith(CN=L) 0001  AB MINUS 1",
		"Arith(CN=L) 0010  A~(B) MINUS 1",
		"Arith(CN=L) 0011  MINUS 1 (2's COMP)",
		"Arith(CN=L) 0100  A PLUS (A + ~(B))",
		"Arith(CN=L) 0101  AB PLUS (A + ~(B))",
		"Arith(CN=L) 0110  A MINUS B MINUS 1",
		"Arith(CN=L) 0111  A + ~(B)",
		"Arith(CN=L) 1000  A PLUS (A + B)",
		"Arith(CN=L) 1001  A PLUS B",
		"Arith(CN=L) 1010  A~(B) PLUS (A + B)",
		"Arith(CN=L) 1011  (A + B)",
		"Arith(CN=L) 1100  A PLUS A",
		"Arith(CN=L) 1101  A~(B) PLUS A",
		"Arith(CN=L) 1110  AB PLUS A",
		"Arith(CN=L) 1111  A",
	};

	private static readonly string[] s_arithActiveLow_WithCarry_CnH =
	{
		"Arith(CN=H) 0000  A",
		"Arith(CN=H) 0001  AB",
		"Arith(CN=H) 0010  A~(B)",
		"Arith(CN=H) 0011  ZERO",
		"Arith(CN=H) 0100  A PLUS (A + ~(B)) PLUS 1",
		"Arith(CN=H) 0101  AB PLUS (A + ~(B)) PLUS 1",
		"Arith(CN=H) 0110  A MINUS B",
		"Arith(CN=H) 0111  (A + ~(B)) PLUS 1",
		"Arith(CN=H) 1000  A PLUS (A + B) PLUS 1",
		"Arith(CN=H) 1001  A PLUS B PLUS 1",
		"Arith(CN=H) 1010  A~(B) PLUS (A + B) PLUS 1",
		"Arith(CN=H) 1011  (A + B) PLUS 1",
		"Arith(CN=H) 1100  A PLUS A PLUS 1",
		"Arith(CN=H) 1101  AB PLUS A PLUS 1",
		"Arith(CN=H) 1110  A~(B) PLUS A PLUS 1",
		"Arith(CN=H) 1111  A PLUS 1",
	};

	private static readonly string[] s_logicActiveHigh =
	{
		"Logic 0000  ~(A)",
		"Logic 0001  A + B",
		"Logic 0010  A + ~(B)",
		"Logic 0011  0",
		"Logic 0100  ~(AB)",
		"Logic 0101  ~(B)",
		"Logic 0110  A + B",
		"Logic 0111  A~(B)",
		"Logic 1000  ~(A) + B",
		"Logic 1001  A + B",
		"Logic 1010  B",
		"Logic 1011  AB",
		"Logic 1100  1",
		"Logic 1101  A + ~(B)",
		"Logic 1110  A + B",
		"Logic 1111  A",
	};

	private static readonly string[] s_arithActiveHigh_NoCarry_CnH =
	{
		"Arith(CN=H) 0000  A",
		"Arith(CN=H) 0001  A + B",
		"Arith(CN=H) 0010  A + ~(B)",
		"Arith(CN=H) 0011  MINUS 1 (2's COMPL)",
		"Arith(CN=H) 0100  A PLUS A~(B)",
		"Arith(CN=H) 0101  (A + B) PLUS A~(B)",
		"Arith(CN=H) 0110  A MINUS B MINUS 1",
		"Arith(CN=H) 0111  A~(B) MINUS 1",
		"Arith(CN=H) 1000  A PLUS AB",
		"Arith(CN=H) 1001  A PLUS B",
		"Arith(CN=H) 1010  (A + ~(B)) PLUS AB",
		"Arith(CN=H) 1011  AB MINUS 1",
		"Arith(CN=H) 1100  A PLUS A",
		"Arith(CN=H) 1101  (A + B) PLUS A",
		"Arith(CN=H) 1110  (A + ~(B)) PLUS A",
		"Arith(CN=H) 1111  A MINUS 1",
	};

	private static readonly string[] s_arithActiveHigh_WithCarry_CnL =
	{
		"Arith(CN=L) 0000  A PLUS 1",
		"Arith(CN=L) 0001  (A + B) PLUS 1",
		"Arith(CN=L) 0010  (A + ~(B)) PLUS 1",
		"Arith(CN=L) 0011  ZERO",
		"Arith(CN=L) 0100  A PLUS A~(B) PLUS 1",
		"Arith(CN=L) 0101  (A + B) PLUS A~(B) PLUS 1",
		"Arith(CN=L) 0110  A MINUS B",
		"Arith(CN=L) 0111  A~(B)",
		"Arith(CN=L) 1000  A PLUS AB PLUS 1",
		"Arith(CN=L) 1001  A PLUS B PLUS 1",
		"Arith(CN=L) 1010  (A + ~(B)) PLUS AB PLUS 1",
		"Arith(CN=L) 1011  AB",
		"Arith(CN=L) 1100  A PLUS A PLUS 1",
		"Arith(CN=L) 1101  (A + B) PLUS A PLUS 1",
		"Arith(CN=L) 1110  (A + ~(B)) PLUS A PLUS 1",
		"Arith(CN=L) 1111  A",
	};
}
