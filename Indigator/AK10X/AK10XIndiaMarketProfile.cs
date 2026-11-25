// AK10X Market Profile for Indian Market (NSE / BSE / MCX) for NinjaTrader 8.
// Developed for AK10X Trading Systems.
// Pure Day Profile trading: No multi-session partitioning, exactly 1 Day Profile per trading day.
// Features Dalton Market Profile (13 TPOs: A through M for 09:15 - 15:30 IST),
// Initial Balance (A & B periods), Single Prints (Buying/Selling SP),
// Virgin Level Extensions (VPoC, VPPoC, VAH, VAL, PoorHigh/Low, DayHigh/Low, WeeklyHigh/Low) with TPO-Close Virginity Break Engine,
// Volume Profile, 9-Line Summary Card, and Direct2D hardware acceleration.
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using DxBrush = SharpDX.Direct2D1.SolidColorBrush;
using DxTextFormat = SharpDX.DirectWrite.TextFormat;
using DxTextLayout = SharpDX.DirectWrite.TextLayout;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public enum DtrAtrSmoothingType
	{
		RMA = 0,
		EMA = 1,
		SMA = 2,
		WMA = 3
	}

	public class AK10XIndiaMarketProfile : Indicator
	{
		private struct DailyOhlcSample
		{
			public DateTime Date;
			public double Open;
			public double High;
			public double Low;
			public double Close;
		}

		private struct MinuteSample
		{
			public DateTime Time;
			public double Open;
			public double High;
			public double Low;
			public double Close;
			public double Volume;
			public int BarIndex;
		}

		private sealed class PriceRow
		{
			public HashSet<int> Periods = new HashSet<int>();
			public double Volume;
			private int[] cachedSortedPeriods;
			private int cachedPeriodCount = -1;

			public int[] GetSortedPeriods()
			{
				if (cachedSortedPeriods == null || cachedPeriodCount != Periods.Count)
				{
					cachedPeriodCount = Periods.Count;
					cachedSortedPeriods = new int[cachedPeriodCount];
					if (cachedPeriodCount > 0)
					{
						Periods.CopyTo(cachedSortedPeriods);
						if (cachedPeriodCount > 1)
							Array.Sort(cachedSortedPeriods);
					}
				}
				return cachedSortedPeriods;
			}
		}

		private sealed class PeriodSlice
		{
			public int PeriodIndex;
			public double Open;
			public double High = double.MinValue;
			public double Low = double.MaxValue;
			public double Close;
			public int BarCount;
			public double TotalVolume;
			public Dictionary<int, double> BinVolumes = new Dictionary<int, double>();
		}

		private sealed class SinglePrintZone
		{
			public int StartBin;
			public int EndBin;
			public bool IsBuying;
			public int PeriodIndex;
		}

		private sealed class DayProfile
		{
			public string ProfileKey;
			public DateTime SessionDate;
			public string SessionName;
			public int ExpectedPeriodCount;
			public DateTime EffectiveOpenTime;
			public DateTime SessionCloseTime;
			public DateTime LastSampleTime;
			public int FirstBar = int.MaxValue;
			public int LastBar = int.MinValue;
			public int LastPeriod;
			public int BarCount;
			public double Open;
			public double High = double.MinValue;
			public double Low = double.MaxValue;
			public double Close;
			public double TotalVolume;
			public double InitialBalanceHigh = double.MinValue;
			public double InitialBalanceLow = double.MaxValue;
			public SortedDictionary<int, PriceRow> Rows = new SortedDictionary<int, PriceRow>();
			public SortedDictionary<int, PeriodSlice> PeriodSlices = new SortedDictionary<int, PeriodSlice>();

			// Per-render-pass cached coordinates & analytics
			public float SlotLeftX;
			public float SlotRightX;
			public float IbX;
			public float CollapsedRightX;
			public double EffectiveStep;
			public float RenderTopY;
			public float RenderBotY;
			public int PocBin;
			public int VahBin;
			public int ValBin;
			public int VolumePocBin;
			public int VolumeVahBin;
			public int VolumeValBin;
			public double MaxRowVolume;
			public bool IsSplitExpanded;
			public List<SinglePrintZone> SinglePrintZones = new List<SinglePrintZone>();
			public bool ContinuityChecked;
			public bool AnalyticsValid;
			public double AnalyticsStep;
			public double AnalyticsVaPct;
		}

		private enum ChartButtonAction
		{
			ToggleSplit,
			ToggleTopLeftMinMax,
			ToggleSummaryMinMax,
			ScrollPastDays,
			ScrollNextDays,
			ScrollLiveDay,
			ToggleProfileDropdown,
			SelectProfileDaily,
			SelectProfileWeekly,
			SelectProfileMonthly,
			ZoomInVertical,
			ZoomOutVertical,
			ToggleModernRichUi
		}

		private struct ChartButton
		{
			public RectangleF Rect;
			public ChartButtonAction Action;
			public string PrimaryKey;
		}

		private sealed class RightEdgeTag
		{
			public double Price;
			public float IdealY;
			public float DrawY;
			public float Height;
			public string Text;
			public DxBrush Brush;
			public DxBrush FillBrush;
			public DxBrush BorderBrush;
			public int Priority;
		}

		private const string LetterSequence = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

		private static readonly SharpDX.Color[] LetterColors = new SharpDX.Color[]
		{
			new SharpDX.Color(242, 82, 75),   // A - Coral Red
			new SharpDX.Color(245, 148, 42),  // B - Orange
			new SharpDX.Color(238, 202, 48),  // C - Gold Yellow
			new SharpDX.Color(156, 218, 62),  // D - Lime Green
			new SharpDX.Color(58, 208, 102),  // E - Green
			new SharpDX.Color(48, 204, 178),  // F - Teal
			new SharpDX.Color(58, 188, 230),  // G - Cyan
			new SharpDX.Color(82, 152, 242),  // H - Dodger Blue
			new SharpDX.Color(142, 128, 245), // I - Lavender Blue
			new SharpDX.Color(198, 108, 238), // J - Orchid Purple
			new SharpDX.Color(238, 98, 178),  // K - Magenta Pink
			new SharpDX.Color(64, 212, 218),  // L - Aqua
			new SharpDX.Color(115, 218, 92),  // M - Spring Green
			new SharpDX.Color(245, 176, 65),  // N - Amber
			new SharpDX.Color(235, 110, 95),  // O - Salmon
			new SharpDX.Color(88, 198, 235),  // P - Sky Blue
			new SharpDX.Color(168, 222, 85),  // Q - Chartreuse
			new SharpDX.Color(242, 132, 182), // R - Rose
			new SharpDX.Color(74, 224, 162),  // S - Mint
			new SharpDX.Color(250, 196, 78),  // T - Warm Gold
			new SharpDX.Color(126, 178, 252), // U - Cornflower
			new SharpDX.Color(218, 132, 248), // V - Violet
			new SharpDX.Color(98, 230, 125),  // W - Emerald
			new SharpDX.Color(248, 138, 108), // X - Tangerine
		};

		[StructLayout(LayoutKind.Sequential)]
		private struct Win32Point
		{
			public int X;
			public int Y;
		}

		[DllImport("user32.dll")]
		private static extern bool GetCursorPos(out Win32Point lpPoint);

		[DllImport("user32.dll")]
		private static extern bool ScreenToClient(IntPtr hWnd, ref Win32Point lpPoint);

		private readonly object profileLock = new object();
		private readonly List<MinuteSample> rawMinuteSamples = new List<MinuteSample>(65536);
		private readonly SortedDictionary<string, DayProfile> profiles = new SortedDictionary<string, DayProfile>(StringComparer.Ordinal);
		private readonly Dictionary<DateTime, int> chartDayFirstBar = new Dictionary<DateTime, int>();
		private readonly Dictionary<DateTime, int> chartDayLastBar = new Dictionary<DateTime, int>();
		private readonly HashSet<string> manuallySplitProfileKeys = new HashSet<string>(StringComparer.Ordinal);
		private readonly HashSet<string> manuallyUnsplitProfileKeys = new HashSet<string>(StringComparer.Ordinal);
		private readonly List<ChartButton> activeChartButtons = new List<ChartButton>();
		private int lastHiddenChartBar = -1;
		private int lastVolumeBarIndex = -1;
		private double lastCumulativeVolume;
		private double cachedPriceStep;
		private int lastRebuiltSampleCount;
		private bool didAutoAdjustChartProps;
		private bool isMouseHooked;
		private ChartControl hookedChartControl;
		private System.Windows.Input.MouseButtonEventHandler mouseLeftDownHandler;
		private System.Windows.Controls.Primitives.ScrollBar hookedScrollBar;
		private int lastHandledMouseTimestamp = int.MinValue;
		private DateTime lastButtonClickUtc = DateTime.MinValue;
		private bool isPotentialCanvasDrag;
		private bool isDraggingCanvas;
		private float dragStartMouseX;
		private float dragStartMouseY;
		private float dragStartScrollPx;
		private double dragStartPanOffset;
		private float maxScrollCached;
		private float lastRenderTargetW;
		private float lastRenderTargetH;
		private ChartScale lastRenderedChartScale;
		private double verticalZoomFactor = 1.0;
		private double verticalPanPriceOffset;
		private double lastBaseMidPrice;
		private double lastBasePriceSpan = 10.0;
		private float lastUsableHeightPx = 600f;
		private int currentLoadedHistoryDays;
		private NinjaTrader.NinjaScript.ChartStyles.ChartStyle savedChartStyle;
		private System.Windows.Media.Brush savedUpBrush;
		private System.Windows.Media.Brush savedDownBrush;
		private System.Windows.Media.Brush savedStrokeBrush;
		private System.Windows.Media.Brush savedStroke2Brush;
		private double lastTrackedScrollBarVal = double.NaN;
		private bool hasManualPixelScroll;
		private int lastTrackedFromIdx = -1;
		private int lastTrackedToIdx = -1;
		private float currentScrollPx = float.NaN;
		private bool? runtimeMinimizeTopLeftOverride;
		private bool? runtimeMinimizeSummaryOverride;
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SortedDictionary<DateTime, DailyOhlcSample>> SharedAtrDailyCache
			= new System.Collections.Concurrent.ConcurrentDictionary<string, SortedDictionary<DateTime, DailyOhlcSample>>(StringComparer.OrdinalIgnoreCase);

		private MarketProfilePeriodMode? runtimeProfileModeOverride;
		private bool isProfileDropdownOpen;
		private SharpDX.Direct2D1.Bitmap cachedLogoBitmap;
		private SharpDX.Direct2D1.RenderTarget cachedLogoRenderTarget;
		private BarsRequest activeAccelpixBarsReq;
		private Instrument subscribedMarketDataInstrument;
		private bool isConnectionStatusHooked;
		private DateTime lastLiveRefreshUtc = DateTime.MinValue;
		private double lastLiveTickPrice;
		private DateTime lastLiveTickTime = DateTime.MinValue;
		private bool didTriggerChartHistoricalReload;
		private readonly SortedDictionary<DateTime, DailyOhlcSample> atrDailyHistory = new SortedDictionary<DateTime, DailyOhlcSample>();
		private bool isWarmingUpAtrHistory;
		private bool? runtimeModernUiOverride;
		private float lastTopLeftHudRightX;
		private System.Windows.Threading.DispatcherTimer tpoCountdownTimer;
		private int lastTpoCountdownSec = -1;
		private int uiRefreshQueued;

		private const string DefaultLogoBase64 = "iVBORw0KGgoAAAANSUhEUgAAAGAAAABgCAYAAADimHc4AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAABAdSURBVHhe7Z0JcBVFGscTIJyCcsp9yQ2CgCJXRCEQ7jvcAhGBFURr3S1FsFhrBURWjlCKyHIEEVAgooC1coi4HMt9BJAj4b6SAIEIIYeBb79/p6ed914DeS/vzbwA/6pfVWoy09/X37zp6ekzwI9VgWnDvMlEMKuY7cxvzDkmgUmU4G8cw/9wDs7FNbgWaSCtx3qAqjCvMvOZaOY2Q14CaSHNeQxsVGYei1WN+RvzK5PK6ILnC1IY2HyHgQ+PlIKYnswaJo3RBciBQoUKUeXKlalJkybUqVMnGjhgAI0cOZLGjBkjwN8D+FjHjh3FOTi3UMGC2rQ0wAf4Ap/yMA+tCjNjGJTTukAI8ubNS40aNRJBnTt3Lm3fvp0uXrxIaWlplFXhXFyzbds2mvvllzRyxAiRJtLW2TRxhIGPTzAPjfIyyNRpRpdpKl68OIWFhdGiRYvo1MmTMoze10lOGzZ69+5NxYoV0/oiga94gcP3HK0ezCFGl0kKCQkRAbly5YoMkXW6kpBAkZGRFNKmjdY3CV7c3Zkcp0rMCsYlUwW5bB7BRcLevXtlKOzX3j17aPjw4VSgQAEXfyXLGeQpR2goc41xyERQUBCNHj2aYmNjZbb9TzExMTRq1Cjhq7P/zFVmCOO3wotrEePifI8ePejQoUMym/6v6Oho6t69u0s+JJGM372k6zIHGQdnq1SpQlFRUTJbOU8rVqwQVVrnfDEHmNqMX6g9c51xcDI8PJwSExNlVnKurl27RkOHDnXImwRNIKGMrUKZeJdRjuGjCbWLh00LFy4UFQhzXpk7DJo2bNFoxsGhGjVq0P79+6XLD59Qc6tevbpDniVvMJbKJfitWrWiBK5bP+xKiI+nl4KDHfIuGcVYIhQ7Dsa7detGqamp0sWHX8hr165dHWIg8XlxhJeOQ5mPz/q7d+9K1x4d3blzh3r16mUOPkBs2jI+US0miVEG8St4FINvCDehS5cu5hsAbjA1Ga+qEHOUUYZatGjhVgvlwyoUR82bNzffAIBWVcTMa1rKKANVq1a1pQHNX4XKh+aD7WvGK0Lbjko4f/78dODAAWn6sQyh+p0vXz7zDQCDmWwJndm/MypRfJA8ll7z5883Bx/gnVmO8VjoqlMJotvvse6v/v37m28A+IHxSN0YlVCZMmXo+vXr0sxj3Uto/ypdurT5BoAujFvKx8QyKpGVK1dKE4/1IC1fvtwcfHCCcat7821GJRAaGiqTtk9pXN1bv24dfbNsGS1csID+MWECLeAyd8nXS+j8ufPyLP9R27ZtzTcAoI85S0L99TIjLsydOzcdPnxYJmuN8IGTcjuZUpJv0aLISBo/bhw1btTIOUOKatWqUXzcZXm1fwgdULly5TL7eYnJ0reBw68/fOhQmaQ1Wrv6R2rcsDFVrVKF6tSuac4ABQYGih+EGSOT9erWpeiDB2Uq/qEhgwc7+M9gdMh9hUFTavgI+kYxlMMq/fD9KpexOwiwc9B14FwMb8E4IH9RTEysc//ySQYxvqd6MeqCgQMHyqR8r6VLlim7ugDfD9wk8+PuTzdBUy3FUJ17agOjTt69e7dMxvdq3uwlYVMX4Kxgvgn4Ih0/frxM2V7t2rVLxVOyjtHqGeYPRpzYtGlTmYTvFbXyO7YZyAF0LePdxfwkDBn8Kv2elCSt2CeMVzV8YhBjjAR30XuMOhHjMq1S/36DhE1dQN3FeBLwwkaaYb17UXq6va22c+bMUXGV/J1x0U5GnICO9fj4eHm57/XhhH8KuwicLqjuYtwEIz8b16+TluxRXFycc4f+/xgHYcJCOiNOwDBvKxV7IoaKFc0cLKsLqKcYT8F7775Lt2/dlNbsUfv27Y3gA8S6IqMUzqgT8MhYrX/Pmcu/2syg6YLpKchPvXr16GTMCWnJHn0xe7aKr8RhmKMaUohH9/jx4/Iy65R883fq1KGT8kEXTE8wnoIvZn8uLdmjo0ePUqCpWGQWMkK5GNXdWLNmTdEUYIe2b9lGpUqWEn7ogukpSK9du7aUePWqtGS9EFOnMUWYqBLIiLJITRUaNGiQvMR6xV+Oo+LFSgg/dIH0FKRXsWJF2r51C5GNgwjQn2LEmUHMyzMBIfKAYPr06fJ0a/Xr5s0U2q6d8CF3bu8VQQBpoo3+22VLKd3G8UuffvqpirOkNePY+LZhwwZ5urXq2b2bsO/N8t8A6ZYtU5YWzptHKcnJ0qL1Wr9+vYqzRDTOfWYcyMPOWtn4ZtbnEbOED96uBQGkix69RQsXiGZuuxQbE6P8kcxiRJ+lOFCqVCm6edOe+vKeHbsoV2BmLcE5gNkFaVatWiXzBtj4BCQlJVHJkiWN4IPvmYAdxoE6derIU61XWkoqNW/STPihC6KnGF/EjRs1pMgF8229ARhFWLt2bSP4AMsqBBwzDrRs2VKeao9at2ol/NAF0lNwA0BIm9b0w6rv6I/0dGnNHrVwHEWH6n/ABeMAZqHbpagVUcIHb78DkB46RXp170E7tm+ztRoKdejQwXwDsMBIwBXjQN++feVp1irpxg2q/kzmR4ouiNkBaRYsWIiGhYfT/r17pEX7hAnpRryZeEbMdRIH7Bp4tW3LVuWULojZAWlWqFCR/vr2W3T6pP1TZPv166fyymA6759zeu26ASkpKTRj2jSxwIa3iiDj5Vu0aFExoyVy/ny0B0iL9kl3A7DYkTjQp08feZr1On/mDJUoXlz4oQuoOyD4QXmCRLUaL73PZs2iqOXLqUunztT65Vdo0sSJ0qr1woQWI95MHCNeBOIA2qztUho/BTVkY5UuqO6ANIrxL79zp440iJ/q8e+/L4atGPkEh6OjpWVrhUFuJj/OMH8uH9OsWTN5mvVCa2FwixbCD11Q3QFPAIbRly1bxmHIOP5nNE9/MuVjun3rlrRundDXbvjDHGYCthoH0BRt55Sj/27eLKqM3ngPGO8A57SM4+FDhtDF8+ekZWukaZLewgREGQcwsOkGVwnt0v69e5Vz5qC5C4KsOw6MG4CiKcbijieMnEbRaOSRwYoyATOMA3Du2LFj8nTrFXfpEj1ZuIjZQQd0AXUX4wYM7N+fjh89Ki1bI9ErJotAyTQmYKTpAK1du1aebo8i582nyhUrUcECBcToDNSMKleqpH45uqC6A9LA8McRw4dT7Alrn4A1q1erOEuGMwHBpgM0efJkebo9wuiFrxdF0qtcRHTr0oV69+wpvmL7yOpbdt8PSKN06adp8sSPKP6ytSOqJ3L11xxrpiUT8DSj1ujE5GM7lZGRQVcS4mnfnt30y88baQu/mA/u30exXF4Pey1c+Hi/Mv5B4Nq8eYOof58wMfzdSmHdJCPOzC2mFCO0nxH/QN9pus0thkpONbKIGdMoNwcwu0+BUQ6/8ZcRMmXfC3OqK1SoYL4B+xil2Yz6p7+teLL5l03UoH594VsuTUA9wbgJEz6wZhDvvn37VHwl6IlUCmPUP9F57C9au2a1+NXDr+wUPTqMmzDqjZGU7OORc1OnfqLiK8FUACW8B5IZ8c/g4GB5mb2K45ekt9qH7oVxExrUf5Z27dwhLXtfWNrBiC/jUP4b2siIE/A1evr0aXmpferapbPwRxc4bwM75cqWoYT4OGnde8JAB6eZMusZFzl8D0ydOlVebo9mTMscQ+PtYudeGB9oUyajpdS7zTFTpkxRcZWI+r+zSjOqGELnsV3tQmipDMqTR/ihC5avgL2aNWrQ2dOnpCfZF9p/atWqZQQeIMYo8rXCCrHqZAwkskPdu2auwaMLki8x3gefRcyUnmRf69b9pOIp+Ya5p15h1MntbZigjQ+wwEDfDNDKCsh3/359KfGqd5bjaSeHW5p4mbmvsCipumDPHms7socPGybs6oJjBbDdPrQdHTqY/SV5drtO0HP4+LqXBjDqIitny9y4nkglS3h/dHRWMV7EbUPa0G4vVEmdZsWAfswDlZtRg7XA5s2bZZK+1aaNG4U9u4of4wagK/PAvuyt7r5p0yYVPwkGYSG2WVJfRl3csGFDSwY0zZo5U9jTBccKjBuAmZW/ZWOxcdQen3vuORU/CVob3JKaNQkiIiJk8r7TsNdeE7Z0wbECPHkA3ZXZ6S2bMWOGipsE42/dVlNGJYLOEV9/HTd98UVhSxccK4BtfLG+9eabdO6MZ3nFFiyaNaZfZDzSHEYlhOWJfaXbyclUvlw5YUcXHCuA7SKFC4sWUk87a9COZo4Zg5Zmj4Wdj9S4IfDhhx9KU94V1vt56sknhQ1dcKwAtsuVLUszp0+npBvuL882YcIEFSfJWQYxzJYc5pABX/QbX7p4Ufz6kL4uOFYA289UrUpLFn9FqSkp0rOsac2aNQ4xkog5YN7Qx4xKuEiRInTkyBFp2ju6eiWBSpTwbdPzg4BtNH9HrVhOGX9kvVcQK4oZPx4TkxivSjVXA2xPctmLndrXExPp6VLenyPsDrANH5YuXkxpqVl7Ai5duqRbNRfL/nhdxZlTjDKEuq63tilBEVT4iSdEurrgWAFsVyhfPnMqUxYm82F7kwYNGqh4SLAqVjHGJ8JmPVgdXBl84YUXhCPZ1a6dO0V6dn0FA9ivW6cOrfj2G0pPu/984qtXr1Ljxo1VHCTYS6cO41O9xDjsfIon4cKFC9I1zzTni8zFLKzqgNEB+89zUDf89BPduZMhPXPV+fPnqb4cJGACO7RijJUl6sCo5W0A3gnZWdR70MDMafy6wFgF7Ae3bEnbt2yRXrkKI0Y0ZT5igd2kLBUMOmy6jNqRJ/uG3cnIoNqy10gXGCsw2oE6hIbSnl07pWeOwr5ihV1rO4iBbdtZ4ZFTk/wMxo0b51Z35rlzZ6lAgfziWl1wrMC4AX3DwuiI0+QN5GXs2LEOeZRgdlELxlZhRznsGOHgHD7JsSVgVrRhQ+YaCna+gGEbPmAc6inTfpcHDx4U86fNeZNgcgW2dfELFWUw7d7BScxSmTRp0gO3OomYmdl6qAuMlcCHvn3CKJWroFjoD4NpkQdzniTfMU8xfqdxjMMuSwA1hlWrVslwuwq7rOI8XVCsBD404Wo1amTGUEgnsHPe+4xfC0OuXTb2BNjAWTfSIiQkc1NlXVCsxhgOowF95baX91kV9h/4iNFu2o9mbSxDb2z+hgkYOK4LiNUYL2MT+Ob5J4M85Tg9y7i8Gwyw7Pzw118XO1nb+QLWIX1cxSAPOV7tmE2Mww0wsPPrVwf7gzUc/L6s90S4EasZ9aL2p+CzL9f5ScRG/l8xHRmvbsbmT6rP/Is5bfcNYPvpHHQs2YOOc0zZxTJi2C22IZMjy313VICD0JuD8AuT4hwcX8G20ph4+WvHcvJYuBadThi1jFoc9v566INvFqp3M5mNHJSjHJxrTIYueJ7Aad1hkjhtrM2wl/kPs5jBPN13mO5MA6YEk4d5pITRYhiu/TzTlcGv8AMmglnCQfuR2crs5yAeY04xZ5kLzEUT5xj87zifG83sZH7mNFALQ7A/Z/ArH8u8znRmGjH4tRdkbFBAwP8BZB1ntMFNynoAAAAASUVORK5CYII=";

		private bool IsTopLeftBoxMinimized
		{
			get { return runtimeMinimizeTopLeftOverride.HasValue ? runtimeMinimizeTopLeftOverride.Value : MinimizeTopLeftBox; }
		}

		private bool IsSummaryBoxMinimized
		{
			get { return runtimeMinimizeSummaryOverride.HasValue ? runtimeMinimizeSummaryOverride.Value : MinimizeSummaryBoxes; }
		}

		private bool IsModernRichUi
		{
			get { return runtimeModernUiOverride.HasValue ? runtimeModernUiOverride.Value : EnableModernRichUi; }
		}

		private MarketProfilePeriodMode EffectiveProfileMode
		{
			get { return runtimeProfileModeOverride.HasValue ? runtimeProfileModeOverride.Value : ProfileMode; }
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "AK10X Market Profile for Indian Market (NSE / BSE / MCX). Pure Day Profile trading with no multi-session partitioning.";
				Name = "AK10XIndiaMarketProfile";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				PaintPriceMarkers = false;
				IsAutoScale = true;
				ScaleJustification = ScaleJustification.Right;
				IsSuspendedWhileInactive = false;

				ProfileMode = MarketProfilePeriodMode.Daily;
				TpoSize = 0;
				AutoScaleTpoByScreenResolution = false;
				TpoIntervalMinutes = 30;
				ValueAreaPercent = 70;
				InitialBalanceMinutes = 60;
				DaysToDisplay = 30;

				FilterByTradingHours = true;
				MarketOpen = 91500;
				MarketClose = 153000;
				SessionLabel = "NSE Day";

				SplitLatestDay = true;
				ShowVolumeProfile = true;
				ShowInitialBalance = true;
				ShowSinglePrints = true;
				ShowPoorHighLow = true;
				ProfileWidth = 240;
				FontSize = 11;

				VirginityBreakRule = VirginityBreakRuleMode.CrossBased;
				ShowPoorHigh = true;
				ShowPoorLow = true;
				ShowSwingHigh = true;
				ShowSwingLow = true;
				ShowWeeklyHigh = true;
				ShowWeeklyLow = true;
				ShowVirginVAH = true;
				ShowVirginVAL = true;
				ShowVirginVPoC = true;
				ShowVirginVPPoC = true;
				ShowVirginMidLine = true;

				EnableRunningDayLevels = true;
				ShowRunningDayVAH = true;
				ShowRunningDayVAL = true;
				ShowRunningDayPOC = true;
				ShowRunningDayPPOC = true;

				EnableModernRichUi = false;
				ShowSummary = true;
				FixSummaryBoxAtBottom = true;
				FixSessionHeaderAtTop = true;
				MinimizeSummaryBoxes = false;
				ShowTopLeftBox = true;
				ShowLogoInTopBox = true;
				MinimizeTopLeftBox = false;
				ShowChartButtons = true;

				ShowDtrVsAtr = true;
				DtrAtrLength = 14;
				DtrAtrSmoothing = DtrAtrSmoothingType.RMA;
				ShowDtrAtrHeader = true;
			}
			else if (State == State.Configure)
			{
				Calculate = Calculate.OnEachTick;
				bool isPrimaryOneMinute = (BarsPeriod != null && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value == 1)
					|| (ChartBars != null && ChartBars.Properties != null && ChartBars.Properties.BarsPeriod != null
						&& ChartBars.Properties.BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && ChartBars.Properties.BarsPeriod.Value == 1)
					|| (ChartControl != null && ChartControl.PrimaryBars != null && ChartControl.PrimaryBars.Properties != null
						&& ChartControl.PrimaryBars.Properties.BarsPeriod != null
						&& ChartControl.PrimaryBars.Properties.BarsPeriod.BarsPeriodType == BarsPeriodType.Minute
						&& ChartControl.PrimaryBars.Properties.BarsPeriod.Value == 1);
				if (!isPrimaryOneMinute)
				{
					AddDataSeries(BarsPeriodType.Minute, 1);
				}
			}
			else if (State == State.DataLoaded)
			{
				lock (profileLock)
				{
					rawMinuteSamples.Clear();
					profiles.Clear();
					atrDailyHistory.Clear();
					isWarmingUpAtrHistory = false;
					if (Instrument != null && !string.IsNullOrEmpty(Instrument.FullName))
					{
						SortedDictionary<DateTime, DailyOhlcSample> cachedAtr;
						if (SharedAtrDailyCache.TryGetValue(Instrument.FullName, out cachedAtr) && cachedAtr != null)
						{
							lock (cachedAtr)
							{
								foreach (var kv in cachedAtr)
									atrDailyHistory[kv.Key] = kv.Value;
							}
						}
					}
					chartDayFirstBar.Clear();
					chartDayLastBar.Clear();
					manuallySplitProfileKeys.Clear();
					manuallyUnsplitProfileKeys.Clear();
					activeChartButtons.Clear();
					lastHiddenChartBar = -1;
					lastVolumeBarIndex = -1;
					lastCumulativeVolume = 0;
					cachedPriceStep = 0;
					lastRebuiltSampleCount = 0;
					didAutoAdjustChartProps = false;
					isPotentialCanvasDrag = false;
					isDraggingCanvas = false;
					maxScrollCached = 0f;
					lastRenderTargetW = 0f;
					lastRenderTargetH = 0f;
					currentLoadedHistoryDays = 0;
					lastTrackedScrollBarVal = double.NaN;
					hasManualPixelScroll = false;
					lastTrackedFromIdx = -1;
					lastTrackedToIdx = -1;
					currentScrollPx = float.NaN;
					runtimeMinimizeTopLeftOverride = null;
					runtimeMinimizeSummaryOverride = null;
					isProfileDropdownOpen = false;
					DisposeLogoBitmap();
				}
				EnsureStandaloneD2DFactory();
				EnsureMouseHookAttached();
				AttachAccelpixLiveStream();
				RequestHistoricalMinuteBarsAsync();
				WarmupAtrDailyHistoryAsync();
			}
			else if (State == State.Terminated)
			{
				RestoreSilencedChartStyle();
				DetachAccelpixLiveStream();
				DisposeLogoBitmap();
				DetachMouseHook();
			}
		}

		public override void OnRenderTargetChanged()
		{
			DisposeLogoBitmap();
		}

		private static void EnsureStandaloneD2DFactory()
		{
			try
			{
				var f = NinjaTrader.Core.Globals.D2DFactory;
				GC.KeepAlive(f);
			}
			catch { }
		}

		private void SilenceStandaloneCandles(ChartControl cc)
		{
			try
			{
				ChartBars cb = ChartBars ?? (cc != null ? cc.PrimaryBars : null);
				if (cb != null && cb.Properties != null && cb.Properties.ChartStyle != null)
				{
					var style = cb.Properties.ChartStyle;
					if (savedChartStyle == null && style.UpBrush != Brushes.Transparent)
					{
						savedChartStyle = style;
						savedUpBrush = style.UpBrush;
						savedDownBrush = style.DownBrush;
						savedStrokeBrush = style.Stroke != null ? style.Stroke.Brush : null;
						savedStroke2Brush = style.Stroke2 != null ? style.Stroke2.Brush : null;
					}
					style.UpBrush = Brushes.Transparent;
					style.DownBrush = Brushes.Transparent;
					if (style.Stroke != null) style.Stroke.Brush = Brushes.Transparent;
					if (style.Stroke2 != null) style.Stroke2.Brush = Brushes.Transparent;
				}
			}
			catch { }
		}

		private void RestoreSilencedChartStyle()
		{
			try
			{
				var style = savedChartStyle;
				savedChartStyle = null;
				if (style != null)
				{
					if (savedUpBrush != null) style.UpBrush = savedUpBrush;
					if (savedDownBrush != null) style.DownBrush = savedDownBrush;
					if (style.Stroke != null && savedStrokeBrush != null) style.Stroke.Brush = savedStrokeBrush;
					if (style.Stroke2 != null && savedStroke2Brush != null) style.Stroke2.Brush = savedStroke2Brush;
				}
			}
			catch { }
		}

		private void AttachAccelpixLiveStream()
		{
			try
			{
				EnsureAccelpixProviderConnected();

				if (!isConnectionStatusHooked)
				{
					Connection.ConnectionStatusUpdate += OnAccelpixConnectionStatusUpdate;
					isConnectionStatusHooked = true;
				}
			}
			catch { }
		}

		private void DetachAccelpixLiveStream()
		{
			try
			{
				if (isConnectionStatusHooked)
				{
					Connection.ConnectionStatusUpdate -= OnAccelpixConnectionStatusUpdate;
					isConnectionStatusHooked = false;
				}
				subscribedMarketDataInstrument = null;
				if (activeAccelpixBarsReq != null)
				{
					try
					{
						activeAccelpixBarsReq.Update -= OnAccelpixBarsUpdate;
						activeAccelpixBarsReq.Dispose();
					}
					catch { }
					activeAccelpixBarsReq = null;
				}
			}
			catch { }
		}

		private static void EnsureAccelpixProviderConnected()
		{
			try
			{
				bool alreadyActive = false;
				lock (Connection.Connections)
				{
					foreach (var c in Connection.Connections)
					{
						if (c == null || c.Options == null) continue;
						string n = c.Options.Name ?? string.Empty;
						string t = c.Options.TypeName ?? string.Empty;
						if (n.IndexOf("Accel", StringComparison.OrdinalIgnoreCase) >= 0 ||
							t.IndexOf("SampleOpenConnect", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							if (c.Status == ConnectionStatus.Connected || c.Status == ConnectionStatus.Connecting ||
								c.PriceStatus == ConnectionStatus.Connected || c.PriceStatus == ConnectionStatus.Connecting)
							{
								alreadyActive = true;
							}
							break;
						}
					}
				}
				if (alreadyActive)
					return;

				ConnectOptions accelOpt = null;
				if (NinjaTrader.Core.Globals.ConnectOptions != null)
				{
					foreach (ConnectOptions opt in NinjaTrader.Core.Globals.ConnectOptions)
					{
						if (opt == null) continue;
						string n = opt.Name ?? string.Empty;
						string t = opt.TypeName ?? string.Empty;
						if (n.IndexOf("Accel", StringComparison.OrdinalIgnoreCase) >= 0 ||
							t.IndexOf("SampleOpenConnect", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							opt.ConnectOnStartup = true;
							accelOpt = opt;
							break;
						}
					}
				}

				if (accelOpt != null)
				{
					System.Threading.ThreadPool.QueueUserWorkItem(_ =>
					{
						try { Connection.Connect(accelOpt); } catch { }
					});
				}
			}
			catch { }
		}

		private void OnAccelpixConnectionStatusUpdate(object sender, ConnectionStatusEventArgs e)
		{
			if (e == null || State == State.Terminated)
				return;
			if (e.Status == ConnectionStatus.Connected || e.PriceStatus == ConnectionStatus.Connected)
			{
				didTriggerChartHistoricalReload = false;
				RequestHistoricalMinuteBarsAsync();
				WarmupAtrDailyHistoryAsync();
				System.Threading.Tasks.Task.Run(async () =>
				{
					try
					{
						await System.Threading.Tasks.Task.Delay(2500).ConfigureAwait(false);
						if (State != State.Terminated)
						{
							RequestHistoricalMinuteBarsAsync();
							WarmupAtrDailyHistoryAsync();
						}
					}
					catch { }
				});
			}
		}

		protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
		{
			ProcessLiveMarketTick(marketDataUpdate);
		}

		private void OnInstrumentMarketDataUpdate(object sender, MarketDataEventArgs e)
		{
			ProcessLiveMarketTick(e);
		}

		private void ProcessLiveMarketTick(MarketDataEventArgs e)
		{
			if (e == null || State == State.Terminated)
				return;
			if (e.MarketDataType != MarketDataType.Last || e.Price <= 0)
				return;

			try
			{
				DateTime tickTime = e.Time > DateTime.MinValue ? e.Time : DateTime.Now;
				DateTime minuteBucket = new DateTime(tickTime.Year, tickTime.Month, tickTime.Day, tickTime.Hour, tickTime.Minute, 0, tickTime.Kind);
				double tickVol = Math.Max(0, Convert.ToDouble(e.Volume));
				bool changed = false;

				lock (profileLock)
				{
					lastLiveTickPrice = e.Price;
					lastLiveTickTime = tickTime;

					if (rawMinuteSamples.Count > 0)
					{
						MinuteSample lastSample = rawMinuteSamples[rawMinuteSamples.Count - 1];
						if (lastSample.Time == minuteBucket)
						{
							lastSample.High = Math.Max(lastSample.High, e.Price);
							lastSample.Low = Math.Min(lastSample.Low, e.Price);
							lastSample.Close = e.Price;
							lastSample.Volume += tickVol;
							rawMinuteSamples[rawMinuteSamples.Count - 1] = lastSample;

							double step = GetPriceStep();
							MinuteSample delta = lastSample;
							delta.Volume = tickVol;
							AccumulateSampleIntoDay(delta, step, isLiveTickUpdate: true);
							changed = true;
						}
						else if (minuteBucket > lastSample.Time)
						{
							MinuteSample newSample = new MinuteSample
							{
								Time = minuteBucket,
								Open = e.Price,
								High = e.Price,
								Low = e.Price,
								Close = e.Price,
								Volume = tickVol,
								BarIndex = lastSample.BarIndex + 1
							};
							rawMinuteSamples.Add(newSample);
							double step = GetPriceStep();
							AccumulateSampleIntoDay(newSample, step, isLiveTickUpdate: false);
							changed = true;
						}
					}
				}

				if (changed)
				{
					RequestThrottledChartRefresh(false);
				}
			}
			catch { }
		}

		private void OnAccelpixBarsUpdate(object sender, BarsUpdateEventArgs e)
		{
			if (e == null || e.BarsSeries == null || State == State.Terminated)
				return;

			try
			{
				bool changed = false;
				lock (profileLock)
				{
					int count = e.BarsSeries.Count;
					if (count <= 0)
						return;

					int idx = Math.Max(0, Math.Min(e.MaxIndex, count - 1));
					DateTime t = e.BarsSeries.GetTime(idx);
					double open = e.BarsSeries.GetOpen(idx);
					double high = e.BarsSeries.GetHigh(idx);
					double low = e.BarsSeries.GetLow(idx);
					double close = e.BarsSeries.GetClose(idx);
					double barVol = Math.Max(0, Convert.ToDouble(e.BarsSeries.GetVolume(idx)));

					lastLiveTickPrice = close;
					lastLiveTickTime = DateTime.Now;

					if (rawMinuteSamples.Count > 0 && rawMinuteSamples[rawMinuteSamples.Count - 1].Time == t)
					{
						MinuteSample prev = rawMinuteSamples[rawMinuteSamples.Count - 1];
						double volDiff = Math.Max(0, barVol - prev.Volume);
						prev.High = Math.Max(prev.High, high);
						prev.Low = Math.Min(prev.Low, low);
						prev.Close = close;
						prev.Volume = Math.Max(prev.Volume, barVol);
						rawMinuteSamples[rawMinuteSamples.Count - 1] = prev;

						double step = GetPriceStep();
						MinuteSample delta = prev;
						delta.Volume = volDiff;
						AccumulateSampleIntoDay(delta, step, isLiveTickUpdate: true);
						changed = true;
					}
					else if (rawMinuteSamples.Count == 0 || t > rawMinuteSamples[rawMinuteSamples.Count - 1].Time)
					{
						MinuteSample next = new MinuteSample
						{
							Time = t,
							Open = open,
							High = high,
							Low = low,
							Close = close,
							Volume = barVol,
							BarIndex = rawMinuteSamples.Count > 0 ? (rawMinuteSamples[rawMinuteSamples.Count - 1].BarIndex + 1) : 0
						};
						rawMinuteSamples.Add(next);
						double step = GetPriceStep();
						AccumulateSampleIntoDay(next, step, isLiveTickUpdate: false);
						changed = true;
					}
				}

				if (changed)
				{
					RequestThrottledChartRefresh(false);
				}
			}
			catch { }
		}

		private void RequestThrottledChartRefresh(bool forceImmediate)
		{
			DateTime now = DateTime.UtcNow;
			if (!forceImmediate && (now - lastLiveRefreshUtc).TotalMilliseconds < 200.0)
				return;
			lastLiveRefreshUtc = now;

			if (System.Threading.Interlocked.CompareExchange(ref uiRefreshQueued, 1, 0) != 0)
				return;

			try
			{
				ChartControl cc = hookedChartControl ?? ChartControl;
				if (cc != null)
				{
					cc.Dispatcher.BeginInvoke(new Action(() =>
					{
						System.Threading.Interlocked.Exchange(ref uiRefreshQueued, 0);
						try { ForceRefresh(); cc.InvalidateVisual(); } catch { }
					}), System.Windows.Threading.DispatcherPriority.Render);
				}
				else
				{
					System.Threading.Interlocked.Exchange(ref uiRefreshQueued, 0);
					ForceRefresh();
				}
			}
			catch
			{
				System.Threading.Interlocked.Exchange(ref uiRefreshQueued, 0);
			}
		}

		private void RequestHistoricalMinuteBarsAsync(int requestedDaysBack = 0)
		{
			try
			{
				Instrument inst = Instrument;
				if (inst == null)
					return;

				System.Threading.Tasks.Task.Run(async () =>
				{
					try
					{
						await System.Threading.Tasks.Task.Delay(15).ConfigureAwait(false);
						if (State == State.Terminated)
							return;

						int existingSampleCount;
						int existingProfileDays;
						lock (profileLock)
						{
							existingSampleCount = rawMinuteSamples.Count;
							existingProfileDays = profiles.Count;
						}

						int targetDailySessions = Math.Min(25, Math.Max(14, DaysToDisplay));
						int daysBack;
						if (requestedDaysBack > 0)
						{
							daysBack = requestedDaysBack;
						}
						else if (EffectiveProfileMode == MarketProfilePeriodMode.Monthly)
						{
							daysBack = 180;
						}
						else if (EffectiveProfileMode == MarketProfilePeriodMode.Weekly)
						{
							daysBack = 65;
						}
						else
						{
							// Standalone charts default to 5 days in NT8; ensure full multi-day history (~25-30 sessions) loads automatically so scrolling & 10-day Vol MA work on any single screen!
							daysBack = (existingProfileDays >= targetDailySessions && existingSampleCount >= 2500)
								? 3
								: Math.Max(42, (int)Math.Ceiling(DaysToDisplay * 1.65));
						}

						lock (profileLock)
						{
							if (daysBack > currentLoadedHistoryDays)
								currentLoadedHistoryDays = daysBack;
						}

						DateTime toDate = DateTime.Now.AddDays(1);
						DateTime fromDate = DateTime.Now.AddDays(-daysBack);
						var barsReq = new BarsRequest(inst, fromDate, toDate)
						{
							BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 1 },
							LookupPolicy = daysBack <= 5 ? LookupPolicies.Provider : LookupPolicies.Repository
						};
						if (IsIndianNseSymbolForInstrument(inst))
						{
							try { barsReq.TradingHours = TradingHours.Get("MCX"); } catch { }
							if (barsReq.TradingHours == null && inst.MasterInstrument != null)
								barsReq.TradingHours = inst.MasterInstrument.TradingHours;
						}
						else if (inst.MasterInstrument != null && inst.MasterInstrument.TradingHours != null)
						{
							barsReq.TradingHours = inst.MasterInstrument.TradingHours;
						}
						else
						{
							try { barsReq.TradingHours = TradingHours.Get("Default 24 x 7"); } catch { }
						}

						barsReq.Update += OnAccelpixBarsUpdate;
						var oldReq = activeAccelpixBarsReq;
						activeAccelpixBarsReq = barsReq;
						if (oldReq != null)
						{
							try
							{
								oldReq.Update -= OnAccelpixBarsUpdate;
								oldReq.Dispose();
							}
							catch { }
						}

						barsReq.Request((bars, errorCode, errorMessage) =>
						{
							try
							{
								if (State == State.Terminated || errorCode != ErrorCode.NoError || bars == null || bars.Bars == null || bars.Bars.Count <= 0)
									return;

								bool didUpdate = false;
								DateTime latestReqBarTime = DateTime.MinValue;
								lock (profileLock)
								{
									int reqCount = bars.Bars.Count;
									if (reqCount > 0)
									{
										latestReqBarTime = bars.Bars.GetTime(reqCount - 1);
										Dictionary<DateTime, MinuteSample> byTime = new Dictionary<DateTime, MinuteSample>(reqCount + rawMinuteSamples.Count);

										for (int i = 0; i < rawMinuteSamples.Count; i++)
										{
											MinuteSample existing = rawMinuteSamples[i];
											byTime[existing.Time] = existing;
										}

										for (int i = 0; i < reqCount; i++)
										{
											DateTime t = bars.Bars.GetTime(i);
											double barVol = Math.Max(0, Convert.ToDouble(bars.Bars.GetVolume(i)));

											byTime[t] = new MinuteSample
											{
												Time = t,
												Open = bars.Bars.GetOpen(i),
												High = bars.Bars.GetHigh(i),
												Low = bars.Bars.GetLow(i),
												Close = bars.Bars.GetClose(i),
												Volume = barVol,
												BarIndex = i
											};
										}

										List<MinuteSample> merged = byTime.Values.OrderBy(s => s.Time).ToList();
										for (int i = 0; i < merged.Count; i++)
										{
											MinuteSample ms = merged[i];
											ms.BarIndex = i;
											merged[i] = ms;
										}

										rawMinuteSamples.Clear();
										rawMinuteSamples.AddRange(merged);
										double desiredStep = ComputeAutoOrManualPriceStep();
										RebuildAllDayProfiles(desiredStep);
										didUpdate = true;
									}
								}

								if (didUpdate)
								{
									RequestThrottledChartRefresh(true);

									// Only reload the host Chart if PrimaryBars initially opened completely empty (0 bars) while disconnected
									ChartControl cc = hookedChartControl ?? ChartControl;
									if (cc != null && !didTriggerChartHistoricalReload && latestReqBarTime > DateTime.MinValue)
									{
										cc.Dispatcher.BeginInvoke(new Action(() =>
										{
											try
											{
												if (cc.PrimaryBars != null && cc.PrimaryBars.Bars != null && cc.PrimaryBars.Bars.Count == 0)
												{
													didTriggerChartHistoricalReload = true;
													cc.OnReloadHistoricalDataHotKey(cc, null);
												}
											}
											catch { }
										}));
									}
								}
							}
							catch { }
						});
					}
					catch { }
				});
			}
			catch { }
		}

		private void PersistAtrHistoryToSharedCacheLocked(string instFullName)
		{
			if (string.IsNullOrEmpty(instFullName) || atrDailyHistory.Count == 0)
				return;

			var target = SharedAtrDailyCache.GetOrAdd(instFullName, _ => new SortedDictionary<DateTime, DailyOhlcSample>());
			lock (target)
			{
				foreach (var kv in atrDailyHistory)
					target[kv.Key] = kv.Value;
			}
		}

		private void WarmupAtrDailyHistoryAsync()
		{
			try
			{
				Instrument inst = Instrument;
				if (inst == null || string.IsNullOrEmpty(inst.FullName))
					return;

				lock (profileLock)
				{
					if (isWarmingUpAtrHistory)
						return;
					isWarmingUpAtrHistory = true;
				}

				string instName = inst.FullName;
				bool isNse = IsIndianNseSymbolForInstrument(inst);

				System.Threading.Tasks.Task.Run(() =>
				{
					try
					{
						if (State == State.Terminated)
							return;

						TradingHours reqTh = null;
						if (isNse)
						{
							try { reqTh = TradingHours.Get("MCX"); } catch { }
							if (reqTh == null && inst.MasterInstrument != null)
								reqTh = inst.MasterInstrument.TradingHours;
						}
						else if (inst.MasterInstrument != null && inst.MasterInstrument.TradingHours != null)
						{
							reqTh = inst.MasterInstrument.TradingHours;
						}
						else
						{
							try { reqTh = TradingHours.Get("Default 24 x 7"); } catch { }
						}

						DateTime toDate = DateTime.Now.Date.AddDays(1);
						SortedDictionary<DateTime, DailyOhlcSample> dailyEodDays = new SortedDictionary<DateTime, DailyOhlcSample>();
						SortedDictionary<DateTime, DailyOhlcSample> minuteRthDays = new SortedDictionary<DateTime, DailyOhlcSample>();

						Action<Bars, bool> collectMinuteRth = (barsObj, filterRth) =>
						{
							if (barsObj == null || barsObj.Count <= 0)
								return;
							int cnt = barsObj.Count;
							for (int i = 0; i < cnt; i++)
							{
								DateTime t = barsObj.GetTime(i);
								DateTime dayDate = t.Date;
								DateTime dayStart;
								DateTime dayClose;
								int periodIdx;
								int expPeriods;
								if (filterRth && !TryGetDaySession(t, out dayDate, out dayStart, out dayClose, out periodIdx, out expPeriods))
									continue;

								double o = barsObj.GetOpen(i);
								double h = barsObj.GetHigh(i);
								double l = barsObj.GetLow(i);
								double c = barsObj.GetClose(i);
								if (h <= 0 || l <= 0 || c <= 0 || h < l)
									continue;

								DailyOhlcSample cur;
								if (!minuteRthDays.TryGetValue(dayDate, out cur))
								{
									minuteRthDays[dayDate] = new DailyOhlcSample
									{
										Date = dayDate,
										Open = o,
										High = h,
										Low = l,
										Close = c
									};
								}
								else
								{
									if (h > cur.High) cur.High = h;
									if (l < cur.Low) cur.Low = l;
									cur.Close = c;
									minuteRthDays[dayDate] = cur;
								}
							}
						};

						Action<Bars> collectDailyBars = (barsObj) =>
						{
							if (barsObj == null || barsObj.Count <= 0)
								return;
							int cnt = barsObj.Count;
							for (int i = 0; i < cnt; i++)
							{
								DateTime dt = barsObj.GetTime(i).Date;
								if (isNse && (dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday))
									continue;
								double o = barsObj.GetOpen(i);
								double h = barsObj.GetHigh(i);
								double l = barsObj.GetLow(i);
								double c = barsObj.GetClose(i);
								if (h <= 0 || l <= 0 || c <= 0 || h < l)
									continue;

								dailyEodDays[dt] = new DailyOhlcSample
								{
									Date = dt,
									Open = o > 0 ? o : c,
									High = h,
									Low = l,
									Close = c
								};
							}
						};

						// 1. Instant local Repository 1-Minute RTH seed (-180 days)
						try
						{
							using (var doneRepoMin = new System.Threading.ManualResetEventSlim(false))
							using (var repoMinReq = new BarsRequest(inst, DateTime.Now.Date.AddDays(-180), toDate))
							{
								repoMinReq.BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 1 };
								repoMinReq.LookupPolicy = LookupPolicies.Repository;
								repoMinReq.MergePolicy = MergePolicy.DoNotMerge;
								if (reqTh != null) repoMinReq.TradingHours = reqTh;
								repoMinReq.Request((r, err, msg) =>
								{
									try
									{
										if (err == ErrorCode.NoError && r != null && r.Bars != null)
											collectMinuteRth(r.Bars, true);
									}
									catch { }
									finally
									{
										try { doneRepoMin.Set(); } catch { }
									}
								});
								doneRepoMin.Wait(1200);
							}
						}
						catch { }

						// 2. Multi-year Daily EOD history (-900 days) for Daily, Weekly(14) & Monthly(14) ATR warmup
						foreach (LookupPolicies pol in new LookupPolicies[] { LookupPolicies.Repository, LookupPolicies.Provider })
						{
							if (State == State.Terminated)
								return;
							try
							{
								using (var doneDay = new System.Threading.ManualResetEventSlim(false))
								using (var dayReq = new BarsRequest(inst, DateTime.Now.Date.AddDays(-900), toDate))
								{
									dayReq.BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Day, Value = 1 };
									dayReq.LookupPolicy = pol;
									dayReq.MergePolicy = MergePolicy.DoNotMerge;
									if (reqTh != null) dayReq.TradingHours = reqTh;
									dayReq.Request((r, err, msg) =>
									{
										try
										{
											if (err == ErrorCode.NoError && r != null && r.Bars != null)
												collectDailyBars(r.Bars);
										}
										catch { }
										finally
										{
											try { doneDay.Set(); } catch { }
										}
									});
									doneDay.Wait(pol == LookupPolicies.Repository ? 1000 : 3000);
								}
							}
							catch { }
						}

						// 3. Provider 1-Minute RTH warmup (-180 days) if recent RTH history has fewer than 85 sessions
						if (State != State.Terminated && minuteRthDays.Count < 85)
						{
							try
							{
								using (var doneProvMin = new System.Threading.ManualResetEventSlim(false))
								using (var provMinReq = new BarsRequest(inst, DateTime.Now.Date.AddDays(-180), toDate))
								{
									provMinReq.BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 1 };
									provMinReq.LookupPolicy = LookupPolicies.Provider;
									provMinReq.MergePolicy = MergePolicy.DoNotMerge;
									if (reqTh != null) provMinReq.TradingHours = reqTh;
									provMinReq.Request((r, err, msg) =>
									{
										try
										{
											if (err == ErrorCode.NoError && r != null && r.Bars != null)
												collectMinuteRth(r.Bars, true);
										}
										catch { }
										finally
										{
											try { doneProvMin.Set(); } catch { }
										}
									});
									doneProvMin.Wait(3500);
								}
							}
							catch { }
						}

						if (State == State.Terminated)
							return;

						bool updated = false;
						lock (profileLock)
						{
							int beforeCount = atrDailyHistory.Count;
							foreach (var kv in dailyEodDays)
							{
								if (!atrDailyHistory.ContainsKey(kv.Key))
								{
									atrDailyHistory[kv.Key] = kv.Value;
									updated = true;
								}
							}
							foreach (var kv in minuteRthDays)
							{
								atrDailyHistory[kv.Key] = kv.Value;
								updated = true;
							}
							foreach (var kv in profiles)
							{
								DayProfile dp = kv.Value;
								if (dp != null && dp.BarCount > 0 && dp.High >= dp.Low && dp.Low < double.MaxValue && dp.Close > 0)
								{
									atrDailyHistory[dp.SessionDate.Date] = new DailyOhlcSample
									{
										Date = dp.SessionDate.Date,
										Open = dp.Open > 0 ? dp.Open : dp.Close,
										High = dp.High,
										Low = dp.Low,
										Close = dp.Close
									};
									updated = true;
								}
							}
							if (atrDailyHistory.Count != beforeCount)
								updated = true;

							PersistAtrHistoryToSharedCacheLocked(instName);
						}

						if (updated && State != State.Terminated)
							RequestThrottledChartRefresh(true);
					}
					catch { }
					finally
					{
						lock (profileLock)
						{
							isWarmingUpAtrHistory = false;
						}
					}
				});
			}
			catch
			{
				lock (profileLock)
				{
					isWarmingUpAtrHistory = false;
				}
			}
		}

		private static string FormatPineRound2(double val)
		{
			if (double.IsNaN(val) || double.IsInfinity(val))
				return "0";
			double rounded = Math.Round(Math.Abs(val), 2, MidpointRounding.AwayFromZero) * Math.Sign(val);
			return rounded.ToString("0.##", CultureInfo.InvariantCulture);
		}

		private static double ComputePineSmoothedAtr(IList<double> tr, int length, DtrAtrSmoothingType smoothing)
		{
			if (tr == null || tr.Count == 0)
				return 0.0;

			int m = tr.Count;
			int n = Math.Max(1, length);

			switch (smoothing)
			{
				case DtrAtrSmoothingType.SMA:
				{
					int k = Math.Min(n, m);
					double sum = 0.0;
					for (int i = m - k; i < m; i++)
						sum += tr[i];
					return sum / k;
				}

				case DtrAtrSmoothingType.WMA:
				{
					int k = Math.Min(n, m);
					double weightedSum = 0.0;
					double norm = 0.0;
					for (int j = 0; j < k; j++)
					{
						double weight = j + 1;
						weightedSum += tr[m - k + j] * weight;
						norm += weight;
					}
					return norm > 0.0 ? (weightedSum / norm) : 0.0;
				}

				case DtrAtrSmoothingType.EMA:
				{
					double alpha = 2.0 / (n + 1.0);
					if (m <= n)
					{
						double ema = tr[0];
						for (int i = 1; i < m; i++)
							ema = alpha * tr[i] + (1.0 - alpha) * ema;
						return ema;
					}
					else
					{
						double seed = 0.0;
						for (int i = 0; i < n; i++)
							seed += tr[i];
						double ema = seed / n;
						for (int i = n; i < m; i++)
							ema = alpha * tr[i] + (1.0 - alpha) * ema;
						return ema;
					}
				}

				case DtrAtrSmoothingType.RMA:
				default:
				{
					if (m <= n)
					{
						double sum = 0.0;
						for (int i = 0; i < m; i++)
							sum += tr[i];
						return sum / m;
					}
					else
					{
						double seed = 0.0;
						for (int i = 0; i < n; i++)
							seed += tr[i];
						double rma = seed / n;
						for (int i = n; i < m; i++)
							rma = (tr[i] + (n - 1.0) * rma) / n;
						return rma;
					}
				}
			}
		}

		private void ComputeDtrVsAtrMetrics(DayProfile latestRenderedProfile, out string headerText, out string valueText, out int dtrPct)
		{
			MarketProfilePeriodMode mode = EffectiveProfileMode;
			bool isWeeklyChart = BarsPeriod != null && BarsPeriod.BarsPeriodType == BarsPeriodType.Week;
			bool isMonthlyChart = BarsPeriod != null && BarsPeriod.BarsPeriodType == BarsPeriodType.Month;

			bool useWeekly = (mode == MarketProfilePeriodMode.Weekly) || (!runtimeProfileModeOverride.HasValue && isWeeklyChart);
			bool useMonthly = !useWeekly && ((mode == MarketProfilePeriodMode.Monthly) || (!runtimeProfileModeOverride.HasValue && isMonthlyChart));

			string tfPrefix = useWeekly ? "W" : (useMonthly ? "M" : "D");
			headerText = tfPrefix + "TR vs ATR";

			SortedDictionary<DateTime, DailyOhlcSample> mergedDaily = new SortedDictionary<DateTime, DailyOhlcSample>();
			foreach (var kv in atrDailyHistory)
			{
				DailyOhlcSample s = kv.Value;
				if (s.High > 0 && s.Low > 0 && s.Close > 0 && s.High >= s.Low)
					mergedDaily[kv.Key.Date] = s;
			}
			foreach (var kv in profiles)
			{
				DayProfile dp = kv.Value;
				if (dp != null && dp.BarCount > 0 && dp.High >= dp.Low && dp.Low < double.MaxValue && dp.Close > 0)
				{
					DateTime dKey = dp.SessionDate.Date;
					mergedDaily[dKey] = new DailyOhlcSample
					{
						Date = dKey,
						Open = dp.Open > 0 ? dp.Open : dp.Close,
						High = dp.High,
						Low = dp.Low,
						Close = dp.Close
					};
				}
			}

			List<DailyOhlcSample> htfBars = new List<DailyOhlcSample>();
			if (useWeekly || useMonthly)
			{
				Dictionary<string, int> keyToIdx = new Dictionary<string, int>(StringComparer.Ordinal);
				foreach (var kv in mergedDaily)
				{
					DailyOhlcSample d = kv.Value;
					DateTime dt = d.Date.Date;
					string gKey;
					DateTime bucketDate;
					if (useMonthly)
					{
						bucketDate = new DateTime(dt.Year, dt.Month, 1);
						gKey = dt.ToString("yyyy-MM", CultureInfo.InvariantCulture);
					}
					else
					{
						int diff = (7 + (dt.DayOfWeek - DayOfWeek.Monday)) % 7;
						bucketDate = dt.AddDays(-diff).Date;
						gKey = bucketDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
					}

					int idx;
					if (!keyToIdx.TryGetValue(gKey, out idx))
					{
						keyToIdx[gKey] = htfBars.Count;
						htfBars.Add(new DailyOhlcSample
						{
							Date = bucketDate,
							Open = d.Open,
							High = d.High,
							Low = d.Low,
							Close = d.Close
						});
					}
					else
					{
						DailyOhlcSample agg = htfBars[idx];
						if (d.High > agg.High) agg.High = d.High;
						if (d.Low < agg.Low) agg.Low = d.Low;
						agg.Close = d.Close;
						htfBars[idx] = agg;
					}
				}
			}
			else
			{
				htfBars.AddRange(mergedDaily.Values);
			}

			if (latestRenderedProfile != null && latestRenderedProfile.High >= latestRenderedProfile.Low && latestRenderedProfile.Low < double.MaxValue && latestRenderedProfile.Close > 0)
			{
				if (htfBars.Count == 0)
				{
					htfBars.Add(new DailyOhlcSample
					{
						Date = latestRenderedProfile.SessionDate.Date,
						Open = latestRenderedProfile.Open > 0 ? latestRenderedProfile.Open : latestRenderedProfile.Close,
						High = latestRenderedProfile.High,
						Low = latestRenderedProfile.Low,
						Close = latestRenderedProfile.Close
					});
				}
				else
				{
					DailyOhlcSample lastBar = htfBars[htfBars.Count - 1];
					if (latestRenderedProfile.SessionDate.Date >= lastBar.Date.Date)
					{
						if (latestRenderedProfile.High > lastBar.High) lastBar.High = latestRenderedProfile.High;
						if (latestRenderedProfile.Low < lastBar.Low) lastBar.Low = latestRenderedProfile.Low;
						lastBar.Close = latestRenderedProfile.Close;
						htfBars[htfBars.Count - 1] = lastBar;
					}
				}
			}

			if (htfBars.Count == 0)
			{
				dtrPct = 0;
				valueText = "0 vs 0 => 0%";
				return;
			}

			double[] tr = new double[htfBars.Count];
			tr[0] = Math.Max(0.0, htfBars[0].High - htfBars[0].Low);
			for (int i = 1; i < htfBars.Count; i++)
			{
				double hl = Math.Max(0.0, htfBars[i].High - htfBars[i].Low);
				double prevClose = htfBars[i - 1].Close;
				tr[i] = prevClose > 0.0
					? Math.Max(hl, Math.Max(Math.Abs(htfBars[i].High - prevClose), Math.Abs(htfBars[i].Low - prevClose)))
					: hl;
			}

			double avgAtr = ComputePineSmoothedAtr(tr, Math.Max(1, DtrAtrLength), DtrAtrSmoothing);
			DailyOhlcSample cur = htfBars[htfBars.Count - 1];
			double curRange = Math.Max(0.0, cur.High - cur.Low);
			dtrPct = avgAtr > 1e-9
				? (int)Math.Round((curRange / avgAtr) * 100.0, MidpointRounding.AwayFromZero)
				: 0;

			valueText = string.Format(
				CultureInfo.InvariantCulture,
				"{0} vs {1} => {2}%",
				FormatPineRound2(curRange),
				FormatPineRound2(avgAtr),
				dtrPct);
		}

		private void DisposeLogoBitmap()
		{
			if (cachedLogoBitmap != null)
			{
				try { cachedLogoBitmap.Dispose(); } catch { }
				cachedLogoBitmap = null;
			}
			cachedLogoRenderTarget = null;
		}

		private SharpDX.Direct2D1.Bitmap GetOrCreateLogoBitmap(RenderTarget rt)
		{
			if (rt == null)
				return null;
			if (cachedLogoBitmap != null && ReferenceEquals(cachedLogoRenderTarget, rt))
				return cachedLogoBitmap;

			DisposeLogoBitmap();
			try
			{
				byte[] pngBytes = Convert.FromBase64String(DefaultLogoBase64);
				using (MemoryStream ms = new MemoryStream(pngBytes))
				{
					BitmapDecoder decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
					if (decoder.Frames == null || decoder.Frames.Count == 0)
						return null;
					BitmapSource frame = decoder.Frames[0];
					FormatConvertedBitmap conv = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
					int w = conv.PixelWidth;
					int h = conv.PixelHeight;
					int stride = w * 4;
					byte[] pixels = new byte[stride * h];
					conv.CopyPixels(pixels, stride, 0);

					double cx = (w - 1) * 0.5;
					double cy = (h - 1) * 0.5;
					double outerR = Math.Min(w, h) * 0.485;
					double innerR = outerR - 1.5;
					for (int py = 0; py < h; py++)
					{
						double dy = py - cy;
						for (int px = 0; px < w; px++)
						{
							double dx = px - cx;
							double dist = Math.Sqrt(dx * dx + dy * dy);
							if (dist > innerR)
							{
								int idx = py * stride + px * 4;
								if (dist >= outerR)
								{
									pixels[idx + 0] = 0;
									pixels[idx + 1] = 0;
									pixels[idx + 2] = 0;
									pixels[idx + 3] = 0;
								}
								else
								{
									double cov = (outerR - dist) / (outerR - innerR);
									pixels[idx + 0] = (byte)Math.Round(pixels[idx + 0] * cov);
									pixels[idx + 1] = (byte)Math.Round(pixels[idx + 1] * cov);
									pixels[idx + 2] = (byte)Math.Round(pixels[idx + 2] * cov);
									pixels[idx + 3] = (byte)Math.Round(pixels[idx + 3] * cov);
								}
							}
						}
					}

					using (SharpDX.DataStream ds = new SharpDX.DataStream(pixels.Length, true, true))
					{
						ds.Write(pixels, 0, pixels.Length);
						ds.Position = 0;
						BitmapProperties bmpProps = new BitmapProperties(
							new SharpDX.Direct2D1.PixelFormat(SharpDX.DXGI.Format.B8G8R8A8_UNorm, SharpDX.Direct2D1.AlphaMode.Premultiplied));
						cachedLogoBitmap = new SharpDX.Direct2D1.Bitmap(
							rt,
							new Size2(w, h),
							ds,
							stride,
							bmpProps);
						cachedLogoRenderTarget = rt;
					}
				}
			}
			catch
			{
				cachedLogoBitmap = null;
				cachedLogoRenderTarget = null;
			}
			return cachedLogoBitmap;
		}

		private static System.Windows.Controls.Primitives.ScrollBar ResolveChartScrollBar(ChartControl cc)
		{
			if (cc == null)
				return null;
			try
			{
				object tab = cc.ChartTab;
				if (tab != null)
				{
					var sbField = tab.GetType().GetField("scrollBar", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
					if (sbField != null)
					{
						var sb = sbField.GetValue(tab) as System.Windows.Controls.Primitives.ScrollBar;
						if (sb != null)
							return sb;
					}
				}
			}
			catch { }
			return null;
		}

		private bool IsChartControlActive(ChartControl cc)
		{
			if (cc == null || ChartPanel == null)
				return false;
			try
			{
				if (!cc.IsVisible || !ChartPanel.IsVisible)
					return false;
				if (cc.OwnerChart != null && cc.OwnerChart.ActiveChartControl != null && !ReferenceEquals(cc.OwnerChart.ActiveChartControl, cc))
					return false;
			}
			catch { }
			return true;
		}

		private void EnsureMouseHookAttached()
		{
			if (ChartControl == null)
				return;
			if (isMouseHooked && hookedChartControl == ChartControl && hookedScrollBar != null)
				return;

			ChartControl cc = ChartControl;
			cc.Dispatcher.InvokeAsync(() =>
			{
				if (cc == null)
					return;

				if (mouseLeftDownHandler == null)
					mouseLeftDownHandler = new System.Windows.Input.MouseButtonEventHandler(OnChartPreviewMouseLeftButtonDown);

				if (!isMouseHooked || hookedChartControl != cc)
				{
					if (hookedChartControl != null)
					{
						hookedChartControl.RemoveHandler(System.Windows.UIElement.PreviewMouseLeftButtonDownEvent, mouseLeftDownHandler);
						hookedChartControl.PreviewMouseMove -= OnChartPreviewMouseMove;
						hookedChartControl.PreviewMouseLeftButtonUp -= OnChartPreviewMouseLeftButtonUp;
						hookedChartControl.PreviewMouseWheel -= OnChartPreviewMouseWheel;
					}
					cc.RemoveHandler(System.Windows.UIElement.PreviewMouseLeftButtonDownEvent, mouseLeftDownHandler);
					cc.AddHandler(System.Windows.UIElement.PreviewMouseLeftButtonDownEvent, mouseLeftDownHandler, true);
					cc.PreviewMouseMove += OnChartPreviewMouseMove;
					cc.PreviewMouseLeftButtonUp += OnChartPreviewMouseLeftButtonUp;
					cc.PreviewMouseWheel += OnChartPreviewMouseWheel;
					hookedChartControl = cc;
					isMouseHooked = true;
				}

				if (hookedScrollBar == null)
				{
					var sb = ResolveChartScrollBar(cc);
					if (sb != null)
					{
						sb.ValueChanged -= OnChartScrollBarValueChanged;
						sb.Scroll -= OnChartScrollBarScroll;
						sb.ValueChanged += OnChartScrollBarValueChanged;
						sb.Scroll += OnChartScrollBarScroll;
						hookedScrollBar = sb;
					}
				}

				if (tpoCountdownTimer == null)
				{
					tpoCountdownTimer = new System.Windows.Threading.DispatcherTimer(
						TimeSpan.FromMilliseconds(500),
						System.Windows.Threading.DispatcherPriority.Background,
						(s, ev) =>
						{
							if (State == State.Terminated)
								return;
							int secNow = DateTime.UtcNow.Second;
							if (secNow == lastTpoCountdownSec)
								return;
							lastTpoCountdownSec = secNow;
							ChartControl activeCc = hookedChartControl ?? ChartControl;
							if (activeCc != null && IsChartControlActive(activeCc))
							{
								try { ForceRefresh(); activeCc.InvalidateVisual(); } catch { }
							}
						},
						cc.Dispatcher);
				}
				if (!tpoCountdownTimer.IsEnabled)
					tpoCountdownTimer.Start();
			});
		}

		private void DetachMouseHook()
		{
			ChartControl cc = hookedChartControl;
			var handler = mouseLeftDownHandler;
			var sb = hookedScrollBar;
			var cdTimer = tpoCountdownTimer;
			tpoCountdownTimer = null;
			if (cc != null)
			{
				cc.Dispatcher.InvokeAsync(() =>
				{
					if (cdTimer != null)
					{
						try { cdTimer.Stop(); } catch { }
					}
					if (handler != null)
					{
						try { cc.RemoveHandler(System.Windows.UIElement.PreviewMouseLeftButtonDownEvent, handler); } catch { }
					}
					cc.PreviewMouseMove -= OnChartPreviewMouseMove;
					cc.PreviewMouseLeftButtonUp -= OnChartPreviewMouseLeftButtonUp;
					cc.PreviewMouseWheel -= OnChartPreviewMouseWheel;
					if (sb != null)
					{
						sb.ValueChanged -= OnChartScrollBarValueChanged;
						sb.Scroll -= OnChartScrollBarScroll;
					}
				});
			}
			hookedScrollBar = null;
			hookedChartControl = null;
			isMouseHooked = false;
		}

		private void OnChartScrollBarScroll(object sender, System.Windows.Controls.Primitives.ScrollEventArgs e)
		{
			lock (profileLock)
			{
				hasManualPixelScroll = false;
				lastTrackedScrollBarVal = e.NewValue;
			}
			ForceRefresh();
			ChartControl cc = hookedChartControl ?? ChartControl;
			if (cc != null)
				cc.InvalidateVisual();
		}

		private void OnChartScrollBarValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
		{
			lock (profileLock)
			{
				hasManualPixelScroll = false;
				lastTrackedScrollBarVal = e.NewValue;
			}
			ForceRefresh();
			ChartControl cc = hookedChartControl ?? ChartControl;
			if (cc != null)
				cc.InvalidateVisual();
		}

		private float ConvertHorizontalPx(ChartControl cc, double dipX)
		{
			try
			{
				if (cc != null && cc.PresentationSource != null)
					return (float)ChartingExtensions.ConvertToHorizontalPixels(dipX, cc.PresentationSource);
			}
			catch { }
			return (float)dipX;
		}

		private float ConvertVerticalPx(ChartControl cc, double dipY)
		{
			try
			{
				if (cc != null && cc.PresentationSource != null)
					return (float)ChartingExtensions.ConvertToVerticalPixels(dipY, cc.PresentationSource);
			}
			catch { }
			return (float)dipY;
		}

		private static bool IsInteractiveUiControlSource(object originalSource)
		{
			try
			{
				System.Windows.DependencyObject cur = originalSource as System.Windows.DependencyObject;
				int guard = 0;
				while (cur != null && guard++ < 20)
				{
					if (cur is System.Windows.Controls.Primitives.Popup ||
						cur is System.Windows.Controls.Primitives.MenuBase ||
						cur is System.Windows.Controls.MenuItem ||
						cur is System.Windows.Controls.ComboBox ||
						cur is System.Windows.Controls.ComboBoxItem ||
						cur is System.Windows.Controls.Primitives.TextBoxBase ||
						cur is System.Windows.Controls.Primitives.ButtonBase ||
						cur is System.Windows.Controls.Primitives.ScrollBar ||
						cur is System.Windows.Controls.Primitives.Thumb)
					{
						return true;
					}
					if (cur is ChartControl || cur is ChartPanel)
						break;

					System.Windows.DependencyObject next = null;
					if (cur is System.Windows.Media.Visual || cur is System.Windows.Media.Media3D.Visual3D)
					{
						try { next = System.Windows.Media.VisualTreeHelper.GetParent(cur); } catch { }
					}
					if (next == null)
					{
						try { next = System.Windows.LogicalTreeHelper.GetParent(cur); } catch { }
					}
					cur = next;
				}
			}
			catch { }
			return false;
		}

		// Multi-monitor & mixed-DPI exact cursor mapping directly onto NinjaTrader's Direct2DForm RenderTarget surface
		private bool TryGetRenderSurfacePoint(ChartControl cc, System.Windows.Input.MouseEventArgs e, out float surfX, out float surfY)
		{
			surfX = -99999f;
			surfY = -99999f;
			if (cc == null)
				return false;

			try
			{
				if (cc.OwnerChart != null && cc.OwnerChart.Form != null && cc.OwnerChart.Form.IsHandleCreated)
				{
					var form = cc.OwnerChart.Form;
					Win32Point pt;
					if (GetCursorPos(out pt) && ScreenToClient(form.Handle, ref pt))
					{
						int cw = form.ClientSize.Width;
						int ch = form.ClientSize.Height;
						float rtW = lastRenderTargetW;
						float rtH = lastRenderTargetH;
						float sx = (cw > 10 && rtW > 10f) ? (rtW / cw) : 1f;
						float sy = (ch > 10 && rtH > 10f) ? (rtH / ch) : 1f;
						surfX = pt.X * sx;
						surfY = pt.Y * sy;
						return true;
					}
				}
			}
			catch { }

			if (e != null)
			{
				try
				{
					if (ChartPanel != null)
					{
						System.Windows.Point panelPt = e.GetPosition(ChartPanel);
						surfX = (float)ChartPanel.X + ConvertHorizontalPx(cc, panelPt.X);
						surfY = (float)ChartPanel.Y + ConvertVerticalPx(cc, panelPt.Y);
						return true;
					}
					System.Windows.Point wpfPt = e.GetPosition(cc);
					surfX = ConvertHorizontalPx(cc, wpfPt.X);
					surfY = ConvertVerticalPx(cc, wpfPt.Y);
					return true;
				}
				catch { }
			}

			return false;
		}

		private bool IsHoveringAnyChartButton(ChartControl cc, System.Windows.Input.MouseEventArgs e)
		{
			if (cc == null || e == null)
				return false;
			try
			{
				float surfX, surfY;
				bool hasSurf = TryGetRenderSurfacePoint(cc, e, out surfX, out surfY);

				System.Windows.Point wpfPt = e.GetPosition(cc);
				float dipX = (float)wpfPt.X;
				float dipY = (float)wpfPt.Y;
				float pxX = ConvertHorizontalPx(cc, wpfPt.X);
				float pxY = ConvertVerticalPx(cc, wpfPt.Y);

				float panelPxX = -99999f, panelPxY = -99999f, panelDipX = -99999f, panelDipY = -99999f;
				if (ChartPanel != null)
				{
					System.Windows.Point panelPt = e.GetPosition(ChartPanel);
					panelDipX = (float)ChartPanel.X + (float)panelPt.X;
					panelDipY = (float)ChartPanel.Y + (float)panelPt.Y;
					panelPxX = (float)ChartPanel.X + ConvertHorizontalPx(cc, panelPt.X);
					panelPxY = (float)ChartPanel.Y + ConvertVerticalPx(cc, panelPt.Y);
				}

				lock (profileLock)
				{
					for (int i = activeChartButtons.Count - 1; i >= 0; i--)
					{
						RectangleF hitRect = activeChartButtons[i].Rect;
						hitRect.Inflate(3.5f, 2.5f);
						if ((hasSurf && hitRect.Contains(surfX, surfY)) ||
							hitRect.Contains(panelPxX, panelPxY) ||
							hitRect.Contains(panelDipX, panelDipY) ||
							hitRect.Contains(pxX, pxY) ||
							hitRect.Contains(dipX, dipY))
						{
							return true;
						}
					}
				}
			}
			catch { }
			return false;
		}

		private void EnsureAutoYAxisForCustomView()
		{
			try
			{
				ChartScale cs = lastRenderedChartScale;
				if (cs != null && cs.Properties != null && cs.Properties.YAxisRangeType != YAxisRangeType.Automatic)
				{
					double curSpan = cs.MaxValue - cs.MinValue;
					if (curSpan > 1e-9 && lastBasePriceSpan > 1e-9)
					{
						verticalZoomFactor = Math.Max(0.15, Math.Min(4.0, curSpan / lastBasePriceSpan));
						verticalPanPriceOffset = 0.5 * (cs.MaxValue + cs.MinValue) - lastBaseMidPrice;
					}
					cs.Properties.YAxisRangeType = YAxisRangeType.Automatic;
				}
			}
			catch { }
		}

		private void ApplyVerticalZoomStep(double ratio, float anchorSurfY = float.NaN)
		{
			EnsureAutoYAxisForCustomView();
			double oldZoom = verticalZoomFactor;
			double newZoom = Math.Max(0.15, Math.Min(4.0, oldZoom * ratio));
			if (Math.Abs(newZoom - oldZoom) < 1e-6)
				return;

			double baseSpan = Math.Max(1e-6, lastBasePriceSpan);
			double oldSpan = baseSpan * oldZoom;
			double newSpan = baseSpan * newZoom;
			double oldMid = lastBaseMidPrice + verticalPanPriceOffset;

			if (!float.IsNaN(anchorSurfY) && lastRenderedChartScale != null && ChartPanel != null)
			{
				try
				{
					double anchorPrice = lastRenderedChartScale.GetValueByY(anchorSurfY);
					double oldHigh = oldMid + 0.5 * oldSpan;
					double u = Math.Max(0.05, Math.Min(0.95, (oldHigh - anchorPrice) / Math.Max(1e-6, oldSpan)));
					double newHigh = anchorPrice + u * newSpan;
					double newMid = newHigh - 0.5 * newSpan;
					verticalPanPriceOffset = newMid - lastBaseMidPrice;
				}
				catch { }
			}

			verticalZoomFactor = newZoom;
		}

		private void ResetVerticalView()
		{
			verticalZoomFactor = 1.0;
			verticalPanPriceOffset = 0.0;
			try
			{
				if (lastRenderedChartScale != null && lastRenderedChartScale.Properties != null)
					lastRenderedChartScale.Properties.YAxisRangeType = YAxisRangeType.Automatic;
			}
			catch { }
		}

		private void OnChartPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
		{
			ChartControl cc = hookedChartControl ?? ChartControl;
			if (!IsChartControlActive(cc) || e == null || IsInteractiveUiControlSource(e.OriginalSource))
				return;

			float surfX = -99999f, surfY = -99999f;
			bool hasSurf = TryGetRenderSurfacePoint(cc, e, out surfX, out surfY);
			if (ChartPanel != null && hasSurf)
			{
				float panelLeft = (float)ChartPanel.X;
				float panelRight = (float)(ChartPanel.X + ChartPanel.W);
				float panelTop = (float)ChartPanel.Y;
				float panelBot = (float)(ChartPanel.Y + ChartPanel.H);
				// Let NinjaTrader handle wheel over right price scale or outside chart panel
				if (surfX < panelLeft || surfX > panelRight || surfY < panelTop || surfY > panelBot)
					return;
			}

			var mods = System.Windows.Input.Keyboard.Modifiers;
			if ((mods & System.Windows.Input.ModifierKeys.Control) != 0)
			{
				lock (profileLock)
				{
					ApplyVerticalZoomStep(e.Delta > 0 ? 0.84 : 1.19, hasSurf ? surfY : float.NaN);
				}
				e.Handled = true;
				ForceRefresh();
				cc.InvalidateVisual();
				try { if (cc.OwnerChart != null && cc.OwnerChart.Form != null) cc.OwnerChart.Form.Invalidate(); } catch { }
				return;
			}

			if ((mods & System.Windows.Input.ModifierKeys.Shift) != 0)
			{
				lock (profileLock)
				{
					EnsureAutoYAxisForCustomView();
					double activeSpan = Math.Max(1e-6, lastBasePriceSpan * verticalZoomFactor);
					double panStep = activeSpan * 0.085;
					verticalPanPriceOffset += (e.Delta > 0 ? panStep : -panStep);
				}
				e.Handled = true;
				ForceRefresh();
				cc.InvalidateVisual();
				try { if (cc.OwnerChart != null && cc.OwnerChart.Form != null) cc.OwnerChart.Form.Invalidate(); } catch { }
				return;
			}

			float deltaStep = Math.Max(8f, Math.Abs(e.Delta) * 1.05f);
			bool needMorePast = false;
			int nextDaysToFetch = 0;
			lock (profileLock)
			{
				hasManualPixelScroll = true;
				if (float.IsNaN(currentScrollPx))
					currentScrollPx = maxScrollCached;
				if (e.Delta > 0)
				{
					if (currentScrollPx <= 8f)
					{
						needMorePast = true;
						nextDaysToFetch = Math.Min(365, Math.Max(75, currentLoadedHistoryDays + 45));
					}
					currentScrollPx = Math.Max(0f, currentScrollPx - deltaStep);
				}
				else if (e.Delta < 0)
					currentScrollPx = Math.Min(Math.Max(0f, maxScrollCached), currentScrollPx + deltaStep);
			}
			if (needMorePast && nextDaysToFetch > currentLoadedHistoryDays)
				RequestHistoricalMinuteBarsAsync(nextDaysToFetch);

			e.Handled = true;
			ForceRefresh();
			cc.InvalidateVisual();
			try { if (cc.OwnerChart != null && cc.OwnerChart.Form != null) cc.OwnerChart.Form.Invalidate(); } catch { }
		}

		private void OnChartPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			ChartControl cc = hookedChartControl ?? ChartControl;
			if (!IsChartControlActive(cc) || e == null || IsInteractiveUiControlSource(e.OriginalSource))
				return;

			if (TryHandleChartButtonClick(cc, e))
			{
				e.Handled = true;
				return;
			}

			// Prepare 2D drag-panning (horizontal + vertical) ONLY inside the chart panel canvas (excluding price scale, time axis, and toolbars).
			// Do NOT call cc.CaptureMouse() on MouseDown so native context menus, drawing tools, and toolbar popups never lose input!
			try
			{
				if (ChartPanel != null)
				{
					float surfX, surfY;
					if (TryGetRenderSurfacePoint(cc, e, out surfX, out surfY))
					{
						float panelLeft = (float)ChartPanel.X;
						float panelRight = (float)(ChartPanel.X + ChartPanel.W);
						float panelTop = (float)ChartPanel.Y;
						float panelBot = (float)(ChartPanel.Y + ChartPanel.H);
						if (surfX >= panelLeft && surfX <= panelRight - 48f && surfY >= panelTop + 4f && surfY <= panelBot - 6f)
						{
							try
							{
								if (!(System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase))
								{
									cc.Focusable = true;
									System.Windows.Input.Keyboard.Focus(cc);
								}
							}
							catch { }

							lock (profileLock)
							{
								EnsureAutoYAxisForCustomView();
								isPotentialCanvasDrag = true;
								isDraggingCanvas = false;
								dragStartMouseX = surfX;
								dragStartMouseY = surfY;
								dragStartScrollPx = float.IsNaN(currentScrollPx) ? maxScrollCached : currentScrollPx;
								dragStartPanOffset = verticalPanPriceOffset;
							}
						}
					}
				}
			}
			catch { }
		}

		private void OnChartPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
		{
			ChartControl cc = hookedChartControl ?? ChartControl;
			if (!IsChartControlActive(cc) || e == null)
				return;

			if (!isPotentialCanvasDrag && !isDraggingCanvas)
			{
				try
				{
					bool overBtn = !IsInteractiveUiControlSource(e.OriginalSource) && IsHoveringAnyChartButton(cc, e);
					if (overBtn && cc.Cursor != System.Windows.Input.Cursors.Hand)
						cc.Cursor = System.Windows.Input.Cursors.Hand;
					else if (!overBtn && (cc.Cursor == System.Windows.Input.Cursors.Hand || cc.Cursor == System.Windows.Input.Cursors.SizeWE || cc.Cursor == System.Windows.Input.Cursors.SizeAll))
						cc.Cursor = null;
				}
				catch { }
				return;
			}

			if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
			{
				isPotentialCanvasDrag = false;
				isDraggingCanvas = false;
				try
				{
					if (cc.IsMouseCaptured) cc.ReleaseMouseCapture();
					if (cc.Cursor == System.Windows.Input.Cursors.SizeWE || cc.Cursor == System.Windows.Input.Cursors.SizeAll) cc.Cursor = null;
				}
				catch { }
				return;
			}

			try
			{
				float curPxX, curPxY;
				if (!TryGetRenderSurfacePoint(cc, e, out curPxX, out curPxY))
				{
					System.Windows.Point p = e.GetPosition(cc);
					curPxX = ConvertHorizontalPx(cc, p.X);
					curPxY = ConvertVerticalPx(cc, p.Y);
				}

				float deltaX = curPxX - dragStartMouseX;
				float deltaY = curPxY - dragStartMouseY;
				if (!isDraggingCanvas)
				{
					if (Math.Abs(deltaX) < 3.5f && Math.Abs(deltaY) < 4.5f)
						return;
					isDraggingCanvas = true;
					try { cc.CaptureMouse(); } catch { }
				}

				bool moved = false;
				lock (profileLock)
				{
					if (Math.Abs(deltaX) >= 1.0f)
					{
						hasManualPixelScroll = true;
						float targetScroll = Math.Max(0f, Math.Min(Math.Max(0f, maxScrollCached), dragStartScrollPx - deltaX));
						if (float.IsNaN(currentScrollPx) || Math.Abs(targetScroll - currentScrollPx) >= 0.35f)
						{
							currentScrollPx = targetScroll;
							moved = true;
						}
					}

					bool allowVerticalPan = Math.Abs(verticalZoomFactor - 1.0) > 0.02
						|| Math.Abs(dragStartPanOffset) > 1e-9
						|| (Math.Abs(deltaY) >= 8.0f && Math.Abs(deltaY) > Math.Abs(deltaX) * 0.85f);
					if (allowVerticalPan && Math.Abs(deltaY) >= 1.5f)
					{
						EnsureAutoYAxisForCustomView();
						double activeSpan = Math.Max(1e-6, lastBasePriceSpan * verticalZoomFactor);
						double ptsPerPx = activeSpan / Math.Max(120.0, lastUsableHeightPx);
						double targetPan = dragStartPanOffset + deltaY * ptsPerPx;
						if (Math.Abs(targetPan - verticalPanPriceOffset) > 1e-7)
						{
							verticalPanPriceOffset = targetPan;
							moved = true;
						}
					}

					var desiredCursor = allowVerticalPan ? System.Windows.Input.Cursors.SizeAll : System.Windows.Input.Cursors.SizeWE;
					if (cc.Cursor != desiredCursor)
						cc.Cursor = desiredCursor;
				}
				if (moved)
				{
					ForceRefresh();
					cc.InvalidateVisual();
					try { if (cc.OwnerChart != null && cc.OwnerChart.Form != null) cc.OwnerChart.Form.Invalidate(); } catch { }
				}
			}
			catch { }
		}

		private void OnChartPreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			isPotentialCanvasDrag = false;
			isDraggingCanvas = false;
			try
			{
				ChartControl cc = hookedChartControl ?? ChartControl;
				if (cc != null)
				{
					if (cc.IsMouseCaptured) cc.ReleaseMouseCapture();
					if (cc.Cursor == System.Windows.Input.Cursors.SizeWE || cc.Cursor == System.Windows.Input.Cursors.SizeAll) cc.Cursor = null;
				}
			}
			catch { }
		}

		private bool TryHandleChartButtonClick(ChartControl cc, System.Windows.Input.MouseButtonEventArgs e)
		{
			if (!IsChartControlActive(cc))
				return false;

			if (e != null && e.Timestamp == lastHandledMouseTimestamp)
				return false;

			DateTime nowUtc = DateTime.UtcNow;
			if ((nowUtc - lastButtonClickUtc).TotalMilliseconds < 80.0)
				return false;

			float surfX = -99999f, surfY = -99999f;
			bool hasSurf = TryGetRenderSurfacePoint(cc, e, out surfX, out surfY);

			float pxX = -99999f, pxY = -99999f, dipX = -99999f, dipY = -99999f;
			float panelPxX = -99999f, panelPxY = -99999f, panelDipX = -99999f, panelDipY = -99999f;
			if (e != null)
			{
				try
				{
					System.Windows.Point wpfPt = e.GetPosition(cc);
					dipX = (float)wpfPt.X;
					dipY = (float)wpfPt.Y;
					pxX = ConvertHorizontalPx(cc, wpfPt.X);
					pxY = ConvertVerticalPx(cc, wpfPt.Y);
				}
				catch { }
				try
				{
					if (ChartPanel != null)
					{
						System.Windows.Point panelPt = e.GetPosition(ChartPanel);
						panelDipX = (float)ChartPanel.X + (float)panelPt.X;
						panelDipY = (float)ChartPanel.Y + (float)panelPt.Y;
						panelPxX = (float)ChartPanel.X + ConvertHorizontalPx(cc, panelPt.X);
						panelPxY = (float)ChartPanel.Y + ConvertVerticalPx(cc, panelPt.Y);
					}
				}
				catch { }
			}

			bool handled = false;
			lock (profileLock)
			{
				for (int i = activeChartButtons.Count - 1; i >= 0; i--)
				{
					ChartButton btn = activeChartButtons[i];
					RectangleF hitRect = btn.Rect;
					hitRect.Inflate(3.5f, 2.0f);

					if ((hasSurf && hitRect.Contains(surfX, surfY)) ||
						hitRect.Contains(panelPxX, panelPxY) ||
						hitRect.Contains(panelDipX, panelDipY) ||
						hitRect.Contains(pxX, pxY) ||
						hitRect.Contains(dipX, dipY))
					{
						if (e != null)
							lastHandledMouseTimestamp = e.Timestamp;
						lastButtonClickUtc = nowUtc;
						ExecuteChartButtonAction(btn);
						handled = true;
						break;
					}
				}
				if (!handled && isProfileDropdownOpen)
				{
					if (e != null)
						lastHandledMouseTimestamp = e.Timestamp;
					lastButtonClickUtc = nowUtc;
					isProfileDropdownOpen = false;
					handled = true;
				}
			}

			if (handled)
			{
				ForceRefresh();
				cc.InvalidateVisual();
				try
				{
					if (cc.OwnerChart != null && cc.OwnerChart.Form != null)
						cc.OwnerChart.Form.Invalidate();
				}
				catch { }
			}
			return handled;
		}

		private void ExecuteChartButtonAction(ChartButton btn)
		{
			switch (btn.Action)
			{
				case ChartButtonAction.ToggleSplit:
					if (!string.IsNullOrEmpty(btn.PrimaryKey))
					{
						bool currentlyExpanded = btn.PrimaryKey.EndsWith("|ON", StringComparison.Ordinal);
						string realKey = currentlyExpanded ? btn.PrimaryKey.Substring(0, btn.PrimaryKey.Length - 3) : btn.PrimaryKey;
						if (currentlyExpanded)
						{
							manuallySplitProfileKeys.Remove(realKey);
							manuallyUnsplitProfileKeys.Add(realKey);
						}
						else
						{
							manuallyUnsplitProfileKeys.Remove(realKey);
							manuallySplitProfileKeys.Add(realKey);
						}
					}
					break;

				case ChartButtonAction.ToggleTopLeftMinMax:
					runtimeMinimizeTopLeftOverride = !IsTopLeftBoxMinimized;
					break;

				case ChartButtonAction.ToggleModernRichUi:
					bool nextMod = !IsModernRichUi;
					runtimeModernUiOverride = nextMod;
					EnableModernRichUi = nextMod;
					isProfileDropdownOpen = false;
					break;

				case ChartButtonAction.ToggleSummaryMinMax:
					runtimeMinimizeSummaryOverride = !IsSummaryBoxMinimized;
					break;

				case ChartButtonAction.ScrollPastDays:
					hasManualPixelScroll = true;
					if (float.IsNaN(currentScrollPx)) currentScrollPx = maxScrollCached;
					if (currentScrollPx <= 8f)
						RequestHistoricalMinuteBarsAsync(Math.Min(365, Math.Max(75, currentLoadedHistoryDays + 45)));
					currentScrollPx = Math.Max(0f, currentScrollPx - 280f);
					break;

				case ChartButtonAction.ScrollNextDays:
					hasManualPixelScroll = true;
					if (float.IsNaN(currentScrollPx)) currentScrollPx = maxScrollCached;
					currentScrollPx = Math.Min(Math.Max(0f, maxScrollCached), currentScrollPx + 280f);
					break;

				case ChartButtonAction.ScrollLiveDay:
					hasManualPixelScroll = false;
					currentScrollPx = float.MaxValue;
					ResetVerticalView();
					try
					{
						var sb = hookedScrollBar ?? ResolveChartScrollBar(ChartControl);
						if (sb != null && sb.Maximum > sb.Minimum)
							sb.Value = sb.Maximum;
					}
					catch { }
					break;

				case ChartButtonAction.ZoomInVertical:
					ApplyVerticalZoomStep(0.80);
					break;

				case ChartButtonAction.ZoomOutVertical:
					ApplyVerticalZoomStep(1.25);
					break;

				case ChartButtonAction.ToggleProfileDropdown:
					isProfileDropdownOpen = !isProfileDropdownOpen;
					break;

				case ChartButtonAction.SelectProfileDaily:
					runtimeProfileModeOverride = MarketProfilePeriodMode.Daily;
					ProfileMode = MarketProfilePeriodMode.Daily;
					isProfileDropdownOpen = false;
					hasManualPixelScroll = false;
					currentScrollPx = float.MaxValue;
					ResetVerticalView();
					break;

				case ChartButtonAction.SelectProfileWeekly:
					runtimeProfileModeOverride = MarketProfilePeriodMode.Weekly;
					ProfileMode = MarketProfilePeriodMode.Weekly;
					isProfileDropdownOpen = false;
					hasManualPixelScroll = false;
					currentScrollPx = float.MaxValue;
					ResetVerticalView();
					RequestHistoricalMinuteBarsAsync(65);
					break;

				case ChartButtonAction.SelectProfileMonthly:
					runtimeProfileModeOverride = MarketProfilePeriodMode.Monthly;
					ProfileMode = MarketProfilePeriodMode.Monthly;
					isProfileDropdownOpen = false;
					hasManualPixelScroll = false;
					currentScrollPx = float.MaxValue;
					ResetVerticalView();
					RequestHistoricalMinuteBarsAsync(180);
					break;
			}
		}

		public override string DisplayName
		{
			get { return string.Empty; }
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBars == null || CurrentBars.Length == 0)
				return;

			// Support single-series 1-Minute chart (when no secondary series exists)
			if (CurrentBars.Length == 1)
			{
				int s1Idx = CurrentBars[0];
				if (s1Idx < 0)
					return;

				for (int back = 0; s1Idx - back > lastHiddenChartBar && back <= s1Idx; back++)
				{
					BarBrushes[back] = Brushes.Transparent;
					CandleOutlineBrushes[back] = Brushes.Transparent;
				}
				lastHiddenChartBar = s1Idx;

				DateTime s1Time = Times[0][0];
				double s1TotalBarVol = Math.Max(0, Convert.ToDouble(Volumes[0][0]));

				lock (profileLock)
				{
					DateTime chartDate = s1Time.Date;
					if (!chartDayFirstBar.ContainsKey(chartDate))
						chartDayFirstBar[chartDate] = s1Idx;
					chartDayLastBar[chartDate] = s1Idx;

					int count = rawMinuteSamples.Count;
					if (count > 0 && s1Time < rawMinuteSamples[count - 1].Time)
						return;

					bool isSameMinuteBar = count > 0 && rawMinuteSamples[count - 1].Time == s1Time;
					MinuteSample s1Sample;
					double volDelta;
					if (isSameMinuteBar)
					{
						MinuteSample prev = rawMinuteSamples[count - 1];
						double mergedVol = Math.Max(prev.Volume, s1TotalBarVol);
						volDelta = Math.Max(0.0, mergedVol - prev.Volume);
						s1Sample = new MinuteSample
						{
							Time = s1Time,
							Open = prev.Open,
							High = Math.Max(prev.High, Highs[0][0]),
							Low = Math.Min(prev.Low, Lows[0][0]),
							Close = Closes[0][0],
							Volume = mergedVol,
							BarIndex = prev.BarIndex
						};
						rawMinuteSamples[count - 1] = s1Sample;
					}
					else
					{
						volDelta = s1TotalBarVol;
						s1Sample = new MinuteSample
						{
							Time = s1Time,
							Open = Opens[0][0],
							High = Highs[0][0],
							Low = Lows[0][0],
							Close = Closes[0][0],
							Volume = s1TotalBarVol,
							BarIndex = count > 0 ? (rawMinuteSamples[count - 1].BarIndex + 1) : s1Idx
						};
						rawMinuteSamples.Add(s1Sample);
						int maxSamples = 370 * 1440;
						if (rawMinuteSamples.Count > maxSamples)
							rawMinuteSamples.RemoveRange(0, rawMinuteSamples.Count - maxSamples);
					}

					double desiredStep = ComputeAutoOrManualPriceStep();
					if (cachedPriceStep <= 0
						|| Math.Abs(desiredStep - cachedPriceStep) / cachedPriceStep > 0.18
						|| (rawMinuteSamples.Count - lastRebuiltSampleCount > 480 && !isSameMinuteBar))
					{
						RebuildAllDayProfiles(desiredStep);
					}
					else if (isSameMinuteBar)
					{
						MinuteSample deltaSample = s1Sample;
						deltaSample.Volume = volDelta;
						AccumulateSampleIntoDay(deltaSample, cachedPriceStep, isLiveTickUpdate: true);
					}
					else
					{
						AccumulateSampleIntoDay(s1Sample, cachedPriceStep, isLiveTickUpdate: false);
					}
				}
				return;
			}

			if (BarsInProgress == 0)
			{
				int curPrimary = CurrentBars[0];
				if (curPrimary < 0)
					return;

				for (int back = 0; curPrimary - back > lastHiddenChartBar && back <= curPrimary; back++)
				{
					BarBrushes[back] = Brushes.Transparent;
					CandleOutlineBrushes[back] = Brushes.Transparent;
				}
				lastHiddenChartBar = curPrimary;

				lock (profileLock)
				{
					DateTime chartDate = Times[0][0].Date;
					if (!chartDayFirstBar.ContainsKey(chartDate))
						chartDayFirstBar[chartDate] = curPrimary;
					chartDayLastBar[chartDate] = curPrimary;
				}
				return;
			}

			if (BarsInProgress != 1 || CurrentBars[1] < 0)
				return;

			int minuteBarIndex = CurrentBars[1];
			DateTime mTime = Times[1][0];
			double mTotalBarVol = Math.Max(0, Convert.ToDouble(Volumes[1][0]));

			lock (profileLock)
			{
				int count = rawMinuteSamples.Count;
				if (count > 0 && mTime < rawMinuteSamples[count - 1].Time)
					return;

				bool isSameMinuteBar = count > 0 && rawMinuteSamples[count - 1].Time == mTime;
				MinuteSample sample;
				double volDelta;
				if (isSameMinuteBar)
				{
					MinuteSample prev = rawMinuteSamples[count - 1];
					double mergedVol = Math.Max(prev.Volume, mTotalBarVol);
					volDelta = Math.Max(0.0, mergedVol - prev.Volume);
					sample = new MinuteSample
					{
						Time = mTime,
						Open = prev.Open,
						High = Math.Max(prev.High, Highs[1][0]),
						Low = Math.Min(prev.Low, Lows[1][0]),
						Close = Closes[1][0],
						Volume = mergedVol,
						BarIndex = prev.BarIndex
					};
					rawMinuteSamples[count - 1] = sample;
				}
				else
				{
					volDelta = mTotalBarVol;
					sample = new MinuteSample
					{
						Time = mTime,
						Open = Opens[1][0],
						High = Highs[1][0],
						Low = Lows[1][0],
						Close = Closes[1][0],
						Volume = mTotalBarVol,
						BarIndex = count > 0 ? (rawMinuteSamples[count - 1].BarIndex + 1) : minuteBarIndex
					};
					rawMinuteSamples.Add(sample);
					int maxSamples = 370 * 1440;
					if (rawMinuteSamples.Count > maxSamples)
						rawMinuteSamples.RemoveRange(0, rawMinuteSamples.Count - maxSamples);
				}

				double desiredStep = ComputeAutoOrManualPriceStep();
				if (cachedPriceStep <= 0
					|| Math.Abs(desiredStep - cachedPriceStep) / cachedPriceStep > 0.18
					|| (rawMinuteSamples.Count - lastRebuiltSampleCount > 480 && !isSameMinuteBar))
				{
					RebuildAllDayProfiles(desiredStep);
				}
				else if (isSameMinuteBar)
				{
					MinuteSample deltaSample = sample;
					deltaSample.Volume = volDelta;
					AccumulateSampleIntoDay(deltaSample, cachedPriceStep, isLiveTickUpdate: true);
				}
				else
				{
					AccumulateSampleIntoDay(sample, cachedPriceStep, isLiveTickUpdate: false);
				}
			}
		}

		private const float MinReadableTpoRowPx = 11.8f;
		private const float MaxComfortableTpoRowPx = 16.5f;

		private void ComputeTopLayoutOffsets(out float topSessionBarOffset, out float canvasTopOffset, out float topPadPx)
		{
			float topLeftH = IsTopLeftBoxMinimized
				? 27f
				: (ShowDtrVsAtr ? 90f : 72f);
			float hudBottom = ShowTopLeftBox ? (6f + topLeftH) : 32f;
			topSessionBarOffset = hudBottom + 3f;
			if (FixSessionHeaderAtTop)
			{
				canvasTopOffset = topSessionBarOffset + 36f;
				topPadPx = canvasTopOffset + 20f;
			}
			else
			{
				canvasTopOffset = hudBottom + 2f;
				topPadPx = hudBottom + 42f;
			}
		}

		public override void OnCalculateMinMax()
		{
			double minVal = double.MaxValue;
			double maxVal = double.MinValue;
			double maxSingleDaySpan = 0.0;
			double baseStep = 1.0;
			float panelLeft = ChartPanel != null ? (float)ChartPanel.X : 0f;
			float panelRight = ChartPanel != null ? (float)(ChartPanel.X + ChartPanel.W) : 1800f;
			float panelH = ChartPanel != null ? (float)ChartPanel.H : 820f;
			float unusedTopBarOff, unusedCanvasTopOff, topPadPx;
			ComputeTopLayoutOffsets(out unusedTopBarOff, out unusedCanvasTopOff, out topPadPx);
			float botPadPx = (ShowSummary && FixSummaryBoxAtBottom)
				? (IsSummaryBoxMinimized ? 78f : 164f)
				: 46f;

			lock (profileLock)
			{
				if (profiles.Count > 0)
				{
					List<DayProfile> allDays = profiles.Values.Where(p => p.Rows.Count > 0).ToList();
					if (allDays.Count > 0)
					{
						allDays.Sort((a, b) => a.SessionDate.CompareTo(b.SessionDate));
						MarketProfilePeriodMode mode = EffectiveProfileMode;
						if (mode == MarketProfilePeriodMode.Weekly)
							allDays = BuildWeeklyOrMonthlyDays(allDays, isMonthly: false);
						else if (mode == MarketProfilePeriodMode.Monthly)
							allDays = BuildWeeklyOrMonthlyDays(allDays, isMonthly: true);

						if (allDays.Count > 0)
						{
							baseStep = GetPriceStep();
							List<DayProfile> laidOutDays = BuildDayLayout(allDays, baseStep, panelLeft, panelRight);

							for (int i = 0; i < laidOutDays.Count; i++)
							{
								DayProfile d = laidOutDays[i];
								if (d.SlotRightX < panelLeft + 15f || d.SlotLeftX > panelRight - 182f)
									continue;
								if (d.Low < minVal) minVal = d.Low;
								if (d.High > maxVal) maxVal = d.High;
								double dSpan = Math.Max(0.0, d.High - d.Low);
								if (dSpan > maxSingleDaySpan) maxSingleDaySpan = dSpan;
							}

							if (minVal == double.MaxValue || maxVal == double.MinValue)
							{
								int tailStart = Math.Max(0, laidOutDays.Count - 4);
								for (int i = tailStart; i < laidOutDays.Count; i++)
								{
									DayProfile d = laidOutDays[i];
									if (d.Low < minVal) minVal = d.Low;
									if (d.High > maxVal) maxVal = d.High;
									double dSpan = Math.Max(0.0, d.High - d.Low);
									if (dSpan > maxSingleDaySpan) maxSingleDaySpan = dSpan;
								}
							}

							if (minVal < double.MaxValue && maxVal > double.MinValue)
							{
								float topBoxRight = panelLeft + 750f;
								float leftBoxClearancePx = ShowTopLeftBox ? (IsTopLeftBoxMinimized ? 92f : 138f) : 64f;
								float usableBotRef = Math.Max(220f, panelH - botPadPx);
								float maxSpanPx = Math.Max(160f, usableBotRef - 64f);
								double safeSpan = Math.Max(baseStep * 6.0, maxVal - minVal);
								for (int i = 0; i < laidOutDays.Count; i++)
								{
									DayProfile d = laidOutDays[i];
									if (d.SlotRightX < panelLeft + 15f || d.SlotLeftX > panelRight - 182f)
										continue;
									if (d.IbX < topBoxRight)
									{
										double frac = (d.High - minVal) / safeSpan;
										if (frac > 0.08)
										{
											float allowedSpan = (float)((usableBotRef - leftBoxClearancePx) / frac);
											if (allowedSpan < maxSpanPx)
												maxSpanPx = Math.Max(160f, allowedSpan);
										}
									}
								}
								topPadPx = Math.Max(64f, usableBotRef - maxSpanPx);
							}
						}
					}
				}
			}

			if (minVal < double.MaxValue && maxVal > double.MinValue)
			{
				float usableH = Math.Max(160f, panelH - topPadPx - botPadPx);
				double rawPriceSpan = Math.Max(baseStep * 6.0, maxVal - minVal);
				double rawRows = rawPriceSpan / baseStep + 2.0;
				float rawPxPerRow = usableH / (float)Math.Max(1.0, rawRows);
				int groupMult = 1;
				if (AutoScaleTpoByScreenResolution)
				{
					groupMult = rawPxPerRow < MinReadableTpoRowPx
						? Math.Max(1, (int)Math.Ceiling(MinReadableTpoRowPx / Math.Max(0.4f, rawPxPerRow)))
						: 1;
					int reqDayMult = (int)Math.Ceiling((maxSingleDaySpan / baseStep + 1.0) / Math.Max(12.0, usableH / MinReadableTpoRowPx));
					if (reqDayMult > groupMult)
						groupMult = reqDayMult;
				}
				double renderStep = baseStep * groupMult;

				double lowEdge = minVal - renderStep * 0.6;
				double highEdge = maxVal + renderStep * 0.6;
				double minComfortableSpan = (usableH / MaxComfortableTpoRowPx) * renderStep;
				if (highEdge - lowEdge < minComfortableSpan)
				{
					double mid = 0.5 * (highEdge + lowEdge);
					lowEdge = mid - 0.5 * minComfortableSpan;
					highEdge = mid + 0.5 * minComfortableSpan;
				}

				double baseMid = 0.5 * (highEdge + lowEdge);
				double baseSpan = Math.Max(renderStep * 3.0, highEdge - lowEdge);
				lastBaseMidPrice = baseMid;
				lastBasePriceSpan = baseSpan;
				lastUsableHeightPx = usableH;

				double activeZoom = Math.Max(0.15, Math.Min(4.0, verticalZoomFactor));
				double activeSpan = Math.Max(renderStep * 3.0, baseSpan * activeZoom);
				double activeMid = baseMid + verticalPanPriceOffset;
				highEdge = activeMid + 0.5 * activeSpan;
				lowEdge = activeMid - 0.5 * activeSpan;

				double ptsPerPx = (highEdge - lowEdge) / usableH;
				MaxValue = highEdge + topPadPx * ptsPerPx;
				MinValue = lowEdge - botPadPx * ptsPerPx;
			}
		}

		private static bool IsIndianNseSymbolForInstrument(Instrument inst)
		{
			if (inst == null)
				return true;
			try
			{
				string fn = inst.FullName ?? string.Empty;
				string mn = inst.MasterInstrument != null ? (inst.MasterInstrument.Name ?? string.Empty) : string.Empty;
				if (fn.EndsWith("_I", StringComparison.OrdinalIgnoreCase) ||
					fn.EndsWith("_II", StringComparison.OrdinalIgnoreCase) ||
					fn.EndsWith("_III", StringComparison.OrdinalIgnoreCase) ||
					mn.EndsWith("_I", StringComparison.OrdinalIgnoreCase) ||
					mn.EndsWith("_II", StringComparison.OrdinalIgnoreCase) ||
					mn.EndsWith("_III", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
				if (fn.StartsWith("NIFTY", StringComparison.OrdinalIgnoreCase) ||
					fn.StartsWith("BANKNIFTY", StringComparison.OrdinalIgnoreCase) ||
					fn.StartsWith("FINNIFTY", StringComparison.OrdinalIgnoreCase) ||
					fn.StartsWith("MIDCPNIFTY", StringComparison.OrdinalIgnoreCase) ||
					fn.StartsWith("SENSEX", StringComparison.OrdinalIgnoreCase) ||
					fn.StartsWith("INDIAVIX", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
				if (inst.MasterInstrument != null && inst.MasterInstrument.TradingHours != null)
				{
					string thName = inst.MasterInstrument.TradingHours.Name ?? string.Empty;
					if (thName.IndexOf("NSE", StringComparison.OrdinalIgnoreCase) >= 0 ||
						thName.IndexOf("BSE", StringComparison.OrdinalIgnoreCase) >= 0 ||
						thName.IndexOf("India", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						return true;
					}
				}
			}
			catch { }
			return false;
		}

		private bool IsIndianNseSymbol()
		{
			return IsIndianNseSymbolForInstrument(Instrument);
		}

		private double ComputeAutoOrManualPriceStep()
		{
			double tick = Instrument == null || Instrument.MasterInstrument == null
				? 0.05
				: Math.Max(0.0000001, Instrument.MasterInstrument.TickSize);
			if (IsIndianNseSymbol() && tick < 0.05)
				tick = 0.05;

			if (TpoSize > 0)
				return Math.Max(tick, Math.Round(TpoSize / tick) * tick);

			if (rawMinuteSamples.Count == 0)
				return tick * 10.0;

			// Median day range targeting ~20-24 clean, tall rows per daily profile
			Dictionary<DateTime, double[]> dayHiLo = new Dictionary<DateTime, double[]>();
			int startIdx = Math.Max(0, rawMinuteSamples.Count - 20 * 1440);
			for (int i = startIdx; i < rawMinuteSamples.Count; i++)
			{
				MinuteSample s = rawMinuteSamples[i];
				DateTime dt = s.Time.Date;
				double[] hl;
				if (!dayHiLo.TryGetValue(dt, out hl))
				{
					hl = new double[] { s.High, s.Low };
					dayHiLo[dt] = hl;
				}
				else
				{
					if (s.High > hl[0]) hl[0] = s.High;
					if (s.Low < hl[1]) hl[1] = s.Low;
				}
			}

			List<double> ranges = dayHiLo.Values
				.Select(hl => hl[0] - hl[1])
				.Where(r => r > tick * 2)
				.OrderBy(r => r)
				.ToList();

			if (ranges.Count == 0)
				return tick * 10.0;

			double medianRange = ranges[ranges.Count / 2];
			double rawStep = medianRange / 22.0;
			double ticksPerRow = Math.Max(1.0, Math.Round(rawStep / tick));
			return ticksPerRow * tick;
		}

		private void RebuildAllDayProfiles(double step)
		{
			cachedPriceStep = step;
			lastRebuiltSampleCount = rawMinuteSamples.Count;
			profiles.Clear();

			for (int i = 0; i < rawMinuteSamples.Count; i++)
				AccumulateSampleIntoDay(rawMinuteSamples[i], step, isLiveTickUpdate: false);

			if (Instrument != null && !string.IsNullOrEmpty(Instrument.FullName))
				PersistAtrHistoryToSharedCacheLocked(Instrument.FullName);

			int maxProfiles = Math.Max(30, DaysToDisplay * 2);
			while (profiles.Count > maxProfiles)
				profiles.Remove(profiles.Keys.First());
		}

		private bool TryGetDaySession(DateTime time, out DateTime dayDate, out DateTime dayStart, out DateTime dayClose, out int periodIndex, out int expectedPeriods)
		{
			dayDate = time.Date;
			int openSec = ToSeconds(MarketOpen);
			int closeSec = ToSeconds(MarketClose);
			if (openSec <= 0 && closeSec <= 0)
			{
				openSec = 9 * 3600 + 15 * 60; // 09:15:00
				closeSec = 15 * 3600 + 30 * 60; // 15:30:00
			}

			bool isNse = IsIndianNseSymbol();
			bool hasDefaultNseHours = (MarketOpen == 91500 && MarketClose == 153000);
			if (!isNse && hasDefaultNseHours)
			{
				// Automatically support MCX / US Futures / Crypto / Forex on standalone charts without dropping post-15:30 bars
				int intervalMin = Math.Max(1, TpoIntervalMinutes);
				expectedPeriods = Math.Max(1, (int)Math.Ceiling((24 * 60) / (double)intervalMin));
				dayStart = dayDate;
				dayClose = dayDate.AddDays(1);
				double elapsed24 = Math.Max(0.0, (time - dayStart).TotalMinutes - 0.001);
				periodIndex = Math.Max(0, Math.Min(expectedPeriods - 1, (int)Math.Floor(elapsed24 / intervalMin)));
				return true;
			}

			int durationSec = closeSec > openSec ? closeSec - openSec : (24 * 3600 - openSec) + closeSec;
			if (durationSec <= 0) durationSec = 375 * 60;
			expectedPeriods = Math.Max(1, (int)Math.Ceiling(durationSec / (double)(Math.Max(1, TpoIntervalMinutes) * 60)));

			dayStart = dayDate.AddSeconds(openSec);
			dayClose = dayStart.AddSeconds(durationSec);

			int nowSec = time.Hour * 3600 + time.Minute * 60 + time.Second;
			if (FilterByTradingHours)
			{
				// Allow NSE closing settlement minute bars (15:30 - 15:44) cleanly into Period M (index 12)
				int closeGraceSec = isNse ? (15 * 60) : 59;
				bool inSession = (nowSec >= openSec && nowSec <= closeSec + closeGraceSec);
				if (!inSession)
				{
					periodIndex = 0;
					return false;
				}
			}

			double elapsedMinutes = Math.Max(0.0, (time - dayStart).TotalMinutes - 0.001);
			periodIndex = (int)Math.Floor(elapsedMinutes / Math.Max(1, TpoIntervalMinutes));
			periodIndex = Math.Max(0, Math.Min(expectedPeriods - 1, periodIndex));
			return true;
		}

		private void AccumulateSampleIntoDay(MinuteSample sample, double step, bool isLiveTickUpdate)
		{
			DateTime dayDate;
			DateTime dayStart;
			DateTime dayClose;
			int periodIndex;
			int expectedPeriods;

			if (!TryGetDaySession(sample.Time, out dayDate, out dayStart, out dayClose, out periodIndex, out expectedPeriods))
				return;

			string profileKey = dayDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
			DayProfile day;
			if (!profiles.TryGetValue(profileKey, out day))
			{
				day = new DayProfile
				{
					ProfileKey = profileKey,
					SessionDate = dayDate,
					SessionName = SessionLabel,
					ExpectedPeriodCount = expectedPeriods,
					EffectiveOpenTime = dayStart,
					SessionCloseTime = dayClose
				};
				profiles.Add(profileKey, day);
			}

			double elapsedFromOpen = Math.Max(0.0, (sample.Time - day.EffectiveOpenTime).TotalMinutes - 0.001);
			periodIndex = (int)Math.Floor(elapsedFromOpen / Math.Max(1, TpoIntervalMinutes));
			periodIndex = Math.Max(0, Math.Min(expectedPeriods - 1, periodIndex));

			double prevClose = day.BarCount > 0 ? day.Close : sample.Open;
			if (day.BarCount == 0)
				day.Open = sample.Open;
			if (!isLiveTickUpdate)
				day.BarCount++;
			day.FirstBar = Math.Min(day.FirstBar, sample.BarIndex);
			day.LastBar = sample.BarIndex;
			day.LastPeriod = Math.Max(day.LastPeriod, periodIndex);
			if (sample.Time > day.LastSampleTime)
				day.LastSampleTime = sample.Time;
			day.High = Math.Max(day.High, sample.High);
			day.Low = Math.Min(day.Low, sample.Low);
			day.Close = sample.Close;
			day.ContinuityChecked = false;
			day.AnalyticsValid = false;

			PeriodSlice slice;
			if (!day.PeriodSlices.TryGetValue(periodIndex, out slice))
			{
				slice = new PeriodSlice
				{
					PeriodIndex = periodIndex,
					Open = prevClose
				};
				day.PeriodSlices.Add(periodIndex, slice);
			}
			if (!isLiveTickUpdate)
				slice.BarCount++;
			slice.High = Math.Max(slice.High, Math.Max(sample.High, prevClose));
			slice.Low = Math.Min(slice.Low, Math.Min(sample.Low, prevClose));
			slice.Close = sample.Close;
			slice.TotalVolume += sample.Volume;

			int effFirstBin = PriceToBin(Math.Min(sample.Low, prevClose), step);
			int effLastBin = PriceToBin(Math.Max(sample.High, prevClose), step);
			if (effLastBin < effFirstBin)
			{
				int tmpB = effFirstBin;
				effFirstBin = effLastBin;
				effLastBin = tmpB;
			}
			if (effLastBin - effFirstBin > 600)
			{
				effFirstBin = PriceToBin(sample.Close, step);
				effLastBin = effFirstBin;
			}
			int effBinCount = Math.Max(1, effLastBin - effFirstBin + 1);
			double effVolumePerBin = sample.Volume / effBinCount;

			for (int bin = effFirstBin; bin <= effLastBin; bin++)
			{
				PriceRow row;
				if (!day.Rows.TryGetValue(bin, out row))
				{
					row = new PriceRow();
					day.Rows.Add(bin, row);
				}
				row.Periods.Add(periodIndex);
				row.Volume += effVolumePerBin;

				double prevSliceVol;
				slice.BinVolumes.TryGetValue(bin, out prevSliceVol);
				slice.BinVolumes[bin] = prevSliceVol + effVolumePerBin;
			}

			int sliceMinBin = PriceToBin(slice.Low, step);
			int sliceMaxBin = PriceToBin(slice.High, step);
			if (sliceMaxBin - sliceMinBin <= 600)
			{
				for (int sBin = sliceMinBin; sBin <= sliceMaxBin; sBin++)
				{
					PriceRow sRow;
					if (!day.Rows.TryGetValue(sBin, out sRow))
					{
						sRow = new PriceRow();
						day.Rows.Add(sBin, sRow);
					}
					sRow.Periods.Add(periodIndex);
				}
			}
			day.TotalVolume += sample.Volume;

			// Initial Balance (Periods A & B = first InitialBalanceMinutes)
			int ibPeriods = Math.Max(1, (int)Math.Round(InitialBalanceMinutes / (double)Math.Max(1, TpoIntervalMinutes)));
			if (periodIndex < ibPeriods || (elapsedFromOpen >= 0 && elapsedFromOpen <= InitialBalanceMinutes))
			{
				day.InitialBalanceHigh = Math.Max(day.InitialBalanceHigh, sample.High);
				day.InitialBalanceLow = Math.Min(day.InitialBalanceLow, sample.Low);
			}

			if (day.High >= day.Low && day.Low < double.MaxValue && day.Close > 0)
			{
				atrDailyHistory[day.SessionDate.Date] = new DailyOhlcSample
				{
					Date = day.SessionDate.Date,
					Open = day.Open > 0 ? day.Open : day.Close,
					High = day.High,
					Low = day.Low,
					Close = day.Close
				};
			}

			int maxProfiles = Math.Max(30, DaysToDisplay * 2);
			while (profiles.Count > maxProfiles)
				profiles.Remove(profiles.Keys.First());
		}

		private double GetPriceStep()
		{
			return cachedPriceStep > 0 ? cachedPriceStep : ComputeAutoOrManualPriceStep();
		}

		private static int PriceToBin(double price, double step)
		{
			return (int)Math.Round(price / step, MidpointRounding.AwayFromZero);
		}

		private static int ToSeconds(int hhmmss)
		{
			int h = hhmmss / 10000;
			int m = (hhmmss / 100) % 100;
			int s = hhmmss % 100;
			return h * 3600 + m * 60 + s;
		}

		private string FormatPrice(double price)
		{
			double tick = Instrument == null || Instrument.MasterInstrument == null ? 0.05 : Instrument.MasterInstrument.TickSize;
			if (IsIndianNseSymbol() && tick < 0.05)
				tick = 0.05;
			if (tick < 0.00005)
				return price.ToString("0.00000", CultureInfo.InvariantCulture);
			if (tick < 0.0005)
				return price.ToString("0.0000", CultureInfo.InvariantCulture);
			if (tick < 0.005)
				return price.ToString("0.000", CultureInfo.InvariantCulture);
			return price.ToString("0.00", CultureInfo.InvariantCulture);
		}

		// Buying Single Print -> Bottom line & Bottom price (SL below support).
		// Selling Single Print -> Top line & Top price (SL above resistance).
		private double GetSinglePrintStopLossPrice(SinglePrintZone spZone, double dStep)
		{
			double tick = (Instrument == null || Instrument.MasterInstrument == null || Instrument.MasterInstrument.TickSize <= 0)
				? 0.05
				: Instrument.MasterInstrument.TickSize;
			if (IsIndianNseSymbol() && tick < 0.05)
				tick = 0.05;

			double rawPrice;
			if (dStep <= tick * 1.05)
			{
				rawPrice = spZone.IsBuying ? (spZone.StartBin * dStep) : (spZone.EndBin * dStep);
			}
			else
			{
				rawPrice = spZone.IsBuying
					? (spZone.StartBin * dStep - dStep * 0.5)
					: (spZone.EndBin * dStep + dStep * 0.5);
			}

			if (!IsIndianNseSymbol() && Instrument != null && Instrument.MasterInstrument != null && tick > 0)
				return Instrument.MasterInstrument.RoundToTickSize(rawPrice);

			return Math.Round(rawPrice / tick, MidpointRounding.AwayFromZero) * tick;
		}

		private static void CalculateTpoValueArea(DayProfile day, double percent, out int poc, out int vah, out int val)
		{
			poc = 0;
			vah = 0;
			val = 0;
			if (day == null || day.Rows.Count == 0)
				return;

			int min = day.Rows.Keys.Min();
			int max = day.Rows.Keys.Max();
			double mid = (min + max) * 0.5;
			poc = day.Rows
				.OrderByDescending(kv => kv.Value.Periods.Count)
				.ThenByDescending(kv => kv.Value.Volume)
				.ThenBy(kv => Math.Abs(kv.Key - mid))
				.First().Key;

			int totalTpo = Math.Max(1, day.Rows.Values.Sum(r => r.Periods.Count));
			int target = Math.Max(1, (int)Math.Ceiling(totalTpo * (percent / 100.0)));
			int included = day.Rows[poc].Periods.Count;
			int low = poc;
			int high = poc;

			while (included < target && (low > min || high < max))
			{
				PriceRow lowRow;
				PriceRow highRow;
				int lowCount = low > min && day.Rows.TryGetValue(low - 1, out lowRow) ? lowRow.Periods.Count : -1;
				int highCount = high < max && day.Rows.TryGetValue(high + 1, out highRow) ? highRow.Periods.Count : -1;

				if (highCount >= lowCount && high < max)
				{
					high++;
					if (highCount > 0) included += highCount;
				}
				else if (low > min)
				{
					low--;
					if (lowCount > 0) included += lowCount;
				}
				else if (high < max)
				{
					high++;
					if (highCount > 0) included += highCount;
				}
			}
			vah = high;
			val = low;
		}

		private static void CalculateVolumeValueArea(DayProfile day, double percent, int volumePoc, out int vah, out int val)
		{
			vah = volumePoc;
			val = volumePoc;
			if (day == null || day.Rows.Count == 0)
				return;

			int min = day.Rows.Keys.Min();
			int max = day.Rows.Keys.Max();
			double totalVolume = Math.Max(1e-9, day.Rows.Values.Sum(r => r.Volume));
			double target = totalVolume * (percent / 100.0);
			double included = day.Rows[volumePoc].Volume;
			int low = volumePoc;
			int high = volumePoc;

			while (included < target && (low > min || high < max))
			{
				PriceRow lowRow;
				PriceRow highRow;
				double lowVolume = low > min && day.Rows.TryGetValue(low - 1, out lowRow) ? lowRow.Volume : -1;
				double highVolume = high < max && day.Rows.TryGetValue(high + 1, out highRow) ? highRow.Volume : -1;

				if (highVolume >= lowVolume && high < max)
				{
					high++;
					included += Math.Max(0, highVolume);
				}
				else if (low > min)
				{
					low--;
					included += Math.Max(0, lowVolume);
				}
				else if (high < max)
				{
					high++;
					included += Math.Max(0, highVolume);
				}
			}
			vah = high;
			val = low;
		}

		// Computes per-candle (per-TPO PeriodSlice) volume distribution, Volume PoC, and 70% Value Area for the Last-3-Candles Split Volume Bars
		private static void CalculateSliceVolumeValueArea(
			PeriodSlice slice,
			int minBin,
			int maxBin,
			double percent,
			out Dictionary<int, double> binVols,
			out double maxSliceVol,
			out int sliceVolPoc,
			out int sliceVolVah,
			out int sliceVolVal)
		{
			binVols = new Dictionary<int, double>();
			maxSliceVol = 1.0;
			sliceVolPoc = (minBin + maxBin) / 2;
			sliceVolVah = maxBin;
			sliceVolVal = minBin;
			if (slice == null || maxBin < minBin)
				return;

			double minPositive = double.MaxValue;
			foreach (KeyValuePair<int, double> kv in slice.BinVolumes)
			{
				if (kv.Key >= minBin && kv.Key <= maxBin && kv.Value > 0 && kv.Value < minPositive)
					minPositive = kv.Value;
			}
			double fallbackVol = (minPositive < double.MaxValue) ? Math.Max(1e-6, minPositive * 0.35) : 1.0;

			double totalVol = 0.0;
			double mid = (minBin + maxBin) * 0.5;
			double bestVol = -1.0;
			double bestDist = double.MaxValue;

			for (int b = minBin; b <= maxBin; b++)
			{
				double v;
				if (!slice.BinVolumes.TryGetValue(b, out v) || v <= 0)
					v = fallbackVol;
				binVols[b] = v;
				totalVol += v;
				double dist = Math.Abs(b - mid);
				if (v > bestVol || (Math.Abs(v - bestVol) <= 1e-9 && dist < bestDist))
				{
					bestVol = v;
					bestDist = dist;
					sliceVolPoc = b;
				}
			}

			maxSliceVol = Math.Max(1e-9, bestVol);
			double targetVol = totalVol * (percent / 100.0);
			double includedVol = binVols[sliceVolPoc];
			int low = sliceVolPoc;
			int high = sliceVolPoc;

			while (includedVol < targetVol && (low > minBin || high < maxBin))
			{
				double lowVol = low > minBin ? binVols[low - 1] : -1.0;
				double highVol = high < maxBin ? binVols[high + 1] : -1.0;

				if (highVol >= lowVol && high < maxBin)
				{
					high++;
					includedVol += highVol;
				}
				else if (low > minBin)
				{
					low--;
					includedVol += lowVol;
				}
				else if (high < maxBin)
				{
					high++;
					includedVol += highVol;
				}
			}

			sliceVolVah = high;
			sliceVolVal = low;
		}

		// Exact AK10X Profile Engine (10030E3B..10030EA5) Single Print direction classification:
		// Compares session Close against the Single Print zone [startBin..endBin], with fallback to period slice and session direction.
		private static bool IsBuyingSinglePrintZone(DayProfile day, int startBin, int endBin, int periodIndex, int pocBin)
		{
			if (day != null)
			{
				double step = day.EffectiveStep;
				if (step > 0)
				{
					int closeBin = PriceToBin(day.Close, step);
					if (closeBin > endBin)
						return true;
					if (closeBin < startBin)
						return false;
				}

				PeriodSlice slice;
				if (day.PeriodSlices.TryGetValue(periodIndex, out slice))
				{
					if (Math.Abs(slice.Close - slice.Open) > 1e-9)
						return slice.Close > slice.Open;
				}

				if (Math.Abs(day.Close - day.Open) > 1e-9)
					return day.Close >= day.Open;
			}
			return ((startBin + endBin) / 2) <= pocBin;
		}

		private static bool IsBuyingSinglePrintBin(DayProfile day, int bin, PriceRow row, int minBin, int maxBin, int pocBin)
		{
			int pIdx = (row != null && row.Periods.Count > 0) ? row.Periods.First() : 0;
			return IsBuyingSinglePrintZone(day, bin, bin, pIdx, pocBin);
		}

		// Builds contiguous interior Buying & Selling Single Print zones strictly between the multi-TPO profile body
		// (Excludes Buying Tail at the bottom extreme and Selling Tail at the top extreme)
		private static List<SinglePrintZone> ComputeSinglePrintZones(DayProfile day, int minBin, int maxBin, int pocBin)
		{
			List<SinglePrintZone> zones = new List<SinglePrintZone>();
			if (day == null || maxBin - minBin < 4)
				return zones;

			// Skip the entire Buying Tail (contiguous 1-TPO rows connected to minBin)
			int bodyBottomBin = minBin;
			while (bodyBottomBin <= maxBin)
			{
				PriceRow r;
				if (day.Rows.TryGetValue(bodyBottomBin, out r) && r.Periods.Count >= 2)
					break;
				bodyBottomBin++;
			}

			// Skip the entire Selling Tail (contiguous 1-TPO rows connected to maxBin)
			int bodyTopBin = maxBin;
			while (bodyTopBin >= minBin)
			{
				PriceRow r;
				if (day.Rows.TryGetValue(bodyTopBin, out r) && r.Periods.Count >= 2)
					break;
				bodyTopBin--;
			}

			if (bodyTopBin - bodyBottomBin < 2)
				return zones;

			SinglePrintZone current = null;
			for (int b = bodyBottomBin + 1; b <= bodyTopBin - 1; b++)
			{
				PriceRow row;
				if (day.Rows.TryGetValue(b, out row) && row.Periods.Count == 1)
				{
					int pIdx = row.Periods.First();
					if (current != null && current.EndBin == b - 1 && current.PeriodIndex == pIdx)
					{
						current.EndBin = b;
					}
					else
					{
						current = new SinglePrintZone
						{
							StartBin = b,
							EndBin = b,
							PeriodIndex = pIdx
						};
						zones.Add(current);
					}
				}
				else
				{
					current = null;
				}
			}

			for (int i = 0; i < zones.Count; i++)
			{
				SinglePrintZone z = zones[i];
				z.IsBuying = IsBuyingSinglePrintZone(day, z.StartBin, z.EndBin, z.PeriodIndex, pocBin);
			}

			return zones;
		}

		private static string GetOpeningType(DayProfile day, DayProfile prevDay, double step, out bool isHighlightGreen)
		{
			isHighlightGreen = false;
			if (prevDay == null || prevDay.Rows.Count == 0)
				return "Open - Inside PDVA (OAIR)";

			int prevPoc, prevVah, prevVal;
			CalculateTpoValueArea(prevDay, 70, out prevPoc, out prevVah, out prevVal);
			double pdVah = prevVah * step + step * 0.5;
			double pdVal = prevVal * step - step * 0.5;

			if (day.Open > prevDay.High)
				return "Open - Outside PDR Up (OOR)";
			if (day.Open < prevDay.Low)
				return "Open - Outside PDR Down (OOR)";
			if (day.Open > pdVah)
			{
				isHighlightGreen = true;
				return "Open - Above PDVA (OAR)";
			}
			if (day.Open < pdVal)
			{
				isHighlightGreen = true;
				return "Open - Below PDVA (ORR)";
			}
			return "Open - Inside PDVA (OAIR)";
		}

		private static string ClassifyDayType(DayProfile day, int poc, int vah, int val, double step, out bool isGreen)
		{
			bool up = day.Close >= day.Open;
			isGreen = up;
			string dir = up ? "Up" : "Down";

			double ibH = day.InitialBalanceHigh;
			double ibL = day.InitialBalanceLow;
			double ibRange = (ibH != double.MinValue && ibL != double.MaxValue && ibH > ibL)
				? (ibH - ibL)
				: (day.High - day.Low) * 0.5;
			double dayRange = Math.Max(step, day.High - day.Low);

			bool extUp = ibH != double.MinValue && (day.High - ibH) > step * 1.5;
			bool extDown = ibL != double.MaxValue && (ibL - day.Low) > step * 1.5;

			int minBin = day.Rows.Keys.Min();
			int maxBin = day.Rows.Keys.Max();
			if (maxBin - minBin >= 10 && day.SinglePrintZones != null && day.SinglePrintZones.Count > 0 && dayRange > ibRange * 1.35)
			{
				isGreen = false;
				return "Double Distribution - " + dir;
			}

			if (extUp && extDown)
			{
				double closePos = (day.Close - day.Low) / dayRange;
				if (closePos >= 0.72 || closePos <= 0.28)
					return "Neutral - Extreme day - " + dir;
				return "Neutral - Center - " + dir;
			}

			if (dayRange > ibRange * 2.1)
				return "Trend day - " + dir;
			if (extUp || extDown)
				return "Normal Variation - " + dir;
			return "Normal day - " + dir;
		}

		private static void ComputeOneTimeFraming(DayProfile day, int intervalMinutes, out string otfText, out bool otfIsUp, out string roundNumberText)
		{
			otfIsUp = false;
			otfText = intervalMinutes + " min - One time framing - Down (1) - Active";
			roundNumberText = intervalMinutes + " min - Round Number (A - Low)";

			if (day == null || day.PeriodSlices.Count == 0)
				return;

			List<PeriodSlice> slices = day.PeriodSlices.Values.OrderBy(s => s.PeriodIndex).ToList();
			int streak = 1;
			int dir = 0;
			int brokenPeriod = -1;

			for (int i = 1; i < slices.Count; i++)
			{
				PeriodSlice prev = slices[i - 1];
				PeriodSlice cur = slices[i];
				bool higher = cur.High > prev.High && cur.Low >= prev.Low;
				bool lower = cur.Low < prev.Low && cur.High <= prev.High;

				if (higher)
				{
					if (dir == 1) streak++;
					else { dir = 1; streak = 1; brokenPeriod = -1; }
				}
				else if (lower)
				{
					if (dir == -1) streak++;
					else { dir = -1; streak = 1; brokenPeriod = -1; }
				}
				else if (dir != 0)
				{
					brokenPeriod = cur.PeriodIndex;
				}
			}

			if (dir == 0)
				dir = day.Close >= day.Open ? 1 : -1;
			otfIsUp = dir > 0;
			string dirName = otfIsUp ? "Up" : "Down";
			string lastLetter = LetterSequence[Math.Min(LetterSequence.Length - 1, slices[slices.Count - 1].PeriodIndex)].ToString();
			if (brokenPeriod >= 0)
			{
				string brkLetter = LetterSequence[Math.Min(LetterSequence.Length - 1, brokenPeriod)].ToString();
				otfText = string.Format(CultureInfo.InvariantCulture, "{0} min - One time framing - {1} ({2}) - Broken (in {3})",
					intervalMinutes, dirName, streak, brkLetter);
			}
			else
			{
				otfText = string.Format(CultureInfo.InvariantCulture, "{0} min - One time framing - {1} ({2}) - (in {3})",
					intervalMinutes, dirName, streak, lastLetter);
			}

			PeriodSlice latestSlice = slices[slices.Count - 1];
			string latestLet = LetterSequence[Math.Min(LetterSequence.Length - 1, latestSlice.PeriodIndex)].ToString();
			bool nearHigh = Math.Abs(latestSlice.Close - latestSlice.High) < Math.Abs(latestSlice.Close - latestSlice.Low);
			roundNumberText = string.Format(CultureInfo.InvariantCulture, "{0} min - Round Number ({1} - {2})",
				intervalMinutes, latestLet, nearHigh ? "High" : "Low");
		}

		private bool IsDaySplit(DayProfile day, bool isLatestDay)
		{
			if (day == null)
				return false;
			if (manuallyUnsplitProfileKeys.Contains(day.ProfileKey))
				return false;
			if (manuallySplitProfileKeys.Contains(day.ProfileKey))
				return true;
			if (SplitLatestDay && isLatestDay)
				return true;
			return false;
		}

		private static void EnsureFullDayTpoPeriods(DayProfile day, double step)
		{
			if (day == null || day.Rows.Count == 0)
				return;

			int expected = day.ExpectedPeriodCount > 0 ? day.ExpectedPeriodCount : 13;
			if (day.PeriodSlices.Count >= expected && day.LastPeriod >= expected - 1)
				return;

			double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;
			if (dStep <= 0) dStep = 0.05;

			int minBin = day.Rows.Keys.Min();
			int maxBin = day.Rows.Keys.Max();
			int spanBins = Math.Max(1, maxBin - minBin);

			int pocBin, vahBin, valBin;
			CalculateTpoValueArea(day, 70, out pocBin, out vahBin, out valBin);
			if (vahBin <= valBin)
			{
				valBin = minBin + spanBins / 4;
				vahBin = maxBin - spanBins / 4;
			}
			int vaSpan = Math.Max(2, vahBin - valBin);
			double avgSliceVol = day.PeriodSlices.Count > 0
				? Math.Max(1.0, day.PeriodSlices.Values.Average(s => s.TotalVolume))
				: Math.Max(1.0, day.TotalVolume / expected);

			double prevClose = day.Close;
			PeriodSlice lastExisting = day.PeriodSlices.Values.OrderBy(s => s.PeriodIndex).LastOrDefault();
			if (lastExisting != null)
				prevClose = lastExisting.Close;

			for (int p = 0; p < expected; p++)
			{
				if (day.PeriodSlices.ContainsKey(p))
				{
					prevClose = day.PeriodSlices[p].Close;
					continue;
				}

				double phase = (p * 1.35) + (day.SessionDate.DayOfYear * 0.4);
				double offsetNorm = Math.Sin(phase) * 0.35;
				int centerBin = (int)Math.Round(pocBin + offsetNorm * vaSpan);
				centerBin = Math.Max(minBin + 1, Math.Min(maxBin - 1, centerBin));

				int halfHeight = Math.Max(2, Math.Min(vaSpan / 2 + 1, spanBins / 3));
				int sLowBin = Math.Max(minBin, centerBin - halfHeight);
				int sHighBin = Math.Min(maxBin, centerBin + halfHeight);
				if (sHighBin < sLowBin)
				{
					sLowBin = minBin;
					sHighBin = maxBin;
				}

				double sLow = sLowBin * dStep;
				double sHigh = sHighBin * dStep;
				double sOpen = Math.Max(sLow, Math.Min(sHigh, prevClose));
				double sClose = (p == expected - 1)
					? Math.Max(sLow, Math.Min(sHigh, day.Close))
					: ((sLow + sHigh) * 0.5);

				PeriodSlice synth = new PeriodSlice
				{
					PeriodIndex = p,
					Open = sOpen,
					High = sHigh,
					Low = sLow,
					Close = sClose,
					BarCount = 30,
					TotalVolume = avgSliceVol
				};

				int bCount = Math.Max(1, sHighBin - sLowBin + 1);
				double volPerB = avgSliceVol / bCount;
				for (int b = sLowBin; b <= sHighBin; b++)
				{
					PriceRow r;
					if (!day.Rows.TryGetValue(b, out r))
					{
						r = new PriceRow();
						day.Rows.Add(b, r);
					}
					r.Periods.Add(p);
					synth.BinVolumes[b] = volPerB;
				}

				day.PeriodSlices[p] = synth;
				prevClose = sClose;
			}

			day.LastPeriod = Math.Max(day.LastPeriod, expected - 1);
		}

		private void EnsureDayRowContinuity(DayProfile src, double baseStep, bool isLatestDay)
		{
			if (src == null || src.Rows.Count == 0)
			{
				if (src != null)
					src.EffectiveStep = baseStep;
				return;
			}

			int minB = src.Rows.Keys.Min();
			int maxB = src.Rows.Keys.Max();
			int spanBins = Math.Max(1, maxB - minB);

			// Bridge any missing intermediate TPO periods in [0 .. src.LastPeriod] (e.g. intraday reconnect gap)
			if (src.PeriodSlices.Count > 0 && src.LastPeriod > 0 && src.PeriodSlices.Count < src.LastPeriod + 1 && baseStep > 0)
			{
				double avgSliceVol = Math.Max(1.0, src.PeriodSlices.Values.Average(s => s.TotalVolume));
				int ibPeriods = Math.Max(1, (int)Math.Round(InitialBalanceMinutes / (double)Math.Max(1, TpoIntervalMinutes)));

				for (int p = 0; p <= src.LastPeriod; p++)
				{
					if (src.PeriodSlices.ContainsKey(p))
						continue;

					PeriodSlice prevSl = null;
					for (int bp = p - 1; bp >= 0; bp--)
					{
						if (src.PeriodSlices.TryGetValue(bp, out prevSl))
							break;
					}
					PeriodSlice nextSl = null;
					for (int fp = p + 1; fp <= src.LastPeriod; fp++)
					{
						if (src.PeriodSlices.TryGetValue(fp, out nextSl))
							break;
					}

					double startPx = prevSl != null ? prevSl.Close : (nextSl != null ? nextSl.Open : src.Open);
					double endPx = nextSl != null ? nextSl.Open : (prevSl != null ? prevSl.Close : src.Close);
					int gapSpan = ((nextSl != null ? nextSl.PeriodIndex : src.LastPeriod) - (prevSl != null ? prevSl.PeriodIndex : 0));
					int gapStep = p - (prevSl != null ? prevSl.PeriodIndex : 0);
					double t0 = gapSpan > 0 ? Math.Max(0.0, Math.Min(1.0, (gapStep - 1.0) / gapSpan)) : 0.0;
					double t1 = gapSpan > 0 ? Math.Max(0.0, Math.Min(1.0, (double)gapStep / gapSpan)) : 1.0;

					double sOpen = startPx + (endPx - startPx) * t0;
					double sClose = startPx + (endPx - startPx) * t1;
					double midPx = 0.5 * (sOpen + sClose);
					double wave = Math.Sin(p * 1.45 + src.SessionDate.DayOfYear * 0.3) * (spanBins * 0.12) * baseStep;
					int centerBin = Math.Max(minB, Math.Min(maxB, PriceToBin(midPx + wave, baseStep)));
					int halfH = Math.Max(2, Math.Min(6, spanBins / 3));
					int sLowBin = Math.Max(minB, Math.Min(centerBin - halfH, Math.Min(PriceToBin(sOpen, baseStep), PriceToBin(sClose, baseStep))));
					int sHighBin = Math.Min(maxB, Math.Max(centerBin + halfH, Math.Max(PriceToBin(sOpen, baseStep), PriceToBin(sClose, baseStep))));
					if (sHighBin < sLowBin)
					{
						int tmp = sLowBin;
						sLowBin = sHighBin;
						sHighBin = tmp;
					}

					double sLow = sLowBin * baseStep;
					double sHigh = sHighBin * baseStep;
					sOpen = Math.Max(sLow, Math.Min(sHigh, sOpen));
					sClose = Math.Max(sLow, Math.Min(sHigh, sClose));

					PeriodSlice bridged = new PeriodSlice
					{
						PeriodIndex = p,
						Open = sOpen,
						High = sHigh,
						Low = sLow,
						Close = sClose,
						BarCount = Math.Max(1, TpoIntervalMinutes),
						TotalVolume = avgSliceVol
					};

					int bCnt = Math.Max(1, sHighBin - sLowBin + 1);
					double volPerB = avgSliceVol / bCnt;
					for (int b = sLowBin; b <= sHighBin; b++)
					{
						PriceRow r;
						if (!src.Rows.TryGetValue(b, out r))
						{
							r = new PriceRow();
							src.Rows.Add(b, r);
						}
						r.Periods.Add(p);
						r.Volume += volPerB * 0.35;
						bridged.BinVolumes[b] = volPerB;
					}

					src.PeriodSlices[p] = bridged;
					if (p < ibPeriods)
					{
						src.InitialBalanceHigh = Math.Max(src.InitialBalanceHigh, sHigh);
						src.InitialBalanceLow = Math.Min(src.InitialBalanceLow, sLow);
					}
				}

				minB = src.Rows.Keys.Min();
				maxB = src.Rows.Keys.Max();
			}

			for (int b = minB + 1; b < maxB; b++)
			{
				if (!src.Rows.ContainsKey(b))
				{
					PriceRow prevR;
					src.Rows.TryGetValue(b - 1, out prevR);
					PriceRow bridgeRow = new PriceRow();
					if (prevR != null && prevR.Periods.Count > 0)
					{
						bridgeRow.Periods.Add(prevR.Periods.Max());
						bridgeRow.Volume = prevR.Volume * 0.5;
					}
					else
					{
						bridgeRow.Periods.Add(0);
					}
					src.Rows.Add(b, bridgeRow);
				}
			}
			src.EffectiveStep = baseStep;
		}

		private static DayProfile BuildRenderDayProfile(DayProfile src, double baseStep, int groupMult)
		{
			if (src == null || src.Rows.Count == 0 || groupMult <= 1)
			{
				if (src != null)
					src.EffectiveStep = baseStep;
				return src;
			}

			double newStep = baseStep * groupMult;

			DayProfile dst = new DayProfile
			{
				ProfileKey = src.ProfileKey,
				SessionDate = src.SessionDate,
				SessionName = src.SessionName,
				ExpectedPeriodCount = src.ExpectedPeriodCount,
				EffectiveOpenTime = src.EffectiveOpenTime,
				SessionCloseTime = src.SessionCloseTime,
				LastSampleTime = src.LastSampleTime,
				FirstBar = src.FirstBar,
				LastBar = src.LastBar,
				LastPeriod = src.LastPeriod,
				BarCount = src.BarCount,
				Open = src.Open,
				High = src.High,
				Low = src.Low,
				Close = src.Close,
				TotalVolume = src.TotalVolume,
				InitialBalanceHigh = src.InitialBalanceHigh,
				InitialBalanceLow = src.InitialBalanceLow,
				IsSplitExpanded = src.IsSplitExpanded,
				EffectiveStep = newStep
			};

			foreach (KeyValuePair<int, PriceRow> kv in src.Rows)
			{
				double rowPrice = kv.Key * baseStep;
				int newBin = PriceToBin(rowPrice, newStep);
				PriceRow dstRow;
				if (!dst.Rows.TryGetValue(newBin, out dstRow))
				{
					dstRow = new PriceRow();
					dst.Rows.Add(newBin, dstRow);
				}
				dstRow.Volume += kv.Value.Volume;
				foreach (int p in kv.Value.Periods)
					dstRow.Periods.Add(p);
			}

			if (dst.Rows.Count > 1)
			{
				int dMin = dst.Rows.Keys.Min();
				int dMax = dst.Rows.Keys.Max();
				for (int b = dMin + 1; b < dMax; b++)
				{
					if (!dst.Rows.ContainsKey(b))
					{
						PriceRow prevR;
						dst.Rows.TryGetValue(b - 1, out prevR);
						PriceRow bridgeRow = new PriceRow();
						if (prevR != null && prevR.Periods.Count > 0)
						{
							bridgeRow.Periods.Add(prevR.Periods.Max());
							bridgeRow.Volume = prevR.Volume * 0.5;
						}
						else
						{
							bridgeRow.Periods.Add(0);
						}
						dst.Rows.Add(b, bridgeRow);
					}
				}
			}

			foreach (KeyValuePair<int, PeriodSlice> kv in src.PeriodSlices)
			{
				PeriodSlice srcSlice = kv.Value;
				PeriodSlice dstSlice = new PeriodSlice
				{
					PeriodIndex = srcSlice.PeriodIndex,
					Open = srcSlice.Open,
					High = srcSlice.High,
					Low = srcSlice.Low,
					Close = srcSlice.Close,
					BarCount = srcSlice.BarCount,
					TotalVolume = srcSlice.TotalVolume
				};
				foreach (KeyValuePair<int, double> bkv in srcSlice.BinVolumes)
				{
					int newBin = PriceToBin(bkv.Key * baseStep, newStep);
					double prevV;
					dstSlice.BinVolumes.TryGetValue(newBin, out prevV);
					dstSlice.BinVolumes[newBin] = prevV + bkv.Value;
				}
				dst.PeriodSlices[kv.Key] = dstSlice;
			}

			return dst;
		}

		private static float SessionPriceToY(DayProfile d, double price, double fallbackStep)
		{
			if (d == null || d.Rows.Count == 0)
				return 0f;
			double dStep = d.EffectiveStep > 0 ? d.EffectiveStep : fallbackStep;
			int minB = d.Rows.Keys.Min();
			int maxB = d.Rows.Keys.Max();
			double topEdge = maxB * dStep + dStep * 0.5;
			double botEdge = minB * dStep - dStep * 0.5;
			double span = Math.Max(dStep * 0.5, topEdge - botEdge);
			double norm = (topEdge - price) / span;
			return (float)(d.RenderTopY + norm * (d.RenderBotY - d.RenderTopY));
		}

		private List<DayProfile> BuildWeeklyOrMonthlyDays(List<DayProfile> rawDays, bool isMonthly)
		{
			List<DayProfile> result = new List<DayProfile>();
			if (rawDays == null || rawDays.Count == 0)
				return result;

			Dictionary<string, List<DayProfile>> grouped = new Dictionary<string, List<DayProfile>>(StringComparer.Ordinal);
			List<string> orderedKeys = new List<string>();

			for (int i = 0; i < rawDays.Count; i++)
			{
				DayProfile d = rawDays[i];
				DateTime dt = d.SessionDate.Date;
				string groupKey;
				if (isMonthly)
				{
					groupKey = dt.ToString("yyyy-MM", CultureInfo.InvariantCulture);
				}
				else
				{
					int diff = (7 + (dt.DayOfWeek - DayOfWeek.Monday)) % 7;
					DateTime weekStart = dt.AddDays(-diff).Date;
					groupKey = weekStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
				}

				List<DayProfile> list;
				if (!grouped.TryGetValue(groupKey, out list))
				{
					list = new List<DayProfile>();
					grouped[groupKey] = list;
					orderedKeys.Add(groupKey);
				}
				list.Add(d);
			}

			for (int g = 0; g < orderedKeys.Count; g++)
			{
				string gKey = orderedKeys[g];
				List<DayProfile> group = grouped[gKey];
				if (group.Count == 0)
					continue;

				DayProfile first = group[0];
				DayProfile last = group[group.Count - 1];
				DateTime groupStartDate = first.SessionDate.Date;
				DateTime groupEndDate = last.SessionDate.Date;

				string titleName = isMonthly
					? groupStartDate.ToString("MMM yyyy", CultureInfo.InvariantCulture)
					: string.Format(CultureInfo.InvariantCulture, "Wk {0}-{1}",
						groupStartDate.ToString("dd MMM", CultureInfo.InvariantCulture),
						groupEndDate.ToString("dd MMM", CultureInfo.InvariantCulture));

				string profileKey = (isMonthly ? "MONTHLY_" : "WEEKLY_") + gKey;
				DayProfile agg = new DayProfile
				{
					ProfileKey = profileKey,
					SessionDate = groupStartDate,
					SessionName = titleName,
					ExpectedPeriodCount = 0,
					EffectiveOpenTime = first.EffectiveOpenTime,
					SessionCloseTime = last.SessionCloseTime,
					LastSampleTime = last.LastSampleTime,
					FirstBar = first.FirstBar,
					LastBar = last.LastBar,
					LastPeriod = 0,
					BarCount = 0,
					Open = first.Open,
					High = first.High,
					Low = first.Low,
					Close = last.Close,
					TotalVolume = 0,
					InitialBalanceHigh = first.InitialBalanceHigh,
					InitialBalanceLow = first.InitialBalanceLow,
					EffectiveStep = first.EffectiveStep
				};

				int periodOffset = 0;
				for (int k = 0; k < group.Count; k++)
				{
					DayProfile sub = group[k];
					agg.ExpectedPeriodCount += Math.Max(1, sub.ExpectedPeriodCount);
					agg.BarCount += sub.BarCount;
					agg.TotalVolume += sub.TotalVolume;
					if (sub.FirstBar < agg.FirstBar) agg.FirstBar = sub.FirstBar;
					if (sub.LastBar > agg.LastBar) agg.LastBar = sub.LastBar;
					if (sub.High > agg.High) agg.High = sub.High;
					if (sub.Low < agg.Low) agg.Low = sub.Low;
					agg.Close = sub.Close;

					int maxSubPeriod = 0;
					foreach (KeyValuePair<int, PriceRow> kv in sub.Rows)
					{
						PriceRow dstRow;
						if (!agg.Rows.TryGetValue(kv.Key, out dstRow))
						{
							dstRow = new PriceRow();
							agg.Rows.Add(kv.Key, dstRow);
						}
						dstRow.Volume += kv.Value.Volume;
						foreach (int p in kv.Value.Periods)
						{
							int shifted = p + periodOffset;
							dstRow.Periods.Add(shifted);
							if (p > maxSubPeriod) maxSubPeriod = p;
						}
					}

					foreach (KeyValuePair<int, PeriodSlice> skv in sub.PeriodSlices)
					{
						int shiftedIdx = skv.Key + periodOffset;
						if (skv.Key > maxSubPeriod) maxSubPeriod = skv.Key;
						PeriodSlice srcSlice = skv.Value;
						PeriodSlice dstSlice;
						if (!agg.PeriodSlices.TryGetValue(shiftedIdx, out dstSlice))
						{
							dstSlice = new PeriodSlice
							{
								PeriodIndex = shiftedIdx,
								Open = srcSlice.Open,
								High = srcSlice.High,
								Low = srcSlice.Low,
								Close = srcSlice.Close,
								BarCount = srcSlice.BarCount,
								TotalVolume = srcSlice.TotalVolume
							};
							foreach (KeyValuePair<int, double> bkv in srcSlice.BinVolumes)
								dstSlice.BinVolumes[bkv.Key] = bkv.Value;
							agg.PeriodSlices[shiftedIdx] = dstSlice;
						}
						else
						{
							if (srcSlice.High > dstSlice.High) dstSlice.High = srcSlice.High;
							if (srcSlice.Low < dstSlice.Low) dstSlice.Low = srcSlice.Low;
							dstSlice.Close = srcSlice.Close;
							dstSlice.BarCount += srcSlice.BarCount;
							dstSlice.TotalVolume += srcSlice.TotalVolume;
							foreach (KeyValuePair<int, double> bkv in srcSlice.BinVolumes)
							{
								double prevV;
								dstSlice.BinVolumes.TryGetValue(bkv.Key, out prevV);
								dstSlice.BinVolumes[bkv.Key] = prevV + bkv.Value;
							}
						}
					}

					periodOffset = periodOffset + maxSubPeriod + 1;
				}

				agg.LastPeriod = agg.PeriodSlices.Count > 0 ? agg.PeriodSlices.Keys.Max() : Math.Max(0, periodOffset - 1);
				result.Add(agg);
			}

			return result;
		}

		private List<DayProfile> BuildDayLayout(List<DayProfile> allDays, double step, float panelLeft, float panelRight)
		{
			if (allDays == null || allDays.Count == 0)
				return allDays;

			allDays.Sort((a, b) => a.SessionDate.CompareTo(b.SessionDate));

			// Ignore tiny pre-open / stub past day files when complete prior sessions exist, but never drop today's live session
			while (allDays.Count > 1 && allDays[allDays.Count - 1].BarCount < 2 && allDays[allDays.Count - 1].SessionDate < DateTime.Today)
			{
				allDays.RemoveAt(allDays.Count - 1);
			}

			// Display only up to DaysToDisplay
			if (DaysToDisplay > 0 && allDays.Count > DaysToDisplay)
				allDays = allDays.Skip(allDays.Count - DaysToDisplay).ToList();

			float charCellW = Math.Max(9.6f, Math.Min(11.2f, FontSize * 0.88f));
			float volReserveW = ShowVolumeProfile ? 54f : 0f;
			float minHeaderSlotW = ShowSummary ? 214f : 202f;

			float totalStripWidth = 0f;
			for (int i = 0; i < allDays.Count; i++)
			{
				DayProfile day = allDays[i];
				bool isLatestDay = (i == allDays.Count - 1);
				day.IsSplitExpanded = IsDaySplit(day, isLatestDay);

				double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;
				int closeBin = PriceToBin(day.Close, dStep);
				int maxRowCells = day.Rows.Count > 0
					? Math.Max(2, day.Rows.Max(kv => kv.Value.Periods.Count + (kv.Key == closeBin ? 1 : 0)))
					: 8;
				float visualProfileW = 6f + maxRowCells * charCellW + volReserveW + 6f;
				float collapsedW = Math.Max(minHeaderSlotW, visualProfileW + 14f);
				float dayW = collapsedW;
				if (day.IsSplitExpanded && day.PeriodSlices.Count > 0)
				{
					int sliceCols = Math.Max(3, day.PeriodSlices.Count);
					int volBarCount = ShowVolumeProfile ? Math.Min(3, Math.Max(1, day.PeriodSlices.Count)) : 0;
					float volAreaW = volBarCount > 0 ? (volBarCount * 28f + (volBarCount - 1) * 8f + 14f) : 0f;
					float splitVisualW = Math.Max(156f, sliceCols * 15.4f + volAreaW + 24f);
					float splitLeadW = FixSessionHeaderAtTop ? Math.Max(visualProfileW + 18f, minHeaderSlotW) : (visualProfileW + 18f);
					dayW = splitLeadW + splitVisualW + 8f;
				}
				totalStripWidth += dayW;
			}

			// Reserve 188px on right edge exclusively for virgin level labels
			float leftMargin = panelLeft + 10f;
			float rightLimit = panelRight - 188f;
			float usableW = Math.Max(420f, rightLimit - leftMargin);

			float maxScroll = Math.Max(0f, totalStripWidth - usableW);
			maxScrollCached = maxScroll;
			float scrollRatio = 1.0f;
			bool usedScrollBarRatio = false;
			try
			{
				var sb = hookedScrollBar ?? ResolveChartScrollBar(ChartControl);
				if (sb != null && sb.Maximum > sb.Minimum)
				{
					double sbVal = sb.Value;
					double sbMin = sb.Minimum;
					double sbMax = sb.Maximum;
					scrollRatio = (float)Math.Max(0.0, Math.Min(1.0, (sbVal - sbMin) / (sbMax - sbMin)));
					if (sbMax - sbVal <= 1.5)
						scrollRatio = 1.0f;
					else if (sbVal - sbMin <= 1.5)
						scrollRatio = 0.0f;
					usedScrollBarRatio = true;

					if (double.IsNaN(lastTrackedScrollBarVal))
					{
						lastTrackedScrollBarVal = sbVal;
					}
					else if (Math.Abs(sbVal - lastTrackedScrollBarVal) > 0.5)
					{
						lastTrackedScrollBarVal = sbVal;
						hasManualPixelScroll = false;
					}
				}
			}
			catch { }

			ChartBars activeBars = (ChartControl != null && ChartControl.PrimaryBars != null) ? ChartControl.PrimaryBars : ChartBars;
			if (!usedScrollBarRatio && activeBars != null)
			{
				int totalBars = Math.Max(activeBars.Count, activeBars.Bars != null ? activeBars.Bars.Count : 0);
				if (totalBars > 1)
				{
					int fromIdx = Math.Max(0, activeBars.FromIndex);
					int toIdx = Math.Max(fromIdx, activeBars.ToIndex);
					int visBars = Math.Max(1, toIdx - fromIdx + 1);
					int maxFrom = Math.Max(1, totalBars - visBars);

					if (visBars <= 3 && fromIdx == 0 && totalBars > 20)
						scrollRatio = 1.0f;
					else if (toIdx >= totalBars - 2)
						scrollRatio = 1.0f;
					else if (fromIdx <= 1 && toIdx <= visBars + 2)
						scrollRatio = 0.0f;
					else if (maxFrom > 1 && fromIdx > 1)
						scrollRatio = Math.Max(0f, Math.Min(1f, (float)fromIdx / maxFrom));
					else
						scrollRatio = Math.Max(0f, Math.Min(1f, (float)toIdx / Math.Max(1, totalBars - 1)));

					if (lastTrackedFromIdx < 0)
					{
						lastTrackedFromIdx = fromIdx;
						lastTrackedToIdx = toIdx;
					}
					else if (Math.Abs(fromIdx - lastTrackedFromIdx) > 2)
					{
						lastTrackedFromIdx = fromIdx;
						lastTrackedToIdx = toIdx;
						hasManualPixelScroll = false;
					}
				}
			}

			if (!hasManualPixelScroll || float.IsNaN(currentScrollPx))
				currentScrollPx = scrollRatio * maxScroll;

			currentScrollPx = Math.Max(0f, Math.Min(maxScroll, currentScrollPx));
			float curX = totalStripWidth <= usableW
				? leftMargin
				: (leftMargin - currentScrollPx);

			for (int i = 0; i < allDays.Count; i++)
			{
				DayProfile day = allDays[i];
				double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;
				int closeBin = PriceToBin(day.Close, dStep);
				int maxRowCells = day.Rows.Count > 0
					? Math.Max(2, day.Rows.Max(kv => kv.Value.Periods.Count + (kv.Key == closeBin ? 1 : 0)))
					: 8;
				float visualProfileW = 6f + maxRowCells * charCellW + volReserveW + 6f;
				float collapsedW = Math.Max(minHeaderSlotW, visualProfileW + 14f);
				float slotWidth = collapsedW;
				if (day.IsSplitExpanded && day.PeriodSlices.Count > 0)
				{
					int sliceCols = Math.Max(3, day.PeriodSlices.Count);
					int volBarCount = ShowVolumeProfile ? Math.Min(3, Math.Max(1, day.PeriodSlices.Count)) : 0;
					float volAreaW = volBarCount > 0 ? (volBarCount * 28f + (volBarCount - 1) * 8f + 14f) : 0f;
					float splitVisualW = Math.Max(156f, sliceCols * 15.4f + volAreaW + 24f);
					float splitLeadW = FixSessionHeaderAtTop ? Math.Max(visualProfileW + 18f, minHeaderSlotW) : (visualProfileW + 18f);
					slotWidth = splitLeadW + splitVisualW + 8f;
				}

				day.SlotLeftX = curX;
				day.SlotRightX = curX + slotWidth;
				day.IbX = curX + 6f;
				day.CollapsedRightX = day.IbX + visualProfileW;

				curX += slotWidth;
			}

			return allDays;
		}

		private enum LevelBreakMode
		{
			CloseAbove = 0,
			CloseBelow = 1,
			CrossClose = 2
		}

		private DateTime GetReferenceNow(List<DayProfile> days = null)
		{
			bool isPlayback = false;
			try
			{
				isPlayback = NinjaTrader.Cbi.Connection.PlaybackConnection != null
					&& NinjaTrader.Cbi.Connection.PlaybackConnection.Status == ConnectionStatus.Connected;
			}
			catch { }

			if (isPlayback && days != null && days.Count > 0)
			{
				DateTime maxSample = DateTime.MinValue;
				for (int i = 0; i < days.Count; i++)
				{
					if (days[i] != null && days[i].LastSampleTime > maxSample)
						maxSample = days[i].LastSampleTime;
				}
				if (maxSample > DateTime.MinValue)
					return maxSample;
			}
			return DateTime.Now;
		}

		private DateTime GetTradingDayCloseTime(DateTime sessionDate)
		{
			DateTime dt = sessionDate.Date;
			bool isNse = IsIndianNseSymbol();
			if (!isNse && MarketOpen == 91500 && MarketClose == 153000)
				return dt.AddDays(1);

			int openSec = ToSeconds(MarketOpen);
			int closeSec = ToSeconds(MarketClose);
			if (openSec <= 0 && closeSec <= 0)
			{
				openSec = 9 * 3600 + 15 * 60;
				closeSec = 15 * 3600 + 30 * 60;
			}
			int dur = closeSec > openSec ? (closeSec - openSec) : (24 * 3600 - openSec + closeSec);
			if (dur <= 0)
				dur = 375 * 60;
			return dt.AddSeconds(openSec + dur);
		}

		private static DateTime GetMondayOfWeek(DateTime dt)
		{
			int diff = ((int)dt.DayOfWeek + 6) % 7;
			return dt.Date.AddDays(-diff);
		}

		private static string FormatWeekRangeLabel(DateTime weekMonday, DateTime weekFriday)
		{
			if (weekMonday.Month == weekFriday.Month)
				return weekMonday.ToString("dd", CultureInfo.InvariantCulture) + "-" + weekFriday.ToString("dd MMM", CultureInfo.InvariantCulture);
			return weekMonday.ToString("ddMMM", CultureInfo.InvariantCulture) + "-" + weekFriday.ToString("ddMMM", CultureInfo.InvariantCulture);
		}

		private double GetVirginBreakEpsilon()
		{
			double tick = (Instrument == null || Instrument.MasterInstrument == null || Instrument.MasterInstrument.TickSize <= 0)
				? 0.05
				: Instrument.MasterInstrument.TickSize;
			if (IsIndianNseSymbol() && tick < 0.05)
				tick = 0.05;
			return Math.Max(1e-7, tick * 0.15);
		}

		private static bool IsRawDayRunning(DayProfile rawDay, bool isLatestRaw, DateTime refNow)
		{
			if (!isLatestRaw || rawDay == null)
				return false;
			if (rawDay.SessionCloseTime > DateTime.MinValue)
				return refNow < rawDay.SessionCloseTime;
			int exp = Math.Max(1, rawDay.ExpectedPeriodCount);
			int prn = Math.Max(rawDay.PeriodSlices.Count, rawDay.LastPeriod + 1);
			return prn < exp;
		}

		private bool IsDayRunning(DayProfile day, bool isLatestDay)
		{
			if (!isLatestDay || day == null)
				return false;

			bool isPlayback = false;
			try
			{
				isPlayback = NinjaTrader.Cbi.Connection.PlaybackConnection != null
					&& NinjaTrader.Cbi.Connection.PlaybackConnection.Status == ConnectionStatus.Connected;
			}
			catch { }

			DateTime refNow = (isPlayback && day.LastSampleTime > DateTime.MinValue) ? day.LastSampleTime : DateTime.Now;

			MarketProfilePeriodMode mode = EffectiveProfileMode;
			if (mode == MarketProfilePeriodMode.Monthly)
			{
				DateTime monthEnd = new DateTime(
					day.SessionDate.Year,
					day.SessionDate.Month,
					DateTime.DaysInMonth(day.SessionDate.Year, day.SessionDate.Month),
					23, 59, 59);
				return refNow <= monthEnd;
			}
			if (mode == MarketProfilePeriodMode.Weekly)
			{
				DateTime fridayClose = GetTradingDayCloseTime(day.SessionDate.Date.AddDays(4));
				return refNow < fridayClose;
			}

			if (day.SessionCloseTime > DateTime.MinValue)
				return refNow < day.SessionCloseTime;

			int exp = Math.Max(1, day.ExpectedPeriodCount);
			int prn = Math.Max(day.PeriodSlices.Count, day.LastPeriod + 1);
			return prn < exp;
		}

		private bool IsTpoSliceClosed(DayProfile day, PeriodSlice slice, bool isDayRunning, DateTime refNow)
		{
			if (day == null || slice == null)
				return false;
			if (!isDayRunning)
				return true;
			if (slice.PeriodIndex < day.LastPeriod)
				return true;

			if (EffectiveProfileMode == MarketProfilePeriodMode.Daily && day.EffectiveOpenTime > DateTime.MinValue)
			{
				DateTime sliceEnd = day.EffectiveOpenTime.AddMinutes((slice.PeriodIndex + 1) * Math.Max(1, TpoIntervalMinutes));
				if (refNow >= sliceEnd)
					return true;
			}

			if (day.SessionCloseTime > DateTime.MinValue && refNow >= day.SessionCloseTime)
				return true;

			return false;
		}

		private bool IsRawTpoSliceClosed(DayProfile rawDay, PeriodSlice slice, bool isRawRunning, DateTime refNow)
		{
			if (rawDay == null || slice == null)
				return false;
			if (!isRawRunning)
				return true;
			if (slice.PeriodIndex < rawDay.LastPeriod)
				return true;

			if (rawDay.EffectiveOpenTime > DateTime.MinValue)
			{
				DateTime sliceEnd = rawDay.EffectiveOpenTime.AddMinutes((slice.PeriodIndex + 1) * Math.Max(1, TpoIntervalMinutes));
				if (refNow >= sliceEnd)
					return true;
			}

			if (rawDay.SessionCloseTime > DateTime.MinValue && refNow >= rawDay.SessionCloseTime)
				return true;

			return false;
		}

		private static bool DoesClosedTpoBreakLevel(
			double tpoOpen,
			double tpoHigh,
			double tpoLow,
			double tpoClose,
			double levelPrice,
			double eps,
			LevelBreakMode mode,
			ref int currentSide,
			double originMidPrice)
		{
			if (mode == LevelBreakMode.CloseAbove)
				return tpoClose > levelPrice + eps;
			if (mode == LevelBreakMode.CloseBelow)
				return tpoClose < levelPrice - eps;

			if (currentSide > 0)
				return tpoClose < levelPrice - eps;
			if (currentSide < 0)
				return tpoClose > levelPrice + eps;

			if (tpoOpen > levelPrice + eps)
			{
				currentSide = 1;
				return tpoClose < levelPrice - eps;
			}
			if (tpoOpen < levelPrice - eps)
			{
				currentSide = -1;
				return tpoClose > levelPrice + eps;
			}
			if (tpoHigh > levelPrice + eps && tpoLow < levelPrice - eps)
			{
				if (originMidPrice > levelPrice + eps && tpoClose < levelPrice - eps)
					return true;
				if (originMidPrice < levelPrice - eps && tpoClose > levelPrice + eps)
					return true;
			}
			if (tpoClose > levelPrice + eps)
				currentSide = 1;
			else if (tpoClose < levelPrice - eps)
				currentSide = -1;
			return false;
		}

		private DayProfile FindRenderedDayForRaw(List<DayProfile> renderedDays, DayProfile rawDay)
		{
			if (renderedDays == null || renderedDays.Count == 0 || rawDay == null)
				return null;

			MarketProfilePeriodMode mode = EffectiveProfileMode;
			if (mode == MarketProfilePeriodMode.Daily)
			{
				for (int i = 0; i < renderedDays.Count; i++)
				{
					if (renderedDays[i].SessionDate.Date == rawDay.SessionDate.Date)
						return renderedDays[i];
				}
			}
			else if (mode == MarketProfilePeriodMode.Weekly)
			{
				DateTime wkMon = GetMondayOfWeek(rawDay.SessionDate);
				for (int i = 0; i < renderedDays.Count; i++)
				{
					if (GetMondayOfWeek(renderedDays[i].SessionDate) == wkMon)
						return renderedDays[i];
				}
			}
			else if (mode == MarketProfilePeriodMode.Monthly)
			{
				for (int i = 0; i < renderedDays.Count; i++)
				{
					if (renderedDays[i].SessionDate.Year == rawDay.SessionDate.Year && renderedDays[i].SessionDate.Month == rawDay.SessionDate.Month)
						return renderedDays[i];
				}
			}

			return null;
		}

		private static bool DoesCrossBreakLevel(double sessionHigh, double sessionLow, double levelPrice, LevelBreakMode mode, int originSide)
		{
			const double crossEps = 1e-5;
			if (mode == LevelBreakMode.CloseAbove)
				return sessionHigh >= levelPrice - crossEps;
			if (mode == LevelBreakMode.CloseBelow)
				return sessionLow <= levelPrice + crossEps;
			if (originSide > 0)
				return sessionLow <= levelPrice + crossEps;
			if (originSide < 0)
				return sessionHigh >= levelPrice - crossEps;
			return sessionLow <= levelPrice + crossEps && sessionHigh >= levelPrice - crossEps;
		}

		// Computes horizontal Virgin extension using selected VirginityBreakRule (CrossBased default vs CloseBased TPO-Close):
		private bool GetLevelExtensionX(
			List<DayProfile> allDays,
			int currentIdx,
			double levelPrice,
			float xOriginEnd,
			float panelRight,
			DateTime refNow,
			double eps,
			LevelBreakMode breakMode,
			out float drawEndX)
		{
			DayProfile origin = (allDays != null && currentIdx >= 0 && currentIdx < allDays.Count) ? allDays[currentIdx] : null;
			double originClose = origin != null ? origin.Close : levelPrice;
			double originMid = origin != null ? 0.5 * (origin.High + origin.Low) : levelPrice;
			int side = originClose > levelPrice + eps ? 1 : (originClose < levelPrice - eps ? -1 : 0);
			bool useCloseRule = (VirginityBreakRule == VirginityBreakRuleMode.CloseBased);

			for (int j = currentIdx + 1; j < allDays.Count; j++)
			{
				DayProfile next = allDays[j];
				bool broken = false;

				if (!useCloseRule)
				{
					broken = DoesCrossBreakLevel(next.High, next.Low, levelPrice, breakMode, side);
				}
				else
				{
					bool nextRunning = IsDayRunning(next, j == allDays.Count - 1);
					if (next.PeriodSlices != null && next.PeriodSlices.Count > 0)
					{
						foreach (KeyValuePair<int, PeriodSlice> kv in next.PeriodSlices)
						{
							PeriodSlice sl = kv.Value;
							if (!IsTpoSliceClosed(next, sl, nextRunning, refNow))
								continue;
							if (DoesClosedTpoBreakLevel(sl.Open, sl.High, sl.Low, sl.Close, levelPrice, eps, breakMode, ref side, originMid))
							{
								broken = true;
								break;
							}
						}
					}
					else if (!nextRunning)
					{
						if (DoesClosedTpoBreakLevel(next.Open, next.High, next.Low, next.Close, levelPrice, eps, breakMode, ref side, originMid))
							broken = true;
					}
				}

				if (broken)
				{
					drawEndX = Math.Min(panelRight - 6f, Math.Max(xOriginEnd, next.IbX));
					return false;
				}
			}

			drawEndX = panelRight - 6f;
			return true;
		}

		private bool GetRawLevelExtensionX(
			List<DayProfile> rawDays,
			int afterRawIdx,
			List<DayProfile> renderedDays,
			double levelPrice,
			float xOriginEnd,
			float panelLeft,
			float panelRight,
			DateTime refNow,
			double eps,
			LevelBreakMode breakMode,
			out float drawEndX)
		{
			DayProfile origin = (rawDays != null && afterRawIdx >= 0 && afterRawIdx < rawDays.Count) ? rawDays[afterRawIdx] : null;
			double originClose = origin != null ? origin.Close : levelPrice;
			double originMid = origin != null ? 0.5 * (origin.High + origin.Low) : levelPrice;
			int side = originClose > levelPrice + eps ? 1 : (originClose < levelPrice - eps ? -1 : 0);
			bool useCloseRule = (VirginityBreakRule == VirginityBreakRuleMode.CloseBased);

			for (int k = afterRawIdx + 1; k < rawDays.Count; k++)
			{
				DayProfile nextRaw = rawDays[k];
				bool broken = false;

				if (!useCloseRule)
				{
					broken = DoesCrossBreakLevel(nextRaw.High, nextRaw.Low, levelPrice, breakMode, side);
				}
				else
				{
					bool nextRunning = IsRawDayRunning(nextRaw, k == rawDays.Count - 1, refNow);
					if (nextRaw.PeriodSlices != null && nextRaw.PeriodSlices.Count > 0)
					{
						foreach (KeyValuePair<int, PeriodSlice> kv in nextRaw.PeriodSlices)
						{
							PeriodSlice sl = kv.Value;
							if (!IsRawTpoSliceClosed(nextRaw, sl, nextRunning, refNow))
								continue;
							if (DoesClosedTpoBreakLevel(sl.Open, sl.High, sl.Low, sl.Close, levelPrice, eps, breakMode, ref side, originMid))
							{
								broken = true;
								break;
							}
						}
					}
					else if (!nextRunning)
					{
						if (DoesClosedTpoBreakLevel(nextRaw.Open, nextRaw.High, nextRaw.Low, nextRaw.Close, levelPrice, eps, breakMode, ref side, originMid))
							broken = true;
					}
				}

				if (broken)
				{
					DayProfile breakCol = FindRenderedDayForRaw(renderedDays, nextRaw);
					if (breakCol == null)
					{
						drawEndX = panelLeft - 24f;
						return false;
					}
					drawEndX = Math.Min(panelRight - 6f, Math.Max(xOriginEnd, breakCol.IbX));
					return false;
				}
			}

			drawEndX = panelRight - 6f;
			return true;
		}

		private void RenderProfileLoadingOverlay()
		{
			if (RenderTarget == null || ChartPanel == null)
				return;
			try
			{
				float px = (float)ChartPanel.X;
				float py = (float)ChartPanel.Y;
				float pw = (float)ChartPanel.W;
				float ph = (float)ChartPanel.H;
				if (pw < 20f || ph < 20f)
					return;

				using (var bgBrush = new DxBrush(RenderTarget, new SharpDX.Color(18, 22, 29, 255)))
				using (var pillBg = new DxBrush(RenderTarget, new SharpDX.Color(12, 24, 36, 245)))
				using (var pillBorder = new DxBrush(RenderTarget, new SharpDX.Color(0, 229, 255, 190)))
				using (var txtBrush = new DxBrush(RenderTarget, new SharpDX.Color(225, 238, 248, 255)))
				using (var tf = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.SemiBold, SharpDX.DirectWrite.FontStyle.Normal, 12.5f))
				{
					tf.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
					tf.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
					RenderTarget.FillRectangle(new RectangleF(px, py, pw, ph), bgBrush);
					float bw = Math.Min(360f, pw - 24f);
					float bh = 36f;
					var pillRect = new RectangleF(px + (pw - bw) * 0.5f, py + (ph - bh) * 0.5f, bw, bh);
					RenderTarget.FillRectangle(pillRect, pillBg);
					RenderTarget.DrawRectangle(pillRect, pillBorder, 1.1f);
					string sym = Instrument != null ? Instrument.FullName : "SYMBOL";
					RenderTarget.DrawText("⚡ Loading Market Profile — " + sym + "...", tf, pillRect, txtBrush);
				}
			}
			catch { }
		}

		private void EnsureVisibleChartCandlesHidden()
		{
			try
			{
				if (ChartBars != null && BarBrushes != null && CandleOutlineBrushes != null && ChartBars.Count > 0)
				{
					int fIdx = Math.Max(0, ChartBars.FromIndex - 2);
					int tIdx = Math.Min(ChartBars.Count - 1, ChartBars.ToIndex + 2);
					for (int idx = fIdx; idx <= tIdx; idx++)
					{
						if (BarBrushes.Get(idx) == null)
							BarBrushes.Set(idx, Brushes.Transparent);
						if (CandleOutlineBrushes.Get(idx) == null)
							CandleOutlineBrushes.Set(idx, Brushes.Transparent);
					}
				}
			}
			catch { }
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (chartControl == null) return;
			if (ChartBars == null && chartControl.PrimaryBars != null)
				ChartBars = chartControl.PrimaryBars;
			if (ChartPanel == null && chartControl.ChartPanels != null && chartControl.ChartPanels.Count > 0)
				ChartPanel = chartControl.ChartPanels[0];

			if (ChartBars == null || ChartPanel == null || RenderTarget == null || IsInHitTest)
				return;

			EnsureVisibleChartCandlesHidden();

			try
			{
				lastRenderTargetW = RenderTarget.Size.Width;
				lastRenderTargetH = RenderTarget.Size.Height;
				lastRenderedChartScale = chartScale;
			}
			catch { }

			SilenceStandaloneCandles(chartControl);

			if (!didAutoAdjustChartProps && chartControl.Properties != null)
			{
				didAutoAdjustChartProps = true;
				bool isDayChart = BarsPeriods != null && BarsPeriods.Length > 0 && BarsPeriods[0].BarsPeriodType == BarsPeriodType.Day;
				if (isDayChart)
				{
					if (chartControl.Properties.BarDistance > 300f || chartControl.Properties.BarDistance < 20f)
						chartControl.Properties.BarDistance = 85f;
				}
				else
				{
					if (chartControl.Properties.BarDistance < 8.0f || chartControl.Properties.BarDistance > 30.0f)
						chartControl.Properties.BarDistance = 12.0f;
				}
				if (chartScale != null && chartScale.Properties != null)
				{
					chartScale.Properties.YAxisRangeType = YAxisRangeType.Automatic;
				}
				chartControl.Dispatcher.BeginInvoke(new Action(() =>
				{
					try
					{
						var initSlots = typeof(ChartControl).GetMethod("InitSlotsPainted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
						if (initSlots != null) initSlots.Invoke(chartControl, null);
						var initSb = typeof(ChartControl).GetMethod("InitScrollbar", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
						if (initSb != null && initSb.GetParameters().Length == 0) initSb.Invoke(chartControl, null);
						chartControl.InvalidateVisual();
					}
					catch { }
				}));
			}

			EnsureMouseHookAttached();

			lock (profileLock)
			{
				activeChartButtons.Clear();
				if (profiles.Count == 0)
				{
					RenderProfileLoadingOverlay();
					return;
				}

				List<DayProfile> allDays = profiles.Values.Where(p => p.Rows.Count > 0).ToList();
				if (allDays.Count == 0)
				{
					RenderProfileLoadingOverlay();
					return;
				}

				allDays.Sort((a, b) => a.SessionDate.CompareTo(b.SessionDate));
				List<DayProfile> rawAllDays = new List<DayProfile>(allDays);
				while (rawAllDays.Count > 1 && rawAllDays[rawAllDays.Count - 1].BarCount < 2 && rawAllDays[rawAllDays.Count - 1].SessionDate < DateTime.Today)
					rawAllDays.RemoveAt(rawAllDays.Count - 1);

				MarketProfilePeriodMode mode = EffectiveProfileMode;
				if (mode == MarketProfilePeriodMode.Weekly)
					allDays = BuildWeeklyOrMonthlyDays(allDays, isMonthly: false);
				else if (mode == MarketProfilePeriodMode.Monthly)
					allDays = BuildWeeklyOrMonthlyDays(allDays, isMonthly: true);

				if (allDays.Count == 0)
					return;

				double baseStep = GetPriceStep();
				for (int i = 0; i < allDays.Count; i++)
				{
					if (!allDays[i].ContinuityChecked || i == allDays.Count - 1)
					{
						EnsureDayRowContinuity(allDays[i], baseStep, i == allDays.Count - 1);
						allDays[i].ContinuityChecked = true;
					}
				}

				float panelLeft = (float)ChartPanel.X;
				float panelRight = (float)(ChartPanel.X + ChartPanel.W);
				float panelTop = (float)ChartPanel.Y;
				float panelBottom = (float)(ChartPanel.Y + ChartPanel.H);

				allDays = BuildDayLayout(allDays, baseStep, panelLeft, panelRight);
				if (allDays.Count == 0)
					return;

				double prelimMinPrice = double.MaxValue;
				double prelimMaxPrice = double.MinValue;
				for (int i = 0; i < allDays.Count; i++)
				{
					DayProfile d = allDays[i];
					if (d.SlotRightX < panelLeft + 15f || d.SlotLeftX > panelRight - 182f)
						continue;
					if (d.Low < prelimMinPrice) prelimMinPrice = d.Low;
					if (d.High > prelimMaxPrice) prelimMaxPrice = d.High;
				}
				if (prelimMinPrice == double.MaxValue || prelimMaxPrice == double.MinValue)
				{
					int tailStart = Math.Max(0, allDays.Count - 3);
					for (int i = tailStart; i < allDays.Count; i++)
					{
						DayProfile d = allDays[i];
						if (d.Low < prelimMinPrice) prelimMinPrice = d.Low;
						if (d.High > prelimMaxPrice) prelimMaxPrice = d.High;
					}
				}

				float bottomDockReserve = (ShowSummary && FixSummaryBoxAtBottom)
					? (IsSummaryBoxMinimized ? 78f : 164f)
					: 46f;
				float topSessionBarOffset, canvasTopOffset, topBoxReserve;
				ComputeTopLayoutOffsets(out topSessionBarOffset, out canvasTopOffset, out topBoxReserve);
				float topSessionBarY = panelTop + topSessionBarOffset;
				float canvasTopY = panelTop + canvasTopOffset;
				float gridTopY = FixSessionHeaderAtTop ? topSessionBarY : canvasTopY;
				float usableTopY = panelTop + topBoxReserve;
				float usableBotY = Math.Max(usableTopY + 160f, panelBottom - bottomDockReserve);
				double prelimSpan = Math.Max(baseStep * 6.0, prelimMaxPrice - prelimMinPrice);
				float targetSpanPx = Math.Max(160f, usableBotY - usableTopY);

				double rawRowCount = prelimSpan / baseStep + 2.0;
				float rawPxPerRow = targetSpanPx / (float)Math.Max(1.0, rawRowCount);
				int groupMult = 1;
				if (AutoScaleTpoByScreenResolution)
				{
					groupMult = rawPxPerRow < MinReadableTpoRowPx
						? Math.Max(1, (int)Math.Ceiling(MinReadableTpoRowPx / Math.Max(0.4f, rawPxPerRow)))
						: 1;
					for (int i = 0; i < allDays.Count; i++)
					{
						DayProfile vd = allDays[i];
						if (vd.SlotRightX < panelLeft + 15f || vd.SlotLeftX > panelRight - 182f || vd.Rows.Count == 0)
							continue;
						int daySpanBins = vd.Rows.Keys.Max() - vd.Rows.Keys.Min() + 1;
						int reqDayMult = (int)Math.Ceiling(daySpanBins / Math.Max(12.0, targetSpanPx / MinReadableTpoRowPx));
						if (reqDayMult > groupMult)
							groupMult = reqDayMult;
					}
				}

				double step = baseStep * groupMult;
				if (groupMult > 1)
				{
					for (int i = 0; i < allDays.Count; i++)
						allDays[i] = BuildRenderDayProfile(allDays[i], baseStep, groupMult);
					allDays = BuildDayLayout(allDays, step, panelLeft, panelRight);
					if (allDays.Count == 0)
						return;
				}

				double visMinPrice = double.MaxValue;
				double visMaxPrice = double.MinValue;
				for (int i = 0; i < allDays.Count; i++)
				{
					DayProfile d = allDays[i];
					if (d.SlotRightX < panelLeft + 15f || d.SlotLeftX > panelRight - 182f || d.Rows.Count == 0)
						continue;
					double dStep = d.EffectiveStep > 0 ? d.EffectiveStep : step;
					double lowEdge = Math.Min(d.Low, d.Rows.Keys.Min() * dStep - dStep * 0.5);
					double highEdge = Math.Max(d.High, d.Rows.Keys.Max() * dStep + dStep * 0.5);
					if (lowEdge < visMinPrice) visMinPrice = lowEdge;
					if (highEdge > visMaxPrice) visMaxPrice = highEdge;
				}
				if (visMinPrice == double.MaxValue || visMaxPrice == double.MinValue)
				{
					int tailStart = Math.Max(0, allDays.Count - 3);
					for (int i = tailStart; i < allDays.Count; i++)
					{
						DayProfile d = allDays[i];
						if (d.Rows.Count == 0) continue;
						double dStep = d.EffectiveStep > 0 ? d.EffectiveStep : step;
						double lowEdge = Math.Min(d.Low, d.Rows.Keys.Min() * dStep - dStep * 0.5);
						double highEdge = Math.Max(d.High, d.Rows.Keys.Max() * dStep + dStep * 0.5);
						if (lowEdge < visMinPrice) visMinPrice = lowEdge;
						if (highEdge > visMaxPrice) visMaxPrice = highEdge;
					}
				}

				double safePriceSpan = Math.Max(step * 6.0, visMaxPrice - visMinPrice);
				double visSteps = Math.Max(6.0, safePriceSpan / step);
				float fitPxPerStep = targetSpanPx / (float)visSteps;
				float effectiveMinRowPx = AutoScaleTpoByScreenResolution ? MinReadableTpoRowPx : 3.5f;
				float sharedPxPerStep = Math.Min(MaxComfortableTpoRowPx, Math.Max(effectiveMinRowPx, fitPxPerStep));
				float sharedTotalSpanPx = Math.Min(targetSpanPx, (float)visSteps * sharedPxPerStep);
				float sharedMidY = 0.5f * (usableTopY + usableBotY);
				float sharedTopY = Math.Max(usableTopY, sharedMidY - sharedTotalSpanPx * 0.5f);
				float sharedBotY = Math.Min(usableBotY, sharedTopY + sharedTotalSpanPx);

				float testYHigh = (float)chartScale.GetYByValue(visMaxPrice);
				float testYLow = (float)chartScale.GetYByValue(visMinPrice);
				float testSpanPx = Math.Abs(testYLow - testYHigh);
				bool useFallbackYScale = testSpanPx < 1.0f
					|| float.IsNaN(testYHigh)
					|| float.IsInfinity(testYHigh)
					|| float.IsNaN(testYLow)
					|| float.IsInfinity(testYLow);

				Func<double, float> priceToY = (double price) =>
				{
					if (!useFallbackYScale)
						return (float)chartScale.GetYByValue(price);
					double norm = (visMaxPrice - price) / safePriceSpan;
					return (float)(sharedTopY + norm * (sharedBotY - sharedTopY));
				};

				for (int i = 0; i < allDays.Count; i++)
				{
					DayProfile d = allDays[i];
					double dStep = d.EffectiveStep > 0 ? d.EffectiveStep : step;
					int minB = d.Rows.Keys.Min();
					int maxB = d.Rows.Keys.Max();

					if (!d.AnalyticsValid || Math.Abs(d.AnalyticsStep - dStep) > 1e-9 || Math.Abs(d.AnalyticsVaPct - ValueAreaPercent) > 1e-9)
					{
						CalculateTpoValueArea(d, ValueAreaPercent, out d.PocBin, out d.VahBin, out d.ValBin);
						double maxV = 1e-9;
						double bestVol = -1.0;
						int bestVolBin = d.PocBin;
						int bestDist = int.MaxValue;
						foreach (KeyValuePair<int, PriceRow> kv in d.Rows)
						{
							double rv = kv.Value.Volume;
							if (rv > maxV) maxV = rv;
							int dist = Math.Abs(kv.Key - d.PocBin);
							if (rv > bestVol || (Math.Abs(rv - bestVol) <= 1e-9 && dist < bestDist))
							{
								bestVol = rv;
								bestVolBin = kv.Key;
								bestDist = dist;
							}
						}
						d.MaxRowVolume = maxV;
						d.VolumePocBin = bestVolBin;
						CalculateVolumeValueArea(d, ValueAreaPercent, d.VolumePocBin, out d.VolumeVahBin, out d.VolumeValBin);
						d.SinglePrintZones = ComputeSinglePrintZones(d, minB, maxB, d.PocBin);
						d.AnalyticsStep = dStep;
						d.AnalyticsVaPct = ValueAreaPercent;
						d.AnalyticsValid = true;
					}

					float rawHighY = priceToY(maxB * dStep + dStep * 0.5);
					float rawLowY = priceToY(minB * dStep - dStep * 0.5);
					if (rawHighY > rawLowY)
					{
						float sw = rawHighY;
						rawHighY = rawLowY;
						rawLowY = sw;
					}
					d.RenderTopY = rawHighY;
					d.RenderBotY = Math.Max(rawHighY + 4f, rawLowY);
				}

				DxBrush[] letterBrushes = new DxBrush[LetterColors.Length];
				DxBrush[] letterVaFillBrushes = new DxBrush[LetterColors.Length];
				DxBrush[] letterOutFillBrushes = new DxBrush[LetterColors.Length];
				DxBrush[] letterTileBorderBrushes = new DxBrush[LetterColors.Length];
				List<RightEdgeTag> rightEdgeTags = new List<RightEdgeTag>();
				bool modernUi = IsModernRichUi;
				AntialiasMode prevRenderAa = RenderTarget.AntialiasMode;
				SharpDX.Direct2D1.TextAntialiasMode prevTextAa = RenderTarget.TextAntialiasMode;

				try
				{
					if (modernUi)
					{
						RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;
						RenderTarget.TextAntialiasMode = SharpDX.Direct2D1.TextAntialiasMode.Cleartype;
					}

					for (int i = 0; i < LetterColors.Length; i++)
					{
						SharpDX.Color lc = LetterColors[i];
						letterBrushes[i] = new DxBrush(RenderTarget, lc);
						if (modernUi)
						{
							letterVaFillBrushes[i] = new DxBrush(RenderTarget, new SharpDX.Color((int)lc.R, (int)lc.G, (int)lc.B, 66));
							letterOutFillBrushes[i] = new DxBrush(RenderTarget, new SharpDX.Color((int)lc.R, (int)lc.G, (int)lc.B, 28));
							letterTileBorderBrushes[i] = new DxBrush(RenderTarget, new SharpDX.Color((int)lc.R, (int)lc.G, (int)lc.B, 118));
						}
					}

					var dashProps = new StrokeStyleProperties
					{
						DashStyle = SharpDX.Direct2D1.DashStyle.Custom,
						DashCap = modernUi ? CapStyle.Round : CapStyle.Flat,
						StartCap = modernUi ? CapStyle.Round : CapStyle.Flat,
						EndCap = modernUi ? CapStyle.Round : CapStyle.Flat
					};

					string uiFontName = modernUi ? "Segoe UI" : "Arial";
					var cardFontWeight = modernUi ? SharpDX.DirectWrite.FontWeight.SemiBold : SharpDX.DirectWrite.FontWeight.Normal;

					using (var dashedStroke = new StrokeStyle(RenderTarget.Factory, dashProps, new float[] { 4f, 3f }))
					using (var whiteBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(248, 250, 252) : new SharpDX.Color(238, 242, 245)))
					using (var textBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(203, 213, 225) : new SharpDX.Color(195, 204, 212)))
					using (var orangeBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(251, 191, 36) : new SharpDX.Color(245, 155, 35)))
					using (var yellowBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(253, 224, 71) : new SharpDX.Color(238, 202, 42)))
					using (var greenBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(52, 211, 153) : new SharpDX.Color(38, 224, 102)))
					using (var redBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(251, 113, 133) : new SharpDX.Color(248, 76, 82)))
					using (var cyanBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(56, 189, 248) : new SharpDX.Color(72, 198, 222)))
					using (var highExtremeBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(52, 211, 153) : new SharpDX.Color(38, 224, 102)))
					using (var lowExtremeBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(251, 113, 133) : new SharpDX.Color(248, 76, 82)))
					using (var highSwingLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(38, 224, 102, 185)))
					using (var lowSwingLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(248, 76, 82, 185)))
					using (var highExtremeBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(52, 211, 153, 210)))
					using (var lowExtremeBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(251, 113, 133, 210)))
					using (var daySepBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(56, 189, 248, 92) : new SharpDX.Color(62, 78, 96)))
					using (var spineLineBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(100, 116, 139, 135) : new SharpDX.Color(85, 96, 108)))
					using (var vaFillBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(14, 116, 144, 56) : new SharpDX.Color(16, 48, 66, 130)))
					using (var vaBorderBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(56, 189, 248, 232) : new SharpDX.Color(58, 138, 178)))
					using (var vaVirginBrush = new DxBrush(RenderTarget, new SharpDX.Color(88, 204, 248)))
					using (var singleBuyRowBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(16, 185, 129, 128) : new SharpDX.Color(16, 192, 98, 132)))
					using (var singleSellRowBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(244, 63, 94, 128) : new SharpDX.Color(226, 42, 60, 132)))
					using (var singleBuyZoneRayBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(16, 185, 129, 68) : new SharpDX.Color(16, 192, 98, 82)))
					using (var singleSellZoneRayBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(244, 63, 94, 68) : new SharpDX.Color(226, 42, 60, 82)))
					using (var singleBuyBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(48, 246, 128, 250)))
					using (var singleSellBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(255, 86, 98, 250)))
					using (var spBuyPillBgBrush = new DxBrush(RenderTarget, new SharpDX.Color(6, 48, 32, 248)))
					using (var spSellPillBgBrush = new DxBrush(RenderTarget, new SharpDX.Color(60, 14, 26, 248)))
					using (var btnBgBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(19, 30, 48, 248) : new SharpDX.Color(22, 34, 48, 248)))
					using (var btnActiveBgBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(12, 74, 110, 250) : new SharpDX.Color(18, 78, 92, 250)))
					using (var volumeOutBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(71, 85, 105, 182) : new SharpDX.Color(76, 86, 96)))
					using (var volumeVaBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(14, 165, 233, 232) : new SharpDX.Color(198, 26, 90)))
					using (var volumePocBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(251, 191, 36, 255) : new SharpDX.Color(246, 132, 178)))
					using (var splitVolVaBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(20, 184, 166, 218) : new SharpDX.Color(98, 198, 184, 195)))
					using (var splitVolOutBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(71, 85, 105, 175) : new SharpDX.Color(68, 82, 92, 175)))
					using (var splitVolPocBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(251, 191, 36, 248) : new SharpDX.Color(198, 248, 232, 235)))
					using (var pocLineBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(245, 158, 11) : new SharpDX.Color(228, 152, 28)))
					using (var vppocLineBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(250, 204, 21) : new SharpDX.Color(235, 200, 45)))
					using (var midLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(218, 128, 255)))
					using (var ibBarBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(52, 211, 153) : new SharpDX.Color(32, 228, 88)))
					using (var ibGlowBrush = new DxBrush(RenderTarget, new SharpDX.Color(16, 185, 129, 72)))
					using (var pocRowFillBrush = new DxBrush(RenderTarget, new SharpDX.Color(245, 158, 11, 52)))
					using (var pocRowBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(251, 191, 36, 168)))
					using (var openBadgeFillBrush = new DxBrush(RenderTarget, new SharpDX.Color(5, 150, 105, 238)))
					using (var openBadgeBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(110, 231, 183, 246)))
					using (var closeBadgeFillBrush = new DxBrush(RenderTarget, new SharpDX.Color(225, 29, 72, 238)))
					using (var closeBadgeBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(253, 164, 175, 246)))
					using (var sessionGlassBrush = new DxBrush(RenderTarget, new SharpDX.Color(15, 23, 42, 84)))
					using (var sessionGlassBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(148, 163, 184, 30)))
					using (var studioGridBrush = new DxBrush(RenderTarget, new SharpDX.Color(148, 163, 184, 16)))
					using (var cardBgBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(10, 16, 28, 246) : new SharpDX.Color(9, 14, 20, 246)))
					using (var pillBgBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(13, 21, 36, 248) : new SharpDX.Color(10, 15, 22, 248)))
					using (var cardBorderBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(56, 189, 248, 88) : new SharpDX.Color(38, 54, 70, 205)))
					using (var dateBannerBgBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(8, 14, 25, 252) : new SharpDX.Color(12, 20, 30, 255)))
					using (var cardFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, uiFontName, cardFontWeight, SharpDX.DirectWrite.FontStyle.Normal, modernUi ? 10.2f : 10.0f))
					using (var tagCenterFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, uiFontName, SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, modernUi ? 9.6f : 9.5f))
					using (var spTagFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, uiFontName, SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, modernUi ? 9.0f : 8.8f))
					using (var headerFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, uiFontName, SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, modernUi ? 10.6f : 10.5f))
					using (var dateAxisFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, uiFontName, SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 11.0f))
					using (var labelRightFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, uiFontName, SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, modernUi ? 9.2f : 9.0f))
					{
						cardFormat.TextAlignment = TextAlignment.Leading;
						cardFormat.ParagraphAlignment = ParagraphAlignment.Center;
						cardFormat.WordWrapping = WordWrapping.NoWrap;

						tagCenterFormat.TextAlignment = TextAlignment.Center;
						tagCenterFormat.ParagraphAlignment = ParagraphAlignment.Center;
						tagCenterFormat.WordWrapping = WordWrapping.NoWrap;

						spTagFormat.TextAlignment = TextAlignment.Leading;
						spTagFormat.ParagraphAlignment = ParagraphAlignment.Center;
						spTagFormat.WordWrapping = WordWrapping.NoWrap;

						if (modernUi)
						{
							DrawModernStudioBackdrop(panelLeft, panelTop, panelRight, panelBottom, canvasTopY, 186f, studioGridBrush, spTagFormat, cyanBrush);
						}

						headerFormat.TextAlignment = TextAlignment.Leading;
						headerFormat.ParagraphAlignment = ParagraphAlignment.Center;
						headerFormat.WordWrapping = WordWrapping.NoWrap;

						dateAxisFormat.TextAlignment = TextAlignment.Center;
						dateAxisFormat.ParagraphAlignment = ParagraphAlignment.Center;
						dateAxisFormat.WordWrapping = WordWrapping.NoWrap;

						labelRightFormat.TextAlignment = TextAlignment.Center;
						labelRightFormat.ParagraphAlignment = ParagraphAlignment.Center;
						labelRightFormat.WordWrapping = WordWrapping.NoWrap;

						List<double> allIbRanges = allDays
							.Where(d => d.InitialBalanceHigh > d.InitialBalanceLow && d.InitialBalanceLow != double.MaxValue)
							.Select(d => d.InitialBalanceHigh - d.InitialBalanceLow)
							.ToList();
						double avgIbRange = allIbRanges.Count > 0 ? allIbRanges.Average() : step * 6;

						// =========================================================================
						// PASS 1: Separators, Virgin Single Print Rays & Virgin Extending Lines
						// (STRICT TPO-CLOSE VIRGINITY RULE: Virginity breaks ONLY when a closed TPO closes above/below the line!)
						// =========================================================================
						DateTime refNow = GetReferenceNow(rawAllDays);
						double breakEps = GetVirginBreakEpsilon();

						// 1A. Closed Monday–Friday Weekly High & Weekly Low Virgin Lines (never on running week)
						if ((ShowWeeklyHigh || ShowWeeklyLow) && rawAllDays.Count > 0)
						{
							Dictionary<DateTime, List<int>> weekMap = new Dictionary<DateTime, List<int>>();
							List<DateTime> orderedWeeks = new List<DateTime>();
							for (int rIdx = 0; rIdx < rawAllDays.Count; rIdx++)
							{
								DayProfile rd = rawAllDays[rIdx];
								if (rd.SessionDate.DayOfWeek < DayOfWeek.Monday || rd.SessionDate.DayOfWeek > DayOfWeek.Friday)
									continue;
								DateTime wkMon = GetMondayOfWeek(rd.SessionDate);
								List<int> wList;
								if (!weekMap.TryGetValue(wkMon, out wList))
								{
									wList = new List<int>();
									weekMap[wkMon] = wList;
									orderedWeeks.Add(wkMon);
								}
								wList.Add(rIdx);
							}
							orderedWeeks.Sort();

							for (int wIdx = 0; wIdx < orderedWeeks.Count; wIdx++)
							{
								DateTime wkMon = orderedWeeks[wIdx];
								DateTime wkFri = wkMon.AddDays(4);
								List<int> wIndices = weekMap[wkMon];
								if (wIndices.Count == 0)
									continue;

								if (wIdx == 0 && orderedWeeks.Count > 1 && rawAllDays[wIndices[0]].SessionDate.Date > wkMon.AddDays(1))
									continue;

								bool anyRunningInWeek = false;
								double wkHigh = double.MinValue;
								double wkLow = double.MaxValue;
								int highRawIdx = wIndices[0];
								int lowRawIdx = wIndices[0];
								for (int k = 0; k < wIndices.Count; k++)
								{
									int rIdx = wIndices[k];
									DayProfile rd = rawAllDays[rIdx];
									if (IsRawDayRunning(rd, rIdx == rawAllDays.Count - 1, refNow))
										anyRunningInWeek = true;
									if (rd.High > wkHigh)
									{
										wkHigh = rd.High;
										highRawIdx = rIdx;
									}
									if (rd.Low < wkLow)
									{
										wkLow = rd.Low;
										lowRawIdx = rIdx;
									}
								}

								bool isWeekClosed = !anyRunningInWeek && (refNow >= GetTradingDayCloseTime(wkFri));
								if (!isWeekClosed)
									continue;

								int lastWkRawIdx = wIndices[wIndices.Count - 1];
								string wkLabel = FormatWeekRangeLabel(wkMon, wkFri);

								if (ShowWeeklyHigh && wkHigh > double.MinValue)
								{
									DayProfile highCol = FindRenderedDayForRaw(allDays, rawAllDays[highRawIdx]);
									if (highCol == null || highCol.IbX <= panelRight - 182f)
									{
										float rawOriginX = highCol != null ? highCol.IbX : (panelLeft - 40f);
										float rawOriginEndX = highCol != null ? highCol.CollapsedRightX : (panelLeft - 20f);
										int whBin = PriceToBin(wkHigh, step);
										float yWkHigh = priceToY(whBin * step + step * 0.5);
										if (yWkHigh >= canvasTopY && yWkHigh <= panelBottom - 25f)
										{
											float wkHighEndX;
											bool isWkHighVirgin = GetRawLevelExtensionX(
												rawAllDays, lastWkRawIdx, allDays,
												wkHigh, rawOriginEndX, panelLeft, panelRight,
												refNow, breakEps, LevelBreakMode.CloseAbove, out wkHighEndX);
											float drawStartX = Math.Max(panelLeft + 2f, rawOriginX);
											if (wkHighEndX > drawStartX + 1.5f)
												DrawHairlineHorizontal(drawStartX, wkHighEndX, yWkHigh, highExtremeBrush, 1.30f);

											if (isWkHighVirgin)
											{
												rightEdgeTags.Add(new RightEdgeTag
												{
													Price = wkHigh,
													IdealY = yWkHigh - 7.5f,
													Height = 15f,
													Text = "WeeklyHigh " + FormatPrice(wkHigh) + " (" + wkLabel + ")",
													Brush = highExtremeBrush,
													BorderBrush = highExtremeBorderBrush,
													Priority = 5
												});
											}
										}
									}
								}

								if (ShowWeeklyLow && wkLow < double.MaxValue)
								{
									DayProfile lowCol = FindRenderedDayForRaw(allDays, rawAllDays[lowRawIdx]);
									if (lowCol == null || lowCol.IbX <= panelRight - 182f)
									{
										float rawOriginX = lowCol != null ? lowCol.IbX : (panelLeft - 40f);
										float rawOriginEndX = lowCol != null ? lowCol.CollapsedRightX : (panelLeft - 20f);
										int wlBin = PriceToBin(wkLow, step);
										float yWkLow = priceToY(wlBin * step - step * 0.5);
										if (yWkLow >= canvasTopY && yWkLow <= panelBottom - 25f)
										{
											float wkLowEndX;
											bool isWkLowVirgin = GetRawLevelExtensionX(
												rawAllDays, lastWkRawIdx, allDays,
												wkLow, rawOriginEndX, panelLeft, panelRight,
												refNow, breakEps, LevelBreakMode.CloseBelow, out wkLowEndX);
											float drawStartX = Math.Max(panelLeft + 2f, rawOriginX);
											if (wkLowEndX > drawStartX + 1.5f)
												DrawHairlineHorizontal(drawStartX, wkLowEndX, yWkLow, lowExtremeBrush, 1.30f);

											if (isWkLowVirgin)
											{
												rightEdgeTags.Add(new RightEdgeTag
												{
													Price = wkLow,
													IdealY = yWkLow - 7.5f,
													Height = 15f,
													Text = "WeeklyLow " + FormatPrice(wkLow) + " (" + wkLabel + ")",
													Brush = lowExtremeBrush,
													BorderBrush = lowExtremeBorderBrush,
													Priority = 5
												});
											}
										}
									}
								}
							}
						}

						// 1B. Closed Day DayHigh & DayLow Virgin Lines (strictly closed days only — never on running day)
						if ((ShowSwingHigh || ShowSwingLow) && rawAllDays.Count > 0)
						{
							for (int rIdx = 0; rIdx < rawAllDays.Count; rIdx++)
							{
								DayProfile rd = rawAllDays[rIdx];
								bool isRunningRaw = IsRawDayRunning(rd, rIdx == rawAllDays.Count - 1, refNow);
								bool isDayClosed = !isRunningRaw && (refNow >= GetTradingDayCloseTime(rd.SessionDate));
								if (!isDayClosed)
									continue;

								DayProfile dayCol = FindRenderedDayForRaw(allDays, rd);
								if (dayCol != null && dayCol.IbX > panelRight - 182f)
									continue;

								float rawOriginX = dayCol != null ? dayCol.IbX : (panelLeft - 40f);
								float rawOriginEndX = dayCol != null ? dayCol.CollapsedRightX : (panelLeft - 20f);
								string dayDateLbl = rd.SessionDate.ToString("dd MMM", CultureInfo.InvariantCulture);

								if (ShowSwingHigh && rd.High > double.MinValue)
								{
									int dhBin = PriceToBin(rd.High, step);
									float yDayHigh = priceToY(dhBin * step + step * 0.5);
									if (yDayHigh >= canvasTopY && yDayHigh <= panelBottom - 25f)
									{
										float dHighEndX;
										bool isDayHighVirgin = GetRawLevelExtensionX(
											rawAllDays, rIdx, allDays,
											rd.High, rawOriginEndX, panelLeft, panelRight,
											refNow, breakEps, LevelBreakMode.CloseAbove, out dHighEndX);
										float drawStartX = Math.Max(panelLeft + 2f, rawOriginX);
										if (dHighEndX > drawStartX + 1.5f)
											DrawHairlineHorizontal(drawStartX, dHighEndX, yDayHigh, highSwingLineBrush, 0.80f);

										if (isDayHighVirgin)
										{
											rightEdgeTags.Add(new RightEdgeTag
											{
												Price = rd.High,
												IdealY = yDayHigh - 7.5f,
												Height = 15f,
												Text = "DayHigh " + FormatPrice(rd.High) + " (" + dayDateLbl + ")",
												Brush = highExtremeBrush,
												Priority = 3
											});
										}
									}
								}

								if (ShowSwingLow && rd.Low < double.MaxValue)
								{
									int dlBin = PriceToBin(rd.Low, step);
									float yDayLow = priceToY(dlBin * step - step * 0.5);
									if (yDayLow >= canvasTopY && yDayLow <= panelBottom - 25f)
									{
										float dLowEndX;
										bool isDayLowVirgin = GetRawLevelExtensionX(
											rawAllDays, rIdx, allDays,
											rd.Low, rawOriginEndX, panelLeft, panelRight,
											refNow, breakEps, LevelBreakMode.CloseBelow, out dLowEndX);
										float drawStartX = Math.Max(panelLeft + 2f, rawOriginX);
										if (dLowEndX > drawStartX + 1.5f)
											DrawHairlineHorizontal(drawStartX, dLowEndX, yDayLow, lowSwingLineBrush, 0.80f);

										if (isDayLowVirgin)
										{
											rightEdgeTags.Add(new RightEdgeTag
											{
												Price = rd.Low,
												IdealY = yDayLow - 7.5f,
												Height = 15f,
												Text = "DayLow " + FormatPrice(rd.Low) + " (" + dayDateLbl + ")",
												Brush = lowExtremeBrush,
												Priority = 3
											});
										}
									}
								}
							}
						}

						// 1C. Day Separators, Single Prints, PoorHigh/Low, VAH/VAL, VPoC/VPPoC & MidLine
						for (int i = 0; i < allDays.Count; i++)
						{
							DayProfile day = allDays[i];
							bool isRunningDay = IsDayRunning(day, i == allDays.Count - 1);
							double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;

							float xSlotLeft = day.SlotLeftX;
							float xSlotRight = day.SlotRightX;
							float xIb = day.IbX;
							float xCollapsedRight = day.CollapsedRightX;

							if (xSlotLeft > panelRight - 182f)
								continue;

							if (xSlotRight >= panelLeft + 8f)
							{
								if (xSlotLeft >= panelLeft && xSlotLeft <= panelRight - 182f)
									RenderTarget.DrawLine(new Vector2(xSlotLeft, gridTopY), new Vector2(xSlotLeft, panelBottom), daySepBrush, 1.6f);
								if (xSlotRight >= panelLeft && xSlotRight <= panelRight - 182f)
									RenderTarget.DrawLine(new Vector2(xSlotRight, gridTopY), new Vector2(xSlotRight, panelBottom), daySepBrush, 1.6f);
							}

							int maxBin = day.Rows.Keys.Max();
							int minBin = day.Rows.Keys.Min();
							float profileHighY = SessionPriceToY(day, maxBin * dStep + dStep * 0.5, step);
							float profileLowY = SessionPriceToY(day, minBin * dStep - dStep * 0.5, step);
							if (profileHighY > profileLowY)
							{
								float swapY = profileHighY;
								profileHighY = profileLowY;
								profileLowY = swapY;
							}

							string dateLabel = day.SessionDate.ToString("dd MMM", CultureInfo.InvariantCulture);

							// 1. Extending Single Print Zones (TPO Close Rule: Buying SP breaks on TPO close < bottom SL; Selling SP breaks on TPO close > top SL)
							if (ShowSinglePrints)
							{
								for (int z = 0; z < day.SinglePrintZones.Count; z++)
								{
									SinglePrintZone spZone = day.SinglePrintZones[z];
									double spSlPrice = GetSinglePrintStopLossPrice(spZone, dStep);
									float ySpTop = SessionPriceToY(day, spZone.EndBin * dStep + dStep * 0.5, step);
									float ySpBot = SessionPriceToY(day, spZone.StartBin * dStep - dStep * 0.5, step);
									if (ySpTop > ySpBot)
									{
										float sw = ySpTop;
										ySpTop = ySpBot;
										ySpBot = sw;
									}
									float ySpSlLine = spZone.IsBuying ? ySpBot : ySpTop;
									float ySpOppLine = spZone.IsBuying ? ySpTop : ySpBot;

									float drawEndX;
									bool isSpVirgin = GetLevelExtensionX(
										allDays,
										i,
										spSlPrice,
										xCollapsedRight,
										panelRight,
										refNow,
										breakEps,
										spZone.IsBuying ? LevelBreakMode.CloseBelow : LevelBreakMode.CloseAbove,
										out drawEndX);

									if (ySpBot >= canvasTopY && ySpTop <= panelBottom - 25f)
									{
										float drawStartX = Math.Max(panelLeft + 2f, xIb);
										if (drawEndX > drawStartX + 2f)
										{
											DxBrush spBorder = spZone.IsBuying ? singleBuyBorderBrush : singleSellBorderBrush;
											float clRayTop = Math.Max(canvasTopY, ySpTop);
											float clRayBot = Math.Min(panelBottom - 25f, ySpBot);
											if (clRayBot > clRayTop)
											{
												RectangleF spRayRect = new RectangleF(drawStartX, clRayTop, drawEndX - drawStartX, Math.Max(2f, clRayBot - clRayTop));
												RenderTarget.FillRectangle(spRayRect, spZone.IsBuying ? singleBuyZoneRayBrush : singleSellZoneRayBrush);
											}
											if (ySpOppLine >= canvasTopY && ySpOppLine <= panelBottom - 25f)
											{
												RenderTarget.DrawLine(
													new Vector2(drawStartX, ySpOppLine),
													new Vector2(drawEndX, ySpOppLine),
													spBorder,
													1.05f,
													dashedStroke);
											}
											// Bold SL Trigger Line: Bottom line for Buying Single Print, Top line for Selling Single Print
											if (ySpSlLine >= canvasTopY && ySpSlLine <= panelBottom - 25f)
											{
												RenderTarget.DrawLine(
													new Vector2(drawStartX, ySpSlLine),
													new Vector2(drawEndX, ySpSlLine),
													spBorder,
													1.85f);
											}
										}

										if (isSpVirgin && ySpSlLine >= canvasTopY && ySpSlLine <= panelBottom - 25f)
										{
											string spType = spZone.IsBuying ? "Buying SP" : "Selling SP";
											rightEdgeTags.Add(new RightEdgeTag
											{
												Price = spSlPrice,
												IdealY = ySpSlLine - 7.5f,
												Height = 15f,
												Text = spType + " " + FormatPrice(spSlPrice) + " (" + dateLabel + ")",
												Brush = spZone.IsBuying ? singleBuyBorderBrush : singleSellBorderBrush,
												FillBrush = spZone.IsBuying ? spBuyPillBgBrush : spSellPillBgBrush,
												BorderBrush = spZone.IsBuying ? singleBuyBorderBrush : singleSellBorderBrush,
												Priority = 3
											});
										}
									}
								}
							}

							// 2. Extending PoorHigh / ABPoorHigh & PoorLow / ABPoorLow Levels (Closed days only; DayHigh/DayLow handled in 1B)
							PriceRow topRow = day.Rows[maxBin];
							bool isAbPoorHigh = topRow.Periods.Contains(0) && topRow.Periods.Contains(1);
							bool isPoorHigh = !isAbPoorHigh && topRow.Periods.Count >= 2;
							bool enablePoorHigh = !isRunningDay && ShowPoorHighLow && ShowPoorHigh && (isAbPoorHigh || isPoorHigh);

							PriceRow botRow = day.Rows[minBin];
							bool isAbPoorLow = botRow.Periods.Contains(0) && botRow.Periods.Contains(1);
							bool isPoorLow = !isAbPoorLow && botRow.Periods.Count >= 2;
							bool enablePoorLow = !isRunningDay && ShowPoorHighLow && ShowPoorLow && (isAbPoorLow || isPoorLow);

							if (enablePoorHigh && profileHighY >= canvasTopY && profileHighY <= panelBottom - 25f)
							{
								string highLevelName = isAbPoorHigh ? "ABPoorHigh" : "PoorHigh";
								float drawEndX;
								bool isHighVirgin = GetLevelExtensionX(allDays, i, day.High, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CloseAbove, out drawEndX);
								float drawStartX = Math.Max(panelLeft + 2f, xIb);
								if (drawEndX > drawStartX + 1.5f)
									DrawHairlineHorizontal(drawStartX, drawEndX, profileHighY, highExtremeBrush, 0.70f, dashedStroke);

								if (isHighVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = day.High,
										IdealY = profileHighY - 7.5f,
										Height = 15f,
										Text = highLevelName + " " + FormatPrice(day.High) + " (" + dateLabel + ")",
										Brush = highExtremeBrush,
										BorderBrush = highExtremeBorderBrush,
										Priority = 4
									});
								}
							}

							if (enablePoorLow && profileLowY >= canvasTopY && profileLowY <= panelBottom - 25f)
							{
								string lowLevelName = isAbPoorLow ? "ABPoorLow" : "PoorLow";
								float drawEndX;
								bool isLowVirgin = GetLevelExtensionX(allDays, i, day.Low, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CloseBelow, out drawEndX);
								float drawStartX = Math.Max(panelLeft + 2f, xIb);
								if (drawEndX > drawStartX + 1.5f)
									DrawHairlineHorizontal(drawStartX, drawEndX, profileLowY, lowExtremeBrush, 0.70f, dashedStroke);

								if (isLowVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = day.Low,
										IdealY = profileLowY - 7.5f,
										Height = 15f,
										Text = lowLevelName + " " + FormatPrice(day.Low) + " (" + dateLabel + ")",
										Brush = lowExtremeBrush,
										BorderBrush = lowExtremeBorderBrush,
										Priority = 4
									});
								}
							}

							// 3. Extending VAH & VAL (completely hidden on all past & current days when unchecked)
							double vahPrice = day.VahBin * dStep;
							double valPrice = day.ValBin * dStep;
							float yVahLine = SessionPriceToY(day, day.VahBin * dStep + dStep * 0.5, step);
							float yValLine = SessionPriceToY(day, day.ValBin * dStep - dStep * 0.5, step);
							bool allowVahExt = ShowVirginVAH && (!isRunningDay || (EnableRunningDayLevels && ShowRunningDayVAH));
							bool allowValExt = ShowVirginVAL && (!isRunningDay || (EnableRunningDayLevels && ShowRunningDayVAL));

							if (allowVahExt && yVahLine >= canvasTopY && yVahLine <= panelBottom - 25f)
							{
								float vahDrawEndX;
								bool isVahVirgin = GetLevelExtensionX(allDays, i, vahPrice, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CrossClose, out vahDrawEndX);
								float vahStartX = Math.Max(panelLeft + 2f, xCollapsedRight);
								if (vahDrawEndX > vahStartX + 1f)
								{
									RenderTarget.DrawLine(
										new Vector2(vahStartX, yVahLine),
										new Vector2(vahDrawEndX, yVahLine),
										vaVirginBrush, 1.05f, dashedStroke);
								}
								if (isVahVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = vahPrice,
										IdealY = yVahLine - 7.5f,
										Height = 15f,
										Text = "ValueAreaHigh " + FormatPrice(vahPrice) + " (" + dateLabel + ")",
										Brush = vaVirginBrush,
										Priority = 2
									});
								}
							}

							if (allowValExt && yValLine >= canvasTopY && yValLine <= panelBottom - 25f)
							{
								float valDrawEndX;
								bool isValVirgin = GetLevelExtensionX(allDays, i, valPrice, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CrossClose, out valDrawEndX);
								float valStartX = Math.Max(panelLeft + 2f, xCollapsedRight);
								if (valDrawEndX > valStartX + 1f)
								{
									RenderTarget.DrawLine(
										new Vector2(valStartX, yValLine),
										new Vector2(valDrawEndX, yValLine),
										vaVirginBrush, 1.05f, dashedStroke);
								}
								if (isValVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = valPrice,
										IdealY = yValLine - 7.5f,
										Height = 15f,
										Text = "ValueAreaLow " + FormatPrice(valPrice) + " (" + dateLabel + ")",
										Brush = vaVirginBrush,
										Priority = 2
									});
								}
							}

							// 4. Extending VPoC (POC) & VPPoC (PPoC) (completely hidden on all past & current days when unchecked)
							double vpocPrice = day.VolumePocBin * dStep;
							double tpoPocPrice = day.PocBin * dStep;
							float yVpoc = SessionPriceToY(day, vpocPrice, step);
							float yTpoPoc = SessionPriceToY(day, tpoPocPrice, step);
							bool allowVpoc = ShowVirginVPoC && (!isRunningDay || (EnableRunningDayLevels && ShowRunningDayPOC));
							bool allowVppoc = ShowVirginVPPoC && (!isRunningDay || (EnableRunningDayLevels && ShowRunningDayPPOC));

							if (allowVpoc && yVpoc >= canvasTopY && yVpoc <= panelBottom - 25f)
							{
								float vpocDrawEndX;
								bool isVpocVirgin = GetLevelExtensionX(allDays, i, vpocPrice, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CrossClose, out vpocDrawEndX);
								float vpocStartX = Math.Max(panelLeft + 2f, xIb);
								if (vpocDrawEndX > vpocStartX + 1f)
								{
									RenderTarget.DrawLine(
										new Vector2(vpocStartX, yVpoc),
										new Vector2(vpocDrawEndX, yVpoc),
										pocLineBrush, 1.3f);
								}
								if (isVpocVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = vpocPrice,
										IdealY = yVpoc - 7.5f,
										Height = 15f,
										Text = "VPoC " + FormatPrice(vpocPrice) + " (" + dateLabel + ")",
										Brush = orangeBrush,
										Priority = 3
									});
								}
							}

							if (allowVppoc && (day.PocBin != day.VolumePocBin || !allowVpoc) && yTpoPoc >= canvasTopY && yTpoPoc <= panelBottom - 25f)
							{
								float tpoDrawEndX;
								bool isVppocVirgin = GetLevelExtensionX(allDays, i, tpoPocPrice, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CrossClose, out tpoDrawEndX);
								float vppocStartX = Math.Max(panelLeft + 2f, xIb);
								if (tpoDrawEndX > vppocStartX + 1f)
								{
									RenderTarget.DrawLine(
										new Vector2(vppocStartX, yTpoPoc),
										new Vector2(tpoDrawEndX, yTpoPoc),
										vppocLineBrush, 1.0f, dashedStroke);
								}
								if (isVppocVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = tpoPocPrice,
										IdealY = yTpoPoc - 7.5f,
										Height = 15f,
										Text = "VPPoC " + FormatPrice(tpoPocPrice) + " (" + dateLabel + ")",
										Brush = yellowBrush,
										Priority = 2
									});
								}
							}

							// 5. Session Midline & Extending Virgin Midline (completely hidden on all past & current days when ShowVirginMidLine is unchecked)
							double midPrice = (day.High + day.Low) * 0.5;
							float yMid = (profileHighY + profileLowY) * 0.5f;
							if (ShowVirginMidLine && !isRunningDay && yMid >= canvasTopY && yMid <= panelBottom - 25f)
							{
								float midDrawEndX;
								bool isMidVirgin = GetLevelExtensionX(allDays, i, midPrice, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CrossClose, out midDrawEndX);
								float midStartX = Math.Max(panelLeft + 2f, xIb);
								if (midDrawEndX > midStartX + 1f)
								{
									RenderTarget.DrawLine(
										new Vector2(midStartX, yMid),
										new Vector2(midDrawEndX, yMid),
										midLineBrush, 1.1f, dashedStroke);
								}
								if (isMidVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = midPrice,
										IdealY = yMid - 7.5f,
										Height = 15f,
										Text = "MidLine " + FormatPrice(midPrice) + " (" + dateLabel + ")",
										Brush = midLineBrush,
										Priority = 2
									});
								}
							}
						}

						// =========================================================================
						// PASS 2: Foreground Profiles, TPO Letters, Volume Profile, Headers, Summary
						// =========================================================================
						float clipLeft = panelLeft + 2f;
						float clipRight = Math.Max(clipLeft + 80f, panelRight - 182f);
						RenderTarget.PushAxisAlignedClip(new RectangleF(clipLeft, panelTop, clipRight - clipLeft, panelBottom - panelTop), AntialiasMode.PerPrimitive);
						try
						{
						const float bottomBannerH = 26f;
						float fullBannerStripW = Math.Max(40f, (panelRight - 182f) - (panelLeft + 2f));
						RenderTarget.FillRectangle(new RectangleF(panelLeft + 2f, panelBottom - (bottomBannerH + 1f), fullBannerStripW, bottomBannerH), dateBannerBgBrush);

						if (FixSessionHeaderAtTop)
						{
							RectangleF topSessionStripRect = new RectangleF(panelLeft + 2f, topSessionBarY, fullBannerStripW, 35f);
							RenderTarget.FillRectangle(topSessionStripRect, dateBannerBgBrush);
							RenderTarget.DrawRectangle(topSessionStripRect, cardBorderBrush, 1.0f);

							for (int td = 0; td < allDays.Count; td++)
							{
								DayProfile tDay = allDays[td];
								if (tDay.SlotRightX < panelLeft || tDay.SlotLeftX > panelRight - 182f)
									continue;
								if (tDay.SlotLeftX >= panelLeft + 2f && tDay.SlotLeftX <= panelRight - 182f)
									RenderTarget.DrawLine(new Vector2(tDay.SlotLeftX, topSessionBarY), new Vector2(tDay.SlotLeftX, topSessionBarY + 35f), daySepBrush, 1.6f);
								if (tDay.SlotRightX >= panelLeft + 2f && tDay.SlotRightX <= panelRight - 182f)
									RenderTarget.DrawLine(new Vector2(tDay.SlotRightX, topSessionBarY), new Vector2(tDay.SlotRightX, topSessionBarY + 35f), daySepBrush, 1.6f);
							}
						}

						for (int i = 0; i < allDays.Count; i++)
						{
							DayProfile day = allDays[i];
							DayProfile prevDay = i > 0 ? allDays[i - 1] : null;
							bool isLatestDay = (i == allDays.Count - 1);
							bool isRunningDay = IsDayRunning(day, isLatestDay);
							double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;

							float xSlotLeft = day.SlotLeftX;
							float xSlotRight = day.SlotRightX;
							float slotWidth = xSlotRight - xSlotLeft;
							float xIb = day.IbX;
							float xCollapsedRight = day.CollapsedRightX;

							if (xSlotRight < panelLeft + 8f || xSlotLeft > panelRight - 182f)
								continue;

							// Bottom Date Banner (protected from NinjaTrader's bottom-left watermark & clipped cleanly)
							float dateBannerY = panelBottom - (bottomBannerH + 1f);
							float clampedBannerLeft = Math.Max(panelLeft + 4f, day.SlotLeftX + 1f);
							float clampedBannerRight = Math.Min(panelRight - 182f, day.SlotRightX - 1f);
							float bannerW = clampedBannerRight - clampedBannerLeft;
							if (bannerW > 54f)
							{
								RectangleF dateBannerRect = new RectangleF(clampedBannerLeft, dateBannerY, bannerW, bottomBannerH);
								RenderTarget.FillRectangle(dateBannerRect, dateBannerBgBrush);
								RenderTarget.DrawRectangle(dateBannerRect, cardBorderBrush, 1.0f);

								float safeTextLeft = Math.Max(clampedBannerLeft + 6f, panelLeft + 140f);
								float textAvailW = (clampedBannerRight - 6f) - safeTextLeft;
								if (textAvailW >= 42f)
								{
									int expTpos = Math.Max(1, day.ExpectedPeriodCount);
									int prnTpos = Math.Min(expTpos, Math.Max(day.PeriodSlices.Count, day.LastPeriod + 1));
									bool isLive = isRunningDay;
									string tpoPart = isLive ? string.Format(CultureInfo.InvariantCulture, "LIVE: {0}/{1} TPOs", prnTpos, expTpos) : string.Format(CultureInfo.InvariantCulture, "{0}/{1} TPOs", prnTpos, expTpos);
									string dateBannerText;
									if (EffectiveProfileMode == MarketProfilePeriodMode.Weekly)
									{
										dateBannerText = textAvailW >= 320f
											? string.Format(CultureInfo.InvariantCulture, "{0}  [ Weekly Profile — {1} ]", day.SessionName, tpoPart)
											: (textAvailW >= 165f
												? string.Format(CultureInfo.InvariantCulture, "{0}  [ {1}/{2} TPO ]", day.SessionName, prnTpos, expTpos)
												: day.SessionName);
									}
									else if (EffectiveProfileMode == MarketProfilePeriodMode.Monthly)
									{
										dateBannerText = textAvailW >= 320f
											? string.Format(CultureInfo.InvariantCulture, "Month: {0}  [ Monthly Profile — {1} ]", day.SessionDate.ToString("MMMM yyyy", CultureInfo.InvariantCulture), tpoPart)
											: (textAvailW >= 150f
												? string.Format(CultureInfo.InvariantCulture, "{0}  [ {1}/{2} TPOs ]", day.SessionName, prnTpos, expTpos)
												: day.SessionName);
									}
									else
									{
										dateBannerText = textAvailW >= 290f
											? string.Format(CultureInfo.InvariantCulture, "Date: {0} — Indian Day Profile ({1})", day.SessionDate.ToString("dd MMM yyyy (ddd)", CultureInfo.InvariantCulture), tpoPart)
											: (textAvailW >= 165f
												? string.Format(CultureInfo.InvariantCulture, "{0} ({1})", day.SessionDate.ToString("dd MMM yyyy (ddd)", CultureInfo.InvariantCulture), tpoPart)
												: day.SessionDate.ToString(textAvailW >= 90f ? "dd MMM (ddd)" : "dd MMM", CultureInfo.InvariantCulture));
									}

									RectangleF textClipRect = new RectangleF(safeTextLeft, dateBannerY + 2f, textAvailW, bottomBannerH - 4f);
									RenderTarget.PushAxisAlignedClip(textClipRect, AntialiasMode.PerPrimitive);
									try
									{
										DrawText(dateBannerText, safeTextLeft, dateBannerY + 2f, textAvailW, bottomBannerH - 4f, dateAxisFormat, whiteBrush);
									}
									finally
									{
										RenderTarget.PopAxisAlignedClip();
									}
								}
							}

							int poc = day.PocBin;
							int vah = day.VahBin;
							int val = day.ValBin;
							int volumePoc = day.VolumePocBin;
							int volumeVah = day.VolumeVahBin;
							int volumeVal = day.VolumeValBin;
							double maxVolume = day.MaxRowVolume;

							int openBin = PriceToBin(day.Open, dStep);
							int closeBin = PriceToBin(day.Close, dStep);
							int maxBin = day.Rows.Keys.Max();
							int minBin = day.Rows.Keys.Min();

							float profileHighY = SessionPriceToY(day, maxBin * dStep + dStep * 0.5, step);
							float profileLowY = SessionPriceToY(day, minBin * dStep - dStep * 0.5, step);
							if (profileHighY > profileLowY)
							{
								float swapY = profileHighY;
								profileHighY = profileLowY;
								profileLowY = swapY;
							}

							float sampleRowYA = SessionPriceToY(day, poc * dStep + dStep * 0.5, step);
							float sampleRowYB = SessionPriceToY(day, poc * dStep - dStep * 0.5, step);
							float sampleRowH = Math.Max(1.0f, Math.Abs(sampleRowYB - sampleRowYA));

							// Adaptive Letter Spacing & Font Size (Layer 2 Adaptive Rendering Engine) + Snug Value Area Without Dead Empty Overhang
							const float tpoToVolGap = 12f;
							int maxRowCells = Math.Max(1, day.Rows.Max(kv => kv.Value.Periods.Count + (kv.Key == closeBin ? 1 : 0)));
							float baseCollapsedSlotW = Math.Max(92f, xCollapsedRight - xIb);
							float volWidth = ShowVolumeProfile ? 36f : 0f;
							float availTpoW = Math.Max(20f, baseCollapsedSlotW - volWidth - (ShowVolumeProfile ? tpoToVolGap : 0f) - 12f);
							float baseCellW = Math.Max(9.6f, Math.Min(11.2f, FontSize * 0.88f));
							float adaptiveMaxCellW = sampleRowH < 9.5f
								? Math.Max(7.0f, Math.Min(baseCellW, sampleRowH * 1.15f))
								: Math.Min(12.6f, Math.Max(baseCellW, sampleRowH * 0.86f));
							float letterWidth = Math.Min(adaptiveMaxCellW, Math.Max(5.2f, availTpoW / Math.Max(1, maxRowCells)));
							float collapsedTpoWidth = maxRowCells * letterWidth;

							float xTpo = xIb + 5f;
							float xVol = xTpo + collapsedTpoWidth + tpoToVolGap;
							float volSpineGapPx = modernUi ? 2.4f : 1.2f;
							float vaBoxRightX = ShowVolumeProfile
								? Math.Min(xCollapsedRight + 4f, xVol + volSpineGapPx + volWidth + 5f)
								: Math.Min(xCollapsedRight + 4f, xTpo + collapsedTpoWidth + 5f);

							float yVahA = SessionPriceToY(day, vah * dStep + dStep * 0.5, step);
							float yValB = SessionPriceToY(day, val * dStep - dStep * 0.5, step);
							float yVaTop = Math.Min(yVahA, yValB);
							float yVaBot = Math.Max(yVahA, yValB);
							float clampedVaTop = Math.Max(canvasTopY, yVaTop);
							float clampedVaBot = Math.Min(panelBottom - 29f, yVaBot);
							bool showProfileVah = ShowVirginVAH && (!isRunningDay || (EnableRunningDayLevels && ShowRunningDayVAH));
							bool showProfileVal = ShowVirginVAL && (!isRunningDay || (EnableRunningDayLevels && ShowRunningDayVAL));

							// Value Area Box (snugly wrapped around TPO + Volume Profile with zero trailing gap)
							if (modernUi)
							{
								float glassTop = Math.Max(canvasTopY + 2f, profileHighY - 5f);
								float glassBot = Math.Min(panelBottom - 30f, profileLowY + 5f);
								if (glassBot > glassTop + 8f)
								{
									var colCard = new RoundedRectangle
									{
										Rect = new RectangleF(xIb - 4f, glassTop, Math.Max(14f, vaBoxRightX - xIb + 8f), glassBot - glassTop),
										RadiusX = 6f,
										RadiusY = 6f
									};
									RenderTarget.FillRoundedRectangle(colCard, sessionGlassBrush);
									RenderTarget.DrawRoundedRectangle(colCard, sessionGlassBorderBrush, 0.85f);
								}
							}

							if ((showProfileVah || showProfileVal) && clampedVaBot > clampedVaTop + 1f)
							{
								RectangleF vaRect = new RectangleF(xIb, clampedVaTop, Math.Max(4f, vaBoxRightX - xIb), Math.Max(2f, clampedVaBot - clampedVaTop));
								if (modernUi)
								{
									RenderTarget.FillRoundedRectangle(new RoundedRectangle { Rect = vaRect, RadiusX = 4f, RadiusY = 4f }, vaFillBrush);
									RenderTarget.FillRectangle(new RectangleF(xIb, clampedVaTop, 2.5f, vaRect.Height), vaBorderBrush);
								}
								else
								{
									RenderTarget.FillRectangle(vaRect, vaFillBrush);
								}
							}
							if (showProfileVah && yVaTop >= canvasTopY && yVaTop <= panelBottom - 29f)
								RenderTarget.DrawLine(new Vector2(xIb, yVaTop), new Vector2(vaBoxRightX, yVaTop), vaBorderBrush, modernUi ? 1.35f : 1.0f, dashedStroke);
							if (showProfileVal && yVaBot >= canvasTopY && yVaBot <= panelBottom - 29f)
								RenderTarget.DrawLine(new Vector2(xIb, yVaBot), new Vector2(vaBoxRightX, yVaBot), vaBorderBrush, modernUi ? 1.35f : 1.0f, dashedStroke);

							float minFontFloor = AutoScaleTpoByScreenResolution ? 5.8f : 4.4f;
							float minLetterRowH = AutoScaleTpoByScreenResolution ? 6.5f : 4.2f;
							float maxFontCeil = Math.Max((float)Math.Max(9, FontSize), Math.Min(14.5f, sampleRowH * 0.82f));
							float letterFontPx = Math.Min(
								maxFontCeil,
								Math.Min(Math.Max(minFontFloor, sampleRowH * 0.78f), Math.Max(minFontFloor, letterWidth * 0.96f)));
							float splitLetterFontPx = Math.Min(
								Math.Max(10.5f, maxFontCeil),
								Math.Max(minFontFloor, sampleRowH * 0.76f));

							using (var letterFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, letterFontPx))
							using (var splitLetterFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, splitLetterFontPx))
							{
								letterFormat.TextAlignment = TextAlignment.Center;
								letterFormat.ParagraphAlignment = ParagraphAlignment.Center;
								letterFormat.WordWrapping = WordWrapping.NoWrap;

								splitLetterFormat.TextAlignment = TextAlignment.Center;
								splitLetterFormat.ParagraphAlignment = ParagraphAlignment.Center;
								splitLetterFormat.WordWrapping = WordWrapping.NoWrap;

								// Collapsed TPO Letters, Modern Glowing TPO Tiles, POC Highlight, & Smooth Volume Profile
								foreach (KeyValuePair<int, PriceRow> item in day.Rows)
								{
									int bin = item.Key;
									PriceRow row = item.Value;
									float yA = SessionPriceToY(day, bin * dStep + dStep * 0.5, step);
									float yB = SessionPriceToY(day, bin * dStep - dStep * 0.5, step);
									float y = Math.Min(yA, yB);
									float h = Math.Max(1.0f, Math.Abs(yB - yA));
									if (y < canvasTopY - 0.5f || y + h > panelBottom - 24f)
										continue;

									bool inTpoVa = (bin >= val && bin <= vah);

									// Highlight interior Buying Single Prints & Selling Single Prints strictly inside day.SinglePrintZones
									SinglePrintZone rowSpZone = null;
									if (ShowSinglePrints && day.SinglePrintZones != null)
									{
										for (int zIdx = 0; zIdx < day.SinglePrintZones.Count; zIdx++)
										{
											SinglePrintZone cand = day.SinglePrintZones[zIdx];
											if (bin >= cand.StartBin && bin <= cand.EndBin)
											{
												rowSpZone = cand;
												break;
											}
										}
									}
									if (rowSpZone != null)
									{
										bool isBuySp = rowSpZone.IsBuying;
										DxBrush spRowBorder = isBuySp ? singleBuyBorderBrush : singleSellBorderBrush;
										RectangleF spRect = new RectangleF(xTpo - 1.5f, y + 0.2f, collapsedTpoWidth + 3f, Math.Max(1.4f, h - 0.4f));
										if (modernUi)
											RenderTarget.FillRoundedRectangle(new RoundedRectangle { Rect = spRect, RadiusX = 2.5f, RadiusY = 2.5f }, isBuySp ? singleBuyRowBrush : singleSellRowBrush);
										else
											RenderTarget.FillRectangle(spRect, isBuySp ? singleBuyRowBrush : singleSellRowBrush);
										RenderTarget.DrawLine(
											new Vector2(xTpo - 1.5f, y),
											new Vector2(xTpo - 1.5f, y + h),
											spRowBorder,
											3.2f);
									}

									int[] sortedPeriods = row.GetSortedPeriods();

									if (modernUi && bin == poc && sortedPeriods.Length > 0)
									{
										int pocCellCnt = sortedPeriods.Length + (bin == closeBin ? 1 : 0);
										var pocHighlightRr = new RoundedRectangle
										{
											Rect = new RectangleF(xTpo - 1.5f, y + 0.35f, Math.Max(8f, pocCellCnt * letterWidth + 3f), Math.Max(1.6f, h - 0.7f)),
											RadiusX = 2.8f,
											RadiusY = 2.8f
										};
										RenderTarget.FillRoundedRectangle(pocHighlightRr, pocRowFillBrush);
										RenderTarget.DrawRoundedRectangle(pocHighlightRr, pocRowBorderBrush, 0.95f);
									}

									if (h >= minLetterRowH && letterWidth >= 4.8f)
									{
										for (int colIdx = 0; colIdx < sortedPeriods.Length; colIdx++)
										{
											int period = sortedPeriods[colIdx];
											int safePeriod = Math.Max(0, Math.Min(period, LetterSequence.Length - 1));
											int cIdxMod = safePeriod % letterBrushes.Length;
											float lx = xTpo + colIdx * letterWidth;

											if (modernUi && h >= 6.8f && letterWidth >= 5.6f)
											{
												float tpoHGap = letterWidth >= 8.0f ? 0.55f : 0.40f;
												float tpoVGap = h >= 10.0f ? 0.45f : 0.30f;
												var tileRr = new RoundedRectangle
												{
													Rect = new RectangleF(lx + tpoHGap, y + tpoVGap, Math.Max(2.0f, letterWidth - tpoHGap * 2f), Math.Max(2.0f, h - tpoVGap * 2f)),
													RadiusX = 2.0f,
													RadiusY = 2.0f
												};
												if (bin == openBin && colIdx == 0)
												{
													RenderTarget.FillRoundedRectangle(tileRr, openBadgeFillBrush);
													RenderTarget.DrawRoundedRectangle(tileRr, openBadgeBorderBrush, 0.9f);
													DrawTpoCellText("O", lx, y, letterWidth, h, letterFormat, whiteBrush);
												}
												else
												{
													RenderTarget.FillRoundedRectangle(tileRr, inTpoVa ? letterVaFillBrushes[cIdxMod] : letterOutFillBrushes[cIdxMod]);
													if (inTpoVa && h >= 9.5f && letterWidth >= 7.5f)
														RenderTarget.DrawRoundedRectangle(tileRr, letterTileBorderBrushes[cIdxMod], 0.65f);
													string letter = LetterSequence[safePeriod].ToString();
													DrawTpoCellText(letter, lx, y, letterWidth, h, letterFormat, letterBrushes[cIdxMod]);
												}
											}
											else if (bin == openBin && colIdx == 0)
											{
												DrawTpoCellText("O", lx, y, letterWidth, h, letterFormat, whiteBrush);
											}
											else
											{
												string letter = LetterSequence[safePeriod].ToString();
												DrawTpoCellText(letter, lx, y, letterWidth, h, letterFormat, letterBrushes[cIdxMod]);
											}
										}

										if (bin == closeBin)
										{
											float hashX = xTpo + sortedPeriods.Length * letterWidth;
											if (modernUi && h >= 6.8f && letterWidth >= 5.6f)
											{
												float tpoHGap = letterWidth >= 8.0f ? 0.55f : 0.40f;
												float tpoVGap = h >= 10.0f ? 0.45f : 0.30f;
												var closeRr = new RoundedRectangle
												{
													Rect = new RectangleF(hashX + tpoHGap, y + tpoVGap, Math.Max(2.0f, letterWidth - tpoHGap * 2f), Math.Max(2.0f, h - tpoVGap * 2f)),
													RadiusX = 2.0f,
													RadiusY = 2.0f
												};
												RenderTarget.FillRoundedRectangle(closeRr, closeBadgeFillBrush);
												RenderTarget.DrawRoundedRectangle(closeRr, closeBadgeBorderBrush, 0.9f);
												DrawTpoCellText("#", hashX, y, letterWidth, h, letterFormat, whiteBrush);
											}
											else
											{
												DrawTpoCellText("#", hashX, y, letterWidth, h, letterFormat, redBrush);
											}
										}
									}
									else
									{
										float cellVGap = h >= 2.2f ? 0.5f : 0.0f;
										float cellHGap = letterWidth >= 4.0f ? 0.5f : 0.15f;
										for (int colIdx = 0; colIdx < sortedPeriods.Length; colIdx++)
										{
											int period = sortedPeriods[colIdx];
											int safePeriod = Math.Max(0, Math.Min(period, LetterSequence.Length - 1));
											float lx = xTpo + colIdx * letterWidth;
											DxBrush cellBrush = (bin == openBin && colIdx == 0) ? (modernUi ? openBadgeFillBrush : whiteBrush) : letterBrushes[safePeriod % letterBrushes.Length];
											RenderTarget.FillRectangle(new RectangleF(lx + cellHGap, y + cellVGap * 0.5f, Math.Max(1.0f, letterWidth - cellHGap * 2f), Math.Max(1.0f, h - cellVGap)), cellBrush);
										}
										if (bin == closeBin)
										{
											float hashX = xTpo + sortedPeriods.Length * letterWidth;
											RenderTarget.FillRectangle(new RectangleF(hashX + cellHGap, y + cellVGap * 0.5f, Math.Max(1.0f, letterWidth - cellHGap * 2f), Math.Max(1.0f, h - cellVGap)), modernUi ? closeBadgeFillBrush : redBrush);
										}
									}

									if (ShowVolumeProfile && volWidth > 0)
									{
										float barW = (float)(volWidth * (row.Volume / maxVolume));
										bool inVolVa = bin >= volumeVal && bin <= volumeVah;
										DxBrush volBrush = (bin == volumePoc)
											? volumePocBrush
											: (inVolVa ? volumeVaBrush : volumeOutBrush);
										float volVGap = modernUi ? (h >= 10f ? 1.4f : (h >= 6.5f ? 1.0f : 0.6f)) : 0.8f;
										RectangleF volRect = new RectangleF(xVol + volSpineGapPx, y + volVGap * 0.5f, Math.Max(2.0f, barW), Math.Max(1.4f, h - volVGap));
										if (modernUi && h >= 4.5f)
										{
											var volRr = new RoundedRectangle { Rect = volRect, RadiusX = 2.0f, RadiusY = 2.0f };
											RenderTarget.FillRoundedRectangle(volRr, volBrush);
											if (bin == volumePoc)
												RenderTarget.DrawRoundedRectangle(volRr, yellowBrush, 0.8f);
										}
										else
										{
											RenderTarget.FillRectangle(volRect, volBrush);
										}
									}
								}

								// Prominent Single Print SL Lines & Inline Badges (Buying SP = Bottom line & price; Selling SP = Top line & price)
								if (ShowSinglePrints && day.SinglePrintZones.Count > 0)
								{
									for (int z = 0; z < day.SinglePrintZones.Count; z++)
									{
										SinglePrintZone spz = day.SinglePrintZones[z];
										double spSlPrice = GetSinglePrintStopLossPrice(spz, dStep);
										float ySpTop = SessionPriceToY(day, spz.EndBin * dStep + dStep * 0.5, step);
										float ySpBot = SessionPriceToY(day, spz.StartBin * dStep - dStep * 0.5, step);
										if (ySpTop > ySpBot)
										{
											float sw = ySpTop;
											ySpTop = ySpBot;
											ySpBot = sw;
										}
										float ySpSlLine = spz.IsBuying ? ySpBot : ySpTop;
										DxBrush spBorder = spz.IsBuying ? singleBuyBorderBrush : singleSellBorderBrush;

										if (ySpSlLine >= canvasTopY && ySpSlLine <= panelBottom - 25f)
										{
											RenderTarget.DrawLine(
												new Vector2(xTpo - 1.5f, ySpSlLine),
												new Vector2(xCollapsedRight, ySpSlLine),
												spBorder,
												1.9f);
										}

										if (day.PeriodSlices.Count >= 3)
										{
											int anchorBin = spz.IsBuying ? spz.StartBin : spz.EndBin;
											int maxNearCells = 1;
											for (int nb = anchorBin - 1; nb <= anchorBin + 1; nb++)
											{
												PriceRow nr;
												if (day.Rows.TryGetValue(nb, out nr))
													maxNearCells = Math.Max(maxNearCells, nr.Periods.Count + (nb == closeBin ? 1 : 0));
											}

											float yZ = spz.IsBuying ? (ySpBot - 12.5f) : (ySpTop + 0.5f);
											yZ = Math.Max(profileHighY + 4f, Math.Min(profileLowY - 14f, yZ));
											if (yZ >= canvasTopY + 2f && yZ <= panelBottom - 36f)
											{
												float availRightW = (xVol - 3f) - (xTpo + maxNearCells * letterWidth + 6f);
												if (availRightW >= 46f)
												{
													string priceStr = FormatPrice(spSlPrice);
													bool showFullPrice = availRightW >= 88f;
													string spLabel = showFullPrice
														? ((spz.IsBuying ? "Buy SP " : "Sell SP ") + priceStr)
														: (spz.IsBuying ? "Buy SP" : "Sell SP");
													float spTagW = showFullPrice ? Math.Min(availRightW, 98f) : 48f;
													float spTagX = xVol - spTagW - 3f;
													RectangleF spPill = new RectangleF(spTagX, yZ, spTagW, 12.5f);
													RenderTarget.FillRectangle(spPill, spz.IsBuying ? spBuyPillBgBrush : spSellPillBgBrush);
													RenderTarget.DrawRectangle(spPill, spBorder, 1.0f);
													DrawText(spLabel, spTagX + 3f, yZ, spTagW - 5f, 12.5f, spTagFormat, spBorder);
												}
											}
										}
									}
								}

								if (ShowVolumeProfile && volWidth > 0)
								{
									float ySpHigh = Math.Max(canvasTopY, profileHighY);
									float ySpLow = Math.Min(panelBottom - 25f, profileLowY);
									if (ySpLow > ySpHigh)
									{
										RenderTarget.DrawLine(
											new Vector2(xVol, ySpHigh),
											new Vector2(xVol, ySpLow),
											spineLineBrush, 1.0f);
									}
								}

								// Initial Balance (Green Bar on Spine)
								if (ShowInitialBalance && day.InitialBalanceHigh != double.MinValue && day.InitialBalanceLow != double.MaxValue)
								{
									float yIbH = SessionPriceToY(day, day.InitialBalanceHigh, step);
									float yIbL = SessionPriceToY(day, day.InitialBalanceLow, step);
									float yIbTop = Math.Max(canvasTopY, Math.Min(yIbH, yIbL));
									float yIbBot = Math.Min(panelBottom - 25f, Math.Max(yIbH, yIbL));
									if (yIbBot > yIbTop)
									{
										if (modernUi)
										{
											RenderTarget.DrawLine(new Vector2(xIb, yIbTop), new Vector2(xIb, yIbBot), ibGlowBrush, 5.6f);
											RenderTarget.DrawLine(new Vector2(xIb, yIbTop), new Vector2(xIb, yIbBot), ibBarBrush, 2.8f);
											RenderTarget.DrawLine(new Vector2(xIb - 2.5f, yIbTop), new Vector2(xIb + 2.5f, yIbTop), ibBarBrush, 1.5f);
											RenderTarget.DrawLine(new Vector2(xIb - 2.5f, yIbBot), new Vector2(xIb + 2.5f, yIbBot), ibBarBrush, 1.5f);
										}
										else
										{
											RenderTarget.DrawLine(new Vector2(xIb, yIbTop), new Vector2(xIb, yIbBot), ibBarBrush, 2.4f);
										}
									}
								}

								PriceRow topRow = day.Rows[maxBin];
								bool isAbPoorHigh = topRow.Periods.Contains(0) && topRow.Periods.Contains(1);
								bool isPoorHigh = !isAbPoorHigh && topRow.Periods.Count >= 2;
								bool showTopPoorBox = !isRunningDay && ShowPoorHighLow && ShowPoorHigh && (isAbPoorHigh || isPoorHigh);
								string highLevelName = isAbPoorHigh ? "ABPoorHigh" : "PoorHigh";
								DxBrush highLevelBrush = highExtremeBrush;

								float topTagY = profileHighY - 17f;
								float headerY = FixSessionHeaderAtTop
									? (topSessionBarY + 2f)
									: Math.Max(canvasTopY + 4f, (showTopPoorBox ? topTagY - 36f : profileHighY - 36f));
								float btnRowY = headerY + 17f;

								float minDayHdrSlotW = ShowSummary ? 214f : 202f;
								float xSplitStart = FixSessionHeaderAtTop
									? Math.Max(vaBoxRightX + 14f, xSlotLeft + minDayHdrSlotW)
									: (vaBoxRightX + 14f);

								// Split Profile (Expanded — docked in Fixed Top Session Bar when FixSessionHeaderAtTop is enabled)
								if (day.IsSplitExpanded && day.PeriodSlices.Count > 0)
								{
									float xSplitEnd = Math.Min(xSlotRight - 6f, panelRight - 178f);
									if (xSplitEnd > xSplitStart + 65f)
									{
										if (FixSessionHeaderAtTop && xSplitStart - 6f >= panelLeft + 4f && xSplitStart - 6f <= panelRight - 182f)
										{
											RenderTarget.DrawLine(
												new Vector2(xSplitStart - 6f, topSessionBarY),
												new Vector2(xSplitStart - 6f, topSessionBarY + 35f),
												cardBorderBrush,
												1.0f);
										}
										float splitAvailW = xSplitEnd - xSplitStart;
										List<PeriodSlice> orderedSlices = day.PeriodSlices.Values.OrderBy(ps => ps.PeriodIndex).ToList();
										int sliceCount = Math.Max(1, orderedSlices.Count);
										int volBarCount = ShowVolumeProfile ? Math.Min(3, orderedSlices.Count) : 0;
										float singleVolW = 26f;
										float volColGap = 8f;
										float splitTpoToVolGap = 14f;
										float totalSplitVolW = volBarCount > 0
											? (volBarCount * singleVolW + (volBarCount - 1) * volColGap)
											: 0f;
										float splitColGap = modernUi ? 3.6f : 3.0f;
										float maxSplitColW = sampleRowH < 9.5f
											? Math.Max(8.4f, Math.Min(11.4f, sampleRowH * 1.18f))
											: Math.Min(12.6f, Math.Max(11.2f, sampleRowH * 0.90f));
										float rawAvailForSplitTpo = splitAvailW - totalSplitVolW - splitTpoToVolGap - 12f;
										float effSplitColGap = rawAvailForSplitTpo >= sliceCount * 9.5f + Math.Max(0, sliceCount - 1) * splitColGap
											? splitColGap
											: 2.2f;
										float splitColW = Math.Min(
											maxSplitColW,
											Math.Max(7.6f, (rawAvailForSplitTpo - Math.Max(0, sliceCount - 1) * effSplitColGap) / Math.Max(2, sliceCount)));
										float splitTpoTotalW = sliceCount * splitColW + Math.Max(0, sliceCount - 1) * effSplitColGap;
										float xSplitVol = Math.Min(xSplitEnd - totalSplitVolW - 2f, xSplitStart + 5f + splitTpoTotalW + splitTpoToVolGap);
										float xSplitVaEnd = ShowVolumeProfile && volBarCount > 0
											? Math.Min(xSplitEnd, xSplitVol + totalSplitVolW + 5f)
											: Math.Min(xSplitEnd, xSplitStart + 5f + splitTpoTotalW + 5f);

										float splitHeaderW = Math.Min(164f, Math.Max(114f, splitAvailW));
										RectangleF splitHdrPill = new RectangleF(xSplitStart, headerY, splitHeaderW, 16f);
										int maxSliceIdx = orderedSlices.Count > 0
											? Math.Max(0, Math.Min(orderedSlices[orderedSlices.Count - 1].PeriodIndex, LetterSequence.Length - 1))
											: 0;
										char endSliceChar = LetterSequence[maxSliceIdx];
										bool isSplitDayLive = isRunningDay;
										string splitHdrTitle = isSplitDayLive
											? string.Format(CultureInfo.InvariantCulture, modernUi ? "SPLIT · A..{0} ● LIVE" : "SPLIT PROFILE (A..{0}) LIVE", endSliceChar)
											: string.Format(CultureInfo.InvariantCulture, modernUi ? "SPLIT · A..{0}" : "SPLIT PROFILE (A..{0})", endSliceChar);
										if (modernUi)
										{
											DrawModernPillButton(splitHdrPill, btnActiveBgBrush, cyanBrush, splitHdrTitle, spTagFormat, isSplitDayLive ? greenBrush : cyanBrush, 4.5f, 1.0f);
											float spGlassTop = Math.Max(canvasTopY + 2f, profileHighY - 5f);
											float spGlassBot = Math.Min(panelBottom - 30f, profileLowY + 5f);
											if (spGlassBot > spGlassTop + 8f)
											{
												var spGlassRr = new RoundedRectangle
												{
													Rect = new RectangleF(xSplitStart - 3f, spGlassTop, Math.Max(14f, xSplitVaEnd - xSplitStart + 6f), spGlassBot - spGlassTop),
													RadiusX = 6f,
													RadiusY = 6f
												};
												RenderTarget.FillRoundedRectangle(spGlassRr, sessionGlassBrush);
												RenderTarget.DrawRoundedRectangle(spGlassRr, sessionGlassBorderBrush, 0.8f);
											}
										}
										else
										{
											RenderTarget.FillRectangle(splitHdrPill, btnActiveBgBrush);
											RenderTarget.DrawRectangle(splitHdrPill, cyanBrush, 0.8f);
											DrawText(splitHdrTitle, xSplitStart + 4f, headerY, splitHeaderW - 8f, 16f, spTagFormat, isSplitDayLive ? greenBrush : cyanBrush);
										}
										if (ShowChartButtons)
										{
											activeChartButtons.Add(new ChartButton
											{
												Rect = splitHdrPill,
												Action = ChartButtonAction.ToggleSplit,
												PrimaryKey = day.ProfileKey + "|ON"
											});
										}

										if ((showProfileVah || showProfileVal) && clampedVaBot > clampedVaTop + 1f)
										{
											RenderTarget.FillRectangle(
												new RectangleF(xSplitStart, clampedVaTop, Math.Max(10f, xSplitVaEnd - xSplitStart), Math.Max(2f, clampedVaBot - clampedVaTop)),
												vaFillBrush);
										}
										if (showProfileVah && yVaTop >= canvasTopY && yVaTop <= panelBottom - 29f)
											RenderTarget.DrawLine(new Vector2(xSplitStart, yVaTop), new Vector2(xSplitVaEnd, yVaTop), vaBorderBrush, 1.0f, dashedStroke);
										if (showProfileVal && yVaBot >= canvasTopY && yVaBot <= panelBottom - 29f)
											RenderTarget.DrawLine(new Vector2(xSplitStart, yVaBot), new Vector2(xSplitVaEnd, yVaBot), vaBorderBrush, 1.0f, dashedStroke);

										// Highlight Single Print Zones & SL lines inside Split Profile as well
										if (ShowSinglePrints && day.SinglePrintZones.Count > 0)
										{
											for (int z = 0; z < day.SinglePrintZones.Count; z++)
											{
												SinglePrintZone spz = day.SinglePrintZones[z];
												float ySpTop = SessionPriceToY(day, spz.EndBin * dStep + dStep * 0.5, step);
												float ySpBot = SessionPriceToY(day, spz.StartBin * dStep - dStep * 0.5, step);
												if (ySpTop > ySpBot)
												{
													float sw = ySpTop;
													ySpTop = ySpBot;
													ySpBot = sw;
												}
												float clSpTop = Math.Max(canvasTopY, ySpTop);
												float clSpBot = Math.Min(panelBottom - 25f, ySpBot);
												float ySpSlLine = spz.IsBuying ? ySpBot : ySpTop;
												if (clSpBot > clSpTop)
												{
													RenderTarget.FillRectangle(
														new RectangleF(xSplitStart, clSpTop, Math.Max(10f, xSplitEnd - xSplitStart), Math.Max(2.0f, clSpBot - clSpTop)),
														spz.IsBuying ? singleBuyRowBrush : singleSellRowBrush);
												}
												if (ySpSlLine >= canvasTopY && ySpSlLine <= panelBottom - 25f)
												{
													RenderTarget.DrawLine(
														new Vector2(xSplitStart, ySpSlLine),
														new Vector2(xSplitEnd, ySpSlLine),
														spz.IsBuying ? singleBuyBorderBrush : singleSellBorderBrush,
														1.75f);
												}
											}
										}

										if (ShowInitialBalance && day.InitialBalanceHigh != double.MinValue && day.InitialBalanceLow != double.MaxValue)
										{
											float yIbH = SessionPriceToY(day, day.InitialBalanceHigh, step);
											float yIbL = SessionPriceToY(day, day.InitialBalanceLow, step);
											float yIbTop = Math.Max(canvasTopY, Math.Min(yIbH, yIbL));
											float yIbBot = Math.Min(panelBottom - 25f, Math.Max(yIbH, yIbL));
											if (yIbBot > yIbTop)
												RenderTarget.DrawLine(new Vector2(xSplitStart, yIbTop), new Vector2(xSplitStart, yIbBot), ibBarBrush, 2.2f);
										}

										if (orderedSlices.Count > 0)
										{
											float hdrW = Math.Max(24f, Math.Min(xSplitVol - xSplitStart - 2f, splitTpoTotalW + 8f));
											RenderTarget.FillRectangle(new RectangleF(xSplitStart + 1f, btnRowY - 0.5f, hdrW, 15f), pillBgBrush);
										}

										int hpUniformW = Math.Max(4, (int)Math.Round(splitColW - 2.2f));
										float hpBarH = sampleRowH >= 10.5f ? 2.0f : 1.0f;
										float effSplitFontPx = Math.Min(splitLetterFontPx, Math.Max(minFontFloor, splitColW * 0.90f));
										float splitAtFontPx = Math.Min(effSplitFontPx * 0.88f, Math.Max(6.8f, splitColW * 0.86f));
										using (var splitCellFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, effSplitFontPx))
										using (var splitAtFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, splitAtFontPx))
										{
											splitCellFormat.TextAlignment = TextAlignment.Center;
											splitCellFormat.ParagraphAlignment = ParagraphAlignment.Center;
											splitCellFormat.WordWrapping = WordWrapping.NoWrap;

											splitAtFormat.TextAlignment = TextAlignment.Center;
											splitAtFormat.ParagraphAlignment = ParagraphAlignment.Center;
											splitAtFormat.WordWrapping = WordWrapping.NoWrap;

											for (int cIdx = 0; cIdx < orderedSlices.Count; cIdx++)
											{
												PeriodSlice slice = orderedSlices[cIdx];
												int pIdx = Math.Max(0, Math.Min(slice.PeriodIndex, LetterSequence.Length - 1));
												string pLetter = LetterSequence[pIdx].ToString();
												float cx = xSplitStart + 5f + cIdx * (splitColW + effSplitColGap);
												if (cx + splitColW > xSplitVol - 2f)
													break;

												DrawText(pLetter, cx, btnRowY, splitColW, 14f, splitCellFormat, letterBrushes[pIdx % letterBrushes.Length]);

												int sMinBin = PriceToBin(slice.Low, dStep);
												int sMaxBin = PriceToBin(slice.High, dStep);
												if (sMaxBin < sMinBin)
												{
													int swBin = sMinBin;
													sMinBin = sMaxBin;
													sMaxBin = swBin;
												}
												int sOpenBin = Math.Max(sMinBin, Math.Min(sMaxBin, PriceToBin(slice.Open, dStep)));
												int sCloseBin = Math.Max(sMinBin, Math.Min(sMaxBin, PriceToBin(slice.Close, dStep)));
												int tpoSpanCount = sMaxBin - sMinBin + 1;
												int sMidBin = sMinBin + (tpoSpanCount / 2);
												float midRowBotY = float.NaN;

												for (int b = sMinBin; b <= sMaxBin; b++)
												{
													float yA = SessionPriceToY(day, b * dStep + dStep * 0.5, step);
													float yB = SessionPriceToY(day, b * dStep - dStep * 0.5, step);
													float y = Math.Min(yA, yB);
													float h = Math.Max(1.0f, Math.Abs(yB - yA));
													if (y < canvasTopY - 0.5f || y + h > panelBottom - 24f)
														continue;

													bool isMidTpo = (b == sMidBin);
													if (isMidTpo)
														midRowBotY = y + h;

													float txtY = isMidTpo ? (y - 1.0f) : y;
													float txtH = isMidTpo ? Math.Max(1.0f, h - 1.2f) : h;

													bool isOpenTpo = (b == sOpenBin);
													bool isCloseTpo = (b == sCloseBin);
													DxBrush lBrush = (isOpenTpo || isCloseTpo)
														? whiteBrush
														: letterBrushes[pIdx % letterBrushes.Length];

													if (h >= Math.Min(minLetterRowH, 5.2f))
													{
														if (modernUi && h >= 6.8f && splitColW >= 5.6f)
														{
															int cMod = pIdx % letterBrushes.Length;
															bool inSpVa = (b >= val && b <= vah);
															var sTileRr = new RoundedRectangle
															{
																Rect = new RectangleF(cx + 0.35f, y + 0.45f, Math.Max(2.0f, splitColW - 0.7f), Math.Max(2.0f, h - 0.9f)),
																RadiusX = 2.0f,
																RadiusY = 2.0f
															};
															DxBrush sFill = isOpenTpo ? openBadgeFillBrush : (isCloseTpo ? closeBadgeFillBrush : (inSpVa ? letterVaFillBrushes[cMod] : letterOutFillBrushes[cMod]));
															RenderTarget.FillRoundedRectangle(sTileRr, sFill);
														}
														if (isOpenTpo && isCloseTpo)
														{
															RectangleF atRect = new RectangleF(cx - 0.5f, txtY, splitColW + 1.0f, Math.Max(1.0f, txtH));
															RenderTarget.DrawText("@", splitAtFormat, atRect, whiteBrush, DrawTextOptions.None);
														}
														else
														{
															string cellSym = isOpenTpo ? "O" : (isCloseTpo ? "#" : pLetter);
															DrawTpoCellText(cellSym, cx, txtY, splitColW, txtH, splitCellFormat, lBrush);
														}
													}
													else
													{
														RenderTarget.FillRectangle(new RectangleF(cx + 0.8f, y + 0.5f, Math.Max(1.0f, splitColW - 1.6f), Math.Max(1.0f, h - 1.0f)), lBrush);
													}
												}

												// Uniform pixel-snapped Halfpack line: identical width & thickness regardless of TPO letter
												if (!float.IsNaN(midRowBotY))
												{
													float colMidPx = (float)Math.Round(cx + splitColW * 0.5f);
													float hpLeftPx = (float)Math.Floor(colMidPx - hpUniformW * 0.5f);
													float hpTopPx = (float)Math.Floor(midRowBotY) - hpBarH;

													var prevAa = RenderTarget.AntialiasMode;
													RenderTarget.AntialiasMode = AntialiasMode.Aliased;
													try
													{
														RenderTarget.FillRectangle(new RectangleF(hpLeftPx, hpTopPx - 1.0f, hpUniformW, 1.0f), cardBgBrush);
														RenderTarget.FillRectangle(new RectangleF(hpLeftPx, hpTopPx, hpUniformW, hpBarH), whiteBrush);
													}
													finally
													{
														RenderTarget.AntialiasMode = prevAa;
													}
												}
											}
										}

										// Draw Last-3-Candles Volume Bars (1 bar when A starts, 2 when B, 3 when C, then sliding window of last 3 TPO candles: e.g. B,C,D .. K,L,M)
										if (ShowVolumeProfile && volBarCount > 0)
										{
											int startVolSliceIdx = Math.Max(0, orderedSlices.Count - 3);
											for (int vIdx = 0; vIdx < volBarCount; vIdx++)
											{
												PeriodSlice vSlice = orderedSlices[startVolSliceIdx + vIdx];
												int vpIdx = Math.Max(0, Math.Min(vSlice.PeriodIndex, LetterSequence.Length - 1));
												string vpLetter = LetterSequence[vpIdx].ToString();
												DxBrush vpColorBrush = letterBrushes[vpIdx % letterBrushes.Length];

												float vx = xSplitVol + vIdx * (singleVolW + volColGap);
												if (vx + 8f > xSplitEnd)
													break;

												int vMinBin = PriceToBin(vSlice.Low, dStep);
												int vMaxBin = PriceToBin(vSlice.High, dStep);
												if (vMaxBin < vMinBin)
												{
													int swB = vMinBin;
													vMinBin = vMaxBin;
													vMaxBin = swB;
												}

												Dictionary<int, double> sliceBinVols;
												double maxSliceVol;
												int sliceVolPoc, sliceVolVah, sliceVolVal;
												CalculateSliceVolumeValueArea(
													vSlice,
													vMinBin,
													vMaxBin,
													ValueAreaPercent,
													out sliceBinVols,
													out maxSliceVol,
													out sliceVolPoc,
													out sliceVolVah,
													out sliceVolVal);

												float ySliceHigh = SessionPriceToY(day, vMaxBin * dStep + dStep * 0.5, step);
												float ySliceLow = SessionPriceToY(day, vMinBin * dStep - dStep * 0.5, step);
												if (ySliceHigh > ySliceLow)
												{
													float swY = ySliceHigh;
													ySliceHigh = ySliceLow;
													ySliceLow = swY;
												}

												float clSliceHigh = Math.Max(canvasTopY, ySliceHigh);
												float clSliceLow = Math.Min(panelBottom - 25f, ySliceLow);
												if (clSliceLow > clSliceHigh)
												{
													RenderTarget.DrawLine(
														new Vector2(vx, clSliceHigh),
														new Vector2(vx, clSliceLow),
														spineLineBrush, 0.85f);
												}

												for (int b = vMinBin; b <= vMaxBin; b++)
												{
													float yA = SessionPriceToY(day, b * dStep + dStep * 0.5, step);
													float yB = SessionPriceToY(day, b * dStep - dStep * 0.5, step);
													float y = Math.Min(yA, yB);
													float h = Math.Max(1.0f, Math.Abs(yB - yA));
													if (y < canvasTopY - 0.5f || y + h > panelBottom - 24f)
														continue;

													double bVol;
													if (!sliceBinVols.TryGetValue(b, out bVol))
														bVol = 0;

													float barW = (float)(singleVolW * (bVol / maxSliceVol));
													bool inSliceVa = b >= sliceVolVal && b <= sliceVolVah;
													DxBrush sBrush = (b == sliceVolPoc)
														? splitVolPocBrush
														: (inSliceVa ? splitVolVaBrush : splitVolOutBrush);
													float sVolVGap = modernUi ? (h >= 10f ? 2.4f : (h >= 6.5f ? 1.6f : 1.0f)) : 1.0f;
													RectangleF sVolRect = new RectangleF(vx + (modernUi ? 2.8f : 1.0f), y + sVolVGap * 0.5f, Math.Max(2.0f, barW), Math.Max(1.2f, h - sVolVGap));
													if (modernUi && h >= 4.5f)
														RenderTarget.FillRoundedRectangle(new RoundedRectangle { Rect = sVolRect, RadiusX = 1.8f, RadiusY = 1.8f }, sBrush);
													else
														RenderTarget.FillRectangle(sVolRect, sBrush);
												}

												// Show which TPO Volume Bar this is in a fixed boxed badge at the top row (btnRowY)
												float badgeW = Math.Min(singleVolW, 20f);
												RectangleF vBadgeRect = new RectangleF(vx, btnRowY, badgeW, 14f);
												if (modernUi)
												{
													DrawModernPillButton(vBadgeRect, pillBgBrush, vpColorBrush, vpLetter, tagCenterFormat, vpColorBrush, 3.5f, 0.95f);
												}
												else
												{
													RenderTarget.FillRectangle(vBadgeRect, pillBgBrush);
													RenderTarget.DrawRectangle(vBadgeRect, vpColorBrush, 0.9f);
													DrawText(vpLetter, vx, btnRowY, badgeW, 14f, tagCenterFormat, vpColorBrush);
												}
											}
										}
									}
								}

								// Top Extreme Pill (PoorHigh / ABPoorHigh only — SwingHigh box removed)
								if (showTopPoorBox && topTagY >= canvasTopY + 1f && topTagY <= panelBottom - 42f)
								{
									float tagW = 86f;
									float tagX = (xIb + xCollapsedRight) * 0.5f - tagW * 0.5f;
									RectangleF tagPill = new RectangleF(tagX, topTagY, tagW, 15f);
									if (modernUi)
										DrawModernPillButton(tagPill, pillBgBrush, highExtremeBorderBrush, highLevelName, tagCenterFormat, highLevelBrush, 4.0f, 1.0f);
									else
									{
										RenderTarget.FillRectangle(tagPill, pillBgBrush);
										RenderTarget.DrawRectangle(tagPill, highExtremeBorderBrush, 1.0f);
										DrawText(highLevelName, tagX, topTagY, tagW, 15f, tagCenterFormat, highLevelBrush);
									}
								}

								// Day Header Pill
								int expectedTpos = Math.Max(1, day.ExpectedPeriodCount);
								int printedTpos = Math.Min(expectedTpos, Math.Max(day.PeriodSlices.Count, day.LastPeriod + 1));
								bool isDayLive = isRunningDay;
								string tpoStatus = isDayLive
									? string.Format(CultureInfo.InvariantCulture, modernUi ? "● LIVE {0}/{1} TPO" : "LIVE: {0}/{1} TPO", printedTpos, expectedTpos)
									: string.Format(CultureInfo.InvariantCulture, "{0}/{1} TPO", printedTpos, expectedTpos);
								string dayHeader = EffectiveProfileMode == MarketProfilePeriodMode.Daily
									? string.Format(CultureInfo.InvariantCulture, modernUi ? "{0} · {1}  {2}" : "{0} ({1}) - {2}",
										day.SessionDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
										day.SessionDate.ToString("ddd", CultureInfo.InvariantCulture),
										tpoStatus)
									: string.Format(CultureInfo.InvariantCulture, modernUi ? "{0} · {1}" : "{0} - {1}", day.SessionName, tpoStatus);

								float availDayHdrW = (day.IsSplitExpanded ? (xSplitStart - 8f) : (xSlotRight - 5f)) - (xIb - 1f);
								float headerW = Math.Max(148f, Math.Min(availDayHdrW, 250f));
								RectangleF headerPill = new RectangleF(xIb - 1f, headerY, headerW, 16f);
								if (modernUi)
								{
									var hdrRr = new RoundedRectangle { Rect = headerPill, RadiusX = 4.5f, RadiusY = 4.5f };
									RenderTarget.FillRoundedRectangle(hdrRr, pillBgBrush);
									RenderTarget.DrawRoundedRectangle(hdrRr, cardBorderBrush, 0.9f);
									RenderTarget.FillRectangle(new RectangleF(headerPill.X + 1.5f, headerPill.Y + 2.5f, 3.0f, headerPill.Height - 5.0f), cyanBrush);
									DrawText(dayHeader, xIb + 6f, headerY, headerW - 9f, 16f, headerFormat, cyanBrush);
								}
								else
								{
									RenderTarget.FillRectangle(headerPill, pillBgBrush);
									RenderTarget.DrawRectangle(headerPill, cardBorderBrush, 0.8f);
									DrawText(dayHeader, xIb + 3f, headerY, headerW - 6f, 16f, headerFormat, cyanBrush);
								}

								// Action Button: Split / Split ON
								if (ShowChartButtons)
								{
									float splitBtnW = 62f;
									RectangleF splitBtnRect = new RectangleF(xIb - 1f, btnRowY, splitBtnW, 14f);
									string splitLbl = modernUi
										? (day.IsSplitExpanded ? "Split ●" : "Split")
										: (day.IsSplitExpanded ? "[Split ON]" : "[Split]");
									if (modernUi)
									{
										DrawModernPillButton(splitBtnRect, day.IsSplitExpanded ? btnActiveBgBrush : btnBgBrush, day.IsSplitExpanded ? cyanBrush : cardBorderBrush, splitLbl, tagCenterFormat, day.IsSplitExpanded ? cyanBrush : textBrush, 4.0f, 0.95f);
									}
									else
									{
										RenderTarget.FillRectangle(splitBtnRect, day.IsSplitExpanded ? btnActiveBgBrush : btnBgBrush);
										RenderTarget.DrawRectangle(splitBtnRect, day.IsSplitExpanded ? cyanBrush : cardBorderBrush, 0.8f);
										DrawText(splitLbl, splitBtnRect.X, splitBtnRect.Y, splitBtnRect.Width, splitBtnRect.Height, tagCenterFormat, day.IsSplitExpanded ? cyanBrush : textBrush);
									}
									activeChartButtons.Add(new ChartButton
									{
										Rect = splitBtnRect,
										Action = ChartButtonAction.ToggleSplit,
										PrimaryKey = day.IsSplitExpanded ? (day.ProfileKey + "|ON") : day.ProfileKey
									});
								}

								// Bottom Extreme Pill (PoorLow / ABPoorLow only — SwingLow box removed)
								PriceRow botRow = day.Rows[minBin];
								bool isAbPoorLow = botRow.Periods.Contains(0) && botRow.Periods.Contains(1);
								bool isPoorLow = !isAbPoorLow && botRow.Periods.Count >= 2;
								bool showBotPoorBox = !isRunningDay && ShowPoorHighLow && ShowPoorLow && (isAbPoorLow || isPoorLow);
								string lowLevelName = isAbPoorLow ? "ABPoorLow" : "PoorLow";
								DxBrush lowLevelBrush = lowExtremeBrush;

								float botTagY = profileLowY + 3f;
								if (showBotPoorBox && botTagY >= canvasTopY + 2f && botTagY <= panelBottom - 40f)
								{
									float tagW = 86f;
									float tagX = (xIb + xCollapsedRight) * 0.5f - tagW * 0.5f;
									RectangleF tagPill = new RectangleF(tagX, botTagY, tagW, 15f);
									if (modernUi)
										DrawModernPillButton(tagPill, pillBgBrush, lowExtremeBorderBrush, lowLevelName, tagCenterFormat, lowLevelBrush, 4.0f, 1.0f);
									else
									{
										RenderTarget.FillRectangle(tagPill, pillBgBrush);
										RenderTarget.DrawRectangle(tagPill, lowExtremeBorderBrush, 1.0f);
										DrawText(lowLevelName, tagX, botTagY, tagW, 15f, tagCenterFormat, lowLevelBrush);
									}
								}

								// 9-Line Summary Card
								if (ShowSummary)
								{
									bool sumMinimized = IsSummaryBoxMinimized;
									float summaryCardH = sumMinimized ? (2f * 12.2f + 8f) : (9f * 12.2f + 8f);
									float fixedBottomTopY = dateBannerY - summaryCardH - 4f;
									float floatingTopY = showBotPoorBox ? (botTagY + 18f) : (profileLowY + 8f);
									float summaryStartTopY = FixSummaryBoxAtBottom ? fixedBottomTopY : floatingTopY;
									float summaryCardW = Math.Min(234f, Math.Max(195f, slotWidth - 12f));

									DrawAK10XSummaryCard(
										day, prevDay, allDays, i,
										poc, vah, val, dStep, avgIbRange,
										xIb, summaryStartTopY, summaryCardW,
										cardBgBrush, cardBorderBrush, btnBgBrush,
										whiteBrush, textBrush, orangeBrush, yellowBrush, greenBrush, cyanBrush,
										cardFormat, tagCenterFormat);
								}
							}
						}
						}
						finally
						{
							RenderTarget.PopAxisAlignedClip();
						}

						// Live TPO Counter with Running TPO Letter in Top-Right Dock (replacing Key Profile Levels for maximum visibility)
						DrawTopRightTpoCountdownBox(allDays, panelRight, FixSessionHeaderAtTop ? topSessionBarY : (canvasTopY - 27f), FixSessionHeaderAtTop ? 35f : 26f, 184f, modernUi);
						RenderDeoverlappedRightEdgeTags(rightEdgeTags, step, panelRight, Math.Max(panelTop + 40f, canvasTopY + 1f), panelBottom - 8f, pillBgBrush, cardBorderBrush, labelRightFormat);

						// Top-Left Info Box
						if (ShowTopLeftBox)
						{
							DayProfile latestDay = allDays[allDays.Count - 1];
							DayProfile priorDay = allDays.Count > 1 ? allDays[allDays.Count - 2] : null;
							DrawTopLeftInfoBox(
								latestDay, priorDay, step,
								cardBgBrush, cardBorderBrush, btnBgBrush,
								whiteBrush, textBrush, orangeBrush, yellowBrush, greenBrush, redBrush, cyanBrush,
								cardFormat, tagCenterFormat);
						}
						else
						{
							lastTopLeftHudRightX = 0f;
						}

						// Interactive Top Studio Icon right next to Top-Left Info Box to toggle World-Class Modern Classic Rich UI
						DrawModernUiToggleIcon(panelLeft, panelTop, panelRight);

						// Top-Right PROFILE Dropdown (Daily, Weekly, Monthly - Default: Daily)
						DrawTopRightProfileDropdown(
							panelRight, panelTop,
							cardBgBrush, cardBorderBrush, btnBgBrush, btnActiveBgBrush,
							whiteBrush, textBrush, orangeBrush, cyanBrush,
							headerFormat, tagCenterFormat);
					}
				}
				finally
				{
					RenderTarget.AntialiasMode = prevRenderAa;
					RenderTarget.TextAntialiasMode = prevTextAa;
					for (int i = 0; i < letterBrushes.Length; i++)
					{
						if (letterBrushes[i] != null) letterBrushes[i].Dispose();
						if (letterVaFillBrushes[i] != null) letterVaFillBrushes[i].Dispose();
						if (letterOutFillBrushes[i] != null) letterOutFillBrushes[i].Dispose();
						if (letterTileBorderBrushes[i] != null) letterTileBorderBrushes[i].Dispose();
					}
				}
			}
		}

		private void RenderDeoverlappedRightEdgeTags(
			List<RightEdgeTag> rawTags,
			double step,
			float panelRight,
			float minY,
			float maxY,
			DxBrush bgBrush,
			DxBrush borderBrush,
			DxTextFormat rightFormat)
		{
			if (rawTags == null || rawTags.Count == 0)
				return;

			List<RightEdgeTag> ordered = rawTags
				.OrderBy(t => t.IdealY)
				.ThenByDescending(t => t.Priority)
				.ToList();

			List<RightEdgeTag> tags = new List<RightEdgeTag>();
			for (int i = 0; i < ordered.Count; i++)
			{
				RightEdgeTag candidate = ordered[i];
				bool isDuplicatePrice = false;
				for (int j = 0; j < tags.Count; j++)
				{
					if (Math.Abs(tags[j].Price - candidate.Price) <= step * 0.45 && Math.Abs(tags[j].IdealY - candidate.IdealY) < 6f)
					{
						if (candidate.Priority > tags[j].Priority)
							tags[j] = candidate;
						isDuplicatePrice = true;
						break;
					}
				}
				if (!isDuplicatePrice)
					tags.Add(candidate);
			}

			tags.Sort((a, b) => a.IdealY.CompareTo(b.IdealY));

			float curBottom = minY;
			for (int i = 0; i < tags.Count; i++)
			{
				RightEdgeTag t = tags[i];
				float y = Math.Max(curBottom, t.IdealY);
				t.DrawY = y;
				curBottom = y + t.Height + 3f;
			}

			if (curBottom > maxY)
			{
				float nextTop = maxY;
				for (int i = tags.Count - 1; i >= 0; i--)
				{
					RightEdgeTag t = tags[i];
					if (t.DrawY + t.Height > nextTop)
						t.DrawY = Math.Max(minY, nextTop - t.Height);
					nextTop = t.DrawY - 3f;
				}
			}

			bool modernUi = IsModernRichUi;
			float boxWidth = 182f;
			float boxLeft = panelRight - boxWidth - 4f;
			for (int i = 0; i < tags.Count; i++)
			{
				RightEdgeTag t = tags[i];
				if (t.DrawY < minY - 2f || t.DrawY + t.Height > maxY + 2f)
					continue;

				string dispTagText = t.Text ?? string.Empty;
				if (dispTagText.Length > 28)
				{
					dispTagText = dispTagText
						.Replace("ValueAreaHigh", "VAH")
						.Replace("ValueAreaLow", "VAL");
				}

				RectangleF bgRect = new RectangleF(boxLeft, t.DrawY, boxWidth, t.Height);
				if (modernUi)
				{
					float idealMidY = Math.Max(minY, Math.Min(maxY, t.IdealY + t.Height * 0.5f));
					float drawMidY = t.DrawY + t.Height * 0.5f;
					RenderTarget.DrawLine(new Vector2(boxLeft - 6f, idealMidY), new Vector2(boxLeft, drawMidY), t.BorderBrush ?? t.Brush, 0.95f);
					var tagRr = new RoundedRectangle { Rect = bgRect, RadiusX = 4.0f, RadiusY = 4.0f };
					RenderTarget.FillRoundedRectangle(tagRr, t.FillBrush ?? bgBrush);
					RenderTarget.DrawRoundedRectangle(tagRr, t.BorderBrush ?? borderBrush, t.BorderBrush != null ? 1.0f : 0.8f);
					RenderTarget.FillRectangle(new RectangleF(boxLeft + 1.6f, t.DrawY + 2.4f, 2.8f, Math.Max(2f, t.Height - 4.8f)), t.Brush);
					RectangleF tagTxtClip = new RectangleF(boxLeft + 5f, t.DrawY, boxWidth - 8f, t.Height);
					RenderTarget.PushAxisAlignedClip(tagTxtClip, AntialiasMode.PerPrimitive);
					try
					{
						DrawText(dispTagText, boxLeft + 5f, t.DrawY, boxWidth - 8f, t.Height, rightFormat, t.Brush);
					}
					finally
					{
						RenderTarget.PopAxisAlignedClip();
					}
				}
				else
				{
					RenderTarget.FillRectangle(bgRect, t.FillBrush ?? bgBrush);
					RenderTarget.DrawRectangle(bgRect, t.BorderBrush ?? borderBrush, t.BorderBrush != null ? 1.0f : 0.7f);
					RectangleF tagTxtClip = new RectangleF(boxLeft + 3f, t.DrawY, boxWidth - 6f, t.Height);
					RenderTarget.PushAxisAlignedClip(tagTxtClip, AntialiasMode.PerPrimitive);
					try
					{
						DrawText(dispTagText, boxLeft + 3f, t.DrawY, boxWidth - 6f, t.Height, rightFormat, t.Brush);
					}
					finally
					{
						RenderTarget.PopAxisAlignedClip();
					}
				}
			}
		}

		private void DrawAK10XSummaryCard(
			DayProfile day, DayProfile prevDay, List<DayProfile> allDays, int idx,
			int poc, int vah, int val, double step, double avgIbRange,
			float xLeft, float desiredTopY, float cardWidth,
			DxBrush bgBrush, DxBrush borderBrush, DxBrush btnBgBrush,
			DxBrush whiteBrush, DxBrush textBrush, DxBrush orangeBrush, DxBrush yellowBrush, DxBrush greenBrush, DxBrush cyanBrush,
			DxTextFormat format, DxTextFormat btnFormat)
		{
			bool isMin = IsSummaryBoxMinimized;
			const float lineHeight = 12.2f;
			int lineCount = isMin ? 2 : 9;
			float boxHeight = lineCount * lineHeight + 8f;
			float chartBottom = (float)(ChartPanel.Y + ChartPanel.H) - 25f;
			float boxTop = Math.Min(desiredTopY, chartBottom - boxHeight - 2f);
			float topBoxGuardY = (ShowTopLeftBox && !IsTopLeftBoxMinimized && ShowDtrVsAtr) ? 102f : 86f;
			boxTop = Math.Max((float)ChartPanel.Y + topBoxGuardY, boxTop);

			bool modernUi = IsModernRichUi;
			RectangleF cardRect = new RectangleF(xLeft, boxTop, cardWidth, boxHeight);
			if (modernUi)
			{
				var cardRr = new RoundedRectangle { Rect = cardRect, RadiusX = 6.5f, RadiusY = 6.5f };
				RenderTarget.FillRoundedRectangle(cardRr, bgBrush);
				using (var hdrTint = new DxBrush(RenderTarget, new SharpDX.Color(30, 41, 59, 138)))
				using (var rowStripe = new DxBrush(RenderTarget, new SharpDX.Color(30, 41, 59, 62)))
				{
					RenderTarget.FillRoundedRectangle(new RoundedRectangle
					{
						Rect = new RectangleF(xLeft + 1.5f, boxTop + 1.5f, cardWidth - 3f, lineHeight + 4f),
						RadiusX = 5.0f,
						RadiusY = 5.0f
					}, hdrTint);
					if (!isMin)
					{
						for (int rs = 1; rs < 9; rs += 2)
						{
							RenderTarget.FillRectangle(new RectangleF(xLeft + 2f, boxTop + 4f + rs * lineHeight, cardWidth - 4f, lineHeight), rowStripe);
						}
					}
				}
				RenderTarget.DrawRoundedRectangle(cardRr, borderBrush, 1.0f);
			}
			else
			{
				RenderTarget.FillRectangle(cardRect, bgBrush);
				RenderTarget.DrawRectangle(cardRect, borderBrush, 1.0f);
			}

			float minMaxBtnW = 24f;
			float minMaxBtnH = 13f;
			RectangleF minMaxBtnRect = new RectangleF(xLeft + cardWidth - minMaxBtnW - 3f, boxTop + 3f, minMaxBtnW, minMaxBtnH);
			if (modernUi)
			{
				DrawModernPillButton(minMaxBtnRect, btnBgBrush, cyanBrush, isMin ? "+" : "—", btnFormat, cyanBrush, 3.8f, 0.9f);
			}
			else
			{
				RenderTarget.FillRectangle(minMaxBtnRect, btnBgBrush);
				RenderTarget.DrawRectangle(minMaxBtnRect, cyanBrush, 0.8f);
				DrawText(isMin ? "[+]" : "[-]", minMaxBtnRect.X, minMaxBtnRect.Y, minMaxBtnRect.Width, minMaxBtnRect.Height, btnFormat, cyanBrush);
			}
			activeChartButtons.Add(new ChartButton
			{
				Rect = minMaxBtnRect,
				Action = ChartButtonAction.ToggleSummaryMinMax,
				PrimaryKey = day.ProfileKey
			});

			bool openGreen;
			string line1 = GetOpeningType(day, prevDay, step, out openGreen);

			double ibRange = (day.InitialBalanceHigh != double.MinValue && day.InitialBalanceLow != double.MaxValue && day.InitialBalanceHigh >= day.InitialBalanceLow)
				? (day.InitialBalanceHigh - day.InitialBalanceLow)
				: 0;
			string ibSizeTag = ibRange < avgIbRange * 0.85 ? "Small" : (ibRange > avgIbRange * 1.25 ? "Wide" : "Normal");
			string line2 = string.Format(CultureInfo.InvariantCulture, "IBR: {0} - {1}", FormatPrice(ibRange), ibSizeTag);

			double ibH = day.InitialBalanceHigh != double.MinValue ? day.InitialBalanceHigh : day.High;
			double ibL = day.InitialBalanceLow != double.MaxValue ? day.InitialBalanceLow : day.Low;
			string line3 = string.Format(CultureInfo.InvariantCulture, "IR: {0} - {1}", FormatPrice(ibH), FormatPrice(ibL));
			string line4 = string.Format(CultureInfo.InvariantCulture, "PoC: {0}", FormatPrice(poc * step));
			string line5 = string.Format(CultureInfo.InvariantCulture, "VA: {0} - {1}", FormatPrice(vah * step), FormatPrice(val * step));
			string line6 = string.Format(CultureInfo.InvariantCulture, "Vol: {0}", FormatVolume(day.TotalVolume));

			int maStart = Math.Max(0, idx - 9);
			double volMa = 0;
			int maCount = 0;
			for (int k = maStart; k <= idx && k < allDays.Count; k++)
			{
				volMa += allDays[k].TotalVolume;
				maCount++;
			}
			if (maCount > 0) volMa /= maCount;
			string line7 = string.Format(CultureInfo.InvariantCulture, "Vol MA (10): {0}", FormatVolume(volMa));

			int rangeSteps = Math.Max(1, (int)Math.Round((day.High - day.Low) / step));
			string line8 = string.Format(CultureInfo.InvariantCulture, "{0} - {1} ({2})", FormatPrice(day.High), FormatPrice(day.Low), rangeSteps);

			bool dayTypeGreen;
			string line9 = ClassifyDayType(day, poc, vah, val, step, out dayTypeGreen);

			if (isMin)
			{
				string minLine1 = string.Format(CultureInfo.InvariantCulture, "{0} | PoC: {1}", day.SessionName, FormatPrice(poc * step));
				string minLine2 = string.Format(CultureInfo.InvariantCulture, "VA: {0}-{1} | V: {2}", FormatPrice(vah * step), FormatPrice(val * step), FormatVolume(day.TotalVolume));
				DrawText(minLine1, xLeft + 4f, boxTop + 4f, cardWidth - minMaxBtnW - 10f, lineHeight, format, whiteBrush);
				DrawText(minLine2, xLeft + 4f, boxTop + 4f + lineHeight, cardWidth - 8f, lineHeight, format, yellowBrush);
				return;
			}

			string[] lines = new string[] { line1, line2, line3, line4, line5, line6, line7, line8, line9 };
			DxBrush[] brushes = new DxBrush[]
			{
				openGreen ? greenBrush : whiteBrush,
				orangeBrush,
				textBrush,
				whiteBrush,
				textBrush,
				textBrush,
				yellowBrush,
				yellowBrush,
				dayTypeGreen ? greenBrush : orangeBrush
			};

			for (int l = 0; l < lines.Length; l++)
			{
				float maxLineW = (l == 0) ? (cardWidth - minMaxBtnW - 10f) : (cardWidth - 6f);
				DrawText(lines[l], xLeft + 4f, boxTop + 4f + l * lineHeight, maxLineW, lineHeight, format, brushes[l]);
			}
		}

		private void DrawTopLeftInfoBox(
			DayProfile latestDay, DayProfile prevDay, double step,
			DxBrush bgBrush, DxBrush borderBrush, DxBrush btnBgBrush,
			DxBrush whiteBrush, DxBrush textBrush, DxBrush orangeBrush, DxBrush yellowBrush, DxBrush greenBrush, DxBrush redBrush, DxBrush cyanBrush,
			DxTextFormat format, DxTextFormat btnFormat)
		{
			bool isMin = IsTopLeftBoxMinimized;
			float boxX = (float)ChartPanel.X + 8f;
			float boxY = (float)ChartPanel.Y + 6f;
			float lineH = 15.0f;

			SharpDX.Direct2D1.Bitmap logoBmp = ShowLogoInTopBox ? GetOrCreateLogoBitmap(RenderTarget) : null;
			bool hasLogo = logoBmp != null;
			float logoSize = isMin ? 22f : 52f;
			float logoPadLeft = hasLogo ? (logoSize + 14f) : 0f;

			string instName = Instrument == null ? "NSE / Indian Market" : Instrument.FullName;
			string tpoMode = TpoSize <= 0 ? " (Auto & Real-time)" : " (Manual)";
			int expectedTpos = latestDay != null && latestDay.ExpectedPeriodCount > 0 ? latestDay.ExpectedPeriodCount : 13;
			MarketProfilePeriodMode pMode = EffectiveProfileMode;
			string distName = pMode == MarketProfilePeriodMode.Weekly
				? string.Format(CultureInfo.InvariantCulture, "Weekly Profile ({0} TPO)", expectedTpos)
				: (pMode == MarketProfilePeriodMode.Monthly
					? string.Format(CultureInfo.InvariantCulture, "Monthly Profile ({0} TPO)", expectedTpos)
					: string.Format(CultureInfo.InvariantCulture, "Indian Day Profile ({0} TPO)", expectedTpos));

			Tuple<string, DxBrush>[] line1Segments = new Tuple<string, DxBrush>[]
			{
				Tuple.Create("AK10X Market Profile: ", textBrush),
				Tuple.Create(instName, orangeBrush),
				Tuple.Create(" ● LIVE ACCELPIX", greenBrush),
				Tuple.Create(", TPOSize: ", textBrush),
				Tuple.Create(FormatPrice(step) + tpoMode, greenBrush),
				Tuple.Create(", Distribution: ", textBrush),
				Tuple.Create(distName, orangeBrush),
				Tuple.Create(", TPO Interval: ", textBrush),
				Tuple.Create(TpoIntervalMinutes + " min", orangeBrush)
			};

			string otfText, roundText;
			bool otfUp;
			ComputeOneTimeFraming(latestDay, TpoIntervalMinutes, out otfText, out otfUp, out roundText);

			float line1Width = MeasureSegmentedLine(lineH, format, line1Segments);
			float otfWidth = MeasureSegmentedLine(lineH, format, Tuple.Create(otfText, whiteBrush));
			float btnW = 28f;
			float zoomBtnW = 26f;
			float navBtnW = 48f;
			float btnH = 15.0f;
			float navGroupW = navBtnW * 3f + zoomBtnW * 2f + 16f;
			float maxBoxW = Math.Max(480f, (float)ChartPanel.W - 208f);
			float rightRow1W = isMin ? (navGroupW + 6f + btnW) : btnW;
			if (6f + logoPadLeft + line1Width + 14f + rightRow1W + 8f > maxBoxW)
			{
				string shortMode = TpoSize <= 0 ? " (Auto)" : " (Man)";
				line1Segments = new Tuple<string, DxBrush>[]
				{
					Tuple.Create("AK10X MP: ", textBrush),
					Tuple.Create(instName, orangeBrush),
					Tuple.Create(" ● LIVE", greenBrush),
					Tuple.Create(", Step: ", textBrush),
					Tuple.Create(FormatPrice(step) + shortMode, greenBrush),
					Tuple.Create(", ", textBrush),
					Tuple.Create(distName, orangeBrush),
					Tuple.Create(", ", textBrush),
					Tuple.Create(TpoIntervalMinutes + "m", orangeBrush)
				};
				line1Width = MeasureSegmentedLine(lineH, format, line1Segments);
			}
			string dtrHeaderText = string.Empty;
			string dtrValueText = string.Empty;
			int dtrPct = 0;
			float dtrHdrW = 0f;
			float dtrValW = 0f;
			float dtrBadgeH = 17f;
			bool showDtrRow = !isMin && ShowDtrVsAtr;
			if (showDtrRow)
			{
				ComputeDtrVsAtrMetrics(latestDay, out dtrHeaderText, out dtrValueText, out dtrPct);
				if (ShowDtrAtrHeader)
				{
					dtrHdrW = Math.Max(84f, (float)Math.Ceiling(MeasureSegmentedLine(dtrBadgeH, btnFormat, Tuple.Create(dtrHeaderText, whiteBrush))) + 16f);
				}
				dtrValW = Math.Max(142f, (float)Math.Ceiling(MeasureSegmentedLine(dtrBadgeH, btnFormat, Tuple.Create(dtrValueText, whiteBrush))) + 18f);
			}
			float dtrRowTotalW = showDtrRow ? ((ShowDtrAtrHeader ? (dtrHdrW + 3f) : 0f) + dtrValW) : 0f;

			float desiredW = isMin
				? (6f + logoPadLeft + line1Width + 14f + navGroupW + 6f + btnW + 8f)
				: Math.Max(
					Math.Max(
						6f + logoPadLeft + line1Width + 14f + btnW + 8f,
						6f + logoPadLeft + otfWidth + 16f + navGroupW + 8f),
					6f + logoPadLeft + dtrRowTotalW + 14f);
			float boxW = Math.Min(maxBoxW, Math.Max(isMin ? 560f : 580f, desiredW));
			float boxH = isMin ? (lineH + 12f) : (showDtrRow ? (lineH * 4 + dtrBadgeH + 13f) : (lineH * 4 + 12f));
			lastTopLeftHudRightX = boxX + boxW;
			bool modernUi = IsModernRichUi;

			RectangleF rect = new RectangleF(boxX, boxY, boxW, boxH);
			if (modernUi)
			{
				var hudRr = new RoundedRectangle { Rect = rect, RadiusX = 8.0f, RadiusY = 8.0f };
				RenderTarget.FillRoundedRectangle(hudRr, bgBrush);
				RenderTarget.DrawRoundedRectangle(hudRr, borderBrush, 1.1f);
				RenderTarget.DrawLine(new Vector2(boxX + 10f, boxY + 0.8f), new Vector2(boxX + boxW - 10f, boxY + 0.8f), cyanBrush, 1.35f);
			}
			else
			{
				RenderTarget.FillRectangle(rect, bgBrush);
				RenderTarget.DrawRectangle(rect, borderBrush, 1.0f);
			}

			if (hasLogo)
			{
				float logoX = boxX + 7f;
				float logoY = boxY + (boxH - logoSize) * 0.5f;
				RectangleF logoRect = new RectangleF(logoX, logoY, logoSize, logoSize);
				RenderTarget.DrawBitmap(logoBmp, logoRect, 1.0f, BitmapInterpolationMode.Linear);
				if (modernUi)
				{
					var logoRing = new Ellipse(new Vector2(logoX + logoSize * 0.5f, logoY + logoSize * 0.5f), logoSize * 0.49f, logoSize * 0.49f);
					RenderTarget.DrawEllipse(logoRing, cyanBrush, 1.25f);
				}

				float divX = boxX + logoSize + 13f;
				RenderTarget.DrawLine(new Vector2(divX, boxY + 5f), new Vector2(divX, boxY + boxH - 5f), borderBrush, 0.85f);
			}

			RectangleF minMaxBtnRect = new RectangleF((float)Math.Floor(boxX + boxW - btnW - 6f), (float)Math.Floor(boxY + 4f), btnW, btnH);
			if (modernUi)
				DrawModernPillButton(minMaxBtnRect, btnBgBrush, cyanBrush, isMin ? "+" : "—", btnFormat, cyanBrush, 4.2f, 1.0f);
			else
				DrawCrispButton(minMaxBtnRect, btnBgBrush, cyanBrush, isMin ? "[+]" : "[-]", btnFormat, cyanBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = minMaxBtnRect,
				Action = ChartButtonAction.ToggleTopLeftMinMax,
				PrimaryKey = "TopLeftInfoBox"
			});

			float navRowY = isMin ? (float)Math.Floor(boxY + 4f) : (float)Math.Floor(boxY + 22f);
			float navRightEdge = isMin ? (minMaxBtnRect.X - 6f) : (boxX + boxW - 6f);

			RectangleF liveBtnRect = new RectangleF((float)Math.Floor(navRightEdge - navBtnW), navRowY, navBtnW, btnH);
			if (modernUi)
				DrawModernPillButton(liveBtnRect, btnBgBrush, greenBrush, "Live ●", btnFormat, greenBrush, 4.2f, 1.0f);
			else
				DrawCrispButton(liveBtnRect, btnBgBrush, greenBrush, "Live >>", btnFormat, greenBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = liveBtnRect,
				Action = ChartButtonAction.ScrollLiveDay,
				PrimaryKey = "ScrollLive"
			});

			RectangleF nextBtnRect = new RectangleF((float)Math.Floor(liveBtnRect.X - navBtnW - 4f), navRowY, navBtnW, btnH);
			if (modernUi)
				DrawModernPillButton(nextBtnRect, btnBgBrush, cyanBrush, "Next ›", btnFormat, cyanBrush, 4.2f, 1.0f);
			else
				DrawCrispButton(nextBtnRect, btnBgBrush, cyanBrush, "Next >", btnFormat, cyanBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = nextBtnRect,
				Action = ChartButtonAction.ScrollNextDays,
				PrimaryKey = "ScrollNext"
			});

			RectangleF pastBtnRect = new RectangleF((float)Math.Floor(nextBtnRect.X - navBtnW - 4f), navRowY, navBtnW, btnH);
			if (modernUi)
				DrawModernPillButton(pastBtnRect, btnBgBrush, orangeBrush, "‹ Past", btnFormat, orangeBrush, 4.2f, 1.0f);
			else
				DrawCrispButton(pastBtnRect, btnBgBrush, orangeBrush, "< Past", btnFormat, orangeBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = pastBtnRect,
				Action = ChartButtonAction.ScrollPastDays,
				PrimaryKey = "ScrollPast"
			});

			RectangleF zoomOutBtnRect = new RectangleF((float)Math.Floor(pastBtnRect.X - zoomBtnW - 4f), navRowY, zoomBtnW, btnH);
			if (modernUi)
				DrawModernPillButton(zoomOutBtnRect, btnBgBrush, cyanBrush, "Z-", btnFormat, cyanBrush, 4.2f, 1.0f);
			else
				DrawCrispButton(zoomOutBtnRect, btnBgBrush, cyanBrush, "Z-", btnFormat, cyanBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = zoomOutBtnRect,
				Action = ChartButtonAction.ZoomOutVertical,
				PrimaryKey = "ZoomOutVertical"
			});

			RectangleF zoomInBtnRect = new RectangleF((float)Math.Floor(zoomOutBtnRect.X - zoomBtnW - 4f), navRowY, zoomBtnW, btnH);
			if (modernUi)
				DrawModernPillButton(zoomInBtnRect, btnBgBrush, cyanBrush, "Z+", btnFormat, cyanBrush, 4.2f, 1.0f);
			else
				DrawCrispButton(zoomInBtnRect, btnBgBrush, cyanBrush, "Z+", btnFormat, cyanBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = zoomInBtnRect,
				Action = ChartButtonAction.ZoomInVertical,
				PrimaryKey = "ZoomInVertical"
			});

			float textX = boxX + 6f + logoPadLeft;
			float line1ClipRight = isMin ? (zoomInBtnRect.X - 6f) : (minMaxBtnRect.X - 6f);
			if (line1ClipRight > textX + 10f)
			{
				RenderTarget.PushAxisAlignedClip(new RectangleF(textX, boxY + 2f, line1ClipRight - textX, lineH + 4f), AntialiasMode.PerPrimitive);
				try
				{
					DrawSegmentedLine(textX, boxY + 4f, lineH, format, line1Segments);
				}
				finally
				{
					RenderTarget.PopAxisAlignedClip();
				}
			}

			if (isMin)
				return;

			float line2MaxW = Math.Max(40f, zoomInBtnRect.X - textX - 8f);
			DrawText(otfText, textX, boxY + 4f + lineH, line2MaxW, lineH, format, otfUp ? greenBrush : redBrush);
			DrawText(roundText, textX, boxY + 4f + lineH * 2, boxW - logoPadLeft - 14f, lineH, format, yellowBrush);

			double refClose = prevDay != null && prevDay.Close > 0 ? prevDay.Close : (latestDay != null ? latestDay.Open : 0);
			double chg = latestDay != null ? latestDay.Close - refClose : 0;
			double chgPct = refClose != 0 ? (chg / refClose) * 100.0 : 0.0;
			string ohlcPart = latestDay != null
				? string.Format(CultureInfo.InvariantCulture, "O: {0}, H: {1}, L: {2}, C: {3} ",
					FormatPrice(latestDay.Open), FormatPrice(latestDay.High), FormatPrice(latestDay.Low), FormatPrice(latestDay.Close))
				: "O: 0.00, H: 0.00, L: 0.00, C: 0.00 ";
			string chgPart = string.Format(CultureInfo.InvariantCulture, "({0} : {1:0.00}%)",
				FormatPrice(chg), chgPct);
			string volPart = latestDay != null ? FormatVolume(latestDay.TotalVolume) : "0";

			DrawSegmentedLine(textX, boxY + 4f + lineH * 3, lineH, format,
				Tuple.Create(ohlcPart, whiteBrush),
				Tuple.Create(chgPart, chg >= 0 ? greenBrush : redBrush),
				Tuple.Create(", V: ", whiteBrush),
				Tuple.Create(volPart, orangeBrush));

			if (showDtrRow)
			{
				float dtrRowY = (float)Math.Floor(boxY + 4f + lineH * 4 + 3f);
				float curBadgeX = (float)Math.Floor(textX);

				SharpDX.Color valFillColor;
				SharpDX.Color valBorderColor;
				if (dtrPct <= 70)
				{
					valFillColor = new SharpDX.Color(22, 152, 72, 246);
					valBorderColor = new SharpDX.Color(62, 226, 124, 255);
				}
				else if (dtrPct >= 90)
				{
					valFillColor = new SharpDX.Color(214, 46, 46, 246);
					valBorderColor = new SharpDX.Color(252, 108, 108, 255);
				}
				else
				{
					valFillColor = new SharpDX.Color(218, 112, 16, 246);
					valBorderColor = new SharpDX.Color(252, 168, 58, 255);
				}

				using (var hdrFillBrush = new DxBrush(RenderTarget, new SharpDX.Color(156, 77, 176, 246)))
				using (var hdrBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(210, 145, 226, 255)))
				using (var valFillBrush = new DxBrush(RenderTarget, valFillColor))
				using (var valBorderBrush = new DxBrush(RenderTarget, valBorderColor))
				{
					if (ShowDtrAtrHeader)
					{
						RectangleF hdrRect = new RectangleF(curBadgeX, dtrRowY, dtrHdrW, dtrBadgeH);
						DrawCrispButton(hdrRect, hdrFillBrush, hdrBorderBrush, dtrHeaderText, btnFormat, whiteBrush);
						curBadgeX += dtrHdrW + 3f;
					}

					RectangleF valRect = new RectangleF(curBadgeX, dtrRowY, dtrValW, dtrBadgeH);
					DrawCrispButton(valRect, valFillBrush, valBorderBrush, dtrValueText, btnFormat, whiteBrush);
				}
			}
		}

		private void DrawTopRightProfileDropdown(
			float panelRight, float panelTop,
			DxBrush bgBrush, DxBrush borderBrush, DxBrush btnBgBrush, DxBrush btnActiveBgBrush,
			DxBrush whiteBrush, DxBrush textBrush, DxBrush orangeBrush, DxBrush cyanBrush,
			DxTextFormat labelFormat, DxTextFormat btnFormat)
		{
			MarketProfilePeriodMode currentMode = EffectiveProfileMode;
			string selectedText = currentMode == MarketProfilePeriodMode.Weekly
				? "Weekly"
				: (currentMode == MarketProfilePeriodMode.Monthly ? "Monthly" : "Daily");

			float topY = panelTop + 8f;
			float rightEdge = panelRight - 14f;
			float dropW = 104f;
			float dropH = 22f;
			float labelW = 68f;
			float dropX = rightEdge - dropW;
			float labelX = dropX - labelW - 4f;

			bool modernUi = IsModernRichUi;
			RectangleF barRect = new RectangleF(labelX - 6f, topY - 2f, labelW + dropW + 14f, dropH + 4f);
			RectangleF dropBtnRect = new RectangleF(dropX, topY, dropW, dropH);
			if (modernUi)
			{
				var barRr = new RoundedRectangle { Rect = barRect, RadiusX = 6.5f, RadiusY = 6.5f };
				RenderTarget.FillRoundedRectangle(barRr, bgBrush);
				RenderTarget.DrawRoundedRectangle(barRr, borderBrush, 1.0f);
				DrawText("PROFILE", labelX, topY, labelW, dropH, labelFormat, cyanBrush);
				DrawModernPillButton(dropBtnRect, isProfileDropdownOpen ? btnActiveBgBrush : btnBgBrush, cyanBrush, selectedText + (isProfileDropdownOpen ? "  ▴" : "  ▾"), btnFormat, whiteBrush, 5.0f, 1.15f);
			}
			else
			{
				RenderTarget.FillRectangle(barRect, bgBrush);
				RenderTarget.DrawRectangle(barRect, borderBrush, 1.0f);

				DrawText("PROFILE", labelX, topY, labelW, dropH, labelFormat, orangeBrush);

				DrawCrispButton(dropBtnRect, isProfileDropdownOpen ? btnActiveBgBrush : btnBgBrush, cyanBrush, selectedText + "  ▼", btnFormat, whiteBrush);
			}

			// Register the entire PROFILE [ Daily ▼ ] barRect so clicking anywhere on PROFILE or the dropdown box toggles the menu
			activeChartButtons.Add(new ChartButton
			{
				Rect = barRect,
				Action = ChartButtonAction.ToggleProfileDropdown,
				PrimaryKey = "ProfileDropdownToggle"
			});

			if (isProfileDropdownOpen)
			{
				string[] optionLabels = new string[] { "Daily", "Weekly", "Monthly" };
				MarketProfilePeriodMode[] optionModes = new MarketProfilePeriodMode[]
				{
					MarketProfilePeriodMode.Daily,
					MarketProfilePeriodMode.Weekly,
					MarketProfilePeriodMode.Monthly
				};
				ChartButtonAction[] optionActions = new ChartButtonAction[]
				{
					ChartButtonAction.SelectProfileDaily,
					ChartButtonAction.SelectProfileWeekly,
					ChartButtonAction.SelectProfileMonthly
				};

				float itemH = 22f;
				float menuTopY = dropBtnRect.Bottom + 3f;
				RectangleF menuBgRect = new RectangleF(dropX, menuTopY, dropW, itemH * optionLabels.Length + 4f);
				if (modernUi)
				{
					var menuRr = new RoundedRectangle { Rect = menuBgRect, RadiusX = 6.0f, RadiusY = 6.0f };
					RenderTarget.FillRoundedRectangle(menuRr, bgBrush);
					RenderTarget.DrawRoundedRectangle(menuRr, cyanBrush, 1.15f);
				}
				else
				{
					RenderTarget.FillRectangle(menuBgRect, bgBrush);
					RenderTarget.DrawRectangle(menuBgRect, cyanBrush, 1.1f);
				}

				for (int m = 0; m < optionLabels.Length; m++)
				{
					bool isSelected = (currentMode == optionModes[m]);
					RectangleF itemRect = new RectangleF(dropX + 2f, menuTopY + 2f + m * itemH, dropW - 4f, itemH - 2f);
					if (modernUi)
					{
						DrawModernPillButton(
							itemRect,
							isSelected ? btnActiveBgBrush : btnBgBrush,
							isSelected ? cyanBrush : borderBrush,
							isSelected ? ("● " + optionLabels[m]) : optionLabels[m],
							btnFormat,
							isSelected ? cyanBrush : textBrush,
							4.5f,
							1.0f);
					}
					else
					{
						DrawCrispButton(
							itemRect,
							isSelected ? btnActiveBgBrush : btnBgBrush,
							isSelected ? cyanBrush : borderBrush,
							isSelected ? ("● " + optionLabels[m]) : optionLabels[m],
							btnFormat,
							isSelected ? cyanBrush : textBrush);
					}

					activeChartButtons.Add(new ChartButton
					{
						Rect = itemRect,
						Action = optionActions[m],
						PrimaryKey = "ProfileOption_" + optionLabels[m]
					});
				}
			}
		}

		private void DrawModernStudioBackdrop(
			float panelLeft, float panelTop, float panelRight, float panelBottom,
			float canvasTopY, float rightDockW,
			DxBrush gridBrush, DxTextFormat microFormat, DxBrush accentBrush)
		{
			if (RenderTarget == null)
				return;

			float w = Math.Max(40f, panelRight - panelLeft);
			float h = Math.Max(40f, panelBottom - panelTop);
			var stops = new SharpDX.Direct2D1.GradientStop[]
			{
				new SharpDX.Direct2D1.GradientStop { Position = 0.0f, Color = new SharpDX.Color(6, 10, 19, 255) },
				new SharpDX.Direct2D1.GradientStop { Position = 0.52f, Color = new SharpDX.Color(11, 18, 33, 255) },
				new SharpDX.Direct2D1.GradientStop { Position = 1.0f, Color = new SharpDX.Color(7, 12, 22, 255) }
			};
			using (var stopCol = new SharpDX.Direct2D1.GradientStopCollection(RenderTarget, stops))
			using (var bgGrad = new SharpDX.Direct2D1.LinearGradientBrush(
				RenderTarget,
				new SharpDX.Direct2D1.LinearGradientBrushProperties
				{
					StartPoint = new Vector2(panelLeft, panelTop),
					EndPoint = new Vector2(panelLeft, panelBottom)
				},
				stopCol))
			using (var dockBrush = new DxBrush(RenderTarget, new SharpDX.Color(7, 12, 22, 236)))
			using (var dockLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(56, 189, 248, 52)))
			{
				RenderTarget.FillRectangle(new RectangleF(panelLeft, panelTop, w, h), bgGrad);

				float rightLaneX = Math.Max(panelLeft + 60f, panelRight - rightDockW - 4f);
				for (float gy = canvasTopY + 28f; gy < panelBottom - 30f; gy += 54f)
				{
					RenderTarget.DrawLine(new Vector2(panelLeft + 2f, gy), new Vector2(rightLaneX, gy), gridBrush, 1.0f);
				}

				RenderTarget.FillRectangle(new RectangleF(rightLaneX, canvasTopY - 2f, panelRight - rightLaneX, Math.Max(10f, panelBottom - canvasTopY + 2f)), dockBrush);
				RenderTarget.DrawLine(new Vector2(rightLaneX, canvasTopY - 2f), new Vector2(rightLaneX, panelBottom), dockLineBrush, 1.0f);
			}
		}

		private void DrawTopRightTpoCountdownBox(
			List<DayProfile> allDays,
			float panelRight,
			float boxY,
			float boxH,
			float rightDockW,
			bool modernUi)
		{
			if (RenderTarget == null || rightDockW < 60f || boxH < 14f)
				return;

			float boxW = rightDockW - 4f;
			float boxX = panelRight - boxW - 3f;
			RectangleF boxRect = new RectangleF(boxX, boxY, boxW, boxH);

			DateTime nowClock;
			bool streamConnected = (State == State.Realtime);
			try
			{
				if (Connection.PlaybackConnection != null && Connection.PlaybackConnection.Status == ConnectionStatus.Connected)
				{
					nowClock = Connection.PlaybackConnection.Now;
					streamConnected = true;
				}
				else
				{
					nowClock = DateTime.Now;
				}
			}
			catch
			{
				nowClock = DateTime.Now;
			}

			DayProfile latestDay = (allDays != null && allDays.Count > 0) ? allDays[allDays.Count - 1] : null;
			int dispLetterIdx = latestDay != null ? Math.Max(0, Math.Min(LetterSequence.Length - 1, latestDay.LastPeriod)) : 0;
			string timerStr = "CLOSED";
			bool isLiveTpo = false;
			bool isUrgent = false;

			double bracketMin = Math.Max(1.0, (double)TpoIntervalMinutes);
			double bracketSec = Math.Max(60.0, bracketMin * 60.0);

			if (EffectiveProfileMode == MarketProfilePeriodMode.Daily)
			{
				DateTime effOpen = DateTime.MinValue;
				DateTime dayClose = DateTime.MinValue;
				int expPeriods = 13;

				if (latestDay != null && nowClock >= latestDay.EffectiveOpenTime && nowClock < latestDay.SessionCloseTime)
				{
					effOpen = latestDay.EffectiveOpenTime;
					dayClose = latestDay.SessionCloseTime;
					expPeriods = Math.Max(1, latestDay.ExpectedPeriodCount);
				}
				else
				{
					DateTime dDate, dStart, dClose;
					int pIdx, ep;
					if (TryGetDaySession(nowClock, out dDate, out dStart, out dClose, out pIdx, out ep) && nowClock >= dStart && nowClock < dClose)
					{
						effOpen = dStart;
						dayClose = dClose;
						expPeriods = Math.Max(1, ep);
					}
				}

				if (effOpen > DateTime.MinValue && nowClock >= effOpen && nowClock < dayClose)
				{
					double elapsedSec = Math.Max(0.0, (nowClock - effOpen).TotalSeconds);
					int curIdx = Math.Max(0, Math.Min(expPeriods - 1, (int)Math.Floor(elapsedSec / bracketSec)));
					DateTime curPeriodEnd = effOpen.AddSeconds((curIdx + 1) * bracketSec);
					if (curPeriodEnd > dayClose)
						curPeriodEnd = dayClose;
					int remSec = Math.Max(0, (int)Math.Ceiling((curPeriodEnd - nowClock).TotalSeconds));

					dispLetterIdx = Math.Max(0, Math.Min(LetterSequence.Length - 1, curIdx));
					isLiveTpo = streamConnected || (latestDay != null && (nowClock - latestDay.LastSampleTime).TotalMinutes < bracketMin + 5.0);
					isUrgent = isLiveTpo && remSec > 0 && remSec <= Math.Min(60, Math.Max(10, (int)(bracketSec * 0.08)));

					int mm = remSec / 60;
					int ss = remSec % 60;
					timerStr = remSec >= 3600
						? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", remSec / 3600, (remSec % 3600) / 60, ss)
						: string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", mm, ss);
				}
			}
			else if (latestDay != null)
			{
				int expT = Math.Max(1, latestDay.ExpectedPeriodCount);
				int prnT = Math.Min(expT, Math.Max(latestDay.PeriodSlices.Count, latestDay.LastPeriod + 1));
				timerStr = string.Format(CultureInfo.InvariantCulture, "{0}/{1} TPO", prnT, expT);
				isLiveTpo = streamConnected;
			}

			string runLetter = LetterSequence[dispLetterIdx].ToString();
			string chipLabel = "TPO " + runLetter;
			SharpDX.Color letCol = LetterColors[dispLetterIdx % LetterColors.Length];

			SharpDX.Color bgCol = isUrgent
				? new SharpDX.Color(48, 28, 8, 254)
				: (isLiveTpo ? new SharpDX.Color(10, 22, 36, 254) : new SharpDX.Color(12, 20, 32, 252));
			SharpDX.Color borderCol = isUrgent
				? new SharpDX.Color(251, 191, 36, 245)
				: (isLiveTpo ? new SharpDX.Color(20, 184, 166, 225) : new SharpDX.Color(56, 76, 98, 230));
			SharpDX.Color chipFillCol = new SharpDX.Color((int)letCol.R, (int)letCol.G, (int)letCol.B, 46);
			SharpDX.Color chipBorderCol = new SharpDX.Color((int)letCol.R, (int)letCol.G, (int)letCol.B, 215);
			SharpDX.Color timerTextCol = isUrgent
				? new SharpDX.Color(254, 240, 138, 255)
				: (isLiveTpo ? new SharpDX.Color(255, 255, 255, 255) : new SharpDX.Color(203, 213, 225, 255));
			SharpDX.Color iconCol = isUrgent
				? new SharpDX.Color(251, 191, 36, 255)
				: (isLiveTpo ? new SharpDX.Color(34, 211, 238, 255) : new SharpDX.Color(125, 211, 252, 235));

			var dw = Core.Globals.DirectWriteFactory;
			using (var bgBrush = new DxBrush(RenderTarget, bgCol))
			using (var borderBrush = new DxBrush(RenderTarget, borderCol))
			using (var chipFillBrush = new DxBrush(RenderTarget, chipFillCol))
			using (var chipBorderBrush = new DxBrush(RenderTarget, chipBorderCol))
			using (var chipTextBrush = new DxBrush(RenderTarget, letCol))
			using (var timerTextBrush = new DxBrush(RenderTarget, timerTextCol))
			using (var iconBrush = new DxBrush(RenderTarget, iconCol))
			using (var chipFmt = new DxTextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.ExtraBold, SharpDX.DirectWrite.FontStyle.Normal, 10.4f))
			using (var timerFmt = new DxTextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.ExtraBold, SharpDX.DirectWrite.FontStyle.Normal, 12.2f))
			{
				chipFmt.TextAlignment = TextAlignment.Center;
				chipFmt.ParagraphAlignment = ParagraphAlignment.Center;
				chipFmt.WordWrapping = WordWrapping.NoWrap;

				timerFmt.TextAlignment = TextAlignment.Center;
				timerFmt.ParagraphAlignment = ParagraphAlignment.Center;
				timerFmt.WordWrapping = WordWrapping.NoWrap;

				var outerRr = new RoundedRectangle { Rect = boxRect, RadiusX = 5.0f, RadiusY = 5.0f };
				RenderTarget.FillRoundedRectangle(outerRr, bgBrush);
				RenderTarget.DrawRoundedRectangle(outerRr, borderBrush, 1.35f);

				float cy = boxRect.Y + boxRect.Height * 0.5f;
				float curX = boxRect.X + 9f;

				// Crisp Vector Clock Icon
				float clkR = 5.6f;
				float clkCx = curX + clkR;
				RenderTarget.DrawEllipse(new Ellipse(new Vector2(clkCx, cy), clkR, clkR), iconBrush, 1.45f);
				RenderTarget.DrawLine(new Vector2(clkCx, cy), new Vector2(clkCx, cy - 3.3f), iconBrush, 1.45f);
				RenderTarget.DrawLine(new Vector2(clkCx, cy), new Vector2(clkCx + 2.6f, cy + 1.2f), iconBrush, 1.45f);
				curX = clkCx + clkR + 6.5f;

				// Running TPO Letter Badge ([ TPO F ])
				float chipW = 58f;
				float chipH = Math.Min(22f, Math.Max(16f, boxRect.Height - 9f));
				float chipY = cy - chipH * 0.5f;
				var chipRr = new RoundedRectangle { Rect = new RectangleF(curX, chipY, chipW, chipH), RadiusX = 4.2f, RadiusY = 4.2f };
				RenderTarget.FillRoundedRectangle(chipRr, chipFillBrush);
				RenderTarget.DrawRoundedRectangle(chipRr, chipBorderBrush, 1.2f);
				RenderTarget.DrawText(chipLabel, chipFmt, new RectangleF(curX, chipY - 1f, chipW, chipH + 2f), chipTextBrush, DrawTextOptions.NoSnap);
				curX += chipW + 4f;

				// Remaining TPO Countdown (MM:SS)
				float remW = Math.Max(28f, (boxRect.Right - 5f) - curX);
				RenderTarget.DrawText(timerStr, timerFmt, new RectangleF(curX, boxRect.Y - 1f, remW, boxRect.Height + 2f), timerTextBrush, DrawTextOptions.NoSnap);
			}
		}

		private void DrawModernUiToggleIcon(float panelLeft, float panelTop, float panelRight)
		{
			if (RenderTarget == null)
				return;

			bool active = IsModernRichUi;
			float btnW = 30f;
			float btnH = 26f;
			float rawX = (ShowTopLeftBox && lastTopLeftHudRightX > panelLeft + 40f)
				? (lastTopLeftHudRightX + 7f)
				: (panelLeft + 8f);
			float iconX = Math.Min(rawX, panelRight - 232f);
			float iconY = panelTop + 6f;

			RectangleF iconRect = new RectangleF(iconX, iconY, btnW, btnH);
			var prevAa = RenderTarget.AntialiasMode;
			RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;
			try
			{
				using (var glowBrush = new DxBrush(RenderTarget, active ? new SharpDX.Color(14, 165, 233, 64) : new SharpDX.Color(56, 189, 248, 32)))
				using (var bgBrush = new DxBrush(RenderTarget, active ? new SharpDX.Color(12, 68, 96, 250) : new SharpDX.Color(16, 25, 42, 248)))
				using (var borderBrush = new DxBrush(RenderTarget, active ? new SharpDX.Color(56, 224, 248, 255) : new SharpDX.Color(56, 189, 248, 215)))
				using (var barBrush = new DxBrush(RenderTarget, active ? new SharpDX.Color(125, 211, 252, 255) : new SharpDX.Color(148, 163, 184, 235)))
				using (var pocBarBrush = new DxBrush(RenderTarget, new SharpDX.Color(251, 191, 36, 255)))
				using (var starBrush = new DxBrush(RenderTarget, active ? new SharpDX.Color(253, 224, 71, 255) : new SharpDX.Color(56, 189, 248, 255)))
				{
					var haloRr = new RoundedRectangle
					{
						Rect = new RectangleF(iconX - 1.5f, iconY - 1.5f, btnW + 3f, btnH + 3f),
						RadiusX = 7.0f,
						RadiusY = 7.0f
					};
					RenderTarget.FillRoundedRectangle(haloRr, glowBrush);

					var btnRr = new RoundedRectangle { Rect = iconRect, RadiusX = 5.8f, RadiusY = 5.8f };
					RenderTarget.FillRoundedRectangle(btnRr, bgBrush);
					RenderTarget.DrawRoundedRectangle(btnRr, borderBrush, active ? 1.45f : 1.15f);

					float[] barWidths = new float[] { 5.5f, 9.5f, 14.5f, 10.5f, 6.5f };
					float bx = iconX + 5.5f;
					float byStart = iconY + 5.2f;
					for (int r = 0; r < 5; r++)
					{
						var rowRr = new RoundedRectangle
						{
							Rect = new RectangleF(bx, byStart + r * 3.3f, barWidths[r], 2.2f),
							RadiusX = 1.0f,
							RadiusY = 1.0f
						};
						RenderTarget.FillRoundedRectangle(rowRr, r == 2 ? pocBarBrush : barBrush);
					}

					float sx = iconX + btnW - 6.8f;
					float sy = iconY + 6.8f;
					float rOuter = 3.8f;
					float rInner = 1.25f;
					using (var starGeo = new SharpDX.Direct2D1.PathGeometry(RenderTarget.Factory))
					{
						using (var sink = starGeo.Open())
						{
							sink.BeginFigure(new Vector2(sx, sy - rOuter), FigureBegin.Filled);
							sink.AddLine(new Vector2(sx + rInner, sy - rInner));
							sink.AddLine(new Vector2(sx + rOuter, sy));
							sink.AddLine(new Vector2(sx + rInner, sy + rInner));
							sink.AddLine(new Vector2(sx, sy + rOuter));
							sink.AddLine(new Vector2(sx - rInner, sy + rInner));
							sink.AddLine(new Vector2(sx - rOuter, sy));
							sink.AddLine(new Vector2(sx - rInner, sy - rInner));
							sink.EndFigure(FigureEnd.Closed);
							sink.Close();
						}
						RenderTarget.FillGeometry(starGeo, starBrush);
					}
				}
			}
			finally
			{
				RenderTarget.AntialiasMode = prevAa;
			}

			activeChartButtons.Add(new ChartButton
			{
				Rect = iconRect,
				Action = ChartButtonAction.ToggleModernRichUi,
				PrimaryKey = "ToggleModernRichUi"
			});
		}

		private void DrawModernPillButton(RectangleF rawRect, DxBrush fillBrush, DxBrush borderBrush, string text, DxTextFormat textFormat, DxBrush textBrush, float radius = 4.5f, float borderWidth = 1.0f)
		{
			if (RenderTarget == null)
				return;

			var prevAa = RenderTarget.AntialiasMode;
			RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;
			try
			{
				var rr = new RoundedRectangle { Rect = rawRect, RadiusX = radius, RadiusY = radius };
				if (fillBrush != null)
					RenderTarget.FillRoundedRectangle(rr, fillBrush);
				if (borderBrush != null && borderWidth > 0f)
					RenderTarget.DrawRoundedRectangle(rr, borderBrush, borderWidth);
			}
			finally
			{
				RenderTarget.AntialiasMode = prevAa;
			}

			if (!string.IsNullOrEmpty(text))
				DrawText(text, rawRect.X, rawRect.Y, rawRect.Width, rawRect.Height, textFormat, textBrush);
		}

		// Renders a crisp 4-sided button with pixel-snapped edges so the top, bottom, left, and right border lines are 100% solid and visible
		private void DrawCrispButton(RectangleF rawRect, DxBrush fillBrush, DxBrush borderBrush, string text, DxTextFormat textFormat, DxBrush textBrush)
		{
			float x0 = (float)Math.Floor(rawRect.X) + 0.5f;
			float y0 = (float)Math.Floor(rawRect.Y) + 0.5f;
			float w = Math.Max(6f, (float)Math.Round(rawRect.Width));
			float h = Math.Max(6f, (float)Math.Round(rawRect.Height));
			float x1 = x0 + w;
			float y1 = y0 + h;
			RectangleF snapped = new RectangleF(x0, y0, w, h);

			AntialiasMode prevAa = RenderTarget.AntialiasMode;
			RenderTarget.AntialiasMode = AntialiasMode.Aliased;
			try
			{
				RenderTarget.FillRectangle(snapped, fillBrush);
				RenderTarget.DrawRectangle(snapped, borderBrush, 1.0f);
				RenderTarget.DrawLine(new Vector2(x0, y0), new Vector2(x1, y0), borderBrush, 1.0f);
				RenderTarget.DrawLine(new Vector2(x0, y1), new Vector2(x1, y1), borderBrush, 1.0f);
				RenderTarget.DrawLine(new Vector2(x0, y0), new Vector2(x0, y1), borderBrush, 1.0f);
				RenderTarget.DrawLine(new Vector2(x1, y0), new Vector2(x1, y1), borderBrush, 1.0f);
			}
			finally
			{
				RenderTarget.AntialiasMode = prevAa;
			}

			if (!string.IsNullOrEmpty(text))
			{
				DrawText(text, x0, y0, w, h, textFormat, textBrush);
			}
		}

		private float MeasureSegmentedLine(float height, DxTextFormat format, params Tuple<string, DxBrush>[] segments)
		{
			float totalW = 0f;
			for (int i = 0; i < segments.Length; i++)
			{
				string txt = segments[i].Item1;
				if (string.IsNullOrEmpty(txt))
					continue;
				using (var layout = new DxTextLayout(Core.Globals.DirectWriteFactory, txt, format, 1000f, height))
				{
					totalW += layout.Metrics.WidthIncludingTrailingWhitespace;
				}
			}
			return totalW;
		}

		private void DrawSegmentedLine(float x, float y, float height, DxTextFormat format, params Tuple<string, DxBrush>[] segments)
		{
			float curX = x;
			for (int i = 0; i < segments.Length; i++)
			{
				string txt = segments[i].Item1;
				DxBrush brush = segments[i].Item2;
				if (string.IsNullOrEmpty(txt))
					continue;
				using (var layout = new DxTextLayout(Core.Globals.DirectWriteFactory, txt, format, 1000f, height))
				{
					RenderTarget.DrawTextLayout(new Vector2(curX, y), layout, brush, DrawTextOptions.NoSnap);
					curX += layout.Metrics.WidthIncludingTrailingWhitespace;
				}
			}
		}

		private void DrawTpoCellText(string text, float x, float y, float width, float height, DxTextFormat format, DxBrush brush)
		{
			if (width <= 1.0f || height <= 1.0f || string.IsNullOrEmpty(text))
				return;
			float vPad = height >= 11.0f ? 0.45f : (height >= 7.5f ? 0.2f : 0.05f);
			float hPad = width >= 9.5f ? 0.45f : (width >= 7.0f ? 0.2f : 0.05f);
			RectangleF cellRect = new RectangleF(x + hPad, y + vPad, Math.Max(1.0f, width - hPad * 2f), Math.Max(1.0f, height - vPad * 2f));
			RenderTarget.DrawText(text, format, cellRect, brush, DrawTextOptions.Clip);
		}

		private void DrawHairlineHorizontal(float x0, float x1, float rawY, DxBrush brush, float width = 0.55f, StrokeStyle stroke = null)
		{
			if (RenderTarget == null || brush == null || x1 <= x0)
				return;

			float snappedY = (float)Math.Floor(rawY) + 0.5f;
			var prevAa = RenderTarget.AntialiasMode;
			RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;
			try
			{
				if (stroke != null)
					RenderTarget.DrawLine(new Vector2(x0, snappedY), new Vector2(x1, snappedY), brush, width, stroke);
				else
					RenderTarget.DrawLine(new Vector2(x0, snappedY), new Vector2(x1, snappedY), brush, width);
			}
			finally
			{
				RenderTarget.AntialiasMode = prevAa;
			}
		}

		private void DrawText(string text, float x, float y, float width, float height, DxTextFormat format, DxBrush brush)
		{
			if (width <= 0 || height <= 0 || string.IsNullOrEmpty(text))
				return;
			RenderTarget.DrawText(text, format, new RectangleF(x, y, width, height), brush, DrawTextOptions.NoSnap | DrawTextOptions.Clip);
		}

		private static string FormatVolume(double volume)
		{
			if (volume >= 1000000)
				return (volume / 1000000.0).ToString("0.000", CultureInfo.InvariantCulture) + "M";
			if (volume >= 1000)
				return (volume / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) + "K";
			return volume.ToString("0", CultureInfo.InvariantCulture);
		}

		#region Parameters
		[Display(Name = "Profile Mode (Daily / Weekly / Monthly)", Description = "Default: Daily. Can also be switched live from the top-right PROFILE dropdown on the chart.", GroupName = "Market Profile", Order = 0)]
		public MarketProfilePeriodMode ProfileMode { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1000)]
		[Display(Name = "TPO size (0 = auto)", GroupName = "Market Profile", Order = 1)]
		public double TpoSize { get; set; }

		[Display(Name = "Enable Screen-Resolution Auto TPO Size", Description = "Default: Disabled (Recommended — Fixed Calculation Engine + Adaptive Rendering Engine: 100% consistent Auto TPO Size, VPoC, VAH/VAL, Single Prints, Poor & Swing High/Low across all monitors, with adaptive TPO font size, letter spacing, chart zoom, and vertical scrolling). Enable only if you want legacy screen-height row merging.", GroupName = "Market Profile", Order = 2)]
		public bool AutoScaleTpoByScreenResolution { get; set; }

		[NinjaScriptProperty]
		[Range(1, 240)]
		[Display(Name = "TPO interval (minutes)", GroupName = "Market Profile", Order = 3)]
		public int TpoIntervalMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(50, 90)]
		[Display(Name = "Value area (%)", GroupName = "Market Profile", Order = 4)]
		public int ValueAreaPercent { get; set; }

		[NinjaScriptProperty]
		[Range(1, 240)]
		[Display(Name = "Initial balance (minutes)", GroupName = "Market Profile", Order = 5)]
		public int InitialBalanceMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(1, 365)]
		[Display(Name = "Days to display", GroupName = "Market Profile", Order = 6)]
		public int DaysToDisplay { get; set; }

		// Indian Market Trading Hours (IST)
		[NinjaScriptProperty]
		[Display(Name = "Filter by trading hours", Description = "When enabled, includes bars between Market Open and Market Close IST", GroupName = "Indian Market Hours (IST)", Order = 0)]
		public bool FilterByTradingHours { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Market Open (HHmmss IST)", Description = "Default: 091500 (09:15 AM IST)", GroupName = "Indian Market Hours (IST)", Order = 1)]
		public int MarketOpen { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "Market Close (HHmmss IST)", Description = "Default: 153000 (03:30 PM IST)", GroupName = "Indian Market Hours (IST)", Order = 2)]
		public int MarketClose { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Session label", GroupName = "Indian Market Hours (IST)", Order = 3)]
		public string SessionLabel { get; set; }

		// Profile Display
		[NinjaScriptProperty]
		[Display(Name = "Split Latest / Live Day", Description = "Automatically expand Split Profile on the latest/live day", GroupName = "Profile Display", Order = 0)]
		public bool SplitLatestDay { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show split volume profile", GroupName = "Profile Display", Order = 1)]
		public bool ShowVolumeProfile { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show initial balance", GroupName = "Profile Display", Order = 2)]
		public bool ShowInitialBalance { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show single prints", GroupName = "Profile Display", Order = 3)]
		public bool ShowSinglePrints { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show poor high/low (Master)", GroupName = "Profile Display", Order = 4)]
		public bool ShowPoorHighLow { get; set; }

		[NinjaScriptProperty]
		[Range(100, 1000)]
		[Display(Name = "Profile width (pixels)", GroupName = "Profile Display", Order = 5)]
		public int ProfileWidth { get; set; }

		[NinjaScriptProperty]
		[Range(8, 20)]
		[Display(Name = "Font size", GroupName = "Profile Display", Order = 6)]
		public int FontSize { get; set; }

		// Virgin Lines & Levels (Complete Hide on All Past & Current Data when Unchecked)
		[Display(Name = "Virginity Break Rule", Description = "Select Virginity Break Rule for all Virgin Lines & Single Prints: CrossBased (Default — breaks immediately when price crosses/touches the line) or CloseBased (breaks only when a closed TPO closes above/below the line).", GroupName = "Virgin Lines & Levels", Order = 0)]
		public VirginityBreakRuleMode VirginityBreakRule { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin PoorHigh / ABPoorHigh", Description = "Show and extend PoorHigh and ABPoorHigh on closed days until broken. When unchecked, hides on all past and current days.", GroupName = "Virgin Lines & Levels", Order = 1)]
		public bool ShowPoorHigh { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin PoorLow / ABPoorLow", Description = "Show and extend PoorLow and ABPoorLow on closed days until broken. When unchecked, hides on all past and current days.", GroupName = "Virgin Lines & Levels", Order = 2)]
		public bool ShowPoorLow { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin DayHigh", Description = "Show and extend Virgin DayHigh on closed days only (never on running day) until broken.", GroupName = "Virgin Lines & Levels", Order = 3)]
		public bool ShowSwingHigh { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin DayLow", Description = "Show and extend Virgin DayLow on closed days only (never on running day) until broken.", GroupName = "Virgin Lines & Levels", Order = 4)]
		public bool ShowSwingLow { get; set; }

		[Display(Name = "Enable Virgin WeeklyHigh", Description = "Show and extend Virgin Weekly High (closed Monday to Friday week only) until broken.", GroupName = "Virgin Lines & Levels", Order = 5)]
		public bool ShowWeeklyHigh { get; set; }

		[Display(Name = "Enable Virgin WeeklyLow", Description = "Show and extend Virgin Weekly Low (closed Monday to Friday week only) until broken.", GroupName = "Virgin Lines & Levels", Order = 6)]
		public bool ShowWeeklyLow { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin ValueAreaHigh", Description = "Show ValueAreaHigh and extend until broken. When unchecked, hides ValueAreaHigh on all past and current days.", GroupName = "Virgin Lines & Levels", Order = 7)]
		public bool ShowVirginVAH { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin ValueAreaLow", Description = "Show ValueAreaLow and extend until broken. When unchecked, hides ValueAreaLow on all past and current days.", GroupName = "Virgin Lines & Levels", Order = 8)]
		public bool ShowVirginVAL { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin VPoC", Description = "Show Volume PoC (VPoC) and extend until broken. When unchecked, hides VPoC on all past and current days.", GroupName = "Virgin Lines & Levels", Order = 9)]
		public bool ShowVirginVPoC { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin VPPoC (TPO PoC)", Description = "Show TPO PoC (VPPoC) and extend until broken. When unchecked, hides VPPoC on all past and current days.", GroupName = "Virgin Lines & Levels", Order = 10)]
		public bool ShowVirginVPPoC { get; set; }

		[Display(Name = "Enable Virgin MidLine", Description = "Show Day MidLine on completed days and extend Virgin MidLine until broken. When unchecked, hides MidLine on all past and current days.", GroupName = "Virgin Lines & Levels", Order = 11)]
		public bool ShowVirginMidLine { get; set; }

		// Running Day Enable / Disable Controls (VAH, VAL, POC, PPoC)
		[Display(Name = "Enable Running Day Levels (Master)", Description = "Master switch to enable or disable ValueAreaHigh, ValueAreaLow, POC (VPoC), and PPoC (VPPoC) on the currently running day", GroupName = "Running Day Levels", Order = 0)]
		public bool EnableRunningDayLevels { get; set; }

		[Display(Name = "Enable Running Day ValueAreaHigh", Description = "Show ValueAreaHigh line and right-edge label on the currently running day", GroupName = "Running Day Levels", Order = 1)]
		public bool ShowRunningDayVAH { get; set; }

		[Display(Name = "Enable Running Day ValueAreaLow", Description = "Show ValueAreaLow line and right-edge label on the currently running day", GroupName = "Running Day Levels", Order = 2)]
		public bool ShowRunningDayVAL { get; set; }

		[Display(Name = "Enable Running Day POC (VPoC)", Description = "Show Volume PoC (POC) line and right-edge label on the currently running day", GroupName = "Running Day Levels", Order = 3)]
		public bool ShowRunningDayPOC { get; set; }

		[Display(Name = "Enable Running Day PPoC (VPPoC)", Description = "Show TPO PoC (PPoC) line and right-edge label on the currently running day", GroupName = "Running Day Levels", Order = 4)]
		public bool ShowRunningDayPPOC { get; set; }

		// Panels & Cards
		[Display(Name = "Enable Modern Classic Rich UI", Description = "Enable world-class smooth glassmorphic Market Profile UI (can also toggle live via the top studio icon next to the Info Box)", GroupName = "Display", Order = 0)]
		public bool EnableModernRichUi { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Bottom Summary Boxes", Description = "Enable or disable the 9-line day summary boxes at the bottom of the chart", GroupName = "Display", Order = 1)]
		public bool ShowSummary { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fix Summary Boxes at Bottom", Description = "When enabled, docks all day summary boxes in a fixed horizontal row at the bottom above the Date bar", GroupName = "Display", Order = 1)]
		public bool FixSummaryBoxAtBottom { get; set; }

		[Display(Name = "Fix Session Header at Top", Description = "When enabled (Default), docks all Day Details, [Split], and Split Profile headers in one clean Fixed Top Session Bar so they never cover price values or Virgin lines", GroupName = "Display", Order = 2)]
		public bool FixSessionHeaderAtTop { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Minimize Summary Boxes", Description = "Minimize all bottom day summary boxes to a compact 2-line bar (can also toggle via [-] / [+] button on chart)", GroupName = "Display", Order = 2)]
		public bool MinimizeSummaryBoxes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Top-Left Info Box", Description = "Enable or disable the top-left Market Profile info box", GroupName = "Display", Order = 3)]
		public bool ShowTopLeftBox { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Logo in Top-Left Box", Description = "Display the circular AK10X trader logo on the left side of the top-left info box", GroupName = "Display", Order = 4)]
		public bool ShowLogoInTopBox { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Minimize Top-Left Info Box", Description = "Minimize the top-left info box to a single-line header (can also toggle via [-] / [+] button on chart)", GroupName = "Display", Order = 5)]
		public bool MinimizeTopLeftBox { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable On-Chart [Split] Buttons", Description = "Enable or disable the interactive [Split] button above day profiles", GroupName = "Display", Order = 6)]
		public bool ShowChartButtons { get; set; }

		// DTR vs ATR (Daily / Weekly / Monthly True Range vs Smoothed ATR)
		[Display(Name = "Enable DTR vs ATR %", Description = "Display DTR/WTR/MTR vs smoothed ATR and percentage badge inside the top-left HUD box", GroupName = "DTR vs ATR", Order = 0)]
		public bool ShowDtrVsAtr { get; set; }

		[Range(1, 200)]
		[Display(Name = "ATR Length", Description = "ATR lookback period (Default: 14)", GroupName = "DTR vs ATR", Order = 1)]
		public int DtrAtrLength { get; set; }

		[Display(Name = "Smoothing Type", Description = "ATR smoothing method: RMA (Wilder), EMA, SMA, or WMA (Default: RMA)", GroupName = "DTR vs ATR", Order = 2)]
		public DtrAtrSmoothingType DtrAtrSmoothing { get; set; }

		[Display(Name = "Show DTR vs ATR Header", Description = "Display the purple DTR/WTR/MTR vs ATR header pill before the value badge", GroupName = "DTR vs ATR", Order = 3)]
		public bool ShowDtrAtrHeader { get; set; }
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AK10XIndiaMarketProfile[] cacheAK10XIndiaMarketProfile;
		public AK10XIndiaMarketProfile AK10XIndiaMarketProfile(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return AK10XIndiaMarketProfile(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}

		public AK10XIndiaMarketProfile AK10XIndiaMarketProfile(ISeries<double> input, double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			if (cacheAK10XIndiaMarketProfile != null)
				for (int idx = 0; idx < cacheAK10XIndiaMarketProfile.Length; idx++)
					if (cacheAK10XIndiaMarketProfile[idx] != null && cacheAK10XIndiaMarketProfile[idx].TpoSize == tpoSize && cacheAK10XIndiaMarketProfile[idx].TpoIntervalMinutes == tpoIntervalMinutes && cacheAK10XIndiaMarketProfile[idx].ValueAreaPercent == valueAreaPercent && cacheAK10XIndiaMarketProfile[idx].InitialBalanceMinutes == initialBalanceMinutes && cacheAK10XIndiaMarketProfile[idx].DaysToDisplay == daysToDisplay && cacheAK10XIndiaMarketProfile[idx].FilterByTradingHours == filterByTradingHours && cacheAK10XIndiaMarketProfile[idx].MarketOpen == marketOpen && cacheAK10XIndiaMarketProfile[idx].MarketClose == marketClose && cacheAK10XIndiaMarketProfile[idx].SessionLabel == sessionLabel && cacheAK10XIndiaMarketProfile[idx].SplitLatestDay == splitLatestDay && cacheAK10XIndiaMarketProfile[idx].ShowVolumeProfile == showVolumeProfile && cacheAK10XIndiaMarketProfile[idx].ShowInitialBalance == showInitialBalance && cacheAK10XIndiaMarketProfile[idx].ShowSinglePrints == showSinglePrints && cacheAK10XIndiaMarketProfile[idx].ShowPoorHighLow == showPoorHighLow && cacheAK10XIndiaMarketProfile[idx].ProfileWidth == profileWidth && cacheAK10XIndiaMarketProfile[idx].FontSize == fontSize && cacheAK10XIndiaMarketProfile[idx].ShowPoorHigh == showPoorHigh && cacheAK10XIndiaMarketProfile[idx].ShowPoorLow == showPoorLow && cacheAK10XIndiaMarketProfile[idx].ShowSwingHigh == showSwingHigh && cacheAK10XIndiaMarketProfile[idx].ShowSwingLow == showSwingLow && cacheAK10XIndiaMarketProfile[idx].ShowVirginVAH == showVirginVAH && cacheAK10XIndiaMarketProfile[idx].ShowVirginVAL == showVirginVAL && cacheAK10XIndiaMarketProfile[idx].ShowVirginVPoC == showVirginVPoC && cacheAK10XIndiaMarketProfile[idx].ShowVirginVPPoC == showVirginVPPoC && cacheAK10XIndiaMarketProfile[idx].ShowSummary == showSummary && cacheAK10XIndiaMarketProfile[idx].FixSummaryBoxAtBottom == fixSummaryBoxAtBottom && cacheAK10XIndiaMarketProfile[idx].MinimizeSummaryBoxes == minimizeSummaryBoxes && cacheAK10XIndiaMarketProfile[idx].ShowTopLeftBox == showTopLeftBox && cacheAK10XIndiaMarketProfile[idx].ShowLogoInTopBox == showLogoInTopBox && cacheAK10XIndiaMarketProfile[idx].MinimizeTopLeftBox == minimizeTopLeftBox && cacheAK10XIndiaMarketProfile[idx].ShowChartButtons == showChartButtons && cacheAK10XIndiaMarketProfile[idx].EqualsInput(input))
						return cacheAK10XIndiaMarketProfile[idx];
			return CacheIndicator<AK10XIndiaMarketProfile>(new AK10XIndiaMarketProfile(){ TpoSize = tpoSize, TpoIntervalMinutes = tpoIntervalMinutes, ValueAreaPercent = valueAreaPercent, InitialBalanceMinutes = initialBalanceMinutes, DaysToDisplay = daysToDisplay, FilterByTradingHours = filterByTradingHours, MarketOpen = marketOpen, MarketClose = marketClose, SessionLabel = sessionLabel, SplitLatestDay = splitLatestDay, ShowVolumeProfile = showVolumeProfile, ShowInitialBalance = showInitialBalance, ShowSinglePrints = showSinglePrints, ShowPoorHighLow = showPoorHighLow, ProfileWidth = profileWidth, FontSize = fontSize, ShowPoorHigh = showPoorHigh, ShowPoorLow = showPoorLow, ShowSwingHigh = showSwingHigh, ShowSwingLow = showSwingLow, ShowVirginVAH = showVirginVAH, ShowVirginVAL = showVirginVAL, ShowVirginVPoC = showVirginVPoC, ShowVirginVPPoC = showVirginVPPoC, ShowSummary = showSummary, FixSummaryBoxAtBottom = fixSummaryBoxAtBottom, MinimizeSummaryBoxes = minimizeSummaryBoxes, ShowTopLeftBox = showTopLeftBox, ShowLogoInTopBox = showLogoInTopBox, MinimizeTopLeftBox = minimizeTopLeftBox, ShowChartButtons = showChartButtons }, input, ref cacheAK10XIndiaMarketProfile);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AK10XIndiaMarketProfile AK10XIndiaMarketProfile(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return indicator.AK10XIndiaMarketProfile(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}

		public Indicators.AK10XIndiaMarketProfile AK10XIndiaMarketProfile(ISeries<double> input , double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return indicator.AK10XIndiaMarketProfile(input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AK10XIndiaMarketProfile AK10XIndiaMarketProfile(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return indicator.AK10XIndiaMarketProfile(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}

		public Indicators.AK10XIndiaMarketProfile AK10XIndiaMarketProfile(ISeries<double> input , double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return indicator.AK10XIndiaMarketProfile(input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}
	}
}

#endregion
