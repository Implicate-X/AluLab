using System.Text.RegularExpressions;
using Iot.Device.Ft4232H;
using Iot.Device.FtCommon;

namespace AluLab.Board.Communication
{
	/// <summary>
	/// Provides access to FT4232H devices configured for I2C, GPIO, and SPI communication channels used in laboratory
	/// bridge hardware.
	/// </summary>
	/// <remarks>
	/// <para> The <see cref="SerialBus"/> class is responsible for discovering connected FTDI FT4232H devices and mapping their
	/// individual channels to fixed roles used by the laboratory bridge hardware. </para>
	/// <para> Mapping is performed by <see cref="Initialize"/> using the device <c>Description</c> (e.g. <c>LabBridge.1</c>,
	/// <c>LabBridge.2</c>) and the FT channel letter (<see cref="FtChannel"/> A/B). After successful initialization, the
	/// channel properties (<see cref="ChannelI2cInOut"/>, <see cref="ChannelPioInOut"/>, <see cref="ChannelSpiDispl"/>,
	/// <see cref="ChannelSpiTouch"/>) can be used to communicate with I/O extenders, display, and touch controller. </para>
	/// <para> Accessing a channel property before initialization (or if initialization failed) throws an
	/// <see cref="ArgumentNullException"/>. </para>
	/// </remarks>
	public partial class SerialBus
	{
		/// <summary>
		/// Expected base description for the first bridge device. Used to select the FT4232H channels that provide
		/// I2C and GPIO access for the I/O extender boards.
		/// </summary>
		private const string LAB_BRIDGE_1 = "LabBridge.1";

		/// <summary>
		/// Expected base description for the second bridge device. Used to select the FT4232H channels that provide
		/// SPI access for the display and touch controller.
		/// </summary>
		private const string LAB_BRIDGE_2 = "LabBridge.2";

		/// <summary>
		/// Backing field for <see cref="ChannelI2cInOut"/>.
		/// </summary>
		protected Ft4232HDevice? _channelI2cInOut = null;

		/// <summary>
		/// Backing field for <see cref="ChannelPioInOut"/>.
		/// </summary>
		protected Ft4232HDevice? _channelPioInOut = null;

		/// <summary>
		/// Backing field for <see cref="ChannelSpiDispl"/>.
		/// </summary>
		protected Ft4232HDevice? _channelSpiDispl = null;

		/// <summary>
		/// Backing field for <see cref="ChannelSpiTouch"/>.
		/// </summary>
		protected Ft4232HDevice? _channelSpiTouch = null;

		/// <summary>
		/// FT4232H device instance used for I2C communication with the I/O extenders V1 and V2.
		/// </summary>
		/// <exception cref="ArgumentNullException">
		/// Thrown when the channel has not been assigned (typically because <see cref="Initialize"/> was not called or
		/// did not find the required device/channel).
		/// </exception>
		public Ft4232HDevice ChannelI2cInOut
		{
			get => _channelI2cInOut ?? throw new ArgumentNullException( nameof( _channelI2cInOut ) );
			protected set => _channelI2cInOut = value;
		}

		/// <summary>
		/// FT4232H device instance used for GPIO communication with the I/O extenders V1 and V2.
		/// </summary>
		/// <exception cref="ArgumentNullException">
		/// Thrown when the channel has not been assigned (typically because <see cref="Initialize"/> was not called or
		/// did not find the required device/channel).
		/// </exception>
		public Ft4232HDevice ChannelPioInOut
		{
			get => _channelPioInOut ?? throw new ArgumentNullException( nameof( _channelPioInOut ) );
			protected set => _channelPioInOut = value;
		}

		/// <summary>
		/// FT4232H device instance used for SPI communication with the display.
		/// </summary>
		/// <exception cref="ArgumentNullException">
		/// Thrown when the channel has not been assigned (typically because <see cref="Initialize"/> was not called or
		/// did not find the required device/channel).
		/// </exception>
		public Ft4232HDevice ChannelSpiDispl
		{
			get => _channelSpiDispl ?? throw new ArgumentNullException( nameof( _channelSpiDispl ) );
			protected set => _channelSpiDispl = value;
		}

		/// <summary>
		/// FT4232H device instance used for SPI communication with the touch controller.
		/// </summary>
		/// <exception cref="ArgumentNullException">
		/// Thrown when the channel has not been assigned (typically because <see cref="Initialize"/> was not called or
		/// did not find the required device/channel).
		/// </exception>
		public Ft4232HDevice ChannelSpiTouch
		{
			get => _channelSpiTouch ?? throw new ArgumentNullException( nameof( _channelSpiTouch ) );
			protected set => _channelSpiTouch = value;
		}

		/// <summary>
		/// Creates the regular expression used to normalize FTDI device descriptions to the base LabBridge identifier.
		/// </summary>
		/// <remarks>
		/// Accepts the base descriptions <c>LabBridge.1</c> and <c>LabBridge.2</c> and optionally a trailing channel
		/// suffix (<c>A</c>.. <c>D</c>) separated by whitespace (e.g. <c>LabBridge.1 A</c>). The returned regex is used
		/// to extract the base identifier (<c>LabBridge.1</c>/<c>LabBridge.2</c>) for subsequent channel mapping.
		/// </remarks>
		[GeneratedRegex( @"^(LabBridge\.1|LabBridge\.2)(?:\s+[A-D])?$", RegexOptions.CultureInvariant )]
		private static partial Regex LabBridgeRegex();

		/// <summary>
		/// Enumerates and assigns FT4232H devices to their respective communication channels based on channel type and
		/// device description.
		/// </summary>
		/// <remarks>
		/// <para> The method scans <see cref="Ftx232HDevice.GetFtx232H"/> and assigns channels according to the following rules: </para>
		/// <list type="bullet">
		/// <item><description><c>LabBridge.1</c> / Channel A → I2C (<see cref="ChannelI2cInOut"/>)</description></item>
		/// <item><description><c>LabBridge.1</c> / Channel B → GPIO (<see cref="ChannelPioInOut"/>)</description></item>
		/// <item><description><c>LabBridge.2</c> / Channel A → SPI display (<see cref="ChannelSpiDispl"/>)</description></item>
		/// <item><description><c>LabBridge.2</c> / Channel B → SPI touch (<see cref="ChannelSpiTouch"/>)</description></item>
		/// </list>
		/// <para> Only devices of type <see cref="FtDeviceType.Ft4232H"/> are considered. The method returns early as soon as all
		/// four required channels have been found. </para>
		/// </remarks>
		/// <returns>
		/// <see langword="true"/> if all required devices (I2C board, GPIO board, SPI touch, SPI display) were found and
		/// assigned; otherwise, <see langword="false"/>.
		/// </returns>
		public bool Initialize()
		{
			List<Ftx232HDevice> ftx232HDevices = Ftx232HDevice.GetFtx232H();
			if( ftx232HDevices.Count <= 0 )
			{
				return false;
			}

			var regex = LabBridgeRegex();

			foreach( var ftx232HDevice in ftx232HDevices )
			{
				var type = ftx232HDevice.Type;
				var channel = ftx232HDevice.Channel;
				var description = ftx232HDevice.Description;

				var match = regex.Match( description );
				var baseDesc = match.Success ? match.Groups[ 1 ].Value : string.Empty;

				_ = (type, channel, baseDesc) switch
				{
					(FtDeviceType.Ft4232H, FtChannel.A, LAB_BRIDGE_1 ) => _channelI2cInOut = new Ft4232HDevice( ftx232HDevice ),
					(FtDeviceType.Ft4232H, FtChannel.B, LAB_BRIDGE_1 ) => _channelPioInOut = new Ft4232HDevice( ftx232HDevice ),
					(FtDeviceType.Ft4232H, FtChannel.A, LAB_BRIDGE_2 ) => _channelSpiDispl = new Ft4232HDevice( ftx232HDevice ),
					(FtDeviceType.Ft4232H, FtChannel.B, LAB_BRIDGE_2 ) => _channelSpiTouch = new Ft4232HDevice( ftx232HDevice ),
					_ => null
				};

				if( _channelI2cInOut != null &&
					_channelPioInOut != null &&
					_channelSpiDispl != null &&
					_channelSpiTouch != null )
				{
					return true;
				}
			}

			return false;
		}

	}
}
