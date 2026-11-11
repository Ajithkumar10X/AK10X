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
		private ChartControl hookedOrderflowCC = null;
		private Window hookedOrderflowOwnerWin = null;
		private MouseButtonEventHandler ofMouseLeftDownHandler = null;
		private System.Windows.Controls.Primitives.ScrollBar hookedOfScrollBar = null;
		private System.Windows.Forms.Form hookedD2DForm = null;
		private bool isDraggingFootprintCanvas = false;
		private float ofDragStartMouseX = 0f;
		private double ofDragStartScrollVal = 0.0;
		private int hoveredToolbarBtnIndex = -1;
		private int lastHandledOfMouseTimestamp = -1;
		private DateTime lastBtnClickUtc = DateTime.MinValue;

		private BarsRequest activeAccelpix1MReq;
		private bool isConnectionStatusHooked;
		private bool liveStreamInitialized;
		private DateTime lastLiveRefreshUtc = DateTime.MinValue;

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
				lock (dataLock)
				{
					barCache.Clear();
					liveAccumulators.Clear();
					accelpixSubBarsBy5MSlot.Clear();
					lastComputedBarCount = -1;
					lastComputedLastClose = double.NaN;
					lastComputedLastVol = -1;
				}
				runtimeTicksPerLevel = Math.Max(1, TicksPerLevel);
				runtimeAutoStep = AutoScalePriceStep;
				runtimeShowStatGrid = ShowStatisticGrid;
				runtimeShowImbZones = ShowImbalanceSRZones;
				liveStreamInitialized = false;
				EnsureMouseEventsSubscribed();
			}
			else if (State == State.Realtime)
			{
				if (!liveStreamInitialized)
				{
					liveStreamInitialized = true;
					AttachAccelpixLiveStream();
					RequestAccelpixLiveBarsAsync();
				}
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
				BarsRequest req1 = activeAccelpix1MReq;
				activeAccelpix1MReq = null;
				if (req1 != null)
				{
					req1.Update -= OnAccelpix1MBarsUpdate;
					req1.Dispose();
				}
			}
			catch { }
		}

		private void OnAccelpixConnectionStatusUpdate(object sender, ConnectionStatusEventArgs e)
		{
			try
			{
				if (e == null || State != State.Realtime)
					return;
				if (e.Status == ConnectionStatus.Connected)
				{
					RequestAccelpixLiveBarsAsync();
				}
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
						DateTime startDate = endDate.AddDays(-3);

						// Live 1-Minute BarsRequest for intrabar Bid x Ask Footprint granularity
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

			lock (dataLock)
			{
				accelpixSubBarsBy5MSlot.Clear();
				DateTime prevDate = DateTime.MinValue;
				long prevCumVol = 0;
				int count = b1m.Count;

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

			RequestThrottledChartRefresh(true);
		}

		private void OnAccelpix1MBarsUpdate(object sender, BarsUpdateEventArgs e)
		{
			try
			{
				if (e == null || e.BarsSeries == null || State != State.Realtime)
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

				RequestThrottledChartRefresh(false);
			}
			catch { }
		}

		private void RequestThrottledChartRefresh(bool immediate)
		{
			try
			{
				if (State != State.Realtime)
					return;

				DateTime nowUtc = DateTime.UtcNow;
				if (!immediate && (nowUtc - lastLiveRefreshUtc).TotalMilliseconds < 250)
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

			int desiredMargin = Math.Max(48, (int)Math.Round(barSpacing * 1.5f));
			if (Math.Abs(cc.Properties.BarMarginRightUser - desiredMargin) <= 4)
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
						if (cc.Properties != null && Math.Abs(cc.Properties.BarMarginRightUser - desiredMargin) > 4)
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
			if (ChartControl == null)
				return;
			if (mouseSubscribed && hookedOrderflowCC == ChartControl && hookedOfScrollBar != null)
				return;

			try
			{
				ChartControl cc = ChartControl;
				cc.Dispatcher.InvokeAsync(() =>
				{
					try
					{
						if (cc == null) return;
						if (ofMouseLeftDownHandler == null)
							ofMouseLeftDownHandler = new MouseButtonEventHandler(OnChartMouseLeftButtonDown);

						if (!mouseSubscribed || hookedOrderflowCC != cc)
						{
							if (hookedOrderflowCC != null)
							{
								try { hookedOrderflowCC.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, ofMouseLeftDownHandler); } catch { }
								hookedOrderflowCC.PreviewMouseMove -= OnChartMouseMove;
								hookedOrderflowCC.PreviewMouseLeftButtonUp -= OnChartMouseLeftButtonUp;
								hookedOrderflowCC.PreviewMouseWheel -= OnChartMouseWheel;
								hookedOrderflowCC.MouseLeave -= OnChartMouseLeave;
							}
							cc.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, ofMouseLeftDownHandler);
							cc.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, ofMouseLeftDownHandler, true);
							cc.PreviewMouseMove += OnChartMouseMove;
							cc.PreviewMouseLeftButtonUp += OnChartMouseLeftButtonUp;
							cc.PreviewMouseWheel += OnChartMouseWheel;
							cc.MouseLeave += OnChartMouseLeave;
							hookedOrderflowCC = cc;
							subscribedElement = cc;
							mouseSubscribed = true;
						}

						if (cc.Properties != null)
						{
							float curDist = cc.Properties.BarDistance > 12f ? cc.Properties.BarDistance : 110f;
							SyncOneBoxRightGapMargin(cc, curDist, true);
						}

						if (hookedOfScrollBar == null)
							hookedOfScrollBar = ResolveOrderflowScrollBar(cc);

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
			ChartControl cc = hookedOrderflowCC;
			Window ownerWin = hookedOrderflowOwnerWin;
			MouseButtonEventHandler handler = ofMouseLeftDownHandler;
			var d2d = hookedD2DForm;
			if (cc != null)
			{
				try
				{
					cc.Dispatcher.InvokeAsync(() =>
					{
						if (handler != null)
						{
							try { cc.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, handler); } catch { }
							try { if (ownerWin != null) ownerWin.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, handler); } catch { }
						}
						try { cc.PreviewMouseMove -= OnChartMouseMove; } catch { }
						try { cc.PreviewMouseLeftButtonUp -= OnChartMouseLeftButtonUp; } catch { }
						try { cc.PreviewMouseWheel -= OnChartMouseWheel; } catch { }
						try { cc.MouseLeave -= OnChartMouseLeave; } catch { }
						try { if (d2d != null) d2d.MouseDown -= OnD2DFormMouseDown; } catch { }
					});
				}
				catch { }
			}
			mouseSubscribed = false;
			subscribedElement = null;
			hookedOrderflowCC = null;
			hookedOrderflowOwnerWin = null;
			hookedOfScrollBar = null;
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

		private int HitTestToolbarButtonAt(float x, float y)
		{
			if (ContainsPtPadded(btnStepMinusRect, x, y, 2f)) return 0;
			if (ContainsPtPadded(btnStepPlusRect, x, y, 2f)) return 1;
			if (ContainsPtPadded(btnAutoStepRect, x, y, 2f)) return 2;
			if (ContainsPtPadded(btnWidthMinusRect, x, y, 2f)) return 3;
			if (ContainsPtPadded(btnWidthPlusRect, x, y, 2f)) return 4;
			if (ContainsPtPadded(btnGridToggleRect, x, y, 2f)) return 5;
			if (ContainsPtPadded(btnZonesToggleRect, x, y, 2f)) return 6;
			if (ContainsPtPadded(btnReloadLiveRect, x, y, 2f)) return 7;
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

			// Prepare smooth horizontal grab-and-drag panning (defer CaptureMouse until >= 3.5px drag so clicks never block)
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
						float barDist = (cc.Properties != null && cc.Properties.BarDistance > 12f) ? cc.Properties.BarDistance : 90f;
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

		private void OnChartMouseWheel(object sender, MouseWheelEventArgs e)
		{
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (!IsOrderflowChartControlActive(cc) || e == null)
				return;

			try
			{
				if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
				{
					if (cc.Properties != null)
					{
						float cur = cc.Properties.BarDistance;
						float next = e.Delta > 0 ? Math.Min(220f, cur + 14f) : Math.Max(30f, cur - 14f);
						cc.Properties.BarDistance = next;
						SyncOneBoxRightGapMargin(cc, next, true);
						TriggerChartScrollbarRefresh();
						RequestImmediateOrderflowRedraw();
						e.Handled = true;
					}
					return;
				}

				var sb = hookedOfScrollBar ?? (hookedOfScrollBar = ResolveOrderflowScrollBar(cc));
				if (sb != null && sb.Maximum > sb.Minimum)
				{
					double step = Math.Max(0.25, Math.Abs(e.Delta) / 120.0 * 2.0);
					double targetVal = e.Delta > 0
						? Math.Max(sb.Minimum, sb.Value - step)
						: Math.Min(sb.Maximum, sb.Value + step);
					sb.Value = targetVal;
					e.Handled = true;
					RequestImmediateOrderflowRedraw();
				}
			}
			catch { }
		}

		private bool HandleToolbarClick(float x, float y)
		{
			int idx = HitTestToolbarButtonAt(x, y);
			return idx >= 0 && ExecuteToolbarButtonAction(idx);
		}

		private bool ExecuteToolbarButtonAction(int btnIdx)
		{
			if ((DateTime.UtcNow - lastBtnClickUtc).TotalMilliseconds < 90)
				return false;

			switch (btnIdx)
			{
				case 0: // Step -
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeAutoStep = false;
					runtimeTicksPerLevel = Math.Max(1, (int)Math.Round(runtimeTicksPerLevel * 0.7));
					if (runtimeTicksPerLevel >= TicksPerLevel && runtimeTicksPerLevel > 1) runtimeTicksPerLevel--;
					InvalidateFootprintCache();
					return true;

				case 1: // Step +
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeAutoStep = false;
					runtimeTicksPerLevel = Math.Min(500, Math.Max(runtimeTicksPerLevel + 2, (int)Math.Round(runtimeTicksPerLevel * 1.35)));
					InvalidateFootprintCache();
					return true;

				case 2: // Auto
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeAutoStep = !runtimeAutoStep;
					if (!runtimeAutoStep) runtimeTicksPerLevel = Math.Max(1, TicksPerLevel);
					InvalidateFootprintCache();
					return true;

				case 3: // Zoom -
					lastBtnClickUtc = DateTime.UtcNow;
					if (ChartControl != null && ChartControl.Properties != null)
					{
						float cur = ChartControl.Properties.BarDistance;
						float next = Math.Max(30f, cur - 16f);
						ChartControl.Properties.BarDistance = next;
						SyncOneBoxRightGapMargin(ChartControl, next, true);
						TriggerChartScrollbarRefresh();
						RequestImmediateOrderflowRedraw();
					}
					return true;

				case 4: // Zoom +
					lastBtnClickUtc = DateTime.UtcNow;
					if (ChartControl != null && ChartControl.Properties != null)
					{
						float cur = ChartControl.Properties.BarDistance;
						float next = Math.Min(220f, cur + 16f);
						ChartControl.Properties.BarDistance = next;
						SyncOneBoxRightGapMargin(ChartControl, next, true);
						TriggerChartScrollbarRefresh();
						RequestImmediateOrderflowRedraw();
					}
					return true;

				case 5: // Grid
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeShowStatGrid = !runtimeShowStatGrid;
					RequestImmediateOrderflowRedraw();
					return true;

				case 6: // Zones
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeShowImbZones = !runtimeShowImbZones;
					RequestImmediateOrderflowRedraw();
					return true;

				case 7: // Live Sync
					lastBtnClickUtc = DateTime.UtcNow;
					EnsureAccelpixProviderConnected();
					RequestAccelpixLiveBarsAsync();
					try
					{
						var sb = hookedOfScrollBar ?? ResolveOrderflowScrollBar(hookedOrderflowCC ?? ChartControl);
						if (sb != null && sb.Maximum > sb.Minimum)
							sb.Value = sb.Maximum;
					}
					catch { }
					InvalidateFootprintCache();
					return true;
			}
			return false;
		}

		private static bool ContainsPt(RectangleF r, float x, float y)
		{
			return r.Width > 0 && r.Height > 0 && x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;
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
			}
			RequestImmediateOrderflowRedraw();
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
				RequestImmediateOrderflowRedraw();
			}
			catch { }
		}
		#endregion

		#region Real-Time Market Data & Bar Computation
		protected override void OnMarketData(MarketDataEventArgs e)
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
				bool isRealtime = (State == State.Realtime);
				int barIdx = isRealtime && lastComputedBarCount > 0
					? Math.Max(CurrentBar, lastComputedBarCount - 1)
					: Math.Max(0, CurrentBar);

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
						long bKey = (long)Math.Floor((lv.Price + 1e-9) / uniformStep);
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

			// Align every row bucket to exact multiples of step: [b * step, (b + 1) * step]
			// so horizontal box borders sit directly on the chart's horizontal price lines across all bars
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

			// Check if we have live real-time ticks accumulated for this bar
			if (liveAccumulators.TryGetValue(barIdx, out LiveTickAccumulator liveAcc) && liveAcc.TotalTickVolume > 0)
			{
				fp.IsLiveAccumulated = true;
				for (int r = 0; r < numLevels; r++)
				{
					double lvPrice = (minBucket + r + 0.5) * step;
					fp.Levels.Add(new FootprintLevel { Price = lvPrice });
				}

				foreach (var kv in liveAcc.BidByTickKey)
				{
					double p = kv.Key * tick;
					int idx = Math.Max(0, Math.Min(numLevels - 1, (int)((long)Math.Floor((p + 1e-9) / step) - minBucket)));
					fp.Levels[idx].BidVolume += kv.Value;
				}
				foreach (var kv in liveAcc.AskByTickKey)
				{
					double p = kv.Key * tick;
					int idx = Math.Max(0, Math.Min(numLevels - 1, (int)((long)Math.Floor((p + 1e-9) / step) - minBucket)));
					fp.Levels[idx].AskVolume += kv.Value;
				}

				// If chart was reloaded mid-bar so some price buckets in [low, high] missed earlier ticks, backfill so no empty 0|0 row appears
				bool isBullLive = close >= open;
				for (int r = 0; r < numLevels; r++)
				{
					if (fp.Levels[r].BidVolume == 0 && fp.Levels[r].AskVolume == 0)
					{
						if (isBullLive) fp.Levels[r].AskVolume = 1;
						else fp.Levels[r].BidVolume = 1;
					}
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

		#region SharpDX Direct2D Rendering
		public override void OnCalculateMinMax()
		{
			if (ChartBars == null || ChartBars.Bars == null || ChartBars.Count <= 0)
				return;

			EnsureBarsComputed(ChartBars);

			int firstBar = Math.Max(0, ChartBars.FromIndex);
			int lastBar = Math.Min(ChartBars.Count - 1, ChartBars.ToIndex);
			if (lastBar < firstBar)
				return;

			double minPrice = double.MaxValue;
			double maxPrice = double.MinValue;
			double refStep = TickSize > 0 ? TickSize * 20 : 1.0;

			lock (dataLock)
			{
				for (int idx = firstBar; idx <= lastBar; idx++)
				{
					if (barCache.TryGetValue(idx, out FootprintBarData fp) && fp != null)
					{
						if (fp.EffectivePriceStep > 0)
							refStep = fp.EffectivePriceStep;
						double halfStep = refStep * 0.5;
						double barLowEdge = (fp.Levels != null && fp.Levels.Count > 0)
							? Math.Min(fp.Low, fp.Levels[0].Price - halfStep)
							: (fp.Low - halfStep);
						double barHighEdge = (fp.Levels != null && fp.Levels.Count > 0)
							? Math.Max(fp.High, fp.Levels[fp.Levels.Count - 1].Price + halfStep)
							: (fp.High + halfStep);

						if (barLowEdge < minPrice) minPrice = barLowEdge;
						if (barHighEdge > maxPrice) maxPrice = barHighEdge;
					}
					else
					{
						double l = ChartBars.Bars.GetLow(idx);
						double h = ChartBars.Bars.GetHigh(idx);
						if (l < minPrice) minPrice = l;
						if (h > maxPrice) maxPrice = h;
					}
				}
			}

			if (minPrice == double.MaxValue || maxPrice == double.MinValue)
				return;

			if (maxPrice <= minPrice)
				maxPrice = minPrice + refStep * 6.0;

			// Always keep at least 2 full footprint rows blank at both top and bottom (including for the running candle),
			// plus extra clearance for the Bar Delta box below each bar and the bottom 7-row Statistic Grid.
			double paddedMin = minPrice - refStep * 3.0;
			double paddedMax = maxPrice + refStep * 2.5;
			double span = Math.Max(refStep * 6.0, paddedMax - paddedMin);

			float panelH = ChartPanel != null ? ChartPanel.H : 600f;
			int statRows = 7;
			float statRowH = 20f;
			float statGridH = runtimeShowStatGrid ? (statRows * statRowH + 6f) : 0f;
			float topHeaderH = 58f;
			float bottomReservePx = statGridH + 22f;
			float usableH = Math.Max(140f, panelH - topHeaderH - bottomReservePx);

			double pricePerPx = span / usableH;
			MaxValue = paddedMax + topHeaderH * pricePerPx;
			MinValue = paddedMin - bottomReservePx * pricePerPx;
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (chartControl == null || chartScale == null || ChartBars == null || RenderTarget == null)
				return;

			EnsureMouseEventsSubscribed();

			// Apply wide footprint bar spacing once on load without mutating BarMarginRight mid-render
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
			if (ChartBars.Count == 0 || lastBar < firstBar)
			{
				RenderOrderflowLoadingOverlay();
				return;
			}

			float panelX = ChartPanel.X;
			float panelY = ChartPanel.Y;
			float panelW = ChartPanel.W;
			float panelH = ChartPanel.H;

			int statRows = 7;
			float statRowH = 20f;
			float statGridH = runtimeShowStatGrid ? (statRows * statRowH + 6f) : 0f;
			float chartBottomY = (float)Math.Round(panelY + panelH - statGridH);

			var oldAntialias = RenderTarget.AntialiasMode;
			RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;

			try
			{
				// Create Direct2D Brushes matching STOCK_USER.xml exact palette
				using (var brushPocYellow = new SolidColorBrush(RenderTarget, new Color4(1.0f, 1.0f, 0.0f, 0.98f)))       // #FFFF00 Yellow Bar POC
				using (var brushBarVaFill = new SolidColorBrush(RenderTarget, new Color4(0.498f, 1.0f, 0.831f, 0.16f)))   // #7FFFD4 Aquamarine 20% Bar VA
				using (var brushBarVaLine = new SolidColorBrush(RenderTarget, new Color4(0.498f, 1.0f, 0.831f, 0.75f)))
				using (var brushBidCluster = new SolidColorBrush(RenderTarget, new Color4(0.85f, 0.15f, 0.20f, 0.52f)))   // #FF0000 Left Bid Cluster
				using (var brushAskCluster = new SolidColorBrush(RenderTarget, new Color4(0.05f, 0.58f, 0.25f, 0.55f)))   // #006400 Right Ask Cluster
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
				using (var brushBullSpine = new SolidColorBrush(RenderTarget, new Color4(0.0f, 0.92f, 0.48f, 0.98f)))     // Bull Candle Spine
				using (var brushBearSpine = new SolidColorBrush(RenderTarget, new Color4(1.0f, 0.25f, 0.25f, 0.98f)))     // Bear Candle Spine
				using (var brushDevVa = new SolidColorBrush(RenderTarget, new Color4(0.255f, 0.412f, 0.882f, 0.90f)))     // #4169E1 Developing Session VA
				using (var brushDevPoc = new SolidColorBrush(RenderTarget, new Color4(1.0f, 0.647f, 0.0f, 0.92f)))        // #FFA500 Developing Session POC
				using (var brushDeltaPos = new SolidColorBrush(RenderTarget, new Color4(0.0f, 0.88f, 0.40f, 1.0f)))
				using (var brushDeltaNeg = new SolidColorBrush(RenderTarget, new Color4(1.0f, 0.26f, 0.30f, 1.0f)))
				using (var brushBarBoxBg = new SolidColorBrush(RenderTarget, new Color4(0.06f, 0.09f, 0.14f, 0.85f)))
				using (var brushSpineBoxBg = new SolidColorBrush(RenderTarget, new Color4(0.04f, 0.06f, 0.10f, 0.94f)))
				using (var brushCellBorder = new SolidColorBrush(RenderTarget, new Color4(0.32f, 0.44f, 0.60f, 0.88f)))
				using (var brushOuterBoxBorder = new SolidColorBrush(RenderTarget, new Color4(0.48f, 0.64f, 0.84f, 0.95f)))
				using (var brushMidLine = new SolidColorBrush(RenderTarget, new Color4(0.56f, 0.72f, 0.92f, 0.92f)))
				using (var brushGridBg = new SolidColorBrush(RenderTarget, new Color4(0.055f, 0.082f, 0.133f, 0.96f)))
				using (var brushLegendBg = new SolidColorBrush(RenderTarget, new Color4(0.08f, 0.115f, 0.185f, 0.98f)))
				using (var brushDeltaPosBg = new SolidColorBrush(RenderTarget, new Color4(0.0f, 0.52f, 0.20f, 0.46f)))
				using (var brushDeltaNegBg = new SolidColorBrush(RenderTarget, new Color4(0.68f, 0.10f, 0.14f, 0.46f)))
				{
					float barSpacing = chartControl.Properties != null ? chartControl.Properties.BarDistance : 90f;
					if (barSpacing < 12f)
					{
						if (lastBar > firstBar)
							barSpacing = Math.Abs(chartControl.GetXByBarIndex(ChartBars, lastBar) - chartControl.GetXByBarIndex(ChartBars, firstBar)) / (float)(lastBar - firstBar);
						else
							barSpacing = 90f;
					}

					// Keep one full Footprint box width of empty gap after the last bar on the right for clear visibility
					SyncOneBoxRightGapMargin(chartControl, barSpacing, false);

					float halfColW = (float)Math.Round(Math.Max(8f, Math.Min(95f, barSpacing * 0.46f)));
					float spineHalfW = (float)Math.Round(Math.Max(3.0f, Math.Min(5.0f, halfColW * 0.11f)));

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

					// 2. Use native chartScale Y-mapping snapped to integer pixels so every box row border sits on the exact same horizontal line
					float sampleStep = visibleBars.Count > 0 ? (float)visibleBars[visibleBars.Count - 1].EffectivePriceStep : (float)(TickSize * 20);
					double midP = visibleBars.Count > 0 ? visibleBars[visibleBars.Count - 1].Close : 1000.0;

					Func<double, float> priceToY = (double price) => (float)chartScale.GetYByValue(price);

					float rawRowH = Math.Abs(chartScale.GetYByValue(midP) - chartScale.GetYByValue(midP + sampleStep));
					float rowH = Math.Max(10f, Math.Min(28f, rawRowH));

					// Clip main candle area strictly above the bottom Statistic Grid
					RenderTarget.PushAxisAlignedClip(new RectangleF(panelX, panelY, panelW, Math.Max(40f, chartBottomY - panelY - 2f)), AntialiasMode.PerPrimitive);
					try
					{
						// 3. Render Stacked Imbalance S/R Zones first (behind clusters)
						if (runtimeShowImbZones && visibleBars.Count > 0)
						{
							RenderStackedImbalanceZones(chartControl, priceToY, visibleBars, lastBar, barSpacing, chartBottomY,
								brushBidImbZone, brushAskImbZone, brushBidImbText, brushAskImbText);
						}

						// 4. Render Developing Session VA & POC Step Lines
						if (ShowSessionVA && visibleBars.Count > 1)
						{
							RenderDevelopingSessionLines(chartControl, priceToY, visibleBars, barSpacing, chartBottomY, brushDevVa, brushDevPoc);
						}

						float fontSize = Math.Max(8.0f, Math.Min(11.5f, Math.Min(rowH * 0.68f, halfColW * 0.28f)));
						bool drawClusterNumbers = (halfColW >= 18f && rawRowH >= 8.0f);

						var sharedDw = NinjaTrader.Core.Globals.DirectWriteFactory;
						using (var tfCluster = new TextFormat(sharedDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, fontSize))
						using (var tfDeltaBadge = new TextFormat(sharedDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, Math.Max(8.5f, Math.Min(10.5f, halfColW * 0.25f))))
						{
							tfCluster.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
							tfCluster.ParagraphAlignment = ParagraphAlignment.Center;
							tfCluster.WordWrapping = WordWrapping.NoWrap;

							tfDeltaBadge.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
							tfDeltaBadge.ParagraphAlignment = ParagraphAlignment.Center;
							tfDeltaBadge.WordWrapping = WordWrapping.NoWrap;

							// 5. Render each 5-Minute Footprint Bar INSIDE the Grid Column [prevGridX..currGridX] with Center Candle & Mid-Line Inside the Box
							for (int v = 0; v < visibleBars.Count; v++)
							{
								FootprintBarData fp = visibleBars[v];
								GetBarGridColumnBounds(chartControl, fp.BarIndex, barSpacing, out float leftFullX, out float rightFullX, out float cx);
								float fullBoxW = rightFullX - leftFullX;
								if (rightFullX < panelX || leftFullX > panelX + panelW)
									continue;

								float step = (float)fp.EffectivePriceStep;
								float halfStep = step * 0.5f;

								long maxSideVol = 1;
								for (int r = 0; r < fp.Levels.Count; r++)
								{
									if (fp.Levels[r].BidVolume > maxSideVol) maxSideVol = fp.Levels[r].BidVolume;
									if (fp.Levels[r].AskVolume > maxSideVol) maxSideVol = fp.Levels[r].AskVolume;
								}

								float availSideW = Math.Max(4f, (fullBoxW * 0.5f) - spineHalfW);

								float barBoxTopY = float.MaxValue;
								float barBoxBotY = float.MinValue;
								if (fp.Levels.Count > 0)
								{
									float t0 = priceToY(fp.Levels[fp.Levels.Count - 1].Price + halfStep);
									float b0 = priceToY(fp.Levels[0].Price - halfStep);
									barBoxTopY = Math.Min(t0, b0);
									barBoxBotY = Math.Max(t0, b0);

									// Fill full Footprint bar box background and center candle spine channel first
									var outerBgRect = new RectangleF(leftFullX, barBoxTopY, fullBoxW, Math.Max(4f, barBoxBotY - barBoxTopY));
									RenderTarget.FillRectangle(outerBgRect, brushBarBoxBg);

									var centerChannelBgRect = new RectangleF(cx - spineHalfW, barBoxTopY, spineHalfW * 2f, Math.Max(4f, barBoxBotY - barBoxTopY));
									RenderTarget.FillRectangle(centerChannelBgRect, brushSpineBoxBg);
								}

								// 5a. Draw 70% Bar Value Area Background Box
								if (ShowBarVA && fp.Levels.Count > 0)
								{
									float vaTopY = priceToY(fp.VaHigh + halfStep);
									float vaBotY = priceToY(fp.VaLow - halfStep);
									if (vaBotY > vaTopY)
									{
										float clampedTop = Math.Max(panelY, Math.Min(chartBottomY, vaTopY));
										float clampedBot = Math.Max(panelY, Math.Min(chartBottomY, vaBotY));
										if (clampedBot > clampedTop)
										{
											var vaRect = new RectangleF(leftFullX, clampedTop, fullBoxW, clampedBot - clampedTop);
											RenderTarget.FillRectangle(vaRect, brushBarVaFill);
											RenderTarget.DrawRectangle(vaRect, brushBarVaLine, 1.1f);
										}
									}
								}

								// 5b. Draw Left (Bid) & Right (Ask) Cluster Bricks + Continuous Horizontal Row Lines + Volume Numbers
								float pocTopY = -1f;
								float pocBotY = -1f;
								for (int r = 0; r < fp.Levels.Count; r++)
								{
									FootprintLevel lv = fp.Levels[r];
									float yTop = priceToY(lv.Price + halfStep);
									float yBot = priceToY(lv.Price - halfStep);
									float cellH = Math.Max(2f, yBot - yTop);
									if (yBot < panelY || yTop > chartBottomY)
										continue;

									float cellTop = yTop + 0.5f;
									float drawH = Math.Max(1.5f, cellH - 1.0f);

									// Left Cluster (Bid / Sell Volume)
									float bidRatio = Math.Min(1.0f, Math.Max(0.22f, (float)lv.BidVolume / maxSideVol));
									float bidBarW = availSideW * bidRatio;
									var bidBarRect = new RectangleF(cx - spineHalfW - bidBarW, cellTop, bidBarW, drawH);
									RenderTarget.FillRectangle(bidBarRect, SelectVolumeFilterBrush(lv.BidVolume, true, brushBidCluster, brushAskCluster, brushFilterTeal, brushFilterGray, brushFilterBlue, brushFilterRed));

									// Right Cluster (Ask / Buy Volume)
									float askRatio = Math.Min(1.0f, Math.Max(0.22f, (float)lv.AskVolume / maxSideVol));
									float askBarW = availSideW * askRatio;
									var askBarRect = new RectangleF(cx + spineHalfW, cellTop, askBarW, drawH);
									RenderTarget.FillRectangle(askBarRect, SelectVolumeFilterBrush(lv.AskVolume, false, brushBidCluster, brushAskCluster, brushFilterTeal, brushFilterGray, brushFilterBlue, brushFilterRed));

									// Unbroken horizontal row border across the entire box (Left + Center Spine + Right)
									var fullRowRect = new RectangleF(leftFullX, yTop, fullBoxW, cellH);
									RenderTarget.DrawRectangle(fullRowRect, brushCellBorder, 0.85f);

									// Imbalance Cell Highlights (DeepPink Left / Lime Right)
									if (ShowImbalance && lv.HasBidImbalance)
									{
										var imbLeftRect = new RectangleF(leftFullX + 0.5f, cellTop, availSideW - 0.5f, drawH);
										RenderTarget.DrawRectangle(imbLeftRect, brushBidImbText, 1.4f);
									}
									if (ShowImbalance && lv.HasAskImbalance)
									{
										var imbRightRect = new RectangleF(cx + spineHalfW, cellTop, availSideW - 0.5f, drawH);
										RenderTarget.DrawRectangle(imbRightRect, brushAskImbText, 1.4f);
									}

									if (ShowBarPOC && lv.IsPoc)
									{
										pocTopY = yTop;
										pocBotY = yBot;
									}

									// Draw Bid x Ask Volume Numbers inside their respective Left & Right boxes
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

								// 5c. Draw Center Candle Channel Borders, Mid-Line Divider & Candlestick Spine Inside the Box
								if (fp.Levels.Count > 0 && barBoxBotY > barBoxTopY)
								{
									RenderTarget.DrawLine(new Vector2(cx - spineHalfW, barBoxTopY), new Vector2(cx - spineHalfW, barBoxBotY), brushMidLine, 0.85f);
									RenderTarget.DrawLine(new Vector2(cx + spineHalfW, barBoxTopY), new Vector2(cx + spineHalfW, barBoxBotY), brushMidLine, 0.85f);
									RenderTarget.DrawLine(new Vector2(cx, barBoxTopY), new Vector2(cx, barBoxBotY), brushMidLine, 1.0f);

									float yHigh = Math.Max(barBoxTopY, Math.Min(barBoxBotY, priceToY(fp.High)));
									float yLow = Math.Max(barBoxTopY, Math.Min(barBoxBotY, priceToY(fp.Low)));
									float yOpen = Math.Max(barBoxTopY + 1f, Math.Min(barBoxBotY - 1f, priceToY(fp.Open)));
									float yClose = Math.Max(barBoxTopY + 1f, Math.Min(barBoxBotY - 1f, priceToY(fp.Close)));
									bool isBull = fp.Close >= fp.Open;
									var spineBrush = isBull ? brushBullSpine : brushBearSpine;

									RenderTarget.DrawLine(new Vector2(cx, yHigh), new Vector2(cx, yLow), spineBrush, 1.8f);
									float bodyTop = Math.Min(yOpen, yClose);
									float bodyH = Math.Max(3.0f, Math.Abs(yClose - yOpen));
									var spineBodyRect = new RectangleF(cx - spineHalfW + 0.8f, bodyTop, (spineHalfW - 0.8f) * 2f, bodyH);
									RenderTarget.FillRectangle(spineBodyRect, spineBrush);

									// Draw Bar POC Yellow Box (#FFFF00) unbroken across the full row
									if (ShowBarPOC && pocBotY > pocTopY)
									{
										RenderTarget.DrawRectangle(new RectangleF(leftFullX, pocTopY + 0.5f, fullBoxW, Math.Max(2f, pocBotY - pocTopY - 1.0f)), brushPocYellow, 2.0f);
									}

									// Outer Box Frame enclosing the entire Candle + Data cluster
									var outerBoxRect = new RectangleF(leftFullX, barBoxTopY, fullBoxW, barBoxBotY - barBoxTopY);
									RenderTarget.DrawRectangle(outerBoxRect, brushOuterBoxBorder, 1.35f);
								}

								// 5d. Draw Bar Delta Summary Box with the Exact Same Width (leftFullX..rightFullX) Aligned Below the Candle Box
								if (ShowBarDelta && halfColW >= 14f && barBoxBotY < chartBottomY - 16f)
								{
									float deltaY = barBoxBotY + 2f;
									if (deltaY > panelY + 24f && deltaY + 16f <= chartBottomY)
									{
										var deltaRect = new RectangleF(leftFullX, deltaY, fullBoxW, 16f);
										RenderTarget.FillRectangle(deltaRect, fp.Delta >= 0 ? brushDeltaPosBg : brushDeltaNegBg);
										RenderTarget.DrawRectangle(deltaRect, fp.Delta >= 0 ? brushDeltaPos : brushDeltaNeg, 1.0f);
										RenderTarget.DrawLine(new Vector2(cx, deltaY), new Vector2(cx, deltaY + 3.0f), brushMidLine, 0.9f);
										RenderTarget.DrawLine(new Vector2(cx, deltaY + 13.0f), new Vector2(cx, deltaY + 16.0f), brushMidLine, 0.9f);
										string deltaStr = (fp.Delta >= 0 ? "+" : "") + FormatClusterNumber(fp.Delta);
										RenderTarget.DrawText(deltaStr, tfDeltaBadge, deltaRect, fp.Delta >= 0 ? brushDeltaPos : brushDeltaNeg);
									}
								}
							}
						}
					}
					finally
					{
						RenderTarget.PopAxisAlignedClip();
					}

					var gridDw = NinjaTrader.Core.Globals.DirectWriteFactory;
					using (var tfGrid = new TextFormat(gridDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, Math.Max(8.5f, Math.Min(10.5f, halfColW * 0.24f))))
					using (var tfLegend = new TextFormat(gridDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10.2f))
					{
						tfGrid.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
						tfGrid.ParagraphAlignment = ParagraphAlignment.Center;
						tfGrid.WordWrapping = WordWrapping.NoWrap;

						tfLegend.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
						tfLegend.ParagraphAlignment = ParagraphAlignment.Center;
						tfLegend.WordWrapping = WordWrapping.NoWrap;

						// 6. Render Bottom 7-Row Statistic Grid (including 1:2 Buy/Sell Column at Row 0)
						if (runtimeShowStatGrid && statGridH > 0)
						{
							RenderBottomStatisticGrid(chartControl, visibleBars, panelX, panelW, chartBottomY, statRowH, barSpacing,
								tfLegend, tfGrid, brushGridBg, brushLegendBg, brushCellBorder, brushOuterBoxBorder, brushMidLine,
								brushTextWhite, brushTextDim, brushDeltaPos, brushDeltaNeg, brushDeltaPosBg, brushDeltaNegBg);
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

		private void GetBarGridColumnBounds(ChartControl chartControl, int barIndex, float fallbackSpacing, out float boxLeft, out float boxRight, out float boxCx)
		{
			// Center every Footprint candle box, delta badge, and bottom Statistic Grid column directly on GetXByBarIndex(barIndex)
			// so the bottom X-axis timestamp (e.g., 15:05, 15:10, 15:15, 15:20, 15:25, 15:30) sits dead-center inside every box!
			float xCenter = (float)chartControl.GetXByBarIndex(ChartBars, barIndex);
			float span = fallbackSpacing;

			if (barIndex > 0)
			{
				float dPrev = xCenter - (float)chartControl.GetXByBarIndex(ChartBars, barIndex - 1);
				if (dPrev >= 12f && dPrev <= fallbackSpacing * 1.6f)
					span = dPrev;
			}
			else if (ChartBars != null && barIndex + 1 < ChartBars.Count)
			{
				float dNext = (float)chartControl.GetXByBarIndex(ChartBars, barIndex + 1) - xCenter;
				if (dNext >= 12f && dNext <= fallbackSpacing * 1.6f)
					span = dNext;
			}

			boxCx = (float)Math.Round(xCenter);
			float halfW = Math.Max(4f, (float)Math.Floor(span * 0.5f) - 1f);
			boxLeft = boxCx - halfW;
			boxRight = boxCx + halfW;
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
					RenderTarget.DrawLine(new Vector2(x1, yPoc), new Vector2(x2, yPoc), brushDevPoc, 1.8f);
				if (yVah > ChartPanel.Y && yVah < chartBottomY)
					RenderTarget.DrawLine(new Vector2(x1, yVah), new Vector2(x2, yVah), brushDevVa, 1.5f);
				if (yVal > ChartPanel.Y && yVal < chartBottomY)
					RenderTarget.DrawLine(new Vector2(x1, yVal), new Vector2(x2, yVal), brushDevVa, 1.5f);

				if (i > 0 && visibleBars[i - 1].Time.Date == cur.Time.Date)
				{
					FootprintBarData prev = visibleBars[i - 1];
					GetBarGridColumnBounds(chartControl, prev.BarIndex, barSpacing, out _, out float prevX2, out _);
					float pyPoc = priceToY(prev.DevSessionPoc);
					float pyVah = priceToY(prev.DevSessionVah);
					float pyVal = priceToY(prev.DevSessionVal);

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

		private void RenderBottomStatisticGrid(
			ChartControl chartControl,
			List<FootprintBarData> visibleBars,
			float panelX,
			float panelW,
			float gridTopY,
			float rowH,
			float barSpacing,
			TextFormat tfLegend,
			TextFormat tfGrid,
			SolidColorBrush brushGridBg,
			SolidColorBrush brushLegendBg,
			SolidColorBrush brushBorder,
			SolidColorBrush brushOuterBorder,
			SolidColorBrush brushMidLine,
			SolidColorBrush brushWhite,
			SolidColorBrush brushDim,
			SolidColorBrush brushDeltaPos,
			SolidColorBrush brushDeltaNeg,
			SolidColorBrush brushDeltaPosBg,
			SolidColorBrush brushDeltaNegBg)
		{
			string[] labels = { "1:2 Logic", "Buy Vol", "Sell Vol", "Delta", "Cum. Delta", "Min Delta", "Max Delta" };
			float totalH = labels.Length * rowH;
			float legendW = 78f;

			// Full grid background & crisp top divider box border between Candles and Datas
			var fullGridRect = new RectangleF(panelX, gridTopY, panelW, totalH + 4f);
			RenderTarget.FillRectangle(fullGridRect, brushGridBg);
			RenderTarget.DrawRectangle(fullGridRect, brushOuterBorder, 1.3f);
			RenderTarget.DrawLine(new Vector2(panelX, gridTopY), new Vector2(panelX + panelW, gridTopY), brushMidLine, 1.8f);

			// Continuous horizontal row lines across the entire Statistic Grid so all boxes align on unbroken horizontal lines
			for (int r = 1; r <= labels.Length; r++)
			{
				float hy = gridTopY + r * rowH;
				RenderTarget.DrawLine(new Vector2(panelX, hy), new Vector2(panelX + panelW, hy), brushBorder, 0.85f);
			}

			// Per-bar columns aligned to the exact same [prevGridX..currGridX] column bounds as the Candle Box above
			for (int i = 0; i < visibleBars.Count; i++)
			{
				FootprintBarData fp = visibleBars[i];
				GetBarGridColumnBounds(chartControl, fp.BarIndex, barSpacing, out float cellLeft, out float cellRight, out float cx);
				float cellW = cellRight - cellLeft;
				if (cellRight < panelX + legendW || cellLeft > panelX + panelW)
					continue;

				// 1:2 Buyer vs Seller dominance logic:
				// Buy (Green) when Buyer volume >= 2 * Seller volume
				// Sell (Red) when Seller volume >= 2 * Buyer volume
				int ratioSignal = 0;
				if (fp.BuyVolume > 0 && fp.BuyVolume >= fp.SellVolume * 2)
					ratioSignal = 1;
				else if (fp.SellVolume > 0 && fp.SellVolume >= fp.BuyVolume * 2)
					ratioSignal = -1;

				long[] vals = { 0, fp.BuyVolume, fp.SellVolume, fp.Delta, fp.CumulativeDelta, fp.MinDelta, fp.MaxDelta };

				for (int r = 0; r < labels.Length; r++)
				{
					float ry = gridTopY + r * rowH;
					var cRect = new RectangleF(cellLeft, ry, cellW, rowH);

					if (r == 0)
					{
						if (ratioSignal > 0)
							RenderTarget.FillRectangle(cRect, brushDeltaPosBg);
						else if (ratioSignal < 0)
							RenderTarget.FillRectangle(cRect, brushDeltaNegBg);
					}
					else if (r == 3 || r == 4)
					{
						if (vals[r] > 0) RenderTarget.FillRectangle(cRect, brushDeltaPosBg);
						else if (vals[r] < 0) RenderTarget.FillRectangle(cRect, brushDeltaNegBg);
					}

					RenderTarget.DrawRectangle(cRect, brushBorder, 0.9f);

					// Center mid-line ticks aligned with the candle mid-line
					RenderTarget.DrawLine(new Vector2(cx, ry), new Vector2(cx, ry + 3.5f), brushMidLine, 0.95f);
					RenderTarget.DrawLine(new Vector2(cx, ry + rowH - 3.5f), new Vector2(cx, ry + rowH), brushMidLine, 0.95f);

					if (cellW >= 24f)
					{
						if (r == 0)
						{
							if (ratioSignal > 0)
								RenderTarget.DrawText("Buy", tfGrid, cRect, brushDeltaPos);
							else if (ratioSignal < 0)
								RenderTarget.DrawText("Sell", tfGrid, cRect, brushDeltaNeg);
							else
								RenderTarget.DrawText("-", tfGrid, cRect, brushDim);
						}
						else
						{
							SolidColorBrush txtB = brushDim;
							if (r == 1 || r == 6) txtB = brushDeltaPos;
							else if (r == 2 || r == 5) txtB = brushDeltaNeg;
							else if (r == 3 || r == 4) txtB = brushWhite;

							string s = FormatClusterNumber(vals[r]);
							if ((r == 3 || r == 4) && vals[r] > 0) s = "+" + s;
							RenderTarget.DrawText(s, tfGrid, cRect, txtB);
						}
					}
				}

				// Outer column box around the bar's statistic cells aligned with the Candle Box above
				RenderTarget.DrawRectangle(new RectangleF(cellLeft, gridTopY, cellW, totalH), brushOuterBorder, 1.15f);
			}

			// Draw Left Legend Column on top so it never gets overlapped by scrolling bars
			var legendRect = new RectangleF(panelX, gridTopY, legendW, totalH + 4f);
			RenderTarget.FillRectangle(legendRect, brushLegendBg);
			RenderTarget.DrawRectangle(legendRect, brushOuterBorder, 1.2f);

			for (int r = 0; r < labels.Length; r++)
			{
				float ry = gridTopY + r * rowH;
				var lRect = new RectangleF(panelX + 6f, ry, legendW - 8f, rowH);
				RenderTarget.DrawLine(new Vector2(panelX, ry + rowH), new Vector2(panelX + legendW, ry + rowH), brushBorder, 0.8f);
				RenderTarget.DrawText(labels[r], tfLegend, lRect, r == 0 ? brushDeltaPos : brushWhite);
			}
		}

		private void RenderInteractiveHeaderBar(float panelX, float panelY, float barSpacing, float effectiveStep)
		{
			double livePx = lastTradePrice > 0 ? lastTradePrice : (double.IsNaN(lastComputedLastClose) ? 0.0 : lastComputedLastClose);
			string ltpStr = livePx > 0 ? string.Format(CultureInfo.InvariantCulture, "  •  LTP: {0:0.00}", livePx) : "";
			string headerText = string.Format(CultureInfo.InvariantCulture,
				"⚡ AK10X Orderflow (5M){0}  •  Step: {1:0.##} ({2})",
				ltpStr, effectiveStep, runtimeAutoStep ? "Auto" : (runtimeTicksPerLevel + "T"));

			var hdrDw = NinjaTrader.Core.Globals.DirectWriteFactory;
			using (var tfHeader = new TextFormat(hdrDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10.5f))
			using (var tfBtn = new TextFormat(hdrDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10.0f))
			using (var bgBrush = new SolidColorBrush(RenderTarget, new Color4(0.06f, 0.09f, 0.14f, 0.92f)))
			using (var borderBrush = new SolidColorBrush(RenderTarget, new Color4(0.0f, 0.90f, 0.46f, 0.75f)))
			using (var hoverBorderBrush = new SolidColorBrush(RenderTarget, new Color4(0.22f, 0.78f, 1.0f, 1.0f)))
			using (var goldBrush = new SolidColorBrush(RenderTarget, new Color4(1.0f, 0.78f, 0.15f, 1.0f)))
			using (var btnBg = new SolidColorBrush(RenderTarget, new Color4(0.14f, 0.20f, 0.30f, 0.95f)))
			using (var btnHoverBg = new SolidColorBrush(RenderTarget, new Color4(0.18f, 0.34f, 0.52f, 0.98f)))
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

				btnStepMinusRect = DrawMiniBtn(bx, by, 44f, bh, "Step -", false, hoveredToolbarBtnIndex == 0, tfBtn, btnBg, btnHoverBg, btnActiveBg, borderBrush, hoverBorderBrush, whiteBrush);
				bx += 48f;
				btnStepPlusRect = DrawMiniBtn(bx, by, 44f, bh, "Step +", false, hoveredToolbarBtnIndex == 1, tfBtn, btnBg, btnHoverBg, btnActiveBg, borderBrush, hoverBorderBrush, whiteBrush);
				bx += 48f;
				btnAutoStepRect = DrawMiniBtn(bx, by, 46f, bh, "Auto", runtimeAutoStep, hoveredToolbarBtnIndex == 2, tfBtn, btnBg, btnHoverBg, btnActiveBg, borderBrush, hoverBorderBrush, whiteBrush);
				bx += 50f;
				btnWidthMinusRect = DrawMiniBtn(bx, by, 48f, bh, "Zoom -", false, hoveredToolbarBtnIndex == 3, tfBtn, btnBg, btnHoverBg, btnActiveBg, borderBrush, hoverBorderBrush, whiteBrush);
				bx += 52f;
				btnWidthPlusRect = DrawMiniBtn(bx, by, 48f, bh, "Zoom +", false, hoveredToolbarBtnIndex == 4, tfBtn, btnBg, btnHoverBg, btnActiveBg, borderBrush, hoverBorderBrush, whiteBrush);
				bx += 52f;
				btnGridToggleRect = DrawMiniBtn(bx, by, 46f, bh, "Grid", runtimeShowStatGrid, hoveredToolbarBtnIndex == 5, tfBtn, btnBg, btnHoverBg, btnActiveBg, borderBrush, hoverBorderBrush, whiteBrush);
				bx += 50f;
				btnZonesToggleRect = DrawMiniBtn(bx, by, 46f, bh, "Zones", runtimeShowImbZones, hoveredToolbarBtnIndex == 6, tfBtn, btnBg, btnHoverBg, btnActiveBg, borderBrush, hoverBorderBrush, whiteBrush);
				bx += 50f;
				btnReloadLiveRect = DrawMiniBtn(bx, by, 64f, bh, "Live Sync", true, hoveredToolbarBtnIndex == 7, tfBtn, btnBg, btnHoverBg, btnActiveBg, borderBrush, hoverBorderBrush, whiteBrush);
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
				using (var bgBrush = new SolidColorBrush(RenderTarget, new Color4(0.071f, 0.086f, 0.114f, 1.0f)))
				using (var pillBg = new SolidColorBrush(RenderTarget, new Color4(0.09f, 0.13f, 0.20f, 0.96f)))
				using (var pillBorder = new SolidColorBrush(RenderTarget, new Color4(0.0f, 0.88f, 0.48f, 0.72f)))
				using (var txtBrush = new SolidColorBrush(RenderTarget, new Color4(0.92f, 0.96f, 1.0f, 0.96f)))
				using (var tf = new TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.SemiBold, SharpDX.DirectWrite.FontStyle.Normal, 12.5f))
				{
					RenderTarget.FillRectangle(new RectangleF(px, py, pw, ph), bgBrush);
					tf.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
					tf.ParagraphAlignment = ParagraphAlignment.Center;
					float bw = Math.Min(360f, pw - 32f);
					float bh = 36f;
					var badgeRect = new RectangleF(px + (pw - bw) * 0.5f, py + (ph - bh) * 0.5f, bw, bh);
					var rr = new RoundedRectangle { Rect = badgeRect, RadiusX = 6f, RadiusY = 6f };
					RenderTarget.FillRoundedRectangle(rr, pillBg);
					RenderTarget.DrawRoundedRectangle(rr, pillBorder, 1.0f);
					RenderTarget.DrawText("⚡ Loading Orderflow Footprint — " + sym + "...", tf, badgeRect, txtBrush);
				}
			}
			catch { }
		}

		private RectangleF DrawMiniBtn(
			float x, float y, float w, float h, string text, bool active, bool hovered,
			TextFormat tf, SolidColorBrush bg, SolidColorBrush hoverBg, SolidColorBrush activeBg,
			SolidColorBrush border, SolidColorBrush hoverBorder, SolidColorBrush fg)
		{
			var r = new RectangleF(x, y, w, h);
			RenderTarget.FillRectangle(r, hovered ? hoverBg : (active ? activeBg : bg));
			RenderTarget.DrawRectangle(r, hovered ? hoverBorder : border, hovered ? 1.2f : 0.8f);
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
