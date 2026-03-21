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
using Avalonia.Threading;
using AluLab.Common.Services;
using AluLab.Common.ViewModels;

namespace AluLab.Common.Views;

/// <summary>
/// UI control that renders an ALU “housing” (pinout) and provides interactive pin toggling plus real-time
/// synchronization via <see cref="SyncService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The view maintains a local pin state cache (<c>_pinStates</c>) and updates the UI by finding the corresponding
/// <see cref="Ellipse"/> for each pin name (from <c>_pinNames</c>). Pins listed in <c>_outputPins</c> are treated as
/// read-only outputs and are not directly user-toggleable.
/// </para>
/// <para>
/// Synchronization:
/// <list type="bullet">
/// <item>
/// <description>
/// Remote updates arrive via <see cref="SyncService"/> subscriptions (pin toggles, ALU outputs, and snapshots) and are
/// applied on the Avalonia UI thread via <see cref="Dispatcher.UIThread"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// When applying remote updates, <c>_suppressSyncSend</c> is set to prevent echoing those changes back to the server.
/// Local user interactions only send sync messages when suppression is not active.
/// </description>
/// </item>
/// </list>
/// </para>
/// <para>
/// Operation selection rules:
/// <list type="bullet">
/// <item>
/// <description>
/// Pins <c>M</c> and <c>CN</c> are externally controlled, read-only inputs. They are reflected into the operation UI
/// but must never be set as a side-effect of operation selection.
/// </description>
/// </item>
/// <item>
/// <description>
/// The operation selector is allowed to drive only <c>S0..S3</c>. Changes to <c>S0..S3</c> update the selector, and
/// selector changes update only <c>S0..S3</c>.
/// </description>
/// </item>
/// </list>
/// </para>
/// </remarks>
public partial class HousingView : UserControl, INotifyPropertyChanged
{
	// ViewModel and service references for data binding and synchronization.
	/// <summary>
	/// View model used as the enforced <see cref="UserControl.DataContext"/> for bindings and commands.
	/// </summary>
	private readonly HousingViewModel? _viewModel;

	/// <summary>
	/// Real-time synchronization service used to publish local input changes and apply remote state changes.
	/// </summary>
	private readonly SyncService? _syncService;

	/// <summary>Subscription for remote "PinToggled" events.</summary>
	private IDisposable? _pinToggledSubscription;

	/// <summary>Subscription for remote "AluOutputsChanged" events.</summary>
	private IDisposable? _aluOutputsSubscription;

	/// <summary>Subscription for diagnostic log events from <see cref="SyncService"/>.</summary>
	private IDisposable? _syncLogSubscription;

	/// <summary>Subscription for snapshot pin state pushes.</summary>
	private IDisposable? _snapshotPinsSubscription;

	/// <summary>Subscription for snapshot output state pushes.</summary>
	private IDisposable? _snapshotOutputsSubscription;

	/// <summary>
	/// Send-suppression flag used to avoid sending sync messages when the UI is being updated from remote state.
	/// </summary>
	/// <remarks>
	/// This is an <see cref="int"/> so it can be manipulated atomically via <see cref="Interlocked"/> and read via
	/// <see cref="Volatile"/>.
	/// </remarks>
	private int _suppressSyncSend;

	/// <summary>
	/// Most-recent-first log entries for use by an overlay or list UI.
	/// </summary>
	private readonly ObservableCollection<string> _logItems = new();

	/// <summary>
	/// Read-only projection of <see cref="_logItems"/> for binding/inspection.
	/// </summary>
	public IReadOnlyCollection<string> LogItems => _logItems;

	private string _logsText = string.Empty;

	/// <summary>
	/// Aggregated log output as a single string (newest entry prepended).
	/// </summary>
	/// <remarks>
	/// This is updated by <see cref="AddLogItem(string)"/> and raises <see cref="PropertyChanged"/>.
	/// </remarks>
	public string LogsText
	{
		get => _logsText;
		private set => SetProperty( ref _logsText, value );
	}

	/// <summary>
	/// Raised when a user toggles an input pin locally via pointer/touch interaction.
	/// </summary>
	/// <remarks>
	/// This event reflects local user intent. Remote updates applied through sync do not raise this event.
	/// </remarks>
	public event EventHandler<PinToggledEventArgs>? PinToggled;

	/// <summary>
	/// Local cache of pin high/low state keyed by pin name (e.g. <c>"A0"</c>, <c>"S3"</c>, <c>"F1"</c>).
	/// </summary>
	private readonly Dictionary<string, bool> _pinStates = new();

	/// <summary>
	/// Pin names that are treated as output-only in the UI (read-only, set via ALU results/sync snapshots).
	/// </summary>
	private readonly HashSet<string> _outputPins = new()
	{
		"F3", "F2", "F1", "F0", "P", "G", "AEqualsB", "CN4"
	};

	/// <summary>
	/// Canonical list of all pins that are expected to exist in the visual tree as <see cref="Ellipse"/> controls.
	/// </summary>
	/// <remarks>
	/// Pin visuals are found by name (see <see cref="ControlExtensions.FindControl{T}(Avalonia.Controls.Control,string)"/>).
	/// </remarks>
	private readonly string[] _pinNames = new[]
	{
		"A3","A2","A1","A0",
		"B3","B2","B1","B0",
		"S3","S2","S1","S0",
		"CN","M",
		"F3","F2","F1","F0",
		"P","G","AEqualsB","CN4"
	};

	/// <summary>Default fill brush for a low (inactive) pin.</summary>
	private static readonly SolidColorBrush s_brushLowFill = new( Colors.AliceBlue );

	/// <summary>Default stroke brush for a low (inactive) pin.</summary>
	private static readonly SolidColorBrush s_brushLowStroke = new( Colors.Gray );

	/// <summary>
	/// Per-pin high-state styles (fill + stroke) used to color pins by semantic group (A/B/S/F/etc.).
	/// </summary>
	private static readonly IReadOnlyDictionary<string, PinStyle> s_stylesByPin = CreateStylesByPin();

	/// <summary>
	/// Visual styling for a pin when high (active).
	/// </summary>
	private sealed record PinStyle( SolidColorBrush HighFill, SolidColorBrush HighStroke );

	/// <summary>
	/// Initializes the view, wires up pin controls, resolves dependencies, and sets up attach/detach behavior.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Dependency resolution:
	/// pulls <see cref="HousingViewModel"/> and <see cref="SyncService"/> from <see cref="App.Services"/>.
	/// </para>
	/// <para>
	/// Data context:
	/// enforces that <see cref="DataContext"/> is a <see cref="HousingViewModel"/> (reverts external changes).
	/// </para>
	/// <para>
	/// Visual tree integration:
	/// on attach → starts sync subscriptions and initial connection;
	/// on detach → disposes subscriptions and disposes the view model.
	/// </para>
	/// </remarks>
	public HousingView()
	{
		InitializeComponent();
		InitializePins();

		App app = ( App )Application.Current!;
		_viewModel = app.Services.GetRequiredService<HousingViewModel>();
		_syncService = app.Services.GetRequiredService<SyncService>();

		if( DataContext is null )
		{
			DataContext = _viewModel;
		}

		DataContextChanged += ( _, __ ) =>
		{
			// Enforce the expected VM type since this control relies on internal services + bindings.
			if( DataContext is not HousingViewModel )
				DataContext = _viewModel;
		};

		Debug.WriteLine( $"ConnectCommand is null? {_viewModel?.ConnectCommand == null}" );
		Debug.WriteLine( $"DisconnectCommand is null? {_viewModel?.DisconnectCommand == null}" );

		// Clean up subscriptions and dispose ViewModel when detached from visual tree.
		DetachedFromVisualTree += async ( _, __ ) =>
		{
			_pinToggledSubscription?.Dispose();
			_pinToggledSubscription = null;

			_aluOutputsSubscription?.Dispose();
			_aluOutputsSubscription = null;

			_snapshotPinsSubscription?.Dispose();
			_snapshotPinsSubscription = null;

			_snapshotOutputsSubscription?.Dispose();
			_snapshotOutputsSubscription = null;

			_syncLogSubscription?.Dispose();
			_syncLogSubscription = null;

			if( _viewModel is null )
				return;

			await _viewModel.DisposeAsync();
		};

		// Start synchronization when attached to the visual tree.
		AttachedToVisualTree += ( _, __ ) => StartSyncAutoConnect();
	}

	/// <summary>
	/// Creates/recreates synchronization subscriptions and kicks off the initial connect + state load.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Subscriptions are disposed/replaced to avoid duplicate handlers if the view is re-attached.
	/// </para>
	/// <para>
	/// Remote updates are applied within a suppression scope (<c>_suppressSyncSend</c>) to prevent echoing those changes
	/// back through the sync service.
	/// </para>
	/// </remarks>
	private void StartSyncAutoConnect()
	{
		if( _syncService is null )
			return;

		// Always clear existing subscriptions to ensure idempotent re-attach behavior.
		_pinToggledSubscription?.Dispose();
		_aluOutputsSubscription?.Dispose();

		_snapshotPinsSubscription?.Dispose();
		_snapshotOutputsSubscription?.Dispose();

		_syncLogSubscription?.Dispose();
		_syncLogSubscription = SubscribeSyncLog( _syncService );

		_snapshotPinsSubscription = SubscribeSnapshotPins( _syncService );
		_snapshotOutputsSubscription = SubscribeSnapshotOutputs( _syncService );

		// Remote single-pin changes -> apply on UI thread without re-sending.
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

		// Remote ALU outputs -> map bitfield to output pins on UI thread without re-sending.
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

		_ = ConnectAndLoadInitialStateAsync();
	}

	/// <summary>
	/// Subscribes to log events from the specified synchronization service and posts log messages to the UI thread for
	/// display.
	/// </summary>
	/// <remarks>
	/// This method ensures that log messages are posted to the UI thread, making it safe to update UI elements in
	/// response to log events in multi-threaded scenarios.
	/// </remarks>
	/// <param name="syncService">The synchronization service to subscribe to for log events. This parameter must not be null.</param>
	/// <returns>An <see cref="IDisposable"/> that unsubscribes from the log events when disposed.</returns>
	private IDisposable SubscribeSyncLog( SyncService syncService )
	{
		void Handler( string message )
		{
			Dispatcher.UIThread.Post( () => AddLogItem( message ) );
		}

		syncService.Log += Handler;
		return new DelegateDisposable( () => syncService.Log -= Handler );
	}

	/// <summary>
	/// Subscribes to snapshot state updates from the specified synchronization service and applies received pin states to
	/// the UI in a thread-safe manner.
	/// </summary>
	/// <remarks>
	/// This method posts pin state updates to the UI thread to maintain thread safety. Updates are applied inside a
	/// suppression scope to avoid re-sending the snapshot changes back through the sync channel.
	/// </remarks>
	/// <param name="syncService">The synchronization service that provides snapshot state updates. Cannot be null.</param>
	/// <returns>An <see cref="IDisposable"/> that unsubscribes from the snapshot state updates when disposed.</returns>
	private IDisposable SubscribeSnapshotPins( SyncService syncService )
	{
		void Handler( AluLab.Common.Relay.SyncState state )
		{
			Dispatcher.UIThread.Post( () =>
			{
				Interlocked.Exchange( ref _suppressSyncSend, 1 );
				try
				{
					foreach( var kvp in state.Pins )
						SetPinState( kvp.Key, kvp.Value );

					AddLogItem( $"SnapshotPins applied: {state.Pins.Count} Pins" );
				}
				finally
				{
					Interlocked.Exchange( ref _suppressSyncSend, 0 );
				}
			} );
		}

		syncService.SnapshotStateReceived += Handler;
		return new DelegateDisposable( () => syncService.SnapshotStateReceived -= Handler );
	}

	/// <summary>
	/// Subscribes to snapshot output updates from the specified synchronization service and applies received output state
	/// to the UI in a thread-safe manner.
	/// </summary>
	/// <remarks>
	/// This method posts output updates to the UI thread. Received output snapshots are applied by mapping the raw output
	/// bitfield to the output pins (see <see cref="ApplyAluOutputsToUi(byte)"/>). Updates are applied inside a
	/// suppression scope to avoid re-sending the snapshot changes back through the sync channel.
	/// </remarks>
	/// <param name="syncService">The <see cref="SyncService"/> instance that provides the snapshot output data. Cannot be null.</param>
	/// <returns>An <see cref="IDisposable"/> that can be used to unsubscribe from the SnapshotOutputsReceived event.</returns>
	private IDisposable SubscribeSnapshotOutputs( SyncService syncService )
	{
		void Handler( AluLab.Common.Relay.SyncHub.AluOutputsDto? dto )
		{
			Dispatcher.UIThread.Post( () =>
			{
				Interlocked.Exchange( ref _suppressSyncSend, 1 );
				try
				{
					var raw = dto?.Raw ?? ( byte )0;
					ApplyAluOutputsToUi( raw );

					AddLogItem( dto is null
						? "SnapshotOutputs applied: null -> Raw=0x00"
						: $"SnapshotOutputs applied: Raw=0x{dto.Hex}" );
				}
				finally
				{
					Interlocked.Exchange( ref _suppressSyncSend, 0 );
				}
			} );
		}

		syncService.SnapshotOutputsReceived += Handler;
		return new DelegateDisposable( () => syncService.SnapshotOutputsReceived -= Handler );
	}

	/// <summary>
	/// Small helper that converts an <see cref="Action"/> into an <see cref="IDisposable"/>.
	/// </summary>
	/// <remarks>
	/// Uses <see cref="Interlocked.Exchange(ref Action?, Action?)"/> to ensure the dispose action is invoked at most once.
	/// </remarks>
	private sealed class DelegateDisposable : IDisposable
	{
		private Action? _dispose;

		public DelegateDisposable( Action dispose ) => _dispose = dispose;

		public void Dispose()
		{
			Interlocked.Exchange( ref _dispose, null )?.Invoke();
		}
	}

	/// <summary>
	/// Ensures the sync connection is established and relies on server push snapshot subscriptions to populate initial
	/// state.
	/// </summary>
	/// <remarks>
	/// This method only performs connection and logging; it does not directly request state. The initial pin/output state
	/// is expected to arrive via <see cref="SubscribeSnapshotPins(SyncService)"/> and
	/// <see cref="SubscribeSnapshotOutputs(SyncService)"/>.
	/// </remarks>
	private async System.Threading.Tasks.Task ConnectAndLoadInitialStateAsync()
	{
		try
		{
			AddLogItem( "Sync: ConnectAndLoadInitialStateAsync start" );

			await EnsureSyncConnectedWithLoggingAsync();

			AddLogItem( $"Sync: IsConnected={_syncService?.IsConnected}" );
			AddLogItem( "Sync: Initial-Snapshot über Server-Push (SnapshotPins/SnapshotOutputs)." );
			AddLogItem( "Sync: ConnectAndLoadInitialStateAsync end" );
		}
		catch( Exception ex )
		{
			AddLogItem( $"Sync: ConnectAndLoadInitialStateAsync Fehler: {ex}" );
		}
	}

	/// <summary>
	/// Connects to the sync backend (if not already connected) and logs the outcome.
	/// </summary>
	/// <remarks>
	/// Exceptions are handled locally and logged; callers can treat failures as non-fatal.
	/// </remarks>
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
	/// Applies the ALU output bitfield to the corresponding output pin visuals.
	/// </summary>
	/// <param name="raw">
	/// Raw output byte where bits map to:
	/// <c>F0..F3</c> (bits 0..3), <c>P</c> (bit 4), <c>G</c> (bit 5), <c>AEqualsB</c> (bit 6), <c>CN4</c> (bit 7).
	/// </param>
	/// <remarks>
	/// This updates only output pins and then recomputes the displayed F nibble (<see cref="UpdateResultFFromPins"/>).
	/// </remarks>
	private void ApplyAluOutputsToUi( byte raw )
	{
		SetPinState( "F0", ( raw & 0b0000_0001 ) != 0 );
		SetPinState( "F1", ( raw & 0b0000_0010 ) != 0 );
		SetPinState( "F2", ( raw & 0b0000_0100 ) != 0 );
		SetPinState( "F3", ( raw & 0b0000_1000 ) != 0 );

		SetPinState( "P", ( raw & 0b0001_0000 ) != 0 );
		SetPinState( "G", ( raw & 0b0010_0000 ) != 0 );

		SetPinState( "AEqualsB", ( raw & 0b0100_0000 ) != 0 );
		SetPinState( "CN4", ( raw & 0b1000_0000 ) != 0 );

		UpdateResultFFromPins();
	}

	/// <summary>
	/// Locates pin visuals by name, initializes their state to low, and wires pointer handlers for input pins.
	/// </summary>
	/// <remarks>
	/// Output pins (as defined by <see cref="_outputPins"/>) are not assigned pointer handlers.
	/// After initialization, derived UI elements (operands/result/operation selector) are brought into sync with the pin
	/// cache.
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
			SetEllipseFill( name, ellipse, high: false );

			if( !_outputPins.Contains( name ) )
			{
				ellipse.PointerPressed += ( s, e ) => HandlePinPointer( name, ellipse, e );
				ellipse.PointerReleased += ( s, e ) => HandlePinPointer( name, ellipse, e );
			}
		}

		UpdateOperandAFromPins();
		UpdateOperandBFromPins();
		UpdateResultFFromPins();
		InitializeOperationSelector();
	}

	/// <summary>
	/// Normalizes pointer input across mouse/touch/pen so that pins toggle once per user gesture.
	/// </summary>
	/// <remarks>
	/// Mouse toggles on press; touch/pen toggles on release to reduce accidental toggles while dragging.
	/// </remarks>
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
	/// Toggles a local input pin state, updates dependent UI, raises <see cref="PinToggled"/>, and optionally publishes
	/// the change to <see cref="SyncService"/>.
	/// </summary>
	/// <remarks>
	/// Sync publication is skipped when <c>_suppressSyncSend</c> is active (i.e., when applying remote state).
	/// </remarks>
	private void TogglePinState( string name, Ellipse ellipse )
	{
		if( !_pinStates.TryGetValue( name, out var currentState ) )
		{
			currentState = false;
			_pinStates[ name ] = currentState;
		}

		var newState = !currentState;
		_pinStates[ name ] = newState;

		SetEllipseFill( name, ellipse, newState );

		if( name is "A0" or "A1" or "A2" or "A3" )
			UpdateOperandAFromPins();

		if( name is "B0" or "B1" or "B2" or "B3" )
			UpdateOperandBFromPins();

		if( name is "S0" or "S1" or "S2" or "S3" or "M" or "CN" )
			UpdateOperationSelectorFromPins();

		PinToggled?.Invoke( this, new PinToggledEventArgs( name, newState ) );

		if( Volatile.Read( ref _suppressSyncSend ) == 0 )
		{
			_ = SendPinToggledToSyncAsync( name, newState );
		}
	}

	/// <summary>
	/// Applies the correct visual style to a pin ellipse based on its high/low state.
	/// </summary>
	/// <remarks>
	/// High pins receive a pin-specific color and a glow (<see cref="DropShadowEffect"/>). Low pins use the shared
	/// <see cref="s_brushLowFill"/>/<see cref="s_brushLowStroke"/> with no effect.
	/// </remarks>
	private void SetEllipseFill( string pinName, Ellipse ellipse, bool high )
	{
		if( !high )
		{
			ellipse.Fill = s_brushLowFill;
			ellipse.Effect = null;
			ellipse.Stroke = s_brushLowStroke;
			return;
		}

		if( !s_stylesByPin.TryGetValue( pinName, out var style ) )
		{
			style = new PinStyle( new SolidColorBrush( Colors.LimeGreen ), new SolidColorBrush( Colors.Green ) );
		}

		ellipse.Fill = style.HighFill;
		ellipse.Effect = new DropShadowEffect
		{
			Color = style.HighFill.Color,
			BlurRadius = 24,
			OffsetX = 0,
			OffsetY = 0,
			Opacity = 1,
		};
		ellipse.Stroke = style.HighStroke;
	}

	/// <summary>
	/// Sets a pin state programmatically (typically from sync or derived output computation) and updates dependent UI.
	/// </summary>
	/// <remarks>
	/// Unlike <see cref="TogglePinState(string,Ellipse)"/>, this does not raise <see cref="PinToggled"/> and does not
	/// send sync messages. It is the core rendering method used both for remote and local updates.
	/// </remarks>
	private void SetPinState( string name, bool high )
	{
		var ellipse = this.FindControl<Ellipse>( name );
		if( ellipse == null )
		{
			AddLogItem( $"SetPinState ignoriert: Unbekannter Pin im UI: {name}" );
			return;
		}

		_pinStates[ name ] = high;
		SetEllipseFill( name, ellipse, high );

		if( name is "A0" or "A1" or "A2" or "A3" )
			UpdateOperandAFromPins();

		if( name is "B0" or "B1" or "B2" or "B3" )
			UpdateOperandBFromPins();

		if( name is "S0" or "S1" or "S2" or "S3" or "M" or "CN" )
			UpdateOperationSelectorFromPins();

		ellipse.InvalidateVisual();
		ellipse.InvalidateMeasure();
		ellipse.InvalidateArrange();

		this.InvalidateVisual();
	}

	/// <summary>
	/// Recomputes the displayed operand A value from pins <c>A0..A3</c> and updates the bound UI control (if present).
	/// </summary>
	private void UpdateOperandAFromPins()
	{
		var a0 = _pinStates.TryGetValue( "A0", out var v0 ) && v0 ? 1 : 0;
		var a1 = _pinStates.TryGetValue( "A1", out var v1 ) && v1 ? 2 : 0;
		var a2 = _pinStates.TryGetValue( "A2", out var v2 ) && v2 ? 4 : 0;
		var a3 = _pinStates.TryGetValue( "A3", out var v3 ) && v3 ? 8 : 0;

		var value = a0 | a1 | a2 | a3;

		if( OperandA is not null )
			OperandA.Value = value;
	}

	/// <summary>
	/// Recomputes the displayed operand B value from pins <c>B0..B3</c> and updates the bound UI control (if present).
	/// </summary>
	private void UpdateOperandBFromPins()
	{
		var b0 = _pinStates.TryGetValue( "B0", out var v0 ) && v0 ? 1 : 0;
		var b1 = _pinStates.TryGetValue( "B1", out var v1 ) && v1 ? 2 : 0;
		var b2 = _pinStates.TryGetValue( "B2", out var v2 ) && v2 ? 4 : 0;
		var b3 = _pinStates.TryGetValue( "B3", out var v3 ) && v3 ? 8 : 0;

		var value = b0 | b1 | b2 | b3;

		if( OperandB is not null )
			OperandB.Value = value;
	}

	/// <summary>
	/// Recomputes the displayed function result nibble from pins <c>F0..F3</c> and updates the bound UI control (if
	/// present).
	/// </summary>
	private void UpdateResultFFromPins()
	{
		var f0 = _pinStates.TryGetValue( "F0", out var v0 ) && v0 ? 1 : 0;
		var f1 = _pinStates.TryGetValue( "F1", out var v1 ) && v1 ? 2 : 0;
		var f2 = _pinStates.TryGetValue( "F2", out var v2 ) && v2 ? 4 : 0;
		var f3 = _pinStates.TryGetValue( "F3", out var v3 ) && v3 ? 8 : 0;

		var value = f0 | f1 | f2 | f3;

		if( ResultF is not null )
			ResultF.Value = value;
	}

	/// <summary>
	/// Publishes a local pin toggle to the synchronization service after ensuring the connection is established.
	/// </summary>
	/// <remarks>
	/// Intended to be invoked only for local user input changes (not remote-applied changes), and only when
	/// <c>_suppressSyncSend</c> is not active.
	/// </remarks>
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
	/// Adds a timestamped log entry to <see cref="LogItems"/> and prepends it to <see cref="LogsText"/>.
	/// </summary>
	private void AddLogItem( string text )
	{
		var entry = $"{DateTime.Now:HH:mm:ss} - {text}";
		_logItems.Insert( 0, entry );

		LogsText = entry + Environment.NewLine + LogsText;
	}

	/// <summary>
	/// Sets a backing field and raises <see cref="PropertyChanged"/> if the value changed.
	/// </summary>
	/// <remarks>
	/// Uses <see cref="CallerMemberNameAttribute"/> to infer the property name when not explicitly provided.
	/// </remarks>
	protected bool SetProperty<T>( ref T field, T value, [CallerMemberName] string? propertyName = null )
	{
		if( EqualityComparer<T>.Default.Equals( field, value ) )
			return false;

		field = value;
		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
		return true;
	}

	/// <summary>
	/// Property changed notification for bindings in this view.
	/// </summary>
	/// <remarks>
	/// Declared with <c>new</c> because <see cref="UserControl"/> already exposes a <c>PropertyChanged</c> member via
	/// Avalonia infrastructure; this event is specifically for <see cref="INotifyPropertyChanged"/> usage from this view.
	/// </remarks>
	public new event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>
	/// Toggle for whether a log overlay should be visible, controlled at compile time.
	/// </summary>
	public bool IsLogOverlayVisible
	{
		get
		{
#if ALULAB_LOGOVERLAY
			return true;
#else
			return false;
#endif
		}
	}

	/// <summary>
	/// Performs hit-testing for a touch point in window coordinates and toggles the first matching input pin.
	/// </summary>
	/// <param name="x">Touch X coordinate in window space.</param>
	/// <param name="y">Touch Y coordinate in window space.</param>
	/// <remarks>
	/// This method is intended for external touch routing scenarios where the original pointer event is handled
	/// elsewhere. Output pins are ignored.
	/// </remarks>
	public void ProcessTouch( double x, double y )
	{
		var window = VisualRoot as Window;
		if( window is null )
			return;

		foreach( var name in _pinNames )
		{
			if( _outputPins.Contains( name ) )
				continue;

			var ellipse = this.FindControl<Ellipse>( name );
			if( ellipse is null )
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
			double dx = x - centerX;
			double dy = y - centerY;
			double radius = Math.Max( bw, bh ) / 2.0;

			if( dx * dx + dy * dy <= radius * radius )
			{
				TogglePinState( name, ellipse );
				break;
			}
		}
	}

	/// <summary>
	/// Creates the pin-to-style mapping used to color pins by functional group (A/B inputs, select pins, outputs, etc.).
	/// </summary>
	/// <remarks>
	/// The returned dictionary is used only for high-state rendering; low-state rendering uses shared brushes.
	/// </remarks>
	private static IReadOnlyDictionary<string, PinStyle> CreateStylesByPin()
	{
		var dict = new Dictionary<string, PinStyle>();

		// A pins (operand A) - blue LEDs
		var aStyle = new PinStyle( new SolidColorBrush( Colors.DeepSkyBlue ), new SolidColorBrush( Colors.DodgerBlue ) );
		dict[ "A0" ] = aStyle;
		dict[ "A1" ] = aStyle;
		dict[ "A2" ] = aStyle;
		dict[ "A3" ] = aStyle;

		// B pins (operand B) - blue LEDs
		var bStyle = new PinStyle( new SolidColorBrush( Colors.DeepSkyBlue ), new SolidColorBrush( Colors.DodgerBlue ) );
		dict[ "B0" ] = bStyle;
		dict[ "B1" ] = bStyle;
		dict[ "B2" ] = bStyle;
		dict[ "B3" ] = bStyle;

		// S pins (select/operation) - red LEDs
		var sStyle = new PinStyle( new SolidColorBrush( Colors.Red ), new SolidColorBrush( Colors.DarkRed ) );
		dict[ "S0" ] = sStyle;
		dict[ "S1" ] = sStyle;
		dict[ "S2" ] = sStyle;
		dict[ "S3" ] = sStyle;

		// Carry pins - orange LEDs
		var carryStyle = new PinStyle( new SolidColorBrush( Colors.Orange ), new SolidColorBrush( Colors.DarkOrange ) );
		dict[ "CN" ] = carryStyle;
		dict[ "CN4" ] = carryStyle;

		// M pin (mode control) - yellow LED
		var controlStyle = new PinStyle( new SolidColorBrush( Colors.Yellow ), new SolidColorBrush( Colors.Goldenrod ) );
		dict[ "M" ] = controlStyle;

		// F pins (function outputs) - green LEDs
		var fStyle = new PinStyle( new SolidColorBrush( Colors.LimeGreen ), new SolidColorBrush( Colors.Green ) );
		dict[ "F0" ] = fStyle;
		dict[ "F1" ] = fStyle;
		dict[ "F2" ] = fStyle;
		dict[ "F3" ] = fStyle;

		// Compare pin (A=B) - green LED
		var compareStyle = new PinStyle( new SolidColorBrush( Colors.LimeGreen ), new SolidColorBrush( Colors.Green ) );
		dict[ "AEqualsB" ] = compareStyle;

		// Other output pins - yellow LEDs
		var outputStyle = new PinStyle( new SolidColorBrush( Colors.Yellow ), new SolidColorBrush( Colors.Goldenrod ) );
		dict[ "P" ] = outputStyle;
		dict[ "G" ] = outputStyle;

		return dict;
	}

	/// <summary>
	/// Wires the <c>OperationSelector</c> UI to drive only the select pins (<c>S0..S3</c>) and initializes it from the
	/// current pin cache.
	/// </summary>
	/// <remarks>
	/// Pins <c>M</c> and <c>CN</c> are treated as read-only inputs and must not be modified by selector changes.
	/// </remarks>
	private void InitializeOperationSelector()
	{
		if( OperationSelector is null )
			return;

		OperationSelector.SelectedSCodeChangedByUser += ( _, sCode ) =>
		{
			// M and CN are read-only inputs. Do NOT set them here.
			SetSCodeFromSelector( sCode );
		};

		UpdateOperationSelectorFromPins();
	}

	/// <summary>
	/// Drives the select input pins (<c>S0..S3</c>) from a selector-provided S-code and publishes each change to sync.
	/// </summary>
	/// <param name="sCode">4-bit S-code where bits 0..3 map to <c>S0..S3</c>.</param>
	/// <remarks>
	/// This intentionally does not set <c>M</c> or <c>CN</c> (read-only inputs).
	/// </remarks>
	private void SetSCodeFromSelector( int sCode )
	{
		var s0 = ( sCode & 0b0001 ) != 0;
		var s1 = ( sCode & 0b0010 ) != 0;
		var s2 = ( sCode & 0b0100 ) != 0;
		var s3 = ( sCode & 0b1000 ) != 0;

		// User intent: drive the 4 select inputs only.
		SetPinState( "S0", s0 ); _ = SendPinToggledToSyncAsync( "S0", s0 );
		SetPinState( "S1", s1 ); _ = SendPinToggledToSyncAsync( "S1", s1 );
		SetPinState( "S2", s2 ); _ = SendPinToggledToSyncAsync( "S2", s2 );
		SetPinState( "S3", s3 ); _ = SendPinToggledToSyncAsync( "S3", s3 );
	}

	/// <summary>
	/// Updates the <c>OperationSelector</c> UI from the current pin cache.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Pins <c>M</c> and <c>CN</c> are displayed as read-only inputs (externally controlled).
	/// </para>
	/// <para>
	/// The selected S-code is computed solely from <c>S0..S3</c> so that pin toggles (local or remote) remain the source
	/// of truth.
	/// </para>
	/// </remarks>
	private void UpdateOperationSelectorFromPins()
	{
		if( OperationSelector is null )
			return;

		var m = _pinStates.TryGetValue( "M", out var mv ) && mv;      // read-only input
		var cn = _pinStates.TryGetValue( "CN", out var cnv ) && cnv; // read-only input

		var s0 = _pinStates.TryGetValue( "S0", out var s0v ) && s0v ? 1 : 0;
		var s1 = _pinStates.TryGetValue( "S1", out var s1v ) && s1v ? 2 : 0;
		var s2 = _pinStates.TryGetValue( "S2", out var s2v ) && s2v ? 4 : 0;
		var s3 = _pinStates.TryGetValue( "S3", out var s3v ) && s3v ? 8 : 0;

		OperationSelector.ModeM = m;
		OperationSelector.CarryInCn = cn;
		OperationSelector.SelectedSCode = s0 | s1 | s2 | s3;
	}
}

public sealed record PinToggledEventArgs( string PinName, bool State );