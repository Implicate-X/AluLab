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

public partial class HousingView : UserControl, INotifyPropertyChanged
{
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

		AttachedToVisualTree += ( _, __ ) => StartSyncAutoConnect();
	}

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

	private IDisposable SubscribeSyncLog( SyncService syncService )
	{
		void Handler( string message )
		{
			Dispatcher.UIThread.Post( () => AddLogItem( message ) );
		}

		syncService.Log += Handler;
		return new DelegateDisposable( () => syncService.Log -= Handler );
	}

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

	private IDisposable SubscribeSnapshotOutputs( SyncService syncService )
	{
		void Handler( AluLab.Common.Relay.SyncHub.AluOutputsDto? dto )
		{
			Dispatcher.UIThread.Post( () =>
			{
				Interlocked.Exchange( ref _suppressSyncSend, 1 );
				try
				{
					var raw = dto?.Raw ?? (byte)0;
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

			// Snapshot NICHT manuell laden:
			// Server pusht SnapshotPins/SnapshotOutputs nach ClientReady.
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
			SetEllipseFill( ellipse, false );

			if( !_outputPins.Contains( name ) )
			{
				ellipse.PointerPressed += ( s, e ) => HandlePinPointer( name, ellipse, e );
				ellipse.PointerReleased += ( s, e ) => HandlePinPointer( name, ellipse, e );
			}
		}
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

		SetEllipseFill( ellipse, newState );

		PinToggled?.Invoke( this, new PinToggledEventArgs( name, newState ) );

		if( Volatile.Read( ref _suppressSyncSend ) == 0 )
		{
			_ = SendPinToggledToSyncAsync( name, newState );
		}
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

	private void SetEllipseFill( Ellipse ellipse, bool high )
	{
		ellipse.Fill = high ? _brushHigh : _brushLow;
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
		SetEllipseFill( ellipse, high );

		// Browser/WASM: Rendering zuverlässig anstoßen
		ellipse.InvalidateVisual();
		ellipse.InvalidateMeasure();
		ellipse.InvalidateArrange();
		this.InvalidateVisual();
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
			return false;
//#if ALULAB_LOGOVERLAY
//			return true;
//#else
//			return false;
//#endif
		}
	}
}

public sealed record PinToggledEventArgs( string PinName, bool State );