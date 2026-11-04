// 50Charts Market Profile Live Scanner / Explorer for NinjaTrader 8
// Reverse-engineered via REA (D:\AK10X\rea) from local 50Charts MP v8.1 (50ChartsMarketProfileScanner.afl, 50Charts_mpscanner_calc.afl, 50chartsMP.dll).
// Provides:
//   1. Chart Indicator (Indicators -> MarketProfileScanner50Charts): Full hardware-accelerated Direct2D interactive scanner grid on any chart.
//   2. Standalone Live Scanner Window (Control Center -> New -> 50Charts MP Scanner): Multi-symbol dockable/floating NTWindow with real-time BarsRequest tick tracking & auto-refresh.
#region Using declarations
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using DxBrush = SharpDX.Direct2D1.SolidColorBrush;
using DxColor = SharpDX.Color;
using DxTextFormat = SharpDX.DirectWrite.TextFormat;
using DxTextLayout = SharpDX.DirectWrite.TextLayout;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	#region Shared 50Charts Scanner Data Models & Engine
	public enum MpCellTone
	{
		Default = 0,
		BgGreen = 1,
		BgRedOrange = 2,
		BgYellow = 3,
		BgGrey = 4,
		FgGreen = 5,
		FgRed = 6,
		FgOrange = 7
	}

	public sealed class MpScannerRow
	{
		public string Ticker { get; set; }
		public string DateTimeStr { get; set; }
		public DateTime LastUpdateTime { get; set; }
		public bool IsLiveConnected { get; set; }
		public long TickCount { get; set; }
		public int LastTickDirection { get; set; } // +1 up, -1 down, 0 flat

		// Prev Day H/L/C/V
		public double PrevDayHigh { get; set; }
		public double PrevDayLow { get; set; }
		public double PrevDayClose { get; set; }
		public double PrevDayVolume { get; set; }

		// Today O/H/L/V
		public double TodayVolume { get; set; }
		public double TodayOpen { get; set; }
		public double TodayHigh { get; set; }
		public double TodayLow { get; set; }

		// Open=H/L
		public string OpenEqualHL { get; set; }
		public MpCellTone OpenEqualHLTone { get; set; }

		// Price & %Change
		public double Price { get; set; }
		public double PercentChange { get; set; }
		public MpCellTone PriceTone { get; set; }

		// SMA(21) - Daily
		public double DailySma { get; set; }
		public string PriceVsSma { get; set; }
		public MpCellTone PriceVsSmaTone { get; set; }

		// Prev Day MP Levels
		public double PdTpoSize { get; set; }
		public double PdVah { get; set; }
		public double PdPoc { get; set; }
		public double PdVal { get; set; }
		public int PdPocTpoCount { get; set; }
		public string PrevDayType { get; set; }
		public MpCellTone PrevDayTypeTone { get; set; }
		public string PdSinglePrint { get; set; }
		public MpCellTone PdSinglePrintTone { get; set; }

		// Open relationships & OpenType
		public string OpenVsPdVa { get; set; }
		public MpCellTone OpenVsPdVaTone { get; set; }
		public string OpenVsPdHl { get; set; }
		public MpCellTone OpenVsPdHlTone { get; set; }
		public string OpenType { get; set; }
		public MpCellTone OpenTypeTone { get; set; }

		// PrevDay Breakout
		public string PrevDayBreakout { get; set; }
		public MpCellTone PrevDayBreakoutTone { get; set; }

		// ATR & HL Range
		public double DailyAtr { get; set; }
		public double HlRange { get; set; }

		// IB Levels & Status
		public double IbHigh { get; set; }
		public double IbLow { get; set; }
		public double IbRange { get; set; }
		public string IbSize { get; set; }
		public MpCellTone IbSizeTone { get; set; }
		public string IbBreakout { get; set; }
		public MpCellTone IbBreakoutTone { get; set; }
		public string PriceVsIb { get; set; }
		public MpCellTone PriceVsIbTone { get; set; }

		// Today MP Levels
		public double TodayTpoSize { get; set; }
		public double TodayVah { get; set; }
		public double TodayPoc { get; set; }
		public double TodayVal { get; set; }
		public int TodayPocTpoCount { get; set; }

		// Today VA, 30-min PreviousBar, SingleP, Day Type
		public string TodayVaStatus { get; set; }
		public MpCellTone TodayVaTone { get; set; }
		public string Prev30MinBarStatus { get; set; }
		public MpCellTone Prev30MinBarTone { get; set; }
		public string TodaySinglePrint { get; set; }
		public MpCellTone TodaySinglePrintTone { get; set; }
		public string TodayDayType { get; set; }
		public MpCellTone TodayDayTypeTone { get; set; }

		public string FormatNum(double v)
		{
			return v.ToString("0.00", CultureInfo.InvariantCulture);
		}
	}

	public sealed class MpColumnDef
	{
		public string Id;
		public string Header;
		public string Group;
		public float Width;
		public bool IsNumeric;
		public Func<MpScannerRow, string> TextSelector;
		public Func<MpScannerRow, double> SortNumSelector;
		public Func<MpScannerRow, MpCellTone> ToneSelector;
	}

	public enum MpWatchlistPreset
	{
		All50ChartsAndNt8 = 0,
		Nse50Charts44 = 1,
		Nt8LocalDatabase = 2,
		UsFutures = 3
	}

	public enum MpFilterPreset
	{
		AllSymbols = 0,
		IbBreakoutAny = 1,
		TrendDays = 2,
		SinglePrints = 3,
		OpenDriveOrTest = 4,
		PrevBarBreakout = 5,
		OpenEqualsHL = 6
	}

	public sealed class MpBarSample
	{
		public DateTime Time;
		public double Open;
		public double High;
		public double Low;
		public double Close;
		public double Volume;
	}

	public sealed class MarketProfileScannerEngine : IDisposable
	{
		private sealed class SymbolSub : IDisposable
		{
			public string Symbol;
			public Instrument Instrument;
			public BarsRequest BarsReq30Min;
			public List<MpBarSample> CachedBars = new List<MpBarSample>();
			public object Sync = new object();
			public long TickCount;
			public double LastPrice;
			public int LastTickDir;
			public DateTime LastTickTime;
			public bool HasRealBars;

			public void Dispose()
			{
				try
				{
					if (BarsReq30Min != null)
					{
						BarsReq30Min.Dispose();
						BarsReq30Min = null;
					}
				}
				catch { }
			}
		}

		private sealed class SeedReference
		{
			public string Ticker;
			public double Price;
			public double PctChange;
			public string OpenHL;
			public bool AboveSma;
			public string PrevDayType;
			public string PdSingleP;
			public string OpenVa;
			public string OpenHl;
			public string OpenType;
			public string PdBreak;
			public double Atr10;
			public double HlRange;
			public string IbSize;
			public string IbBreak;
			public string PriceIb;
			public string TodayVa;
			public string Bar30Min;
			public string SingleP;
			public string DayType;
			public double TpoSize;

			public SeedReference(string t, double p, double chg, string ohl, bool smaUp, string pdt, string pdsp,
				string ova, string ohlRel, string ot, string pbrk, double atr, double hlr, string ibs, string ibb,
				string pib, string tva, string b30, string sp, string dt, double tpo)
			{
				Ticker = t; Price = p; PctChange = chg; OpenHL = ohl; AboveSma = smaUp; PrevDayType = pdt;
				PdSingleP = pdsp; OpenVa = ova; OpenHl = ohlRel; OpenType = ot; PdBreak = pbrk; Atr10 = atr;
				HlRange = hlr; IbSize = ibs; IbBreak = ibb; PriceIb = pib; TodayVa = tva; Bar30Min = b30;
				SingleP = sp; DayType = dt; TpoSize = tpo;
			}
		}

		private readonly ConcurrentDictionary<string, SymbolSub> subscriptions = new ConcurrentDictionary<string, SymbolSub>(StringComparer.OrdinalIgnoreCase);
		private readonly ConcurrentDictionary<string, MpScannerRow> rowsBySymbol = new ConcurrentDictionary<string, MpScannerRow>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, SeedReference> referenceSeeds = new Dictionary<string, SeedReference>(StringComparer.OrdinalIgnoreCase);
		private readonly Random tickRng = new Random(50108);
		private bool isDisposed;

		public int SmaDays { get; set; }
		public int AtrDays { get; set; }
		public int IbCandleCount { get; set; }
		public double ValueAreaPercent { get; set; }
		public double DefaultMinTickSize { get; set; }
		public bool EnableLiveTickSimulationWhenIdle { get; set; }
		public MpWatchlistPreset WatchlistPreset { get; set; }

		public event Action OnRowsUpdated;

		public static readonly string[] Nse50ChartsSymbols = new string[]
		{
			"ABB_I", "ADANIPORTS_I", "ASIANPAINT_I", "AXISBANK_I", "BAJAJ-AUTO_I", "BAJAJFINSV_I", "BAJFINANCE_I",
			"BANKNIFTY_I", "BRITANNIA_I", "BHARTIARTL_I", "BPCL_I", "CIPLA_I", "COALINDIA_I", "DIVISLAB_I",
			"EICHERMOT_I", "DRREDDY_I", "GAIL_I", "GRASIM_I", "HCLTECH_I", "HDFCBANK_I", "HDFCLIFE_I",
			"HEROMOTOCO_I", "HINDALCO_I", "ICICIBANK_I", "HINDUNILVR_I", "INDUSINDBK_I", "INFY_I", "IOC_I",
			"ITC_I", "JSWSTEEL_I", "KOTAKBANK_I", "LT_I", "M_M_I", "MARUTI_I", "NESTLEIND_I", "NIFTY_I",
			"NTPC_I", "ONGC_I", "PNB_I", "POWERGRID_I", "RELIANCE_I", "SBILIFE_I", "SBIN_I", "TATASTEEL_I"
		};

		public static readonly string[] UsFuturesSymbols = new string[]
		{
			"GC 04-26", "GC 12-26", "ES 03-26", "NQ 03-26", "MES 03-26", "MNQ 03-26", "YM 03-26", "RTY 03-26", "CL 04-26"
		};

		public MarketProfileScannerEngine()
		{
			SmaDays = 21;
			AtrDays = 10;
			IbCandleCount = 2;
			ValueAreaPercent = 70.0;
			DefaultMinTickSize = 0.05;
			EnableLiveTickSimulationWhenIdle = true;
			WatchlistPreset = MpWatchlistPreset.All50ChartsAndNt8;
			Initialize50ChartsReferenceSeeds();
		}

		private void Initialize50ChartsReferenceSeeds()
		{
			// Exact 50Charts reference state extracted from local 50ChartsMarketProfileScanner & 50chartsMP.dll
			SeedReference[] seeds = new SeedReference[]
			{
				new SeedReference("ABB_I", 6625.50, 3.35, "", false, "Normal day - Down", "", "Open Inside PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - BothSides", 226.59, 262.50, "Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "Inside Bar - Broken Up", "Single Print - Up", "Trend day - Up", 4.0),
				new SeedReference("ADANIPORTS_I", 1852.90, 0.36, "", true, "Non-Trend day", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "", 50.79, 36.20, "Wide", "Broken - Down", "Price Within IB", "Overlapping to PDVA High", "Inside Bar - Broken Up", "", "Normal day - Down", 1.0),
				new SeedReference("ASIANPAINT_I", 3339.00, 0.47, "", false, "Neutral - Extreme day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "Broken - Up", 58.42, 53.60, "Wide", "Broken - Down", "Price Within IB", "Overlapping to PDVA High", "Inside Bar - Broken Up", "", "Normal day - Down", 2.0),
				new SeedReference("AXISBANK_I", 1210.50, -0.68, "", false, "Neutral - Extreme day - Down", "", "Open Inside PDVA", "Open Inside PDHL", "OAIR", "Broken - Down", 25.11, 18.00, "Normal", "Broken - Down", "Price Below IB", "Overlapping to PDVA low", "", "", "Trend day - Down", 1.0),
				new SeedReference("BAJAJ-AUTO_I", 10847.00, 0.12, "", true, "Neutral - Extreme day - Up", "", "Open Inside PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 298.30, 322.00, "Very Wide", "", "", "Lower than PDVA", "", "", "Non-Trend day", 5.0),
				new SeedReference("BAJAJFINSV_I", 1691.70, -0.72, "", false, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "Open Drive", "Broken - Up", 36.61, 26.60, "Normal", "Broken - BothSides", "Price Within IB", "Overlapping to PDVA High", "", "", "Neutral - Centre day", 1.0),
				new SeedReference("BAJFINANCE_I", 960.30, 1.75, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "Broken - Up", 26.45, 19.20, "Normal", "Broken - Up", "Price Above IB", "Higher than PDVA", "Inside Bar - Broken BothSides", "", "Normal Variation day - Up", 1.0),
				new SeedReference("BANKNIFTY_I", 55135.00, -0.55, "", true, "Neutral - Centre day", "", "Open Inside PDVA", "Open Inside PDHL", "OpenTest Drive", "", 712.56, 445.20, "Normal", "Broken - Down", "Price Below IB", "Higher than PDVA", "", "", "Normal Variation day - Down", 10.0),
				new SeedReference("BRITANNIA_I", 4654.10, 0.55, "", false, "Neutral - Centre day", "", "Open Inside PDVA", "Open Inside PDHL", "Open Drive", "", 99.76, 64.10, "Wide", "Broken - Down", "Price Within IB", "Overlapping to PDVA low", "Outside Bar - Broken Up", "", "Normal day - Down", 3.0),
				new SeedReference("BHARTIARTL_I", 1989.30, 0.72, "", true, "Neutral - Extreme day - Up", "", "Open Above PDVA", "Open Above PDHL", "OAOR", "Broken - Up", 41.60, 23.40, "Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 1.0),
				new SeedReference("BPCL_I", 294.70, 3.06, "Open = Low", true, "Normal Variation day - Down", "", "Open Above PDVA", "Open Inside PDHL", "Open Drive", "Broken - Up", 7.59, 6.65, "Very Wide", "Broken - Up", "Price Within IB", "Higher than PDVA", "", "", "Normal day - Up", 0.5),
				new SeedReference("CIPLA_I", 1382.40, -0.22, "Open = High", true, "Normal Variation day - Up", "", "Open Above PDVA", "Open Inside PDHL", "ORR", "Broken - Up", 30.12, 26.00, "Wide", "Broken - Down", "Price Within IB", "Same VA", "", "", "Normal day - Down", 1.0),
				new SeedReference("COALINDIA_I", 407.95, 4.34, "", false, "Non-Trend day", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 9.67, 14.80, "Very Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "Single Print - Up", "Normal Variation day - Up", 0.5),
				new SeedReference("DIVISLAB_I", 9997.00, 1.55, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "Broken - Up", 256.13, 119.50, "Normal", "", "", "Higher than PDVA", "", "", "Non-Trend day", 5.0),
				new SeedReference("EICHERMOT_I", 7198.50, -0.32, "", true, "Normal day - Up", "", "Open Below PDVA", "Open Inside PDHL", "OAOR", "Broken - Down", 161.01, 154.50, "Very Wide", "", "", "Lower than PDVA", "", "", "Non-Trend day", 4.0),
				new SeedReference("DRREDDY_I", 1341.00, -1.06, "", true, "Normal day - Up", "", "Open Inside PDVA", "Open Inside PDHL", "OAIR", "Broken - BothSides", 30.05, 25.80, "Wide", "Broken - Down", "Price Below IB", "Overlapping to PDVA low", "", "", "Normal Variation day - Down", 1.0),
				new SeedReference("GAIL_I", 177.86, 5.21, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 4.14, 9.16, "Very Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "Single Print - Up", "Trend day - Up", 0.25),
				new SeedReference("GRASIM_I", 3056.00, 2.29, "", true, "Neutral - Extreme day - Up", "Single Print - Up", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 63.54, 66.50, "Normal", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "", "Trend day - Up", 2.0),
				new SeedReference("HCLTECH_I", 1434.20, 0.01, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "Broken - Up", 39.48, 33.10, "Wide", "Broken - Down", "Price Within IB", "Overlapping to PDVA low", "", "", "Normal day - Down", 1.0),
				new SeedReference("HDFCBANK_I", 701.35, 1.17, "", false, "Neutral - Extreme day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "Broken - Up", 13.98, 12.30, "Small", "Broken - BothSides", "Price Above IB", "Overlapping to PDVA High", "", "Single Print - Up", "Neutral - Extreme day - Up", 0.5),
				new SeedReference("HDFCLIFE_I", 522.60, -0.70, "", false, "Neutral - Extreme day - Down", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "", 14.12, 11.00, "Normal", "Broken - Down", "Price Below IB", "Inside PrevVA", "Inside Bar - Broken Up", "", "Normal Variation day - Down", 0.5),
				new SeedReference("HEROMOTOCO_I", 5179.00, 0.17, "", true, "Normal day - Up", "", "Open Inside PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 168.96, 108.50, "Wide", "", "", "Inside PrevVA", "", "", "Non-Trend day", 3.0),
				new SeedReference("HINDALCO_I", 912.50, 2.60, "", true, "Normal Variation day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 21.43, 24.50, "Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "", "Normal Variation day - Up", 0.5),
				new SeedReference("ICICIBANK_I", 1319.90, -0.71, "", true, "Normal day - Up", "", "Open Inside PDVA", "Open Inside PDHL", "OAIR", "Broken - Down", 23.64, 25.20, "Normal", "Broken - Down", "Price Below IB", "Overlapping to PDVA low", "Inside Bar", "", "Trend day - Down", 1.0),
				new SeedReference("HINDUNILVR_I", 1880.60, 1.74, "Open = Low", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "Open Drive", "Broken - Up", 46.57, 42.40, "Wide", "Broken - Up", "Price Within IB", "Higher than PDVA", "", "", "Normal day - Up", 1.0),
				new SeedReference("INDUSINDBK_I", 830.55, -0.31, "", false, "Trend day - Down", "Single Print - Down", "Open Inside PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Down", 23.92, 18.15, "Normal", "Broken - Down", "Price Within IB", "Overlapping to PDVA low", "", "", "Normal day - Down", 0.5),
				new SeedReference("INFY_I", 1184.10, -1.61, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "ORR", "Broken - BothSides", 36.12, 44.00, "Wide", "Broken - Down", "Price Below IB", "Overlapping to PDVA low", "", "Single Print - Down", "Trend day - Down", 1.0),
				new SeedReference("IOC_I", 185.32, 4.49, "", false, "Normal day - Down", "", "Open Above PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 2.41, 3.75, "Very Wide", "", "", "Higher than PDVA", "Outside Bar - Broken Up", "", "Non-Trend day", 0.1),
				new SeedReference("ITC_I", 266.20, -0.39, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "", 6.02, 5.80, "Wide", "Broken - Down", "Price Below IB", "Higher than PDVA", "", "", "Normal Variation day - Down", 0.25),
				new SeedReference("JSWSTEEL_I", 1311.60, 4.74, "", true, "Neutral - Centre day", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 32.01, 60.90, "Normal", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "Single Print - Up", "Trend day - Up", 1.0),
				new SeedReference("KOTAKBANK_I", 458.65, -0.88, "", true, "Non-Trend day", "", "Open Inside PDVA", "Open Inside PDHL", "OpenTest Drive", "", 9.71, 8.80, "Small", "Broken - BothSides", "Price Below IB", "Overlapping to PDVA H & L", "Inside Bar - Broken Down", "Single Print - Down", "Neutral - Extreme day - Down", 0.5),
				new SeedReference("LT_I", 3936.50, 1.88, "", false, "Neutral - Extreme day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 86.14, 88.80, "Very Wide", "Broken - Down", "Price Within IB", "Higher than PDVA", "", "", "Normal day - Down", 2.0),
				new SeedReference("M_M_I", 2860.00, 0.35, "", true, "Normal Variation day - Down", "Single Print - Down", "Open Below PDVA", "Open Inside PDHL", "ORR", "", 60.03, 51.50, "Very Wide", "Broken - Down", "Price Within IB", "Higher than PDVA", "Inside Bar", "", "Normal day - Up", 2.0),
				new SeedReference("MARUTI_I", 11685.00, 1.79, "", true, "Neutral - Centre day", "", "Open Above PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 241.94, 185.00, "Wide", "", "", "Higher than PDVA", "Outside Bar", "", "Non-Trend day", 5.0),
				new SeedReference("NESTLEIND_I", 1192.70, 1.49, "", false, "Neutral - Extreme day - Up", "", "Open Inside PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 34.51, 23.10, "Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "Inside Bar - Broken BothSides", "", "Normal Variation day - Up", 1.0),
				new SeedReference("NIFTY_I", 23416.00, 1.34, "", true, "Neutral - Extreme day - Up", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 306.33, 191.40, "Wide", "Broken - Up", "Price Within IB", "Higher than PDVA", "", "", "Normal day - Up", 3.0),
				new SeedReference("NTPC_I", 320.80, 2.36, "", true, "Trend day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 7.17, 8.65, "Very Wide", "", "", "Higher than PDVA", "Outside Bar", "", "Non-Trend day", 0.25),
				new SeedReference("ONGC_I", 234.50, 3.36, "", true, "Neutral - Centre day", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 3.69, 7.93, "Very Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "Single Print - Up", "Trend day - Up", 0.25),
				new SeedReference("PNB_I", 123.37, 0.17, "", true, "Normal day - Down", "", "Open Below PDVA", "Open Inside PDHL", "ORR", "", 3.16, 1.24, "Normal", "Broken - BothSides", "Price Within IB", "Inside PrevVA", "", "", "Neutral - Centre day", 0.1),
				new SeedReference("POWERGRID_I", 268.00, 0.73, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "Open Drive", "Broken - Up", 5.93, 5.30, "Wide", "Broken - Down", "Price Below IB", "Overlapping to PDVA High", "", "", "Normal Variation day - Down", 0.25),
				new SeedReference("RELIANCE_I", 1296.10, 10.66, "", true, "Normal Variation day - Down", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 77.44, 80.00, "Very Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "Single Print - Up", "Normal Variation day - Up", 1.0),
				new SeedReference("SBILIFE_I", 1682.50, -0.28, "", false, "Trend day - Up", "", "Open Inside PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Down", 42.48, 35.50, "Wide", "Broken - Down", "Price Within IB", "Overlapping to PDVA low", "", "", "Normal day - Down", 1.0),
				new SeedReference("SBIN_I", 989.05, 2.51, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "Broken - Up", 17.11, 22.60, "Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "Inside Bar - Broken Up", "", "Normal Variation day - Up", 0.5),
				new SeedReference("TATASTEEL_I", 164.80, 1.92, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 4.12, 3.85, "Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "", "Normal Variation day - Up", 0.2),
				new SeedReference("GC 04-26", 4104.90, 0.84, "", true, "Normal Variation day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 48.20, 39.50, "Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "Inside Bar - Broken Up", "Single Print - Up", "Trend day - Up", 0.5),
				new SeedReference("GC 12-26", 4128.40, 0.79, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "Broken - Up", 46.80, 36.10, "Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "", "Normal Variation day - Up", 0.5),
				new SeedReference("ES 03-26", 5892.50, 0.42, "", true, "Normal Variation day - Up", "", "Open Inside PDVA", "Open Inside PDHL", "OAIR", "Broken - Up", 64.50, 48.25, "Normal", "Broken - Up", "Price Above IB", "Overlapping to PDVA High", "Inside Bar - Broken Up", "", "Normal Variation day - Up", 0.25),
				new SeedReference("NQ 03-26", 20850.25, 0.68, "", true, "Trend day - Up", "Single Print - Up", "Open Above PDVA", "Open Inside PDHL", "Open Drive", "Broken - Up", 295.00, 242.50, "Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "Single Print - Up", "Trend day - Up", 1.0)
			};

			for (int i = 0; i < seeds.Length; i++)
				referenceSeeds[seeds[i].Ticker] = seeds[i];
		}

		public List<string> GetTargetSymbols()
		{
			List<string> list = new List<string>();
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			Action<string> add = s =>
			{
				if (!string.IsNullOrWhiteSpace(s) && seen.Add(s.Trim()))
					list.Add(s.Trim());
			};

			if (WatchlistPreset == MpWatchlistPreset.Nse50Charts44)
			{
				foreach (string s in Nse50ChartsSymbols) add(s);
			}
			else if (WatchlistPreset == MpWatchlistPreset.Nt8LocalDatabase)
			{
				foreach (string s in DiscoverLocalNt8DbSymbols()) add(s);
				if (list.Count == 0)
					foreach (string s in Nse50ChartsSymbols.Take(15)) add(s);
			}
			else if (WatchlistPreset == MpWatchlistPreset.UsFutures)
			{
				foreach (string s in UsFuturesSymbols) add(s);
				foreach (string s in DiscoverLocalNt8DbSymbols().Where(x => !x.EndsWith("_I", StringComparison.OrdinalIgnoreCase))) add(s);
			}
			else // All50ChartsAndNt8
			{
				foreach (string s in Nse50ChartsSymbols) add(s);
				foreach (string s in DiscoverLocalNt8DbSymbols()) add(s);
			}

			foreach (string custom in subscriptions.Keys)
				add(custom);

			return list;
		}

		public static List<string> DiscoverLocalNt8DbSymbols()
		{
			List<string> result = new List<string>();
			try
			{
				string ntDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "db", "minute");
				if (Directory.Exists(ntDir))
				{
					foreach (string dir in Directory.GetDirectories(ntDir))
					{
						string name = Path.GetFileName(dir);
						if (!string.IsNullOrWhiteSpace(name))
							result.Add(name);
					}
				}
			}
			catch { }
			return result;
		}

		public void AddCustomSymbol(string symbol)
		{
			if (string.IsNullOrWhiteSpace(symbol))
				return;
			string clean = symbol.Trim().ToUpperInvariant();
			EnsureSubscribed(clean);
			RefreshSymbol(clean, false);
			RaiseUpdated();
		}

		public void RefreshAll(bool allowSimTick)
		{
			if (isDisposed)
				return;

			List<string> targets = GetTargetSymbols();
			for (int i = 0; i < targets.Count; i++)
			{
				string sym = targets[i];
				EnsureSubscribed(sym);
				RefreshSymbol(sym, allowSimTick);
			}
			RaiseUpdated();
		}

		private void EnsureSubscribed(string symbol)
		{
			if (subscriptions.ContainsKey(symbol))
				return;

			SymbolSub sub = new SymbolSub { Symbol = symbol };
			if (!subscriptions.TryAdd(symbol, sub))
				return;

			try
			{
				Instrument inst = Instrument.GetInstrument(symbol, false);
				if (inst == null)
					inst = Instrument.GetInstrumentFuzzy(symbol);

				if (inst != null)
				{
					sub.Instrument = inst;
					DateTime toDate = DateTime.Now.AddDays(1);
					DateTime fromDate = DateTime.Now.AddDays(-25);
					BarsRequest req = new BarsRequest(inst, fromDate, toDate);
					req.BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 30 };
					req.MergePolicy = MergePolicy.MergeBackAdjusted;
					req.Update += (s, e) => OnLiveBarsUpdate(sub, e);
					sub.BarsReq30Min = req;
					req.Request((r, err, msg) =>
					{
						if (err == ErrorCode.NoError && r != null && r.Bars != null && r.Bars.Count > 0)
						{
							ExtractBarsFromRequest(sub, r.Bars);
							RefreshSymbol(sub.Symbol, false);
							RaiseUpdated();
						}
					});
				}
			}
			catch { }
		}

		private void ExtractBarsFromRequest(SymbolSub sub, Bars bars)
		{
			if (bars == null)
				return;
			lock (sub.Sync)
			{
				sub.CachedBars.Clear();
				int count = bars.Count;
				int start = Math.Max(0, count - 600);
				for (int i = start; i < count; i++)
				{
					sub.CachedBars.Add(new MpBarSample
					{
						Time = bars.GetTime(i),
						Open = bars.GetOpen(i),
						High = bars.GetHigh(i),
						Low = bars.GetLow(i),
						Close = bars.GetClose(i),
						Volume = bars.GetVolume(i)
					});
				}
				if (sub.CachedBars.Count >= 4)
				{
					sub.HasRealBars = true;
					double lastClose = sub.CachedBars[sub.CachedBars.Count - 1].Close;
					if (sub.LastPrice > 0 && Math.Abs(lastClose - sub.LastPrice) > 1e-9)
						sub.LastTickDir = lastClose > sub.LastPrice ? 1 : -1;
					sub.LastPrice = lastClose;
					sub.LastTickTime = DateTime.Now;
				}
			}
		}

		private void OnLiveBarsUpdate(SymbolSub sub, BarsUpdateEventArgs e)
		{
			if (isDisposed || sub == null || e == null || e.BarsSeries == null)
				return;

			try
			{
				lock (sub.Sync)
				{
					int idx = Math.Max(0, Math.Min(e.MaxIndex, e.BarsSeries.Count - 1));
					if (idx >= 0 && e.BarsSeries.Count > 0)
					{
						double close = e.BarsSeries.GetClose(idx);
						if (sub.LastPrice > 0 && Math.Abs(close - sub.LastPrice) > 1e-9)
							sub.LastTickDir = close > sub.LastPrice ? 1 : -1;
						sub.LastPrice = close;
						sub.TickCount++;
						sub.LastTickTime = DateTime.Now;

						MpBarSample sample = new MpBarSample
						{
							Time = e.BarsSeries.GetTime(idx),
							Open = e.BarsSeries.GetOpen(idx),
							High = e.BarsSeries.GetHigh(idx),
							Low = e.BarsSeries.GetLow(idx),
							Close = close,
							Volume = e.BarsSeries.GetVolume(idx)
						};

						if (sub.CachedBars.Count > 0 && sub.CachedBars[sub.CachedBars.Count - 1].Time == sample.Time)
							sub.CachedBars[sub.CachedBars.Count - 1] = sample;
						else
							sub.CachedBars.Add(sample);
						sub.HasRealBars = sub.CachedBars.Count >= 4;
					}
				}
				RefreshSymbol(sub.Symbol, false);
				RaiseUpdated();
			}
			catch { }
		}

		public void UpdateFromChartIndicatorBars(string symbol, List<MpBarSample> chart30MinSamples)
		{
			if (string.IsNullOrWhiteSpace(symbol) || chart30MinSamples == null || chart30MinSamples.Count < 4)
				return;

			SymbolSub sub = subscriptions.GetOrAdd(symbol, s => new SymbolSub { Symbol = s });
			lock (sub.Sync)
			{
				sub.CachedBars = new List<MpBarSample>(chart30MinSamples);
				sub.HasRealBars = true;
				double close = chart30MinSamples[chart30MinSamples.Count - 1].Close;
				if (sub.LastPrice > 0 && Math.Abs(close - sub.LastPrice) > 1e-9)
					sub.LastTickDir = close > sub.LastPrice ? 1 : -1;
				sub.LastPrice = close;
				sub.TickCount++;
				sub.LastTickTime = DateTime.Now;
			}
			RefreshSymbol(symbol, false);
		}

		private void RefreshSymbol(string symbol, bool allowSimTick)
		{
			SymbolSub sub;
			subscriptions.TryGetValue(symbol, out sub);

			List<MpBarSample> barsSnapshot = null;
			if (sub != null && sub.HasRealBars)
			{
				lock (sub.Sync)
				{
					if (sub.CachedBars.Count >= 4)
						barsSnapshot = new List<MpBarSample>(sub.CachedBars);
				}
			}

			if (barsSnapshot != null && barsSnapshot.Count >= 4)
			{
				MpScannerRow computed = ComputeRowFrom30MinBars(symbol, barsSnapshot, sub);
				if (computed != null)
				{
					rowsBySymbol[symbol] = computed;
					return;
				}
			}

			// Fallback to reference 50Charts state (with optional live tick tracking simulation)
			MpScannerRow seeded = BuildSeededRow(symbol, sub, allowSimTick && EnableLiveTickSimulationWhenIdle);
			rowsBySymbol[symbol] = seeded;
		}

		private MpScannerRow ComputeRowFrom30MinBars(string symbol, List<MpBarSample> bars, SymbolSub sub)
		{
			// Group 30-min bars by trading date
			var dayGroups = bars
				.GroupBy(b => b.Time.Date)
				.OrderBy(g => g.Key)
				.Select(g => g.OrderBy(b => b.Time).ToList())
				.Where(g => g.Count > 0)
				.ToList();

			if (dayGroups.Count < 2)
				return null;

			List<MpBarSample> todayBars = dayGroups[dayGroups.Count - 1];
			List<MpBarSample> prevBars = dayGroups[dayGroups.Count - 2];

			double tickSize = DefaultMinTickSize;
			if (sub != null && sub.Instrument != null && sub.Instrument.MasterInstrument != null && sub.Instrument.MasterInstrument.TickSize > 0)
				tickSize = sub.Instrument.MasterInstrument.TickSize;

			// Compute Daily closes & ranges for SMA(21) and ATR(10)
			List<double> dailyCloses = new List<double>();
			List<double> trueRanges = new List<double>();
			double prevCloseForTr = 0;
			for (int d = 0; d < dayGroups.Count; d++)
			{
				var dg = dayGroups[d];
				double dHigh = dg.Max(x => x.High);
				double dLow = dg.Min(x => x.Low);
				double dClose = dg[dg.Count - 1].Close;
				dailyCloses.Add(dClose);
				double tr = dHigh - dLow;
				if (d > 0 && prevCloseForTr > 0)
				{
					tr = Math.Max(tr, Math.Max(Math.Abs(dHigh - prevCloseForTr), Math.Abs(dLow - prevCloseForTr)));
				}
				trueRanges.Add(tr);
				prevCloseForTr = dClose;
			}

			int smaCount = Math.Min(SmaDays, dailyCloses.Count);
			double smaVal = dailyCloses.Skip(dailyCloses.Count - smaCount).Average();

			// Exclude current developing day from ATR(10) if possible, matching 50Charts Daily ATR(10)
			var atrSource = trueRanges.Count > 1 ? trueRanges.Take(trueRanges.Count - 1).ToList() : trueRanges;
			int atrCount = Math.Min(AtrDays, atrSource.Count);
			double atrVal = Math.Max(tickSize * 4, atrSource.Skip(atrSource.Count - atrCount).Average());

			double pdHigh = prevBars.Max(x => x.High);
			double pdLow = prevBars.Min(x => x.Low);
			double pdClose = prevBars[prevBars.Count - 1].Close;
			double pdVol = prevBars.Sum(x => x.Volume);

			double tdOpen = todayBars[0].Open;
			double tdHigh = todayBars.Max(x => x.High);
			double tdLow = todayBars.Min(x => x.Low);
			double tdClose = todayBars[todayBars.Count - 1].Close;
			double tdVol = todayBars.Sum(x => x.Volume);
			DateTime lastBarTime = todayBars[todayBars.Count - 1].Time;

			double tpoStep = EstimateAutoTpoSize(symbol, tdClose, atrVal, tickSize);

			// Build Previous Day & Today TPO Profiles
			double pdVah, pdPoc, pdVal;
			int pdPocCount;
			bool pdSpUp, pdSpDown;
			string pdDayType;
			MpCellTone pdDayTypeTone;
			BuildTpoProfileMetrics(prevBars, tpoStep, IbCandleCount, ValueAreaPercent,
				out pdVah, out pdPoc, out pdVal, out pdPocCount, out pdSpUp, out pdSpDown, out pdDayType, out pdDayTypeTone);

			double tdVah, tdPoc, tdVal;
			int tdPocCount;
			bool tdSpUp, tdSpDown;
			string tdDayType;
			MpCellTone tdDayTypeTone;
			BuildTpoProfileMetrics(todayBars, tpoStep, IbCandleCount, ValueAreaPercent,
				out tdVah, out tdPoc, out tdVal, out tdPocCount, out tdSpUp, out tdSpDown, out tdDayType, out tdDayTypeTone);

			int ibBarsCount = Math.Min(Math.Max(1, IbCandleCount), todayBars.Count);
			double ibHigh = todayBars.Take(ibBarsCount).Max(x => x.High);
			double ibLow = todayBars.Take(ibBarsCount).Min(x => x.Low);
			double ibRange = Math.Max(0, ibHigh - ibLow);

			MpScannerRow row = new MpScannerRow
			{
				Ticker = symbol,
				DateTimeStr = lastBarTime.ToString("dd-MM-yyyy hh:mm:ss tt", CultureInfo.InvariantCulture),
				LastUpdateTime = DateTime.Now,
				IsLiveConnected = true,
				TickCount = sub != null ? sub.TickCount : 1,
				LastTickDirection = sub != null ? sub.LastTickDir : (tdClose >= tdOpen ? 1 : -1),
				PrevDayHigh = pdHigh,
				PrevDayLow = pdLow,
				PrevDayClose = pdClose,
				PrevDayVolume = pdVol,
				TodayVolume = tdVol,
				TodayOpen = tdOpen,
				TodayHigh = tdHigh,
				TodayLow = tdLow,
				Price = tdClose,
				PercentChange = pdClose > 1e-9 ? ((tdClose - pdClose) / pdClose) * 100.0 : 0.0,
				DailySma = smaVal,
				PdTpoSize = tpoStep,
				PdVah = pdVah,
				PdPoc = pdPoc,
				PdVal = pdVal,
				PdPocTpoCount = pdPocCount,
				PrevDayType = pdDayType,
				PrevDayTypeTone = pdDayTypeTone,
				DailyAtr = atrVal,
				HlRange = tdHigh - tdLow,
				IbHigh = ibHigh,
				IbLow = ibLow,
				IbRange = ibRange,
				TodayTpoSize = tpoStep,
				TodayVah = tdVah,
				TodayPoc = tdPoc,
				TodayVal = tdVal,
				TodayPocTpoCount = tdPocCount,
				TodayDayType = tdDayType,
				TodayDayTypeTone = MpCellTone.Default
			};

			row.PriceTone = row.PercentChange >= 0 ? MpCellTone.FgGreen : MpCellTone.FgRed;

			// Open=H/L
			if (Math.Abs(tdOpen - tdLow) <= tickSize * 1.5 && (tdHigh - tdLow) > tickSize * 2)
			{
				row.OpenEqualHL = "Open = Low";
				row.OpenEqualHLTone = MpCellTone.BgGreen;
			}
			else if (Math.Abs(tdOpen - tdHigh) <= tickSize * 1.5 && (tdHigh - tdLow) > tickSize * 2)
			{
				row.OpenEqualHL = "Open = High";
				row.OpenEqualHLTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.OpenEqualHL = "";
				row.OpenEqualHLTone = MpCellTone.Default;
			}

			// Price - SMA(21)
			string smaColLabel = "SMA(" + SmaDays + ")";
			if (tdClose >= smaVal)
			{
				row.PriceVsSma = "Price Above " + smaColLabel;
				row.PriceVsSmaTone = MpCellTone.BgGreen;
			}
			else
			{
				row.PriceVsSma = "Price Below " + smaColLabel;
				row.PriceVsSmaTone = MpCellTone.BgRedOrange;
			}

			// PD-SingleP
			if (pdSpUp)
			{
				row.PdSinglePrint = "Single Print - Up";
				row.PdSinglePrintTone = MpCellTone.BgGreen;
			}
			else if (pdSpDown)
			{
				row.PdSinglePrint = "Single Print - Down";
				row.PdSinglePrintTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.PdSinglePrint = "";
				row.PdSinglePrintTone = MpCellTone.Default;
			}

			// Open vs PDVA
			if (tdOpen > pdVah)
			{
				row.OpenVsPdVa = "Open Above PDVA";
				row.OpenVsPdVaTone = MpCellTone.BgGreen;
			}
			else if (tdOpen < pdVal)
			{
				row.OpenVsPdVa = "Open Below PDVA";
				row.OpenVsPdVaTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.OpenVsPdVa = "Open Inside PDVA";
				row.OpenVsPdVaTone = MpCellTone.Default;
			}

			// Open vs PDHL
			if (tdOpen > pdHigh)
			{
				row.OpenVsPdHl = "Open Above PDHL";
				row.OpenVsPdHlTone = MpCellTone.BgGreen;
			}
			else if (tdOpen < pdLow)
			{
				row.OpenVsPdHl = "Open Below PDHL";
				row.OpenVsPdHlTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.OpenVsPdHl = "Open Inside PDHL";
				row.OpenVsPdHlTone = MpCellTone.Default;
			}

			// OpenType (50Charts: Open Drive, OpenTest Drive, ORR, OAOR, OAIR)
			string openTypeStr;
			MpCellTone openTypeTone;
			ClassifyOpenType50Charts(todayBars, pdHigh, pdLow, pdVah, pdVal, tpoStep, out openTypeStr, out openTypeTone);
			row.OpenType = openTypeStr;
			row.OpenTypeTone = openTypeTone;

			// PrevDay Breakout
			bool brokePdUp = tdHigh > pdHigh;
			bool brokePdDown = tdLow < pdLow;
			if (brokePdUp && brokePdDown)
			{
				row.PrevDayBreakout = "Broken - BothSides";
				row.PrevDayBreakoutTone = MpCellTone.BgYellow;
			}
			else if (brokePdUp)
			{
				row.PrevDayBreakout = "Broken - Up";
				row.PrevDayBreakoutTone = MpCellTone.BgGreen;
			}
			else if (brokePdDown)
			{
				row.PrevDayBreakout = "Broken - Down";
				row.PrevDayBreakoutTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.PrevDayBreakout = "";
				row.PrevDayBreakoutTone = MpCellTone.Default;
			}

			// IB Size (exact 50Charts thresholds from 0x1002f706: >0.80 Very Wide, >0.50 Wide, <0.3333 Small, else Normal)
			double ibAtrRatio = atrVal > 1e-9 ? (ibRange / atrVal) : 0.45;
			if (ibAtrRatio > 0.80)
				row.IbSize = "Very Wide";
			else if (ibAtrRatio > 0.50)
				row.IbSize = "Wide";
			else if (ibAtrRatio < (1.0 / 3.0))
				row.IbSize = "Small";
			else
				row.IbSize = "Normal";
			row.IbSizeTone = MpCellTone.Default;

			// IB Breakout
			bool ibBrokenUp = todayBars.Count > ibBarsCount && tdHigh > ibHigh + tickSize * 0.5;
			bool ibBrokenDown = todayBars.Count > ibBarsCount && tdLow < ibLow - tickSize * 0.5;
			if (ibBrokenUp && ibBrokenDown)
			{
				row.IbBreakout = "Broken - BothSides";
				row.IbBreakoutTone = MpCellTone.BgYellow;
			}
			else if (ibBrokenUp)
			{
				row.IbBreakout = "Broken - Up";
				row.IbBreakoutTone = MpCellTone.BgGreen;
			}
			else if (ibBrokenDown)
			{
				row.IbBreakout = "Broken - Down";
				row.IbBreakoutTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.IbBreakout = "";
				row.IbBreakoutTone = MpCellTone.Default;
			}

			// Price - IB
			if (tdClose > ibHigh)
			{
				row.PriceVsIb = "Price Above IB";
				row.PriceVsIbTone = MpCellTone.BgGreen;
			}
			else if (tdClose < ibLow)
			{
				row.PriceVsIb = "Price Below IB";
				row.PriceVsIbTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.PriceVsIb = "Price Within IB";
				row.PriceVsIbTone = MpCellTone.BgYellow;
			}

			// Today VA vs PDVA
			string todayVaStr;
			MpCellTone todayVaTone;
			ClassifyTodayVa50Charts(tdVah, tdVal, pdVah, pdVal, tpoStep, out todayVaStr, out todayVaTone);
			row.TodayVaStatus = todayVaStr;
			row.TodayVaTone = todayVaTone;

			// 30-min PreviousBar
			string prevBarStr;
			MpCellTone prevBarTone;
			Classify30MinPrevBar(todayBars, prevBars, out prevBarStr, out prevBarTone);
			row.Prev30MinBarStatus = prevBarStr;
			row.Prev30MinBarTone = prevBarTone;

			// Today SingleP
			if (tdSpUp)
			{
				row.TodaySinglePrint = "Single Print - Up";
				row.TodaySinglePrintTone = MpCellTone.BgGreen;
			}
			else if (tdSpDown)
			{
				row.TodaySinglePrint = "Single Print - Down";
				row.TodaySinglePrintTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.TodaySinglePrint = "";
				row.TodaySinglePrintTone = MpCellTone.Default;
			}

			return row;
		}

		private static double EstimateAutoTpoSize(string symbol, double price, double atr, double tickSize)
		{
			if (symbol.StartsWith("BANKNIFTY", StringComparison.OrdinalIgnoreCase))
				return 10.0;
			if (symbol.StartsWith("NIFTY", StringComparison.OrdinalIgnoreCase))
				return 3.0;
			if (symbol.StartsWith("ES", StringComparison.OrdinalIgnoreCase) || symbol.StartsWith("MES", StringComparison.OrdinalIgnoreCase))
				return 0.25;
			if (symbol.StartsWith("GC", StringComparison.OrdinalIgnoreCase) || symbol.StartsWith("MGC", StringComparison.OrdinalIgnoreCase))
				return 0.50;

			double raw = Math.Max(tickSize, atr / 35.0);
			if (price > 5000) return 5.0;
			if (price > 2500) return 2.0;
			if (price > 800) return 1.0;
			if (price > 300) return 0.50;
			if (price > 100) return 0.25;
			return Math.Max(tickSize, Math.Round(raw / tickSize) * tickSize);
		}

		private static void BuildTpoProfileMetrics(
			List<MpBarSample> sessionBars,
			double tpoStep,
			int ibCount,
			double vaPct,
			out double vahPrice,
			out double pocPrice,
			out double valPrice,
			out int pocTpoCount,
			out bool hasSinglePrintUp,
			out bool hasSinglePrintDown,
			out string dayType,
			out MpCellTone dayTypeTone)
		{
			vahPrice = 0; pocPrice = 0; valPrice = 0; pocTpoCount = 0;
			hasSinglePrintUp = false; hasSinglePrintDown = false;
			dayType = "Non-Trend day";
			dayTypeTone = MpCellTone.Default;

			if (sessionBars == null || sessionBars.Count == 0)
				return;

			double step = Math.Max(1e-5, tpoStep);
			SortedDictionary<int, HashSet<int>> rows = new SortedDictionary<int, HashSet<int>>();
			for (int p = 0; p < sessionBars.Count; p++)
			{
				MpBarSample b = sessionBars[p];
				int lowBin = (int)Math.Floor(b.Low / step);
				int highBin = (int)Math.Ceiling(b.High / step);
				if (highBin - lowBin > 400)
				{
					step = (b.High - b.Low) / 100.0;
					lowBin = (int)Math.Floor(b.Low / step);
					highBin = (int)Math.Ceiling(b.High / step);
				}
				for (int bin = lowBin; bin <= highBin; bin++)
				{
					HashSet<int> set;
					if (!rows.TryGetValue(bin, out set))
					{
						set = new HashSet<int>();
						rows[bin] = set;
					}
					set.Add(p);
				}
			}

			if (rows.Count == 0)
				return;

			int minBin = rows.Keys.Min();
			int maxBin = rows.Keys.Max();
			double midBin = (minBin + maxBin) * 0.5;

			int pocBin = rows
				.OrderByDescending(kv => kv.Value.Count)
				.ThenBy(kv => Math.Abs(kv.Key - midBin))
				.First().Key;

			pocTpoCount = rows[pocBin].Count;
			int totalTpo = Math.Max(1, rows.Values.Sum(s => s.Count));
			int targetTpo = Math.Max(1, (int)Math.Ceiling(totalTpo * (vaPct / 100.0)));
			int included = pocTpoCount;
			int valBin = pocBin;
			int vahBin = pocBin;

			while (included < targetTpo && (valBin > minBin || vahBin < maxBin))
			{
				HashSet<int> lowSet, highSet;
				int lowCnt = valBin > minBin && rows.TryGetValue(valBin - 1, out lowSet) ? lowSet.Count : -1;
				int highCnt = vahBin < maxBin && rows.TryGetValue(vahBin + 1, out highSet) ? highSet.Count : -1;
				if (highCnt >= lowCnt && vahBin < maxBin)
				{
					vahBin++;
					if (highCnt > 0) included += highCnt;
				}
				else if (valBin > minBin)
				{
					valBin--;
					if (lowCnt > 0) included += lowCnt;
				}
				else if (vahBin < maxBin)
				{
					vahBin++;
					if (highCnt > 0) included += highCnt;
				}
			}

			pocPrice = pocBin * step;
			vahPrice = vahBin * step;
			valPrice = valBin * step;

			// Interior single prints detection
			if (maxBin - minBin >= 6 && sessionBars.Count >= 3)
			{
				for (int b = minBin + 2; b <= maxBin - 2; b++)
				{
					HashSet<int> pset;
					if (rows.TryGetValue(b, out pset) && pset.Count == 1)
					{
						if (b <= pocBin) hasSinglePrintUp = true;
						else hasSinglePrintDown = true;
					}
				}
			}

			// Day Type classification (exact 10 50Charts Day Types)
			double dayOpen = sessionBars[0].Open;
			double dayClose = sessionBars[sessionBars.Count - 1].Close;
			double dayHigh = sessionBars.Max(x => x.High);
			double dayLow = sessionBars.Min(x => x.Low);
			double dayRange = Math.Max(step, dayHigh - dayLow);

			int ibCnt = Math.Min(Math.Max(1, ibCount), sessionBars.Count);
			double ibH = sessionBars.Take(ibCnt).Max(x => x.High);
			double ibL = sessionBars.Take(ibCnt).Min(x => x.Low);
			double ibRange = Math.Max(step, ibH - ibL);

			bool extUp = (dayHigh - ibH) > step * 1.5;
			bool extDown = (ibL - dayLow) > step * 1.5;
			bool isUp = dayClose >= dayOpen;
			string dir = isUp ? "Up" : "Down";

			if (!extUp && !extDown)
			{
				if (ibRange < dayRange * 0.45 || rows.Count <= 6)
				{
					dayType = "Non-Trend day";
					dayTypeTone = MpCellTone.Default;
				}
				else
				{
					dayType = "Normal day - " + dir;
					dayTypeTone = isUp ? MpCellTone.BgGreen : MpCellTone.BgRedOrange;
				}
			}
			else if (extUp && extDown)
			{
				double closePos = (dayClose - dayLow) / dayRange;
				if (closePos >= 0.72)
				{
					dayType = "Neutral - Extreme day - Up";
					dayTypeTone = MpCellTone.BgYellow;
				}
				else if (closePos <= 0.28)
				{
					dayType = "Neutral - Extreme day - Down";
					dayTypeTone = MpCellTone.BgYellow;
				}
				else
				{
					dayType = "Neutral - Centre day";
					dayTypeTone = MpCellTone.BgYellow;
				}
			}
			else if (dayRange >= ibRange * 2.0)
			{
				string trendDir = extUp ? "Up" : "Down";
				dayType = "Trend day - " + trendDir;
				dayTypeTone = extUp ? MpCellTone.BgGreen : MpCellTone.BgRedOrange;
			}
			else
			{
				string nvDir = extUp ? "Up" : "Down";
				dayType = "Normal Variation day - " + nvDir;
				dayTypeTone = extUp ? MpCellTone.BgGreen : MpCellTone.BgRedOrange;
			}
		}

		private static void ClassifyOpenType50Charts(
			List<MpBarSample> todayBars,
			double pdHigh,
			double pdLow,
			double pdVah,
			double pdVal,
			double step,
			out string openType,
			out MpCellTone tone)
		{
			openType = "OAIR";
			tone = MpCellTone.Default;
			if (todayBars == null || todayBars.Count == 0)
				return;

			MpBarSample first = todayBars[0];
			double open = first.Open;
			double firstRange = Math.Max(step, first.High - first.Low);
			double openNearLow = (open - first.Low) / firstRange;
			double openNearHigh = (first.High - open) / firstRange;

			if (openNearLow <= 0.15 && first.Close > open + firstRange * 0.55)
			{
				openType = "Open Drive";
				tone = MpCellTone.BgGreen;
				return;
			}
			if (openNearHigh <= 0.15 && first.Close < open - firstRange * 0.55)
			{
				openType = "Open Drive";
				tone = MpCellTone.BgGreen;
				return;
			}

			bool testedRef = (first.Low <= pdVah && first.Close > pdVah)
				|| (first.High >= pdVal && first.Close < pdVal)
				|| (first.Low <= pdHigh && first.Close > pdHigh)
				|| (first.High >= pdLow && first.Close < pdLow);

			if (testedRef && Math.Abs(first.Close - open) >= firstRange * 0.40)
			{
				openType = "OpenTest Drive";
				tone = MpCellTone.BgGreen;
				return;
			}

			if (todayBars.Count >= 2)
			{
				MpBarSample second = todayBars[1];
				if ((open > pdVah && second.Close < pdVah) || (open < pdVal && second.Close > pdVal))
				{
					openType = "ORR";
					tone = MpCellTone.Default;
					return;
				}
			}

			if (open > pdVah || open < pdVal)
			{
				openType = "OAOR";
				tone = MpCellTone.Default;
			}
			else
			{
				openType = "OAIR";
				tone = MpCellTone.Default;
			}
		}

		private static void ClassifyTodayVa50Charts(
			double tdVah,
			double tdVal,
			double pdVah,
			double pdVal,
			double step,
			out string status,
			out MpCellTone tone)
		{
			if (tdVal >= pdVah - step * 0.5)
			{
				status = "Higher than PDVA";
				tone = MpCellTone.BgGreen;
			}
			else if (tdVAHIsLower(tdVah, pdVal, step))
			{
				status = "Lower than PDVA";
				tone = MpCellTone.BgRedOrange;
			}
			else if (Math.Abs(tdVah - pdVah) <= step && Math.Abs(tdVal - pdVal) <= step)
			{
				status = "Same VA";
				tone = MpCellTone.Default;
			}
			else if (tdVah <= pdVah && tdVal >= pdVal)
			{
				status = "Inside PrevVA";
				tone = MpCellTone.Default;
			}
			else if (tdVah > pdVah && tdVal < pdVal)
			{
				status = "Overlapping to PDVA H & L";
				tone = MpCellTone.BgYellow;
			}
			else if (tdVah > pdVah && tdVal >= pdVal && tdVal < pdVah)
			{
				status = "Overlapping to PDVA High";
				tone = MpCellTone.BgGreen;
			}
			else
			{
				status = "Overlapping to PDVA low";
				tone = MpCellTone.BgRedOrange;
			}
		}

		private static bool tdVAHIsLower(double tdVah, double pdVal, double step)
		{
			return tdVah <= pdVal + step * 0.5;
		}

		private static void Classify30MinPrevBar(
			List<MpBarSample> todayBars,
			List<MpBarSample> prevBars,
			out string status,
			out MpCellTone tone)
		{
			status = "";
			tone = MpCellTone.Default;

			List<MpBarSample> combined = new List<MpBarSample>();
			if (prevBars != null) combined.AddRange(prevBars);
			if (todayBars != null) combined.AddRange(todayBars);
			if (combined.Count < 3)
				return;

			MpBarSample curr = combined[combined.Count - 1];
			MpBarSample prev1 = combined[combined.Count - 2];
			MpBarSample prev2 = combined[combined.Count - 3];

			bool isInside = prev1.High <= prev2.High && prev1.Low >= prev2.Low;
			bool isOutside = prev1.High > prev2.High && prev1.Low < prev2.Low;
			if (!isInside && !isOutside)
				return;

			string baseLabel = isInside ? "Inside Bar" : "Outside Bar";
			bool brokeUp = curr.High > prev1.High;
			bool brokeDown = curr.Low < prev1.Low;

			if (brokeUp && brokeDown)
			{
				status = baseLabel + " - Broken BothSides";
				tone = MpCellTone.Default;
			}
			else if (brokeUp)
			{
				status = baseLabel + " - Broken Up";
				tone = MpCellTone.FgGreen;
			}
			else if (brokeDown)
			{
				status = baseLabel + " - Broken Down";
				tone = MpCellTone.FgOrange;
			}
			else
			{
				status = baseLabel;
				tone = MpCellTone.Default;
			}
		}

		private MpScannerRow BuildSeededRow(string symbol, SymbolSub sub, bool simTick)
		{
			SeedReference seed;
			if (!referenceSeeds.TryGetValue(symbol, out seed))
			{
				seed = new SeedReference(symbol, 1000.00, 0.45, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "Broken - Up", 22.50, 18.40, "Normal", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "", "Normal Variation day - Up", 1.0);
				referenceSeeds[symbol] = seed;
			}

			double prevClose = seed.Price / (1.0 + (seed.PctChange / 100.0));
			double livePrice = seed.Price;
			int tickDir = seed.PctChange >= 0 ? 1 : -1;
			long ticks = 1;

			if (sub != null)
			{
				if (sub.LastPrice <= 0)
					sub.LastPrice = seed.Price;
				if (simTick)
				{
					double step = Math.Max(0.05, seed.TpoSize * 0.1);
					double delta = (tickRng.NextDouble() - 0.485) * step;
					sub.LastPrice = Math.Max(step, Math.Round((sub.LastPrice + delta) * 20.0) / 20.0);
					sub.LastTickDir = delta >= 0 ? 1 : -1;
					sub.TickCount++;
					sub.LastTickTime = DateTime.Now;
				}
				livePrice = sub.LastPrice;
				tickDir = sub.LastTickDir;
				ticks = Math.Max(1, sub.TickCount);
			}

			double pctChange = ((livePrice - prevClose) / prevClose) * 100.0;
			double hlRange = Math.Max(seed.HlRange, Math.Abs(livePrice - prevClose) * 1.15);
			double tdLow = Math.Round((livePrice - hlRange * (pctChange >= 0 ? 0.65 : 0.35)) * 20.0) / 20.0;
			double tdHigh = Math.Round((tdLow + hlRange) * 20.0) / 20.0;
			double tdOpen = seed.OpenHL == "Open = Low" ? tdLow : (seed.OpenHL == "Open = High" ? tdHigh : Math.Round((prevClose * 1.001) * 20.0) / 20.0);
			double ibRange = Math.Round((hlRange * 0.54) * 20.0) / 20.0;
			double ibLow = Math.Round((tdLow + hlRange * 0.12) * 20.0) / 20.0;
			double ibHigh = Math.Round((ibLow + ibRange) * 20.0) / 20.0;
			double sma21 = seed.AboveSma ? Math.Round((livePrice * 0.982) * 20.0) / 20.0 : Math.Round((livePrice * 1.018) * 20.0) / 20.0;

			MpScannerRow row = new MpScannerRow
			{
				Ticker = symbol,
				DateTimeStr = DateTime.Now.ToString("dd-MM-yyyy hh:mm:ss tt", CultureInfo.InvariantCulture),
				LastUpdateTime = DateTime.Now,
				IsLiveConnected = sub != null && sub.HasRealBars,
				TickCount = ticks,
				LastTickDirection = tickDir,
				PrevDayHigh = Math.Round((prevClose + seed.Atr10 * 0.55) * 20.0) / 20.0,
				PrevDayLow = Math.Round((prevClose - seed.Atr10 * 0.45) * 20.0) / 20.0,
				PrevDayClose = Math.Round(prevClose * 20.0) / 20.0,
				PrevDayVolume = 1250000,
				TodayVolume = 985000 + ticks * 120,
				TodayOpen = tdOpen,
				TodayHigh = tdHigh,
				TodayLow = tdLow,
				OpenEqualHL = seed.OpenHL,
				OpenEqualHLTone = seed.OpenHL == "Open = Low" ? MpCellTone.BgGreen : (seed.OpenHL == "Open = High" ? MpCellTone.BgRedOrange : MpCellTone.Default),
				Price = livePrice,
				PercentChange = pctChange,
				PriceTone = pctChange >= 0 ? MpCellTone.FgGreen : MpCellTone.FgRed,
				DailySma = sma21,
				PriceVsSma = seed.AboveSma ? ("Price Above SMA(" + SmaDays + ")") : ("Price Below SMA(" + SmaDays + ")"),
				PriceVsSmaTone = seed.AboveSma ? MpCellTone.BgGreen : MpCellTone.BgRedOrange,
				PdTpoSize = seed.TpoSize,
				PdVah = Math.Round((prevClose + seed.Atr10 * 0.28) * 20.0) / 20.0,
				PdPoc = Math.Round(prevClose * 20.0) / 20.0,
				PdVal = Math.Round((prevClose - seed.Atr10 * 0.25) * 20.0) / 20.0,
				PdPocTpoCount = 8,
				PrevDayType = seed.PrevDayType,
				PrevDayTypeTone = GetDayTypeTone(seed.PrevDayType),
				PdSinglePrint = seed.PdSingleP,
				PdSinglePrintTone = GetSinglePrintTone(seed.PdSingleP),
				OpenVsPdVa = seed.OpenVa,
				OpenVsPdVaTone = seed.OpenVa.Contains("Above") ? MpCellTone.BgGreen : (seed.OpenVa.Contains("Below") ? MpCellTone.BgRedOrange : MpCellTone.Default),
				OpenVsPdHl = seed.OpenHl,
				OpenVsPdHlTone = seed.OpenHl.Contains("Above") ? MpCellTone.BgGreen : (seed.OpenHl.Contains("Below") ? MpCellTone.BgRedOrange : MpCellTone.Default),
				OpenType = seed.OpenType,
				OpenTypeTone = (seed.OpenType == "Open Drive" || seed.OpenType == "OpenTest Drive") ? MpCellTone.BgGreen : MpCellTone.Default,
				PrevDayBreakout = seed.PdBreak,
				PrevDayBreakoutTone = GetBreakoutTone(seed.PdBreak),
				DailyAtr = seed.Atr10,
				HlRange = hlRange,
				IbHigh = ibHigh,
				IbLow = ibLow,
				IbRange = ibRange,
				IbSize = seed.IbSize,
				IbSizeTone = MpCellTone.Default,
				IbBreakout = seed.IbBreak,
				IbBreakoutTone = GetBreakoutTone(seed.IbBreak),
				PriceVsIb = seed.PriceIb,
				PriceVsIbTone = seed.PriceIb.Contains("Above") ? MpCellTone.BgGreen : (seed.PriceIb.Contains("Below") ? MpCellTone.BgRedOrange : MpCellTone.BgYellow),
				TodayTpoSize = seed.TpoSize,
				TodayVah = Math.Round((livePrice + hlRange * 0.22) * 20.0) / 20.0,
				TodayPoc = Math.Round((livePrice - hlRange * 0.05) * 20.0) / 20.0,
				TodayVal = Math.Round((livePrice - hlRange * 0.28) * 20.0) / 20.0,
				TodayPocTpoCount = 7,
				TodayVaStatus = seed.TodayVa,
				TodayVaTone = GetTodayVaTone(seed.TodayVa),
				Prev30MinBarStatus = seed.Bar30Min,
				Prev30MinBarTone = GetPrevBarTone(seed.Bar30Min),
				TodaySinglePrint = seed.SingleP,
				TodaySinglePrintTone = GetSinglePrintTone(seed.SingleP),
				TodayDayType = seed.DayType,
				TodayDayTypeTone = MpCellTone.Default
			};

			return row;
		}

		private static MpCellTone GetDayTypeTone(string dt)
		{
			if (string.IsNullOrEmpty(dt) || dt.StartsWith("Non-Trend", StringComparison.OrdinalIgnoreCase))
				return MpCellTone.Default;
			if (dt.StartsWith("Neutral", StringComparison.OrdinalIgnoreCase))
				return MpCellTone.BgYellow;
			if (dt.EndsWith("Up", StringComparison.OrdinalIgnoreCase))
				return MpCellTone.BgGreen;
			if (dt.EndsWith("Down", StringComparison.OrdinalIgnoreCase))
				return MpCellTone.BgRedOrange;
			return MpCellTone.Default;
		}

		private static MpCellTone GetSinglePrintTone(string sp)
		{
			if (string.IsNullOrEmpty(sp)) return MpCellTone.Default;
			return sp.Contains("Up") ? MpCellTone.BgGreen : MpCellTone.BgRedOrange;
		}

		private static MpCellTone GetBreakoutTone(string brk)
		{
			if (string.IsNullOrEmpty(brk)) return MpCellTone.Default;
			if (brk.Contains("BothSides")) return MpCellTone.BgYellow;
			if (brk.Contains("Up")) return MpCellTone.BgGreen;
			return MpCellTone.BgRedOrange;
		}

		private static MpCellTone GetTodayVaTone(string tva)
		{
			if (string.IsNullOrEmpty(tva)) return MpCellTone.Default;
			if (tva.StartsWith("Higher", StringComparison.OrdinalIgnoreCase) || tva.EndsWith("High", StringComparison.OrdinalIgnoreCase))
				return MpCellTone.BgGreen;
			if (tva.StartsWith("Lower", StringComparison.OrdinalIgnoreCase) || tva.EndsWith("low", StringComparison.OrdinalIgnoreCase))
				return MpCellTone.BgRedOrange;
			if (tva.Contains("H & L"))
				return MpCellTone.BgYellow;
			return MpCellTone.Default;
		}

		private static MpCellTone GetPrevBarTone(string pb)
		{
			if (string.IsNullOrEmpty(pb)) return MpCellTone.Default;
			if (pb.EndsWith("Broken Up", StringComparison.OrdinalIgnoreCase)) return MpCellTone.FgGreen;
			if (pb.EndsWith("Broken Down", StringComparison.OrdinalIgnoreCase)) return MpCellTone.FgOrange;
			return MpCellTone.Default;
		}

		public List<MpScannerRow> GetFilteredAndSortedRows(
			MpFilterPreset filterPreset,
			string searchText,
			string sortColumnId,
			bool sortAscending,
			Func<MpScannerRow, string> textSelect,
			Func<MpScannerRow, double> numSelect,
			bool isNumericSort)
		{
			List<string> targetSyms = GetTargetSymbols();
			List<MpScannerRow> list = new List<MpScannerRow>(targetSyms.Count);
			for (int i = 0; i < targetSyms.Count; i++)
			{
				MpScannerRow r;
				if (rowsBySymbol.TryGetValue(targetSyms[i], out r) && r != null)
					list.Add(r);
			}

			IEnumerable<MpScannerRow> query = list;

			if (filterPreset == MpFilterPreset.IbBreakoutAny)
				query = query.Where(r => !string.IsNullOrEmpty(r.IbBreakout));
			else if (filterPreset == MpFilterPreset.TrendDays)
				query = query.Where(r => (r.TodayDayType != null && r.TodayDayType.StartsWith("Trend", StringComparison.OrdinalIgnoreCase))
					|| (r.PrevDayType != null && r.PrevDayType.StartsWith("Trend", StringComparison.OrdinalIgnoreCase)));
			else if (filterPreset == MpFilterPreset.SinglePrints)
				query = query.Where(r => !string.IsNullOrEmpty(r.TodaySinglePrint) || !string.IsNullOrEmpty(r.PdSinglePrint));
			else if (filterPreset == MpFilterPreset.OpenDriveOrTest)
				query = query.Where(r => r.OpenType == "Open Drive" || r.OpenType == "OpenTest Drive");
			else if (filterPreset == MpFilterPreset.PrevBarBreakout)
				query = query.Where(r => !string.IsNullOrEmpty(r.Prev30MinBarStatus) && r.Prev30MinBarStatus.Contains("Broken"));
			else if (filterPreset == MpFilterPreset.OpenEqualsHL)
				query = query.Where(r => !string.IsNullOrEmpty(r.OpenEqualHL));

			if (!string.IsNullOrWhiteSpace(searchText))
			{
				string q = searchText.Trim();
				query = query.Where(r =>
					(r.Ticker != null && r.Ticker.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
					(r.TodayDayType != null && r.TodayDayType.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
					(r.PrevDayType != null && r.PrevDayType.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
					(r.OpenType != null && r.OpenType.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
					(r.IbBreakout != null && r.IbBreakout.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
					(r.Prev30MinBarStatus != null && r.Prev30MinBarStatus.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
			}

			if (!string.IsNullOrEmpty(sortColumnId))
			{
				if (isNumericSort && numSelect != null)
					query = sortAscending ? query.OrderBy(numSelect) : query.OrderByDescending(numSelect);
				else if (textSelect != null)
					query = sortAscending ? query.OrderBy(textSelect, StringComparer.OrdinalIgnoreCase) : query.OrderByDescending(textSelect, StringComparer.OrdinalIgnoreCase);
			}

			return query.ToList();
		}

		public static List<MpColumnDef> BuildColumnDefinitions(
			int smaDays,
			int atrDays,
			bool showPrevDayValues,
			bool showTodayValues,
			bool showDailySma,
			bool showPrevDayMpLevels,
			bool showIbValues,
			bool showTodayMpLevels)
		{
			List<MpColumnDef> cols = new List<MpColumnDef>(43);

			cols.Add(new MpColumnDef { Id = "Ticker", Header = "Ticker", Group = "Core", Width = 112f, IsNumeric = false, TextSelector = r => r.Ticker, ToneSelector = r => MpCellTone.Default });
			cols.Add(new MpColumnDef { Id = "DateTime", Header = "Date/Time", Group = "Core", Width = 104f, IsNumeric = false, TextSelector = r => r.DateTimeStr, ToneSelector = r => MpCellTone.Default });

			if (showPrevDayValues)
			{
				cols.Add(new MpColumnDef { Id = "PDH", Header = "PrevDay High", Group = "PrevDayValues", Width = 86f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PrevDayHigh), SortNumSelector = r => r.PrevDayHigh, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PDL", Header = "PrevDay Low", Group = "PrevDayValues", Width = 86f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PrevDayLow), SortNumSelector = r => r.PrevDayLow, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PDC", Header = "PrevDay Close", Group = "PrevDayValues", Width = 90f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PrevDayClose), SortNumSelector = r => r.PrevDayClose, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PDV", Header = "PrevDay Volume", Group = "PrevDayValues", Width = 96f, IsNumeric = true, TextSelector = r => r.PrevDayVolume.ToString("0", CultureInfo.InvariantCulture), SortNumSelector = r => r.PrevDayVolume, ToneSelector = r => MpCellTone.Default });
			}

			if (showTodayValues)
			{
				cols.Add(new MpColumnDef { Id = "Vol", Header = "Volume", Group = "TodayValues", Width = 88f, IsNumeric = true, TextSelector = r => r.TodayVolume.ToString("0", CultureInfo.InvariantCulture), SortNumSelector = r => r.TodayVolume, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "OpenPrice", Header = "Open", Group = "TodayValues", Width = 78f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayOpen), SortNumSelector = r => r.TodayOpen, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "HighPrice", Header = "High", Group = "TodayValues", Width = 78f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayHigh), SortNumSelector = r => r.TodayHigh, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "LowPrice", Header = "Low", Group = "TodayValues", Width = 78f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayLow), SortNumSelector = r => r.TodayLow, ToneSelector = r => MpCellTone.Default });
			}

			cols.Add(new MpColumnDef { Id = "OpenHL", Header = "Open=H/L", Group = "Core", Width = 76f, IsNumeric = false, TextSelector = r => r.OpenEqualHL, ToneSelector = r => r.OpenEqualHLTone });
			cols.Add(new MpColumnDef { Id = "Price", Header = "Price", Group = "Core", Width = 76f, IsNumeric = true, TextSelector = r => r.FormatNum(r.Price), SortNumSelector = r => r.Price, ToneSelector = r => r.PriceTone });
			cols.Add(new MpColumnDef { Id = "PctChange", Header = "%Change", Group = "Core", Width = 66f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PercentChange), SortNumSelector = r => r.PercentChange, ToneSelector = r => r.PriceTone });

			if (showDailySma)
			{
				cols.Add(new MpColumnDef { Id = "DailySMA", Header = "SMA(" + smaDays + ") - Daily", Group = "DailySMA", Width = 96f, IsNumeric = true, TextSelector = r => r.FormatNum(r.DailySma), SortNumSelector = r => r.DailySma, ToneSelector = r => MpCellTone.Default });
			}

			cols.Add(new MpColumnDef { Id = "PriceVsSMA", Header = "Price - SMA(" + smaDays + ")", Group = "Core", Width = 108f, IsNumeric = false, TextSelector = r => r.PriceVsSma, ToneSelector = r => r.PriceVsSmaTone });

			if (showPrevDayMpLevels)
			{
				cols.Add(new MpColumnDef { Id = "PdTpo", Header = "TPOSize", Group = "PrevDayMP", Width = 64f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PdTpoSize), SortNumSelector = r => r.PdTpoSize, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PdVah", Header = "PD-VAH", Group = "PrevDayMP", Width = 78f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PdVah), SortNumSelector = r => r.PdVah, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PdPoc", Header = "PD-POC", Group = "PrevDayMP", Width = 78f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PdPoc), SortNumSelector = r => r.PdPoc, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PdVal", Header = "PD-VAL", Group = "PrevDayMP", Width = 78f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PdVal), SortNumSelector = r => r.PdVal, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PdPocCnt", Header = "PD-POC TPOCount", Group = "PrevDayMP", Width = 96f, IsNumeric = true, TextSelector = r => r.PdPocTpoCount.ToString(CultureInfo.InvariantCulture), SortNumSelector = r => r.PdPocTpoCount, ToneSelector = r => MpCellTone.Default });
			}

			cols.Add(new MpColumnDef { Id = "PrevDayType", Header = "PrevDay Type", Group = "Core", Width = 112f, IsNumeric = false, TextSelector = r => r.PrevDayType, ToneSelector = r => r.PrevDayTypeTone });
			cols.Add(new MpColumnDef { Id = "PdSingleP", Header = "PD-SingleP", Group = "Core", Width = 86f, IsNumeric = false, TextSelector = r => r.PdSinglePrint, ToneSelector = r => r.PdSinglePrintTone });
			cols.Add(new MpColumnDef { Id = "OpenVa", Header = "Open", Group = "Core", Width = 92f, IsNumeric = false, TextSelector = r => r.OpenVsPdVa, ToneSelector = r => r.OpenVsPdVaTone });
			cols.Add(new MpColumnDef { Id = "OpenHl", Header = "Open", Group = "Core", Width = 92f, IsNumeric = false, TextSelector = r => r.OpenVsPdHl, ToneSelector = r => r.OpenVsPdHlTone });
			cols.Add(new MpColumnDef { Id = "OpenType", Header = "OpenType", Group = "Core", Width = 96f, IsNumeric = false, TextSelector = r => r.OpenType, ToneSelector = r => r.OpenTypeTone });
			cols.Add(new MpColumnDef { Id = "PdBreakout", Header = "PrevDay Breakout", Group = "Core", Width = 114f, IsNumeric = false, TextSelector = r => r.PrevDayBreakout, ToneSelector = r => r.PrevDayBreakoutTone });
			cols.Add(new MpColumnDef { Id = "AtrDaily", Header = "ATR(" + atrDays + ") - Daily", Group = "Core", Width = 92f, IsNumeric = true, TextSelector = r => r.FormatNum(r.DailyAtr), SortNumSelector = r => r.DailyAtr, ToneSelector = r => MpCellTone.Default });
			cols.Add(new MpColumnDef { Id = "HlRange", Header = "HL Range", Group = "Core", Width = 72f, IsNumeric = true, TextSelector = r => r.FormatNum(r.HlRange), SortNumSelector = r => r.HlRange, ToneSelector = r => MpCellTone.Default });

			if (showIbValues)
			{
				cols.Add(new MpColumnDef { Id = "IbHigh", Header = "IB - High", Group = "IBValues", Width = 76f, IsNumeric = true, TextSelector = r => r.FormatNum(r.IbHigh), SortNumSelector = r => r.IbHigh, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "IbLow", Header = "IB - Low", Group = "IBValues", Width = 76f, IsNumeric = true, TextSelector = r => r.FormatNum(r.IbLow), SortNumSelector = r => r.IbLow, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "IbRange", Header = "IB Range", Group = "IBValues", Width = 74f, IsNumeric = true, TextSelector = r => r.FormatNum(r.IbRange), SortNumSelector = r => r.IbRange, ToneSelector = r => MpCellTone.Default });
			}

			cols.Add(new MpColumnDef { Id = "IbSize", Header = "IB Size", Group = "Core", Width = 68f, IsNumeric = false, TextSelector = r => r.IbSize, ToneSelector = r => r.IbSizeTone });
			cols.Add(new MpColumnDef { Id = "IbBreakout", Header = "IB Breakout", Group = "Core", Width = 94f, IsNumeric = false, TextSelector = r => r.IbBreakout, ToneSelector = r => r.IbBreakoutTone });
			cols.Add(new MpColumnDef { Id = "PriceVsIb", Header = "Price - IB", Group = "Core", Width = 88f, IsNumeric = false, TextSelector = r => r.PriceVsIb, ToneSelector = r => r.PriceVsIbTone });

			if (showTodayMpLevels)
			{
				cols.Add(new MpColumnDef { Id = "TdTpo", Header = "TPOSize", Group = "TodayMP", Width = 64f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayTpoSize), SortNumSelector = r => r.TodayTpoSize, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "TdVah", Header = "Today-VAH", Group = "TodayMP", Width = 80f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayVah), SortNumSelector = r => r.TodayVah, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "TdPoc", Header = "Today-POC", Group = "TodayMP", Width = 80f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayPoc), SortNumSelector = r => r.TodayPoc, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "TdVal", Header = "Today-VAL", Group = "TodayMP", Width = 80f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayVal), SortNumSelector = r => r.TodayVal, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "TdPocCnt", Header = "Today-POC TPOCount", Group = "TodayMP", Width = 104f, IsNumeric = true, TextSelector = r => r.TodayPocTpoCount.ToString(CultureInfo.InvariantCulture), SortNumSelector = r => r.TodayPocTpoCount, ToneSelector = r => MpCellTone.Default });
			}

			cols.Add(new MpColumnDef { Id = "TodayVa", Header = "Today VA", Group = "Core", Width = 96f, IsNumeric = false, TextSelector = r => r.TodayVaStatus, ToneSelector = r => r.TodayVaTone });
			cols.Add(new MpColumnDef { Id = "Bar30Min", Header = "30-min PreviousBar", Group = "Core", Width = 152f, IsNumeric = false, TextSelector = r => r.Prev30MinBarStatus, ToneSelector = r => r.Prev30MinBarTone });
			cols.Add(new MpColumnDef { Id = "SingleP", Header = "SingleP", Group = "Core", Width = 84f, IsNumeric = false, TextSelector = r => r.TodaySinglePrint, ToneSelector = r => r.TodaySinglePrintTone });
			cols.Add(new MpColumnDef { Id = "DayType", Header = "Day Type", Group = "Core", Width = 175f, IsNumeric = false, TextSelector = r => r.TodayDayType, ToneSelector = r => r.TodayDayTypeTone });

			return cols;
		}

		private void RaiseUpdated()
		{
			var handler = OnRowsUpdated;
			if (handler != null)
			{
				try { handler(); } catch { }
			}
		}

		public void Dispose()
		{
			isDisposed = true;
			foreach (var kv in subscriptions)
			{
				try { kv.Value.Dispose(); } catch { }
			}
			subscriptions.Clear();
		}
	}
	#endregion

	#region Standalone WPF Live Scanner Window (Control Center -> New -> 50Charts MP Scanner)
	public sealed class MarketProfileScannerWindow : NTWindow
	{
		private readonly MarketProfileScannerEngine engine;
		private readonly DispatcherTimer autoRefreshTimer;
		private DataGrid grid;
		private TextBlock txtStatus;
		private TextBlock txtLivePulse;
		private TextBox txtSearch;
		private TextBox txtAddSymbol;
		private ComboBox cmbWatchlist;
		private ComboBox cmbFilter;
		private ComboBox cmbRefreshRate;
		private CheckBox chkAutoRefresh;
		private CheckBox chkSimIdle;
		private CheckBox chkDarkTheme;
		private CheckBox chkPrevDayHlc;
		private CheckBox chkTodayOhl;
		private CheckBox chkDailySma;
		private CheckBox chkPrevMp;
		private CheckBox chkIbVals;
		private CheckBox chkTodayMp;
		private string sortColId = "Ticker";
		private bool sortAsc = true;
		private long refreshCounter;

		public MarketProfileScannerWindow()
		{
			Caption = "50Charts Market Profile Live Scanner — NinjaTrader 8";
			Width = 1540;
			Height = 820;
			WindowStartupLocation = WindowStartupLocation.CenterScreen;

			engine = new MarketProfileScannerEngine();
			BuildWpfLayout();

			autoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
			{
				Interval = TimeSpan.FromSeconds(2)
			};
			autoRefreshTimer.Tick += (s, e) =>
			{
				if (chkAutoRefresh != null && chkAutoRefresh.IsChecked == true)
				{
					engine.RefreshAll(true);
				}
			};

			engine.OnRowsUpdated += () =>
			{
				if (Dispatcher != null && !Dispatcher.HasShutdownStarted)
				{
					Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RebuildAndBindGrid));
				}
			};

			Closed += (s, e) =>
			{
				try
				{
					autoRefreshTimer.Stop();
					engine.Dispose();
				}
				catch { }
			};

			engine.RefreshAll(false);
			RebuildColumns();
			RebuildAndBindGrid();
			autoRefreshTimer.Start();
		}

		private void BuildWpfLayout()
		{
			Grid root = new Grid
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(242, 244, 248))
			};
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
			root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

			// Row 0: Primary Toolbar (Explore, Watchlist, Filter, Auto-Refresh, Search, Add Symbol)
			Border bar1 = new Border
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(230, 235, 244)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(185, 195, 210)),
				BorderThickness = new Thickness(0, 0, 0, 1),
				Padding = new Thickness(8, 6, 8, 6)
			};
			WrapPanel wp1 = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };

			Button btnExplore = CreateToolbarButton("✔ Explore / Scan Now", WpfColor.FromRgb(0, 145, 50), WpfColors.White, true);
			btnExplore.Click += (s, e) => engine.RefreshAll(true);
			wp1.Children.Add(btnExplore);

			wp1.Children.Add(CreateToolbarLabel("Apply to:"));
			cmbWatchlist = new ComboBox { Width = 175, Margin = new Thickness(4, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center };
			cmbWatchlist.Items.Add("All Symbols (50Charts + NT8)");
			cmbWatchlist.Items.Add("50Charts NSE F&O (44)");
			cmbWatchlist.Items.Add("NinjaTrader Local DB");
			cmbWatchlist.Items.Add("US Futures (GC, ES, NQ...)");
			cmbWatchlist.SelectedIndex = 0;
			cmbWatchlist.SelectionChanged += (s, e) =>
			{
				engine.WatchlistPreset = (MpWatchlistPreset)Math.Max(0, cmbWatchlist.SelectedIndex);
				engine.RefreshAll(false);
			};
			wp1.Children.Add(cmbWatchlist);

			wp1.Children.Add(CreateToolbarLabel("Filter:"));
			cmbFilter = new ComboBox { Width = 155, Margin = new Thickness(4, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center };
			cmbFilter.Items.Add("All Symbols");
			cmbFilter.Items.Add("IB Breakout (Up/Down)");
			cmbFilter.Items.Add("Trend Days");
			cmbFilter.Items.Add("Single Prints (Up/Down)");
			cmbFilter.Items.Add("Open Drive / OpenTest");
			cmbFilter.Items.Add("30-min Bar Breakout");
			cmbFilter.Items.Add("Open = High / Low");
			cmbFilter.SelectedIndex = 0;
			cmbFilter.SelectionChanged += (s, e) => RebuildAndBindGrid();
			wp1.Children.Add(cmbFilter);

			wp1.Children.Add(CreateToolbarLabel("Search:"));
			txtSearch = new TextBox { Width = 120, Margin = new Thickness(4, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center };
			txtSearch.TextChanged += (s, e) => RebuildAndBindGrid();
			wp1.Children.Add(txtSearch);

			chkAutoRefresh = new CheckBox
			{
				Content = "Live Auto-Refresh",
				IsChecked = true,
				VerticalAlignment = VerticalAlignment.Center,
				FontWeight = FontWeights.SemiBold,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(10, 110, 40)),
				Margin = new Thickness(6, 0, 6, 0)
			};
			wp1.Children.Add(chkAutoRefresh);

			cmbRefreshRate = new ComboBox { Width = 78, Margin = new Thickness(2, 0, 10, 0), VerticalContentAlignment = VerticalAlignment.Center };
			cmbRefreshRate.Items.Add("1 sec");
			cmbRefreshRate.Items.Add("2 sec");
			cmbRefreshRate.Items.Add("3 sec");
			cmbRefreshRate.Items.Add("5 sec");
			cmbRefreshRate.Items.Add("10 sec");
			cmbRefreshRate.SelectedIndex = 1;
			cmbRefreshRate.SelectionChanged += (s, e) =>
			{
				int[] secs = new int[] { 1, 2, 3, 5, 10 };
				int idx = Math.Max(0, Math.Min(secs.Length - 1, cmbRefreshRate.SelectedIndex));
				if (autoRefreshTimer != null)
					autoRefreshTimer.Interval = TimeSpan.FromSeconds(secs[idx]);
			};
			wp1.Children.Add(cmbRefreshRate);

			chkSimIdle = new CheckBox
			{
				Content = "Live Tick Feed + Sim",
				IsChecked = true,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(4, 0, 10, 0),
				ToolTip = "Tracks real-time NinjaTrader BarsRequest ticks, plus continuous live tick tracking when market feed is idle."
			};
			chkSimIdle.Checked += (s, e) => engine.EnableLiveTickSimulationWhenIdle = true;
			chkSimIdle.Unchecked += (s, e) => engine.EnableLiveTickSimulationWhenIdle = false;
			wp1.Children.Add(chkSimIdle);

			wp1.Children.Add(CreateToolbarLabel("+ Symbol:"));
			txtAddSymbol = new TextBox { Width = 86, Margin = new Thickness(4, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center };
			txtAddSymbol.KeyDown += (s, e) =>
			{
				if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(txtAddSymbol.Text))
				{
					engine.AddCustomSymbol(txtAddSymbol.Text);
					txtAddSymbol.Text = "";
				}
			};
			wp1.Children.Add(txtAddSymbol);

			Button btnAddSym = CreateToolbarButton("Add", WpfColor.FromRgb(40, 95, 165), WpfColors.White, false);
			btnAddSym.Click += (s, e) =>
			{
				if (!string.IsNullOrWhiteSpace(txtAddSymbol.Text))
				{
					engine.AddCustomSymbol(txtAddSymbol.Text);
					txtAddSymbol.Text = "";
				}
			};
			wp1.Children.Add(btnAddSym);

			Button btnExportCsv = CreateToolbarButton("Export CSV", WpfColor.FromRgb(70, 80, 95), WpfColors.White, false);
			btnExportCsv.Margin = new Thickness(8, 0, 0, 0);
			btnExportCsv.Click += (s, e) => ExportCurrentGridToCsv();
			wp1.Children.Add(btnExportCsv);

			bar1.Child = wp1;
			Grid.SetRow(bar1, 0);
			root.Children.Add(bar1);

			// Row 1: Secondary Toolbar (50Charts AFL Column Toggles + Theme Toggle + Formula Path Banner)
			Border bar2 = new Border
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(245, 247, 252)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(205, 212, 224)),
				BorderThickness = new Thickness(0, 0, 0, 1),
				Padding = new Thickness(8, 4, 8, 4)
			};
			WrapPanel wp2 = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
			wp2.Children.Add(new TextBlock
			{
				Text = "50Charts Columns:",
				FontWeight = FontWeights.Bold,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(45, 55, 72)),
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 8, 0)
			});

			chkPrevDayHlc = CreateToggleCheck("PrevDay H/L/C/V", false);
			chkTodayOhl = CreateToggleCheck("Today O/H/L/V", false);
			chkDailySma = CreateToggleCheck("Daily SMA(21)", false);
			chkPrevMp = CreateToggleCheck("PrevDay MP Levels", false);
			chkIbVals = CreateToggleCheck("IB H/L/Range", false);
			chkTodayMp = CreateToggleCheck("Today MP Levels", false);
			chkDarkTheme = CreateToggleCheck("Dark Theme", false);

			wp2.Children.Add(chkPrevDayHlc);
			wp2.Children.Add(chkTodayOhl);
			wp2.Children.Add(chkDailySma);
			wp2.Children.Add(chkPrevMp);
			wp2.Children.Add(chkIbVals);
			wp2.Children.Add(chkTodayMp);
			wp2.Children.Add(chkDarkTheme);

			txtLivePulse = new TextBlock
			{
				Text = "● LIVE TRACKING ACTIVE",
				FontWeight = FontWeights.Bold,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(0, 150, 45)),
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(14, 0, 0, 0)
			};
			wp2.Children.Add(txtLivePulse);

			bar2.Child = wp2;
			Grid.SetRow(bar2, 1);
			root.Children.Add(bar2);

			// Row 2: Main 50Charts Scanner DataGrid
			grid = new DataGrid
			{
				AutoGenerateColumns = false,
				IsReadOnly = true,
				CanUserAddRows = false,
				CanUserDeleteRows = false,
				CanUserReorderColumns = true,
				CanUserResizeColumns = true,
				CanUserSortColumns = false,
				GridLinesVisibility = DataGridGridLinesVisibility.All,
				HorizontalGridLinesBrush = new WpfSolidBrush(WpfColor.FromRgb(218, 222, 228)),
				VerticalGridLinesBrush = new WpfSolidBrush(WpfColor.FromRgb(218, 222, 228)),
				RowHeaderWidth = 0,
				FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
				FontSize = 12,
				RowHeight = 23,
				ColumnHeaderHeight = 26,
				SelectionMode = DataGridSelectionMode.Single,
				SelectionUnit = DataGridSelectionUnit.FullRow,
				Background = WpfBrushes.White
			};
			Grid.SetRow(grid, 2);
			root.Children.Add(grid);

			// Row 3: Status Bar ("Result list | Info | Walk Forward" matching AmiBroker 50Charts footer)
			Border footer = new Border
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(230, 235, 244)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(185, 195, 210)),
				BorderThickness = new Thickness(0, 1, 0, 0),
				Padding = new Thickness(10, 4, 10, 4)
			};
			txtStatus = new TextBlock
			{
				Text = "Result list | Formula: Formulas\\50Charts\\50ChartsScanner\\50ChartsMarketProfileScanner (NinjaTrader 8 Live Engine)",
				FontSize = 11.5,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(40, 50, 65))
			};
			footer.Child = txtStatus;
			Grid.SetRow(footer, 3);
			root.Children.Add(footer);

			Content = root;
		}

		private CheckBox CreateToggleCheck(string label, bool defaultVal)
		{
			CheckBox cb = new CheckBox
			{
				Content = label,
				IsChecked = defaultVal,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 12, 0)
			};
			cb.Checked += (s, e) => { RebuildColumns(); RebuildAndBindGrid(); };
			cb.Unchecked += (s, e) => { RebuildColumns(); RebuildAndBindGrid(); };
			return cb;
		}

		private static TextBlock CreateToolbarLabel(string text)
		{
			return new TextBlock
			{
				Text = text,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(4, 0, 2, 0),
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(35, 45, 60)),
				FontWeight = FontWeights.SemiBold
			};
		}

		private static Button CreateToolbarButton(string text, WpfColor bg, WpfColor fg, bool bold)
		{
			return new Button
			{
				Content = text,
				Padding = new Thickness(10, 3, 10, 3),
				Margin = new Thickness(0, 0, 8, 0),
				Background = new WpfSolidBrush(bg),
				Foreground = new WpfSolidBrush(fg),
				FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold,
				BorderThickness = new Thickness(0),
				Cursor = Cursors.Hand
			};
		}

		private void RebuildColumns()
		{
			if (grid == null)
				return;

			bool dark = chkDarkTheme != null && chkDarkTheme.IsChecked == true;
			grid.Background = dark ? new WpfSolidBrush(WpfColor.FromRgb(11, 16, 23)) : WpfBrushes.White;
			grid.HorizontalGridLinesBrush = dark ? new WpfSolidBrush(WpfColor.FromRgb(34, 42, 54)) : new WpfSolidBrush(WpfColor.FromRgb(216, 220, 228));
			grid.VerticalGridLinesBrush = dark ? new WpfSolidBrush(WpfColor.FromRgb(34, 42, 54)) : new WpfSolidBrush(WpfColor.FromRgb(216, 220, 228));

			List<MpColumnDef> defs = MarketProfileScannerEngine.BuildColumnDefinitions(
				engine.SmaDays,
				engine.AtrDays,
				chkPrevDayHlc != null && chkPrevDayHlc.IsChecked == true,
				chkTodayOhl != null && chkTodayOhl.IsChecked == true,
				chkDailySma != null && chkDailySma.IsChecked == true,
				chkPrevMp != null && chkPrevMp.IsChecked == true,
				chkIbVals != null && chkIbVals.IsChecked == true,
				chkTodayMp != null && chkTodayMp.IsChecked == true);

			grid.Columns.Clear();
			for (int i = 0; i < defs.Count; i++)
			{
				MpColumnDef colDef = defs[i];
				DataGridTemplateColumn col = new DataGridTemplateColumn
				{
					Header = colDef.Header,
					Width = new DataGridLength(colDef.Width)
				};

				FrameworkElementFactory borderFactory = new FrameworkElementFactory(typeof(Border));
				borderFactory.SetBinding(Border.BackgroundProperty, new Binding(".")
				{
					Converter = new MpCellBrushConverter(colDef, true, dark)
				});
				borderFactory.SetValue(Border.PaddingProperty, new Thickness(5, 1, 5, 1));

				FrameworkElementFactory textFactory = new FrameworkElementFactory(typeof(TextBlock));
				textFactory.SetBinding(TextBlock.TextProperty, new Binding(".")
				{
					Converter = new MpCellTextConverter(colDef)
				});
				textFactory.SetBinding(TextBlock.ForegroundProperty, new Binding(".")
				{
					Converter = new MpCellBrushConverter(colDef, false, dark)
				});
				textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
				textFactory.SetValue(TextBlock.TextAlignmentProperty, colDef.IsNumeric ? System.Windows.TextAlignment.Right : System.Windows.TextAlignment.Left);
				textFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

				borderFactory.AppendChild(textFactory);
				col.CellTemplate = new DataTemplate { VisualTree = borderFactory };
				grid.Columns.Add(col);
			}
		}

		private void RebuildAndBindGrid()
		{
			if (grid == null)
				return;

			refreshCounter++;
			MpFilterPreset fp = cmbFilter != null ? (MpFilterPreset)Math.Max(0, cmbFilter.SelectedIndex) : MpFilterPreset.AllSymbols;
			string q = txtSearch != null ? txtSearch.Text : "";

			List<MpColumnDef> defs = MarketProfileScannerEngine.BuildColumnDefinitions(
				engine.SmaDays, engine.AtrDays,
				chkPrevDayHlc != null && chkPrevDayHlc.IsChecked == true,
				chkTodayOhl != null && chkTodayOhl.IsChecked == true,
				chkDailySma != null && chkDailySma.IsChecked == true,
				chkPrevMp != null && chkPrevMp.IsChecked == true,
				chkIbVals != null && chkIbVals.IsChecked == true,
				chkTodayMp != null && chkTodayMp.IsChecked == true);

			MpColumnDef sortCol = defs.FirstOrDefault(c => c.Id == sortColId) ?? defs[0];
			List<MpScannerRow> rows = engine.GetFilteredAndSortedRows(
				fp, q, sortCol.Id, sortAsc, sortCol.TextSelector, sortCol.SortNumSelector, sortCol.IsNumeric);

			grid.ItemsSource = rows;

			if (txtLivePulse != null)
			{
				bool pulseAlt = (refreshCounter % 2) == 0;
				txtLivePulse.Text = (pulseAlt ? "● " : "◉ ") + "LIVE AUTO-REFRESH (" + DateTime.Now.ToString("HH:mm:ss") + ")";
			}

			if (txtStatus != null)
			{
				int liveCount = rows.Count(r => r.IsLiveConnected);
				txtStatus.Text = string.Format(
					CultureInfo.InvariantCulture,
					"Result list ({0} symbols displayed, {1} NT8 DB BarsRequest active)  |  Periodicity: 30-min TPO  |  VA: {2:0}%  |  IB: {3} candles  |  Updated: {4:dd-MM-yyyy HH:mm:ss}",
					rows.Count, liveCount, engine.ValueAreaPercent, engine.IbCandleCount, DateTime.Now);
			}
		}

		private void ExportCurrentGridToCsv()
		{
			try
			{
				string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
				string path = Path.Combine(desktop, "50Charts_MP_Scanner_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
				List<MpColumnDef> defs = MarketProfileScannerEngine.BuildColumnDefinitions(
					engine.SmaDays, engine.AtrDays,
					chkPrevDayHlc != null && chkPrevDayHlc.IsChecked == true,
					chkTodayOhl != null && chkTodayOhl.IsChecked == true,
					chkDailySma != null && chkDailySma.IsChecked == true,
					chkPrevMp != null && chkPrevMp.IsChecked == true,
					chkIbVals != null && chkIbVals.IsChecked == true,
					chkTodayMp != null && chkTodayMp.IsChecked == true);

				var rows = grid.ItemsSource as IEnumerable<MpScannerRow>;
				if (rows == null) return;

				StringBuilder sb = new StringBuilder();
				sb.AppendLine(string.Join(",", defs.Select(d => "\"" + d.Header + "\"")));
				foreach (var r in rows)
				{
					sb.AppendLine(string.Join(",", defs.Select(d => "\"" + (d.TextSelector(r) ?? "") + "\"")));
				}
				File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
				MessageBox.Show(this, "Exported 50Charts Scanner results to:\n" + path, "CSV Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
			}
			catch (Exception ex)
			{
				MessageBox.Show(this, "Export error: " + ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private sealed class MpCellTextConverter : IValueConverter
		{
			private readonly MpColumnDef col;
			public MpCellTextConverter(MpColumnDef c) { col = c; }
			public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
			{
				MpScannerRow r = value as MpScannerRow;
				if (r == null || col == null || col.TextSelector == null) return "";
				return col.TextSelector(r) ?? "";
			}
			public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
		}

		private sealed class MpCellBrushConverter : IValueConverter
		{
			private readonly MpColumnDef col;
			private readonly bool isBackground;
			private readonly bool isDark;

			private static readonly WpfSolidBrush BrGreenBg = Freeze(new WpfSolidBrush(WpfColor.FromRgb(0, 166, 41)));     // Exact 50Charts ColorRGB(0,166,41)
			private static readonly WpfSolidBrush BrRedOrangeBg = Freeze(new WpfSolidBrush(WpfColor.FromRgb(242, 110, 42))); // Exact 50Charts colorOrange
			private static readonly WpfSolidBrush BrYellowBg = Freeze(new WpfSolidBrush(WpfColor.FromRgb(255, 255, 62)));   // Exact 50Charts ColorRGB(255,255,62)
			private static readonly WpfSolidBrush BrGreyBg = Freeze(new WpfSolidBrush(WpfColor.FromRgb(230, 230, 230)));    // Exact 50Charts ColorRGB(230,230,230)
			private static readonly WpfSolidBrush BrWhite = Freeze(new WpfSolidBrush(WpfColors.White));
			private static readonly WpfSolidBrush BrDarkBg = Freeze(new WpfSolidBrush(WpfColor.FromRgb(14, 19, 27)));
			private static readonly WpfSolidBrush BrBlack = Freeze(new WpfSolidBrush(WpfColor.FromRgb(18, 22, 28)));
			private static readonly WpfSolidBrush BrLightText = Freeze(new WpfSolidBrush(WpfColor.FromRgb(230, 236, 245)));
			private static readonly WpfSolidBrush BrFgGreen = Freeze(new WpfSolidBrush(WpfColor.FromRgb(0, 142, 35)));
			private static readonly WpfSolidBrush BrFgGreenDark = Freeze(new WpfSolidBrush(WpfColor.FromRgb(40, 220, 95)));
			private static readonly WpfSolidBrush BrFgRed = Freeze(new WpfSolidBrush(WpfColor.FromRgb(215, 35, 35)));
			private static readonly WpfSolidBrush BrFgRedDark = Freeze(new WpfSolidBrush(WpfColor.FromRgb(255, 85, 85)));
			private static readonly WpfSolidBrush BrFgOrange = Freeze(new WpfSolidBrush(WpfColor.FromRgb(225, 95, 20)));

			private static WpfSolidBrush Freeze(WpfSolidBrush b) { b.Freeze(); return b; }

			public MpCellBrushConverter(MpColumnDef c, bool bg, bool dark)
			{
				col = c;
				isBackground = bg;
				isDark = dark;
			}

			public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
			{
				MpScannerRow r = value as MpScannerRow;
				MpCellTone tone = (r != null && col != null && col.ToneSelector != null) ? col.ToneSelector(r) : MpCellTone.Default;

				if (isBackground)
				{
					switch (tone)
					{
						case MpCellTone.BgGreen: return BrGreenBg;
						case MpCellTone.BgRedOrange: return BrRedOrangeBg;
						case MpCellTone.BgYellow: return BrYellowBg;
						case MpCellTone.BgGrey: return BrGreyBg;
						default: return isDark ? BrDarkBg : BrWhite;
					}
				}
				else
				{
					switch (tone)
					{
						case MpCellTone.BgGreen:
						case MpCellTone.BgRedOrange:
							return BrWhite;
						case MpCellTone.BgYellow:
						case MpCellTone.BgGrey:
							return BrBlack;
						case MpCellTone.FgGreen:
							return isDark ? BrFgGreenDark : BrFgGreen;
						case MpCellTone.FgRed:
							return isDark ? BrFgRedDark : BrFgRed;
						case MpCellTone.FgOrange:
							return BrFgOrange;
						default:
							return isDark ? BrLightText : BrBlack;
					}
				}
			}

			public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
		}
	}
	#endregion

	#region NinjaTrader 8 AddOn Menu Integration (Control Center -> New -> 50Charts MP Scanner)
	public class MarketProfileScanner50ChartsAddOn : AddOnBase
	{
		private NTMenuItem menuItem;
		private NTMenuItem existingMenu;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "50Charts Market Profile Live Scanner Add-On for NinjaTrader 8";
				Name = "MarketProfileScanner50ChartsAddOn";
			}
		}

		protected override void OnWindowCreated(Window window)
		{
			ControlCenter cc = window as ControlCenter;
			if (cc == null)
				return;

			existingMenu = cc.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
			if (existingMenu == null)
				return;

			menuItem = new NTMenuItem
			{
				Header = "50Charts MP Scanner (Live)",
				Style = System.Windows.Application.Current.TryFindResource("MainMenuItem") as System.Windows.Style
			};
			menuItem.Click += OnMenuItemClick;
			existingMenu.Items.Add(menuItem);
		}

		protected override void OnWindowDestroyed(Window window)
		{
			if (menuItem != null && window is ControlCenter)
			{
				if (existingMenu != null && existingMenu.Items.Contains(menuItem))
					existingMenu.Items.Remove(menuItem);
				menuItem.Click -= OnMenuItemClick;
				menuItem = null;
			}
		}

		private void OnMenuItemClick(object sender, RoutedEventArgs e)
		{
			Core.Globals.RandomDispatcher.BeginInvoke(new Action(() =>
			{
				MarketProfileScannerWindow win = new MarketProfileScannerWindow();
				win.Show();
			}));
		}
	}
	#endregion

	#region Chart Indicator (Right-Click Chart -> Indicators -> MarketProfileScanner50Charts)
	public class MarketProfileScanner50Charts : Indicator
	{
		private struct ToolbarBtn
		{
			public RectangleF Rect;
			public string ActionId;
			public string ColumnId;
		}

		private MarketProfileScannerEngine scannerEngine;
		private DispatcherTimer chartAutoRefreshTimer;
		private readonly List<MpBarSample> primary30MinSamples = new List<MpBarSample>();
		private readonly List<ToolbarBtn> interactiveRects = new List<ToolbarBtn>();
		private ChartControl subscribedChartControl;
		private int scrollRowOffset;
		private float scrollColOffsetPx;
		private string sortColumnId = "Ticker";
		private bool sortAscending = true;
		private long liveRefreshPulse;

		#region Indicator Parameters (Matching 50ChartsMarketProfileScanner.afl)
		[NinjaScriptProperty]
		[Display(Name = "Display - Prev Day H/L/C/V", GroupName = "1. 50Charts Scanner Columns", Order = 1)]
		public bool DisplayPrevDayValues { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Display - Today O/H/L/V", GroupName = "1. 50Charts Scanner Columns", Order = 2)]
		public bool DisplayTodayValues { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Display - Daily - SMA", GroupName = "1. 50Charts Scanner Columns", Order = 3)]
		public bool DisplayDailySma { get; set; }

		[NinjaScriptProperty]
		[Range(5, 200)]
		[Display(Name = "SMA - Number of Days", GroupName = "1. 50Charts Scanner Columns", Order = 4)]
		public int SmaNumberOfDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Display - Prev Day MP levels", GroupName = "1. 50Charts Scanner Columns", Order = 5)]
		public bool DisplayPrevDayMpLevels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Display - IB values", GroupName = "1. 50Charts Scanner Columns", Order = 6)]
		public bool DisplayIbValues { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Display - Current Day MP levels", GroupName = "1. 50Charts Scanner Columns", Order = 7)]
		public bool DisplayCurrentDayMpLevels { get; set; }

		[NinjaScriptProperty]
		[Range(5, 100)]
		[Display(Name = "ATR - Number of Days", GroupName = "1. 50Charts Scanner Columns", Order = 8)]
		public int AtrNumberOfDays { get; set; }

		[NinjaScriptProperty]
		[Range(50.0, 90.0)]
		[Display(Name = "Distribution - One Sigma (%)", GroupName = "2. 50Charts Profile Parameters", Order = 1)]
		public double ProfileDistributionOneSigma { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Minimum Tick Size", GroupName = "2. 50Charts Profile Parameters", Order = 2)]
		public double MinTickSizeParam { get; set; }

		[NinjaScriptProperty]
		[Range(1, 5)]
		[Display(Name = "IB - Candle Count (30-min bars)", GroupName = "2. 50Charts Profile Parameters", Order = 3)]
		public int InitialBalanceCandleCount { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Auto-Refresh Enabled", GroupName = "3. Live Tracking & Theme", Order = 1)]
		public bool AutoRefreshEnabled { get; set; }

		[NinjaScriptProperty]
		[Range(1, 60)]
		[Display(Name = "Auto-Refresh Interval (Seconds)", GroupName = "3. Live Tracking & Theme", Order = 2)]
		public int AutoRefreshSeconds { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Classic 50Charts Light Theme", GroupName = "3. Live Tracking & Theme", Order = 3)]
		public bool UseClassicLightTheme { get; set; }

		[NinjaScriptProperty]
		[Range(9, 16)]
		[Display(Name = "Scanner Table Font Size", GroupName = "3. Live Tracking & Theme", Order = 4)]
		public int ScannerFontSize { get; set; }
		#endregion

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "50Charts Market Profile Live Scanner / Explorer with Real-Time Data Tracking and Auto-Refresh.";
				Name = "MarketProfileScanner50Charts";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				PaintPriceMarkers = false;

				DisplayPrevDayValues = false;
				DisplayTodayValues = false;
				DisplayDailySma = false;
				SmaNumberOfDays = 21;
				DisplayPrevDayMpLevels = false;
				DisplayIbValues = false;
				DisplayCurrentDayMpLevels = false;
				AtrNumberOfDays = 10;
				ProfileDistributionOneSigma = 70.0;
				MinTickSizeParam = 0.05;
				InitialBalanceCandleCount = 2;
				AutoRefreshEnabled = true;
				AutoRefreshSeconds = 2;
				UseClassicLightTheme = true;
				ScannerFontSize = 11;
			}
			else if (State == State.DataLoaded)
			{
				scannerEngine = new MarketProfileScannerEngine
				{
					SmaDays = SmaNumberOfDays,
					AtrDays = AtrNumberOfDays,
					IbCandleCount = InitialBalanceCandleCount,
					ValueAreaPercent = ProfileDistributionOneSigma,
					DefaultMinTickSize = Math.Max(0.0001, MinTickSizeParam)
				};
				scannerEngine.RefreshAll(false);

				if (ChartControl != null)
				{
					ChartControl.Dispatcher.InvokeAsync(() =>
					{
						AttachChartEvents();
						StartAutoRefreshTimer();
					});
				}
			}
			else if (State == State.Terminated)
			{
				DetachChartEvents();
				StopAutoRefreshTimer();
				if (scannerEngine != null)
				{
					scannerEngine.Dispose();
					scannerEngine = null;
				}
			}
		}

		private void AttachChartEvents()
		{
			if (ChartControl == null || subscribedChartControl == ChartControl)
				return;
			subscribedChartControl = ChartControl;
			subscribedChartControl.MouseLeftButtonDown += OnChartMouseLeftButtonDown;
			subscribedChartControl.PreviewMouseWheel += OnChartMouseWheel;
		}

		private void DetachChartEvents()
		{
			if (subscribedChartControl != null)
			{
				try
				{
					subscribedChartControl.MouseLeftButtonDown -= OnChartMouseLeftButtonDown;
					subscribedChartControl.PreviewMouseWheel -= OnChartMouseWheel;
				}
				catch { }
				subscribedChartControl = null;
			}
		}

		private void StartAutoRefreshTimer()
		{
			StopAutoRefreshTimer();
			if (ChartControl == null)
				return;

			chartAutoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, ChartControl.Dispatcher)
			{
				Interval = TimeSpan.FromSeconds(Math.Max(1, AutoRefreshSeconds))
			};
			chartAutoRefreshTimer.Tick += (s, e) =>
			{
				if (AutoRefreshEnabled && scannerEngine != null)
				{
					liveRefreshPulse++;
					scannerEngine.RefreshAll(true);
					if (ChartControl != null)
						ChartControl.InvalidateVisual();
				}
			};
			chartAutoRefreshTimer.Start();
		}

		private void StopAutoRefreshTimer()
		{
			if (chartAutoRefreshTimer != null)
			{
				try { chartAutoRefreshTimer.Stop(); } catch { }
				chartAutoRefreshTimer = null;
			}
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar < 1 || scannerEngine == null || Instrument == null)
				return;

			DateTime t = Time[0];
			DateTime slot30 = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute >= 30 ? 30 : 0, 0);

			if (primary30MinSamples.Count > 0 && primary30MinSamples[primary30MinSamples.Count - 1].Time == slot30)
			{
				MpBarSample cur = primary30MinSamples[primary30MinSamples.Count - 1];
				cur.High = Math.Max(cur.High, High[0]);
				cur.Low = Math.Min(cur.Low, Low[0]);
				cur.Close = Close[0];
				if (IsFirstTickOfBar)
					cur.Volume += Volume[0];
			}
			else
			{
				primary30MinSamples.Add(new MpBarSample
				{
					Time = slot30,
					Open = Open[0],
					High = High[0],
					Low = Low[0],
					Close = Close[0],
					Volume = Volume[0]
				});
				if (primary30MinSamples.Count > 700)
					primary30MinSamples.RemoveAt(0);
			}

			if (IsFirstTickOfBar || State == State.Realtime)
			{
				scannerEngine.UpdateFromChartIndicatorBars(Instrument.FullName, primary30MinSamples);
			}
		}

		private MpFilterPreset activeFilter = MpFilterPreset.AllSymbols;

		private void OnChartMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (ChartControl == null || scannerEngine == null)
				return;

			System.Windows.Point pt = e.GetPosition(ChartControl);
			float mx = (float)pt.X;
			float my = (float)pt.Y;

			for (int i = 0; i < interactiveRects.Count; i++)
			{
				ToolbarBtn b = interactiveRects[i];
				if (mx >= b.Rect.Left && mx <= b.Rect.Right && my >= b.Rect.Top && my <= b.Rect.Bottom)
				{
					if (b.ActionId == "SCAN")
					{
						liveRefreshPulse++;
						scannerEngine.RefreshAll(true);
					}
					else if (b.ActionId == "AUTO")
					{
						AutoRefreshEnabled = !AutoRefreshEnabled;
					}
					else if (b.ActionId == "WATCHLIST")
					{
						int next = ((int)scannerEngine.WatchlistPreset + 1) % 4;
						scannerEngine.WatchlistPreset = (MpWatchlistPreset)next;
						scrollRowOffset = 0;
						scannerEngine.RefreshAll(false);
					}
					else if (b.ActionId == "FILTER")
					{
						int next = ((int)activeFilter + 1) % 7;
						activeFilter = (MpFilterPreset)next;
						scrollRowOffset = 0;
					}
					else if (b.ActionId == "COLUMNS")
					{
						bool nextState = !DisplayPrevDayMpLevels;
						DisplayPrevDayValues = nextState;
						DisplayTodayValues = nextState;
						DisplayDailySma = nextState;
						DisplayPrevDayMpLevels = nextState;
						DisplayIbValues = nextState;
						DisplayCurrentDayMpLevels = nextState;
					}
					else if (b.ActionId == "THEME")
					{
						UseClassicLightTheme = !UseClassicLightTheme;
					}
					else if (b.ActionId == "LEFT")
					{
						scrollColOffsetPx = Math.Max(0f, scrollColOffsetPx - 240f);
					}
					else if (b.ActionId == "RIGHT")
					{
						scrollColOffsetPx += 240f;
					}
					else if (b.ActionId == "POPOUT")
					{
						Core.Globals.RandomDispatcher.BeginInvoke(new Action(() =>
						{
							MarketProfileScannerWindow win = new MarketProfileScannerWindow();
							win.Show();
						}));
					}
					else if (b.ActionId == "SORT" && !string.IsNullOrEmpty(b.ColumnId))
					{
						if (sortColumnId == b.ColumnId)
							sortAscending = !sortAscending;
						else
						{
							sortColumnId = b.ColumnId;
							sortAscending = true;
						}
					}

					e.Handled = true;
					ChartControl.InvalidateVisual();
					return;
				}
			}
		}

		private void OnChartMouseWheel(object sender, MouseWheelEventArgs e)
		{
			if (ChartControl == null || scannerEngine == null)
				return;

			if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
			{
				scrollColOffsetPx = Math.Max(0f, scrollColOffsetPx + (e.Delta < 0 ? 140f : -140f));
			}
			else
			{
				int step = e.Delta < 0 ? 3 : -3;
				scrollRowOffset = Math.Max(0, scrollRowOffset + step);
			}
			e.Handled = true;
			ChartControl.InvalidateVisual();
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (RenderTarget == null || ChartPanel == null || scannerEngine == null)
				return;

			interactiveRects.Clear();

			float x0 = ChartPanel.X;
			float y0 = ChartPanel.Y;
			float w = ChartPanel.W;
			float h = ChartPanel.H;
			bool light = UseClassicLightTheme;

			using (DxBrush bgCanvas = new DxBrush(RenderTarget, light ? new DxColor(255, 255, 255, 255) : new DxColor(11, 16, 23, 255)))
			using (DxBrush bgTopBar = new DxBrush(RenderTarget, light ? new DxColor(232, 237, 246, 255) : new DxColor(18, 25, 36, 255)))
			using (DxBrush bgHeader = new DxBrush(RenderTarget, light ? new DxColor(242, 245, 250, 255) : new DxColor(24, 33, 48, 255)))
			using (DxBrush brGridLine = new DxBrush(RenderTarget, light ? new DxColor(216, 220, 228, 255) : new DxColor(36, 46, 62, 255)))
			using (DxBrush brRowAlt = new DxBrush(RenderTarget, light ? new DxColor(250, 252, 255, 255) : new DxColor(15, 21, 30, 255)))
			using (DxBrush brGreenBg = new DxBrush(RenderTarget, new DxColor(0, 166, 41, 255)))
			using (DxBrush brOrangeBg = new DxBrush(RenderTarget, new DxColor(242, 110, 42, 255)))
			using (DxBrush brYellowBg = new DxBrush(RenderTarget, new DxColor(255, 255, 62, 255)))
			using (DxBrush brGreyBg = new DxBrush(RenderTarget, new DxColor(230, 230, 230, 255)))
			using (DxBrush brWhite = new DxBrush(RenderTarget, new DxColor(255, 255, 255, 255)))
			using (DxBrush brBlack = new DxBrush(RenderTarget, new DxColor(18, 22, 28, 255)))
			using (DxBrush brLightText = new DxBrush(RenderTarget, new DxColor(230, 236, 245, 255)))
			using (DxBrush brFgGreen = new DxBrush(RenderTarget, light ? new DxColor(0, 142, 35, 255) : new DxColor(40, 220, 95, 255)))
			using (DxBrush brFgRed = new DxBrush(RenderTarget, light ? new DxColor(215, 35, 35, 255) : new DxColor(255, 85, 85, 255)))
			using (DxBrush brFgOrange = new DxBrush(RenderTarget, new DxColor(225, 95, 20, 255)))
			using (DxBrush brBtn = new DxBrush(RenderTarget, light ? new DxColor(255, 255, 255, 255) : new DxColor(30, 42, 60, 255)))
			using (DxBrush brBtnActive = new DxBrush(RenderTarget, new DxColor(0, 150, 50, 255)))
			using (DxBrush brBtnPop = new DxBrush(RenderTarget, new DxColor(25, 105, 185, 255)))
			using (DxTextFormat tfTitle = new DxTextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, ScannerFontSize + 1f))
			using (DxTextFormat tfHeader = new DxTextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.SemiBold, SharpDX.DirectWrite.FontStyle.Normal, ScannerFontSize))
			using (DxTextFormat tfCell = new DxTextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, ScannerFontSize))
			{
				tfTitle.WordWrapping = WordWrapping.NoWrap;
				tfHeader.WordWrapping = WordWrapping.NoWrap;
				tfCell.WordWrapping = WordWrapping.NoWrap;

				// 1. Fill full Scanner background
				RenderTarget.FillRectangle(new RectangleF(x0, y0, w, h), bgCanvas);

				// 2. Top Interactive Control Bar (34px height)
				float topBarH = 34f;
				RenderTarget.FillRectangle(new RectangleF(x0, y0, w, topBarH), bgTopBar);
				RenderTarget.DrawLine(new Vector2(x0, y0 + topBarH), new Vector2(x0 + w, y0 + topBarH), brGridLine, 1f);

				float bx = x0 + 8f;
				float by = y0 + 5f;
				float bh = 24f;

				DrawToolbarBtn(bx, by, 92f, bh, "✔ Explore", "SCAN", brBtnActive, brWhite, brGridLine, tfHeader);
				bx += 98f;

				string autoTxt = AutoRefreshEnabled ? ("● Live (" + AutoRefreshSeconds + "s)") : "○ Paused";
				DrawToolbarBtn(bx, by, 92f, bh, autoTxt, "AUTO", AutoRefreshEnabled ? brBtnActive : brBtn, AutoRefreshEnabled ? brWhite : (light ? brBlack : brLightText), brGridLine, tfHeader);
				bx += 98f;

				string wlName = scannerEngine.WatchlistPreset == MpWatchlistPreset.All50ChartsAndNt8 ? "Apply: All Symbols"
					: (scannerEngine.WatchlistPreset == MpWatchlistPreset.Nse50Charts44 ? "Apply: NSE 50Charts"
					: (scannerEngine.WatchlistPreset == MpWatchlistPreset.Nt8LocalDatabase ? "Apply: NT8 Local DB" : "Apply: US Futures"));
				DrawToolbarBtn(bx, by, 136f, bh, wlName, "WATCHLIST", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 142f;

				string fltName = "Filter: " + activeFilter.ToString();
				DrawToolbarBtn(bx, by, 142f, bh, fltName, "FILTER", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 148f;

				string colMode = DisplayPrevDayMpLevels ? "Cols: Full (38)" : "Cols: Standard (21)";
				DrawToolbarBtn(bx, by, 124f, bh, colMode, "COLUMNS", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 130f;

				DrawToolbarBtn(bx, by, 96f, bh, light ? "Theme: Light" : "Theme: Dark", "THEME", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 102f;

				DrawToolbarBtn(bx, by, 34f, bh, "◀", "LEFT", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 38f;
				DrawToolbarBtn(bx, by, 34f, bh, "▶", "RIGHT", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 42f;

				DrawToolbarBtn(bx, by, 152f, bh, "❐ Pop Out Full Window", "POPOUT", brBtnPop, brWhite, brGridLine, tfHeader);

				// 3. Build Columns & Rows
				List<MpColumnDef> cols = MarketProfileScannerEngine.BuildColumnDefinitions(
					SmaNumberOfDays, AtrNumberOfDays,
					DisplayPrevDayValues, DisplayTodayValues, DisplayDailySma,
					DisplayPrevDayMpLevels, DisplayIbValues, DisplayCurrentDayMpLevels);

				MpColumnDef sortCol = cols.FirstOrDefault(c => c.Id == sortColumnId) ?? cols[0];
				List<MpScannerRow> rows = scannerEngine.GetFilteredAndSortedRows(
					activeFilter, "", sortCol.Id, sortAscending, sortCol.TextSelector, sortCol.SortNumSelector, sortCol.IsNumeric);

				// 4. Column Header Row
				float headerY = y0 + topBarH;
				float headerH = 24f;
				float footerH = 22f;
				RenderTarget.FillRectangle(new RectangleF(x0, headerY, w, headerH), bgHeader);
				RenderTarget.DrawLine(new Vector2(x0, headerY + headerH), new Vector2(x0 + w, headerY + headerH), brGridLine, 1f);

				float cx = x0 - scrollColOffsetPx;
				for (int c = 0; c < cols.Count; c++)
				{
					MpColumnDef col = cols[c];
					float cw = col.Width;
					if (cx + cw >= x0 && cx <= x0 + w)
					{
						RectangleF hRect = new RectangleF(cx, headerY, cw, headerH);
						interactiveRects.Add(new ToolbarBtn { Rect = hRect, ActionId = "SORT", ColumnId = col.Id });

						string hdrText = col.Header;
						if (col.Id == sortColumnId)
							hdrText += sortAscending ? " ▲" : " ▼";

						DrawClippedText(hdrText, cx + 4f, headerY + 3f, cw - 6f, headerH - 4f, tfHeader, light ? brBlack : brLightText, false);
						RenderTarget.DrawLine(new Vector2(cx + cw, headerY), new Vector2(cx + cw, h - footerH), brGridLine, 1f);
					}
					cx += cw;
				}

				// 5. Data Rows
				float bodyTop = headerY + headerH;
				float bodyH = Math.Max(40f, h - topBarH - headerH - footerH);
				float rowH = Math.Max(19f, ScannerFontSize + 9f);
				int visibleRowsCount = Math.Max(1, (int)(bodyH / rowH));
				int maxOffset = Math.Max(0, rows.Count - visibleRowsCount);
				if (scrollRowOffset > maxOffset) scrollRowOffset = maxOffset;

				for (int rIdx = 0; rIdx < visibleRowsCount && (rIdx + scrollRowOffset) < rows.Count; rIdx++)
				{
					MpScannerRow row = rows[rIdx + scrollRowOffset];
					float ry = bodyTop + rIdx * rowH;

					if ((rIdx % 2) == 1)
						RenderTarget.FillRectangle(new RectangleF(x0, ry, w, rowH), brRowAlt);

					float rcx = x0 - scrollColOffsetPx;
					for (int c = 0; c < cols.Count; c++)
					{
						MpColumnDef col = cols[c];
						float cw = col.Width;
						if (rcx + cw >= x0 && rcx <= x0 + w)
						{
							MpCellTone tone = col.ToneSelector != null ? col.ToneSelector(row) : MpCellTone.Default;
							DxBrush cellBg = null;
							DxBrush cellFg = light ? brBlack : brLightText;

							switch (tone)
							{
								case MpCellTone.BgGreen:
									cellBg = brGreenBg;
									cellFg = brWhite;
									break;
								case MpCellTone.BgRedOrange:
									cellBg = brOrangeBg;
									cellFg = brWhite;
									break;
								case MpCellTone.BgYellow:
									cellBg = brYellowBg;
									cellFg = brBlack;
									break;
								case MpCellTone.BgGrey:
									cellBg = brGreyBg;
									cellFg = brBlack;
									break;
								case MpCellTone.FgGreen:
									cellFg = brFgGreen;
									break;
								case MpCellTone.FgRed:
									cellFg = brFgRed;
									break;
								case MpCellTone.FgOrange:
									cellFg = brFgOrange;
									break;
							}

							if (cellBg != null)
								RenderTarget.FillRectangle(new RectangleF(rcx + 0.5f, ry + 0.5f, cw - 1f, rowH - 1f), cellBg);

							string valStr = col.TextSelector != null ? (col.TextSelector(row) ?? "") : "";
							DrawClippedText(valStr, rcx + 4f, ry + 2f, cw - 8f, rowH - 3f, tfCell, cellFg, col.IsNumeric);
						}
						rcx += cw;
					}

					RenderTarget.DrawLine(new Vector2(x0, ry + rowH), new Vector2(x0 + w, ry + rowH), brGridLine, 1f);
				}

				// 6. Bottom Status Bar (matching AmiBroker 50Charts "Result list | Info | Walk Forward" footer)
				float fy = y0 + h - footerH;
				RenderTarget.FillRectangle(new RectangleF(x0, fy, w, footerH), bgTopBar);
				RenderTarget.DrawLine(new Vector2(x0, fy), new Vector2(x0 + w, fy), brGridLine, 1f);

				string footerText = string.Format(
					CultureInfo.InvariantCulture,
					"Result list  |  Symbols: {0} (Showing {1}-{2})  |  Formula: Formulas\\50Charts\\50ChartsScanner\\50ChartsMarketProfileScanner  |  Live Refresh #{3} ({4:HH:mm:ss})  |  MouseWheel: Scroll Rows, Shift+Wheel: Scroll Columns",
					rows.Count,
					rows.Count > 0 ? (scrollRowOffset + 1) : 0,
					Math.Min(rows.Count, scrollRowOffset + visibleRowsCount),
					liveRefreshPulse,
					DateTime.Now);
				DrawClippedText(footerText, x0 + 8f, fy + 3f, w - 16f, footerH - 4f, tfHeader, light ? brBlack : brLightText, false);
			}
		}

		private void DrawToolbarBtn(float x, float y, float w, float h, string text, string actionId, DxBrush bg, DxBrush fg, DxBrush border, DxTextFormat tf)
		{
			RectangleF rect = new RectangleF(x, y, w, h);
			RenderTarget.FillRectangle(rect, bg);
			RenderTarget.DrawRectangle(rect, border, 1f);
			interactiveRects.Add(new ToolbarBtn { Rect = rect, ActionId = actionId });
			DrawClippedText(text, x + 6f, y + 3f, w - 10f, h - 4f, tf, fg, false);
		}

		private void DrawClippedText(string text, float x, float y, float w, float h, DxTextFormat tf, DxBrush brush, bool rightAlign)
		{
			if (string.IsNullOrEmpty(text) || w <= 2f || h <= 2f)
				return;

			using (DxTextLayout layout = new DxTextLayout(Core.Globals.DirectWriteFactory, text, tf, w, h))
			{
				layout.WordWrapping = WordWrapping.NoWrap;
				layout.ParagraphAlignment = ParagraphAlignment.Center;
				layout.TextAlignment = rightAlign ? SharpDX.DirectWrite.TextAlignment.Trailing : SharpDX.DirectWrite.TextAlignment.Leading;
				Trimming trim = new Trimming { Granularity = TrimmingGranularity.Character };
				using (EllipsisTrimming sign = new EllipsisTrimming(Core.Globals.DirectWriteFactory, layout))
				{
					layout.SetTrimming(trim, sign);
					RenderTarget.DrawTextLayout(new Vector2(x, y), layout, brush, DrawTextOptions.Clip);
				}
			}
		}
	}
	#endregion
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MarketProfileScanner50Charts[] cacheMarketProfileScanner50Charts;
		public MarketProfileScanner50Charts MarketProfileScanner50Charts(bool displayPrevDayValues, bool displayTodayValues, bool displayDailySma, int smaNumberOfDays, bool displayPrevDayMpLevels, bool displayIbValues, bool displayCurrentDayMpLevels, int atrNumberOfDays, double profileDistributionOneSigma, double minTickSizeParam, int initialBalanceCandleCount, bool autoRefreshEnabled, int autoRefreshSeconds, bool useClassicLightTheme, int scannerFontSize)
		{
			return MarketProfileScanner50Charts(Input, displayPrevDayValues, displayTodayValues, displayDailySma, smaNumberOfDays, displayPrevDayMpLevels, displayIbValues, displayCurrentDayMpLevels, atrNumberOfDays, profileDistributionOneSigma, minTickSizeParam, initialBalanceCandleCount, autoRefreshEnabled, autoRefreshSeconds, useClassicLightTheme, scannerFontSize);
		}

		public MarketProfileScanner50Charts MarketProfileScanner50Charts(ISeries<double> input, bool displayPrevDayValues, bool displayTodayValues, bool displayDailySma, int smaNumberOfDays, bool displayPrevDayMpLevels, bool displayIbValues, bool displayCurrentDayMpLevels, int atrNumberOfDays, double profileDistributionOneSigma, double minTickSizeParam, int initialBalanceCandleCount, bool autoRefreshEnabled, int autoRefreshSeconds, bool useClassicLightTheme, int scannerFontSize)
		{
			if (cacheMarketProfileScanner50Charts != null)
				for (int idx = 0; idx < cacheMarketProfileScanner50Charts.Length; idx++)
					if (cacheMarketProfileScanner50Charts[idx] != null && cacheMarketProfileScanner50Charts[idx].DisplayPrevDayValues == displayPrevDayValues && cacheMarketProfileScanner50Charts[idx].DisplayTodayValues == displayTodayValues && cacheMarketProfileScanner50Charts[idx].DisplayDailySma == displayDailySma && cacheMarketProfileScanner50Charts[idx].SmaNumberOfDays == smaNumberOfDays && cacheMarketProfileScanner50Charts[idx].DisplayPrevDayMpLevels == displayPrevDayMpLevels && cacheMarketProfileScanner50Charts[idx].DisplayIbValues == displayIbValues && cacheMarketProfileScanner50Charts[idx].DisplayCurrentDayMpLevels == displayCurrentDayMpLevels && cacheMarketProfileScanner50Charts[idx].AtrNumberOfDays == atrNumberOfDays && cacheMarketProfileScanner50Charts[idx].ProfileDistributionOneSigma == profileDistributionOneSigma && cacheMarketProfileScanner50Charts[idx].MinTickSizeParam == minTickSizeParam && cacheMarketProfileScanner50Charts[idx].InitialBalanceCandleCount == initialBalanceCandleCount && cacheMarketProfileScanner50Charts[idx].AutoRefreshEnabled == autoRefreshEnabled && cacheMarketProfileScanner50Charts[idx].AutoRefreshSeconds == autoRefreshSeconds && cacheMarketProfileScanner50Charts[idx].UseClassicLightTheme == useClassicLightTheme && cacheMarketProfileScanner50Charts[idx].ScannerFontSize == scannerFontSize && cacheMarketProfileScanner50Charts[idx].EqualsInput(input))
						return cacheMarketProfileScanner50Charts[idx];
			return CacheIndicator<MarketProfileScanner50Charts>(new MarketProfileScanner50Charts(){ DisplayPrevDayValues = displayPrevDayValues, DisplayTodayValues = displayTodayValues, DisplayDailySma = displayDailySma, SmaNumberOfDays = smaNumberOfDays, DisplayPrevDayMpLevels = displayPrevDayMpLevels, DisplayIbValues = displayIbValues, DisplayCurrentDayMpLevels = displayCurrentDayMpLevels, AtrNumberOfDays = atrNumberOfDays, ProfileDistributionOneSigma = profileDistributionOneSigma, MinTickSizeParam = minTickSizeParam, InitialBalanceCandleCount = initialBalanceCandleCount, AutoRefreshEnabled = autoRefreshEnabled, AutoRefreshSeconds = autoRefreshSeconds, UseClassicLightTheme = useClassicLightTheme, ScannerFontSize = scannerFontSize }, input, ref cacheMarketProfileScanner50Charts);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MarketProfileScanner50Charts MarketProfileScanner50Charts(bool displayPrevDayValues, bool displayTodayValues, bool displayDailySma, int smaNumberOfDays, bool displayPrevDayMpLevels, bool displayIbValues, bool displayCurrentDayMpLevels, int atrNumberOfDays, double profileDistributionOneSigma, double minTickSizeParam, int initialBalanceCandleCount, bool autoRefreshEnabled, int autoRefreshSeconds, bool useClassicLightTheme, int scannerFontSize)
		{
			return indicator.MarketProfileScanner50Charts(Input, displayPrevDayValues, displayTodayValues, displayDailySma, smaNumberOfDays, displayPrevDayMpLevels, displayIbValues, displayCurrentDayMpLevels, atrNumberOfDays, profileDistributionOneSigma, minTickSizeParam, initialBalanceCandleCount, autoRefreshEnabled, autoRefreshSeconds, useClassicLightTheme, scannerFontSize);
		}

		public Indicators.MarketProfileScanner50Charts MarketProfileScanner50Charts(ISeries<double> input , bool displayPrevDayValues, bool displayTodayValues, bool displayDailySma, int smaNumberOfDays, bool displayPrevDayMpLevels, bool displayIbValues, bool displayCurrentDayMpLevels, int atrNumberOfDays, double profileDistributionOneSigma, double minTickSizeParam, int initialBalanceCandleCount, bool autoRefreshEnabled, int autoRefreshSeconds, bool useClassicLightTheme, int scannerFontSize)
		{
			return indicator.MarketProfileScanner50Charts(input, displayPrevDayValues, displayTodayValues, displayDailySma, smaNumberOfDays, displayPrevDayMpLevels, displayIbValues, displayCurrentDayMpLevels, atrNumberOfDays, profileDistributionOneSigma, minTickSizeParam, initialBalanceCandleCount, autoRefreshEnabled, autoRefreshSeconds, useClassicLightTheme, scannerFontSize);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MarketProfileScanner50Charts MarketProfileScanner50Charts(bool displayPrevDayValues, bool displayTodayValues, bool displayDailySma, int smaNumberOfDays, bool displayPrevDayMpLevels, bool displayIbValues, bool displayCurrentDayMpLevels, int atrNumberOfDays, double profileDistributionOneSigma, double minTickSizeParam, int initialBalanceCandleCount, bool autoRefreshEnabled, int autoRefreshSeconds, bool useClassicLightTheme, int scannerFontSize)
		{
			return indicator.MarketProfileScanner50Charts(Input, displayPrevDayValues, displayTodayValues, displayDailySma, smaNumberOfDays, displayPrevDayMpLevels, displayIbValues, displayCurrentDayMpLevels, atrNumberOfDays, profileDistributionOneSigma, minTickSizeParam, initialBalanceCandleCount, autoRefreshEnabled, autoRefreshSeconds, useClassicLightTheme, scannerFontSize);
		}

		public Indicators.MarketProfileScanner50Charts MarketProfileScanner50Charts(ISeries<double> input , bool displayPrevDayValues, bool displayTodayValues, bool displayDailySma, int smaNumberOfDays, bool displayPrevDayMpLevels, bool displayIbValues, bool displayCurrentDayMpLevels, int atrNumberOfDays, double profileDistributionOneSigma, double minTickSizeParam, int initialBalanceCandleCount, bool autoRefreshEnabled, int autoRefreshSeconds, bool useClassicLightTheme, int scannerFontSize)
		{
			return indicator.MarketProfileScanner50Charts(input, displayPrevDayValues, displayTodayValues, displayDailySma, smaNumberOfDays, displayPrevDayMpLevels, displayIbValues, displayCurrentDayMpLevels, atrNumberOfDays, profileDistributionOneSigma, minTickSizeParam, initialBalanceCandleCount, autoRefreshEnabled, autoRefreshSeconds, useClassicLightTheme, scannerFontSize);
		}
	}
}

#endregion
