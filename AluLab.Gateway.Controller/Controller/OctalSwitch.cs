using GHIElectronics.TinyCLR.Devices.I2c;


namespace AluLab.Gateway.Controller
{
	public class OctalSwitchV4 : OctalSwitchBase
	{
		public OctalSwitchV4( I2cDevice i2CDevice ) : base( i2CDevice )
		{
		}

		public static int SCLK_A = 0;
		public static int SCLK_B = 1;
		public static int SCLK_C = 2;
		public static int MOSI_A = 3;
		public static int MOSI_B = 4;
		public static int MOSI_C = 5;
		public static int MISO_A = 6;
		public static int MISO_B = 7;

		public static byte Address { get; } = 0x4C;
	}

	public class OctalSwitchV5 : OctalSwitchBase
	{
		public OctalSwitchV5( I2cDevice i2CDevice ) : base( i2CDevice )
		{
		}

		public static int MISO_C   = 0;
		public static int CS_A     = 1;
		public static int CS_B     = 2;
		public static int CS_C     = 3;
		public static int DC_RS_A  = 4;
		public static int DC_RS_B  = 5;
		public static int DC_RS_C  = 6;
		public static int RESET_A  = 7;

		public static byte Address { get; } = 0x4D;
	}

	public class OctalSwitchV6 : OctalSwitchBase
	{
		public OctalSwitchV6( I2cDevice i2CDevice ) : base( i2CDevice )
		{
		}

		public static int RESET_B = 0;
		public static int RESET_C = 1;
		public static int T_CLK_A = 2;
		public static int T_CLK_B = 3;
		public static int T_CLK_C = 4;
		public static int T_DI_A  = 5;
		public static int T_DI_B  = 6;
		public static int T_DI_C  = 7;

		public static byte Address { get; } = 0x4E;
	}

	public class OctalSwitchV7 : OctalSwitchBase
	{
		public OctalSwitchV7( I2cDevice i2CDevice ) : base( i2CDevice )
		{
		}

		public static int T_DO_A = 0;
		public static int T_DO_B = 1;
		public static int T_DO_C = 2;
		public static int T_CS_A = 3;
		public static int T_CS_B = 4;
		public static int T_CS_C = 5;

		public static byte Address { get; } = 0x4F;
	}
}

