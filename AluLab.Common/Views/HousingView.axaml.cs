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
public partial class HousingView : UserControl, INotifyPropertyChanged
{
	// ViewModel and service references for data binding and synchronization.
	private readonly HousingViewModel? _viewModel;
	private readonly SyncService? _syncService;

	private IDisposable? _pinToggledSubscription;
	private IDisposable? _aluOutputsSubscription;
	private IDisposable? _syncLogSubscription;
	private IDisposable? _snapshotPinsSubscription;
	private IDisposable? _snapshotOutputsSubscription;

	private int _suppressSyncSend;

	private readonly ObservableCollection<string> _logItems = new();
	public IReadOnlyCollection<string> LogItems => _logItems;

	private string _logsText = string.Empty;
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

	private static readonly SolidColorBrush s_brushLowFill = new( Colors.AliceBlue );
	private static readonly SolidColorBrush s_brushLowStroke = new( Colors.Gray );
	private static readonly IReadOnlyDictionary<string, PinStyle> s_stylesByPin = CreateStylesByPin();
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

	private sealed class DelegateDisposable : IDisposable
	{
		private Action? _dispose;

		public DelegateDisposable( Action dispose ) => _dispose = dispose;

		public void Dispose()
		{
			Interlocked.Exchange( ref _dispose, null )?.Invoke();
		}
	}

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
	}

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

		PinToggled?.Invoke( this, new PinToggledEventArgs( name, newState ) );

		if( Volatile.Read( ref _suppressSyncSend ) == 0 )
		{
			_ = SendPinToggledToSyncAsync( name, newState );
		}
	}

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

		ellipse.InvalidateVisual();
		ellipse.InvalidateMeasure();
		ellipse.InvalidateArrange();

		this.InvalidateVisual();
	}

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

	private void AddLogItem( string text )
	{
		var entry = $"{DateTime.Now:HH:mm:ss} - {text}";
		_logItems.Insert( 0, entry );

		LogsText = entry + Environment.NewLine + LogsText;
	}

	protected bool SetProperty<T>( ref T field, T value, [CallerMemberName] string? propertyName = null )
	{
		if( EqualityComparer<T>.Default.Equals( field, value ) )
			return false;

		field = value;
		PropertyChanged?.Invoke( this, new PropertyChangedEventArgs( propertyName ) );
		return true;
	}

	public new event PropertyChangedEventHandler? PropertyChanged;

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
}

public sealed record PinToggledEventArgs( string PinName, bool State );