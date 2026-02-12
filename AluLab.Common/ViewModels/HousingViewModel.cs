using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using AluLab.Common.Services;

namespace AluLab.Common.ViewModels
{
	public partial class HousingViewModel : ViewModelBase, IAsyncDisposable
	{
		private readonly SyncService _service;
		private IDisposable? _subscription;
		private bool _disposed;

		private string? _statusValue;
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
		public string? LastMessage
		{
			get => _lastMessage;
			set => SetProperty( ref _lastMessage, value );
		}

		private string? _message;
		public string? Message
		{
			get => _message;
			set => SetProperty( ref _message, value );
		}

		private string _logsText = string.Empty;
		public string LogsText
		{
			get => _logsText;
			private set => SetProperty( ref _logsText, value );
		}

		private readonly IAsyncRelayCommand _connectCommand;
		private readonly IAsyncRelayCommand _disconnectCommand;
		//private readonly IAsyncRelayCommand _sendTestCommand;

		public IAsyncRelayCommand ConnectCommand => _connectCommand;
		public IAsyncRelayCommand DisconnectCommand => _disconnectCommand;
		//public IAsyncRelayCommand SendTestCommand => _sendTestCommand;

		public HousingViewModel( SyncService service )
		{
			_service = service ?? throw new ArgumentNullException( nameof( service ) );

			Status = "Disconnected";
			AddLog( "ViewModel erstellt" );

			_connectCommand = new AsyncRelayCommand( ConnectImplAsync );
			_disconnectCommand = new AsyncRelayCommand( DisconnectImplAsync );
			//_sendTestCommand = new AsyncRelayCommand( SendTestImplAsync );
		}

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

		private void OnServiceLog(string msg)
		{
			Dispatcher.UIThread.Post(() =>
			{
				LastMessage = msg;
				AddLog($"Received: {msg}");
			});
		}

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

		private async Task SendTestImplAsync()
		{
			if( !_service.IsConnected )
			{
				Status = "Not connected";
				AddLog( "Send aborted: not connected" );
				return;
			}

			var payload = string.IsNullOrWhiteSpace( Message ) ? "Hello from AluLab Sync client" : Message;

			try
			{
				// Da SyncService keine SendMessageAsync-Methode besitzt,
				// muss hier eine alternative Methode genutzt werden.

				AddLog( $"Sent: {payload}" );
			}
			catch( Exception ex )
			{
				Status = $"Send failed: {ex.Message}";
				AddLog( $"Send failed: {ex.Message}" );
			}
		}

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

		public ValueTask DisposeAsync()
		{
			if( _disposed ) return ValueTask.CompletedTask;
			_disposed = true;

			_subscription?.Dispose();
			AddLog( "ViewModel disposed" );

			return ValueTask.CompletedTask;
		}

		private class DisposableAction : IDisposable
		{
			private readonly Action _dispose;
			public DisposableAction(Action dispose) => _dispose = dispose;
			public void Dispose() => _dispose();
		}
	}
}