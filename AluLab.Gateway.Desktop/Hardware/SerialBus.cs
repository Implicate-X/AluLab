using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Iot.Device.Ft4232H;
using Iot.Device.FtCommon;

namespace AluLab.Workbench.Hardware
{
	/// <summary>
	/// Provides access to FT4232H devices configured for I2C, GPIO, and SPI communication channels used in laboratory
	/// bridge hardware.
	/// </summary>
	/// <remarks>The SerialBus class manages the assignment of FT4232H devices to specific communication channels
	/// required for interfacing with I/O extenders, displays, and touch controllers. Use the Initialize method to detect
	/// and assign the appropriate devices before accessing the channel properties. This class is intended for scenarios
	/// where multiple FT4232H devices are present and need to be mapped to their respective roles based on device
	/// descriptions.</remarks>
	public class SerialBus()
	{
		private const string LAB_BRIDGE_1 = "LabBridge.1";
		private const string LAB_BRIDGE_2 = "LabBridge.2";

		protected Ft4232HDevice? _channelI2cInOut = null;
		protected Ft4232HDevice? _channelPioInOut = null;
		protected Ft4232HDevice? _channelSpiDispl = null;
		protected Ft4232HDevice? _channelSpiTouch = null;

		/// <summary>
		/// FT4232H device instance used for I2C communication with the I/O extenders V1 and V2.
		/// </summary>
		public Ft4232HDevice ChannelI2cInOut
		{
			get => _channelI2cInOut ?? throw new ArgumentNullException( nameof( _channelI2cInOut ) );
			protected set => _channelI2cInOut = value;
		}

		/// <summary>
		/// FT4232H device instance used for GPIO communication with the I/O extenders V1 and V2.
		/// </summary>
		public Ft4232HDevice ChannelPioInOut
		{
			get => _channelPioInOut ?? throw new ArgumentNullException( nameof( _channelPioInOut ) );
			protected set => _channelPioInOut = value;
		}

		/// <summary>
		/// FT4232H device instance used for SPI communication with the display.
		/// </summary>
		public Ft4232HDevice ChannelSpiDispl
		{
			get => _channelSpiDispl ?? throw new ArgumentNullException( nameof( _channelSpiDispl ) );
			protected set => _channelSpiDispl = value;
		}

		/// <summary>
		/// FT4232H device instance used for SPI communication with the touch controller.
		/// </summary>
		public Ft4232HDevice ChannelSpiTouch
		{
			get => _channelSpiTouch ?? throw new ArgumentNullException( nameof( _channelSpiTouch ) );
			protected set => _channelSpiTouch = value;
		}

		/// <summary>
		/// Enumerates and assigns FT4232H devices to their respective communication channels
		/// based on channel type and device description.
		/// </summary>
		/// <returns>
		/// <see langword="true"/> if all required devices (I2C board, SPI touch, SPI display)
		/// were found and assigned; otherwise, <see langword="false"/>.</returns>
		public bool Initialize()
		{
			List<Ftx232HDevice> ftx232HDevices = Ftx232HDevice.GetFtx232H();
			if( ftx232HDevices.Count <= 0 )
			{
				return false;
			}

			var regex = new Regex( $@"^({LAB_BRIDGE_1}|{LAB_BRIDGE_2})", RegexOptions.Compiled );

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