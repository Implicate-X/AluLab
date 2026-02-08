using System;
using System.Threading;
using System.Diagnostics;
using System.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AluLab.Common.Services;
using AluLab.Common.ViewModels;

namespace AluLab.Common.Views;

public partial class HousingView : UserControl, INotifyPropertyChanged
{
	private readonly HousingViewModel? _viewModel;
	private readonly SyncService? _syncService;

	private IDisposable? _pinToggledSubscription;
	private IDisposable? _aluOutputsSubscription;

	private int _suppressSyncSend;

	private readonly ObservableCollection<string> _logItems = new();

	public IReadOnlyCollection<string> LogItems => _logItems;

	private char _logsSeparator = ':';

	private string _logsText = string.Empty;

	public string LogsText
	{
		get => _logsText;
		private set => SetProperty( ref _logsText, value );
	}

	public event EventHandler<PinToggledEventArgs>? PinToggled;

	private readonly Dictionary<string, bool> _pinStates = new();

	private readonly HashSet<string> _outputPins = new()
	{
		"F3", "F2", "F1", "F0", "P", "G", "AEqualsB", "CN4"
	};

	private readonly string[] _pinNames = new[]
	{
		"A3","A2","A1","A0",
		"B3","B2","B1","B0",
		"S3","S2","S1","S0",
		"CN","M",
		"F3","F2","F1","F0",
		"P","G","AEqualsB","CN4"
	};

	private readonly SolidColorBrush _brushHigh = new SolidColorBrush( Colors.LimeGreen );
	private readonly SolidColorBrush _brushLow = new SolidColorBrush( Colors.White );

	public HousingView()
	{
		InitializeComponent();
		InitializePins();

		App app = (App)Application.Current!;
		_viewModel = app.Services.GetRequiredService<HousingViewModel>();
		_syncService = app.Services.GetRequiredService<SyncService>();

		if( DataContext is null )
		{
			DataContext = _viewModel;
		}

		DataContextChanged += ( _, __ ) =>
		{
			if( DataContext is not HousingViewModel )
				DataContext = _viewModel;
		};

		Debug.WriteLine( $"ConnectCommand is null? {_viewModel?.ConnectCommand == null}" );
		Debug.WriteLine( $"DisconnectCommand is null? {_viewModel?.DisconnectCommand == null}" );
		Debug.WriteLine( $"SendTestCommand is null? {_viewModel?.SendTestCommand == null}" );

		DetachedFromVisualTree += async ( _, __ ) =>
		{
			_pinToggledSubscription?.Dispose();
			_pinToggledSubscription = null;

			_aluOutputsSubscription?.Dispose();
			_aluOutputsSubscription = null;

			if( _viewModel is null )
				return;

			await _viewModel.DisposeAsync();
		};

		AttachedToVisualTree += ( _, __ ) => StartSyncAutoConnect();
	}

	/// <summary>
	/// Initializes and starts subscriptions for synchronization events, enabling automatic connection and state updates
	/// with the remote sync service.
	/// </summary>
	/// <remarks>This method sets up event handlers to synchronize pin and ALU output states between the local
	/// application and a remote service. It should be called when synchronization is required. If the synchronization
	/// service is not available, the method returns without performing any actions. Calling this method multiple times
	/// will dispose of any previous subscriptions before creating new ones.</remarks>
	private void StartSyncAutoConnect()
	{
		if( _syncService is null )
			return;

		_pinToggledSubscription?.Dispose();
		_aluOutputsSubscription?.Dispose();

		_pinToggledSubscription = _syncService.Subscribe<string, bool>(
			"PinToggled",
			( pin, state ) =>
			{
				Dispatcher.UIThread.Post( () =>
				{
					Interlocked.Exchange( ref _suppressSyncSend, 1 );
					try
					{
						SetPinState( pin, state );
						AddLogItem( $"Remote PinToggled: Pin={pin}, State={state}" );
					}
					finally
					{
						Interlocked.Exchange( ref _suppressSyncSend, 0 );
					}
				} );
			} );

		// Outputs werden nur gesendet, wenn ein Hardware-Host (Gateway/Workbench) sie reportet.
		_aluOutputsSubscription = _syncService.Subscribe<byte, string, string>(
			"AluOutputsChanged",
			( raw, binary, hex ) =>
			{
				Dispatcher.UIThread.Post( () =>
				{
					Interlocked.Exchange( ref _suppressSyncSend, 1 );
					try
					{
						ApplyAluOutputsToUi( raw );
						AddLogItem( $"Remote ALU Outputs: Raw=0x{hex}, Bin={binary}" );
					}
					finally
					{
						Interlocked.Exchange( ref _suppressSyncSend, 0 );
					}
				} );
			} );

		_ = EnsureSyncConnectedWithLoggingAsync();
		_ = LoadInitialPinStateAsync();
	}

	/// <summary>
	/// Updates the user interface to reflect the current state of the ALU output signals based on the specified raw value.
	/// </summary>
	/// <remarks>Each bit in the <paramref name="raw"/> parameter maps to a distinct ALU output signal: bit 0 to F0,
	/// bit 1 to F1, bit 2 to F2, bit 3 to F3, bit 4 to P, bit 5 to G, bit 6 to AEqualsB, and bit 7 to CN4. Setting a bit
	/// to 1 indicates the corresponding signal is active.</remarks>
	/// <param name="raw">A byte representing the ALU output signals, where each bit corresponds to a specific signal to be displayed on the
	/// UI.</param>
	private void ApplyAluOutputsToUi( byte raw )
	{
		// V2SignalInpALU.PortB: bit0=F0, bit1=F1, bit2=F2, bit3=F3, bit4=P, bit5=G, bit6=A_EQ_B, bit7=CN_4
		SetPinState( "F0", ( raw & 0b0000_0001 ) != 0 );
		SetPinState( "F1", ( raw & 0b0000_0010 ) != 0 );
		SetPinState( "F2", ( raw & 0b0000_0100 ) != 0 );
		SetPinState( "F3", ( raw & 0b0000_1000 ) != 0 );

		SetPinState( "P", ( raw & 0b0001_0000 ) != 0 );
		SetPinState( "G", ( raw & 0b0010_0000 ) != 0 );

		SetPinState( "AEqualsB", ( raw & 0b0100_0000 ) != 0 );
		SetPinState( "CN4", ( raw & 0b1000_0000 ) != 0 );
	}

	/// <summary>
	/// Loads the current pin snapshot (late-joiner state) from <see cref="_syncService"/> and transfers it to the UI.
	/// </summary>
	/// <remarks>
	/// <para> Used in auto-connect so that a client that joins later immediately sees the current state of all pins
	/// without having to perform local interactions first. </para>
	/// <list type="bullet">
	/// <item><description> Ensures that the <see cref="_syncService"/> is connected (<c>EnsureConnectedAsync</c>) 
	/// and then reads the snapshot using <c>GetStateAsync</c>. </description></item>
	/// <item><description> Marshals the update to the UI thread via <see cref="Dispatcher.UIThread"/> 
	/// and sets the pin states via <see cref="SetPinState"/>. </description></item>
	/// <item><description> During the UI update, activates echo suppression via <see cref="Interlocked.Exchange(ref int, int)"/> on
	/// <c>_suppressSyncSend</c> so that the incoming remote changes are not immediately sent back to the hub. </description></item>
	/// <item><description> Logs success/error via <see cref="AddLogItem"/>. </description></item>
	/// </list>
	/// </remarks>
	/// <returns>
	/// A <see cref="System.Threading.Tasks.Task"/> that completes once the snapshot has been loaded and the UI update
	/// has been scheduled (via <see cref="Dispatcher.UIThread.Post(Action)"/>).
	/// </returns>
	private async System.Threading.Tasks.Task LoadInitialPinStateAsync()
	{
		if( _syncService is null )
			return;

		try
		{
			await _syncService.EnsureConnectedAsync();
		}
		catch( Exception ex )
		{
			AddLogItem( $"Snapshot: EnsureConnected Fehler: {ex.Message}" );
			return;
		}

		AluLab.Common.Relay.SyncState? state = null;
		try
		{
			state = await _syncService.GetStateAsync();
		}
		catch( Exception ex )
		{
			AddLogItem( $"Snapshot: GetState Fehler: {ex.Message}" );
			return;
		}

		AluLab.Common.Relay.SyncHub.AluOutputsDto? lastOutputs = null;
		try
		{
			lastOutputs = await _syncService.GetLastOutputsAsync();
		}
		catch( Exception ex )
		{
			// Wichtig: Nicht abbrechen, Inputs sollen trotzdem gesetzt werden.
			AddLogItem( $"Snapshot: GetLastOutputs Fehler (ignoriere): {ex.Message}" );
		}

		Dispatcher.UIThread.Post( () =>
		{
			Interlocked.Exchange( ref _suppressSyncSend, 1 );
			try
			{
				foreach( var kvp in state.Pins )
					SetPinState( kvp.Key, kvp.Value );

				if( lastOutputs is not null )
					ApplyAluOutputsToUi( lastOutputs.Raw );

				AddLogItem( $"Initialer SyncState geladen: {state.Pins.Count} Pins" );
			}
			finally
			{
				Interlocked.Exchange( ref _suppressSyncSend, 0 );
			}
		} );
	}

	/// <summary>
	/// Ensures that <see cref="_syncService"/> is connected and logs the result in the UI.
	/// </summary>
	/// <remarks>
	/// <para> This helper method encapsulates the “connect, but don't crash” process for the view:</para>
	/// <list type="bullet">
	/// <item><description> If <see cref="_syncService"/> is not available (e.g., DI not initialized), 
	/// the method terminates immediately. </description></item>
	/// <item><description> Otherwise, <c>EnsureConnectedAsync</c> is called to establish or reuse a connection. </description></item>
	/// <item><description> Success and error states are logged via <see cref="AddLogItem"/> 
	/// so that the connection status can be tracked in the UI. </description></item>
	/// </list>
	/// <para> Exceptions are deliberately caught, as a failed sync connect should not invalidate the view. </para>
	/// </remarks>
	/// <returns>
	/// A <see cref="System.Threading.Tasks.Task"/> that is completed as soon as the connection attempt has been terminated and logged, if necessary.
	/// </returns>
	private async System.Threading.Tasks.Task EnsureSyncConnectedWithLoggingAsync()
	{
		if( _syncService is null )
			return;

		try
		{
			await _syncService.EnsureConnectedAsync();
			AddLogItem( "SyncService verbunden." );
		}
		catch( Exception ex )
		{
			AddLogItem( $"Sync Connect Fehler: {ex.Message}" );
		}
	}

	/// <summary>
	/// Initializes the pin UI (ellipses) within the view and wires user interactions.
	/// </summary>
	/// <remarks>
	/// <para> For each pin name from <see cref="_pinNames"/>, an attempt is made to retrieve the matching
	/// <see cref="Ellipse"/> element from the XAML using <c>FindControl</c>.
	/// Elements that are not found are skipped (e.g., if a pin is missing in the XAML). </para>
	/// <list type="bullet">
	/// <item><description> Sets the local initial state in <see cref="_pinStates"/> to <c>false</c> (Low). </description></item>
	/// <item><description> Updates the visual representation via <see cref="SetEllipseFill(Ellipse, bool)"/>. </description></item>
	/// <item><description> Registers pointer handlers (<see cref="InputElement.PointerPressed"/>/<see cref="InputElement.PointerReleased"/>)
	/// only for input pins (i.e., pins that are not included in <see cref="_outputPins"/>),
	/// so that output pins are only displayed and not switched interactively. </description></item>
	/// </list>
	/// <para>
	/// The actual distinction between mouse and touch/pen interaction, as well as switching the state,
	/// is done in <see cref="HandlePinPointer(string, Ellipse, PointerEventArgs)"/> and <see cref="TogglePinState(string, Ellipse)"/>.
	/// </para>
	/// </remarks>
	private void InitializePins()
	{
		foreach( var name in _pinNames )
		{
			var ellipse = this.FindControl<Ellipse>( name );
			if( ellipse == null )
			{
				continue;
			}

			_pinStates[ name ] = false;
			SetEllipseFill( ellipse, false );

			if( !_outputPins.Contains( name ) )
			{
				ellipse.PointerPressed += ( s, e ) => HandlePinPointer( name, ellipse, e );
				ellipse.PointerReleased += ( s, e ) => HandlePinPointer( name, ellipse, e );
			}
		}
	}

	/// <summary>
	/// Central pointer handler for a pin (ellipse) and the associated Avalonia pointer events.
	/// </summary>
	/// <remarks>
	/// <para> This method standardizes interaction across different input devices and replicates the behavior expected in the UI: </para>
	/// <list type="bullet">
	/// <item><description> <see cref="PointerType.Mouse"/>: Switch already at <see cref="PointerPressedEventArgs"/> 
	/// (classic “click” behavior). </description></item>
	/// <item><description> <see cref="PointerType.Touch"/>/<see cref="PointerType.Pen"/>: Switch only at <see cref="PointerReleasedEventArgs"/>
	/// (prevents accidental toggles when placing/dragging and feels more natural on touch). </description></item>
	/// </list>
	/// <para> When a toggle is executed, the event is marked as handled (<see cref="RoutedEventArgs.Handled"/>)
	/// to prevent further processing (bubbling/additional handlers). </para>
	/// <para> The actual state change (including UI update, raising of <see cref="PinToggled"/>, and optional sync)
	/// takes place in <see cref="TogglePinState(string, Ellipse)"/>. </para>
	/// </remarks>
	/// <param name="name">The logical pin name (e.g., <c>“A0”</c>) that is listed in <see cref="_pinStates"/>. </param>
	/// <param name="ellipse">The UI element (pin LED) whose fill color represents the state. </param>
	/// <param name="e">The specific pointer event (pressed/released), including pointer type. </param>
	private void HandlePinPointer( string name, Ellipse ellipse, PointerEventArgs e )
	{
		var pointerType = e.Pointer.Type;

		if( pointerType == PointerType.Mouse )
		{
			if( e is PointerPressedEventArgs )
			{
				TogglePinState( name, ellipse );
				e.Handled = true;
			}
		}
		else if( pointerType == PointerType.Touch || pointerType == PointerType.Pen )
		{
			if( e is PointerReleasedEventArgs )
			{
				TogglePinState( name, ellipse );
				e.Handled = true;
			}
		}
	}

	/// <summary>
	/// Switches the state of a pin (UI + internal state tracking) and propagates the change.
	/// </summary>
	/// <remarks>
	/// <para> This method is the central switching point for local interactions (e.g., via pointer/touch).
	/// It updates both the internal state and the visual representation and then triggers/ the relevant notifications. </para>
	/// <list type="bullet">
	/// <item><description> Reads the current state from <see cref="_pinStates"/>; 
	/// unknown pins are defensively initialized with <c>false</c>. </description></item>
	/// <item><description> Updates <see cref="_pinStates"/> and sets the LED color of the pin 
	/// via <see cref="SetEllipseFill(Ellipse, bool)"/>. </description></item>
	/// <item><description> Triggers <see cref="PinToggled"/> 
	/// so that local listeners (e.g., logic/tests) can process the change. </description></item>
	/// <item><description> Optionally sends the change to the <c>SyncService</c> (via <see cref="SendPinToggledToSyncAsync(string, bool)"/>),
	/// but only if echo suppression is not active.
	/// Suppression is thread-safe via <see cref="Volatile. Read(ref int)"/> on <see cref="_suppressSyncSend"/>,
	/// to avoid feedback (remote &gt; UI update must not send back to remote). </description></item>
	/// </list>
	/// </remarks>
	/// <param name="name">Logical pin name (e.g., <c>“A0”</c>), key in <see cref="_pinStates"/>. </param>
	/// <param name="ellipse">The associated UI LED (<see cref="Ellipse"/>), whose fill represents the status. </param>
	private void TogglePinState( string name, Ellipse ellipse )
	{
		if( !_pinStates.TryGetValue( name, out var currentState ) )
		{
			currentState = false;
			_pinStates[ name ] = currentState;
		}

		var newState = !currentState;
		_pinStates[ name ] = newState;

		SetEllipseFill( ellipse, newState );

		PinToggled?.Invoke( this, new PinToggledEventArgs( name, newState ) );

		// Klarer Read, statt CompareExchange als "Pseudo-Read"
		if( Volatile.Read( ref _suppressSyncSend ) == 0 )
		{
			_ = SendPinToggledToSyncAsync( name, newState );
		}
	}

	/// <summary>
	/// Sends a local pin status change to the <see cref="SyncService"/> (e.g., SignalR hub)
	/// so that other clients can adopt the change in real time.
	/// </summary>
	/// <remarks>
	/// <para> The method is deliberately implemented defensively: Connection and transmission errors are intercepted and written to the UI logs
	/// so that the view remains functional in the event of temporary network problems. </para>
	/// <list type="bullet">
	/// <item><description> First checks whether <see cref="_syncService"/> is available (DI/initialization). </description></item>
	/// <item><description> Ensures that a connection exists via <c>EnsureConnectedAsync</c>; in case of errors, 
	/// the error is logged and the process is aborted. </description></item>
	/// <item><description> Then sends the event via <see cref="SyncService.SendPinToggledAsync(string, bool)"/> 
	/// and logs success/error. </description></item>
	/// </list>
	/// <para> Note: Echo suppression (preventing remotely received events from being sent back)
	/// is not decided in this method, but already when the method is called (see <c>_suppressSyncSend</c> in <see cref="TogglePinState"/>). </para>
	/// </remarks>
	/// <param name="pin">The name of the pin whose state is to be synchronized.</param>
	/// <param name="state">The new state of the pin (<c>true</c> = High, <c>false</c> = Low).</param>
	/// <returns>
	/// A <see cref="System.Threading.Tasks.Task"/> that is complete as soon as (optionally) connected and the send attempt is finished.
	/// </returns>
	private async System.Threading.Tasks.Task SendPinToggledToSyncAsync( string pin, bool state )
	{
		if( _syncService is null )
			return;

		try
		{
			await _syncService.EnsureConnectedAsync();
		}
		catch( Exception ex )
		{
			AddLogItem( $"Sync Connect Fehler: {ex.Message} (Pin={pin}, State={state})" );
			return;
		}

		try
		{
			await _syncService.SendPinToggledAsync( pin, state );
			AddLogItem( $"Sync gesendet: Pin={pin}, State={state}" );
		}
		catch( Exception ex )
		{
			AddLogItem( $"Sync Fehler: {ex.Message} (Pin={pin}, State={state})" );
		}
	}

	/// <summary>
	/// Sets the fill color (LED status) of a pin <see cref="Ellipse"/> depending on the logic level.
	/// </summary>
	/// <remarks>
	/// <para> This helper method encapsulates the central visualization of a pin state:
	/// <c>true</c> (High) is represented by <see cref="_brushHigh"/>, <c>false</c> (Low) by <see cref="_brushLow"/>. </para>
	/// <para> It is used by both local interactions (e.g., <see cref="TogglePinState(string, Ellipse)"/>)
	/// and remote/snapshot updates (e.g., <see cref="SetPinState(string, bool)"/>) to consistently update the UI. </para>
	/// <para> Note: The method only updates the UI element 
	/// and does not perform synchronization or status management in <see cref="_pinStates"/>. </para>
	/// </remarks>
	/// <param name="ellipse">The pin LED to be displayed as <see cref="Ellipse"/>.</param>
	/// <param name="high">The logic level: <c>true</c> = High, <c>false</c> = Low.</param>
	private void SetEllipseFill( Ellipse ellipse, bool high )
	{
		ellipse.Fill = high ? _brushHigh : _brushLow;
	}

	/// <summary>
	/// Sets the state (high/low) of a pin programmatically and updates the UI (LED color).
	/// </summary>
	/// <remarks>
	/// <para> This method is intended for incoming state changes that do not originate from a local pointer interaction,
	/// e.g., through remote sync events or an initial snapshot. It only does the following: </para>
	/// <list type="bullet">
	/// <item><description>Find the associated <see cref="Ellipse"/> element based on the pin name in the XAML.</description></item>
	/// <item><description>Updating the internal state in <see cref="_pinStates"/>.</description></item>
	/// <item><description>Visually updating the LED color via <see cref="SetEllipseFill(Ellipse, bool)"/>.</description></item>
	/// </list>
	/// <para> If the pin does not exist in the XAML, the change is discarded and written to the log for diagnostic purposes.
	/// No exceptions are thrown deliberately so that remote messages with unknown pins do not affect the view. </para>
	/// <para> Note: This method intentionally does not trigger <see cref="PinToggled"/> or a sync transmission.
	/// The sync/echo suppression logic is controlled elsewhere (e.g., in <c>StartSyncAutoConnect</c>/<c>LoadInitialPinStateAsync</c>). </para>
	/// </remarks>
	/// <param name="name">Logical pin name (must correspond to a named <see cref="Ellipse"/> element in the XAML).</param>
	/// <param name="high"><c>true</c> for high, <c>false</c> for low.</param>
	public void SetPinState( string name, bool high )
	{
		var ellipse = this.FindControl<Ellipse>( name );
		if( ellipse == null )
		{
			// Hilft beim Debuggen, wenn Remote Pins sendet, die im XAML fehlen
			AddLogItem( $"SetPinState ignoriert: Unbekannter Pin im UI: {name}" );
			return;
		}

		_pinStates[ name ] = high;
		SetEllipseFill( ellipse, high );
	}

	/// <summary>
	/// Returns the currently cached state (High/Low) of a pin.
	/// </summary>
	/// <remarks>
	/// <para> The pin states are maintained in <see cref="_pinStates"/> and are updated, among other things, 
	/// by local interactions (see <see cref="TogglePinState(string, Ellipse)"/>) and remote/snapshot updates
	/// (see <see cref="SetPinState(string, bool)"/>). </para>
	/// <para> If the requested pin does not exist in the dictionary, <c>false</c> is returned defensively
	/// (corresponds to “Low”). No new entry is deliberately created. </para>
	/// </remarks>
	/// <param name="name">Logical pin name (e.g., <c>“A0”</c>), key in <see cref="_pinStates"/>.</param>
	/// <returns><c>true</c> if the pin is “High”; otherwise <c>false</c>.</returns>

	public bool GetPinState( string name )
	{
		return _pinStates.TryGetValue( name, out var v ) && v;
	}


	/// <summary>
	/// Processes a touch coordinate (window coordinates) and toggles the first matching input pin.
	/// </summary>
	/// <remarks>
	/// <para> This method is used when touch events are not processed directly via the individual pin controls (ellipses)
	/// but are transferred to the view as global coordinates <paramref name="x"/>/<paramref name="y"/>
	/// (e.g., by a higher-level touch or overlay logic). </para>
	/// <para> Procedure: </para>
	/// <list type="number">
	/// <item><description> Determines the current <see cref="Window"/> via <see cref="Visual.VisualRoot"/>. Without a window, 
	/// no meaningful coordinate transformation is possible, so the process is terminated early. </description></item>
	/// <item><description> Iterates over all pins from <see cref="_pinNames"/>, skipping pins that are marked as output in
	/// <see cref="_outputPins"/> (outputs are for display only, not interactive). </description></item>
	/// <item><description> Searches for the corresponding <see cref="Ellipse"/> using <c>FindControl</c>. 
	/// If a pin is missing in the XAML, it is ignored. </description></item>
	/// <item><description> Transforms the local ellipse position via <see cref="Visual.TranslatePoint(Point, Visual)"/> into
	/// window coordinates and calculates the center point and effective radius (from <see cref="Visual.Bounds"/>). </description></item>
	/// <item><description> Performs a simple circle hit test (<c>dx² + dy² &lt;= r²</c>). If the touch hits, the pin is toggled via
	/// <see cref="TogglePinState(string, Ellipse)"/> and the loop is terminated (only one pin per touch is changed). </description></item>
	/// </list>
	/// <para> Note: Echo suppression and sync forwarding are not controlled here, 
	/// but within <see cref="TogglePinState(string, Ellipse)"/>. </para>
	/// </remarks>
	/// <param name="x">Touch X position in window coordinates.</param>
	/// <param name="y">Touch Y position in window coordinates.</param>
	public void ProcessTouch( double x, double y )
	{
		var window = this.VisualRoot as Window;
		if( window == null )
		{
			return;
		}

		var touchX = x;
		var touchY = y;

		foreach( var name in _pinNames )
		{
			if( _outputPins.Contains( name ) )
				continue;

			var ellipse = this.FindControl<Ellipse>( name );
			if( ellipse == null )
				continue;

			var pos = ellipse.TranslatePoint( new Point( 0, 0 ), window );
			if( !pos.HasValue )
				continue;

			double bx = pos.Value.X;
			double by = pos.Value.Y;
			double bw = ellipse.Bounds.Width;
			double bh = ellipse.Bounds.Height;

			double centerX = bx + bw / 2.0;
			double centerY = by + bh / 2.0;
			double dx = touchX - centerX;
			double dy = touchY - centerY;
			double radius = Math.Max( bw, bh ) / 2.0;

			if( dx * dx + dy * dy <= radius * radius )
			{
				TogglePinState( name, ellipse );
				break;
			}
		}
	}

	/// <summary>
	/// Click handler for the (temporary) Connect button: initiates the local ViewModel connection and sets up
	/// remote synchronization (PinToggled subscription) so that incoming remote events are immediately visible in the UI.
	/// </summary>
	/// <remarks>
	/// <para> Sync/AutoConnect normally already takes place in this view in <see cref="StartSyncAutoConnect"/>;
	/// this handler is primarily intended for testing/debugging and replicates the setup: </para>
	/// <list type="bullet">
	/// <item><description> Executes <c>ConnectCommand</c> of <see cref="HousingViewModel"/> 
	/// (if available) to initialize the local connection. </description></item>
	/// <item><description> Proactively establishes a connection to <see cref="SyncService"/> (<see cref="EnsureSyncConnectedWithLoggingAsync"/>)
	/// so that remote activity can be received without further local interaction. </description></item>
	/// <item><description> Disposes any existing subscription (<see cref="_pinToggledSubscription"/>) 
	/// to avoid duplicate handlers. </description></item>
	/// <item><description> Subscribe to the remote event <c>“PinToggled”</c> and marshal the processing via 
	/// <see cref="Dispatcher.UIThread"/> to the UI thread. </description></item>
	/// <item><description> Activates echo suppression during UI update via 
	/// <see cref="Interlocked.Exchange(ref int, int)"/> on <see cref="_suppressSyncSend"/> 
	/// so that an incoming remote update is not immediately sent back to the hub (avoid feedback loop). </description></item>
	/// <item><description> Updates the pin state purely visually/programmatically via 
	/// <see cref="SetPinState(string, bool)"/> and writes diagnostic logs via <see cref="AddLogItem(string)"/>. </description></item>
	/// </list>
	/// <para> Error handling is performed indirectly in <see cref="EnsureSyncConnectedWithLoggingAsync"/> and through defensive null checks
	/// (missing <see cref="_syncService"/> terminates the handler prematurely). </para>
	/// </remarks>
	private void OnConnectClick( object? sender, RoutedEventArgs e )
	{
		_viewModel?.ConnectCommand?.Execute( null );

		if( _syncService is null )
			return;

		// Wichtig: Verbindung proaktiv herstellen, damit Remote-Aktivität sofort ankommt
		_ = EnsureSyncConnectedWithLoggingAsync();

		_pinToggledSubscription?.Dispose();

		_pinToggledSubscription = _syncService.Subscribe<string, bool>(
			"PinToggled",
			( pin, state ) =>
			{
				Dispatcher.UIThread.Post( () =>
				{
					Interlocked.Exchange( ref _suppressSyncSend, 1 );
					try
					{
						SetPinState( pin, state );
						AddLogItem( $"Remote PinToggled: Pin={pin}, State={state}" );
					}
					finally
					{
						Interlocked.Exchange( ref _suppressSyncSend, 0 );
					}
				} );
			} );
	}

	/// <summary>
	/// Adds a log entry and updates the log properties bound to the UI.
	/// </summary>
	/// <remarks>
	/// <para> The text is stamped with a timestamp (<c>HH:mm:ss</c>) and inserted at the beginning of <see cref="_logItems"/>
	/// so that the latest entry can be displayed first. </para>
	/// <para> In addition, <see cref="LogsText"/> is maintained as contiguous text (multiline) 
	/// by placing the new entry before the previous content. This allows the UI 
	/// to display either a list (<see cref="LogItems"/>) or a text block (<see cref="LogsText"/>). </para>
	/// <para> Note: The method does not execute thread marshalling logic. Calls from non-UI threads must first
	/// be dispatched to the UI thread (e.g., via <see cref="Dispatcher.UIThread.Post(System.Action)"/>). </para>
	/// </remarks>
	/// <param name="text">The message to be logged without a timestamp. </param>
	private void AddLogItem( string text )
	{
		var entry = $"{DateTime.Now:HH:mm:ss} - {text}";
		_logItems.Insert( 0, entry );

		LogsText = entry + Environment.NewLine + LogsText;
	}

	/// <summary>
	/// Sets a property field and triggers a <see cref="PropertyChanged"/> notification when an actual change occurs.
	/// </summary>
	/// <remarks>
	/// <para> This helper method supports the <see cref="INotifyPropertyChanged"/> implementation of the view and is used, 
	/// for example, by the setter of <see cref="LogsText"/>. </para>
	/// <list type="bullet">
	/// <item><description> Compares the current field value with the new value using <see cref="EqualityComparer{T}.Default"/>
	/// to avoid unnecessary UI updates. </description></item>
	/// <item><description> Updates the underlying field and then fires 
	/// <see cref="PropertyChanged"/> with the affected property name. </description></item>
	/// <item><description> The property name is automatically determined via 
	/// <see cref="CallerMemberNameAttribute"/> unless explicitly passed. </description></item>
	/// </list>
	/// <para> Note: The method does not perform thread marshalling. 
	/// Callers must ensure that property changes (if necessary) are made on the UI thread. </para>
	/// </remarks>
	/// <typeparam name="T">Type of the property/field value. </typeparam>
	/// <param name="field">Reference to the backing field to be updated. </param>
	/// <param name="value">New value to be set.</param>
	/// <param name="propertyName"> Name of the property; is usually set automatically and only needs to be specified in special cases. </param>
	/// <returns><c>true</c> if the value has been changed; otherwise <c>false</c>.</returns>
	protected bool SetProperty<T>( ref T field, T value, [CallerMemberName] string? propertyName = null )
	{
		if( EqualityComparer<T>.Default.Equals( field, value ) )
			return false;

		field = value;
		PropertyChanged?.Invoke( this, new System.ComponentModel.PropertyChangedEventArgs( propertyName ) );
		return true;
	}

	public new event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Event arguments for switching a pin (locally or remotely).
/// </summary>
/// <remarks>
/// <para> Used by <see cref="HousingView.PinToggled"/> to transport the name of the affected pin and its new state. 
/// The payload is deliberately kept to a minimum so that it can be easily logged, serialized, 
/// and passed on via synchronization mechanisms (e.g., <c>SyncService</c>). </para>
/// <para>
/// <see cref="PinName"/> corresponds to an entry from the pin list of the view (e.g., <c>“A0”</c>, <c>“CN”</c>).
/// <see cref="State"/> is <c>true</c> for high and <c>false</c> for low.
/// </para>
/// </remarks>
/// <param name="PinName">Name/identifier of the pin. </param>
/// <param name="State">New pin state (<c>true</c> = High, <c>false</c> = Low). </param>
public sealed record PinToggledEventArgs( string PinName, bool State );