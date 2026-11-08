// 50Charts-style Three-Session (Asia: 20 TPOs, London: 11 TPOs, New York: 15 TPOs = 46 TPOs) Market Profile for NinjaTrader 8.
// Includes Buying & Selling Single Prints, Virgin Level Extensions (PoorHigh/Low, ABPoorHigh/Low, SwingHigh/Low, VPoC, VPPoC),
// configurable From/To session times (IST), and a 2-pass non-overlapping renderer.
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

	public class MarketProfile50Charts : Indicator
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
			SelectProfileMonthly
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
		private bool? runtimeMinimizeTopLeftOverride;
		private bool? runtimeMinimizeSummaryOverride;
		private MarketProfilePeriodMode? runtimeProfileModeOverride;
		private bool isProfileDropdownOpen;
		private bool isBackfillInFlight;
		private int highestBackfillDaysRequested;
		private float maxScrollCached;
		private bool hasManualPixelScroll;
		private int lastTrackedFromIdx = -1;
		private int lastTrackedToIdx = -1;
		private float currentScrollPx = float.NaN;
		private SharpDX.Direct2D1.Bitmap cachedLogoBitmap;
		private SharpDX.Direct2D1.RenderTarget cachedLogoRenderTarget;

		private const string DefaultLogoBase64 = "iVBORw0KGgoAAAANSUhEUgAAAFAAAABQCAYAAACOEfKtAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAA0DSURBVHhe5ZwJcBVVFobDvpOIJBgYZYuTAIZEYXAUl7GGzRFHZxAQUAERilJkkx0XKAZBkRlZJIAIikrQEQQMhUJkSwTRQVkFBogSMOwMm0lIkDP/f3O76Zd0yHt53U3Av+orsd/tc849ea/79rm3b8hVVhT4KxgJ3gVfgm3gJ3AUnNLw3zzGz9hmHuA5PJc2fjMKBx3B22AnyAUSJLRBW7RJ2/RxXakc4DflI8Bvk10SfKhYsaKEhYVJeHi4gv+uWKGCbVsbTgL6ehjQ9zWrmmAo2A3sOiqRkZHSqlUreeGFF2TOnDmSvGqVbNu2TdLT0+XEiRNy5swZBf/NY/xsFdrMmfO2DB48WJ1700032drW0PcQcCO4ZhQKXgRHQIFO3XnnnTJ27FhJTU2Vs2fPSrCijZSUFBkzZoy0aNGigD/NYTAaVAclWj0AL/Y+HahXr56MHDlStmzZorvtnuhj5IgRUrduXZ8YND+C7qDEKQZ8DnwCjouLk3feeUfOnz+vu+ed6JO+mzZt6hOTZgWIBiVCfcA5YAbYoH4DFfzFixd1d66eGAOvr/Xr17cmkJwFvcFVU0XA8ZgZVOnSpWX48OHqwl/SxJiGDRsmpUqVsiaRzAUVgKeqAzYCM5D4+HjZuGGDDrfkagNi5KXFGjvYAGoDT9QY8GJsBtCnTx/JysrSIZZ8ZWZmSu9nnrEmkOwHjYCrigd8rDIdT58+XYd17Wnq1KnWBBIOvZoCV8S/zjGgnFWqVEmWLVumQ7l2tWTJEvXkY/QL8AvCUYWj4vXhAFBOqlatKmvXrtUhXPtas2aNVKlSxZpEjmUjgSPiHepboIzzr7Vu3Trt+vrRWiSxgu/z9iZQHgSt94Fp+Hr42RampUuXWhNI3gNBiYNk0+C0adO0q+tXU6ZMsSaQPAOKJRYqs4Ay1LNnT+3i+lf37t2tCcwEDUHAWguUkZiYmGtqnBes2Nfo6GhrEleDgMSqijqZjz6bNm3Spn87+vrrr/M/9j0J/FI1wPqZOnHQoEHa5G9PAwYMsCYwA1QFReploE5ixfjcuXPanLvKwuPVihUr5Llnn5Wnn35a2rVtKx07dpTevXvLlu+/1628FYu1+SreLBRfUTcAc95i9uzZ2pTzOn7suEybMlVmz0qQTh0fk/r1CpSbTBo0aCDbtrpfjLXTzJkzrbFwviUMFKrhQDWOioqS3NxcbcZZpR9IlyaNb7MGpuA1p0yZMgXgZzVq1JCFiYnagndiDho2bGiNk/M8tuKo2yzHz5o1S5twVmn706RO7TrKh12y7GCd0Yhr2tSp2pJ3SkhIMP0DVqJsZ/s49agaRUREuFaGHz5slPLBpNglyw6jrXHe6FGjtDVvxPsAp1eN/ID2oIA+BaoBpwzd0IGf0iU0NEz5yJ8kf7B+Ex/v3FkyM3/Rlt3XwIEDTd9gEfAR53DNeY3vXbrrfbPpP2YQdgnyB2sSx455RVt2X5s3bzb9AubKZ665M1AfxsbG6lOcV3ZWtsTH3a782CXHX4wk3h4fL8eOHNbW3VeTJk2MBBIuIzHFhT3qA87fuqnklaukSpWqypddcvyFd+zy5cvLksWLtGX3xQkzI0+Ak1FKZcB/gfpg9erVurl7GtCvv0qAcXMoLoz3sQ4d5GJujrbsrpKTk60J5LKR0kBVXX4FahHP6dOndXP3lHHoZ6kVXksFYpcYf+H5NWvWlC3fbdaW3dWpU6dwEww1EngRqCrNo/qAtGzZUjd1TxyYfrposVSoULHQgbO/MOayZcvJe/PmiVy6pD24q7vuustIIOHQTz3fqQN9+/bVzdzTgg8+MAOwS0og0AbL8G9Oniy5ORe0B3fFKVwjfjAKXC7ZezHKX4drrOHPLimBQBsqgf+cLDkXvElgvoq1KvmvMQ54Md9x9vQZqfu7W5Q/u6QEAm1Uq1ZNpk15Uy5kZ2sP7orToEa+gCq07jAOeFU47d2zl/Jnl5RAoI169erKrIQZnn0DN27caCSPcM12SDr/hxf0PXv26GbuKScnR5rdcYdjCbwNg9ukpUs9u4ns2rVL5Yq+AYsveasNON978OBB3cw9vTHpDeU82DGg8TRy3z33Sso67yb5udTYMn/MVQx5BVTOzB/x4LGocUwj5dwuKYFAG+XKlZNHH3lEdm7frq27r4yMDKlcubKRQBZYLyfw8GH3E7g6eZXUjowMagyog1czZxNfHS9H0anzHk092CVQrbbi15JfT/d1Sf6oF4PbJacomHjeeZs3ayajRoyQ99+dJzHRMapI+/prE7UP93TgwAH1DM74AVdzXa5C8wLphVq3ahVUAiMiwqXl3XfLH5o3V6V+I/6YmGg5ejhDe3FHP+zcafoDaSBkq3GAqze90MIFC/AIVrbYP2NrTZDwGP8bWr067shLtBd39FVqqtX3FhCSbBxYvHixbuau9uzeZQaRPznFhbaqI4Hz8ZN2c0jzySefmLGDlUDVtdSByXim9EInj5+QyFr2bxjZJccfeG5YaJi8O3euXPr1V+3JeU2aNMka7xwQMsw4wAltr7Qcj43NMaCuFREht9x8s8TFxkqknsS2S1BR8DxOxS7698eufgN79OhhJI/wNbKQvxgHmuOi7JVyLmTLlytXyusTJ8qE8f+Qt2fNlDdefw3fotBiXRsZf0z072Vjaor24I6a4e5v5Au0AyG3gBwe4Fjw2LFjuqn74k8tK/MXybRMoT71RDcVnF2SioLnPfTgg5KdlamtOaujR49ax4AXwM1AaTtQHyxfvlw391Y7dmyXNq1bqwDtkuMPxt358c4dtVVnlfTZZ0byCEcvpmYA9UH//v11c+/EdS+hodWV/2CeUIiRxIfbPySHDjr7YNCvXz8jeWQ6MMWZdvUB14F4+Y5bbm6OxDWNVb7tElIcjCTyprRuzRrtKTgxJ1zkZOQJ8N5hiu/R8rlOfcj3b73SoIF5a/BK2yQiWGi3aextct6B95LXr19vJI6cAFxH6aP5QDXo1auXPs1dLU/Ku6YE+7MtDNql/ffmzdUeiy+uETfyAziPXkAPANWAI3q+Zu+mcnNy1LCD/uw67xS0f/+99wb1LTx+/LgqYBj5AfeDAioFuPuFajRxwgR9ujsyZud4vbLruFPQx401asjGr1K158A1fvx4I3GEIxbmylbmeyFc2urmm+b33XOP8mPXaSehjxo33CAfJS6A18CfULi0rVatvEUAmiu+N1IJHAKqMTPvhvbt3StldefsOu0k9FGz5o3y0YIP4TnwBI4bN85IHOH8EV80v6KeBeoEXguPHDmiTTmnxYvyKhpu/3wJ/dSpXVuWLflUe/dfrD7nu/b1BUWKy1fNxUbdunXV5pzTmFdeUbbtOuw09HNrVENJ/uIL7d1/denSxUgc4WKissAvmQUGkpSUpE06ow5//5uya9dhp6Gf2+PjAr6JcJGBNQfgQRCQPgbqZN5QeCt3SizF065dh53EeCL58wMPyI6tW7X3osWCSr4bx0IQsLiJl/mGert27bT54NWkcWNl067TTmIksGuXxyX9px+196LVpk0bI3GEk25cAl0scQMv0xhXaDqh+Li8zXDsOu0kRgKfevIJ+d+pk9r7lTV06FCzv5qHQFCaCEyDM2bM0K6KrxYe/4S7dO4sJ/yoc7711ltmPzWvAkf0GTANL/iQY6riiUXURjExyo5dp53ESGC3rl0k4+dDOgJ7fWBZt6hZChxTFWDum0DosDg6zLFV1eAXmfsDiwmlSoXI8889JydPFH4TnD9/vtkvzTegMnBUEeAHYDqaXowtAD5fsUKd61YFxorh5+UXR/tMG1hls3cM6wGu7YLJ7U/M9YRkyJAhOhT/9PJLL6nz7DrsNPTDJSuTXpsoF7ILvnHPN7KsfQEsFDi23Ulh4l+He02Zjtu2bev30riH27dX59h12Gnoh6vqZyUkyEXLm6cH09PzD1XIV6DYw5VAxaKDOdAmHGwvXLhQh2gvrtC/NSpKtbfrsNPQz+VKTJ4SExPzD5IJ91xlnzyX+Xa7QadOnWTv3r06XF/xL29ss2TXYacx/HCyfd++fSo2a6yal8BVVRvAVUpmUNwaavTo0Woe1arUlBT1uRc3EMJhDOFSuOq+VRXC3dpagxIhbhUwE/gEGREerhKZlpamEvi+Hi7YddZN8scFEgBjLnH6E0gFPgFzpzeuKeHztFffvvyEwK+OjTGWeHUF3wGfRBK7zrkNfsa/4A/HlVRFVpNLkjjx0gGsAlcleUjcWSSOA2O+jc+Nc0r8PtKFiTtfTkCH9tt11EngIxtJ47wO93hNBGMBX47mm5V+V5RLqhqhc3PBbnT0NLBNQiDQBjgPm5zw4TPsYvAmeB7wLlsXOLIPYEkQH9DjwGNgMPgXOp4IVoFvkYgfwH6QDn4GGRYOgjSwG22/A+txfhLgy5JTAN865ZQj1+xxo1xukFPo/K0zCgn5PxQ3IHGhHh9TAAAAAElFTkSuQmCC";

		private bool IsTopLeftBoxMinimized
		{
			get { return runtimeMinimizeTopLeftOverride.HasValue ? runtimeMinimizeTopLeftOverride.Value : MinimizeTopLeftBox; }
		}

		private bool IsSummaryBoxMinimized
		{
			get { return runtimeMinimizeSummaryOverride.HasValue ? runtimeMinimizeSummaryOverride.Value : MinimizeSummaryBoxes; }
		}

		private MarketProfilePeriodMode EffectiveProfileMode
		{
			get { return runtimeProfileModeOverride.HasValue ? runtimeProfileModeOverride.Value : ProfileMode; }
		}

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "50Charts-style 3-Session (Asia: 20 TPOs, London: 11 TPOs, New York: 15 TPOs) Market Profile for NinjaTrader 8.";
				Name = "MarketProfile50Charts";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				PaintPriceMarkers = false;
				IsAutoScale = true;
				ScaleJustification = ScaleJustification.Right;
				IsSuspendedWhileInactive = false;

				TpoSize = 0;
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

				ShowPoorHigh = true;
				ShowPoorLow = true;
				ShowSwingHigh = true;
				ShowSwingLow = true;
				ShowVirginVAH = true;
				ShowVirginVAL = true;
				ShowVirginVPoC = true;
				ShowVirginVPPoC = true;
				ShowVirginMidLine = true;

				ShowVolumeProfile = true;
				ShowInitialBalance = true;
				ShowSinglePrints = true;
				ShowPoorHighLow = true;
				ShowSummary = true;
				FixSummaryBoxAtBottom = true;
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
			else if (State == State.Terminated)
			{
				DisposeLogoBitmap();
				DetachMouseHook();
			}
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

					using (SharpDX.DataStream ds = new SharpDX.DataStream(pixels.Length, true, true))
					{
						ds.Write(pixels, 0, pixels.Length);
						ds.Position = 0;
						for (int pIdx = 0; pIdx + 3 < pixels.Length; pIdx += 4)
						{
							int a = pixels[pIdx + 3];
							if (a < 255)
							{
								int invA = 255 - a;
								pixels[pIdx + 0] = (byte)(pixels[pIdx + 0] + (23 * invA) / 255);
								pixels[pIdx + 1] = (byte)(pixels[pIdx + 1] + (15 * invA) / 255);
								pixels[pIdx + 2] = (byte)(pixels[pIdx + 2] + (13 * invA) / 255);
								pixels[pIdx + 3] = 255;
							}
						}
						object pf = typeof(RenderTarget).GetProperty("PixelFormat").GetValue(rt, null);
						object bmpProps = Activator.CreateInstance(typeof(BitmapProperties), pf);
						cachedLogoBitmap = (SharpDX.Direct2D1.Bitmap)Activator.CreateInstance(
							typeof(SharpDX.Direct2D1.Bitmap),
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
					}
					cc.RemoveHandler(System.Windows.UIElement.PreviewMouseLeftButtonDownEvent, mouseLeftDownHandler);
					cc.AddHandler(System.Windows.UIElement.PreviewMouseLeftButtonDownEvent, mouseLeftDownHandler, true);
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
			});
		}

		private void DetachMouseHook()
		{
			ChartControl cc = hookedChartControl;
			var ownerWin = hookedOwnerWindow;
			var handler = mouseLeftDownHandler;
			if (cc != null && handler != null)
			{
				cc.Dispatcher.InvokeAsync(() =>
				{
					try { cc.RemoveHandler(System.Windows.UIElement.PreviewMouseLeftButtonDownEvent, handler); } catch { }
					try
					{
						if (ownerWin != null)
							ownerWin.RemoveHandler(System.Windows.UIElement.PreviewMouseLeftButtonDownEvent, handler);
					}
					catch { }
				});
			}
			hookedOwnerWindow = null;
			hookedChartControl = null;
			isMouseHooked = false;
		}

		private void OnChartPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			ChartControl cc = hookedChartControl ?? ChartControl;
			if (!IsChartControlActive(cc))
				return;

			if (TryHandleChartButtonClick(cc, e))
			{
				e.Handled = true;
			}
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
					break;

				case ChartButtonAction.SelectProfileWeekly:
					runtimeProfileModeOverride = MarketProfilePeriodMode.Weekly;
					ProfileMode = MarketProfilePeriodMode.Weekly;
					isProfileDropdownOpen = false;
					hasManualPixelScroll = false;
					currentScrollPx = float.MaxValue;
					TriggerHistoricalBackfillIfNeeded(65);
					break;

				case ChartButtonAction.SelectProfileMonthly:
					runtimeProfileModeOverride = MarketProfilePeriodMode.Monthly;
					ProfileMode = MarketProfilePeriodMode.Monthly;
					isProfileDropdownOpen = false;
					hasManualPixelScroll = false;
					currentScrollPx = float.MaxValue;
					TriggerHistoricalBackfillIfNeeded(180);
					break;
			}
		}

		private void TriggerHistoricalBackfillIfNeeded(int daysNeeded)
		{
			if (isBackfillInFlight || daysNeeded <= highestBackfillDaysRequested || Instrument == null)
				return;

			double currentSpanDays = 0;
			lock (profileLock)
			{
				if (rawMinuteSamples.Count >= 2)
					currentSpanDays = (rawMinuteSamples[rawMinuteSamples.Count - 1].Time - rawMinuteSamples[0].Time).TotalDays;
			}
			if (currentSpanDays >= daysNeeded * 0.8)
			{
				highestBackfillDaysRequested = daysNeeded;
				return;
			}

			isBackfillInFlight = true;
			highestBackfillDaysRequested = daysNeeded;
			Instrument inst = Instrument;
			DateTime toTime = DateTime.Now.AddDays(1);
			DateTime fromTime = toTime.AddDays(-daysNeeded);

			System.Threading.Tasks.Task.Run(() =>
			{
				try
				{
					BarsRequest req = new BarsRequest(inst, fromTime, toTime);
					req.BarsPeriod = new BarsPeriod
					{
						BarsPeriodType = BarsPeriodType.Minute,
						Value = daysNeeded > 90 ? 15 : 5
					};
					req.MergePolicy = MergePolicy.MergeBackAdjusted;
					if (inst.MasterInstrument != null && inst.MasterInstrument.TradingHours != null)
						req.TradingHours = inst.MasterInstrument.TradingHours;

					req.Request((bars, errorCode, errorMessage) =>
					{
						try
						{
							if (State == State.Terminated || errorCode != ErrorCode.NoError || bars == null || bars.Bars == null || bars.Bars.Count <= 0)
								return;

							bool addedAny = false;
							lock (profileLock)
							{
								DateTime earliestExisting = rawMinuteSamples.Count > 0 ? rawMinuteSamples[0].Time : DateTime.MaxValue;
								List<MinuteSample> prepended = new List<MinuteSample>();
								int cnt = bars.Bars.Count;
								for (int i = 0; i < cnt; i++)
								{
									DateTime t = bars.Bars.GetTime(i);
									if (t >= earliestExisting)
										break;
									prepended.Add(new MinuteSample
									{
										Time = t,
										Open = bars.Bars.GetOpen(i),
										High = bars.Bars.GetHigh(i),
										Low = bars.Bars.GetLow(i),
										Close = bars.Bars.GetClose(i),
										Volume = Math.Max(0, Convert.ToDouble(bars.Bars.GetVolume(i))),
										BarIndex = 0
									});
								}
								if (prepended.Count > 0)
								{
									prepended.AddRange(rawMinuteSamples);
									rawMinuteSamples.Clear();
									rawMinuteSamples.AddRange(prepended);
									double step = ComputeAutoOrManualPriceStep();
									RebuildAllSessionProfiles(step);
									addedAny = true;
								}
							}

							if (addedAny && ChartControl != null)
							{
								ChartControl.Dispatcher.InvokeAsync(() =>
								{
									ForceRefresh();
									ChartControl.InvalidateVisual();
								});
							}
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
			if (CurrentBars == null || CurrentBars.Length < 2)
				return;

			if (BarsInProgress == 0)
			{
				if (CurrentBars[0] < 0)
					return;

				BarBrushes[0] = Brushes.Transparent;
				CandleOutlineBrushes[0] = Brushes.Transparent;

				lock (profileLock)
				{
					DateTime chartDate = Times[0][0].Date;
					if (!chartDayFirstBar.ContainsKey(chartDate))
						chartDayFirstBar[chartDate] = CurrentBars[0];
					chartDayLastBar[chartDate] = CurrentBars[0];
				}
				return;
			}

			if (BarsInProgress != 1 || CurrentBars[1] < 0)
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
				if (rawMinuteSamples.Count > 0 && rawMinuteSamples[rawMinuteSamples.Count - 1].BarIndex == minuteBarIndex)
				{
					isSameMinuteBar = true;
					MinuteSample prev = rawMinuteSamples[rawMinuteSamples.Count - 1];
					sample.Open = prev.Open;
					sample.High = Math.Max(prev.High, sample.High);
					sample.Low = Math.Min(prev.Low, sample.Low);
					sample.Volume = prev.Volume + barVolume;
					rawMinuteSamples[rawMinuteSamples.Count - 1] = sample;
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

		public override void OnCalculateMinMax()
		{
			double minVal = double.MaxValue;
			double maxVal = double.MinValue;

			lock (profileLock)
			{
				if (profiles.Count > 0)
				{
					List<DayProfile> rawSessions = profiles.Values.Where(p => p.Rows.Count > 0).ToList();
					List<DayProfile> allSessions = BuildDisplaySessions(rawSessions);
					if (allSessions.Count > 0)
					{
						double step = GetPriceStep();
						float panelLeft = ChartPanel != null ? (float)ChartPanel.X : 0f;
						float panelRight = ChartPanel != null ? (float)(ChartPanel.X + ChartPanel.W) : 1800f;
						List<DateColumnGroup> dateGroups = BuildDateColumnGroups(allSessions, step, panelLeft, panelRight);

						for (int g = 0; g < dateGroups.Count; g++)
						{
							DateColumnGroup group = dateGroups[g];
							if (group.RightX < panelLeft || group.LeftX > panelRight)
								continue;
							for (int s = 0; s < group.Sessions.Count; s++)
							{
								DayProfile d = group.Sessions[s];
								if (d.Low < minVal) minVal = d.Low;
								if (d.High > maxVal) maxVal = d.High;
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
							}
						}
					}
				}
			}

			if (minVal < double.MaxValue && maxVal > double.MinValue)
			{
				double step = GetPriceStep();
				double span = Math.Max(step * 10.0, maxVal - minVal);
				MinValue = minVal - span * 0.28;
				MaxValue = maxVal + span * 0.15;
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
					// If the market opened after an exchange maintenance break (e.g., 04:30 AM IST on CME/Globex when 03:30-04:30 AM is closed),
					// snap EffectiveOpenTime to the first active 30-min bracket boundary and distribute across all expectedPeriods (A..T = 20 TPOs)
					// so Asia ALWAYS starts with 'A' (0) and 'B' (1) and populates all 20 TPOs!
					double initialGapMins = Math.Max(0.0, (sample.Time.AddSeconds(-1) - sessionStart).TotalMinutes);
					int skippedIntervals = (int)Math.Floor(initialGapMins / Math.Max(1, TpoIntervalMinutes));
					DateTime effectiveOpen = sessionStart;
					if (skippedIntervals > 0 && skippedIntervals < expectedPeriods)
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

				// Compute periodIndex from EffectiveOpenTime..SessionCloseTime so 'A' (0) and 'B' (1) are always the first two brackets of the session
				double activeWindowMinutes = Math.Max((double)Math.Max(1, TpoIntervalMinutes), (day.SessionCloseTime - day.EffectiveOpenTime).TotalMinutes);
				double bracketMinutes = activeWindowMinutes / Math.Max(1, expectedPeriods);
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
				day.High = Math.Max(day.High, sample.High);
				day.Low = Math.Min(day.Low, sample.Low);
				day.Close = sample.Close;

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

		// Determines whether a single-print bin is a Buying Single Print (true) or Selling Single Print (false)
		private static bool IsBuyingSinglePrintBin(DayProfile day, int bin, PriceRow row, int minBin, int maxBin, int pocBin)
		{
			int totalRange = Math.Max(1, maxBin - minBin);
			// Bottom tail single prints are Buying tail single prints; top tail single prints are Selling tail single prints
			if (bin <= minBin + Math.Max(1, totalRange / 6))
				return true;
			if (bin >= maxBin - Math.Max(1, totalRange / 6))
				return false;

			if (row != null && row.Periods.Count == 1)
			{
				int pIdx = row.Periods.First();
				PeriodSlice slice;
				if (day.PeriodSlices.TryGetValue(pIdx, out slice))
				{
					if (Math.Abs(slice.Close - slice.Open) > 1e-9)
						return slice.Close > slice.Open;
				}
			}
			return bin <= pocBin;
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
					bool isBuy = IsBuyingSinglePrintBin(day, b, row, minBin, maxBin, pocBin);
					if (current != null && current.EndBin == b - 1 && current.IsBuying == isBuy && current.PeriodIndex == pIdx)
					{
						current.EndBin = b;
					}
					else
					{
						current = new SinglePrintZone
						{
							StartBin = b,
							EndBin = b,
							IsBuying = isBuy,
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
						int shifted = Math.Min(LetterSequence.Length - 1, p + periodOffset);
						dstRow.Periods.Add(shifted);
						if (shifted > merged.LastPeriod)
							merged.LastPeriod = shifted;
					}
				}

				foreach (KeyValuePair<int, PeriodSlice> kv in s.PeriodSlices)
				{
					PeriodSlice srcSlice = kv.Value;
					int shiftedIdx = Math.Min(LetterSequence.Length - 1, srcSlice.PeriodIndex + periodOffset);
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

		private DayProfile NormalizeSessionRowCount(DayProfile src, double baseStep)
		{
			if (src == null || src.Rows.Count == 0)
			{
				if (src != null)
					src.EffectiveStep = baseStep;
				return src;
			}

			int minB = src.Rows.Keys.Min();
			int maxB = src.Rows.Keys.Max();
			// Ensure every integer bin between minB and maxB exists in src.Rows so no profile EVER has a missing-row gap
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

			int rawSpan = maxB - minB + 1;
			if ((TpoSize > 0 && EffectiveProfileMode == MarketProfilePeriodMode.Daily && rawSpan <= 65) || rawSpan <= 44)
			{
				src.EffectiveStep = baseStep;
				return src;
			}

			int groupMult = Math.Max(2, (int)Math.Ceiling(rawSpan / 30.0));
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

					double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;
					int closeBin = PriceToBin(day.Close, dStep);
					int maxRowCells = day.Rows.Count > 0
						? Math.Max(8, day.Rows.Max(kv => kv.Value.Periods.Count + (kv.Key == closeBin ? 1 : 0)))
						: 12;
					float collapsedW = Math.Max(238f, maxRowCells * 8.6f + (ShowVolumeProfile ? 38f : 0f) + 24f);
					float splitExtraW = 0f;
					if (day.IsSplitExpanded && day.PeriodSlices.Count > 0)
					{
						int sliceCols = Math.Max(5, day.PeriodSlices.Count + 1);
						int volBarCount = ShowVolumeProfile ? Math.Min(3, Math.Max(1, day.PeriodSlices.Count)) : 0;
						float volAreaW = volBarCount > 0 ? (volBarCount * 30f + (volBarCount - 1) * 6f + 8f) : 0f;
						splitExtraW = Math.Max(205f, sliceCols * 9.6f + volAreaW + 26f);
					}
					groupW += collapsedW + splitExtraW;
				}
				grp.Width = Math.Max(238f, groupW);
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
					double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;
					int closeBin = PriceToBin(day.Close, dStep);
					int maxRowCells = day.Rows.Count > 0
						? Math.Max(8, day.Rows.Max(kv => kv.Value.Periods.Count + (kv.Key == closeBin ? 1 : 0)))
						: 12;
					float collapsedW = Math.Max(238f, maxRowCells * 8.6f + (ShowVolumeProfile ? 38f : 0f) + 24f);
					float splitExtraW = 0f;
					if (day.IsSplitExpanded && day.PeriodSlices.Count > 0)
					{
						int sliceCols = Math.Max(5, day.PeriodSlices.Count + 1);
						int volBarCount = ShowVolumeProfile ? Math.Min(3, Math.Max(1, day.PeriodSlices.Count)) : 0;
						float volAreaW = volBarCount > 0 ? (volBarCount * 30f + (volBarCount - 1) * 6f + 8f) : 0f;
						splitExtraW = Math.Max(205f, sliceCols * 9.6f + volAreaW + 26f);
					}
					float slotWidth = collapsedW + splitExtraW;
					day.SlotLeftX = slotX;
					day.SlotRightX = slotX + slotWidth;
					day.IbX = slotX + 6f;
					day.CollapsedRightX = slotX + collapsedW - 8f;
					slotX += slotWidth;
				}
			}

			return groups;
		}

		// Computes how far horizontally a level from sessions[currentIdx] extends:
		// - If broken by a subsequent session j > currentIdx, extends horizontally until sessions[j].IbX (where it breaks) and returns false.
		// - If never broken by any subsequent session, extends all the way to panelRight - 6f (Virgin) and returns true.
		private static bool GetLevelExtensionX(
			List<DayProfile> sessions,
			int currentIdx,
			double levelPrice,
			float xCollapsedRight,
			float panelRight,
			out float drawEndX,
			bool isHighExtreme = false,
			bool isLowExtreme = false)
		{
			const double eps = 1e-5;
			for (int j = currentIdx + 1; j < sessions.Count; j++)
			{
				DayProfile next = sessions[j];
				bool broken = false;
				if (isHighExtreme)
				{
					if (next.High >= levelPrice - eps)
						broken = true;
				}
				else if (isLowExtreme)
				{
					if (next.Low <= levelPrice + eps)
						broken = true;
				}
				else
				{
					if (next.Low <= levelPrice + eps && next.High >= levelPrice - eps)
						broken = true;
				}

				if (broken)
				{
					drawEndX = Math.Min(panelRight - 6f, Math.Max(xCollapsedRight, next.IbX));
					return false;
				}
			}

			drawEndX = panelRight - 6f;
			return true;
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (ChartBars == null || ChartPanel == null || RenderTarget == null || IsInHitTest)
				return;

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

			lock (profileLock)
			{
				activeChartButtons.Clear();
				if (profiles.Count == 0)
					return;

				List<DayProfile> rawSessions = profiles.Values.Where(p => p.Rows.Count > 0).ToList();
				List<DayProfile> allSessions = BuildDisplaySessions(rawSessions);
				if (allSessions.Count == 0)
					return;

				double step = GetPriceStep();
				for (int i = 0; i < allSessions.Count; i++)
					allSessions[i] = NormalizeSessionRowCount(allSessions[i], step);

				float panelLeft = (float)ChartPanel.X;
				float panelRight = (float)(ChartPanel.X + ChartPanel.W);
				float panelTop = (float)ChartPanel.Y;
				float panelBottom = (float)(ChartPanel.Y + ChartPanel.H);

				List<DateColumnGroup> dateGroups = BuildDateColumnGroups(allSessions, step, panelLeft, panelRight);
				if (dateGroups.Count == 0)
					return;

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
						if (d.IbX < panelLeft + 4f || d.CollapsedRightX > panelRight - 178f)
							continue;
						if (d.Low < visMinPrice) visMinPrice = d.Low;
						if (d.High > visMaxPrice) visMaxPrice = d.High;
					}
				}
				if (visMinPrice == double.MaxValue || visMaxPrice == double.MinValue)
				{
					int tailStart = Math.Max(0, allSessions.Count - 3);
					for (int i = tailStart; i < allSessions.Count; i++)
					{
						DayProfile d = allSessions[i];
						if (d.Low < visMinPrice) visMinPrice = d.Low;
						if (d.High > visMaxPrice) visMaxPrice = d.High;
					}
				}

				float bottomDockReserve = (ShowSummary && FixSummaryBoxAtBottom)
					? (IsSummaryBoxMinimized ? 68f : 168f)
					: 48f;
				float topBoxReserve = (ShowTopLeftBox && !IsTopLeftBoxMinimized) ? 114f : 68f;
				float usableTopY = panelTop + topBoxReserve;
				float usableBotY = Math.Max(usableTopY + 160f, panelBottom - bottomDockReserve);
				float targetSpanPx = usableBotY - usableTopY;

				float testYHigh = (float)chartScale.GetYByValue(visMaxPrice);
				float testYLow = (float)chartScale.GetYByValue(visMinPrice);
				float testSpanPx = Math.Abs(testYLow - testYHigh);
				// Auto-fit visible sessions to the usable vertical height above the fixed bottom Summary Box dock
				bool useFallbackYScale = testSpanPx < targetSpanPx * 0.72f
					|| testYHigh < usableTopY - 25f
					|| testYHigh > usableTopY + 45f
					|| testYLow > usableBotY + 4f
					|| testYLow < usableBotY - 55f;

				double safePriceSpan = Math.Max(step * 8.0, visMaxPrice - visMinPrice);
				double visSteps = Math.Max(8.0, safePriceSpan / step + 2.0);
				float fitPxPerStep = targetSpanPx / (float)visSteps;
				float sharedPxPerStep = Math.Min(11.4f, Math.Max(7.0f, fitPxPerStep));
				float sharedTotalSpanPx = (float)(safePriceSpan / step) * sharedPxPerStep;
				float sharedMidY = 0.5f * (usableTopY + usableBotY);
				float sharedTopY = Math.Max(usableTopY + 2f, sharedMidY - sharedTotalSpanPx * 0.5f);
				float sharedBotY = Math.Min(usableBotY - 2f, sharedTopY + sharedTotalSpanPx);

				float chartScalePxPerStep = (float)(testSpanPx / Math.Max(1.0, safePriceSpan / step));
				if (chartScalePxPerStep > 11.8f || chartScalePxPerStep < 7.2f)
					useFallbackYScale = true;

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
					CalculateTpoValueArea(d, ValueAreaPercent, out d.PocBin, out d.VahBin, out d.ValBin);
					d.MaxRowVolume = Math.Max(1e-9, d.Rows.Values.Max(r => r.Volume));
					d.VolumePocBin = d.Rows.OrderByDescending(kv => kv.Value.Volume)
						.ThenBy(kv => Math.Abs(kv.Key - d.PocBin)).First().Key;
					CalculateVolumeValueArea(d, ValueAreaPercent, d.VolumePocBin, out d.VolumeVahBin, out d.VolumeValBin);
					int minB = d.Rows.Keys.Min();
					int maxB = d.Rows.Keys.Max();
					d.SinglePrintZones = ComputeSinglePrintZones(d, minB, maxB, d.PocBin);

					float rawHighY = priceToY(maxB * dStep + dStep * 0.5);
					float rawLowY = priceToY(minB * dStep - dStep * 0.5);
					if (rawHighY > rawLowY)
					{
						float sw = rawHighY;
						rawHighY = rawLowY;
						rawLowY = sw;
					}
					if (EffectiveProfileMode != MarketProfilePeriodMode.Daily)
					{
						int dRowCount = Math.Max(1, maxB - minB + 1);
						float minReqSpan = Math.Min(usableBotY - usableTopY - 8f, dRowCount * 8.8f);
						if (rawLowY - rawHighY < minReqSpan)
						{
							float midY = 0.5f * (rawHighY + rawLowY);
							rawHighY = Math.Max(usableTopY + 2f, midY - minReqSpan * 0.5f);
							rawLowY = Math.Min(usableBotY - 2f, rawHighY + minReqSpan);
							if (rawLowY - rawHighY < minReqSpan)
								rawHighY = Math.Max(usableTopY + 2f, rawLowY - minReqSpan);
						}
					}
					d.RenderTopY = rawHighY;
					d.RenderBotY = Math.Max(rawHighY + 4f, rawLowY);
				}

				DxBrush[] letterBrushes = new DxBrush[LetterColors.Length];
				DxBrush[] sessionBrushes = new DxBrush[SessionColors.Length];
				List<RightEdgeTag> rightEdgeTags = new List<RightEdgeTag>();

				try
				{
					for (int i = 0; i < LetterColors.Length; i++)
						letterBrushes[i] = new DxBrush(RenderTarget, LetterColors[i]);
					for (int i = 0; i < SessionColors.Length; i++)
						sessionBrushes[i] = new DxBrush(RenderTarget, SessionColors[i]);

					var dashProps = new StrokeStyleProperties
					{
						DashStyle = SharpDX.Direct2D1.DashStyle.Custom,
						DashCap = CapStyle.Flat,
						StartCap = CapStyle.Flat,
						EndCap = CapStyle.Flat
					};

					using (var dashedStroke = new StrokeStyle(Core.Globals.D2DFactory, dashProps, new float[] { 4f, 3f }))
					using (var whiteBrush = new DxBrush(RenderTarget, new SharpDX.Color(238, 242, 245)))
					using (var textBrush = new DxBrush(RenderTarget, new SharpDX.Color(195, 204, 212)))
					using (var orangeBrush = new DxBrush(RenderTarget, new SharpDX.Color(245, 155, 35)))
					using (var yellowBrush = new DxBrush(RenderTarget, new SharpDX.Color(238, 202, 42)))
					using (var greenBrush = new DxBrush(RenderTarget, new SharpDX.Color(38, 218, 98)))
					using (var redBrush = new DxBrush(RenderTarget, new SharpDX.Color(242, 72, 72)))
					using (var cyanBrush = new DxBrush(RenderTarget, new SharpDX.Color(72, 198, 222)))
					using (var abPoorBrush = new DxBrush(RenderTarget, new SharpDX.Color(46, 232, 168)))
					using (var swingLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(82, 138, 252)))
					using (var daySepBrush = new DxBrush(RenderTarget, new SharpDX.Color(62, 78, 96)))
					using (var sessionSepBrush = new DxBrush(RenderTarget, new SharpDX.Color(32, 42, 54)))
					using (var spineLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(85, 96, 108)))
					using (var vaFillBrush = new DxBrush(RenderTarget, new SharpDX.Color(16, 48, 66, 130)))
					using (var vaBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(58, 138, 178)))
					using (var vaVirginBrush = new DxBrush(RenderTarget, new SharpDX.Color(88, 204, 248)))
					using (var singleBuyRowBrush = new DxBrush(RenderTarget, new SharpDX.Color(14, 148, 78, 68)))
					using (var singleSellRowBrush = new DxBrush(RenderTarget, new SharpDX.Color(178, 32, 48, 68)))
					using (var singleBuyZoneRayBrush = new DxBrush(RenderTarget, new SharpDX.Color(14, 148, 78, 36)))
					using (var singleSellZoneRayBrush = new DxBrush(RenderTarget, new SharpDX.Color(178, 32, 48, 36)))
					using (var singleBuyBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(42, 224, 118, 195)))
					using (var singleSellBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(248, 82, 92, 195)))
					using (var btnBgBrush = new DxBrush(RenderTarget, new SharpDX.Color(22, 34, 48, 248)))
					using (var btnActiveBgBrush = new DxBrush(RenderTarget, new SharpDX.Color(18, 78, 92, 250)))
					using (var btnMergeBgBrush = new DxBrush(RenderTarget, new SharpDX.Color(68, 48, 16, 250)))
					using (var volumeOutBrush = new DxBrush(RenderTarget, new SharpDX.Color(76, 86, 96)))
					using (var volumeVaBrush = new DxBrush(RenderTarget, new SharpDX.Color(198, 26, 90)))
					using (var volumePocBrush = new DxBrush(RenderTarget, new SharpDX.Color(246, 132, 178)))
					using (var splitVolVaBrush = new DxBrush(RenderTarget, new SharpDX.Color(98, 198, 184, 195)))
					using (var splitVolOutBrush = new DxBrush(RenderTarget, new SharpDX.Color(68, 82, 92, 175)))
					using (var splitVolPocBrush = new DxBrush(RenderTarget, new SharpDX.Color(198, 248, 232, 235)))
					using (var pocLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(228, 152, 28)))
					using (var vppocLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(235, 200, 45)))
					using (var midLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(218, 128, 255)))
					using (var ibBarBrush = new DxBrush(RenderTarget, new SharpDX.Color(32, 228, 88)))
					using (var cardBgBrush = new DxBrush(RenderTarget, new SharpDX.Color(9, 14, 20, 246)))
					using (var pillBgBrush = new DxBrush(RenderTarget, new SharpDX.Color(10, 15, 22, 248)))
					using (var cardBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(38, 54, 70, 205)))
					using (var dateBannerBgBrush = new DxBrush(RenderTarget, new SharpDX.Color(12, 20, 30, 255)))
					using (var cardFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, 10.0f))
					using (var tagCenterFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 9.5f))
					using (var spTagFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 8.8f))
					using (var headerFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10.5f))
					using (var dateAxisFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 11.0f))
					using (var labelRightFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 9.0f))
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

						labelRightFormat.TextAlignment = TextAlignment.Trailing;
						labelRightFormat.ParagraphAlignment = ParagraphAlignment.Center;
						labelRightFormat.WordWrapping = WordWrapping.NoWrap;

						List<double> allIbRanges = allSessions
							.Where(d => d.InitialBalanceHigh > d.InitialBalanceLow && d.InitialBalanceLow != double.MaxValue)
							.Select(d => d.InitialBalanceHigh - d.InitialBalanceLow)
							.ToList();
						double avgIbRange = allIbRanges.Count > 0 ? allIbRanges.Average() : step * 6;

						// =========================================================================
						// PASS 1: Draw Background Separators, Value Area Boxes, Virgin Single Print Rays & Virgin Extending Lines
						// (STRICTLY VIRGIN ONLY: any level crossed by a subsequent session is NOT extended!)
						// =========================================================================
						for (int g = 0; g < dateGroups.Count; g++)
						{
							DateColumnGroup group = dateGroups[g];
							if (group.RightX < panelLeft || group.LeftX > panelRight - 178f)
								continue;

							if (group.LeftX >= panelLeft && group.LeftX <= panelRight - 178f)
								RenderTarget.DrawLine(new Vector2(group.LeftX, panelTop), new Vector2(group.LeftX, panelBottom), daySepBrush, 1.6f);
							if (group.RightX >= panelLeft && group.RightX <= panelRight - 178f)
								RenderTarget.DrawLine(new Vector2(group.RightX, panelTop), new Vector2(group.RightX, panelBottom), daySepBrush, 1.6f);

							for (int sIdx = 0; sIdx < group.Sessions.Count; sIdx++)
							{
								DayProfile day = group.Sessions[sIdx];
								int globalIdx = allSessions.IndexOf(day);
								bool isLatestSession = (globalIdx == allSessions.Count - 1);
								double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;

								float xSlotLeft = day.SlotLeftX;
								float xIb = day.IbX;
								float xCollapsedRight = day.CollapsedRightX;

								if (xIb < panelLeft + 4f || xCollapsedRight > panelRight - 178f)
									continue;

								if (sIdx > 0 && xSlotLeft >= panelLeft && xSlotLeft <= panelRight - 178f)
								{
									RenderTarget.DrawLine(
										new Vector2(xSlotLeft, panelTop),
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

								// 1. Extending Buying & Selling Single Print Zones (extends until broken; right-edge tag when Virgin)
								if (ShowSinglePrints)
								{
									for (int z = 0; z < day.SinglePrintZones.Count; z++)
									{
										SinglePrintZone spZone = day.SinglePrintZones[z];
										double zoneMidPrice = ((spZone.StartBin + spZone.EndBin) * 0.5) * dStep;
										float ySpTop = SessionPriceToY(day, spZone.EndBin * dStep + dStep * 0.5, step);
										float ySpBot = SessionPriceToY(day, spZone.StartBin * dStep - dStep * 0.5, step);
										if (ySpTop > ySpBot)
										{
											float sw = ySpTop;
											ySpTop = ySpBot;
											ySpBot = sw;
										}
										float ySpMid = (ySpTop + ySpBot) * 0.5f;
										float spH = Math.Max(2.5f, ySpBot - ySpTop);

										float drawEndX;
										bool isSpVirgin = GetLevelExtensionX(allSessions, globalIdx, zoneMidPrice, xCollapsedRight, panelRight, out drawEndX);
										if (ySpBot >= panelTop && ySpTop <= panelBottom - 25f)
										{
											float drawStartX = Math.Max(panelLeft, xIb);
											if (drawEndX > drawStartX + 2f)
											{
												RectangleF spRayRect = new RectangleF(drawStartX, ySpTop, drawEndX - drawStartX, spH);
												RenderTarget.FillRectangle(spRayRect, spZone.IsBuying ? singleBuyZoneRayBrush : singleSellZoneRayBrush);
												RenderTarget.DrawLine(
													new Vector2(drawStartX, ySpMid),
													new Vector2(drawEndX, ySpMid),
													spZone.IsBuying ? singleBuyBorderBrush : singleSellBorderBrush,
													1.0f, dashedStroke);
											}

											if (isSpVirgin)
											{
												string spType = spZone.IsBuying ? "Buying SP" : "Selling SP";
												rightEdgeTags.Add(new RightEdgeTag
												{
													Price = zoneMidPrice,
													IdealY = ySpMid - 7.5f,
													Height = 15f,
													Text = spType + " (" + day.SessionName + " " + FormatPrice(zoneMidPrice) + ")",
													Brush = spZone.IsBuying ? greenBrush : redBrush,
													Priority = 2
												});
											}
										}
									}
								}

								// 2. Extending High/Low Levels (ABPoorHigh, PoorHigh, SwingHigh, ABPoorLow, PoorLow, SwingLow) until broken
								PriceRow topRow = day.Rows[maxBin];
								bool isAbPoorHigh = topRow.Periods.Contains(0) && topRow.Periods.Contains(1);
								bool isPoorHigh = !isAbPoorHigh && topRow.Periods.Count >= 2;
								string highLevelName = isAbPoorHigh ? "ABPoorHigh" : (isPoorHigh ? "PoorHigh" : "SwingHigh");
								DxBrush highLevelBrush = isAbPoorHigh ? abPoorBrush : (isPoorHigh ? cyanBrush : swingLineBrush);
								StrokeStyle highLevelStroke = (isAbPoorHigh || isPoorHigh) ? dashedStroke : null;
								bool enableThisHigh = ShowPoorHighLow && ((isAbPoorHigh || isPoorHigh) ? ShowPoorHigh : ShowSwingHigh);

								PriceRow botRow = day.Rows[minBin];
								bool isAbPoorLow = botRow.Periods.Contains(0) && botRow.Periods.Contains(1);
								bool isPoorLow = !isAbPoorLow && botRow.Periods.Count >= 2;
								string lowLevelName = isAbPoorLow ? "ABPoorLow" : (isPoorLow ? "PoorLow" : "SwingLow");
								DxBrush lowLevelBrush = isAbPoorLow ? abPoorBrush : (isPoorLow ? cyanBrush : swingLineBrush);
								StrokeStyle lowLevelStroke = (isAbPoorLow || isPoorLow) ? dashedStroke : null;
								bool enableThisLow = ShowPoorHighLow && ((isAbPoorLow || isPoorLow) ? ShowPoorLow : ShowSwingLow);

								if (enableThisHigh && profileHighY >= panelTop && profileHighY <= panelBottom - 25f)
								{
									float drawEndX;
									bool isHighVirgin = GetLevelExtensionX(allSessions, globalIdx, day.High, xCollapsedRight, panelRight, out drawEndX, isHighExtreme: true);
									float drawStartX = Math.Max(panelLeft, xIb);
									if (highLevelStroke != null)
										RenderTarget.DrawLine(new Vector2(drawStartX, profileHighY), new Vector2(drawEndX, profileHighY), highLevelBrush, 1.15f, highLevelStroke);
									else
										RenderTarget.DrawLine(new Vector2(drawStartX, profileHighY), new Vector2(drawEndX, profileHighY), highLevelBrush, 1.15f);

									if (isHighVirgin)
									{
										rightEdgeTags.Add(new RightEdgeTag
										{
											Price = day.High,
											IdealY = profileHighY - 7.5f,
											Height = 15f,
											Text = highLevelName + " (" + day.SessionName + " " + FormatPrice(day.High) + ")",
											Brush = highLevelBrush,
											Priority = 1
										});
									}
								}

								if (enableThisLow && profileLowY >= panelTop && profileLowY <= panelBottom - 25f)
								{
									float drawEndX;
									bool isLowVirgin = GetLevelExtensionX(allSessions, globalIdx, day.Low, xCollapsedRight, panelRight, out drawEndX, isLowExtreme: true);
									float drawStartX = Math.Max(panelLeft, xIb);
									if (lowLevelStroke != null)
										RenderTarget.DrawLine(new Vector2(drawStartX, profileLowY), new Vector2(drawEndX, profileLowY), lowLevelBrush, 1.15f, lowLevelStroke);
									else
										RenderTarget.DrawLine(new Vector2(drawStartX, profileLowY), new Vector2(drawEndX, profileLowY), lowLevelBrush, 1.15f);

									if (isLowVirgin)
									{
										rightEdgeTags.Add(new RightEdgeTag
										{
											Price = day.Low,
											IdealY = profileLowY - 7.5f,
											Height = 15f,
											Text = lowLevelName + " (" + day.SessionName + " " + FormatPrice(day.Low) + ")",
											Brush = lowLevelBrush,
											Priority = 1
										});
									}
								}

								// 3. Extending ValueAreaHigh (VAH) & ValueAreaLow (VAL) Lines (extends until broken; right-edge tag when Virgin)
								double vahPrice = day.VahBin * dStep;
								double valPrice = day.ValBin * dStep;
								float yVahLine = SessionPriceToY(day, day.VahBin * dStep + dStep * 0.5, step);
								float yValLine = SessionPriceToY(day, day.ValBin * dStep - dStep * 0.5, step);

								if (ShowVirginVAH && yVahLine >= panelTop && yVahLine <= panelBottom - 25f)
								{
									float vahDrawEndX;
									bool isVahVirgin = GetLevelExtensionX(allSessions, globalIdx, vahPrice, xCollapsedRight, panelRight, out vahDrawEndX);
									if (vahDrawEndX > xCollapsedRight + 1f)
									{
										RenderTarget.DrawLine(
											new Vector2(xCollapsedRight, yVahLine),
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

								if (ShowVirginVAL && yValLine >= panelTop && yValLine <= panelBottom - 25f)
								{
									float valDrawEndX;
									bool isValVirgin = GetLevelExtensionX(allSessions, globalIdx, valPrice, xCollapsedRight, panelRight, out valDrawEndX);
									if (valDrawEndX > xCollapsedRight + 1f)
									{
										RenderTarget.DrawLine(
											new Vector2(xCollapsedRight, yValLine),
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

								// 4. Extending VPoC & VPPoC Lines (extends until broken; right-edge tag when Virgin)
								double vpocPrice = day.VolumePocBin * dStep;
								double tpoPocPrice = day.PocBin * dStep;
								float yVpoc = SessionPriceToY(day, vpocPrice, step);
								float yTpoPoc = SessionPriceToY(day, tpoPocPrice, step);

								if (yVpoc >= panelTop && yVpoc <= panelBottom - 25f)
								{
									float vpocDrawEndX = xCollapsedRight;
									bool isVpocVirgin = false;
									if (ShowVirginVPoC)
										isVpocVirgin = GetLevelExtensionX(allSessions, globalIdx, vpocPrice, xCollapsedRight, panelRight, out vpocDrawEndX);
									RenderTarget.DrawLine(
										new Vector2(Math.Max(panelLeft, xIb), yVpoc),
										new Vector2(vpocDrawEndX, yVpoc),
										pocLineBrush, 1.3f);
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

								if (day.PocBin != day.VolumePocBin && yTpoPoc >= panelTop && yTpoPoc <= panelBottom - 25f)
								{
									float tpoDrawEndX = xCollapsedRight;
									bool isVppocVirgin = false;
									if (ShowVirginVPPoC)
										isVppocVirgin = GetLevelExtensionX(allSessions, globalIdx, tpoPocPrice, xCollapsedRight, panelRight, out tpoDrawEndX);
									RenderTarget.DrawLine(
										new Vector2(Math.Max(panelLeft, xIb), yTpoPoc),
										new Vector2(tpoDrawEndX, yTpoPoc),
										vppocLineBrush, 1.0f, dashedStroke);
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

								// 5. Session Midline & Extending Virgin Midline (extends until broken; right-edge tag when Virgin)
								double midPrice = (day.High + day.Low) * 0.5;
								float yMid = (profileHighY + profileLowY) * 0.5f;
								if (yMid >= panelTop && yMid <= panelBottom - 25f)
								{
									float midDrawEndX = xCollapsedRight;
									bool isMidVirgin = false;
									if (ShowVirginMidLine)
										isMidVirgin = GetLevelExtensionX(allSessions, globalIdx, midPrice, xCollapsedRight, panelRight, out midDrawEndX);
									RenderTarget.DrawLine(
										new Vector2(Math.Max(panelLeft, xIb), yMid),
										new Vector2(midDrawEndX, yMid),
										midLineBrush, 1.1f, dashedStroke);
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
						float fullBannerStripW = Math.Max(40f, (panelRight - 180f) - (panelLeft + 2f));
						RenderTarget.FillRectangle(new RectangleF(panelLeft + 2f, panelBottom - 23f, fullBannerStripW, 22f), dateBannerBgBrush);

						for (int g = 0; g < dateGroups.Count; g++)
						{
							DateColumnGroup group = dateGroups[g];
							if (group.RightX < panelLeft || group.LeftX > panelRight - 178f)
								continue;

							// Opaque Single-Line Bottom Date Banner (clamped to panelRight - 180f so it never collides with right-edge labels)
							float dateBannerY = panelBottom - 23f;
							float clampedBannerLeft = Math.Max(panelLeft + 4f, group.LeftX + 1f);
							float clampedBannerRight = Math.Min(panelRight - 180f, group.RightX - 1f);
							float bannerW = clampedBannerRight - clampedBannerLeft;
							if (bannerW > 54f)
							{
								RectangleF dateBannerRect = new RectangleF(clampedBannerLeft, dateBannerY, bannerW, 22f);
								RenderTarget.FillRectangle(dateBannerRect, dateBannerBgBrush);
								RenderTarget.DrawRectangle(dateBannerRect, cardBorderBrush, 1.0f);

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
									string fullSummary = sessionTpoParts.Count > 0
										? string.Format(CultureInfo.InvariantCulture, "   [ {0} Sessions: {1} — {2} TPOs ]",
											activeSessions.Length, string.Join(" | ", sessionTpoParts), totalDayTpos)
										: "   [ 3 Sessions: Asia (20 TPO) | London (11 TPO) | New York (15 TPO) — 46 TPOs ]";
									string shortSummary = string.Format(CultureInfo.InvariantCulture, "  [ {0} Sessions | {1} TPO ]",
										Math.Max(1, activeSessions.Length), totalDayTpos > 0 ? totalDayTpos : 46);

									string dateBannerText = bannerW >= 620f
										? ("Date: " + group.Date.ToString("dd MMM yyyy (ddd)", CultureInfo.InvariantCulture) + fullSummary)
										: (bannerW >= 260f
											? ("Date: " + group.Date.ToString("dd MMM yyyy (ddd)", CultureInfo.InvariantCulture) + shortSummary)
											: group.Date.ToString("dd MMM yyyy (ddd)", CultureInfo.InvariantCulture));
									float mergeDayBtnW = (ShowChartButtons && bannerW >= 240f) ? 92f : 0f;
									DrawText(dateBannerText, clampedBannerLeft + 4f, dateBannerY + 2f, bannerW - mergeDayBtnW - 8f, 18f, dateAxisFormat, whiteBrush);

									// Interactive [Merge Day] / [Unmerge Day] Button on the Date Banner
									List<DayProfile> rawForDate = rawSessions.Where(rs => rs.SessionDate.Date == group.Date.Date).ToList();
									if (ShowChartButtons && mergeDayBtnW > 0f && rawForDate.Count > 1)
									{
										List<string> dayRawKeys = rawForDate.Select(rs => rs.ProfileKey).ToList();
										bool isDayMerged = group.Sessions.Count == 1 && group.Sessions[0].IsMerged;
										string dayBtnText = isDayMerged ? "[Unmerge Day]" : "[Merge Day]";
										RectangleF dayBtnRect = new RectangleF(clampedBannerRight - mergeDayBtnW - 4f, dateBannerY + 3f, mergeDayBtnW, 16f);
										RenderTarget.FillRectangle(dayBtnRect, isDayMerged ? btnMergeBgBrush : btnBgBrush);
										RenderTarget.DrawRectangle(dayBtnRect, isDayMerged ? orangeBrush : cyanBrush, 0.9f);
										DrawText(dayBtnText, dayBtnRect.X, dayBtnRect.Y, dayBtnRect.Width, dayBtnRect.Height, tagCenterFormat, isDayMerged ? yellowBrush : cyanBrush);

										activeChartButtons.Add(new ChartButton
										{
											Rect = dayBtnRect,
											Action = ChartButtonAction.ToggleMergeDay,
											PrimaryKey = dayRawKeys[0],
											GroupKeys = dayRawKeys
										});
									}
								}
								else if (curMode == MarketProfilePeriodMode.Monthly)
								{
									DayProfile firstInGrp = group.Sessions.Count > 0 ? group.Sessions[0] : null;
									int expTpos = firstInGrp != null ? Math.Max(1, firstInGrp.ExpectedPeriodCount) : 22;
									int prnTpos = firstInGrp != null ? Math.Min(expTpos, Math.Max(firstInGrp.PeriodSlices.Count, firstInGrp.LastPeriod + 1)) : expTpos;
									string mShort = group.Date.ToString("MMM yyyy", CultureInfo.InvariantCulture);
									string mLong = group.Date.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
									string bannerSummary = bannerW >= 320f
										? string.Format(CultureInfo.InvariantCulture, "Month: {0}  [ Monthly Profile — {1}/{2} TPOs ]", mLong, prnTpos, expTpos)
										: (bannerW >= 150f
											? string.Format(CultureInfo.InvariantCulture, "{0}  [ {1}/{2} TPOs ]", mShort, prnTpos, expTpos)
											: mShort);
									DrawText(bannerSummary, clampedBannerLeft + 4f, dateBannerY + 2f, bannerW - 8f, 18f, dateAxisFormat, whiteBrush);
								}
								else
								{
									DayProfile firstInGrp = group.Sessions.Count > 0 ? group.Sessions[0] : null;
									int expTpos = firstInGrp != null ? Math.Max(1, firstInGrp.ExpectedPeriodCount) : 15;
									int prnTpos = firstInGrp != null ? Math.Min(expTpos, Math.Max(firstInGrp.PeriodSlices.Count, firstInGrp.LastPeriod + 1)) : expTpos;
									string wkTitle = firstInGrp != null && !string.IsNullOrEmpty(firstInGrp.SessionName)
										? firstInGrp.SessionName
										: ("Wk " + group.Date.ToString("dd MMM", CultureInfo.InvariantCulture));
									string bannerSummary = bannerW >= 320f
										? string.Format(CultureInfo.InvariantCulture, "{0}  [ Weekly Profile — {1}/{2} TPOs ]", wkTitle, prnTpos, expTpos)
										: (bannerW >= 165f
											? string.Format(CultureInfo.InvariantCulture, "{0}  [ {1}/{2} TPO ]", wkTitle, prnTpos, expTpos)
											: wkTitle);
									DrawText(bannerSummary, clampedBannerLeft + 4f, dateBannerY + 2f, bannerW - 8f, 18f, dateAxisFormat, whiteBrush);
								}
							}

							for (int sIdx = 0; sIdx < group.Sessions.Count; sIdx++)
							{
								DayProfile day = group.Sessions[sIdx];
								int globalIdx = allSessions.IndexOf(day);
								DayProfile prevSession = globalIdx > 0 ? allSessions[globalIdx - 1] : null;
								bool isLatestSession = (globalIdx == allSessions.Count - 1);
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

								// Include the '#' close marker in maxRowCells so '#' NEVER overlaps the last TPO letter!
								int maxRowCells = Math.Max(1, day.Rows.Max(kv => kv.Value.Periods.Count + (kv.Key == closeBin ? 1 : 0)));
								float baseCollapsedSlotW = Math.Max(224f, xCollapsedRight - xIb);
								float volWidth = ShowVolumeProfile ? 38f : 0f;
								float availTpoW = baseCollapsedSlotW - volWidth - 14f;
								float letterWidth = Math.Min(9.4f, Math.Max(8.2f, availTpoW / Math.Max(8, maxRowCells)));
								float collapsedTpoWidth = maxRowCells * letterWidth;

								float xTpo = xIb + 4f;
								float xVol = xTpo + collapsedTpoWidth + 4f;

								float yVahA = SessionPriceToY(day, vah * dStep + dStep * 0.5, step);
								float yValB = SessionPriceToY(day, val * dStep - dStep * 0.5, step);
								float yVaTop = Math.Min(yVahA, yValB);
								float yVaBot = Math.Max(yVahA, yValB);

								// 1. Shaded Value Area Box + Dashed Cyan Borders
								RenderTarget.FillRectangle(
									new RectangleF(xIb, yVaTop, Math.Max(4f, xCollapsedRight - xIb), Math.Max(2f, yVaBot - yVaTop)),
									vaFillBrush);
								RenderTarget.DrawLine(new Vector2(xIb, yVaTop), new Vector2(xCollapsedRight, yVaTop), vaBorderBrush, 1.0f, dashedStroke);
								RenderTarget.DrawLine(new Vector2(xIb, yVaBot), new Vector2(xCollapsedRight, yVaBot), vaBorderBrush, 1.0f, dashedStroke);

								float sampleRowYA = SessionPriceToY(day, poc * dStep + dStep * 0.5, step);
								float sampleRowYB = SessionPriceToY(day, poc * dStep - dStep * 0.5, step);
								float sampleRowH = Math.Max(7.5f, Math.Abs(sampleRowYB - sampleRowYA));
								float letterFontPx = Math.Min((float)Math.Max(10, FontSize), Math.Max(7.5f, sampleRowH * 0.90f));
								float splitLetterFontPx = Math.Min(9.8f, Math.Max(7.4f, sampleRowH * 0.86f));

								using (var letterFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, letterFontPx))
								using (var splitLetterFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, splitLetterFontPx))
								{
									letterFormat.TextAlignment = TextAlignment.Center;
									letterFormat.ParagraphAlignment = ParagraphAlignment.Center;
									letterFormat.WordWrapping = WordWrapping.NoWrap;

									splitLetterFormat.TextAlignment = TextAlignment.Center;
									splitLetterFormat.ParagraphAlignment = ParagraphAlignment.Center;
									splitLetterFormat.WordWrapping = WordWrapping.NoWrap;

									// 2. Draw Collapsed TPO Letters (All TPOs crisp, tall, and 100% readable on any scrolled date), Single Print Highlights, and Volume Profile
									foreach (KeyValuePair<int, PriceRow> item in day.Rows)
									{
										int bin = item.Key;
										PriceRow row = item.Value;
										float yA = SessionPriceToY(day, bin * dStep + dStep * 0.5, step);
										float yB = SessionPriceToY(day, bin * dStep - dStep * 0.5, step);
										float y = Math.Min(yA, yB);
										float h = Math.Max(7.5f, Math.Abs(yB - yA));
										if (y > panelBottom - 25f || y + h < panelTop)
											continue;

										// Highlight interior Buying Single Prints (Emerald Green) & Selling Single Prints (Crimson Red)
										// strictly inside day.SinglePrintZones (never on Buying Tail or Selling Tail rows)
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
											RectangleF spRect = new RectangleF(xTpo - 1f, y + 0.3f, collapsedTpoWidth + 2f, Math.Max(1.5f, h - 0.6f));
											RenderTarget.FillRectangle(spRect, isBuySp ? singleBuyRowBrush : singleSellRowBrush);
											RenderTarget.DrawLine(
												new Vector2(xTpo - 1f, y),
												new Vector2(xTpo - 1f, y + h),
												isBuySp ? singleBuyBorderBrush : singleSellBorderBrush,
												2.2f);
										}

										List<int> sortedPeriods = row.Periods.OrderBy(p => p).ToList();
										for (int colIdx = 0; colIdx < sortedPeriods.Count; colIdx++)
										{
											int period = sortedPeriods[colIdx];
											int safePeriod = Math.Max(0, Math.Min(period, LetterSequence.Length - 1));
											float lx = xTpo + colIdx * letterWidth;

											if (bin == openBin && colIdx == 0)
											{
												DrawText("O", lx, y, letterWidth, h, letterFormat, whiteBrush);
											}
											else
											{
												string letter = LetterSequence[safePeriod].ToString();
												DrawText(letter, lx, y, letterWidth, h, letterFormat, letterBrushes[safePeriod % letterBrushes.Length]);
											}
										}

										if (bin == closeBin)
										{
											float hashX = xTpo + sortedPeriods.Count * letterWidth;
											DrawText("#", hashX, y, letterWidth, h, letterFormat, redBrush);
										}

										if (ShowVolumeProfile && volWidth > 0)
										{
											float barW = (float)(volWidth * (row.Volume / maxVolume));
											bool inVolVa = bin >= volumeVal && bin <= volumeVah;
											DxBrush volBrush = (bin == volumePoc)
												? volumePocBrush
												: (inVolVa ? volumeVaBrush : volumeOutBrush);
											RenderTarget.FillRectangle(
												new RectangleF(xVol + 1f, y + 0.5f, Math.Max(1.5f, barW), Math.Max(1.0f, h - 1.0f)),
												volBrush);
										}
									}

									// Inline Buying / Selling Single Print Badges placed strictly in empty space to the right of TPO letters
									if (ShowSinglePrints && day.PeriodSlices.Count >= 6)
									{
										for (int z = 0; z < day.SinglePrintZones.Count; z++)
										{
											SinglePrintZone spz = day.SinglePrintZones[z];
											int midBin = (spz.StartBin + spz.EndBin) / 2;
											int maxNearCells = 1;
											for (int nb = midBin - 1; nb <= midBin + 1; nb++)
											{
												PriceRow nr;
												if (day.Rows.TryGetValue(nb, out nr))
													maxNearCells = Math.Max(maxNearCells, nr.Periods.Count + (nb == closeBin ? 1 : 0));
											}
											double zMidPrice = ((spz.StartBin + spz.EndBin) * 0.5) * dStep;
											float yZ = SessionPriceToY(day, zMidPrice, step) - 5.5f;
											if (yZ >= profileHighY + 8f && yZ <= profileLowY - 18f)
											{
												float spTagW = 48f;
												float spTagX = xVol - spTagW - 3f;
												if (spTagX > xTpo + maxNearCells * letterWidth + 8f)
												{
													string spLabel = spz.IsBuying ? "Buy SP" : "Sell SP";
													RectangleF spPill = new RectangleF(spTagX, yZ, spTagW, 11.5f);
													RenderTarget.FillRectangle(spPill, pillBgBrush);
													RenderTarget.DrawRectangle(spPill, spz.IsBuying ? singleBuyBorderBrush : singleSellBorderBrush, 0.8f);
													DrawText(spLabel, spTagX + 3f, yZ, spTagW - 5f, 11.5f, spTagFormat, spz.IsBuying ? greenBrush : redBrush);
												}
											}
										}
									}

									if (ShowVolumeProfile && volWidth > 0)
									{
										RenderTarget.DrawLine(
											new Vector2(xVol, profileHighY),
											new Vector2(xVol, profileLowY),
											spineLineBrush, 1.0f);
									}

									// 3. Green Initial Balance (IB) vertical bar on the left spine
									if (ShowInitialBalance && day.InitialBalanceHigh != double.MinValue && day.InitialBalanceLow != double.MaxValue)
									{
										float yIbH = SessionPriceToY(day, day.InitialBalanceHigh, step);
										float yIbL = SessionPriceToY(day, day.InitialBalanceLow, step);
										RenderTarget.DrawLine(new Vector2(xIb, yIbH), new Vector2(xIb, yIbL), ibBarBrush, 2.4f);
									}

									// Precompute Top Level Pill & Session Header Y coordinates so Split Profile header aligns cleanly above SwingHigh
									PriceRow topRow = day.Rows[maxBin];
									bool isAbPoorHigh = topRow.Periods.Contains(0) && topRow.Periods.Contains(1);
									bool isPoorHigh = !isAbPoorHigh && topRow.Periods.Count >= 2;
									bool enableThisHigh = ShowPoorHighLow && ((isAbPoorHigh || isPoorHigh) ? ShowPoorHigh : ShowSwingHigh);
									string highLevelName = isAbPoorHigh ? "ABPoorHigh" : (isPoorHigh ? "PoorHigh" : "SwingHigh");
									DxBrush highLevelBrush = isAbPoorHigh ? abPoorBrush : (isPoorHigh ? cyanBrush : swingLineBrush);

									float topTagY = profileHighY - 17f;
									float headerY = Math.Max(panelTop + 6f, (enableThisHigh ? topTagY - 36f : profileHighY - 38f));
									float btnRowY = headerY + 17f;

									// 4. Split / Expanded TPO & Volume Profile for ANY Selected Session (zero text overlap with SwingHigh or right-edge tags)
									if (day.IsSplitExpanded && day.PeriodSlices.Count > 0)
									{
										float xSplitStart = xCollapsedRight + 12f;
										float xSplitEnd = Math.Min(xSlotRight - 6f, panelRight - 178f);
										if (xSplitEnd > xSplitStart + 65f)
										{
											float splitAvailW = xSplitEnd - xSplitStart;
											List<PeriodSlice> orderedSlices = day.PeriodSlices.Values.OrderBy(ps => ps.PeriodIndex).ToList();
											int sliceCount = Math.Max(1, orderedSlices.Count);
											int volBarCount = ShowVolumeProfile ? Math.Min(3, orderedSlices.Count) : 0;
											float singleVolW = 28f;
											float volColGap = 6f;
											float totalSplitVolW = volBarCount > 0
												? (volBarCount * singleVolW + (volBarCount - 1) * volColGap)
												: 0f;
											float splitColW = Math.Min(10.0f, Math.Max(8.5f, (splitAvailW - totalSplitVolW - 16f) / Math.Max(5, sliceCount + 1)));
											float xSplitVol = Math.Min(xSplitEnd - totalSplitVolW - 2f, xSplitStart + 6f + (sliceCount + 1) * splitColW + 6f);

											// Split Profile Header Banner aligned at headerY (well ABOVE SwingHigh and profileHighY!)
											float splitHeaderW = Math.Min(164f, Math.Max(118f, splitAvailW));
											RectangleF splitHdrPill = new RectangleF(xSplitStart, headerY, splitHeaderW, 16f);
											RenderTarget.FillRectangle(splitHdrPill, btnActiveBgBrush);
											RenderTarget.DrawRectangle(splitHdrPill, cyanBrush, 0.8f);
											DrawText("SPLIT PROFILE (" + day.SessionName + ")", xSplitStart + 4f, headerY, splitHeaderW - 8f, 16f, spTagFormat, cyanBrush);

											RenderTarget.FillRectangle(
												new RectangleF(xSplitStart, yVaTop, Math.Max(10f, xSplitEnd - xSplitStart), Math.Max(2f, yVaBot - yVaTop)),
												vaFillBrush);
											RenderTarget.DrawLine(new Vector2(xSplitStart, yVaTop), new Vector2(xSplitEnd, yVaTop), vaBorderBrush, 1.0f, dashedStroke);
											RenderTarget.DrawLine(new Vector2(xSplitStart, yVaBot), new Vector2(xSplitEnd, yVaBot), vaBorderBrush, 1.0f, dashedStroke);

											if (ShowInitialBalance && day.InitialBalanceHigh != double.MinValue && day.InitialBalanceLow != double.MaxValue)
											{
												float yIbH = SessionPriceToY(day, day.InitialBalanceHigh, step);
												float yIbL = SessionPriceToY(day, day.InitialBalanceLow, step);
												RenderTarget.DrawLine(new Vector2(xSplitStart, yIbH), new Vector2(xSplitStart, yIbL), ibBarBrush, 2.2f);
											}

											for (int cIdx = 0; cIdx < orderedSlices.Count; cIdx++)
											{
												PeriodSlice slice = orderedSlices[cIdx];
												int pIdx = Math.Max(0, Math.Min(slice.PeriodIndex, LetterSequence.Length - 1));
												string pLetter = LetterSequence[pIdx].ToString();
												float cx = xSplitStart + 4f + cIdx * splitColW;
												if (cx + splitColW > xSplitVol - 2f)
													break;

												// Draw Period Column Header Letter at btnRowY above the Split Profile
												DrawText(pLetter, cx, btnRowY, splitColW, 14f, splitLetterFormat, letterBrushes[pIdx % letterBrushes.Length]);

												int sMinBin = PriceToBin(slice.Low, dStep);
												int sMaxBin = PriceToBin(slice.High, dStep);
												int sOpenBin = PriceToBin(slice.Open, dStep);
												int sCloseBin = PriceToBin(slice.Close, dStep);
												int sMidBin = (sMinBin + sMaxBin) / 2;

												for (int b = sMinBin; b <= sMaxBin; b++)
												{
													float yA = SessionPriceToY(day, b * dStep + dStep * 0.5, step);
													float yB = SessionPriceToY(day, b * dStep - dStep * 0.5, step);
													float y = Math.Min(yA, yB);
													float h = Math.Max(7.5f, Math.Abs(yB - yA));
													if (y > panelBottom - 25f || y + h < panelTop)
														continue;

													// Only the Open letter is colored white; Mid-candle letter is highlighted with a white underline
													DxBrush lBrush = (b == sOpenBin)
														? whiteBrush
														: letterBrushes[pIdx % letterBrushes.Length];
													DrawText(pLetter, cx, y, splitColW, h, splitLetterFormat, lBrush);

													if (b == sMidBin)
													{
														float ulY = y + h - 1.2f;
														RenderTarget.DrawLine(
															new Vector2(cx + 1.0f, ulY),
															new Vector2(cx + splitColW - 1.0f, ulY),
															whiteBrush,
															1.6f);
													}
												}

												if (cIdx == orderedSlices.Count - 1 && cx + splitColW * 2f <= xSplitVol)
												{
													float yCloseA = SessionPriceToY(day, sCloseBin * dStep + dStep * 0.5, step);
													float yCloseB = SessionPriceToY(day, sCloseBin * dStep - dStep * 0.5, step);
													float yClose = Math.Min(yCloseA, yCloseB);
													float hClose = Math.Max(7.5f, Math.Abs(yCloseB - yCloseA));
													DrawText("#", cx + splitColW, yClose, splitColW, hClose, splitLetterFormat, redBrush);
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

													RenderTarget.DrawLine(
														new Vector2(vx, Math.Max(panelTop, ySliceHigh)),
														new Vector2(vx, Math.Min(panelBottom - 25f, ySliceLow)),
														spineLineBrush, 0.85f);

													for (int b = vMinBin; b <= vMaxBin; b++)
													{
														float yA = SessionPriceToY(day, b * dStep + dStep * 0.5, step);
														float yB = SessionPriceToY(day, b * dStep - dStep * 0.5, step);
														float y = Math.Min(yA, yB);
														float h = Math.Max(7.5f, Math.Abs(yB - yA));
														if (y > panelBottom - 25f || y + h < panelTop)
															continue;

														double bVol;
														if (!sliceBinVols.TryGetValue(b, out bVol))
															bVol = 0;

														float barW = (float)(singleVolW * (bVol / maxSliceVol));
														bool inSliceVa = b >= sliceVolVal && b <= sliceVolVah;
														DxBrush sBrush = (b == sliceVolPoc)
															? splitVolPocBrush
															: (inSliceVa ? splitVolVaBrush : splitVolOutBrush);
														RenderTarget.FillRectangle(
															new RectangleF(vx + 1f, y + 0.5f, Math.Max(2.0f, barW), Math.Max(1.0f, h - 1.0f)),
															sBrush);
													}

													// Show which TPO Volume Bar this is in a fixed boxed badge at the top row (btnRowY)
													float badgeW = Math.Min(singleVolW, 20f);
													RectangleF vBadgeRect = new RectangleF(vx, btnRowY, badgeW, 14f);
													RenderTarget.FillRectangle(vBadgeRect, pillBgBrush);
													RenderTarget.DrawRectangle(vBadgeRect, vpColorBrush, 0.9f);
													DrawText(vpLetter, vx, btnRowY, badgeW, 14f, tagCenterFormat, vpColorBrush);
												}
											}
										}
									}

									// 5. Opaque Top Level Pill (ABPoorHigh / PoorHigh / SwingHigh) & Session Header Pill + Interactive [Split] / [Merge] Buttons
									if (enableThisHigh)
									{
										float tagW = 84f;
										float tagX = (xIb + xCollapsedRight) * 0.5f - tagW * 0.5f;
										RectangleF tagPill = new RectangleF(tagX, topTagY, tagW, 15f);
										RenderTarget.FillRectangle(tagPill, pillBgBrush);
										RenderTarget.DrawRectangle(tagPill, cardBorderBrush, 0.8f);
										DrawText(highLevelName, tagX, topTagY, tagW, 15f, tagCenterFormat, highLevelBrush);
									}

									int sessionColorIdx = Math.Max(0, Math.Min(day.SessionId, sessionBrushes.Length - 1));
									int expectedTpos = Math.Max(1, day.ExpectedPeriodCount);
									int printedTpos = Math.Min(expectedTpos, Math.Max(day.PeriodSlices.Count, day.LastPeriod + 1));
									bool isSessionLive = isLatestSession && printedTpos < expectedTpos;
									string tpoStatus = isSessionLive
										? string.Format(CultureInfo.InvariantCulture, "LIVE {0}/{1} TPO", printedTpos, expectedTpos)
										: string.Format(CultureInfo.InvariantCulture, "{0}/{1} TPO", printedTpos, expectedTpos);
									string sessionHeader = EffectiveProfileMode == MarketProfilePeriodMode.Daily
										? string.Format(CultureInfo.InvariantCulture, "{0} ({1}) - {2}",
											day.SessionName,
											day.SessionDate.ToString("dd MMM", CultureInfo.InvariantCulture),
											tpoStatus)
										: string.Format(CultureInfo.InvariantCulture, "{0} - {1}",
											day.SessionName,
											tpoStatus);

									float headerW = Math.Min(xCollapsedRight - xIb + 2f, 248f);
									RectangleF headerPill = new RectangleF(xIb - 1f, headerY, headerW, 16f);
									RenderTarget.FillRectangle(headerPill, pillBgBrush);
									RenderTarget.DrawRectangle(headerPill, cardBorderBrush, 0.8f);
									DrawText(sessionHeader, xIb + 3f, headerY, headerW - 6f, 16f, headerFormat, sessionBrushes[sessionColorIdx]);

									// Action Row right below Session Header: [Split] button + [Merge >] / [Unmerge] buttons (controlled by ShowChartButtons)
									if (ShowChartButtons)
									{
										float curBtnX = xIb - 1f;

										// [Split] / [Split ON] button
										float splitBtnW = 58f;
										RectangleF splitBtnRect = new RectangleF(curBtnX, btnRowY, splitBtnW, 14f);
										RenderTarget.FillRectangle(splitBtnRect, day.IsSplitExpanded ? btnActiveBgBrush : btnBgBrush);
										RenderTarget.DrawRectangle(splitBtnRect, day.IsSplitExpanded ? cyanBrush : cardBorderBrush, 0.8f);
										DrawText(day.IsSplitExpanded ? "[Split ON]" : "[Split]", splitBtnRect.X, splitBtnRect.Y, splitBtnRect.Width, splitBtnRect.Height, tagCenterFormat, day.IsSplitExpanded ? cyanBrush : textBrush);
										activeChartButtons.Add(new ChartButton
										{
											Rect = splitBtnRect,
											Action = ChartButtonAction.ToggleSplit,
											PrimaryKey = day.IsSplitExpanded ? (day.ProfileKey + "|ON") : day.ProfileKey
										});
										curBtnX += splitBtnW + 4f;

										// [Merge >] button (if there is a next session to merge with)
										if (globalIdx < allSessions.Count - 1)
										{
											float mergeBtnW = 58f;
											RectangleF mergeBtnRect = new RectangleF(curBtnX, btnRowY, mergeBtnW, 14f);
											RenderTarget.FillRectangle(mergeBtnRect, btnBgBrush);
											RenderTarget.DrawRectangle(mergeBtnRect, cardBorderBrush, 0.8f);
											DrawText("[Merge >]", mergeBtnRect.X, mergeBtnRect.Y, mergeBtnRect.Width, mergeBtnRect.Height, tagCenterFormat, yellowBrush);
											activeChartButtons.Add(new ChartButton
											{
												Rect = mergeBtnRect,
												Action = ChartButtonAction.MergeWithNext,
												PrimaryKey = day.ProfileKey,
												GroupKeys = new List<string>(day.SourceProfileKeys)
											});
											curBtnX += mergeBtnW + 4f;
										}

										// [Unmerge] button (if this profile is currently merged from 2+ sessions)
										if (day.IsMerged && day.SourceProfileKeys.Count > 1)
										{
											float unmergeBtnW = 60f;
											RectangleF unmergeBtnRect = new RectangleF(curBtnX, btnRowY, unmergeBtnW, 14f);
											RenderTarget.FillRectangle(unmergeBtnRect, btnMergeBgBrush);
											RenderTarget.DrawRectangle(unmergeBtnRect, orangeBrush, 0.8f);
											DrawText("[Unmerge]", unmergeBtnRect.X, unmergeBtnRect.Y, unmergeBtnRect.Width, unmergeBtnRect.Height, tagCenterFormat, orangeBrush);
											activeChartButtons.Add(new ChartButton
											{
												Rect = unmergeBtnRect,
												Action = ChartButtonAction.UnmergeGroup,
												PrimaryKey = day.ProfileKey,
												GroupKeys = new List<string>(day.SourceProfileKeys)
											});
										}
									}

									// 6. Opaque Bottom Level Pill (ABPoorLow / PoorLow / SwingLow)
									PriceRow botRow = day.Rows[minBin];
									bool isAbPoorLow = botRow.Periods.Contains(0) && botRow.Periods.Contains(1);
									bool isPoorLow = !isAbPoorLow && botRow.Periods.Count >= 2;
									bool enableThisLow = ShowPoorHighLow && ((isAbPoorLow || isPoorLow) ? ShowPoorLow : ShowSwingLow);
									string lowLevelName = isAbPoorLow ? "ABPoorLow" : (isPoorLow ? "PoorLow" : "SwingLow");
									DxBrush lowLevelBrush = isAbPoorLow ? abPoorBrush : (isPoorLow ? cyanBrush : swingLineBrush);

									float botTagY = profileLowY + 3f;
									if (enableThisLow)
									{
										float tagW = 84f;
										float tagX = (xIb + xCollapsedRight) * 0.5f - tagW * 0.5f;
										RectangleF tagPill = new RectangleF(tagX, botTagY, tagW, 15f);
										RenderTarget.FillRectangle(tagPill, pillBgBrush);
										RenderTarget.DrawRectangle(tagPill, cardBorderBrush, 0.8f);
										DrawText(lowLevelName, tagX, botTagY, tagW, 15f, tagCenterFormat, lowLevelBrush);
									}

									// 7. Bottom 9-Line 50Charts Summary Card — Fixed at Bottom Row (above Date bar) or below profile, with Enable/Disable & Min/Max
									if (ShowSummary)
									{
										bool sumMinimized = IsSummaryBoxMinimized;
										float summaryCardH = sumMinimized ? (2f * 12.2f + 8f) : (9f * 12.2f + 8f);
										float fixedBottomTopY = dateBannerY - summaryCardH - 4f;
										float floatingTopY = enableThisLow ? (botTagY + 18f) : (profileLowY + 8f);
										float summaryStartTopY = FixSummaryBoxAtBottom ? fixedBottomTopY : floatingTopY;
										float summaryCardW = Math.Min(234f, Math.Max(195f, slotWidth - 12f));

										Draw50ChartsSummaryCard(
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
						RenderDeoverlappedRightEdgeTags(rightEdgeTags, step, panelRight, panelTop + 82f, panelBottom - 28f, pillBgBrush, cardBorderBrush, labelRightFormat);

						// 9. Top-Left 4-Line 50Charts Info Panel with Logo & Min/Max button (controlled by ShowTopLeftBox)
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
					for (int i = 0; i < letterBrushes.Length; i++)
						if (letterBrushes[i] != null)
							letterBrushes[i].Dispose();
					for (int i = 0; i < sessionBrushes.Length; i++)
						if (sessionBrushes[i] != null)
							sessionBrushes[i].Dispose();
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

			float boxWidth = 178f;
			float boxLeft = panelRight - boxWidth - 4f;
			for (int i = 0; i < tags.Count; i++)
			{
				RightEdgeTag t = tags[i];
				if (t.DrawY < minY - 2f || t.DrawY + t.Height > maxY + 2f)
					continue;

				RectangleF bgRect = new RectangleF(boxLeft, t.DrawY, boxWidth, t.Height);
				RenderTarget.FillRectangle(bgRect, bgBrush);
				RenderTarget.DrawRectangle(bgRect, borderBrush, 0.7f);
				DrawText(t.Text, boxLeft + 2f, t.DrawY, boxWidth - 5f, t.Height, rightFormat, t.Brush);
			}
		}

		private void Draw50ChartsSummaryCard(
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

			RectangleF cardRect = new RectangleF(xLeft, boxTop, cardWidth, boxHeight);
			RenderTarget.FillRectangle(cardRect, bgBrush);
			RenderTarget.DrawRectangle(cardRect, borderBrush, 1.0f);

			// Top-right [-] / [+] Min/Max Toggle Button inside each Summary Box
			float minMaxBtnW = 24f;
			float minMaxBtnH = 13f;
			RectangleF minMaxBtnRect = new RectangleF(xLeft + cardWidth - minMaxBtnW - 3f, boxTop + 3f, minMaxBtnW, minMaxBtnH);
			RenderTarget.FillRectangle(minMaxBtnRect, btnBgBrush);
			RenderTarget.DrawRectangle(minMaxBtnRect, cyanBrush, 0.8f);
			DrawText(isMin ? "[+]" : "[-]", minMaxBtnRect.X, minMaxBtnRect.Y, minMaxBtnRect.Width, minMaxBtnRect.Height, btnFormat, cyanBrush);
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
			float logoSize = isMin ? 22f : 56f;
			float logoPadLeft = hasLogo ? (logoSize + 12f) : 0f;

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
				Tuple.Create("Market Profile: ", textBrush),
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
			float navBtnW = 48f;
			float btnH = 15.0f;
			float navGroupW = navBtnW * 3f + 8f;
			float maxBoxW = Math.Max(480f, (float)ChartPanel.W - 208f);
			float rightRow1W = isMin ? (navGroupW + 6f + btnW) : btnW;
			if (6f + logoPadLeft + line1Width + 14f + rightRow1W + 8f > maxBoxW)
			{
				string shortMode = TpoSize <= 0 ? " (Auto)" : " (Man)";
				line1Segments = new Tuple<string, DxBrush>[]
				{
					Tuple.Create("MP: ", textBrush),
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

			RectangleF rect = new RectangleF(boxX, boxY, boxW, boxH);
			RenderTarget.FillRectangle(rect, bgBrush);
			RenderTarget.DrawRectangle(rect, borderBrush, 1.0f);

			// Render Logo on the left side of the Top-Left Info Box
			if (hasLogo)
			{
				float logoX = boxX + 6f;
				float logoY = boxY + (boxH - logoSize) * 0.5f;
				RectangleF logoRect = new RectangleF(logoX, logoY, logoSize, logoSize);
				RenderTarget.DrawBitmap(logoBmp, logoRect, 1.0f, BitmapInterpolationMode.Linear);

				// Subtle vertical divider between logo and info text
				float divX = boxX + logoSize + 10f;
				RenderTarget.DrawLine(new Vector2(divX, boxY + 4f), new Vector2(divX, boxY + boxH - 4f), borderBrush, 0.8f);
			}

			// Top-right [-] / [+] interactive toggle button
			RectangleF minMaxBtnRect = new RectangleF((float)Math.Floor(boxX + boxW - btnW - 6f), (float)Math.Floor(boxY + 4f), btnW, btnH);
			DrawCrispButton(minMaxBtnRect, btnBgBrush, cyanBrush, isMin ? "[+]" : "[-]", btnFormat, cyanBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = minMaxBtnRect,
				Action = ChartButtonAction.ToggleTopLeftMinMax,
				PrimaryKey = "TopLeftInfoBox"
			});

			// Interactive < Past, Next >, Live >> navigation buttons (with crisp 4-sided borders including solid upper line)
			float navRowY = isMin ? (float)Math.Floor(boxY + 4f) : (float)Math.Floor(boxY + 22f);
			float navRightEdge = isMin ? (minMaxBtnRect.X - 6f) : (boxX + boxW - 6f);

			RectangleF liveBtnRect = new RectangleF((float)Math.Floor(navRightEdge - navBtnW), navRowY, navBtnW, btnH);
			DrawCrispButton(liveBtnRect, btnBgBrush, greenBrush, "Live >>", btnFormat, greenBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = liveBtnRect,
				Action = ChartButtonAction.ScrollLiveDay,
				PrimaryKey = "ScrollLive"
			});

			RectangleF nextBtnRect = new RectangleF((float)Math.Floor(liveBtnRect.X - navBtnW - 4f), navRowY, navBtnW, btnH);
			DrawCrispButton(nextBtnRect, btnBgBrush, cyanBrush, "Next >", btnFormat, cyanBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = nextBtnRect,
				Action = ChartButtonAction.ScrollNextDays,
				PrimaryKey = "ScrollNext"
			});

			RectangleF pastBtnRect = new RectangleF((float)Math.Floor(nextBtnRect.X - navBtnW - 4f), navRowY, navBtnW, btnH);
			DrawCrispButton(pastBtnRect, btnBgBrush, orangeBrush, "< Past", btnFormat, orangeBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = pastBtnRect,
				Action = ChartButtonAction.ScrollPastDays,
				PrimaryKey = "ScrollPast"
			});

			float textX = boxX + 6f + logoPadLeft;
			float line1ClipRight = isMin ? (pastBtnRect.X - 6f) : (minMaxBtnRect.X - 6f);
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

			float line2MaxW = Math.Max(40f, pastBtnRect.X - textX - 8f);
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

			float dropW = 112f;
			float dropH = 22f;
			float labelW = 64f;
			float totalW = labelW + dropW + 14f;
			float startX = panelRight - totalW - 8f;
			float topY = panelTop + 8f;

			RectangleF barRect = new RectangleF(startX, topY - 2f, totalW, dropH + 4f);
			RenderTarget.FillRectangle(barRect, bgBrush);
			RenderTarget.DrawRectangle(barRect, borderBrush, 0.9f);

			DrawText("PROFILE", startX + 6f, topY, labelW, dropH, headerFormat, orangeBrush);

			float boxX = startX + labelW + 4f;
			RectangleF dropBoxRect = new RectangleF(boxX, topY, dropW, dropH);
			RenderTarget.FillRectangle(dropBoxRect, isProfileDropdownOpen ? btnActiveBgBrush : btnBgBrush);
			RenderTarget.DrawRectangle(dropBoxRect, isProfileDropdownOpen ? cyanBrush : redBrush, 1.3f);

			DrawText(modeText, boxX + 8f, topY, dropW - 28f, dropH, headerFormat, whiteBrush);

			float arrowBoxW = 20f;
			RectangleF arrowRect = new RectangleF(boxX + dropW - arrowBoxW - 2f, topY + 2f, arrowBoxW, dropH - 4f);
			RenderTarget.FillRectangle(arrowRect, bgBrush);
			RenderTarget.DrawRectangle(arrowRect, isProfileDropdownOpen ? cyanBrush : redBrush, 0.9f);
			DrawText(isProfileDropdownOpen ? "▲" : "▼", arrowRect.X, arrowRect.Y, arrowRect.Width, arrowRect.Height, centerFormat, isProfileDropdownOpen ? cyanBrush : orangeBrush);

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
				RenderTarget.FillRectangle(menuRect, bgBrush);
				RenderTarget.DrawRectangle(menuRect, cyanBrush, 1.2f);

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
					DrawCrispButton(itemRect, isSelected ? btnActiveBgBrush : btnBgBrush, isSelected ? cyanBrush : borderBrush, string.Empty, headerFormat, whiteBrush);

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

		[NinjaScriptProperty]
		[Range(1, 240)]
		[Display(Name = "TPO interval (minutes)", GroupName = "Market Profile", Order = 1)]
		public int TpoIntervalMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(50, 90)]
		[Display(Name = "Value area (%)", GroupName = "Market Profile", Order = 2)]
		public int ValueAreaPercent { get; set; }

		[NinjaScriptProperty]
		[Range(1, 240)]
		[Display(Name = "Initial balance (minutes)", GroupName = "Market Profile", Order = 3)]
		public int InitialBalanceMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(1, 365)]
		[Display(Name = "Days to display", GroupName = "Market Profile", Order = 4)]
		public int DaysToDisplay { get; set; }

		[Display(Name = "Profile Mode (Daily / Weekly / Monthly)", Description = "Select Daily (default), Weekly, or Monthly Market Profile distribution (can also switch live via the top-right PROFILE dropdown on chart)", GroupName = "Market Profile", Order = 5)]
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

		// Virgin Lines & Levels (Strict Virgin-Only Extensions with Individual Enable/Disable Controls)
		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin PoorHigh / ABPoorHigh", Description = "Show and extend Virgin PoorHigh and ABPoorHigh lines until broken", GroupName = "Virgin Lines & Levels", Order = 0)]
		public bool ShowPoorHigh { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin PoorLow / ABPoorLow", Description = "Show and extend Virgin PoorLow and ABPoorLow lines until broken", GroupName = "Virgin Lines & Levels", Order = 1)]
		public bool ShowPoorLow { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin SwingHigh", Description = "Show and extend Virgin SwingHigh lines until broken", GroupName = "Virgin Lines & Levels", Order = 2)]
		public bool ShowSwingHigh { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin SwingLow", Description = "Show and extend Virgin SwingLow lines until broken", GroupName = "Virgin Lines & Levels", Order = 3)]
		public bool ShowSwingLow { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin ValueAreaHigh (with Session)", Description = "Show and extend only Virgin ValueAreaHigh lines with session name until broken", GroupName = "Virgin Lines & Levels", Order = 4)]
		public bool ShowVirginVAH { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin ValueAreaLow (with Session)", Description = "Show and extend only Virgin ValueAreaLow lines with session name until broken", GroupName = "Virgin Lines & Levels", Order = 5)]
		public bool ShowVirginVAL { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin VPoC", Description = "Show and extend only Virgin Volume PoC (VPoC) lines until broken", GroupName = "Virgin Lines & Levels", Order = 6)]
		public bool ShowVirginVPoC { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin VPPoC (TPO PoC)", Description = "Show and extend only Virgin TPO PoC (VPPoC) lines until broken", GroupName = "Virgin Lines & Levels", Order = 7)]
		public bool ShowVirginVPPoC { get; set; }

		[Display(Name = "Enable Virgin MidLine", Description = "Show and extend Virgin Session Midline across subsequent sessions until broken", GroupName = "Virgin Lines & Levels", Order = 8)]
		public bool ShowVirginMidLine { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Bottom Summary Boxes", Description = "Enable or disable the 9-line session summary boxes at the bottom of the chart", GroupName = "Display", Order = 0)]
		public bool ShowSummary { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fix Summary Boxes at Bottom", Description = "When enabled, docks all session summary boxes in a fixed horizontal row at the bottom above the Date bar", GroupName = "Display", Order = 1)]
		public bool FixSummaryBoxAtBottom { get; set; }

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
		private MarketProfile50Charts[] cacheMarketProfile50Charts;
		public MarketProfile50Charts MarketProfile50Charts(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool showAsia, string asiaFromTime, string asiaToTime, bool showLondon, string londonFromTime, string londonToTime, bool showNewYork, string newYorkFromTime, string newYorkToTime, int marketOpen, int marketClose, string sessionLabel, bool showIndianSession, bool splitLatestSession, bool splitAsiaSessions, bool splitLondonSessions, bool splitNewYorkSessions, bool mergeAsiaAndLondon, bool mergeLondonAndNewYork, bool mergeAllDaySessions, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize)
		{
			return MarketProfile50Charts(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, showAsia, asiaFromTime, asiaToTime, showLondon, londonFromTime, londonToTime, showNewYork, newYorkFromTime, newYorkToTime, marketOpen, marketClose, sessionLabel, showIndianSession, splitLatestSession, splitAsiaSessions, splitLondonSessions, splitNewYorkSessions, mergeAsiaAndLondon, mergeLondonAndNewYork, mergeAllDaySessions, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize);
		}

		public MarketProfile50Charts MarketProfile50Charts(ISeries<double> input, double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool showAsia, string asiaFromTime, string asiaToTime, bool showLondon, string londonFromTime, string londonToTime, bool showNewYork, string newYorkFromTime, string newYorkToTime, int marketOpen, int marketClose, string sessionLabel, bool showIndianSession, bool splitLatestSession, bool splitAsiaSessions, bool splitLondonSessions, bool splitNewYorkSessions, bool mergeAsiaAndLondon, bool mergeLondonAndNewYork, bool mergeAllDaySessions, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize)
		{
			if (cacheMarketProfile50Charts != null)
				for (int idx = 0; idx < cacheMarketProfile50Charts.Length; idx++)
					if (cacheMarketProfile50Charts[idx] != null && cacheMarketProfile50Charts[idx].TpoSize == tpoSize && cacheMarketProfile50Charts[idx].TpoIntervalMinutes == tpoIntervalMinutes && cacheMarketProfile50Charts[idx].ValueAreaPercent == valueAreaPercent && cacheMarketProfile50Charts[idx].InitialBalanceMinutes == initialBalanceMinutes && cacheMarketProfile50Charts[idx].DaysToDisplay == daysToDisplay && cacheMarketProfile50Charts[idx].ShowAsia == showAsia && cacheMarketProfile50Charts[idx].AsiaFromTime == asiaFromTime && cacheMarketProfile50Charts[idx].AsiaToTime == asiaToTime && cacheMarketProfile50Charts[idx].ShowLondon == showLondon && cacheMarketProfile50Charts[idx].LondonFromTime == londonFromTime && cacheMarketProfile50Charts[idx].LondonToTime == londonToTime && cacheMarketProfile50Charts[idx].ShowNewYork == showNewYork && cacheMarketProfile50Charts[idx].NewYorkFromTime == newYorkFromTime && cacheMarketProfile50Charts[idx].NewYorkToTime == newYorkToTime && cacheMarketProfile50Charts[idx].MarketOpen == marketOpen && cacheMarketProfile50Charts[idx].MarketClose == marketClose && cacheMarketProfile50Charts[idx].SessionLabel == sessionLabel && cacheMarketProfile50Charts[idx].ShowIndianSession == showIndianSession && cacheMarketProfile50Charts[idx].SplitLatestSession == splitLatestSession && cacheMarketProfile50Charts[idx].SplitAsiaSessions == splitAsiaSessions && cacheMarketProfile50Charts[idx].SplitLondonSessions == splitLondonSessions && cacheMarketProfile50Charts[idx].SplitNewYorkSessions == splitNewYorkSessions && cacheMarketProfile50Charts[idx].MergeAsiaAndLondon == mergeAsiaAndLondon && cacheMarketProfile50Charts[idx].MergeLondonAndNewYork == mergeLondonAndNewYork && cacheMarketProfile50Charts[idx].MergeAllDaySessions == mergeAllDaySessions && cacheMarketProfile50Charts[idx].ShowPoorHigh == showPoorHigh && cacheMarketProfile50Charts[idx].ShowPoorLow == showPoorLow && cacheMarketProfile50Charts[idx].ShowSwingHigh == showSwingHigh && cacheMarketProfile50Charts[idx].ShowSwingLow == showSwingLow && cacheMarketProfile50Charts[idx].ShowVirginVAH == showVirginVAH && cacheMarketProfile50Charts[idx].ShowVirginVAL == showVirginVAL && cacheMarketProfile50Charts[idx].ShowVirginVPoC == showVirginVPoC && cacheMarketProfile50Charts[idx].ShowVirginVPPoC == showVirginVPPoC && cacheMarketProfile50Charts[idx].ShowSummary == showSummary && cacheMarketProfile50Charts[idx].FixSummaryBoxAtBottom == fixSummaryBoxAtBottom && cacheMarketProfile50Charts[idx].MinimizeSummaryBoxes == minimizeSummaryBoxes && cacheMarketProfile50Charts[idx].ShowTopLeftBox == showTopLeftBox && cacheMarketProfile50Charts[idx].ShowLogoInTopBox == showLogoInTopBox && cacheMarketProfile50Charts[idx].MinimizeTopLeftBox == minimizeTopLeftBox && cacheMarketProfile50Charts[idx].ShowChartButtons == showChartButtons && cacheMarketProfile50Charts[idx].ShowVolumeProfile == showVolumeProfile && cacheMarketProfile50Charts[idx].ShowInitialBalance == showInitialBalance && cacheMarketProfile50Charts[idx].ShowSinglePrints == showSinglePrints && cacheMarketProfile50Charts[idx].ShowPoorHighLow == showPoorHighLow && cacheMarketProfile50Charts[idx].ProfileWidth == profileWidth && cacheMarketProfile50Charts[idx].FontSize == fontSize && cacheMarketProfile50Charts[idx].EqualsInput(input))
						return cacheMarketProfile50Charts[idx];
			return CacheIndicator<MarketProfile50Charts>(new MarketProfile50Charts(){ TpoSize = tpoSize, TpoIntervalMinutes = tpoIntervalMinutes, ValueAreaPercent = valueAreaPercent, InitialBalanceMinutes = initialBalanceMinutes, DaysToDisplay = daysToDisplay, ShowAsia = showAsia, AsiaFromTime = asiaFromTime, AsiaToTime = asiaToTime, ShowLondon = showLondon, LondonFromTime = londonFromTime, LondonToTime = londonToTime, ShowNewYork = showNewYork, NewYorkFromTime = newYorkFromTime, NewYorkToTime = newYorkToTime, MarketOpen = marketOpen, MarketClose = marketClose, SessionLabel = sessionLabel, ShowIndianSession = showIndianSession, SplitLatestSession = splitLatestSession, SplitAsiaSessions = splitAsiaSessions, SplitLondonSessions = splitLondonSessions, SplitNewYorkSessions = splitNewYorkSessions, MergeAsiaAndLondon = mergeAsiaAndLondon, MergeLondonAndNewYork = mergeLondonAndNewYork, MergeAllDaySessions = mergeAllDaySessions, ShowPoorHigh = showPoorHigh, ShowPoorLow = showPoorLow, ShowSwingHigh = showSwingHigh, ShowSwingLow = showSwingLow, ShowVirginVAH = showVirginVAH, ShowVirginVAL = showVirginVAL, ShowVirginVPoC = showVirginVPoC, ShowVirginVPPoC = showVirginVPPoC, ShowSummary = showSummary, FixSummaryBoxAtBottom = fixSummaryBoxAtBottom, MinimizeSummaryBoxes = minimizeSummaryBoxes, ShowTopLeftBox = showTopLeftBox, ShowLogoInTopBox = showLogoInTopBox, MinimizeTopLeftBox = minimizeTopLeftBox, ShowChartButtons = showChartButtons, ShowVolumeProfile = showVolumeProfile, ShowInitialBalance = showInitialBalance, ShowSinglePrints = showSinglePrints, ShowPoorHighLow = showPoorHighLow, ProfileWidth = profileWidth, FontSize = fontSize }, input, ref cacheMarketProfile50Charts);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MarketProfile50Charts MarketProfile50Charts(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool showAsia, string asiaFromTime, string asiaToTime, bool showLondon, string londonFromTime, string londonToTime, bool showNewYork, string newYorkFromTime, string newYorkToTime, int marketOpen, int marketClose, string sessionLabel, bool showIndianSession, bool splitLatestSession, bool splitAsiaSessions, bool splitLondonSessions, bool splitNewYorkSessions, bool mergeAsiaAndLondon, bool mergeLondonAndNewYork, bool mergeAllDaySessions, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize)
		{
			return indicator.MarketProfile50Charts(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, showAsia, asiaFromTime, asiaToTime, showLondon, londonFromTime, londonToTime, showNewYork, newYorkFromTime, newYorkToTime, marketOpen, marketClose, sessionLabel, showIndianSession, splitLatestSession, splitAsiaSessions, splitLondonSessions, splitNewYorkSessions, mergeAsiaAndLondon, mergeLondonAndNewYork, mergeAllDaySessions, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize);
		}

		public Indicators.MarketProfile50Charts MarketProfile50Charts(ISeries<double> input , double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool showAsia, string asiaFromTime, string asiaToTime, bool showLondon, string londonFromTime, string londonToTime, bool showNewYork, string newYorkFromTime, string newYorkToTime, int marketOpen, int marketClose, string sessionLabel, bool showIndianSession, bool splitLatestSession, bool splitAsiaSessions, bool splitLondonSessions, bool splitNewYorkSessions, bool mergeAsiaAndLondon, bool mergeLondonAndNewYork, bool mergeAllDaySessions, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize)
		{
			return indicator.MarketProfile50Charts(input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, showAsia, asiaFromTime, asiaToTime, showLondon, londonFromTime, londonToTime, showNewYork, newYorkFromTime, newYorkToTime, marketOpen, marketClose, sessionLabel, showIndianSession, splitLatestSession, splitAsiaSessions, splitLondonSessions, splitNewYorkSessions, mergeAsiaAndLondon, mergeLondonAndNewYork, mergeAllDaySessions, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MarketProfile50Charts MarketProfile50Charts(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool showAsia, string asiaFromTime, string asiaToTime, bool showLondon, string londonFromTime, string londonToTime, bool showNewYork, string newYorkFromTime, string newYorkToTime, int marketOpen, int marketClose, string sessionLabel, bool showIndianSession, bool splitLatestSession, bool splitAsiaSessions, bool splitLondonSessions, bool splitNewYorkSessions, bool mergeAsiaAndLondon, bool mergeLondonAndNewYork, bool mergeAllDaySessions, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize)
		{
			return indicator.MarketProfile50Charts(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, showAsia, asiaFromTime, asiaToTime, showLondon, londonFromTime, londonToTime, showNewYork, newYorkFromTime, newYorkToTime, marketOpen, marketClose, sessionLabel, showIndianSession, splitLatestSession, splitAsiaSessions, splitLondonSessions, splitNewYorkSessions, mergeAsiaAndLondon, mergeLondonAndNewYork, mergeAllDaySessions, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize);
		}

		public Indicators.MarketProfile50Charts MarketProfile50Charts(ISeries<double> input , double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool showAsia, string asiaFromTime, string asiaToTime, bool showLondon, string londonFromTime, string londonToTime, bool showNewYork, string newYorkFromTime, string newYorkToTime, int marketOpen, int marketClose, string sessionLabel, bool showIndianSession, bool splitLatestSession, bool splitAsiaSessions, bool splitLondonSessions, bool splitNewYorkSessions, bool mergeAsiaAndLondon, bool mergeLondonAndNewYork, bool mergeAllDaySessions, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize)
		{
			return indicator.MarketProfile50Charts(input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, showAsia, asiaFromTime, asiaToTime, showLondon, londonFromTime, londonToTime, showNewYork, newYorkFromTime, newYorkToTime, marketOpen, marketClose, sessionLabel, showIndianSession, splitLatestSession, splitAsiaSessions, splitLondonSessions, splitNewYorkSessions, mergeAsiaAndLondon, mergeLondonAndNewYork, mergeAllDaySessions, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize);
		}
	}
}

#endregion
