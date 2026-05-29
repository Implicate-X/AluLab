using System;
using System.Threading;
using GHIElectronics.TinyCLR.Devices.Gpio;
using GHIElectronics.TinyCLR.Devices.I2c;

namespace AluLab.Gateway.Controller
{
	/// <summary>
	/// MAX14662<br/>
	/// Serially controlled 8 x SPST switch for	general purpose signal switching applications.
	/// </summary>
	public class OctalSwitchBase
	{
		/// <summary> Constructor. </summary>
		///
		/// <param name="slaveAddress"> (Optional) The slave address. </param>
		public OctalSwitchBase( I2cDevice i2CDevice )
		{
			Device = i2CDevice;
		}

		public I2cDevice Device { get; set; }

		/// <summary> Gets connection settings. </summary>
		///
		/// <returns> The connection settings. </returns>
		public static I2cConnectionSettings GetConnectionSettings( int slaveAddress = 0x4C )
		{
			return new I2cConnectionSettings( slaveAddress )
			{
				AddressFormat = I2cAddressFormat.SevenBit,
				BusSpeed = 400_000U,
			};
		}

		/// <summary> Initializes this object. </summary>
		///
		/// <returns> True if it succeeds, false if it fails. </returns>
		public bool Initialize()
		{
			try
			{
				byte[] rBuf = new byte[ 1 ];

				if( Device.ReadPartial( rBuf ).Status == I2cTransferStatus.SlaveAddressNotAcknowledged )
				{
					Device.Dispose();

					return false;
				}
			}
			catch( Exception )
			{
				return false;
			}

			return true;
		}


		public void ValidateBitPos( byte switchNumber )
		{
			if( switchNumber > 7 )
			{
				throw new IndexOutOfRangeException( $"Invalid bit position {switchNumber}." );
			}
		}

		public void SwitchOff( params byte[] switchNumbers )
		{
			byte switches = 0;

			if( switchNumbers.Length >= 1 )
			{
				byte[] rBuf = new byte[ 1 ];

				Device.ReadPartial( rBuf );

				switches = rBuf[ 0 ];

				for( byte i = 0; i < switchNumbers.Length; i++ )
				{
					byte switchNumber = switchNumbers[ i ];

					ValidateBitPos( switchNumber );

					switches &= ( byte )~( 1 << switchNumber );
				}
			}

			Device.WritePartial( new byte[] { 0x00, switches } );

			Thread.Sleep( 100 );
		}

		public void SwitchOn( params byte[] switchNumbers )
		{
			if( switchNumbers.Length < 1 )
			{
				return;
			}

			byte[] rBuf = new byte[ 1 ];

			Device.ReadPartial( rBuf );

			byte switches = rBuf[ 0 ];

			for( byte i = 0; i < switchNumbers.Length; i++ )
			{
				byte switchNumber = switchNumbers[ i ];

				ValidateBitPos( switchNumber );

				switches |= ( byte )( 1 << switchNumber );
			}

			Device.WritePartial( new byte[] { 0x00, switches } );
		}

		public bool SwitchState( byte switchNumber )
		{
			ValidateBitPos( switchNumber );

			byte[] rBuf = new byte[ 1 ];

			Device.ReadPartial( rBuf );

			return ( ( rBuf[0] >> switchNumber ) & 1 ) == 1;
		}
	}
}
