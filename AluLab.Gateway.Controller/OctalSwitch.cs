using GHIElectronics.TinyCLR.Devices.I2c;


namespace AluLab.Gateway.Controller
{
	public class OctalSwitchV4 : OctalSwitchBase
	{
		public OctalSwitchV4( I2cDevice i2CDevice ) : base( i2CDevice )
		{
		}

		public static byte SCLK_A = 0b_0000_0001;
		public static byte SCLK_B = 0b_0000_0010;
		public static byte SCLK_C = 0b_0000_0100;
		public static byte MOSI_A = 0b_0000_1000;
		public static byte MOSI_B = 0b_0001_0000;
		public static byte MOSI_C = 0b_0010_0000;
		public static byte MISO_A = 0b_0100_0000;
		public static byte MISO_B = 0b_1000_0000;

		public static byte Address { get; } = 0x4C;
	}

	public class OctalSwitchV5 : OctalSwitchBase
	{
		public OctalSwitchV5( I2cDevice i2CDevice ) : base( i2CDevice )
		{
		}

		public static byte MISO_C = 0b_0000_0001;
		public static byte CS_A = 0b_0000_0010;
		public static byte CS_B = 0b_0000_0100;
		public static byte CS_C = 0b_0000_1000;
		public static byte DC_RS_A = 0b_0001_0000;
		public static byte DC_RS_B = 0b_0010_0000;
		public static byte DC_RS_C = 0b_0100_0000;
		public static byte RESET_A = 0b_1000_0000;

		public static byte Address { get; } = 0x4D;
	}

	public class OctalSwitchV6 : OctalSwitchBase
	{
		public OctalSwitchV6( I2cDevice i2CDevice ) : base( i2CDevice )
		{
		}

		public static byte RESET_B = 0b_0000_0001;
		public static byte RESET_C = 0b_0000_0010;
		public static byte T_CLK_A = 0b_0000_0100;
		public static byte T_CLK_B = 0b_0000_1000;
		public static byte T_CLK_C = 0b_0001_0000;
		public static byte T_DI_A = 0b_0010_0000;
		public static byte T_DI_B = 0b_0100_0000;
		public static byte T_DI_C = 0b_1000_0000;

		public static byte Address { get; } = 0x4E;
	}

	public class OctalSwitchV7 : OctalSwitchBase
	{
		public OctalSwitchV7( I2cDevice i2CDevice ) : base( i2CDevice )
		{
		}

		public static byte T_DO_A = 0b_0000_0001;
		public static byte T_DO_B = 0b_0000_0010;
		public static byte T_DO_C = 0b_0000_0100;
		public static byte T_CS_A = 0b_0000_1000;
		public static byte T_CS_B = 0b_0001_0000;
		public static byte T_CS_C = 0b_0010_0000;

		public static byte Address { get; } = 0x4F;
	}
}

