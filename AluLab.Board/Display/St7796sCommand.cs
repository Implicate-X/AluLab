namespace AluLab.Board.Display
{
    /// <summary>
    /// Represents all supported commands for the ST7796S display controller.
    /// </summary>
    internal enum St7796sCommand : byte
    {
        /// <summary>No operation (00h).<br />9.2.1</summary>
        Nop = 0x00,
        /// <summary>Software reset of the display controller (01h).<br />9.2.2</summary>
        SoftwareReset = 0x01,
        /// <summary>Read display identification information (04h).<br />9.2.3</summary>
        ReadDisplayId = 0x04,
        /// <summary>Read Number of the Errors on DSI (05h).<br />9.2.4</summary>
        ReadNumberOfErrorsOnDsi = 0x05,
        /// <summary>Read display status (09h).<br />9.2.5</summary>
        ReadDisplayStatus = 0x09,
        /// <summary>Read display power mode (0Ah).<br />9.2.6</summary>
        ReadDisplayPowerMode = 0x0A,
        /// <summary>Read display MADCTL register (0Bh).<br />9.2.7</summary>
        ReadDisplayMadctl = 0x0B,
        /// <summary>Read display pixel format (0Ch).<br />9.2.8</summary>
        ReadDisplayPixelFormat = 0x0C,
        /// <summary>Read display image format (0Dh).<br />9.2.9</summary>
        ReadDisplayImageFormat = 0x0D,
        /// <summary>Read display signal mode (0Eh).<br />9.2.10</summary>
        ReadDisplaySignalMode = 0x0E,
        /// <summary>Read display self-diagnostic result (0Fh).<br />9.2.11</summary>
        ReadDisplaySelfDiagnosticResult = 0x0F,
        /// <summary>Enter sleep mode (10h).<br />9.2.12</summary>
        EnterSleepMode = 0x10,
        /// <summary>Exit sleep mode (11h).<br />9.2.13</summary>
        SleepOut = 0x11,
        /// <summary>Partial mode on (12h).<br />9.2.14</summary>
        PartialModeOn = 0x12,
        /// <summary>Normal display mode on (13h).<br />9.2.15</summary>
        NormalDisplayModeOn = 0x13,
        /// <summary>Display inversion off (20h).<br />9.2.16</summary>
        DisplayInversionOff = 0x20,
        /// <summary>Display inversion on (21h).<br />9.2.17</summary>
        DisplayInversionOn = 0x21,
        /// <summary>Gamma set (26h).<br />9.2.18</summary>
        GammaSet = 0x26,
        /// <summary>Display off (28h).<br />9.2.19</summary>
        DisplayOff = 0x28,
        /// <summary>Display on (29h).<br />9.2.20</summary>
        DisplayOn = 0x29,
        /// <summary>Column address set (2Ah).<br />9.2.21</summary>
        ColumnAddressSet = 0x2A,
        /// <summary>Page address set (2Bh).<br />9.2.22</summary>
        PageAddressSet = 0x2B,
        /// <summary>Memory write (2Ch).<br />9.2.23</summary>
        MemoryWrite = 0x2C,
        /// <summary>Color set (2Dh).<br />9.2.24</summary>
        ColorSet = 0x2D,
        /// <summary>Memory read (2Eh).<br />9.2.25</summary>
        MemoryRead = 0x2E,
        /// <summary>Partial area (30h).<br />9.2.26</summary>
        PartialArea = 0x30,
        /// <summary>Vertical scrolling definition (33h).<br />9.2.27</summary>
        VerticalScrollingDefinition = 0x33,
        /// <summary>Tearing effect line off (34h).<br />9.2.28</summary>
        TearingEffectLineOff = 0x34,
        /// <summary>Tearing effect line on (35h).<br />9.2.29</summary>
        TearingEffectLineOn = 0x35,
        /// <summary>Memory access control (MADCTL) (36h).<br />9.2.30</summary>
        MemoryAccessControl = 0x36,
        /// <summary>Vertical scrolling start address (37h).<br />9.2.31</summary>
        VerticalScrollingStartAddress = 0x37,
        /// <summary>Idle mode off (38h).<br />9.2.32</summary>
        IdleModeOff = 0x38,
        /// <summary>Idle mode on (39h).<br />9.2.33</summary>
        IdleModeOn = 0x39,
        /// <summary>Pixel format set (COLMOD) (3Ah).<br />9.2.34</summary>
        ColModPixelFormatSet = 0x3A,
        /// <summary>Write memory continue (3Ch).<br />9.2.35</summary>
        WriteMemoryContinue = 0x3C,
        /// <summary>Read memory continue (3Eh).<br />9.2.36</summary>
        ReadMemoryContinue = 0x3E,
        /// <summary>Set tear scanline (44h).<br />9.2.37</summary>
        SetTearScanline = 0x44,
        /// <summary>Get scanline (45h).<br />9.2.38</summary>
        GetScanline = 0x45,
        /// <summary>Write display brightness (51h).<br />9.2.39</summary>
        WriteDisplayBrightness = 0x51,
        /// <summary>Read display brightness (52h).<br />9.2.40</summary>
        ReadDisplayBrightness = 0x52,
        /// <summary>Write control display register (53h).<br />9.2.41</summary>
        WriteControlDisplay = 0x53,
        /// <summary>Read control display register (54h).<br />9.2.42</summary>
        ReadControlDisplay = 0x54,
        /// <summary>Write content adaptive brightness control register (55h).<br />9.2.43</summary>
        WriteContentAdaptiveBrightnessControl = 0x55,
        /// <summary>Read content adaptive brightness control register (56h).<br />9.2.44</summary>
        ReadContentAdaptiveBrightnessControl = 0x56,
        /// <summary>Write CABC minimum brightness (5Eh).<br />9.2.45</summary>
        WriteCabcMinimumBrightness = 0x5E,
        /// <summary>Read CABC minimum brightness (5Fh).<br />9.2.46</summary>
        ReadCabcMinimumBrightness = 0x5F,
        /// <summary>Read ID1 (DAh).<br />9.2.47</summary>
        ReadId1 = 0xDA,
        /// <summary>Read ID2 (DBh).<br />9.2.48</summary>
        ReadId2 = 0xDB,
        /// <summary>Read ID3 (DCh).<br />9.2.49</summary>
        ReadId3 = 0xDC,
        /// <summary>RGB interface signal control (B0h).<br />9.2.50</summary>
        RgbInterfaceSignalControl = 0xB0,
        /// <summary>Frame rate control in normal mode (B1h).<br />9.2.51</summary>
        FrameRateControlNormal = 0xB1,
        /// <summary>Frame rate control in idle mode (B2h).<br />9.2.52</summary>
        FrameRateControlIdle = 0xB2,
        /// <summary>Frame rate control in partial mode (B3h).<br />9.2.53</summary>
        FrameRateControlPartial = 0xB3,
        /// <summary>Display inversion control (B4h).<br />9.2.54</summary>
        DisplayInversionControl = 0xB4,
        /// <summary>Blanking porch control (B5h).<br />9.2.55</summary>
        BlankingPorchControl = 0xB5,
        /// <summary>Display function control (B6h).<br />9.2.56</summary>
        DisplayFunctionControl = 0xB6,
        /// <summary>Entry mode set (B7h).<br />9.2.57</summary>
        EntryModeSet = 0xB7,
        /// <summary>Backlight control 1 (B8h).<br />9.2.58</summary>
        BacklightControl1 = 0xB8,
        /// <summary>Backlight control 2 (B9h).<br />9.2.59</summary>
        BacklightControl2 = 0xB9,
        /// <summary>Backlight control 3 (BAh).<br />9.2.60</summary>
        BacklightControl3 = 0xBA,
        /// <summary>Backlight control 4 (BBh).<br />9.2.61</summary>
        BacklightControl4 = 0xBB,
        /// <summary>Backlight control 5 (BCh).<br />9.2.62</summary>
        BacklightControl5 = 0xBC,
        /// <summary>Backlight control 7 (BEh).<br />9.2.63</summary>
        BacklightControl7 = 0xBE,
        /// <summary>Backlight control 8 (BFh).<br />9.2.64</summary>
        BacklightControl8 = 0xBF,
        /// <summary>Power control 1 (C0h).<br />9.2.65</summary>
        PowerControl1 = 0xC0,
        /// <summary>Power control 2 (C1h).<br />9.2.66</summary>
        PowerControl2 = 0xC1,
        /// <summary>Power control 3 (C2h).<br />9.2.67</summary>
        PowerControl3 = 0xC2,
        /// <summary>Power control 4 (C3h).<br />9.2.68</summary>
        PowerControl4 = 0xC3,
        /// <summary>Power control 5 (C4h).<br />9.2.69</summary>
        PowerControl5 = 0xC4,
        /// <summary>VCOM control 1 (C5h).<br />9.2.70</summary>
        VcomControl1 = 0xC5,
		/// <summary>Vcom Offset Register (C6h).<br />9.3.13</summary>
		VcomOffsetRegister = 0xC6,
		/// <summary>VCOM control 2 (C7h).<br />9.2.71</summary>
		VcomControl2 = 0xC7,
        /// <summary>NVM write (D0h).<br />9.2.72</summary>
        NvMemoryWrite = 0xD0,
        /// <summary>NVM protection key (D1h).<br />9.2.73</summary>
        NvMemoryProtectionKey = 0xD1,
        /// <summary>NVM status read (D2h).<br />9.2.74</summary>
        NvMemoryStatusRead = 0xD2,
        /// <summary>Read ID4 (D3h).<br />9.2.75</summary>
        ReadId4 = 0xD3,
        /// <summary>Positive gamma correction (E0h).<br />9.2.76</summary>
        PositiveGammaCorrection = 0xE0,
        /// <summary>Negative gamma correction (E1h).<br />9.2.77</summary>
        NegativeGammaCorrection = 0xE1,
        /// <summary>Binary gamma control 1 (E2h).<br />9.2.78</summary>
        BinaryGammaControl1 = 0xE2,
        /// <summary>Binary gamma control 2 (E3h).<br />9.2.79</summary>
        BinaryGammaControl2 = 0xE3,
		/// <summary>Display Output Ctrl Adjust (E8h).<br />9.3.22</summary>
		DisplayOutputCtrlAdjust = 0xE8,
        /// <summary>Adjust control 1 (F0h).<br />9.2.80</summary>
        AdjustControl1 = 0xF0,
        /// <summary>Adjust control 2 (F1h).<br />9.2.80</summary>
        AdjustControl2 = 0xF1,
        /// <summary>Adjust control 2A (F2h).<br />9.2.80</summary>
        AdjustControl2A = 0xF2,
        /// <summary>Adjust control 2B (F3h).<br />9.2.80</summary>
        AdjustControl2B = 0xF3,
        /// <summary>Adjust control 2C (F4h).<br />9.2.80</summary>
        AdjustControl2C = 0xF4,
        /// <summary>Adjust control 2D (F5h).<br />9.2.80</summary>
        AdjustControl2D = 0xF5,
		/// <summary>Interface control (F6h).<br />9.2.80</summary>
		InterfaceControl = 0xF6,
        /// <summary>Panel interface control (F7h).<br />9.2.81</summary>
        PanelInterfaceControl = 0xF7,
        /// <summary>Adjust control 3 (F8h).<br />9.2.82</summary>
        AdjustControl3 = 0xF8,
        /// <summary>Adjust control 4 (F9h).<br />9.2.83</summary>
        AdjustControl4 = 0xF9,
        /// <summary>Adjust control 5 (FCh).<br />9.2.84</summary>
        AdjustControl5 = 0xFC,
        /// <summary>Command set control (F0h).<br />9.3.23</summary>
        CommandSetControl = 0xF0,
        /// <summary>Command set control 2 (F1h).<br />9.3.23</summary>
        CommandSetControl2 = 0xF1,
        /// <summary>Command set control 3 (F2h).<br />9.3.23</summary>
        CommandSetControl3 = 0xF2,
        /// <summary>Command set control 4 (F3h).<br />9.3.23</summary>
        CommandSetControl4 = 0xF3,
        /// <summary>Command set control 5 (F4h).<br />9.3.23</summary>
        CommandSetControl5 = 0xF4,
        /// <summary>Command set control 6 (F5h).<br />9.3.23</summary>
        CommandSetControl6 = 0xF5,
        /// <summary>SPI read control (FBh).<br />9.3.24</summary>
        SpiReadControl = 0xFB,
        /// <summary>Reserved (FAh).</summary>
        Reserved_FA = 0xFA,
        /// <summary>Reserved (FDh).</summary>
        Reserved_FD = 0xFD,
        /// <summary>Reserved (FEh).</summary>
        Reserved_FE = 0xFE,
        /// <summary>Reserved (FFh).</summary>
        Reserved_FF = 0xFF,
    }
}