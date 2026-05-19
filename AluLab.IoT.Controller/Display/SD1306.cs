using System;
using System.Collections;
using System.Text;
using System.Threading;
using GHIElectronics.TinyCLR.Devices.I2c;

namespace ImplicateX.Display
{
	public class SD1306
	{
		/// <summary> No operation. </summary>
		private const byte NOP = 0x00;

		/// <summary> Draw 'off' pixels. </summary>
		private const byte BLACK = 0;

		/// <summary> Draw 'on' pixels. </summary>
		private const byte WHITE = 1;

		/// <summary> Invert pixels. </summary>
		private const byte INVERSE = 2;

		/// <summary> Enable Charge pump. </summary>
		private const byte ENABLE_CHARGEPUMP = 0x14;

		/// <summary> Set Memory Addressing Mode. </summary>
		private const byte MEMORYMODE = 0x20;

		/// <summary> Set Column Address. </summary>
		private const byte COLUMNADDR = 0x21;

		/// <summary> Set Page Address. </summary>
		private const byte PAGEADDR = 0x22;

		/// <summary> Set start line address. </summary>
		private const byte SETSTARTLINE = 0x40;

		/// <summary> The suggested ratio. </summary>
		private const byte SUGGESTED_RATIO = 0x80;

		/// <summary> Set Contrast Control for BANK0. </summary>
		private const byte SETCONTRAST = 0x81;

		/// <summary> Set Segment Re-map. </summary>
		private const byte SEGREMAP = 0xA1;

		/// <summary> Set Multiplex Ratio. </summary>
		private const byte SETMULTIPLEX = 0xA8;

		/// <summary> Set Display OFF. </summary>
		private const byte DISPLAYOFF = 0xAE;

		/// <summary> Set Display ON. </summary>
		private const byte DISPLAYON = 0xAF;

		/// <summary> Set COM Output Scan Direction. </summary>
		private const byte COMSCANINC = 0xC0;

		/// <summary> Set COM Output Scan Direction. </summary>
		private const byte COMSCANDEC = 0xC8;

		/// <summary> Set display offset. </summary>
		private const byte SETDISPLAYOFFSET = 0xD3;

		/// <summary> Set display clock divide ratio / oscillator frequency. </summary>
		private const byte SETDISPLAYCLOCKDIV = 0xD5;



		/// <summary> Set Charge Pump enable/disable. </summary>
		private const byte CHARGEPUMP = 0x8D;
		private const byte DISPLAYALLON_RESUME = 0xA4; ///< See datasheet
		private const byte DISPLAYALLON = 0xA5;        ///< Not currently used
		private const byte NORMALDISPLAY = 0xA6;       ///< See datasheet
		private const byte INVERTDISPLAY = 0xA7;       ///< See datasheet





		private const byte SETPRECHARGE = 0xD9;        ///< See datasheet
		private const byte SETCOMPINS = 0xDA;          ///< See datasheet
		private const byte SETVCOMDETECT = 0xDB;       ///< See datasheet

		private const byte SETLOWCOLUMN = 0x00;  ///< Not currently used
		private const byte SETHIGHCOLUMN = 0x10; ///< Not currently used


		private const byte EXTERNALVCC = 0x01;  ///< External display voltage source
		private const byte SWITCHCAPVCC = 0x02; ///< Gen. display voltage from 3.3V

		private const byte RIGHT_HORIZONTAL_SCROLL = 0x26;              ///< Init rt scroll
		private const byte LEFT_HORIZONTAL_SCROLL = 0x27;               ///< Init left scroll
		private const byte VERTICAL_AND_RIGHT_HORIZONTAL_SCROLL = 0x29; ///< Init diag scroll
		private const byte VERTICAL_AND_LEFT_HORIZONTAL_SCROLL = 0x2A;  ///< Init diag scroll
		private const byte DEACTIVATE_SCROLL = 0x2E;                    ///< Stop scroll
		private const byte ACTIVATE_SCROLL = 0x2F;                      ///< Start scroll
		private const byte SET_VERTICAL_SCROLL_AREA = 0xA3;             ///< Set scroll range

		private byte[] vram = new byte[ 128 * 32 / 8 + 1 ];
		private readonly I2cDevice display;

		public int Width => 128;
		public int Height => 32;

		public static I2cConnectionSettings GetConnectionSettings( int slaveAddress = 0x3C )
		{
			return new I2cConnectionSettings( slaveAddress )
			{
				AddressFormat = I2cAddressFormat.SevenBit,
				BusSpeed = 400_000U,
			};
		}

		public SD1306( I2cDevice i2cDevice )
		{
			vram[ 0 ] = 0x40;
			display = i2cDevice;

			Initialize();
		}

		private void Initialize()
		{
			SendCommand( new byte[]
			{
				DISPLAYOFF,
				SETDISPLAYCLOCKDIV,
				SUGGESTED_RATIO,
				SETMULTIPLEX,
				( Height == 32 ) ? ( byte )0x1F : ( byte )0x3F
			} );

			SendCommand( new byte[]
			{
				SETDISPLAYOFFSET,
				NOP,
				SETSTARTLINE | 0x00, // Line #0
				CHARGEPUMP,
				ENABLE_CHARGEPUMP
			} );

			SendCommand( new byte[]
			{
				MEMORYMODE,
				0x00,	// No offset
				SEGREMAP,
				COMSCANDEC
			} );

			SendCommand( new byte[]
			{
				SETCOMPINS,
				0x02,
				SETCONTRAST,
				0x8F,
				SETPRECHARGE,
				0xF1
			} );

			SendCommand( new byte[]
			{
				SETVCOMDETECT,
				0x40,
				DISPLAYALLON_RESUME,
				NORMALDISPLAY,
				DEACTIVATE_SCROLL,
				DISPLAYON
			} );

			SendCommand( new byte[]
			{
				0x20,
				0x00,
				0x21,
				0x00,
				0x7F,
				0x22,
				0x00,
				0xFF
			} );

			//SendCommand( 0x14 ); //set(0x10) disable proper vcc

			//SendCommand( 0x20 );
			//SendCommand( 0x00 );
			//SendCommand( 0xA1 ); //set segment re-map 95 to 0
			//SendCommand( 0xC8 ); //mirror the screen

			//SendCommand( 0xDA ); //set com pins hardware configuration
			//SendCommand( ( Height == 32 ) ? ( byte )0x02 : ( byte )0x12 );

			//SendCommand( 0x81 ); //set contrast control register
			//SendCommand( ( Height == 32 ) ? ( byte )0x8F : ( byte )0xCF );

			//SendCommand( 0xD9 ); //set pre-charge period
			//SendCommand( 0xF1 );

			//SendCommand( 0xDB ); //set vcomh
			//SendCommand( 0x40 ); //set startline 0x0

			//SendCommand( 0xA4 );
			//SendCommand( 0xA6 ); //set normal display
			//SendCommand( 0x2E ); //change
			//SendCommand( 0xAF ); //turn on oled panel

			//SendCommand( 0x20 );
			//SendCommand( 0x00 );
			//SendCommand( 0x21 );
			//SendCommand( 0 );
			//SendCommand( 128 - 1 );
			//SendCommand( 0x22 );
			//SendCommand( 0 );
			//SendCommand( 0xFF );
		}

		public void Dispose() => display.Dispose();

		private void SendCommand( byte cmd )
		{
			byte[] buffer = new byte[ 2 ];

			buffer[ 0 ] = ( byte )0x00;
			buffer[ 1 ] = cmd;

			display.Write( buffer );
		}

		private void SendCommand( byte[] sequence )
		{
			byte[] buffer = new byte[ sequence.Length + 1 ];

			buffer[ 0 ] = ( byte )0x00;

			Array.Copy( sequence, 0, buffer, 1, sequence.Length );

			display.Write( buffer );
		}

		public void ClearBuffer()
		{
			Array.Clear( vram, 0, vram.Length );
		}

		public void SetColorFormat( bool invert ) => SendCommand( ( byte )( invert ? 0xA7 : 0xA6 ) );

		public void DrawBufferNative( byte[] buffer ) => DrawBufferNative( buffer, 0, buffer.Length );

		public void DrawBufferNative( byte[] buffer, int offset, int count )
		{
			Array.Copy( buffer, offset, vram, 1, count );

			display.Write( vram );
			display.Write( vram );
		}
	}
}
