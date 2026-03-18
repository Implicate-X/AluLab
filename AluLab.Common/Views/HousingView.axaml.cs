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
/// Responsibilities:
/// </para>
/// <list type="bullet">
/// <item><description>Owns and initializes pin UI elements (ellipses) and their input handlers.</description></item>
/// <item><description>Keeps a local pin-state cache (<c>_pinStates</c>) to model UI state quickly.</description></item>
/// <item><description>Subscribes to remote sync streams (pin toggles, ALU output changes, snapshot state).</description></item>
/// <item><description>Prevents feedback loops by suppressing outgoing sync events while applying remote updates.</description></item>
/// <item><description>Collects log entries for diagnostics and optional on-screen overlay.</description></item>
/// </list>
/// <para>
/// Threading:
/// remote events may arrive on background threads; all UI mutations are marshaled onto
/// <see cref="Dispatcher.UIThread"/>. During those UI updates, <c>_suppressSyncSend</c> is set to prevent sending the
/// same state back to the server.
/// </para>
/// <para>
/// Lifetime:
/// upon <see cref="Visual.DetachedFromVisualTree"/>, all subscriptions are disposed and the injected
/// <see cref="HousingViewModel"/> is disposed asynchronously.
/// </para>
/// </remarks>
public partial class HousingView : UserControl, INotifyPropertyChanged
{
	// ViewModel and service references for data binding and synchronization.
	/// <summary>
	/// View model used as the <see cref="DataContext"/> for bindings and commands.
	/// </summary>
	private readonly HousingViewModel? _viewModel;

	/// <summary>
	/// Synchronization service used to publish local changes and apply remote changes (snapshots and events).
	/// </summary>
	private readonly SyncService? _syncService;

	// Subscriptions for various sync and event streams.
	/// <summary>
	/// Subscription for remote "PinToggled" events.
	/// </summary>
	private IDisposable? _pinToggledSubscription;

	/// <summary>
	/// Subscription for remote "AluOutputsChanged" events.
	/// </summary>
	private IDisposable? _aluOutputsSubscription;

	/// <summary>
	/// Subscription bridging <see cref="SyncService.Log"/> into the local log UI.
	/// </summary>
	private IDisposable? _syncLogSubscription;

	/// <summary>
	/// Subscription for pin snapshot state (<see cref="SyncService.SnapshotStateReceived"/>).
	/// </summary>
	private IDisposable? _snapshotPinsSubscription;

	/// <summary>
	/// Subscription for output snapshot state (<see cref="SyncService.SnapshotOutputsReceived"/>).
	/// </summary>
	private IDisposable? _snapshotOutputsSubscription;

	// Used to suppress sending sync events during UI updates.
	/// <summary>
	/// 0/1 flag used to suppress outgoing sync sends while applying remote state to the UI.
	/// </summary>
	/// <remarks>
	/// This prevents feedback loops: remote update → UI update → local handler sends the same update back.
	/// Use <see cref="Volatile.Read(ref int)"/> for reads and <see cref="Interlocked.Exchange(ref int, int)"/> for writes.
	/// </remarks>
	private int _suppressSyncSend;

	// Collection of log entries for UI and diagnostics.
	/// <summary>
	/// Backing store for <see cref="LogItems"/>; newest entries are inserted at index 0.
	/// </summary>
	private readonly ObservableCollection<string> _logItems = new();

	/// <summary>
	/// Log entries for display/diagnostics (most recent first).
	/// </summary>
	public IReadOnlyCollection<string> LogItems => _logItems;

	private string _logsText = string.Empty;

	/// <summary>
	/// Concatenated log text used for a text-based overlay/view (most recent entry prepended).
	/// </summary>
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
	/// Local cache of current pin states keyed in своем pin name (e.g. <c>"A0"</c>, <c>"F3"</c>).
	/// </summary>
	private readonly Dictionary<string, bool> _pinStates = new();

	/// <summary>
	/// Set of pins that are output-only and must not be user-toggleable in the UI.
	/// </summary>
	private readonly HashSet<string> _outputPins = new()
	{
		"F3", "F2", "F1", "F0", "P", "G", "AEqualsB", "CN4"
	};

	/// <summary>
	/// Ordered list of all pins represented by the UI. Used for initialization and touch hit testing.
	/// </summary>
	private readonly string[] _pinNames = new[]
	{
		"A3","A2","A1","A0",
		"B3","B2","B1","B0",
		"S3","S2","S1","S0",
		"CN","M",
		"F3","F2","F1","F0",
		"P","G","AEqualsB","CN4"
	};

	/// <summary>
	/// Default visual style for an inactive/low pin fill.
	/// </summary>
	private static readonly SolidColorBrush s_brushLowFill = new( Colors.AliceBlue );

	/// <summary>
	/// Default visual style for an inactive/low pin stroke.
	/// </summary>
	private static readonly SolidColorBrush s_brushLowStroke = new( Colors.Gray );

	/// <summary>
	/// Mapping of pin name to its “high” (active) visual style.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, PinStyle> s_stylesByPin = CreateStylesByPin();

	/// <summary>
	/// Encapsulates the “active/high” look for a pin (fill + stroke).
	/// </summary>
	private sealed record PinStyle( SolidColorBrush HighFill, SolidColorBrush HighStroke );

	/// <summary>
	/// Builds the static mapping of pin names to their thematic colors when active.
	/// </summary>
	/// <remarks>
	/// This allows grouping related pins (A/B inputs, select lines, outputs, carry pins) with consistent colors for
	/// readability.
	/// </remarks>
	private static IReadOnlyDictionary<string, PinStyle> CreateStylesByPin()
	{
		Dictionary<string, PinStyle> pinStyleMap = new( StringComparer.Ordinal );

		void Add( PinStyle style, params string[] pins )
		{
			foreach( string pin in pins )
				pinStyleMap[pin] = style;
		}

		PinStyle pinStyleBl = new( new SolidColorBrush( Colors.DeepSkyBlue ), new SolidColorBrush( Colors.DodgerBlue ) );
		PinStyle pinStyleRd = new( new SolidColorBrush( Colors.Red ), new SolidColorBrush( Colors.DarkRed ) );
		PinStyle pinStyleGn = new( new SolidColorBrush( Colors.LimeGreen ), new SolidColorBrush( Colors.Green ) );
		PinStyle pinStyleYe = new( new SolidColorBrush( Colors.Yellow ), new SolidColorBrush( Colors.Goldenrod ) );
		PinStyle pinStyleOr = new( new SolidColorBrush( Colors.Orange ), new SolidColorBrush( Colors.DarkOrange ) );

		Add( pinStyleBl, "A0", "A1", "A2", "A3", "B0", "B1", "B2", "B3" );
		Add( pinStyleRd, "S0", "S1", "S2", "S3" );
		Add( pinStyleGn, "F0", "F1", "F2", "F3" );
		Add( pinStyleYe, "P", "G", "M" );
		Add( pinStyleOr, "CN", "CN4" );

		return pinStyleMap;
	}

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

		_pinToggledSubscription?.Dispose();
		_aluOutputsSubscription?.Dispose();

		_snapshotPinsSubscription?.Dispose();
		_snapshotOutputsSubscription?.Dispose();

		_syncLogSubscription?.Dispose();
		_syncLogSubscription = SubscribeSyncLog( _syncService );

		_snapshotPinsSubscription = SubscribeSnapshotPins( _syncService );
		_snapshotOutputsSubscription = SubscribeSnapshotOutputs( _syncService );

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
	/// <remarks>This method ensures that log messages are posted to the UI thread, making it safe to update UI
	/// elements in response to log events in multi-threaded scenarios.</remarks>
	/// <param name="syncService">The synchronization service to subscribe to for log events. This parameter must not be null.</param>
	/// <returns>An IDisposable that unsubscribes from the log events when disposed.</returns>
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
	/// <remarks>This method ensures that pin state updates are posted to the UI thread to maintain thread safety.
	/// After each update, the number of pins applied is logged. Disposing the returned object will unsubscribe from
	/// further snapshot state notifications.</remarks>
	/// <param name="syncService">The synchronization service that provides snapshot state updates. Cannot be null.</param>
	/// <returns>An IDisposable that unsubscribes from the snapshot state updates when disposed.</returns>
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
	/// Subscribes to the SnapshotOutputsReceived event of the specified SyncService to handle incoming snapshot output
	/// data.
	/// </summary>
	/// <remarks>This method posts the received snapshot output data to the UI thread for processing. It ensures
	/// that the synchronization of UI updates is managed correctly, preventing concurrent modifications.</remarks>
	/// <param name="syncService">The SyncService instance that provides the snapshot output data. This parameter cannot be null.</param>
	/// <returns>An IDisposable that can be used to unsubscribe from the SnapshotOutputsReceived event.</returns>
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
	/// Provides a disposable object that executes a specified action when disposed.
	/// </summary>
	/// <remarks>Use this class to ensure that a custom cleanup action is performed when the object is disposed,
	/// such as releasing unmanaged resources or performing other teardown logic. The action is invoked only once;
	/// subsequent calls to Dispose have no effect. This class is typically used to encapsulate resource management
	/// patterns in a concise and reliable manner.</remarks>
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
	/// Asynchronously establishes a connection to the synchronization service and initiates loading of the initial state
	/// from the server.
	/// </summary>
	/// <remarks>This method logs the connection process and the state of the synchronization service. It handles
	/// exceptions by logging any errors encountered during the connection process. The initial state is received from the
	/// server after the client is ready.</remarks>
	/// <returns>A task that represents the asynchronous operation.</returns>
	private async System.Threading.Tasks.Task ConnectAndLoadInitialStateAsync()
	{
		try
		{
			AddLogItem( "Sync: ConnectAndLoadInitialStateAsync start" );

			await EnsureSyncConnectedWithLoggingAsync();

			AddLogItem( $"Sync: IsConnected={_syncService?.IsConnected}" );

			// Snapshot is pushed by the server after client is ready.
			AddLogItem( "Sync: Initial-Snapshot über Server-Push (SnapshotPins/SnapshotOutputs)." );

			AddLogItem( "Sync: ConnectAndLoadInitialStateAsync end" );
		}
		catch( Exception ex )
		{
			AddLogItem( $"Sync: ConnectAndLoadInitialStateAsync Fehler: {ex}" );
		}
	}

	/// <summary>
	/// Ensures that the synchronization service is connected and logs the connection status asynchronously.
	/// </summary>
	/// <remarks>If the synchronization service is not initialized, the method exits without performing any actions.
	/// In case of a connection failure, an error message is logged.</remarks>
	/// <returns>A task that represents the asynchronous operation.</returns>
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
	/// Updates the user interface to reflect the current state of the ALU output pins based on the specified raw byte
	/// value.
	/// </summary>
	/// <remarks>Each bit in the raw value is mapped to a particular pin in the user interface, such as F0–F3, P, G,
	/// AEqualsB, and CN4. Setting the appropriate bit to <see langword="true"/> activates the corresponding pin in the UI.
	/// This method is typically used to visually represent the ALU's output state for debugging or educational
	/// purposes.</remarks>
	/// <param name="raw">A byte value representing the ALU outputs, where each bit corresponds to the state of a specific output pin.</param>
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
	}

	/// <summary>
	/// Initializes the pin controls and their associated event handlers for user interaction.
	/// </summary>
	/// <remarks>This method sets the initial state of each pin to inactive and attaches pointer event handlers to
	/// input pins only. Pins identified as output pins are excluded from user interaction setup.</remarks>
	private void InitializePins()
	{
		foreach( var name in _pinNames )
		{
			var ellipse = this.FindControl<Ellipse>( name );
			if( ellipse == null )
			{
				continue;
			}

			_pinStates[name] = false;
			SetEllipseFill( name, ellipse, high: false );

			if( !_outputPins.Contains( name ) )
			{
				ellipse.PointerPressed += ( s, e ) => HandlePinPointer( name, ellipse, e );
				ellipse.PointerReleased += ( s, e ) => HandlePinPointer( name, ellipse, e );
			}
		}
	}

	/// <summary>
	/// Handles pointer events to toggle the pin state of the specified ellipse based on the pointer type and event.
	/// </summary>
	/// <remarks>This method responds to mouse pointer pressed events and touch or pen pointer released events to
	/// toggle the pin state. The event is marked as handled when the pin state is toggled.</remarks>
	/// <param name="name">The identifier associated with the pin action, used to track the pin state for the ellipse.</param>
	/// <param name="ellipse">The ellipse visual element whose pin state is to be toggled.</param>
	/// <param name="e">The pointer event arguments containing information about the pointer interaction.</param>
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
	/// Toggles a pin locally, updates its visuals, raises <see cref="PinToggled"/>, and (unless suppressed) sends the
	/// change to the sync service.
	/// </summary>
	/// <remarks>
	/// Outgoing sync is skipped while <c>_suppressSyncSend</c> is set, which is used when applying remote updates.
	/// </remarks>
	/// <param name="name">Pin identifier.</param>
	/// <param name="ellipse">UI element representing the pin.</param>
	private void TogglePinState( string name, Ellipse ellipse )
	{
		if( !_pinStates.TryGetValue( name, out var currentState ) )
		{
			currentState = false;
			_pinStates[name] = currentState;
		}

		var newState = !currentState;
		_pinStates[name] = newState;

		SetEllipseFill( name, ellipse, newState );

		PinToggled?.Invoke( this, new PinToggledEventArgs( name, newState ) );

		if( Volatile.Read( ref _suppressSyncSend ) == 0 )
		{
			_ = SendPinToggledToSyncAsync( name, newState );
		}
	}

	/// <summary>
	/// Applies the appropriate “low/high” styling to a pin ellipse.
	/// </summary>
	/// <remarks>
	/// When <paramref name="high"/> is <see langword="true"/>, a colored fill/stroke and drop shadow are applied based on
	/// <paramref name="pinName"/>. If no style is mapped, a green fallback style is used.
	/// </remarks>
	/// <param name="pinName">Pin identifier used to pick a style.</param>
	/// <param name="ellipse">Target UI element.</param>
	/// <param name="high">Whether the pin is active/high.</param>
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
			// Fallback, falls ein Pin-Name nicht gemappt ist.
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
	/// Sets a pin state programmatically (typically from remote sync) and refreshes the UI element if present.
	/// </summary>
	/// <remarks>
	/// If the pin is not found in the XAML visual tree, the update is ignored and a log entry is produced.
	/// </remarks>
	/// <param name="name">Pin identifier.</param>
	/// <param name="high">New state.</param>
	private void SetPinState( string name, bool high )
	{
		var ellipse = this.FindControl<Ellipse>( name );
		if( ellipse == null )
		{
			AddLogItem( $"SetPinState ignoriert: Unbekannter Pin im UI: {name}" );
			return;
		}

		_pinStates[name] = high;
		SetEllipseFill( name, ellipse, high );

		ellipse.InvalidateVisual();
		ellipse.InvalidateMeasure();
		ellipse.InvalidateArrange();
		this.InvalidateVisual();
	}

	/// <summary>
	/// Asynchronously notifies the synchronization service of a pin state change.
	/// </summary>
	/// <remarks>This method attempts to ensure the synchronization service is connected before sending the pin
	/// state change. If the connection cannot be established or an error occurs during the send operation, a log entry is
	/// created. No exception is thrown to the caller in these cases.</remarks>
	/// <param name="pin">The identifier of the pin whose state has changed.</param>
	/// <param name="state">A value indicating the new state of the pin; <see langword="true"/> if the pin is activated, otherwise <see
	/// langword="false"/>.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
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
	/// Adds a new log entry with the current timestamp to the collection of log items and updates the displayed log text.
	/// </summary>
	/// <remarks>The new log entry is inserted at the beginning of the log items list, ensuring that the most recent
	/// entries appear first in the display.</remarks>
	/// <param name="text">The message text to include in the log entry.</param>
	private void AddLogItem( string text )
	{
		var entry = $"{DateTime.Now:HH:mm:ss} - {text}";
		_logItems.Insert( 0, entry );

		LogsText = entry + Environment.NewLine + LogsText;
	}

	/// <summary>
	/// Sets the specified property's backing field to a new value and raises the PropertyChanged event if the value has
	/// changed.
	/// </summary>
	/// <remarks>This method is typically used within property setters in classes that implement
	/// INotifyPropertyChanged to ensure that property change notifications are only raised when the value actually
	/// changes.</remarks>
	/// <typeparam name="T">The type of the property being set.</typeparam>
	/// <param name="field">A reference to the backing field of the property to update.</param>
	/// <param name="value">The new value to assign to the property.</param>
	/// <param name="propertyName">The name of the property that is being set. This value is automatically provided by the compiler if not specified.</param>
	/// <returns>true if the property value was changed and the PropertyChanged event was raised; otherwise, false.</returns>
	protected bool SetProperty<T>( ref T field, T value, [CallerMemberName] string? propertyName = null )
	{
		if( EqualityComparer<T>.Default.Equals( field, value ) )
			return false;

		field = value;
		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
		return true;
	}

	/// <summary>
	/// Occurs when a property value changes.
	/// </summary>
	/// <remarks>This event is typically used to notify subscribers about changes to property values in data-bound
	/// scenarios. It is important to raise this event whenever a property changes to ensure that the UI or other listeners
	/// can react accordingly.</remarks>
	public new event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>
	/// Gets a value indicating whether the log overlay is currently visible.
	/// </summary>
	/// <remarks>This property returns <see langword="true"/> if the log overlay feature is enabled at compile time;
	/// otherwise, it returns <see langword="false"/>. The visibility is determined by the presence of the
	/// 'ALULAB_LOGOVERLAY' compilation symbol.</remarks>
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
	/// Processes a touch event at the specified coordinates, toggling the pin if a hit is detected.
	/// </summary>
	/// <remarks>
	/// Performs a simple circular hit test against each input pin ellipse (output pins are skipped). When a hit is found,
	/// this delegates to <see cref="TogglePinState(string, Ellipse)"/>.
	/// </remarks>
	/// <param name="x">The X coordinate of the touch.</param>
	/// <param name="y">The Y coordinate of the touch.</param>
	public void ProcessTouch( double x, double y )
	{
		var window = VisualRoot as Window;
		if( window is null )
			return;

		foreach( var name in _pinNames )
		{
			// Output pins are read-only (set by ALU)
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

			// Circle hit test: Check distance to center point
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
}

/// <summary>
/// Represents the data for an event that occurs when a pin is toggled, including the pin's name and its new state.
/// </summary>
/// <remarks>Use this event data to determine which pin was toggled and whether it is now active or inactive. This
/// is commonly used in scenarios involving hardware pin monitoring or user interface controls that simulate pin
/// states.</remarks>
/// <param name="PinName">The name of the pin that was toggled.</param>
/// <param name="State">The new state of the pin. Set to <see langword="true"/> if the pin is activated; otherwise, <see langword="false"/>.</param>
public sealed record PinToggledEventArgs( string PinName, bool State );