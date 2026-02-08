using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Iot.Device.Mcp23xxx;
using AluLab.Board.InputOutputExpander;
using AluLab.Common.Relay;

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
		/// Represents the current ALU output state in multiple representations.
		/// </summary>
		/// <param name="Raw">Raw value (byte) of the read output lines. </param>
		/// <param name="Binary">Binary string (always 8 characters, including leading zeros). </param>
		/// <param name="Hex">Hex string (always 2 digits, uppercase). </param>
		public record AluOutputs( byte Raw, string Binary, string Hex );

		/// <summary>
		/// Triggered as soon as outputs have been read (e.g., after <see cref="ReadOutputs"/> or <see cref="ApplyPinToHardware"/>).
		/// </summary>
		public event Action<AluOutputs>? OutputsUpdated;

		/// <summary>
		/// Triggered when the <see cref="SyncClient"/> receives a remote input event (pin + state).
		/// </summary>
		/// <remarks>
		/// This event only signals the remote input; the actual hardware update occurs afterwards
		/// (see handler in <see cref="ConfigureSync"/>).
		/// </remarks>
		public event Action<string, bool>? RemotePinToggled;

		/// <summary>
		/// Optional client for synchronizing inputs/outputs via a hub.
		/// </summary>
		private SyncClient? _syncClient;

		/// <summary>
		/// Maps logical pin names (e.g., “A0,” “S3”) to (port, bit mask) for writing to the hardware.
		/// </summary>
		/// <remarks>
		/// Pins that are not included in this map are considered unknown or read-only in this context
		/// and are not written to the hardware.
		/// </remarks>
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

		/// <summary>
		/// Configures synchronization via a hub and starts <see cref="SyncClient"/> in the background.
		/// </summary>
		/// <param name="hubUrl">URL of the SyncHub. Empty/whitespace values are ignored.</param>
		/// <param name="logger">Optional logger for the <see cref="SyncClient"/> (fallback: controller logger).</param>
		/// <remarks>
		/// <para> If synchronization is already active, it is first terminated via <c>StopSync()</c>. </para>
		/// <para> Incoming remote pin toggles are:
		/// <list type="number">
		/// <item><description>signaled as an event via <see cref="RemotePinToggled"/> to local listeners,</description></item>
		/// <item><description>applied to the hardware without sending the input event back to the hub (to avoid echo),</description></item>
		/// <item><description>and the subsequently read outputs are reported to the hub.</description></item>
		/// </list></para>
		/// </remarks>
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

		/// <summary>
		/// Stops active synchronization, terminates the <see cref="SyncClient"/>, and releases resources.
		/// </summary>
		/// <remarks>
		/// Stopping/disposing is deliberately performed asynchronously in the background so that callers are not blocked.
		/// Errors are logged but not thrown externally.
		/// </remarks>
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

		/// <summary>
		/// Reads the current output state of the ALU and signals it via <see cref="OutputsUpdated"/>.
		/// </summary>
		/// <returns>An <see cref="AluOutputs"/> snapshot (Raw/Binary/Hex).</returns>
		/// <remarks>
		/// The outputs are read from the GPIO register on <see cref="Port.PortB"/>.
		/// </remarks>
		public AluOutputs ReadOutputs()
		{
			byte raw = _v2.ReadRegisterSafe( Register.GPIO, Port.PortB );
			var binary = Convert.ToString( raw, 2 ).PadLeft( 8, '0' );
			var hex = raw.ToString( "X2" );
			var outp = new AluOutputs( raw, binary, hex );
			OutputsUpdated?.Invoke( outp );
			return outp;
		}

		/// <summary>
		/// Writes an input signal to the real hardware and then immediately reads back the outputs.
		/// <paramref name="forwardInputToSync"/> controls whether the input event is sent to the hub (to avoid echo).
		/// <paramref name="reportOutputsToSync"/> controls whether outputs are reported to the hub (so that all clients update).
		/// </summary>
		/// <param name="pinName">Logical pin name (e.g., “A0”, “S3”).</param>
		/// <param name="state"><see langword="true"/> to set, <see langword="false"/> to reset.</param>
		/// <param name="forwardInputToSync"> If <see langword="true"/>, the input event is sent to the SyncHub (for other clients).
		/// For remote calls, this value should typically be <see langword="false"/> to avoid echo. </param>
		/// <param name="reportOutputsToSync"> If <see langword="true"/>, the output snapshot is reported 
		/// to the SyncHub after writing and reading the outputs so that other clients can update their display. </param>
		/// <remarks>
		/// Procedure:
		/// <list type="number">
		/// <item><description>Resolve pin in <see cref="s_pinMap"/> (unknown/read-only is only optionally passed on to Sync).</description></item>
		/// <item><description>Set/reset bit on hardware (via <see cref="SetOrResetPort"/>).</description></item>
		/// <item><description>Optionally send input event to SyncHub.</description></item>
		/// <item><description>Read outputs (via <see cref="ReadOutputs"/>) and signal locally.</description></item>
		/// <item><description>Optionally report outputs to SyncHub.</description></item>
		/// </list>
		/// </remarks>
		public void ApplyPinToHardware(
			string pinName,
			bool state,
			bool forwardInputToSync = true,
			bool reportOutputsToSync = true )
		{
			_logger.LogInformation( "ApplyPinToHardware: {Pin} -> {State}", pinName, state );

			try
			{
				if( !s_pinMap.TryGetValue( pinName, out var entry ) )
				{
					_logger.LogWarning( "ApplyPinToHardware: unknown or read-only pin '{Pin}'", pinName );
					if( forwardInputToSync )
						_ = _syncClient?.SendPinToggledAsync( pinName, state );
					return;
				}

				// 1) Write input to real hardware
				SetOrResetPort( entry.Port, entry.Mask, state );

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

		/// <summary>
		/// Auxiliary method for setting or resetting a bit on an MCP23xxx port.
		/// </summary>
		/// <param name="port">Target port (<see cref="Port.PortA"/> or <see cref="Port.PortB"/>).</param>
		/// <param name="mask">Bit mask of the pin to be manipulated.</param>
		/// <param name="state"><see langword="true"/> = set, <see langword="false"/> = reset.
		private void SetOrResetPort( Port port, byte mask, bool state )
		{
			if( state )
				_v1.SetPort( port, mask );
			else
				_v1.ResetPort( port, mask );
		}

		/// <summary>
		/// Terminates any active synchronization and releases all resources used.
		/// </summary>
		public void Dispose()
		{
			StopSync();
			_cts?.Dispose();
			_v1?.Dispose();
			_v2?.Dispose();
		}
	}
}