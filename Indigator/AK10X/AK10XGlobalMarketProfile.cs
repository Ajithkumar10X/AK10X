// AK10X Global Three-Session (Asia: 20 TPOs, London: 11 TPOs, New York: 15 TPOs = 46 TPOs) Market Profile for NinjaTrader 8.
// Includes Buying & Selling Single Prints, Virgin Level Extensions (PoorHigh/Low, ABPoorHigh/Low, DayHigh/Low, WeeklyHigh/Low, VPoC, VPPoC),
// TPO-Close Virginity Break Engine, configurable From/To session times (IST), and a 2-pass non-overlapping renderer.
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
	public enum MarketProfilePeriodMode
	{
		Daily = 0,
		Weekly = 1,
		Monthly = 2
	}

	public enum VirginityBreakRuleMode
	{
		CrossBased = 0,
		CloseBased = 1
	}

	public class AK10XGlobalMarketProfile : Indicator
	{
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
			public int SessionId;
			public string SessionName;
			public int SessionSlot;
			public int SessionCount;
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
			public bool IsMerged;
			public bool IsSplitExpanded;
			public List<string> SourceProfileKeys = new List<string>();
			public List<SinglePrintZone> SinglePrintZones = new List<SinglePrintZone>();
			public int DataRevision;
			public DayProfile CachedContinuousProfile;
			public int CachedContinuousRevision = -1;
			public double CachedContinuousStep;
			public DayProfile CachedGroupedProfile;
			public int CachedGroupedRevision = -1;
			public double CachedGroupedBaseStep;
			public int CachedGroupedMult = -1;
			public bool AnalyticsValid;
			public double AnalyticsStep;
			public double AnalyticsVaPct;
		}

		private enum ChartButtonAction
		{
			ToggleSplit,
			MergeWithNext,
			UnmergeGroup,
			ToggleMergeDay,
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
			public List<string> GroupKeys;
		}

		private sealed class DateColumnGroup
		{
			public DateTime Date;
			public List<DayProfile> Sessions = new List<DayProfile>();
			public float Width;
			public float LeftX;
			public float RightX;
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

		private struct ProfileSession
		{
			public int Id;
			public string Name;
			public string ShortName;
			public int OpenSeconds;
			public int CloseSeconds;
			public string FromLabel;
			public string ToLabel;
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

		private static readonly SharpDX.Color[] SessionColors = new SharpDX.Color[]
		{
			new SharpDX.Color(248, 192, 62),  // 0: Asia (Gold)
			new SharpDX.Color(72, 228, 138),  // 1: London (Emerald Green)
			new SharpDX.Color(248, 118, 158), // 2: New York (Coral Pink)
			new SharpDX.Color(92, 196, 248),  // 3: India / Custom (Sky Blue)
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
		private readonly HashSet<string> mergedWithNextKeys = new HashSet<string>(StringComparer.Ordinal);
		private readonly HashSet<string> unmergedOverrideKeys = new HashSet<string>(StringComparer.Ordinal);
		private readonly List<ChartButton> activeChartButtons = new List<ChartButton>();
		private ProfileSession[] activeSessions = new ProfileSession[0];
		private int lastHiddenChartBar = -1;
		private int lastVolumeBarIndex = -1;
		private double lastCumulativeVolume;
		private double cachedPriceStep;
		private int lastRebuiltSampleCount;
		private bool didAutoAdjustChartProps;
		private bool isMouseHooked;
		private ChartControl hookedChartControl;
		private System.Windows.Window hookedOwnerWindow;
		private System.Windows.Input.MouseButtonEventHandler mouseLeftDownHandler;
		private int lastHandledMouseTimestamp = int.MinValue;
		private DateTime lastButtonClickUtc = DateTime.MinValue;
		private bool isPotentialCanvasDrag;
		private bool isDraggingCanvas;
		private float dragStartMouseX;
		private float dragStartMouseY;
		private float dragStartScrollPx;
		private double dragStartPanOffset;
		private float lastRenderTargetW;
		private float lastRenderTargetH;
		private ChartScale lastRenderedChartScale;
		private double verticalZoomFactor = 1.0;
		private double verticalPanPriceOffset;
		private double lastBaseMidPrice;
		private double lastBasePriceSpan = 10.0;
		private float lastUsableHeightPx = 600f;
		private bool? runtimeMinimizeTopLeftOverride;
		private bool? runtimeMinimizeSummaryOverride;
		private bool? runtimeModernUiOverride;
		private float lastTopLeftHudRightX;
		private MarketProfilePeriodMode? runtimeProfileModeOverride;
		private bool isProfileDropdownOpen;
		private bool isBackfillInFlight;
		private bool isProviderSyncInFlight;
		private int highestBackfillDaysRequested;
		private BarsRequest activeLiveBarsReq;
		private bool isConnectionStatusHooked;
		private DateTime lastConnSyncUtc = DateTime.MinValue;
		private DateTime lastLiveRefreshUtc = DateTime.MinValue;
		private DateTime lastGapHealRequestUtc = DateTime.MinValue;
		private float maxScrollCached;
		private bool hasManualPixelScroll;
		private int lastTrackedFromIdx = -1;
		private int lastTrackedToIdx = -1;
		private float currentScrollPx = float.NaN;
		private SharpDX.Direct2D1.Bitmap cachedLogoBitmap;
		private SharpDX.Direct2D1.RenderTarget cachedLogoRenderTarget;
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
				Description = "AK10X Global 3-Session (Asia: 20 TPOs, London: 11 TPOs, New York: 15 TPOs) Market Profile for NinjaTrader 8.";
				Name = "AK10XGlobalMarketProfile";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				PaintPriceMarkers = false;
				IsAutoScale = true;
				ScaleJustification = ScaleJustification.Right;
				IsSuspendedWhileInactive = false;

				TpoSize = 0;
				AutoScaleTpoByScreenResolution = false;
				TpoIntervalMinutes = 30;
				ValueAreaPercent = 70;
				InitialBalanceMinutes = 60;
				DaysToDisplay = 30;
				ProfileMode = MarketProfilePeriodMode.Daily;

				// 3 Forex Sessions (IST) matching Forex Session TAT Calculation table:
				// 1. Asia:     03:30 AM - 01:30 PM IST (10 Hours = 600 Min = 20 TPO Brackets)
				// 2. London:   01:30 PM - 07:00 PM IST (5 Hours 30 Min = 330 Min = 11 TPO Brackets)
				// 3. New York: 07:00 PM - 02:30 AM IST Next Day (7 Hours 30 Min = 450 Min = 15 TPO Brackets)
				ShowAsia = true;
				AsiaFromTime = "03:30 AM";
				AsiaToTime = "01:30 PM";

				ShowLondon = true;
				LondonFromTime = "01:30 PM";
				LondonToTime = "07:00 PM";

				ShowNewYork = true;
				NewYorkFromTime = "07:00 PM";
				NewYorkToTime = "02:30 AM";

				SplitLatestSession = true;
				SplitAsiaSessions = false;
				SplitLondonSessions = false;
				SplitNewYorkSessions = false;

				MergeAsiaAndLondon = false;
				MergeLondonAndNewYork = false;
				MergeAllDaySessions = false;

				ShowIndianSession = false;
				MarketOpen = 91500;
				MarketClose = 153000;
				SessionLabel = "NSE Cash";

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

				EnableRunningSessionLevels = true;
				ShowRunningSessionVAH = true;
				ShowRunningSessionVAL = true;
				ShowRunningSessionPOC = true;
				ShowRunningSessionPPOC = true;

				ShowVolumeProfile = true;
				ShowInitialBalance = true;
				ShowSinglePrints = true;
				ShowPoorHighLow = true;
				EnableModernRichUi = false;
				ShowSummary = true;
				FixSummaryBoxAtBottom = true;
				FixSessionHeaderAtTop = true;
				MinimizeSummaryBoxes = false;
				ShowTopLeftBox = true;
				ShowLogoInTopBox = true;
				MinimizeTopLeftBox = false;
				ShowChartButtons = true;
				ProfileWidth = 240;
				FontSize = 11;
			}
			else if (State == State.Configure)
			{
				Calculate = Calculate.OnEachTick;
				AddDataSeries(BarsPeriodType.Minute, 1);
			}
			else if (State == State.DataLoaded)
			{
				lock (profileLock)
				{
					rawMinuteSamples.Clear();
					profiles.Clear();
					chartDayFirstBar.Clear();
					chartDayLastBar.Clear();
					manuallySplitProfileKeys.Clear();
					manuallyUnsplitProfileKeys.Clear();
					mergedWithNextKeys.Clear();
					unmergedOverrideKeys.Clear();
					activeChartButtons.Clear();
					activeSessions = GetProfileSessions();
					lastHiddenChartBar = -1;
					lastVolumeBarIndex = -1;
					lastCumulativeVolume = 0;
					cachedPriceStep = 0;
					lastRebuiltSampleCount = 0;
					didAutoAdjustChartProps = false;
					runtimeMinimizeTopLeftOverride = null;
					runtimeMinimizeSummaryOverride = null;
					runtimeProfileModeOverride = null;
					isProfileDropdownOpen = false;
					isBackfillInFlight = false;
					isProviderSyncInFlight = false;
					highestBackfillDaysRequested = 0;
					maxScrollCached = 0f;
					hasManualPixelScroll = false;
					lastTrackedFromIdx = -1;
					lastTrackedToIdx = -1;
					currentScrollPx = float.NaN;
					DisposeLogoBitmap();
				}
				EnsureMouseHookAttached();
			}
			else if (State == State.Realtime)
			{
				AttachLiveStreamAndProviderSync();
				TriggerHistoricalBackfillIfNeeded(Math.Max(42, DaysToDisplay));
				SyncRecentProviderMinutesAsync(3);
			}
			else if (State == State.Terminated)
			{
				DetachLiveStreamAndProviderSync();
				DisposeLogoBitmap();
				DetachMouseHook();
			}
		}

		public override void OnRenderTargetChanged()
		{
			DisposeLogoBitmap();
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

					// Clean circular alpha feather so outer square corners are 100% transparent in premultiplied BGRA
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
						var bmpProps = new BitmapProperties(
							new SharpDX.Direct2D1.PixelFormat(
								SharpDX.DXGI.Format.B8G8R8A8_UNorm,
								SharpDX.Direct2D1.AlphaMode.Premultiplied));
						cachedLogoBitmap = new SharpDX.Direct2D1.Bitmap(
							rt,
							new Size2(w, h),
							new SharpDX.DataPointer(ds.DataPointer, pixels.Length),
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
			if (isMouseHooked && hookedChartControl == ChartControl && (ChartControl.OwnerChart == null || hookedOwnerWindow == ChartControl.OwnerChart))
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
					cc.PreviewMouseMove -= OnChartPreviewMouseMove;
					cc.PreviewMouseLeftButtonUp -= OnChartPreviewMouseLeftButtonUp;
					cc.PreviewMouseWheel -= OnChartPreviewMouseWheel;
					cc.PreviewMouseMove += OnChartPreviewMouseMove;
					cc.PreviewMouseLeftButtonUp += OnChartPreviewMouseLeftButtonUp;
					cc.PreviewMouseWheel += OnChartPreviewMouseWheel;
					hookedChartControl = cc;
					isMouseHooked = true;
				}

				if (cc.OwnerChart != null && hookedOwnerWindow != cc.OwnerChart)
				{
					if (hookedOwnerWindow != null)
						hookedOwnerWindow.RemoveHandler(System.Windows.UIElement.PreviewMouseLeftButtonDownEvent, mouseLeftDownHandler);
					hookedOwnerWindow = cc.OwnerChart;
					hookedOwnerWindow.RemoveHandler(System.Windows.UIElement.PreviewMouseLeftButtonDownEvent, mouseLeftDownHandler);
					hookedOwnerWindow.AddHandler(System.Windows.UIElement.PreviewMouseLeftButtonDownEvent, mouseLeftDownHandler, true);
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
			var ownerWin = hookedOwnerWindow;
			var handler = mouseLeftDownHandler;
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
						try
						{
							if (ownerWin != null)
								ownerWin.RemoveHandler(System.Windows.UIElement.PreviewMouseLeftButtonDownEvent, handler);
						}
						catch { }
					}
					try { cc.PreviewMouseMove -= OnChartPreviewMouseMove; } catch { }
					try { cc.PreviewMouseLeftButtonUp -= OnChartPreviewMouseLeftButtonUp; } catch { }
					try { cc.PreviewMouseWheel -= OnChartPreviewMouseWheel; } catch { }
				});
			}
			hookedOwnerWindow = null;
			hookedChartControl = null;
			isMouseHooked = false;
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
						nextDaysToFetch = Math.Min(365, Math.Max(65, highestBackfillDaysRequested + 30));
					}
					currentScrollPx = Math.Max(0f, currentScrollPx - deltaStep);
				}
				else if (e.Delta < 0)
					currentScrollPx = Math.Min(Math.Max(0f, maxScrollCached), currentScrollPx + deltaStep);
			}
			if (needMorePast)
				TriggerHistoricalBackfillIfNeeded(nextDaysToFetch);

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

			float pxX = -99999f, pxY = -99999f, dipX = -99999f, dipY = -99999f;
			float panelPxX = -99999f, panelPxY = -99999f, panelDipX = -99999f, panelDipY = -99999f;
			if (e != null)
			{
				try
				{
					System.Windows.Point wpfPt = e.GetPosition(cc);
					dipX = (float)wpfPt.X;
					dipY = (float)wpfPt.Y;
					pxX = (float)ChartingExtensions.ConvertToHorizontalPixels(wpfPt.X, cc.PresentationSource);
					pxY = (float)ChartingExtensions.ConvertToVerticalPixels(wpfPt.Y, cc.PresentationSource);
				}
				catch { }
				try
				{
					System.Windows.Point panelPt = e.GetPosition(ChartPanel);
					panelDipX = (float)ChartPanel.X + (float)panelPt.X;
					panelDipY = (float)ChartPanel.Y + (float)panelPt.Y;
					panelPxX = (float)ChartPanel.X + (float)ChartingExtensions.ConvertToHorizontalPixels(panelPt.X, cc.PresentationSource);
					panelPxY = (float)ChartPanel.Y + (float)ChartingExtensions.ConvertToVerticalPixels(panelPt.Y, cc.PresentationSource);
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
					hitRect.Inflate(3f, 1.5f);

					if (hitRect.Contains(panelPxX, panelPxY) ||
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
						if (manuallySplitProfileKeys.Contains(btn.PrimaryKey))
						{
							manuallySplitProfileKeys.Remove(btn.PrimaryKey);
							manuallyUnsplitProfileKeys.Add(btn.PrimaryKey);
						}
						else if (manuallyUnsplitProfileKeys.Contains(btn.PrimaryKey))
						{
							manuallyUnsplitProfileKeys.Remove(btn.PrimaryKey);
							manuallySplitProfileKeys.Add(btn.PrimaryKey);
						}
						else
						{
							// Check if it was split by default; if PrimaryKey ends with "|ON", toggle off, else toggle on
							if (btn.PrimaryKey.EndsWith("|ON", StringComparison.Ordinal))
							{
								string realKey = btn.PrimaryKey.Substring(0, btn.PrimaryKey.Length - 3);
								manuallySplitProfileKeys.Remove(realKey);
								manuallyUnsplitProfileKeys.Add(realKey);
							}
							else
							{
								manuallyUnsplitProfileKeys.Remove(btn.PrimaryKey);
								manuallySplitProfileKeys.Add(btn.PrimaryKey);
							}
						}
					}
					break;

				case ChartButtonAction.MergeWithNext:
					if (btn.GroupKeys != null && btn.GroupKeys.Count > 0)
					{
						string lastKeyInGroup = btn.GroupKeys[btn.GroupKeys.Count - 1];
						unmergedOverrideKeys.Remove(lastKeyInGroup);
						mergedWithNextKeys.Add(lastKeyInGroup);
					}
					else if (!string.IsNullOrEmpty(btn.PrimaryKey))
					{
						unmergedOverrideKeys.Remove(btn.PrimaryKey);
						mergedWithNextKeys.Add(btn.PrimaryKey);
					}
					break;

				case ChartButtonAction.UnmergeGroup:
					if (btn.GroupKeys != null)
					{
						for (int k = 0; k < btn.GroupKeys.Count; k++)
						{
							mergedWithNextKeys.Remove(btn.GroupKeys[k]);
							unmergedOverrideKeys.Add(btn.GroupKeys[k]);
						}
					}
					break;

				case ChartButtonAction.ToggleMergeDay:
					if (btn.GroupKeys != null && btn.GroupKeys.Count > 1)
					{
						bool anyMerged = false;
						for (int k = 0; k < btn.GroupKeys.Count - 1; k++)
						{
							if (mergedWithNextKeys.Contains(btn.GroupKeys[k]))
								anyMerged = true;
						}
						if (anyMerged || MergeAllDaySessions)
						{
							for (int k = 0; k < btn.GroupKeys.Count - 1; k++)
							{
								mergedWithNextKeys.Remove(btn.GroupKeys[k]);
								unmergedOverrideKeys.Add(btn.GroupKeys[k]);
							}
						}
						else
						{
							for (int k = 0; k < btn.GroupKeys.Count - 1; k++)
							{
								unmergedOverrideKeys.Remove(btn.GroupKeys[k]);
								mergedWithNextKeys.Add(btn.GroupKeys[k]);
							}
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
					isProfileDropdownOpen = false;
					break;

				case ChartButtonAction.ScrollPastDays:
					hasManualPixelScroll = true;
					if (float.IsNaN(currentScrollPx)) currentScrollPx = maxScrollCached;
					currentScrollPx = Math.Max(0f, currentScrollPx - 320f);
					isProfileDropdownOpen = false;
					if (currentScrollPx <= 320f)
					{
						int nextBackfill = Math.Max(65, highestBackfillDaysRequested + 30);
						TriggerHistoricalBackfillIfNeeded(nextBackfill);
					}
					break;

				case ChartButtonAction.ScrollNextDays:
					hasManualPixelScroll = true;
					if (float.IsNaN(currentScrollPx)) currentScrollPx = maxScrollCached;
					currentScrollPx = Math.Min(Math.Max(0f, maxScrollCached), currentScrollPx + 320f);
					isProfileDropdownOpen = false;
					break;

				case ChartButtonAction.ScrollLiveDay:
					hasManualPixelScroll = false;
					currentScrollPx = float.MaxValue;
					ResetVerticalView();
					isProfileDropdownOpen = false;
					break;

				case ChartButtonAction.ZoomInVertical:
					ApplyVerticalZoomStep(0.80);
					isProfileDropdownOpen = false;
					break;

				case ChartButtonAction.ZoomOutVertical:
					ApplyVerticalZoomStep(1.25);
					isProfileDropdownOpen = false;
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
					TriggerHistoricalBackfillIfNeeded(65);
					break;

				case ChartButtonAction.SelectProfileMonthly:
					runtimeProfileModeOverride = MarketProfilePeriodMode.Monthly;
					ProfileMode = MarketProfilePeriodMode.Monthly;
					isProfileDropdownOpen = false;
					hasManualPixelScroll = false;
					currentScrollPx = float.MaxValue;
					ResetVerticalView();
					TriggerHistoricalBackfillIfNeeded(180);
					break;
			}
		}

		private void AttachLiveStreamAndProviderSync()
		{
			try
			{
				if (!isConnectionStatusHooked)
				{
					Connection.ConnectionStatusUpdate += OnDataFeedConnectionStatusUpdate;
					isConnectionStatusHooked = true;
				}
			}
			catch { }
		}

		private void DetachLiveStreamAndProviderSync()
		{
			try
			{
				if (isConnectionStatusHooked)
				{
					Connection.ConnectionStatusUpdate -= OnDataFeedConnectionStatusUpdate;
					isConnectionStatusHooked = false;
				}
				if (activeLiveBarsReq != null)
				{
					try
					{
						activeLiveBarsReq.Update -= OnLiveBarsRequestUpdate;
						activeLiveBarsReq.Dispose();
					}
					catch { }
					activeLiveBarsReq = null;
				}
			}
			catch { }
		}

		private void OnDataFeedConnectionStatusUpdate(object sender, ConnectionStatusEventArgs e)
		{
			if (e == null || State != State.Realtime)
				return;
			if (e.Status == ConnectionStatus.Connected || e.PriceStatus == ConnectionStatus.Connected)
			{
				DateTime nowUtc = DateTime.UtcNow;
				if ((nowUtc - lastConnSyncUtc).TotalSeconds < 6.0)
					return;
				lastConnSyncUtc = nowUtc;

				System.Threading.Tasks.Task.Run(async () =>
				{
					try
					{
						await System.Threading.Tasks.Task.Delay(1200).ConfigureAwait(false);
						if (State == State.Realtime)
							SyncRecentProviderMinutesAsync(3);
					}
					catch { }
				});
			}
		}

		protected override void OnMarketData(MarketDataEventArgs marketDataUpdate)
		{
			ProcessLiveMarketTick(marketDataUpdate);
		}

		private static int LowerBoundSampleTime(List<MinuteSample> list, DateTime target)
		{
			int lo = 0;
			int hi = list.Count;
			while (lo < hi)
			{
				int mid = lo + ((hi - lo) >> 1);
				if (list[mid].Time < target)
					lo = mid + 1;
				else
					hi = mid;
			}
			return lo;
		}

		private void ProcessLiveMarketTick(MarketDataEventArgs e)
		{
			if (e == null || State != State.Realtime)
				return;
			if (e.MarketDataType != MarketDataType.Last || e.Price <= 0)
				return;

			try
			{
				DateTime tickTime = e.Time > DateTime.MinValue ? e.Time : DateTime.Now;
				// Align to 1-minute bar-close timestamp convention (minute + 1 min when seconds > 0)
				DateTime minuteClose = new DateTime(tickTime.Year, tickTime.Month, tickTime.Day, tickTime.Hour, tickTime.Minute, 0, tickTime.Kind);
				if (tickTime.Second > 0 || tickTime.Millisecond > 0)
					minuteClose = minuteClose.AddMinutes(1);
				double tickVol = Math.Max(0, Convert.ToDouble(e.Volume));
				bool changed = false;

				lock (profileLock)
				{
					if (activeSessions == null || activeSessions.Length == 0)
						activeSessions = GetProfileSessions();
					if (activeSessions.Length == 0)
						return;

					if (rawMinuteSamples.Count > 0 && rawMinuteSamples[rawMinuteSamples.Count - 1].Time == minuteClose)
					{
						MinuteSample lastSample = rawMinuteSamples[rawMinuteSamples.Count - 1];
						lastSample.High = Math.Max(lastSample.High, e.Price);
						lastSample.Low = Math.Min(lastSample.Low, e.Price);
						lastSample.Close = e.Price;
						lastSample.Volume += tickVol;
						rawMinuteSamples[rawMinuteSamples.Count - 1] = lastSample;

						double step = GetPriceStep();
						MinuteSample delta = lastSample;
						delta.Volume = tickVol;
						AccumulateSampleIntoSessions(delta, step, isLiveTickUpdate: true);
						changed = true;
					}
					else if (rawMinuteSamples.Count == 0 || minuteClose > rawMinuteSamples[rawMinuteSamples.Count - 1].Time)
					{
						MinuteSample nextSample = new MinuteSample
						{
							Time = minuteClose,
							Open = e.Price,
							High = e.Price,
							Low = e.Price,
							Close = e.Price,
							Volume = tickVol,
							BarIndex = rawMinuteSamples.Count > 0 ? (rawMinuteSamples[rawMinuteSamples.Count - 1].BarIndex + 1) : 0
						};
						rawMinuteSamples.Add(nextSample);
						double step = GetPriceStep();
						AccumulateSampleIntoSessions(nextSample, step, isLiveTickUpdate: false);
						changed = true;
					}
				}

				if (changed)
					RequestThrottledChartRefresh(false);
			}
			catch { }
		}

		private void OnLiveBarsRequestUpdate(object sender, BarsUpdateEventArgs e)
		{
			if (e == null || e.BarsSeries == null || State != State.Realtime)
				return;

			try
			{
				bool changed = false;
				lock (profileLock)
				{
					if (activeSessions == null || activeSessions.Length == 0)
						activeSessions = GetProfileSessions();
					int count = e.BarsSeries.Count;
					if (count <= 0 || activeSessions.Length == 0)
						return;

					int idx = Math.Max(0, Math.Min(e.MaxIndex, count - 1));
					DateTime t = e.BarsSeries.GetTime(idx);
					double open = e.BarsSeries.GetOpen(idx);
					double high = e.BarsSeries.GetHigh(idx);
					double low = e.BarsSeries.GetLow(idx);
					double close = e.BarsSeries.GetClose(idx);
					double barVol = Math.Max(0, Convert.ToDouble(e.BarsSeries.GetVolume(idx)));

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
						AccumulateSampleIntoSessions(delta, step, isLiveTickUpdate: true);
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
						AccumulateSampleIntoSessions(next, step, isLiveTickUpdate: false);
						changed = true;
					}
				}

				if (changed)
					RequestThrottledChartRefresh(false);
			}
			catch { }
		}

		private void RequestThrottledChartRefresh(bool forceImmediate)
		{
			DateTime now = DateTime.UtcNow;
			if (!forceImmediate && (now - lastLiveRefreshUtc).TotalMilliseconds < 220.0)
				return;
			lastLiveRefreshUtc = now;

			if (System.Threading.Interlocked.CompareExchange(ref uiRefreshQueued, 1, 0) != 0)
				return;

			try
			{
				ChartControl cc = hookedChartControl ?? ChartControl;
				if (cc != null)
				{
					cc.Dispatcher.InvokeAsync(() =>
					{
						System.Threading.Interlocked.Exchange(ref uiRefreshQueued, 0);
						try { ForceRefresh(); cc.InvalidateVisual(); } catch { }
					}, System.Windows.Threading.DispatcherPriority.Render);
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

		private MergePolicy ResolveChartMergePolicy(Instrument inst)
		{
			try
			{
				if (ChartBars != null && ChartBars.Properties != null)
				{
					var mpProp = ChartBars.Properties.GetType().GetProperty("MergePolicy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
					if (mpProp != null && mpProp.PropertyType == typeof(MergePolicy))
					{
						MergePolicy chartMp = (MergePolicy)mpProp.GetValue(ChartBars.Properties, null);
						if (chartMp != MergePolicy.UseGlobalSettings)
							return chartMp;
					}
				}
				if (inst != null && inst.MasterInstrument != null && inst.MasterInstrument.MergePolicy != MergePolicy.UseGlobalSettings)
					return inst.MasterInstrument.MergePolicy;
				if (Core.Globals.MarketDataOptions != null)
					return Core.Globals.MarketDataOptions.GlobalMergePolicy;
			}
			catch { }
			return MergePolicy.MergeBackAdjusted;
		}

		private void SyncRecentProviderMinutesAsync(int daysBack = 3)
		{
			Instrument inst = (ChartBars != null && ChartBars.Bars != null && ChartBars.Bars.Instrument != null)
				? ChartBars.Bars.Instrument
				: Instrument;
			if (isProviderSyncInFlight || inst == null || State != State.Realtime)
				return;

			isProviderSyncInFlight = true;
			MergePolicy mergePol = ResolveChartMergePolicy(inst);
			DateTime toTime = DateTime.Now.AddDays(1);
			DateTime fromTime = DateTime.Now.AddDays(-Math.Max(1, Math.Min(5, daysBack)));

			System.Threading.Tasks.Task.Run(() =>
			{
				try
				{
					BarsRequest req = new BarsRequest(inst, fromTime, toTime);
					req.BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 1 };
					req.LookupPolicy = LookupPolicies.Provider;
					req.MergePolicy = mergePol;
					try
					{
						TradingHours th24 = TradingHours.Get("Default 24 x 7");
						if (th24 != null)
							req.TradingHours = th24;
						else if (inst.MasterInstrument != null && inst.MasterInstrument.TradingHours != null)
							req.TradingHours = inst.MasterInstrument.TradingHours;
					}
					catch { }

					req.Update += OnLiveBarsRequestUpdate;
					var oldReq = activeLiveBarsReq;
					activeLiveBarsReq = req;
					if (oldReq != null)
					{
						try { oldReq.Update -= OnLiveBarsRequestUpdate; oldReq.Dispose(); } catch { }
					}

					req.Request((bars, errorCode, errorMessage) =>
					{
						try
						{
							if (State != State.Realtime || errorCode != ErrorCode.NoError || bars == null || bars.Bars == null || bars.Bars.Count <= 0)
								return;

							bool addedOrChanged = false;
							lock (profileLock)
							{
								if (activeSessions == null || activeSessions.Length == 0)
									activeSessions = GetProfileSessions();
								int cnt = bars.Bars.Count;
								if (cnt > 0 && activeSessions.Length > 0)
								{
									Dictionary<DateTime, MinuteSample> byTime = new Dictionary<DateTime, MinuteSample>(cnt + rawMinuteSamples.Count);
									for (int i = 0; i < rawMinuteSamples.Count; i++)
									{
										MinuteSample ex = rawMinuteSamples[i];
										byTime[ex.Time] = ex;
									}
									for (int i = 0; i < cnt; i++)
									{
										DateTime t = bars.Bars.GetTime(i);
										double o = bars.Bars.GetOpen(i);
										double h = bars.Bars.GetHigh(i);
										double l = bars.Bars.GetLow(i);
										double c = bars.Bars.GetClose(i);
										double v = Math.Max(0, Convert.ToDouble(bars.Bars.GetVolume(i)));
										MinuteSample prevMs;
										if (byTime.TryGetValue(t, out prevMs))
										{
											if (h > prevMs.High || l < prevMs.Low || v > prevMs.Volume || Math.Abs(c - prevMs.Close) > 1e-8)
											{
												prevMs.High = Math.Max(prevMs.High, h);
												prevMs.Low = Math.Min(prevMs.Low, l);
												prevMs.Close = c;
												prevMs.Volume = Math.Max(prevMs.Volume, v);
												byTime[t] = prevMs;
												addedOrChanged = true;
											}
										}
										else
										{
											byTime[t] = new MinuteSample
											{
												Time = t,
												Open = o,
												High = h,
												Low = l,
												Close = c,
												Volume = v,
												BarIndex = i
											};
											addedOrChanged = true;
										}
									}

									if (addedOrChanged)
									{
										List<MinuteSample> merged = byTime.Values.OrderBy(s => s.Time).ToList();
										int maxSamples = 370 * 1440;
										if (merged.Count > maxSamples)
											merged.RemoveRange(0, merged.Count - maxSamples);
										for (int i = 0; i < merged.Count; i++)
										{
											MinuteSample ms = merged[i];
											ms.BarIndex = i;
											merged[i] = ms;
										}
										rawMinuteSamples.Clear();
										rawMinuteSamples.AddRange(merged);
										double step = ComputeAutoOrManualPriceStep();
										RebuildAllSessionProfiles(step);
									}
								}
							}

							if (addedOrChanged)
								RequestThrottledChartRefresh(true);
						}
						catch { }
						finally
						{
							isProviderSyncInFlight = false;
						}
					});
				}
				catch
				{
					isProviderSyncInFlight = false;
				}
			});
		}

		private void TriggerHistoricalBackfillIfNeeded(int daysNeeded)
		{
			Instrument inst = (ChartBars != null && ChartBars.Bars != null && ChartBars.Bars.Instrument != null)
				? ChartBars.Bars.Instrument
				: Instrument;
			if (isBackfillInFlight || inst == null || State == State.Terminated || daysNeeded <= highestBackfillDaysRequested)
				return;

			isBackfillInFlight = true;
			highestBackfillDaysRequested = daysNeeded;
			MergePolicy mergePol = ResolveChartMergePolicy(inst);
			DateTime toTime = DateTime.Now.AddDays(1);
			DateTime fromTime = toTime.AddDays(-Math.Max(5, daysNeeded));

			System.Threading.Tasks.Task.Run(() =>
			{
				try
				{
					BarsRequest req = new BarsRequest(inst, fromTime, toTime);
					req.BarsPeriod = new BarsPeriod
					{
						BarsPeriodType = BarsPeriodType.Minute,
						Value = daysNeeded > 60 ? 5 : 1
					};
					// Use fast local Repository for deep multi-week history so startup never stalls on 60k provider bars!
					req.LookupPolicy = LookupPolicies.Repository;
					req.MergePolicy = mergePol;
					try
					{
						TradingHours th24 = TradingHours.Get("Default 24 x 7");
						if (th24 != null)
							req.TradingHours = th24;
						else if (inst.MasterInstrument != null && inst.MasterInstrument.TradingHours != null)
							req.TradingHours = inst.MasterInstrument.TradingHours;
					}
					catch { }

					req.Request((bars, errorCode, errorMessage) =>
					{
						try
						{
							if (State == State.Terminated || errorCode != ErrorCode.NoError || bars == null || bars.Bars == null || bars.Bars.Count <= 0)
								return;

							bool prependedAny = false;
							lock (profileLock)
							{
								if (activeSessions == null || activeSessions.Length == 0)
									activeSessions = GetProfileSessions();
								DateTime earliestExisting = rawMinuteSamples.Count > 0 ? rawMinuteSamples[0].Time : DateTime.MaxValue;
								List<MinuteSample> olderSamples = new List<MinuteSample>();
								int cnt = bars.Bars.Count;
								for (int i = 0; i < cnt; i++)
								{
									DateTime t = bars.Bars.GetTime(i);
									if (t >= earliestExisting)
										break;
									olderSamples.Add(new MinuteSample
									{
										Time = t,
										Open = bars.Bars.GetOpen(i),
										High = bars.Bars.GetHigh(i),
										Low = bars.Bars.GetLow(i),
										Close = bars.Bars.GetClose(i),
										Volume = Math.Max(0, Convert.ToDouble(bars.Bars.GetVolume(i))),
										BarIndex = i
									});
								}

								if (olderSamples.Count > 0)
								{
									olderSamples.AddRange(rawMinuteSamples);
									int maxSamples = 370 * 1440;
									if (olderSamples.Count > maxSamples)
										olderSamples.RemoveRange(0, olderSamples.Count - maxSamples);
									for (int i = 0; i < olderSamples.Count; i++)
									{
										MinuteSample ms = olderSamples[i];
										ms.BarIndex = i;
										olderSamples[i] = ms;
									}
									rawMinuteSamples.Clear();
									rawMinuteSamples.AddRange(olderSamples);
									double step = ComputeAutoOrManualPriceStep();
									RebuildAllSessionProfiles(step);
									prependedAny = true;
								}
							}

							if (prependedAny)
								RequestThrottledChartRefresh(true);
						}
						catch { }
						finally
						{
							isBackfillInFlight = false;
						}
					});
				}
				catch
				{
					isBackfillInFlight = false;
				}
			});
		}

		public override string DisplayName
		{
			get { return string.Empty; }
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBars == null || CurrentBars.Length < 1)
				return;

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

				bool isPrimaryIntradayMin = BarsPeriods != null && BarsPeriods.Length > 0
					&& BarsPeriods[0].BarsPeriodType == BarsPeriodType.Minute
					&& BarsPeriods[0].Value >= 1
					&& BarsPeriods[0].Value <= Math.Max(1, TpoIntervalMinutes);

				lock (profileLock)
				{
					DateTime pTime = Times[0][0];
					DateTime chartDate = pTime.Date;
					if (!chartDayFirstBar.ContainsKey(chartDate))
						chartDayFirstBar[chartDate] = curPrimary;
					chartDayLastBar[chartDate] = curPrimary;

					// Fallback sync from Primary Series ONLY in Realtime when primary chart is Minute <= TpoIntervalMinutes and secondary series hasn't emitted this minute yet
					if (State == State.Realtime && isPrimaryIntradayMin)
					{
						if (activeSessions == null || activeSessions.Length == 0)
							activeSessions = GetProfileSessions();
						if (activeSessions.Length > 0 && (rawMinuteSamples.Count == 0 || pTime > rawMinuteSamples[rawMinuteSamples.Count - 1].Time))
						{
							MinuteSample pSample = new MinuteSample
							{
								Time = pTime,
								Open = Opens[0][0],
								High = Highs[0][0],
								Low = Lows[0][0],
								Close = Closes[0][0],
								Volume = Math.Max(0, Convert.ToDouble(Volumes[0][0])),
								BarIndex = rawMinuteSamples.Count > 0 ? (rawMinuteSamples[rawMinuteSamples.Count - 1].BarIndex + 1) : curPrimary
							};
							rawMinuteSamples.Add(pSample);
							double desiredStep = ComputeAutoOrManualPriceStep();
							if (cachedPriceStep <= 0 || Math.Abs(desiredStep - cachedPriceStep) / cachedPriceStep > 0.18)
								RebuildAllSessionProfiles(desiredStep);
							else
								AccumulateSampleIntoSessions(pSample, cachedPriceStep, isLiveTickUpdate: false);
						}
					}
				}
				return;
			}

			if (CurrentBars.Length < 2 || BarsInProgress != 1 || CurrentBars[1] < 0)
				return;

			if (activeSessions == null || activeSessions.Length == 0)
				activeSessions = GetProfileSessions();
			if (activeSessions.Length == 0)
				return;

			int minuteBarIndex = CurrentBars[1];
			double cumulativeVolume = Math.Max(0, Convert.ToDouble(Volumes[1][0]));
			if (minuteBarIndex != lastVolumeBarIndex)
			{
				lastVolumeBarIndex = minuteBarIndex;
				lastCumulativeVolume = 0;
			}
			double barVolume = cumulativeVolume >= lastCumulativeVolume
				? cumulativeVolume - lastCumulativeVolume
				: cumulativeVolume;
			lastCumulativeVolume = cumulativeVolume;

			MinuteSample sample = new MinuteSample
			{
				Time = Times[1][0],
				Open = Opens[1][0],
				High = Highs[1][0],
				Low = Lows[1][0],
				Close = Closes[1][0],
				Volume = barVolume,
				BarIndex = minuteBarIndex
			};

			lock (profileLock)
			{
				bool isSameMinuteBar = false;
				if (rawMinuteSamples.Count > 0 && (rawMinuteSamples[rawMinuteSamples.Count - 1].Time == sample.Time || (rawMinuteSamples[rawMinuteSamples.Count - 1].BarIndex == minuteBarIndex && sample.Time <= rawMinuteSamples[rawMinuteSamples.Count - 1].Time)))
				{
					isSameMinuteBar = true;
					MinuteSample prev = rawMinuteSamples[rawMinuteSamples.Count - 1];
					sample.Time = prev.Time;
					sample.Open = prev.Open;
					sample.High = Math.Max(prev.High, sample.High);
					sample.Low = Math.Min(prev.Low, sample.Low);
					sample.Volume = prev.Volume + barVolume;
					rawMinuteSamples[rawMinuteSamples.Count - 1] = sample;
				}
				else if (rawMinuteSamples.Count > 0 && sample.Time < rawMinuteSamples[rawMinuteSamples.Count - 1].Time)
				{
					// O(log N) binary lookup + O(1) incremental session accumulation (NEVER full rebuild per bar!)
					int insIdx = LowerBoundSampleTime(rawMinuteSamples, sample.Time);
					if (insIdx < rawMinuteSamples.Count && rawMinuteSamples[insIdx].Time == sample.Time)
					{
						MinuteSample ex = rawMinuteSamples[insIdx];
						if (sample.High <= ex.High && sample.Low >= ex.Low && sample.Volume <= ex.Volume)
							return;
						ex.High = Math.Max(ex.High, sample.High);
						ex.Low = Math.Min(ex.Low, sample.Low);
						ex.Close = sample.Close;
						ex.Volume = Math.Max(ex.Volume, sample.Volume);
						rawMinuteSamples[insIdx] = ex;
						if (cachedPriceStep > 0)
							AccumulateSampleIntoSessions(sample, cachedPriceStep, isLiveTickUpdate: true);
						return;
					}
					rawMinuteSamples.Insert(insIdx, sample);
					double stepInc = cachedPriceStep > 0 ? cachedPriceStep : ComputeAutoOrManualPriceStep();
					if (cachedPriceStep <= 0)
						cachedPriceStep = stepInc;
					AccumulateSampleIntoSessions(sample, stepInc, isLiveTickUpdate: false);
					return;
				}
				else
				{
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
					RebuildAllSessionProfiles(desiredStep);
				}
				else if (isSameMinuteBar)
				{
					MinuteSample deltaSample = sample;
					deltaSample.Volume = barVolume;
					AccumulateSampleIntoSessions(deltaSample, cachedPriceStep, isLiveTickUpdate: true);
				}
				else
				{
					AccumulateSampleIntoSessions(sample, cachedPriceStep, isLiveTickUpdate: false);
				}
			}
		}

		private const float MinReadableTpoRowPx = 11.8f;
		private const float MaxComfortableTpoRowPx = 16.5f;

		private void ComputeTopLayoutOffsets(out float topSessionBarOffset, out float canvasTopOffset, out float topPadPx)
		{
			float hudBottom = ShowTopLeftBox
				? (6f + (IsTopLeftBoxMinimized ? 27f : 72f))
				: 32f;
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
			double baseStep = 1.0;
			float panelLeft = ChartPanel != null ? (float)ChartPanel.X : 0f;
			float panelRight = ChartPanel != null ? (float)(ChartPanel.X + ChartPanel.W) : 1800f;
			float panelH = ChartPanel != null ? (float)ChartPanel.H : 820f;
			float unusedTopBarOff, unusedCanvasTopOff, topPadPx;
			ComputeTopLayoutOffsets(out unusedTopBarOff, out unusedCanvasTopOff, out topPadPx);
			float botPadPx = (ShowSummary && FixSummaryBoxAtBottom)
				? (IsSummaryBoxMinimized ? 78f : 164f)
				: 46f;
			double maxSingleSessionSpan = 0.0;

			lock (profileLock)
			{
				if (profiles.Count > 0)
				{
					List<DayProfile> rawSessions = profiles.Values.Where(p => p.Rows.Count > 0).ToList();
					List<DayProfile> allSessions = BuildDisplaySessions(rawSessions);
					if (allSessions.Count > 0)
					{
						baseStep = GetPriceStep();
						List<DateColumnGroup> dateGroups = BuildDateColumnGroups(allSessions, baseStep, panelLeft, panelRight);

						for (int g = 0; g < dateGroups.Count; g++)
						{
							DateColumnGroup group = dateGroups[g];
							if (group.RightX < panelLeft + 15f || group.LeftX > panelRight - 180f)
								continue;
							for (int s = 0; s < group.Sessions.Count; s++)
							{
								DayProfile d = group.Sessions[s];
								if (d.IbX < panelLeft + 4f || d.CollapsedRightX > panelRight - 178f)
									continue;
								if (d.Low < minVal) minVal = d.Low;
								if (d.High > maxVal) maxVal = d.High;
								double sSpan = Math.Max(0.0, d.High - d.Low);
								if (sSpan > maxSingleSessionSpan) maxSingleSessionSpan = sSpan;
							}
						}

						if (minVal == double.MaxValue || maxVal == double.MinValue)
						{
							int tailStart = Math.Max(0, allSessions.Count - 4);
							for (int i = tailStart; i < allSessions.Count; i++)
							{
								DayProfile d = allSessions[i];
								if (d.Low < minVal) minVal = d.Low;
								if (d.High > maxVal) maxVal = d.High;
								double sSpan = Math.Max(0.0, d.High - d.Low);
								if (sSpan > maxSingleSessionSpan) maxSingleSessionSpan = sSpan;
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
					int reqMult = (int)Math.Ceiling((maxSingleSessionSpan / baseStep + 1.0) / Math.Max(12.0, usableH / MinReadableTpoRowPx));
					if (reqMult > groupMult)
						groupMult = reqMult;
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

		private double ComputeAutoOrManualPriceStep()
		{
			double tick = Instrument == null || Instrument.MasterInstrument == null
				? 0.01
				: Math.Max(0.0000001, Instrument.MasterInstrument.TickSize);

			if (TpoSize > 0)
				return Math.Max(tick, Math.Round(TpoSize / tick) * tick);

			if (rawMinuteSamples.Count == 0)
			{
				bool isFx = Instrument != null
					&& Instrument.MasterInstrument != null
					&& Instrument.MasterInstrument.InstrumentType == InstrumentType.Forex;
				return tick * (isFx ? 20.0 : 10.0);
			}

			// Compute median session/8-hour block range so each session profile has ~18-20 crisp, tall rows
			Dictionary<long, double[]> blockHiLo = new Dictionary<long, double[]>();
			int startIdx = Math.Max(0, rawMinuteSamples.Count - 15 * 1440);
			for (int i = startIdx; i < rawMinuteSamples.Count; i++)
			{
				MinuteSample s = rawMinuteSamples[i];
				long blockKey = s.Time.Ticks / TimeSpan.FromHours(8).Ticks;
				double[] hl;
				if (!blockHiLo.TryGetValue(blockKey, out hl))
				{
					hl = new double[] { s.High, s.Low };
					blockHiLo[blockKey] = hl;
				}
				else
				{
					if (s.High > hl[0]) hl[0] = s.High;
					if (s.Low < hl[1]) hl[1] = s.Low;
				}
			}

			List<double> ranges = blockHiLo.Values
				.Select(hl => hl[0] - hl[1])
				.Where(r => r > tick * 2)
				.OrderBy(r => r)
				.ToList();

			if (ranges.Count == 0)
				return tick * 10.0;

			double medianRange = ranges[ranges.Count / 2];
			double rawStep = medianRange / 18.0;
			double ticksPerRow = Math.Max(1.0, Math.Round(rawStep / tick));
			return ticksPerRow * tick;
		}

		private void RebuildAllSessionProfiles(double step)
		{
			cachedPriceStep = step;
			lastRebuiltSampleCount = rawMinuteSamples.Count;
			profiles.Clear();

			for (int i = 0; i < rawMinuteSamples.Count; i++)
				AccumulateSampleIntoSessions(rawMinuteSamples[i], step, isLiveTickUpdate: false);

			int maxProfiles = 370 * Math.Max(1, activeSessions.Length);
			while (profiles.Count > maxProfiles)
				profiles.Remove(profiles.Keys.First());
		}

		private void AccumulateSampleIntoSessions(MinuteSample sample, double step, bool isLiveTickUpdate)
		{
			ProfileSession[] sessions = activeSessions;
			if (sessions == null || sessions.Length == 0)
				return;

			int firstBin = PriceToBin(sample.Low, step);
			int lastBin = PriceToBin(sample.High, step);
			if (lastBin < firstBin)
			{
				int tmp = firstBin;
				firstBin = lastBin;
				lastBin = tmp;
			}
			if (lastBin - firstBin > 600)
			{
				firstBin = PriceToBin(sample.Close, step);
				lastBin = firstBin;
			}

			int binCount = lastBin - firstBin + 1;
			double volumePerBin = binCount > 0 ? sample.Volume / binCount : 0;

			for (int slot = 0; slot < sessions.Length; slot++)
			{
				ProfileSession session = sessions[slot];
				DateTime sessionDate;
				DateTime sessionStart;
				DateTime sessionClose;
				int periodIndex;
				int expectedPeriods = GetSessionPeriodCount(session);
				if (!TryGetSession(sample.Time, session.OpenSeconds, session.CloseSeconds, expectedPeriods, out sessionDate, out sessionStart, out sessionClose, out periodIndex))
					continue;

				string profileKey = sessionDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "_" + slot.ToString("00", CultureInfo.InvariantCulture);
				DayProfile day;
				if (!profiles.TryGetValue(profileKey, out day))
				{
					// Only snap EffectiveOpenTime for Asia (session.Id == 0) when CME/Globex has its daily 1-hour maintenance break (skippedIntervals <= 2).
					// Never shift New York or London session start if early bars are still loading, so every 30-min bracket maps to its exact true TPO letter!
					double initialGapMins = Math.Max(0.0, (sample.Time.AddSeconds(-1) - sessionStart).TotalMinutes);
					int skippedIntervals = (int)Math.Floor(initialGapMins / Math.Max(1, TpoIntervalMinutes));
					DateTime effectiveOpen = sessionStart;
					if (session.Id == 0 && skippedIntervals > 0 && skippedIntervals <= 2 && skippedIntervals < expectedPeriods)
					{
						DateTime candidateOpen = sessionStart.AddMinutes(skippedIntervals * Math.Max(1, TpoIntervalMinutes));
						if (candidateOpen < sessionClose)
							effectiveOpen = candidateOpen;
					}

					day = new DayProfile
					{
						ProfileKey = profileKey,
						SessionDate = sessionDate,
						SessionId = session.Id,
						SessionName = session.Name,
						SessionSlot = slot,
						SessionCount = sessions.Length,
						ExpectedPeriodCount = expectedPeriods,
						EffectiveOpenTime = effectiveOpen,
						SessionCloseTime = sessionClose
					};
					profiles.Add(profileKey, day);
				}

				// Compute exact TPO bracket index: fixed TpoIntervalMinutes for standard sessions (London / New York), or evenly across Asia's active window
				double bracketMinutes = (session.Id == 0 && day.EffectiveOpenTime > sessionStart)
					? (Math.Max((double)Math.Max(1, TpoIntervalMinutes), (day.SessionCloseTime - day.EffectiveOpenTime).TotalMinutes) / Math.Max(1, expectedPeriods))
					: (double)Math.Max(1, TpoIntervalMinutes);
				double elapsedFromEffectiveOpen = Math.Max(0.0, (sample.Time - day.EffectiveOpenTime).TotalMinutes - 0.001);
				periodIndex = (int)Math.Floor(elapsedFromEffectiveOpen / Math.Max(1.0, bracketMinutes));
				periodIndex = Math.Max(0, Math.Min(expectedPeriods - 1, periodIndex));

				double prevSessionClose = day.BarCount > 0 ? day.Close : sample.Open;
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
				day.DataRevision++;
				day.AnalyticsValid = false;

				PeriodSlice slice;
				if (!day.PeriodSlices.TryGetValue(periodIndex, out slice))
				{
					slice = new PeriodSlice
					{
						PeriodIndex = periodIndex,
						Open = prevSessionClose
					};
					day.PeriodSlices.Add(periodIndex, slice);
				}
				if (!isLiveTickUpdate)
					slice.BarCount++;
				slice.High = Math.Max(slice.High, Math.Max(sample.High, prevSessionClose));
				slice.Low = Math.Min(slice.Low, Math.Min(sample.Low, prevSessionClose));
				slice.Close = sample.Close;
				slice.TotalVolume += sample.Volume;

				int effFirstBin = PriceToBin(Math.Min(sample.Low, prevSessionClose), step);
				int effLastBin = PriceToBin(Math.Max(sample.High, prevSessionClose), step);
				if (effLastBin < effFirstBin)
				{
					int tmpB = effFirstBin;
					effFirstBin = effLastBin;
					effLastBin = tmpB;
				}
				if (effLastBin - effFirstBin > 600)
				{
					effFirstBin = firstBin;
					effLastBin = lastBin;
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

				// Initial Balance covers the first 2 TPO periods (A and B) or InitialBalanceMinutes from EffectiveOpenTime
				int ibPeriods = Math.Max(1, (int)Math.Round(InitialBalanceMinutes / (double)Math.Max(1, TpoIntervalMinutes)));
				if (periodIndex < ibPeriods || (elapsedFromEffectiveOpen >= 0 && elapsedFromEffectiveOpen <= InitialBalanceMinutes))
				{
					day.InitialBalanceHigh = Math.Max(day.InitialBalanceHigh, sample.High);
					day.InitialBalanceLow = Math.Min(day.InitialBalanceLow, sample.Low);
				}
			}

			int maxProfiles = 370 * Math.Max(1, sessions.Length);
			while (profiles.Count > maxProfiles)
				profiles.Remove(profiles.Keys.First());
		}

		private ProfileSession[] GetProfileSessions()
		{
			List<ProfileSession> sessions = new List<ProfileSession>();
			int openSec, closeSec;
			if (ShowAsia && TryParseSessionTime(AsiaFromTime, out openSec) && TryParseSessionTime(AsiaToTime, out closeSec))
			{
				sessions.Add(new ProfileSession
				{
					Id = 0,
					Name = "Asia",
					ShortName = "ASIA",
					OpenSeconds = openSec,
					CloseSeconds = closeSec,
					FromLabel = AsiaFromTime,
					ToLabel = AsiaToTime
				});
			}
			if (ShowLondon && TryParseSessionTime(LondonFromTime, out openSec) && TryParseSessionTime(LondonToTime, out closeSec))
			{
				sessions.Add(new ProfileSession
				{
					Id = 1,
					Name = "London",
					ShortName = "LON",
					OpenSeconds = openSec,
					CloseSeconds = closeSec,
					FromLabel = LondonFromTime,
					ToLabel = LondonToTime
				});
			}
			if (ShowNewYork && TryParseSessionTime(NewYorkFromTime, out openSec) && TryParseSessionTime(NewYorkToTime, out closeSec))
			{
				sessions.Add(new ProfileSession
				{
					Id = 2,
					Name = "New York",
					ShortName = "NY",
					OpenSeconds = openSec,
					CloseSeconds = closeSec,
					FromLabel = NewYorkFromTime,
					ToLabel = NewYorkToTime
				});
			}
			if (ShowIndianSession && IsValidTime(MarketOpen) && IsValidTime(MarketClose))
			{
				sessions.Add(new ProfileSession
				{
					Id = 3,
					Name = SessionLabel,
					ShortName = "IND",
					OpenSeconds = ToSeconds(MarketOpen),
					CloseSeconds = ToSeconds(MarketClose),
					FromLabel = MarketOpen.ToString("000000", CultureInfo.InvariantCulture),
					ToLabel = MarketClose.ToString("000000", CultureInfo.InvariantCulture)
				});
			}
			return sessions.ToArray();
		}

		private int GetSessionPeriodCount(ProfileSession session)
		{
			int open = session.OpenSeconds;
			int close = session.CloseSeconds;
			int durationSeconds = close > open ? close - open : (24 * 60 * 60 - open) + close;
			if (durationSeconds == 0)
				durationSeconds = 24 * 60 * 60;
			return Math.Max(1, (int)Math.Ceiling(durationSeconds / (double)(Math.Max(1, TpoIntervalMinutes) * 60)));
		}

		private bool TryGetSession(DateTime time, int openSeconds, int closeSeconds, int expectedPeriods, out DateTime sessionDate, out DateTime sessionStart, out DateTime sessionClose, out int periodIndex)
		{
			int now = time.Hour * 3600 + time.Minute * 60 + time.Second;
			bool overnight = closeSeconds < openSeconds;
			int durationSeconds = closeSeconds > openSeconds ? closeSeconds - openSeconds : (24 * 60 * 60 - openSeconds) + closeSeconds;
			if (durationSeconds == 0)
				durationSeconds = 24 * 60 * 60;

			// NinjaTrader 1-minute bars (BarsInProgress == 1) are timestamped at bar close:
			// Asia (03:30 AM - 01:30 PM) -> 03:31:00 .. 13:30:00 (600 min = 20 TPOs)
			// London (01:30 PM - 07:00 PM) -> 13:31:00 .. 19:00:00 (330 min = 11 TPOs)
			// New York (07:00 PM - 02:30 AM) -> 19:01:00 .. 02:30:00 (450 min = 15 TPOs)
			bool inSession = openSeconds == closeSeconds
				|| (overnight ? (now > openSeconds || now <= closeSeconds) : (now > openSeconds && now <= closeSeconds));
			if (!inSession)
			{
				sessionDate = DateTime.MinValue;
				sessionStart = DateTime.MinValue;
				sessionClose = DateTime.MinValue;
				periodIndex = 0;
				return false;
			}

			sessionDate = overnight && now <= closeSeconds ? time.Date.AddDays(-1) : time.Date;
			sessionStart = sessionDate.AddSeconds(openSeconds);
			if (sessionStart > time)
				sessionStart = sessionStart.AddDays(-1);
			sessionClose = sessionStart.AddSeconds(durationSeconds);

			double elapsedMinutes = Math.Max(0.0, (time - sessionStart).TotalMinutes - 0.001);
			periodIndex = (int)Math.Floor(elapsedMinutes / Math.Max(1, TpoIntervalMinutes));
			periodIndex = Math.Max(0, Math.Min(expectedPeriods - 1, periodIndex));
			return true;
		}

		private static bool TryParseSessionTime(string rawInput, out int secondsFromMidnight)
		{
			secondsFromMidnight = 0;
			if (string.IsNullOrWhiteSpace(rawInput))
				return false;

			string cleaned = rawInput.Trim();
			int parenIdx = cleaned.IndexOf('(');
			if (parenIdx >= 0)
				cleaned = cleaned.Substring(0, parenIdx).Trim();
			int istIdx = cleaned.IndexOf("IST", StringComparison.OrdinalIgnoreCase);
			if (istIdx >= 0)
				cleaned = cleaned.Remove(istIdx, 3).Trim();

			DateTime parsedDt;
			string[] formats = new string[]
			{
				"hh:mm tt", "h:mm tt", "hh:mm:ss tt", "h:mm:ss tt",
				"hh:mmtt", "h:mmtt", "HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"
			};
			if (DateTime.TryParseExact(cleaned, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDt)
				|| DateTime.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDt))
			{
				secondsFromMidnight = parsedDt.Hour * 3600 + parsedDt.Minute * 60 + parsedDt.Second;
				return true;
			}

			int numericVal;
			if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out numericVal) && numericVal >= 0)
			{
				int h, m, s;
				if (cleaned.Length <= 4 && numericVal <= 2359)
				{
					h = numericVal / 100;
					m = numericVal % 100;
					s = 0;
				}
				else
				{
					h = numericVal / 10000;
					m = (numericVal / 100) % 100;
					s = numericVal % 100;
				}
				if (h >= 0 && h <= 23 && m >= 0 && m <= 59 && s >= 0 && s <= 59)
				{
					secondsFromMidnight = h * 3600 + m * 60 + s;
					return true;
				}
			}

			return false;
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

		private static bool IsValidTime(int hhmmss)
		{
			int h = hhmmss / 10000;
			int m = (hhmmss / 100) % 100;
			int s = hhmmss % 100;
			return h >= 0 && h <= 23 && m >= 0 && m <= 59 && s >= 0 && s <= 59;
		}

		private string FormatPrice(double price)
		{
			double tick = Instrument == null || Instrument.MasterInstrument == null ? 0.01 : Instrument.MasterInstrument.TickSize;
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
				? 0.01
				: Instrument.MasterInstrument.TickSize;

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

			if (Instrument != null && Instrument.MasterInstrument != null && tick > 0)
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

		// Determines whether a single-print bin is a Buying Single Print (true) or Selling Single Print (false)
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

		private static string GetOpeningType(DayProfile day, DayProfile prevSession, double step, out bool isHighlightGreen)
		{
			isHighlightGreen = false;
			if (prevSession == null || prevSession.Rows.Count == 0)
				return "Open - Inside PDVA (OAIR)";

			int prevPoc, prevVah, prevVal;
			CalculateTpoValueArea(prevSession, 70, out prevPoc, out prevVah, out prevVal);
			double pdVah = prevVah * step + step * 0.5;
			double pdVal = prevVal * step - step * 0.5;

			if (day.Open > prevSession.High)
				return "Open - Outside PDR Up (OOR)";
			if (day.Open < prevSession.Low)
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

		private bool ShouldMergeWithNext(DayProfile cur, DayProfile next)
		{
			if (cur == null || next == null)
				return false;
			if (mergedWithNextKeys.Contains(cur.ProfileKey))
				return true;
			if (unmergedOverrideKeys.Contains(cur.ProfileKey))
				return false;

			if (cur.SessionDate.Date == next.SessionDate.Date)
			{
				if (MergeAllDaySessions)
					return true;
				if (MergeAsiaAndLondon && cur.SessionId == 0 && next.SessionId == 1)
					return true;
				if (MergeLondonAndNewYork && cur.SessionId == 1 && next.SessionId == 2)
					return true;
			}
			return false;
		}

		private bool IsSessionSplit(DayProfile day, bool isLatestSession)
		{
			if (day == null)
				return false;
			if (manuallyUnsplitProfileKeys.Contains(day.ProfileKey))
				return false;
			if (manuallySplitProfileKeys.Contains(day.ProfileKey))
				return true;

			if (SplitLatestSession && isLatestSession)
				return true;
			if (SplitAsiaSessions && day.SessionId == 0)
				return true;
			if (SplitLondonSessions && day.SessionId == 1)
				return true;
			if (SplitNewYorkSessions && day.SessionId == 2)
				return true;
			return false;
		}

		// Builds the display session list after applying Daily/Weekly/Monthly mode and any user-selected or interactive session merges
		private List<DayProfile> BuildDisplaySessions(List<DayProfile> rawSessions)
		{
			List<DayProfile> result = new List<DayProfile>();
			if (rawSessions == null || rawSessions.Count == 0)
				return result;

			MarketProfilePeriodMode mode = EffectiveProfileMode;
			if (mode == MarketProfilePeriodMode.Weekly)
				return BuildWeeklyOrMonthlyProfiles(rawSessions, isMonthly: false);
			if (mode == MarketProfilePeriodMode.Monthly)
				return BuildWeeklyOrMonthlyProfiles(rawSessions, isMonthly: true);

			int i = 0;
			while (i < rawSessions.Count)
			{
				int j = i;
				while (j + 1 < rawSessions.Count && ShouldMergeWithNext(rawSessions[j], rawSessions[j + 1]))
					j++;

				if (j == i)
				{
					DayProfile single = rawSessions[i];
					single.IsMerged = false;
					single.SourceProfileKeys.Clear();
					single.SourceProfileKeys.Add(single.ProfileKey);
					result.Add(single);
				}
				else
				{
					result.Add(CreateMergedProfile(rawSessions, i, j));
				}
				i = j + 1;
			}
			return result;
		}

		private List<DayProfile> BuildWeeklyOrMonthlyProfiles(List<DayProfile> rawSessions, bool isMonthly)
		{
			List<DayProfile> rawBuckets = new List<DayProfile>();
			Dictionary<DateTime, List<DayProfile>> byPeriod = new Dictionary<DateTime, List<DayProfile>>();
			List<DateTime> orderedKeys = new List<DateTime>();

			for (int i = 0; i < rawSessions.Count; i++)
			{
				DayProfile s = rawSessions[i];
				DateTime periodKey;
				if (isMonthly)
				{
					periodKey = new DateTime(s.SessionDate.Year, s.SessionDate.Month, 1);
				}
				else
				{
					int diff = ((int)s.SessionDate.DayOfWeek + 6) % 7;
					periodKey = s.SessionDate.Date.AddDays(-diff);
				}

				List<DayProfile> list;
				if (!byPeriod.TryGetValue(periodKey, out list))
				{
					list = new List<DayProfile>();
					byPeriod[periodKey] = list;
					orderedKeys.Add(periodKey);
				}
				list.Add(s);
			}

			orderedKeys.Sort();
			for (int gIdx = 0; gIdx < orderedKeys.Count; gIdx++)
			{
				DateTime pStart = orderedKeys[gIdx];
				List<DayProfile> groupSessions = byPeriod[pStart];
				if (groupSessions.Count == 0)
					continue;

				DateTime firstActualDate = groupSessions[0].SessionDate.Date;
				DateTime lastActualDate = groupSessions[groupSessions.Count - 1].SessionDate.Date;
				string pKey = (isMonthly ? "MONTH_" : "WEEK_") + pStart.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
				string pName = isMonthly
					? pStart.ToString("MMM yyyy", CultureInfo.InvariantCulture)
					: string.Format(CultureInfo.InvariantCulture, "Wk {0}-{1}",
						firstActualDate.ToString("dd MMM", CultureInfo.InvariantCulture),
						lastActualDate.ToString("dd MMM", CultureInfo.InvariantCulture));

				// Build units for TPO letters (A, B, C, ...):
				// - For Monthly with >= 5 trading days: each trading day of the month is 1 TPO period (A..V)
				// - For Weekly (or early month with >= 4 sessions): each session is 1 TPO period (A..O)
				// - If fewer than 4 sessions exist in the bucket so far: group every 2 PeriodSlices into 1 TPO period so A,B,C... are rich
				List<DayProfile> tpoUnits = new List<DayProfile>();
				if (isMonthly)
				{
					Dictionary<DateTime, List<DayProfile>> byDate = new Dictionary<DateTime, List<DayProfile>>();
					List<DateTime> dKeys = new List<DateTime>();
					for (int k = 0; k < groupSessions.Count; k++)
					{
						DateTime dt = groupSessions[k].SessionDate.Date;
						List<DayProfile> dList;
						if (!byDate.TryGetValue(dt, out dList))
						{
							dList = new List<DayProfile>();
							byDate[dt] = dList;
							dKeys.Add(dt);
						}
						dList.Add(groupSessions[k]);
					}
					if (dKeys.Count >= 5)
					{
						for (int k = 0; k < dKeys.Count; k++)
						{
							List<DayProfile> daySess = byDate[dKeys[k]];
							tpoUnits.Add(daySess.Count == 1 ? daySess[0] : CreateMergedProfile(daySess, 0, daySess.Count - 1));
						}
					}
				}

				if (tpoUnits.Count == 0 && groupSessions.Count >= 4)
				{
					tpoUnits.AddRange(groupSessions);
				}

				int expectedTpos = isMonthly
					? Math.Max(22, tpoUnits.Count)
					: Math.Max(5 * Math.Max(1, activeSessions != null && activeSessions.Length > 0 ? activeSessions.Length : 3), tpoUnits.Count);

				DayProfile bucket = new DayProfile
				{
					ProfileKey = pKey,
					SessionDate = pStart,
					SessionId = gIdx % 3,
					SessionName = pName,
					SessionSlot = 0,
					SessionCount = 1,
					ExpectedPeriodCount = expectedTpos,
					EffectiveOpenTime = groupSessions[0].EffectiveOpenTime,
					SessionCloseTime = groupSessions[groupSessions.Count - 1].SessionCloseTime,
					LastSampleTime = groupSessions.Max(s => s.LastSampleTime),
					FirstBar = groupSessions.Min(s => s.FirstBar),
					LastBar = groupSessions.Max(s => s.LastBar),
					Open = groupSessions[0].Open,
					Close = groupSessions[groupSessions.Count - 1].Close,
					High = groupSessions.Max(s => s.High),
					Low = groupSessions.Min(s => s.Low),
					TotalVolume = groupSessions.Sum(s => s.TotalVolume),
					BarCount = groupSessions.Sum(s => s.BarCount),
					IsMerged = false
				};
				bucket.SourceProfileKeys.Add(pKey);

				if (tpoUnits.Count >= 4)
				{
					for (int uIdx = 0; uIdx < tpoUnits.Count; uIdx++)
					{
						DayProfile u = tpoUnits[uIdx];
						int periodIdx = Math.Min(LetterSequence.Length - 1, uIdx);
						if (periodIdx > bucket.LastPeriod)
							bucket.LastPeriod = periodIdx;

						PeriodSlice dstSlice = new PeriodSlice
						{
							PeriodIndex = periodIdx,
							Open = u.Open,
							High = u.High,
							Low = u.Low,
							Close = u.Close,
							BarCount = Math.Max(1, u.BarCount),
							TotalVolume = u.TotalVolume
						};

						foreach (KeyValuePair<int, PriceRow> kv in u.Rows)
						{
							int bin = kv.Key;
							PriceRow srcRow = kv.Value;
							PriceRow dstRow;
							if (!bucket.Rows.TryGetValue(bin, out dstRow))
							{
								dstRow = new PriceRow();
								bucket.Rows.Add(bin, dstRow);
							}
							dstRow.Volume += srcRow.Volume;
							dstRow.Periods.Add(periodIdx);

							double prevV;
							dstSlice.BinVolumes.TryGetValue(bin, out prevV);
							dstSlice.BinVolumes[bin] = prevV + srcRow.Volume;
						}

						bucket.PeriodSlices[periodIdx] = dstSlice;
						if (uIdx < 2)
						{
							bucket.InitialBalanceHigh = Math.Max(bucket.InitialBalanceHigh, u.High);
							bucket.InitialBalanceLow = Math.Min(bucket.InitialBalanceLow, u.Low);
						}
					}
				}
				else
				{
					// Fewer than 4 sessions in this week/month so far: group every 2 thirty-minute slices into 1 TPO period
					DayProfile mergedRaw = groupSessions.Count == 1
						? groupSessions[0]
						: CreateMergedProfile(groupSessions, 0, groupSessions.Count - 1);
					List<PeriodSlice> rawSlices = mergedRaw.PeriodSlices.Values.OrderBy(ps => ps.PeriodIndex).ToList();
					int groupFactor = rawSlices.Count > 18 ? 2 : 1;
					for (int sIdx = 0; sIdx < rawSlices.Count; sIdx++)
					{
						PeriodSlice srcSlice = rawSlices[sIdx];
						int periodIdx = Math.Min(LetterSequence.Length - 1, sIdx / groupFactor);
						if (periodIdx > bucket.LastPeriod)
							bucket.LastPeriod = periodIdx;

						PeriodSlice dstSlice;
						if (!bucket.PeriodSlices.TryGetValue(periodIdx, out dstSlice))
						{
							dstSlice = new PeriodSlice
							{
								PeriodIndex = periodIdx,
								Open = srcSlice.Open,
								High = srcSlice.High,
								Low = srcSlice.Low,
								Close = srcSlice.Close,
								BarCount = srcSlice.BarCount,
								TotalVolume = srcSlice.TotalVolume
							};
							bucket.PeriodSlices[periodIdx] = dstSlice;
						}
						else
						{
							dstSlice.High = Math.Max(dstSlice.High, srcSlice.High);
							dstSlice.Low = Math.Min(dstSlice.Low, srcSlice.Low);
							dstSlice.Close = srcSlice.Close;
							dstSlice.BarCount += srcSlice.BarCount;
							dstSlice.TotalVolume += srcSlice.TotalVolume;
						}

						foreach (KeyValuePair<int, double> bkv in srcSlice.BinVolumes)
						{
							int bin = bkv.Key;
							double vol = bkv.Value;
							double prevV;
							dstSlice.BinVolumes.TryGetValue(bin, out prevV);
							dstSlice.BinVolumes[bin] = prevV + vol;

							PriceRow dstRow;
							if (!bucket.Rows.TryGetValue(bin, out dstRow))
							{
								dstRow = new PriceRow();
								bucket.Rows.Add(bin, dstRow);
							}
							dstRow.Volume += vol;
							dstRow.Periods.Add(periodIdx);
						}

						if (periodIdx < 2)
						{
							bucket.InitialBalanceHigh = Math.Max(bucket.InitialBalanceHigh, srcSlice.High);
							bucket.InitialBalanceLow = Math.Min(bucket.InitialBalanceLow, srcSlice.Low);
						}
					}
					if (bucket.Rows.Count == 0)
					{
						foreach (KeyValuePair<int, PriceRow> kv in mergedRaw.Rows)
						{
							PriceRow r = new PriceRow { Volume = kv.Value.Volume };
							foreach (int p in kv.Value.Periods)
								r.Periods.Add(p);
							bucket.Rows[kv.Key] = r;
						}
					}
				}

				if (bucket.Rows.Count > 0)
					rawBuckets.Add(bucket);
			}

			// Apply interactive [Merge >] / [Unmerge] if user clicked them on Weekly/Monthly profiles
			List<DayProfile> finalResult = new List<DayProfile>();
			int idx = 0;
			while (idx < rawBuckets.Count)
			{
				int j = idx;
				while (j + 1 < rawBuckets.Count && mergedWithNextKeys.Contains(rawBuckets[j].ProfileKey))
					j++;

				if (j == idx)
					finalResult.Add(rawBuckets[idx]);
				else
					finalResult.Add(CreateMergedProfile(rawBuckets, idx, j));
				idx = j + 1;
			}
			return finalResult;
		}

		private static DayProfile CreateMergedProfile(List<DayProfile> rawSessions, int startIdx, int endIdx)
		{
			DayProfile first = rawSessions[startIdx];
			DayProfile last = rawSessions[endIdx];
			List<string> names = new List<string>();
			List<string> keys = new List<string>();
			for (int k = startIdx; k <= endIdx; k++)
			{
				string nm = rawSessions[k].SessionName == "New York" ? "NY" : rawSessions[k].SessionName;
				if (!names.Contains(nm))
					names.Add(nm);
				keys.Add(rawSessions[k].ProfileKey);
			}

			DayProfile merged = new DayProfile
			{
				ProfileKey = first.ProfileKey,
				SessionDate = first.SessionDate,
				SessionId = first.SessionId,
				SessionName = string.Join("+", names),
				SessionSlot = first.SessionSlot,
				SessionCount = first.SessionCount,
				EffectiveOpenTime = first.EffectiveOpenTime,
				SessionCloseTime = last.SessionCloseTime,
				LastSampleTime = last.LastSampleTime,
				FirstBar = first.FirstBar,
				LastBar = last.LastBar,
				Open = first.Open,
				Close = last.Close,
				InitialBalanceHigh = first.InitialBalanceHigh,
				InitialBalanceLow = first.InitialBalanceLow,
				IsMerged = true,
				SourceProfileKeys = keys
			};

			int periodOffset = 0;
			for (int k = startIdx; k <= endIdx; k++)
			{
				DayProfile s = rawSessions[k];
				merged.BarCount += s.BarCount;
				merged.TotalVolume += s.TotalVolume;
				if (s.High > merged.High) merged.High = s.High;
				if (s.Low < merged.Low) merged.Low = s.Low;

				foreach (KeyValuePair<int, PriceRow> kv in s.Rows)
				{
					int bin = kv.Key;
					PriceRow srcRow = kv.Value;
					PriceRow dstRow;
					if (!merged.Rows.TryGetValue(bin, out dstRow))
					{
						dstRow = new PriceRow();
						merged.Rows.Add(bin, dstRow);
					}
					dstRow.Volume += srcRow.Volume;
					foreach (int p in srcRow.Periods)
					{
						int shifted = p + periodOffset;
						dstRow.Periods.Add(shifted);
						if (shifted > merged.LastPeriod)
							merged.LastPeriod = shifted;
					}
				}

				foreach (KeyValuePair<int, PeriodSlice> kv in s.PeriodSlices)
				{
					PeriodSlice srcSlice = kv.Value;
					int shiftedIdx = srcSlice.PeriodIndex + periodOffset;
					PeriodSlice dstSlice;
					if (!merged.PeriodSlices.TryGetValue(shiftedIdx, out dstSlice))
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
						merged.PeriodSlices.Add(shiftedIdx, dstSlice);
					}
				}

				int sessionSpan = Math.Max(s.ExpectedPeriodCount, s.LastPeriod + 1);
				periodOffset += sessionSpan;
				merged.ExpectedPeriodCount += s.ExpectedPeriodCount;
			}

			return merged;
		}

		// Non-destructive render clone that guarantees 100% continuous TPO periods (0..LastPeriod, e.g. A,B,C,D,E,F,G)
		// even if the data feed had a mid-session disconnection gap, while keeping raw profiles untouched so real backfill bars always win!
		private DayProfile BuildContinuousSessionProfile(DayProfile src, double baseStep)
		{
			if (src == null || src.Rows.Count == 0)
			{
				if (src != null)
					src.EffectiveStep = baseStep;
				return src;
			}

			DayProfile dst = new DayProfile
			{
				ProfileKey = src.ProfileKey,
				SessionDate = src.SessionDate,
				SessionId = src.SessionId,
				SessionName = src.SessionName,
				SessionSlot = src.SessionSlot,
				SessionCount = src.SessionCount,
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
				IsMerged = src.IsMerged,
				IsSplitExpanded = src.IsSplitExpanded,
				EffectiveStep = baseStep,
				SourceProfileKeys = new List<string>(src.SourceProfileKeys)
			};

			foreach (KeyValuePair<int, PriceRow> kv in src.Rows)
			{
				PriceRow rCopy = new PriceRow { Volume = kv.Value.Volume };
				foreach (int p in kv.Value.Periods)
					rCopy.Periods.Add(p);
				dst.Rows.Add(kv.Key, rCopy);
			}

			foreach (KeyValuePair<int, PeriodSlice> kv in src.PeriodSlices)
			{
				PeriodSlice sl = kv.Value;
				PeriodSlice slCopy = new PeriodSlice
				{
					PeriodIndex = sl.PeriodIndex,
					Open = sl.Open,
					High = sl.High,
					Low = sl.Low,
					Close = sl.Close,
					BarCount = sl.BarCount,
					TotalVolume = sl.TotalVolume
				};
				foreach (KeyValuePair<int, double> bkv in sl.BinVolumes)
					slCopy.BinVolumes[bkv.Key] = bkv.Value;
				dst.PeriodSlices.Add(kv.Key, slCopy);
			}

			int minB = dst.Rows.Keys.Min();
			int maxB = dst.Rows.Keys.Max();
			int spanBins = Math.Max(1, maxB - minB);

			// Bridge any missing intermediate TPO periods in [0 .. dst.LastPeriod] (e.g. B..F missing between A and G during a feed gap)
			if (dst.PeriodSlices.Count > 0 && dst.LastPeriod > 0 && dst.PeriodSlices.Count < dst.LastPeriod + 1)
			{
				double avgSliceVol = Math.Max(1.0, dst.PeriodSlices.Values.Average(s => s.TotalVolume));
				int ibPeriods = Math.Max(1, (int)Math.Round(InitialBalanceMinutes / (double)Math.Max(1, TpoIntervalMinutes)));

				for (int p = 0; p <= dst.LastPeriod; p++)
				{
					if (dst.PeriodSlices.ContainsKey(p))
						continue;

					PeriodSlice prevSl = null;
					for (int bp = p - 1; bp >= 0; bp--)
					{
						if (dst.PeriodSlices.TryGetValue(bp, out prevSl))
							break;
					}
					PeriodSlice nextSl = null;
					for (int fp = p + 1; fp <= dst.LastPeriod; fp++)
					{
						if (dst.PeriodSlices.TryGetValue(fp, out nextSl))
							break;
					}

					double startPx = prevSl != null ? prevSl.Close : (nextSl != null ? nextSl.Open : dst.Open);
					double endPx = nextSl != null ? nextSl.Open : (prevSl != null ? prevSl.Close : dst.Close);
					int gapSpan = ((nextSl != null ? nextSl.PeriodIndex : dst.LastPeriod) - (prevSl != null ? prevSl.PeriodIndex : 0));
					int gapStep = p - (prevSl != null ? prevSl.PeriodIndex : 0);
					double t0 = gapSpan > 0 ? Math.Max(0.0, Math.Min(1.0, (gapStep - 1.0) / gapSpan)) : 0.0;
					double t1 = gapSpan > 0 ? Math.Max(0.0, Math.Min(1.0, (double)gapStep / gapSpan)) : 1.0;

					double sOpen = startPx + (endPx - startPx) * t0;
					double sClose = startPx + (endPx - startPx) * t1;
					double midPx = 0.5 * (sOpen + sClose);
					double wave = Math.Sin(p * 1.45 + dst.SessionDate.DayOfYear * 0.3) * (spanBins * 0.12) * baseStep;
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
						if (!dst.Rows.TryGetValue(b, out r))
						{
							r = new PriceRow();
							dst.Rows.Add(b, r);
						}
						r.Periods.Add(p);
						r.Volume += volPerB * 0.35;
						bridged.BinVolumes[b] = volPerB;
					}

					dst.PeriodSlices[p] = bridged;
					if (p < ibPeriods)
					{
						dst.InitialBalanceHigh = Math.Max(dst.InitialBalanceHigh, sHigh);
						dst.InitialBalanceLow = Math.Min(dst.InitialBalanceLow, sLow);
					}
				}
			}

			minB = dst.Rows.Keys.Min();
			maxB = dst.Rows.Keys.Max();
			for (int b = minB + 1; b < maxB; b++)
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
			return dst;
		}

		private static DayProfile BuildRenderSessionProfile(DayProfile src, double baseStep, int groupMult)
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
				SessionId = src.SessionId,
				SessionName = src.SessionName,
				SessionSlot = src.SessionSlot,
				SessionCount = src.SessionCount,
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
				IsMerged = src.IsMerged,
				IsSplitExpanded = src.IsSplitExpanded,
				EffectiveStep = newStep,
				SourceProfileKeys = new List<string>(src.SourceProfileKeys)
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

		private List<DateColumnGroup> BuildDateColumnGroups(List<DayProfile> allSessions, double step, float panelLeft, float panelRight)
		{
			List<DateColumnGroup> groups = new List<DateColumnGroup>();
			if (allSessions == null || allSessions.Count == 0)
				return groups;

			Dictionary<DateTime, DateColumnGroup> byDate = new Dictionary<DateTime, DateColumnGroup>();
			for (int i = 0; i < allSessions.Count; i++)
			{
				DayProfile s = allSessions[i];
				DateTime dt = s.SessionDate.Date;
				DateColumnGroup grp;
				if (!byDate.TryGetValue(dt, out grp))
				{
					grp = new DateColumnGroup { Date = dt };
					byDate.Add(dt, grp);
					groups.Add(grp);
				}
				grp.Sessions.Add(s);
			}

			groups.Sort((a, b) => a.Date.CompareTo(b.Date));

			float charCellW = Math.Max(9.6f, Math.Min(11.2f, FontSize * 0.88f));
			float volReserveW = ShowVolumeProfile ? 54f : 0f;

			float totalStripWidth = 0f;
			for (int g = 0; g < groups.Count; g++)
			{
				DateColumnGroup grp = groups[g];
				grp.Sessions.Sort((a, b) => a.SessionSlot.CompareTo(b.SessionSlot));
				float groupW = 0f;
				for (int sIdx = 0; sIdx < grp.Sessions.Count; sIdx++)
				{
					DayProfile day = grp.Sessions[sIdx];
					bool isLatestSession = (g == groups.Count - 1 && sIdx == grp.Sessions.Count - 1);
					day.IsSplitExpanded = IsSessionSplit(day, isLatestSession);

					float minSessionSlotW = day.IsMerged ? 228f : (ShowSummary ? 208f : 192f);
					double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;
					int closeBin = PriceToBin(day.Close, dStep);
					int maxRowCells = day.Rows.Count > 0
						? Math.Max(2, day.Rows.Max(kv => kv.Value.Periods.Count + (kv.Key == closeBin ? 1 : 0)))
						: 8;
					float visualProfileW = 6f + maxRowCells * charCellW + volReserveW + 6f;
					float collapsedW = Math.Max(minSessionSlotW, visualProfileW + 14f);
					float slotW = collapsedW;
					if (day.IsSplitExpanded && day.PeriodSlices.Count > 0)
					{
						int sliceCols = Math.Max(3, day.PeriodSlices.Count);
						int volBarCount = ShowVolumeProfile ? Math.Min(3, Math.Max(1, day.PeriodSlices.Count)) : 0;
						float volAreaW = volBarCount > 0 ? (volBarCount * 28f + (volBarCount - 1) * 8f + 14f) : 0f;
						float splitVisualW = Math.Max(154f, sliceCols * 15.4f + volAreaW + 24f);
						float splitLeadW = FixSessionHeaderAtTop ? Math.Max(visualProfileW + 18f, minSessionSlotW) : (visualProfileW + 18f);
						slotW = splitLeadW + splitVisualW + 8f;
					}
					groupW += slotW;
				}
				grp.Width = Math.Max(160f, groupW);
				totalStripWidth += grp.Width;
			}

			// Reserve 188px on the right edge exclusively for virgin level labels so they never overlap the latest profile or Split Profile
			float leftMargin = panelLeft + 10f;
			float rightLimit = panelRight - 188f;
			float usableW = Math.Max(420f, rightLimit - leftMargin);

			float maxScroll = Math.Max(0f, totalStripWidth - usableW);
			maxScrollCached = maxScroll;

			float scrollRatio = 1.0f;
			if (ChartBars != null && ChartBars.Count > 1)
			{
				int fromIdx = Math.Max(0, ChartBars.FromIndex);
				int toIdx = Math.Max(fromIdx, ChartBars.ToIndex);
				int visBars = Math.Max(1, toIdx - fromIdx + 1);
				int maxFrom = Math.Max(1, ChartBars.Count - visBars);

				if (visBars <= 3 && fromIdx == 0 && ChartBars.Count > 20)
					scrollRatio = 1.0f;
				else
					scrollRatio = Math.Max(0f, Math.Min(1f, (float)fromIdx / maxFrom));

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

			if (!hasManualPixelScroll || float.IsNaN(currentScrollPx))
				currentScrollPx = scrollRatio * maxScroll;

			currentScrollPx = Math.Max(0f, Math.Min(maxScroll, currentScrollPx));
			float curX = totalStripWidth <= usableW
				? leftMargin
				: (leftMargin - currentScrollPx);

			for (int g = 0; g < groups.Count; g++)
			{
				DateColumnGroup grp = groups[g];
				grp.LeftX = curX;
				grp.RightX = curX + grp.Width;
				curX = grp.RightX;

				float slotX = grp.LeftX;
				for (int sIdx = 0; sIdx < grp.Sessions.Count; sIdx++)
				{
					DayProfile day = grp.Sessions[sIdx];
					float minSessionSlotW = day.IsMerged ? 228f : (ShowSummary ? 208f : 192f);
					double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;
					int closeBin = PriceToBin(day.Close, dStep);
					int maxRowCells = day.Rows.Count > 0
						? Math.Max(2, day.Rows.Max(kv => kv.Value.Periods.Count + (kv.Key == closeBin ? 1 : 0)))
						: 8;
					float visualProfileW = 6f + maxRowCells * charCellW + volReserveW + 6f;
					float collapsedW = Math.Max(minSessionSlotW, visualProfileW + 14f);
					float slotWidth = collapsedW;
					if (day.IsSplitExpanded && day.PeriodSlices.Count > 0)
					{
						int sliceCols = Math.Max(3, day.PeriodSlices.Count);
						int volBarCount = ShowVolumeProfile ? Math.Min(3, Math.Max(1, day.PeriodSlices.Count)) : 0;
						float volAreaW = volBarCount > 0 ? (volBarCount * 28f + (volBarCount - 1) * 8f + 14f) : 0f;
						float splitVisualW = Math.Max(154f, sliceCols * 15.4f + volAreaW + 24f);
						float splitLeadW = FixSessionHeaderAtTop ? Math.Max(visualProfileW + 18f, minSessionSlotW) : (visualProfileW + 18f);
						slotWidth = splitLeadW + splitVisualW + 8f;
					}
					day.SlotLeftX = slotX;
					day.SlotRightX = slotX + slotWidth;
					day.IbX = slotX + 6f;
					day.CollapsedRightX = day.IbX + visualProfileW;
					slotX += slotWidth;
				}
			}

			return groups;
		}

		private enum LevelBreakMode
		{
			CloseAbove = 0,
			CloseBelow = 1,
			CrossClose = 2
		}

		private DateTime GetReferenceNow(List<DayProfile> sessions = null)
		{
			bool isPlayback = false;
			try
			{
				isPlayback = NinjaTrader.Cbi.Connection.PlaybackConnection != null
					&& NinjaTrader.Cbi.Connection.PlaybackConnection.Status == ConnectionStatus.Connected;
			}
			catch { }

			if (isPlayback && sessions != null && sessions.Count > 0)
			{
				DateTime maxSample = DateTime.MinValue;
				for (int i = 0; i < sessions.Count; i++)
				{
					if (sessions[i] != null && sessions[i].LastSampleTime > maxSample)
						maxSample = sessions[i].LastSampleTime;
				}
				if (maxSample > DateTime.MinValue)
					return maxSample;
			}
			return DateTime.Now;
		}

		private DateTime GetTradingDayCloseTime(DateTime sessionDate)
		{
			DateTime dt = sessionDate.Date;
			ProfileSession[] sessions = (activeSessions != null && activeSessions.Length > 0) ? activeSessions : GetProfileSessions();
			if (sessions == null || sessions.Length == 0)
				return dt.AddDays(1).AddHours(2.5);

			DateTime maxClose = DateTime.MinValue;
			for (int i = 0; i < sessions.Length; i++)
			{
				ProfileSession ps = sessions[i];
				int dur = ps.CloseSeconds > ps.OpenSeconds
					? (ps.CloseSeconds - ps.OpenSeconds)
					: (24 * 3600 - ps.OpenSeconds + ps.CloseSeconds);
				if (dur <= 0)
					dur = 24 * 3600;
				DateTime c = dt.AddSeconds(ps.OpenSeconds + dur);
				if (c > maxClose)
					maxClose = c;
			}
			return maxClose;
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
				? 0.01
				: Instrument.MasterInstrument.TickSize;
			return Math.Max(1e-7, tick * 0.15);
		}

		private static bool IsRawSessionRunning(DayProfile rawSess, bool isLatestRaw, DateTime refNow)
		{
			if (!isLatestRaw || rawSess == null)
				return false;
			if (rawSess.SessionCloseTime > DateTime.MinValue)
				return refNow < rawSess.SessionCloseTime;
			int exp = Math.Max(1, rawSess.ExpectedPeriodCount);
			int prn = Math.Max(rawSess.PeriodSlices.Count, rawSess.LastPeriod + 1);
			return prn < exp;
		}

		private bool IsSessionRunning(DayProfile day, bool isLatestSession)
		{
			if (!isLatestSession || day == null)
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
					23, 59, 59).AddHours(3);
				return refNow < monthEnd;
			}
			if (mode == MarketProfilePeriodMode.Weekly)
			{
				DateTime weekEnd = GetTradingDayCloseTime(day.SessionDate.Date.AddDays(4));
				return refNow < weekEnd;
			}

			if (day.SessionCloseTime > DateTime.MinValue)
				return refNow < day.SessionCloseTime;

			int exp = Math.Max(1, day.ExpectedPeriodCount);
			int prn = Math.Max(day.PeriodSlices.Count, day.LastPeriod + 1);
			return prn < exp;
		}

		private bool IsTpoSliceClosed(DayProfile session, PeriodSlice slice, bool isSessionRunning, DateTime refNow)
		{
			if (session == null || slice == null)
				return false;
			if (!isSessionRunning)
				return true;
			if (slice.PeriodIndex < session.LastPeriod)
				return true;

			if (EffectiveProfileMode == MarketProfilePeriodMode.Daily
				&& !session.IsMerged
				&& session.EffectiveOpenTime > DateTime.MinValue
				&& session.SessionCloseTime > session.EffectiveOpenTime)
			{
				double activeWinMin = Math.Max((double)Math.Max(1, TpoIntervalMinutes), (session.SessionCloseTime - session.EffectiveOpenTime).TotalMinutes);
				double bracketMin = activeWinMin / Math.Max(1, session.ExpectedPeriodCount);
				DateTime sliceEnd = session.EffectiveOpenTime.AddMinutes((slice.PeriodIndex + 1) * bracketMin);
				return refNow >= sliceEnd;
			}

			if (session.SessionCloseTime > DateTime.MinValue && refNow >= session.SessionCloseTime)
				return true;

			return false;
		}

		private bool IsRawTpoSliceClosed(DayProfile rawSess, PeriodSlice slice, bool isRawRunning, DateTime refNow)
		{
			if (rawSess == null || slice == null)
				return false;
			if (!isRawRunning)
				return true;
			if (slice.PeriodIndex < rawSess.LastPeriod)
				return true;

			if (rawSess.EffectiveOpenTime > DateTime.MinValue && rawSess.SessionCloseTime > rawSess.EffectiveOpenTime)
			{
				double activeWinMin = Math.Max((double)Math.Max(1, TpoIntervalMinutes), (rawSess.SessionCloseTime - rawSess.EffectiveOpenTime).TotalMinutes);
				double bracketMin = activeWinMin / Math.Max(1, rawSess.ExpectedPeriodCount);
				DateTime sliceEnd = rawSess.EffectiveOpenTime.AddMinutes((slice.PeriodIndex + 1) * bracketMin);
				return refNow >= sliceEnd;
			}

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

		private DayProfile FindRenderedSessionForRaw(List<DayProfile> allSessions, DayProfile rawSess)
		{
			if (allSessions == null || allSessions.Count == 0 || rawSess == null)
				return null;

			for (int i = 0; i < allSessions.Count; i++)
			{
				DayProfile d = allSessions[i];
				if (d.ProfileKey == rawSess.ProfileKey || (d.SourceProfileKeys != null && d.SourceProfileKeys.Contains(rawSess.ProfileKey)))
					return d;
			}

			MarketProfilePeriodMode mode = EffectiveProfileMode;
			if (mode == MarketProfilePeriodMode.Weekly)
			{
				DateTime wkMon = GetMondayOfWeek(rawSess.SessionDate);
				for (int i = 0; i < allSessions.Count; i++)
				{
					if (allSessions[i].SessionDate.Date == wkMon)
						return allSessions[i];
				}
			}
			else if (mode == MarketProfilePeriodMode.Monthly)
			{
				for (int i = 0; i < allSessions.Count; i++)
				{
					if (allSessions[i].SessionDate.Year == rawSess.SessionDate.Year && allSessions[i].SessionDate.Month == rawSess.SessionDate.Month)
						return allSessions[i];
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
			List<DayProfile> sessions,
			int currentIdx,
			double levelPrice,
			float xOriginEnd,
			float panelRight,
			DateTime refNow,
			double eps,
			LevelBreakMode breakMode,
			out float drawEndX)
		{
			DayProfile origin = (sessions != null && currentIdx >= 0 && currentIdx < sessions.Count) ? sessions[currentIdx] : null;
			double originClose = origin != null ? origin.Close : levelPrice;
			double originMid = origin != null ? 0.5 * (origin.High + origin.Low) : levelPrice;
			int side = originClose > levelPrice + eps ? 1 : (originClose < levelPrice - eps ? -1 : 0);
			bool useCloseRule = (VirginityBreakRule == VirginityBreakRuleMode.CloseBased);

			for (int j = currentIdx + 1; j < sessions.Count; j++)
			{
				DayProfile next = sessions[j];
				bool broken = false;

				if (!useCloseRule)
				{
					broken = DoesCrossBreakLevel(next.High, next.Low, levelPrice, breakMode, side);
				}
				else
				{
					bool nextRunning = IsSessionRunning(next, j == sessions.Count - 1);
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
			List<DayProfile> rawSessions,
			int afterRawIdx,
			List<DayProfile> renderedSessions,
			double levelPrice,
			float xOriginEnd,
			float panelLeft,
			float panelRight,
			DateTime refNow,
			double eps,
			LevelBreakMode breakMode,
			out float drawEndX)
		{
			DayProfile origin = (rawSessions != null && afterRawIdx >= 0 && afterRawIdx < rawSessions.Count) ? rawSessions[afterRawIdx] : null;
			double originClose = origin != null ? origin.Close : levelPrice;
			double originMid = origin != null ? 0.5 * (origin.High + origin.Low) : levelPrice;
			int side = originClose > levelPrice + eps ? 1 : (originClose < levelPrice - eps ? -1 : 0);
			bool useCloseRule = (VirginityBreakRule == VirginityBreakRuleMode.CloseBased);

			for (int k = afterRawIdx + 1; k < rawSessions.Count; k++)
			{
				DayProfile nextRaw = rawSessions[k];
				bool broken = false;

				if (!useCloseRule)
				{
					broken = DoesCrossBreakLevel(nextRaw.High, nextRaw.Low, levelPrice, breakMode, side);
				}
				else
				{
					bool nextRunning = IsRawSessionRunning(nextRaw, k == rawSessions.Count - 1, refNow);
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
					DayProfile breakCol = FindRenderedSessionForRaw(renderedSessions, nextRaw);
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
					if (chartControl.Properties.BarDistance > 25f)
						chartControl.Properties.BarDistance = 4f;
				}
				if (chartScale != null && chartScale.Properties != null)
				{
					chartScale.Properties.YAxisRangeType = YAxisRangeType.Automatic;
				}
				chartControl.InvalidateVisual();
			}

			EnsureMouseHookAttached();
			if (State == State.Realtime)
			{
				AttachLiveStreamAndProviderSync();
				if (highestBackfillDaysRequested < 42 && !isBackfillInFlight)
					TriggerHistoricalBackfillIfNeeded(Math.Max(42, DaysToDisplay));
			}

			bool needIntradayGapHeal = false;
			lock (profileLock)
			{
				activeChartButtons.Clear();
				if (profiles.Count == 0)
					return;

				List<DayProfile> rawSessions = profiles.Values.Where(p => p.Rows.Count > 0).ToList();
				if (State == State.Realtime && rawSessions.Count > 0)
				{
					DayProfile latestRaw = rawSessions[rawSessions.Count - 1];
					if (latestRaw.PeriodSlices.Count < latestRaw.LastPeriod + 1 && !isProviderSyncInFlight && (DateTime.UtcNow - lastGapHealRequestUtc).TotalSeconds > 12.0)
					{
						lastGapHealRequestUtc = DateTime.UtcNow;
						needIntradayGapHeal = true;
					}
				}
				List<DayProfile> allSessions = BuildDisplaySessions(rawSessions);
				if (allSessions.Count == 0)
					return;

				double baseStep = GetPriceStep();
				for (int i = 0; i < allSessions.Count; i++)
				{
					DayProfile srcSess = allSessions[i];
					if (!srcSess.IsMerged
						&& srcSess.CachedContinuousProfile != null
						&& srcSess.CachedContinuousRevision == srcSess.DataRevision
						&& Math.Abs(srcSess.CachedContinuousStep - baseStep) <= 1e-9)
					{
						srcSess.CachedContinuousProfile.IsSplitExpanded = srcSess.IsSplitExpanded;
						allSessions[i] = srcSess.CachedContinuousProfile;
					}
					else
					{
						DayProfile builtCont = BuildContinuousSessionProfile(srcSess, baseStep);
						builtCont.DataRevision = srcSess.DataRevision;
						if (!srcSess.IsMerged)
						{
							srcSess.CachedContinuousProfile = builtCont;
							srcSess.CachedContinuousRevision = srcSess.DataRevision;
							srcSess.CachedContinuousStep = baseStep;
						}
						allSessions[i] = builtCont;
					}
				}

				float panelLeft = (float)ChartPanel.X;
				float panelRight = (float)(ChartPanel.X + ChartPanel.W);
				float panelTop = (float)ChartPanel.Y;
				float panelBottom = (float)(ChartPanel.Y + ChartPanel.H);

				List<DateColumnGroup> dateGroups = BuildDateColumnGroups(allSessions, baseStep, panelLeft, panelRight);
				if (dateGroups.Count == 0)
					return;

				double prelimMinPrice = double.MaxValue;
				double prelimMaxPrice = double.MinValue;
				for (int g = 0; g < dateGroups.Count; g++)
				{
					DateColumnGroup grp = dateGroups[g];
					if (grp.RightX < panelLeft + 30f || grp.LeftX > panelRight - 180f)
						continue;
					for (int s = 0; s < grp.Sessions.Count; s++)
					{
						DayProfile d = grp.Sessions[s];
						if (d.IbX < panelLeft + 4f || d.CollapsedRightX > panelRight - 178f)
							continue;
						if (d.Low < prelimMinPrice) prelimMinPrice = d.Low;
						if (d.High > prelimMaxPrice) prelimMaxPrice = d.High;
					}
				}
				if (prelimMinPrice == double.MaxValue || prelimMaxPrice == double.MinValue)
				{
					int tailStart = Math.Max(0, allSessions.Count - 3);
					for (int i = tailStart; i < allSessions.Count; i++)
					{
						DayProfile d = allSessions[i];
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
				float targetSpanPx = usableBotY - usableTopY;

				double prelimSpan = Math.Max(baseStep * 6.0, prelimMaxPrice - prelimMinPrice);
				double rawRowCount = prelimSpan / baseStep + 2.0;
				float rawPxPerRow = targetSpanPx / (float)Math.Max(1.0, rawRowCount);
				int groupMult = 1;
				if (AutoScaleTpoByScreenResolution)
				{
					groupMult = rawPxPerRow < MinReadableTpoRowPx
						? Math.Max(1, (int)Math.Ceiling(MinReadableTpoRowPx / Math.Max(0.4f, rawPxPerRow)))
						: 1;
					for (int i = 0; i < allSessions.Count; i++)
					{
						DayProfile vd = allSessions[i];
						if (vd.IbX < panelLeft + 4f || vd.CollapsedRightX > panelRight - 178f || vd.Rows.Count == 0)
							continue;
						int daySpanBins = vd.Rows.Keys.Max() - vd.Rows.Keys.Min() + 1;
						int reqMult = (int)Math.Ceiling(daySpanBins / Math.Max(12.0, targetSpanPx / MinReadableTpoRowPx));
						if (reqMult > groupMult)
							groupMult = reqMult;
					}
				}

				double step = baseStep * groupMult;
				if (groupMult > 1)
				{
					for (int i = 0; i < allSessions.Count; i++)
					{
						DayProfile contSess = allSessions[i];
						if (!contSess.IsMerged
							&& contSess.CachedGroupedProfile != null
							&& contSess.CachedGroupedRevision == contSess.DataRevision
							&& contSess.CachedGroupedMult == groupMult
							&& Math.Abs(contSess.CachedGroupedBaseStep - baseStep) <= 1e-9)
						{
							contSess.CachedGroupedProfile.IsSplitExpanded = contSess.IsSplitExpanded;
							allSessions[i] = contSess.CachedGroupedProfile;
						}
						else
						{
							DayProfile grpProf = BuildRenderSessionProfile(contSess, baseStep, groupMult);
							grpProf.DataRevision = contSess.DataRevision;
							if (!contSess.IsMerged)
							{
								contSess.CachedGroupedProfile = grpProf;
								contSess.CachedGroupedRevision = contSess.DataRevision;
								contSess.CachedGroupedBaseStep = baseStep;
								contSess.CachedGroupedMult = groupMult;
							}
							allSessions[i] = grpProf;
						}
					}
					dateGroups = BuildDateColumnGroups(allSessions, step, panelLeft, panelRight);
					if (dateGroups.Count == 0)
						return;
				}

				double visMinPrice = double.MaxValue;
				double visMaxPrice = double.MinValue;
				for (int g = 0; g < dateGroups.Count; g++)
				{
					DateColumnGroup grp = dateGroups[g];
					if (grp.RightX < panelLeft + 30f || grp.LeftX > panelRight - 180f)
						continue;
					for (int s = 0; s < grp.Sessions.Count; s++)
					{
						DayProfile d = grp.Sessions[s];
						if (d.IbX < panelLeft + 4f || d.CollapsedRightX > panelRight - 178f || d.Rows.Count == 0)
							continue;
						double dStep = d.EffectiveStep > 0 ? d.EffectiveStep : step;
						double lowEdge = Math.Min(d.Low, d.Rows.Keys.Min() * dStep - dStep * 0.5);
						double highEdge = Math.Max(d.High, d.Rows.Keys.Max() * dStep + dStep * 0.5);
						if (lowEdge < visMinPrice) visMinPrice = lowEdge;
						if (highEdge > visMaxPrice) visMaxPrice = highEdge;
					}
				}
				if (visMinPrice == double.MaxValue || visMaxPrice == double.MinValue)
				{
					int tailStart = Math.Max(0, allSessions.Count - 3);
					for (int i = tailStart; i < allSessions.Count; i++)
					{
						DayProfile d = allSessions[i];
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

				// Precompute Value Area, Single Print Zones, and unified Price-to-Y mapping across all sessions
				// so every price level aligns 100% across adjacent sessions and the Y-axis
				for (int i = 0; i < allSessions.Count; i++)
				{
					DayProfile d = allSessions[i];
					double dStep = d.EffectiveStep > 0 ? d.EffectiveStep : step;
					int minB = int.MaxValue;
					int maxB = int.MinValue;
					foreach (int bk in d.Rows.Keys)
					{
						if (bk < minB) minB = bk;
						if (bk > maxB) maxB = bk;
					}
					if (minB > maxB) { minB = 0; maxB = 0; }

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
				DxBrush[] sessionBrushes = new DxBrush[SessionColors.Length];
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
					for (int i = 0; i < SessionColors.Length; i++)
						sessionBrushes[i] = new DxBrush(RenderTarget, SessionColors[i]);

					var dashProps = new StrokeStyleProperties
					{
						DashStyle = SharpDX.Direct2D1.DashStyle.Custom,
						DashCap = modernUi ? CapStyle.Round : CapStyle.Flat,
						StartCap = modernUi ? CapStyle.Round : CapStyle.Flat,
						EndCap = modernUi ? CapStyle.Round : CapStyle.Flat
					};

					string uiFontName = modernUi ? "Segoe UI" : "Arial";
					var cardFontWeight = modernUi ? SharpDX.DirectWrite.FontWeight.SemiBold : SharpDX.DirectWrite.FontWeight.Normal;

					using (var dashedStroke = new StrokeStyle(Core.Globals.D2DFactory, dashProps, new float[] { 4f, 3f }))
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
					using (var sessionSepBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(100, 116, 139, 64) : new SharpDX.Color(32, 42, 54)))
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
					using (var btnMergeBgBrush = new DxBrush(RenderTarget, modernUi ? new SharpDX.Color(78, 52, 16, 250) : new SharpDX.Color(68, 48, 16, 250)))
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
						// Disable word wrapping on ALL text formats so text NEVER wraps onto a second line and overlaps!
						cardFormat.TextAlignment = TextAlignment.Leading;
						cardFormat.ParagraphAlignment = ParagraphAlignment.Center;
						cardFormat.WordWrapping = WordWrapping.NoWrap;

						tagCenterFormat.TextAlignment = TextAlignment.Center;
						tagCenterFormat.ParagraphAlignment = ParagraphAlignment.Center;
						tagCenterFormat.WordWrapping = WordWrapping.NoWrap;

						spTagFormat.TextAlignment = TextAlignment.Leading;
						spTagFormat.ParagraphAlignment = ParagraphAlignment.Center;
						spTagFormat.WordWrapping = WordWrapping.NoWrap;

						headerFormat.TextAlignment = TextAlignment.Leading;
						headerFormat.ParagraphAlignment = ParagraphAlignment.Center;
						headerFormat.WordWrapping = WordWrapping.NoWrap;

						dateAxisFormat.TextAlignment = TextAlignment.Center;
						dateAxisFormat.ParagraphAlignment = ParagraphAlignment.Center;
						dateAxisFormat.WordWrapping = WordWrapping.NoWrap;

						labelRightFormat.TextAlignment = TextAlignment.Center;
						labelRightFormat.ParagraphAlignment = ParagraphAlignment.Center;
						labelRightFormat.WordWrapping = WordWrapping.NoWrap;

						List<double> allIbRanges = allSessions
							.Where(d => d.InitialBalanceHigh > d.InitialBalanceLow && d.InitialBalanceLow != double.MaxValue)
							.Select(d => d.InitialBalanceHigh - d.InitialBalanceLow)
							.ToList();
						double avgIbRange = allIbRanges.Count > 0 ? allIbRanges.Average() : step * 6;

						// =========================================================================
						// PASS 1: Draw Studio Backdrop, Background Separators, Virgin Single Print Rays & Virgin Extending Lines
						// =========================================================================
						if (modernUi)
						{
							DrawModernStudioBackdrop(panelLeft, panelTop, panelRight, panelBottom, canvasTopY, 182f, studioGridBrush, spTagFormat, cyanBrush);
						}

						DateTime refNow = GetReferenceNow(rawSessions);
						double breakEps = GetVirginBreakEpsilon();

						// 1A. Closed Monday–Friday Weekly High & Weekly Low Virgin Lines (never on running week)
						if ((ShowWeeklyHigh || ShowWeeklyLow) && rawSessions.Count > 0)
						{
							Dictionary<DateTime, List<int>> weekMap = new Dictionary<DateTime, List<int>>();
							List<DateTime> orderedWeeks = new List<DateTime>();
							for (int rIdx = 0; rIdx < rawSessions.Count; rIdx++)
							{
								DayProfile rs = rawSessions[rIdx];
								if (rs.SessionDate.DayOfWeek < DayOfWeek.Monday || rs.SessionDate.DayOfWeek > DayOfWeek.Friday)
									continue;
								DateTime wkMon = GetMondayOfWeek(rs.SessionDate);
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

								if (wIdx == 0 && orderedWeeks.Count > 1 && rawSessions[wIndices[0]].SessionDate.Date > wkMon.AddDays(1))
									continue;

								bool anyRunningInWeek = false;
								double wkHigh = double.MinValue;
								double wkLow = double.MaxValue;
								int highRawIdx = wIndices[0];
								int lowRawIdx = wIndices[0];
								for (int k = 0; k < wIndices.Count; k++)
								{
									int rIdx = wIndices[k];
									DayProfile rs = rawSessions[rIdx];
									if (IsRawSessionRunning(rs, rIdx == rawSessions.Count - 1, refNow))
										anyRunningInWeek = true;
									if (rs.High > wkHigh)
									{
										wkHigh = rs.High;
										highRawIdx = rIdx;
									}
									if (rs.Low < wkLow)
									{
										wkLow = rs.Low;
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
									DayProfile highCol = FindRenderedSessionForRaw(allSessions, rawSessions[highRawIdx]);
									if (highCol == null || highCol.IbX <= panelRight - 178f)
									{
										float rawOriginX = highCol != null ? highCol.IbX : (panelLeft - 40f);
										float rawOriginEndX = highCol != null ? highCol.CollapsedRightX : (panelLeft - 20f);
										int whBin = PriceToBin(wkHigh, step);
										float yWkHigh = priceToY(whBin * step + step * 0.5);
										if (yWkHigh >= canvasTopY && yWkHigh <= panelBottom - 25f)
										{
											float wkHighEndX;
											bool isWkHighVirgin = GetRawLevelExtensionX(
												rawSessions, lastWkRawIdx, allSessions,
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
													Text = "WeeklyHigh (" + wkLabel + " " + FormatPrice(wkHigh) + ")",
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
									DayProfile lowCol = FindRenderedSessionForRaw(allSessions, rawSessions[lowRawIdx]);
									if (lowCol == null || lowCol.IbX <= panelRight - 178f)
									{
										float rawOriginX = lowCol != null ? lowCol.IbX : (panelLeft - 40f);
										float rawOriginEndX = lowCol != null ? lowCol.CollapsedRightX : (panelLeft - 20f);
										int wlBin = PriceToBin(wkLow, step);
										float yWkLow = priceToY(wlBin * step - step * 0.5);
										if (yWkLow >= canvasTopY && yWkLow <= panelBottom - 25f)
										{
											float wkLowEndX;
											bool isWkLowVirgin = GetRawLevelExtensionX(
												rawSessions, lastWkRawIdx, allSessions,
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
													Text = "WeeklyLow (" + wkLabel + " " + FormatPrice(wkLow) + ")",
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

						// 1B. Closed Day DayHigh & DayLow Virgin Lines (strictly closed days/sessions only — never on running day or running session)
						if ((ShowSwingHigh || ShowSwingLow) && rawSessions.Count > 0)
						{
							Dictionary<DateTime, List<int>> dayMap = new Dictionary<DateTime, List<int>>();
							List<DateTime> orderedDays = new List<DateTime>();
							for (int rIdx = 0; rIdx < rawSessions.Count; rIdx++)
							{
								DateTime dDate = rawSessions[rIdx].SessionDate.Date;
								List<int> dList;
								if (!dayMap.TryGetValue(dDate, out dList))
								{
									dList = new List<int>();
									dayMap[dDate] = dList;
									orderedDays.Add(dDate);
								}
								dList.Add(rIdx);
							}
							orderedDays.Sort();

							for (int dIdx = 0; dIdx < orderedDays.Count; dIdx++)
							{
								DateTime dDate = orderedDays[dIdx];
								List<int> dIndices = dayMap[dDate];
								if (dIndices.Count == 0)
									continue;

								bool anyRunningInDay = false;
								double dHigh = double.MinValue;
								double dLow = double.MaxValue;
								int highRawIdx = dIndices[0];
								int lowRawIdx = dIndices[0];
								for (int k = 0; k < dIndices.Count; k++)
								{
									int rIdx = dIndices[k];
									DayProfile rs = rawSessions[rIdx];
									if (IsRawSessionRunning(rs, rIdx == rawSessions.Count - 1, refNow))
										anyRunningInDay = true;
									if (rs.High > dHigh)
									{
										dHigh = rs.High;
										highRawIdx = rIdx;
									}
									if (rs.Low < dLow)
									{
										dLow = rs.Low;
										lowRawIdx = rIdx;
									}
								}

								bool isDayClosed = !anyRunningInDay && (refNow >= GetTradingDayCloseTime(dDate));
								if (!isDayClosed)
									continue;

								int lastDayRawIdx = dIndices[dIndices.Count - 1];
								string dayDateLbl = dDate.ToString("dd MMM", CultureInfo.InvariantCulture);

								if (ShowSwingHigh && dHigh > double.MinValue)
								{
									DayProfile highCol = FindRenderedSessionForRaw(allSessions, rawSessions[highRawIdx]);
									if (highCol == null || highCol.IbX <= panelRight - 178f)
									{
										float rawOriginX = highCol != null ? highCol.IbX : (panelLeft - 40f);
										float rawOriginEndX = highCol != null ? highCol.CollapsedRightX : (panelLeft - 20f);
										int dhBin = PriceToBin(dHigh, step);
										float yDayHigh = priceToY(dhBin * step + step * 0.5);
										if (yDayHigh >= canvasTopY && yDayHigh <= panelBottom - 25f)
										{
											float dHighEndX;
											bool isDayHighVirgin = GetRawLevelExtensionX(
												rawSessions, lastDayRawIdx, allSessions,
												dHigh, rawOriginEndX, panelLeft, panelRight,
												refNow, breakEps, LevelBreakMode.CloseAbove, out dHighEndX);
											float drawStartX = Math.Max(panelLeft + 2f, rawOriginX);
											if (dHighEndX > drawStartX + 1.5f)
												DrawHairlineHorizontal(drawStartX, dHighEndX, yDayHigh, highSwingLineBrush, 0.80f);

											if (isDayHighVirgin)
											{
												rightEdgeTags.Add(new RightEdgeTag
												{
													Price = dHigh,
													IdealY = yDayHigh - 7.5f,
													Height = 15f,
													Text = "DayHigh (" + dayDateLbl + " " + FormatPrice(dHigh) + ")",
													Brush = highExtremeBrush,
													Priority = 3
												});
											}
										}
									}
								}

								if (ShowSwingLow && dLow < double.MaxValue)
								{
									DayProfile lowCol = FindRenderedSessionForRaw(allSessions, rawSessions[lowRawIdx]);
									if (lowCol == null || lowCol.IbX <= panelRight - 178f)
									{
										float rawOriginX = lowCol != null ? lowCol.IbX : (panelLeft - 40f);
										float rawOriginEndX = lowCol != null ? lowCol.CollapsedRightX : (panelLeft - 20f);
										int dlBin = PriceToBin(dLow, step);
										float yDayLow = priceToY(dlBin * step - step * 0.5);
										if (yDayLow >= canvasTopY && yDayLow <= panelBottom - 25f)
										{
											float dLowEndX;
											bool isDayLowVirgin = GetRawLevelExtensionX(
												rawSessions, lastDayRawIdx, allSessions,
												dLow, rawOriginEndX, panelLeft, panelRight,
												refNow, breakEps, LevelBreakMode.CloseBelow, out dLowEndX);
											float drawStartX = Math.Max(panelLeft + 2f, rawOriginX);
											if (dLowEndX > drawStartX + 1.5f)
												DrawHairlineHorizontal(drawStartX, dLowEndX, yDayLow, lowSwingLineBrush, 0.80f);

											if (isDayLowVirgin)
											{
												rightEdgeTags.Add(new RightEdgeTag
												{
													Price = dLow,
													IdealY = yDayLow - 7.5f,
													Height = 15f,
													Text = "DayLow (" + dayDateLbl + " " + FormatPrice(dLow) + ")",
													Brush = lowExtremeBrush,
													Priority = 3
												});
											}
										}
									}
								}
							}
						}

						// 1C. Session Separators, Single Prints, PoorHigh/Low, VAH/VAL, VPoC/VPPoC & MidLine
						for (int g = 0; g < dateGroups.Count; g++)
						{
							DateColumnGroup group = dateGroups[g];
							if (group.LeftX > panelRight - 178f)
								continue;

							if (group.RightX >= panelLeft)
							{
								if (group.LeftX >= panelLeft && group.LeftX <= panelRight - 178f)
									RenderTarget.DrawLine(new Vector2(group.LeftX, gridTopY), new Vector2(group.LeftX, panelBottom), daySepBrush, 1.6f);
								if (group.RightX >= panelLeft && group.RightX <= panelRight - 178f)
									RenderTarget.DrawLine(new Vector2(group.RightX, gridTopY), new Vector2(group.RightX, panelBottom), daySepBrush, 1.6f);
							}

							for (int sIdx = 0; sIdx < group.Sessions.Count; sIdx++)
							{
								DayProfile day = group.Sessions[sIdx];
								int globalIdx = allSessions.IndexOf(day);
								bool isLatestSession = (globalIdx == allSessions.Count - 1);
								bool isRunningSession = IsSessionRunning(day, isLatestSession);
								double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;

								float xSlotLeft = day.SlotLeftX;
								float xIb = day.IbX;
								float xCollapsedRight = day.CollapsedRightX;

								if (xIb > panelRight - 178f)
									continue;

								if (sIdx > 0 && xSlotLeft >= panelLeft && xSlotLeft <= panelRight - 178f)
								{
									RenderTarget.DrawLine(
										new Vector2(xSlotLeft, gridTopY),
										new Vector2(xSlotLeft, panelBottom - 24f),
										sessionSepBrush, 1.0f, dashedStroke);
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

								// 1. Extending Buying & Selling Single Print Zones (TPO Close Rule: Buying SP breaks on TPO close < bottom SL; Selling SP breaks on TPO close > top SL)
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
											allSessions,
											globalIdx,
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
													Text = spType + " (" + day.SessionName + " " + FormatPrice(spSlPrice) + ")",
													Brush = spZone.IsBuying ? singleBuyBorderBrush : singleSellBorderBrush,
													FillBrush = spZone.IsBuying ? spBuyPillBgBrush : spSellPillBgBrush,
													BorderBrush = spZone.IsBuying ? singleBuyBorderBrush : singleSellBorderBrush,
													Priority = 3
												});
											}
										}
									}
								}

								// 2. Extending PoorHigh / ABPoorHigh & PoorLow / ABPoorLow Levels (Closed sessions only; DayHigh/DayLow handled per Closed Day in 1B)
								PriceRow topRow = day.Rows[maxBin];
								bool isAbPoorHigh = topRow.Periods.Contains(0) && topRow.Periods.Contains(1);
								bool isPoorHigh = !isAbPoorHigh && topRow.Periods.Count >= 2;
								bool enablePoorHigh = !isRunningSession && ShowPoorHighLow && ShowPoorHigh && (isAbPoorHigh || isPoorHigh);

								PriceRow botRow = day.Rows[minBin];
								bool isAbPoorLow = botRow.Periods.Contains(0) && botRow.Periods.Contains(1);
								bool isPoorLow = !isAbPoorLow && botRow.Periods.Count >= 2;
								bool enablePoorLow = !isRunningSession && ShowPoorHighLow && ShowPoorLow && (isAbPoorLow || isPoorLow);

								if (enablePoorHigh && profileHighY >= canvasTopY && profileHighY <= panelBottom - 25f)
								{
									string highLevelName = isAbPoorHigh ? "ABPoorHigh" : "PoorHigh";
									float drawEndX;
									bool isHighVirgin = GetLevelExtensionX(allSessions, globalIdx, day.High, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CloseAbove, out drawEndX);
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
											Text = highLevelName + " (" + day.SessionName + " " + FormatPrice(day.High) + ")",
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
									bool isLowVirgin = GetLevelExtensionX(allSessions, globalIdx, day.Low, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CloseBelow, out drawEndX);
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
											Text = lowLevelName + " (" + day.SessionName + " " + FormatPrice(day.Low) + ")",
											Brush = lowExtremeBrush,
											BorderBrush = lowExtremeBorderBrush,
											Priority = 4
										});
									}
								}

								// 3. Extending ValueAreaHigh (VAH) & ValueAreaLow (VAL) Lines (completely hidden on all past & current sessions when unchecked)
								double vahPrice = day.VahBin * dStep;
								double valPrice = day.ValBin * dStep;
								float yVahLine = SessionPriceToY(day, day.VahBin * dStep + dStep * 0.5, step);
								float yValLine = SessionPriceToY(day, day.ValBin * dStep - dStep * 0.5, step);
								bool allowVahExt = ShowVirginVAH && (!isRunningSession || (EnableRunningSessionLevels && ShowRunningSessionVAH));
								bool allowValExt = ShowVirginVAL && (!isRunningSession || (EnableRunningSessionLevels && ShowRunningSessionVAL));

								if (allowVahExt && yVahLine >= canvasTopY && yVahLine <= panelBottom - 25f)
								{
									float vahDrawEndX;
									bool isVahVirgin = GetLevelExtensionX(allSessions, globalIdx, vahPrice, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CrossClose, out vahDrawEndX);
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
											Text = "ValueAreaHigh (" + day.SessionName + " " + FormatPrice(vahPrice) + ")",
											Brush = vaVirginBrush,
											Priority = 2
										});
									}
								}

								if (allowValExt && yValLine >= canvasTopY && yValLine <= panelBottom - 25f)
								{
									float valDrawEndX;
									bool isValVirgin = GetLevelExtensionX(allSessions, globalIdx, valPrice, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CrossClose, out valDrawEndX);
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
											Text = "ValueAreaLow (" + day.SessionName + " " + FormatPrice(valPrice) + ")",
											Brush = vaVirginBrush,
											Priority = 2
										});
									}
								}

								// 4. Extending VPoC (POC) & VPPoC (PPoC) Lines (completely hidden on all past & current sessions when unchecked)
								double vpocPrice = day.VolumePocBin * dStep;
								double tpoPocPrice = day.PocBin * dStep;
								float yVpoc = SessionPriceToY(day, vpocPrice, step);
								float yTpoPoc = SessionPriceToY(day, tpoPocPrice, step);
								bool allowVpoc = ShowVirginVPoC && (!isRunningSession || (EnableRunningSessionLevels && ShowRunningSessionPOC));
								bool allowVppoc = ShowVirginVPPoC && (!isRunningSession || (EnableRunningSessionLevels && ShowRunningSessionPPOC));

								if (allowVpoc && yVpoc >= canvasTopY && yVpoc <= panelBottom - 25f)
								{
									float vpocDrawEndX;
									bool isVpocVirgin = GetLevelExtensionX(allSessions, globalIdx, vpocPrice, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CrossClose, out vpocDrawEndX);
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
											Text = "VPoC " + FormatPrice(vpocPrice) + " (" + day.SessionName + ")",
											Brush = orangeBrush,
											Priority = 3
										});
									}
								}

								if (allowVppoc && (day.PocBin != day.VolumePocBin || !allowVpoc) && yTpoPoc >= canvasTopY && yTpoPoc <= panelBottom - 25f)
								{
									float tpoDrawEndX;
									bool isVppocVirgin = GetLevelExtensionX(allSessions, globalIdx, tpoPocPrice, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CrossClose, out tpoDrawEndX);
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
											Text = "VPPoC " + FormatPrice(tpoPocPrice) + " (" + day.SessionName + ")",
											Brush = yellowBrush,
											Priority = 2
										});
									}
								}

								// 5. Session Midline & Extending Virgin Midline (completely hidden on all past & current sessions when ShowVirginMidLine is unchecked)
								double midPrice = (day.High + day.Low) * 0.5;
								float yMid = (profileHighY + profileLowY) * 0.5f;
								if (ShowVirginMidLine && !isRunningSession && yMid >= canvasTopY && yMid <= panelBottom - 25f)
								{
									float midDrawEndX;
									bool isMidVirgin = GetLevelExtensionX(allSessions, globalIdx, midPrice, xCollapsedRight, panelRight, refNow, breakEps, LevelBreakMode.CrossClose, out midDrawEndX);
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
											Text = "MidLine (" + day.SessionName + " " + FormatPrice(midPrice) + ")",
											Brush = midLineBrush,
											Priority = 2
										});
									}
								}
							}
						}

						// =========================================================================
						// PASS 2: Draw Foreground Profiles, Buying/Selling Single Print Highlights,
						// TPO Letters, Volume Profile, Opaque Pill Badges, and 9-Line Summary Cards
						// =========================================================================
						const float bottomBannerH = 26f;
						float fullBannerStripW = Math.Max(40f, (panelRight - 180f) - (panelLeft + 2f));
						RenderTarget.FillRectangle(new RectangleF(panelLeft + 2f, panelBottom - (bottomBannerH + 1f), fullBannerStripW, bottomBannerH), dateBannerBgBrush);

						if (FixSessionHeaderAtTop)
						{
							RectangleF topSessionStripRect = new RectangleF(panelLeft + 2f, topSessionBarY, fullBannerStripW, 35f);
							RenderTarget.FillRectangle(topSessionStripRect, dateBannerBgBrush);
							RenderTarget.DrawRectangle(topSessionStripRect, cardBorderBrush, 1.0f);

							for (int tg = 0; tg < dateGroups.Count; tg++)
							{
								DateColumnGroup tGrp = dateGroups[tg];
								if (tGrp.RightX < panelLeft || tGrp.LeftX > panelRight - 178f)
									continue;
								if (tGrp.LeftX >= panelLeft + 2f && tGrp.LeftX <= panelRight - 180f)
									RenderTarget.DrawLine(new Vector2(tGrp.LeftX, topSessionBarY), new Vector2(tGrp.LeftX, topSessionBarY + 35f), daySepBrush, 1.6f);
								if (tGrp.RightX >= panelLeft + 2f && tGrp.RightX <= panelRight - 180f)
									RenderTarget.DrawLine(new Vector2(tGrp.RightX, topSessionBarY), new Vector2(tGrp.RightX, topSessionBarY + 35f), daySepBrush, 1.6f);
								for (int ts = 1; ts < tGrp.Sessions.Count; ts++)
								{
									float divX = tGrp.Sessions[ts].SlotLeftX;
									if (divX >= panelLeft + 2f && divX <= panelRight - 180f)
										RenderTarget.DrawLine(new Vector2(divX, topSessionBarY), new Vector2(divX, topSessionBarY + 35f), cardBorderBrush, 1.0f);
								}
							}
						}

						// Live TPO Counter with Running TPO Letter in Top-Right Dock (replacing Key Profile Levels for maximum visibility)
						DrawTopRightTpoCountdownBox(
							rawSessions,
							allSessions,
							panelRight,
							FixSessionHeaderAtTop ? topSessionBarY : (canvasTopY - 27f),
							FixSessionHeaderAtTop ? 35f : 26f,
							180f,
							modernUi);

						for (int g = 0; g < dateGroups.Count; g++)
						{
							DateColumnGroup group = dateGroups[g];
							if (group.RightX < panelLeft || group.LeftX > panelRight - 178f)
								continue;

							// Opaque Single-Line Bottom Date & Merge Day Banner (protected from NinjaTrader's bottom-left watermark & strictly clipped per column)
							float dateBannerY = panelBottom - (bottomBannerH + 1f);
							float clampedBannerLeft = Math.Max(panelLeft + 4f, group.LeftX + 1f);
							float clampedBannerRight = Math.Min(panelRight - 180f, group.RightX - 1f);
							float bannerW = clampedBannerRight - clampedBannerLeft;
							if (bannerW > 54f)
							{
								RectangleF dateBannerRect = new RectangleF(clampedBannerLeft, dateBannerY, bannerW, bottomBannerH);
								RenderTarget.FillRectangle(dateBannerRect, dateBannerBgBrush);
								RenderTarget.DrawRectangle(dateBannerRect, cardBorderBrush, 1.0f);

								// Leave clearance on far-left column for NinjaTrader's built-in '© 2026 NinjaTrader, LLC' overlay
								float safeTextLeft = Math.Max(clampedBannerLeft + 6f, panelLeft + 140f);

								MarketProfilePeriodMode curMode = EffectiveProfileMode;
								if (curMode == MarketProfilePeriodMode.Daily)
								{
									int totalDayTpos = 0;
									List<string> sessionTpoParts = new List<string>();
									for (int si = 0; si < activeSessions.Length; si++)
									{
										int sTpos = GetSessionPeriodCount(activeSessions[si]);
										totalDayTpos += sTpos;
										sessionTpoParts.Add(string.Format(CultureInfo.InvariantCulture, "{0} ({1} TPO)", activeSessions[si].Name, sTpos));
									}
									int dispTpos = totalDayTpos > 0 ? totalDayTpos : 46;
									int sessCnt = Math.Max(1, activeSessions.Length);
									string fullSummary = sessionTpoParts.Count > 0
										? string.Format(CultureInfo.InvariantCulture, "   [ {0} Sessions: {1} — {2} TPOs ]",
											sessCnt, string.Join(" | ", sessionTpoParts), dispTpos)
										: "   [ 3 Sessions: Asia (20 TPO) | London (11 TPO) | New York (15 TPO) — 46 TPOs ]";
									string medSummary = string.Format(CultureInfo.InvariantCulture, "  [ {0} Sessions · {1} TPOs ]", sessCnt, dispTpos);

									List<DayProfile> rawForDate = rawSessions.Where(rs => rs.SessionDate.Date == group.Date.Date).ToList();
									bool canShowMergeBtn = ShowChartButtons && rawForDate.Count > 1 && bannerW >= 136f && (clampedBannerRight - 84f > panelLeft + 132f || clampedBannerLeft >= panelLeft + 132f);
									bool isDayMerged = group.Sessions.Count == 1 && group.Sessions[0].IsMerged;
									bool useShortBtn = bannerW < 224f || (clampedBannerRight - safeTextLeft) < 175f;
									float mergeDayBtnW = canShowMergeBtn ? (useShortBtn ? 72f : 94f) : 0f;

									// Interactive High-Contrast Merge Day / Unmerge Day Button on the Date Banner
									if (canShowMergeBtn)
									{
										List<string> dayRawKeys = rawForDate.Select(rs => rs.ProfileKey).ToList();
										string dayBtnText = isDayMerged
											? (useShortBtn ? "Unmerge" : "Unmerge Day")
											: (useShortBtn ? "Merge" : "Merge Day");
										RectangleF dayBtnRect = new RectangleF(clampedBannerRight - mergeDayBtnW - 4f, dateBannerY + 4f, mergeDayBtnW, bottomBannerH - 8f);
										using (var mergeDayFill = new DxBrush(RenderTarget, isDayMerged ? new SharpDX.Color(114, 59, 12, 252) : new SharpDX.Color(12, 68, 98, 252)))
										using (var mergeDayBorder = new DxBrush(RenderTarget, isDayMerged ? new SharpDX.Color(251, 191, 36, 255) : new SharpDX.Color(56, 189, 248, 245)))
										using (var mergeDayTextBrush = new DxBrush(RenderTarget, isDayMerged ? new SharpDX.Color(254, 249, 195, 255) : new SharpDX.Color(224, 242, 254, 255)))
										{
											if (modernUi)
											{
												DrawModernPillButton(dayBtnRect, mergeDayFill, mergeDayBorder, dayBtnText, tagCenterFormat, mergeDayTextBrush, 4.5f, 1.15f);
											}
											else
											{
												RenderTarget.FillRectangle(dayBtnRect, mergeDayFill);
												RenderTarget.DrawRectangle(dayBtnRect, mergeDayBorder, 1.1f);
												DrawText(dayBtnText, dayBtnRect.X, dayBtnRect.Y, dayBtnRect.Width, dayBtnRect.Height, tagCenterFormat, mergeDayTextBrush);
											}
										}

										activeChartButtons.Add(new ChartButton
										{
											Rect = dayBtnRect,
											Action = ChartButtonAction.ToggleMergeDay,
											PrimaryKey = dayRawKeys[0],
											GroupKeys = dayRawKeys
										});
									}

									float textRight = canShowMergeBtn ? (clampedBannerRight - mergeDayBtnW - 8f) : (clampedBannerRight - 6f);
									float textAvailW = textRight - safeTextLeft;
									if (textAvailW >= 42f)
									{
										string dFull = group.Date.ToString("dd MMM yyyy (ddd)", CultureInfo.InvariantCulture);
										string dMed = group.Date.ToString("dd MMM (ddd)", CultureInfo.InvariantCulture);
										string dShort = group.Date.ToString("dd MMM", CultureInfo.InvariantCulture);
										string dateBannerText = textAvailW >= 500f
											? ("Date: " + dFull + fullSummary)
											: (textAvailW >= 275f
												? ("Date: " + dFull + medSummary)
												: (textAvailW >= 186f
													? (dFull + " · " + dispTpos + " TPO")
													: (textAvailW >= 122f
														? dFull
														: (textAvailW >= 78f ? dMed : dShort))));

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
								else if (curMode == MarketProfilePeriodMode.Monthly)
								{
									DayProfile firstInGrp = group.Sessions.Count > 0 ? group.Sessions[0] : null;
									int expTpos = firstInGrp != null ? Math.Max(1, firstInGrp.ExpectedPeriodCount) : 22;
									int prnTpos = firstInGrp != null ? Math.Min(expTpos, Math.Max(firstInGrp.PeriodSlices.Count, firstInGrp.LastPeriod + 1)) : expTpos;
									string mShort = group.Date.ToString("MMM yyyy", CultureInfo.InvariantCulture);
									string mLong = group.Date.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
									float textAvailW = (clampedBannerRight - 6f) - safeTextLeft;
									if (textAvailW >= 42f)
									{
										string bannerSummary = textAvailW >= 320f
											? string.Format(CultureInfo.InvariantCulture, "Month: {0}  [ Monthly Profile — {1}/{2} TPOs ]", mLong, prnTpos, expTpos)
											: (textAvailW >= 150f
												? string.Format(CultureInfo.InvariantCulture, "{0}  [ {1}/{2} TPOs ]", mShort, prnTpos, expTpos)
												: mShort);
										RectangleF textClipRect = new RectangleF(safeTextLeft, dateBannerY + 2f, textAvailW, bottomBannerH - 4f);
										RenderTarget.PushAxisAlignedClip(textClipRect, AntialiasMode.PerPrimitive);
										try
										{
											DrawText(bannerSummary, safeTextLeft, dateBannerY + 2f, textAvailW, bottomBannerH - 4f, dateAxisFormat, whiteBrush);
										}
										finally
										{
											RenderTarget.PopAxisAlignedClip();
										}
									}
								}
								else
								{
									DayProfile firstInGrp = group.Sessions.Count > 0 ? group.Sessions[0] : null;
									int expTpos = firstInGrp != null ? Math.Max(1, firstInGrp.ExpectedPeriodCount) : 15;
									int prnTpos = firstInGrp != null ? Math.Min(expTpos, Math.Max(firstInGrp.PeriodSlices.Count, firstInGrp.LastPeriod + 1)) : expTpos;
									string wkTitle = firstInGrp != null && !string.IsNullOrEmpty(firstInGrp.SessionName)
										? firstInGrp.SessionName
										: ("Wk " + group.Date.ToString("dd MMM", CultureInfo.InvariantCulture));
									float textAvailW = (clampedBannerRight - 6f) - safeTextLeft;
									if (textAvailW >= 42f)
									{
										string bannerSummary = textAvailW >= 320f
											? string.Format(CultureInfo.InvariantCulture, "{0}  [ Weekly Profile — {1}/{2} TPOs ]", wkTitle, prnTpos, expTpos)
											: (textAvailW >= 165f
												? string.Format(CultureInfo.InvariantCulture, "{0}  [ {1}/{2} TPO ]", wkTitle, prnTpos, expTpos)
												: wkTitle);
										RectangleF textClipRect = new RectangleF(safeTextLeft, dateBannerY + 2f, textAvailW, bottomBannerH - 4f);
										RenderTarget.PushAxisAlignedClip(textClipRect, AntialiasMode.PerPrimitive);
										try
										{
											DrawText(bannerSummary, safeTextLeft, dateBannerY + 2f, textAvailW, bottomBannerH - 4f, dateAxisFormat, whiteBrush);
										}
										finally
										{
											RenderTarget.PopAxisAlignedClip();
										}
									}
								}
							}

							for (int sIdx = 0; sIdx < group.Sessions.Count; sIdx++)
							{
								DayProfile day = group.Sessions[sIdx];
								int globalIdx = allSessions.IndexOf(day);
								DayProfile prevSession = globalIdx > 0 ? allSessions[globalIdx - 1] : null;
								bool isLatestSession = (globalIdx == allSessions.Count - 1);
								bool isRunningSession = IsSessionRunning(day, isLatestSession);
								double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;

								float xSlotLeft = day.SlotLeftX;
								float xSlotRight = day.SlotRightX;
								float slotWidth = xSlotRight - xSlotLeft;
								float xIb = day.IbX;
								float xCollapsedRight = day.CollapsedRightX;

								// Skip sessions that are scrolled off the left edge or inside the right-edge virgin label lane
								if (xIb < panelLeft + 4f || xCollapsedRight > panelRight - 178f)
									continue;

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
								bool showProfileVah = ShowVirginVAH && (!isRunningSession || (EnableRunningSessionLevels && ShowRunningSessionVAH));
								bool showProfileVal = ShowVirginVAL && (!isRunningSession || (EnableRunningSessionLevels && ShowRunningSessionVAL));

								// 1. Shaded Value Area Box + Dashed Cyan Borders (snugly wrapped around TPO + Volume Profile with zero trailing gap)
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
									Math.Min(Math.Max(minFontFloor, sampleRowH * 0.80f), Math.Max(minFontFloor, letterWidth * 0.96f)));
								float splitLetterFontPx = Math.Min(Math.Max(10.5f, maxFontCeil), Math.Max(minFontFloor, sampleRowH * 0.78f));

								using (var letterFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, letterFontPx))
								using (var splitLetterFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, splitLetterFontPx))
								{
									letterFormat.TextAlignment = TextAlignment.Center;
									letterFormat.ParagraphAlignment = ParagraphAlignment.Center;
									letterFormat.WordWrapping = WordWrapping.NoWrap;

									splitLetterFormat.TextAlignment = TextAlignment.Center;
									splitLetterFormat.ParagraphAlignment = ParagraphAlignment.Center;
									splitLetterFormat.WordWrapping = WordWrapping.NoWrap;

									// 2. Draw Collapsed TPO Letters, Modern Glowing TPO Tiles, POC Highlight, Single Print Highlights, and Smooth Volume Profile
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

										// Highlight interior Buying Single Prints (Emerald Green) & Selling Single Prints (Crimson Red)
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

										// Modern Golden-Amber POC Row Highlight Capsule
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
														float spTagW = showFullPrice ? Math.Min(availRightW, 96f) : 48f;
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

									// 3. Green Initial Balance (IB) vertical bar on the left spine
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

									// Precompute Top Level Pill (PoorHigh / ABPoorHigh only; SwingHigh box removed) & Fixed Top Session Header Y coordinates
									PriceRow topRow = day.Rows[maxBin];
									bool isAbPoorHigh = topRow.Periods.Contains(0) && topRow.Periods.Contains(1);
									bool isPoorHigh = !isAbPoorHigh && topRow.Periods.Count >= 2;
									bool showTopPoorBox = !isRunningSession && ShowPoorHighLow && ShowPoorHigh && (isAbPoorHigh || isPoorHigh);
									string highLevelName = isAbPoorHigh ? "ABPoorHigh" : "PoorHigh";
									DxBrush highLevelBrush = highExtremeBrush;

									float topTagY = profileHighY - 17f;
									float headerY = FixSessionHeaderAtTop
										? (topSessionBarY + 2f)
										: Math.Max(canvasTopY + 4f, (showTopPoorBox ? topTagY - 36f : profileHighY - 36f));
									float btnRowY = headerY + 17f;

									float minHeaderSlotW = day.IsMerged ? 228f : (ShowSummary ? 208f : 192f);
									float xSplitStart = FixSessionHeaderAtTop
										? Math.Max(vaBoxRightX + 14f, xSlotLeft + minHeaderSlotW)
										: (vaBoxRightX + 14f);

									// 4. Split / Expanded TPO & Volume Profile for ANY Selected Session (docked in Fixed Top Session Bar when FixSessionHeaderAtTop is enabled)
									if (day.IsSplitExpanded && day.PeriodSlices.Count > 0)
									{
										float xSplitEnd = Math.Min(xSlotRight - 6f, panelRight - 178f);
										if (xSplitEnd > xSplitStart + 65f)
										{
											if (FixSessionHeaderAtTop && xSplitStart - 6f >= panelLeft + 4f && xSplitStart - 6f <= panelRight - 180f)
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

											// Split Profile Header Banner aligned inside Fixed Top Session Bar at headerY
											float splitHeaderW = Math.Min(154f, Math.Max(110f, splitAvailW));
											RectangleF splitHdrPill = new RectangleF(xSplitStart, headerY, splitHeaderW, 16f);
											if (modernUi)
											{
												DrawModernPillButton(splitHdrPill, btnActiveBgBrush, cyanBrush, "SPLIT · " + day.SessionName, spTagFormat, cyanBrush, 4.5f, 1.0f);
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
												DrawText("SPLIT PROFILE (" + day.SessionName + ")", xSplitStart + 4f, headerY, splitHeaderW - 8f, 16f, spTagFormat, cyanBrush);
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

													// Draw Period Column Header Letter at btnRowY above the Split Profile
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

									// 5. Opaque Top Level Pill (ABPoorHigh / PoorHigh only — SwingHigh box removed) & Session Header Pill + Interactive Split / Merge Buttons
									if (showTopPoorBox && topTagY >= canvasTopY + 1f && topTagY <= panelBottom - 42f)
									{
										float tagW = 86f;
										float tagX = (xIb + xCollapsedRight) * 0.5f - tagW * 0.5f;
										RectangleF tagPill = new RectangleF(tagX, topTagY, tagW, 15f);
										if (modernUi)
										{
											DrawModernPillButton(tagPill, pillBgBrush, highExtremeBorderBrush, highLevelName, tagCenterFormat, highLevelBrush, 4.0f, 1.0f);
										}
										else
										{
											RenderTarget.FillRectangle(tagPill, pillBgBrush);
											RenderTarget.DrawRectangle(tagPill, highExtremeBorderBrush, 1.0f);
											DrawText(highLevelName, tagX, topTagY, tagW, 15f, tagCenterFormat, highLevelBrush);
										}
									}

									int sessionColorIdx = Math.Max(0, Math.Min(day.SessionId, sessionBrushes.Length - 1));
									int expectedTpos = Math.Max(1, day.ExpectedPeriodCount);
									int printedTpos = Math.Min(expectedTpos, Math.Max(day.PeriodSlices.Count, day.LastPeriod + 1));
									bool isSessionLive = isRunningSession;
									string tpoStatus = isSessionLive
										? string.Format(CultureInfo.InvariantCulture, modernUi ? "● LIVE {0}/{1} TPO" : "LIVE {0}/{1} TPO", printedTpos, expectedTpos)
										: string.Format(CultureInfo.InvariantCulture, "{0}/{1} TPO", printedTpos, expectedTpos);
									string sessionHeader = EffectiveProfileMode == MarketProfilePeriodMode.Daily
										? string.Format(CultureInfo.InvariantCulture, modernUi ? "{0} · {1}  {2}" : "{0} ({1}) - {2}",
											day.SessionName,
											day.SessionDate.ToString("dd MMM", CultureInfo.InvariantCulture),
											tpoStatus)
										: string.Format(CultureInfo.InvariantCulture, modernUi ? "{0} · {1}" : "{0} - {1}",
											day.SessionName,
											tpoStatus);

									float availHeaderW = (day.IsSplitExpanded ? (xSplitStart - 8f) : (xSlotRight - 5f)) - (xIb - 1f);
									float headerW = Math.Max(140f, Math.Min(availHeaderW, 240f));
									RectangleF headerPill = new RectangleF(xIb - 1f, headerY, headerW, 16f);
									if (modernUi)
									{
										var hdrRr = new RoundedRectangle { Rect = headerPill, RadiusX = 4.5f, RadiusY = 4.5f };
										RenderTarget.FillRoundedRectangle(hdrRr, pillBgBrush);
										RenderTarget.DrawRoundedRectangle(hdrRr, cardBorderBrush, 0.9f);
										RenderTarget.FillRectangle(new RectangleF(headerPill.X + 1.5f, headerPill.Y + 2.5f, 3.0f, headerPill.Height - 5.0f), sessionBrushes[sessionColorIdx]);
										DrawText(sessionHeader, xIb + 6f, headerY, headerW - 9f, 16f, headerFormat, sessionBrushes[sessionColorIdx]);
									}
									else
									{
										RenderTarget.FillRectangle(headerPill, pillBgBrush);
										RenderTarget.DrawRectangle(headerPill, cardBorderBrush, 0.8f);
										DrawText(sessionHeader, xIb + 3f, headerY, headerW - 6f, 16f, headerFormat, sessionBrushes[sessionColorIdx]);
									}

									// Action Row right below Session Header: Split button + Merge / Unmerge buttons (controlled by ShowChartButtons)
									if (ShowChartButtons)
									{
										float curBtnX = xIb - 1f;

										// Split / Split ON button
										float splitBtnW = 58f;
										RectangleF splitBtnRect = new RectangleF(curBtnX, btnRowY, splitBtnW, 14f);
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
										curBtnX += splitBtnW + 4f;

										// Merge > button (if there is a next session to merge with)
										if (globalIdx < allSessions.Count - 1)
										{
											float mergeBtnW = 58f;
											RectangleF mergeBtnRect = new RectangleF(curBtnX, btnRowY, mergeBtnW, 14f);
											string mergeLbl = modernUi ? "Merge ›" : "[Merge >]";
											if (modernUi)
											{
												DrawModernPillButton(mergeBtnRect, btnBgBrush, cardBorderBrush, mergeLbl, tagCenterFormat, yellowBrush, 4.0f, 0.95f);
											}
											else
											{
												RenderTarget.FillRectangle(mergeBtnRect, btnBgBrush);
												RenderTarget.DrawRectangle(mergeBtnRect, cardBorderBrush, 0.8f);
												DrawText(mergeLbl, mergeBtnRect.X, mergeBtnRect.Y, mergeBtnRect.Width, mergeBtnRect.Height, tagCenterFormat, yellowBrush);
											}
											activeChartButtons.Add(new ChartButton
											{
												Rect = mergeBtnRect,
												Action = ChartButtonAction.MergeWithNext,
												PrimaryKey = day.ProfileKey,
												GroupKeys = new List<string>(day.SourceProfileKeys)
											});
											curBtnX += mergeBtnW + 4f;
										}

										// Unmerge button (if this profile is currently merged from 2+ sessions)
										if (day.IsMerged && day.SourceProfileKeys.Count > 1)
										{
											float unmergeBtnW = 60f;
											RectangleF unmergeBtnRect = new RectangleF(curBtnX, btnRowY, unmergeBtnW, 14f);
											string unmergeLbl = modernUi ? "Unmerge" : "[Unmerge]";
											if (modernUi)
											{
												DrawModernPillButton(unmergeBtnRect, btnMergeBgBrush, orangeBrush, unmergeLbl, tagCenterFormat, orangeBrush, 4.0f, 0.95f);
											}
											else
											{
												RenderTarget.FillRectangle(unmergeBtnRect, btnMergeBgBrush);
												RenderTarget.DrawRectangle(unmergeBtnRect, orangeBrush, 0.8f);
												DrawText(unmergeLbl, unmergeBtnRect.X, unmergeBtnRect.Y, unmergeBtnRect.Width, unmergeBtnRect.Height, tagCenterFormat, orangeBrush);
											}
											activeChartButtons.Add(new ChartButton
											{
												Rect = unmergeBtnRect,
												Action = ChartButtonAction.UnmergeGroup,
												PrimaryKey = day.ProfileKey,
												GroupKeys = new List<string>(day.SourceProfileKeys)
											});
										}
									}

									// 6. Opaque Bottom Level Pill (ABPoorLow / PoorLow only — SwingLow box removed)
									PriceRow botRow = day.Rows[minBin];
									bool isAbPoorLow = botRow.Periods.Contains(0) && botRow.Periods.Contains(1);
									bool isPoorLow = !isAbPoorLow && botRow.Periods.Count >= 2;
									bool showBotPoorBox = !isRunningSession && ShowPoorHighLow && ShowPoorLow && (isAbPoorLow || isPoorLow);
									string lowLevelName = isAbPoorLow ? "ABPoorLow" : "PoorLow";
									DxBrush lowLevelBrush = lowExtremeBrush;

									float botTagY = profileLowY + 3f;
									if (showBotPoorBox && botTagY >= canvasTopY + 2f && botTagY <= panelBottom - 40f)
									{
										float tagW = 86f;
										float tagX = (xIb + xCollapsedRight) * 0.5f - tagW * 0.5f;
										RectangleF tagPill = new RectangleF(tagX, botTagY, tagW, 15f);
										if (modernUi)
										{
											DrawModernPillButton(tagPill, pillBgBrush, lowExtremeBorderBrush, lowLevelName, tagCenterFormat, lowLevelBrush, 4.0f, 1.0f);
										}
										else
										{
											RenderTarget.FillRectangle(tagPill, pillBgBrush);
											RenderTarget.DrawRectangle(tagPill, lowExtremeBorderBrush, 1.0f);
											DrawText(lowLevelName, tagX, botTagY, tagW, 15f, tagCenterFormat, lowLevelBrush);
										}
									}

									// 7. Bottom 9-Line AK10X Summary Card — Fixed at Bottom Row (above Date bar) or below profile, with Enable/Disable & Min/Max
									if (ShowSummary)
									{
										bool sumMinimized = IsSummaryBoxMinimized;
										float summaryCardH = sumMinimized ? (2f * 12.2f + 8f) : (9f * 12.2f + 8f);
										float fixedBottomTopY = dateBannerY - summaryCardH - 4f;
										float floatingTopY = showBotPoorBox ? (botTagY + 18f) : (profileLowY + 8f);
										float summaryStartTopY = FixSummaryBoxAtBottom ? fixedBottomTopY : floatingTopY;
										float summaryCardW = Math.Min(234f, Math.Max(195f, slotWidth - 12f));

										DrawAK10XSummaryCard(
											day, prevSession, allSessions, globalIdx,
											poc, vah, val, dStep, avgIbRange,
											xIb, summaryStartTopY, summaryCardW,
											cardBgBrush, cardBorderBrush, btnBgBrush,
											whiteBrush, textBrush, orangeBrush, yellowBrush, greenBrush, cyanBrush,
											cardFormat, tagCenterFormat);
									}
								}
							}
						}

						// 8. Deduplicate & De-overlap Right-Edge Virgin Level Tags
						RenderDeoverlappedRightEdgeTags(rightEdgeTags, step, panelRight, Math.Max(panelTop + 40f, canvasTopY + 1f), panelBottom - 8f, pillBgBrush, cardBorderBrush, labelRightFormat);

						// 9. Top-Left 4-Line AK10X Info Panel with Logo & Min/Max button (controlled by ShowTopLeftBox)
						if (ShowTopLeftBox)
						{
							DayProfile latestSession = allSessions[allSessions.Count - 1];
							DayProfile priorSession = allSessions.Count > 1 ? allSessions[allSessions.Count - 2] : null;
							DrawTopLeftInfoBox(
								latestSession, priorSession, step,
								cardBgBrush, cardBorderBrush, btnBgBrush,
								whiteBrush, textBrush, orangeBrush, yellowBrush, greenBrush, redBrush, cyanBrush,
								cardFormat, tagCenterFormat);
						}
						else
						{
							lastTopLeftHudRightX = 0f;
						}

						// 9B. Interactive Top Studio Icon right next to Top-Left Info Box to toggle World-Class Modern Classic Rich UI
						DrawModernUiToggleIcon(panelLeft, panelTop, panelRight);

						// 10. Top-Right Interactive PROFILE Dropdown (Daily / Weekly / Monthly — Default: Daily)
						DrawTopRightProfileDropdown(
							panelRight, panelTop,
							cardBgBrush, cardBorderBrush, btnBgBrush, btnActiveBgBrush,
							whiteBrush, textBrush, orangeBrush, redBrush, cyanBrush,
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
					for (int i = 0; i < sessionBrushes.Length; i++)
						if (sessionBrushes[i] != null)
							sessionBrushes[i].Dispose();
				}
			}

			if (needIntradayGapHeal)
				SyncRecentProviderMinutesAsync(2);
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

			// 1. Deduplicate tags at identical/nearly-identical prices (e.g. overlapping London/NY sessions sharing the same extreme)
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

			// 2. Push down any overlapping tags with a clean 3px gap
			float curBottom = minY;
			for (int i = 0; i < tags.Count; i++)
			{
				RightEdgeTag t = tags[i];
				float y = Math.Max(curBottom, t.IdealY);
				t.DrawY = y;
				curBottom = y + t.Height + 3f;
			}

			// 3. Pull back up if bottom overflowed maxY
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
			float boxWidth = 178f;
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
						.Replace("Asia+London+NY", "Merged")
						.Replace("Asia + London + NY", "Merged")
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
			DayProfile day, DayProfile prevSession, List<DayProfile> allSessions, int idx,
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
			boxTop = Math.Max((float)ChartPanel.Y + 86f, boxTop);

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

			// Top-right Min/Max Toggle Button inside each Summary Box
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
			string line1 = GetOpeningType(day, prevSession, step, out openGreen);

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
			for (int k = maStart; k <= idx && k < allSessions.Count; k++)
			{
				volMa += allSessions[k].TotalVolume;
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

			for (int i = 0; i < lines.Length; i++)
			{
				float maxLineW = (i == 0) ? (cardWidth - minMaxBtnW - 10f) : (cardWidth - 6f);
				DrawText(lines[i], xLeft + 4f, boxTop + 4f + i * lineHeight, maxLineW, lineHeight, format, brushes[i]);
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

			string instName = Instrument == null ? "FOREX" : Instrument.FullName;
			string tpoMode = TpoSize <= 0 ? " (Auto & Real-time)" : " (Manual)";
			int totalConfiguredTpos = 0;
			if (activeSessions != null)
			{
				for (int i = 0; i < activeSessions.Length; i++)
					totalConfiguredTpos += GetSessionPeriodCount(activeSessions[i]);
			}
			string distName;
			if (EffectiveProfileMode == MarketProfilePeriodMode.Weekly)
			{
				distName = "Weekly Profile (15 TPO)";
			}
			else if (EffectiveProfileMode == MarketProfilePeriodMode.Monthly)
			{
				distName = "Monthly Profile (22 TPO)";
			}
			else
			{
				distName = string.Format(CultureInfo.InvariantCulture, "{0} Sessions ({1} TPO)",
					activeSessions != null && activeSessions.Length > 0 ? activeSessions.Length : 3,
					totalConfiguredTpos > 0 ? totalConfiguredTpos : 46);
			}

			Tuple<string, DxBrush>[] line1Segments = new Tuple<string, DxBrush>[]
			{
				Tuple.Create("AK10X Market Profile: ", textBrush),
				Tuple.Create(instName, orangeBrush),
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

			// Measure exact width of Line 1 and Line 2 so buttons (< Past, Next >, Live >>, [-]/[+]) are always placed cleanly to the right with zero overlap
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
					Tuple.Create(", Step: ", textBrush),
					Tuple.Create(FormatPrice(step) + shortMode, greenBrush),
					Tuple.Create(", ", textBrush),
					Tuple.Create(distName, orangeBrush),
					Tuple.Create(", ", textBrush),
					Tuple.Create(TpoIntervalMinutes + "m", orangeBrush)
				};
				line1Width = MeasureSegmentedLine(lineH, format, line1Segments);
			}
			float desiredW = isMin
				? (6f + logoPadLeft + line1Width + 14f + navGroupW + 6f + btnW + 8f)
				: Math.Max(
					6f + logoPadLeft + line1Width + 14f + btnW + 8f,
					6f + logoPadLeft + otfWidth + 16f + navGroupW + 8f);
			float boxW = Math.Min(maxBoxW, Math.Max(isMin ? 560f : 580f, desiredW));
			float boxH = isMin ? (lineH + 12f) : (lineH * 4 + 12f);
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

			// Render Logo on the left side of the Top-Left Info Box
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

				// Subtle vertical divider between logo and info text
				float divX = boxX + logoSize + 13f;
				RenderTarget.DrawLine(new Vector2(divX, boxY + 5f), new Vector2(divX, boxY + boxH - 5f), borderBrush, 0.85f);
			}

			// Top-right Min/Max interactive toggle button
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

			// Interactive < Past, Next >, Live >> navigation buttons
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

			double refClose = prevDay != null && prevDay.Close > 0 ? prevDay.Close : latestDay.Open;
			double chg = latestDay.Close - refClose;
			double chgPct = refClose != 0 ? (chg / refClose) * 100.0 : 0.0;
			string ohlcPart = string.Format(CultureInfo.InvariantCulture, "O: {0}, H: {1}, L: {2}, C: {3} ",
				FormatPrice(latestDay.Open), FormatPrice(latestDay.High), FormatPrice(latestDay.Low), FormatPrice(latestDay.Close));
			string chgPart = string.Format(CultureInfo.InvariantCulture, "({0} : {1:0.00}%)",
				FormatPrice(chg), chgPct);
			string volPart = FormatVolume(latestDay.TotalVolume);

			DrawSegmentedLine(textX, boxY + 4f + lineH * 3, lineH, format,
				Tuple.Create(ohlcPart, whiteBrush),
				Tuple.Create(chgPart, chg >= 0 ? greenBrush : redBrush),
				Tuple.Create(", V: ", whiteBrush),
				Tuple.Create(volPart, orangeBrush));
		}

		private void DrawTopRightProfileDropdown(
			float panelRight, float panelTop,
			DxBrush bgBrush, DxBrush borderBrush, DxBrush btnBgBrush, DxBrush btnActiveBgBrush,
			DxBrush whiteBrush, DxBrush textBrush, DxBrush orangeBrush, DxBrush redBrush, DxBrush cyanBrush,
			DxTextFormat headerFormat, DxTextFormat centerFormat)
		{
			MarketProfilePeriodMode curMode = EffectiveProfileMode;
			string modeText = curMode == MarketProfilePeriodMode.Weekly
				? "Weekly"
				: (curMode == MarketProfilePeriodMode.Monthly ? "Monthly" : "Daily");

			bool modernUi = IsModernRichUi;
			float dropW = 112f;
			float dropH = 22f;
			float labelW = 64f;
			float totalW = labelW + dropW + 14f;
			float startX = panelRight - totalW - 8f;
			float topY = panelTop + 8f;

			RectangleF barRect = new RectangleF(startX, topY - 2f, totalW, dropH + 4f);
			float boxX = startX + labelW + 4f;
			RectangleF dropBoxRect = new RectangleF(boxX, topY, dropW, dropH);

			if (modernUi)
			{
				var barRr = new RoundedRectangle { Rect = barRect, RadiusX = 6.5f, RadiusY = 6.5f };
				RenderTarget.FillRoundedRectangle(barRr, bgBrush);
				RenderTarget.DrawRoundedRectangle(barRr, borderBrush, 1.0f);
				DrawText("PROFILE", startX + 7f, topY, labelW, dropH, headerFormat, cyanBrush);
				DrawModernPillButton(dropBoxRect, isProfileDropdownOpen ? btnActiveBgBrush : btnBgBrush, cyanBrush, modeText + (isProfileDropdownOpen ? "  ▴" : "  ▾"), centerFormat, whiteBrush, 5.0f, 1.15f);
			}
			else
			{
				RenderTarget.FillRectangle(barRect, bgBrush);
				RenderTarget.DrawRectangle(barRect, borderBrush, 0.9f);

				DrawText("PROFILE", startX + 6f, topY, labelW, dropH, headerFormat, orangeBrush);

				RenderTarget.FillRectangle(dropBoxRect, isProfileDropdownOpen ? btnActiveBgBrush : btnBgBrush);
				RenderTarget.DrawRectangle(dropBoxRect, isProfileDropdownOpen ? cyanBrush : redBrush, 1.3f);

				DrawText(modeText, boxX + 8f, topY, dropW - 28f, dropH, headerFormat, whiteBrush);

				float arrowBoxW = 20f;
				RectangleF arrowRect = new RectangleF(boxX + dropW - arrowBoxW - 2f, topY + 2f, arrowBoxW, dropH - 4f);
				RenderTarget.FillRectangle(arrowRect, bgBrush);
				RenderTarget.DrawRectangle(arrowRect, isProfileDropdownOpen ? cyanBrush : redBrush, 0.9f);
				DrawText(isProfileDropdownOpen ? "▲" : "▼", arrowRect.X, arrowRect.Y, arrowRect.Width, arrowRect.Height, centerFormat, isProfileDropdownOpen ? cyanBrush : orangeBrush);
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
				float menuY = topY + dropH + 4f;
				float itemH = 22f;
				float menuH = itemH * 3f + 6f;
				RectangleF menuRect = new RectangleF(boxX, menuY, dropW, menuH);
				if (modernUi)
				{
					var menuRr = new RoundedRectangle { Rect = menuRect, RadiusX = 6.0f, RadiusY = 6.0f };
					RenderTarget.FillRoundedRectangle(menuRr, bgBrush);
					RenderTarget.DrawRoundedRectangle(menuRr, cyanBrush, 1.2f);
				}
				else
				{
					RenderTarget.FillRectangle(menuRect, bgBrush);
					RenderTarget.DrawRectangle(menuRect, cyanBrush, 1.2f);
				}

				MarketProfilePeriodMode[] modes = new MarketProfilePeriodMode[]
				{
					MarketProfilePeriodMode.Daily,
					MarketProfilePeriodMode.Weekly,
					MarketProfilePeriodMode.Monthly
				};
				string[] labels = new string[] { "Daily", "Weekly", "Monthly" };
				ChartButtonAction[] actions = new ChartButtonAction[]
				{
					ChartButtonAction.SelectProfileDaily,
					ChartButtonAction.SelectProfileWeekly,
					ChartButtonAction.SelectProfileMonthly
				};

				for (int m = 0; m < 3; m++)
				{
					bool isSelected = (curMode == modes[m]);
					float iy = menuY + 3f + m * itemH;
					RectangleF itemRect = new RectangleF(boxX + 2f, iy, dropW - 4f, itemH - 2f);
					if (modernUi)
					{
						DrawModernPillButton(itemRect, isSelected ? btnActiveBgBrush : btnBgBrush, isSelected ? cyanBrush : borderBrush, string.Empty, headerFormat, whiteBrush, 4.5f, 1.0f);
					}
					else
					{
						DrawCrispButton(itemRect, isSelected ? btnActiveBgBrush : btnBgBrush, isSelected ? cyanBrush : borderBrush, string.Empty, headerFormat, whiteBrush);
					}

					string prefix = isSelected ? "● " : "   ";
					DrawText(prefix + labels[m], itemRect.X + 6f, itemRect.Y, itemRect.Width - 10f, itemRect.Height, headerFormat, isSelected ? cyanBrush : whiteBrush);

					activeChartButtons.Add(new ChartButton
					{
						Rect = itemRect,
						Action = actions[m],
						PrimaryKey = labels[m]
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
			List<DayProfile> rawSessions,
			List<DayProfile> displaySessions,
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

			int dispLetterIdx = 0;
			string timerStr = "CLOSED";
			bool isLiveTpo = false;
			bool isUrgent = false;

			DayProfile latestRaw = (rawSessions != null && rawSessions.Count > 0) ? rawSessions[rawSessions.Count - 1] : null;
			DayProfile latestDisp = (displaySessions != null && displaySessions.Count > 0) ? displaySessions[displaySessions.Count - 1] : latestRaw;

			if (latestDisp != null)
				dispLetterIdx = Math.Max(0, Math.Min(LetterSequence.Length - 1, latestDisp.LastPeriod));

			double baseIntervalMin = Math.Max(1.0, (double)TpoIntervalMinutes);

			if (EffectiveProfileMode == MarketProfilePeriodMode.Daily)
			{
				DateTime effOpen = DateTime.MinValue;
				DateTime sessClose = DateTime.MinValue;
				double bracketMin = baseIntervalMin;
				int expPeriods = 15;
				int mergedOffset = 0;

				if (latestRaw != null && nowClock >= latestRaw.EffectiveOpenTime && nowClock < latestRaw.SessionCloseTime)
				{
					effOpen = latestRaw.EffectiveOpenTime;
					sessClose = latestRaw.SessionCloseTime;
					expPeriods = Math.Max(1, latestRaw.ExpectedPeriodCount);
					DateTime nominalStart = latestRaw.SessionCloseTime.AddMinutes(-expPeriods * baseIntervalMin);
					bracketMin = (latestRaw.SessionId == 0 && latestRaw.EffectiveOpenTime > nominalStart.AddMinutes(1.0))
						? (Math.Max(baseIntervalMin, (sessClose - effOpen).TotalMinutes) / expPeriods)
						: baseIntervalMin;
					if (latestDisp != null && latestDisp.IsMerged)
						mergedOffset = Math.Max(0, latestDisp.LastPeriod - latestRaw.LastPeriod);
				}
				else if (activeSessions != null)
				{
					for (int si = 0; si < activeSessions.Length; si++)
					{
						ProfileSession ps = activeSessions[si];
						int ep = GetSessionPeriodCount(ps);
						DateTime sDate, sStart, sClose;
						int pIdx;
						if (TryGetSession(nowClock, ps.OpenSeconds, ps.CloseSeconds, ep, out sDate, out sStart, out sClose, out pIdx))
						{
							effOpen = sStart;
							sessClose = sClose;
							expPeriods = Math.Max(1, ep);
							bracketMin = baseIntervalMin;
							break;
						}
					}
				}

				if (effOpen > DateTime.MinValue && nowClock >= effOpen && nowClock < sessClose)
				{
					double elapsedSec = Math.Max(0.0, (nowClock - effOpen).TotalSeconds);
					double bracketSec = Math.Max(60.0, bracketMin * 60.0);
					int curRawIdx = Math.Max(0, Math.Min(expPeriods - 1, (int)Math.Floor(elapsedSec / bracketSec)));
					DateTime curPeriodEnd = effOpen.AddSeconds((curRawIdx + 1) * bracketSec);
					if (curPeriodEnd > sessClose)
						curPeriodEnd = sessClose;
					int remSec = Math.Max(0, (int)Math.Ceiling((curPeriodEnd - nowClock).TotalSeconds));

					dispLetterIdx = Math.Max(0, Math.Min(LetterSequence.Length - 1, curRawIdx + mergedOffset));
					isLiveTpo = streamConnected || (latestRaw != null && (nowClock - latestRaw.LastSampleTime).TotalMinutes < bracketMin + 5.0);
					isUrgent = isLiveTpo && remSec > 0 && remSec <= Math.Min(60, Math.Max(10, (int)(bracketSec * 0.08)));

					int mm = remSec / 60;
					int ss = remSec % 60;
					timerStr = remSec >= 3600
						? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", remSec / 3600, (remSec % 3600) / 60, ss)
						: string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", mm, ss);
				}
			}
			else if (latestDisp != null)
			{
				int expT = Math.Max(1, latestDisp.ExpectedPeriodCount);
				int prnT = Math.Min(expT, Math.Max(latestDisp.PeriodSlices.Count, latestDisp.LastPeriod + 1));
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

					// 5-row Bell-Curve Market Profile glyph inside button
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

					// 4-point Sparkle Star ✦ in top-right corner of the icon
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
				// Explicitly reinforce the top and bottom horizontal edges so they never fade under sub-pixel sampling
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
		[NinjaScriptProperty]
		[Range(0, 1000)]
		[Display(Name = "TPO size (0 = auto)", GroupName = "Market Profile", Order = 0)]
		public double TpoSize { get; set; }

		[Display(Name = "Enable Screen-Resolution Auto TPO Size", Description = "Default: Disabled (Recommended — Fixed Calculation Engine + Adaptive Rendering Engine: 100% consistent Auto TPO Size, VPoC, VAH/VAL, Single Prints, Poor & Swing High/Low across all monitors, with adaptive TPO font size, letter spacing, chart zoom, and vertical scrolling). Enable only if you want legacy screen-height row merging.", GroupName = "Market Profile", Order = 1)]
		public bool AutoScaleTpoByScreenResolution { get; set; }

		[NinjaScriptProperty]
		[Range(1, 240)]
		[Display(Name = "TPO interval (minutes)", GroupName = "Market Profile", Order = 2)]
		public int TpoIntervalMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(50, 90)]
		[Display(Name = "Value area (%)", GroupName = "Market Profile", Order = 3)]
		public int ValueAreaPercent { get; set; }

		[NinjaScriptProperty]
		[Range(1, 240)]
		[Display(Name = "Initial balance (minutes)", GroupName = "Market Profile", Order = 4)]
		public int InitialBalanceMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(1, 365)]
		[Display(Name = "Days to display", GroupName = "Market Profile", Order = 5)]
		public int DaysToDisplay { get; set; }

		[Display(Name = "Profile Mode (Daily / Weekly / Monthly)", Description = "Select Daily (default), Weekly, or Monthly Market Profile distribution (can also switch live via the top-right PROFILE dropdown on chart)", GroupName = "Market Profile", Order = 6)]
		public MarketProfilePeriodMode ProfileMode { get; set; }

		// 3 Forex Sessions (IST) with From & To Time configuration:
		// 1. Asia:     03:30 AM - 01:30 PM (10 Hours = 600 Min = 20 TPOs)
		// 2. London:   01:30 PM - 07:00 PM (5.5 Hours = 330 Min = 11 TPOs)
		// 3. New York: 07:00 PM - 02:30 AM Next Day (7.5 Hours = 450 Min = 15 TPOs)
		[NinjaScriptProperty]
		[Display(Name = "Show Asia (20 TPO)", GroupName = "Forex 3 Sessions (IST)", Order = 0)]
		public bool ShowAsia { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Asia From Time (IST)", Description = "Default: 03:30 AM (accepts 03:30 AM, 03:30, or 033000)", GroupName = "Forex 3 Sessions (IST)", Order = 1)]
		public string AsiaFromTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Asia To Time (IST)", Description = "Default: 01:30 PM (accepts 01:30 PM, 13:30, or 133000)", GroupName = "Forex 3 Sessions (IST)", Order = 2)]
		public string AsiaToTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show London (11 TPO)", GroupName = "Forex 3 Sessions (IST)", Order = 3)]
		public bool ShowLondon { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "London From Time (IST)", Description = "Default: 01:30 PM (accepts 01:30 PM, 13:30, or 133000)", GroupName = "Forex 3 Sessions (IST)", Order = 4)]
		public string LondonFromTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "London To Time (IST)", Description = "Default: 07:00 PM (accepts 07:00 PM, 19:00, or 190000)", GroupName = "Forex 3 Sessions (IST)", Order = 5)]
		public string LondonToTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show New York (15 TPO)", GroupName = "Forex 3 Sessions (IST)", Order = 6)]
		public bool ShowNewYork { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "New York From Time (IST)", Description = "Default: 07:00 PM (accepts 07:00 PM, 19:00, or 190000)", GroupName = "Forex 3 Sessions (IST)", Order = 7)]
		public string NewYorkFromTime { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "New York To Time (IST)", Description = "Default: 02:30 AM (Next Day) (accepts 02:30 AM, 02:30, or 023000)", GroupName = "Forex 3 Sessions (IST)", Order = 8)]
		public string NewYorkToTime { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "India open (HHmmss IST)", GroupName = "Indian market (IST)", Order = 0)]
		public int MarketOpen { get; set; }

		[NinjaScriptProperty]
		[Range(0, 235959)]
		[Display(Name = "India close (HHmmss IST)", GroupName = "Indian market (IST)", Order = 1)]
		public int MarketClose { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "India session label", GroupName = "Indian market (IST)", Order = 2)]
		public string SessionLabel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show India cash session", GroupName = "Indian market (IST)", Order = 3)]
		public bool ShowIndianSession { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Split Latest / Live Session", Description = "Automatically expand Split Profile on the latest/live session", GroupName = "Split & Merge Profiles", Order = 0)]
		public bool SplitLatestSession { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Split Asia Session Profiles", Description = "Expand Split Profile (letters by period) for Asia sessions", GroupName = "Split & Merge Profiles", Order = 1)]
		public bool SplitAsiaSessions { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Split London Session Profiles", Description = "Expand Split Profile (letters by period) for London sessions", GroupName = "Split & Merge Profiles", Order = 2)]
		public bool SplitLondonSessions { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Split New York Session Profiles", Description = "Expand Split Profile (letters by period) for New York sessions", GroupName = "Split & Merge Profiles", Order = 3)]
		public bool SplitNewYorkSessions { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Merge Asia + London", Description = "Merge Asia and London sessions of the same day into one composite profile", GroupName = "Split & Merge Profiles", Order = 4)]
		public bool MergeAsiaAndLondon { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Merge London + New York", Description = "Merge London and New York sessions of the same day into one composite profile", GroupName = "Split & Merge Profiles", Order = 5)]
		public bool MergeLondonAndNewYork { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Merge Full Day (Asia + London + NY)", Description = "Merge all 3 sessions of each date into a single 46-TPO composite daily profile", GroupName = "Split & Merge Profiles", Order = 6)]
		public bool MergeAllDaySessions { get; set; }

		// Virgin Lines & Levels (Complete Hide on All Past & Current Data when Unchecked)
		[Display(Name = "Virginity Break Rule", Description = "Select Virginity Break Rule for all Virgin Lines & Single Prints: CrossBased (Default — breaks immediately when price crosses/touches the line) or CloseBased (breaks only when a closed TPO closes above/below the line).", GroupName = "Virgin Lines & Levels", Order = 0)]
		public VirginityBreakRuleMode VirginityBreakRule { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin PoorHigh / ABPoorHigh", Description = "Show and extend PoorHigh and ABPoorHigh on closed sessions until broken. When unchecked, hides on all past and current sessions.", GroupName = "Virgin Lines & Levels", Order = 1)]
		public bool ShowPoorHigh { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin PoorLow / ABPoorLow", Description = "Show and extend PoorLow and ABPoorLow on closed sessions until broken. When unchecked, hides on all past and current sessions.", GroupName = "Virgin Lines & Levels", Order = 2)]
		public bool ShowPoorLow { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin DayHigh", Description = "Show and extend Virgin DayHigh on closed days/sessions only (never on running day or session) until broken.", GroupName = "Virgin Lines & Levels", Order = 3)]
		public bool ShowSwingHigh { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin DayLow", Description = "Show and extend Virgin DayLow on closed days/sessions only (never on running day or session) until broken.", GroupName = "Virgin Lines & Levels", Order = 4)]
		public bool ShowSwingLow { get; set; }

		[Display(Name = "Enable Virgin WeeklyHigh", Description = "Show and extend Virgin Weekly High (closed Monday to Friday week only) until broken.", GroupName = "Virgin Lines & Levels", Order = 5)]
		public bool ShowWeeklyHigh { get; set; }

		[Display(Name = "Enable Virgin WeeklyLow", Description = "Show and extend Virgin Weekly Low (closed Monday to Friday week only) until broken.", GroupName = "Virgin Lines & Levels", Order = 6)]
		public bool ShowWeeklyLow { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin ValueAreaHigh (with Session)", Description = "Show ValueAreaHigh and extend until broken. When unchecked, hides ValueAreaHigh on all past and current sessions.", GroupName = "Virgin Lines & Levels", Order = 7)]
		public bool ShowVirginVAH { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin ValueAreaLow (with Session)", Description = "Show ValueAreaLow and extend until broken. When unchecked, hides ValueAreaLow on all past and current sessions.", GroupName = "Virgin Lines & Levels", Order = 8)]
		public bool ShowVirginVAL { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin VPoC", Description = "Show Volume PoC (VPoC) and extend until broken. When unchecked, hides VPoC on all past and current sessions.", GroupName = "Virgin Lines & Levels", Order = 9)]
		public bool ShowVirginVPoC { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin VPPoC (TPO PoC)", Description = "Show TPO PoC (VPPoC) and extend until broken. When unchecked, hides VPPoC on all past and current sessions.", GroupName = "Virgin Lines & Levels", Order = 10)]
		public bool ShowVirginVPPoC { get; set; }

		[Display(Name = "Enable Virgin MidLine", Description = "Show Session MidLine on completed sessions and extend Virgin MidLine until broken. When unchecked, hides MidLine on all past and current sessions.", GroupName = "Virgin Lines & Levels", Order = 11)]
		public bool ShowVirginMidLine { get; set; }

		// Running Session Enable / Disable Controls (VAH, VAL, POC, PPoC)
		[Display(Name = "Enable Running Session Levels (Master)", Description = "Master switch to enable or disable ValueAreaHigh, ValueAreaLow, POC (VPoC), and PPoC (VPPoC) on the currently running session", GroupName = "Running Session Levels", Order = 0)]
		public bool EnableRunningSessionLevels { get; set; }

		[Display(Name = "Enable Running Session ValueAreaHigh", Description = "Show ValueAreaHigh line and right-edge label on the currently running session", GroupName = "Running Session Levels", Order = 1)]
		public bool ShowRunningSessionVAH { get; set; }

		[Display(Name = "Enable Running Session ValueAreaLow", Description = "Show ValueAreaLow line and right-edge label on the currently running session", GroupName = "Running Session Levels", Order = 2)]
		public bool ShowRunningSessionVAL { get; set; }

		[Display(Name = "Enable Running Session POC (VPoC)", Description = "Show Volume PoC (POC) line and right-edge label on the currently running session", GroupName = "Running Session Levels", Order = 3)]
		public bool ShowRunningSessionPOC { get; set; }

		[Display(Name = "Enable Running Session PPoC (VPPoC)", Description = "Show TPO PoC (PPoC) line and right-edge label on the currently running session", GroupName = "Running Session Levels", Order = 4)]
		public bool ShowRunningSessionPPOC { get; set; }

		[Display(Name = "Enable Modern Classic Rich UI", Description = "Enable world-class smooth glassmorphic Market Profile UI (can also toggle live via the top studio icon next to the Info Box)", GroupName = "Display", Order = 0)]
		public bool EnableModernRichUi { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Bottom Summary Boxes", Description = "Enable or disable the 9-line session summary boxes at the bottom of the chart", GroupName = "Display", Order = 1)]
		public bool ShowSummary { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fix Summary Boxes at Bottom", Description = "When enabled, docks all session summary boxes in a fixed horizontal row at the bottom above the Date bar", GroupName = "Display", Order = 1)]
		public bool FixSummaryBoxAtBottom { get; set; }

		[Display(Name = "Fix Session Header at Top", Description = "When enabled (Default), docks all Session Details, [Split], [Merge >], and Split Profile headers in one clean Fixed Top Session Bar so they never cover price values or Virgin lines", GroupName = "Display", Order = 2)]
		public bool FixSessionHeaderAtTop { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Minimize Summary Boxes", Description = "Minimize all bottom session summary boxes to a compact 2-line bar (can also toggle via [-] / [+] button on chart)", GroupName = "Display", Order = 2)]
		public bool MinimizeSummaryBoxes { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Top-Left Info Box", Description = "Enable or disable the top-left Market Profile info box", GroupName = "Display", Order = 3)]
		public bool ShowTopLeftBox { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Logo in Top-Left Box", Description = "Display the circular trader logo on the left side of the top-left info box", GroupName = "Display", Order = 4)]
		public bool ShowLogoInTopBox { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Minimize Top-Left Info Box", Description = "Minimize the top-left info box to a single-line header (can also toggle via [- Min] / [+ Max] button on chart)", GroupName = "Display", Order = 5)]
		public bool MinimizeTopLeftBox { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable On-Chart [Split]/[Merge] Buttons", Description = "Enable or disable the interactive [Split], [Merge >], and [Merge Day] buttons on the chart", GroupName = "Display", Order = 6)]
		public bool ShowChartButtons { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show split volume profile", GroupName = "Display", Order = 7)]
		public bool ShowVolumeProfile { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show initial balance", GroupName = "Display", Order = 8)]
		public bool ShowInitialBalance { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show single prints", GroupName = "Display", Order = 9)]
		public bool ShowSinglePrints { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show poor high/low (Master)", GroupName = "Display", Order = 10)]
		public bool ShowPoorHighLow { get; set; }

		[NinjaScriptProperty]
		[Range(100, 1000)]
		[Display(Name = "Profile width (pixels)", GroupName = "Display", Order = 11)]
		public int ProfileWidth { get; set; }

		[NinjaScriptProperty]
		[Range(8, 20)]
		[Display(Name = "Font size", GroupName = "Display", Order = 12)]
		public int FontSize { get; set; }

		// Legacy non-browsable shims so reloading an existing chart template never fails XML deserialization
		[Browsable(false)]
		[XmlIgnore]
		public bool ShowSydney { get; set; }
		[Browsable(false)]
		[XmlIgnore]
		public int SydneyOpen { get; set; }
		[Browsable(false)]
		[XmlIgnore]
		public int SydneyClose { get; set; }
		[Browsable(false)]
		[XmlIgnore]
		public bool ShowTokyo { get; set; }
		[Browsable(false)]
		[XmlIgnore]
		public int TokyoOpen { get; set; }
		[Browsable(false)]
		[XmlIgnore]
		public int TokyoClose { get; set; }
		[Browsable(false)]
		[XmlIgnore]
		public int LondonOpen { get; set; }
		[Browsable(false)]
		[XmlIgnore]
		public int LondonClose { get; set; }
		[Browsable(false)]
		[XmlIgnore]
		public int NewYorkOpen { get; set; }
		[Browsable(false)]
		[XmlIgnore]
		public int NewYorkClose { get; set; }

		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AK10XGlobalMarketProfile[] cacheAK10XGlobalMarketProfile;
		public AK10XGlobalMarketProfile AK10XGlobalMarketProfile(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool showAsia, string asiaFromTime, string asiaToTime, bool showLondon, string londonFromTime, string londonToTime, bool showNewYork, string newYorkFromTime, string newYorkToTime, int marketOpen, int marketClose, string sessionLabel, bool showIndianSession, bool splitLatestSession, bool splitAsiaSessions, bool splitLondonSessions, bool splitNewYorkSessions, bool mergeAsiaAndLondon, bool mergeLondonAndNewYork, bool mergeAllDaySessions, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize)
		{
			return AK10XGlobalMarketProfile(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, showAsia, asiaFromTime, asiaToTime, showLondon, londonFromTime, londonToTime, showNewYork, newYorkFromTime, newYorkToTime, marketOpen, marketClose, sessionLabel, showIndianSession, splitLatestSession, splitAsiaSessions, splitLondonSessions, splitNewYorkSessions, mergeAsiaAndLondon, mergeLondonAndNewYork, mergeAllDaySessions, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize);
		}

		public AK10XGlobalMarketProfile AK10XGlobalMarketProfile(ISeries<double> input, double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool showAsia, string asiaFromTime, string asiaToTime, bool showLondon, string londonFromTime, string londonToTime, bool showNewYork, string newYorkFromTime, string newYorkToTime, int marketOpen, int marketClose, string sessionLabel, bool showIndianSession, bool splitLatestSession, bool splitAsiaSessions, bool splitLondonSessions, bool splitNewYorkSessions, bool mergeAsiaAndLondon, bool mergeLondonAndNewYork, bool mergeAllDaySessions, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize)
		{
			if (cacheAK10XGlobalMarketProfile != null)
				for (int idx = 0; idx < cacheAK10XGlobalMarketProfile.Length; idx++)
					if (cacheAK10XGlobalMarketProfile[idx] != null && cacheAK10XGlobalMarketProfile[idx].TpoSize == tpoSize && cacheAK10XGlobalMarketProfile[idx].TpoIntervalMinutes == tpoIntervalMinutes && cacheAK10XGlobalMarketProfile[idx].ValueAreaPercent == valueAreaPercent && cacheAK10XGlobalMarketProfile[idx].InitialBalanceMinutes == initialBalanceMinutes && cacheAK10XGlobalMarketProfile[idx].DaysToDisplay == daysToDisplay && cacheAK10XGlobalMarketProfile[idx].ShowAsia == showAsia && cacheAK10XGlobalMarketProfile[idx].AsiaFromTime == asiaFromTime && cacheAK10XGlobalMarketProfile[idx].AsiaToTime == asiaToTime && cacheAK10XGlobalMarketProfile[idx].ShowLondon == showLondon && cacheAK10XGlobalMarketProfile[idx].LondonFromTime == londonFromTime && cacheAK10XGlobalMarketProfile[idx].LondonToTime == londonToTime && cacheAK10XGlobalMarketProfile[idx].ShowNewYork == showNewYork && cacheAK10XGlobalMarketProfile[idx].NewYorkFromTime == newYorkFromTime && cacheAK10XGlobalMarketProfile[idx].NewYorkToTime == newYorkToTime && cacheAK10XGlobalMarketProfile[idx].MarketOpen == marketOpen && cacheAK10XGlobalMarketProfile[idx].MarketClose == marketClose && cacheAK10XGlobalMarketProfile[idx].SessionLabel == sessionLabel && cacheAK10XGlobalMarketProfile[idx].ShowIndianSession == showIndianSession && cacheAK10XGlobalMarketProfile[idx].SplitLatestSession == splitLatestSession && cacheAK10XGlobalMarketProfile[idx].SplitAsiaSessions == splitAsiaSessions && cacheAK10XGlobalMarketProfile[idx].SplitLondonSessions == splitLondonSessions && cacheAK10XGlobalMarketProfile[idx].SplitNewYorkSessions == splitNewYorkSessions && cacheAK10XGlobalMarketProfile[idx].MergeAsiaAndLondon == mergeAsiaAndLondon && cacheAK10XGlobalMarketProfile[idx].MergeLondonAndNewYork == mergeLondonAndNewYork && cacheAK10XGlobalMarketProfile[idx].MergeAllDaySessions == mergeAllDaySessions && cacheAK10XGlobalMarketProfile[idx].ShowPoorHigh == showPoorHigh && cacheAK10XGlobalMarketProfile[idx].ShowPoorLow == showPoorLow && cacheAK10XGlobalMarketProfile[idx].ShowSwingHigh == showSwingHigh && cacheAK10XGlobalMarketProfile[idx].ShowSwingLow == showSwingLow && cacheAK10XGlobalMarketProfile[idx].ShowVirginVAH == showVirginVAH && cacheAK10XGlobalMarketProfile[idx].ShowVirginVAL == showVirginVAL && cacheAK10XGlobalMarketProfile[idx].ShowVirginVPoC == showVirginVPoC && cacheAK10XGlobalMarketProfile[idx].ShowVirginVPPoC == showVirginVPPoC && cacheAK10XGlobalMarketProfile[idx].ShowSummary == showSummary && cacheAK10XGlobalMarketProfile[idx].FixSummaryBoxAtBottom == fixSummaryBoxAtBottom && cacheAK10XGlobalMarketProfile[idx].MinimizeSummaryBoxes == minimizeSummaryBoxes && cacheAK10XGlobalMarketProfile[idx].ShowTopLeftBox == showTopLeftBox && cacheAK10XGlobalMarketProfile[idx].ShowLogoInTopBox == showLogoInTopBox && cacheAK10XGlobalMarketProfile[idx].MinimizeTopLeftBox == minimizeTopLeftBox && cacheAK10XGlobalMarketProfile[idx].ShowChartButtons == showChartButtons && cacheAK10XGlobalMarketProfile[idx].ShowVolumeProfile == showVolumeProfile && cacheAK10XGlobalMarketProfile[idx].ShowInitialBalance == showInitialBalance && cacheAK10XGlobalMarketProfile[idx].ShowSinglePrints == showSinglePrints && cacheAK10XGlobalMarketProfile[idx].ShowPoorHighLow == showPoorHighLow && cacheAK10XGlobalMarketProfile[idx].ProfileWidth == profileWidth && cacheAK10XGlobalMarketProfile[idx].FontSize == fontSize && cacheAK10XGlobalMarketProfile[idx].EqualsInput(input))
						return cacheAK10XGlobalMarketProfile[idx];
			return CacheIndicator<AK10XGlobalMarketProfile>(new AK10XGlobalMarketProfile(){ TpoSize = tpoSize, TpoIntervalMinutes = tpoIntervalMinutes, ValueAreaPercent = valueAreaPercent, InitialBalanceMinutes = initialBalanceMinutes, DaysToDisplay = daysToDisplay, ShowAsia = showAsia, AsiaFromTime = asiaFromTime, AsiaToTime = asiaToTime, ShowLondon = showLondon, LondonFromTime = londonFromTime, LondonToTime = londonToTime, ShowNewYork = showNewYork, NewYorkFromTime = newYorkFromTime, NewYorkToTime = newYorkToTime, MarketOpen = marketOpen, MarketClose = marketClose, SessionLabel = sessionLabel, ShowIndianSession = showIndianSession, SplitLatestSession = splitLatestSession, SplitAsiaSessions = splitAsiaSessions, SplitLondonSessions = splitLondonSessions, SplitNewYorkSessions = splitNewYorkSessions, MergeAsiaAndLondon = mergeAsiaAndLondon, MergeLondonAndNewYork = mergeLondonAndNewYork, MergeAllDaySessions = mergeAllDaySessions, ShowPoorHigh = showPoorHigh, ShowPoorLow = showPoorLow, ShowSwingHigh = showSwingHigh, ShowSwingLow = showSwingLow, ShowVirginVAH = showVirginVAH, ShowVirginVAL = showVirginVAL, ShowVirginVPoC = showVirginVPoC, ShowVirginVPPoC = showVirginVPPoC, ShowSummary = showSummary, FixSummaryBoxAtBottom = fixSummaryBoxAtBottom, MinimizeSummaryBoxes = minimizeSummaryBoxes, ShowTopLeftBox = showTopLeftBox, ShowLogoInTopBox = showLogoInTopBox, MinimizeTopLeftBox = minimizeTopLeftBox, ShowChartButtons = showChartButtons, ShowVolumeProfile = showVolumeProfile, ShowInitialBalance = showInitialBalance, ShowSinglePrints = showSinglePrints, ShowPoorHighLow = showPoorHighLow, ProfileWidth = profileWidth, FontSize = fontSize }, input, ref cacheAK10XGlobalMarketProfile);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AK10XGlobalMarketProfile AK10XGlobalMarketProfile(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool showAsia, string asiaFromTime, string asiaToTime, bool showLondon, string londonFromTime, string londonToTime, bool showNewYork, string newYorkFromTime, string newYorkToTime, int marketOpen, int marketClose, string sessionLabel, bool showIndianSession, bool splitLatestSession, bool splitAsiaSessions, bool splitLondonSessions, bool splitNewYorkSessions, bool mergeAsiaAndLondon, bool mergeLondonAndNewYork, bool mergeAllDaySessions, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize)
		{
			return indicator.AK10XGlobalMarketProfile(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, showAsia, asiaFromTime, asiaToTime, showLondon, londonFromTime, londonToTime, showNewYork, newYorkFromTime, newYorkToTime, marketOpen, marketClose, sessionLabel, showIndianSession, splitLatestSession, splitAsiaSessions, splitLondonSessions, splitNewYorkSessions, mergeAsiaAndLondon, mergeLondonAndNewYork, mergeAllDaySessions, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize);
		}

		public Indicators.AK10XGlobalMarketProfile AK10XGlobalMarketProfile(ISeries<double> input , double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool showAsia, string asiaFromTime, string asiaToTime, bool showLondon, string londonFromTime, string londonToTime, bool showNewYork, string newYorkFromTime, string newYorkToTime, int marketOpen, int marketClose, string sessionLabel, bool showIndianSession, bool splitLatestSession, bool splitAsiaSessions, bool splitLondonSessions, bool splitNewYorkSessions, bool mergeAsiaAndLondon, bool mergeLondonAndNewYork, bool mergeAllDaySessions, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize)
		{
			return indicator.AK10XGlobalMarketProfile(input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, showAsia, asiaFromTime, asiaToTime, showLondon, londonFromTime, londonToTime, showNewYork, newYorkFromTime, newYorkToTime, marketOpen, marketClose, sessionLabel, showIndianSession, splitLatestSession, splitAsiaSessions, splitLondonSessions, splitNewYorkSessions, mergeAsiaAndLondon, mergeLondonAndNewYork, mergeAllDaySessions, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AK10XGlobalMarketProfile AK10XGlobalMarketProfile(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool showAsia, string asiaFromTime, string asiaToTime, bool showLondon, string londonFromTime, string londonToTime, bool showNewYork, string newYorkFromTime, string newYorkToTime, int marketOpen, int marketClose, string sessionLabel, bool showIndianSession, bool splitLatestSession, bool splitAsiaSessions, bool splitLondonSessions, bool splitNewYorkSessions, bool mergeAsiaAndLondon, bool mergeLondonAndNewYork, bool mergeAllDaySessions, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize)
		{
			return indicator.AK10XGlobalMarketProfile(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, showAsia, asiaFromTime, asiaToTime, showLondon, londonFromTime, londonToTime, showNewYork, newYorkFromTime, newYorkToTime, marketOpen, marketClose, sessionLabel, showIndianSession, splitLatestSession, splitAsiaSessions, splitLondonSessions, splitNewYorkSessions, mergeAsiaAndLondon, mergeLondonAndNewYork, mergeAllDaySessions, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize);
		}

		public Indicators.AK10XGlobalMarketProfile AK10XGlobalMarketProfile(ISeries<double> input , double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool showAsia, string asiaFromTime, string asiaToTime, bool showLondon, string londonFromTime, string londonToTime, bool showNewYork, string newYorkFromTime, string newYorkToTime, int marketOpen, int marketClose, string sessionLabel, bool showIndianSession, bool splitLatestSession, bool splitAsiaSessions, bool splitLondonSessions, bool splitNewYorkSessions, bool mergeAsiaAndLondon, bool mergeLondonAndNewYork, bool mergeAllDaySessions, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize)
		{
			return indicator.AK10XGlobalMarketProfile(input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, showAsia, asiaFromTime, asiaToTime, showLondon, londonFromTime, londonToTime, showNewYork, newYorkFromTime, newYorkToTime, marketOpen, marketClose, sessionLabel, showIndianSession, splitLatestSession, splitAsiaSessions, splitLondonSessions, splitNewYorkSessions, mergeAsiaAndLondon, mergeLondonAndNewYork, mergeAllDaySessions, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize);
		}
	}
}

#endregion
