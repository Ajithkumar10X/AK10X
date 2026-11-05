// AK10X Market Profile for Indian Market (NSE / BSE / MCX) for NinjaTrader 8.
// Developed for AK10X Trading Systems.
// Pure Day Profile trading: No multi-session partitioning, exactly 1 Day Profile per trading day.
// Features Dalton Market Profile (13 TPOs: A through M for 09:15 - 15:30 IST),
// Initial Balance (A & B periods), Single Prints (Buying/Selling SP),
// Virgin Level Extensions (VPoC, VPPoC, VAH, VAL, PoorHigh/Low, SwingHigh/Low),
// Volume Profile, 9-Line Summary Card, and Direct2D hardware acceleration.
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
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
	public class INDMarketProfile50Charts : Indicator
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
			public string SessionName;
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
			public bool IsSplitExpanded;
			public List<SinglePrintZone> SinglePrintZones = new List<SinglePrintZone>();
		}

		private enum ChartButtonAction
		{
			ToggleSplit,
			ToggleTopLeftMinMax,
			ToggleSummaryMinMax,
			ScrollPastDays,
			ScrollNextDays,
			ScrollLiveDay
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

		private readonly object profileLock = new object();
		private readonly List<MinuteSample> rawMinuteSamples = new List<MinuteSample>(65536);
		private readonly SortedDictionary<string, DayProfile> profiles = new SortedDictionary<string, DayProfile>(StringComparer.Ordinal);
		private readonly Dictionary<DateTime, int> chartDayFirstBar = new Dictionary<DateTime, int>();
		private readonly Dictionary<DateTime, int> chartDayLastBar = new Dictionary<DateTime, int>();
		private readonly HashSet<string> manuallySplitProfileKeys = new HashSet<string>(StringComparer.Ordinal);
		private readonly HashSet<string> manuallyUnsplitProfileKeys = new HashSet<string>(StringComparer.Ordinal);
		private readonly List<ChartButton> activeChartButtons = new List<ChartButton>();
		private int lastVolumeBarIndex = -1;
		private double lastCumulativeVolume;
		private double cachedPriceStep;
		private int lastRebuiltSampleCount;
		private bool didAutoAdjustChartProps;
		private bool isMouseHooked;
		private ChartControl hookedChartControl;
		private System.Windows.Controls.Primitives.ScrollBar hookedScrollBar;
		private double lastTrackedScrollBarVal = double.NaN;
		private bool hasManualPixelScroll;
		private int lastTrackedFromIdx = -1;
		private int lastTrackedToIdx = -1;
		private float currentScrollPx = float.NaN;
		private bool? runtimeMinimizeTopLeftOverride;
		private bool? runtimeMinimizeSummaryOverride;
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

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "AK10X Market Profile for Indian Market (NSE / BSE / MCX). Pure Day Profile trading with no multi-session partitioning.";
				Name = "INDMarketProfile50Charts";
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

				ShowPoorHigh = true;
				ShowPoorLow = true;
				ShowSwingHigh = true;
				ShowSwingLow = true;
				ShowVirginVAH = true;
				ShowVirginVAL = true;
				ShowVirginVPoC = true;
				ShowVirginVPPoC = true;

				ShowSummary = true;
				FixSummaryBoxAtBottom = true;
				MinimizeSummaryBoxes = false;
				ShowTopLeftBox = true;
				ShowLogoInTopBox = true;
				MinimizeTopLeftBox = false;
				ShowChartButtons = true;
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
					chartDayFirstBar.Clear();
					chartDayLastBar.Clear();
					manuallySplitProfileKeys.Clear();
					manuallyUnsplitProfileKeys.Clear();
					activeChartButtons.Clear();
					lastVolumeBarIndex = -1;
					lastCumulativeVolume = 0;
					cachedPriceStep = 0;
					lastRebuiltSampleCount = 0;
					didAutoAdjustChartProps = false;
					lastTrackedScrollBarVal = double.NaN;
					hasManualPixelScroll = false;
					lastTrackedFromIdx = -1;
					lastTrackedToIdx = -1;
					currentScrollPx = float.NaN;
					runtimeMinimizeTopLeftOverride = null;
					runtimeMinimizeSummaryOverride = null;
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
						return sbField.GetValue(tab) as System.Windows.Controls.Primitives.ScrollBar;
				}
			}
			catch { }
			return null;
		}

		private void EnsureMouseHookAttached()
		{
			if (isMouseHooked || ChartControl == null)
				return;

			ChartControl cc = ChartControl;
			cc.Dispatcher.InvokeAsync(() =>
			{
				if (!isMouseHooked && cc != null)
				{
					cc.PreviewMouseLeftButtonDown += OnChartPreviewMouseLeftButtonDown;
					cc.PreviewMouseWheel += OnChartPreviewMouseWheel;
					hookedChartControl = cc;

					var sb = ResolveChartScrollBar(cc);
					if (sb != null)
					{
						sb.ValueChanged += OnChartScrollBarValueChanged;
						hookedScrollBar = sb;
					}

					isMouseHooked = true;
				}
			});
		}

		private void DetachMouseHook()
		{
			ChartControl cc = hookedChartControl;
			var sb = hookedScrollBar;
			if (cc != null)
			{
				cc.Dispatcher.InvokeAsync(() =>
				{
					cc.PreviewMouseLeftButtonDown -= OnChartPreviewMouseLeftButtonDown;
					cc.PreviewMouseWheel -= OnChartPreviewMouseWheel;
					if (sb != null)
						sb.ValueChanged -= OnChartScrollBarValueChanged;
				});
			}
			hookedScrollBar = null;
			hookedChartControl = null;
			isMouseHooked = false;
		}

		private void OnChartScrollBarValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
		{
			lock (profileLock)
			{
				hasManualPixelScroll = false;
			}
			ForceRefresh();
			ChartControl cc = hookedChartControl ?? ChartControl;
			if (cc != null)
				cc.InvalidateVisual();
		}

		private void OnChartPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
		{
			ChartControl cc = hookedChartControl ?? ChartControl;
			if (cc == null || ChartPanel == null)
				return;

			lock (profileLock)
			{
				hasManualPixelScroll = true;
				if (float.IsNaN(currentScrollPx))
					currentScrollPx = 0f;
				if (e.Delta > 0)
					currentScrollPx = Math.Max(0f, currentScrollPx - 140f);
				else if (e.Delta < 0)
					currentScrollPx = currentScrollPx + 140f;
			}
			e.Handled = true;
			ForceRefresh();
			cc.InvalidateVisual();
		}

		private void OnChartPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
		{
			ChartControl cc = hookedChartControl ?? ChartControl;
			if (cc == null || ChartPanel == null)
				return;

			System.Windows.Point wpfPt = e.GetPosition(cc);
			float pxX = (float)ChartingExtensions.ConvertToHorizontalPixels(wpfPt.X, cc.PresentationSource);
			float pxY = (float)ChartingExtensions.ConvertToVerticalPixels(wpfPt.Y, cc.PresentationSource);
			float dipX = (float)wpfPt.X;
			float dipY = (float)wpfPt.Y;

			bool handled = false;
			lock (profileLock)
			{
				for (int i = activeChartButtons.Count - 1; i >= 0; i--)
				{
					ChartButton btn = activeChartButtons[i];
					if (btn.Rect.Contains(pxX, pxY) || btn.Rect.Contains(dipX, dipY))
					{
						ExecuteChartButtonAction(btn);
						handled = true;
						break;
					}
				}
			}

			if (handled)
			{
				e.Handled = true;
				ForceRefresh();
				cc.InvalidateVisual();
			}
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

				case ChartButtonAction.ToggleTopLeftMinMax:
					runtimeMinimizeTopLeftOverride = !IsTopLeftBoxMinimized;
					break;

				case ChartButtonAction.ToggleSummaryMinMax:
					runtimeMinimizeSummaryOverride = !IsSummaryBoxMinimized;
					break;

				case ChartButtonAction.ScrollPastDays:
					hasManualPixelScroll = true;
					if (float.IsNaN(currentScrollPx)) currentScrollPx = 0f;
					currentScrollPx = Math.Max(0f, currentScrollPx - 260f);
					break;

				case ChartButtonAction.ScrollNextDays:
					hasManualPixelScroll = true;
					if (float.IsNaN(currentScrollPx)) currentScrollPx = 0f;
					currentScrollPx = currentScrollPx + 260f;
					break;

				case ChartButtonAction.ScrollLiveDay:
					hasManualPixelScroll = false;
					currentScrollPx = float.MaxValue;
					try
					{
						var sb = hookedScrollBar ?? ResolveChartScrollBar(ChartControl);
						if (sb != null && sb.Maximum > sb.Minimum)
							sb.Value = sb.Maximum;
					}
					catch { }
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
				if (CurrentBars[0] < 0)
					return;

				BarBrushes[0] = Brushes.Transparent;
				CandleOutlineBrushes[0] = Brushes.Transparent;

				int s1Idx = CurrentBars[0];
				double s1CumVol = Math.Max(0, Convert.ToDouble(Volumes[0][0]));
				if (s1Idx != lastVolumeBarIndex)
				{
					lastVolumeBarIndex = s1Idx;
					lastCumulativeVolume = 0;
				}
				double s1BarVol = s1CumVol >= lastCumulativeVolume
					? s1CumVol - lastCumulativeVolume
					: s1CumVol;
				lastCumulativeVolume = s1CumVol;

				MinuteSample s1Sample = new MinuteSample
				{
					Time = Times[0][0],
					Open = Opens[0][0],
					High = Highs[0][0],
					Low = Lows[0][0],
					Close = Closes[0][0],
					Volume = s1BarVol,
					BarIndex = s1Idx
				};

				lock (profileLock)
				{
					DateTime chartDate = Times[0][0].Date;
					if (!chartDayFirstBar.ContainsKey(chartDate))
						chartDayFirstBar[chartDate] = CurrentBars[0];
					chartDayLastBar[chartDate] = CurrentBars[0];

					bool isSameMinuteBar = false;
					if (rawMinuteSamples.Count > 0 && rawMinuteSamples[rawMinuteSamples.Count - 1].BarIndex == s1Idx)
					{
						isSameMinuteBar = true;
						MinuteSample prev = rawMinuteSamples[rawMinuteSamples.Count - 1];
						s1Sample.Open = prev.Open;
						s1Sample.High = Math.Max(prev.High, s1Sample.High);
						s1Sample.Low = Math.Min(prev.Low, s1Sample.Low);
						s1Sample.Volume = prev.Volume + s1BarVol;
						rawMinuteSamples[rawMinuteSamples.Count - 1] = s1Sample;
					}
					else
					{
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
						deltaSample.Volume = s1BarVol;
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
					RebuildAllDayProfiles(desiredStep);
				}
				else if (isSameMinuteBar)
				{
					MinuteSample deltaSample = sample;
					deltaSample.Volume = barVolume;
					AccumulateSampleIntoDay(deltaSample, cachedPriceStep, isLiveTickUpdate: true);
				}
				else
				{
					AccumulateSampleIntoDay(sample, cachedPriceStep, isLiveTickUpdate: false);
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
					List<DayProfile> allDays = profiles.Values.Where(p => p.Rows.Count > 0).ToList();
					if (allDays.Count > 0)
					{
						double step = GetPriceStep();
						float panelLeft = ChartPanel != null ? (float)ChartPanel.X : 0f;
						float panelRight = ChartPanel != null ? (float)(ChartPanel.X + ChartPanel.W) : 1800f;
						List<DayProfile> laidOutDays = BuildDayLayout(allDays, step, panelLeft, panelRight);

						for (int i = 0; i < laidOutDays.Count; i++)
						{
							DayProfile d = laidOutDays[i];
							if (d.SlotRightX < panelLeft || d.SlotLeftX > panelRight)
								continue;
							if (d.Low < minVal) minVal = d.Low;
							if (d.High > maxVal) maxVal = d.High;
						}

						if (minVal == double.MaxValue || maxVal == double.MinValue)
						{
							int tailStart = Math.Max(0, allDays.Count - 4);
							for (int i = tailStart; i < allDays.Count; i++)
							{
								DayProfile d = allDays[i];
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
				? 0.05
				: Math.Max(0.0000001, Instrument.MasterInstrument.TickSize);

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

			int durationSec = closeSec > openSec ? closeSec - openSec : (24 * 3600 - openSec) + closeSec;
			if (durationSec <= 0) durationSec = 375 * 60;
			expectedPeriods = Math.Max(1, (int)Math.Ceiling(durationSec / (double)(Math.Max(1, TpoIntervalMinutes) * 60)));

			dayStart = dayDate.AddSeconds(openSec);
			dayClose = dayStart.AddSeconds(durationSec);

			int nowSec = time.Hour * 3600 + time.Minute * 60 + time.Second;
			if (FilterByTradingHours)
			{
				bool inSession = (nowSec >= openSec && nowSec <= closeSec + 59);
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
			day.High = Math.Max(day.High, sample.High);
			day.Low = Math.Min(day.Low, sample.Low);
			day.Close = sample.Close;

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

		private static bool IsBuyingSinglePrintBin(DayProfile day, int bin, PriceRow row, int minBin, int maxBin, int pocBin)
		{
			int totalRange = Math.Max(1, maxBin - minBin);
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

		private static List<SinglePrintZone> ComputeSinglePrintZones(DayProfile day, int minBin, int maxBin, int pocBin)
		{
			List<SinglePrintZone> zones = new List<SinglePrintZone>();
			if (day == null || maxBin - minBin < 4)
				return zones;

			SinglePrintZone current = null;
			for (int b = minBin + 1; b <= maxBin - 1; b++)
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
			if (maxBin - minBin >= 10)
			{
				int quarter = Math.Max(2, (maxBin - minBin) / 4);
				bool hasSingleWaist = false;
				for (int b = minBin + quarter; b <= maxBin - quarter; b++)
				{
					PriceRow r;
					if (day.Rows.TryGetValue(b, out r) && r.Periods.Count <= 1)
					{
						hasSingleWaist = true;
						break;
					}
				}
				if (hasSingleWaist && dayRange > ibRange * 1.35)
				{
					isGreen = false;
					return "Double Distribution - " + dir;
				}
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

		private DayProfile NormalizeDayRowCount(DayProfile src, double baseStep)
		{
			if (src == null || src.Rows.Count == 0)
			{
				if (src != null)
					src.EffectiveStep = baseStep;
				return src;
			}

			int minB = src.Rows.Keys.Min();
			int maxB = src.Rows.Keys.Max();
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
			if (TpoSize > 0 || rawSpan <= 44)
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
				SessionName = src.SessionName,
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

		private List<DayProfile> BuildDayLayout(List<DayProfile> allDays, double step, float panelLeft, float panelRight)
		{
			if (allDays == null || allDays.Count == 0)
				return allDays;

			allDays.Sort((a, b) => a.SessionDate.CompareTo(b.SessionDate));

			// Display only up to DaysToDisplay
			if (DaysToDisplay > 0 && allDays.Count > DaysToDisplay)
				allDays = allDays.Skip(allDays.Count - DaysToDisplay).ToList();

			float totalStripWidth = 0f;
			for (int i = 0; i < allDays.Count; i++)
			{
				DayProfile day = allDays[i];
				bool isLatestDay = (i == allDays.Count - 1);
				day.IsSplitExpanded = IsDaySplit(day, isLatestDay);

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
					splitExtraW = Math.Max(172f, sliceCols * 9.8f + 40f + 26f);
				}
				float dayW = Math.Max(238f, collapsedW + splitExtraW);
				totalStripWidth += dayW;
			}

			// Reserve 188px on right edge exclusively for virgin level labels
			float leftMargin = panelLeft + 10f;
			float rightLimit = panelRight - 188f;
			float usableW = Math.Max(420f, rightLimit - leftMargin);

			float maxScroll = Math.Max(0f, totalStripWidth - usableW);
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

					if (double.IsNaN(lastTrackedScrollBarVal) || Math.Abs(sbVal - lastTrackedScrollBarVal) > 0.01)
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

					if (fromIdx != lastTrackedFromIdx || toIdx != lastTrackedToIdx)
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
					? Math.Max(8, day.Rows.Max(kv => kv.Value.Periods.Count + (kv.Key == closeBin ? 1 : 0)))
					: 12;
				float collapsedW = Math.Max(238f, maxRowCells * 8.6f + (ShowVolumeProfile ? 38f : 0f) + 24f);
				float splitExtraW = 0f;
				if (day.IsSplitExpanded && day.PeriodSlices.Count > 0)
				{
					int sliceCols = Math.Max(5, day.PeriodSlices.Count + 1);
					splitExtraW = Math.Max(172f, sliceCols * 9.8f + 40f + 26f);
				}
				float slotWidth = collapsedW + splitExtraW;

				day.SlotLeftX = curX;
				day.SlotRightX = curX + slotWidth;
				day.IbX = curX + 6f;
				day.CollapsedRightX = curX + collapsedW - 8f;

				curX += slotWidth;
			}

			return allDays;
		}

		private static bool IsLevelVirgin(List<DayProfile> allDays, int currentIdx, double levelPrice, float yLevel, double step, bool isHighExtreme = false, bool isLowExtreme = false)
		{
			for (int j = currentIdx + 1; j < allDays.Count; j++)
			{
				DayProfile next = allDays[j];
				if (isHighExtreme)
				{
					if (next.High >= levelPrice - step * 0.25)
						return false;
					if (next.RenderTopY <= yLevel + 2.0f && next.RenderBotY >= yLevel - 2.0f)
						return false;
				}
				else if (isLowExtreme)
				{
					if (next.Low <= levelPrice + step * 0.25)
						return false;
					if (next.RenderBotY >= yLevel - 2.0f && next.RenderTopY <= yLevel + 2.0f)
						return false;
				}
				else
				{
					if (next.Low <= levelPrice + step * 0.35 && next.High >= levelPrice - step * 0.35)
						return false;
					if (yLevel >= next.RenderTopY - 2.0f && yLevel <= next.RenderBotY + 2.0f)
						return false;
				}
			}
			return true;
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (chartControl == null) return;
			if (chartControl.PrimaryBars != null)
				ChartBars = chartControl.PrimaryBars;
			if (chartControl.ChartPanels != null && chartControl.ChartPanels.Count > 0)
				ChartPanel = chartControl.ChartPanels[0];

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
					if (chartControl.Properties.BarDistance < 1.5f || chartControl.Properties.BarDistance > 4.0f)
						chartControl.Properties.BarDistance = 2.5f;
				}
				if (chartScale != null && chartScale.Properties != null)
				{
					chartScale.Properties.YAxisRangeType = YAxisRangeType.Automatic;
				}
				chartControl.Dispatcher.BeginInvoke(new Action(() => chartControl.InvalidateVisual()));
			}

			EnsureMouseHookAttached();

			lock (profileLock)
			{
				activeChartButtons.Clear();
				if (profiles.Count == 0)
					return;

				List<DayProfile> allDays = profiles.Values.Where(p => p.Rows.Count > 0).ToList();
				if (allDays.Count == 0)
					return;

				double step = GetPriceStep();
				for (int i = 0; i < allDays.Count; i++)
					allDays[i] = NormalizeDayRowCount(allDays[i], step);

				float panelLeft = (float)ChartPanel.X;
				float panelRight = (float)(ChartPanel.X + ChartPanel.W);
				float panelTop = (float)ChartPanel.Y;
				float panelBottom = (float)(ChartPanel.Y + ChartPanel.H);

				allDays = BuildDayLayout(allDays, step, panelLeft, panelRight);
				if (allDays.Count == 0)
					return;

				double visMinPrice = double.MaxValue;
				double visMaxPrice = double.MinValue;
				for (int i = 0; i < allDays.Count; i++)
				{
					DayProfile d = allDays[i];
					if (d.SlotRightX < panelLeft + 15f || d.SlotLeftX > panelRight - 182f)
						continue;
					if (d.Low < visMinPrice) visMinPrice = d.Low;
					if (d.High > visMaxPrice) visMaxPrice = d.High;
				}
				if (visMinPrice == double.MaxValue || visMaxPrice == double.MinValue)
				{
					int tailStart = Math.Max(0, allDays.Count - 3);
					for (int i = tailStart; i < allDays.Count; i++)
					{
						DayProfile d = allDays[i];
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
				bool useFallbackYScale = testSpanPx < targetSpanPx * 0.72f
					|| testYHigh < usableTopY - 25f
					|| testYHigh > usableTopY + 45f
					|| testYLow > usableBotY + 4f
					|| testYLow < usableBotY - 55f;

				double safePriceSpan = Math.Max(step * 8.0, visMaxPrice - visMinPrice);

				Func<double, float> priceToY = (double price) =>
				{
					if (!useFallbackYScale)
						return (float)chartScale.GetYByValue(price);
					double norm = (visMaxPrice - price) / safePriceSpan;
					return (float)(usableTopY + norm * (usableBotY - usableTopY));
				};

				for (int i = 0; i < allDays.Count; i++)
				{
					DayProfile d = allDays[i];
					double dStep = d.EffectiveStep > 0 ? d.EffectiveStep : step;
					CalculateTpoValueArea(d, ValueAreaPercent, out d.PocBin, out d.VahBin, out d.ValBin);
					d.MaxRowVolume = Math.Max(1e-9, d.Rows.Values.Max(r => r.Volume));
					d.VolumePocBin = d.Rows.OrderByDescending(kv => kv.Value.Volume)
						.ThenBy(kv => Math.Abs(kv.Key - d.PocBin)).First().Key;
					CalculateVolumeValueArea(d, ValueAreaPercent, d.VolumePocBin, out d.VolumeVahBin, out d.VolumeValBin);
					int minB = d.Rows.Keys.Min();
					int maxB = d.Rows.Keys.Max();
					d.SinglePrintZones = ComputeSinglePrintZones(d, minB, maxB, d.PocBin);

					int dRowCount = Math.Max(1, maxB - minB + 1);
					float rawHighY = priceToY(maxB * dStep + dStep * 0.5);
					float rawLowY = priceToY(minB * dStep - dStep * 0.5);
					if (rawHighY > rawLowY)
					{
						float sw = rawHighY;
						rawHighY = rawLowY;
						rawLowY = sw;
					}
					float naturalCenterY = 0.5f * (rawHighY + rawLowY);
					float naturalSpanH = Math.Max(10f, rawLowY - rawHighY);
					float minRequiredSpanH = dRowCount * 10.6f;
					float maxAllowedSpanH = dRowCount * 11.4f;
					float targetSpanH = Math.Max(minRequiredSpanH, Math.Min(maxAllowedSpanH, naturalSpanH));
					float finalSpanH = Math.Min(usableBotY - usableTopY, targetSpanH);
					float rTop = naturalCenterY - finalSpanH * 0.5f;
					float rBot = naturalCenterY + finalSpanH * 0.5f;
					bool isSessionOnScreen = (d.SlotRightX >= panelLeft + 8f && d.SlotLeftX <= panelRight - 182f);
					if (isSessionOnScreen)
					{
						if (rTop < usableTopY)
						{
							rTop = usableTopY;
							rBot = rTop + finalSpanH;
						}
						if (rBot > usableBotY)
						{
							rBot = usableBotY;
							rTop = Math.Max(usableTopY, rBot - finalSpanH);
						}
					}
					d.RenderTopY = rTop;
					d.RenderBotY = rTop + finalSpanH;
				}

				DxBrush[] letterBrushes = new DxBrush[LetterColors.Length];
				List<RightEdgeTag> rightEdgeTags = new List<RightEdgeTag>();

				try
				{
					for (int i = 0; i < LetterColors.Length; i++)
						letterBrushes[i] = new DxBrush(RenderTarget, LetterColors[i]);

					var dashProps = new StrokeStyleProperties
					{
						DashStyle = SharpDX.Direct2D1.DashStyle.Custom,
						DashCap = CapStyle.Flat,
						StartCap = CapStyle.Flat,
						EndCap = CapStyle.Flat
					};

					using (var dashedStroke = new StrokeStyle(RenderTarget.Factory, dashProps, new float[] { 4f, 3f }))
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
					using (var volumeOutBrush = new DxBrush(RenderTarget, new SharpDX.Color(76, 86, 96)))
					using (var volumeVaBrush = new DxBrush(RenderTarget, new SharpDX.Color(198, 26, 90)))
					using (var volumePocBrush = new DxBrush(RenderTarget, new SharpDX.Color(246, 132, 178)))
					using (var splitVolVaBrush = new DxBrush(RenderTarget, new SharpDX.Color(98, 198, 184, 195)))
					using (var splitVolOutBrush = new DxBrush(RenderTarget, new SharpDX.Color(68, 82, 92, 175)))
					using (var splitVolPocBrush = new DxBrush(RenderTarget, new SharpDX.Color(198, 248, 232, 235)))
					using (var pocLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(228, 152, 28)))
					using (var vppocLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(235, 200, 45)))
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

						List<double> allIbRanges = allDays
							.Where(d => d.InitialBalanceHigh > d.InitialBalanceLow && d.InitialBalanceLow != double.MaxValue)
							.Select(d => d.InitialBalanceHigh - d.InitialBalanceLow)
							.ToList();
						double avgIbRange = allIbRanges.Count > 0 ? allIbRanges.Average() : step * 6;

						// =========================================================================
						// PASS 1: Separators, Value Area, Virgin Single Print Rays & Virgin Lines
						// =========================================================================
						for (int i = 0; i < allDays.Count; i++)
						{
							DayProfile day = allDays[i];
							double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;

							float xSlotLeft = day.SlotLeftX;
							float xSlotRight = day.SlotRightX;
							float xIb = day.IbX;
							float xCollapsedRight = day.CollapsedRightX;

							if (xSlotRight < panelLeft + 8f || xSlotLeft > panelRight - 182f)
								continue;

							if (xSlotLeft >= panelLeft && xSlotLeft <= panelRight - 182f)
								RenderTarget.DrawLine(new Vector2(xSlotLeft, panelTop), new Vector2(xSlotLeft, panelBottom), daySepBrush, 1.6f);
							if (xSlotRight >= panelLeft && xSlotRight <= panelRight - 182f)
								RenderTarget.DrawLine(new Vector2(xSlotRight, panelTop), new Vector2(xSlotRight, panelBottom), daySepBrush, 1.6f);

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

							// 1. Virgin Single Print Zones
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

									bool isSpVirgin = IsLevelVirgin(allDays, i, zoneMidPrice, ySpMid, dStep);
									if (isSpVirgin && ySpBot >= panelTop && ySpTop <= panelBottom - 25f)
									{
										float drawStartX = Math.Max(panelLeft, xIb);
										float drawEndX = panelRight - 6f;
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

										string spType = spZone.IsBuying ? "Buying SP" : "Selling SP";
										rightEdgeTags.Add(new RightEdgeTag
										{
											Price = zoneMidPrice,
											IdealY = ySpMid - 7.5f,
											Height = 15f,
											Text = spType + " " + FormatPrice(zoneMidPrice) + " (" + dateLabel + ")",
											Brush = spZone.IsBuying ? greenBrush : redBrush,
											Priority = 2
										});
									}
								}
							}

							// 2. Virgin Poor/Swing High/Low
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
								bool isHighVirgin = IsLevelVirgin(allDays, i, day.High, profileHighY, dStep, isHighExtreme: true);
								float drawStartX = Math.Max(panelLeft, xIb);
								float drawEndX = isHighVirgin ? (panelRight - 6f) : xCollapsedRight;
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
										Text = highLevelName + " " + FormatPrice(day.High) + " (" + dateLabel + ")",
										Brush = highLevelBrush,
										Priority = 1
									});
								}
							}

							if (enableThisLow && profileLowY >= panelTop && profileLowY <= panelBottom - 25f)
							{
								bool isLowVirgin = IsLevelVirgin(allDays, i, day.Low, profileLowY, dStep, isLowExtreme: true);
								float drawStartX = Math.Max(panelLeft, xIb);
								float drawEndX = isLowVirgin ? (panelRight - 6f) : xCollapsedRight;
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
										Text = lowLevelName + " " + FormatPrice(day.Low) + " (" + dateLabel + ")",
										Brush = lowLevelBrush,
										Priority = 1
									});
								}
							}

							// 3. Virgin VAH & VAL
							double vahPrice = day.VahBin * dStep;
							double valPrice = day.ValBin * dStep;
							float yVahLine = SessionPriceToY(day, day.VahBin * dStep + dStep * 0.5, step);
							float yValLine = SessionPriceToY(day, day.ValBin * dStep - dStep * 0.5, step);

							if (ShowVirginVAH && yVahLine >= panelTop && yVahLine <= panelBottom - 25f)
							{
								bool isVahVirgin = IsLevelVirgin(allDays, i, vahPrice, yVahLine, dStep);
								if (isVahVirgin)
								{
									RenderTarget.DrawLine(
										new Vector2(xCollapsedRight, yVahLine),
										new Vector2(panelRight - 6f, yVahLine),
										vaVirginBrush, 1.05f, dashedStroke);
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

							if (ShowVirginVAL && yValLine >= panelTop && yValLine <= panelBottom - 25f)
							{
								bool isValVirgin = IsLevelVirgin(allDays, i, valPrice, yValLine, dStep);
								if (isValVirgin)
								{
									RenderTarget.DrawLine(
										new Vector2(xCollapsedRight, yValLine),
										new Vector2(panelRight - 6f, yValLine),
										vaVirginBrush, 1.05f, dashedStroke);
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

							// 4. Virgin VPoC & VPPoC
							double vpocPrice = day.VolumePocBin * dStep;
							double tpoPocPrice = day.PocBin * dStep;
							float yVpoc = SessionPriceToY(day, vpocPrice, step);
							float yTpoPoc = SessionPriceToY(day, tpoPocPrice, step);

							bool isVpocVirgin = ShowVirginVPoC && IsLevelVirgin(allDays, i, vpocPrice, yVpoc, dStep);
							if (yVpoc >= panelTop && yVpoc <= panelBottom - 25f)
							{
								float vpocDrawEndX = isVpocVirgin ? (panelRight - 6f) : xCollapsedRight;
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
										Text = "VPoC " + FormatPrice(vpocPrice) + " (" + dateLabel + ")",
										Brush = orangeBrush,
										Priority = 3
									});
								}
							}

							if (day.PocBin != day.VolumePocBin && yTpoPoc >= panelTop && yTpoPoc <= panelBottom - 25f)
							{
								bool isVppocVirgin = ShowVirginVPPoC && IsLevelVirgin(allDays, i, tpoPocPrice, yTpoPoc, dStep);
								float tpoDrawEndX = isVppocVirgin ? (panelRight - 6f) : xCollapsedRight;
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
										Text = "VPPoC " + FormatPrice(tpoPocPrice) + " (" + dateLabel + ")",
										Brush = yellowBrush,
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
						for (int i = 0; i < allDays.Count; i++)
						{
							DayProfile day = allDays[i];
							DayProfile prevDay = i > 0 ? allDays[i - 1] : null;
							bool isLatestDay = (i == allDays.Count - 1);
							double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;

							float xSlotLeft = day.SlotLeftX;
							float xSlotRight = day.SlotRightX;
							float slotWidth = xSlotRight - xSlotLeft;
							float xIb = day.IbX;
							float xCollapsedRight = day.CollapsedRightX;

							if (xSlotRight < panelLeft + 8f || xSlotLeft > panelRight - 182f)
								continue;

							// Bottom Date Banner
							float dateBannerY = panelBottom - 23f;
							float clampedBannerLeft = Math.Max(panelLeft + 6f, day.SlotLeftX + 1f);
							float clampedBannerRight = Math.Min(panelRight - 182f, day.SlotRightX - 1f);
							float bannerW = clampedBannerRight - clampedBannerLeft;
							if (bannerW > 70f)
							{
								RectangleF dateBannerRect = new RectangleF(clampedBannerLeft, dateBannerY, bannerW, 22f);
								RenderTarget.FillRectangle(dateBannerRect, dateBannerBgBrush);
								RenderTarget.DrawRectangle(dateBannerRect, cardBorderBrush, 1.0f);

								int expTpos = Math.Max(1, day.ExpectedPeriodCount);
								int prnTpos = Math.Min(expTpos, Math.Max(day.PeriodSlices.Count, day.LastPeriod + 1));
								bool isLive = isLatestDay && prnTpos < expTpos;
								string tpoPart = isLive ? string.Format(CultureInfo.InvariantCulture, "LIVE: {0}/{1} TPOs", prnTpos, expTpos) : string.Format(CultureInfo.InvariantCulture, "{0} TPOs", expTpos);
								string dateBannerText = bannerW >= 290f
									? string.Format(CultureInfo.InvariantCulture, "Date: {0} — Indian Day Profile ({1})", day.SessionDate.ToString("dd MMM yyyy (ddd)", CultureInfo.InvariantCulture), tpoPart)
									: string.Format(CultureInfo.InvariantCulture, "Date: {0} ({1})", day.SessionDate.ToString("dd MMM (ddd)", CultureInfo.InvariantCulture), tpoPart);

								DrawText(dateBannerText, clampedBannerLeft + 6f, dateBannerY + 2f, bannerW - 12f, 18f, dateAxisFormat, whiteBrush);
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

							// Value Area Box
							RenderTarget.FillRectangle(
								new RectangleF(xIb, yVaTop, Math.Max(4f, xCollapsedRight - xIb), Math.Max(2f, yVaBot - yVaTop)),
								vaFillBrush);
							RenderTarget.DrawLine(new Vector2(xIb, yVaTop), new Vector2(xCollapsedRight, yVaTop), vaBorderBrush, 1.0f, dashedStroke);
							RenderTarget.DrawLine(new Vector2(xIb, yVaBot), new Vector2(xCollapsedRight, yVaBot), vaBorderBrush, 1.0f, dashedStroke);

							float sampleRowYA = SessionPriceToY(day, poc * dStep + dStep * 0.5, step);
							float sampleRowYB = SessionPriceToY(day, poc * dStep - dStep * 0.5, step);
							float sampleRowH = Math.Max(10.5f, Math.Abs(sampleRowYB - sampleRowYA));
							float letterFontPx = Math.Min((float)Math.Max(10, FontSize), Math.Max(9.5f, sampleRowH * 0.92f));
							float splitLetterFontPx = Math.Min(9.8f, Math.Max(9.2f, sampleRowH * 0.88f));

							using (var letterFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, letterFontPx))
							using (var splitLetterFormat = new DxTextFormat(Core.Globals.DirectWriteFactory, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, splitLetterFontPx))
							{
								letterFormat.TextAlignment = TextAlignment.Center;
								letterFormat.ParagraphAlignment = ParagraphAlignment.Center;
								letterFormat.WordWrapping = WordWrapping.NoWrap;

								splitLetterFormat.TextAlignment = TextAlignment.Center;
								splitLetterFormat.ParagraphAlignment = ParagraphAlignment.Center;
								splitLetterFormat.WordWrapping = WordWrapping.NoWrap;

								// Collapsed TPO Letters & Volume Profile
								foreach (KeyValuePair<int, PriceRow> item in day.Rows)
								{
									int bin = item.Key;
									PriceRow row = item.Value;
									float yA = SessionPriceToY(day, bin * dStep + dStep * 0.5, step);
									float yB = SessionPriceToY(day, bin * dStep - dStep * 0.5, step);
									float y = Math.Min(yA, yB);
									float h = Math.Max(10.5f, Math.Abs(yB - yA));
									if (y > panelBottom - 25f || y + h < panelTop)
										continue;

									if (ShowSinglePrints && row.Periods.Count == 1 && bin > minBin && bin < maxBin && maxBin - minBin >= 4)
									{
										bool isBuySp = IsBuyingSinglePrintBin(day, bin, row, minBin, maxBin, poc);
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

								// Inline Single Print Badges
								if (ShowSinglePrints && day.PeriodSlices.Count >= 4)
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

								// Initial Balance (Green Bar on Spine)
								if (ShowInitialBalance && day.InitialBalanceHigh != double.MinValue && day.InitialBalanceLow != double.MaxValue)
								{
									float yIbH = SessionPriceToY(day, day.InitialBalanceHigh, step);
									float yIbL = SessionPriceToY(day, day.InitialBalanceLow, step);
									RenderTarget.DrawLine(new Vector2(xIb, yIbH), new Vector2(xIb, yIbL), ibBarBrush, 2.4f);
								}

								PriceRow topRow = day.Rows[maxBin];
								bool isAbPoorHigh = topRow.Periods.Contains(0) && topRow.Periods.Contains(1);
								bool isPoorHigh = !isAbPoorHigh && topRow.Periods.Count >= 2;
								bool enableThisHigh = ShowPoorHighLow && ((isAbPoorHigh || isPoorHigh) ? ShowPoorHigh : ShowSwingHigh);
								string highLevelName = isAbPoorHigh ? "ABPoorHigh" : (isPoorHigh ? "PoorHigh" : "SwingHigh");
								DxBrush highLevelBrush = isAbPoorHigh ? abPoorBrush : (isPoorHigh ? cyanBrush : swingLineBrush);

								float topTagY = profileHighY - 17f;
								float headerY = Math.Max(panelTop + 6f, (enableThisHigh ? topTagY - 36f : profileHighY - 38f));
								float btnRowY = headerY + 17f;

								// Split Profile (Expanded)
								if (day.IsSplitExpanded && day.PeriodSlices.Count > 0)
								{
									float xSplitStart = xCollapsedRight + 12f;
									float xSplitEnd = Math.Min(xSlotRight - 6f, panelRight - 178f);
									if (xSplitEnd > xSplitStart + 65f)
									{
										float splitAvailW = xSplitEnd - xSplitStart;
										float splitVolW = 36f;
										List<PeriodSlice> orderedSlices = day.PeriodSlices.Values.OrderBy(ps => ps.PeriodIndex).ToList();
										int sliceCount = Math.Max(1, orderedSlices.Count);
										float splitColW = Math.Min(10.2f, Math.Max(9.0f, (splitAvailW - splitVolW - 14f) / Math.Max(5, sliceCount + 1)));
										float xSplitVol = Math.Min(xSplitEnd - splitVolW - 2f, xSplitStart + 6f + (sliceCount + 1) * splitColW + 6f);

										float splitHeaderW = Math.Min(174f, Math.Max(124f, splitAvailW));
										RectangleF splitHdrPill = new RectangleF(xSplitStart, headerY, splitHeaderW, 16f);
										RenderTarget.FillRectangle(splitHdrPill, btnActiveBgBrush);
										RenderTarget.DrawRectangle(splitHdrPill, cyanBrush, 0.8f);
										DrawText("SPLIT PROFILE (A..M)", xSplitStart + 4f, headerY, splitHeaderW - 8f, 16f, spTagFormat, cyanBrush);

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

											DrawText(pLetter, cx, btnRowY, splitColW, 14f, splitLetterFormat, letterBrushes[pIdx % letterBrushes.Length]);

											int sMinBin = PriceToBin(slice.Low, dStep);
											int sMaxBin = PriceToBin(slice.High, dStep);
											int sOpenBin = PriceToBin(slice.Open, dStep);
											int sCloseBin = PriceToBin(slice.Close, dStep);

											for (int b = sMinBin; b <= sMaxBin; b++)
											{
												float yA = SessionPriceToY(day, b * dStep + dStep * 0.5, step);
												float yB = SessionPriceToY(day, b * dStep - dStep * 0.5, step);
												float y = Math.Min(yA, yB);
												float h = Math.Max(10.5f, Math.Abs(yB - yA));
												if (y > panelBottom - 25f || y + h < panelTop)
													continue;

												DxBrush lBrush = (b == sOpenBin || b == sCloseBin)
													? whiteBrush
													: letterBrushes[pIdx % letterBrushes.Length];
												DrawText(pLetter, cx, y, splitColW, h, splitLetterFormat, lBrush);
											}

											if (cIdx == orderedSlices.Count - 1 && cx + splitColW * 2f <= xSplitVol)
											{
												float yCloseA = SessionPriceToY(day, sCloseBin * dStep + dStep * 0.5, step);
												float yCloseB = SessionPriceToY(day, sCloseBin * dStep - dStep * 0.5, step);
												float yClose = Math.Min(yCloseA, yCloseB);
												float hClose = Math.Max(10.5f, Math.Abs(yCloseB - yCloseA));
												DrawText("#", cx + splitColW, yClose, splitColW, hClose, splitLetterFormat, redBrush);
											}
										}

										RenderTarget.DrawLine(
											new Vector2(xSplitVol, profileHighY),
											new Vector2(xSplitVol, profileLowY),
											spineLineBrush, 0.9f);

										foreach (KeyValuePair<int, PriceRow> item in day.Rows)
										{
											int bin = item.Key;
											PriceRow row = item.Value;
											float yA = SessionPriceToY(day, bin * dStep + dStep * 0.5, step);
											float yB = SessionPriceToY(day, bin * dStep - dStep * 0.5, step);
											float y = Math.Min(yA, yB);
											float h = Math.Max(10.5f, Math.Abs(yB - yA));
											if (y > panelBottom - 25f || y + h < panelTop)
												continue;

											float barW = (float)(splitVolW * (row.Volume / maxVolume));
											bool inVolVa = bin >= volumeVal && bin <= volumeVah;
											DxBrush sBrush = (bin == volumePoc)
												? splitVolPocBrush
												: (inVolVa ? splitVolVaBrush : splitVolOutBrush);
											RenderTarget.FillRectangle(
												new RectangleF(xSplitVol + 1f, y + 0.5f, Math.Max(1.5f, barW), Math.Max(1.0f, h - 1.0f)),
												sBrush);
										}
									}
								}

								// Top Extreme Pill
								if (enableThisHigh)
								{
									float tagW = 84f;
									float tagX = (xIb + xCollapsedRight) * 0.5f - tagW * 0.5f;
									RectangleF tagPill = new RectangleF(tagX, topTagY, tagW, 15f);
									RenderTarget.FillRectangle(tagPill, pillBgBrush);
									RenderTarget.DrawRectangle(tagPill, cardBorderBrush, 0.8f);
									DrawText(highLevelName, tagX, topTagY, tagW, 15f, tagCenterFormat, highLevelBrush);
								}

								// Day Header Pill
								int expectedTpos = Math.Max(1, day.ExpectedPeriodCount);
								int printedTpos = Math.Min(expectedTpos, Math.Max(day.PeriodSlices.Count, day.LastPeriod + 1));
								bool isDayLive = isLatestDay && printedTpos < expectedTpos;
								string tpoStatus = isDayLive
									? string.Format(CultureInfo.InvariantCulture, "LIVE: {0}/{1} TPO", printedTpos, expectedTpos)
									: string.Format(CultureInfo.InvariantCulture, "{0}/{1} TPO", printedTpos, expectedTpos);
								string dayHeader = string.Format(CultureInfo.InvariantCulture, "{0} ({1}) - {2}",
									day.SessionDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
									day.SessionDate.ToString("ddd", CultureInfo.InvariantCulture),
									tpoStatus);

								float headerW = Math.Min(xCollapsedRight - xIb + 2f, 260f);
								RectangleF headerPill = new RectangleF(xIb - 1f, headerY, headerW, 16f);
								RenderTarget.FillRectangle(headerPill, pillBgBrush);
								RenderTarget.DrawRectangle(headerPill, cardBorderBrush, 0.8f);
								DrawText(dayHeader, xIb + 3f, headerY, headerW - 6f, 16f, headerFormat, cyanBrush);

								// Action Button: [Split] / [Split ON]
								if (ShowChartButtons)
								{
									float splitBtnW = 62f;
									RectangleF splitBtnRect = new RectangleF(xIb - 1f, btnRowY, splitBtnW, 14f);
									RenderTarget.FillRectangle(splitBtnRect, day.IsSplitExpanded ? btnActiveBgBrush : btnBgBrush);
									RenderTarget.DrawRectangle(splitBtnRect, day.IsSplitExpanded ? cyanBrush : cardBorderBrush, 0.8f);
									DrawText(day.IsSplitExpanded ? "[Split ON]" : "[Split]", splitBtnRect.X, splitBtnRect.Y, splitBtnRect.Width, splitBtnRect.Height, tagCenterFormat, day.IsSplitExpanded ? cyanBrush : textBrush);
									activeChartButtons.Add(new ChartButton
									{
										Rect = splitBtnRect,
										Action = ChartButtonAction.ToggleSplit,
										PrimaryKey = day.IsSplitExpanded ? (day.ProfileKey + "|ON") : day.ProfileKey
									});
								}

								// Bottom Extreme Pill
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

								// 9-Line Summary Card
								if (ShowSummary)
								{
									bool sumMinimized = IsSummaryBoxMinimized;
									float summaryCardH = sumMinimized ? (2f * 12.2f + 8f) : (9f * 12.2f + 8f);
									float fixedBottomTopY = dateBannerY - summaryCardH - 4f;
									float floatingTopY = enableThisLow ? (botTagY + 18f) : (profileLowY + 8f);
									float summaryStartTopY = FixSummaryBoxAtBottom ? fixedBottomTopY : floatingTopY;
									float summaryCardW = Math.Min(234f, Math.Max(195f, slotWidth - 12f));

									Draw50ChartsSummaryCard(
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

						// Right-Edge Virgin Level Tags (Deduplicated & Non-overlapping)
						RenderDeoverlappedRightEdgeTags(rightEdgeTags, step, panelRight, panelTop + 82f, panelBottom - 28f, pillBgBrush, cardBorderBrush, labelRightFormat);

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
					}
				}
				finally
				{
					for (int i = 0; i < letterBrushes.Length; i++)
						if (letterBrushes[i] != null)
							letterBrushes[i].Dispose();
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

			float boxWidth = 182f;
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
			boxTop = Math.Max((float)ChartPanel.Y + 86f, boxTop);

			RectangleF cardRect = new RectangleF(xLeft, boxTop, cardWidth, boxHeight);
			RenderTarget.FillRectangle(cardRect, bgBrush);
			RenderTarget.DrawRectangle(cardRect, borderBrush, 1.0f);

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
			float logoSize = isMin ? 22f : 56f;
			float logoPadLeft = hasLogo ? (logoSize + 12f) : 0f;

			string instName = Instrument == null ? "NSE / Indian Market" : Instrument.FullName;
			string tpoMode = TpoSize <= 0 ? " (Auto & Real-time)" : " (Manual)";
			int expectedTpos = latestDay != null && latestDay.ExpectedPeriodCount > 0 ? latestDay.ExpectedPeriodCount : 13;
			string distName = string.Format(CultureInfo.InvariantCulture, "Indian Day Profile ({0} TPO)", expectedTpos);

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

			float line1Width = MeasureSegmentedLine(lineH, format, line1Segments);
			float btnW = 28f;
			float navBtnW = 46f;
			float btnH = 14.5f;
			float maxBoxW = Math.Max(420f, (float)ChartPanel.W - 196f);
			float boxW = Math.Min(maxBoxW, Math.Max(isMin ? 520f : 560f, 6f + logoPadLeft + line1Width + 16f + btnW + 8f));
			float boxH = isMin ? (lineH + 12f) : (lineH * 4 + 10f);

			RectangleF rect = new RectangleF(boxX, boxY, boxW, boxH);
			RenderTarget.FillRectangle(rect, bgBrush);
			RenderTarget.DrawRectangle(rect, borderBrush, 1.0f);

			if (hasLogo)
			{
				float logoX = boxX + 6f;
				float logoY = boxY + (boxH - logoSize) * 0.5f;
				RectangleF logoRect = new RectangleF(logoX, logoY, logoSize, logoSize);
				RenderTarget.DrawBitmap(logoBmp, logoRect, 1.0f, BitmapInterpolationMode.Linear);

				float divX = boxX + logoSize + 10f;
				RenderTarget.DrawLine(new Vector2(divX, boxY + 4f), new Vector2(divX, boxY + boxH - 4f), borderBrush, 0.8f);
			}

			float textX = boxX + 6f + logoPadLeft;
			DrawSegmentedLine(textX, boxY + 4f, lineH, format, line1Segments);

			RectangleF minMaxBtnRect = new RectangleF(boxX + boxW - btnW - 6f, boxY + 5f, btnW, btnH);
			RenderTarget.FillRectangle(minMaxBtnRect, btnBgBrush);
			RenderTarget.DrawRectangle(minMaxBtnRect, cyanBrush, 0.8f);
			DrawText(isMin ? "[+]" : "[-]", minMaxBtnRect.X, minMaxBtnRect.Y, minMaxBtnRect.Width, minMaxBtnRect.Height, btnFormat, cyanBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = minMaxBtnRect,
				Action = ChartButtonAction.ToggleTopLeftMinMax,
				PrimaryKey = "TopLeftInfoBox"
			});

			float navRowY = isMin ? (boxY + 5f) : (boxY + 5f + lineH + 1f);
			float navRightEdge = isMin ? (minMaxBtnRect.X - 4f) : (boxX + boxW - 6f);

			RectangleF liveBtnRect = new RectangleF(navRightEdge - navBtnW, navRowY, navBtnW, btnH);
			RenderTarget.FillRectangle(liveBtnRect, btnBgBrush);
			RenderTarget.DrawRectangle(liveBtnRect, greenBrush, 0.8f);
			DrawText("Live >>", liveBtnRect.X, liveBtnRect.Y, liveBtnRect.Width, liveBtnRect.Height, btnFormat, greenBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = liveBtnRect,
				Action = ChartButtonAction.ScrollLiveDay,
				PrimaryKey = "ScrollLive"
			});

			RectangleF nextBtnRect = new RectangleF(liveBtnRect.X - navBtnW - 4f, navRowY, navBtnW, btnH);
			RenderTarget.FillRectangle(nextBtnRect, btnBgBrush);
			RenderTarget.DrawRectangle(nextBtnRect, cyanBrush, 0.8f);
			DrawText("Next >", nextBtnRect.X, nextBtnRect.Y, nextBtnRect.Width, nextBtnRect.Height, btnFormat, cyanBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = nextBtnRect,
				Action = ChartButtonAction.ScrollNextDays,
				PrimaryKey = "ScrollNext"
			});

			RectangleF pastBtnRect = new RectangleF(nextBtnRect.X - navBtnW - 4f, navRowY, navBtnW, btnH);
			RenderTarget.FillRectangle(pastBtnRect, btnBgBrush);
			RenderTarget.DrawRectangle(pastBtnRect, orangeBrush, 0.8f);
			DrawText("< Past", pastBtnRect.X, pastBtnRect.Y, pastBtnRect.Width, pastBtnRect.Height, btnFormat, orangeBrush);
			activeChartButtons.Add(new ChartButton
			{
				Rect = pastBtnRect,
				Action = ChartButtonAction.ScrollPastDays,
				PrimaryKey = "ScrollPast"
			});

			if (isMin)
				return;

			string otfText, roundText;
			bool otfUp;
			ComputeOneTimeFraming(latestDay, TpoIntervalMinutes, out otfText, out otfUp, out roundText);
			DrawText(otfText, textX, boxY + 4f + lineH, boxW - logoPadLeft - 14f, lineH, format, otfUp ? greenBrush : redBrush);
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
			RenderTarget.DrawText(text, format, new RectangleF(x, y, width, height), brush, DrawTextOptions.NoSnap);
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

		// Virgin Lines & Levels
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
		[Display(Name = "Enable Virgin ValueAreaHigh", Description = "Show and extend only Virgin ValueAreaHigh lines until broken", GroupName = "Virgin Lines & Levels", Order = 4)]
		public bool ShowVirginVAH { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin ValueAreaLow", Description = "Show and extend only Virgin ValueAreaLow lines until broken", GroupName = "Virgin Lines & Levels", Order = 5)]
		public bool ShowVirginVAL { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin VPoC", Description = "Show and extend only Virgin Volume PoC (VPoC) lines until broken", GroupName = "Virgin Lines & Levels", Order = 6)]
		public bool ShowVirginVPoC { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Virgin VPPoC (TPO PoC)", Description = "Show and extend only Virgin TPO PoC (VPPoC) lines until broken", GroupName = "Virgin Lines & Levels", Order = 7)]
		public bool ShowVirginVPPoC { get; set; }

		// Panels & Cards
		[NinjaScriptProperty]
		[Display(Name = "Enable Bottom Summary Boxes", Description = "Enable or disable the 9-line day summary boxes at the bottom of the chart", GroupName = "Display", Order = 0)]
		public bool ShowSummary { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Fix Summary Boxes at Bottom", Description = "When enabled, docks all day summary boxes in a fixed horizontal row at the bottom above the Date bar", GroupName = "Display", Order = 1)]
		public bool FixSummaryBoxAtBottom { get; set; }

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
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private INDMarketProfile50Charts[] cacheINDMarketProfile50Charts;
		public INDMarketProfile50Charts INDMarketProfile50Charts(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return INDMarketProfile50Charts(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}

		public INDMarketProfile50Charts INDMarketProfile50Charts(ISeries<double> input, double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			if (cacheINDMarketProfile50Charts != null)
				for (int idx = 0; idx < cacheINDMarketProfile50Charts.Length; idx++)
					if (cacheINDMarketProfile50Charts[idx] != null && cacheINDMarketProfile50Charts[idx].TpoSize == tpoSize && cacheINDMarketProfile50Charts[idx].TpoIntervalMinutes == tpoIntervalMinutes && cacheINDMarketProfile50Charts[idx].ValueAreaPercent == valueAreaPercent && cacheINDMarketProfile50Charts[idx].InitialBalanceMinutes == initialBalanceMinutes && cacheINDMarketProfile50Charts[idx].DaysToDisplay == daysToDisplay && cacheINDMarketProfile50Charts[idx].FilterByTradingHours == filterByTradingHours && cacheINDMarketProfile50Charts[idx].MarketOpen == marketOpen && cacheINDMarketProfile50Charts[idx].MarketClose == marketClose && cacheINDMarketProfile50Charts[idx].SessionLabel == sessionLabel && cacheINDMarketProfile50Charts[idx].SplitLatestDay == splitLatestDay && cacheINDMarketProfile50Charts[idx].ShowVolumeProfile == showVolumeProfile && cacheINDMarketProfile50Charts[idx].ShowInitialBalance == showInitialBalance && cacheINDMarketProfile50Charts[idx].ShowSinglePrints == showSinglePrints && cacheINDMarketProfile50Charts[idx].ShowPoorHighLow == showPoorHighLow && cacheINDMarketProfile50Charts[idx].ProfileWidth == profileWidth && cacheINDMarketProfile50Charts[idx].FontSize == fontSize && cacheINDMarketProfile50Charts[idx].ShowPoorHigh == showPoorHigh && cacheINDMarketProfile50Charts[idx].ShowPoorLow == showPoorLow && cacheINDMarketProfile50Charts[idx].ShowSwingHigh == showSwingHigh && cacheINDMarketProfile50Charts[idx].ShowSwingLow == showSwingLow && cacheINDMarketProfile50Charts[idx].ShowVirginVAH == showVirginVAH && cacheINDMarketProfile50Charts[idx].ShowVirginVAL == showVirginVAL && cacheINDMarketProfile50Charts[idx].ShowVirginVPoC == showVirginVPoC && cacheINDMarketProfile50Charts[idx].ShowVirginVPPoC == showVirginVPPoC && cacheINDMarketProfile50Charts[idx].ShowSummary == showSummary && cacheINDMarketProfile50Charts[idx].FixSummaryBoxAtBottom == fixSummaryBoxAtBottom && cacheINDMarketProfile50Charts[idx].MinimizeSummaryBoxes == minimizeSummaryBoxes && cacheINDMarketProfile50Charts[idx].ShowTopLeftBox == showTopLeftBox && cacheINDMarketProfile50Charts[idx].ShowLogoInTopBox == showLogoInTopBox && cacheINDMarketProfile50Charts[idx].MinimizeTopLeftBox == minimizeTopLeftBox && cacheINDMarketProfile50Charts[idx].ShowChartButtons == showChartButtons && cacheINDMarketProfile50Charts[idx].EqualsInput(input))
						return cacheINDMarketProfile50Charts[idx];
			return CacheIndicator<INDMarketProfile50Charts>(new INDMarketProfile50Charts(){ TpoSize = tpoSize, TpoIntervalMinutes = tpoIntervalMinutes, ValueAreaPercent = valueAreaPercent, InitialBalanceMinutes = initialBalanceMinutes, DaysToDisplay = daysToDisplay, FilterByTradingHours = filterByTradingHours, MarketOpen = marketOpen, MarketClose = marketClose, SessionLabel = sessionLabel, SplitLatestDay = splitLatestDay, ShowVolumeProfile = showVolumeProfile, ShowInitialBalance = showInitialBalance, ShowSinglePrints = showSinglePrints, ShowPoorHighLow = showPoorHighLow, ProfileWidth = profileWidth, FontSize = fontSize, ShowPoorHigh = showPoorHigh, ShowPoorLow = showPoorLow, ShowSwingHigh = showSwingHigh, ShowSwingLow = showSwingLow, ShowVirginVAH = showVirginVAH, ShowVirginVAL = showVirginVAL, ShowVirginVPoC = showVirginVPoC, ShowVirginVPPoC = showVirginVPPoC, ShowSummary = showSummary, FixSummaryBoxAtBottom = fixSummaryBoxAtBottom, MinimizeSummaryBoxes = minimizeSummaryBoxes, ShowTopLeftBox = showTopLeftBox, ShowLogoInTopBox = showLogoInTopBox, MinimizeTopLeftBox = minimizeTopLeftBox, ShowChartButtons = showChartButtons }, input, ref cacheINDMarketProfile50Charts);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.INDMarketProfile50Charts INDMarketProfile50Charts(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return indicator.INDMarketProfile50Charts(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}

		public Indicators.INDMarketProfile50Charts INDMarketProfile50Charts(ISeries<double> input , double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return indicator.INDMarketProfile50Charts(input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.INDMarketProfile50Charts INDMarketProfile50Charts(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return indicator.INDMarketProfile50Charts(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}

		public Indicators.INDMarketProfile50Charts INDMarketProfile50Charts(ISeries<double> input , double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return indicator.INDMarketProfile50Charts(input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}
	}
}

#endregion
