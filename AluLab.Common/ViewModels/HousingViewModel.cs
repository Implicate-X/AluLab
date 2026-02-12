using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using AluLab.Common.Services;

namespace AluLab.Common.ViewModels
{
	/// <summary>
	/// ViewModel for managing housing-related synchronization and status updates.
	/// Handles connection and disconnection to a backend service via SignalR,
	/// maintains status and log messages, and exposes commands for UI interaction.
	/// </summary>
	public partial class HousingViewModel : ViewModelBase, IAsyncDisposable
	{
		private readonly SyncService _service;
		private IDisposable? _subscription;
		private bool _disposed;

		private string? _statusValue;
		/// <summary>
		/// Gets or sets the current connection status.
		/// Updates the log when the status changes.
		/// </summary>
		public string? Status
		{
			get => _statusValue;
			set
			{
				if( SetProperty( ref _statusValue, value ) )
				{
					AddLog( $"Status: {value}" );
				}
			}
		}

		private string? _lastMessage;
		/// <summary>
		/// Gets or sets the last message received from the service.
		/// </summary>
		public string? LastMessage
		{
			get => _lastMessage;
			set => SetProperty( ref _lastMessage, value );
		}

		private string? _message;
		/// <summary>
		/// Gets or sets a general message property for UI binding.
		/// </summary>
		public string? Message
		{
			get => _message;
			set => SetProperty( ref _message, value );
		}

		private string _logsText = string.Empty;
		/// <summary>
		/// Gets the accumulated log text for display in the UI.
		/// </summary>
		public string LogsText
		{
			get => _logsText;
			private set => SetProperty( ref _logsText, value );
		}

		private readonly IAsyncRelayCommand _connectCommand;
		private readonly IAsyncRelayCommand _disconnectCommand;

		/// <summary>
		/// Command to initiate connection to the backend service.
		/// </summary>
		public IAsyncRelayCommand ConnectCommand => _connectCommand;
		/// <summary>
		/// Command to disconnect from the backend service.
		/// </summary>
		public IAsyncRelayCommand DisconnectCommand => _disconnectCommand;

		/// <summary>
		/// Initializes a new instance of the <see cref="HousingViewModel"/> class.
		/// </summary>
		/// <param name="service">The synchronization service to use for backend communication.</param>
		/// <exception cref="ArgumentNullException">Thrown if <paramref name="service"/> is null.</exception>
		public HousingViewModel( SyncService service )
		{
			_service = service ?? throw new ArgumentNullException( nameof( service ) );

			Status = "Disconnected";
			AddLog( "ViewModel erstellt" );

			_connectCommand = new AsyncRelayCommand( ConnectImplAsync );
			_disconnectCommand = new AsyncRelayCommand( DisconnectImplAsync );
		}

		/// <summary>
		/// Asynchronously connects to the backend service and subscribes to log events.
		/// Updates status and logs accordingly.
		/// </summary>
		private async Task ConnectImplAsync()
		{
			try
			{
				await _service.EnsureConnectedAsync();
				Status = "Connected";
				AddLog( "Connected to SignalR" );

				_service.Log += OnServiceLog;
				_subscription = new DisposableAction(() => _service.Log -= OnServiceLog);
			}
			catch( Exception ex )
			{
				Status = $"Connect failed: {ex.Message}";
				AddLog( $"Connect failed: {ex.Message}" );
			}
		}

		/// <summary>
		/// Handles log messages from the service, updating the last message and appending to the log.
		/// Ensures updates are posted to the UI thread.
		/// </summary>
		/// <param name="msg">The log message received from the service.</param>
		private void OnServiceLog(string msg)
		{
			Dispatcher.UIThread.Post(() =>
			{
				LastMessage = msg;
				AddLog($"Received: {msg}");
			});
		}

		/// <summary>
		/// Asynchronously disconnects from the backend service and unsubscribes from log events.
		/// Updates status and logs accordingly.
		/// </summary>
		private async Task DisconnectImplAsync()
		{
			try
			{
				_subscription?.Dispose();
				await _service.DisposeAsync();
				Status = "Disconnected";
				AddLog( "Disconnected from SignalR" );
			}
			catch( Exception ex )
			{
				Status = $"Disconnect failed: {ex.Message}";
				AddLog( $"Disconnect failed: {ex.Message}" );
			}
		}

		/// <summary>
		/// Adds a log entry with a timestamp to the <see cref="LogsText"/> property.
		/// Ensures thread-safe updates to the UI.
		/// </summary>
		/// <param name="text">The log text to add.</param>
		private void AddLog( string text )
		{
			var entry = $"{DateTime.Now:HH:mm:ss} - {text}";
			if( Dispatcher.UIThread.CheckAccess() )
			{
				LogsText = entry + Environment.NewLine + LogsText;
			}
			else
			{
				Dispatcher.UIThread.Post( () => LogsText = entry + Environment.NewLine + LogsText );
			}
		}

		/// <summary>
		/// Disposes the ViewModel asynchronously, unsubscribing from events and logging disposal.
		/// </summary>
		/// <returns>A completed <see cref="ValueTask"/>.</returns>
		public ValueTask DisposeAsync()
		{
			if( _disposed ) return ValueTask.CompletedTask;
			_disposed = true;

			_subscription?.Dispose();
			AddLog( "ViewModel disposed" );

			return ValueTask.CompletedTask;
		}

		/// <summary>
		/// Helper class for executing an action upon disposal.
		/// Used for unsubscribing from events.
		/// </summary>
		private class DisposableAction : IDisposable
		{
			private readonly Action _dispose;
			public DisposableAction(Action dispose) => _dispose = dispose;
			public void Dispose() => _dispose();
		}
	}
}