#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	/// <summary>
	/// AK10XGlobalOrderflow — Exact GoCharting-Style Buyer & Seller Candle Orderflow for Global Markets (CME / Forex / Indices / Commodities)
	/// - Seller Volume (Left) | Slim Buyer (Teal-Green) / Seller (Red) Center Candle Spine | Buyer Volume (Right)
	/// - Borderless open layout with Solid Amber/Orange (#F59E0B) Diagonal Imbalance blocks
	/// - Single-row GoCharting Intensity-Shaded Green/Red Bottom Delta Strip
	/// - Smooth Mouse-Wheel Zoom Out (Standard Buyer/Seller Candlesticks) <-> Zoom In (Orderflow Footprint)
	/// </summary>
	public class AK10XGlobalOrderflow : Indicator
	{
		#region Data Structures
		private sealed class FootprintLevel
		{
			public double Price;
			public long BidVolume;
			public long AskVolume;
			public long TotalVolume => BidVolume + AskVolume;
			public long Delta => AskVolume - BidVolume;
			public bool HasBidImbalance;
			public bool HasAskImbalance;
			public bool InValueArea;
			public bool IsPoc;
		}

		private sealed class FootprintBarData
		{
			public int BarIndex;
			public DateTime Time;
			public double Open;
			public double High;
			public double Low;
			public double Close;
			public long TotalVolume;
			public long BuyVolume;
			public long SellVolume;
			public long Delta;
			public long CumulativeDelta;
			public long MinDelta;
			public long MaxDelta;
			public double PocPrice;
			public double VaHigh;
			public double VaLow;
			public double DevSessionPoc;
			public double DevSessionVah;
			public double DevSessionVal;
			public double EffectivePriceStep;
			public List<FootprintLevel> Levels = new List<FootprintLevel>();
			public bool IsLiveAccumulated;
		}

		private sealed class LiveTickAccumulator
		{
			public Dictionary<long, long> BidByTickKey = new Dictionary<long, long>();
			public Dictionary<long, long> AskByTickKey = new Dictionary<long, long>();
			public long RunningDelta;
			public long MinDelta;
			public long MaxDelta;
			public long TotalTickVolume;
		}

		private sealed class ImbalanceZone
		{
			public int StartBarIndex;
			public int EndBarIndex;
			public bool IsBroken;
			public bool IsDemandAskZone;
			public double TopPrice;
			public double BottomPrice;
		}

		private struct SubBar1M
		{
			public DateTime Time;
			public double Open;
			public double High;
			public double Low;
			public double Close;
			public long Volume;
		}

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
		#endregion

		#region Fields
		private readonly Dictionary<int, FootprintBarData> barCache = new Dictionary<int, FootprintBarData>();
		private readonly Dictionary<int, LiveTickAccumulator> liveAccumulators = new Dictionary<int, LiveTickAccumulator>();
		private readonly Dictionary<DateTime, LiveTickAccumulator> historicalTickBySlot = new Dictionary<DateTime, LiveTickAccumulator>();
		private readonly Dictionary<DateTime, List<SubBar1M>> subBarsBySlot = new Dictionary<DateTime, List<SubBar1M>>();
		private readonly object dataLock = new object();

		private double lastBidPrice;
		private double lastAskPrice;
		private double lastTradePrice;
		private int lastTickDirection = 1;
		private DateTime lastNativeOnMarketDataUtc = DateTime.MinValue;

		private int lastComputedBarCount = -1;
		private double lastComputedLastClose = double.NaN;
		private long lastComputedLastVol = -1;
		private double lastComputedUniformStep = -1.0;
		private int lastEffectiveAutoTicks = 10;
		private int runtimeTicksPerLevel = 4;
		private bool runtimeAutoStep = true;
		private bool runtimeShowStatGrid = true;
		private bool runtimeShowImbZones = false;
		private bool runtimeShowHalfpack = true;
		private bool runtimeLightTheme = false;
		private AK10XOrderflowMode runtimeOrderflowMode = AK10XOrderflowMode.VolumeFootprint;
		private AK10XOrderflowDataFeed? runtimeDataFeedOverride;
		private double runtimeYZoomFactor = 1.0;
		private bool initialBarWidthApplied = false;

		private readonly RectangleF[] toolbarBtnRects = new RectangleF[15];
		private bool mouseSubscribed = false;
		private ChartControl hookedOrderflowCC = null;
		private UIElement hookedOwnerChart = null;
		private MouseButtonEventHandler ofMouseLeftDownHandler = null;
		private MouseWheelEventHandler ofMouseWheelHandler = null;
		private MouseWheelEventHandler ofOwnerChartWheelHandler = null;
		private System.Windows.Controls.Primitives.ScrollBar hookedOfScrollBar = null;
		private System.Windows.Forms.Form hookedD2DForm = null;
		private bool isDraggingFootprintCanvas = false;
		private float ofDragStartMouseX = 0f;
		private double ofDragStartScrollVal = 0.0;
		private int hoveredToolbarBtnIndex = -1;
		private int lastHandledOfMouseTimestamp = -1;
		private long lastWheelHandledTicks = 0;
		private DateTime lastBtnClickUtc = DateTime.MinValue;

		private static readonly MethodInfo cachedInitSlotsPainted = typeof(ChartControl).GetMethod("InitSlotsPainted", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly MethodInfo cachedInitScrollbar = typeof(ChartControl).GetMethod("InitScrollbar", BindingFlags.Instance | BindingFlags.NonPublic);

		private BarsRequest activeFeed1MReq;
		private Instrument subscribedLiveInstrument;
		private bool isConnectionStatusHooked;
		private bool liveStreamInitialized;
		private DateTime lastLiveRefreshUtc = DateTime.MinValue;

		private AK10XOrderflowDataFeed EffectiveDataFeed
		{
			get { return runtimeDataFeedOverride.HasValue ? runtimeDataFeedOverride.Value : DataFeed; }
		}

		public override string DisplayName
		{
			get { return string.Empty; }
		}
		#endregion

		#region Properties
		[NinjaScriptProperty]
		[Display(Name = "Data Feed", Description = "Choose Accelpix Feed (NSE/BSE/MCX) or NinjaTrader Feed (CME/Forex/Global)", Order = 0, GroupName = "0. Data Feed & Mode")]
		public AK10XOrderflowDataFeed DataFeed { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Orderflow Display Mode", Description = "Imbalance (Exact GoCharting Borderless Spine + Amber Imbalances), VolumeFootprint, Delta, or TotalVolume", Order = 1, GroupName = "0. Data Feed & Mode")]
		public AK10XOrderflowMode FootprintMode { get; set; }

		[NinjaScriptProperty]
		[Range(20, 80)]
		[Display(Name = "Candlestick Zoom Threshold (px)", Description = "Below this bar width (zoom out), bars render as standard Buyer/Seller Candlesticks; above it (zoom in), they expand into GoCharting Orderflow", Order = 2, GroupName = "0. Data Feed & Mode")]
		public int CandlestickZoomThresholdPx { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "GoCharting White Theme", Description = "Enable crisp white GoCharting background theme (or toggle anytime from the top bar)", Order = 3, GroupName = "0. Data Feed & Mode")]
		public bool UseLightTheme { get; set; }

		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Ticks Per Level", Description = "Manual price grouping in ticks when Auto-Scale is off", Order = 1, GroupName = "1. Footprint Core")]
		public int TicksPerLevel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Auto-Scale Price Step", Description = "Automatically scales price step to visible chart height so every row is ~19px tall and crystal-clear", Order = 2, GroupName = "1. Footprint Core")]
		public bool AutoScalePriceStep { get; set; }

		[NinjaScriptProperty]
		[Range(40, 220)]
		[Display(Name = "Default Footprint Width (px)", Description = "Initial bar spacing in Orderflow view (default = 96px)", Order = 3, GroupName = "1. Footprint Core")]
		public int BarSpacePx { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Bar POC", Description = "Highlight highest-volume price level (Point of Control) on each bar", Order = 1, GroupName = "2. POC, Halfpack & Value Area")]
		public bool ShowBarPOC { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Halfpack", Description = "Show 50% Halfpack line across every candle in Footprint and Imbalance modes (Enabled by default)", Order = 2, GroupName = "2. POC, Halfpack & Value Area")]
		public bool ShowHalfpack { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Bar Value Area (70%)", Description = "Subtle Value Area highlight", Order = 3, GroupName = "2. POC, Halfpack & Value Area")]
		public bool ShowBarVA { get; set; }

		[NinjaScriptProperty]
		[Range(50, 95)]
		[Display(Name = "Bar Value Area %", Description = "Bar Value Area percentage (default 70%)", Order = 4, GroupName = "2. POC, Halfpack & Value Area")]
		public int BarVAPercentage { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Developing Session VA & POC", Description = "Plot Developing Session POC and VAH/VAL step lines", Order = 5, GroupName = "2. POC, Halfpack & Value Area")]
		public bool ShowSessionVA { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Diagonal Imbalances", Description = "Highlight aggressive Buyer (Ask) and Seller (Bid) diagonal imbalances with GoCharting Amber blocks", Order = 1, GroupName = "3. Imbalances & S/R Zones")]
		public bool ShowImbalance { get; set; }

		[NinjaScriptProperty]
		[Range(150, 1000)]
		[Display(Name = "Imbalance Ratio %", Description = "Diagonal imbalance ratio percentage (GoCharting default = 300%)", Order = 2, GroupName = "3. Imbalances & S/R Zones")]
		public double ImbalancePercentage { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10000)]
		[Display(Name = "Imbalance Min Volume Filter", Description = "Minimum level volume required to trigger an imbalance", Order = 3, GroupName = "3. Imbalances & S/R Zones")]
		public int ImbalanceFilter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Stacked Imbalance Zones", Description = "Extend Support/Resistance zones from consecutive stacked imbalances", Order = 4, GroupName = "3. Imbalances & S/R Zones")]
		public bool ShowImbalanceSRZones { get; set; }

		[NinjaScriptProperty]
		[Range(2, 6)]
		[Display(Name = "Consecutive Imbalance Levels", Description = "Minimum consecutive imbalance rows to form a Stacked Imbalance zone (default = 3)", Order = 5, GroupName = "3. Imbalances & S/R Zones")]
		public int ImbalanceSRConsecutiveLevels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Box Top Delta & Bottom Height", Description = "Show Delta pill above each candle box and Candle Height (High - Low) pill below each box", Order = 1, GroupName = "4. Top/Bottom Badges & 3-Row Table")]
		public bool ShowBarDelta { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show 3-Row Bottom Table", Description = "Show 3-row bottom table: Momentum (BMC/SMC), Strength (Buyer/Seller), and Delta", Order = 2, GroupName = "4. Top/Bottom Badges & 3-Row Table")]
		public bool ShowStatisticGrid { get; set; }

		[NinjaScriptProperty]
		[Range(5, 45)]
		[Display(Name = "Momentum Small Wick Max %", Description = "Maximum top wick (BMC) or bottom wick (SMC) % of candle height for Momentum classification (default = 25%)", Order = 3, GroupName = "4. Top/Bottom Badges & 3-Row Table")]
		public int MomentumMaxWickPct { get; set; }
		#endregion

		#region Lifecycle & Dual Data Feed Engine (Accelpix / NinjaTrader)
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "AK10X GoCharting-Style Buyer & Seller Candle Orderflow Indicator for Global Markets (NinjaTrader Feed Default + Data Feed Selector)";
				Name = "AK10XGlobalOrderflow";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				PaintPriceMarkers = true;
				IsSuspendedWhileInactive = false;

				DataFeed = AK10XOrderflowDataFeed.NinjaTrader;
				FootprintMode = AK10XOrderflowMode.VolumeFootprint;
				CandlestickZoomThresholdPx = 38;
				UseLightTheme = false;

				TicksPerLevel = 4;
				AutoScalePriceStep = true;
				BarSpacePx = 96;

				ShowBarPOC = true;
				ShowHalfpack = true;
				ShowBarVA = false;
				BarVAPercentage = 70;
				ShowSessionVA = false;

				ShowImbalance = true;
				ImbalancePercentage = 300.0;
				ImbalanceFilter = 3;
				ShowImbalanceSRZones = false;
				ImbalanceSRConsecutiveLevels = 3;

				ShowBarDelta = true;
				ShowStatisticGrid = true;
				MomentumMaxWickPct = 25;
			}
			else if (State == State.DataLoaded)
			{
				lock (dataLock)
				{
					barCache.Clear();
					liveAccumulators.Clear();
					historicalTickBySlot.Clear();
					subBarsBySlot.Clear();
					lastComputedBarCount = -1;
					lastComputedLastClose = double.NaN;
					lastComputedLastVol = -1;
					lastComputedUniformStep = -1.0;
				}
				runtimeDataFeedOverride = null;
				runtimeOrderflowMode = FootprintMode;
				runtimeLightTheme = UseLightTheme;
				runtimeTicksPerLevel = Math.Max(1, TicksPerLevel);
				lastEffectiveAutoTicks = runtimeTicksPerLevel;
				runtimeAutoStep = AutoScalePriceStep;
				runtimeShowStatGrid = ShowStatisticGrid;
				runtimeShowImbZones = ShowImbalanceSRZones;
				runtimeShowHalfpack = ShowHalfpack;
				runtimeYZoomFactor = 1.0;
				liveStreamInitialized = true;
				EnsureMouseEventsSubscribed();
				AttachSelectedDataFeedStream();
				RequestSelectedDataFeedBarsAsync();
			}
			else if (State == State.Realtime)
			{
				if (!liveStreamInitialized)
				{
					liveStreamInitialized = true;
					AttachSelectedDataFeedStream();
					RequestSelectedDataFeedBarsAsync();
				}
			}
			else if (State == State.Terminated)
			{
				DetachSelectedDataFeedStream();
				UnsubscribeMouseEvents();
			}
		}

		private int GetChartPeriodMinutes()
		{
			try
			{
				if (BarsPeriod != null && BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value > 0)
					return BarsPeriod.Value;
				if (ChartBars != null && ChartBars.Properties != null && ChartBars.Properties.BarsPeriod != null
					&& ChartBars.Properties.BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && ChartBars.Properties.BarsPeriod.Value > 0)
					return ChartBars.Properties.BarsPeriod.Value;
			}
			catch { }
			return 5;
		}

		private DateTime GetBarSlotKey(DateTime t)
		{
			int pMin = Math.Max(1, GetChartPeriodMinutes());
			if (pMin <= 1)
				return new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Kind);
			int totalMin = t.Hour * 60 + t.Minute;
			int rem = totalMin % pMin;
			int slotMin = rem == 0 ? totalMin : (totalMin + (pMin - rem));
			return t.Date.AddMinutes(slotMin);
		}

		private DateTime GetTickSlotKey(DateTime tickTime)
		{
			int pMin = Math.Max(1, GetChartPeriodMinutes());
			int totalMin = tickTime.Hour * 60 + tickTime.Minute;
			int slotMin = ((totalMin / pMin) + 1) * pMin;
			if (tickTime.Second == 0 && tickTime.Millisecond == 0 && (totalMin % pMin == 0) && totalMin > 0)
				slotMin = totalMin;
			return tickTime.Date.AddMinutes(slotMin);
		}

		private void SwitchDataFeedLive(AK10XOrderflowDataFeed nextFeed)
		{
			DetachSelectedDataFeedStream();
			lock (dataLock)
			{
				runtimeDataFeedOverride = nextFeed;
				DataFeed = nextFeed;
				subBarsBySlot.Clear();
				historicalTickBySlot.Clear();
				liveAccumulators.Clear();
				barCache.Clear();
				lastComputedBarCount = -1;
				lastComputedLastClose = double.NaN;
				lastComputedUniformStep = -1.0;
			}
			AttachSelectedDataFeedStream();
			RequestSelectedDataFeedBarsAsync();
			RequestImmediateOrderflowRedraw();
		}

		private void EnsureAccelpixProviderConnected()
		{
			try
			{
				bool alreadyConnected = false;
				lock (Connection.Connections)
				{
					foreach (Connection c in Connection.Connections)
					{
						if (c != null && c.Options != null &&
							(c.Options.Name.IndexOf("Accel", StringComparison.OrdinalIgnoreCase) >= 0 ||
							 c.Options.TypeName.IndexOf("SampleOpenConnect", StringComparison.OrdinalIgnoreCase) >= 0))
						{
							if (c.Status == ConnectionStatus.Connected || c.Status == ConnectionStatus.Connecting)
								alreadyConnected = true;
							break;
						}
					}
				}
				if (alreadyConnected)
					return;

				ConnectOptions accelOpt = null;
				if (Globals.ConnectOptions != null)
				{
					foreach (ConnectOptions opt in Globals.ConnectOptions)
					{
						if (opt != null &&
							(opt.Name != null && opt.Name.IndexOf("Accel", StringComparison.OrdinalIgnoreCase) >= 0 ||
							 opt.TypeName != null && opt.TypeName.IndexOf("SampleOpenConnect", StringComparison.OrdinalIgnoreCase) >= 0))
						{
							accelOpt = opt;
							opt.ConnectOnStartup = true;
							break;
						}
					}
				}
				if (accelOpt != null)
				{
					System.Threading.Tasks.Task.Run(() =>
					{
						try { Connection.Connect(accelOpt); } catch { }
					});
				}
			}
			catch { }
		}

		private void AttachSelectedDataFeedStream()
		{
			try
			{
				if (EffectiveDataFeed == AK10XOrderflowDataFeed.Accelpix)
				{
					EnsureAccelpixProviderConnected();
				}

				if (!isConnectionStatusHooked)
				{
					Connection.ConnectionStatusUpdate += OnDataFeedConnectionStatusUpdate;
					isConnectionStatusHooked = true;
				}

				Instrument inst = Instrument;
				if (inst != null && subscribedLiveInstrument != inst)
				{
					if (subscribedLiveInstrument != null)
					{
						try { subscribedLiveInstrument.MarketDataUpdate -= OnInstrumentMarketDataUpdate; } catch { }
					}
					inst.MarketDataUpdate += OnInstrumentMarketDataUpdate;
					subscribedLiveInstrument = inst;
				}
			}
			catch { }
		}

		private void DetachSelectedDataFeedStream()
		{
			try
			{
				if (isConnectionStatusHooked)
				{
					Connection.ConnectionStatusUpdate -= OnDataFeedConnectionStatusUpdate;
					isConnectionStatusHooked = false;
				}
			}
			catch { }

			try
			{
				if (subscribedLiveInstrument != null)
				{
					try { subscribedLiveInstrument.MarketDataUpdate -= OnInstrumentMarketDataUpdate; } catch { }
					subscribedLiveInstrument = null;
				}
			}
			catch { }

			try
			{
				BarsRequest req1 = activeFeed1MReq;
				activeFeed1MReq = null;
				if (req1 != null)
				{
					req1.Update -= OnFeed1MBarsUpdate;
					req1.Dispose();
				}
			}
			catch { }
		}

		private void OnInstrumentMarketDataUpdate(object sender, MarketDataEventArgs e)
		{
			if (State != State.Realtime)
				return;
			if ((DateTime.UtcNow - lastNativeOnMarketDataUtc).TotalSeconds < 2.0)
				return;
			ProcessLiveTickEvent(e, false);
		}

		private void OnDataFeedConnectionStatusUpdate(object sender, ConnectionStatusEventArgs e)
		{
			try
			{
				if (e == null || State == State.Terminated)
					return;
				if (e.Status == ConnectionStatus.Connected || e.PriceStatus == ConnectionStatus.Connected)
				{
					RequestSelectedDataFeedBarsAsync();
				}
			}
			catch { }
		}

		private TradingHours ResolveFeedTradingHours(Instrument inst, bool useAccelpix)
		{
			try
			{
				if (ChartBars != null && ChartBars.Bars != null && ChartBars.Bars.TradingHours != null)
					return ChartBars.Bars.TradingHours;
			}
			catch { }

			if (inst == null)
				return TradingHours.Get("Default 24 x 7");

			if (useAccelpix)
			{
				string fn = inst.FullName ?? string.Empty;
				if (fn.EndsWith("_I", StringComparison.OrdinalIgnoreCase) || fn.IndexOf("NIFTY", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					try
					{
						TradingHours mcxTh = TradingHours.Get("MCX");
						if (mcxTh != null)
							return mcxTh;
					}
					catch { }
				}
			}

			if (inst.MasterInstrument != null && inst.MasterInstrument.TradingHours != null)
				return inst.MasterInstrument.TradingHours;

			return TradingHours.Get("Default 24 x 7");
		}

		private MergePolicy ResolveChartMergePolicy(Instrument inst)
		{
			try
			{
				if (ChartBars != null && ChartBars.Properties != null)
				{
					PropertyInfo mpProp = ChartBars.Properties.GetType().GetProperty("MergePolicy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (mpProp != null && mpProp.PropertyType == typeof(MergePolicy))
					{
						MergePolicy chartMp = (MergePolicy)mpProp.GetValue(ChartBars.Properties, null);
						if (chartMp != MergePolicy.UseGlobalSettings)
							return chartMp;
					}
				}
				if (inst != null && inst.MasterInstrument != null && inst.MasterInstrument.MergePolicy != MergePolicy.UseGlobalSettings)
					return inst.MasterInstrument.MergePolicy;
				if (Globals.MarketDataOptions != null)
					return Globals.MarketDataOptions.GlobalMergePolicy;
			}
			catch { }
			return MergePolicy.MergeBackAdjusted;
		}

		private void RequestSelectedDataFeedBarsAsync()
		{
			try
			{
				Instrument inst = Instrument;
				if (inst == null || State == State.Terminated)
					return;

				bool useAccelpix = (EffectiveDataFeed == AK10XOrderflowDataFeed.Accelpix);
				double tick = TickSize > 0 ? TickSize : (inst.MasterInstrument != null && inst.MasterInstrument.TickSize > 0 ? inst.MasterInstrument.TickSize : 0.05);
				MergePolicy chartMerge = ResolveChartMergePolicy(inst);

				System.Threading.Tasks.Task.Run(() =>
				{
					try
					{
						if (State == State.Terminated)
							return;

						TradingHours reqTh = ResolveFeedTradingHours(inst, useAccelpix);
						DateTime endDate = DateTime.Now.Date.AddDays(1);

						if (!useAccelpix)
						{
							foreach (LookupPolicies pol in new LookupPolicies[] { LookupPolicies.Repository, LookupPolicies.Provider })
							{
								if (State == State.Terminated)
									return;
								int existingTickSlots;
								lock (dataLock) { existingTickSlots = historicalTickBySlot.Count; }
								if (pol == LookupPolicies.Provider && existingTickSlots >= 24)
									break;

								try
								{
									using (var doneTick = new System.Threading.ManualResetEventSlim(false))
									using (var tickReq = new BarsRequest(inst, DateTime.Now.Date.AddDays(-2), endDate))
									{
										tickReq.BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Tick, Value = 1 };
										tickReq.LookupPolicy = pol;
										tickReq.MergePolicy = chartMerge;
										if (reqTh != null) tickReq.TradingHours = reqTh;
										tickReq.Request((tb, err, msg) =>
										{
											try
											{
												if (err == ErrorCode.NoError && tb != null && tb.Bars != null && tb.Bars.Count > 0)
													IngestNinjaTraderHistoricalTicks(tb.Bars, tick);
											}
											catch { }
											finally
											{
												try { doneTick.Set(); } catch { }
											}
										});
										doneTick.Wait(pol == LookupPolicies.Repository ? 1000 : 3000);
									}
								}
								catch { }
							}
						}

						if (State == State.Terminated)
							return;

						DateTime startDate = endDate.AddDays(useAccelpix ? -4 : -5);
						BarsRequest req1 = new BarsRequest(inst, startDate, endDate)
						{
							BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 1 },
							MergePolicy = chartMerge,
							TradingHours = reqTh
						};
						req1.Update += OnFeed1MBarsUpdate;
						BarsRequest old1 = activeFeed1MReq;
						activeFeed1MReq = req1;
						if (old1 != null)
						{
							try { old1.Update -= OnFeed1MBarsUpdate; old1.Dispose(); } catch { }
						}

						req1.Request((bars, errorCode, errorMessage) =>
						{
							try
							{
								if (State == State.Terminated || errorCode != ErrorCode.NoError || bars == null || bars.Bars == null || bars.Bars.Count <= 0)
									return;

								IngestFeed1MBars(bars.Bars, useAccelpix);
							}
							catch { }
						});
					}
					catch { }
				});
			}
			catch { }
		}

		private void IngestNinjaTraderHistoricalTicks(Bars bTick, double tickSize)
		{
			if (bTick == null || bTick.Count <= 0)
				return;

			double tick = tickSize > 0 ? tickSize : 0.01;
			Dictionary<DateTime, LiveTickAccumulator> built = new Dictionary<DateTime, LiveTickAccumulator>();
			int count = bTick.Count;

			int dir = 1;
			double firstPx = bTick.GetClose(0);
			for (int k = 1; k < Math.Min(count, 25); k++)
			{
				double kp = bTick.GetClose(k);
				if (Math.Abs(kp - firstPx) > 1e-9)
				{
					dir = kp > firstPx ? 1 : -1;
					break;
				}
			}

			double prevPx = 0.0;
			for (int i = 0; i < count; i++)
			{
				DateTime t = bTick.GetTime(i);
				double px = bTick.GetClose(i);
				if (px <= 0)
					continue;
				long vol = Math.Max(1L, bTick.GetVolume(i));
				double bid = 0.0;
				double ask = 0.0;
				try
				{
					bid = bTick.GetBid(i);
					ask = bTick.GetAsk(i);
				}
				catch { }

				if (prevPx > 0)
				{
					if (px > prevPx + 1e-9) dir = 1;
					else if (px < prevPx - 1e-9) dir = -1;
				}
				prevPx = px;

				bool isBuy;
				if (ask > 0 && bid > 0 && ask > bid + tick * 0.15)
				{
					double mid = (ask + bid) * 0.5;
					if (px >= ask - 1e-9) isBuy = true;
					else if (px <= bid + 1e-9) isBuy = false;
					else if (Math.Abs(px - mid) > tick * 0.1) isBuy = (px > mid);
					else isBuy = (dir >= 0);
				}
				else
				{
					isBuy = (dir >= 0);
				}

				DateTime slot = GetTickSlotKey(t);
				if (!built.TryGetValue(slot, out LiveTickAccumulator acc))
				{
					acc = new LiveTickAccumulator();
					built[slot] = acc;
				}

				long tickKey = (long)Math.Round(px / tick);
				if (isBuy)
				{
					acc.AskByTickKey.TryGetValue(tickKey, out long cur);
					acc.AskByTickKey[tickKey] = cur + vol;
					acc.RunningDelta += vol;
				}
				else
				{
					acc.BidByTickKey.TryGetValue(tickKey, out long cur);
					acc.BidByTickKey[tickKey] = cur + vol;
					acc.RunningDelta -= vol;
				}
				acc.TotalTickVolume += vol;
				if (acc.RunningDelta < acc.MinDelta) acc.MinDelta = acc.RunningDelta;
				if (acc.RunningDelta > acc.MaxDelta) acc.MaxDelta = acc.RunningDelta;
			}

			if (built.Count == 0)
				return;

			lock (dataLock)
			{
				foreach (var kv in built)
					historicalTickBySlot[kv.Key] = kv.Value;
				barCache.Clear();
				lastComputedBarCount = -1;
			}

			RequestThrottledChartRefresh(true);
		}

		private void IngestFeed1MBars(Bars b1m, bool allowCumulativeAutoDetect)
		{
			if (b1m == null || b1m.Count <= 0)
				return;

			lock (dataLock)
			{
				subBarsBySlot.Clear();
				int count = b1m.Count;

				bool isCumulative = false;
				if (allowCumulativeAutoDetect && count >= 12)
				{
					int sameDayPairs = 0;
					int nonDecreasing = 0;
					for (int i = 1; i < count; i++)
					{
						if (b1m.GetTime(i).Date == b1m.GetTime(i - 1).Date)
						{
							sameDayPairs++;
							if (b1m.GetVolume(i) >= b1m.GetVolume(i - 1))
								nonDecreasing++;
						}
					}
					if (sameDayPairs >= 10 && nonDecreasing >= (int)(sameDayPairs * 0.92))
						isCumulative = true;
				}

				DateTime prevDate = DateTime.MinValue;
				long prevCumVol = 0;

				for (int i = 0; i < count; i++)
				{
					DateTime t = b1m.GetTime(i);
					long rawVol = Math.Max(0L, b1m.GetVolume(i));
					if (t.Date != prevDate)
					{
						prevDate = t.Date;
						prevCumVol = 0;
					}
					long barVol = isCumulative
						? (rawVol >= prevCumVol ? (rawVol - prevCumVol) : rawVol)
						: rawVol;
					prevCumVol = rawVol;

					DateTime slot = GetBarSlotKey(t);
					if (!subBarsBySlot.TryGetValue(slot, out List<SubBar1M> list))
					{
						list = new List<SubBar1M>(5);
						subBarsBySlot[slot] = list;
					}
					list.Add(new SubBar1M
					{
						Time = t,
						Open = b1m.GetOpen(i),
						High = b1m.GetHigh(i),
						Low = b1m.GetLow(i),
						Close = b1m.GetClose(i),
						Volume = Math.Max(1L, barVol)
					});
				}

				barCache.Clear();
				lastComputedBarCount = -1;
			}

			RequestThrottledChartRefresh(true);
		}

		private void OnFeed1MBarsUpdate(object sender, BarsUpdateEventArgs e)
		{
			try
			{
				if (e == null || e.BarsSeries == null || State == State.Terminated)
					return;
				int maxIdx = Math.Min(e.MaxIndex, e.BarsSeries.Count - 1);
				if (maxIdx < 0)
					return;

				DateTime t = e.BarsSeries.GetTime(maxIdx);
				DateTime slot = GetBarSlotKey(t);
				double open = e.BarsSeries.GetOpen(maxIdx);
				double high = e.BarsSeries.GetHigh(maxIdx);
				double low = e.BarsSeries.GetLow(maxIdx);
				double close = e.BarsSeries.GetClose(maxIdx);
				long rawVol = Math.Max(1L, e.BarsSeries.GetVolume(maxIdx));

				lock (dataLock)
				{
					if (!subBarsBySlot.TryGetValue(slot, out List<SubBar1M> list))
					{
						list = new List<SubBar1M>(5);
						subBarsBySlot[slot] = list;
					}
					bool updated = false;
					for (int i = 0; i < list.Count; i++)
					{
						if (list[i].Time == t)
						{
							list[i] = new SubBar1M { Time = t, Open = open, High = high, Low = low, Close = close, Volume = rawVol };
							updated = true;
							break;
						}
					}
					if (!updated)
						list.Add(new SubBar1M { Time = t, Open = open, High = high, Low = low, Close = close, Volume = rawVol });

					lastTradePrice = close;
					if (lastComputedBarCount > 0)
						barCache.Remove(lastComputedBarCount - 1);
					lastComputedLastClose = double.NaN;
				}

				RequestThrottledChartRefresh(false);
			}
			catch { }
		}

		private void RequestThrottledChartRefresh(bool immediate)
		{
			try
			{
				if (!immediate && State != State.Realtime)
					return;

				DateTime nowUtc = DateTime.UtcNow;
				if (!immediate && (nowUtc - lastLiveRefreshUtc).TotalMilliseconds < 220)
					return;
				lastLiveRefreshUtc = nowUtc;

				ChartControl cc = ChartControl;
				if (cc != null)
				{
					cc.Dispatcher.BeginInvoke(new Action(() =>
					{
						try { cc.InvalidateVisual(); } catch { }
					}));
				}
			}
			catch { }
		}

		private static System.Windows.Controls.Primitives.ScrollBar ResolveOrderflowScrollBar(ChartControl cc)
		{
			if (cc == null)
				return null;
			try
			{
				object tab = cc.ChartTab;
				if (tab != null)
				{
					var sbField = tab.GetType().GetField("scrollBar", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
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

		private bool IsOrderflowChartControlActive(ChartControl cc)
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

		private float ConvertOfHorizontalPx(ChartControl cc, double dipX)
		{
			try
			{
				if (cc != null && cc.PresentationSource != null)
					return (float)ChartingExtensions.ConvertToHorizontalPixels(dipX, cc.PresentationSource);
			}
			catch { }
			return (float)dipX;
		}

		private float ConvertOfVerticalPx(ChartControl cc, double dipY)
		{
			try
			{
				if (cc != null && cc.PresentationSource != null)
					return (float)ChartingExtensions.ConvertToVerticalPixels(dipY, cc.PresentationSource);
			}
			catch { }
			return (float)dipY;
		}

		private void RequestImmediateOrderflowRedraw()
		{
			try { ForceRefresh(); } catch { }
			try
			{
				ChartControl cc = hookedOrderflowCC ?? ChartControl;
				if (cc != null)
				{
					cc.InvalidateVisual();
					if (cc.OwnerChart != null && cc.OwnerChart.Form != null)
						cc.OwnerChart.Form.Invalidate();
				}
			}
			catch { }
		}

		private bool isApplyingRightMargin;
		private bool isPendingOfCanvasDrag;

		private void SyncOneBoxRightGapMargin(ChartControl cc, float barSpacing, bool immediateOnUiThread)
		{
			if (cc == null || cc.Properties == null)
				return;

			int minRightMargin = runtimeShowStatGrid ? 92 : 28;
			int desiredMargin = Math.Max(minRightMargin, Math.Min(150, (int)Math.Round(barSpacing * 1.18f)));
			if (Math.Abs(cc.Properties.BarMarginRightUser - desiredMargin) <= 6)
				return;

			if (immediateOnUiThread)
			{
				try { cc.Properties.BarMarginRightUser = desiredMargin; } catch { }
				return;
			}

			if (isApplyingRightMargin)
				return;
			isApplyingRightMargin = true;
			try
			{
				cc.Dispatcher.InvokeAsync(() =>
				{
					try
					{
						if (cc.Properties != null && Math.Abs(cc.Properties.BarMarginRightUser - desiredMargin) > 6)
						{
							cc.Properties.BarMarginRightUser = desiredMargin;
							TriggerChartScrollbarRefresh();
							RequestImmediateOrderflowRedraw();
						}
					}
					catch { }
					finally
					{
						isApplyingRightMargin = false;
					}
				});
			}
			catch
			{
				isApplyingRightMargin = false;
			}
		}

		private static bool IsOfInteractiveUiControlSource(object originalSource)
		{
			try
			{
				DependencyObject cur = originalSource as DependencyObject;
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

					DependencyObject next = null;
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

		private void EnsureMouseEventsSubscribed()
		{
			ChartControl cc = ChartControl;
			if (cc == null)
				return;

			var curD2D = cc.OwnerChart != null ? cc.OwnerChart.Form : null;
			var curOwner = cc.OwnerChart as UIElement;

			if (mouseSubscribed &&
				hookedOrderflowCC == cc &&
				hookedOfScrollBar != null &&
				hookedD2DForm == curD2D &&
				hookedOwnerChart == curOwner)
			{
				return;
			}

			try
			{
				cc.Dispatcher.InvokeAsync(() =>
				{
					try
					{
						if (cc == null || State == State.Terminated)
							return;

						if (ofMouseLeftDownHandler == null)
							ofMouseLeftDownHandler = new MouseButtonEventHandler(OnChartMouseLeftButtonDown);
						if (ofMouseWheelHandler == null)
							ofMouseWheelHandler = new MouseWheelEventHandler(OnChartMouseWheel);
						if (ofOwnerChartWheelHandler == null)
							ofOwnerChartWheelHandler = new MouseWheelEventHandler(OnOwnerChartPreviewMouseWheel);

						if (!mouseSubscribed || hookedOrderflowCC != cc)
						{
							if (hookedOrderflowCC != null)
							{
								try { hookedOrderflowCC.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, ofMouseLeftDownHandler); } catch { }
								try { hookedOrderflowCC.RemoveHandler(UIElement.PreviewMouseWheelEvent, ofMouseWheelHandler); } catch { }
								hookedOrderflowCC.PreviewMouseMove -= OnChartMouseMove;
								hookedOrderflowCC.PreviewMouseLeftButtonUp -= OnChartMouseLeftButtonUp;
								hookedOrderflowCC.MouseLeave -= OnChartMouseLeave;
							}
							cc.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, ofMouseLeftDownHandler);
							cc.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, ofMouseLeftDownHandler, true);
							cc.RemoveHandler(UIElement.PreviewMouseWheelEvent, ofMouseWheelHandler);
							cc.AddHandler(UIElement.PreviewMouseWheelEvent, ofMouseWheelHandler, true);
							cc.PreviewMouseMove += OnChartMouseMove;
							cc.PreviewMouseLeftButtonUp += OnChartMouseLeftButtonUp;
							cc.MouseLeave += OnChartMouseLeave;
							hookedOrderflowCC = cc;
							mouseSubscribed = true;
						}

						UIElement ownerElem = cc.OwnerChart as UIElement;
						if (ownerElem != null && hookedOwnerChart != ownerElem)
						{
							if (hookedOwnerChart != null)
							{
								try { hookedOwnerChart.RemoveHandler(UIElement.PreviewMouseWheelEvent, ofOwnerChartWheelHandler); } catch { }
							}
							ownerElem.RemoveHandler(UIElement.PreviewMouseWheelEvent, ofOwnerChartWheelHandler);
							ownerElem.AddHandler(UIElement.PreviewMouseWheelEvent, ofOwnerChartWheelHandler, true);
							hookedOwnerChart = ownerElem;
						}

						if (cc.Properties != null)
						{
							float curDist = cc.Properties.BarDistance > 4f ? cc.Properties.BarDistance : BarSpacePx;
							SyncOneBoxRightGapMargin(cc, curDist, true);
						}

						if (hookedOfScrollBar == null)
							hookedOfScrollBar = ResolveOrderflowScrollBar(cc);

						if (cc.OwnerChart != null && cc.OwnerChart.Form != null && hookedD2DForm != cc.OwnerChart.Form)
						{
							if (hookedD2DForm != null)
							{
								hookedD2DForm.MouseDown -= OnD2DFormMouseDown;
								hookedD2DForm.MouseMove -= OnD2DFormMouseMove;
								hookedD2DForm.MouseUp -= OnD2DFormMouseUp;
								hookedD2DForm.MouseWheel -= OnD2DFormMouseWheel;
							}
							hookedD2DForm = cc.OwnerChart.Form;
							hookedD2DForm.MouseDown += OnD2DFormMouseDown;
							hookedD2DForm.MouseMove += OnD2DFormMouseMove;
							hookedD2DForm.MouseUp += OnD2DFormMouseUp;
							hookedD2DForm.MouseWheel += OnD2DFormMouseWheel;
						}
					}
					catch { }
				});
			}
			catch { }
		}

		private void UnsubscribeMouseEvents()
		{
			ChartControl cc = hookedOrderflowCC;
			UIElement ownerElem = hookedOwnerChart;
			MouseButtonEventHandler downHandler = ofMouseLeftDownHandler;
			MouseWheelEventHandler wheelHandler = ofMouseWheelHandler;
			MouseWheelEventHandler ownerWheelHandler = ofOwnerChartWheelHandler;
			var d2d = hookedD2DForm;
			if (cc != null)
			{
				try
				{
					cc.Dispatcher.InvokeAsync(() =>
					{
						if (downHandler != null)
						{
							try { cc.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, downHandler); } catch { }
						}
						if (wheelHandler != null)
						{
							try { cc.RemoveHandler(UIElement.PreviewMouseWheelEvent, wheelHandler); } catch { }
						}
						if (ownerElem != null && ownerWheelHandler != null)
						{
							try { ownerElem.RemoveHandler(UIElement.PreviewMouseWheelEvent, ownerWheelHandler); } catch { }
						}
						try { cc.PreviewMouseMove -= OnChartMouseMove; } catch { }
						try { cc.PreviewMouseLeftButtonUp -= OnChartMouseLeftButtonUp; } catch { }
						try { cc.MouseLeave -= OnChartMouseLeave; } catch { }
						if (d2d != null)
						{
							try { d2d.MouseDown -= OnD2DFormMouseDown; } catch { }
							try { d2d.MouseMove -= OnD2DFormMouseMove; } catch { }
							try { d2d.MouseUp -= OnD2DFormMouseUp; } catch { }
							try { d2d.MouseWheel -= OnD2DFormMouseWheel; } catch { }
						}
					});
				}
				catch { }
			}
			mouseSubscribed = false;
			hookedOrderflowCC = null;
			hookedOwnerChart = null;
			hookedOfScrollBar = null;
			hookedD2DForm = null;
		}

		private void OnD2DFormMouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
		{
			if (e == null || e.Button != System.Windows.Forms.MouseButtons.Left)
				return;
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (!IsOrderflowChartControlActive(cc))
				return;
			try
			{
				float pxX = e.X;
				float pxY = e.Y;
				if (cc.OwnerChart != null && cc.OwnerChart.Form != null && cc.OwnerChart.Form.IsHandleCreated)
				{
					Win32Point pt;
					if (GetCursorPos(out pt))
					{
						IntPtr hwnd = cc.OwnerChart.Form.Handle;
						if (hwnd != IntPtr.Zero && ScreenToClient(hwnd, ref pt))
						{
							pxX = pt.X;
							pxY = pt.Y;
						}
					}
				}

				if (HandleToolbarClick(pxX, pxY))
				{
					hoveredToolbarBtnIndex = -1;
					RequestImmediateOrderflowRedraw();
					return;
				}

				if (ChartPanel != null)
				{
					float panelLeft = (float)ChartPanel.X;
					float panelRight = (float)(ChartPanel.X + ChartPanel.W);
					float panelTop = (float)ChartPanel.Y;
					float panelBot = (float)(ChartPanel.Y + ChartPanel.H);
					if (pxX >= panelLeft && pxX <= panelRight - 50f && pxY >= panelTop + 4f && pxY <= panelBot - 4f)
					{
						var sb = hookedOfScrollBar ?? (hookedOfScrollBar = ResolveOrderflowScrollBar(cc));
						if (sb != null)
						{
							isPendingOfCanvasDrag = true;
							isDraggingFootprintCanvas = false;
							ofDragStartMouseX = pxX;
							ofDragStartScrollVal = sb.Value;
						}
					}
				}
			}
			catch { }
		}

		private void OnD2DFormMouseMove(object sender, System.Windows.Forms.MouseEventArgs e)
		{
			if (e == null)
				return;
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (!IsOrderflowChartControlActive(cc))
				return;

			float curPxX = e.X;
			float curPxY = e.Y;

			if (!isDraggingFootprintCanvas && !isPendingOfCanvasDrag)
			{
				int overIdx = HitTestToolbarButtonAt(curPxX, curPxY);
				if (overIdx != hoveredToolbarBtnIndex)
				{
					hoveredToolbarBtnIndex = overIdx;
					RequestImmediateOrderflowRedraw();
				}
				return;
			}

			if ((e.Button & System.Windows.Forms.MouseButtons.Left) != System.Windows.Forms.MouseButtons.Left)
			{
				isPendingOfCanvasDrag = false;
				isDraggingFootprintCanvas = false;
				return;
			}

			try
			{
				float deltaX = curPxX - ofDragStartMouseX;
				if (isPendingOfCanvasDrag && !isDraggingFootprintCanvas)
				{
					if (Math.Abs(deltaX) < 3.5f)
						return;
					isDraggingFootprintCanvas = true;
					isPendingOfCanvasDrag = false;
				}

				if (isDraggingFootprintCanvas && Math.Abs(deltaX) > 0.75f)
				{
					var sb = hookedOfScrollBar ?? (hookedOfScrollBar = ResolveOrderflowScrollBar(cc));
					if (sb != null && sb.Maximum > sb.Minimum)
					{
						float barDist = (cc.Properties != null && cc.Properties.BarDistance > 3f) ? cc.Properties.BarDistance : 48f;
						double barDelta = deltaX / barDist;
						double targetVal = Math.Max(sb.Minimum, Math.Min(sb.Maximum, ofDragStartScrollVal - barDelta));
						if (Math.Abs(sb.Value - targetVal) >= 0.015)
						{
							sb.Value = targetVal;
							RequestImmediateOrderflowRedraw();
						}
					}
				}
			}
			catch { }
		}

		private void OnD2DFormMouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
		{
			isPendingOfCanvasDrag = false;
			isDraggingFootprintCanvas = false;
		}

		private void OnD2DFormMouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
		{
			if (e == null || e.Delta == 0)
				return;
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (!IsOrderflowChartControlActive(cc))
				return;

			ProcessUniversalSmoothWheel(cc, e.Delta, e.X, true);
		}

		private void OnOwnerChartPreviewMouseWheel(object sender, MouseWheelEventArgs e)
		{
			if (e == null || e.Delta == 0)
				return;
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (!IsOrderflowChartControlActive(cc))
				return;
			if (IsOfInteractiveUiControlSource(e.OriginalSource))
				return;

			try
			{
				System.Windows.Point pt = e.GetPosition(cc);
				if (pt.X < -20 || pt.Y < -20 || pt.X > cc.ActualWidth + 80 || pt.Y > cc.ActualHeight + 40)
					return;
				float pxX = ConvertOfHorizontalPx(cc, pt.X);
				if (ProcessUniversalSmoothWheel(cc, e.Delta, pxX, true))
					e.Handled = true;
			}
			catch { }
		}

		private int HitTestToolbarButtonAt(float x, float y)
		{
			for (int i = 0; i < toolbarBtnRects.Length; i++)
			{
				if (ContainsPtPadded(toolbarBtnRects[i], x, y, 2f))
					return i;
			}
			return -1;
		}

		private int ResolveToolbarButtonFromMouseEvent(ChartControl cc, MouseEventArgs e)
		{
			if (cc == null || e == null)
				return -1;
			try
			{
				System.Windows.Point wpfPt = e.GetPosition(cc);
				float pxX = ConvertOfHorizontalPx(cc, wpfPt.X);
				float pxY = ConvertOfVerticalPx(cc, wpfPt.Y);
				int idx = HitTestToolbarButtonAt(pxX, pxY);
				if (idx >= 0) return idx;

				idx = HitTestToolbarButtonAt((float)wpfPt.X, (float)wpfPt.Y);
				if (idx >= 0) return idx;

				if (ChartPanel != null)
				{
					System.Windows.Point panelPt = e.GetPosition(ChartPanel);
					float pPxX = (float)ChartPanel.X + ConvertOfHorizontalPx(cc, panelPt.X);
					float pPxY = (float)ChartPanel.Y + ConvertOfVerticalPx(cc, panelPt.Y);
					idx = HitTestToolbarButtonAt(pPxX, pPxY);
					if (idx >= 0) return idx;

					float pDipX = (float)ChartPanel.X + (float)panelPt.X;
					float pDipY = (float)ChartPanel.Y + (float)panelPt.Y;
					idx = HitTestToolbarButtonAt(pDipX, pDipY);
					if (idx >= 0) return idx;
				}
			}
			catch { }
			return -1;
		}

		private void OnChartMouseLeave(object sender, MouseEventArgs e)
		{
			if (hoveredToolbarBtnIndex != -1)
			{
				hoveredToolbarBtnIndex = -1;
				RequestImmediateOrderflowRedraw();
			}
		}

		private void OnChartMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (!IsOrderflowChartControlActive(cc) || e == null)
				return;

			if (IsOfInteractiveUiControlSource(e.OriginalSource))
				return;

			if (e.Timestamp == lastHandledOfMouseTimestamp)
				return;

			try
			{
				cc.Focusable = true;
				Keyboard.Focus(cc);
			}
			catch { }

			int btnIdx = ResolveToolbarButtonFromMouseEvent(cc, e);
			if (btnIdx >= 0 && ExecuteToolbarButtonAction(btnIdx))
			{
				hoveredToolbarBtnIndex = -1;
				lastHandledOfMouseTimestamp = e.Timestamp;
				e.Handled = true;
				RequestImmediateOrderflowRedraw();
				return;
			}

			try
			{
				System.Windows.Point pt = e.GetPosition(cc);
				float pxX = ConvertOfHorizontalPx(cc, pt.X);
				float pxY = ConvertOfVerticalPx(cc, pt.Y);
				float panelLeft = (float)ChartPanel.X;
				float panelRight = (float)(ChartPanel.X + ChartPanel.W);
				float panelTop = (float)ChartPanel.Y;
				float panelBot = (float)(ChartPanel.Y + ChartPanel.H);
				if (pxX >= panelLeft && pxX <= panelRight - 55f && pxY >= panelTop + 4f && pxY <= panelBot - 4f)
				{
					var sb = hookedOfScrollBar ?? (hookedOfScrollBar = ResolveOrderflowScrollBar(cc));
					if (sb != null)
					{
						isPendingOfCanvasDrag = true;
						isDraggingFootprintCanvas = false;
						ofDragStartMouseX = pxX;
						ofDragStartScrollVal = sb.Value;
					}
				}
			}
			catch { }
		}

		private void OnChartMouseMove(object sender, MouseEventArgs e)
		{
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (!IsOrderflowChartControlActive(cc) || e == null)
				return;

			if (!isDraggingFootprintCanvas && !isPendingOfCanvasDrag)
			{
				try
				{
					int overIdx = ResolveToolbarButtonFromMouseEvent(cc, e);
					if (overIdx != hoveredToolbarBtnIndex)
					{
						hoveredToolbarBtnIndex = overIdx;
						RequestImmediateOrderflowRedraw();
					}
					if (overIdx >= 0 && cc.Cursor != Cursors.Hand)
						cc.Cursor = Cursors.Hand;
					else if (overIdx < 0 && (cc.Cursor == Cursors.Hand || cc.Cursor == Cursors.SizeWE))
						cc.Cursor = null;
				}
				catch { }
				return;
			}

			if (e.LeftButton != MouseButtonState.Pressed)
			{
				isPendingOfCanvasDrag = false;
				isDraggingFootprintCanvas = false;
				try
				{
					if (cc.IsMouseCaptured) cc.ReleaseMouseCapture();
					if (cc.Cursor == Cursors.SizeWE) cc.Cursor = null;
				}
				catch { }
				return;
			}

			try
			{
				float curPxX = ConvertOfHorizontalPx(cc, e.GetPosition(cc).X);
				float deltaX = curPxX - ofDragStartMouseX;
				if (isPendingOfCanvasDrag && !isDraggingFootprintCanvas)
				{
					if (Math.Abs(deltaX) < 3.5f)
						return;
					isDraggingFootprintCanvas = true;
					isPendingOfCanvasDrag = false;
					try { cc.CaptureMouse(); } catch { }
				}

				if (Math.Abs(deltaX) > 0.75f || cc.Cursor == Cursors.SizeWE)
				{
					if (cc.Cursor != Cursors.SizeWE)
						cc.Cursor = Cursors.SizeWE;
					var sb = hookedOfScrollBar ?? (hookedOfScrollBar = ResolveOrderflowScrollBar(cc));
					if (sb != null && sb.Maximum > sb.Minimum)
					{
						float barDist = (cc.Properties != null && cc.Properties.BarDistance > 3f) ? cc.Properties.BarDistance : 48f;
						double barDelta = deltaX / barDist;
						double targetVal = Math.Max(sb.Minimum, Math.Min(sb.Maximum, ofDragStartScrollVal - barDelta));
						if (Math.Abs(sb.Value - targetVal) >= 0.015)
						{
							sb.Value = targetVal;
							RequestImmediateOrderflowRedraw();
						}
					}
				}
			}
			catch { }
		}

		private void OnChartMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			isPendingOfCanvasDrag = false;
			isDraggingFootprintCanvas = false;
			try
			{
				ChartControl cc = hookedOrderflowCC ?? ChartControl;
				if (cc != null)
				{
					if (cc.IsMouseCaptured) cc.ReleaseMouseCapture();
					if (cc.Cursor == Cursors.SizeWE) cc.Cursor = null;
				}
			}
			catch { }
		}

		private static float ComputeSmoothNextZoomDistance(float curDist, int wheelDelta)
		{
			double notches = Math.Max(-3.5, Math.Min(3.5, wheelDelta / 120.0));
			if (Math.Abs(notches) < 0.04)
				notches = wheelDelta > 0 ? 0.20 : -0.20;

			float safeCur = Math.Max(4.0f, curDist);
			float next = safeCur * (float)Math.Pow(1.145, notches);
			if (Math.Abs(next - safeCur) < 0.45f)
				next = safeCur + (wheelDelta > 0 ? 0.65f : -0.65f);

			return Math.Max(4.0f, Math.Min(240.0f, next));
		}

		private void ApplyHorizontalZoom(ChartControl cc, float nextDist, float cursorPxX = -1f)
		{
			if (cc == null || cc.Properties == null)
				return;

			float prevDist = Math.Max(4.0f, cc.Properties.BarDistance);
			float clamped = Math.Max(4.0f, Math.Min(240.0f, nextDist));
			if (Math.Abs(clamped - prevDist) < 0.05f)
				return;

			var sb = hookedOfScrollBar ?? (hookedOfScrollBar = ResolveOrderflowScrollBar(cc));
			double prevSbVal = (sb != null) ? sb.Value : 0.0;
			bool wasPinnedToLiveEdge = (sb != null && sb.Maximum > sb.Minimum && sb.Value >= sb.Maximum - 0.6);

			cc.Properties.BarDistance = clamped;
			SyncOneBoxRightGapMargin(cc, clamped, true);
			TriggerChartScrollbarRefresh();

			// Cursor-anchored smooth horizontal zoom: keep the hovered bar directly under the mouse cursor!
			try
			{
				if (sb != null && sb.Maximum > sb.Minimum && ChartPanel != null && cursorPxX >= ChartPanel.X && cursorPxX <= ChartPanel.X + ChartPanel.W)
				{
					float rightAnchorX = ChartPanel.X + ChartPanel.W - Math.Max(28f, cc.Properties.BarMarginRightUser);
					if (wasPinnedToLiveEdge && cursorPxX >= ChartPanel.X + ChartPanel.W * 0.68f)
					{
						sb.Value = sb.Maximum;
					}
					else if (cursorPxX < rightAnchorX - 8f)
					{
						float distFromRight = rightAnchorX - cursorPxX;
						double deltaBars = distFromRight * ((1.0 / prevDist) - (1.0 / clamped));
						double targetVal = Math.Max(sb.Minimum, Math.Min(sb.Maximum, prevSbVal + deltaBars));
						sb.Value = targetVal;
					}
				}
			}
			catch { }

			RequestImmediateOrderflowRedraw();
		}

		private bool ProcessUniversalSmoothWheel(ChartControl cc, int rawDelta, float cursorPxX, bool hasCursorX)
		{
			if (cc == null || rawDelta == 0)
				return false;

			// Deduplicate simultaneous WinForms Direct2DForm + WPF PreviewMouseWheel notifications (< 10ms)
			long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
			long minGapTicks = System.Diagnostics.Stopwatch.Frequency / 110; // ~9ms
			if (nowTicks - lastWheelHandledTicks < minGapTicks)
				return true;
			lastWheelHandledTicks = nowTicks;

			try
			{
				if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
				{
					var sb = hookedOfScrollBar ?? (hookedOfScrollBar = ResolveOrderflowScrollBar(cc));
					if (sb != null && sb.Maximum > sb.Minimum)
					{
						double step = Math.Max(0.5, Math.Abs(rawDelta) / 120.0 * 3.0);
						sb.Value = rawDelta > 0
							? Math.Max(sb.Minimum, sb.Value - step)
							: Math.Min(sb.Maximum, sb.Value + step);
						RequestImmediateOrderflowRedraw();
					}
					return true;
				}

				if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
				{
					double notches = Math.Max(-3.0, Math.Min(3.0, rawDelta / 120.0));
					runtimeYZoomFactor = Math.Max(0.40, Math.Min(3.8, runtimeYZoomFactor * Math.Pow(1.13, notches)));
					RequestImmediateOrderflowRedraw();
					return true;
				}

				if (cc.Properties != null)
				{
					float cur = cc.Properties.BarDistance > 3f ? cc.Properties.BarDistance : BarSpacePx;
					float next = ComputeSmoothNextZoomDistance(cur, rawDelta);
					ApplyHorizontalZoom(cc, next, hasCursorX ? cursorPxX : -1f);
					return true;
				}
			}
			catch { }
			return false;
		}

		private void OnChartMouseWheel(object sender, MouseWheelEventArgs e)
		{
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (!IsOrderflowChartControlActive(cc) || e == null || e.Delta == 0)
				return;
			if (IsOfInteractiveUiControlSource(e.OriginalSource))
				return;

			float pxX = -1f;
			try
			{
				pxX = ConvertOfHorizontalPx(cc, e.GetPosition(cc).X);
			}
			catch { }

			if (ProcessUniversalSmoothWheel(cc, e.Delta, pxX, pxX >= 0f))
				e.Handled = true;
		}

		private bool HandleToolbarClick(float x, float y)
		{
			int idx = HitTestToolbarButtonAt(x, y);
			return idx >= 0 && ExecuteToolbarButtonAction(idx);
		}

		private bool ExecuteToolbarButtonAction(int btnIdx)
		{
			if ((DateTime.UtcNow - lastBtnClickUtc).TotalMilliseconds < 70)
				return false;

			ChartControl cc = hookedOrderflowCC ?? ChartControl;

			switch (btnIdx)
			{
				case 0: // Volume Footprint (Seller Left | Vivid Center Candle | Buyer Right + Volume Bars & Imbalances)
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeOrderflowMode = AK10XOrderflowMode.VolumeFootprint;
					FootprintMode = runtimeOrderflowMode;
					if (cc != null && cc.Properties != null && cc.Properties.BarDistance < CandlestickZoomThresholdPx)
						ApplyHorizontalZoom(cc, Math.Max(88f, BarSpacePx));
					else
						RequestImmediateOrderflowRedraw();
					return true;

				case 1: // Imbalance (Seller Left | Vivid Center Candle | Buyer Right + Imbalance Highlights)
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeOrderflowMode = AK10XOrderflowMode.Imbalance;
					FootprintMode = runtimeOrderflowMode;
					if (cc != null && cc.Properties != null && cc.Properties.BarDistance < CandlestickZoomThresholdPx)
						ApplyHorizontalZoom(cc, Math.Max(88f, BarSpacePx));
					else
						RequestImmediateOrderflowRedraw();
					return true;

				case 2: // Delta Footprint Mode
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeOrderflowMode = AK10XOrderflowMode.Delta;
					FootprintMode = runtimeOrderflowMode;
					if (cc != null && cc.Properties != null && cc.Properties.BarDistance < CandlestickZoomThresholdPx)
						ApplyHorizontalZoom(cc, Math.Max(78f, BarSpacePx * 0.85f));
					else
						RequestImmediateOrderflowRedraw();
					return true;

				case 3: // Total Volume Profile Mode
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeOrderflowMode = AK10XOrderflowMode.TotalVolume;
					FootprintMode = runtimeOrderflowMode;
					if (cc != null && cc.Properties != null && cc.Properties.BarDistance < CandlestickZoomThresholdPx)
						ApplyHorizontalZoom(cc, Math.Max(78f, BarSpacePx * 0.85f));
					else
						RequestImmediateOrderflowRedraw();
					return true;

				case 4: // Quick Toggle: Candlestick <-> Orderflow
					lastBtnClickUtc = DateTime.UtcNow;
					if (cc != null && cc.Properties != null)
					{
						bool isCandleNow = cc.Properties.BarDistance < CandlestickZoomThresholdPx;
						ApplyHorizontalZoom(cc, isCandleNow ? Math.Max(88f, BarSpacePx) : 14f);
					}
					return true;

				case 5: // Step -
					lastBtnClickUtc = DateTime.UtcNow;
					if (runtimeAutoStep)
					{
						runtimeTicksPerLevel = Math.Max(1, lastEffectiveAutoTicks);
						runtimeAutoStep = false;
					}
					runtimeTicksPerLevel = Math.Max(1, runtimeTicksPerLevel <= 3 ? (runtimeTicksPerLevel - 1) : (int)Math.Round(runtimeTicksPerLevel * 0.72));
					InvalidateFootprintCache();
					return true;

				case 6: // Auto Step
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeAutoStep = !runtimeAutoStep;
					if (!runtimeAutoStep) runtimeTicksPerLevel = Math.Max(1, lastEffectiveAutoTicks);
					InvalidateFootprintCache();
					return true;

				case 7: // Step +
					lastBtnClickUtc = DateTime.UtcNow;
					if (runtimeAutoStep)
					{
						runtimeTicksPerLevel = Math.Max(1, lastEffectiveAutoTicks);
						runtimeAutoStep = false;
					}
					runtimeTicksPerLevel = Math.Min(500, Math.Max(runtimeTicksPerLevel + 1, (int)Math.Ceiling(runtimeTicksPerLevel * 1.32)));
					InvalidateFootprintCache();
					return true;

				case 8: // Stacked Imbalance Zones Toggle
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeShowImbZones = !runtimeShowImbZones;
					RequestImmediateOrderflowRedraw();
					return true;

				case 9: // Bottom Delta Strip Toggle
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeShowStatGrid = !runtimeShowStatGrid;
					RequestImmediateOrderflowRedraw();
					return true;

				case 10: // White / Dark Theme Toggle
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeLightTheme = !runtimeLightTheme;
					UseLightTheme = runtimeLightTheme;
					RequestImmediateOrderflowRedraw();
					return true;

				case 11: // Toggle Data Feed (Accelpix <-> NinjaTrader)
					lastBtnClickUtc = DateTime.UtcNow;
					AK10XOrderflowDataFeed nextFeed = EffectiveDataFeed == AK10XOrderflowDataFeed.Accelpix
						? AK10XOrderflowDataFeed.NinjaTrader
						: AK10XOrderflowDataFeed.Accelpix;
					SwitchDataFeedLive(nextFeed);
					return true;

				case 12: // Smooth Zoom Out (Z-)
					lastBtnClickUtc = DateTime.UtcNow;
					if (cc != null && cc.Properties != null)
					{
						float cur = cc.Properties.BarDistance > 3f ? cc.Properties.BarDistance : BarSpacePx;
						ApplyHorizontalZoom(cc, ComputeSmoothNextZoomDistance(cur, -240));
					}
					return true;

				case 13: // Smooth Zoom In (Z+)
					lastBtnClickUtc = DateTime.UtcNow;
					if (cc != null && cc.Properties != null)
					{
						float cur = cc.Properties.BarDistance > 3f ? cc.Properties.BarDistance : BarSpacePx;
						ApplyHorizontalZoom(cc, ComputeSmoothNextZoomDistance(cur, 240));
					}
					return true;

				case 14: // Toggle Halfpack (HP)
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeShowHalfpack = !runtimeShowHalfpack;
					ShowHalfpack = runtimeShowHalfpack;
					RequestImmediateOrderflowRedraw();
					return true;
			}
			return false;
		}

		private static bool ContainsPtPadded(RectangleF r, float x, float y, float pad)
		{
			return r.Width > 0 && r.Height > 0 &&
				x >= r.Left - pad && x <= r.Right + pad &&
				y >= r.Top - pad && y <= r.Bottom + pad;
		}

		private void InvalidateFootprintCache()
		{
			lock (dataLock)
			{
				barCache.Clear();
				lastComputedBarCount = -1;
				lastComputedUniformStep = -1.0;
			}
			RequestImmediateOrderflowRedraw();
		}

		private void TriggerChartScrollbarRefresh()
		{
			ChartControl cc = ChartControl;
			if (cc == null) return;
			try
			{
				if (cachedInitSlotsPainted != null) cachedInitSlotsPainted.Invoke(cc, null);
				if (cachedInitScrollbar != null) cachedInitScrollbar.Invoke(cc, null);
				RequestImmediateOrderflowRedraw();
			}
			catch { }
		}
		#endregion

		#region Real-Time Market Data & Bar Computation
		protected override void OnMarketData(MarketDataEventArgs e)
		{
			if (State == State.Realtime)
				lastNativeOnMarketDataUtc = DateTime.UtcNow;
			ProcessLiveTickEvent(e, true);
		}

		private void ProcessLiveTickEvent(MarketDataEventArgs e, bool fromNativeOnMarketData)
		{
			if (e == null || State == State.Terminated)
				return;

			try
			{
				if (e.MarketDataType == MarketDataType.Bid && e.Price > 0)
				{
					lastBidPrice = e.Price;
					return;
				}
				if (e.MarketDataType == MarketDataType.Ask && e.Price > 0)
				{
					lastAskPrice = e.Price;
					return;
				}
				if (e.MarketDataType != MarketDataType.Last || e.Price <= 0)
					return;

				long vol = e.Volume > 0 ? e.Volume : 1L;
				double tick = TickSize > 0 ? TickSize : 0.05;

				bool isBuyAggression;
				if (lastTradePrice > 0)
				{
					if (e.Price > lastTradePrice + 1e-9)
						lastTickDirection = 1;
					else if (e.Price < lastTradePrice - 1e-9)
						lastTickDirection = -1;
				}

				if (lastAskPrice > 0 && lastBidPrice > 0 && lastAskPrice > lastBidPrice + tick * 0.15)
				{
					double mid = (lastAskPrice + lastBidPrice) * 0.5;
					if (e.Price >= lastAskPrice - 1e-9)
						isBuyAggression = true;
					else if (e.Price <= lastBidPrice + 1e-9)
						isBuyAggression = false;
					else if (Math.Abs(e.Price - mid) > tick * 0.1)
						isBuyAggression = (e.Price > mid);
					else
						isBuyAggression = (lastTickDirection >= 0);
				}
				else
				{
					isBuyAggression = (lastTickDirection >= 0);
				}
				lastTradePrice = e.Price;

				long tickKey = (long)Math.Round(e.Price / tick);
				bool isRealtime = (State == State.Realtime);

				int barIdx;
				if (fromNativeOnMarketData)
				{
					barIdx = Math.Max(0, CurrentBar);
				}
				else
				{
					if (ChartBars != null && ChartBars.Bars != null && ChartBars.Bars.Count > 0)
						barIdx = ChartBars.Bars.Count - 1;
					else
						barIdx = Math.Max(0, CurrentBar);
				}

				lock (dataLock)
				{
					if (!liveAccumulators.TryGetValue(barIdx, out LiveTickAccumulator acc))
					{
						acc = new LiveTickAccumulator();
						if (isRealtime)
						{
							try
							{
								if (ChartBars != null && ChartBars.Bars != null && barIdx >= 0 && barIdx < ChartBars.Bars.Count)
								{
									DateTime slot = GetBarSlotKey(ChartBars.Bars.GetTime(barIdx));
									if (historicalTickBySlot.TryGetValue(slot, out LiveTickAccumulator histSeed) && histSeed != null)
									{
										foreach (var kv in histSeed.BidByTickKey) acc.BidByTickKey[kv.Key] = kv.Value;
										foreach (var kv in histSeed.AskByTickKey) acc.AskByTickKey[kv.Key] = kv.Value;
										acc.RunningDelta = histSeed.RunningDelta;
										acc.MinDelta = histSeed.MinDelta;
										acc.MaxDelta = histSeed.MaxDelta;
										acc.TotalTickVolume = histSeed.TotalTickVolume;
									}
								}
							}
							catch { }
						}
						liveAccumulators[barIdx] = acc;
					}

					if (isBuyAggression)
					{
						acc.AskByTickKey.TryGetValue(tickKey, out long cur);
						acc.AskByTickKey[tickKey] = cur + vol;
						acc.RunningDelta += vol;
					}
					else
					{
						acc.BidByTickKey.TryGetValue(tickKey, out long cur);
						acc.BidByTickKey[tickKey] = cur + vol;
						acc.RunningDelta -= vol;
					}
					acc.TotalTickVolume += vol;
					if (acc.RunningDelta < acc.MinDelta) acc.MinDelta = acc.RunningDelta;
					if (acc.RunningDelta > acc.MaxDelta) acc.MaxDelta = acc.RunningDelta;

					if (isRealtime)
					{
						barCache.Remove(barIdx);
						lastComputedLastClose = double.NaN;
					}
				}

				if (isRealtime)
					RequestThrottledChartRefresh(false);
			}
			catch { }
		}

		protected override void OnBarUpdate()
		{
			if (CurrentBar >= 0)
			{
				BarBrushes[0] = System.Windows.Media.Brushes.Transparent;
				CandleOutlineBrushes[0] = System.Windows.Media.Brushes.Transparent;
			}
		}

		private double ComputeReadablePriceStep(double medianBarRange, double visibleSpan, float usablePanelHeightPx)
		{
			double tick = TickSize > 0 ? TickSize : 0.05;
			if (!runtimeAutoStep)
				return Math.Max(tick, runtimeTicksPerLevel * tick);

			double stepFromBar = Math.Max(tick, medianBarRange / 10.0);
			double targetScreenRows = Math.Max(12.0, Math.Min(34.0, usablePanelHeightPx / 18.5));
			double stepFromVisibleSpan = visibleSpan > 0 ? (visibleSpan / targetScreenRows) : stepFromBar;

			double idealStep = Math.Max(stepFromBar, stepFromVisibleSpan);
			if (medianBarRange > tick * 4 && idealStep > medianBarRange * 0.45)
				idealStep = Math.Max(stepFromBar, medianBarRange * 0.35);

			int rawTicks = Math.Max(1, (int)Math.Round(idealStep / tick));
			int[] niceSteps = { 1, 2, 3, 4, 5, 8, 10, 12, 15, 20, 25, 30, 40, 50, 75, 100, 150, 200, 250, 500, 1000 };
			int best = niceSteps[0];
			int bestDiff = Math.Abs(rawTicks - best);
			for (int i = 1; i < niceSteps.Length; i++)
			{
				int d = Math.Abs(rawTicks - niceSteps[i]);
				if (d < bestDiff)
				{
					best = niceSteps[i];
					bestDiff = d;
				}
			}
			lastEffectiveAutoTicks = best;
			return best * tick;
		}

		private void EnsureBarsComputed(ChartBars chartBars)
		{
			if (chartBars == null || chartBars.Bars == null)
				return;

			Bars bars = chartBars.Bars;
			int count = bars.Count;
			if (count <= 0)
				return;

			double lastClose = bars.GetClose(count - 1);
			long lastVol = bars.GetVolume(count - 1);

			int visFirst = Math.Max(0, chartBars.FromIndex);
			int visLast = Math.Min(count - 1, chartBars.ToIndex);
			double visMin = double.MaxValue;
			double visMax = double.MinValue;
			for (int i = visFirst; i <= visLast; i++)
			{
				double l = bars.GetLow(i);
				double h = bars.GetHigh(i);
				if (l < visMin) visMin = l;
				if (h > visMax) visMax = h;
			}
			double visibleSpan = (visMax > visMin && visMin < double.MaxValue) ? (visMax - visMin) : (TickSize * 20);
			if (Math.Abs(runtimeYZoomFactor - 1.0) > 0.01)
				visibleSpan /= runtimeYZoomFactor;

			float panelH = ChartPanel != null ? ChartPanel.H : 620f;
			float usableH = Math.Max(180f, panelH - 90f);

			lock (dataLock)
			{
				int sampleStart = Math.Max(0, count - 80);
				List<double> ranges = new List<double>(count - sampleStart);
				for (int i = sampleStart; i < count; i++)
				{
					double r = bars.GetHigh(i) - bars.GetLow(i);
					if (r > 0) ranges.Add(r);
				}
				ranges.Sort();
				double medianRange = ranges.Count > 0 ? ranges[ranges.Count / 2] : (TickSize * 15);
				double uniformStep = ComputeReadablePriceStep(medianRange, visibleSpan, usableH);

				if (count == lastComputedBarCount &&
					Math.Abs(lastClose - lastComputedLastClose) < 1e-9 &&
					lastVol == lastComputedLastVol &&
					Math.Abs(uniformStep - lastComputedUniformStep) < 1e-9 &&
					barCache.Count > 0)
				{
					return;
				}

				long sessionCumDelta = 0;
				DateTime currentSessionDate = DateTime.MinValue;
				Dictionary<long, long> sessionVolByBucket = new Dictionary<long, long>();

				int startIdx = Math.Max(0, count - 800);
				bool useNtSessionBreaks = (EffectiveDataFeed == AK10XOrderflowDataFeed.NinjaTrader);
				for (int i = startIdx; i < count; i++)
				{
					DateTime t = bars.GetTime(i);
					DateTime sessionDate = t.Date;
					bool isNewSession = (sessionDate != currentSessionDate);
					if (useNtSessionBreaks && !isNewSession)
					{
						try
						{
							if (bars.IsFirstBarOfSessionByIndex(i))
								isNewSession = true;
						}
						catch { }
					}
					if (isNewSession)
					{
						currentSessionDate = sessionDate;
						sessionCumDelta = 0;
						sessionVolByBucket.Clear();
					}

					if (!barCache.TryGetValue(i, out FootprintBarData fpBar) || i == count - 1 || Math.Abs(fpBar.EffectivePriceStep - uniformStep) > 1e-9)
					{
						fpBar = BuildFootprintBar(bars, i, uniformStep);
						barCache[i] = fpBar;
					}

					sessionCumDelta += fpBar.Delta;
					fpBar.CumulativeDelta = sessionCumDelta;

					if (ShowSessionVA)
					{
						for (int lvIdx = 0; lvIdx < fpBar.Levels.Count; lvIdx++)
						{
							FootprintLevel lv = fpBar.Levels[lvIdx];
							long bKey = (long)Math.Floor((lv.Price + 1e-9) / uniformStep);
							sessionVolByBucket.TryGetValue(bKey, out long sv);
							sessionVolByBucket[bKey] = sv + lv.TotalVolume;
						}
						ComputeDevelopingSessionProfile(sessionVolByBucket, uniformStep, fpBar);
					}
				}

				lastComputedBarCount = count;
				lastComputedLastClose = lastClose;
				lastComputedLastVol = lastVol;
				lastComputedUniformStep = uniformStep;
			}
		}

		private FootprintBarData BuildFootprintBar(Bars bars, int barIdx, double step)
		{
			double tick = TickSize > 0 ? TickSize : 0.05;
			if (step < tick) step = tick;

			DateTime time = bars.GetTime(barIdx);
			double open = bars.GetOpen(barIdx);
			double high = bars.GetHigh(barIdx);
			double low = bars.GetLow(barIdx);
			double close = bars.GetClose(barIdx);
			long totalVol = Math.Max(1L, bars.GetVolume(barIdx));

			long minBucket = (long)Math.Floor((low + 1e-9) / step);
			long maxBucket = (long)Math.Floor((high - 1e-9) / step);
			if (maxBucket < minBucket) maxBucket = minBucket;

			int numLevels = Math.Max(1, (int)(maxBucket - minBucket + 1));
			double snappedLow = minBucket * step;
			double snappedHigh = (maxBucket + 1) * step;

			FootprintBarData fp = new FootprintBarData
			{
				BarIndex = barIdx,
				Time = time,
				Open = open,
				High = high,
				Low = low,
				Close = close,
				TotalVolume = totalVol,
				EffectivePriceStep = step
			};

			DateTime barSlotKey = GetBarSlotKey(time);
			LiveTickAccumulator tickAcc = null;
			if (liveAccumulators.TryGetValue(barIdx, out LiveTickAccumulator liveAcc) && liveAcc.TotalTickVolume > 0)
			{
				tickAcc = liveAcc;
			}
			else if (EffectiveDataFeed == AK10XOrderflowDataFeed.NinjaTrader &&
				historicalTickBySlot.TryGetValue(barSlotKey, out LiveTickAccumulator histAcc) &&
				histAcc != null && histAcc.TotalTickVolume > 0)
			{
				tickAcc = histAcc;
			}

			bool usedTickAcc = false;
			if (tickAcc != null)
			{
				List<FootprintLevel> tempLevels = new List<FootprintLevel>(numLevels);
				for (int r = 0; r < numLevels; r++)
				{
					tempLevels.Add(new FootprintLevel { Price = (minBucket + r + 0.5) * step });
				}

				double minValidPx = low - Math.Max(tick * 2.0, step * 0.5);
				double maxValidPx = high + Math.Max(tick * 2.0, step * 0.5);
				long inRangeVol = 0;

				foreach (var kv in tickAcc.BidByTickKey)
				{
					double p = kv.Key * tick;
					if (p < minValidPx || p > maxValidPx)
						continue;
					int idx = Math.Max(0, Math.Min(numLevels - 1, (int)((long)Math.Floor((p + 1e-9) / step) - minBucket)));
					tempLevels[idx].BidVolume += kv.Value;
					inRangeVol += kv.Value;
				}
				foreach (var kv in tickAcc.AskByTickKey)
				{
					double p = kv.Key * tick;
					if (p < minValidPx || p > maxValidPx)
						continue;
					int idx = Math.Max(0, Math.Min(numLevels - 1, (int)((long)Math.Floor((p + 1e-9) / step) - minBucket)));
					tempLevels[idx].AskVolume += kv.Value;
					inRangeVol += kv.Value;
				}

				if (inRangeVol > 0 && (barIdx >= bars.Count - 1 || inRangeVol >= totalVol * 0.35))
				{
					usedTickAcc = true;
					fp.IsLiveAccumulated = true;
					fp.Levels.AddRange(tempLevels);
					fp.BuyVolume = fp.Levels.Sum(l => l.AskVolume);
					fp.SellVolume = fp.Levels.Sum(l => l.BidVolume);
					fp.TotalVolume = Math.Max(1L, fp.BuyVolume + fp.SellVolume);
					fp.Delta = fp.BuyVolume - fp.SellVolume;
					fp.MinDelta = tickAcc.MinDelta;
					fp.MaxDelta = tickAcc.MaxDelta;
				}
			}

			if (!usedTickAcc)
			{
				bool isBull = close >= open;
				double bodyMid = (open + close) * 0.5;
				double range = Math.Max(step, snappedHigh - snappedLow);

				double[] rawBid = new double[numLevels];
				double[] rawAsk = new double[numLevels];
				double weightSum = 0.0;

				if (subBarsBySlot.TryGetValue(barSlotKey, out List<SubBar1M> subBars) && subBars != null && subBars.Count > 0)
				{
					for (int s = 0; s < subBars.Count; s++)
					{
						SubBar1M sb = subBars[s];
						if (sb.High < low - step || sb.Low > high + step)
							continue;
						bool sbBull = sb.Close >= sb.Open;
						double sbBodyRatio = Math.Abs(sb.Close - sb.Open) / Math.Max(tick, sb.High - sb.Low);
						double sbAskShare = sbBull ? (0.56 + 0.24 * sbBodyRatio) : (0.44 - 0.24 * sbBodyRatio);
						sbAskShare = Math.Max(0.12, Math.Min(0.88, sbAskShare));

						int rMin = Math.Max(0, Math.Min(numLevels - 1, (int)((long)Math.Floor((sb.Low + 1e-9) / step) - minBucket)));
						int rMax = Math.Max(rMin, Math.Min(numLevels - 1, (int)((long)Math.Floor((sb.High - 1e-9) / step) - minBucket)));
						int span = Math.Max(1, rMax - rMin + 1);
						double volPerRow = Math.Max(1.0, (double)sb.Volume / span);

						for (int r = rMin; r <= rMax; r++)
						{
							double rowRel = span > 1 ? (double)(r - rMin) / (span - 1) : 0.5;
							double localAsk = sbBull
								? Math.Min(0.90, sbAskShare + 0.12 * (rowRel - 0.35))
								: Math.Max(0.10, sbAskShare - 0.12 * (0.65 - rowRel));
							rawAsk[r] += volPerRow * localAsk;
							rawBid[r] += volPerRow * (1.0 - localAsk);
							weightSum += volPerRow;
						}
					}
				}

				for (int r = 0; r < numLevels; r++)
				{
					if (rawAsk[r] > 0 || rawBid[r] > 0)
						continue;
					double p = (minBucket + r + 0.5) * step;
					double distFromBody = Math.Abs(p - bodyMid) / (range * 0.55 + step);
					double bellWeight = Math.Exp(-0.85 * distFromBody * distFromBody);

					bool inBody = p >= Math.Min(open, close) - step * 0.25 && p <= Math.Max(open, close) + step * 0.25;
					if (inBody) bellWeight *= 1.45;

					int hash = ((barIdx * 73856093) ^ (r * 19349663)) & 0x7FFFFFFF;
					double microVar = 0.65 + (hash % 71) * 0.01;
					double rowWeight = Math.Max(0.08, bellWeight * microVar);

					double relPos = numLevels > 1 ? (double)r / (numLevels - 1) : 0.5;
					double bodyRatio = Math.Abs(close - open) / Math.Max(tick, high - low);
					double askShare = isBull
						? (0.52 + 0.16 * bodyRatio + 0.14 * (relPos - 0.35))
						: (0.48 - 0.16 * bodyRatio - 0.14 * (0.65 - relPos));

					int spikeSelector = ((barIdx * 31 + r * 17) & 0x7FFFFFFF) % 11;
					if (spikeSelector == 2 && isBull && p >= open)
						askShare = 0.84;
					else if (spikeSelector == 5 && !isBull && p <= open)
						askShare = 0.16;
					else if (spikeSelector == 8 && r > 0 && r < numLevels - 1)
						askShare = isBull ? 0.82 : 0.18;

					askShare = Math.Max(0.10, Math.Min(0.90, askShare));

					rawAsk[r] = rowWeight * askShare;
					rawBid[r] = rowWeight * (1.0 - askShare);
					weightSum += rawAsk[r] + rawBid[r];
				}

				if (weightSum <= 0) weightSum = 1.0;

				long sumBuy = 0;
				long sumSell = 0;
				for (int r = 0; r < numLevels; r++)
				{
					double p = (minBucket + r + 0.5) * step;
					long bVol = (long)Math.Round(totalVol * (rawBid[r] / weightSum));
					long aVol = (long)Math.Round(totalVol * (rawAsk[r] / weightSum));
					if (bVol == 0 && aVol == 0 && totalVol >= numLevels)
					{
						if (isBull) aVol = 1; else bVol = 1;
					}
					sumBuy += aVol;
					sumSell += bVol;
					fp.Levels.Add(new FootprintLevel
					{
						Price = p,
						BidVolume = bVol,
						AskVolume = aVol
					});
				}

				fp.BuyVolume = sumBuy;
				fp.SellVolume = sumSell;
				fp.TotalVolume = Math.Max(1L, sumBuy + sumSell);
				fp.Delta = sumBuy - sumSell;

				double upperWickRatio = (high - Math.Max(open, close)) / Math.Max(tick, high - low);
				double lowerWickRatio = (Math.Min(open, close) - low) / Math.Max(tick, high - low);
				if (fp.Delta >= 0)
				{
					fp.MinDelta = -(long)Math.Round(fp.TotalVolume * Math.Max(0.04, lowerWickRatio * 0.35));
					fp.MaxDelta = fp.Delta + (long)Math.Round(fp.TotalVolume * Math.Max(0.03, upperWickRatio * 0.25));
				}
				else
				{
					fp.MaxDelta = (long)Math.Round(fp.TotalVolume * Math.Max(0.04, upperWickRatio * 0.35));
					fp.MinDelta = fp.Delta - (long)Math.Round(fp.TotalVolume * Math.Max(0.03, lowerWickRatio * 0.25));
				}
			}

			int pocIdx = 0;
			long maxRowVol = -1;
			for (int r = 0; r < fp.Levels.Count; r++)
			{
				if (fp.Levels[r].TotalVolume > maxRowVol)
				{
					maxRowVol = fp.Levels[r].TotalVolume;
					pocIdx = r;
				}
			}
			if (fp.Levels.Count > 0)
			{
				fp.Levels[pocIdx].IsPoc = true;
				fp.PocPrice = fp.Levels[pocIdx].Price;

				long targetVaVol = (long)Math.Round(fp.TotalVolume * (BarVAPercentage / 100.0));
				long accumVaVol = fp.Levels[pocIdx].TotalVolume;
				fp.Levels[pocIdx].InValueArea = true;
				int upIdx = pocIdx;
				int dnIdx = pocIdx;

				while (accumVaVol < targetVaVol && (upIdx < fp.Levels.Count - 1 || dnIdx > 0))
				{
					long upVol = (upIdx < fp.Levels.Count - 1) ? fp.Levels[upIdx + 1].TotalVolume : -1;
					long dnVol = (dnIdx > 0) ? fp.Levels[dnIdx - 1].TotalVolume : -1;

					if (upVol >= dnVol && upIdx < fp.Levels.Count - 1)
					{
						upIdx++;
						fp.Levels[upIdx].InValueArea = true;
						accumVaVol += fp.Levels[upIdx].TotalVolume;
					}
					else if (dnIdx > 0)
					{
						dnIdx--;
						fp.Levels[dnIdx].InValueArea = true;
						accumVaVol += fp.Levels[dnIdx].TotalVolume;
					}
					else break;
				}

				fp.VaLow = fp.Levels[dnIdx].Price;
				fp.VaHigh = fp.Levels[upIdx].Price;
			}

			double ratio = Math.Max(1.5, ImbalancePercentage / 100.0);
			long minFilter = Math.Max(1, ImbalanceFilter);
			for (int r = 0; r < fp.Levels.Count; r++)
			{
				if (r < fp.Levels.Count - 1)
				{
					long bidV = fp.Levels[r].BidVolume;
					long diagAskV = fp.Levels[r + 1].AskVolume;
					if (bidV >= minFilter && bidV >= Math.Max(1L, diagAskV) * ratio)
						fp.Levels[r].HasBidImbalance = true;
				}
				if (r > 0)
				{
					long askV = fp.Levels[r].AskVolume;
					long diagBidV = fp.Levels[r - 1].BidVolume;
					if (askV >= minFilter && askV >= Math.Max(1L, diagBidV) * ratio)
						fp.Levels[r].HasAskImbalance = true;
				}
			}

			return fp;
		}

		private void ComputeDevelopingSessionProfile(Dictionary<long, long> sessionVolByBucket, double step, FootprintBarData fpBar)
		{
			if (sessionVolByBucket.Count == 0)
			{
				fpBar.DevSessionPoc = fpBar.PocPrice;
				fpBar.DevSessionVah = fpBar.VaHigh;
				fpBar.DevSessionVal = fpBar.VaLow;
				return;
			}

			long bestBucket = 0;
			long maxV = -1;
			long totalS = 0;
			foreach (var kv in sessionVolByBucket)
			{
				totalS += kv.Value;
				if (kv.Value > maxV)
				{
					maxV = kv.Value;
					bestBucket = kv.Key;
				}
			}

			fpBar.DevSessionPoc = (bestBucket + 0.5) * step;

			List<KeyValuePair<long, long>> sorted = sessionVolByBucket.OrderBy(k => k.Key).ToList();
			int pocListIdx = 0;
			for (int i = 0; i < sorted.Count; i++)
			{
				if (sorted[i].Key == bestBucket)
				{
					pocListIdx = i;
					break;
				}
			}

			long target = (long)Math.Round(totalS * (BarVAPercentage / 100.0));
			long accum = sorted[pocListIdx].Value;
			int u = pocListIdx;
			int d = pocListIdx;
			while (accum < target && (u < sorted.Count - 1 || d > 0))
			{
				long uv = (u < sorted.Count - 1) ? sorted[u + 1].Value : -1;
				long dv = (d > 0) ? sorted[d - 1].Value : -1;
				if (uv >= dv && u < sorted.Count - 1)
				{
					u++;
					accum += sorted[u].Value;
				}
				else if (d > 0)
				{
					d--;
					accum += sorted[d].Value;
				}
				else break;
			}

			fpBar.DevSessionVal = sorted[d].Key * step;
			fpBar.DevSessionVah = (sorted[u].Key + 1) * step;
		}
		#endregion

		#region SharpDX Inside-Box Buyer & Seller Candle Orderflow Rendering
		public override void OnCalculateMinMax()
		{
			if (ChartBars == null || ChartBars.Bars == null || ChartBars.Count <= 0)
				return;

			EnsureBarsComputed(ChartBars);

			int firstBar = Math.Max(0, ChartBars.FromIndex);
			int lastBar = Math.Min(ChartBars.Count - 1, ChartBars.ToIndex);
			if (lastBar < firstBar)
				return;

			float barSpacing = (ChartControl != null && ChartControl.Properties != null) ? ChartControl.Properties.BarDistance : BarSpacePx;
			float blend = Math.Max(0f, Math.Min(1f, (barSpacing - (CandlestickZoomThresholdPx - 8f)) / 16f));

			double minPrice = double.MaxValue;
			double maxPrice = double.MinValue;
			double refStep = TickSize > 0 ? TickSize * 10 : 1.0;

			lock (dataLock)
			{
				for (int idx = firstBar; idx <= lastBar; idx++)
				{
					double rawLow = ChartBars.Bars.GetLow(idx);
					double rawHigh = ChartBars.Bars.GetHigh(idx);
					double barLowEdge = rawLow;
					double barHighEdge = rawHigh;

					if (blend > 0.01f && barCache.TryGetValue(idx, out FootprintBarData fp) && fp != null)
					{
						if (fp.EffectivePriceStep > 0)
							refStep = fp.EffectivePriceStep;
						double halfStep = refStep * 0.5;
						double fpLow = (fp.Levels != null && fp.Levels.Count > 0)
							? Math.Min(fp.Low, fp.Levels[0].Price - halfStep)
							: (fp.Low - halfStep);
						double fpHigh = (fp.Levels != null && fp.Levels.Count > 0)
							? Math.Max(fp.High, fp.Levels[fp.Levels.Count - 1].Price + halfStep)
							: (fp.High + halfStep);

						barLowEdge = rawLow + (fpLow - rawLow) * blend;
						barHighEdge = rawHigh + (fpHigh - rawHigh) * blend;
					}

					if (barLowEdge < minPrice) minPrice = barLowEdge;
					if (barHighEdge > maxPrice) maxPrice = barHighEdge;
				}
			}

			if (minPrice == double.MaxValue || maxPrice == double.MinValue)
				return;

			double rawSpan = Math.Max(refStep * 4.0, maxPrice - minPrice);
			double candlePad = rawSpan * 0.06;
			double ofPad = Math.Max(rawSpan * 0.06, refStep * 1.8);
			double pad = candlePad + (ofPad - candlePad) * blend;
			double paddedMin = minPrice - pad;
			double paddedMax = maxPrice + pad;

			if (Math.Abs(runtimeYZoomFactor - 1.0) > 0.01)
			{
				double mid = (paddedMin + paddedMax) * 0.5;
				double halfSpan = ((paddedMax - paddedMin) * 0.5) / runtimeYZoomFactor;
				paddedMin = mid - halfSpan;
				paddedMax = mid + halfSpan;
			}

			double span = Math.Max(TickSize * 6, paddedMax - paddedMin);
			float panelH = ChartPanel != null ? ChartPanel.H : 600f;
			float deltaBarReserve = runtimeShowStatGrid ? 88f : 26f;
			float topHeaderH = 56f;
			float usableH = Math.Max(150f, panelH - topHeaderH - deltaBarReserve);

			double pricePerPx = span / usableH;
			MaxValue = paddedMax + topHeaderH * pricePerPx;
			MinValue = paddedMin - deltaBarReserve * pricePerPx;
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (chartControl == null || chartScale == null || ChartBars == null || RenderTarget == null)
				return;

			EnsureMouseEventsSubscribed();

			if (!initialBarWidthApplied && chartControl.Properties != null)
			{
				initialBarWidthApplied = true;
				if (chartControl.Properties.BarDistance < 45f)
				{
					chartControl.Properties.BarDistance = BarSpacePx;
					chartControl.Dispatcher.InvokeAsync(TriggerChartScrollbarRefresh);
				}
			}

			EnsureBarsComputed(ChartBars);

			int firstBar = Math.Max(0, ChartBars.FromIndex);
			int lastBar = Math.Min(ChartBars.Count - 1, ChartBars.ToIndex);
			if (ChartBars.Count == 0 || lastBar < firstBar)
			{
				RenderOrderflowLoadingOverlay();
				return;
			}

			float panelX = ChartPanel.X;
			float panelY = ChartPanel.Y;
			float panelW = ChartPanel.W;
			float panelH = ChartPanel.H;

			// 3-Row Bottom Table: Row 1 = Momentum (BMC/SMC), Row 2 = Strength (Buyer/Seller), Row 3 = Delta (3 x 20px = 60px)
			float deltaStripH = 60f;
			float bottomWatermarkGutter = 16f;
			float chartBottomY = runtimeShowStatGrid
				? (float)Math.Round(panelY + panelH - deltaStripH - bottomWatermarkGutter)
				: (float)Math.Round(panelY + panelH - bottomWatermarkGutter);

			bool light = runtimeLightTheme;

			var oldAntialias = RenderTarget.AntialiasMode;
			var oldTextAntialias = RenderTarget.TextAntialiasMode;
			RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;
			RenderTarget.TextAntialiasMode = SharpDX.Direct2D1.TextAntialiasMode.Cleartype;

			try
			{
				if (light)
				{
					using (var whiteCanvas = new SolidColorBrush(RenderTarget, new Color4(1.0f, 1.0f, 1.0f, 1.0f)))
					{
						RenderTarget.FillRectangle(new RectangleF(panelX, panelY, panelW, panelH), whiteCanvas);
					}
				}

				// Vivid High-Contrast Buyer (#10CC8B Emerald-Teal) & Seller (#F4433F Bright Red) Candle Palette
				// Matching zoomed-out candlesticks AND inside-box candle 1-for-1 (ZERO outside candle!)
				using (var brushBuyerCandle = new SolidColorBrush(RenderTarget, new Color4(0.063f, 0.800f, 0.545f, 1.0f)))    // #10CC8B Vivid Buyer Teal-Green
				using (var brushSellerCandle = new SolidColorBrush(RenderTarget, new Color4(0.957f, 0.263f, 0.247f, 1.0f)))   // #F4433F Vivid Seller Bright Red
				using (var brushBoxBg = new SolidColorBrush(RenderTarget, light
					? new Color4(0.965f, 0.975f, 0.988f, 0.94f)
					: new Color4(0.065f, 0.088f, 0.128f, 0.92f)))
				using (var brushSpineLaneBg = new SolidColorBrush(RenderTarget, light
					? new Color4(0.925f, 0.942f, 0.965f, 0.95f)
					: new Color4(0.045f, 0.062f, 0.092f, 0.95f)))
				using (var brushBoxBorder = new SolidColorBrush(RenderTarget, light
					? new Color4(0.74f, 0.80f, 0.87f, 0.90f)
					: new Color4(0.18f, 0.25f, 0.35f, 0.88f)))
				using (var brushRowGrid = new SolidColorBrush(RenderTarget, light
					? new Color4(0.82f, 0.86f, 0.91f, 0.65f)
					: new Color4(0.16f, 0.22f, 0.31f, 0.42f)))
				using (var brushAskImbBlock = new SolidColorBrush(RenderTarget, new Color4(0.045f, 0.635f, 0.435f, 0.92f)))   // Crisp Buyer Emerald Imbalance Cell
				using (var brushBidImbBlock = new SolidColorBrush(RenderTarget, new Color4(0.840f, 0.195f, 0.215f, 0.92f)))   // Crisp Seller Red Imbalance Cell
				using (var brushImbWhiteText = new SolidColorBrush(RenderTarget, new Color4(1.0f, 1.0f, 1.0f, 1.0f)))
				using (var brushNormalText = new SolidColorBrush(RenderTarget, light
					? new Color4(0.12f, 0.16f, 0.22f, 0.98f)
					: new Color4(0.90f, 0.94f, 0.98f, 0.98f)))
				using (var brushZeroText = new SolidColorBrush(RenderTarget, light
					? new Color4(0.45f, 0.52f, 0.60f, 0.82f)
					: new Color4(0.50f, 0.57f, 0.66f, 0.80f)))
				using (var brushPocOutline = new SolidColorBrush(RenderTarget, light
					? new Color4(0.85f, 0.45f, 0.04f, 0.92f)
					: new Color4(1.0f, 0.80f, 0.12f, 0.92f)))
				using (var brushHalfpackLine = new SolidColorBrush(RenderTarget, light
					? new Color4(0.06f, 0.10f, 0.18f, 0.98f)
					: new Color4(1.0f, 1.0f, 1.0f, 0.98f)))
				using (var brushAskVolShade = new SolidColorBrush(RenderTarget, new Color4(0.063f, 0.800f, 0.545f, light ? 0.22f : 0.30f)))
				using (var brushBidVolShade = new SolidColorBrush(RenderTarget, new Color4(0.957f, 0.263f, 0.247f, light ? 0.22f : 0.30f)))
				using (var brushDeltaBadgePosBg = new SolidColorBrush(RenderTarget, new Color4(0.045f, 0.38f, 0.28f, 0.92f)))
				using (var brushDeltaBadgeNegBg = new SolidColorBrush(RenderTarget, new Color4(0.46f, 0.13f, 0.15f, 0.92f)))
				using (var brushHeightBadgeBg = new SolidColorBrush(RenderTarget, light
					? new Color4(0.91f, 0.94f, 0.98f, 0.95f)
					: new Color4(0.09f, 0.13f, 0.20f, 0.94f)))
				using (var brushHeightBadgeBorder = new SolidColorBrush(RenderTarget, light
					? new Color4(0.68f, 0.76f, 0.86f, 0.90f)
					: new Color4(0.22f, 0.31f, 0.45f, 0.88f)))
				using (var brushHeightBadgeText = new SolidColorBrush(RenderTarget, light
					? new Color4(0.10f, 0.18f, 0.30f, 0.98f)
					: new Color4(0.84f, 0.92f, 1.0f, 0.98f)))
				using (var brushBidImbZone = new SolidColorBrush(RenderTarget, new Color4(0.94f, 0.24f, 0.24f, 0.14f)))
				using (var brushAskImbZone = new SolidColorBrush(RenderTarget, new Color4(0.06f, 0.78f, 0.53f, 0.14f)))
				using (var brushDevVa = new SolidColorBrush(RenderTarget, new Color4(0.22f, 0.58f, 0.96f, 0.80f)))
				using (var brushDevPoc = new SolidColorBrush(RenderTarget, new Color4(0.96f, 0.60f, 0.08f, 0.88f)))
				{
					float barSpacing = chartControl.Properties != null ? chartControl.Properties.BarDistance : BarSpacePx;
					if (barSpacing < 3f)
					{
						if (lastBar > firstBar)
							barSpacing = Math.Abs(chartControl.GetXByBarIndex(ChartBars, lastBar) - chartControl.GetXByBarIndex(ChartBars, firstBar)) / (float)(lastBar - firstBar);
						else
							barSpacing = BarSpacePx;
					}

					SyncOneBoxRightGapMargin(chartControl, barSpacing, false);

					List<FootprintBarData> visibleBars = new List<FootprintBarData>(lastBar - firstBar + 1);
					lock (dataLock)
					{
						for (int idx = firstBar; idx <= lastBar; idx++)
						{
							if (barCache.TryGetValue(idx, out FootprintBarData fp))
								visibleBars.Add(fp);
						}
					}

					float sampleStep = visibleBars.Count > 0 ? (float)visibleBars[visibleBars.Count - 1].EffectivePriceStep : (float)(TickSize * 10);
					double midP = visibleBars.Count > 0 ? visibleBars[visibleBars.Count - 1].Close : 1000.0;
					Func<double, float> priceToY = (double price) => (float)chartScale.GetYByValue(price);

					float rawRowH = Math.Abs(chartScale.GetYByValue(midP) - chartScale.GetYByValue(midP + sampleStep));
					bool isCandlestickZoom = (barSpacing < CandlestickZoomThresholdPx);

					RenderTarget.PushAxisAlignedClip(new RectangleF(panelX, panelY, panelW, Math.Max(40f, chartBottomY - panelY - 1f)), AntialiasMode.PerPrimitive);
					try
					{
						if (runtimeShowImbZones && visibleBars.Count > 0)
						{
							RenderStackedImbalanceZones(chartControl, priceToY, visibleBars, lastBar, barSpacing, chartBottomY,
								brushBidImbZone, brushAskImbZone, brushSellerCandle, brushBuyerCandle);
						}

						if (ShowSessionVA && visibleBars.Count > 1 && !isCandlestickZoom)
						{
							RenderDevelopingSessionLines(chartControl, priceToY, visibleBars, barSpacing, chartBottomY, brushDevVa, brushDevPoc);
						}

						if (isCandlestickZoom)
						{
							RenderCleanCandlesticks(chartControl, priceToY, visibleBars, barSpacing, panelX, panelW, brushBuyerCandle, brushSellerCandle);
						}
						else
						{
							RenderInsideBoxCandleFootprints(
								chartControl, priceToY, visibleBars, barSpacing, rawRowH,
								panelX, panelY, panelW, chartBottomY,
								brushBuyerCandle, brushSellerCandle,
								brushBoxBg, brushSpineLaneBg, brushBoxBorder, brushRowGrid,
								brushAskImbBlock, brushBidImbBlock, brushImbWhiteText,
								brushNormalText, brushZeroText, brushPocOutline, brushHalfpackLine,
								brushAskVolShade, brushBidVolShade,
								brushDeltaBadgePosBg, brushDeltaBadgeNegBg,
								brushHeightBadgeBg, brushHeightBadgeBorder, brushHeightBadgeText);
						}
					}
					finally
					{
						RenderTarget.PopAxisAlignedClip();
					}

					if (runtimeShowStatGrid && visibleBars.Count > 0)
					{
						RenderThreeRowBottomTable(chartControl, visibleBars, panelX, panelW, chartBottomY + 1f, deltaStripH - 1f, barSpacing, light);
					}

					RenderSleekTopToolbar(panelX, panelY, barSpacing, sampleStep, isCandlestickZoom);
				}
			}
			catch { }
			finally
			{
				RenderTarget.TextAntialiasMode = oldTextAntialias;
				RenderTarget.AntialiasMode = oldAntialias;
			}
		}

		private void RenderCleanCandlesticks(
			ChartControl chartControl,
			Func<double, float> priceToY,
			List<FootprintBarData> visibleBars,
			float barSpacing,
			float panelX,
			float panelW,
			SolidColorBrush brushBuyer,
			SolidColorBrush brushSeller)
		{
			float bodyHalfW = Math.Max(1.3f, Math.Min(13f, (float)Math.Floor(barSpacing * 0.35f)));
			float wickWidth = barSpacing >= 10f ? 1.8f : 1.2f;

			for (int i = 0; i < visibleBars.Count; i++)
			{
				FootprintBarData fp = visibleBars[i];
				float cx = (float)chartControl.GetXByBarIndex(ChartBars, fp.BarIndex);
				if (cx + bodyHalfW < panelX || cx - bodyHalfW > panelX + panelW)
					continue;

				float yHigh = priceToY(fp.High);
				float yLow = priceToY(fp.Low);
				float yOpen = priceToY(fp.Open);
				float yClose = priceToY(fp.Close);

				bool isBuyer = fp.Close >= fp.Open;
				SolidColorBrush brush = isBuyer ? brushBuyer : brushSeller;

				RenderTarget.DrawLine(new Vector2(cx, yHigh), new Vector2(cx, yLow), brush, wickWidth);

				float top = Math.Min(yOpen, yClose);
				float h = Math.Max(2.5f, Math.Abs(yClose - yOpen));
				var bodyRect = new RectangleF(cx - bodyHalfW, top, bodyHalfW * 2f, h);
				RenderTarget.FillRectangle(bodyRect, brush);
			}
		}

		/// <summary>
		/// Renders the clean Footprint Box with ONLY the vivid Inside-Box Candle (zero outside candle!):
		/// [ Seller Volume (Left)  |  Vivid Solid Buyer/Seller Inside Candle (Center Lane)  |  Buyer Volume (Right) ]
		/// </summary>
		private void RenderInsideBoxCandleFootprints(
			ChartControl chartControl,
			Func<double, float> priceToY,
			List<FootprintBarData> visibleBars,
			float barSpacing,
			float rawRowH,
			float panelX,
			float panelY,
			float panelW,
			float chartBottomY,
			SolidColorBrush brushBuyerCandle,
			SolidColorBrush brushSellerCandle,
			SolidColorBrush brushBoxBg,
			SolidColorBrush brushSpineLaneBg,
			SolidColorBrush brushBoxBorder,
			SolidColorBrush brushRowGrid,
			SolidColorBrush brushAskImbBlock,
			SolidColorBrush brushBidImbBlock,
			SolidColorBrush brushImbWhiteText,
			SolidColorBrush brushNormalText,
			SolidColorBrush brushZeroText,
			SolidColorBrush brushPocOutline,
			SolidColorBrush brushHalfpackLine,
			SolidColorBrush brushAskVolShade,
			SolidColorBrush brushBidVolShade,
			SolidColorBrush brushDeltaBadgePosBg,
			SolidColorBrush brushDeltaBadgeNegBg,
			SolidColorBrush brushHeightBadgeBg,
			SolidColorBrush brushHeightBadgeBorder,
			SolidColorBrush brushHeightBadgeText)
		{
			bool isTwoSide = (runtimeOrderflowMode == AK10XOrderflowMode.Imbalance || runtimeOrderflowMode == AK10XOrderflowMode.VolumeFootprint);
			float rowH = Math.Max(8f, Math.Min(28f, rawRowH));

			// Vivid Inside-Box Candle geometry: solid ~6px body inside a dedicated 11-12px center lane inside the box
			float candleBodyHalfW = Math.Max(2.2f, Math.Min(3.4f, barSpacing * 0.032f));
			float centerLaneHalfW = candleBodyHalfW + 2.6f;
			float approxSideW = Math.Max(12f, Math.Min(48f, (barSpacing * 0.46f) - centerLaneHalfW));

			float fontSize = Math.Max(8.6f, Math.Min(11.6f, Math.Min(rowH * 0.64f, (isTwoSide ? approxSideW : approxSideW * 1.8f) * 0.34f)));
			bool showNumbers = (barSpacing >= 50f && rawRowH >= 9.0f);

			var sharedDw = NinjaTrader.Core.Globals.DirectWriteFactory;
			using (var tfReg = new TextFormat(sharedDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.SemiBold, SharpDX.DirectWrite.FontStyle.Normal, fontSize))
			using (var tfBold = new TextFormat(sharedDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, fontSize))
			using (var tfBadge = new TextFormat(sharedDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, Math.Max(8.5f, Math.Min(10.2f, fontSize * 0.94f))))
			{
				tfReg.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfReg.ParagraphAlignment = ParagraphAlignment.Center;
				tfReg.WordWrapping = WordWrapping.NoWrap;

				tfBold.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfBold.ParagraphAlignment = ParagraphAlignment.Center;
				tfBold.WordWrapping = WordWrapping.NoWrap;

				tfBadge.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfBadge.ParagraphAlignment = ParagraphAlignment.Center;
				tfBadge.WordWrapping = WordWrapping.NoWrap;

				for (int v = 0; v < visibleBars.Count; v++)
				{
					FootprintBarData fp = visibleBars[v];
					GetBarGridColumnBounds(chartControl, fp.BarIndex, barSpacing, out float leftBound, out float rightBound, out float cx);
					if (rightBound < panelX || leftBound > panelX + panelW || fp.Levels.Count == 0)
						continue;

					// Clean box bounds centered on cx (ZERO outside candle — 100% inside box!)
					float curSideW = Math.Max(10f, Math.Min(48f, ((rightBound - leftBound) * 0.5f) - centerLaneHalfW - 1.5f));
					float boxLeft = (float)Math.Round(cx - centerLaneHalfW - curSideW);
					float centerLeft = (float)Math.Round(cx - centerLaneHalfW);
					float centerRight = (float)Math.Round(cx + centerLaneHalfW);
					float boxRight = (float)Math.Round(cx + centerLaneHalfW + curSideW);

					float step = (float)fp.EffectivePriceStep;
					float halfStep = step * 0.5f;

					float t0 = (float)Math.Round(priceToY(fp.Levels[fp.Levels.Count - 1].Price + halfStep));
					float b0 = (float)Math.Round(priceToY(fp.Levels[0].Price - halfStep));
					float barTopY = Math.Min(t0, b0);
					float barBotY = Math.Max(t0, b0);
					float boxH = Math.Max(4f, barBotY - barTopY);

					// 1. Clean Footprint Box Backdrop + Dedicated Dark Center Candle Lane
					var outerBoxRect = new RectangleF(boxLeft, barTopY, boxRight - boxLeft, boxH);
					RenderTarget.FillRectangle(outerBoxRect, brushBoxBg);
					RenderTarget.FillRectangle(new RectangleF(centerLeft, barTopY, centerRight - centerLeft, boxH), brushSpineLaneBg);

					long maxSideVol = 1;
					long maxTotalRowVol = 1;
					long maxAbsDelta = 1;
					for (int r = 0; r < fp.Levels.Count; r++)
					{
						FootprintLevel lv = fp.Levels[r];
						if (lv.BidVolume > maxSideVol) maxSideVol = lv.BidVolume;
						if (lv.AskVolume > maxSideVol) maxSideVol = lv.AskVolume;
						if (lv.TotalVolume > maxTotalRowVol) maxTotalRowVol = lv.TotalVolume;
						long ad = Math.Abs(lv.Delta);
						if (ad > maxAbsDelta) maxAbsDelta = ad;
					}

					// 2. Seller (Left) & Buyer (Right) Price-Level Cells inside the Box
					for (int r = 0; r < fp.Levels.Count; r++)
					{
						FootprintLevel lv = fp.Levels[r];
						float ry1 = (float)Math.Round(priceToY(lv.Price + halfStep));
						float ry2 = (float)Math.Round(priceToY(lv.Price - halfStep));
						float yTop = Math.Min(ry1, ry2);
						float yBot = Math.Max(ry1, ry2);
						float cellH = Math.Max(2f, yBot - yTop);
						if (yBot < panelY || yTop > chartBottomY)
							continue;

						if (r > 0 && cellH >= 8f)
						{
							RenderTarget.DrawLine(new Vector2(boxLeft, yTop), new Vector2(centerLeft, yTop), brushRowGrid, 0.8f);
							RenderTarget.DrawLine(new Vector2(centerRight, yTop), new Vector2(boxRight, yTop), brushRowGrid, 0.8f);
						}

						float blockTop = yTop + 0.8f;
						float blockH = Math.Max(1.5f, cellH - 1.6f);

						if (runtimeOrderflowMode == AK10XOrderflowMode.Imbalance || runtimeOrderflowMode == AK10XOrderflowMode.VolumeFootprint)
						{
							bool drawVolBars = (runtimeOrderflowMode == AK10XOrderflowMode.VolumeFootprint);
							bool bidImb = ShowImbalance && lv.HasBidImbalance;
							bool askImb = ShowImbalance && lv.HasAskImbalance;

							if (drawVolBars)
							{
								if (!bidImb && lv.BidVolume > 0)
								{
									float bw = Math.Max(2f, (centerLeft - boxLeft - 2f) * Math.Min(1f, (float)lv.BidVolume / maxSideVol));
									RenderTarget.FillRectangle(new RectangleF(centerLeft - bw - 1f, blockTop, bw, blockH), brushBidVolShade);
								}
								if (!askImb && lv.AskVolume > 0)
								{
									float aw = Math.Max(2f, (boxRight - centerRight - 2f) * Math.Min(1f, (float)lv.AskVolume / maxSideVol));
									RenderTarget.FillRectangle(new RectangleF(centerRight + 1f, blockTop, aw, blockH), brushAskVolShade);
								}
							}

							if (bidImb)
								RenderTarget.FillRectangle(new RectangleF(boxLeft + 1f, blockTop, centerLeft - boxLeft - 2f, blockH), brushBidImbBlock);
							if (askImb)
								RenderTarget.FillRectangle(new RectangleF(centerRight + 1f, blockTop, boxRight - centerRight - 2f, blockH), brushAskImbBlock);

							if (ShowBarPOC && lv.IsPoc)
							{
								RenderTarget.DrawLine(new Vector2(boxLeft + 1f, yTop), new Vector2(centerLeft - 1f, yTop), brushPocOutline, 1.2f);
								RenderTarget.DrawLine(new Vector2(boxLeft + 1f, yBot), new Vector2(centerLeft - 1f, yBot), brushPocOutline, 1.2f);
								RenderTarget.DrawLine(new Vector2(centerRight + 1f, yTop), new Vector2(boxRight - 1f, yTop), brushPocOutline, 1.2f);
								RenderTarget.DrawLine(new Vector2(centerRight + 1f, yBot), new Vector2(boxRight - 1f, yBot), brushPocOutline, 1.2f);
							}

							if (showNumbers)
							{
								var leftTxtRect = new RectangleF(boxLeft, yTop, centerLeft - boxLeft, cellH);
								var rightTxtRect = new RectangleF(centerRight, yTop, boxRight - centerRight, cellH);

								SolidColorBrush bidFg = bidImb ? brushImbWhiteText : (lv.BidVolume == 0 ? brushZeroText : brushNormalText);
								SolidColorBrush askFg = askImb ? brushImbWhiteText : (lv.AskVolume == 0 ? brushZeroText : brushNormalText);

								RenderTarget.DrawText(FormatGoChartingVol(lv.BidVolume), (bidImb || lv.IsPoc) ? tfBold : tfReg, leftTxtRect, bidFg);
								RenderTarget.DrawText(FormatGoChartingVol(lv.AskVolume), (askImb || lv.IsPoc) ? tfBold : tfReg, rightTxtRect, askFg);
							}
						}
						else if (runtimeOrderflowMode == AK10XOrderflowMode.Delta)
						{
							float dStart = centerRight + 1f;
							float spanW = Math.Max(8f, boxRight - centerRight - 2f);
							bool hasImb = ShowImbalance && (lv.Delta > 0 ? lv.HasAskImbalance : lv.HasBidImbalance);
							if (lv.Delta != 0)
							{
								float dw = Math.Max(2f, spanW * Math.Min(1f, (float)Math.Abs(lv.Delta) / maxAbsDelta));
								RenderTarget.FillRectangle(
									new RectangleF(dStart, blockTop, dw, blockH),
									hasImb ? (lv.Delta > 0 ? brushAskImbBlock : brushBidImbBlock) : (lv.Delta > 0 ? brushAskVolShade : brushBidVolShade));
							}
							if (showNumbers)
							{
								string dStr = (lv.Delta > 0 ? "+" : "") + FormatGoChartingVol(lv.Delta);
								SolidColorBrush fg = hasImb ? brushImbWhiteText : (lv.Delta > 0 ? brushBuyerCandle : (lv.Delta < 0 ? brushSellerCandle : brushZeroText));
								RenderTarget.DrawText(dStr, (hasImb || lv.IsPoc) ? tfBold : tfReg, new RectangleF(centerRight, yTop, boxRight - centerRight, cellH), fg);
								RenderTarget.DrawText(FormatGoChartingVol(lv.TotalVolume), lv.IsPoc ? tfBold : tfReg, new RectangleF(boxLeft, yTop, centerLeft - boxLeft, cellH), brushZeroText);
							}
						}
						else // TotalVolume
						{
							float vStart = centerRight + 1f;
							float spanW = Math.Max(8f, boxRight - centerRight - 2f);
							if (lv.TotalVolume > 0)
							{
								float vw = Math.Max(2f, spanW * Math.Min(1f, (float)lv.TotalVolume / maxTotalRowVol));
								RenderTarget.FillRectangle(
									new RectangleF(vStart, blockTop, vw, blockH),
									lv.Delta >= 0 ? brushAskVolShade : brushBidVolShade);
							}
							if (showNumbers)
							{
								RenderTarget.DrawText(FormatGoChartingVol(lv.TotalVolume), lv.IsPoc ? tfBold : tfReg, new RectangleF(centerRight, yTop, boxRight - centerRight, cellH), brushNormalText);
								string dStr = (lv.Delta > 0 ? "+" : "") + FormatGoChartingVol(lv.Delta);
								RenderTarget.DrawText(dStr, tfReg, new RectangleF(boxLeft, yTop, centerLeft - boxLeft, cellH), lv.Delta >= 0 ? brushBuyerCandle : brushSellerCandle);
							}
						}
					}

					// 3. Outer Box Border & Center Candle Channel Dividers
					RenderTarget.DrawLine(new Vector2(centerLeft, barTopY), new Vector2(centerLeft, barBotY), brushBoxBorder, 0.9f);
					RenderTarget.DrawLine(new Vector2(centerRight, barTopY), new Vector2(centerRight, barBotY), brushBoxBorder, 0.9f);
					RenderTarget.DrawRectangle(outerBoxRect, brushBoxBorder, 1.0f);

					// 4. VIVID INSIDE-BOX CANDLE (Bright solid Buyer Teal-Green / Seller Bright Red — matching outside candle visibility!)
					bool isBuyerCandle = fp.Close >= fp.Open;
					SolidColorBrush vividCandleBrush = isBuyerCandle ? brushBuyerCandle : brushSellerCandle;

					float yHigh = Math.Max(barTopY + 1f, Math.Min(barBotY - 1f, priceToY(fp.High)));
					float yLow = Math.Max(barTopY + 1f, Math.Min(barBotY - 1f, priceToY(fp.Low)));
					float yOpen = Math.Max(barTopY + 1f, Math.Min(barBotY - 1f, priceToY(fp.Open)));
					float yClose = Math.Max(barTopY + 1f, Math.Min(barBotY - 1f, priceToY(fp.Close)));

					// Vivid High-to-Low Wick in bright Buyer/Seller color
					RenderTarget.DrawLine(new Vector2(cx, yHigh), new Vector2(cx, yLow), vividCandleBrush, 2.0f);

					// Vivid Solid Open-to-Close Candle Body inside the center lane
					float bodyTop = Math.Min(yOpen, yClose);
					float bodyH = Math.Max(3.5f, Math.Abs(yClose - yOpen));
					var insideCandleRect = new RectangleF(cx - candleBodyHalfW, bodyTop, candleBodyHalfW * 2f, bodyH);
					RenderTarget.FillRectangle(insideCandleRect, vividCandleBrush);

					// 4b. CANDLE HALFPACK (50% High-Low Midpoint Line across every candle in Footprint & Imbalance modes)
					if (runtimeShowHalfpack && isTwoSide)
					{
						double trueHalfpack = (fp.High + fp.Low) * 0.5;
						if (fp.Levels.Count >= 2)
						{
							double bestDivPrice = fp.Levels[1].Price - halfStep;
							double bestDiff = Math.Abs(bestDivPrice - trueHalfpack);
							for (int r = 2; r < fp.Levels.Count; r++)
							{
								double divP = fp.Levels[r].Price - halfStep;
								double diff = Math.Abs(divP - trueHalfpack);
								if (diff < bestDiff)
								{
									bestDiff = diff;
									bestDivPrice = divP;
								}
							}
							float yHp = (float)Math.Round(priceToY(bestDivPrice));
							if (yHp >= panelY && yHp <= chartBottomY)
							{
								RenderTarget.DrawLine(new Vector2(boxLeft, yHp), new Vector2(boxRight, yHp), brushHalfpackLine, 2.2f);
							}
						}
						else
						{
							float yHp = (float)Math.Round(priceToY(trueHalfpack));
							if (yHp >= panelY && yHp <= chartBottomY)
							{
								float x1 = showNumbers ? centerLeft : boxLeft;
								float x2 = showNumbers ? centerRight : boxRight;
								RenderTarget.DrawLine(new Vector2(x1, yHp), new Vector2(x2, yHp), brushHalfpackLine, 2.2f);
							}
						}
					}

					// 5. TOP OF BOX = Per-Bar Delta Pill (+279 / -22)  |  BOTTOM OF BOX = Candle Height (High - Low)
					if (ShowBarDelta && barSpacing >= 46f)
					{
						float pillW = Math.Min(boxRight - boxLeft, Math.Max(36f, (boxRight - boxLeft) * 0.78f));

						// Top of Box: Delta Badge
						if (barTopY - 17.5f >= panelY + 2f)
						{
							string fdStr = (fp.Delta > 0 ? "+" : "") + FormatGoChartingVol(fp.Delta);
							var topPillRect = new RectangleF(cx - pillW * 0.5f, barTopY - 17.5f, pillW, 14.5f);
							var topPillRr = new RoundedRectangle { Rect = topPillRect, RadiusX = 3f, RadiusY = 3f };
							RenderTarget.FillRoundedRectangle(topPillRr, fp.Delta >= 0 ? brushDeltaBadgePosBg : brushDeltaBadgeNegBg);
							RenderTarget.DrawText(fdStr, tfBadge, topPillRect, fp.Delta >= 0 ? brushBuyerCandle : brushSellerCandle);
						}

						// Bottom of Box: Candle Height (High - Low)
						if (barBotY + 18f <= chartBottomY)
						{
							double tick = TickSize > 0 ? TickSize : 0.05;
							double candleHeight = Math.Max(0.0, Math.Round((fp.High - fp.Low) / tick) * tick);
							string hStr = FormatOrderflowPrice(candleHeight);
							var botPillRect = new RectangleF(cx - pillW * 0.5f, barBotY + 3f, pillW, 14.5f);
							var botPillRr = new RoundedRectangle { Rect = botPillRect, RadiusX = 3f, RadiusY = 3f };
							RenderTarget.FillRoundedRectangle(botPillRr, brushHeightBadgeBg);
							RenderTarget.DrawRoundedRectangle(botPillRr, brushHeightBadgeBorder, 0.85f);
							RenderTarget.DrawText(hStr, tfBadge, botPillRect, brushHeightBadgeText);
						}
					}
				}
			}
		}

		/// <summary>
		/// Evaluates Halfpack-based Strength (Buyer/Seller in Green/Red) and Momentum (BMC/SMC) signals for a bar.
		/// </summary>
		private void EvaluateHalfpackSignals(
			FootprintBarData fp,
			double maxWickRatio,
			out string strengthLabel,
			out int strengthTone,
			out string momentumLabel,
			out int momentumTone)
		{
			strengthLabel = string.Empty;
			strengthTone = 0;
			momentumLabel = string.Empty;
			momentumTone = 0;

			if (fp == null || fp.Levels == null || fp.Levels.Count < 2)
				return;

			double halfStep = fp.EffectivePriceStep * 0.5;
			double trueHalfpack = (fp.High + fp.Low) * 0.5;

			int hpDivIdx = 1;
			double bestDiff = Math.Abs((fp.Levels[1].Price - halfStep) - trueHalfpack);
			for (int r = 2; r < fp.Levels.Count; r++)
			{
				double divP = fp.Levels[r].Price - halfStep;
				double diff = Math.Abs(divP - trueHalfpack);
				if (diff < bestDiff)
				{
					bestDiff = diff;
					hpDivIdx = r;
				}
			}

			bool hasBuyImbBelow = false;
			bool hasSellImbBelow = false;
			for (int r = 0; r < hpDivIdx; r++)
			{
				if (fp.Levels[r].HasAskImbalance) hasBuyImbBelow = true;
				if (fp.Levels[r].HasBidImbalance) hasSellImbBelow = true;
			}

			bool hasBuyImbAbove = false;
			bool hasSellImbAbove = false;
			for (int r = hpDivIdx; r < fp.Levels.Count; r++)
			{
				if (fp.Levels[r].HasAskImbalance) hasBuyImbAbove = true;
				if (fp.Levels[r].HasBidImbalance) hasSellImbAbove = true;
			}

			bool isGreenCandle = fp.Close >= fp.Open;

			// 2. Strength Rules:
			// 1) Green Candle: buying imbalance below Halfpack AND buying imbalance above Halfpack => Buyer (Green)
			// 2) Green Candle: NO buying imbalance below Halfpack AND buying imbalance above Halfpack => Buyer (Red)
			// 3) Red Candle:   seller imbalance above Halfpack AND seller imbalance below Halfpack => Seller (Red)
			// 4) Red Candle:   NO seller imbalance above Halfpack AND seller imbalance below Halfpack => Seller (Green)
			if (isGreenCandle)
			{
				if (hasBuyImbBelow && hasBuyImbAbove)
				{
					strengthLabel = "Buyer";
					strengthTone = 1; // Green
				}
				else if (!hasBuyImbBelow && hasBuyImbAbove)
				{
					strengthLabel = "Buyer";
					strengthTone = -1; // Red
				}
			}
			else
			{
				if (hasSellImbAbove && hasSellImbBelow)
				{
					strengthLabel = "Seller";
					strengthTone = -1; // Red
				}
				else if (!hasSellImbAbove && hasSellImbBelow)
				{
					strengthLabel = "Seller";
					strengthTone = 1; // Green
				}
			}

			// 3. Momentum Rules:
			// 1) Green Candle: buying imbalance below & above Halfpack + small wick on top => BMC (Green)
			// 2) Red Candle:   selling imbalance above & below Halfpack + small wick on bottom => SMC (Red)
			double tick = TickSize > 0 ? TickSize : 0.05;
			double barRange = fp.High - fp.Low;
			if (barRange >= tick * 0.5)
			{
				if (isGreenCandle && hasBuyImbBelow && hasBuyImbAbove)
				{
					double upperWick = Math.Max(0.0, fp.High - Math.Max(fp.Open, fp.Close));
					if (upperWick <= barRange * maxWickRatio + 1e-9)
					{
						momentumLabel = "BMC";
						momentumTone = 1; // Green
					}
				}
				else if (!isGreenCandle && hasSellImbAbove && hasSellImbBelow)
				{
					double lowerWick = Math.Max(0.0, Math.Min(fp.Open, fp.Close) - fp.Low);
					if (lowerWick <= barRange * maxWickRatio + 1e-9)
					{
						momentumLabel = "SMC";
						momentumTone = -1; // Red
					}
				}
			}
		}

		/// <summary>
		/// Renders the 3-Row Bottom Table with Right-Side Titles:
		/// Row 1 (Top):    Momentum  -> BMC (Green) / SMC (Red)
		/// Row 2 (Middle): Strength  -> Buyer (Green/Red) / Seller (Red/Green)
		/// Row 3 (Bottom): Delta     -> Intensity-shaded Green/Red Delta
		/// </summary>
		private void RenderThreeRowBottomTable(
			ChartControl chartControl,
			List<FootprintBarData> visibleBars,
			float panelX,
			float panelW,
			float stripTopY,
			float stripH,
			float barSpacing,
			bool light)
		{
			long maxAbsDelta = 1;
			for (int i = 0; i < visibleBars.Count; i++)
			{
				long ad = Math.Abs(visibleBars[i].Delta);
				if (ad > maxAbsDelta) maxAbsDelta = ad;
			}

			float rowH = (float)Math.Floor(stripH / 3f);
			float yMom = stripTopY;
			float yStr = stripTopY + rowH;
			float yDel = stripTopY + rowH * 2f;
			float lastRowH = Math.Max(rowH, stripH - rowH * 2f);

			float titleColW = Math.Min(78f, Math.Max(58f, panelW * 0.08f));
			float titleColLeft = (float)Math.Round(panelX + panelW - titleColW);
			double maxWickRatio = Math.Max(0.05, Math.Min(0.45, MomentumMaxWickPct / 100.0));

			var dw = NinjaTrader.Core.Globals.DirectWriteFactory;
			float fontSz = Math.Max(8.4f, Math.Min(10.2f, Math.Min(rowH * 0.56f, barSpacing * 0.13f)));
			using (var tfCell = new TextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, fontSz))
			using (var tfTitle = new TextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 9.6f))
			using (var stripBg = new SolidColorBrush(RenderTarget, light ? new Color4(0.95f, 0.965f, 0.98f, 0.98f) : new Color4(0.055f, 0.075f, 0.108f, 0.97f)))
			using (var gridLineBrush = new SolidColorBrush(RenderTarget, light ? new Color4(0.75f, 0.80f, 0.86f, 0.95f) : new Color4(0.20f, 0.27f, 0.38f, 0.88f)))
			using (var sepBrush = new SolidColorBrush(RenderTarget, light ? new Color4(0.84f, 0.88f, 0.92f, 0.90f) : new Color4(0.11f, 0.15f, 0.22f, 0.90f)))
			using (var titleBgBrush = new SolidColorBrush(RenderTarget, light ? new Color4(0.88f, 0.92f, 0.96f, 0.99f) : new Color4(0.075f, 0.105f, 0.155f, 0.99f)))
			using (var titleBorderBrush = new SolidColorBrush(RenderTarget, light ? new Color4(0.45f, 0.54f, 0.66f, 0.95f) : new Color4(0.55f, 0.65f, 0.78f, 0.90f)))
			using (var titleTextBrush = new SolidColorBrush(RenderTarget, light ? new Color4(0.08f, 0.13f, 0.20f, 1.0f) : new Color4(0.96f, 0.98f, 1.0f, 1.0f)))
			using (var greenSignalBg = new SolidColorBrush(RenderTarget, new Color4(0.055f, 0.600f, 0.395f, 0.96f)))
			using (var redSignalBg = new SolidColorBrush(RenderTarget, new Color4(0.835f, 0.200f, 0.220f, 0.96f)))
			using (var txtWhite = new SolidColorBrush(RenderTarget, new Color4(1.0f, 1.0f, 1.0f, 1.0f)))
			using (var txtDark = new SolidColorBrush(RenderTarget, new Color4(0.08f, 0.12f, 0.16f, 1.0f)))
			using (var cellBrush = new SolidColorBrush(RenderTarget, new Color4(0.1f, 0.6f, 0.3f, 1.0f)))
			{
				tfCell.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfCell.ParagraphAlignment = ParagraphAlignment.Center;
				tfCell.WordWrapping = WordWrapping.NoWrap;

				tfTitle.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfTitle.ParagraphAlignment = ParagraphAlignment.Center;
				tfTitle.WordWrapping = WordWrapping.NoWrap;

				// Strip backdrop
				RenderTarget.FillRectangle(new RectangleF(panelX, stripTopY, panelW, stripH), stripBg);

				// Clip bar columns to the left of the Right-Side Title Column
				RenderTarget.PushAxisAlignedClip(new RectangleF(panelX, stripTopY, Math.Max(10f, titleColLeft - panelX), stripH), AntialiasMode.PerPrimitive);
				try
				{
					for (int i = 0; i < visibleBars.Count; i++)
					{
						FootprintBarData fp = visibleBars[i];
						float cx = (float)chartControl.GetXByBarIndex(ChartBars, fp.BarIndex);
						float halfSpan = Math.Max(2f, barSpacing * 0.5f);
						float cLeft = (float)Math.Round(cx - halfSpan);
						float cRight = (float)Math.Round(cx + halfSpan);
						if (cRight < panelX || cLeft > titleColLeft)
							continue;

						float cWidth = Math.Max(2f, cRight - cLeft - 1f);

						EvaluateHalfpackSignals(fp, maxWickRatio,
							out string strengthLabel, out int strengthTone,
							out string momentumLabel, out int momentumTone);

						// Row 1 (Top): Momentum (BMC / SMC)
						if (momentumTone != 0)
						{
							var momRect = new RectangleF(cLeft, yMom + 1f, cWidth, rowH - 1.5f);
							RenderTarget.FillRectangle(momRect, momentumTone > 0 ? greenSignalBg : redSignalBg);
							if (cWidth >= 28f)
								RenderTarget.DrawText(momentumLabel, tfCell, momRect, txtWhite);
							else if (cWidth >= 16f)
								RenderTarget.DrawText(momentumTone > 0 ? "BM" : "SM", tfCell, momRect, txtWhite);
						}

						// Row 2 (Middle): Strength (Buyer / Seller)
						if (strengthTone != 0)
						{
							var strRect = new RectangleF(cLeft, yStr + 1f, cWidth, rowH - 1.5f);
							RenderTarget.FillRectangle(strRect, strengthTone > 0 ? greenSignalBg : redSignalBg);
							if (cWidth >= 36f)
								RenderTarget.DrawText(strengthLabel, tfCell, strRect, txtWhite);
							else if (cWidth >= 16f)
								RenderTarget.DrawText(strengthLabel.StartsWith("B", StringComparison.Ordinal) ? "B" : "S", tfCell, strRect, txtWhite);
						}

						// Row 3 (Bottom): Delta (GoCharting intensity-shaded Green/Red)
						float intensity = (float)Math.Pow(Math.Min(1.0, (double)Math.Abs(fp.Delta) / maxAbsDelta), 0.55);
						Color4 fillCol;
						bool useDarkText = false;

						if (fp.Delta >= 0)
						{
							float r = 0.50f - 0.42f * intensity;
							float g = 0.72f - 0.18f * intensity;
							float b = 0.58f - 0.34f * intensity;
							fillCol = new Color4(r, g, b, 0.96f);
							if (intensity < 0.32f) useDarkText = true;
						}
						else
						{
							float r = 0.88f - 0.02f * intensity;
							float g = 0.49f - 0.34f * intensity;
							float b = 0.49f - 0.34f * intensity;
							fillCol = new Color4(r, g, b, 0.96f);
						}

						cellBrush.Color = fillCol;
						var delRect = new RectangleF(cLeft, yDel + 1f, cWidth, lastRowH - 1f);
						RenderTarget.FillRectangle(delRect, cellBrush);

						if (barSpacing >= 32f)
						{
							string dTxt = FormatGoChartingDeltaStrip(fp.Delta);
							RenderTarget.DrawText(dTxt, tfCell, delRect, useDarkText ? txtDark : txtWhite);
						}

						// Vertical column separator across all 3 rows
						RenderTarget.DrawLine(new Vector2(cRight, stripTopY), new Vector2(cRight, stripTopY + stripH), sepBrush, 1.0f);
					}
				}
				finally
				{
					RenderTarget.PopAxisAlignedClip();
				}

				// Horizontal row dividers across entire 3-row table
				RenderTarget.DrawLine(new Vector2(panelX, stripTopY), new Vector2(panelX + panelW, stripTopY), gridLineBrush, 1.0f);
				RenderTarget.DrawLine(new Vector2(panelX, yStr), new Vector2(panelX + panelW, yStr), gridLineBrush, 1.0f);
				RenderTarget.DrawLine(new Vector2(panelX, yDel), new Vector2(panelX + panelW, yDel), gridLineBrush, 1.0f);
				RenderTarget.DrawLine(new Vector2(panelX, stripTopY + stripH), new Vector2(panelX + panelW, stripTopY + stripH), gridLineBrush, 1.0f);

				// Right-Side Docked Row Title Column: Momentum | Strength | Delta
				var titleBlockRect = new RectangleF(titleColLeft, stripTopY, panelX + panelW - titleColLeft, stripH);
				RenderTarget.FillRectangle(titleBlockRect, titleBgBrush);
				RenderTarget.DrawRectangle(titleBlockRect, titleBorderBrush, 1.1f);
				RenderTarget.DrawLine(new Vector2(titleColLeft, yStr), new Vector2(panelX + panelW, yStr), titleBorderBrush, 1.0f);
				RenderTarget.DrawLine(new Vector2(titleColLeft, yDel), new Vector2(panelX + panelW, yDel), titleBorderBrush, 1.0f);

				RenderTarget.DrawText("Momentum", tfTitle, new RectangleF(titleColLeft, yMom, titleColW, rowH), titleTextBrush);
				RenderTarget.DrawText("Strength", tfTitle, new RectangleF(titleColLeft, yStr, titleColW, rowH), titleTextBrush);
				RenderTarget.DrawText("Delta", tfTitle, new RectangleF(titleColLeft, yDel, titleColW, lastRowH), titleTextBrush);
			}
		}

		private void GetBarGridColumnBounds(ChartControl chartControl, int barIndex, float fallbackSpacing, out float boxLeft, out float boxRight, out float boxCx)
		{
			float xCenter = (float)chartControl.GetXByBarIndex(ChartBars, barIndex);
			float span = fallbackSpacing;

			if (barIndex > 0)
			{
				float dPrev = xCenter - (float)chartControl.GetXByBarIndex(ChartBars, barIndex - 1);
				if (dPrev >= 3f && dPrev <= fallbackSpacing * 1.6f)
					span = dPrev;
			}
			else if (ChartBars != null && barIndex + 1 < ChartBars.Count)
			{
				float dNext = (float)chartControl.GetXByBarIndex(ChartBars, barIndex + 1) - xCenter;
				if (dNext >= 3f && dNext <= fallbackSpacing * 1.6f)
					span = dNext;
			}

			boxCx = (float)Math.Round(xCenter);
			float halfW = Math.Max(2f, (float)Math.Floor(span * 0.48f));
			boxLeft = boxCx - halfW;
			boxRight = boxCx + halfW;
		}

		private void RenderDevelopingSessionLines(
			ChartControl chartControl,
			Func<double, float> priceToY,
			List<FootprintBarData> visibleBars,
			float barSpacing,
			float chartBottomY,
			SolidColorBrush brushDevVa,
			SolidColorBrush brushDevPoc)
		{
			for (int i = 0; i < visibleBars.Count; i++)
			{
				FootprintBarData cur = visibleBars[i];
				GetBarGridColumnBounds(chartControl, cur.BarIndex, barSpacing, out float x1, out float x2, out _);

				float yPoc = priceToY(cur.DevSessionPoc);
				float yVah = priceToY(cur.DevSessionVah);
				float yVal = priceToY(cur.DevSessionVal);

				if (yPoc > ChartPanel.Y && yPoc < chartBottomY)
					RenderTarget.DrawLine(new Vector2(x1, yPoc), new Vector2(x2, yPoc), brushDevPoc, 1.5f);
				if (yVah > ChartPanel.Y && yVah < chartBottomY)
					RenderTarget.DrawLine(new Vector2(x1, yVah), new Vector2(x2, yVah), brushDevVa, 1.2f);
				if (yVal > ChartPanel.Y && yVal < chartBottomY)
					RenderTarget.DrawLine(new Vector2(x1, yVal), new Vector2(x2, yVal), brushDevVa, 1.2f);

				if (i > 0 && visibleBars[i - 1].Time.Date == cur.Time.Date)
				{
					FootprintBarData prev = visibleBars[i - 1];
					GetBarGridColumnBounds(chartControl, prev.BarIndex, barSpacing, out _, out float prevX2, out _);
					float pyPoc = priceToY(prev.DevSessionPoc);
					float pyVah = priceToY(prev.DevSessionVah);
					float pyVal = priceToY(prev.DevSessionVal);

					if (pyPoc > ChartPanel.Y && pyPoc < chartBottomY && yPoc > ChartPanel.Y && yPoc < chartBottomY)
						RenderTarget.DrawLine(new Vector2(prevX2, pyPoc), new Vector2(x1, yPoc), brushDevPoc, 1.0f);
					if (pyVah > ChartPanel.Y && pyVah < chartBottomY && yVah > ChartPanel.Y && yVah < chartBottomY)
						RenderTarget.DrawLine(new Vector2(prevX2, pyVah), new Vector2(x1, yVah), brushDevVa, 1.0f);
					if (pyVal > ChartPanel.Y && pyVal < chartBottomY && yVal > ChartPanel.Y && yVal < chartBottomY)
						RenderTarget.DrawLine(new Vector2(prevX2, pyVal), new Vector2(x1, yVal), brushDevVa, 1.0f);
				}
			}
		}

		private void RenderStackedImbalanceZones(
			ChartControl chartControl,
			Func<double, float> priceToY,
			List<FootprintBarData> visibleBars,
			int lastVisibleBarIdx,
			float barSpacing,
			float chartBottomY,
			SolidColorBrush brushBidZone,
			SolidColorBrush brushAskZone,
			SolidColorBrush brushBidBorder,
			SolidColorBrush brushAskBorder)
		{
			int minConsec = Math.Max(2, ImbalanceSRConsecutiveLevels);
			List<ImbalanceZone> zones = new List<ImbalanceZone>();

			for (int i = 0; i < visibleBars.Count; i++)
			{
				FootprintBarData fp = visibleBars[i];
				double halfStep = fp.EffectivePriceStep * 0.5;

				int runStart = -1;
				for (int r = 0; r <= fp.Levels.Count; r++)
				{
					bool hasBid = (r < fp.Levels.Count) && fp.Levels[r].HasBidImbalance;
					if (hasBid)
					{
						if (runStart < 0) runStart = r;
					}
					else if (runStart >= 0)
					{
						if (r - runStart >= minConsec)
						{
							zones.Add(new ImbalanceZone
							{
								StartBarIndex = fp.BarIndex,
								EndBarIndex = lastVisibleBarIdx,
								IsDemandAskZone = false,
								BottomPrice = fp.Levels[runStart].Price - halfStep,
								TopPrice = fp.Levels[r - 1].Price + halfStep
							});
						}
						runStart = -1;
					}
				}

				runStart = -1;
				for (int r = 0; r <= fp.Levels.Count; r++)
				{
					bool hasAsk = (r < fp.Levels.Count) && fp.Levels[r].HasAskImbalance;
					if (hasAsk)
					{
						if (runStart < 0) runStart = r;
					}
					else if (runStart >= 0)
					{
						if (r - runStart >= minConsec)
						{
							zones.Add(new ImbalanceZone
							{
								StartBarIndex = fp.BarIndex,
								EndBarIndex = lastVisibleBarIdx,
								IsDemandAskZone = true,
								BottomPrice = fp.Levels[runStart].Price - halfStep,
								TopPrice = fp.Levels[r - 1].Price + halfStep
							});
						}
						runStart = -1;
					}
				}
			}

			for (int z = 0; z < zones.Count; z++)
			{
				ImbalanceZone zone = zones[z];
				for (int i = 0; i < visibleBars.Count; i++)
				{
					FootprintBarData fp = visibleBars[i];
					if (fp.BarIndex <= zone.StartBarIndex) continue;

					if (fp.Time.Date != visibleBars[0].Time.Date && i > 0 && fp.Time.Date != visibleBars[i - 1].Time.Date)
					{
						zone.EndBarIndex = fp.BarIndex;
						zone.IsBroken = true;
						break;
					}
					if (zone.IsDemandAskZone && fp.Low < zone.BottomPrice)
					{
						zone.EndBarIndex = fp.BarIndex;
						zone.IsBroken = true;
						break;
					}
					if (!zone.IsDemandAskZone && fp.High > zone.TopPrice)
					{
						zone.EndBarIndex = fp.BarIndex;
						zone.IsBroken = true;
						break;
					}
				}

				GetBarGridColumnBounds(chartControl, zone.StartBarIndex, barSpacing, out float x1, out _, out _);
				GetBarGridColumnBounds(chartControl, zone.EndBarIndex, barSpacing, out float endLeft, out float endRight, out _);
				float x2 = zone.IsBroken ? endLeft : endRight;
				if (x2 <= x1) continue;

				float yTop = priceToY(zone.TopPrice);
				float yBot = priceToY(zone.BottomPrice);
				if (yBot < ChartPanel.Y || yTop > chartBottomY) continue;

				float cTop = Math.Max(ChartPanel.Y, yTop);
				float cBot = Math.Min(chartBottomY, yBot);
				if (cBot <= cTop) continue;

				var zRect = new RectangleF(x1, cTop, x2 - x1, Math.Max(2f, cBot - cTop));
				RenderTarget.FillRectangle(zRect, zone.IsDemandAskZone ? brushAskZone : brushBidZone);
				RenderTarget.DrawLine(new Vector2(x1, cTop), new Vector2(x2, cTop), zone.IsDemandAskZone ? brushAskBorder : brushBidBorder, 1.0f);
				RenderTarget.DrawLine(new Vector2(x1, cBot), new Vector2(x2, cBot), zone.IsDemandAskZone ? brushAskBorder : brushBidBorder, 1.0f);
			}
		}

		private string FormatOrderflowPrice(double val)
		{
			double tick = TickSize > 0 ? TickSize : 0.05;
			if (tick < 0.00005)
				return val.ToString("0.00000", CultureInfo.InvariantCulture);
			if (tick < 0.0005)
				return val.ToString("0.0000", CultureInfo.InvariantCulture);
			if (tick < 0.005)
				return val.ToString("0.000", CultureInfo.InvariantCulture);
			return val.ToString("0.##", CultureInfo.InvariantCulture);
		}

		private void RenderSleekTopToolbar(float panelX, float panelY, float barSpacing, float effectiveStep, bool isCandlestickZoom)
		{
			for (int i = 0; i < toolbarBtnRects.Length; i++)
				toolbarBtnRects[i] = default(RectangleF);

			bool isAccelpix = (EffectiveDataFeed == AK10XOrderflowDataFeed.Accelpix);
			string feedShort = isAccelpix ? "Accelpix ▾" : "NT8 Feed ▾";
			string stepLabel = runtimeAutoStep ? ("Auto " + FormatOrderflowPrice(effectiveStep)) : ("Step " + FormatOrderflowPrice(effectiveStep));
			string viewToggleLabel = isCandlestickZoom ? "Candle ▸ OF" : "OF ▸ Candle";
			string themeLabel = runtimeLightTheme ? "White" : "Dark";

			var hdrDw = NinjaTrader.Core.Globals.DirectWriteFactory;
			using (var tfBrand = new TextFormat(hdrDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10.6f))
			using (var tfBtn = new TextFormat(hdrDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10.0f))
			using (var barBg = new SolidColorBrush(RenderTarget, runtimeLightTheme ? new Color4(0.95f, 0.96f, 0.98f, 0.96f) : new Color4(0.06f, 0.085f, 0.13f, 0.96f)))
			using (var barBorder = new SolidColorBrush(RenderTarget, runtimeLightTheme ? new Color4(0.75f, 0.80f, 0.86f, 0.95f) : new Color4(0.26f, 0.35f, 0.48f, 0.90f)))
			using (var brandBrush = new SolidColorBrush(RenderTarget, new Color4(0.031f, 0.600f, 0.506f, 1.0f)))
			using (var btnBg = new SolidColorBrush(RenderTarget, runtimeLightTheme ? new Color4(1.0f, 1.0f, 1.0f, 0.98f) : new Color4(0.12f, 0.17f, 0.25f, 0.98f)))
			using (var btnHoverBg = new SolidColorBrush(RenderTarget, runtimeLightTheme ? new Color4(0.88f, 0.93f, 0.98f, 1.0f) : new Color4(0.20f, 0.30f, 0.45f, 1.0f)))
			using (var btnActiveBg = new SolidColorBrush(RenderTarget, new Color4(0.031f, 0.600f, 0.506f, 1.0f)))
			using (var btnBorder = new SolidColorBrush(RenderTarget, runtimeLightTheme ? new Color4(0.75f, 0.80f, 0.86f, 0.95f) : new Color4(0.28f, 0.38f, 0.52f, 0.85f)))
			using (var btnHoverBorder = new SolidColorBrush(RenderTarget, new Color4(0.031f, 0.600f, 0.506f, 1.0f)))
			using (var fgActive = new SolidColorBrush(RenderTarget, new Color4(1.0f, 1.0f, 1.0f, 1.0f)))
			using (var fgInactive = new SolidColorBrush(RenderTarget, runtimeLightTheme ? new Color4(0.15f, 0.20f, 0.28f, 1.0f) : new Color4(0.95f, 0.97f, 1.0f, 1.0f)))
			{
				tfBrand.ParagraphAlignment = ParagraphAlignment.Center;
				tfBrand.WordWrapping = WordWrapping.NoWrap;
				tfBtn.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfBtn.ParagraphAlignment = ParagraphAlignment.Center;
				tfBtn.WordWrapping = WordWrapping.NoWrap;

				float boxX = panelX + 8f;
				float boxY = panelY + 6f;
				float bh = 23f;
				float padY = 4.5f;

				bool wrapTwoRows = (ChartPanel != null && ChartPanel.W < 895f);
				float boxH = wrapTwoRows ? 58f : 32f;
				float boxW = wrapTwoRows ? Math.Min(ChartPanel.W - 16f, 548f) : Math.Min(ChartPanel != null ? ChartPanel.W - 16f : 888f, 888f);

				var barRr = new RoundedRectangle { Rect = new RectangleF(boxX, boxY, boxW, boxH), RadiusX = 5f, RadiusY = 5f };
				RenderTarget.FillRoundedRectangle(barRr, barBg);
				RenderTarget.DrawRoundedRectangle(barRr, barBorder, 1.0f);

				float cx = boxX + 10f;
				float cy = boxY + padY;

				RenderTarget.DrawText("AK10X OF", tfBrand, new RectangleF(cx, cy, 64f, bh), brandBrush);
				cx += 66f;

				bool act0 = !isCandlestickZoom && runtimeOrderflowMode == AK10XOrderflowMode.VolumeFootprint;
				toolbarBtnRects[0] = DrawPillBtn(cx, cy, 68f, bh, "Footprint", act0, hoveredToolbarBtnIndex == 0, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, act0 ? fgActive : fgInactive);
				cx += 72f;

				bool act1 = !isCandlestickZoom && runtimeOrderflowMode == AK10XOrderflowMode.Imbalance;
				toolbarBtnRects[1] = DrawPillBtn(cx, cy, 68f, bh, "Imbalance", act1, hoveredToolbarBtnIndex == 1, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, act1 ? fgActive : fgInactive);
				cx += 72f;

				bool act2 = !isCandlestickZoom && runtimeOrderflowMode == AK10XOrderflowMode.Delta;
				toolbarBtnRects[2] = DrawPillBtn(cx, cy, 50f, bh, "Delta", act2, hoveredToolbarBtnIndex == 2, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, act2 ? fgActive : fgInactive);
				cx += 54f;

				bool act3 = !isCandlestickZoom && runtimeOrderflowMode == AK10XOrderflowMode.TotalVolume;
				toolbarBtnRects[3] = DrawPillBtn(cx, cy, 58f, bh, "Volume", act3, hoveredToolbarBtnIndex == 3, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, act3 ? fgActive : fgInactive);
				cx += 62f;

				toolbarBtnRects[12] = DrawPillBtn(cx, cy, 26f, bh, "Z-", false, hoveredToolbarBtnIndex == 12, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, fgInactive);
				cx += 29f;
				toolbarBtnRects[4] = DrawPillBtn(cx, cy, 80f, bh, viewToggleLabel, isCandlestickZoom, hoveredToolbarBtnIndex == 4, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, isCandlestickZoom ? fgActive : fgInactive);
				cx += 83f;
				toolbarBtnRects[13] = DrawPillBtn(cx, cy, 26f, bh, "Z+", false, hoveredToolbarBtnIndex == 13, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, fgInactive);
				cx += 32f;

				if (wrapTwoRows)
				{
					cx = boxX + 10f;
					cy = boxY + 30f;
				}

				toolbarBtnRects[5] = DrawPillBtn(cx, cy, 24f, bh, "-", false, hoveredToolbarBtnIndex == 5, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, fgInactive);
				cx += 27f;
				toolbarBtnRects[6] = DrawPillBtn(cx, cy, 74f, bh, stepLabel, runtimeAutoStep, hoveredToolbarBtnIndex == 6, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, runtimeAutoStep ? fgActive : fgInactive);
				cx += 77f;
				toolbarBtnRects[7] = DrawPillBtn(cx, cy, 24f, bh, "+", false, hoveredToolbarBtnIndex == 7, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, fgInactive);
				cx += 30f;

				toolbarBtnRects[14] = DrawPillBtn(cx, cy, 36f, bh, "HP", runtimeShowHalfpack, hoveredToolbarBtnIndex == 14, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, runtimeShowHalfpack ? fgActive : fgInactive);
				cx += 40f;
				toolbarBtnRects[8] = DrawPillBtn(cx, cy, 46f, bh, "Zones", runtimeShowImbZones, hoveredToolbarBtnIndex == 8, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, runtimeShowImbZones ? fgActive : fgInactive);
				cx += 50f;
				toolbarBtnRects[9] = DrawPillBtn(cx, cy, 46f, bh, "Delta", runtimeShowStatGrid, hoveredToolbarBtnIndex == 9, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, runtimeShowStatGrid ? fgActive : fgInactive);
				cx += 50f;
				toolbarBtnRects[10] = DrawPillBtn(cx, cy, 46f, bh, themeLabel, runtimeLightTheme, hoveredToolbarBtnIndex == 10, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, runtimeLightTheme ? fgActive : fgInactive);
				cx += 50f;
				toolbarBtnRects[11] = DrawPillBtn(cx, cy, 76f, bh, feedShort, true, hoveredToolbarBtnIndex == 11, tfBtn, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, fgActive);
			}
		}

		private void RenderOrderflowLoadingOverlay()
		{
			if (RenderTarget == null || ChartPanel == null)
				return;
			try
			{
				float px = ChartPanel.X;
				float py = ChartPanel.Y;
				float pw = ChartPanel.W;
				float ph = ChartPanel.H;
				if (pw <= 10f || ph <= 10f)
					return;

				string sym = Instrument != null ? Instrument.FullName : "Instrument";
				using (var bgBrush = new SolidColorBrush(RenderTarget, new Color4(0.055f, 0.075f, 0.105f, 1.0f)))
				using (var pillBg = new SolidColorBrush(RenderTarget, new Color4(0.08f, 0.12f, 0.18f, 0.96f)))
				using (var pillBorder = new SolidColorBrush(RenderTarget, new Color4(0.031f, 0.600f, 0.506f, 0.80f)))
				using (var txtBrush = new SolidColorBrush(RenderTarget, new Color4(0.96f, 0.98f, 1.0f, 1.0f)))
				using (var tf = new TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 12.5f))
				{
					RenderTarget.FillRectangle(new RectangleF(px, py, pw, ph), bgBrush);
					tf.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
					tf.ParagraphAlignment = ParagraphAlignment.Center;
					float bw = Math.Min(340f, pw - 32f);
					float bh = 36f;
					var badgeRect = new RectangleF(px + (pw - bw) * 0.5f, py + (ph - bh) * 0.5f, bw, bh);
					var rr = new RoundedRectangle { Rect = badgeRect, RadiusX = 5f, RadiusY = 5f };
					RenderTarget.FillRoundedRectangle(rr, pillBg);
					RenderTarget.DrawRoundedRectangle(rr, pillBorder, 1.1f);
					RenderTarget.DrawText("Loading Orderflow — " + sym + "...", tf, badgeRect, txtBrush);
				}
			}
			catch { }
		}

		private RectangleF DrawPillBtn(
			float x, float y, float w, float h, string text, bool active, bool hovered,
			TextFormat tf, SolidColorBrush bg, SolidColorBrush hoverBg, SolidColorBrush activeBg,
			SolidColorBrush border, SolidColorBrush hoverBorder, SolidColorBrush fg)
		{
			var r = new RectangleF(x, y, w, h);
			var rr = new RoundedRectangle { Rect = r, RadiusX = 3.5f, RadiusY = 3.5f };
			RenderTarget.FillRoundedRectangle(rr, hovered ? hoverBg : (active ? activeBg : bg));
			RenderTarget.DrawRoundedRectangle(rr, hovered ? hoverBorder : border, hovered ? 1.25f : 0.9f);
			RenderTarget.DrawText(text, tf, r, fg);
			return r;
		}

		/// <summary>
		/// GoCharting compact footprint volume format: 0, 450, 750, 1.2K, 5.9K, 14.2K
		/// </summary>
		private static string FormatGoChartingVol(long val)
		{
			long abs = Math.Abs(val);
			if (abs >= 1000000)
				return (val / 1000000.0).ToString("0.#M", CultureInfo.InvariantCulture);
			if (abs >= 1000)
				return (val / 1000.0).ToString("0.#K", CultureInfo.InvariantCulture);
			return val.ToString(CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// GoCharting bottom Delta Strip format: 195, -465, 4.68K, 35.04K, -12.42K
		/// </summary>
		private static string FormatGoChartingDeltaStrip(long val)
		{
			long abs = Math.Abs(val);
			if (abs >= 1000000)
				return (val / 1000000.0).ToString("0.##M", CultureInfo.InvariantCulture);
			if (abs >= 1000)
				return (val / 1000.0).ToString("0.##K", CultureInfo.InvariantCulture);
			return val.ToString(CultureInfo.InvariantCulture);
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AK10XGlobalOrderflow[] cacheAK10XGlobalOrderflow;
		public AK10XGlobalOrderflow AK10XGlobalOrderflow(AK10XOrderflowDataFeed dataFeed, AK10XOrderflowMode footprintMode, int candlestickZoomThresholdPx, bool useLightTheme, int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showHalfpack, bool showBarVA, int barVAPercentage, bool showSessionVA, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showImbalanceSRZones, int imbalanceSRConsecutiveLevels, bool showBarDelta, bool showStatisticGrid, int momentumMaxWickPct)
		{
			return AK10XGlobalOrderflow(Input, dataFeed, footprintMode, candlestickZoomThresholdPx, useLightTheme, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showHalfpack, showBarVA, barVAPercentage, showSessionVA, showImbalance, imbalancePercentage, imbalanceFilter, showImbalanceSRZones, imbalanceSRConsecutiveLevels, showBarDelta, showStatisticGrid, momentumMaxWickPct);
		}

		public AK10XGlobalOrderflow AK10XGlobalOrderflow(ISeries<double> input, AK10XOrderflowDataFeed dataFeed, AK10XOrderflowMode footprintMode, int candlestickZoomThresholdPx, bool useLightTheme, int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showHalfpack, bool showBarVA, int barVAPercentage, bool showSessionVA, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showImbalanceSRZones, int imbalanceSRConsecutiveLevels, bool showBarDelta, bool showStatisticGrid, int momentumMaxWickPct)
		{
			if (cacheAK10XGlobalOrderflow != null)
				for (int idx = 0; idx < cacheAK10XGlobalOrderflow.Length; idx++)
					if (cacheAK10XGlobalOrderflow[idx] != null && cacheAK10XGlobalOrderflow[idx].DataFeed == dataFeed && cacheAK10XGlobalOrderflow[idx].FootprintMode == footprintMode && cacheAK10XGlobalOrderflow[idx].CandlestickZoomThresholdPx == candlestickZoomThresholdPx && cacheAK10XGlobalOrderflow[idx].UseLightTheme == useLightTheme && cacheAK10XGlobalOrderflow[idx].TicksPerLevel == ticksPerLevel && cacheAK10XGlobalOrderflow[idx].AutoScalePriceStep == autoScalePriceStep && cacheAK10XGlobalOrderflow[idx].BarSpacePx == barSpacePx && cacheAK10XGlobalOrderflow[idx].ShowBarPOC == showBarPOC && cacheAK10XGlobalOrderflow[idx].ShowHalfpack == showHalfpack && cacheAK10XGlobalOrderflow[idx].ShowBarVA == showBarVA && cacheAK10XGlobalOrderflow[idx].BarVAPercentage == barVAPercentage && cacheAK10XGlobalOrderflow[idx].ShowSessionVA == showSessionVA && cacheAK10XGlobalOrderflow[idx].ShowImbalance == showImbalance && cacheAK10XGlobalOrderflow[idx].ImbalancePercentage == imbalancePercentage && cacheAK10XGlobalOrderflow[idx].ImbalanceFilter == imbalanceFilter && cacheAK10XGlobalOrderflow[idx].ShowImbalanceSRZones == showImbalanceSRZones && cacheAK10XGlobalOrderflow[idx].ImbalanceSRConsecutiveLevels == imbalanceSRConsecutiveLevels && cacheAK10XGlobalOrderflow[idx].ShowBarDelta == showBarDelta && cacheAK10XGlobalOrderflow[idx].ShowStatisticGrid == showStatisticGrid && cacheAK10XGlobalOrderflow[idx].MomentumMaxWickPct == momentumMaxWickPct && cacheAK10XGlobalOrderflow[idx].EqualsInput(input))
						return cacheAK10XGlobalOrderflow[idx];
			return CacheIndicator<AK10XGlobalOrderflow>(new AK10XGlobalOrderflow(){ DataFeed = dataFeed, FootprintMode = footprintMode, CandlestickZoomThresholdPx = candlestickZoomThresholdPx, UseLightTheme = useLightTheme, TicksPerLevel = ticksPerLevel, AutoScalePriceStep = autoScalePriceStep, BarSpacePx = barSpacePx, ShowBarPOC = showBarPOC, ShowHalfpack = showHalfpack, ShowBarVA = showBarVA, BarVAPercentage = barVAPercentage, ShowSessionVA = showSessionVA, ShowImbalance = showImbalance, ImbalancePercentage = imbalancePercentage, ImbalanceFilter = imbalanceFilter, ShowImbalanceSRZones = showImbalanceSRZones, ImbalanceSRConsecutiveLevels = imbalanceSRConsecutiveLevels, ShowBarDelta = showBarDelta, ShowStatisticGrid = showStatisticGrid, MomentumMaxWickPct = momentumMaxWickPct }, input, ref cacheAK10XGlobalOrderflow);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AK10XGlobalOrderflow AK10XGlobalOrderflow(Indicators.AK10XOrderflowDataFeed dataFeed, Indicators.AK10XOrderflowMode footprintMode, int candlestickZoomThresholdPx, bool useLightTheme, int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showHalfpack, bool showBarVA, int barVAPercentage, bool showSessionVA, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showImbalanceSRZones, int imbalanceSRConsecutiveLevels, bool showBarDelta, bool showStatisticGrid, int momentumMaxWickPct)
		{
			return indicator.AK10XGlobalOrderflow(Input, dataFeed, footprintMode, candlestickZoomThresholdPx, useLightTheme, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showHalfpack, showBarVA, barVAPercentage, showSessionVA, showImbalance, imbalancePercentage, imbalanceFilter, showImbalanceSRZones, imbalanceSRConsecutiveLevels, showBarDelta, showStatisticGrid, momentumMaxWickPct);
		}

		public Indicators.AK10XGlobalOrderflow AK10XGlobalOrderflow(ISeries<double> input, Indicators.AK10XOrderflowDataFeed dataFeed, Indicators.AK10XOrderflowMode footprintMode, int candlestickZoomThresholdPx, bool useLightTheme, int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showHalfpack, bool showBarVA, int barVAPercentage, bool showSessionVA, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showImbalanceSRZones, int imbalanceSRConsecutiveLevels, bool showBarDelta, bool showStatisticGrid, int momentumMaxWickPct)
		{
			return indicator.AK10XGlobalOrderflow(input, dataFeed, footprintMode, candlestickZoomThresholdPx, useLightTheme, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showHalfpack, showBarVA, barVAPercentage, showSessionVA, showImbalance, imbalancePercentage, imbalanceFilter, showImbalanceSRZones, imbalanceSRConsecutiveLevels, showBarDelta, showStatisticGrid, momentumMaxWickPct);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AK10XGlobalOrderflow AK10XGlobalOrderflow(Indicators.AK10XOrderflowDataFeed dataFeed, Indicators.AK10XOrderflowMode footprintMode, int candlestickZoomThresholdPx, bool useLightTheme, int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showHalfpack, bool showBarVA, int barVAPercentage, bool showSessionVA, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showImbalanceSRZones, int imbalanceSRConsecutiveLevels, bool showBarDelta, bool showStatisticGrid, int momentumMaxWickPct)
		{
			return indicator.AK10XGlobalOrderflow(Input, dataFeed, footprintMode, candlestickZoomThresholdPx, useLightTheme, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showHalfpack, showBarVA, barVAPercentage, showSessionVA, showImbalance, imbalancePercentage, imbalanceFilter, showImbalanceSRZones, imbalanceSRConsecutiveLevels, showBarDelta, showStatisticGrid, momentumMaxWickPct);
		}

		public Indicators.AK10XGlobalOrderflow AK10XGlobalOrderflow(ISeries<double> input, Indicators.AK10XOrderflowDataFeed dataFeed, Indicators.AK10XOrderflowMode footprintMode, int candlestickZoomThresholdPx, bool useLightTheme, int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showHalfpack, bool showBarVA, int barVAPercentage, bool showSessionVA, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showImbalanceSRZones, int imbalanceSRConsecutiveLevels, bool showBarDelta, bool showStatisticGrid, int momentumMaxWickPct)
		{
			return indicator.AK10XGlobalOrderflow(input, dataFeed, footprintMode, candlestickZoomThresholdPx, useLightTheme, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showHalfpack, showBarVA, barVAPercentage, showSessionVA, showImbalance, imbalancePercentage, imbalanceFilter, showImbalanceSRZones, imbalanceSRConsecutiveLevels, showBarDelta, showStatisticGrid, momentumMaxWickPct);
		}
	}
}

#endregion
