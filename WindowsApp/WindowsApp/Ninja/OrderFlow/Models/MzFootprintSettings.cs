namespace WindowsApp.Ninja.OrderFlow.Models;

public class MzFootprintSettings
{
    // ==========================================
    // 1. Left Footprint
    // ==========================================
    public string LeftFootprintStyle { get; set; } = "Bid";
    public string LeftClusterStyle { get; set; } = "None";
    public string LeftClusterScale { get; set; } = "Bar";
    public string LeftScaleSource { get; set; } = "Delta";
    public string LeftColorSource { get; set; } = "Delta";
    public string LeftGradientSource { get; set; } = "Delta";
    public string LeftClusterColor { get; set; } = "SteelBlue";
    public string LeftNegativeDeltaColor { get; set; } = "Red";
    public string LeftPositiveDeltaColor { get; set; } = "DarkGreen";
    public string LeftClusterBorder { get; set; } = "Solid, 1px";
    public string LeftColorMode { get; set; } = "Saturation";
    public bool LeftValuesShow { get; set; } = true;
    public string LeftValuesAlign { get; set; } = "Center";
    public double LeftValuesDivider { get; set; } = 1.0;
    public int LeftValuesDecimalPlaces { get; set; } = 1;
    public bool LeftValuesAbsDelta { get; set; } = true;
    public string LeftValuesColor { get; set; } = "GhostWhite";
    public string LeftValuesFont { get; set; } = "Arial, 18px";
    public bool LeftClusterZonesEnable { get; set; } = false;

    // ==========================================
    // 2. Right Footprint
    // ==========================================
    public string RightFootprintStyle { get; set; } = "Ask";
    public string RightClusterStyle { get; set; } = "None";
    public string RightClusterScale { get; set; } = "Bar";
    public string RightScaleSource { get; set; } = "Delta";
    public string RightColorSource { get; set; } = "Delta";
    public string RightGradientSource { get; set; } = "Delta";
    public string RightClusterColor { get; set; } = "SteelBlue";
    public string RightNegativeDeltaColor { get; set; } = "Red";
    public string RightPositiveDeltaColor { get; set; } = "DarkGreen";
    public string RightClusterBorder { get; set; } = "Solid, 1px";
    public string RightColorMode { get; set; } = "Saturation";
    public bool RightValuesShow { get; set; } = true;
    public string RightValuesAlign { get; set; } = "Center";
    public double RightValuesDivider { get; set; } = 1.0;
    public int RightValuesDecimalPlaces { get; set; } = 1;
    public bool RightValuesAbsDelta { get; set; } = true;
    public string RightValuesColor { get; set; } = "GhostWhite";
    public string RightValuesFont { get; set; } = "Arial, 18px";
    public bool RightClusterZonesEnable { get; set; } = false;

    // ==========================================
    // 3. Filters
    // ==========================================
    public int TicksPerLevel { get; set; } = 1;
    public double TradeMin { get; set; } = 0.0;
    public double TradeMax { get; set; } = -1.0;

    // ==========================================
    // 4. Presentation
    // ==========================================
    public double DisplayVolumeFilter { get; set; } = 1.0;
    public string BidColor { get; set; } = "Red";
    public string AskColor { get; set; } = "DarkGreen";
    public bool AutoScaleValues { get; set; } = true;
    public bool BarBorder { get; set; } = false;
    public string BarBorderStroke { get; set; } = "Solid, 1px";
    public bool BarMarker { get; set; } = false;
    public double BarSpacePx { get; set; } = 110.087914;
    public double BarWidthPx { get; set; } = 3.3026375;
    public double BarOuterMarginPx { get; set; } = 8.0;
    public bool ControlRightMargin { get; set; } = false;
    public double ChartRightMarginPx { get; set; } = 40.0;

    // ==========================================
    // 5. Bar Statistic
    // ==========================================
    public bool BarStatVolume { get; set; } = false;
    public bool BarStatDelta { get; set; } = true;
    public bool BarStatAbsDeltaAvg { get; set; } = false;
    public bool BarStatMinMaxDelta { get; set; } = false;
    public bool BarStatDeltaPercent { get; set; } = false;
    public bool BarStatCot { get; set; } = false;
    public bool BarStatRatioNumbers { get; set; } = false;
    public double BarStatValuesDivider { get; set; } = 1.0;
    public string BarStatNegativeDeltaColor { get; set; } = "Red";
    public string BarStatPositiveDeltaColor { get; set; } = "Green";
    public string BarStatFont { get; set; } = "Arial, 12px";

    // ==========================================
    // 6. Statistic Grid
    // ==========================================
    public bool GridShow { get; set; } = true;
    public bool GridShowLegend { get; set; } = true;
    public string GridLegendPosition { get; set; } = "Left";
    public bool GridTrades { get; set; } = false;
    public bool GridVolume { get; set; } = false;
    public bool GridBuyVolume { get; set; } = true;
    public bool GridSellVolume { get; set; } = true;
    public bool GridDelta { get; set; } = true;
    public bool GridDeltaPercent { get; set; } = false;
    public bool GridAbsDeltaAvg { get; set; } = false;
    public bool GridDeltaCumulative { get; set; } = true;
    public bool GridMinDelta { get; set; } = true;
    public bool GridMaxDelta { get; set; } = true;
    public bool GridDeltaChange { get; set; } = false;
    public bool GridDeltaRate { get; set; } = false;
    public bool GridCotHigh { get; set; } = false;
    public bool GridCotLow { get; set; } = false;
    public bool GridVolPerSec { get; set; } = false;
    public bool GridPredictedValues { get; set; } = false;
    public double GridValuesDivider { get; set; } = 1.0;
    public double GridCellHeightPx { get; set; } = 24.0;
    public string GridCellColorScale { get; set; } = "Chart";
    public bool GridCellBorder { get; set; } = true;
    public string GridCellBorderStroke { get; set; } = "Solid, 1px";
    public bool GridAutoScaleValues { get; set; } = true;
    public bool GridAutoScaleBars { get; set; } = true;
    public string GridFont { get; set; } = "Arial, 20px";
    public string GridAlign { get; set; } = "Right";
    public string GridValuesColor { get; set; } = "LightGray";

    // ==========================================
    // 7. Imbalance
    // ==========================================
    public bool ImbalanceShow { get; set; } = true;
    public bool ImbalanceOnly { get; set; } = false;
    public double ImbalancePercent { get; set; } = 400.0;
    public double ImbalanceFilter { get; set; } = 10.0;
    public string ImbalanceMarkerVisibility { get; set; } = "NoValues";
    public string ImbalanceMarkerType { get; set; } = "Dot";
    public string ImbalanceMarkerPosition { get; set; } = "Outer";
    public string ImbalanceSellZoneColor { get; set; } = "DeepPink";
    public string ImbalanceBuyZoneColor { get; set; } = "Lime";
    public string ImbalanceFont { get; set; } = "Arial, 13px";
    public bool ImbalanceHighlightValues { get; set; } = true;
    public bool ImbalanceSrZonesEnable { get; set; } = true;
    public int ImbalanceSrConsecutiveLevels { get; set; } = 2;
    public double ImbalanceSrVolumeFilter { get; set; } = 1.0;
    public string ImbalanceSrEndedBy { get; set; } = "ByBarHighLow";
    public double ImbalanceSrApproaching { get; set; } = 0.0;
    public bool ImbalanceSrBreakOnSession { get; set; } = true;
    public double ImbalanceSrOpacityPercent { get; set; } = 25.0;
    public bool ImbalanceSrAlert { get; set; } = false;
    public string ImbalanceSupportZoneSound { get; set; } = "imbalance_support_zone.wav";
    public string ImbalanceResistanceZoneSound { get; set; } = "imbalance_resistance_zone.wav";

    // ==========================================
    // 8. Absorption
    // ==========================================
    public bool AbsorptionShow { get; set; } = false;
    public bool AbsorptionOnly { get; set; } = false;
    public double AbsorptionPercent { get; set; } = 70.0;
    public int AbsorptionDepth { get; set; } = 1;
    public double AbsorptionFilter { get; set; } = 10.0;
    public string AbsorptionSellSupportZoneColor { get; set; } = "Red";
    public string AbsorptionBuyResistanceZoneColor { get; set; } = "Lime";
    public bool AbsorptionSrZonesEnable { get; set; } = false;
    public double AbsorptionSrZonesApproaching { get; set; } = 0.0;

    // ==========================================
    // 9. Unfinished Auction
    // ==========================================
    public bool UnfinishedAuctionShow { get; set; } = false;
    public string UnfinishedAuctionColor { get; set; } = "Indigo";
    public double UnfinishedAuctionOpacityPercent { get; set; } = 30.0;
    public string UnfinishedAuctionBorder { get; set; } = "Solid, 1px";

    // ==========================================
    // 10. Bar Volume Profile
    // ==========================================
    public bool BarPocsShow { get; set; } = true;
    public int BarPocsCount { get; set; } = 1;
    public string PrimaryPocBorderColor { get; set; } = "Yellow";
    public string OtherPocsBorderColor { get; set; } = "Orange";
    public bool BarVaShow { get; set; } = true;
    public double BarVaPercent { get; set; } = 70.0;
    public string BarVaColor { get; set; } = "Aquamarine";
    public double BarVaOpacityPercent { get; set; } = 20.0;

    // ==========================================
    // 11. Volume Profile Levels
    // ==========================================
    public string VolumeProfileMode { get; set; } = "Daily";
    public bool VolumeProfilePocEnable { get; set; } = false;
    public bool VolumeProfilePocDeveloping { get; set; } = true;
    public string VolumeProfilePocLineColor { get; set; } = "Orange";
    public bool VolumeProfileVaEnable { get; set; } = true;
    public bool VolumeProfileVaDeveloping { get; set; } = true;
    public double VolumeProfileVaPercent { get; set; } = 68.0;
    public string VolumeProfileVaLineColor { get; set; } = "SteelBlue";
    public bool ShowSessionProfile { get; set; } = true;

    // ==========================================
    // 12. Levels (Support / Resistance Levels)
    // ==========================================
    public bool LevelsEnable { get; set; } = false;
    public string SupportLevelColor { get; set; } = "DarkSeaGreen";
    public string ResistanceLevelColor { get; set; } = "Brown";
    public string LevelValuePosition { get; set; } = "Right";
    public string LevelValueColor { get; set; } = "LightGray";
    public string LevelValueFont { get; set; } = "Arial, 10px";
    public bool LevelAlert { get; set; } = true;
    public string LevelAlertOn { get; set; } = "LevelCross";
    public bool LevelRearm { get; set; } = true;
    public int LevelRearmIntervalSec { get; set; } = 5;
    public string LevelSound { get; set; } = "crossed.wav";
    public bool LevelDragWithMouse { get; set; } = true;

    // ==========================================
    // 13. Orderflow & Overlays
    // ==========================================
    public string OrderflowCalculationMode { get; set; } = "UpDownTick";
    public string OrderflowSpreadTrades { get; set; } = "Split";
    public bool OrderflowReconstructTape { get; set; } = false;
    public string FootprintMode { get; set; } = "Bid x Ask";
    public bool ShowCandleLine { get; set; } = true;
    public bool ShowVwap { get; set; } = true;
    public bool ShowCvd { get; set; } = true;

    // ==========================================
    // 14. General
    // ==========================================
    public bool GeneralShowPriceLine { get; set; } = false;
    public string GeneralPriceLine { get; set; } = "Dot, 1px";
    public double GeneralNonCryptoDivider { get; set; } = 1.0;
    public bool GeneralCryptoForce { get; set; } = false;
    public int GeneralCryptoDecimalPlaces { get; set; } = 2;
    public bool GeneralWorkingTime { get; set; } = false;
    public bool GeneralSmoothRender { get; set; } = false;
    public bool GeneralHint { get; set; } = true;
    public bool GeneralWarning { get; set; } = true;
    public bool GeneralVersion { get; set; } = true;
    public bool GeneralLog { get; set; } = true;

    // ==========================================
    // 15. Licensing
    // ==========================================
    public string HardwareId { get; set; } = "AK10X-NT8-8742-99B1";

    // ==========================================
    // 16. Data Series & Visual
    // ==========================================
    public string DataSeriesInput { get; set; } = "TATASTEEL_I (5 Minute)";
    public bool VisualAutoScale { get; set; } = true;
    public int VisualDisplacement { get; set; } = 0;
    public bool VisualDisplayInDataBox { get; set; } = true;
    public string VisualPanel { get; set; } = "Same as input series";
    public bool VisualPriceMarkers { get; set; } = true;
    public string VisualScaleJustification { get; set; } = "Right";
    public bool VisualVisible { get; set; } = true;

    // ==========================================
    // 17. Setup
    // ==========================================
    public string SetupCalculate { get; set; } = "On each tick";
    public string SetupLabel { get; set; } = "MZpack.NT8.mzFootprint 3.14.21";
    public int SetupMaxBarsLookBack { get; set; } = 256;

    // Convenient Aliases
    public double AbsorptionSrApproaching { get => AbsorptionSrZonesApproaching; set => AbsorptionSrZonesApproaching = value; }
    public double UnfinishedAuctionOpacity { get => UnfinishedAuctionOpacityPercent; set => UnfinishedAuctionOpacityPercent = value; }
    public double BarVaOpacity { get => BarVaOpacityPercent; set => BarVaOpacityPercent = value; }
    public string VpMode { get => VolumeProfileMode; set => VolumeProfileMode = value; }
    public bool VpPocEnable { get => VolumeProfilePocEnable; set => VolumeProfilePocEnable = value; }
    public bool VpPocDeveloping { get => VolumeProfilePocDeveloping; set => VolumeProfilePocDeveloping = value; }
    public bool VpVaEnable { get => VolumeProfileVaEnable; set => VolumeProfileVaEnable = value; }
    public bool VpVaDeveloping { get => VolumeProfileVaDeveloping; set => VolumeProfileVaDeveloping = value; }
    public double VpVaPercent { get => VolumeProfileVaPercent; set => VolumeProfileVaPercent = value; }
    public string LevelsValuePosition { get => LevelValuePosition; set => LevelValuePosition = value; }
    public string LevelsValueColor { get => LevelValueColor; set => LevelValueColor = value; }
    public bool LevelsAlert { get => LevelAlert; set => LevelAlert = value; }
    public string LevelsAlertOn { get => LevelAlertOn; set => LevelAlertOn = value; }
    public bool LevelsRearm { get => LevelRearm; set => LevelRearm = value; }
    public double LevelsRearmInterval { get => LevelRearmIntervalSec; set => LevelRearmIntervalSec = (int)value; }
    public string LevelsSound { get => LevelSound; set => LevelSound = value; }
    public string LevelsAddRemoveKey { get; set; } = "LeftShift";
    public string LevelsModifyKey { get; set; } = "LeftAlt";
    public bool LevelsDragWithMouse { get => LevelDragWithMouse; set => LevelDragWithMouse = value; }
    public string CalculationMode { get => OrderflowCalculationMode; set => OrderflowCalculationMode = value; }
    public string SpreadTrades { get => OrderflowSpreadTrades; set => OrderflowSpreadTrades = value; }
    public bool ReconstructTape { get => OrderflowReconstructTape; set => OrderflowReconstructTape = value; }
    public int GeneralCryptoDecimals { get => GeneralCryptoDecimalPlaces; set => GeneralCryptoDecimalPlaces = value; }

    public MzFootprintSettings Clone()
    {
        return (MzFootprintSettings)MemberwiseClone();
    }
}
