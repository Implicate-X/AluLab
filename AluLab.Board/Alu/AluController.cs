using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Iot.Device.Mcp23xxx;
using AluLab.Board.InputOutputExpander;
using AluLab.Common.Relay;
using System.Diagnostics;

namespace AluLab.Board.Alu
{
	/// <summary>
	/// Controller for the ALU hardware connection, including optional synchronization via a SyncHub.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <see cref="AluController"/> encapsulates the setting of ALU input pins (via <see cref="V1SignalOutALU"/>)
	/// and reading the resulting ALU outputs (via <see cref="V2SignalInpALU"/>).
	/// </para>
	/// <para>
	/// Additionally, a <see cref="SyncClient"/> can be configured to synchronize pin changes/outputs with other
	/// clients. Echo is avoided by not sending remote inputs back to the hub.
	/// </para>
	/// </remarks>
	public sealed class AluController( V1SignalOutALU v1, V2SignalInpALU v2, ILogger? logger = null ) : IDisposable
	{
		private readonly V1SignalOutALU _v1 = v1 ?? throw new ArgumentNullException( nameof( v1 ) );
		private readonly V2SignalInpALU _v2 = v2 ?? throw new ArgumentNullException( nameof( v2 ) );
		private readonly ILogger _logger = logger ?? NullLogger.Instance;

		/// <summary>
		/// Reserved for future asynchronous/periodic background tasks within the controller.
		/// </summary>
		private readonly CancellationTokenSource? _cts = null;

		/// <summary>
		/// When true, the *data* signals are treated as active-low at the boundary (A/B/S/M/CN and F/P/G/CN4/A==B).
		/// This is controlled remotely via the pseudo pin "ALD".
		/// </summary>
		private bool _activeLowData;

		private static readonly HashSet<string> s_dataPins = new( StringComparer.OrdinalIgnoreCase )
		{
			"A0","A1","A2","A3",
			"B0","B1","B2","B3",
			"S0","S1","S2","S3",
			"CN","M"
		};

		/// <summary>
		/// Represents the current ALU output state in multiple representations.
		/// </summary>
		/// <param name="Raw">Raw value (byte) of the read output lines. </param>
		/// <param name="Binary">Binary string (always 8 characters, including leading zeros). </param>
		/// <param name="Hex">Hex string (always 2 digits, uppercase). </param>
		public record AluOutputs( byte Raw, string Binary, string Hex );

		/// <summary>
		/// Snapshot of the ALU state (V1 and V2 outputs).
		/// </summary>
		/// <param name="V1PortA">V1 Port A raw value.</param>
		/// <param name="V1PortABinary">V1 Port A binary representation.</param>
		/// <param name="V1PortB">V1 Port B raw value.</param>
		/// <param name="V1PortBBinary">V1 Port B binary representation.</param>
		/// <param name="V2PortB">V2 Port B raw value.</param>
		/// <param name="V2PortBBinary">V2 Port B binary representation.</param>
		/// <param name="V2PortBHex">V2 Port B hex representation.</param>
		public record AluSnapshot(
			byte V1PortA,
			string V1PortABinary,
			byte V1PortB,
			string V1PortBBinary,
			byte V2PortB,
			string V2PortBBinary,
			string V2PortBHex );

		public event Action<AluOutputs>? OutputsUpdated;
		public event Action<AluSnapshot>? SnapshotUpdated;
		public event Action<string, bool>? RemotePinToggled;

		private SyncClient? _syncClient;

		private static readonly Dictionary<string, (Port Port, byte Mask)> s_pinMap =
		new()
		{
			[ "A0" ] = (Port.PortA, V1SignalOutALU.PortA.A0),
			[ "A1" ] = (Port.PortA, V1SignalOutALU.PortA.A1),
			[ "A2" ] = (Port.PortA, V1SignalOutALU.PortA.A2),
			[ "A3" ] = (Port.PortA, V1SignalOutALU.PortA.A3),

			[ "B0" ] = (Port.PortA, V1SignalOutALU.PortA.B0),
			[ "B1" ] = (Port.PortA, V1SignalOutALU.PortA.B1),
			[ "B2" ] = (Port.PortA, V1SignalOutALU.PortA.B2),
			[ "B3" ] = (Port.PortA, V1SignalOutALU.PortA.B3),

			[ "S0" ] = (Port.PortB, V1SignalOutALU.PortB.S0),
			[ "S1" ] = (Port.PortB, V1SignalOutALU.PortB.S1),
			[ "S2" ] = (Port.PortB, V1SignalOutALU.PortB.S2),
			[ "S3" ] = (Port.PortB, V1SignalOutALU.PortB.S3),

			[ "CN" ] = (Port.PortB, V1SignalOutALU.PortB.CN),
			[ "M" ] = (Port.PortB, V1SignalOutALU.PortB.M)
		};

		public void ConfigureSync( string hubUrl, ILogger? logger = null )
		{
			if( string.IsNullOrWhiteSpace( hubUrl ) ) return;
			_logger.LogInformation( "AluController: ConfigureSync-> { Hub}", hubUrl );
			try
			{
				if( _syncClient != null ) StopSync();

				_syncClient = new SyncClient( hubUrl, logger ?? _logger );
				_syncClient.RemotePinToggled += ( pin, state ) =>
				{
					try
					{
						RemotePinToggled?.Invoke( pin, state );
					}
					catch( Exception ex )
					{
						_logger.LogWarning( ex, "RemotePinToggled handler error: { Message}", ex.Message );
					}

					try
					{
						// Do NOT send remote input back to the hub (no echo),
						// but report outputs to the hub after hardware write.
						ApplyPinToHardware( pin, state, forwardInputToSync: false, reportOutputsToSync: true );
					}
					catch( Exception ex )
					{
						_logger.LogWarning( ex, "ApplyPinToHardware( remote ) failed: { Message}", ex.Message );
					}
				};

				_ = Task.Run( async () =>
				{
					try { await _syncClient.StartAsync().ConfigureAwait( false ); }
					catch( Exception ex )
					{
						_logger.LogWarning( ex, "SyncClient.StartAsync failed: { Message}", ex.Message );
					}
				} );
			}
			catch( Exception ex )
			{
				_logger.LogWarning( ex, "ConfigureSync failed: {Message}", ex.Message );
				_syncClient = null;
			}
		}

		private void StopSync()
		{
			if( _syncClient == null ) return;

			try
			{
				_syncClient.RemotePinToggled -= ( pin, state ) => { };
				_ = Task.Run( async () =>
				{
					try { await _syncClient.StopAsync().ConfigureAwait( false ); } catch { }
					try { await _syncClient.DisposeAsync().ConfigureAwait( false ); } catch { }
				} );
			}
			catch( Exception ex )
			{
				_logger.LogWarning( ex, "StopSync failed: { Message}", ex.Message );
			}
			finally
			{
				_syncClient = null;
			}
		}

		public AluOutputs ReadOutputs()
		{
			byte rawV1PortA = _v1.ReadRegisterSafe( Register.GPIO, Port.PortA );
			byte rawV1PortB = _v1.ReadRegisterSafe( Register.GPIO, Port.PortB );
			byte rawV2PortB = _v2.ReadRegisterSafe( Register.GPIO, Port.PortB );

			byte rawV1PortBOlat;

			(rawV1PortB, rawV1PortBOlat ) = _v1.ReadOlatAndGpioSafe( Port.PortA );

			var binV1PortA = Convert.ToString( rawV1PortA, 2 ).PadLeft( 8, '0' );
			var binV1PortB = Convert.ToString( rawV1PortB, 2 ).PadLeft( 8, '0' );
			var binV2PortB = Convert.ToString( rawV2PortB, 2 ).PadLeft( 8, '0' );
			var hexV2PortB = rawV2PortB.ToString( "X2" );

			var binV1PortBOlat = Convert.ToString( rawV1PortBOlat, 2 ).PadLeft( 8, '0' );

			var outV2PortB = new AluOutputs( rawV2PortB, binV2PortB, hexV2PortB );
			OutputsUpdated?.Invoke( outV2PortB );

			SnapshotUpdated?.Invoke(
				new AluSnapshot(
					rawV1PortA, binV1PortA,
					rawV1PortB, binV1PortB,
					rawV2PortB, binV2PortB, hexV2PortB ) );

			Debug.WriteLine( $"-------------------" );
			Debug.WriteLine( $"V1 Port A: {binV1PortA}" );
			Debug.WriteLine( $"V1 Port B: {binV1PortBOlat}" );
			Debug.WriteLine( $"V1 Port B: {binV1PortB}" );
			Debug.WriteLine( $"V2 Port B: {binV2PortB}" );

			return outV2PortB;
		}

		public void ApplyPinToHardware(
			string pinName,
			bool state,
			bool forwardInputToSync = true,
			bool reportOutputsToSync = true )
		{
			_logger.LogInformation( "ApplyPinToHardware: {Pin} -> {State}", pinName, state );

			Debug.WriteLine( $"ApplyPinToHardware: {pinName} -> {state}" );

			try
			{
				// Pseudo input pin: ActiveLowData toggle (remote-settable)
				if( string.Equals( pinName, "ALD", StringComparison.OrdinalIgnoreCase ) )
				{
					_activeLowData = state;
					_logger.LogInformation( "ActiveLowData set to {ActiveLowData}", _activeLowData );

					if( forwardInputToSync )
						_ = _syncClient?.SendPinToggledAsync( "ALD", state );

					// No hardware IO line to set; but emit outputs so UIs refresh consistently.
					if( reportOutputsToSync )
					{
						AluOutputs aluOutputs = ReadOutputs();
						try { _ = _syncClient?.ReportAluOutputsAsync( aluOutputs.Raw, aluOutputs.Binary, aluOutputs.Hex ); }
						catch( Exception ex ) { _logger.LogWarning( ex, "Failed to report ALU outputs to SyncHub: {Message}", ex.Message ); }
					}

					return;
				}

				// Incoming state is a *signal level* on the wire (HIGH/LOW).
				// If ActiveLowData is enabled, invert the physical level we apply for data pins.
				var hwLevel = ( _activeLowData && s_dataPins.Contains( pinName ) ) ? !state : state;

				if( !s_pinMap.TryGetValue( pinName, out var entry ) )
				{
					_logger.LogWarning( "ApplyPinToHardware: unknown or read-only pin '{Pin}'", pinName );
					if( forwardInputToSync )
						_ = _syncClient?.SendPinToggledAsync( pinName, state );
					return;
				}

				// 1) Write input to real hardware
				SetOrResetPort( entry.Port, entry.Mask, hwLevel );

				// 2) Distribute input to Sync (other clients), if desired
				if( forwardInputToSync )
				{
					try { _ = _syncClient?.SendPinToggledAsync( pinName, state ); }
					catch( Exception ex ) { _logger.LogWarning( ex, "Failed to send PinToggled to SyncHub: {Message}", ex.Message ); }
				}

				// 3) Read outputs and signal locally
				var outputs = ReadOutputs();

				// 4) Report outputs to SyncHub, if desired
				if( reportOutputsToSync )
				{
					try { _ = _syncClient?.ReportAluOutputsAsync( outputs.Raw, outputs.Binary, outputs.Hex ); }
					catch( Exception ex ) { _logger.LogWarning( ex, "Failed to report ALU outputs to SyncHub: {Message}", ex.Message ); }
				}
			}
			catch( Exception ex )
			{
				_logger.LogError( ex, "ApplyPinToHardware failed for {Pin}: {Message}", pinName, ex.Message );
			}
		}

		private void SetOrResetPort( Port port, byte mask, bool state )
		{
			Debug.WriteLine( $"SetOrResetPort: Port={port}, Mask=0b{Convert.ToString( mask, 2 ).PadLeft( 8, '0' )}, State={state}" );
			if( state )
				_v1.SetPort( port, mask );
			else
				_v1.ResetPort( port, mask );
		}

		public void Dispose()
		{
			StopSync();
			_cts?.Dispose();
			_v1?.Dispose();
			_v2?.Dispose();
		}

		public void ApplySyncStateToHardware( SyncState state )
		{
			if( state is null )
				throw new ArgumentNullException( nameof( state ) );

			foreach( var (pin, value) in state.Pins )
			{
				// Initial-Apply: NICHT ins Sync forwarden (sonst Broadcast/Echo beim Start)
				ApplyPinToHardware( pin, value, forwardInputToSync: false, reportOutputsToSync: false );
			}

			// lokalen Status nachziehen
			ReadOutputs();
		}
	}
}