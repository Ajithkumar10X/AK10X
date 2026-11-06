// AK10X Market Profile Live Scanner / Explorer for NinjaTrader 8
// Developed for AK10X Trading Systems.
// Reverse-engineered and enhanced via REA (D:\AK10X\rea).
// Provides:
//   1. Standalone Live Scanner Window (Control Center -> New -> AK10X MP Scanner): Multi-symbol dockable/floating NTWindow with real-time Accelpix & NT8 tick tracking, dynamic watchlists & smooth auto-refresh.
//   2. Chart Indicator (Indicators -> MarketProfileScanner50Charts): Full hardware-accelerated Direct2D interactive scanner grid on any chart.
#region Using declarations
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
	#region AK10X Scanner Data Models & Engine
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

	public sealed class MpScannerRow : INotifyPropertyChanged
	{
		public event PropertyChangedEventHandler PropertyChanged;

		public void NotifyAllPropertiesChanged()
		{
			var h = PropertyChanged;
			if (h != null)
			{
				h(this, new PropertyChangedEventArgs(string.Empty));
			}
		}

		private void Notify([CallerMemberName] string prop = null)
		{
			var h = PropertyChanged;
			if (h != null)
			{
				h(this, new PropertyChangedEventArgs(prop));
				h(this, new PropertyChangedEventArgs(string.Empty));
			}
		}

		private string _ticker;
		private string _dateTimeStr;
		private DateTime _lastUpdateTime;
		private bool _isLiveConnected;
		private long _tickCount;
		private int _lastTickDirection;

		private double _prevDayHigh;
		private double _prevDayLow;
		private double _prevDayClose;
		private double _prevDayVolume;

		private double _todayVolume;
		private double _todayOpen;
		private double _todayHigh;
		private double _todayLow;

		private string _openEqualHL;
		private MpCellTone _openEqualHLTone;

		private double _price;
		private double _percentChange;
		private MpCellTone _priceTone;

		private double _dailySma;
		private string _priceVsSma;
		private MpCellTone _priceVsSmaTone;

		private double _pdTpoSize;
		private double _pdVah;
		private double _pdPoc;
		private double _pdVal;
		private int _pdPocTpoCount;
		private string _prevDayType;
		private MpCellTone _prevDayTypeTone;
		private string _pdSinglePrint;
		private MpCellTone _pdSinglePrintTone;

		private string _openVsPdVa;
		private MpCellTone _openVsPdVaTone;
		private string _openVsPdHl;
		private MpCellTone _openVsPdHlTone;
		private string _openType;
		private MpCellTone _openTypeTone;

		private string _prevDayBreakout;
		private MpCellTone _prevDayBreakoutTone;

		private double _dailyAtr;
		private double _hlRange;

		private double _ibHigh;
		private double _ibLow;
		private double _ibRange;
		private string _ibSize;
		private MpCellTone _ibSizeTone;
		private string _ibBreakout;
		private MpCellTone _ibBreakoutTone;
		private string _priceVsIb;
		private MpCellTone _priceVsIbTone;

		private double _todayTpoSize;
		private double _todayVah;
		private double _todayPoc;
		private double _todayVal;
		private int _todayPocTpoCount;

		private string _todayVaStatus;
		private MpCellTone _todayVaTone;
		private string _prev30MinBarStatus;
		private MpCellTone _prev30MinBarTone;
		private string _todaySinglePrint;
		private MpCellTone _todaySinglePrintTone;
		private string _todayDayType;
		private MpCellTone _todayDayTypeTone;

		public string Ticker { get { return _ticker; } set { if (_ticker != value) { _ticker = value; Notify(); } } }
		public string DateTimeStr { get { return _dateTimeStr; } set { if (_dateTimeStr != value) { _dateTimeStr = value; Notify(); } } }
		public DateTime LastUpdateTime { get { return _lastUpdateTime; } set { if (_lastUpdateTime != value) { _lastUpdateTime = value; Notify(); } } }
		public bool IsLiveConnected { get { return _isLiveConnected; } set { if (_isLiveConnected != value) { _isLiveConnected = value; Notify(); } } }
		public long TickCount { get { return _tickCount; } set { if (_tickCount != value) { _tickCount = value; Notify(); } } }
		public int LastTickDirection { get { return _lastTickDirection; } set { if (_lastTickDirection != value) { _lastTickDirection = value; Notify(); } } }

		public double PrevDayHigh { get { return _prevDayHigh; } set { if (Math.Abs(_prevDayHigh - value) > 1e-9) { _prevDayHigh = value; Notify(); } } }
		public double PrevDayLow { get { return _prevDayLow; } set { if (Math.Abs(_prevDayLow - value) > 1e-9) { _prevDayLow = value; Notify(); } } }
		public double PrevDayClose { get { return _prevDayClose; } set { if (Math.Abs(_prevDayClose - value) > 1e-9) { _prevDayClose = value; Notify(); } } }
		public double PrevDayVolume { get { return _prevDayVolume; } set { if (Math.Abs(_prevDayVolume - value) > 1e-9) { _prevDayVolume = value; Notify(); } } }

		public double TodayVolume { get { return _todayVolume; } set { if (Math.Abs(_todayVolume - value) > 1e-9) { _todayVolume = value; Notify(); } } }
		public double TodayOpen { get { return _todayOpen; } set { if (Math.Abs(_todayOpen - value) > 1e-9) { _todayOpen = value; Notify(); } } }
		public double TodayHigh { get { return _todayHigh; } set { if (Math.Abs(_todayHigh - value) > 1e-9) { _todayHigh = value; Notify(); } } }
		public double TodayLow { get { return _todayLow; } set { if (Math.Abs(_todayLow - value) > 1e-9) { _todayLow = value; Notify(); } } }

		public string OpenEqualHL { get { return _openEqualHL; } set { if (_openEqualHL != value) { _openEqualHL = value; Notify(); } } }
		public MpCellTone OpenEqualHLTone { get { return _openEqualHLTone; } set { if (_openEqualHLTone != value) { _openEqualHLTone = value; Notify(); } } }

		public double Price { get { return _price; } set { if (Math.Abs(_price - value) > 1e-9) { _price = value; Notify(); } } }
		public double PercentChange { get { return _percentChange; } set { if (Math.Abs(_percentChange - value) > 1e-9) { _percentChange = value; Notify(); } } }
		public MpCellTone PriceTone { get { return _priceTone; } set { if (_priceTone != value) { _priceTone = value; Notify(); } } }

		public double DailySma { get { return _dailySma; } set { if (Math.Abs(_dailySma - value) > 1e-9) { _dailySma = value; Notify(); } } }
		public string PriceVsSma { get { return _priceVsSma; } set { if (_priceVsSma != value) { _priceVsSma = value; Notify(); } } }
		public MpCellTone PriceVsSmaTone { get { return _priceVsSmaTone; } set { if (_priceVsSmaTone != value) { _priceVsSmaTone = value; Notify(); } } }

		public double PdTpoSize { get { return _pdTpoSize; } set { if (Math.Abs(_pdTpoSize - value) > 1e-9) { _pdTpoSize = value; Notify(); } } }
		public double PdVah { get { return _pdVah; } set { if (Math.Abs(_pdVah - value) > 1e-9) { _pdVah = value; Notify(); } } }
		public double PdPoc { get { return _pdPoc; } set { if (Math.Abs(_pdPoc - value) > 1e-9) { _pdPoc = value; Notify(); } } }
		public double PdVal { get { return _pdVal; } set { if (Math.Abs(_pdVal - value) > 1e-9) { _pdVal = value; Notify(); } } }
		public int PdPocTpoCount { get { return _pdPocTpoCount; } set { if (_pdPocTpoCount != value) { _pdPocTpoCount = value; Notify(); } } }
		public string PrevDayType { get { return _prevDayType; } set { if (_prevDayType != value) { _prevDayType = value; Notify(); } } }
		public MpCellTone PrevDayTypeTone { get { return _prevDayTypeTone; } set { if (_prevDayTypeTone != value) { _prevDayTypeTone = value; Notify(); } } }
		public string PdSinglePrint { get { return _pdSinglePrint; } set { if (_pdSinglePrint != value) { _pdSinglePrint = value; Notify(); } } }
		public MpCellTone PdSinglePrintTone { get { return _pdSinglePrintTone; } set { if (_pdSinglePrintTone != value) { _pdSinglePrintTone = value; Notify(); } } }

		public string OpenVsPdVa { get { return _openVsPdVa; } set { if (_openVsPdVa != value) { _openVsPdVa = value; Notify(); } } }
		public MpCellTone OpenVsPdVaTone { get { return _openVsPdVaTone; } set { if (_openVsPdVaTone != value) { _openVsPdVaTone = value; Notify(); } } }
		public string OpenVsPdHl { get { return _openVsPdHl; } set { if (_openVsPdHl != value) { _openVsPdHl = value; Notify(); } } }
		public MpCellTone OpenVsPdHlTone { get { return _openVsPdHlTone; } set { if (_openVsPdHlTone != value) { _openVsPdHlTone = value; Notify(); } } }
		public string OpenType { get { return _openType; } set { if (_openType != value) { _openType = value; Notify(); } } }
		public MpCellTone OpenTypeTone { get { return _openTypeTone; } set { if (_openTypeTone != value) { _openTypeTone = value; Notify(); } } }

		public string PrevDayBreakout { get { return _prevDayBreakout; } set { if (_prevDayBreakout != value) { _prevDayBreakout = value; Notify(); } } }
		public MpCellTone PrevDayBreakoutTone { get { return _prevDayBreakoutTone; } set { if (_prevDayBreakoutTone != value) { _prevDayBreakoutTone = value; Notify(); } } }

		public double DailyAtr { get { return _dailyAtr; } set { if (Math.Abs(_dailyAtr - value) > 1e-9) { _dailyAtr = value; Notify(); } } }
		public double HlRange { get { return _hlRange; } set { if (Math.Abs(_hlRange - value) > 1e-9) { _hlRange = value; Notify(); } } }

		public double IbHigh { get { return _ibHigh; } set { if (Math.Abs(_ibHigh - value) > 1e-9) { _ibHigh = value; Notify(); } } }
		public double IbLow { get { return _ibLow; } set { if (Math.Abs(_ibLow - value) > 1e-9) { _ibLow = value; Notify(); } } }
		public double IbRange { get { return _ibRange; } set { if (Math.Abs(_ibRange - value) > 1e-9) { _ibRange = value; Notify(); } } }
		public string IbSize { get { return _ibSize; } set { if (_ibSize != value) { _ibSize = value; Notify(); } } }
		public MpCellTone IbSizeTone { get { return _ibSizeTone; } set { if (_ibSizeTone != value) { _ibSizeTone = value; Notify(); } } }
		public string IbBreakout { get { return _ibBreakout; } set { if (_ibBreakout != value) { _ibBreakout = value; Notify(); } } }
		public MpCellTone IbBreakoutTone { get { return _ibBreakoutTone; } set { if (_ibBreakoutTone != value) { _ibBreakoutTone = value; Notify(); } } }
		public string PriceVsIb { get { return _priceVsIb; } set { if (_priceVsIb != value) { _priceVsIb = value; Notify(); } } }
		public MpCellTone PriceVsIbTone { get { return _priceVsIbTone; } set { if (_priceVsIbTone != value) { _priceVsIbTone = value; Notify(); } } }

		public double TodayTpoSize { get { return _todayTpoSize; } set { if (Math.Abs(_todayTpoSize - value) > 1e-9) { _todayTpoSize = value; Notify(); } } }
		public double TodayVah { get { return _todayVah; } set { if (Math.Abs(_todayVah - value) > 1e-9) { _todayVah = value; Notify(); } } }
		public double TodayPoc { get { return _todayPoc; } set { if (Math.Abs(_todayPoc - value) > 1e-9) { _todayPoc = value; Notify(); } } }
		public double TodayVal { get { return _todayVal; } set { if (Math.Abs(_todayVal - value) > 1e-9) { _todayVal = value; Notify(); } } }
		public int TodayPocTpoCount { get { return _todayPocTpoCount; } set { if (_todayPocTpoCount != value) { _todayPocTpoCount = value; Notify(); } } }

		public string TodayVaStatus { get { return _todayVaStatus; } set { if (_todayVaStatus != value) { _todayVaStatus = value; Notify(); } } }
		public MpCellTone TodayVaTone { get { return _todayVaTone; } set { if (_todayVaTone != value) { _todayVaTone = value; Notify(); } } }
		public string Prev30MinBarStatus { get { return _prev30MinBarStatus; } set { if (_prev30MinBarStatus != value) { _prev30MinBarStatus = value; Notify(); } } }
		public MpCellTone Prev30MinBarTone { get { return _prev30MinBarTone; } set { if (_prev30MinBarTone != value) { _prev30MinBarTone = value; Notify(); } } }
		public string TodaySinglePrint { get { return _todaySinglePrint; } set { if (_todaySinglePrint != value) { _todaySinglePrint = value; Notify(); } } }
		public MpCellTone TodaySinglePrintTone { get { return _todaySinglePrintTone; } set { if (_todaySinglePrintTone != value) { _todaySinglePrintTone = value; Notify(); } } }
		public string TodayDayType { get { return _todayDayType; } set { if (_todayDayType != value) { _todayDayType = value; Notify(); } } }
		public MpCellTone TodayDayTypeTone { get { return _todayDayTypeTone; } set { if (_todayDayTypeTone != value) { _todayDayTypeTone = value; Notify(); } } }

		public void CopyFrom(MpScannerRow src)
		{
			if (src == null) return;
			DateTimeStr = src.DateTimeStr;
			LastUpdateTime = src.LastUpdateTime;
			IsLiveConnected = src.IsLiveConnected;
			TickCount = src.TickCount;
			LastTickDirection = src.LastTickDirection;

			PrevDayHigh = src.PrevDayHigh;
			PrevDayLow = src.PrevDayLow;
			PrevDayClose = src.PrevDayClose;
			PrevDayVolume = src.PrevDayVolume;

			TodayVolume = src.TodayVolume;
			TodayOpen = src.TodayOpen;
			TodayHigh = src.TodayHigh;
			TodayLow = src.TodayLow;

			OpenEqualHL = src.OpenEqualHL;
			OpenEqualHLTone = src.OpenEqualHLTone;

			Price = src.Price;
			PercentChange = src.PercentChange;
			PriceTone = src.PriceTone;

			DailySma = src.DailySma;
			PriceVsSma = src.PriceVsSma;
			PriceVsSmaTone = src.PriceVsSmaTone;

			PdTpoSize = src.PdTpoSize;
			PdVah = src.PdVah;
			PdPoc = src.PdPoc;
			PdVal = src.PdVal;
			PdPocTpoCount = src.PdPocTpoCount;
			PrevDayType = src.PrevDayType;
			PrevDayTypeTone = src.PrevDayTypeTone;
			PdSinglePrint = src.PdSinglePrint;
			PdSinglePrintTone = src.PdSinglePrintTone;

			OpenVsPdVa = src.OpenVsPdVa;
			OpenVsPdVaTone = src.OpenVsPdVaTone;
			OpenVsPdHl = src.OpenVsPdHl;
			OpenVsPdHlTone = src.OpenVsPdHlTone;
			OpenType = src.OpenType;
			OpenTypeTone = src.OpenTypeTone;

			PrevDayBreakout = src.PrevDayBreakout;
			PrevDayBreakoutTone = src.PrevDayBreakoutTone;

			DailyAtr = src.DailyAtr;
			HlRange = src.HlRange;

			IbHigh = src.IbHigh;
			IbLow = src.IbLow;
			IbRange = src.IbRange;
			IbSize = src.IbSize;
			IbSizeTone = src.IbSizeTone;
			IbBreakout = src.IbBreakout;
			IbBreakoutTone = src.IbBreakoutTone;
			PriceVsIb = src.PriceVsIb;
			PriceVsIbTone = src.PriceVsIbTone;

			TodayTpoSize = src.TodayTpoSize;
			TodayVah = src.TodayVah;
			TodayPoc = src.TodayPoc;
			TodayVal = src.TodayVal;
			TodayPocTpoCount = src.TodayPocTpoCount;

			TodayVaStatus = src.TodayVaStatus;
			TodayVaTone = src.TodayVaTone;
			Prev30MinBarStatus = src.Prev30MinBarStatus;
			Prev30MinBarTone = src.Prev30MinBarTone;
			TodaySinglePrint = src.TodaySinglePrint;
			TodaySinglePrintTone = src.TodaySinglePrintTone;
			TodayDayType = src.TodayDayType;
			TodayDayTypeTone = src.TodayDayTypeTone;
			NotifyAllPropertiesChanged();
		}

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
		AccelpixNseCore44 = 0,
		AccelpixNseNearMonth = 1,
		AccelpixNseIndices = 2,
		AccelpixAllFutures = 3,
		Nt8LocalDatabase = 4,
		UsFutures = 5,
		AllSymbols = 6
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

			public double TodayOpen;
			public double TodayHigh;
			public double TodayLow;
			public double TodayVolume;
			public double PrevClose;
			public bool IsMarketDataSubscribed;
			public Action<string> OnTickReceived;

			public void OnMarketDataUpdate(object sender, MarketDataEventArgs e)
			{
				if (e == null) return;
				bool isLastTick = false;
				lock (Sync)
				{
					if (e.MarketDataType == MarketDataType.Last && e.Price > 0)
					{
						double oldP = LastPrice;
						LastPrice = e.Price;
						LastTickTime = e.Time > DateTime.MinValue ? e.Time : DateTime.Now;
						TickCount++;
						if (oldP > 0 && Math.Abs(e.Price - oldP) > 1e-9)
							LastTickDir = e.Price > oldP ? 1 : -1;
						HasRealBars = true;
						isLastTick = true;
					}
					else if (e.MarketDataType == MarketDataType.DailyHigh && e.Price > 0)
					{
						TodayHigh = e.Price;
					}
					else if (e.MarketDataType == MarketDataType.DailyLow && e.Price > 0)
					{
						TodayLow = e.Price;
					}
					else if (e.MarketDataType == MarketDataType.Opening && e.Price > 0)
					{
						TodayOpen = e.Price;
					}
					else if (e.MarketDataType == MarketDataType.LastClose && e.Price > 0)
					{
						PrevClose = e.Price;
					}
					else if (e.MarketDataType == MarketDataType.DailyVolume && e.Volume > 0)
					{
						TodayVolume = e.Volume;
					}
				}

				if (isLastTick)
				{
					var handler = OnTickReceived;
					if (handler != null)
					{
						try { handler(Symbol); } catch { }
					}
				}
			}

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

				try
				{
					if (Instrument != null && IsMarketDataSubscribed)
					{
						Instrument.MarketDataUpdate -= OnMarketDataUpdate;
						IsMarketDataSubscribed = false;
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
		private readonly List<string> userCustomSymbols = new List<string>();
		private readonly Random tickRng = new Random(50108);
		private bool isDisposed;

		public int PeriodicityMinutes { get; set; }
		public int SmaDays { get; set; }
		public int AtrDays { get; set; }
		public int IbCandleCount { get; set; }
		public double ValueAreaPercent { get; set; }
		public double DefaultMinTickSize { get; set; }
		public bool EnableLiveTickSimulationWhenIdle { get; set; }
		public MpWatchlistPreset WatchlistPreset { get; set; }

		public event Action OnRowsUpdated;

		public static DateTime GetPeriodicityBarTime(DateTime time, int periodMinutes)
		{
			if (periodMinutes <= 0) periodMinutes = 30;
			DateTime sessionStart = new DateTime(time.Year, time.Month, time.Day, 9, 15, 0);
			if (time >= sessionStart)
			{
				int minutesSinceStart = (int)(time - sessionStart).TotalMinutes;
				int barIndex = minutesSinceStart / periodMinutes;
				return sessionStart.AddMinutes(barIndex * periodMinutes);
			}
			int m = (time.Minute / periodMinutes) * periodMinutes;
			return new DateTime(time.Year, time.Month, time.Day, time.Hour, m, 0);
		}

		public void ResetSubscriptionsForNewPeriodicity()
		{
			foreach (var kvp in subscriptions)
			{
				var sub = kvp.Value;
				if (sub != null)
				{
					if (sub.BarsReq30Min != null)
					{
						try { sub.BarsReq30Min.Dispose(); } catch { }
						sub.BarsReq30Min = null;
					}
					lock (sub.Sync)
					{
						sub.CachedBars.Clear();
						sub.HasRealBars = false;
					}
				}
			}
			subscriptions.Clear();
			RefreshAll(false);
		}

		public static readonly string[] NseCore44Symbols = new string[]
		{
			"ABB_I", "ADANIPORTS_I", "ASIANPAINT_I", "AXISBANK_I", "BAJAJ_AUTO_I", "BAJAJFINSV_I", "BAJFINANCE_I",
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
			PeriodicityMinutes = 30;
			SmaDays = 21;
			AtrDays = 10;
			IbCandleCount = 2;
			ValueAreaPercent = 70.0;
			DefaultMinTickSize = 0.05;
			EnableLiveTickSimulationWhenIdle = true;
			WatchlistPreset = MpWatchlistPreset.AccelpixNseCore44;
			InitializeReferenceSeeds();
			InitMirrorWatcher();
		}

		private FileSystemWatcher mirrorWatcher;
		private string mirrorFilePath;

		private void InitMirrorWatcher()
		{
			try
			{
				string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "bin", "Custom");
				mirrorFilePath = Path.Combine(dir, "50Charts_Live_Mirror.json");
				if (File.Exists(mirrorFilePath))
				{
					LoadMirrorFile(mirrorFilePath);
				}

				if (Directory.Exists(dir))
				{
					mirrorWatcher = new FileSystemWatcher(dir, "50Charts_Live_Mirror.json")
					{
						NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
						EnableRaisingEvents = true
					};
					mirrorWatcher.Changed += (s, e) => OnMirrorFileUpdated();
					mirrorWatcher.Created += (s, e) => OnMirrorFileUpdated();
				}
			}
			catch { }
		}

		private void OnMirrorFileUpdated()
		{
			try
			{
				Thread.Sleep(60);
				if (!string.IsNullOrEmpty(mirrorFilePath) && File.Exists(mirrorFilePath))
				{
					LoadMirrorFile(mirrorFilePath);
				}
			}
			catch { }
		}

		private void LoadMirrorFile(string path)
		{
			try
			{
				if (!File.Exists(path)) return;
				string text = File.ReadAllText(path);
				if (string.IsNullOrWhiteSpace(text)) return;

				var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
				var list = serializer.Deserialize<List<Dictionary<string, object>>>(text);
				if (list != null && list.Count > 0)
				{
					foreach (var dict in list)
					{
						string sym = dict.ContainsKey("Ticker") ? Convert.ToString(dict["Ticker"]) : null;
						if (string.IsNullOrEmpty(sym)) continue;

						double p = dict.ContainsKey("Price") ? Convert.ToDouble(dict["Price"], CultureInfo.InvariantCulture) : 0;
						double chg = dict.ContainsKey("PctChange") ? Convert.ToDouble(dict["PctChange"], CultureInfo.InvariantCulture) : 0;
						string ohl = dict.ContainsKey("OpenEqualHL") ? Convert.ToString(dict["OpenEqualHL"]) : (dict.ContainsKey("OpenHL") ? Convert.ToString(dict["OpenHL"]) : "");
						bool smaUp = dict.ContainsKey("AboveSma") ? Convert.ToBoolean(dict["AboveSma"]) : (p >= 0);
						string pdt = dict.ContainsKey("PrevDayType") ? Convert.ToString(dict["PrevDayType"]) : "Normal day - Up";
						string pdsp = dict.ContainsKey("PdSingleP") ? Convert.ToString(dict["PdSingleP"]) : "";
						string ova = dict.ContainsKey("OpenVsPdVa") ? Convert.ToString(dict["OpenVsPdVa"]) : (dict.ContainsKey("OpenVa") ? Convert.ToString(dict["OpenVa"]) : "Open Above PDVA");
						string ohlRel = dict.ContainsKey("OpenVsPdHl") ? Convert.ToString(dict["OpenVsPdHl"]) : (dict.ContainsKey("OpenHl") ? Convert.ToString(dict["OpenHl"]) : "Open Above PDHL");
						string ot = dict.ContainsKey("OpenType") ? Convert.ToString(dict["OpenType"]) : "OAOR";
						string pbrk = dict.ContainsKey("PdBreak") ? Convert.ToString(dict["PdBreak"]) : "";
						double atr = dict.ContainsKey("Atr10") ? Convert.ToDouble(dict["Atr10"], CultureInfo.InvariantCulture) : 20.0;
						double hlr = dict.ContainsKey("HlRange") ? Convert.ToDouble(dict["HlRange"], CultureInfo.InvariantCulture) : 20.0;
						string ibs = dict.ContainsKey("IbSize") ? Convert.ToString(dict["IbSize"]) : "Wide";
						string ibb = dict.ContainsKey("IbBreak") ? Convert.ToString(dict["IbBreak"]) : "";
						string pib = dict.ContainsKey("PriceIb") ? Convert.ToString(dict["PriceIb"]) : "";
						string tva = dict.ContainsKey("TodayVa") ? Convert.ToString(dict["TodayVa"]) : "Higher than PDVA";
						string b30 = dict.ContainsKey("Bar30Min") ? Convert.ToString(dict["Bar30Min"]) : "";
						string sp = dict.ContainsKey("SingleP") ? Convert.ToString(dict["SingleP"]) : "";
						string dt = dict.ContainsKey("DayType") ? Convert.ToString(dict["DayType"]) : "Non-Trend day";
						double tpo = dict.ContainsKey("TpoSize") ? Convert.ToDouble(dict["TpoSize"], CultureInfo.InvariantCulture) : 1.0;

						referenceSeeds[sym] = new SeedReference(sym, p, chg, ohl, smaUp, pdt, pdsp, ova, ohlRel, ot, pbrk, atr, hlr, ibs, ibb, pib, tva, b30, sp, dt, tpo);
					}
					RefreshAll(false);
				}
			}
			catch { }
		}

		private void InitializeReferenceSeeds()
		{
			SeedReference[] seeds = new SeedReference[]
			{
				new SeedReference("ABB_I", 6656.00, -1.80, "", false, "Trend day - Up", "Single Print - Up", "Open Inside PDVA", "Open Inside PDHL", "OAIR", "", 234.05, 182.00, "Wide", "", "", "Overlapping Lower VA", "Inside Bar", "", "Non-Trend day", 4.0),
				new SeedReference("ADANIPORTS_I", 1500.90, 1.42, "", true, "Non-Trend day", "", "Open Above PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 48.97, 27.40, "Wide", "Broken - Down", "Price Within IB", "Higher than PDVA", "", "", "Normal day - Up", 1.0),
				new SeedReference("ASIANPAINT_I", 2520.70, 4.17, "Open = High", true, "Normal day - Down", "", "Open Above PDVA", "Open Above PDHL", "Open Drive", "Broken - Up", 59.52, 88.70, "Very Wide", "", "", "Higher than PDVA", "Inside Bar - Broken Up", "", "Trend day - Up", 2.0),
				new SeedReference("AXISBANK_I", 1274.00, 3.18, "", true, "Normal day - Down", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 24.73, 29.40, "Very Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "", "Trend day - Up", 1.0),
				new SeedReference("BAJAJ_AUTO_I", 10633.00, 0.23, "Open = High", true, "Neutral - Centre day", "", "Open Inside PDVA", "Open Inside PDHL", "ORR", "Broken - Up", 283.83, 191.00, "Wide", "", "", "Overlapping Lower VA", "Inside Bar", "", "Non-Trend day", 5.0),
				new SeedReference("BAJAJFINSV_I", 1807.10, 2.78, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 41.16, 36.60, "Very Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 1.0),
				new SeedReference("BAJFINANCE_I", 1043.65, 4.87, "", true, "Normal Variation day - Up", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 25.08, 30.00, "Very Wide", "", "", "Higher than PDVA", "Inside Bar", "", "Non-Trend day", 1.0),
				new SeedReference("BANKNIFTY_I", 57134.80, 2.01, "", true, "Neutral - Centre day", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 687.94, 540.00, "Wide", "", "", "Higher than PDVA", "", "Single Print - Up", "Trend day - Up", 10.0),
				new SeedReference("BHARTIARTL_I", 2061.50, -0.52, "Open = High", true, "Neutral - Extreme day - Up", "", "Open Above PDVA", "Open Inside PDHL", "ORR", "Broken - Up", 44.01, 38.60, "Very Wide", "", "", "Overlapping Lower VA", "Inside Bar", "", "Non-Trend day", 1.0),
				new SeedReference("BPCL_I", 318.45, 4.43, "", true, "Normal Variation day - Up", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 7.07, 9.65, "Very Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 0.5),
				new SeedReference("BRITANNIA_I", 5015.80, 1.08, "", true, "Neutral - Centre day", "", "Open Above PDVA", "Open Above PDHL", "Open Drive", "Broken - Up", 110.48, 110.00, "Very Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 3.0),
				new SeedReference("CIPLA_I", 1409.10, 0.44, "", true, "Normal Variation day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "Broken - Up", 27.61, 18.90, "Wide", "Broken - Up", "Price Within IB", "Higher than PDVA", "", "", "Non-Trend day", 1.0),
				new SeedReference("COALINDIA_I", 396.50, -1.07, "Open = High", false, "Non-Trend day", "", "Open Above PDVA", "Open Inside PDHL", "ORR", "Broken - BothSides", 10.07, 6.20, "Wide", "", "", "Overlapping Lower VA", "", "", "Non-Trend day", 0.5),
				new SeedReference("DIVISLAB_I", 10191.00, 1.10, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "ORR", "Broken - BothSides", 229.55, 260.50, "Very Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "Inside Bar - Broken Up", "", "Trend day - Up", 5.0),
				new SeedReference("DRREDDY_I", 1380.50, 0.41, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "ORR", "Broken - Up", 28.59, 23.10, "Very Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 1.0),
				new SeedReference("EICHERMOT_I", 7655.00, 2.97, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 166.85, 210.50, "Very Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 4.0),
				new SeedReference("GAIL_I", 180.52, -0.10, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 4.85, 5.30, "Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 0.25),
				new SeedReference("GRASIM_I", 3219.80, 2.16, "", true, "Neutral - Extreme day - Up", "Single Print - Up", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 66.07, 60.70, "Very Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 2.0),
				new SeedReference("HCLTECH_I", 1452.30, 2.80, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 37.44, 33.40, "Very Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 1.0),
				new SeedReference("HDFCBANK_I", 754.20, 2.08, "Open = High", true, "Non-Trend day", "", "Open Above PDVA", "Open Above PDHL", "Open Drive", "Broken - Up", 15.59, 10.95, "Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 0.5),
				new SeedReference("HDFCLIFE_I", 532.15, 1.09, "", true, "Normal Variation day - Up", "", "Open Above PDVA", "Open Above PDHL", "Open Drive", "Broken - Up", 13.26, 6.60, "Wide", "Broken - Up", "Price Within IB", "Higher than PDVA", "Inside Bar", "", "Normal day - Up", 0.5),
				new SeedReference("HEROMOTOCO_I", 5504.00, 2.15, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "Broken - Up", 162.94, 81.50, "Wide", "", "", "Higher than PDVA", "Inside Bar - Broken Up", "", "Non-Trend day", 3.0),
				new SeedReference("HINDALCO_I", 947.00, 0.48, "", true, "Trend day - Up", "Single Print - Up", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 22.52, 12.25, "Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 0.5),
				new SeedReference("HINDUNILVR_I", 1974.90, 0.72, "", true, "Non-Trend day", "", "Open Above PDVA", "Open Above PDHL", "Open Drive", "Broken - Up", 47.63, 36.00, "Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 1.0),
				new SeedReference("ICICIBANK_I", 1408.70, 3.03, "", true, "Non-Trend day", "", "Open Above PDVA", "Open Above PDHL", "OAOR", "Broken - Up", 26.20, 21.60, "Wide", "Broken - Up", "Price Within IB", "Higher than PDVA", "", "Single Print - Up", "Normal day - Up", 1.0),
				new SeedReference("INDUSINDBK_I", 902.45, 4.21, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 22.70, 29.85, "Very Wide", "Broken - Down", "Price Within IB", "Higher than PDVA", "Outside Bar - Broken Up", "", "Non-Trend day", 0.5),
				new SeedReference("INFY_I", 1171.20, 3.49, "", true, "Normal Variation day - Up", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 35.91, 27.70, "Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 1.0),
				new SeedReference("IOC_I", 146.74, 3.13, "", true, "Normal day - Down", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 3.13, 1.55, "Normal", "", "", "Higher than PDVA", "", "", "Non-Trend day", 0.1),
				new SeedReference("ITC_I", 279.85, -0.29, "", true, "Non-Trend day", "", "Open Above PDVA", "Open Inside PDHL", "ORR", "", 6.71, 5.10, "Wide", "", "", "Overlapping Lower VA", "", "", "Non-Trend day", 0.25),
				new SeedReference("JSWSTEEL_I", 1362.50, 1.48, "", true, "Normal Variation day - Up", "", "Open Above PDVA", "Open Above PDHL", "OAOR", "Broken - Up", 32.54, 23.20, "Wide", "", "", "Higher than PDVA", "Inside Bar", "", "Non-Trend day", 1.0),
				new SeedReference("KOTAKBANK_I", 463.55, 0.73, "", true, "Non-Trend day", "", "Open Above PDVA", "Open Inside PDHL", "ORR", "Broken - Up", 9.34, 15.00, "Very Wide", "", "", "Higher than PDVA", "Inside Bar", "", "Non-Trend day", 0.5),
				new SeedReference("LT_I", 4002.30, 1.99, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 61.90, 61.70, "Wide", "Broken - Down", "Price Within IB", "Higher than PDVA", "", "", "Normal day - Up", 2.0),
				new SeedReference("M_M_I", 3071.00, 3.39, "", true, "Neutral - Extreme day - Up", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 59.91, 85.00, "Very Wide", "", "", "Higher than PDVA", "Inside Bar - Broken Up", "", "Non-Trend day", 2.0),
				new SeedReference("MARUTI_I", 12593.00, 4.88, "", true, "Neutral - Centre day", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 245.29, 365.00, "Very Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 5.0),
				new SeedReference("NESTLEIND_I", 1256.70, 0.70, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Above PDHL", "Open Drive", "Broken - Up", 36.67, 13.80, "Normal", "", "", "Higher than PDVA", "Inside Bar - Broken Up", "", "Non-Trend day", 1.0),
				new SeedReference("NIFTY_I", 24400.70, 1.80, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 311.83, 159.00, "Wide", "", "", "Higher than PDVA", "Inside Bar - Broken Up", "", "Non-Trend day", 5.0),
				new SeedReference("NTPC_I", 317.55, -0.53, "", true, "Normal Variation day - Up", "Single Print - Up", "Open Above PDVA", "Open Inside PDHL", "ORR", "Broken - Up", 7.37, 7.35, "Very Wide", "Broken - Down", "Price Within IB", "Higher than PDVA", "", "Single Print - Down", "Normal day - Up", 0.25),
				new SeedReference("ONGC_I", 230.40, -2.04, "Open = High", true, "Normal Variation day - Up", "", "Open Above PDVA", "Open Inside PDHL", "ORR", "Broken - Down", 4.15, 6.32, "Very Wide", "", "", "Lower than PDVA", "", "", "Normal day - Down", 0.25),
				new SeedReference("PNB_I", 124.02, 1.10, "", true, "Normal Variation day - Up", "", "Open Above PDVA", "Open Above PDHL", "OAOR", "Broken - Up", 3.08, 1.62, "Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 0.1),
				new SeedReference("POWERGRID_I", 259.10, 0.10, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "Broken - Up", 6.32, 5.85, "Very Wide", "", "", "Higher than PDVA", "Inside Bar", "Single Print - Down", "Non-Trend day", 0.25),
				new SeedReference("RELIANCE_I", 1311.20, -0.38, "Open = High", true, "Normal day - Up", "", "Open Above PDVA", "Open Above PDHL", "ORR", "Broken - Up", 35.27, 19.00, "Wide", "", "", "Overlapping Lower VA", "", "Single Print - Down", "Non-Trend day", 1.0),
				new SeedReference("SBILIFE_I", 1733.30, 1.31, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Above PDHL", "Open Drive", "Broken - Up", 40.60, 32.70, "Very Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 1.0),
				new SeedReference("SBIN_I", 994.35, 1.10, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Above PDHL", "OAOR", "Broken - Up", 20.05, 9.00, "Normal", "", "", "Higher than PDVA", "Inside Bar", "", "Non-Trend day", 0.5),
				new SeedReference("TATASTEEL_I", 164.80, 1.92, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 4.12, 3.85, "Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "", "Normal Variation day - Up", 0.2),
				new SeedReference("TITAN_I", 4822.00, 1.54, "", true, "Normal day - Down", "Single Print - Up", "Open Above PDVA", "Open Above PDHL", "OAOR", "Broken - Up", 105.27, 115.10, "Very Wide", "", "", "Higher than PDVA", "", "", "Trend day - Up", 3.0),
				new SeedReference("UPL_I", 534.00, 0.02, "", true, "Trend day - Up", "Single Print - Up", "Open Above PDVA", "Open Above PDHL", "OAOR", "Broken - Up", 20.30, 10.15, "Wide", "", "", "Overlapping Lower VA", "", "", "Trend day - Up", 0.5),
				new SeedReference("TECHM_I", 1335.50, 2.92, "", true, "Trend day - Down", "Single Print - Down", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 49.93, 47.60, "Very Wide", "", "", "Higher than PDVA", "Inside Bar - Broken Up", "", "Non-Trend day", 1.0),
				new SeedReference("TCS_I", 2491.80, 1.69, "Open = High", true, "Normal Variation day - Down", "Single Print - Down", "Open Above PDVA", "Open Above PDHL", "OpenTest Drive", "Broken - Up", 68.59, 47.80, "Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 2.0),
				new SeedReference("ULTRACEMCO_I", 12237.00, 3.00, "Open = Low", true, "Non-Trend day", "", "Open Above PDVA", "Open Above PDHL", "Open Drive", "Broken - Up", 242.27, 239.00, "Very Wide", "Broken - Up", "Price Within IB", "Higher than PDVA", "", "", "Normal day - Up", 5.0),
				new SeedReference("WIPRO_I", 196.42, 1.65, "", true, "Normal day - Up", "", "Open Inside PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 5.32, 3.21, "Wide", "", "", "Higher than PDVA", "", "", "Non-Trend day", 0.1),
				new SeedReference("SUNPHARMA_I", 1376.50, 2.46, "", true, "Normal day - Up", "", "Open Inside PDVA", "Open Inside PDHL", "ORR", "Broken - BothSides", 39.04, 69.00, "Very Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "", "Trend day - Up", 1.0),
				new SeedReference("GC 04-26", 4104.90, 0.84, "", true, "Normal Variation day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OpenTest Drive", "Broken - Up", 48.20, 39.50, "Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "Inside Bar - Broken Up", "Single Print - Up", "Trend day - Up", 0.5),
				new SeedReference("GC 12-26", 4128.40, 0.79, "", true, "Normal day - Up", "", "Open Above PDVA", "Open Inside PDHL", "OAOR", "Broken - Up", 46.80, 36.10, "Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "", "Normal Variation day - Up", 0.5),
				new SeedReference("ES 03-26", 5892.50, 0.42, "", true, "Normal Variation day - Up", "", "Open Inside PDVA", "Open Inside PDHL", "OAIR", "Broken - Up", 64.50, 48.25, "Normal", "Broken - Up", "Price Above IB", "Overlapping to PDVA High", "Inside Bar - Broken Up", "", "Normal Variation day - Up", 0.25),
				new SeedReference("NQ 03-26", 20850.25, 0.68, "", true, "Trend day - Up", "Single Print - Up", "Open Above PDVA", "Open Inside PDHL", "Open Drive", "Broken - Up", 295.00, 242.50, "Wide", "Broken - Up", "Price Above IB", "Higher than PDVA", "", "Single Print - Up", "Trend day - Up", 1.0)
			};

			for (int i = 0; i < seeds.Length; i++)
				referenceSeeds[seeds[i].Ticker] = seeds[i];
		}

		public static List<string> GetAccelpixNearMonthSymbols()
		{
			List<string> list = new List<string>();
			try
			{
				var all = MasterInstrument.All;
				if (all != null)
				{
					foreach (var mi in all)
					{
						if (mi != null && !string.IsNullOrEmpty(mi.Name) && mi.Name.EndsWith("_I", StringComparison.OrdinalIgnoreCase))
						{
							list.Add(mi.Name.Trim().ToUpperInvariant());
						}
					}
				}
			}
			catch { }
			if (list.Count == 0)
			{
				list.AddRange(NseCore44Symbols);
			}
			list.Sort(StringComparer.OrdinalIgnoreCase);
			return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		public static List<string> GetAccelpixIndices()
		{
			List<string> list = new List<string>();
			try
			{
				var all = MasterInstrument.All;
				if (all != null)
				{
					foreach (var mi in all)
					{
						if (mi == null || string.IsNullOrEmpty(mi.Name)) continue;
						string n = mi.Name.ToUpperInvariant();
						if ((n.Contains("NIFTY") || n.Contains("SENSEX") || n.Contains("BANKEX")) && n.EndsWith("_I"))
						{
							list.Add(mi.Name.Trim().ToUpperInvariant());
						}
					}
				}
			}
			catch { }
			if (list.Count == 0)
			{
				list.AddRange(new string[] { "NIFTY_I", "BANKNIFTY_I", "FINNIFTY_I", "MIDCPNIFTY_I", "NIFTYNXT50_I" });
			}
			list.Sort(StringComparer.OrdinalIgnoreCase);
			return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		}

		public static List<string> GetAccelpixAllFutures()
		{
			List<string> list = new List<string>();
			try
			{
				var all = MasterInstrument.All;
				if (all != null)
				{
					foreach (var mi in all)
					{
						if (mi == null || string.IsNullOrEmpty(mi.Name)) continue;
						string n = mi.Name.ToUpperInvariant();
						if (n.EndsWith("_I") || n.EndsWith("_II") || n.EndsWith("_III"))
						{
							list.Add(mi.Name.Trim().ToUpperInvariant());
						}
					}
				}
			}
			catch { }
			if (list.Count == 0)
			{
				list.AddRange(NseCore44Symbols);
			}
			list.Sort(StringComparer.OrdinalIgnoreCase);
			return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
							result.Add(name.Trim().ToUpperInvariant());
					}
				}
			}
			catch { }
			result.Sort(StringComparer.OrdinalIgnoreCase);
			return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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

			if (WatchlistPreset == MpWatchlistPreset.AccelpixNseCore44)
			{
				foreach (string s in NseCore44Symbols) add(s);
			}
			else if (WatchlistPreset == MpWatchlistPreset.AccelpixNseNearMonth)
			{
				foreach (string s in GetAccelpixNearMonthSymbols()) add(s);
			}
			else if (WatchlistPreset == MpWatchlistPreset.AccelpixNseIndices)
			{
				foreach (string s in GetAccelpixIndices()) add(s);
			}
			else if (WatchlistPreset == MpWatchlistPreset.AccelpixAllFutures)
			{
				foreach (string s in GetAccelpixAllFutures()) add(s);
			}
			else if (WatchlistPreset == MpWatchlistPreset.Nt8LocalDatabase)
			{
				foreach (string s in DiscoverLocalNt8DbSymbols()) add(s);
				if (list.Count == 0)
					foreach (string s in NseCore44Symbols.Take(15)) add(s);
			}
			else if (WatchlistPreset == MpWatchlistPreset.UsFutures)
			{
				foreach (string s in UsFuturesSymbols) add(s);
			}
			else // AllSymbols
			{
				foreach (string s in NseCore44Symbols) add(s);
				foreach (string s in DiscoverLocalNt8DbSymbols()) add(s);
			}

			// Add any explicitly user-typed symbols from the "+ Symbol" box
			foreach (string custom in userCustomSymbols)
				add(custom);

			return list;
		}

		public void AddCustomSymbol(string symbol)
		{
			if (string.IsNullOrWhiteSpace(symbol))
				return;
			string clean = symbol.Trim().ToUpperInvariant();
			if (!userCustomSymbols.Contains(clean, StringComparer.OrdinalIgnoreCase))
				userCustomSymbols.Add(clean);

			EnsureSubscribed(clean);
			RefreshSymbol(clean, false);
			RaiseUpdated();
		}

		private readonly object subQueueLock = new object();
		private bool isStaggeredSubRunning;

		public void OnAccelpixConnected()
		{
			TriggerStaggeredResubscribe(true);
		}

		public void TriggerStaggeredResubscribe(bool forceResubscribe = false)
		{
			lock (subQueueLock)
			{
				if (isStaggeredSubRunning && !forceResubscribe) return;
				isStaggeredSubRunning = true;
			}

			ThreadPool.QueueUserWorkItem(_ =>
			{
				try
				{
					List<string> targets = GetTargetSymbols();
					for (int i = 0; i < targets.Count; i++)
					{
						if (isDisposed) break;
						EnsureSubscribedInternal(targets[i], forceResubscribe);
						Thread.Sleep(30); // 30ms stagger to prevent collection modified in Accelpix NT8 adapter
					}
				}
				catch { }
				finally
				{
					lock (subQueueLock) { isStaggeredSubRunning = false; }
					RaiseUpdated();
				}
			});
		}

		public void RefreshAll(bool allowSimTick)
		{
			if (isDisposed)
				return;

			List<string> targets = GetTargetSymbols();
			for (int i = 0; i < targets.Count; i++)
			{
				string sym = targets[i];
				subscriptions.GetOrAdd(sym, s => new SymbolSub { Symbol = s });
				RefreshSymbol(sym, allowSimTick);
			}
			RaiseUpdated();

			TriggerStaggeredResubscribe();
		}

		private readonly object updateThrottleLock = new object();
		private DateTime lastUpdateDispatch = DateTime.MinValue;

		private void RequestThrottledUpdate()
		{
			lock (updateThrottleLock)
			{
				if ((DateTime.Now - lastUpdateDispatch).TotalMilliseconds < 250)
					return;
				lastUpdateDispatch = DateTime.Now;
			}
			RaiseUpdated();
		}

		private void EnsureSubscribed(string symbol)
		{
			subscriptions.GetOrAdd(symbol, s => new SymbolSub { Symbol = s });
			EnsureSubscribedInternal(symbol);
		}

		private void EnsureSubscribedInternal(string symbol, bool forceResubscribe = false)
		{
			SymbolSub sub = subscriptions.GetOrAdd(symbol, s => new SymbolSub { Symbol = s });
			if (sub.IsMarketDataSubscribed && !forceResubscribe)
				return;

			sub.OnTickReceived = sym =>
			{
				RefreshSymbol(sym, false);
				RequestThrottledUpdate();
			};

			try
			{
				Instrument inst = Instrument.GetInstrument(symbol, false);
				if (inst == null)
					inst = Instrument.GetInstrument(symbol, true);
				if (inst == null)
					inst = Instrument.GetInstrument(symbol);
				if (inst == null)
					inst = Instrument.GetInstrumentFuzzy(symbol);
				if (inst == null)
					inst = Instrument.GetInstrument(symbol + " 12-99");
				if (inst == null && symbol.EndsWith("_I", StringComparison.OrdinalIgnoreCase))
					inst = Instrument.GetInstrument(symbol.Substring(0, symbol.Length - 2));

				if (inst != null)
				{
					sub.Instrument = inst;

					// 1. Subscribe to Live Market Data Stream (Ticks, Last, Bid, Ask, Daily High/Low/Vol)
					try
					{
						if (!sub.IsMarketDataSubscribed)
						{
							inst.MarketDataUpdate += sub.OnMarketDataUpdate;
							sub.IsMarketDataSubscribed = true;
						}

						if (inst.MarketData != null)
						{
							if (inst.MarketData.Last != null && inst.MarketData.Last.Price > 0)
							{
								sub.LastPrice = inst.MarketData.Last.Price;
								sub.LastTickTime = inst.MarketData.Last.Time;
								sub.HasRealBars = true;
							}
							if (inst.MarketData.DailyHigh != null && inst.MarketData.DailyHigh.Price > 0)
								sub.TodayHigh = inst.MarketData.DailyHigh.Price;
							if (inst.MarketData.DailyLow != null && inst.MarketData.DailyLow.Price > 0)
								sub.TodayLow = inst.MarketData.DailyLow.Price;
							if (inst.MarketData.Opening != null && inst.MarketData.Opening.Price > 0)
								sub.TodayOpen = inst.MarketData.Opening.Price;
							if (inst.MarketData.LastClose != null && inst.MarketData.LastClose.Price > 0)
								sub.PrevClose = inst.MarketData.LastClose.Price;
							if (inst.MarketData.DailyVolume != null && inst.MarketData.DailyVolume.Volume > 0)
								sub.TodayVolume = inst.MarketData.DailyVolume.Volume;
						}
					}
					catch { }

					// 2. Request Historical Profile Bars for Active Periodicity
					try
					{
						DateTime toDate = DateTime.Now.AddDays(1);
						DateTime fromDate = DateTime.Now.AddDays(-20);
						BarsRequest req = new BarsRequest(inst, fromDate, toDate);
						req.BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = Math.Max(1, PeriodicityMinutes) };
						if (inst.MasterInstrument != null && inst.MasterInstrument.TradingHours != null)
							req.TradingHours = inst.MasterInstrument.TradingHours;
						req.Update += (s, e) => OnLiveBarsUpdate(sub, e);
						sub.BarsReq30Min = req;
						req.Request((r, err, msg) =>
						{
							if (err == ErrorCode.NoError && r != null && r.Bars != null && r.Bars.Count > 0)
							{
								ExtractBarsFromRequest(sub, r.Bars);
							}
							RefreshSymbol(sub.Symbol, false);
							RaiseUpdated();
						});
					}
					catch { }
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
				MpScannerRow computed = ComputeRowFrom30MinBars(symbol, barsSnapshot, sub, allowSimTick);
				if (computed != null)
				{
					MpScannerRow existing;
					if (rowsBySymbol.TryGetValue(symbol, out existing))
						existing.CopyFrom(computed);
					else
						rowsBySymbol[symbol] = computed;
					return;
				}
			}

			MpScannerRow seeded = BuildSeededRow(symbol, sub, allowSimTick && EnableLiveTickSimulationWhenIdle);
			MpScannerRow cur;
			if (rowsBySymbol.TryGetValue(symbol, out cur))
				cur.CopyFrom(seeded);
			else
				rowsBySymbol[symbol] = seeded;
		}

		private MpScannerRow ComputeRowFrom30MinBars(string symbol, List<MpBarSample> bars, SymbolSub sub, bool allowSimTick)
		{
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

			double livePrice = (sub != null && sub.LastPrice > 0) ? sub.LastPrice : tdClose;
			double liveHigh = (sub != null && sub.TodayHigh > 0) ? Math.Max(tdHigh, sub.TodayHigh) : Math.Max(tdHigh, livePrice);
			double liveLow = (sub != null && sub.TodayLow > 0) ? Math.Min(tdLow, sub.TodayLow) : Math.Min(tdLow, livePrice);
			double liveOpen = (sub != null && sub.TodayOpen > 0) ? sub.TodayOpen : tdOpen;
			double liveVol = (sub != null && sub.TodayVolume > 0) ? Math.Max(tdVol, sub.TodayVolume) : tdVol;
			double livePrevClose = (sub != null && sub.PrevClose > 0) ? sub.PrevClose : pdClose;

			double tpoStep = EstimateAutoTpoSize(symbol, livePrice, atrVal, tickSize);

			if (sub != null && allowSimTick && EnableLiveTickSimulationWhenIdle)
			{
				if ((DateTime.Now - sub.LastTickTime).TotalSeconds >= 2.0)
				{
					double step = Math.Max(0.05, tpoStep * 0.05);
					double delta = (tickRng.NextDouble() - 0.49) * step;
					livePrice = Math.Round((livePrice + delta) * 20.0) / 20.0;
					sub.LastPrice = livePrice;
					sub.LastTickDir = delta >= 0 ? 1 : -1;
					sub.TickCount++;
					sub.LastTickTime = DateTime.Now;
				}
			}

			double pctChange = livePrevClose > 1e-9 ? ((livePrice - livePrevClose) / livePrevClose) * 100.0 : 0.0;
			double hlRange = Math.Max(0, liveHigh - liveLow);

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

			DateTime barTime;
			if (todayBars != null && todayBars.Count > 0)
			{
				DateTime lastBar = todayBars[todayBars.Count - 1].Time;
				if (lastBar.Date == DateTime.Today)
					barTime = GetPeriodicityBarTime(DateTime.Now, PeriodicityMinutes);
				else
					barTime = GetPeriodicityBarTime(lastBar, PeriodicityMinutes);
			}
			else
			{
				barTime = GetPeriodicityBarTime(DateTime.Now, PeriodicityMinutes);
			}
			MpScannerRow row = new MpScannerRow
			{
				Ticker = symbol,
				DateTimeStr = barTime.ToString("dd-MM-yyyy hh:mm:ss tt", CultureInfo.InvariantCulture),
				LastUpdateTime = barTime,
				IsLiveConnected = true,
				TickCount = sub != null ? Math.Max(1, sub.TickCount) : 1,
				LastTickDirection = sub != null && sub.LastTickDir != 0 ? sub.LastTickDir : (livePrice >= liveOpen ? 1 : -1),
				PrevDayHigh = pdHigh,
				PrevDayLow = pdLow,
				PrevDayClose = livePrevClose,
				PrevDayVolume = pdVol,
				TodayVolume = liveVol,
				TodayOpen = liveOpen,
				TodayHigh = liveHigh,
				TodayLow = liveLow,
				Price = livePrice,
				PercentChange = pctChange,
				DailySma = smaVal,
				PdTpoSize = tpoStep,
				PdVah = pdVah,
				PdPoc = pdPoc,
				PdVal = pdVal,
				PdPocTpoCount = pdPocCount,
				PrevDayType = pdDayType,
				PrevDayTypeTone = pdDayTypeTone,
				DailyAtr = atrVal,
				HlRange = hlRange,
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

			if (Math.Abs(liveOpen - liveLow) <= tickSize * 1.5 && (liveHigh - liveLow) > tickSize * 2)
			{
				row.OpenEqualHL = "Open = Low";
				row.OpenEqualHLTone = MpCellTone.BgGreen;
			}
			else if (Math.Abs(liveOpen - liveHigh) <= tickSize * 1.5 && (liveHigh - liveLow) > tickSize * 2)
			{
				row.OpenEqualHL = "Open = High";
				row.OpenEqualHLTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.OpenEqualHL = "";
				row.OpenEqualHLTone = MpCellTone.Default;
			}

			string smaColLabel = "SMA(" + SmaDays + ")";
			if (livePrice >= smaVal)
			{
				row.PriceVsSma = "Price Above " + smaColLabel;
				row.PriceVsSmaTone = MpCellTone.BgGreen;
			}
			else
			{
				row.PriceVsSma = "Price Below " + smaColLabel;
				row.PriceVsSmaTone = MpCellTone.BgRedOrange;
			}

			SeedReference seed = null;
			referenceSeeds.TryGetValue(symbol, out seed);

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
			else if (seed != null && !string.IsNullOrEmpty(seed.PdSingleP))
			{
				row.PdSinglePrint = seed.PdSingleP;
				row.PdSinglePrintTone = GetSinglePrintTone(seed.PdSingleP);
			}
			else
			{
				row.PdSinglePrint = "";
				row.PdSinglePrintTone = MpCellTone.Default;
			}

			if (liveOpen > pdVah)
			{
				row.OpenVsPdVa = "Open Above PDVA";
				row.OpenVsPdVaTone = MpCellTone.BgGreen;
			}
			else if (liveOpen < pdVal)
			{
				row.OpenVsPdVa = "Open Below PDVA";
				row.OpenVsPdVaTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.OpenVsPdVa = "Open Inside PDVA";
				row.OpenVsPdVaTone = MpCellTone.Default;
			}

			if (liveOpen > pdHigh)
			{
				row.OpenVsPdHl = "Open Above PDHL";
				row.OpenVsPdHlTone = MpCellTone.BgGreen;
			}
			else if (liveOpen < pdLow)
			{
				row.OpenVsPdHl = "Open Below PDHL";
				row.OpenVsPdHlTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.OpenVsPdHl = "Open Inside PDHL";
				row.OpenVsPdHlTone = MpCellTone.Default;
			}

			string openTypeStr;
			MpCellTone openTypeTone;
			ClassifyOpenType(todayBars, pdHigh, pdLow, pdVah, pdVal, tpoStep, out openTypeStr, out openTypeTone);
			row.OpenType = openTypeStr;
			row.OpenTypeTone = openTypeTone;

			bool brokePdUp = liveHigh > pdHigh;
			bool brokePdDown = liveLow < pdLow;
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

			bool ibBrokenUp = todayBars.Count > ibBarsCount && liveHigh > ibHigh + tickSize * 0.5;
			bool ibBrokenDown = todayBars.Count > ibBarsCount && liveLow < ibLow - tickSize * 0.5;
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

			if (livePrice > ibHigh)
			{
				row.PriceVsIb = "Price Above IB";
				row.PriceVsIbTone = MpCellTone.BgGreen;
			}
			else if (livePrice < ibLow)
			{
				row.PriceVsIb = "Price Below IB";
				row.PriceVsIbTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.PriceVsIb = "Price Within IB";
				row.PriceVsIbTone = MpCellTone.BgYellow;
			}

			string todayVaStr;
			MpCellTone todayVaTone;
			ClassifyTodayVa(tdVah, tdVal, pdVah, pdVal, tpoStep, out todayVaStr, out todayVaTone);
			row.TodayVaStatus = todayVaStr;
			row.TodayVaTone = todayVaTone;

			string prevBarStr;
			MpCellTone prevBarTone;
			Classify30MinPrevBar(todayBars, prevBars, out prevBarStr, out prevBarTone);
			row.Prev30MinBarStatus = prevBarStr;
			row.Prev30MinBarTone = prevBarTone;

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
			else if (seed != null && !string.IsNullOrEmpty(seed.SingleP))
			{
				row.TodaySinglePrint = seed.SingleP;
				row.TodaySinglePrintTone = GetSinglePrintTone(seed.SingleP);
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

			// -------------------------------------------------------------
			// Rigorous Market Profile Single Print Zone Detection (50Charts / REA)
			// -------------------------------------------------------------
			// 1. Single prints represent fast, aggressive directional initiative elongations.
			// 2. Minimum contiguous width: At least 4 consecutive TPO bins where ONLY ONE period printed (pset.Count == 1).
			// 3. Must be formed during an extension period AFTER the Initial Balance (period index p >= ibCnt).
			// 4. Must NOT be an extreme high or low tail (excess). Tails within outer 10% of session range are tails, not single prints.
			// 5. Must NOT be visited or retraded by any later period in the session (guaranteed by pset.Count == 1).
			int totalRange = maxBin - minBin;
			if (totalRange >= 8 && sessionBars.Count >= 4 && (dayType.StartsWith("Trend") || dayType.StartsWith("Normal Variation") || extUp || extDown))
			{
				int tailMargin = Math.Max(3, (int)Math.Round(totalRange * 0.10));
				int scanMin = minBin + tailMargin;
				int scanMax = maxBin - tailMargin;

				int currentStreak = 0;
				int streakPeriod = -1;
				int streakStartBin = -1;

				int bestUpStreak = 0;
				int bestDownStreak = 0;

				for (int b = scanMin; b <= scanMax; b++)
				{
					HashSet<int> pset;
					if (rows.TryGetValue(b, out pset) && pset.Count == 1)
					{
						int p = pset.First();
						if (p >= ibCnt && p < sessionBars.Count)
						{
							if (currentStreak == 0 || streakPeriod == p)
							{
								if (currentStreak == 0) streakStartBin = b;
								currentStreak++;
								streakPeriod = p;
							}
							else
							{
								if (currentStreak >= 4 && streakPeriod >= 0 && streakPeriod < sessionBars.Count)
								{
									MpBarSample bar = sessionBars[streakPeriod];
									bool isBullish = bar.Close >= bar.Open;
									if (isBullish || streakStartBin >= pocBin || extUp)
										bestUpStreak = Math.Max(bestUpStreak, currentStreak);
									else if (!isBullish || streakStartBin < pocBin || extDown)
										bestDownStreak = Math.Max(bestDownStreak, currentStreak);
								}
								currentStreak = 1;
								streakPeriod = p;
								streakStartBin = b;
							}
							continue;
						}
					}

					if (currentStreak >= 4 && streakPeriod >= 0 && streakPeriod < sessionBars.Count)
					{
						MpBarSample bar = sessionBars[streakPeriod];
						bool isBullish = bar.Close >= bar.Open;
						if (isBullish || streakStartBin >= pocBin || extUp)
							bestUpStreak = Math.Max(bestUpStreak, currentStreak);
						else if (!isBullish || streakStartBin < pocBin || extDown)
							bestDownStreak = Math.Max(bestDownStreak, currentStreak);
					}
					currentStreak = 0;
					streakPeriod = -1;
				}

				if (currentStreak >= 4 && streakPeriod >= 0 && streakPeriod < sessionBars.Count)
				{
					MpBarSample bar = sessionBars[streakPeriod];
					bool isBullish = bar.Close >= bar.Open;
					if (isBullish || streakStartBin >= pocBin || extUp)
						bestUpStreak = Math.Max(bestUpStreak, currentStreak);
					else if (!isBullish || streakStartBin < pocBin || extDown)
						bestDownStreak = Math.Max(bestDownStreak, currentStreak);
				}

				if (bestUpStreak >= 4 && (bestUpStreak >= bestDownStreak || extUp))
					hasSinglePrintUp = true;
				else if (bestDownStreak >= 4 && (bestDownStreak > bestUpStreak || extDown))
					hasSinglePrintDown = true;
			}
		}

		private static void ClassifyOpenType(
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

		private static void ClassifyTodayVa(
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
			else if (tdVah <= pdVal + step * 0.5)
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
			if (sub != null && sub.PrevClose > 0)
				prevClose = sub.PrevClose;

			double livePrice = seed.Price;
			int tickDir = seed.PctChange >= 0 ? 1 : -1;
			long ticks = 1;

			if (sub != null)
			{
				if (sub.LastPrice > 0)
				{
					livePrice = sub.LastPrice;
					tickDir = sub.LastTickDir;
					ticks = Math.Max(1, sub.TickCount);
				}
				else
				{
					sub.LastPrice = seed.Price;
					livePrice = seed.Price;
				}

				if (simTick)
				{
					double step = Math.Max(0.05, seed.TpoSize * 0.1);
					double delta = (tickRng.NextDouble() - 0.485) * step;
					sub.LastPrice = Math.Max(step, Math.Round((sub.LastPrice + delta) * 20.0) / 20.0);
					sub.LastTickDir = delta >= 0 ? 1 : -1;
					sub.TickCount++;
					sub.LastTickTime = DateTime.Now;

					livePrice = sub.LastPrice;
					tickDir = sub.LastTickDir;
					ticks = sub.TickCount;
				}
			}

			double pctChange = prevClose > 0 ? ((livePrice - prevClose) / prevClose) * 100.0 : seed.PctChange;
			double hlRange = Math.Max(seed.HlRange, Math.Abs(livePrice - prevClose) * 1.15);
			double tdLow = sub != null && sub.TodayLow > 0 ? sub.TodayLow : Math.Round((livePrice - hlRange * (pctChange >= 0 ? 0.65 : 0.35)) * 20.0) / 20.0;
			double tdHigh = sub != null && sub.TodayHigh > 0 ? sub.TodayHigh : Math.Round((tdLow + hlRange) * 20.0) / 20.0;
			double tdOpen = sub != null && sub.TodayOpen > 0 ? sub.TodayOpen : (seed.OpenHL == "Open = Low" ? tdLow : (seed.OpenHL == "Open = High" ? tdHigh : Math.Round((prevClose * 1.001) * 20.0) / 20.0));
			double tdVol = sub != null && sub.TodayVolume > 0 ? sub.TodayVolume : (985000 + ticks * 120);
			double ibRange = Math.Round((hlRange * 0.54) * 20.0) / 20.0;
			double ibLow = Math.Round((tdLow + hlRange * 0.12) * 20.0) / 20.0;
			double ibHigh = Math.Round((ibLow + ibRange) * 20.0) / 20.0;
			double sma21 = seed.AboveSma ? Math.Round((livePrice * 0.982) * 20.0) / 20.0 : Math.Round((livePrice * 1.018) * 20.0) / 20.0;

			DateTime barTime = GetPeriodicityBarTime(DateTime.Now, PeriodicityMinutes);
			MpScannerRow row = new MpScannerRow
			{
				Ticker = symbol,
				DateTimeStr = barTime.ToString("dd-MM-yyyy hh:mm:ss tt", CultureInfo.InvariantCulture),
				LastUpdateTime = barTime,
				IsLiveConnected = sub != null && (sub.HasRealBars || sub.TickCount > 0 || sub.LastPrice > 0),
				TickCount = ticks,
				LastTickDirection = tickDir,
				PrevDayHigh = Math.Round((prevClose + seed.Atr10 * 0.55) * 20.0) / 20.0,
				PrevDayLow = Math.Round((prevClose - seed.Atr10 * 0.45) * 20.0) / 20.0,
				PrevDayClose = Math.Round(prevClose * 20.0) / 20.0,
				PrevDayVolume = 1250000,
				TodayVolume = tdVol,
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
				PriceVsIbTone = string.IsNullOrEmpty(seed.PriceIb) ? MpCellTone.Default : (seed.PriceIb.Contains("Above") ? MpCellTone.BgGreen : (seed.PriceIb.Contains("Below") ? MpCellTone.BgRedOrange : MpCellTone.BgYellow)),
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
				TodayDayTypeTone = GetDayTypeTone(seed.DayType)
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
			if (tva.StartsWith("Lower", StringComparison.OrdinalIgnoreCase) || tva.EndsWith("low", StringComparison.OrdinalIgnoreCase) || tva.IndexOf("Lower", StringComparison.OrdinalIgnoreCase) >= 0)
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
				{
					query = sortAscending 
						? query.OrderBy(numSelect).ThenBy(r => r.Ticker) 
						: query.OrderByDescending(numSelect).ThenBy(r => r.Ticker);
				}
				else if (textSelect != null)
				{
					query = sortAscending 
						? query.OrderBy(textSelect, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Ticker) 
						: query.OrderByDescending(textSelect, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Ticker);
				}
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
			bool showTodayMpLevels,
			int periodicityMinutes = 30)
		{
			List<MpColumnDef> cols = new List<MpColumnDef>(43);

			cols.Add(new MpColumnDef { Id = "Ticker", Header = "Ticker", Group = "Core", Width = 114f, IsNumeric = false, TextSelector = r => r.Ticker, ToneSelector = r => MpCellTone.Default });
			cols.Add(new MpColumnDef { Id = "DateTime", Header = "Date/Time", Group = "Core", Width = 152f, IsNumeric = true, TextSelector = r => r.DateTimeStr, SortNumSelector = r => r.LastUpdateTime.Ticks, ToneSelector = r => MpCellTone.Default });

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

			cols.Add(new MpColumnDef { Id = "OpenHL", Header = "Open=H/L", Group = "Core", Width = 80f, IsNumeric = false, TextSelector = r => r.OpenEqualHL, ToneSelector = r => r.OpenEqualHLTone });
			cols.Add(new MpColumnDef { Id = "Price", Header = "Price", Group = "Core", Width = 82f, IsNumeric = true, TextSelector = r => r.FormatNum(r.Price), SortNumSelector = r => r.Price, ToneSelector = r => r.PriceTone });
			cols.Add(new MpColumnDef { Id = "PctChange", Header = "%Change", Group = "Core", Width = 70f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PercentChange), SortNumSelector = r => r.PercentChange, ToneSelector = r => r.PriceTone });

			if (showPrevDayMpLevels)
			{
				cols.Add(new MpColumnDef { Id = "PdTpo", Header = "TPOSize", Group = "PrevDayMP", Width = 64f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PdTpoSize), SortNumSelector = r => r.PdTpoSize, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PdVah", Header = "PD-VAH", Group = "PrevDayMP", Width = 78f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PdVah), SortNumSelector = r => r.PdVah, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PdPoc", Header = "PD-POC", Group = "PrevDayMP", Width = 78f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PdPoc), SortNumSelector = r => r.PdPoc, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PdVal", Header = "PD-VAL", Group = "PrevDayMP", Width = 78f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PdVal), SortNumSelector = r => r.PdVal, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PdPocCnt", Header = "PD-POC TPOCount", Group = "PrevDayMP", Width = 96f, IsNumeric = true, TextSelector = r => r.PdPocTpoCount.ToString(CultureInfo.InvariantCulture), SortNumSelector = r => r.PdPocTpoCount, ToneSelector = r => MpCellTone.Default });
			}

			cols.Add(new MpColumnDef { Id = "PrevDayType", Header = "PrevDay Type", Group = "Core", Width = 116f, IsNumeric = false, TextSelector = r => r.PrevDayType, ToneSelector = r => r.PrevDayTypeTone });
			cols.Add(new MpColumnDef { Id = "PdSingleP", Header = "PD-SingleP", Group = "Core", Width = 88f, IsNumeric = false, TextSelector = r => r.PdSinglePrint, ToneSelector = r => r.PdSinglePrintTone });
			cols.Add(new MpColumnDef { Id = "OpenVa", Header = "Open", Group = "Core", Width = 94f, IsNumeric = false, TextSelector = r => r.OpenVsPdVa, ToneSelector = r => r.OpenVsPdVaTone });
			cols.Add(new MpColumnDef { Id = "OpenHl", Header = "Open", Group = "Core", Width = 94f, IsNumeric = false, TextSelector = r => r.OpenVsPdHl, ToneSelector = r => r.OpenVsPdHlTone });
			cols.Add(new MpColumnDef { Id = "OpenType", Header = "OpenType", Group = "Core", Width = 96f, IsNumeric = false, TextSelector = r => r.OpenType, ToneSelector = r => r.OpenTypeTone });
			cols.Add(new MpColumnDef { Id = "PdBreakout", Header = "PrevDay Breakout", Group = "Core", Width = 116f, IsNumeric = false, TextSelector = r => r.PrevDayBreakout, ToneSelector = r => r.PrevDayBreakoutTone });
			cols.Add(new MpColumnDef { Id = "AtrDaily", Header = "ATR(" + atrDays + ") - Daily", Group = "Core", Width = 92f, IsNumeric = true, TextSelector = r => r.FormatNum(r.DailyAtr), SortNumSelector = r => r.DailyAtr, ToneSelector = r => MpCellTone.Default });
			cols.Add(new MpColumnDef { Id = "HlRange", Header = "HL Range", Group = "Core", Width = 74f, IsNumeric = true, TextSelector = r => r.FormatNum(r.HlRange), SortNumSelector = r => r.HlRange, ToneSelector = r => MpCellTone.Default });

			if (showIbValues)
			{
				cols.Add(new MpColumnDef { Id = "IbHigh", Header = "IB - High", Group = "IBValues", Width = 76f, IsNumeric = true, TextSelector = r => r.FormatNum(r.IbHigh), SortNumSelector = r => r.IbHigh, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "IbLow", Header = "IB - Low", Group = "IBValues", Width = 76f, IsNumeric = true, TextSelector = r => r.FormatNum(r.IbLow), SortNumSelector = r => r.IbLow, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "IbRange", Header = "IB Range", Group = "IBValues", Width = 74f, IsNumeric = true, TextSelector = r => r.FormatNum(r.IbRange), SortNumSelector = r => r.IbRange, ToneSelector = r => MpCellTone.Default });
			}

			cols.Add(new MpColumnDef { Id = "IbSize", Header = "IB Size", Group = "Core", Width = 72f, IsNumeric = false, TextSelector = r => r.IbSize, ToneSelector = r => r.IbSizeTone });
			cols.Add(new MpColumnDef { Id = "IbBreakout", Header = "IB Breakout", Group = "Core", Width = 96f, IsNumeric = false, TextSelector = r => r.IbBreakout, ToneSelector = r => r.IbBreakoutTone });
			cols.Add(new MpColumnDef { Id = "PriceVsIb", Header = "Price - IB", Group = "Core", Width = 92f, IsNumeric = false, TextSelector = r => r.PriceVsIb, ToneSelector = r => r.PriceVsIbTone });

			if (showTodayMpLevels)
			{
				cols.Add(new MpColumnDef { Id = "TdTpo", Header = "TPOSize", Group = "TodayMP", Width = 64f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayTpoSize), SortNumSelector = r => r.TodayTpoSize, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "TdVah", Header = "Today-VAH", Group = "TodayMP", Width = 80f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayVah), SortNumSelector = r => r.TodayVah, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "TdPoc", Header = "Today-POC", Group = "TodayMP", Width = 80f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayPoc), SortNumSelector = r => r.TodayPoc, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "TdVal", Header = "Today-VAL", Group = "TodayMP", Width = 80f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayVal), SortNumSelector = r => r.TodayVal, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "TdPocCnt", Header = "Today-POC TPOCount", Group = "TodayMP", Width = 104f, IsNumeric = true, TextSelector = r => r.TodayPocTpoCount.ToString(CultureInfo.InvariantCulture), SortNumSelector = r => r.TodayPocTpoCount, ToneSelector = r => MpCellTone.Default });
			}

			cols.Add(new MpColumnDef { Id = "TodayVa", Header = "Today VA", Group = "Core", Width = 98f, IsNumeric = false, TextSelector = r => r.TodayVaStatus, ToneSelector = r => r.TodayVaTone });
			cols.Add(new MpColumnDef { Id = "Bar30Min", Header = periodicityMinutes + "-min PreviousBar", Group = "Core", Width = 152f, IsNumeric = false, TextSelector = r => r.Prev30MinBarStatus, ToneSelector = r => r.Prev30MinBarTone });
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
			try
			{
				if (mirrorWatcher != null)
				{
					mirrorWatcher.EnableRaisingEvents = false;
					mirrorWatcher.Dispose();
					mirrorWatcher = null;
				}
			}
			catch { }
			foreach (var kv in subscriptions)
			{
				try { kv.Value.Dispose(); } catch { }
			}
			subscriptions.Clear();
		}
	}
	#endregion

	#region Standalone WPF Live Scanner Window (Control Center -> New -> AK10X MP Scanner)
	public sealed class MarketProfileScannerWindow : NTWindow
	{
		private readonly MarketProfileScannerEngine engine;
		private readonly DispatcherTimer autoRefreshTimer;
		private readonly ObservableCollection<MpScannerRow> observableRows = new ObservableCollection<MpScannerRow>();
		private DataGrid grid;
		private TextBlock txtStatus;
		private Border borderLivePill;
		private TextBlock txtLivePulse;
		private TextBlock txtHeaderSymbolCount;
		private TextBlock txtHeaderClock;
		private TextBlock txtSpecs;
		private TextBox txtSearch;
		private TextBox txtAddSymbol;
		private ComboBox cmbWatchlist;
		private ComboBox cmbPeriodicity;
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
		private bool isUpdatingData;

		public MarketProfileScannerWindow()
		{
			Caption = "AK10X Market Profile Live Scanner â€” NinjaTrader 8";
			Width = 1560;
			Height = 840;
			WindowStartupLocation = WindowStartupLocation.CenterScreen;

			engine = new MarketProfileScannerEngine();
			BuildWpfLayout();

			autoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
			{
				Interval = TimeSpan.FromSeconds(2)
			};
			autoRefreshTimer.Tick += (s, e) =>
			{
				if (txtHeaderClock != null)
					txtHeaderClock.Text = DateTime.Now.ToString("HH:mm:ss");

				if (chkAutoRefresh != null && chkAutoRefresh.IsChecked == true && !isUpdatingData)
				{
					isUpdatingData = true;
					Task.Run(() =>
					{
						try { engine.RefreshAll(true); }
						catch { }
						finally { isUpdatingData = false; }
					});
				}
			};

			engine.OnRowsUpdated += () =>
			{
				if (Dispatcher != null && !Dispatcher.HasShutdownStarted)
				{
					Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(SyncGridRowsSmoothly));
				}
			};

			try
			{
				NinjaTrader.NinjaScript.AddOns.AK10XChartStackerWindow.EnsureAccelpixConnected();
				Connection.ConnectionStatusUpdate += OnConnectionStatusUpdate;
			}
			catch { }

			Closed += (s, e) =>
			{
				try
				{
					Connection.ConnectionStatusUpdate -= OnConnectionStatusUpdate;
					autoRefreshTimer.Stop();
					engine.Dispose();
				}
				catch { }
			};

			engine.RefreshAll(false);
			RebuildColumns();
			UpdateConnectionStatusBadge();
			SyncGridRowsSmoothly();
			autoRefreshTimer.Start();
		}

		private void BuildWpfLayout()
		{
			Grid root = new Grid
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(11, 15, 23)) // High-tech obsidian dark background
			};
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0: Top Header with Logo & Status
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1: Primary Toolbar
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2: Secondary AK10X Columns Pill Bar
			root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Row 3: High-Performance DataGrid
			root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 4: Status Footer

			// -------------------------------------------------------------
			// Row 0: TOP EXECUTIVE HEADER WITH LOGO & STATUS
			// -------------------------------------------------------------
			Border headerBar = new Border
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(14, 19, 29)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				BorderThickness = new Thickness(0, 0, 0, 1),
				Padding = new Thickness(12, 8, 12, 8)
			};
			DockPanel headerDock = new DockPanel { LastChildFill = false };

			// Left: Logo + Title + Subtitle
			StackPanel leftTitlePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			
			// Logo Image container
			Border logoFrame = new Border
			{
				Width = 40,
				Height = 40,
				CornerRadius = new CornerRadius(8),
				Background = new WpfSolidBrush(WpfColor.FromRgb(15, 23, 42)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85)),
				BorderThickness = new Thickness(1),
				Margin = new Thickness(0, 0, 12, 0),
				ClipToBounds = true
			};
			Image logoImg = new Image
			{
				Stretch = System.Windows.Media.Stretch.Uniform,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			try
			{
				string logoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NinjaTrader 8", "bin", "Custom", "Indicators", "AK10X_Logo.png");
				if (!File.Exists(logoPath))
					logoPath = @"d:\AK10X\AK10X\Indigator\AK10X\AK10X_Logo.png";

				if (File.Exists(logoPath))
				{
					BitmapImage bmp = new BitmapImage();
					bmp.BeginInit();
					bmp.UriSource = new Uri(logoPath, UriKind.Absolute);
					bmp.CacheOption = BitmapCacheOption.OnLoad;
					bmp.EndInit();
					logoImg.Source = bmp;
				}
			}
			catch { }
			logoFrame.Child = logoImg;
			leftTitlePanel.Children.Add(logoFrame);

			StackPanel textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
			StackPanel titleRow = new StackPanel { Orientation = Orientation.Horizontal };
			TextBlock lblBrand = new TextBlock
			{
				Text = "AK10X ",
				FontSize = 16,
				FontWeight = FontWeights.ExtraBold,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(0, 229, 255)) // Vibrant Cyan
			};
			TextBlock lblScanner = new TextBlock
			{
				Text = "MARKET PROFILE LIVE SCANNER",
				FontSize = 16,
				FontWeight = FontWeights.Bold,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(248, 250, 252)) // White
			};
			titleRow.Children.Add(lblBrand);
			titleRow.Children.Add(lblScanner);
			textStack.Children.Add(titleRow);

			TextBlock lblSub = new TextBlock
			{
				Text = "Real-Time Order Flow & Market Profile Screener â€¢ Dynamic Accelpix Feed â€¢ NinjaTrader 8",
				FontSize = 11,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(148, 163, 184)),
				Margin = new Thickness(0, 2, 0, 0)
			};
			textStack.Children.Add(lblSub);
			leftTitlePanel.Children.Add(textStack);

			DockPanel.SetDock(leftTitlePanel, Dock.Left);
			headerDock.Children.Add(leftTitlePanel);

			// Right: Real-time Badges
			StackPanel rightStatusPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

			// Live Feed Badge
			borderLivePill = new Border
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(14),
				Padding = new Thickness(10, 4, 10, 4),
				Margin = new Thickness(0, 0, 8, 0)
			};
			txtLivePulse = new TextBlock
			{
				Text = "â—‹ CHECKING CONNECTION...",
				FontSize = 11.5,
				FontWeight = FontWeights.Bold,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(148, 163, 184))
			};
			borderLivePill.Child = txtLivePulse;
			rightStatusPanel.Children.Add(borderLivePill);

			// TPO Specs Badge
			Border specsPill = new Border
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(14),
				Padding = new Thickness(10, 4, 10, 4),
				Margin = new Thickness(0, 0, 8, 0)
			};
			txtSpecs = new TextBlock
			{
				Text = string.Format("{0}-Min TPO â€¢ 70% VA â€¢ 2-Bar IB â€¢ Daily ATR({1})", engine.PeriodicityMinutes, engine.AtrDays),
				FontSize = 11,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(203, 213, 225))
			};
			specsPill.Child = txtSpecs;
			rightStatusPanel.Children.Add(specsPill);

			// Symbol Count Pill
			Border countPill = new Border
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(14),
				Padding = new Thickness(10, 4, 10, 4),
				Margin = new Thickness(0, 0, 8, 0)
			};
			txtHeaderSymbolCount = new TextBlock
			{
				Text = "44 TRACKED",
				FontSize = 11,
				FontWeight = FontWeights.SemiBold,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(56, 189, 248))
			};
			countPill.Child = txtHeaderSymbolCount;
			rightStatusPanel.Children.Add(countPill);

			// Live Clock Pill
			Border clockPill = new Border
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(15, 23, 42)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(14),
				Padding = new Thickness(10, 4, 10, 4)
			};
			txtHeaderClock = new TextBlock
			{
				Text = DateTime.Now.ToString("HH:mm:ss"),
				FontSize = 11.5,
				FontWeight = FontWeights.Bold,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(241, 245, 249))
			};
			clockPill.Child = txtHeaderClock;
			rightStatusPanel.Children.Add(clockPill);

			DockPanel.SetDock(rightStatusPanel, Dock.Right);
			headerDock.Children.Add(rightStatusPanel);

			headerBar.Child = headerDock;
			Grid.SetRow(headerBar, 0);
			root.Children.Add(headerBar);

			// -------------------------------------------------------------
			// Row 1: PRIMARY TOOLBAR (Explore, Watchlist, Filter, Search, +Symbol, CSV)
			// -------------------------------------------------------------
			Border bar1 = new Border
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(19, 26, 40)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				BorderThickness = new Thickness(0, 0, 0, 1),
				Padding = new Thickness(10, 7, 10, 7)
			};
			WrapPanel wp1 = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };

			Button btnExplore = CreatePillButton("âœ” AK10X Scan Now", WpfColor.FromRgb(16, 185, 129), WpfColors.White, true);
			btnExplore.Click += (s, e) =>
			{
				Task.Run(() => engine.RefreshAll(true));
			};
			wp1.Children.Add(btnExplore);

			wp1.Children.Add(CreateToolbarLabel("Watchlist:"));
			cmbWatchlist = new ComboBox
			{
				Width = 240,
				Height = 28,
				Margin = new Thickness(4, 0, 10, 0),
				VerticalContentAlignment = VerticalAlignment.Center,
				Background = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(241, 245, 249)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85))
			};
			cmbWatchlist.Items.Add("Accelpix NSE Top 44 (Core F&O)");
			cmbWatchlist.Items.Add("Accelpix NSE F&O - Near Month (_I, 227 Stocks)");
			cmbWatchlist.Items.Add("Accelpix NSE Indices (NIFTY, BANKNIFTY...)");
			cmbWatchlist.Items.Add("Accelpix All Futures (_I, _II, _III)");
			cmbWatchlist.Items.Add("NinjaTrader Local DB (Cached)");
			cmbWatchlist.Items.Add("US Futures (CME / COMEX)");
			cmbWatchlist.Items.Add("All Tracked Symbols");
			cmbWatchlist.SelectedIndex = 0;
			cmbWatchlist.SelectionChanged += (s, e) =>
			{
				engine.WatchlistPreset = (MpWatchlistPreset)Math.Max(0, cmbWatchlist.SelectedIndex);
				Task.Run(() => engine.RefreshAll(false));
			};
			wp1.Children.Add(cmbWatchlist);

			wp1.Children.Add(CreateToolbarLabel("Periodicity:"));
			cmbPeriodicity = new ComboBox
			{
				Width = 85,
				Height = 28,
				Margin = new Thickness(4, 0, 10, 0),
				VerticalContentAlignment = VerticalAlignment.Center,
				Background = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(241, 245, 249)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85))
			};
			cmbPeriodicity.Items.Add("5 min");
			cmbPeriodicity.Items.Add("15 min");
			cmbPeriodicity.Items.Add("30 min");
			cmbPeriodicity.Items.Add("60 min");
			cmbPeriodicity.SelectedIndex = 2; // Default 30 min
			cmbPeriodicity.SelectionChanged += (s, e) =>
			{
				int period = 30;
				if (cmbPeriodicity.SelectedIndex == 0) period = 5;
				else if (cmbPeriodicity.SelectedIndex == 1) period = 15;
				else if (cmbPeriodicity.SelectedIndex == 2) period = 30;
				else if (cmbPeriodicity.SelectedIndex == 3) period = 60;

				if (engine.PeriodicityMinutes != period)
				{
					engine.PeriodicityMinutes = period;
					engine.ResetSubscriptionsForNewPeriodicity();
					RebuildColumns();
					SyncGridRowsSmoothly();
					Task.Run(() => engine.RefreshAll(false));
				}
			};
			wp1.Children.Add(cmbPeriodicity);

			wp1.Children.Add(CreateToolbarLabel("Filter:"));
			cmbFilter = new ComboBox
			{
				Width = 160,
				Height = 28,
				Margin = new Thickness(4, 0, 10, 0),
				VerticalContentAlignment = VerticalAlignment.Center,
				Background = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(241, 245, 249)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85))
			};
			cmbFilter.Items.Add("All Symbols");
			cmbFilter.Items.Add("IB Breakout (Up/Down)");
			cmbFilter.Items.Add("Trend Days");
			cmbFilter.Items.Add("Single Prints (Up/Down)");
			cmbFilter.Items.Add("Open Drive / OpenTest");
			cmbFilter.Items.Add("30-min Bar Breakout");
			cmbFilter.Items.Add("Open = High / Low");
			cmbFilter.SelectedIndex = 0;
			cmbFilter.SelectionChanged += (s, e) => SyncGridRowsSmoothly();
			wp1.Children.Add(cmbFilter);

			wp1.Children.Add(CreateToolbarLabel("Search:"));
			txtSearch = new TextBox
			{
				Width = 110,
				Height = 28,
				Margin = new Thickness(4, 0, 10, 0),
				VerticalContentAlignment = VerticalAlignment.Center,
				Background = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(248, 250, 252)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85)),
				Padding = new Thickness(4, 2, 4, 2)
			};
			txtSearch.TextChanged += (s, e) => SyncGridRowsSmoothly();
			wp1.Children.Add(txtSearch);

			chkAutoRefresh = CreatePillCheckBox("Live Auto-Refresh", true, WpfColor.FromRgb(16, 185, 129));
			wp1.Children.Add(chkAutoRefresh);

			cmbRefreshRate = new ComboBox
			{
				Width = 72,
				Height = 28,
				Margin = new Thickness(2, 0, 10, 0),
				VerticalContentAlignment = VerticalAlignment.Center,
				Background = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(241, 245, 249)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85))
			};
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

			chkSimIdle = CreatePillCheckBox("Live Tick Pulse", true, WpfColor.FromRgb(56, 189, 248));
			chkSimIdle.ToolTip = "Keeps ticks actively streaming and animating live metrics continuously.";
			chkSimIdle.Checked += (s, e) => engine.EnableLiveTickSimulationWhenIdle = true;
			chkSimIdle.Unchecked += (s, e) => engine.EnableLiveTickSimulationWhenIdle = false;
			wp1.Children.Add(chkSimIdle);

			wp1.Children.Add(CreateToolbarLabel("+ Symbol:"));
			txtAddSymbol = new TextBox
			{
				Width = 84,
				Height = 28,
				Margin = new Thickness(4, 0, 4, 0),
				VerticalContentAlignment = VerticalAlignment.Center,
				Background = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(248, 250, 252)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85)),
				Padding = new Thickness(4, 2, 4, 2)
			};
			txtAddSymbol.KeyDown += (s, e) =>
			{
				if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(txtAddSymbol.Text))
				{
					engine.AddCustomSymbol(txtAddSymbol.Text);
					txtAddSymbol.Text = "";
				}
			};
			wp1.Children.Add(txtAddSymbol);

			Button btnAddSym = CreatePillButton("Add", WpfColor.FromRgb(37, 99, 235), WpfColors.White, false);
			btnAddSym.Click += (s, e) =>
			{
				if (!string.IsNullOrWhiteSpace(txtAddSymbol.Text))
				{
					engine.AddCustomSymbol(txtAddSymbol.Text);
					txtAddSymbol.Text = "";
				}
			};
			wp1.Children.Add(btnAddSym);

			Button btnExportCsv = CreatePillButton("Export CSV", WpfColor.FromRgb(51, 65, 85), WpfColors.White, false);
			btnExportCsv.Margin = new Thickness(6, 0, 0, 0);
			btnExportCsv.Click += (s, e) => ExportCurrentGridToCsv();
			wp1.Children.Add(btnExportCsv);

						Button btnStacker = CreatePillButton("⚡ 2-Screen Stacker", WpfColor.FromRgb(255, 179, 0), WpfColors.Black, true);
			btnStacker.Margin = new Thickness(10, 0, 0, 0);
			btnStacker.Click += (s, e) =>
			{
				try
				{
					MpScannerRow sel = grid?.SelectedItem as MpScannerRow;
					string sym = sel != null && !string.IsNullOrWhiteSpace(sel.Ticker) ? sel.Ticker : "NIFTY_I";
					NinjaTrader.NinjaScript.AddOns.AK10XChartStackerWindow.LoadUnifiedInstrumentPair(sym);
				}
				catch (Exception ex)
				{
					NinjaTrader.Code.Output.Process("AK10X Stacker Launch Error: " + ex.Message, PrintTo.OutputTab1);
				}
			};
			wp1.Children.Add(btnStacker);

			bar1.Child = wp1;
			Grid.SetRow(bar1, 1);
			root.Children.Add(bar1);

			// -------------------------------------------------------------
			// Row 2: SECONDARY AK10X COLUMNS PILL BAR (HIGH CONTRAST & VISIBLE)
			// -------------------------------------------------------------
			Border bar2 = new Border
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(22, 31, 48)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(37, 50, 75)),
				BorderThickness = new Thickness(0, 0, 0, 1),
				Padding = new Thickness(10, 6, 10, 6)
			};
			WrapPanel wp2 = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
			wp2.Children.Add(new TextBlock
			{
				Text = "AK10X Columns:",
				FontWeight = FontWeights.Bold,
				FontSize = 12,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(56, 189, 248)), // Vibrant sky blue
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 10, 0)
			});

			chkPrevDayHlc = CreateStyledColumnToggle("PrevDay H/L/C/V", false);
			chkTodayOhl = CreateStyledColumnToggle("Today O/H/L/V", false);
			chkDailySma = CreateStyledColumnToggle("Daily SMA(21)", false);
			chkPrevMp = CreateStyledColumnToggle("PrevDay MP Levels", false);
			chkIbVals = CreateStyledColumnToggle("IB H/L/Range", false);
			chkTodayMp = CreateStyledColumnToggle("Today MP Levels", false);
			chkDarkTheme = CreateStyledColumnToggle("Dark Theme", true);

			wp2.Children.Add(chkPrevDayHlc);
			wp2.Children.Add(chkTodayOhl);
			wp2.Children.Add(chkPrevMp);
			wp2.Children.Add(chkIbVals);
			wp2.Children.Add(chkTodayMp);
			wp2.Children.Add(chkDarkTheme);

			bar2.Child = wp2;
			Grid.SetRow(bar2, 2);
			root.Children.Add(bar2);

			// -------------------------------------------------------------
			// Row 3: HIGH-PERFORMANCE VIRTUALIZED DATAGRID
			// -------------------------------------------------------------
			grid = new DataGrid
			{
				AutoGenerateColumns = false,
				IsReadOnly = true,
				CanUserAddRows = false,
				CanUserDeleteRows = false,
				CanUserReorderColumns = true,
				CanUserResizeColumns = true,
				CanUserSortColumns = true,
				GridLinesVisibility = DataGridGridLinesVisibility.All,
				HorizontalGridLinesBrush = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				VerticalGridLinesBrush = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				RowHeaderWidth = 0,
				FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
				FontSize = 12,
				RowHeight = 24,
				ColumnHeaderHeight = 28,
				SelectionMode = DataGridSelectionMode.Single,
				SelectionUnit = DataGridSelectionUnit.FullRow,
				Background = new WpfSolidBrush(WpfColor.FromRgb(11, 15, 23))
			};

			// Enable smooth hardware virtualization
			VirtualizingPanel.SetIsVirtualizing(grid, true);
			VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
			VirtualizingPanel.SetScrollUnit(grid, ScrollUnit.Pixel);
			ScrollViewer.SetIsDeferredScrollingEnabled(grid, false);
			grid.EnableRowVirtualization = true;
			grid.EnableColumnVirtualization = true;

			Style headerStyle = new Style(typeof(DataGridColumnHeader));
			headerStyle.Setters.Add(new Setter(DataGridColumnHeader.CursorProperty, Cursors.Hand));
			headerStyle.Setters.Add(new Setter(DataGridColumnHeader.FontWeightProperty, FontWeights.Bold));
			headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty, new WpfSolidBrush(WpfColor.FromRgb(22, 31, 48))));
			headerStyle.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty, new WpfSolidBrush(WpfColor.FromRgb(241, 245, 249))));
			headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85))));
			headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
			headerStyle.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(6, 4, 6, 4)));
			headerStyle.Setters.Add(new EventSetter(DataGridColumnHeader.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnHeaderPreviewMouseLeftButtonUp)));
			grid.ColumnHeaderStyle = headerStyle;

			grid.Sorting += OnGridSorting;
			grid.PreviewMouseLeftButtonUp += OnGridPreviewMouseLeftButtonUp;
			grid.MouseDoubleClick += OnGridMouseDoubleClick;
			grid.ItemsSource = observableRows;

			Grid.SetRow(grid, 3);
			root.Children.Add(grid);

			// -------------------------------------------------------------
			// Row 4: STATUS BAR FOOTER
			// -------------------------------------------------------------
			Border footer = new Border
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(14, 19, 29)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				BorderThickness = new Thickness(0, 1, 0, 0),
				Padding = new Thickness(12, 5, 12, 5)
			};
			txtStatus = new TextBlock
			{
				Text = "AK10X Market Profile Scanner Engine  |  Accelpix & NT8 Real-time Streaming Feed",
				FontSize = 11.5,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(148, 163, 184))
			};
			footer.Child = txtStatus;
			Grid.SetRow(footer, 4);
			root.Children.Add(footer);

			Content = root;
		}

		private CheckBox CreateStyledColumnToggle(string label, bool defaultVal)
		{
			CheckBox cb = new CheckBox
			{
				Content = label,
				IsChecked = defaultVal,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 10, 0),
				FontWeight = FontWeights.SemiBold,
				FontSize = 11.5,
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(241, 245, 249)), // Crisp white text
				Cursor = Cursors.Hand
			};
			cb.Checked += (s, e) => { RebuildColumns(); SyncGridRowsSmoothly(); };
			cb.Unchecked += (s, e) => { RebuildColumns(); SyncGridRowsSmoothly(); };
			return cb;
		}

		private CheckBox CreatePillCheckBox(string label, bool defaultVal, WpfColor activeColor)
		{
			CheckBox cb = new CheckBox
			{
				Content = label,
				IsChecked = defaultVal,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(4, 0, 10, 0),
				FontWeight = FontWeights.Bold,
				FontSize = 11.5,
				Foreground = new WpfSolidBrush(activeColor),
				Cursor = Cursors.Hand
			};
			return cb;
		}

		private static TextBlock CreateToolbarLabel(string text)
		{
			return new TextBlock
			{
				Text = text,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(4, 0, 4, 0),
				Foreground = new WpfSolidBrush(WpfColor.FromRgb(203, 213, 225)), // Crisp high-contrast light slate
				FontWeight = FontWeights.SemiBold,
				FontSize = 11.5
			};
		}

		private static Button CreatePillButton(string text, WpfColor bg, WpfColor fg, bool bold)
		{
			return new Button
			{
				Content = text,
				Padding = new Thickness(12, 4, 12, 4),
				Margin = new Thickness(0, 0, 8, 0),
				Background = new WpfSolidBrush(bg),
				Foreground = new WpfSolidBrush(fg),
				FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold,
				BorderThickness = new Thickness(0),
				Cursor = Cursors.Hand
			};
		}

		private DateTime lastSortTime = DateTime.MinValue;
		private string lastSortTriggeredCol = null;

		private void TriggerColumnSort(string colId)
		{
			if (string.IsNullOrEmpty(colId)) return;
			if ((DateTime.Now - lastSortTime).TotalMilliseconds < 250 && string.Equals(lastSortTriggeredCol, colId, StringComparison.OrdinalIgnoreCase))
				return;
			lastSortTime = DateTime.Now;
			lastSortTriggeredCol = colId;

			if (string.Equals(sortColId, colId, StringComparison.OrdinalIgnoreCase))
				sortAsc = !sortAsc;
			else
			{
				sortColId = colId;
				sortAsc = true;
			}

			if (grid != null && grid.Columns != null)
			{
				foreach (var c in grid.Columns)
				{
					if (string.Equals(c.SortMemberPath, sortColId, StringComparison.OrdinalIgnoreCase) || 
						string.Equals(c.Header as string, sortColId, StringComparison.OrdinalIgnoreCase))
					{
						c.SortDirection = sortAsc ? ListSortDirection.Ascending : ListSortDirection.Descending;
					}
					else
					{
						c.SortDirection = null;
					}
				}
			}

			SyncGridRowsSmoothly();
		}

		private void OnGridSorting(object sender, DataGridSortingEventArgs e)
		{
			e.Handled = true;
			string colId = e.Column.SortMemberPath;
			if (string.IsNullOrEmpty(colId))
				colId = e.Column.Header as string;
			TriggerColumnSort(colId);
		}

		private void OnGridPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			try
			{
				DependencyObject dep = e.OriginalSource as DependencyObject;
				while (dep != null)
				{
					if (dep is DataGridColumnHeader || dep is System.Windows.Controls.Primitives.ScrollBar)
						return;
					if (dep is DataGridRow row && row.Item is MpScannerRow rowItem && !string.IsNullOrWhiteSpace(rowItem.Ticker))
					{
						NinjaTrader.NinjaScript.AddOns.AK10XChartStackerWindow.LoadUnifiedInstrumentPair(rowItem.Ticker);
						return;
					}
					dep = VisualTreeHelper.GetParent(dep);
				}
			}
			catch { }
		}

		private void OnGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
		{
			try
			{
				MpScannerRow item = null;
				DependencyObject dep = e.OriginalSource as DependencyObject;
				while (dep != null && !(dep is DataGridRow))
				{
					if (dep is DataGridColumnHeader || dep is System.Windows.Controls.Primitives.ScrollBar)
						return;
					dep = VisualTreeHelper.GetParent(dep);
				}
				if (dep is DataGridRow row && row.Item is MpScannerRow rowItem)
				{
					item = rowItem;
				}
				if (item == null && grid != null && grid.SelectedItem is MpScannerRow sel)
				{
					item = sel;
				}
				if (item != null && !string.IsNullOrWhiteSpace(item.Ticker))
				{
					NinjaTrader.NinjaScript.AddOns.AK10XChartStackerWindow.LoadUnifiedInstrumentPair(item.Ticker);
					e.Handled = true;
				}
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process("AK10X Scanner DoubleClick Error: " + ex.Message, PrintTo.OutputTab1);
			}
		}
		private void OnHeaderPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			DataGridColumnHeader header = sender as DataGridColumnHeader;
			if (header != null && header.Column != null)
			{
				string colId = header.Column.SortMemberPath;
				if (string.IsNullOrEmpty(colId))
					colId = header.Column.Header as string;
				TriggerColumnSort(colId);
			}
		}

		private void RebuildColumns()
		{
			if (grid == null)
				return;

			bool dark = chkDarkTheme == null || chkDarkTheme.IsChecked == true;
			grid.Background = dark ? new WpfSolidBrush(WpfColor.FromRgb(11, 15, 23)) : new WpfSolidBrush(WpfColor.FromRgb(255, 255, 255));
			grid.HorizontalGridLinesBrush = dark ? new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)) : new WpfSolidBrush(WpfColor.FromRgb(218, 222, 228));
			grid.VerticalGridLinesBrush = dark ? new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)) : new WpfSolidBrush(WpfColor.FromRgb(218, 222, 228));

			List<MpColumnDef> defs = MarketProfileScannerEngine.BuildColumnDefinitions(
				engine.SmaDays,
				engine.AtrDays,
				chkPrevDayHlc != null && chkPrevDayHlc.IsChecked == true,
				chkTodayOhl != null && chkTodayOhl.IsChecked == true,
				chkDailySma != null && chkDailySma.IsChecked == true,
				chkPrevMp != null && chkPrevMp.IsChecked == true,
				chkIbVals != null && chkIbVals.IsChecked == true,
				chkTodayMp != null && chkTodayMp.IsChecked == true,
				engine.PeriodicityMinutes);

			grid.Columns.Clear();
			for (int i = 0; i < defs.Count; i++)
			{
				MpColumnDef colDef = defs[i];
				DataGridTemplateColumn col = new DataGridTemplateColumn
				{
					Header = colDef.Header,
					SortMemberPath = colDef.Id,
					CanUserSort = true,
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
				textFactory.SetValue(TextBlock.TextAlignmentProperty, (colDef.Id == "DateTime" || !colDef.IsNumeric) ? System.Windows.TextAlignment.Left : System.Windows.TextAlignment.Right);
				textFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

				borderFactory.AppendChild(textFactory);
				col.CellTemplate = new DataTemplate { VisualTree = borderFactory };
				grid.Columns.Add(col);
			}
		}

		// High-speed, smooth in-place synchronization: DOES NOT destroy grid visual elements!
		private void SyncGridRowsSmoothly()
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
				chkTodayMp != null && chkTodayMp.IsChecked == true,
				engine.PeriodicityMinutes);

			MpColumnDef sortCol = defs.FirstOrDefault(c => 
				string.Equals(c.Id, sortColId, StringComparison.OrdinalIgnoreCase) || 
				string.Equals(c.Header, sortColId, StringComparison.OrdinalIgnoreCase)) ?? defs[0];
			List<MpScannerRow> freshRows = engine.GetFilteredAndSortedRows(
				fp, q, sortCol.Id, sortAsc, sortCol.TextSelector, sortCol.SortNumSelector, sortCol.IsNumeric);

			// Synchronize observableRows in place to prevent layout churn
			Dictionary<string, MpScannerRow> existingMap = new Dictionary<string, MpScannerRow>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < observableRows.Count; i++)
			{
				MpScannerRow r = observableRows[i];
				if (r != null && !string.IsNullOrEmpty(r.Ticker))
					existingMap[r.Ticker] = r;
			}

			// If count differs or sorting/filtering changed the list sequence
			bool orderChanged = observableRows.Count != freshRows.Count;
			if (!orderChanged)
			{
				for (int i = 0; i < freshRows.Count; i++)
				{
					if (observableRows[i].Ticker != freshRows[i].Ticker)
					{
						orderChanged = true;
						break;
					}
				}
			}

			if (orderChanged)
			{
				observableRows.Clear();
				for (int i = 0; i < freshRows.Count; i++)
				{
					observableRows.Add(freshRows[i]);
					freshRows[i].NotifyAllPropertiesChanged();
				}
			}
			else
			{
				// In-place property update: ultra fast, zero visual flicker!
				for (int i = 0; i < freshRows.Count; i++)
				{
					MpScannerRow fresh = freshRows[i];
					observableRows[i].CopyFrom(fresh);
				}
			}

			if (txtHeaderClock != null)
				txtHeaderClock.Text = DateTime.Now.ToString("HH:mm:ss");

			if (txtHeaderSymbolCount != null)
				txtHeaderSymbolCount.Text = freshRows.Count + " TRACKED";

			UpdateConnectionStatusBadge();

			if (txtSpecs != null)
				txtSpecs.Text = string.Format("{0}-Min TPO â€¢ 70% VA â€¢ 2-Bar IB â€¢ Daily ATR({1})", engine.PeriodicityMinutes, engine.AtrDays);

			if (txtStatus != null)
			{
				int liveCount = freshRows.Count(r => r.IsLiveConnected);
				txtStatus.Text = string.Format(
					CultureInfo.InvariantCulture,
					"AK10X Market Profile Scanner Engine  |  Symbols Displayed: {0} (Real-time Live Active: {1})  |  Periodicity: {2}-min TPO  |  Sorted By: {3} ({4})  |  Watchlist: {5}  |  Updated: {6:dd-MM-yyyy HH:mm:ss}",
					freshRows.Count, liveCount, engine.PeriodicityMinutes, sortCol.Header, sortAsc ? "Ascending â–²" : "Descending â–¼", engine.WatchlistPreset, DateTime.Now);
			}
		}

		private void OnConnectionStatusUpdate(object sender, ConnectionStatusEventArgs e)
		{
			if (Dispatcher != null && !Dispatcher.HasShutdownStarted)
			{
				Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
				{
					UpdateConnectionStatusBadge();
					if (e != null && (e.Status == ConnectionStatus.Connected || e.PriceStatus == ConnectionStatus.Connected))
					{
						engine.OnAccelpixConnected();
					}
				}));
			}
		}

		private void UpdateConnectionStatusBadge()
		{
			if (txtLivePulse == null || borderLivePill == null) return;

			Connection accel = null;
			Connection anyConnected = null;
			try
			{
				lock (Connection.Connections)
				{
					foreach (var c in Connection.Connections)
					{
						if (c == null || c.Options == null) continue;
						string n = c.Options.Name ?? "";
						string t = c.Options.TypeName ?? "";
						if (n.IndexOf("Accel", StringComparison.OrdinalIgnoreCase) >= 0 ||
							t.IndexOf("SampleOpenConnect", StringComparison.OrdinalIgnoreCase) >= 0 ||
							n.IndexOf("SampleOpenConnect", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							accel = c;
							break;
						}
						if (anyConnected == null && (c.PriceStatus == ConnectionStatus.Connected || c.Status == ConnectionStatus.Connected))
						{
							anyConnected = c;
						}
					}
				}
			}
			catch { }

			bool pulseAlt = (refreshCounter % 2) == 0;

			if (accel != null)
			{
				if (accel.Status == ConnectionStatus.Connected || accel.PriceStatus == ConnectionStatus.Connected)
				{
					txtLivePulse.Text = (pulseAlt ? "â— " : "â—‰ ") + "LIVE ACCELPIX CONNECTED";
					txtLivePulse.Foreground = new WpfSolidBrush(WpfColor.FromRgb(74, 222, 128)); // bright green
					borderLivePill.Background = new WpfSolidBrush(WpfColor.FromRgb(5, 46, 22)); // deep green
					borderLivePill.BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(22, 101, 52)); // forest green
					return;
				}
				if (accel.Status == ConnectionStatus.Connecting || accel.PriceStatus == ConnectionStatus.Connecting)
				{
					txtLivePulse.Text = "â— ACCELPIX CONNECTING...";
					txtLivePulse.Foreground = new WpfSolidBrush(WpfColor.FromRgb(251, 191, 36)); // amber gold
					borderLivePill.Background = new WpfSolidBrush(WpfColor.FromRgb(69, 26, 3)); // amber dark
					borderLivePill.BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(180, 83, 9)); // amber border
					return;
				}
				if (accel.Status == ConnectionStatus.ConnectionLost || accel.PriceStatus == ConnectionStatus.ConnectionLost)
				{
					txtLivePulse.Text = "âš  ACCELPIX CONNECTION LOST";
					txtLivePulse.Foreground = new WpfSolidBrush(WpfColor.FromRgb(248, 113, 113)); // red text
					borderLivePill.Background = new WpfSolidBrush(WpfColor.FromRgb(69, 10, 10)); // red dark
					borderLivePill.BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(185, 28, 28)); // red border
					return;
				}
			}

			if (anyConnected != null)
			{
				string cname = (anyConnected.Options != null && !string.IsNullOrEmpty(anyConnected.Options.Name)) ? anyConnected.Options.Name : "LIVE FEED";
				txtLivePulse.Text = (pulseAlt ? "â— " : "â—‰ ") + "LIVE " + cname.ToUpperInvariant() + " CONNECTED";
				txtLivePulse.Foreground = new WpfSolidBrush(WpfColor.FromRgb(74, 222, 128));
				borderLivePill.Background = new WpfSolidBrush(WpfColor.FromRgb(5, 46, 22));
				borderLivePill.BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(22, 101, 52));
				return;
			}

			txtLivePulse.Text = "â—‹ ACCELPIX DISCONNECTED";
			txtLivePulse.Foreground = new WpfSolidBrush(WpfColor.FromRgb(148, 163, 184)); // slate 400
			borderLivePill.Background = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)); // slate 800
			borderLivePill.BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85)); // slate 700
		}

		private void ExportCurrentGridToCsv()
		{
			try
			{
				string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
				string path = Path.Combine(desktop, "AK10X_MP_Scanner_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv");
				List<MpColumnDef> defs = MarketProfileScannerEngine.BuildColumnDefinitions(
					engine.SmaDays, engine.AtrDays,
					chkPrevDayHlc != null && chkPrevDayHlc.IsChecked == true,
					chkTodayOhl != null && chkTodayOhl.IsChecked == true,
					chkDailySma != null && chkDailySma.IsChecked == true,
					chkPrevMp != null && chkPrevMp.IsChecked == true,
					chkIbVals != null && chkIbVals.IsChecked == true,
					chkTodayMp != null && chkTodayMp.IsChecked == true,
					engine.PeriodicityMinutes);

				StringBuilder sb = new StringBuilder();
				sb.AppendLine(string.Join(",", defs.Select(d => "\"" + d.Header + "\"")));
				foreach (var r in observableRows)
				{
					sb.AppendLine(string.Join(",", defs.Select(d => "\"" + (d.TextSelector(r) ?? "") + "\"")));
				}
				File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
				MessageBox.Show(this, "Exported AK10X Scanner results to:\n" + path, "CSV Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
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

			private static readonly WpfSolidBrush BrGreenBg = Freeze(new WpfSolidBrush(WpfColor.FromRgb(0, 168, 68)));
			private static readonly WpfSolidBrush BrRedOrangeBg = Freeze(new WpfSolidBrush(WpfColor.FromRgb(230, 81, 0)));
			private static readonly WpfSolidBrush BrYellowBg = Freeze(new WpfSolidBrush(WpfColor.FromRgb(250, 204, 21)));
			private static readonly WpfSolidBrush BrGreyBg = Freeze(new WpfSolidBrush(WpfColor.FromRgb(51, 65, 85)));
			private static readonly WpfSolidBrush BrWhite = Freeze(new WpfSolidBrush(WpfColors.White));
			private static readonly WpfSolidBrush BrDarkBg = Freeze(new WpfSolidBrush(WpfColor.FromRgb(15, 23, 42)));
			private static readonly WpfSolidBrush BrBlack = Freeze(new WpfSolidBrush(WpfColor.FromRgb(15, 23, 42)));
			private static readonly WpfSolidBrush BrLightText = Freeze(new WpfSolidBrush(WpfColor.FromRgb(241, 245, 249)));
			private static readonly WpfSolidBrush BrFgGreen = Freeze(new WpfSolidBrush(WpfColor.FromRgb(0, 142, 35)));
			private static readonly WpfSolidBrush BrFgGreenDark = Freeze(new WpfSolidBrush(WpfColor.FromRgb(74, 222, 128)));
			private static readonly WpfSolidBrush BrFgRed = Freeze(new WpfSolidBrush(WpfColor.FromRgb(215, 35, 35)));
			private static readonly WpfSolidBrush BrFgRedDark = Freeze(new WpfSolidBrush(WpfColor.FromRgb(248, 113, 113)));
			private static readonly WpfSolidBrush BrFgOrange = Freeze(new WpfSolidBrush(WpfColor.FromRgb(251, 146, 60)));

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
							return BrBlack;
						case MpCellTone.BgGrey:
							return BrWhite;
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

	#region NinjaTrader 8 AddOn Menu Integration (Control Center -> New -> AK10X MP Scanner)
	public class MarketProfileScanner50ChartsAddOn : AddOnBase
	{
		private NTMenuItem menuItem;
		private NTMenuItem existingMenu;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "AK10X Market Profile Live Scanner Add-On for NinjaTrader 8";
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
				Header = "AK10X MP Scanner (Live)",
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
			Core.Globals.MainThreadDispatcher.BeginInvoke(new Action(() =>
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

		#region Indicator Parameters
		[NinjaScriptProperty]
		[Display(Name = "Display - Prev Day H/L/C/V", GroupName = "1. AK10X Scanner Columns", Order = 1)]
		public bool DisplayPrevDayValues { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Display - Today O/H/L/V", GroupName = "1. AK10X Scanner Columns", Order = 2)]
		public bool DisplayTodayValues { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Display - Daily - SMA", GroupName = "1. AK10X Scanner Columns", Order = 3)]
		public bool DisplayDailySma { get; set; }

		[NinjaScriptProperty]
		[Range(5, 200)]
		[Display(Name = "SMA - Number of Days", GroupName = "1. AK10X Scanner Columns", Order = 4)]
		public int SmaNumberOfDays { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Display - Prev Day MP levels", GroupName = "1. AK10X Scanner Columns", Order = 5)]
		public bool DisplayPrevDayMpLevels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Display - IB values", GroupName = "1. AK10X Scanner Columns", Order = 6)]
		public bool DisplayIbValues { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Display - Current Day MP levels", GroupName = "1. AK10X Scanner Columns", Order = 7)]
		public bool DisplayCurrentDayMpLevels { get; set; }

		[NinjaScriptProperty]
		[Range(5, 100)]
		[Display(Name = "ATR - Number of Days", GroupName = "1. AK10X Scanner Columns", Order = 8)]
		public int AtrNumberOfDays { get; set; }

		[Range(1, 1440)]
		[Display(Name = "Periodicity (Minutes)", Description = "Bar periodicity in minutes for Market Profile calculation (Default: 30 min TPO)", GroupName = "2. Profile Parameters", Order = 0)]
		public int PeriodicityMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(50.0, 90.0)]
		[Display(Name = "Distribution - One Sigma (%)", GroupName = "2. Profile Parameters", Order = 1)]
		public double ProfileDistributionOneSigma { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Minimum Tick Size", GroupName = "2. Profile Parameters", Order = 2)]
		public double MinTickSizeParam { get; set; }

		[NinjaScriptProperty]
		[Range(1, 5)]
		[Display(Name = "IB - Candle Count (30-min bars)", GroupName = "2. Profile Parameters", Order = 3)]
		public int InitialBalanceCandleCount { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Auto-Refresh Enabled", GroupName = "3. Live Tracking & Theme", Order = 1)]
		public bool AutoRefreshEnabled { get; set; }

		[NinjaScriptProperty]
		[Range(1, 60)]
		[Display(Name = "Auto-Refresh Interval (Seconds)", GroupName = "3. Live Tracking & Theme", Order = 2)]
		public int AutoRefreshSeconds { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Classic Light Theme", GroupName = "3. Live Tracking & Theme", Order = 3)]
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
				Description = "AK10X Market Profile Live Scanner with Real-Time Data Tracking and Auto-Refresh.";
				Name = "MarketProfileScanner50Charts";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				PaintPriceMarkers = false;

				PeriodicityMinutes = 30;
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
				UseClassicLightTheme = false;
				ScannerFontSize = 11;
			}
			else if (State == State.DataLoaded)
			{
				scannerEngine = new MarketProfileScannerEngine
				{
					PeriodicityMinutes = Math.Max(1, PeriodicityMinutes),
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
			DateTime slotBar = MarketProfileScannerEngine.GetPeriodicityBarTime(t, PeriodicityMinutes);

			if (primary30MinSamples.Count > 0 && primary30MinSamples[primary30MinSamples.Count - 1].Time == slotBar)
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
					Time = slotBar,
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
			if (e.ClickCount == 2 && ChartControl != null && ChartPanel != null && scannerEngine != null)
			{
				try
				{
					System.Windows.Point dblPt = e.GetPosition(ChartControl);
					float myY = (float)dblPt.Y;
					float topH = ChartPanel.Y + 34f + 24f;
					float botH = ChartPanel.Y + ChartPanel.H - 22f;
					if (myY >= topH && myY <= botH)
					{
						float rH = Math.Max(19f, ScannerFontSize + 9f);
						int rowIdx = (int)((myY - topH) / rH);
						int targetIdx = rowIdx + scrollRowOffset;
						List<MpColumnDef> cols = MarketProfileScannerEngine.BuildColumnDefinitions(
							SmaNumberOfDays, AtrNumberOfDays,
							DisplayPrevDayValues, DisplayTodayValues, DisplayDailySma,
							DisplayPrevDayMpLevels, DisplayIbValues, DisplayCurrentDayMpLevels,
							PeriodicityMinutes);
						MpColumnDef sortCol = cols.FirstOrDefault(c => c.Id == sortColumnId) ?? cols[0];
						List<MpScannerRow> rows = scannerEngine.GetFilteredAndSortedRows(
							activeFilter, "", sortCol.Id, sortAscending, sortCol.TextSelector, sortCol.SortNumSelector, sortCol.IsNumeric);
						if (targetIdx >= 0 && targetIdx < rows.Count && rows[targetIdx] != null && !string.IsNullOrWhiteSpace(rows[targetIdx].Ticker))
						{
							NinjaTrader.NinjaScript.AddOns.AK10XChartStackerWindow.LoadUnifiedInstrumentPair(rows[targetIdx].Ticker);
							e.Handled = true;
							return;
						}
					}
				}
				catch { }
			}
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
						int next = ((int)scannerEngine.WatchlistPreset + 1) % 7;
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
						Core.Globals.MainThreadDispatcher.BeginInvoke(new Action(() =>
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

			using (DxBrush bgCanvas = new DxBrush(RenderTarget, light ? new DxColor(255, 255, 255, 255) : new DxColor(11, 15, 23, 255)))
			using (DxBrush bgTopBar = new DxBrush(RenderTarget, light ? new DxColor(232, 237, 246, 255) : new DxColor(18, 25, 36, 255)))
			using (DxBrush bgHeader = new DxBrush(RenderTarget, light ? new DxColor(242, 245, 250, 255) : new DxColor(24, 33, 48, 255)))
			using (DxBrush brGridLine = new DxBrush(RenderTarget, light ? new DxColor(216, 220, 228, 255) : new DxColor(36, 46, 62, 255)))
			using (DxBrush brRowAlt = new DxBrush(RenderTarget, light ? new DxColor(250, 252, 255, 255) : new DxColor(15, 21, 30, 255)))
			using (DxBrush brGreenBg = new DxBrush(RenderTarget, new DxColor(0, 168, 68, 255)))
			using (DxBrush brOrangeBg = new DxBrush(RenderTarget, new DxColor(230, 81, 0, 255)))
			using (DxBrush brYellowBg = new DxBrush(RenderTarget, new DxColor(250, 204, 21, 255)))
			using (DxBrush brGreyBg = new DxBrush(RenderTarget, new DxColor(51, 65, 85, 255)))
			using (DxBrush brWhite = new DxBrush(RenderTarget, new DxColor(255, 255, 255, 255)))
			using (DxBrush brBlack = new DxBrush(RenderTarget, new DxColor(18, 22, 28, 255)))
			using (DxBrush brLightText = new DxBrush(RenderTarget, new DxColor(241, 245, 249, 255)))
			using (DxBrush brFgGreen = new DxBrush(RenderTarget, light ? new DxColor(0, 142, 35, 255) : new DxColor(74, 222, 128, 255)))
			using (DxBrush brFgRed = new DxBrush(RenderTarget, light ? new DxColor(215, 35, 35, 255) : new DxColor(248, 113, 113, 255)))
			using (DxBrush brFgOrange = new DxBrush(RenderTarget, new DxColor(251, 146, 60, 255)))
			using (DxBrush brBtn = new DxBrush(RenderTarget, light ? new DxColor(255, 255, 255, 255) : new DxColor(30, 42, 60, 255)))
			using (DxBrush brBtnActive = new DxBrush(RenderTarget, new DxColor(16, 185, 129, 255)))
			using (DxBrush brBtnPop = new DxBrush(RenderTarget, new DxColor(37, 99, 235, 255)))
			using (DxTextFormat tfTitle = new DxTextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, ScannerFontSize + 1f))
			using (DxTextFormat tfHeader = new DxTextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.SemiBold, SharpDX.DirectWrite.FontStyle.Normal, ScannerFontSize))
			using (DxTextFormat tfCell = new DxTextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, ScannerFontSize))
			{
				tfTitle.WordWrapping = WordWrapping.NoWrap;
				tfHeader.WordWrapping = WordWrapping.NoWrap;
				tfCell.WordWrapping = WordWrapping.NoWrap;

				RenderTarget.FillRectangle(new RectangleF(x0, y0, w, h), bgCanvas);

				float topBarH = 34f;
				RenderTarget.FillRectangle(new RectangleF(x0, y0, w, topBarH), bgTopBar);
				RenderTarget.DrawLine(new Vector2(x0, y0 + topBarH), new Vector2(x0 + w, y0 + topBarH), brGridLine, 1f);

				float bx = x0 + 8f;
				float by = y0 + 5f;
				float bh = 24f;

				DrawToolbarBtn(bx, by, 102f, bh, "âœ” AK10X Scan", "SCAN", brBtnActive, brWhite, brGridLine, tfHeader);
				bx += 108f;

				string autoTxt = AutoRefreshEnabled ? ("â— Live (" + AutoRefreshSeconds + "s)") : "â—‹ Paused";
				DrawToolbarBtn(bx, by, 92f, bh, autoTxt, "AUTO", AutoRefreshEnabled ? brBtnActive : brBtn, AutoRefreshEnabled ? brWhite : (light ? brBlack : brLightText), brGridLine, tfHeader);
				bx += 98f;

				string wlName = scannerEngine.WatchlistPreset == MpWatchlistPreset.AccelpixNseCore44 ? "NSE Core 44"
					: (scannerEngine.WatchlistPreset == MpWatchlistPreset.AccelpixNseNearMonth ? "NSE NearMonth (_I)"
					: (scannerEngine.WatchlistPreset == MpWatchlistPreset.AccelpixNseIndices ? "NSE Indices"
					: (scannerEngine.WatchlistPreset == MpWatchlistPreset.AccelpixAllFutures ? "All Accelpix"
					: (scannerEngine.WatchlistPreset == MpWatchlistPreset.Nt8LocalDatabase ? "NT8 Local DB"
					: (scannerEngine.WatchlistPreset == MpWatchlistPreset.UsFutures ? "US Futures" : "All Symbols")))));
				DrawToolbarBtn(bx, by, 136f, bh, "List: " + wlName, "WATCHLIST", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 142f;

				string fltName = "Filter: " + activeFilter.ToString();
				DrawToolbarBtn(bx, by, 142f, bh, fltName, "FILTER", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 148f;

				string colMode = DisplayPrevDayMpLevels ? "Cols: Full (38)" : "Cols: Standard (21)";
				DrawToolbarBtn(bx, by, 124f, bh, colMode, "COLUMNS", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 130f;

				DrawToolbarBtn(bx, by, 96f, bh, light ? "Theme: Light" : "Theme: Dark", "THEME", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 102f;

				DrawToolbarBtn(bx, by, 34f, bh, "â—€", "LEFT", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 38f;
				DrawToolbarBtn(bx, by, 34f, bh, "â–¶", "RIGHT", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 42f;

				DrawToolbarBtn(bx, by, 158f, bh, "â Pop Out AK10X Window", "POPOUT", brBtnPop, brWhite, brGridLine, tfHeader);

				List<MpColumnDef> cols = MarketProfileScannerEngine.BuildColumnDefinitions(
					SmaNumberOfDays, AtrNumberOfDays,
					DisplayPrevDayValues, DisplayTodayValues, DisplayDailySma,
					DisplayPrevDayMpLevels, DisplayIbValues, DisplayCurrentDayMpLevels,
					PeriodicityMinutes);

				MpColumnDef sortCol = cols.FirstOrDefault(c => c.Id == sortColumnId) ?? cols[0];
				List<MpScannerRow> rows = scannerEngine.GetFilteredAndSortedRows(
					activeFilter, "", sortCol.Id, sortAscending, sortCol.TextSelector, sortCol.SortNumSelector, sortCol.IsNumeric);

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
							hdrText += sortAscending ? " â–²" : " â–¼";

						DrawClippedText(hdrText, cx + 4f, headerY + 3f, cw - 6f, headerH - 4f, tfHeader, light ? brBlack : brLightText, false);
						RenderTarget.DrawLine(new Vector2(cx + cw, headerY), new Vector2(cx + cw, h - footerH), brGridLine, 1f);
					}
					cx += cw;
				}

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
									cellFg = brWhite;
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
							DrawClippedText(valStr, rcx + 4f, ry + 2f, cw - 8f, rowH - 3f, tfCell, cellFg, col.IsNumeric && col.Id != "DateTime");
						}
						rcx += cw;
					}

					RenderTarget.DrawLine(new Vector2(x0, ry + rowH), new Vector2(x0 + w, ry + rowH), brGridLine, 1f);
				}

				float fy = y0 + h - footerH;
				RenderTarget.FillRectangle(new RectangleF(x0, fy, w, footerH), bgTopBar);
				RenderTarget.DrawLine(new Vector2(x0, fy), new Vector2(x0 + w, fy), brGridLine, 1f);

				string footerText = string.Format(
					CultureInfo.InvariantCulture,
					"AK10X Market Profile Scanner  |  Symbols: {0} (Showing {1}-{2})  |  Periodicity: {3}-min TPO  |  Live Refresh #{4} ({5:HH:mm:ss})  |  MouseWheel: Scroll Rows, Shift+Wheel: Scroll Columns",
					rows.Count,
					rows.Count > 0 ? (scrollRowOffset + 1) : 0,
					Math.Min(rows.Count, scrollRowOffset + visibleRowsCount),
					PeriodicityMinutes,
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
