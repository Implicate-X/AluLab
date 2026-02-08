namespace AluLab.Board.Display
{
	/// <summary>
	/// Represents all supported commands for the ILI9341 display controller.
	/// </summary>
	internal enum Ili9341Command : byte
	{
		/// <summary>No operation.</summary>
		Nop = 0x00,
		/// <summary>Performs a software reset of the display controller.</summary>
		SoftwareReset = 0x01,
		/// <summary>Reads the display identification information.</summary>
		ReadDisplayIdentificationInformation = 0x04,
		/// <summary>Reads the display status.</summary>
		ReadDisplayStatus = 0x09,

		/// <summary>Reads the display power mode.</summary>
		ReadDisplayPowerMode = 0x0A,
		/// <summary>Reads the display MAD control register.</summary>
		ReadDisplayMadControl = 0x0B,
		/// <summary>Reads the display pixel format.</summary>
		ReadDisplayPixelFormat = 0x0C,
		/// <summary>Reads the display image format.</summary>
		ReadDisplayImageFormat = 0x0D,
		/// <summary>Reads the display signal mode.</summary>
		ReadDisplaySignalMode = 0x0E,
		/// <summary>Reads the display self-diagnostic result.</summary>
		ReadDisplaySelfDiagnosticResult = 0x0F,

		/// <summary>Puts the display into sleep mode.</summary>
		EnterSleepMode = 0x10,
		/// <summary>Wakes the display from sleep mode.</summary>
		SleepOut = 0x11,
		/// <summary>Enables partial display mode.</summary>
		PartialModeOn = 0x12,
		/// <summary>Enables normal display mode.</summary>
		NormalDisplayModeOn = 0x13,

		/// <summary>Disables display inversion.</summary>
		DisplayInversionOff = 0x20,
		/// <summary>Enables display inversion.</summary>
		DisplayInversionOn = 0x21,
		/// <summary>Sets the gamma curve.</summary>
		GammaSet = 0x26,
		/// <summary>Turns the display off.</summary>
		DisplayOff = 0x28,
		/// <summary>Turns the display on.</summary>
		DisplayOn = 0x29,

		/// <summary>Sets the column address.</summary>
		ColumnAddressSet = 0x2A,
		/// <summary>Sets the page address.</summary>
		PageAddressSet = 0x2B,
		/// <summary>Writes memory to the display.</summary>
		MemoryWrite = 0x2C,
		/// <summary>Sets the color.</summary>
		ColorSet = 0x2D,
		/// <summary>Reads memory from the display.</summary>
		MemoryRead = 0x2E,

		/// <summary>Sets the partial area of the display.</summary>
		PartialArea = 0x30,
		/// <summary>Defines vertical scrolling parameters.</summary>
		VerticalScrollingDefinition = 0x33,
		/// <summary>Disables tearing effect line output.</summary>
		TearingEffectLineOff = 0x34,
		/// <summary>Enables tearing effect line output.</summary>
		TearingEffectLineOn = 0x35,
		/// <summary>Controls memory access (orientation, color order, etc.).</summary>
		MemoryAccessControl = 0x36,
		/// <summary>Sets the vertical scrolling start address.</summary>
		VerticalScrollingStartAccess = 0x37,
		/// <summary>Disables idle mode.</summary>
		IdleModeOff = 0x38,
		/// <summary>Enables idle mode.</summary>
		IdleModeOn = 0x39,
		/// <summary>Sets the pixel format for the display.</summary>
		ColModPixelFormatSet = 0x3A,
		/// <summary>Continues writing memory to the display.</summary>
		WriteMemoryContinue = 0x3C,
		/// <summary>Continues reading memory from the display.</summary>
		ReadMemoryContinue = 0x3E,
		/// <summary>Reads the tear scanline.</summary>
		ReadTearScanline = 0x44,
		/// <summary>Gets the current scanline.</summary>
		GetScanline = 0x45,
		/// <summary>Writes the display brightness.</summary>
		WriteDisplayBrightness = 0x51,
		/// <summary>Reads the display brightness.</summary>
		ReadDisplayBrightness = 0x52,
		/// <summary>Writes the control display register.</summary>
		WriteControlDisplay = 0x53,
		/// <summary>Reads the control display register.</summary>
		ReadControlDisplay = 0x54,
		/// <summary>Writes the content adaptive brightness control register.</summary>
		WriteContentAdaptiveBrightnessControl = 0x55,
		/// <summary>Reads the content adaptive brightness control register.</summary>
		ReadContentAdaptiveBrightnessControl = 0x56,
		/// <summary>Writes the minimum brightness for CABC.</summary>
		WriteCABCMinimumBrightness = 0x5E,
		/// <summary>Reads the minimum brightness for CABC.</summary>
		ReadCABCMinimumBrighness = 0x5F,
		/// <summary>Reads the first display ID.</summary>
		ReadId1 = 0xDA,
		/// <summary>Reads the second display ID.</summary>
		ReadId2 = 0xDB,
		/// <summary>Reads the third display ID.</summary>
		ReadId3 = 0xDC,
		// ILI9341_RDID4 = 0xDD,
		/// <summary>Controls the RGB interface signal.</summary>
		RgbInterfaceSignalControl = 0xB0,
		/// <summary>Controls the frame rate in normal mode.</summary>
		FrameRateControlInNormalMode = 0xB1,
		/// <summary>Controls the frame rate in idle mode.</summary>
		FrameRateControlInIdleMode = 0xB2,
		/// <summary>Controls the frame rate in partial mode.</summary>
		FrameRateControlInPartialMode = 0xB3,
		/// <summary>Controls display inversion.</summary>
		DisplayInversionControl = 0xB4,
		/// <summary>Controls the blanking porch.</summary>
		BlankingPorchControl = 0xB5,
		/// <summary>Controls display functions.</summary>
		DisplayFunctionControl = 0xB6,
		/// <summary>Sets the entry mode.</summary>
		EntryModeSet = 0xB7,
		/// <summary>Controls backlight 1.</summary>
		BacklightControl1 = 0xB8,
		/// <summary>Controls backlight 2.</summary>
		BacklightControl2 = 0xB9,
		/// <summary>Controls backlight 3.</summary>
		BacklightControl3 = 0xBA,
		/// <summary>Controls backlight 4.</summary>
		BacklightControl4 = 0xBB,
		/// <summary>Controls backlight 5.</summary>
		BacklightControl5 = 0xBC,
		/// <summary>Controls backlight 7.</summary>
		BacklightControl7 = 0xBE,
		/// <summary>Controls backlight 8.</summary>
		BacklightControl8 = 0xBF,

		/// <summary>Controls power 1.</summary>
		PowerControl1 = 0xC0,
		/// <summary>Controls power 2.</summary>
		PowerControl2 = 0xC1,
		// ILI9341_PWCTR3  = 0xC2,
		// ILI9341_PWCTR4  = 0xC3,
		// ILI9341_PWCTR5  = 0xC4,
		/// <summary>Controls VCOM 1.</summary>
		VcomControl1 = 0xC5,
		/// <summary>Controls VCOM 2.</summary>
		VcomControl2 = 0xC7,

		/// <summary>Writes to non-volatile memory.</summary>
		NvMemoryWrite = 0xD0,
		/// <summary>Sets the non-volatile memory protection key.</summary>
		NvMemoryProtectionKey = 0xD1,
		/// <summary>Reads the non-volatile memory status.</summary>
		NvMemoryStatusRead = 0xD2,
		/// <summary>Reads the fourth display ID.</summary>
		ReadId4 = 0xD3,

		/// <summary>Sets positive gamma correction.</summary>
		PositiveGammaCorrection = 0xE0,
		/// <summary>Sets negative gamma correction.</summary>
		NegativeGammaCorrection = 0xE1,
		/// <summary>Controls binary gamma 1.</summary>
		BinaryGammaControl1 = 0xE2,
		/// <summary>Controls binary gamma 2.</summary>
		BinaryGammaControl2 = 0xE3,

		/// <summary>Controls the interface.</summary>
		InterfaceControl = 0xF6,
		// ILI9341_PWCTR6 = 0xFC,
	}

	internal enum MemoryAccessControlFlag : byte
	{
		/// <summary> Row Address Order. </summary>
		MY = 0b10000000,

		/// <summary> Column Address Order. </summary>
		MX = 0b01000000,

		/// <summary> Row / Column Exchange. </summary>
		MV = 0b00100000,

		/// <summary> Vertical Refresh Order.<br/>LCD vertical refresh direction control. </summary>
		ML = 0b00010000,

		/// <summary> RGB-BGR Order Color selector switch control.<br/>
		/// (0=RGB color filter panel, 1=BGR color filter panel).</summary>
		BGR = 0b00001000,

		/// <summary> Horizontal Refresh ORDER.<br/>LCD horizontal refreshing direction control. </summary>
		MH = 0b00000100

	}
}
