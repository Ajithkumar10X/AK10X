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
			public List<MpBarSample> Raw1MinBars = new List<MpBarSample>();
			public List<MpBarSample> CachedBars = new List<MpBarSample>();
			public object Sync = new object();
			public long TickCount;
			public double LastPrice;
			public int LastTickDir;
			public DateTime LastTickTime;
			public bool HasRealBars;
			public DateTime LastBarsRequestAttempt;

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

		private readonly ConcurrentDictionary<string, SymbolSub> subscriptions = new ConcurrentDictionary<string, SymbolSub>(StringComparer.OrdinalIgnoreCase);
		private readonly ConcurrentDictionary<string, MpScannerRow> rowsBySymbol = new ConcurrentDictionary<string, MpScannerRow>(StringComparer.OrdinalIgnoreCase);
		private readonly List<string> userCustomSymbols = new List<string>();
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
					lock (sub.Sync)
					{
						if (sub.Raw1MinBars != null && sub.Raw1MinBars.Count > 0)
						{
							RebuildAggregatedBarsLocked(sub, PeriodicityMinutes);
						}
						else
						{
							sub.CachedBars.Clear();
							sub.HasRealBars = false;
						}
					}
				}
			}
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
			EnableLiveTickSimulationWhenIdle = false;
			WatchlistPreset = MpWatchlistPreset.AccelpixNseCore44;
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
			if (sub.IsMarketDataSubscribed && sub.HasRealBars && !forceResubscribe)
				return;
			if (!forceResubscribe && (DateTime.Now - sub.LastBarsRequestAttempt).TotalSeconds < 8.0)
				return;

			sub.LastBarsRequestAttempt = DateTime.Now;
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

					// 2. Request Real Historical 1-Minute Bars from Accelpix / NT8 and Aggregate to Session-Anchored Periodicity (Default 30-Min TPO)
					try
					{
						if (sub.BarsReq30Min != null)
						{
							try { sub.BarsReq30Min.Dispose(); } catch { }
							sub.BarsReq30Min = null;
						}

						DateTime toDate = DateTime.Now.AddDays(1);
						DateTime fromDate = DateTime.Now.AddDays(-38);
						BarsRequest req = new BarsRequest(inst, fromDate, toDate);
						req.BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 1 };
						if (inst.MasterInstrument != null && inst.MasterInstrument.TradingHours != null)
							req.TradingHours = inst.MasterInstrument.TradingHours;
						req.Update += (s, e) => OnLiveBarsUpdate(sub, e);
						sub.BarsReq30Min = req;

						using (ManualResetEventSlim doneEvt = new ManualResetEventSlim(false))
						{
							req.Request((r, err, msg) =>
							{
								try
								{
									if (err == ErrorCode.NoError && r != null && r.Bars != null && r.Bars.Count > 0)
									{
										ExtractBarsFromRequest(sub, r.Bars);
									}
									RefreshSymbol(sub.Symbol, false);
									RequestThrottledUpdate();
								}
								finally
								{
									try { doneEvt.Set(); } catch { }
								}
							});
							doneEvt.Wait(250);
						}
					}
					catch { }
				}
			}
			catch { }
		}

		private static void RebuildAggregatedBarsLocked(SymbolSub sub, int periodMinutes)
		{
			sub.CachedBars.Clear();
			if (sub.Raw1MinBars == null || sub.Raw1MinBars.Count == 0)
			{
				sub.HasRealBars = false;
				return;
			}

			int period = Math.Max(1, periodMinutes);
			MpBarSample curSlot = null;
			for (int i = 0; i < sub.Raw1MinBars.Count; i++)
			{
				MpBarSample m = sub.Raw1MinBars[i];
				if (m.Time.DayOfWeek == DayOfWeek.Saturday || m.Time.DayOfWeek == DayOfWeek.Sunday)
					continue;
				// NT8 1-minute bar timestamp is bar close time (e.g. 09:16:00 for 09:15:00-09:15:59).
				// Subtract 1 second so 09:16..09:45 maps to 09:15 slot (A), 09:46..10:15 maps to 09:45 slot (B), etc.
				DateTime effTime = m.Time.Second == 0 ? m.Time.AddSeconds(-1) : m.Time;
				DateTime slotTime = GetPeriodicityBarTime(effTime, period);

				if (curSlot != null && curSlot.Time == slotTime)
				{
					if (m.High > curSlot.High) curSlot.High = m.High;
					if (m.Low < curSlot.Low) curSlot.Low = m.Low;
					curSlot.Close = m.Close;
					curSlot.Volume += m.Volume;
				}
				else
				{
					curSlot = new MpBarSample
					{
						Time = slotTime,
						Open = m.Open,
						High = m.High,
						Low = m.Low,
						Close = m.Close,
						Volume = m.Volume
					};
					sub.CachedBars.Add(curSlot);
				}
			}

			sub.HasRealBars = sub.CachedBars.Count >= 2;
		}

		private void ExtractBarsFromRequest(SymbolSub sub, Bars bars)
		{
			if (bars == null)
				return;
			lock (sub.Sync)
			{
				sub.Raw1MinBars.Clear();
				int count = bars.Count;
				int start = Math.Max(0, count - 15000);
				for (int i = start; i < count; i++)
				{
					sub.Raw1MinBars.Add(new MpBarSample
					{
						Time = bars.GetTime(i),
						Open = bars.GetOpen(i),
						High = bars.GetHigh(i),
						Low = bars.GetLow(i),
						Close = bars.GetClose(i),
						Volume = bars.GetVolume(i)
					});
				}
				RebuildAggregatedBarsLocked(sub, PeriodicityMinutes);
				if (sub.CachedBars.Count >= 2)
				{
					double lastClose = sub.CachedBars[sub.CachedBars.Count - 1].Close;
					if (sub.LastPrice > 0 && Math.Abs(lastClose - sub.LastPrice) > 1e-9)
						sub.LastTickDir = lastClose > sub.LastPrice ? 1 : -1;
					sub.LastPrice = lastClose;
					sub.LastTickTime = sub.CachedBars[sub.CachedBars.Count - 1].Time;
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

						MpBarSample sample1m = new MpBarSample
						{
							Time = e.BarsSeries.GetTime(idx),
							Open = e.BarsSeries.GetOpen(idx),
							High = e.BarsSeries.GetHigh(idx),
							Low = e.BarsSeries.GetLow(idx),
							Close = close,
							Volume = e.BarsSeries.GetVolume(idx)
						};

						if (sample1m.Time.DayOfWeek != DayOfWeek.Saturday && sample1m.Time.DayOfWeek != DayOfWeek.Sunday)
						{
							if (sub.Raw1MinBars.Count > 0 && sub.Raw1MinBars[sub.Raw1MinBars.Count - 1].Time == sample1m.Time)
								sub.Raw1MinBars[sub.Raw1MinBars.Count - 1] = sample1m;
							else
								sub.Raw1MinBars.Add(sample1m);

							DateTime effTime = sample1m.Time.Second == 0 ? sample1m.Time.AddSeconds(-1) : sample1m.Time;
							DateTime slotTime = GetPeriodicityBarTime(effTime, PeriodicityMinutes);
							if (sub.CachedBars.Count > 0 && sub.CachedBars[sub.CachedBars.Count - 1].Time == slotTime)
							{
								RebuildAggregatedBarsLocked(sub, PeriodicityMinutes);
							}
							else
							{
								sub.CachedBars.Add(new MpBarSample
								{
									Time = slotTime,
									Open = sample1m.Open,
									High = sample1m.High,
									Low = sample1m.Low,
									Close = sample1m.Close,
									Volume = sample1m.Volume
								});
							}
							sub.HasRealBars = sub.CachedBars.Count >= 2;
						}
					}
				}
				RefreshSymbol(sub.Symbol, false);
				RaiseUpdated();
			}
			catch { }
		}

		public void UpdateFromChartIndicatorBars(string symbol, List<MpBarSample> chart30MinSamples)
		{
			if (string.IsNullOrWhiteSpace(symbol) || chart30MinSamples == null || chart30MinSamples.Count < 2)
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
					if (sub.CachedBars.Count >= 2)
						barsSnapshot = new List<MpBarSample>(sub.CachedBars);
				}
			}

			if (barsSnapshot != null && barsSnapshot.Count >= 2)
			{
				MpScannerRow computed = ComputeRowFrom30MinBars(symbol, barsSnapshot, sub);
				if (computed != null)
				{
					MpScannerRow existing;
					if (rowsBySymbol.TryGetValue(symbol, out existing))
						existing.CopyFrom(computed);
					else
						rowsBySymbol[symbol] = computed;
				}
			}
		}

		private MpScannerRow ComputeRowFrom30MinBars(string symbol, List<MpBarSample> bars, SymbolSub sub)
		{
			var dayGroups = bars
				.Where(b => b.Time.DayOfWeek != DayOfWeek.Saturday && b.Time.DayOfWeek != DayOfWeek.Sunday)
				.GroupBy(b => b.Time.Date)
				.OrderBy(g => g.Key)
				.Select(g => g.OrderBy(b => b.Time).ToList())
				.Where(g => g.Count > 0)
				.ToList();

			if (dayGroups.Count < 2)
				return null;

			List<MpBarSample> todayBars = dayGroups[dayGroups.Count - 1];
			List<MpBarSample> prevBars = dayGroups[dayGroups.Count - 2];

			double tickSize = Math.Max(0.0001, DefaultMinTickSize);
			if (sub != null && sub.Instrument != null && sub.Instrument.MasterInstrument != null && sub.Instrument.MasterInstrument.TickSize > 0)
				tickSize = sub.Instrument.MasterInstrument.TickSize;

			// 1. Daily OHLCV Series & Wilder ATR / SMA (Exact 50chartsMP.dll 1001C0BA..1001C352)
			List<double> dailyCloses = new List<double>(dayGroups.Count);
			List<double> trueRanges = new List<double>(dayGroups.Count);
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

			double pdHigh = prevBars.Max(x => x.High);
			double pdLow = prevBars.Min(x => x.Low);
			double pdClose = prevBars[prevBars.Count - 1].Close;
			double pdVol = prevBars.Sum(x => x.Volume);

			double tdOpen = todayBars[0].Open;
			double tdHigh = todayBars.Max(x => x.High);
			double tdLow = todayBars.Min(x => x.Low);
			double tdClose = todayBars[todayBars.Count - 1].Close;
			double tdVol = todayBars.Sum(x => x.Volume);

			DateTime latestSessionDate = todayBars[todayBars.Count - 1].Time.Date;
			bool isTodaySession = latestSessionDate == DateTime.Today && sub != null && sub.LastTickTime.Date == latestSessionDate;
			double livePrice = (isTodaySession && sub != null && sub.LastPrice > 0) ? sub.LastPrice : tdClose;
			double liveHigh = (isTodaySession && sub != null && sub.TodayHigh > 0) ? Math.Max(tdHigh, sub.TodayHigh) : Math.Max(tdHigh, livePrice);
			double liveLow = (isTodaySession && sub != null && sub.TodayLow > 0) ? Math.Min(tdLow, sub.TodayLow) : Math.Min(tdLow, livePrice);
			double liveOpen = (isTodaySession && sub != null && sub.TodayOpen > 0) ? sub.TodayOpen : tdOpen;
			double liveVol = (isTodaySession && sub != null && sub.TodayVolume > 0) ? Math.Max(tdVol, sub.TodayVolume) : tdVol;
			double livePrevClose = pdClose;

			// Update latest daily close with livePrice for SMA(N) - Daily
			if (dailyCloses.Count > 0)
				dailyCloses[dailyCloses.Count - 1] = livePrice;

			int smaCount = Math.Min(Math.Max(1, SmaDays), dailyCloses.Count);
			double smaVal = Math.Round(dailyCloses.Skip(dailyCloses.Count - smaCount).Average(), 2);

			// 50chartsMP.dll uses LastValue(Ref(ATR(N), -1)) (previous trading day's Daily ATR)
			var prevDayTrueRanges = trueRanges.Count > 1 ? trueRanges.Take(trueRanges.Count - 1).ToList() : trueRanges;
			double atrVal = Math.Round(ComputeWilderAtr(prevDayTrueRanges, Math.Max(1, AtrDays), tickSize), 2);
			double atr10Val = Math.Round(ComputeWilderAtr(prevDayTrueRanges, 10, tickSize), 2);

			// Exact 50chartsMP.dll Auto TPO Size (1001C422..1001C51B):
			// ONE tpoSize is computed from Ref(ATR(10), -1) and passed to both PrevDay and Today profiles.
			double tdTpoStep = Compute50ChartsAutoTpoSize(atr10Val, tickSize);
			double pdTpoStep = tdTpoStep;

			// Ensure live high/low are reflected in the current todayBar for real-time profile building
			if (todayBars.Count > 0)
			{
				MpBarSample lastTb = todayBars[todayBars.Count - 1];
				if (liveHigh > lastTb.High) lastTb.High = liveHigh;
				if (liveLow < lastTb.Low) lastTb.Low = liveLow;
				lastTb.Close = livePrice;
			}

			double pctChange = livePrevClose > 1e-9 ? ((livePrice - livePrevClose) / livePrevClose) * 100.0 : 0.0;
			double hlRange = Math.Max(0, liveHigh - liveLow);

			double pdVah, pdPoc, pdVal;
			int pdPocCount;
			int pdSinglePrintCode;
			string pdDayType;
			MpCellTone pdDayTypeTone;
			BuildTpoProfileMetrics(prevBars, pdTpoStep, IbCandleCount, ValueAreaPercent, true,
				out pdVah, out pdPoc, out pdVal, out pdPocCount, out pdSinglePrintCode, out pdDayType, out pdDayTypeTone);

			double tdVah, tdPoc, tdVal;
			int tdPocCount;
			int tdSinglePrintCode;
			string tdDayType;
			MpCellTone tdDayTypeTone;
			BuildTpoProfileMetrics(todayBars, tdTpoStep, IbCandleCount, ValueAreaPercent, false,
				out tdVah, out tdPoc, out tdVal, out tdPocCount, out tdSinglePrintCode, out tdDayType, out tdDayTypeTone);

			int ibBarsCount = Math.Min(Math.Max(1, IbCandleCount), todayBars.Count);
			double ibHigh = todayBars.Take(ibBarsCount).Max(x => x.High);
			double ibLow = todayBars.Take(ibBarsCount).Min(x => x.Low);
			double ibRange = Math.Max(0, ibHigh - ibLow);

			DateTime barTime = todayBars[todayBars.Count - 1].Time;

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
				PdTpoSize = pdTpoStep,
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
				TodayTpoSize = tdTpoStep,
				TodayVah = tdVah,
				TodayPoc = tdPoc,
				TodayVal = tdVal,
				TodayPocTpoCount = tdPocCount,
				TodayDayType = tdDayType,
				TodayDayTypeTone = tdDayTypeTone
			};

			row.PriceTone = row.PercentChange >= 0 ? MpCellTone.FgGreen : MpCellTone.FgRed;

			// Exact 50chartsMP.dll Open=H/L (1001E5D8): exact equality with TodayOpen == TodayHigh / TodayOpen == TodayLow
			if (Math.Abs(liveOpen - liveHigh) <= tickSize * 0.5 && (liveHigh - liveLow) > tickSize)
			{
				row.OpenEqualHL = "Open = High";
				row.OpenEqualHLTone = MpCellTone.BgRedOrange;
			}
			else if (Math.Abs(liveOpen - liveLow) <= tickSize * 0.5 && (liveHigh - liveLow) > tickSize)
			{
				row.OpenEqualHL = "Open = Low";
				row.OpenEqualHLTone = MpCellTone.BgGreen;
			}
			else
			{
				row.OpenEqualHL = "";
				row.OpenEqualHLTone = MpCellTone.Default;
			}

			// Exact 50chartsMP.dll Price - SMA(21) (1001E90D)
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

			// Exact 50chartsMP.dll PD-SingleP (1001ED19)
			if (pdSinglePrintCode == 1)
			{
				row.PdSinglePrint = "Single Print - Up";
				row.PdSinglePrintTone = MpCellTone.BgGreen;
			}
			else if (pdSinglePrintCode == -1)
			{
				row.PdSinglePrint = "Single Print - Down";
				row.PdSinglePrintTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.PdSinglePrint = "";
				row.PdSinglePrintTone = MpCellTone.Default;
			}

			// Exact 50chartsMP.dll Open vs PDVA (1001FA5A): >= pdVah is Above, <= pdVal is Below
			int openVsPdvaCode;
			if (liveOpen >= pdVah)
			{
				row.OpenVsPdVa = "Open Above PDVA";
				row.OpenVsPdVaTone = MpCellTone.BgGreen;
				openVsPdvaCode = 1;
			}
			else if (liveOpen <= pdVal)
			{
				row.OpenVsPdVa = "Open Below PDVA";
				row.OpenVsPdVaTone = MpCellTone.BgRedOrange;
				openVsPdvaCode = -1;
			}
			else
			{
				row.OpenVsPdVa = "Open Inside PDVA";
				row.OpenVsPdVaTone = MpCellTone.Default;
				openVsPdvaCode = 0;
			}

			// Exact 50chartsMP.dll Open vs PDHL (1001F92F): >= pdHigh is Above, <= pdLow is Below
			if (liveOpen >= pdHigh)
			{
				row.OpenVsPdHl = "Open Above PDHL";
				row.OpenVsPdHlTone = MpCellTone.BgGreen;
			}
			else if (liveOpen <= pdLow)
			{
				row.OpenVsPdHl = "Open Below PDHL";
				row.OpenVsPdHlTone = MpCellTone.BgRedOrange;
			}
			else
			{
				row.OpenVsPdHl = "Open Inside PDHL";
				row.OpenVsPdHlTone = MpCellTone.Default;
			}

			// Exact 50chartsMP.dll OpenType (1001FB95)
			string openTypeStr;
			MpCellTone openTypeTone;
			ClassifyOpenType(todayBars, openVsPdvaCode, pdVah, pdVal, tdTpoStep, out openTypeStr, out openTypeTone);
			row.OpenType = openTypeStr;
			row.OpenTypeTone = openTypeTone;

			// Exact 50chartsMP.dll PrevDay Breakout (1001FF41)
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

			// Exact 50chartsMP.dll IB Size (1001F0BB): compared against atr10Val (0.8, 0.5, 1/3)
			if (ibRange >= atr10Val * 0.80)
				row.IbSize = "Very Wide";
			else if (ibRange >= atr10Val * 0.50)
				row.IbSize = "Wide";
			else if (ibRange <= (atr10Val / 3.0))
				row.IbSize = "Small";
			else
				row.IbSize = "Normal";
			row.IbSizeTone = MpCellTone.Default;

			// Exact 50chartsMP.dll IB Breakout (1001F295) & Price - IB (10020352)
			bool ibBrokenUp = liveHigh > ibHigh;
			bool ibBrokenDown = liveLow < ibLow;
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

			// In 50chartsMP.dll (10020352), Price - IB is only populated when IB has been broken (ibBrokenUp || ibBrokenDown)
			if (ibBrokenUp || ibBrokenDown)
			{
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
			}
			else
			{
				row.PriceVsIb = "";
				row.PriceVsIbTone = MpCellTone.Default;
			}

			// Exact 50chartsMP.dll Today VA (100204E2)
			string todayVaStr;
			MpCellTone todayVaTone;
			ClassifyTodayVa(tdVah, tdVal, pdVah, pdVal, out todayVaStr, out todayVaTone);
			row.TodayVaStatus = todayVaStr;
			row.TodayVaTone = todayVaTone;

			// Exact 50chartsMP.dll 30-min PreviousBar (1001F3F0)
			string prevBarStr;
			MpCellTone prevBarTone;
			Classify30MinPrevBar(todayBars, prevBars, out prevBarStr, out prevBarTone);
			row.Prev30MinBarStatus = prevBarStr;
			row.Prev30MinBarTone = prevBarTone;

			// Exact 50chartsMP.dll SingleP (10020203)
			if (tdSinglePrintCode == 1)
			{
				row.TodaySinglePrint = "Single Print - Up";
				row.TodaySinglePrintTone = MpCellTone.BgGreen;
			}
			else if (tdSinglePrintCode == -1)
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

		private static double ComputeWilderAtr(List<double> trueRanges, int period, double minTick)
		{
			if (trueRanges == null || trueRanges.Count == 0)
				return minTick * 10.0;
			int n = Math.Max(1, period);
			if (trueRanges.Count <= n)
				return Math.Max(minTick, trueRanges.Average());

			double atr = trueRanges.Take(n).Average();
			for (int i = n; i < trueRanges.Count; i++)
			{
				atr = ((atr * (n - 1)) + trueRanges[i]) / n;
			}
			return Math.Max(minTick, atr);
		}

		// Exact 50chartsMP.dll Auto TPO Size (1001C422):
		// ceil(((ceil(atr10) * 4.0) / 100.0) / tickSize) * tickSize
		private static double Compute50ChartsAutoTpoSize(double atr10, double tickSize)
		{
			double minTick = Math.Max(0.0001, tickSize);
			double ceilAtr = Math.Ceiling(Math.Max(minTick, atr10));
			double raw = (ceilAtr * 4.0) / 100.0;
			double step = Math.Ceiling((raw / minTick) - 1e-9) * minTick;
			return Math.Max(minTick, Math.Round(step, 4));
		}

		private static void BuildTpoProfileMetrics(
			List<MpBarSample> sessionBars,
			double tpoStep,
			int ibCount,
			double vaPct,
			bool isCompletedSession,
			out double vahPrice,
			out double pocPrice,
			out double valPrice,
			out int pocTpoCount,
			out int singlePrintCode,
			out string dayType,
			out MpCellTone dayTypeTone)
		{
			vahPrice = 0; pocPrice = 0; valPrice = 0; pocTpoCount = 0;
			singlePrintCode = 0;
			dayType = "";
			dayTypeTone = MpCellTone.Default;

			if (sessionBars == null || sessionBars.Count == 0)
				return;

			double dayOpen = sessionBars[0].Open;
			double dayClose = sessionBars[sessionBars.Count - 1].Close;
			double dayHigh = sessionBars.Max(x => x.High);
			double dayLow = sessionBars.Min(x => x.Low);

			// Exact 50chartsMP.dll Profile Grid Construction (10023E24..1002423A)
			double step = Math.Max(1e-5, tpoStep);
			double sessionMinLow = Math.Floor(dayLow / step) * step;
			double sessionMaxHighFloor = Math.Floor(dayHigh / step) * step;
			int numBuckets = Math.Max(1, (int)Math.Round(((sessionMaxHighFloor - sessionMinLow) / step) + 1.0, MidpointRounding.AwayFromZero));
			if (numBuckets > 2000)
			{
				step = Math.Max(1e-5, (dayHigh - dayLow) / 500.0);
				sessionMinLow = Math.Floor(dayLow / step) * step;
				sessionMaxHighFloor = Math.Floor(dayHigh / step) * step;
				numBuckets = Math.Max(1, (int)Math.Round(((sessionMaxHighFloor - sessionMinLow) / step) + 1.0, MidpointRounding.AwayFromZero));
			}

			int[] tpoCount = new int[numBuckets];
			bool[] hasLastBar = new bool[numBuckets];
			int lastBarIdx = sessionBars.Count - 1;

			for (int p = 0; p < sessionBars.Count; p++)
			{
				MpBarSample b = sessionBars[p];
				double bLowFloor = Math.Max(sessionMinLow, Math.Floor(b.Low / step) * step);
				double bHighFloor = Math.Min(sessionMaxHighFloor, Math.Floor(b.High / step) * step);

				for (int i = 0; i < numBuckets; i++)
				{
					double price = sessionMinLow + i * step;
					bool geLow = price >= bLowFloor || AlmostEqual50Charts(bLowFloor, price);
					bool leHigh = price <= bHighFloor || AlmostEqual50Charts(bHighFloor, price);
					if (geLow && leHigh)
					{
						tpoCount[i]++;
						if (p == lastBarIdx)
							hasLastBar[i] = true;
					}
				}
			}

			int maxTpo = 0;
			int totalTpo = 0;
			for (int i = 0; i < numBuckets; i++)
			{
				if (tpoCount[i] > maxTpo)
					maxTpo = tpoCount[i];
				totalTpo += tpoCount[i];
			}
			if (maxTpo <= 0 || totalTpo <= 0)
				return;

			// Exact 50chartsMP.dll Center-Outward POC Search (1002969F..10029700)
			int midIdx = (int)Math.Floor(numBuckets * 0.5);
			int pocIdx = midIdx;
			for (int d = 0; d <= numBuckets; d++)
			{
				int ecx = midIdx - d;
				int edx = midIdx + d;
				if (ecx >= 0 && ecx < numBuckets && tpoCount[ecx] >= maxTpo)
				{
					pocIdx = ecx;
					break;
				}
				if (edx >= 0 && edx < numBuckets && tpoCount[edx] >= maxTpo)
				{
					pocIdx = edx;
					break;
				}
			}

			pocTpoCount = tpoCount[pocIdx];

			// Exact 50chartsMP.dll 70% Value Area 2-Bucket Expansion (10029702..100297FE)
			double targetTpo = (totalTpo * vaPct) / 100.0;
			double included = pocTpoCount;
			int valPtr = pocIdx - 1;
			int vahPtr = pocIdx + 1;

			while (targetTpo > included && (valPtr >= 0 || vahPtr < numBuckets))
			{
				int down2 = (valPtr >= 0 ? tpoCount[valPtr] : 0) + (valPtr > 0 ? tpoCount[valPtr - 1] : 0);
				int up2 = (vahPtr < numBuckets ? tpoCount[vahPtr] : 0) + (vahPtr + 1 < numBuckets ? tpoCount[vahPtr + 1] : 0);

				if (down2 > up2)
				{
					included += down2;
					valPtr -= 2;
				}
				else if (up2 > down2)
				{
					included += up2;
					vahPtr += 2;
				}
				else
				{
					// Simultaneous tie expansion on both sides (100297A8..100297D6)
					included += up2 + down2;
					vahPtr += 2;
					valPtr -= 2;
				}
			}

			int valIdx = Math.Max(0, valPtr + 1);
			int vahIdx = Math.Min(numBuckets - 1, vahPtr - 1);

			pocPrice = Math.Round(sessionMinLow + pocIdx * step, 2);
			vahPrice = Math.Round(sessionMinLow + vahIdx * step, 2);
			valPrice = Math.Round(sessionMinLow + valIdx * step, 2);

			// -------------------------------------------------------------
			// Interior Single Print Detection (50chartsMP.dll 10029AF0, 1001ED19 & 10020203)
			// Excludes buying tail at bottom and selling tail at top, and for live session excludes unconfirmed extreme of latest bar.
			// -------------------------------------------------------------
			int interiorBottom = 0;
			while (interiorBottom < numBuckets && tpoCount[interiorBottom] <= 1)
				interiorBottom++;

			int interiorTop = numBuckets - 1;
			while (interiorTop >= 0 && tpoCount[interiorTop] <= 1)
				interiorTop--;

			List<int> spZoneMidBins = new List<int>();
			if (interiorTop > interiorBottom && sessionBars.Count >= 3)
			{
				int streak = 0;
				int streakStart = -1;
				for (int b = interiorBottom; b <= interiorTop; b++)
				{
					bool isSingle = tpoCount[b] == 1 && (isCompletedSession || !hasLastBar[b]);
					if (isSingle)
					{
						if (streak == 0) streakStart = b;
						streak++;
					}
					else
					{
						if (streak >= 1)
							spZoneMidBins.Add((streakStart + (b - 1)) / 2);
						streak = 0;
					}
				}
				if (streak >= 1)
					spZoneMidBins.Add((streakStart + interiorTop) / 2);
			}

			if (spZoneMidBins.Count > 0)
			{
				double lastSpPrice = sessionMinLow + spZoneMidBins[spZoneMidBins.Count - 1] * step;
				singlePrintCode = dayClose >= lastSpPrice ? 1 : -1;
			}

			// -------------------------------------------------------------
			// Exact 50chartsMP.dll Day Type Classification (10027942..10027AC1, 1001EE26, 100202B9)
			// Only classified once >= 3 TPO periods exist (1001EE5B)
			// -------------------------------------------------------------
			if (!isCompletedSession && sessionBars.Count < 3)
			{
				dayType = "";
				dayTypeTone = MpCellTone.Default;
				return;
			}

			int ibCnt = Math.Min(Math.Max(1, ibCount), sessionBars.Count);
			double ibH = sessionBars.Take(ibCnt).Max(x => x.High);
			double ibL = sessionBars.Take(ibCnt).Min(x => x.Low);

			if (dayHigh > ibH && dayLow < ibL)
			{
				// Both sides of IB broken (100279BE..10027A06)
				if (dayClose >= ibL && dayClose <= ibH)
				{
					dayType = "Neutral - Centre day";
					dayTypeTone = MpCellTone.BgYellow;
				}
				else if (dayClose > ibH)
				{
					dayType = "Neutral - Extreme day - Up";
					dayTypeTone = MpCellTone.BgGreen;
				}
				else
				{
					dayType = "Neutral - Extreme day - Down";
					dayTypeTone = MpCellTone.BgRedOrange;
				}
			}
			else if (dayHigh <= ibH && dayLow >= ibL)
			{
				// Neither side of IB broken (10027A13..10027A25)
				dayType = "Non-Trend day";
				dayTypeTone = MpCellTone.BgYellow;
			}
			else if (dayClose >= ibL && dayClose <= ibH)
			{
				// One side of IB broken, Close inside IB (10027A2A..10027A4E)
				if (dayHigh > ibH)
				{
					dayType = "Normal day - Up";
					dayTypeTone = MpCellTone.BgGreen;
				}
				else
				{
					dayType = "Normal day - Down";
					dayTypeTone = MpCellTone.BgRedOrange;
				}
			}
			else if (dayClose > ibH && dayHigh >= ibH)
			{
				// Close above IB High (10027A50..10027A8B)
				if (pocTpoCount > 7 || (pocTpoCount == 7 && pocPrice >= ibL && pocPrice <= ibH))
				{
					dayType = "Normal Variation day - Up";
					dayTypeTone = MpCellTone.BgGreen;
				}
				else
				{
					dayType = "Trend day - Up";
					dayTypeTone = MpCellTone.BgGreen;
				}
			}
			else if (dayClose < ibL && dayLow <= ibL)
			{
				// Close below IB Low (10027A8D..10027AC1)
				if (pocTpoCount > 7 || (pocTpoCount == 7 && pocPrice >= ibL && pocPrice <= ibH))
				{
					dayType = "Normal Variation day - Down";
					dayTypeTone = MpCellTone.BgRedOrange;
				}
				else
				{
					dayType = "Trend day - Down";
					dayTypeTone = MpCellTone.BgRedOrange;
				}
			}
		}

		// Exact 50chartsMP.dll OpenType Classification (1001FB95..1001FF40)
		private static void ClassifyOpenType(
			List<MpBarSample> todayBars,
			int openVsPdvaCode,
			double pdVah,
			double pdVal,
			double step,
			out string openType,
			out MpCellTone tone)
		{
			openType = "";
			tone = MpCellTone.Default;
			// 1001FBE3..1001FBFB: OpenType is only evaluated when >= 3 bars exist (lastBarIdx >= firstBarIdx + 2)
			if (todayBars == null || todayBars.Count < 3)
				return;

			double s = Math.Max(1e-5, step);
			double qOpen0 = Math.Floor(todayBars[0].Open / s) * s;
			double qHigh0 = Math.Floor(todayBars[0].High / s) * s;
			double qLow0 = Math.Floor(todayBars[0].Low / s) * s;

			// [edi+4] = High array (qHigh1), [edi+8] = Low array (qLow1)
			double qHigh1 = Math.Floor(todayBars[1].High / s) * s;
			double qLow1 = Math.Floor(todayBars[1].Low / s) * s;

			double ib2High = qHigh1;
			double ib2Low = qLow1;
			// 1001FCF5: 3rd bar (index 2) is included in ib2High/ib2Low when lastBarIdx > firstBarIdx + 2 (Count > 3)
			if (todayBars.Count > 3)
			{
				double qHigh2 = Math.Floor(todayBars[2].High / s) * s;
				double qLow2 = Math.Floor(todayBars[2].Low / s) * s;
				ib2High = Math.Max(qHigh1, qHigh2);
				ib2Low = Math.Min(qLow1, qLow2);
			}

			int aOpenAboveLow = (int)Math.Round((qOpen0 - qLow0) / s, MidpointRounding.AwayFromZero);
			int aTotalTicks = (int)Math.Round((qHigh0 - qLow0) / s, MidpointRounding.AwayFromZero);
			int aHighAboveOpen = aTotalTicks - aOpenAboveLow;

			// Case 1 (1001FDBA): Bullish Open Drive from Open = Low (aOpenAboveLow == 0 && B.Low > A.Open)
			if (aOpenAboveLow == 0 && qLow1 > qOpen0)
			{
				if (openVsPdvaCode == -1 && Math.Max(qHigh0, qHigh1) >= pdVal)
				{
					openType = "ORR";
					tone = MpCellTone.Default;
				}
				else
				{
					openType = "Open Drive";
					tone = MpCellTone.BgGreen;
				}
				return;
			}

			// Case 2 (1001FDEC): Bearish Open Drive from Open = High (aHighAboveOpen == 0 && A.Open > B.High)
			if (aHighAboveOpen == 0 && qOpen0 > qHigh1)
			{
				if (openVsPdvaCode == 1 && Math.Min(qLow0, qLow1) <= pdVah)
				{
					openType = "ORR";
					tone = MpCellTone.Default;
				}
				else
				{
					openType = "Open Drive";
					tone = MpCellTone.BgGreen;
				}
				return;
			}

			// Case 3 (1001FE16): Bullish OpenTest Drive (aOpenAboveLow < aHighAboveOpen && B.Low > A.Open)
			if (aOpenAboveLow < aHighAboveOpen && qLow1 > qOpen0)
			{
				if (openVsPdvaCode == -1 && Math.Max(qHigh0, qHigh1) >= pdVal)
				{
					openType = "ORR";
					tone = MpCellTone.Default;
				}
				else
				{
					openType = "OpenTest Drive";
					tone = MpCellTone.BgGreen;
				}
				return;
			}

			// Case 4 (1001FE3E): Bearish OpenTest Drive (aOpenAboveLow > aHighAboveOpen && A.Open > B.High)
			if (aOpenAboveLow > aHighAboveOpen && qOpen0 > qHigh1)
			{
				if (openVsPdvaCode == 1 && Math.Min(qLow0, qLow1) <= pdVah)
				{
					openType = "ORR";
					tone = MpCellTone.Default;
				}
				else
				{
					openType = "OpenTest Drive";
					tone = MpCellTone.BgGreen;
				}
				return;
			}

			// Case 5 (1001FE63): OAIR / OAOR
			if ((qOpen0 >= qLow1 && qOpen0 <= qHigh1) || (qOpen0 >= ib2Low && qOpen0 <= ib2High))
			{
				openType = (openVsPdvaCode == 1 || openVsPdvaCode == -1) ? "OAOR" : "OAIR";
				tone = MpCellTone.Default;
			}
		}

		// Exact 50chartsMP.dll Helper 10018AF0 (0.05% price equality check)
		private static bool AlmostEqual50Charts(double a, double b)
		{
			if (Math.Abs(a) < 1e-9) return Math.Abs(b) < 1e-9;
			return (Math.Abs(a - b) * 100.0 / Math.Abs(a)) <= 0.05;
		}

		// Exact 50chartsMP.dll Today VA Classification (100204E2)
		private static void ClassifyTodayVa(
			double tdVah,
			double tdVal,
			double pdVah,
			double pdVal,
			out string status,
			out MpCellTone tone)
		{
			if (tdVah > pdVah && tdVal > pdVah)
			{
				status = "Higher than PDVA";
				tone = MpCellTone.BgGreen;
			}
			else if (tdVah < pdVal && tdVal < pdVal)
			{
				status = "Lower than PDVA";
				tone = MpCellTone.BgRedOrange;
			}
			else if (AlmostEqual50Charts(tdVah, pdVah) && AlmostEqual50Charts(tdVal, pdVal))
			{
				status = "Same VA";
				tone = MpCellTone.Default;
			}
			else if ((AlmostEqual50Charts(tdVah, pdVah) || tdVah < pdVah) && (AlmostEqual50Charts(tdVal, pdVal) || tdVal > pdVal))
			{
				status = "Inside PrevVA";
				tone = MpCellTone.Default;
			}
			else if (tdVah > pdVah && tdVal < pdVal)
			{
				status = "Overlapping to PDVA H & L";
				tone = MpCellTone.BgYellow;
			}
			else if (tdVah > pdVah && (tdVal > pdVal || AlmostEqual50Charts(tdVal, pdVal)))
			{
				status = "Overlapping to PDVA High";
				tone = MpCellTone.BgGreen;
			}
			else if ((tdVah < pdVah || AlmostEqual50Charts(tdVah, pdVah)) && tdVal < pdVal)
			{
				status = "Overlapping to PDVA low";
				tone = MpCellTone.BgRedOrange;
			}
			else
			{
				status = "";
				tone = MpCellTone.Default;
			}
		}

		// Exact 50chartsMP.dll 30-min PreviousBar Classification (1001F3F0..1001F879)
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

			bool isOutside = prev1.High > prev2.High && prev1.Low < prev2.Low;
			bool isInside = prev1.High < prev2.High && prev1.Low > prev2.Low;
			if (!isInside && !isOutside)
				return;

			string baseLabel = isInside ? "Inside Bar" : "Outside Bar";
			bool brokeBoth = curr.High > prev1.High && curr.Low < prev1.Low;
			bool brokeUp = curr.High > prev1.High && curr.Low > prev1.Low;
			bool brokeDown = curr.High < prev1.High && curr.Low < prev1.Low;

			if (brokeBoth)
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

		private static MpCellTone GetDayTypeTone(string dt)
		{
			if (string.IsNullOrEmpty(dt))
				return MpCellTone.Default;
			if (dt.StartsWith("Non-Trend", StringComparison.OrdinalIgnoreCase) || dt.StartsWith("Neutral - Centre", StringComparison.OrdinalIgnoreCase))
				return MpCellTone.BgYellow;
			if (dt.EndsWith("Up", StringComparison.OrdinalIgnoreCase))
				return MpCellTone.BgGreen;
			if (dt.EndsWith("Down", StringComparison.OrdinalIgnoreCase))
				return MpCellTone.BgRedOrange;
			if (dt.StartsWith("Neutral", StringComparison.OrdinalIgnoreCase))
				return MpCellTone.BgYellow;
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

			if (showDailySma)
			{
				cols.Add(new MpColumnDef { Id = "DailySma", Header = "SMA(" + smaDays + ") - Daily", Group = "DailySMA", Width = 96f, IsNumeric = true, TextSelector = r => r.FormatNum(r.DailySma), SortNumSelector = r => r.DailySma, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PriceVsSma", Header = "Price - SMA(" + smaDays + ")", Group = "DailySMA", Width = 128f, IsNumeric = false, TextSelector = r => r.PriceVsSma, ToneSelector = r => r.PriceVsSmaTone });
			}

			if (showPrevDayMpLevels)
			{
				cols.Add(new MpColumnDef { Id = "PdTpo", Header = "TPOSize", Group = "PrevDayMP", Width = 64f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PdTpoSize), SortNumSelector = r => r.PdTpoSize, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PdVah", Header = "PD-VAH", Group = "PrevDayMP", Width = 78f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PdVah), SortNumSelector = r => r.PdVah, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PdPoc", Header = "PD-POC", Group = "PrevDayMP", Width = 78f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PdPoc), SortNumSelector = r => r.PdPoc, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PdVal", Header = "PD-VAL", Group = "PrevDayMP", Width = 78f, IsNumeric = true, TextSelector = r => r.FormatNum(r.PdVal), SortNumSelector = r => r.PdVal, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "PdPocCnt", Header = "PD-POC TPOCount", Group = "PrevDayMP", Width = 96f, IsNumeric = true, TextSelector = r => r.PdPocTpoCount.ToString(CultureInfo.InvariantCulture), SortNumSelector = r => r.PdPocTpoCount, ToneSelector = r => MpCellTone.Default });
			}

			cols.Add(new MpColumnDef { Id = "PrevDayType", Header = "PrevDay Type", Group = "Core", Width = 148f, IsNumeric = false, TextSelector = r => r.PrevDayType, ToneSelector = r => r.PrevDayTypeTone });
			cols.Add(new MpColumnDef { Id = "PdSingleP", Header = "PD-SingleP", Group = "Core", Width = 114f, IsNumeric = false, TextSelector = r => r.PdSinglePrint, ToneSelector = r => r.PdSinglePrintTone });
			cols.Add(new MpColumnDef { Id = "OpenVa", Header = "Open", Group = "Core", Width = 116f, IsNumeric = false, TextSelector = r => r.OpenVsPdVa, ToneSelector = r => r.OpenVsPdVaTone });
			cols.Add(new MpColumnDef { Id = "OpenHl", Header = "Open", Group = "Core", Width = 116f, IsNumeric = false, TextSelector = r => r.OpenVsPdHl, ToneSelector = r => r.OpenVsPdHlTone });
			cols.Add(new MpColumnDef { Id = "OpenType", Header = "OpenType", Group = "Core", Width = 102f, IsNumeric = false, TextSelector = r => r.OpenType, ToneSelector = r => r.OpenTypeTone });
			cols.Add(new MpColumnDef { Id = "PdBreakout", Header = "PrevDay Breakout", Group = "Core", Width = 118f, IsNumeric = false, TextSelector = r => r.PrevDayBreakout, ToneSelector = r => r.PrevDayBreakoutTone });
			cols.Add(new MpColumnDef { Id = "AtrDaily", Header = "ATR(" + atrDays + ") - Daily", Group = "Core", Width = 92f, IsNumeric = true, TextSelector = r => r.FormatNum(r.DailyAtr), SortNumSelector = r => r.DailyAtr, ToneSelector = r => MpCellTone.Default });
			cols.Add(new MpColumnDef { Id = "HlRange", Header = "HL Range", Group = "Core", Width = 74f, IsNumeric = true, TextSelector = r => r.FormatNum(r.HlRange), SortNumSelector = r => r.HlRange, ToneSelector = r => MpCellTone.Default });

			if (showIbValues)
			{
				cols.Add(new MpColumnDef { Id = "IbHigh", Header = "IB - High", Group = "IBValues", Width = 76f, IsNumeric = true, TextSelector = r => r.FormatNum(r.IbHigh), SortNumSelector = r => r.IbHigh, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "IbLow", Header = "IB - Low", Group = "IBValues", Width = 76f, IsNumeric = true, TextSelector = r => r.FormatNum(r.IbLow), SortNumSelector = r => r.IbLow, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "IbRange", Header = "IB Range", Group = "IBValues", Width = 74f, IsNumeric = true, TextSelector = r => r.FormatNum(r.IbRange), SortNumSelector = r => r.IbRange, ToneSelector = r => MpCellTone.Default });
			}

			cols.Add(new MpColumnDef { Id = "IbSize", Header = "IB Size", Group = "Core", Width = 72f, IsNumeric = false, TextSelector = r => r.IbSize, ToneSelector = r => r.IbSizeTone });
			cols.Add(new MpColumnDef { Id = "IbBreakout", Header = "IB Breakout", Group = "Core", Width = 114f, IsNumeric = false, TextSelector = r => r.IbBreakout, ToneSelector = r => r.IbBreakoutTone });
			cols.Add(new MpColumnDef { Id = "PriceVsIb", Header = "Price - IB", Group = "Core", Width = 104f, IsNumeric = false, TextSelector = r => r.PriceVsIb, ToneSelector = r => r.PriceVsIbTone });

			if (showTodayMpLevels)
			{
				cols.Add(new MpColumnDef { Id = "TdTpo", Header = "TPOSize", Group = "TodayMP", Width = 64f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayTpoSize), SortNumSelector = r => r.TodayTpoSize, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "TdVah", Header = "Today-VAH", Group = "TodayMP", Width = 80f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayVah), SortNumSelector = r => r.TodayVah, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "TdPoc", Header = "Today-POC", Group = "TodayMP", Width = 80f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayPoc), SortNumSelector = r => r.TodayPoc, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "TdVal", Header = "Today-VAL", Group = "TodayMP", Width = 80f, IsNumeric = true, TextSelector = r => r.FormatNum(r.TodayVal), SortNumSelector = r => r.TodayVal, ToneSelector = r => MpCellTone.Default });
				cols.Add(new MpColumnDef { Id = "TdPocCnt", Header = "Today-POC TPOCount", Group = "TodayMP", Width = 104f, IsNumeric = true, TextSelector = r => r.TodayPocTpoCount.ToString(CultureInfo.InvariantCulture), SortNumSelector = r => r.TodayPocTpoCount, ToneSelector = r => MpCellTone.Default });
			}

			cols.Add(new MpColumnDef { Id = "TodayVa", Header = "Today VA", Group = "Core", Width = 148f, IsNumeric = false, TextSelector = r => r.TodayVaStatus, ToneSelector = r => r.TodayVaTone });
			cols.Add(new MpColumnDef { Id = "Bar30Min", Header = periodicityMinutes + "-min PreviousBar", Group = "Core", Width = 152f, IsNumeric = false, TextSelector = r => r.Prev30MinBarStatus, ToneSelector = r => r.Prev30MinBarTone });
			cols.Add(new MpColumnDef { Id = "SingleP", Header = "SingleP", Group = "Core", Width = 114f, IsNumeric = false, TextSelector = r => r.TodaySinglePrint, ToneSelector = r => r.TodaySinglePrintTone });
			cols.Add(new MpColumnDef { Id = "DayType", Header = "Day Type", Group = "Core", Width = 165f, IsNumeric = false, TextSelector = r => r.TodayDayType, ToneSelector = r => r.TodayDayTypeTone });

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
			Caption = "AK10X Market Profile Live Scanner \u2014 NinjaTrader 8";
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
				Text = "Real-Time Order Flow & Market Profile Screener \u2022 Dynamic Accelpix Feed \u2022 NinjaTrader 8",
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
				Text = "\u25CB CHECKING CONNECTION...",
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
				Text = string.Format("{0}-Min TPO \u2022 70% VA \u2022 2-Bar IB \u2022 Daily ATR({1})", engine.PeriodicityMinutes, engine.AtrDays),
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
			// Row 1: PRIMARY TOOLBAR (Explore, Watchlist, Filter, Search, Trading Screen)
			// -------------------------------------------------------------
			Border bar1 = new Border
			{
				Background = new WpfSolidBrush(WpfColor.FromRgb(19, 26, 40)),
				BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(30, 41, 59)),
				BorderThickness = new Thickness(0, 0, 0, 1),
				Padding = new Thickness(10, 7, 10, 7)
			};
			DockPanel toolbarDock = new DockPanel { LastChildFill = true };

			Button btnStacker = CreatePillButton("\u26A1 Trading Screen", WpfColor.FromRgb(255, 179, 0), WpfColors.Black, true);
			btnStacker.Margin = new Thickness(10, 0, 0, 0);
			btnStacker.HorizontalAlignment = HorizontalAlignment.Right;
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
					NinjaTrader.Code.Output.Process("AK10X Trading Screen Launch Error: " + ex.Message, PrintTo.OutputTab1);
				}
			};
			DockPanel.SetDock(btnStacker, Dock.Right);
			toolbarDock.Children.Add(btnStacker);

			WrapPanel wp1 = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };

			Button btnExplore = CreatePillButton("\u2714 AK10X Scan Now", WpfColor.FromRgb(16, 185, 129), WpfColors.White, true);
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

			toolbarDock.Children.Add(wp1);
			bar1.Child = toolbarDock;
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
			wp2.Children.Add(chkDailySma);
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
				txtSpecs.Text = string.Format("{0}-Min TPO \u2022 70% VA \u2022 2-Bar IB \u2022 Daily ATR({1})", engine.PeriodicityMinutes, engine.AtrDays);

			if (txtStatus != null)
			{
				int liveCount = freshRows.Count(r => r.IsLiveConnected);
				txtStatus.Text = string.Format(
					CultureInfo.InvariantCulture,
					"AK10X Market Profile Scanner Engine  |  Symbols Displayed: {0} (Real-time Live Active: {1})  |  Periodicity: {2}-min TPO  |  Sorted By: {3} ({4})  |  Watchlist: {5}  |  Updated: {6:dd-MM-yyyy HH:mm:ss}",
					freshRows.Count, liveCount, engine.PeriodicityMinutes, sortCol.Header, sortAsc ? "Ascending \u25B2" : "Descending \u25BC", engine.WatchlistPreset, DateTime.Now);
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
					txtLivePulse.Text = (pulseAlt ? "\u25CF " : "\u25C9 ") + "LIVE ACCELPIX CONNECTED";
					txtLivePulse.Foreground = new WpfSolidBrush(WpfColor.FromRgb(74, 222, 128)); // bright green
					borderLivePill.Background = new WpfSolidBrush(WpfColor.FromRgb(5, 46, 22)); // deep green
					borderLivePill.BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(22, 101, 52)); // forest green
					return;
				}
				if (accel.Status == ConnectionStatus.Connecting || accel.PriceStatus == ConnectionStatus.Connecting)
				{
					txtLivePulse.Text = "\u25CF ACCELPIX CONNECTING...";
					txtLivePulse.Foreground = new WpfSolidBrush(WpfColor.FromRgb(251, 191, 36)); // amber gold
					borderLivePill.Background = new WpfSolidBrush(WpfColor.FromRgb(69, 26, 3)); // amber dark
					borderLivePill.BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(180, 83, 9)); // amber border
					return;
				}
				if (accel.Status == ConnectionStatus.ConnectionLost || accel.PriceStatus == ConnectionStatus.ConnectionLost)
				{
					txtLivePulse.Text = "\u26A0 ACCELPIX CONNECTION LOST";
					txtLivePulse.Foreground = new WpfSolidBrush(WpfColor.FromRgb(248, 113, 113)); // red text
					borderLivePill.Background = new WpfSolidBrush(WpfColor.FromRgb(69, 10, 10)); // red dark
					borderLivePill.BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(185, 28, 28)); // red border
					return;
				}
			}

			if (anyConnected != null)
			{
				string cname = (anyConnected.Options != null && !string.IsNullOrEmpty(anyConnected.Options.Name)) ? anyConnected.Options.Name : "LIVE FEED";
				txtLivePulse.Text = (pulseAlt ? "\u25CF " : "\u25C9 ") + "LIVE " + cname.ToUpperInvariant() + " CONNECTED";
				txtLivePulse.Foreground = new WpfSolidBrush(WpfColor.FromRgb(74, 222, 128));
				borderLivePill.Background = new WpfSolidBrush(WpfColor.FromRgb(5, 46, 22));
				borderLivePill.BorderBrush = new WpfSolidBrush(WpfColor.FromRgb(22, 101, 52));
				return;
			}

			txtLivePulse.Text = "\u25CB ACCELPIX DISCONNECTED";
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

				DrawToolbarBtn(bx, by, 102f, bh, "\u2714 AK10X Scan", "SCAN", brBtnActive, brWhite, brGridLine, tfHeader);
				bx += 108f;

				string autoTxt = AutoRefreshEnabled ? ("\u25CF Live (" + AutoRefreshSeconds + "s)") : "\u25CB Paused";
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

				DrawToolbarBtn(bx, by, 34f, bh, "\u25C0", "LEFT", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 38f;
				DrawToolbarBtn(bx, by, 34f, bh, "\u25B6", "RIGHT", brBtn, light ? brBlack : brLightText, brGridLine, tfHeader);
				bx += 42f;

				DrawToolbarBtn(bx, by, 158f, bh, "\u2750 Pop Out AK10X Window", "POPOUT", brBtnPop, brWhite, brGridLine, tfHeader);

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
							hdrText += sortAscending ? " \u25B2" : " \u25BC";

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
