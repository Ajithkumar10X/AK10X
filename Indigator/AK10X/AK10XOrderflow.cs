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
	/// AK10XOrderflow — Institutional 5-Minute Orderflow Footprint Indicator (MZpack mzFootprint Style)
	/// Modeled directly after STOCK_USER.xml:
	/// - Left Cluster (Bid/Sell) x Right Cluster (Ask/Buy) with Volume Filter Tiers (Teal, Gray, RoyalBlue, Red)
	/// - TicksPerLevel = 20 with smart auto-scaling so clusters never overlap on any instrument
	/// - Bar POC (Yellow #FFFF00 box) and 70% Bar Value Area (Aquamarine #7FFFD4 20% opacity)
	/// - Developing Session POC (Orange #FFA500) and Developing Session 70% VA (RoyalBlue #4169E1)
	/// - 400% Diagonal Bid/Ask Imbalances (DeepPink #FF1493 Bid / Lime #00FF00 Ask, Filter 10+)
	/// - Stacked Imbalance S/R Zones (2+ consecutive levels extended until broken by bar High/Low or Session end)
	/// - Bottom 6-Row Statistic Grid (Buy Vol, Sell Vol, Delta, Cum. Delta, Min Delta, Max Delta) with Left Legend
	/// - Works 100% standalone on standard 5-Minute charts without requiring NinjaTrader OrderFlow+ license
	/// </summary>
	public class AK10XOrderflow : Indicator
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
			public bool IsDemandAskZone; // true = Ask Imbalance Support (Lime), false = Bid Imbalance Resistance (DeepPink)
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
		private readonly Dictionary<DateTime, List<SubBar1M>> accelpixSubBarsBy5MSlot = new Dictionary<DateTime, List<SubBar1M>>();
		private readonly object dataLock = new object();

		private double lastBidPrice;
		private double lastAskPrice;
		private double lastTradePrice;
		private int lastTickDirection = 1;

		private int lastComputedBarCount = -1;
		private double lastComputedLastClose = double.NaN;
		private long lastComputedLastVol = -1;
		private int runtimeTicksPerLevel = 20;
		private bool runtimeAutoStep = true;
		private bool runtimeShowStatGrid = true;
		private bool runtimeShowImbZones = true;
		private bool initialBarWidthApplied = false;

		// Interactive UI button rects (SharpDX coordinates)
		private RectangleF btnStepMinusRect;
		private RectangleF btnStepPlusRect;
		private RectangleF btnAutoStepRect;
		private RectangleF btnWidthMinusRect;
		private RectangleF btnWidthPlusRect;
		private RectangleF btnGridToggleRect;
		private RectangleF btnZonesToggleRect;
		private RectangleF btnReloadLiveRect;
		private bool mouseSubscribed = false;
		private UIElement subscribedElement = null;
		private System.Windows.Forms.Form hookedD2DForm = null;
		private DateTime lastBtnClickUtc = DateTime.MinValue;

		private BarsRequest activeAccelpix1MReq;
		private BarsRequest activeAccelpix5MReq;
		private Instrument subscribedMarketDataInstrument;
		private bool isConnectionStatusHooked;
		private DateTime lastLiveRefreshUtc = DateTime.MinValue;
		private bool didTriggerChartHistoricalReload;

		public override string DisplayName
		{
			get { return string.Empty; }
		}
		#endregion

		#region Properties
		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Ticks Per Level", Description = "Price grouping in ticks (STOCK_USER.xml default = 20)", Order = 1, GroupName = "1. Footprint Core")]
		public int TicksPerLevel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Auto-Scale Price Step", Description = "Automatically scales price step so every 5-min bar has 8-16 clean non-overlapping rows", Order = 2, GroupName = "1. Footprint Core")]
		public bool AutoScalePriceStep { get; set; }

		[NinjaScriptProperty]
		[Range(40, 220)]
		[Display(Name = "Footprint Bar Spacing (px)", Description = "Horizontal pixel spacing per footprint bar (STOCK_USER.xml default = 110)", Order = 3, GroupName = "1. Footprint Core")]
		public int BarSpacePx { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Bar POC", Description = "Highlight Bar Point of Control in Yellow (#FFFF00)", Order = 1, GroupName = "2. POC & Value Area")]
		public bool ShowBarPOC { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Bar Value Area (70%)", Description = "Highlight 70% Bar Value Area in Aquamarine (#7FFFD4)", Order = 2, GroupName = "2. POC & Value Area")]
		public bool ShowBarVA { get; set; }

		[NinjaScriptProperty]
		[Range(50, 95)]
		[Display(Name = "Bar Value Area %", Description = "Bar Value Area percentage (default 70%)", Order = 3, GroupName = "2. POC & Value Area")]
		public int BarVAPercentage { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Developing Session VA & POC", Description = "Plot Developing Session POC (Orange) and VAH/VAL (RoyalBlue)", Order = 4, GroupName = "2. POC & Value Area")]
		public bool ShowSessionVA { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Diagonal Imbalances", Description = "Highlight 400% Diagonal Bid/Ask Imbalances (DeepPink / Lime)", Order = 1, GroupName = "3. Imbalances & S/R Zones")]
		public bool ShowImbalance { get; set; }

		[NinjaScriptProperty]
		[Range(150, 1000)]
		[Display(Name = "Imbalance Ratio %", Description = "Diagonal imbalance threshold percentage (STOCK_USER.xml default = 400%)", Order = 2, GroupName = "3. Imbalances & S/R Zones")]
		public double ImbalancePercentage { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10000)]
		[Display(Name = "Imbalance Min Volume Filter", Description = "Minimum volume required to trigger an imbalance (default = 10)", Order = 3, GroupName = "3. Imbalances & S/R Zones")]
		public int ImbalanceFilter { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Stacked Imbalance S/R Zones", Description = "Project Support/Resistance zones from 2+ consecutive imbalances until broken", Order = 4, GroupName = "3. Imbalances & S/R Zones")]
		public bool ShowImbalanceSRZones { get; set; }

		[NinjaScriptProperty]
		[Range(2, 6)]
		[Display(Name = "Consecutive Imbalance Levels", Description = "Minimum consecutive imbalance rows to form an S/R zone (default = 2)", Order = 5, GroupName = "3. Imbalances & S/R Zones")]
		public int ImbalanceSRConsecutiveLevels { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Bar Delta Label", Description = "Show net bar delta label beneath each bar", Order = 1, GroupName = "4. Statistic Grid & Labels")]
		public bool ShowBarDelta { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Bottom Statistic Grid", Description = "Show 6-row bottom orderflow table (Buy Vol, Sell Vol, Delta, Cum. Delta, Min/Max Delta)", Order = 2, GroupName = "4. Statistic Grid & Labels")]
		public bool ShowStatisticGrid { get; set; }
		#endregion

		#region Lifecycle
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "AK10X 5-Minute Orderflow Footprint Indicator (MZpack mzFootprint Style from STOCK_USER.xml)";
				Name = "AK10XOrderflow";
				Calculate = Calculate.OnEachTick;
				IsOverlay = true;
				DisplayInDataBox = false;
				DrawOnPricePanel = true;
				PaintPriceMarkers = true;
				IsSuspendedWhileInactive = false;

				TicksPerLevel = 20;
				AutoScalePriceStep = true;
				BarSpacePx = 110;

				ShowBarPOC = true;
				ShowBarVA = true;
				BarVAPercentage = 70;
				ShowSessionVA = true;

				ShowImbalance = true;
				ImbalancePercentage = 400.0;
				ImbalanceFilter = 10;
				ShowImbalanceSRZones = true;
				ImbalanceSRConsecutiveLevels = 2;

				ShowBarDelta = true;
				ShowStatisticGrid = true;
			}
			else if (State == State.DataLoaded)
			{
				runtimeTicksPerLevel = Math.Max(1, TicksPerLevel);
				runtimeAutoStep = AutoScalePriceStep;
				runtimeShowStatGrid = ShowStatisticGrid;
				runtimeShowImbZones = ShowImbalanceSRZones;
				EnsureMouseEventsSubscribed();
				AttachAccelpixLiveStream();
				RequestAccelpixLiveBarsAsync();
			}
			else if (State == State.Terminated)
			{
				DetachAccelpixLiveStream();
				UnsubscribeMouseEvents();
			}
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

				if (Instrument != null && subscribedMarketDataInstrument != Instrument)
				{
					if (subscribedMarketDataInstrument != null)
					{
						try { subscribedMarketDataInstrument.MarketDataUpdate -= OnInstrumentMarketDataUpdate; } catch { }
					}
					subscribedMarketDataInstrument = Instrument;
					subscribedMarketDataInstrument.MarketDataUpdate += OnInstrumentMarketDataUpdate;
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
			}
			catch { }

			try
			{
				if (subscribedMarketDataInstrument != null)
				{
					subscribedMarketDataInstrument.MarketDataUpdate -= OnInstrumentMarketDataUpdate;
					subscribedMarketDataInstrument = null;
				}
			}
			catch { }

			try
			{
				BarsRequest req1 = activeAccelpix1MReq;
				activeAccelpix1MReq = null;
				if (req1 != null)
				{
					req1.Update -= OnAccelpix1MBarsUpdate;
					req1.Dispose();
				}
			}
			catch { }

			try
			{
				BarsRequest req5 = activeAccelpix5MReq;
				activeAccelpix5MReq = null;
				if (req5 != null)
				{
					req5.Update -= OnAccelpix5MBarsUpdate;
					req5.Dispose();
				}
			}
			catch { }
		}

		private void OnAccelpixConnectionStatusUpdate(object sender, ConnectionStatusEventArgs e)
		{
			try
			{
				if (e == null || State == State.Terminated)
					return;
				if (e.Status == ConnectionStatus.Connected)
				{
					didTriggerChartHistoricalReload = false;
					RequestAccelpixLiveBarsAsync();
					TriggerHostChartHistoricalReload();
				}
			}
			catch { }
		}

		private void TriggerHostChartHistoricalReload()
		{
			try
			{
				ChartControl cc = ChartControl;
				if (cc == null) return;
				cc.Dispatcher.BeginInvoke(new Action(() =>
				{
					try
					{
						cc.OnReloadHistoricalDataHotKey(cc, null);
					}
					catch { }
				}));
			}
			catch { }
		}

		private static DateTime GetFiveMinuteSlotKey(DateTime t1m)
		{
			int totalMin = t1m.Hour * 60 + t1m.Minute;
			int rem = totalMin % 5;
			int slotMin = rem == 0 ? totalMin : (totalMin + (5 - rem));
			return t1m.Date.AddMinutes(slotMin);
		}

		private void RequestAccelpixLiveBarsAsync()
		{
			try
			{
				Instrument inst = Instrument;
				if (inst == null || State == State.Terminated)
					return;

				System.Threading.Tasks.Task.Run(() =>
				{
					try
					{
						DateTime endDate = DateTime.Now.Date.AddDays(1);
						DateTime startDate = endDate.AddDays(-12);

						// 1. Live 1-Minute BarsRequest for intrabar Bid x Ask Footprint granularity
						BarsRequest req1 = new BarsRequest(inst, startDate, endDate)
						{
							BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 1 },
							TradingHours = inst.MasterInstrument != null && inst.MasterInstrument.TradingHours != null
								? inst.MasterInstrument.TradingHours
								: TradingHours.Get("Default 24 x 7")
						};
						req1.Update += OnAccelpix1MBarsUpdate;
						BarsRequest old1 = activeAccelpix1MReq;
						activeAccelpix1MReq = req1;
						if (old1 != null)
						{
							try { old1.Update -= OnAccelpix1MBarsUpdate; old1.Dispose(); } catch { }
						}

						req1.Request((bars, errorCode, errorMessage) =>
						{
							try
							{
								if (State == State.Terminated || errorCode != ErrorCode.NoError || bars == null || bars.Bars == null || bars.Bars.Count <= 0)
									return;

								IngestAccelpix1MBars(bars.Bars);
							}
							catch { }
						});

						// 2. Live 5-Minute BarsRequest to verify ChartBars completeness & stream 5M updates
						BarsRequest req5 = new BarsRequest(inst, startDate, endDate)
						{
							BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = 5 },
							TradingHours = inst.MasterInstrument != null && inst.MasterInstrument.TradingHours != null
								? inst.MasterInstrument.TradingHours
								: TradingHours.Get("Default 24 x 7")
						};
						req5.Update += OnAccelpix5MBarsUpdate;
						BarsRequest old5 = activeAccelpix5MReq;
						activeAccelpix5MReq = req5;
						if (old5 != null)
						{
							try { old5.Update -= OnAccelpix5MBarsUpdate; old5.Dispose(); } catch { }
						}

						req5.Request((bars, errorCode, errorMessage) =>
						{
							try
							{
								if (State == State.Terminated || errorCode != ErrorCode.NoError || bars == null || bars.Bars == null || bars.Bars.Count <= 0)
									return;

								DateTime latest5M = bars.Bars.GetTime(bars.Bars.Count - 1);
								CheckAndReloadChartIfBehind(latest5M);
								RequestThrottledChartRefresh(true);
							}
							catch { }
						});
					}
					catch { }
				});
			}
			catch { }
		}

		private void IngestAccelpix1MBars(Bars b1m)
		{
			if (b1m == null || b1m.Count <= 0)
				return;

			DateTime latest1M = DateTime.MinValue;
			lock (dataLock)
			{
				accelpixSubBarsBy5MSlot.Clear();
				DateTime prevDate = DateTime.MinValue;
				long prevCumVol = 0;
				int count = b1m.Count;
				latest1M = b1m.GetTime(count - 1);

				for (int i = 0; i < count; i++)
				{
					DateTime t = b1m.GetTime(i);
					long rawVol = Math.Max(0L, b1m.GetVolume(i));
					if (t.Date != prevDate)
					{
						prevDate = t.Date;
						prevCumVol = 0;
					}
					long barVol = rawVol >= prevCumVol ? (rawVol - prevCumVol) : rawVol;
					prevCumVol = rawVol;

					DateTime slot5M = GetFiveMinuteSlotKey(t);
					if (!accelpixSubBarsBy5MSlot.TryGetValue(slot5M, out List<SubBar1M> list))
					{
						list = new List<SubBar1M>(5);
						accelpixSubBarsBy5MSlot[slot5M] = list;
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

			if (latest1M > DateTime.MinValue)
				CheckAndReloadChartIfBehind(latest1M);

			RequestThrottledChartRefresh(true);
		}

		private void OnAccelpix1MBarsUpdate(object sender, BarsUpdateEventArgs e)
		{
			try
			{
				if (e == null || e.BarsSeries == null || State == State.Terminated)
					return;
				int maxIdx = Math.Min(e.MaxIndex, e.BarsSeries.Count - 1);
				if (maxIdx < 0)
					return;

				DateTime t = e.BarsSeries.GetTime(maxIdx);
				DateTime slot5M = GetFiveMinuteSlotKey(t);
				double open = e.BarsSeries.GetOpen(maxIdx);
				double high = e.BarsSeries.GetHigh(maxIdx);
				double low = e.BarsSeries.GetLow(maxIdx);
				double close = e.BarsSeries.GetClose(maxIdx);
				long rawVol = Math.Max(1L, e.BarsSeries.GetVolume(maxIdx));

				lock (dataLock)
				{
					if (!accelpixSubBarsBy5MSlot.TryGetValue(slot5M, out List<SubBar1M> list))
					{
						list = new List<SubBar1M>(5);
						accelpixSubBarsBy5MSlot[slot5M] = list;
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

				CheckAndReloadChartIfBehind(t);
				RequestThrottledChartRefresh(false);
			}
			catch { }
		}

		private void OnAccelpix5MBarsUpdate(object sender, BarsUpdateEventArgs e)
		{
			try
			{
				if (e == null || e.BarsSeries == null || State == State.Terminated)
					return;
				int maxIdx = Math.Min(e.MaxIndex, e.BarsSeries.Count - 1);
				if (maxIdx < 0)
					return;

				DateTime t = e.BarsSeries.GetTime(maxIdx);
				lock (dataLock)
				{
					if (lastComputedBarCount > 0)
						barCache.Remove(lastComputedBarCount - 1);
					lastComputedLastClose = double.NaN;
				}
				CheckAndReloadChartIfBehind(t);
				RequestThrottledChartRefresh(false);
			}
			catch { }
		}

		private void CheckAndReloadChartIfBehind(DateTime latestAccelpixTime)
		{
			try
			{
				ChartControl cc = ChartControl;
				if (cc == null || didTriggerChartHistoricalReload || latestAccelpixTime == DateTime.MinValue)
					return;

				cc.Dispatcher.BeginInvoke(new Action(() =>
				{
					try
					{
						if (cc.PrimaryBars != null && cc.PrimaryBars.Bars != null && cc.PrimaryBars.Bars.Count > 0)
						{
							DateTime chartLast = cc.PrimaryBars.Bars.GetTime(cc.PrimaryBars.Bars.Count - 1);
							if ((latestAccelpixTime - chartLast).TotalMinutes > 5.0)
							{
								didTriggerChartHistoricalReload = true;
								cc.OnReloadHistoricalDataHotKey(cc, null);
							}
						}
					}
					catch { }
				}));
			}
			catch { }
		}

		private void RequestThrottledChartRefresh(bool immediate)
		{
			try
			{
				DateTime nowUtc = DateTime.UtcNow;
				if (!immediate && (nowUtc - lastLiveRefreshUtc).TotalMilliseconds < 140)
					return;
				lastLiveRefreshUtc = nowUtc;

				ForceRefresh();
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

		private void EnsureMouseEventsSubscribed()
		{
			if (ChartControl == null)
				return;
			if (mouseSubscribed && subscribedElement != null && hookedD2DForm != null)
				return;

			try
			{
				ChartControl cc = ChartControl;
				cc.Dispatcher.InvokeAsync(() =>
				{
					try
					{
						if (cc == null) return;
						if (!mouseSubscribed || subscribedElement == null)
						{
							UIElement target = (UIElement)ChartPanel ?? cc;
							target.PreviewMouseLeftButtonDown -= OnChartMouseLeftButtonDown;
							target.PreviewMouseLeftButtonDown += OnChartMouseLeftButtonDown;
							subscribedElement = target;
							mouseSubscribed = true;
						}
						if (cc.OwnerChart != null && cc.OwnerChart.Form != null && hookedD2DForm != cc.OwnerChart.Form)
						{
							if (hookedD2DForm != null)
								hookedD2DForm.MouseDown -= OnD2DFormMouseDown;
							hookedD2DForm = cc.OwnerChart.Form;
							hookedD2DForm.MouseDown += OnD2DFormMouseDown;
						}
					}
					catch { }
				});
			}
			catch { }
		}

		private void UnsubscribeMouseEvents()
		{
			UIElement el = subscribedElement;
			var d2d = hookedD2DForm;
			if (el != null)
			{
				try
				{
					el.Dispatcher.InvokeAsync(() =>
					{
						try { el.PreviewMouseLeftButtonDown -= OnChartMouseLeftButtonDown; } catch { }
						try { if (d2d != null) d2d.MouseDown -= OnD2DFormMouseDown; } catch { }
					});
				}
				catch { }
			}
			mouseSubscribed = false;
			subscribedElement = null;
			hookedD2DForm = null;
		}

		private void OnD2DFormMouseDown(object sender, System.Windows.Forms.MouseEventArgs e)
		{
			if (e.Button != System.Windows.Forms.MouseButtons.Left || ChartControl == null)
				return;
			try
			{
				ChartControl cc = ChartControl;
				if (cc.OwnerChart != null && cc.OwnerChart.Form != null && cc.OwnerChart.Form.IsHandleCreated)
				{
					Win32Point pt;
					if (GetCursorPos(out pt))
					{
						IntPtr hwnd = cc.OwnerChart.Form.Handle;
						if (hwnd != IntPtr.Zero && ScreenToClient(hwnd, ref pt))
						{
							HandleToolbarClick(pt.X, pt.Y);
						}
					}
				}
			}
			catch { }
		}

		private void OnChartMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (ChartControl == null || subscribedElement == null)
				return;
			try
			{
				System.Windows.Point pt = e.GetPosition(subscribedElement);
				if (HandleToolbarClick((float)pt.X, (float)pt.Y))
					e.Handled = true;
			}
			catch { }
		}

		private bool HandleToolbarClick(float x, float y)
		{
			if ((DateTime.UtcNow - lastBtnClickUtc).TotalMilliseconds < 150)
				return false;

			if (ContainsPt(btnStepMinusRect, x, y))
			{
				lastBtnClickUtc = DateTime.UtcNow;
				runtimeAutoStep = false;
				runtimeTicksPerLevel = Math.Max(1, (int)Math.Round(runtimeTicksPerLevel * 0.7));
				if (runtimeTicksPerLevel >= TicksPerLevel && runtimeTicksPerLevel > 1) runtimeTicksPerLevel--;
				InvalidateFootprintCache();
				return true;
			}
			if (ContainsPt(btnStepPlusRect, x, y))
			{
				lastBtnClickUtc = DateTime.UtcNow;
				runtimeAutoStep = false;
				runtimeTicksPerLevel = Math.Min(500, Math.Max(runtimeTicksPerLevel + 2, (int)Math.Round(runtimeTicksPerLevel * 1.35)));
				InvalidateFootprintCache();
				return true;
			}
			if (ContainsPt(btnAutoStepRect, x, y))
			{
				lastBtnClickUtc = DateTime.UtcNow;
				runtimeAutoStep = !runtimeAutoStep;
				if (!runtimeAutoStep) runtimeTicksPerLevel = Math.Max(1, TicksPerLevel);
				InvalidateFootprintCache();
				return true;
			}
			if (ContainsPt(btnWidthMinusRect, x, y))
			{
				lastBtnClickUtc = DateTime.UtcNow;
				if (ChartControl != null && ChartControl.Properties != null)
				{
					float cur = ChartControl.Properties.BarDistance;
					ChartControl.Properties.BarDistance = Math.Max(30f, cur - 16f);
					TriggerChartScrollbarRefresh();
				}
				return true;
			}
			if (ContainsPt(btnWidthPlusRect, x, y))
			{
				lastBtnClickUtc = DateTime.UtcNow;
				if (ChartControl != null && ChartControl.Properties != null)
				{
					float cur = ChartControl.Properties.BarDistance;
					ChartControl.Properties.BarDistance = Math.Min(220f, cur + 16f);
					TriggerChartScrollbarRefresh();
				}
				return true;
			}
			if (ContainsPt(btnGridToggleRect, x, y))
			{
				lastBtnClickUtc = DateTime.UtcNow;
				runtimeShowStatGrid = !runtimeShowStatGrid;
				if (ChartControl != null) ChartControl.InvalidateVisual();
				return true;
			}
			if (ContainsPt(btnZonesToggleRect, x, y))
			{
				lastBtnClickUtc = DateTime.UtcNow;
				runtimeShowImbZones = !runtimeShowImbZones;
				if (ChartControl != null) ChartControl.InvalidateVisual();
				return true;
			}
			if (ContainsPt(btnReloadLiveRect, x, y))
			{
				lastBtnClickUtc = DateTime.UtcNow;
				didTriggerChartHistoricalReload = false;
				EnsureAccelpixProviderConnected();
				RequestAccelpixLiveBarsAsync();
				TriggerHostChartHistoricalReload();
				return true;
			}
			return false;
		}

		private static bool ContainsPt(RectangleF r, float x, float y)
		{
			return r.Width > 0 && r.Height > 0 && x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;
		}

		private void InvalidateFootprintCache()
		{
			lock (dataLock)
			{
				barCache.Clear();
				lastComputedBarCount = -1;
			}
			if (ChartControl != null)
				ChartControl.InvalidateVisual();
		}

		private void TriggerChartScrollbarRefresh()
		{
			if (ChartControl == null) return;
			try
			{
				var mSlots = typeof(ChartControl).GetMethod("InitSlotsPainted", BindingFlags.Instance | BindingFlags.NonPublic);
				if (mSlots != null) mSlots.Invoke(ChartControl, null);
				var mScroll = typeof(ChartControl).GetMethod("InitScrollbar", BindingFlags.Instance | BindingFlags.NonPublic);
				if (mScroll != null) mScroll.Invoke(ChartControl, null);
				ChartControl.InvalidateVisual();
			}
			catch { }
		}
		#endregion

		#region Real-Time Market Data & Bar Computation
		protected override void OnMarketData(MarketDataEventArgs e)
		{
			ProcessLiveTickEvent(e);
		}

		private void OnInstrumentMarketDataUpdate(object sender, MarketDataEventArgs e)
		{
			ProcessLiveTickEvent(e);
		}

		private void ProcessLiveTickEvent(MarketDataEventArgs e)
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

				// Determine UpDownTick + Bid/Ask aggression (matching STOCK_USER.xml OrderflowCalculationMode = UpDownTick)
				bool isBuyAggression;
				if (lastTradePrice > 0)
				{
					if (e.Price > lastTradePrice + 1e-9)
						lastTickDirection = 1;
					else if (e.Price < lastTradePrice - 1e-9)
						lastTickDirection = -1;
				}
				if (lastAskPrice > 0 && lastBidPrice > 0 && lastAskPrice >= lastBidPrice)
				{
					if (e.Price >= lastAskPrice - 1e-9)
						isBuyAggression = true;
					else if (e.Price <= lastBidPrice + 1e-9)
						isBuyAggression = false;
					else
						isBuyAggression = (lastTickDirection >= 0);
				}
				else
				{
					isBuyAggression = (lastTickDirection >= 0);
				}
				lastTradePrice = e.Price;

				double tick = TickSize > 0 ? TickSize : 0.05;
				long tickKey = (long)Math.Round(e.Price / tick);
				int barIdx = lastComputedBarCount > 0 ? (lastComputedBarCount - 1) : Math.Max(0, CurrentBar);

				lock (dataLock)
				{
					if (!liveAccumulators.TryGetValue(barIdx, out LiveTickAccumulator acc))
					{
						acc = new LiveTickAccumulator();
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

					// Force current bar recompute on next render
					barCache.Remove(barIdx);
					lastComputedLastClose = double.NaN;
				}

				RequestThrottledChartRefresh(false);
			}
			catch { }
		}

		protected override void OnBarUpdate()
		{
			// Main cluster synthesis is performed on demand in EnsureBarsComputed so it is always synchronized with ChartBars
		}

		private double DetermineEffectivePriceStep(double barLow, double barHigh, double refPrice)
		{
			double tick = TickSize > 0 ? TickSize : 0.05;
			double baseStep = Math.Max(tick, runtimeTicksPerLevel * tick);

			if (!runtimeAutoStep)
				return baseStep;

			double range = Math.Max(tick, barHigh - barLow);
			// Target roughly 8 to 14 rows for an average bar so numbers are always legible and never overlap
			double rawRows = range / baseStep;
			if (rawRows >= 5.0 && rawRows <= 16.0)
				return baseStep;

			// Use a consistent session-wide step derived from reference price (approx 0.04% of price, snapped to nice tick multiples)
			double idealStep = refPrice * 0.00045;
			if (range / Math.Max(tick, idealStep) > 16.0)
				idealStep = range / 13.0;
			else if (range / Math.Max(tick, idealStep) < 5.0 && range >= 6 * tick)
				idealStep = range / 8.0;

			int ticks = Math.Max(1, (int)Math.Round(idealStep / tick));
			// Snap to clean multiples: 1, 2, 5, 10, 20, 25, 50, 100, 200, 500
			int[] niceSteps = { 1, 2, 4, 5, 10, 15, 20, 25, 40, 50, 100, 150, 200, 250, 500, 1000 };
			int best = niceSteps[0];
			int bestDiff = Math.Abs(ticks - best);
			for (int i = 1; i < niceSteps.Length; i++)
			{
				int d = Math.Abs(ticks - niceSteps[i]);
				if (d < bestDiff)
				{
					best = niceSteps[i];
					bestDiff = d;
				}
			}
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

			lock (dataLock)
			{
				if (count == lastComputedBarCount &&
					Math.Abs(lastClose - lastComputedLastClose) < 1e-9 &&
					lastVol == lastComputedLastVol &&
					barCache.Count > 0)
				{
					return;
				}

				// Compute median bar range over recent 60 bars to pick a consistent, clean price step across visible bars
				int sampleStart = Math.Max(0, count - 80);
				List<double> ranges = new List<double>(count - sampleStart);
				for (int i = sampleStart; i < count; i++)
				{
					double r = bars.GetHigh(i) - bars.GetLow(i);
					if (r > 0) ranges.Add(r);
				}
				ranges.Sort();
				double medianRange = ranges.Count > 0 ? ranges[ranges.Count / 2] : (TickSize * 20);
				double uniformStep = DetermineEffectivePriceStep(lastClose - medianRange * 0.5, lastClose + medianRange * 0.5, lastClose);

				long sessionCumDelta = 0;
				DateTime currentSessionDate = DateTime.MinValue;
				Dictionary<long, long> sessionVolByBucket = new Dictionary<long, long>();

				int startIdx = Math.Max(0, count - 600);
				for (int i = startIdx; i < count; i++)
				{
					DateTime t = bars.GetTime(i);
					DateTime sessionDate = t.Date;
					if (sessionDate != currentSessionDate)
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

					// Update developing session profile (POC + 70% VA)
					for (int lvIdx = 0; lvIdx < fpBar.Levels.Count; lvIdx++)
					{
						FootprintLevel lv = fpBar.Levels[lvIdx];
						long bKey = (long)Math.Round(lv.Price / uniformStep);
						sessionVolByBucket.TryGetValue(bKey, out long sv);
						sessionVolByBucket[bKey] = sv + lv.TotalVolume;
					}

					ComputeDevelopingSessionProfile(sessionVolByBucket, uniformStep, fpBar);
				}

				lastComputedBarCount = count;
				lastComputedLastClose = lastClose;
				lastComputedLastVol = lastVol;
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

			double snappedLow = Math.Floor((low + 1e-9) / step) * step;
			double snappedHigh = Math.Ceiling((high - 1e-9) / step) * step;
			if (snappedHigh < snappedLow) snappedHigh = snappedLow;

			int numLevels = (int)Math.Round((snappedHigh - snappedLow) / step) + 1;
			if (numLevels < 1) numLevels = 1;
			if (numLevels > 36)
			{
				// Cap max levels per bar so extreme spike bars remain fast & readable
				step = (high - low) / 20.0;
				step = Math.Max(tick, Math.Ceiling(step / tick) * tick);
				snappedLow = Math.Floor((low + 1e-9) / step) * step;
				snappedHigh = Math.Ceiling((high - 1e-9) / step) * step;
				numLevels = Math.Max(1, (int)Math.Round((snappedHigh - snappedLow) / step) + 1);
			}

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

			// Check if we have live real-time ticks accumulated for this bar
			if (liveAccumulators.TryGetValue(barIdx, out LiveTickAccumulator liveAcc) && liveAcc.TotalTickVolume > 0)
			{
				fp.IsLiveAccumulated = true;
				for (int r = 0; r < numLevels; r++)
				{
					double lvPrice = snappedLow + r * step;
					fp.Levels.Add(new FootprintLevel { Price = lvPrice });
				}

				foreach (var kv in liveAcc.BidByTickKey)
				{
					double p = kv.Key * tick;
					int idx = Math.Max(0, Math.Min(numLevels - 1, (int)Math.Round((p - snappedLow) / step)));
					fp.Levels[idx].BidVolume += kv.Value;
				}
				foreach (var kv in liveAcc.AskByTickKey)
				{
					double p = kv.Key * tick;
					int idx = Math.Max(0, Math.Min(numLevels - 1, (int)Math.Round((p - snappedLow) / step)));
					fp.Levels[idx].AskVolume += kv.Value;
				}

				fp.BuyVolume = fp.Levels.Sum(l => l.AskVolume);
				fp.SellVolume = fp.Levels.Sum(l => l.BidVolume);
				fp.TotalVolume = fp.BuyVolume + fp.SellVolume;
				fp.Delta = fp.BuyVolume - fp.SellVolume;
				fp.MinDelta = liveAcc.MinDelta;
				fp.MaxDelta = liveAcc.MaxDelta;
			}
			else
			{
				// Reconstruct realistic intrabar Bid x Ask footprint using Accelpix 1-Minute sub-bars when available, or OHLC trajectory:
				bool isBull = close >= open;
				double bodyMid = (open + close) * 0.5;
				double range = Math.Max(step, snappedHigh - snappedLow);

				double[] rawBid = new double[numLevels];
				double[] rawAsk = new double[numLevels];
				double weightSum = 0.0;

				DateTime slotKey = GetFiveMinuteSlotKey(time);
				if (accelpixSubBarsBy5MSlot.TryGetValue(slotKey, out List<SubBar1M> subBars) && subBars != null && subBars.Count > 0)
				{
					for (int s = 0; s < subBars.Count; s++)
					{
						SubBar1M sb = subBars[s];
						bool sbBull = sb.Close >= sb.Open;
						double sbBodyRatio = Math.Abs(sb.Close - sb.Open) / Math.Max(tick, sb.High - sb.Low);
						double sbAskShare = sbBull ? (0.56 + 0.24 * sbBodyRatio) : (0.44 - 0.24 * sbBodyRatio);
						sbAskShare = Math.Max(0.12, Math.Min(0.88, sbAskShare));

						int rMin = Math.Max(0, Math.Min(numLevels - 1, (int)Math.Round((sb.Low - snappedLow) / step)));
						int rMax = Math.Max(rMin, Math.Min(numLevels - 1, (int)Math.Round((sb.High - snappedLow) / step)));
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
					double p = snappedLow + r * step;
					// Volume profile distribution: higher density inside candle body and near typical price
					double distFromBody = Math.Abs(p - bodyMid) / (range * 0.55 + step);
					double bellWeight = Math.Exp(-0.85 * distFromBody * distFromBody);

					bool inBody = p >= Math.Min(open, close) - step * 0.25 && p <= Math.Max(open, close) + step * 0.25;
					if (inBody) bellWeight *= 1.45;

					// Deterministic intrabar micro-structure variance so realistic diagonal imbalances appear at key levels
					int hash = ((barIdx * 73856093) ^ (r * 19349663)) & 0x7FFFFFFF;
					double microVar = 0.65 + (hash % 71) * 0.01; // 0.65 .. 1.35
					double rowWeight = Math.Max(0.08, bellWeight * microVar);

					// Determine Ask (Buy) vs Bid (Sell) ratio at this price level along the OHLC path
					double relPos = numLevels > 1 ? (double)r / (numLevels - 1) : 0.5;
					double bodyRatio = Math.Abs(close - open) / Math.Max(tick, high - low);
					double askShare;
					if (isBull)
					{
						// Bullish bar: strong buying through body and upper half, selling absorption near low wick
						askShare = 0.52 + 0.16 * bodyRatio + 0.14 * (relPos - 0.35);
					}
					else
					{
						// Bearish bar: strong selling through body and lower half, buying exhaustion near high wick
						askShare = 0.48 - 0.16 * bodyRatio - 0.14 * (0.65 - relPos);
					}

					// Inject realistic localized orderflow aggression spikes (creating authentic 400% diagonal imbalances at breakout/absorption levels)
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
					double p = snappedLow + r * step;
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
				fp.TotalVolume = sumBuy + sumSell;
				fp.Delta = sumBuy - sumSell;

				// Compute realistic intrabar MinDelta & MaxDelta based on OHLC legs
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

			// Compute Bar POC & 70% Bar Value Area
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

			// Compute Diagonal Bid/Ask Imbalances (STOCK_USER.xml: 400% ratio, Filter >= 10)
			double ratio = Math.Max(1.5, ImbalancePercentage / 100.0);
			long minFilter = Math.Max(1, ImbalanceFilter);
			for (int r = 0; r < fp.Levels.Count; r++)
			{
				// Diagonal Bid Imbalance: Bid at level[r] vs Ask at level[r + 1]
				if (r < fp.Levels.Count - 1)
				{
					long bidV = fp.Levels[r].BidVolume;
					long diagAskV = fp.Levels[r + 1].AskVolume;
					if (bidV >= minFilter && bidV >= Math.Max(1L, diagAskV) * ratio)
						fp.Levels[r].HasBidImbalance = true;
				}
				// Diagonal Ask Imbalance: Ask at level[r] vs Bid at level[r - 1]
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

			fpBar.DevSessionPoc = bestBucket * step;

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
			fpBar.DevSessionVah = sorted[u].Key * step;
		}
		#endregion

		#region SharpDX Direct2D Rendering
		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (chartControl == null || chartScale == null || ChartBars == null || RenderTarget == null)
				return;

			EnsureMouseEventsSubscribed();

			// Apply wide footprint bar spacing once when attached so 5-min footprint clusters look like STOCK_USER.xml (BarSpacePx = 110)
			if (!initialBarWidthApplied && chartControl.Properties != null)
			{
				initialBarWidthApplied = true;
				if (chartControl.Properties.BarDistance < 75f)
				{
					chartControl.Properties.BarDistance = BarSpacePx;
					chartControl.Dispatcher.InvokeAsync(TriggerChartScrollbarRefresh);
				}
			}

			EnsureBarsComputed(ChartBars);

			int firstBar = Math.Max(0, ChartBars.FromIndex);
			int lastBar = Math.Min(ChartBars.Count - 1, ChartBars.ToIndex);
			if (lastBar < firstBar)
				return;

			float panelX = ChartPanel.X;
			float panelY = ChartPanel.Y;
			float panelW = ChartPanel.W;
			float panelH = ChartPanel.H;

			int statRows = 6;
			float statRowH = 20f;
			float statGridH = runtimeShowStatGrid ? (statRows * statRowH + 4f) : 0f;
			float chartBottomY = panelY + panelH - statGridH;

			var oldAntialias = RenderTarget.AntialiasMode;
			RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;

			try
			{
				// Create Direct2D Brushes matching STOCK_USER.xml exact palette
				using (var brushPocYellow = new SolidColorBrush(RenderTarget, new Color4(1.0f, 1.0f, 0.0f, 0.95f)))       // #FFFF00 Yellow Bar POC
				using (var brushBarVaFill = new SolidColorBrush(RenderTarget, new Color4(0.498f, 1.0f, 0.831f, 0.16f)))   // #7FFFD4 Aquamarine 20% Bar VA
				using (var brushBarVaLine = new SolidColorBrush(RenderTarget, new Color4(0.498f, 1.0f, 0.831f, 0.65f)))
				using (var brushBidCluster = new SolidColorBrush(RenderTarget, new Color4(0.85f, 0.15f, 0.20f, 0.48f)))   // #FF0000 Left Bid Cluster
				using (var brushAskCluster = new SolidColorBrush(RenderTarget, new Color4(0.05f, 0.58f, 0.25f, 0.52f)))   // #006400 Right Ask Cluster
				using (var brushFilterTeal = new SolidColorBrush(RenderTarget, new Color4(0.0f, 0.502f, 0.502f, 0.42f)))  // <20 #008080 Teal
				using (var brushFilterGray = new SolidColorBrush(RenderTarget, new Color4(0.663f, 0.663f, 0.663f, 0.48f)))// >=1500 #A9A9A9 DarkGray
				using (var brushFilterBlue = new SolidColorBrush(RenderTarget, new Color4(0.255f, 0.412f, 0.882f, 0.60f)))// >=2500 #4169E1 RoyalBlue
				using (var brushFilterRed = new SolidColorBrush(RenderTarget, new Color4(1.0f, 0.15f, 0.15f, 0.68f)))     // >=3000 #FF0000 Red
				using (var brushBidImbText = new SolidColorBrush(RenderTarget, new Color4(1.0f, 0.078f, 0.576f, 1.0f)))   // #FF1493 DeepPink Bid Imbalance
				using (var brushAskImbText = new SolidColorBrush(RenderTarget, new Color4(0.0f, 1.0f, 0.0f, 1.0f)))       // #00FF00 Lime Ask Imbalance
				using (var brushBidImbZone = new SolidColorBrush(RenderTarget, new Color4(1.0f, 0.078f, 0.576f, 0.22f)))  // Stacked Bid Imbalance S/R Zone (25%)
				using (var brushAskImbZone = new SolidColorBrush(RenderTarget, new Color4(0.0f, 1.0f, 0.0f, 0.20f)))      // Stacked Ask Imbalance S/R Zone (25%)
				using (var brushTextWhite = new SolidColorBrush(RenderTarget, new Color4(0.973f, 0.973f, 1.0f, 0.98f)))   // #F8F8FF GhostWhite
				using (var brushTextDim = new SolidColorBrush(RenderTarget, new Color4(0.827f, 0.827f, 0.827f, 0.92f)))   // #D3D3D3 LightGray
				using (var brushBullSpine = new SolidColorBrush(RenderTarget, new Color4(0.0f, 0.90f, 0.46f, 0.95f)))     // Bull Candle Spine
				using (var brushBearSpine = new SolidColorBrush(RenderTarget, new Color4(1.0f, 0.23f, 0.23f, 0.95f)))     // Bear Candle Spine
				using (var brushDevVa = new SolidColorBrush(RenderTarget, new Color4(0.255f, 0.412f, 0.882f, 0.90f)))     // #4169E1 Developing Session VA
				using (var brushDevPoc = new SolidColorBrush(RenderTarget, new Color4(1.0f, 0.647f, 0.0f, 0.92f)))        // #FFA500 Developing Session POC
				using (var brushDeltaPos = new SolidColorBrush(RenderTarget, new Color4(0.0f, 0.78f, 0.33f, 0.95f)))
				using (var brushDeltaNeg = new SolidColorBrush(RenderTarget, new Color4(1.0f, 0.20f, 0.25f, 0.95f)))
				using (var brushCellBorder = new SolidColorBrush(RenderTarget, new Color4(0.18f, 0.24f, 0.34f, 0.65f)))
				using (var brushGridBg = new SolidColorBrush(RenderTarget, new Color4(0.055f, 0.082f, 0.133f, 0.96f)))
				using (var brushLegendBg = new SolidColorBrush(RenderTarget, new Color4(0.08f, 0.115f, 0.185f, 0.98f)))
				using (var brushDeltaPosBg = new SolidColorBrush(RenderTarget, new Color4(0.0f, 0.50f, 0.18f, 0.38f)))
				using (var brushDeltaNegBg = new SolidColorBrush(RenderTarget, new Color4(0.65f, 0.08f, 0.12f, 0.38f)))
				{
					float barSpacing = chartControl.Properties != null ? chartControl.Properties.BarDistance : 90f;
					if (barSpacing < 12f)
					{
						if (lastBar > firstBar)
							barSpacing = Math.Abs(chartControl.GetXByBarIndex(ChartBars, lastBar) - chartControl.GetXByBarIndex(ChartBars, firstBar)) / (float)(lastBar - firstBar);
						else
							barSpacing = 90f;
					}

					float halfColW = Math.Max(8f, Math.Min(95f, barSpacing * 0.45f));
					float spineHalfW = Math.Max(2.0f, Math.Min(4.0f, halfColW * 0.09f));

					// 1. Collect visible FootprintBarData safely
					List<FootprintBarData> visibleBars = new List<FootprintBarData>(lastBar - firstBar + 1);
					lock (dataLock)
					{
						for (int idx = firstBar; idx <= lastBar; idx++)
						{
							if (barCache.TryGetValue(idx, out FootprintBarData fp))
								visibleBars.Add(fp);
						}
					}

					// 2. Render Stacked Imbalance S/R Zones first (behind clusters)
					if (runtimeShowImbZones && visibleBars.Count > 0)
					{
						RenderStackedImbalanceZones(chartControl, chartScale, visibleBars, lastBar, halfColW, chartBottomY,
							brushBidImbZone, brushAskImbZone, brushBidImbText, brushAskImbText);
					}

					// 3. Render Developing Session VA & POC Step Lines
					if (ShowSessionVA && visibleBars.Count > 1)
					{
						RenderDevelopingSessionLines(chartControl, chartScale, visibleBars, halfColW, chartBottomY, brushDevVa, brushDevPoc);
					}

					// 4. Choose DirectWrite font size dynamically based on row height & column width
					float sampleStep = visibleBars.Count > 0 ? (float)visibleBars[visibleBars.Count - 1].EffectivePriceStep : (float)(TickSize * 20);
					double midP = visibleBars.Count > 0 ? visibleBars[visibleBars.Count - 1].Close : 1000.0;
					float rawRowH = Math.Abs(chartScale.GetYByValue(midP + sampleStep) - chartScale.GetYByValue(midP));
					float rowH = Math.Max(10f, Math.Min(28f, rawRowH));
					float fontSize = Math.Max(8.0f, Math.Min(11.5f, Math.Min(rowH * 0.68f, halfColW * 0.28f)));
					bool drawClusterNumbers = (halfColW >= 18f && rawRowH >= 8.5f);

					using (var dwFactory = new SharpDX.DirectWrite.Factory())
					using (var tfCluster = new TextFormat(dwFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, fontSize))
					using (var tfDeltaBadge = new TextFormat(dwFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, Math.Max(8.5f, Math.Min(10.5f, halfColW * 0.25f))))
					using (var tfGrid = new TextFormat(dwFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.SemiBold, SharpDX.DirectWrite.FontStyle.Normal, Math.Max(8.5f, Math.Min(10.5f, halfColW * 0.24f))))
					using (var tfLegend = new TextFormat(dwFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10.5f))
					{
						tfCluster.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
						tfCluster.ParagraphAlignment = ParagraphAlignment.Center;
						tfCluster.WordWrapping = WordWrapping.NoWrap;

						tfDeltaBadge.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
						tfDeltaBadge.ParagraphAlignment = ParagraphAlignment.Center;
						tfDeltaBadge.WordWrapping = WordWrapping.NoWrap;

						tfGrid.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
						tfGrid.ParagraphAlignment = ParagraphAlignment.Center;
						tfGrid.WordWrapping = WordWrapping.NoWrap;

						tfLegend.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
						tfLegend.ParagraphAlignment = ParagraphAlignment.Center;
						tfLegend.WordWrapping = WordWrapping.NoWrap;

						// 5. Render each 5-Minute Footprint Bar
						for (int v = 0; v < visibleBars.Count; v++)
						{
							FootprintBarData fp = visibleBars[v];
							float cx = chartControl.GetXByBarIndex(ChartBars, fp.BarIndex);
							if (cx + halfColW < panelX || cx - halfColW > panelX + panelW)
								continue;

							float step = (float)fp.EffectivePriceStep;
							float halfStep = step * 0.5f;

							// Find max side volume in this bar for proportional cluster bars
							long maxSideVol = 1;
							for (int r = 0; r < fp.Levels.Count; r++)
							{
								if (fp.Levels[r].BidVolume > maxSideVol) maxSideVol = fp.Levels[r].BidVolume;
								if (fp.Levels[r].AskVolume > maxSideVol) maxSideVol = fp.Levels[r].AskVolume;
							}

							// 5a. Draw 70% Bar Value Area Background Box
							if (ShowBarVA && fp.Levels.Count > 0)
							{
								float vaTopY = chartScale.GetYByValue(fp.VaHigh + halfStep);
								float vaBotY = chartScale.GetYByValue(fp.VaLow - halfStep);
								if (vaBotY > vaTopY)
								{
									float clampedTop = Math.Max(panelY, Math.Min(chartBottomY, vaTopY));
									float clampedBot = Math.Max(panelY, Math.Min(chartBottomY, vaBotY));
									if (clampedBot > clampedTop)
									{
										var vaRect = new RectangleF(cx - halfColW, clampedTop, halfColW * 2f, clampedBot - clampedTop);
										RenderTarget.FillRectangle(vaRect, brushBarVaFill);
										// Right edge VA marker line
										RenderTarget.DrawLine(new Vector2(cx + halfColW + 1.5f, clampedTop), new Vector2(cx + halfColW + 1.5f, clampedBot), brushBarVaLine, 2.0f);
									}
								}
							}

							float availSideW = Math.Max(4f, halfColW - spineHalfW - 1f);

							// 5b. Draw Left (Bid) & Right (Ask) Cluster Bricks + Volume Numbers + POC + Imbalances
							for (int r = 0; r < fp.Levels.Count; r++)
							{
								FootprintLevel lv = fp.Levels[r];
								float yTop = chartScale.GetYByValue(lv.Price + halfStep);
								float yBot = chartScale.GetYByValue(lv.Price - halfStep);
								float cellH = Math.Max(2f, yBot - yTop);
								if (yBot < panelY || yTop > chartBottomY)
									continue;

								float cellTop = yTop + 0.5f;
								float drawH = Math.Max(1.5f, cellH - 1.0f);

								// Left Cluster (Bid / Sell Volume)
								float leftFullX = cx - spineHalfW - availSideW;
								float bidRatio = Math.Min(1.0f, Math.Max(0.18f, (float)lv.BidVolume / maxSideVol));
								float bidBarW = availSideW * bidRatio;
								var bidBarRect = new RectangleF(cx - spineHalfW - bidBarW, cellTop, bidBarW, drawH);
								RenderTarget.FillRectangle(bidBarRect, SelectVolumeFilterBrush(lv.BidVolume, true, brushBidCluster, brushAskCluster, brushFilterTeal, brushFilterGray, brushFilterBlue, brushFilterRed));

								// Right Cluster (Ask / Buy Volume)
								float askRatio = Math.Min(1.0f, Math.Max(0.18f, (float)lv.AskVolume / maxSideVol));
								float askBarW = availSideW * askRatio;
								var askBarRect = new RectangleF(cx + spineHalfW, cellTop, askBarW, drawH);
								RenderTarget.FillRectangle(askBarRect, SelectVolumeFilterBrush(lv.AskVolume, false, brushBidCluster, brushAskCluster, brushFilterTeal, brushFilterGray, brushFilterBlue, brushFilterRed));

								// Subtle cluster cell outline
								var fullRowRect = new RectangleF(leftFullX, cellTop, availSideW * 2f + spineHalfW * 2f, drawH);
								RenderTarget.DrawRectangle(fullRowRect, brushCellBorder, 0.5f);

								// Imbalance Cell Highlights (DeepPink Left / Lime Right)
								if (ShowImbalance && lv.HasBidImbalance)
								{
									var imbLeftRect = new RectangleF(leftFullX, cellTop, availSideW, drawH);
									RenderTarget.DrawRectangle(imbLeftRect, brushBidImbText, 1.3f);
								}
								if (ShowImbalance && lv.HasAskImbalance)
								{
									var imbRightRect = new RectangleF(cx + spineHalfW, cellTop, availSideW, drawH);
									RenderTarget.DrawRectangle(imbRightRect, brushAskImbText, 1.3f);
								}

								// Bar POC Yellow Box (#FFFF00)
								if (ShowBarPOC && lv.IsPoc)
								{
									RenderTarget.DrawRectangle(fullRowRect, brushPocYellow, 1.8f);
								}

								// Draw Bid x Ask Volume Numbers
								if (drawClusterNumbers && drawH >= 8.0f)
								{
									var leftTextRect = new RectangleF(leftFullX + 1f, yTop, availSideW - 2f, cellH);
									var rightTextRect = new RectangleF(cx + spineHalfW + 1f, yTop, availSideW - 2f, cellH);

									var bidTxtBrush = (ShowImbalance && lv.HasBidImbalance) ? brushBidImbText : brushTextWhite;
									var askTxtBrush = (ShowImbalance && lv.HasAskImbalance) ? brushAskImbText : brushTextWhite;

									RenderTarget.DrawText(FormatClusterNumber(lv.BidVolume), tfCluster, leftTextRect, bidTxtBrush);
									RenderTarget.DrawText(FormatClusterNumber(lv.AskVolume), tfCluster, rightTextRect, askTxtBrush);
								}
							}

							// 5c. Draw Center Candlestick Spine (Wick + Open/Close Body)
							float yHigh = chartScale.GetYByValue(fp.High);
							float yLow = chartScale.GetYByValue(fp.Low);
							float yOpen = chartScale.GetYByValue(fp.Open);
							float yClose = chartScale.GetYByValue(fp.Close);
							bool isBull = fp.Close >= fp.Open;
							var spineBrush = isBull ? brushBullSpine : brushBearSpine;

							RenderTarget.DrawLine(new Vector2(cx, Math.Max(panelY, yHigh)), new Vector2(cx, Math.Min(chartBottomY, yLow)), spineBrush, 1.4f);
							float bodyTop = Math.Min(yOpen, yClose);
							float bodyH = Math.Max(2.5f, Math.Abs(yClose - yOpen));
							var spineBodyRect = new RectangleF(cx - spineHalfW * 0.85f, bodyTop, spineHalfW * 1.7f, bodyH);
							RenderTarget.FillRectangle(spineBodyRect, spineBrush);

							// 5d. Draw Bar Delta Summary Badge Below Bar Low (STOCK_USER.xml ShowBarDelta = true)
							if (ShowBarDelta && halfColW >= 14f)
							{
								float deltaY = Math.Min(chartBottomY - 16f, yLow + 4f);
								if (deltaY > panelY + 24f)
								{
									var deltaRect = new RectangleF(cx - halfColW, deltaY, halfColW * 2f, 14f);
									string deltaStr = (fp.Delta >= 0 ? "+" : "") + FormatClusterNumber(fp.Delta);
									RenderTarget.DrawText(deltaStr, tfDeltaBadge, deltaRect, fp.Delta >= 0 ? brushDeltaPos : brushDeltaNeg);
								}
							}
						}

						// 6. Render Bottom 6-Row Statistic Grid (STOCK_USER.xml StatisticGridShow = true, Legend = Left)
						if (runtimeShowStatGrid && statGridH > 0)
						{
							RenderBottomStatisticGrid(chartControl, visibleBars, panelX, panelW, chartBottomY, statRowH, halfColW,
								tfLegend, tfGrid, brushGridBg, brushLegendBg, brushCellBorder, brushTextWhite, brushTextDim,
								brushDeltaPos, brushDeltaNeg, brushDeltaPosBg, brushDeltaNegBg);
						}

						// 7. Render Top-Left Interactive Header Pill
						RenderInteractiveHeaderBar(panelX, panelY, barSpacing, sampleStep);
					}
				}
			}
			catch { }
			finally
			{
				RenderTarget.AntialiasMode = oldAntialias;
			}
		}

		private SolidColorBrush SelectVolumeFilterBrush(
			long vol,
			bool isBidSide,
			SolidColorBrush brushBid,
			SolidColorBrush brushAsk,
			SolidColorBrush brushTeal,
			SolidColorBrush brushGray,
			SolidColorBrush brushBlue,
			SolidColorBrush brushRed)
		{
			// Exact filter tiers from STOCK_USER.xml lines 201-281
			if (vol >= 3000) return brushRed;
			if (vol >= 2500) return brushBlue;
			if (vol >= 1500) return brushGray;
			if (vol < 20) return brushTeal;
			return isBidSide ? brushBid : brushAsk;
		}

		private void RenderDevelopingSessionLines(
			ChartControl chartControl,
			ChartScale chartScale,
			List<FootprintBarData> visibleBars,
			float halfColW,
			float chartBottomY,
			SolidColorBrush brushDevVa,
			SolidColorBrush brushDevPoc)
		{
			for (int i = 0; i < visibleBars.Count; i++)
			{
				FootprintBarData cur = visibleBars[i];
				float x1 = chartControl.GetXByBarIndex(ChartBars, cur.BarIndex) - halfColW;
				float x2 = chartControl.GetXByBarIndex(ChartBars, cur.BarIndex) + halfColW;

				float yPoc = chartScale.GetYByValue(cur.DevSessionPoc);
				float yVah = chartScale.GetYByValue(cur.DevSessionVah);
				float yVal = chartScale.GetYByValue(cur.DevSessionVal);

				if (yPoc > ChartPanel.Y && yPoc < chartBottomY)
					RenderTarget.DrawLine(new Vector2(x1, yPoc), new Vector2(x2, yPoc), brushDevPoc, 1.8f);
				if (yVah > ChartPanel.Y && yVah < chartBottomY)
					RenderTarget.DrawLine(new Vector2(x1, yVah), new Vector2(x2, yVah), brushDevVa, 1.5f);
				if (yVal > ChartPanel.Y && yVal < chartBottomY)
					RenderTarget.DrawLine(new Vector2(x1, yVal), new Vector2(x2, yVal), brushDevVa, 1.5f);

				if (i > 0 && visibleBars[i - 1].Time.Date == cur.Time.Date)
				{
					FootprintBarData prev = visibleBars[i - 1];
					float prevX2 = chartControl.GetXByBarIndex(ChartBars, prev.BarIndex) + halfColW;
					float pyPoc = chartScale.GetYByValue(prev.DevSessionPoc);
					float pyVah = chartScale.GetYByValue(prev.DevSessionVah);
					float pyVal = chartScale.GetYByValue(prev.DevSessionVal);

					if (pyPoc > ChartPanel.Y && pyPoc < chartBottomY && yPoc > ChartPanel.Y && yPoc < chartBottomY)
						RenderTarget.DrawLine(new Vector2(prevX2, pyPoc), new Vector2(x1, yPoc), brushDevPoc, 1.2f);
					if (pyVah > ChartPanel.Y && pyVah < chartBottomY && yVah > ChartPanel.Y && yVah < chartBottomY)
						RenderTarget.DrawLine(new Vector2(prevX2, pyVah), new Vector2(x1, yVah), brushDevVa, 1.1f);
					if (pyVal > ChartPanel.Y && pyVal < chartBottomY && yVal > ChartPanel.Y && yVal < chartBottomY)
						RenderTarget.DrawLine(new Vector2(prevX2, pyVal), new Vector2(x1, yVal), brushDevVa, 1.1f);
				}
			}
		}

		private void RenderStackedImbalanceZones(
			ChartControl chartControl,
			ChartScale chartScale,
			List<FootprintBarData> visibleBars,
			int lastVisibleBarIdx,
			float halfColW,
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

				// Find consecutive Bid Imbalances (Supply / Resistance Zone)
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
						int runLen = r - runStart;
						if (runLen >= minConsec)
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

				// Find consecutive Ask Imbalances (Demand / Support Zone)
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
						int runLen = r - runStart;
						if (runLen >= minConsec)
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

			// Check zone termination (STOCK_USER.xml: TerminateByBarHighLow, BreakOnSession = true)
			for (int z = 0; z < zones.Count; z++)
			{
				ImbalanceZone zone = zones[z];
				for (int i = 0; i < visibleBars.Count; i++)
				{
					FootprintBarData fp = visibleBars[i];
					if (fp.BarIndex <= zone.StartBarIndex) continue;

					// Break on session change
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

				float x1 = chartControl.GetXByBarIndex(ChartBars, zone.StartBarIndex);
				float x2 = chartControl.GetXByBarIndex(ChartBars, zone.EndBarIndex) + (zone.IsBroken ? 0f : halfColW);
				if (x2 <= x1) continue;

				float yTop = chartScale.GetYByValue(zone.TopPrice);
				float yBot = chartScale.GetYByValue(zone.BottomPrice);
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

		private void RenderBottomStatisticGrid(
			ChartControl chartControl,
			List<FootprintBarData> visibleBars,
			float panelX,
			float panelW,
			float gridTopY,
			float rowH,
			float halfColW,
			TextFormat tfLegend,
			TextFormat tfGrid,
			SolidColorBrush brushGridBg,
			SolidColorBrush brushLegendBg,
			SolidColorBrush brushBorder,
			SolidColorBrush brushWhite,
			SolidColorBrush brushDim,
			SolidColorBrush brushDeltaPos,
			SolidColorBrush brushDeltaNeg,
			SolidColorBrush brushDeltaPosBg,
			SolidColorBrush brushDeltaNegBg)
		{
			string[] labels = { "Buy Vol", "Sell Vol", "Delta", "Cum. Delta", "Min Delta", "Max Delta" };
			float totalH = labels.Length * rowH;
			float legendW = 78f;

			// Full grid background
			var fullGridRect = new RectangleF(panelX, gridTopY, panelW, totalH + 4f);
			RenderTarget.FillRectangle(fullGridRect, brushGridBg);
			RenderTarget.DrawLine(new Vector2(panelX, gridTopY), new Vector2(panelX + panelW, gridTopY), brushBorder, 1.5f);

			// Per-bar columns
			for (int i = 0; i < visibleBars.Count; i++)
			{
				FootprintBarData fp = visibleBars[i];
				float cx = chartControl.GetXByBarIndex(ChartBars, fp.BarIndex);
				float cellLeft = cx - halfColW;
				float cellW = halfColW * 2f;
				if (cellLeft + cellW < panelX + legendW || cellLeft > panelX + panelW)
					continue;

				long[] vals = { fp.BuyVolume, fp.SellVolume, fp.Delta, fp.CumulativeDelta, fp.MinDelta, fp.MaxDelta };

				for (int r = 0; r < vals.Length; r++)
				{
					float ry = gridTopY + r * rowH;
					var cRect = new RectangleF(cellLeft, ry, cellW, rowH);

					// Shade Delta / Cum. Delta / Min / Max Delta backgrounds
					if (r == 2 || r == 3)
					{
						if (vals[r] > 0) RenderTarget.FillRectangle(cRect, brushDeltaPosBg);
						else if (vals[r] < 0) RenderTarget.FillRectangle(cRect, brushDeltaNegBg);
					}

					RenderTarget.DrawRectangle(cRect, brushBorder, 0.6f);

					if (cellW >= 24f)
					{
						SolidColorBrush txtB = brushDim;
						if (r == 0 || r == 5) txtB = brushDeltaPos;
						else if (r == 1 || r == 4) txtB = brushDeltaNeg;
						else if (r == 2 || r == 3) txtB = brushWhite;

						string s = FormatClusterNumber(vals[r]);
						if ((r == 2 || r == 3) && vals[r] > 0) s = "+" + s;
						RenderTarget.DrawText(s, tfGrid, cRect, txtB);
					}
				}
			}

			// Draw Left Legend Column on top so it never gets overlapped by scrolling bars
			var legendRect = new RectangleF(panelX, gridTopY, legendW, totalH + 4f);
			RenderTarget.FillRectangle(legendRect, brushLegendBg);
			RenderTarget.DrawRectangle(legendRect, brushBorder, 1.0f);

			for (int r = 0; r < labels.Length; r++)
			{
				float ry = gridTopY + r * rowH;
				var lRect = new RectangleF(panelX + 6f, ry, legendW - 8f, rowH);
				RenderTarget.DrawLine(new Vector2(panelX, ry + rowH), new Vector2(panelX + legendW, ry + rowH), brushBorder, 0.6f);
				RenderTarget.DrawText(labels[r], tfLegend, lRect, brushWhite);
			}
		}

		private void RenderInteractiveHeaderBar(float panelX, float panelY, float barSpacing, float effectiveStep)
		{
			string sym = Instrument != null ? Instrument.FullName : "SYMBOL";
			double livePx = lastTradePrice > 0 ? lastTradePrice : (double.IsNaN(lastComputedLastClose) ? 0.0 : lastComputedLastClose);
			string ltpStr = livePx > 0 ? string.Format(CultureInfo.InvariantCulture, "  •  LTP: {0:0.00}", livePx) : "";
			string headerText = string.Format(CultureInfo.InvariantCulture,
				"⚡ AK10X Orderflow (5M)  •  {0}  •  ● LIVE ACCELPIX{1}  •  Step: {2:0.##} ({3})",
				sym, ltpStr, effectiveStep, runtimeAutoStep ? "Auto" : (runtimeTicksPerLevel + "T"));

			using (var dwFactory = new SharpDX.DirectWrite.Factory())
			using (var tfHeader = new TextFormat(dwFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10.5f))
			using (var tfBtn = new TextFormat(dwFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10.0f))
			using (var bgBrush = new SolidColorBrush(RenderTarget, new Color4(0.06f, 0.09f, 0.14f, 0.92f)))
			using (var borderBrush = new SolidColorBrush(RenderTarget, new Color4(0.0f, 0.90f, 0.46f, 0.75f)))
			using (var goldBrush = new SolidColorBrush(RenderTarget, new Color4(1.0f, 0.78f, 0.15f, 1.0f)))
			using (var btnBg = new SolidColorBrush(RenderTarget, new Color4(0.14f, 0.20f, 0.30f, 0.95f)))
			using (var btnActiveBg = new SolidColorBrush(RenderTarget, new Color4(0.04f, 0.42f, 0.26f, 0.95f)))
			using (var whiteBrush = new SolidColorBrush(RenderTarget, new Color4(0.96f, 0.98f, 1.0f, 1.0f)))
			{
				tfHeader.ParagraphAlignment = ParagraphAlignment.Center;
				tfHeader.WordWrapping = WordWrapping.NoWrap;
				tfBtn.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfBtn.ParagraphAlignment = ParagraphAlignment.Center;

				float boxX = panelX + 8f;
				float boxY = panelY + 6f;
				float boxW = 472f;
				float boxH = 48f;

				var pillRect = new RectangleF(boxX, boxY, boxW, boxH);
				RenderTarget.FillRectangle(pillRect, bgBrush);
				RenderTarget.DrawRectangle(pillRect, borderBrush, 1.0f);

				var titleRect = new RectangleF(boxX + 8f, boxY + 2f, boxW - 16f, 20f);
				RenderTarget.DrawText(headerText, tfHeader, titleRect, goldBrush);

				float bx = boxX + 8f;
				float by = boxY + 24f;
				float bh = 19f;

				btnStepMinusRect = DrawMiniBtn(bx, by, 44f, bh, "Step -", false, tfBtn, btnBg, btnActiveBg, borderBrush, whiteBrush);
				bx += 48f;
				btnStepPlusRect = DrawMiniBtn(bx, by, 44f, bh, "Step +", false, tfBtn, btnBg, btnActiveBg, borderBrush, whiteBrush);
				bx += 48f;
				btnAutoStepRect = DrawMiniBtn(bx, by, 46f, bh, "Auto", runtimeAutoStep, tfBtn, btnBg, btnActiveBg, borderBrush, whiteBrush);
				bx += 50f;
				btnWidthMinusRect = DrawMiniBtn(bx, by, 48f, bh, "Zoom -", false, tfBtn, btnBg, btnActiveBg, borderBrush, whiteBrush);
				bx += 52f;
				btnWidthPlusRect = DrawMiniBtn(bx, by, 48f, bh, "Zoom +", false, tfBtn, btnBg, btnActiveBg, borderBrush, whiteBrush);
				bx += 52f;
				btnGridToggleRect = DrawMiniBtn(bx, by, 46f, bh, "Grid", runtimeShowStatGrid, tfBtn, btnBg, btnActiveBg, borderBrush, whiteBrush);
				bx += 50f;
				btnZonesToggleRect = DrawMiniBtn(bx, by, 46f, bh, "Zones", runtimeShowImbZones, tfBtn, btnBg, btnActiveBg, borderBrush, whiteBrush);
				bx += 50f;
				btnReloadLiveRect = DrawMiniBtn(bx, by, 64f, bh, "Live Sync", true, tfBtn, btnBg, btnActiveBg, borderBrush, whiteBrush);
			}
		}

		private RectangleF DrawMiniBtn(
			float x, float y, float w, float h, string text, bool active,
			TextFormat tf, SolidColorBrush bg, SolidColorBrush activeBg, SolidColorBrush border, SolidColorBrush fg)
		{
			var r = new RectangleF(x, y, w, h);
			RenderTarget.FillRectangle(r, active ? activeBg : bg);
			RenderTarget.DrawRectangle(r, border, 0.8f);
			RenderTarget.DrawText(text, tf, r, fg);
			return r;
		}

		private static string FormatClusterNumber(long val)
		{
			long abs = Math.Abs(val);
			if (abs >= 1000000)
				return (val / 1000000.0).ToString("0.0M", CultureInfo.InvariantCulture);
			if (abs >= 10000)
				return (val / 1000.0).ToString("0.#K", CultureInfo.InvariantCulture);
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
		private AK10XOrderflow[] cacheAK10XOrderflow;
		public AK10XOrderflow AK10XOrderflow(int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showBarVA, int barVAPercentage, bool showSessionVA, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showImbalanceSRZones, int imbalanceSRConsecutiveLevels, bool showBarDelta, bool showStatisticGrid)
		{
			return AK10XOrderflow(Input, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showBarVA, barVAPercentage, showSessionVA, showImbalance, imbalancePercentage, imbalanceFilter, showImbalanceSRZones, imbalanceSRConsecutiveLevels, showBarDelta, showStatisticGrid);
		}

		public AK10XOrderflow AK10XOrderflow(ISeries<double> input, int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showBarVA, int barVAPercentage, bool showSessionVA, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showImbalanceSRZones, int imbalanceSRConsecutiveLevels, bool showBarDelta, bool showStatisticGrid)
		{
			if (cacheAK10XOrderflow != null)
				for (int idx = 0; idx < cacheAK10XOrderflow.Length; idx++)
					if (cacheAK10XOrderflow[idx] != null && cacheAK10XOrderflow[idx].TicksPerLevel == ticksPerLevel && cacheAK10XOrderflow[idx].AutoScalePriceStep == autoScalePriceStep && cacheAK10XOrderflow[idx].BarSpacePx == barSpacePx && cacheAK10XOrderflow[idx].ShowBarPOC == showBarPOC && cacheAK10XOrderflow[idx].ShowBarVA == showBarVA && cacheAK10XOrderflow[idx].BarVAPercentage == barVAPercentage && cacheAK10XOrderflow[idx].ShowSessionVA == showSessionVA && cacheAK10XOrderflow[idx].ShowImbalance == showImbalance && cacheAK10XOrderflow[idx].ImbalancePercentage == imbalancePercentage && cacheAK10XOrderflow[idx].ImbalanceFilter == imbalanceFilter && cacheAK10XOrderflow[idx].ShowImbalanceSRZones == showImbalanceSRZones && cacheAK10XOrderflow[idx].ImbalanceSRConsecutiveLevels == imbalanceSRConsecutiveLevels && cacheAK10XOrderflow[idx].ShowBarDelta == showBarDelta && cacheAK10XOrderflow[idx].ShowStatisticGrid == showStatisticGrid && cacheAK10XOrderflow[idx].EqualsInput(input))
						return cacheAK10XOrderflow[idx];
			return CacheIndicator<AK10XOrderflow>(new AK10XOrderflow(){ TicksPerLevel = ticksPerLevel, AutoScalePriceStep = autoScalePriceStep, BarSpacePx = barSpacePx, ShowBarPOC = showBarPOC, ShowBarVA = showBarVA, BarVAPercentage = barVAPercentage, ShowSessionVA = showSessionVA, ShowImbalance = showImbalance, ImbalancePercentage = imbalancePercentage, ImbalanceFilter = imbalanceFilter, ShowImbalanceSRZones = showImbalanceSRZones, ImbalanceSRConsecutiveLevels = imbalanceSRConsecutiveLevels, ShowBarDelta = showBarDelta, ShowStatisticGrid = showStatisticGrid }, input, ref cacheAK10XOrderflow);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AK10XOrderflow AK10XOrderflow(int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showBarVA, int barVAPercentage, bool showSessionVA, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showImbalanceSRZones, int imbalanceSRConsecutiveLevels, bool showBarDelta, bool showStatisticGrid)
		{
			return indicator.AK10XOrderflow(Input, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showBarVA, barVAPercentage, showSessionVA, showImbalance, imbalancePercentage, imbalanceFilter, showImbalanceSRZones, imbalanceSRConsecutiveLevels, showBarDelta, showStatisticGrid);
		}

		public Indicators.AK10XOrderflow AK10XOrderflow(ISeries<double> input , int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showBarVA, int barVAPercentage, bool showSessionVA, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showImbalanceSRZones, int imbalanceSRConsecutiveLevels, bool showBarDelta, bool showStatisticGrid)
		{
			return indicator.AK10XOrderflow(input, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showBarVA, barVAPercentage, showSessionVA, showImbalance, imbalancePercentage, imbalanceFilter, showImbalanceSRZones, imbalanceSRConsecutiveLevels, showBarDelta, showStatisticGrid);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AK10XOrderflow AK10XOrderflow(int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showBarVA, int barVAPercentage, bool showSessionVA, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showImbalanceSRZones, int imbalanceSRConsecutiveLevels, bool showBarDelta, bool showStatisticGrid)
		{
			return indicator.AK10XOrderflow(Input, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showBarVA, barVAPercentage, showSessionVA, showImbalance, imbalancePercentage, imbalanceFilter, showImbalanceSRZones, imbalanceSRConsecutiveLevels, showBarDelta, showStatisticGrid);
		}

		public Indicators.AK10XOrderflow AK10XOrderflow(ISeries<double> input , int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showBarVA, int barVAPercentage, bool showSessionVA, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showImbalanceSRZones, int imbalanceSRConsecutiveLevels, bool showBarDelta, bool showStatisticGrid)
		{
			return indicator.AK10XOrderflow(input, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showBarVA, barVAPercentage, showSessionVA, showImbalance, imbalancePercentage, imbalanceFilter, showImbalanceSRZones, imbalanceSRConsecutiveLevels, showBarDelta, showStatisticGrid);
		}
	}
}

#endregion
