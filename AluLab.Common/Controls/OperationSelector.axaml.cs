using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AluLab.Common.Controls;

/// <summary>
/// UI control that lets the user pick one of the 16 ALU <c>S</c>-code operations (<c>S0..S3</c>) via a <see cref="ComboBox"/>.
/// </summary>
/// <remarks>
/// <para>
/// The selector’s displayed list depends on the external ALU inputs <c>M</c> and <c>CN</c>:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="ModeM"/> chooses between Logic and Arithmetic tables.</description></item>
/// <item><description><see cref="CarryInCn"/> selects the arithmetic variant (CN low vs. high).</description></item>
/// </list>
/// <para>
/// Per project rules, <c>M</c> and <c>CN</c> are treated as read-only inputs: changing the selection updates only
/// <see cref="SelectedSCode"/> (the <c>S</c>-code), and never writes back to <see cref="ModeM"/> or <see cref="CarryInCn"/>.
/// </para>
/// </remarks>
public partial class OperationSelector : UserControl
{
	/// <summary>
	/// Styled property backing <see cref="ModeM"/>.
	/// </summary>
	public static readonly StyledProperty<bool> ModeMProperty =
		AvaloniaProperty.Register<OperationSelector, bool>( nameof( ModeM ) );

	/// <summary>
	/// Styled property backing <see cref="CarryInCn"/>.
	/// </summary>
	public static readonly StyledProperty<bool> CarryInCnProperty =
		AvaloniaProperty.Register<OperationSelector, bool>( nameof( CarryInCn ) );

	/// <summary>
	/// Styled property backing <see cref="SelectedSCode"/>.
	/// </summary>
	/// <remarks>
	/// The value is expected to be in the range 0–15 (inclusive). Out-of-range values are clamped before applying
	/// to the UI selection index.
	/// </remarks>
	public static readonly StyledProperty<int> SelectedSCodeProperty =
		AvaloniaProperty.Register<OperationSelector, int>( nameof( SelectedSCode ), 0 );

	/// <summary>
	/// Raised when the user changes the selection in the UI (as opposed to programmatic updates).
	/// </summary>
	/// <remarks>
	/// The event argument is the selected <c>S</c>-code in the range 0–15.
	/// </remarks>
	public event EventHandler<int>? SelectedSCodeChangedByUser;

	/// <summary>
	/// Gets/sets the ALU mode input (<c>M</c>): <see langword="true"/> selects the logic operation table;
	/// <see langword="false"/> selects arithmetic tables.
	/// </summary>
	public bool ModeM
	{
		get => GetValue( ModeMProperty );
		set => SetValue( ModeMProperty, value );
	}

	/// <summary>
	/// Gets/sets the ALU carry-in input (<c>CN</c>) used only to choose the arithmetic list variant.
	/// </summary>
	public bool CarryInCn
	{
		get => GetValue( CarryInCnProperty );
		set => SetValue( CarryInCnProperty, value );
	}

	/// <summary>
	/// Gets/sets the selected <c>S</c>-code (index 0–15) representing the chosen operation.
	/// </summary>
	/// <remarks>
	/// Setting this property updates the <see cref="ComboBox.SelectedIndex"/> without firing
	/// <see cref="SelectedSCodeChangedByUser"/>.
	/// </remarks>
	public int SelectedSCode
	{
		get => GetValue( SelectedSCodeProperty );
		set => SetValue( SelectedSCodeProperty, value );
	}

	/// <summary>
	/// Guard used to prevent re-entrancy / feedback loops when the control updates the <see cref="ComboBox"/>
	/// programmatically (rebuilding items or setting selection).
	/// </summary>
	private int _suppressSelectionChanged;

	/// <summary>
	/// Initializes the control, wires property/selection change handlers, and builds the initial list.
	/// </summary>
	public OperationSelector()
	{
		InitializeComponent();

		// Keep the ComboBox in sync with external inputs.
		// - ModeM / CarryInCn change => rebuild the list and reselect current S-code.
		// - SelectedSCode change     => update only the selected index.
		PropertyChanged += ( _, e ) =>
		{
			if( e.Property == ModeMProperty || e.Property == CarryInCnProperty )
			{
				RebuildItemsAndReselect();
			}
			else if( e.Property == SelectedSCodeProperty )
			{
				UpdateSelectedIndex();
			}
		};

		// User-driven selection changes update SelectedSCode and notify listeners.
		var combo = this.FindControl<ComboBox>( "OperationComboBox" );
		if( combo is not null )
		{
			combo.SelectionChanged += ( _, __ ) =>
			{
				if( Volatile.Read( ref _suppressSelectionChanged ) != 0 )
					return;

				if( combo.SelectedItem is ComboBoxItem item && item.Tag is int code )
				{
					// Update only S-code; M and CN are external/read-only inputs.
					SetCurrentValue( SelectedSCodeProperty, code );
					SelectedSCodeChangedByUser?.Invoke( this, code );
				}
			};
		}

		RebuildItemsAndReselect();
	}

	/// <summary>
	/// Applies <see cref="SelectedSCode"/> to the ComboBox selection.
	/// </summary>
	/// <remarks>
	/// Uses <see cref="_suppressSelectionChanged"/> to prevent the programmatic selection update
	/// from being treated as a user action.
	/// </remarks>
	private void UpdateSelectedIndex()
	{
		var combo = this.FindControl<ComboBox>( "OperationComboBox" );
		if( combo is null )
			return;

		var idx = Math.Clamp( SelectedSCode, 0, 15 );

		Interlocked.Exchange( ref _suppressSelectionChanged, 1 );
		try
		{
			combo.SelectedIndex = idx;
		}
		finally
		{
			Interlocked.Exchange( ref _suppressSelectionChanged, 0 );
		}
	}

	/// <summary>
	/// Rebuilds the ComboBox item list based on <see cref="ModeM"/> and <see cref="CarryInCn"/>,
	/// then reselects the current <see cref="SelectedSCode"/>.
	/// </summary>
	/// <remarks>
	/// Each item uses <see cref="OverlineText"/> to render a single complemented term (written as <c>~(X)</c>)
	/// as an overline over <c>X</c>.
	/// </remarks>
	private void RebuildItemsAndReselect()
	{
		var combo = this.FindControl<ComboBox>( "OperationComboBox" );
		if( combo is null )
			return;

		var idx = Math.Clamp( SelectedSCode, 0, 15 );
		var list = GetCurrentList();

		var items = new List<ComboBoxItem>( capacity: 16 );
		for( int s = 0; s < 16; s++ )
		{
			var ( text, overStart, overLen ) = ParseSingleOverline( list[ s ] );

			// Store the literal S-code in Tag; selection handler reads it back.
			items.Add( new ComboBoxItem
			{
				Tag = s,
				Content = new OverlineText
				{
					Text = text,
					OverlineStart = overStart,
					OverlineLength = overLen,
					FontFamily = new FontFamily( "Consolas" ),
					FontSize = 14,
					Foreground = Brushes.Black,
					OverlineThickness = 1,
					OverlineOffset = 1.5,
				}
			} );
		}

		Interlocked.Exchange( ref _suppressSelectionChanged, 1 );
		try
		{
			combo.ItemsSource = items;
			combo.SelectedIndex = idx;
		}
		finally
		{
			Interlocked.Exchange( ref _suppressSelectionChanged, 0 );
		}
	}

	/// <summary>
	/// Picks the operation text table for the current state of <see cref="ModeM"/> and <see cref="CarryInCn"/>.
	/// </summary>
	private IReadOnlyList<string> GetCurrentList()
	{
		if( ModeM )
			return s_logic;

		return CarryInCn ? s_arithCnHigh : s_arithCnLow;
	}

	/// <summary>
	/// Logic-mode operation descriptions indexed by S-code (0–15).
	/// </summary>
	private static readonly string[] s_logic =
	{
		"Logic 0000  ~(A)", "Logic 0001  ~(B)", "Logic 0010  A XOR B", "Logic 0011  A OR B",
		"Logic 0100  A AND B", "Logic 0101  ~(A AND B)", "Logic 0110  A", "Logic 0111  B",
		"Logic 1000  0", "Logic 1001  1", "Logic 1010  ~(A OR B)", "Logic 1011  A NAND B",
		"Logic 1100  A NOR B", "Logic 1101  A XNOR B", "Logic 1110  A + B", "Logic 1111  (custom)"
	};

	/// <summary>
	/// Arithmetic-mode operation descriptions when <c>CN</c> is low, indexed by S-code (0–15).
	/// </summary>
	private static readonly string[] s_arithCnLow =
	{
		"Arith(CN=L) 0000  A + B", "Arith(CN=L) 0001  A + ~(B)", "Arith(CN=L) 0010  A - 1", "Arith(CN=L) 0011  A",
		"Arith(CN=L) 0100  A + 1", "Arith(CN=L) 0101  A - B", "Arith(CN=L) 0110  B - A", "Arith(CN=L) 0111  B",
		"Arith(CN=L) 1000  A + B + 1", "Arith(CN=L) 1001  A + ~(B) + 1", "Arith(CN=L) 1010  (placeholder)", "Arith(CN=L) 1011  (placeholder)",
		"Arith(CN=L) 1100  0", "Arith(CN=L) 1101  1", "Arith(CN=L) 1110  (placeholder)", "Arith(CN=L) 1111  (placeholder)"
	};

	/// <summary>
	/// Arithmetic-mode operation descriptions when <c>CN</c> is high, indexed by S-code (0–15).
	/// </summary>
	private static readonly string[] s_arithCnHigh =
	{
		"Arith(CN=H) 0000  A + B + 1", "Arith(CN=H) 0001  A + ~(B) + 1", "Arith(CN=H) 0010  A", "Arith(CN=H) 0011  A + 1",
		"Arith(CN=H) 0100  A - B - 1", "Arith(CN=H) 0101  A - B", "Arith(CN=H) 0110  B - A - 1", "Arith(CN=H) 0111  B - A",
		"Arith(CN=H) 1000  B", "Arith(CN=H) 1001  B + 1", "Arith(CN=H) 1010  (placeholder)", "Arith(CN=H) 1011  (placeholder)",
		"Arith(CN=H) 1100  0", "Arith(CN=H) 1101  1", "Arith(CN=H) 1110  (placeholder)", "Arith(CN=H) 1111  (placeholder)"
	};

	/// <summary>
	/// Converts a single complemented segment written as <c>~(X)</c> into overline metadata for <see cref="OverlineText"/>.
	/// </summary>
	/// <param name="expression">The raw expression text (may contain exactly one <c>~(…)</c> segment).</param>
	/// <returns>
	/// A tuple containing:
	/// <list type="bullet">
	/// <item><description><c>Text</c>: display string with the <c>~(</c> and <c>)</c> removed.</description></item>
	/// <item><description><c>OverlineStart</c>: start index (in <c>Text</c>) of the segment to overline, or -1 if none.</description></item>
	/// <item><description><c>OverlineLength</c>: length of the overlined segment.</description></item>
	/// </list>
	/// </returns>
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

		var before = s[..startMarker];
		var inner = s.Substring( innerStart, endParen - innerStart );
		var after = s[( endParen + 1 )..];

		return ( before + inner + after, before.Length, inner.Length );
	}
}
