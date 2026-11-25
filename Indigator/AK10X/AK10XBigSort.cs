// AK10XBigSort — Unified Buyer & Seller Orderflow + Global Market Profile Zones & Right-Side Virgin Values for NinjaTrader 8.
// Combines the full AK10X Orderflow engine (Footprint, Imbalance, Delta, TotalVolume, Halfpack, Top Delta / Bottom Height pills,
// 3-Row Momentum/Strength/Delta table, and smooth Mouse-Wheel Candlestick <-> Footprint zoom) with the complete AK10XGlobalMarketProfile
// session & Virginity engine (Value Area Zones, Buying/Selling Single Prints, VPoC, VPPoC, PoorHigh/Low, Closed DayHigh/DayLow,
// Closed Mon-Fri WeeklyHigh/WeeklyLow, MidLine, CrossBased/CloseBased Virginity Break Rule, and De-overlapped Right-Side Bar Values).
#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WpfControls = System.Windows.Controls;
using WpfPrimitives = System.Windows.Controls.Primitives;
using WpfMedia = System.Windows.Media;
using WpfShapes = System.Windows.Shapes;
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
using DxBrush = SharpDX.Direct2D1.SolidColorBrush;
using DxTextFormat = SharpDX.DirectWrite.TextFormat;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
	public class AK10XBigSort : Indicator
	{
		#region Orderflow Data Structures
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
			public double EffectivePriceStep;
			public double AppliedImbalancePct;
			public int AppliedMinVolume;
			public string SessionTag;
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
		#endregion

		#region Market Profile Zone Data Structures
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
			public DateTime StartTime;
			public DateTime EndTime;
			public DateTime FirstSampleTime;
			public DateTime LastSampleTime;
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

			public double EffectiveStep;
			public int PocBin;
			public int VahBin;
			public int ValBin;
			public int VolumePocBin;
			public int VolumeVahBin;
			public int VolumeValBin;
			public double MaxRowVolume;
			public bool IsMerged;
			public float ChartStartX;
			public float ChartEndX;
			public float HeaderLeftX;
			public float HeaderRightX;
			public List<string> SourceProfileKeys = new List<string>();
			public List<SinglePrintZone> SinglePrintZones = new List<SinglePrintZone>();
			public bool ContinuityChecked;
			public bool AnalyticsValid;
			public double AnalyticsStep;
			public double AnalyticsVaPct;
		}

		private sealed class DateSpanGroup
		{
			public DateTime Date;
			public List<DayProfile> Sessions = new List<DayProfile>();
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
			public Color4? TextColor;
			public Color4? FillColor;
			public Color4? BorderColor;
			public int Priority;
		}

		private struct SrOhlcBar
		{
			public DateTime Time;
			public DateTime HighTime;
			public DateTime LowTime;
			public double Open;
			public double High;
			public double Low;
			public double Close;
		}

		private struct SrRawPivot
		{
			public bool IsHigh;
			public double Price;
			public double Atr;
			public DateTime Time;
			public DateTime ConfirmTime;
		}

		private sealed class SrLineTrack
		{
			public double Price;
			public bool InitialIsResistance;
			public bool IsMajor;
			public DateTime OriginTime;
			public bool HasFlipped;
			public DateTime FlipTime;
			public bool IsFlipBroken;
			public DateTime ValidationEndTime;
			public string ActiveRightTagText;
		}

		private sealed class SrHorizonLevel
		{
			public string KeyLabel;
			public string RightTagText;
			public double Price;
			public DateTime OriginTime;
			public int Kind; // 0 = Day, 1 = Week, 2 = Month
			public bool IsHigh;
		}

		private sealed class DailyHorizonSample
		{
			public DateTime Date;
			public double High = double.MinValue;
			public DateTime HighTime;
			public double Low = double.MaxValue;
			public DateTime LowTime;
			public double Close;
		}

		private sealed class SrComputedSnapshot
		{
			public List<SrLineTrack> LineTracks = new List<SrLineTrack>();
			public List<SrHorizonLevel> HorizonLevels = new List<SrHorizonLevel>();
		}

		private struct SignalOhlcBar
		{
			public DateTime Time;
			public DateTime HighTime;
			public DateTime LowTime;
			public double Open;
			public double High;
			public double Low;
			public double Close;
			public int ChartBarIndex;
		}

		private struct SignalRawPivot
		{
			public bool IsHigh;
			public double Price;
			public double Atr;
			public DateTime PivotTime;
			public DateTime ConfirmTime;
		}

		private sealed class SignalActiveZone
		{
			public double Top;
			public double Bottom;
			public DateTime LeftTime;
			public int SourceMask; // 1 = Current TF (3m), 2 = Higher TF (9m), 3 = Merged 3m + 9m
			public int Reactions;
			public bool WasInZone;
			public bool VeryStrong;
		}

		private sealed class ChartSignalMarker
		{
			public int BarIndex;
			public bool HasSwingHigh;
			public bool SwingHighHtfConfluence;
			public bool HasSwingLow;
			public bool SwingLowHtfConfluence;
			public bool HasBearishEntry;
			public string BearishPatternShort;
			public string BearishPatternFull;
			public int BearishStrength; // 3 = Very Strong (3m+9m), 2 = Strong, 1 = Zone, 0 = PD Level
			public bool HasBullishEntry;
			public string BullishPatternShort;
			public string BullishPatternFull;
			public int BullishStrength;
		}

		private sealed class SignalComputedSnapshot
		{
			public int LastClosedBarIdx = -1;
			public Dictionary<int, ChartSignalMarker> ByBar = new Dictionary<int, ChartSignalMarker>();
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

		private enum LevelBreakMode
		{
			CloseAbove = 0,
			CloseBelow = 1,
			CrossClose = 2
		}

		private enum RrDragMode
		{
			None = 0,
			PendingMove = 1,
			MoveWholeBox = 2,
			ScaleTarget = 3,
			ScaleStop = 4,
			ScaleEntryAndStart = 5,
			ScaleEnd = 6
		}

		private sealed class RrPositionBox
		{
			public int Id;
			public bool IsLong;
			public double StartBarSlot;
			public double EndBarSlot;
			public double EntryPrice;
			public double TargetPrice;
			public double StopPrice;
			public bool AutoExtendLive;
			public RectangleF LastDeleteBtnRect;
			public RectangleF LastFixedRrRect;
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
		private const float RightValuesLaneWidth = 188f;
		private const string LetterSequence = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

		private static readonly SharpDX.Color[] SessionColors = new SharpDX.Color[]
		{
			new SharpDX.Color(248, 192, 62),  // 0: Asia (Gold)
			new SharpDX.Color(72, 228, 138),  // 1: London (Emerald Green)
			new SharpDX.Color(248, 118, 158), // 2: New York (Coral Pink)
			new SharpDX.Color(92, 196, 248),  // 3: India / Custom (Sky Blue)
		};

		// Orderflow state (guarded by dataLock)
		private readonly Dictionary<int, FootprintBarData> barCache = new Dictionary<int, FootprintBarData>();
		private readonly Dictionary<int, LiveTickAccumulator> liveAccumulators = new Dictionary<int, LiveTickAccumulator>();
		private readonly Dictionary<DateTime, LiveTickAccumulator> historicalTickBySlot = new Dictionary<DateTime, LiveTickAccumulator>();
		private readonly Dictionary<DateTime, List<SubBar1M>> subBarsBySlot = new Dictionary<DateTime, List<SubBar1M>>();
		private readonly object dataLock = new object();

		// Market Profile state (guarded by profileLock — never nested with dataLock)
		private readonly object profileLock = new object();
		private readonly SortedDictionary<DateTime, MinuteSample> mpMinuteMap = new SortedDictionary<DateTime, MinuteSample>();
		private readonly List<MinuteSample> rawMinuteSamples = new List<MinuteSample>(65536);
		private readonly SortedDictionary<string, DayProfile> profiles = new SortedDictionary<string, DayProfile>(StringComparer.Ordinal);
		private readonly HashSet<string> mergedWithNextKeys = new HashSet<string>(StringComparer.Ordinal);
		private readonly HashSet<string> unmergedOverrideKeys = new HashSet<string>(StringComparer.Ordinal);
		private ProfileSession[] activeSessions = new ProfileSession[0];
		private double cachedPriceStep;
		private bool mp1MBackfillLoaded;
		private int mpSeededChartBarCount = -1;
		private int highestBackfillDaysRequested = 0;
		private DateTime lastFeed1MUpdateUtc = DateTime.MinValue;

		private double lastBidPrice;
		private double lastAskPrice;
		private double lastTradePrice;
		private int lastTickDirection = 1;
		private DateTime lastNativeOnMarketDataUtc = DateTime.MinValue;

		private int lastComputedBarCount = -1;
		private double lastComputedLastClose = double.NaN;
		private long lastComputedLastVol = -1;
		private double lastComputedUniformStep = -1.0;
		private int lastComputedSessionCfgHash = 0;
		private int lastEffectiveAutoTicks = 10;
		private int runtimeTicksPerLevel = 4;
		private bool runtimeAutoStep = true;
		private bool runtimeSessionStepActive = true;
		private bool runtimeShowStatGrid = true;
		private bool runtimeShowImbZones = false;
		private bool runtimeShowHalfpack = true;
		private bool runtimeShowMpZones = true;
		private bool runtimeShowSrZones = true;
		private bool runtimeShowSrAnalysis = false;
		private bool runtimeShowSignals = false;
		private bool runtimeLightTheme = false;
		private AK10XOrderflowMode runtimeOrderflowMode = AK10XOrderflowMode.VolumeFootprint;
		private AK10XOrderflowDataFeed? runtimeDataFeedOverride;
		private double runtimeYZoomFactor = 1.0;
		private bool initialBarWidthApplied = false;

		// Multi-Timeframe S/R Zones State (guarded by srLock — never nested with dataLock or profileLock)
		private readonly object srLock = new object();
		private readonly SortedDictionary<DateTime, DailyHorizonSample> srDailyHistory = new SortedDictionary<DateTime, DailyHorizonSample>();
		private SrComputedSnapshot cachedSrSnapshot = null;
		private int srCacheChartBarCount = -1;
		private int srCacheMinuteCount = -1;
		private int srCacheDailyCount = -1;
		private long srCacheCloseTickBucket = long.MinValue;
		private long srCacheLastHighBucket = long.MinValue;
		private long srCacheLastLowBucket = long.MinValue;
		private DateTime lastSrRealtimeRecomputeUtc = DateTime.MinValue;
		private int srCacheConfigHash = 0;
		private BarsRequest activeFeedDailyReq;

		// Multi-Timeframe Closed-Candle Signal State (3m Current TF + 9m Higher TF, guarded by signalLock)
		private readonly object signalLock = new object();
		private SignalComputedSnapshot cachedSignalSnapshot = null;
		private readonly Dictionary<int, ChartSignalMarker> lockedClosedBarSignals = new Dictionary<int, ChartSignalMarker>();
		private int lockedSignalsMaxClosedIdx = -1;
		private long lockedSignalsFirstBarTicks = 0L;
		private int sigCacheChartBarCount = -1;
		private int sigCacheLastClosedIdx = -2;
		private int sigCacheMinuteCount = -1;
		private int sigCacheDailyCount = -1;
		private int sigCacheConfigHash = 0;

		// 360-Degree Free Camera State (Smooth Omni-Pan + Vertical/Horizontal Zoom)
		private bool isFree360View = false;
		private double freeViewMinPrice = double.NaN;
		private double freeViewMaxPrice = double.NaN;
		private float freeRightPanOffsetPx = 0f;
		private double lastRenderedMinPrice = double.NaN;
		private double lastRenderedMaxPrice = double.NaN;
		private float lastRenderedPanelY = 0f;
		private float lastRenderedPanelH = 600f;
		private ChartScale lastRenderedChartScale = null;

		private readonly RectangleF[] toolbarBtnRects = new RectangleF[17];
		private bool mouseSubscribed = false;
		private ChartControl hookedOrderflowCC = null;
		private UIElement hookedOwnerChart = null;
		private MouseButtonEventHandler ofMouseLeftDownHandler = null;
		private MouseWheelEventHandler ofMouseWheelHandler = null;
		private MouseWheelEventHandler ofOwnerChartWheelHandler = null;
		private System.Windows.Controls.Primitives.ScrollBar hookedOfScrollBar = null;
		private System.Windows.Forms.Form hookedD2DForm = null;
		private bool isDraggingFootprintCanvas = false;
		private bool isPendingOfCanvasDrag = false;
		private bool isDraggingVerticalScale = false;
		private bool isPendingVerticalScaleDrag = false;
		private bool isApplyingRightMargin = false;
		private float ofDragStartMouseX = 0f;
		private float ofDragStartMouseY = 0f;
		private double ofDragStartScrollVal = 0.0;
		private float ofDragStartRightOffsetPx = 0f;
		private double ofDragStartMinPrice = 0.0;
		private double ofDragStartMaxPrice = 0.0;
		private DateTime lastCanvasClickUtc = DateTime.MinValue;
		private DateTime lastDoubleClickHandledUtc = DateTime.MinValue;
		private float lastCanvasClickX = -9999f;
		private float lastCanvasClickY = -9999f;
		private int focusedCandleBarIndex = -1;
		private int hoveredToolbarBtnIndex = -1;
		private int lastHandledOfMouseTimestamp = -1;
		private long lastWheelHandledTicks = 0;
		private DateTime lastBtnClickUtc = DateTime.MinValue;
		private float lastRenderTargetW;
		private float lastRenderTargetH;
		private SharpDX.Direct2D1.Bitmap cachedLogoBitmap;
		private SharpDX.Direct2D1.RenderTarget cachedLogoRenderTarget;
		private bool cachedLogoIsLight;

		// Interactive Long & Short R:R Position Tool State (Backtest & Live Order Tracking)
		private readonly List<RrPositionBox> rrPositionBoxes = new List<RrPositionBox>();
		private int nextRrBoxId = 1;
		private int pendingRrToolMode = 0; // 0 = None, 1 = Place Long, 2 = Place Short
		private int selectedRrBoxId = -1;
		private int hoveredRrBoxId = -1;
		private RrDragMode activeRrDragMode = RrDragMode.None;
		private RrPositionBox activeDragRrBox = null;
		private float rrDragStartMouseX = 0f;
		private float rrDragStartMouseY = 0f;
		private double rrDragOrigStartSlot = 0.0;
		private double rrDragOrigEndSlot = 0.0;
		private double rrDragOrigEntry = 0.0;
		private double rrDragOrigTarget = 0.0;
		private double rrDragOrigStop = 0.0;
		private float rrPreviewMouseX = -9999f;
		private float rrPreviewMouseY = -9999f;
		private DateTime lastRrMouseDownUtc = DateTime.MinValue;
		private KeyEventHandler ofPreviewKeyDownHandler = null;

		// ==================== Tradesea Order Execution & Live Chart Order Drag State ====================
		private enum LiveOrderDragMode
		{
			None = 0,
			DragWorkingOrder = 1,
			DragNewTpFromPosition = 2,
			DragNewSlFromPosition = 3,
			DragDraftEntry = 4,
			DragDraftTp = 5,
			DragDraftSl = 6,
			DragPendingBracketTp = 7,
			DragPendingBracketSl = 8
		}

		private enum LiveOrderHitKind
		{
			None = 0,
			ClosePositionBtn = 1,
			ReversePositionBtn = 2,
			BreakevenPositionBtn = 3,
			AddTpDragChip = 4,
			AddSlDragChip = 5,
			CancelWorkingOrderBtn = 6,
			WorkingOrderDrag = 7,
			DraftEntryDrag = 8,
			DraftTpDrag = 9,
			DraftSlDrag = 10,
			PendingBracketTpDrag = 11,
			PendingBracketSlDrag = 12,
			CancelPendingBracketTpBtn = 13,
			CancelPendingBracketSlBtn = 14
		}

		private sealed class PendingBracketSpec
		{
			public Order EntryOrder;
			public string EntryTag;
			public Account Account;
			public Instrument Instrument;
			public bool IsLong;
			public int TotalQty;
			public int BracketedQty;
			public TimeInForce Tif;
			public bool HasSl;
			public int SlTicks;
			public bool HasTp;
			public int TpTicks;
			public bool TrailEnabled;
			public int TrailTicks;
			public DateTime CreatedUtc;
		}

		private sealed class ActiveTrailingOrderState
		{
			public Order StopOrder;
			public Account Account;
			public bool IsLong;
			public int TrailTicks;
			public double BestExtremePrice;
			public DateTime LastChangeSentUtc;
		}

		private sealed class LiveChartHitRegion
		{
			public RectangleF Rect;
			public float LineY;
			public float LineX0;
			public float LineX1;
			public bool AllowLineGrab;
			public LiveOrderHitKind Kind;
			public Order TargetOrder;
			public PendingBracketSpec TargetPendingSpec;
			public bool IsStopOrder;
			public double InitialPrice;
		}

		private sealed class DailyRiskDisciplineRecord
		{
			public string AccountAndDayKey;
			public DateTime UnlockLocalTime;
			public bool IsLocked;
			public string LockReason = string.Empty;
			public HashSet<string> FilledEntryOrderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DailyRiskDisciplineRecord> SharedDailyRiskRecords
			= new System.Collections.Concurrent.ConcurrentDictionary<string, DailyRiskDisciplineRecord>(StringComparer.OrdinalIgnoreCase);
		private static bool sharedRiskFileLoaded = false;
		private static readonly object riskPersistLock = new object();

		private bool runtimeShowTradePanel = true;
		private bool isTradeTabScalp = false; // false = Order tab (default like Image 2), true = Scalp tab
		private bool isOrderSideBuy = true;
		private int selectedOrderTypeIdx = 0; // 0 = Market, 1 = Limit, 2 = Stop
		private TimeInForce selectedOrderTif = TimeInForce.Day;
		private int orderQtyValue = 1;
		private double orderCustomEntryPrice = 0.0;
		private bool orderCustomEntrySeeded = false;
		private bool orderSlEnabled = true;
		private bool orderTrailEnabled = false;
		private int orderTrailTicks = 20;
		private int orderSlTicks = 20;
		private bool orderSlUnitIsCurrency = false;
		private bool orderTpEnabled = true;
		private int orderTpTicks = 60;
		private bool orderTpUnitIsCurrency = false;
		private bool orderTpManuallyCustomized = false;

		private Account activeTradeAccount = null;
		private Account subscribedTradeAccount = null;
		private readonly object bracketLock = new object();
		private readonly List<PendingBracketSpec> pendingEntryBrackets = new List<PendingBracketSpec>();
		private readonly List<ActiveTrailingOrderState> activeTrailingOrders = new List<ActiveTrailingOrderState>();
		private readonly Dictionary<string, DateTime> recentOrderChangeUtc = new Dictionary<string, DateTime>(StringComparer.Ordinal);

		private readonly List<LiveChartHitRegion> liveChartHitRegions = new List<LiveChartHitRegion>();
		private LiveOrderDragMode activeLiveOrderDragMode = LiveOrderDragMode.None;
		private Order activeDraggedWorkingOrder = null;
		private PendingBracketSpec activeDraggedPendingSpec = null;
		private bool activeDraggedOrderIsStop = false;
		private double liveOrderDragStartPrice = 0.0;
		private double liveOrderDragCurrentPrice = 0.0;
		private float liveOrderDragStartMouseY = 0f;
		private bool liveOrderDragMoved = false;
		private int hoveredLiveHitRegionIdx = -1;

		// Risk Management & Capital Protector Runtime State
		private DateTime lastEmergencyFlattenUtc = DateTime.MinValue;
		private DateTime lastMissingSlGuardCheckUtc = DateTime.MinValue;
		private DateTime posOpenedObservedUtc = DateTime.MinValue;
		private int cachedTradesDoneToday = 0;
		private double cachedDailyNetPnl = 0.0;
		private bool cachedIsTradingLocked = false;
		private string cachedLockReason = string.Empty;
		private DateTime cachedUnlockLocalTime = DateTime.MinValue;
		private WpfControls.Border riskGuardCardBorder = null;
		private WpfControls.TextBlock txtRiskGuardTitle = null;
		private WpfControls.Border riskGuardTradesPill = null;
		private WpfControls.TextBlock txtRiskGuardTrades = null;
		private WpfControls.TextBlock txtRiskGuardRow1 = null;
		private WpfControls.TextBlock txtRiskGuardRow2 = null;
		private readonly List<WpfControls.Button> scalpEntryButtons = new List<WpfControls.Button>();
		private WpfControls.Button btnQuickRevRef = null;
		private WpfControls.Button btnScalpRevRef = null;

		// Docked WPF Tradesea Right-Side Panel Controls
		private NinjaTrader.Gui.Chart.Chart hookedTradeChartWindow = null;
		private WpfControls.Grid hookedChartMainGrid = null;
		private WpfControls.ColumnDefinition tradePanelColumnDef = null;
		private WpfControls.Border tradePanelRoot = null;
		private WpfControls.TabControl hookedChartTabCtrl = null;
		private System.Windows.Threading.DispatcherTimer tradeUiTimer = null;
		private int lastCountdownTickSec = -1;
		private bool isSyncingTradePanelUi = false;
		private bool tradePanelAttachQueued = false;
		private int uiTradeSyncQueued = 0;
		private Position cachedLastKnownPosition = null;
		private List<Order> cachedLastKnownWorkingOrders = new List<Order>();
		private string cachedPositionEntryKey = string.Empty;
		private DateTime cachedPositionEntryTime = DateTime.MinValue;
		private List<DayProfile> lastMpRenderSessionsSnapshot = new List<DayProfile>();

		private WpfControls.ComboBox cmbTradeAccount = null;
		private WpfControls.TextBlock txtBalVal = null;
		private WpfControls.TextBlock txtRplVal = null;
		private WpfControls.TextBlock txtUplVal = null;
		private WpfControls.Border tabBtnScalp = null;
		private WpfControls.Border tabBtnOrder = null;
		private WpfControls.TextBlock tabTxtScalp = null;
		private WpfControls.TextBlock tabTxtOrder = null;
		private WpfControls.Border tabLineScalp = null;
		private WpfControls.Border tabLineOrder = null;
		private WpfControls.StackPanel viewOrderTab = null;
		private WpfControls.StackPanel viewScalpTab = null;
		private WpfControls.TextBlock txtSymbolHeader = null;
		private WpfControls.Button btnTifToggle = null;
		private WpfControls.Border cardBuySide = null;
		private WpfControls.Border cardSellSide = null;
		private WpfControls.TextBlock txtBuyBidVal = null;
		private WpfControls.TextBlock txtSellAskVal = null;
		private WpfControls.Border pillTypeMkt = null;
		private WpfControls.Border pillTypeLmt = null;
		private WpfControls.Border pillTypeStp = null;
		private WpfControls.TextBlock txtTypeMkt = null;
		private WpfControls.TextBlock txtTypeLmt = null;
		private WpfControls.TextBlock txtTypeStp = null;
		private WpfControls.TextBlock lblEntryPriceTitle = null;
		private WpfControls.TextBlock txtMarketPriceBig = null;
		private WpfControls.Grid grpCustomEntryInput = null;
		private WpfControls.TextBox txtCustomEntryPrice = null;
		private WpfControls.TextBox txtOrderQty = null;
		private WpfControls.CheckBox chkSlEnable = null;
		private WpfControls.CheckBox chkTrailEnable = null;
		private WpfControls.TextBox txtTrailTicks = null;
		private WpfControls.TextBox txtSlPrice = null;
		private WpfControls.TextBox txtSlOffset = null;
		private WpfControls.Button btnSlUnit = null;
		private WpfControls.CheckBox chkTpEnable = null;
		private WpfControls.TextBlock txtOrderRrSummary = null;
		private WpfControls.TextBox txtTpPrice = null;
		private WpfControls.TextBox txtTpOffset = null;
		private WpfControls.Button btnTpUnit = null;
		private WpfControls.Border livePosBanner = null;
		private WpfControls.TextBlock txtLivePosBanner = null;
		private WpfControls.TextBlock txtLivePosPnl = null;
		private WpfControls.Button btnOrderConfirm = null;
		private WpfControls.TextBlock txtTradeStatusToast = null;

		// Scalp Tab Controls
		private WpfControls.TextBox txtScalpQty = null;
		private WpfControls.CheckBox chkScalpAutoBracket = null;
		private WpfControls.TextBlock txtScalpBracketInfo = null;
		private WpfControls.TextBlock txtScalpBuyBidBtnSub = null;
		private WpfControls.TextBlock txtScalpSellAskBtnSub = null;
		private WpfControls.StackPanel pnlScalpWorkingOrders = null;

		private const string DefaultLogoBase64 = "iVBORw0KGgoAAAANSUhEUgAAAGAAAABgCAYAAADimHc4AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAABAdSURBVHhe7Z0JcBVFGscTIJyCcsp9yQ2CgCJXRCEQ7jvcAhGBFURr3S1FsFhrBURWjlCKyHIEEVAgooC1coi4HMt9BJAj4b6SAIEIIYeBb79/p6ed914DeS/vzbwA/6pfVWoy09/X37zp6ekzwI9VgWnDvMlEMKuY7cxvzDkmgUmU4G8cw/9wDs7FNbgWaSCtx3qAqjCvMvOZaOY2Q14CaSHNeQxsVGYei1WN+RvzK5PK6ILnC1IY2HyHgQ+PlIKYnswaJo3RBciBQoUKUeXKlalJkybUqVMnGjhgAI0cOZLGjBkjwN8D+FjHjh3FOTi3UMGC2rQ0wAf4Ap/yMA+tCjNjGJTTukAI8ubNS40aNRJBnTt3Lm3fvp0uXrxIaWlplFXhXFyzbds2mvvllzRyxAiRJtLW2TRxhIGPTzAPjfIyyNRpRpdpKl68OIWFhdGiRYvo1MmTMoze10lOGzZ69+5NxYoV0/oiga94gcP3HK0ezCFGl0kKCQkRAbly5YoMkXW6kpBAkZGRFNKmjdY3CV7c3Zkcp0rMCsYlUwW5bB7BRcLevXtlKOzX3j17aPjw4VSgQAEXfyXLGeQpR2goc41xyERQUBCNHj2aYmNjZbb9TzExMTRq1Cjhq7P/zFVmCOO3wotrEePifI8ePejQoUMym/6v6Oho6t69u0s+JJGM372k6zIHGQdnq1SpQlFRUTJbOU8rVqwQVVrnfDEHmNqMX6g9c51xcDI8PJwSExNlVnKurl27RkOHDnXImwRNIKGMrUKZeJdRjuGjCbWLh00LFy4UFQhzXpk7DJo2bNFoxsGhGjVq0P79+6XLD59Qc6tevbpDniVvMJbKJfitWrWiBK5bP+xKiI+nl4KDHfIuGcVYIhQ7Dsa7detGqamp0sWHX8hr165dHWIg8XlxhJeOQ5mPz/q7d+9K1x4d3blzh3r16mUOPkBs2jI+US0miVEG8St4FINvCDehS5cu5hsAbjA1Ga+qEHOUUYZatGjhVgvlwyoUR82bNzffAIBWVcTMa1rKKANVq1a1pQHNX4XKh+aD7WvGK0Lbjko4f/78dODAAWn6sQyh+p0vXz7zDQCDmWwJndm/MypRfJA8ll7z5883Bx/gnVmO8VjoqlMJotvvse6v/v37m28A+IHxSN0YlVCZMmXo+vXr0sxj3Uto/ypdurT5BoAujFvKx8QyKpGVK1dKE4/1IC1fvtwcfHCCcat7821GJRAaGiqTtk9pXN1bv24dfbNsGS1csID+MWECLeAyd8nXS+j8ufPyLP9R27ZtzTcAoI85S0L99TIjLsydOzcdPnxYJmuN8IGTcjuZUpJv0aLISBo/bhw1btTIOUOKatWqUXzcZXm1fwgdULly5TL7eYnJ0reBw68/fOhQmaQ1Wrv6R2rcsDFVrVKF6tSuac4ABQYGih+EGSOT9erWpeiDB2Uq/qEhgwc7+M9gdMh9hUFTavgI+kYxlMMq/fD9KpexOwiwc9B14FwMb8E4IH9RTEysc//ySQYxvqd6MeqCgQMHyqR8r6VLlim7ugDfD9wk8+PuTzdBUy3FUJ17agOjTt69e7dMxvdq3uwlYVMX4Kxgvgn4Ih0/frxM2V7t2rVLxVOyjtHqGeYPRpzYtGlTmYTvFbXyO7YZyAF0LePdxfwkDBn8Kv2elCSt2CeMVzV8YhBjjAR30XuMOhHjMq1S/36DhE1dQN3FeBLwwkaaYb17UXq6va22c+bMUXGV/J1x0U5GnICO9fj4eHm57/XhhH8KuwicLqjuYtwEIz8b16+TluxRXFycc4f+/xgHYcJCOiNOwDBvKxV7IoaKFc0cLKsLqKcYT8F7775Lt2/dlNbsUfv27Y3gA8S6IqMUzqgT8MhYrX/Pmcu/2syg6YLpKchPvXr16GTMCWnJHn0xe7aKr8RhmKMaUohH9/jx4/Iy65R883fq1KGT8kEXTE8wnoIvZn8uLdmjo0ePUqCpWGQWMkK5GNXdWLNmTdEUYIe2b9lGpUqWEn7ogukpSK9du7aUePWqtGS9EFOnMUWYqBLIiLJITRUaNGiQvMR6xV+Oo+LFSgg/dIH0FKRXsWJF2r51C5GNgwjQn2LEmUHMyzMBIfKAYPr06fJ0a/Xr5s0U2q6d8CF3bu8VQQBpoo3+22VLKd3G8UuffvqpirOkNePY+LZhwwZ5urXq2b2bsO/N8t8A6ZYtU5YWzptHKcnJ0qL1Wr9+vYqzRDTOfWYcyMPOWtn4ZtbnEbOED96uBQGkix69RQsXiGZuuxQbE6P8kcxiRJ+lOFCqVCm6edOe+vKeHbsoV2BmLcE5gNkFaVatWiXzBtj4BCQlJVHJkiWN4IPvmYAdxoE6derIU61XWkoqNW/STPihC6KnGF/EjRs1pMgF8229ARhFWLt2bSP4AMsqBBwzDrRs2VKeao9at2ol/NAF0lNwA0BIm9b0w6rv6I/0dGnNHrVwHEWH6n/ABeMAZqHbpagVUcIHb78DkB46RXp170E7tm+ztRoKdejQwXwDsMBIwBXjQN++feVp1irpxg2q/kzmR4ouiNkBaRYsWIiGhYfT/r17pEX7hAnpRryZeEbMdRIH7Bp4tW3LVuWULojZAWlWqFCR/vr2W3T6pP1TZPv166fyymA6759zeu26ASkpKTRj2jSxwIa3iiDj5Vu0aFExoyVy/ny0B0iL9kl3A7DYkTjQp08feZr1On/mDJUoXlz4oQuoOyD4QXmCRLUaL73PZs2iqOXLqUunztT65Vdo0sSJ0qr1woQWI95MHCNeBOIA2qztUho/BTVkY5UuqO6ANIrxL79zp440iJ/q8e+/L4atGPkEh6OjpWVrhUFuJj/OMH8uH9OsWTN5mvVCa2FwixbCD11Q3QFPAIbRly1bxmHIOP5nNE9/MuVjun3rlrRundDXbvjDHGYCthoH0BRt55Sj/27eLKqM3ngPGO8A57SM4+FDhtDF8+ekZWukaZLewgREGQcwsOkGVwnt0v69e5Vz5qC5C4KsOw6MG4CiKcbijieMnEbRaOSRwYoyATOMA3Du2LFj8nTrFXfpEj1ZuIjZQQd0AXUX4wYM7N+fjh89Ki1bI9ErJotAyTQmYKTpAK1du1aebo8i582nyhUrUcECBcToDNSMKleqpH45uqC6A9LA8McRw4dT7Alrn4A1q1erOEuGMwHBpgM0efJkebo9wuiFrxdF0qtcRHTr0oV69+wpvmL7yOpbdt8PSKN06adp8sSPKP6ytSOqJ3L11xxrpiUT8DSj1ujE5GM7lZGRQVcS4mnfnt30y88baQu/mA/u30exXF4Pey1c+Hi/Mv5B4Nq8eYOof58wMfzdSmHdJCPOzC2mFCO0nxH/QN9pus0thkpONbKIGdMoNwcwu0+BUQ6/8ZcRMmXfC3OqK1SoYL4B+xil2Yz6p7+teLL5l03UoH594VsuTUA9wbgJEz6wZhDvvn37VHwl6IlUCmPUP9F57C9au2a1+NXDr+wUPTqMmzDqjZGU7OORc1OnfqLiK8FUACW8B5IZ8c/g4GB5mb2K45ekt9qH7oVxExrUf5Z27dwhLXtfWNrBiC/jUP4b2siIE/A1evr0aXmpferapbPwRxc4bwM75cqWoYT4OGnde8JAB6eZMusZFzl8D0ydOlVebo9mTMscQ+PtYudeGB9oUyajpdS7zTFTpkxRcZWI+r+zSjOqGELnsV3tQmipDMqTR/ihC5avgL2aNWrQ2dOnpCfZF9p/atWqZQQeIMYo8rXCCrHqZAwkskPdu2auwaMLki8x3gefRcyUnmRf69b9pOIp+Ya5p15h1MntbZigjQ+wwEDfDNDKCsh3/359KfGqd5bjaSeHW5p4mbmvsCipumDPHms7socPGybs6oJjBbDdPrQdHTqY/SV5drtO0HP4+LqXBjDqIitny9y4nkglS3h/dHRWMV7EbUPa0G4vVEmdZsWAfswDlZtRg7XA5s2bZZK+1aaNG4U9u4of4wagK/PAvuyt7r5p0yYVPwkGYSG2WVJfRl3csGFDSwY0zZo5U9jTBccKjBuAmZW/ZWOxcdQen3vuORU/CVob3JKaNQkiIiJk8r7TsNdeE7Z0wbECPHkA3ZXZ6S2bMWOGipsE42/dVlNGJYLOEV9/HTd98UVhSxccK4BtfLG+9eabdO6MZ3nFFiyaNaZfZDzSHEYlhOWJfaXbyclUvlw5YUcXHCuA7SKFC4sWUk87a9COZo4Zg5Zmj4Wdj9S4IfDhhx9KU94V1vt56sknhQ1dcKwAtsuVLUszp0+npBvuL882YcIEFSfJWQYxzJYc5pABX/QbX7p4Ufz6kL4uOFYA289UrUpLFn9FqSkp0rOsac2aNQ4xkog5YN7Qx4xKuEiRInTkyBFp2ju6eiWBSpTwbdPzg4BtNH9HrVhOGX9kvVcQK4oZPx4TkxivSjVXA2xPctmLndrXExPp6VLenyPsDrANH5YuXkxpqVl7Ai5duqRbNRfL/nhdxZlTjDKEuq63tilBEVT4iSdEurrgWAFsVyhfPnMqUxYm82F7kwYNGqh4SLAqVjHGJ8JmPVgdXBl84YUXhCPZ1a6dO0V6dn0FA9ivW6cOrfj2G0pPu/984qtXr1Ljxo1VHCTYS6cO41O9xDjsfIon4cKFC9I1zzTni8zFLKzqgNEB+89zUDf89BPduZMhPXPV+fPnqb4cJGACO7RijJUl6sCo5W0A3gnZWdR70MDMafy6wFgF7Ae3bEnbt2yRXrkKI0Y0ZT5igd2kLBUMOmy6jNqRJ/uG3cnIoNqy10gXGCsw2oE6hIbSnl07pWeOwr5ihV1rO4iBbdtZ4ZFTk/wMxo0b51Z35rlzZ6lAgfziWl1wrMC4AX3DwuiI0+QN5GXs2LEOeZRgdlELxlZhRznsGOHgHD7JsSVgVrRhQ+YaCna+gGEbPmAc6inTfpcHDx4U86fNeZNgcgW2dfELFWUw7d7BScxSmTRp0gO3OomYmdl6qAuMlcCHvn3CKJWroFjoD4NpkQdzniTfMU8xfqdxjMMuSwA1hlWrVslwuwq7rOI8XVCsBD404Wo1amTGUEgnsHPe+4xfC0OuXTb2BNjAWTfSIiQkc1NlXVCsxhgOowF95baX91kV9h/4iNFu2o9mbSxDb2z+hgkYOK4LiNUYL2MT+Ob5J4M85Tg9y7i8Gwyw7Pzw118XO1nb+QLWIX1cxSAPOV7tmE2Mww0wsPPrVwf7gzUc/L6s90S4EasZ9aL2p+CzL9f5ScRG/l8xHRmvbsbmT6rP/Is5bfcNYPvpHHQs2YOOc0zZxTJi2C22IZMjy313VICD0JuD8AuT4hwcX8G20ph4+WvHcvJYuBadThi1jFoc9v566INvFqp3M5mNHJSjHJxrTIYueJ7Aad1hkjhtrM2wl/kPs5jBPN13mO5MA6YEk4d5pITRYhiu/TzTlcGv8AMmglnCQfuR2crs5yAeY04xZ5kLzEUT5xj87zifG83sZH7mNFALQ7A/Z/ArH8u8znRmGjH4tRdkbFBAwP8BZB1ntMFNynoAAAAASUVORK5CYII=";

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

		private MarketProfilePeriodMode EffectiveProfileMode
		{
			get { return ProfileMode; }
		}

		public override string DisplayName
		{
			get { return string.Empty; }
		}
		#endregion

		#region Properties
		// ==================== 0. Data Feed & Mode ====================
		[NinjaScriptProperty]
		[Display(Name = "Data Feed", Description = "Choose Accelpix Feed (NSE/BSE/MCX) or NinjaTrader Feed (CME/Forex/Global)", Order = 0, GroupName = "0. Data Feed & Mode")]
		public AK10XOrderflowDataFeed DataFeed { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Orderflow Display Mode", Description = "VolumeFootprint, Imbalance, Delta, or TotalVolume", Order = 1, GroupName = "0. Data Feed & Mode")]
		public AK10XOrderflowMode FootprintMode { get; set; }

		[NinjaScriptProperty]
		[Range(20, 80)]
		[Display(Name = "Candlestick Zoom Threshold (px)", Description = "Below this bar width (zoom out), bars render as Buyer/Seller Candlesticks; above it (zoom in), they expand into Inside-Box Orderflow", Order = 2, GroupName = "0. Data Feed & Mode")]
		public int CandlestickZoomThresholdPx { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "White Theme", Description = "Enable crisp white background theme (or toggle anytime from the top bar)", Order = 3, GroupName = "0. Data Feed & Mode")]
		public bool UseLightTheme { get; set; }

		// ==================== 1. Footprint Core ====================
		[NinjaScriptProperty]
		[Range(1, 500)]
		[Display(Name = "Ticks Per Level", Description = "Manual price grouping in ticks when Auto-Scale is off", Order = 1, GroupName = "1. Footprint Core")]
		public int TicksPerLevel { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Auto-Scale Price Step", Description = "Automatically scales Orderflow price step to visible chart height", Order = 2, GroupName = "1. Footprint Core")]
		public bool AutoScalePriceStep { get; set; }

		[NinjaScriptProperty]
		[Range(40, 220)]
		[Display(Name = "Default Footprint Width (px)", Description = "Initial bar spacing in Orderflow view (default = 96px)", Order = 3, GroupName = "1. Footprint Core")]
		public int BarSpacePx { get; set; }

		// ==================== 2. Bar POC, Halfpack & Imbalances ====================
		[NinjaScriptProperty]
		[Display(Name = "Show Bar POC", Description = "Highlight highest-volume price level (Point of Control) on each Footprint bar", Order = 1, GroupName = "2. Bar POC, Halfpack & Imbalances")]
		public bool ShowBarPOC { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Halfpack", Description = "Show 50% Halfpack line across every candle in Footprint and Imbalance modes (Enabled by default)", Order = 2, GroupName = "2. Bar POC, Halfpack & Imbalances")]
		public bool ShowHalfpack { get; set; }

		[Display(Name = "Bar Value Area %", Description = "Bar Value Area percentage (default 70%)", Order = 3, GroupName = "2. Bar POC, Halfpack & Imbalances")]
		[Range(50, 95)]
		public int BarVAPercentage { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show Diagonal Imbalances", Description = "Highlight aggressive Buyer (Ask) and Seller (Bid) diagonal imbalances", Order = 4, GroupName = "2. Bar POC, Halfpack & Imbalances")]
		public bool ShowImbalance { get; set; }

		[NinjaScriptProperty]
		[Range(150, 1000)]
		[Display(Name = "Imbalance Ratio %", Description = "Diagonal imbalance ratio percentage (default = 300%)", Order = 5, GroupName = "2. Bar POC, Halfpack & Imbalances")]
		public double ImbalancePercentage { get; set; }

		[NinjaScriptProperty]
		[Range(1, 10000)]
		[Display(Name = "Imbalance Min Volume Filter", Description = "Minimum level volume required to trigger an imbalance", Order = 6, GroupName = "2. Bar POC, Halfpack & Imbalances")]
		public int ImbalanceFilter { get; set; }

		[Display(Name = "Show Stacked Imbalance Zones", Description = "Extend Support/Resistance zones from consecutive stacked bar imbalances", Order = 7, GroupName = "2. Bar POC, Halfpack & Imbalances")]
		public bool ShowImbalanceSRZones { get; set; }

		[Range(2, 6)]
		[Display(Name = "Consecutive Imbalance Levels", Description = "Minimum consecutive imbalance rows to form a Stacked Imbalance zone (default = 3)", Order = 8, GroupName = "2. Bar POC, Halfpack & Imbalances")]
		public int ImbalanceSRConsecutiveLevels { get; set; }

		// ==================== 2B. Session-Wise Orderflow & Imbalance (Asian / London / New York) ====================
		[Display(Name = "Enable Session-Wise Orderflow (Master)", Description = "Apply session-specific Tick per Level, Imbalance %, and Minimum Volume per Price Level automatically for Asian, London, and New York sessions", Order = 0, GroupName = "2B. Session-Wise Orderflow (Asian / London / NY)")]
		public bool UseSessionWiseOrderflow { get; set; }

		[Range(1, 500)]
		[Display(Name = "Asian — Tick per Level", Description = "Ticks per price level during the Asian session (default = 2)", Order = 1, GroupName = "2B. Session-Wise Orderflow (Asian / London / NY)")]
		public int AsiaTicksPerLevel { get; set; }

		[Range(101, 2000)]
		[Display(Name = "Asian — Imbalance (%)", Description = "Diagonal imbalance ratio % during the Asian session (default = 300%)", Order = 2, GroupName = "2B. Session-Wise Orderflow (Asian / London / NY)")]
		public double AsiaImbalancePercentage { get; set; }

		[Range(0, 100000)]
		[Display(Name = "Asian — Min Volume per Price Level", Description = "Minimum volume per price level required to trigger an imbalance during the Asian session (default = 5)", Order = 3, GroupName = "2B. Session-Wise Orderflow (Asian / London / NY)")]
		public int AsiaMinVolumePerLevel { get; set; }

		[Range(1, 500)]
		[Display(Name = "London — Tick per Level", Description = "Ticks per price level during the London session (default = 1)", Order = 4, GroupName = "2B. Session-Wise Orderflow (Asian / London / NY)")]
		public int LondonTicksPerLevel { get; set; }

		[Range(101, 2000)]
		[Display(Name = "London — Imbalance (%)", Description = "Diagonal imbalance ratio % during the London session (default = 300%)", Order = 5, GroupName = "2B. Session-Wise Orderflow (Asian / London / NY)")]
		public double LondonImbalancePercentage { get; set; }

		[Range(0, 100000)]
		[Display(Name = "London — Min Volume per Price Level", Description = "Minimum volume per price level required to trigger an imbalance during the London session (default = 8)", Order = 6, GroupName = "2B. Session-Wise Orderflow (Asian / London / NY)")]
		public int LondonMinVolumePerLevel { get; set; }

		[Range(1, 500)]
		[Display(Name = "New York — Tick per Level", Description = "Ticks per price level during the New York session (default = 1)", Order = 7, GroupName = "2B. Session-Wise Orderflow (Asian / London / NY)")]
		public int NewYorkTicksPerLevel { get; set; }

		[Range(101, 2000)]
		[Display(Name = "New York — Imbalance (%)", Description = "Diagonal imbalance ratio % during the New York session (default = 400%)", Order = 8, GroupName = "2B. Session-Wise Orderflow (Asian / London / NY)")]
		public double NewYorkImbalancePercentage { get; set; }

		[Range(0, 100000)]
		[Display(Name = "New York — Min Volume per Price Level", Description = "Minimum volume per price level required to trigger an imbalance during the New York session (default = 15)", Order = 9, GroupName = "2B. Session-Wise Orderflow (Asian / London / NY)")]
		public int NewYorkMinVolumePerLevel { get; set; }

		// ==================== 3. Top/Bottom Badges & 3-Row Table ====================
		[NinjaScriptProperty]
		[Display(Name = "Show Box Top Delta & Bottom Height", Description = "Show Delta pill above each candle box and Candle Height (High - Low) pill below each box", Order = 1, GroupName = "3. Top/Bottom Badges & 3-Row Table")]
		public bool ShowBarDelta { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Show 3-Row Bottom Table", Description = "Show 3-row bottom table: Momentum (BMC/SMC), Strength (Buyer/Seller), and Delta", Order = 2, GroupName = "3. Top/Bottom Badges & 3-Row Table")]
		public bool ShowStatisticGrid { get; set; }

		[Range(5, 45)]
		[Display(Name = "Momentum Small Wick Max %", Description = "Maximum top wick (BMC) or bottom wick (SMC) % of candle height for Momentum classification (default = 25%)", Order = 3, GroupName = "3. Top/Bottom Badges & 3-Row Table")]
		public int MomentumMaxWickPct { get; set; }

		// ==================== 4. Market Profile Zones & Sessions (IST) ====================
		[NinjaScriptProperty]
		[Display(Name = "Enable Market Profile Zones (Master)", Description = "Show Market Profile Value Areas, Single Prints, POCs, Virgin Lines, and Right-Side Bar Values on the Orderflow chart", Order = 0, GroupName = "4. Market Profile Zones & Sessions")]
		public bool EnableMarketProfileZones { get; set; }

		[Display(Name = "Show Session Header & Date Footer", Description = "Show Fixed Top Session Header Strip (Asia / London / New York + TPO counts) and Bottom Trading Date Footer Strip", Order = 1, GroupName = "4. Market Profile Zones & Sessions")]
		public bool ShowSessionHeaderFooter { get; set; }

		[Browsable(false)]
		public bool ShowChartButtons { get; set; }

		[Display(Name = "Show Right-Side Bar Values", Description = "Display de-overlapped Market Profile Virgin Level & Zone labels in the Right-Side Values column", Order = 3, GroupName = "4. Market Profile Zones & Sessions")]
		public bool ShowRightSideValues { get; set; }

		[Display(Name = "Show Session Value Area Shaded Zone", Description = "Shade each session's 70% Value Area box (VAH to VAL) behind the Orderflow candles", Order = 4, GroupName = "4. Market Profile Zones & Sessions")]
		public bool ShowValueAreaZoneFill { get; set; }

		[Display(Name = "Show Session Base Lines", Description = "Draw color-coded vertical Session Base Lines across the Session Header, Main Chart, and 3-Row Table", Order = 5, GroupName = "4. Market Profile Zones & Sessions")]
		public bool ShowSessionDividers { get; set; }

		[NinjaScriptProperty]
		[Range(0, 1000)]
		[Display(Name = "TPO size (0 = auto)", Description = "Market Profile TPO row size (0 = automatic real-time calculation matching AK10XGlobalMarketProfile)", Order = 6, GroupName = "4. Market Profile Zones & Sessions")]
		public double TpoSize { get; set; }

		[NinjaScriptProperty]
		[Range(1, 240)]
		[Display(Name = "TPO interval (minutes)", Description = "TPO bracket duration in minutes (default = 30 min)", Order = 7, GroupName = "4. Market Profile Zones & Sessions")]
		public int TpoIntervalMinutes { get; set; }

		[NinjaScriptProperty]
		[Range(50, 90)]
		[Display(Name = "Value area (%)", Description = "Market Profile Value Area percentage (default = 70%)", Order = 8, GroupName = "4. Market Profile Zones & Sessions")]
		public int ValueAreaPercent { get; set; }

		[Range(1, 240)]
		[Display(Name = "Initial balance (minutes)", Description = "Initial Balance duration in minutes (default = 60 min)", Order = 9, GroupName = "4. Market Profile Zones & Sessions")]
		public int InitialBalanceMinutes { get; set; }

		[Range(1, 365)]
		[Display(Name = "Days to calculate", Description = "Historical days for Market Profile Virgin Zones & Weekly levels (default = 45)", Order = 10, GroupName = "4. Market Profile Zones & Sessions")]
		public int DaysToDisplay { get; set; }

		[Display(Name = "Profile Mode (Daily / Weekly / Monthly)", Description = "Select Daily (default), Weekly, or Monthly Market Profile zones (also switchable via top-right PROFILE dropdown)", Order = 11, GroupName = "4. Market Profile Zones & Sessions")]
		public MarketProfilePeriodMode ProfileMode { get; set; }

		[Display(Name = "Show Asia (20 TPO)", GroupName = "5. Forex & India Sessions (IST)", Order = 0)]
		public bool ShowAsia { get; set; }

		[Display(Name = "Asia From Time (IST)", Description = "Default: 03:30 AM", GroupName = "5. Forex & India Sessions (IST)", Order = 1)]
		public string AsiaFromTime { get; set; }

		[Display(Name = "Asia To Time (IST)", Description = "Default: 01:30 PM", GroupName = "5. Forex & India Sessions (IST)", Order = 2)]
		public string AsiaToTime { get; set; }

		[Display(Name = "Show London (11 TPO)", GroupName = "5. Forex & India Sessions (IST)", Order = 3)]
		public bool ShowLondon { get; set; }

		[Display(Name = "London From Time (IST)", Description = "Default: 01:30 PM", GroupName = "5. Forex & India Sessions (IST)", Order = 4)]
		public string LondonFromTime { get; set; }

		[Display(Name = "London To Time (IST)", Description = "Default: 07:00 PM", GroupName = "5. Forex & India Sessions (IST)", Order = 5)]
		public string LondonToTime { get; set; }

		[Display(Name = "Show New York (15 TPO)", GroupName = "5. Forex & India Sessions (IST)", Order = 6)]
		public bool ShowNewYork { get; set; }

		[Display(Name = "New York From Time (IST)", Description = "Default: 07:00 PM", GroupName = "5. Forex & India Sessions (IST)", Order = 7)]
		public string NewYorkFromTime { get; set; }

		[Display(Name = "New York To Time (IST)", Description = "Default: 02:30 AM (Next Day)", GroupName = "5. Forex & India Sessions (IST)", Order = 8)]
		public string NewYorkToTime { get; set; }

		[Display(Name = "Show India cash session", GroupName = "5. Forex & India Sessions (IST)", Order = 9)]
		public bool ShowIndianSession { get; set; }

		[Range(0, 235959)]
		[Display(Name = "India open (HHmmss IST)", GroupName = "5. Forex & India Sessions (IST)", Order = 10)]
		public int MarketOpen { get; set; }

		[Range(0, 235959)]
		[Display(Name = "India close (HHmmss IST)", GroupName = "5. Forex & India Sessions (IST)", Order = 11)]
		public int MarketClose { get; set; }

		[Display(Name = "India session label", GroupName = "5. Forex & India Sessions (IST)", Order = 12)]
		public string SessionLabel { get; set; }

		[Display(Name = "Merge Asia + London", Description = "Merge Asia and London sessions of the same day into one composite profile", GroupName = "5. Forex & India Sessions (IST)", Order = 13)]
		public bool MergeAsiaAndLondon { get; set; }

		[Display(Name = "Merge London + New York", Description = "Merge London and New York sessions of the same day into one composite profile", GroupName = "5. Forex & India Sessions (IST)", Order = 14)]
		public bool MergeLondonAndNewYork { get; set; }

		[Display(Name = "Merge Full Day (Asia + London + NY)", Description = "Merge all 3 sessions of each date into a single composite daily profile", GroupName = "5. Forex & India Sessions (IST)", Order = 15)]
		public bool MergeAllDaySessions { get; set; }

		// ==================== 6. Virgin Lines, Single Prints & Levels ====================
		[Display(Name = "Virginity Break Rule", Description = "Select Virginity Break Rule for all Virgin Lines & Single Prints: CrossBased (Default — breaks immediately when price crosses/touches the line) or CloseBased (breaks only when a closed TPO closes above/below the line).", GroupName = "6. Virgin Lines & Levels", Order = 0)]
		public VirginityBreakRuleMode VirginityBreakRule { get; set; }

		[NinjaScriptProperty]
		[Display(Name = "Enable Single Print Zones (Buying & Selling SP)", Description = "Show shaded Buying & Selling Single Print zones and SL trigger lines until broken", GroupName = "6. Virgin Lines & Levels", Order = 1)]
		public bool ShowSinglePrints { get; set; }

		[Display(Name = "Enable Poor High/Low (Master)", GroupName = "6. Virgin Lines & Levels", Order = 2)]
		public bool ShowPoorHighLow { get; set; }

		[Display(Name = "Enable Virgin PoorHigh / ABPoorHigh", Description = "Show and extend PoorHigh and ABPoorHigh on closed sessions until broken", GroupName = "6. Virgin Lines & Levels", Order = 3)]
		public bool ShowPoorHigh { get; set; }

		[Display(Name = "Enable Virgin PoorLow / ABPoorLow", Description = "Show and extend PoorLow and ABPoorLow on closed sessions until broken", GroupName = "6. Virgin Lines & Levels", Order = 4)]
		public bool ShowPoorLow { get; set; }

		[Display(Name = "Enable Virgin DayHigh", Description = "Show and extend Virgin DayHigh on closed days/sessions only (never on running day or session) until broken", GroupName = "6. Virgin Lines & Levels", Order = 5)]
		public bool ShowSwingHigh { get; set; }

		[Display(Name = "Enable Virgin DayLow", Description = "Show and extend Virgin DayLow on closed days/sessions only (never on running day or session) until broken", GroupName = "6. Virgin Lines & Levels", Order = 6)]
		public bool ShowSwingLow { get; set; }

		[Display(Name = "Enable Virgin WeeklyHigh", Description = "Show and extend Virgin Weekly High (closed Monday to Friday week only) until broken", GroupName = "6. Virgin Lines & Levels", Order = 7)]
		public bool ShowWeeklyHigh { get; set; }

		[Display(Name = "Enable Virgin WeeklyLow", Description = "Show and extend Virgin Weekly Low (closed Monday to Friday week only) until broken", GroupName = "6. Virgin Lines & Levels", Order = 8)]
		public bool ShowWeeklyLow { get; set; }

		[Display(Name = "Enable Virgin ValueAreaHigh (with Session)", Description = "Show ValueAreaHigh and extend until broken", GroupName = "6. Virgin Lines & Levels", Order = 9)]
		public bool ShowVirginVAH { get; set; }

		[Display(Name = "Enable Virgin ValueAreaLow (with Session)", Description = "Show ValueAreaLow and extend until broken", GroupName = "6. Virgin Lines & Levels", Order = 10)]
		public bool ShowVirginVAL { get; set; }

		[Display(Name = "Enable Virgin VPoC", Description = "Show Volume PoC (VPoC) and extend until broken", GroupName = "6. Virgin Lines & Levels", Order = 11)]
		public bool ShowVirginVPoC { get; set; }

		[Display(Name = "Enable Virgin VPPoC (TPO PoC)", Description = "Show TPO PoC (VPPoC) and extend until broken", GroupName = "6. Virgin Lines & Levels", Order = 12)]
		public bool ShowVirginVPPoC { get; set; }

		[Display(Name = "Enable Virgin MidLine", Description = "Show Session MidLine on completed sessions and extend Virgin MidLine until broken", GroupName = "6. Virgin Lines & Levels", Order = 13)]
		public bool ShowVirginMidLine { get; set; }

		// ==================== 7. Running Session Levels ====================
		[Display(Name = "Enable Running Session Levels (Master)", Description = "Master switch to enable or disable ValueAreaHigh, ValueAreaLow, POC (VPoC), and PPoC (VPPoC) on the currently running session", GroupName = "7. Running Session Levels", Order = 0)]
		public bool EnableRunningSessionLevels { get; set; }

		[Display(Name = "Enable Running Session ValueAreaHigh", Description = "Show ValueAreaHigh line and right-edge label on the currently running session", GroupName = "7. Running Session Levels", Order = 1)]
		public bool ShowRunningSessionVAH { get; set; }

		[Display(Name = "Enable Running Session ValueAreaLow", Description = "Show ValueAreaLow line and right-edge label on the currently running session", GroupName = "7. Running Session Levels", Order = 2)]
		public bool ShowRunningSessionVAL { get; set; }

		[Display(Name = "Enable Running Session POC (VPoC)", Description = "Show Volume PoC (POC) line and right-edge label on the currently running session", GroupName = "7. Running Session Levels", Order = 3)]
		public bool ShowRunningSessionPOC { get; set; }

		[Display(Name = "Enable Running Session PPoC (VPPoC)", Description = "Show TPO PoC (PPoC) line and right-edge label on the currently running session", GroupName = "7. Running Session Levels", Order = 4)]
		public bool ShowRunningSessionPPOC { get; set; }

		// ==================== 8. S/R Zones (Minor TF, Major TF×TF, Flip S/R, Day / Week / Month) ====================
		[Display(Name = "Enable S/R Lines (Master)", Description = "Show Multi-Timeframe Minor S/R (Current TF), Major S/R (Current TF × Current TF), Flip S/R, and Day / Week / Month High & Low lines (also toggleable via top S/R Zones button)", GroupName = "8. S/R Zones (Minor / Major / D-W-M)", Order = 0)]
		public bool EnableSrZones { get; set; }

		[Display(Name = "Show Minor S/R Lines (Current TF)", Description = "Show Support & Resistance lines from confirmed swing pivots on the current chart timeframe until validation finishes", GroupName = "8. S/R Zones (Minor / Major / D-W-M)", Order = 1)]
		public bool ShowMinorSrZones { get; set; }

		[Display(Name = "Show Major S/R Lines (TF × TF)", Description = "Show Major Support & Resistance lines from Current TF × Current TF until validation finishes", GroupName = "8. S/R Zones (Minor / Major / D-W-M)", Order = 2)]
		public bool ShowMajorSrZones { get; set; }

		[Display(Name = "Show Major Flip Support & Resistance", Description = "Show role-reversal Flip Support & Flip Resistance lines ONLY for Major S/R (never Minor)", GroupName = "8. S/R Zones (Minor / Major / D-W-M)", Order = 3)]
		public bool ShowSrFlipLines { get; set; }

		[Display(Name = "Show Analysis (Past Invalid S/R Lines)", Description = "When enabled (via top Analysis icon), show historical invalidated/broken S/R lines for chart analysis; when off, keep the UI clean with only active valid S/R lines", GroupName = "8. S/R Zones (Minor / Major / D-W-M)", Order = 4)]
		public bool EnableSrPastAnalysis { get; set; }

		[Browsable(false)]
		public bool ShowSrWeakZones { get; set; }

		[Display(Name = "Show Day High & Low", Description = "Show Previous Trading Day High & Low Support/Resistance lines and right-side labels", GroupName = "8. S/R Zones (Minor / Major / D-W-M)", Order = 4)]
		public bool ShowSrDayHighLow { get; set; }

		[Display(Name = "Show Week High & Low", Description = "Show Previous Trading Week High & Low Support/Resistance lines and right-side labels", GroupName = "8. S/R Zones (Minor / Major / D-W-M)", Order = 5)]
		public bool ShowSrWeekHighLow { get; set; }

		[Display(Name = "Show Month High & Low", Description = "Show Previous Calendar Month High & Low Support/Resistance lines and right-side labels", GroupName = "8. S/R Zones (Minor / Major / D-W-M)", Order = 6)]
		public bool ShowSrMonthHighLow { get; set; }

		[Range(2, 40)]
		[Display(Name = "Minor Pivot Left / Right Bars", Description = "Confirmed swing pivot lookback/lookahead bars for Minor S/R (Current TF, default = 8)", GroupName = "8. S/R Zones (Minor / Major / D-W-M)", Order = 7)]
		public int SrMinorPivotBars { get; set; }

		[Range(2, 40)]
		[Display(Name = "Major Pivot Left / Right Bars", Description = "Confirmed swing pivot lookback/lookahead bars for Major S/R (TF × TF, default = 8)", GroupName = "8. S/R Zones (Minor / Major / D-W-M)", Order = 8)]
		public int SrMajorPivotBars { get; set; }

		[Range(1, 50)]
		[Display(Name = "S/R ATR Length", Description = "ATR period used for breakout validation buffer (default = 14)", GroupName = "8. S/R Zones (Minor / Major / D-W-M)", Order = 9)]
		public int SrAtrLength { get; set; }

		[Range(0.02, 1.0)]
		[Display(Name = "Break Validation Buffer (ATR Mult)", Description = "Close buffer beyond S/R line (as ATR multiple) required to finish validation and trigger S/R Flip (default = 0.14)", GroupName = "8. S/R Zones (Minor / Major / D-W-M)", Order = 10)]
		public double SrZoneAtrMultiplier { get; set; }

		[Browsable(false)]
		public int SrReactionsForStrong { get; set; }

		[Browsable(false)]
		public int SrMaxZonesPerSide { get; set; }

		// ==================== 9. Long / Short R:R Position Tool (Backtest & Live Order Tracking) ====================
		[Range(1.0, 10000000.0)]
		[Display(Name = "Risk Amount per Trade", Description = "Account risk amount used to auto-calculate trade Quantity and Risk/Reward currency amounts (default = 500)", Order = 0, GroupName = "9. Long / Short R:R Position Tool")]
		public double RrRiskPerTradeAmount { get; set; }

		[Display(Name = "Auto-Calculate Qty from Risk", Description = "Automatically calculate contract/lot Quantity from Risk Amount and Stop Loss distance (TradingView style)", Order = 1, GroupName = "9. Long / Short R:R Position Tool")]
		public bool RrAutoCalculateQty { get; set; }

		[Range(1, 100000)]
		[Display(Name = "Fixed Quantity (when Auto Qty off)", Description = "Fixed contract/lot Quantity used when Auto-Calculate Qty is disabled (default = 1)", Order = 2, GroupName = "9. Long / Short R:R Position Tool")]
		public int RrFixedQuantity { get; set; }

		[Range(0.5, 20.0)]
		[Display(Name = "Default Placement R:R Ratio", Description = "Initial Risk:Reward ratio when placing a new Long or Short Position box on the chart (default = 2.0)", Order = 3, GroupName = "9. Long / Short R:R Position Tool")]
		public double RrDefaultRatio { get; set; }

		// ==================== 10. Tradesea Order Execution & Live Order Monitoring ====================
		[Display(Name = "Enable Tradesea Order Panel (Right Sidebar)", Description = "Show the Tradesea-style Order & Scalp execution panel on the right side of the chart window (also toggleable via the top toolbar)", Order = 0, GroupName = "10. Tradesea Order Execution & Live Monitoring")]
		public bool EnableTradePanel { get; set; }

		[Display(Name = "Enable Live Order Lines & Drag TP/SL on Chart", Description = "Show Live Open Position, Working Entry / TP / SL lines, and drag-to-modify TP & SL handles directly on the chart", Order = 1, GroupName = "10. Tradesea Order Execution & Live Monitoring")]
		public bool EnableLiveChartOrders { get; set; }

		[Display(Name = "Hide Built-In NT8 Chart Trader When Open", Description = "Automatically collapse NinjaTrader's default Chart Trader panel when the AK10X Tradesea Order Panel is visible", Order = 2, GroupName = "10. Tradesea Order Execution & Live Monitoring")]
		public bool HideBuiltInChartTrader { get; set; }

		[Range(1, 100000)]
		[Display(Name = "Default Order Quantity", Description = "Initial contract/lot quantity in the Order & Scalp panel (default = 1)", Order = 3, GroupName = "10. Tradesea Order Execution & Live Monitoring")]
		public int DefaultOrderQuantity { get; set; }

		[Range(1, 10000)]
		[Display(Name = "Default Stop Loss (Ticks)", Description = "Initial base Stop Loss distance in ticks when placing orders (default = 20)", Order = 4, GroupName = "10. Tradesea Order Execution & Live Monitoring")]
		public int DefaultSlTicks { get; set; }

		[Range(1, 10000)]
		[Display(Name = "Default Take Profit (Ticks)", Description = "Initial base Take Profit distance in ticks (default = 60 -> 1:3 R:R with 20T SL)", Order = 5, GroupName = "10. Tradesea Order Execution & Live Monitoring")]
		public int DefaultTpTicks { get; set; }

		[Display(Name = "Auto-Enable Initial 1:3 SL & TP", Description = "Automatically enable Stop Loss and 1:3 Take Profit whenever configuring or placing Market, Buy/Sell Limit, or Stop orders (adjustable anytime on chart or panel)", Order = 6, GroupName = "10. Tradesea Order Execution & Live Monitoring")]
		public bool DefaultEnableBracketSlTp { get; set; }

		[Range(0.5, 20.0)]
		[Display(Name = "Initial Order R:R Ratio (1:3)", Description = "Default Risk:Reward multiplier applied to base Stop Loss ticks (default = 3.0 -> 1:3 R:R)", Order = 7, GroupName = "10. Tradesea Order Execution & Live Monitoring")]
		public double InitialOrderRrRatio { get; set; }

		// ==================== 12. Risk Management, Daily Discipline & Capital Protector ====================
		[Display(Name = "Enable Daily Risk & Discipline Guard", Description = "Enforce Max Trades Per Day, Per-Day Profit Target, and Per-Day StopLoss — once reached, disables placing new orders until Next Day Open", Order = 0, GroupName = "12. Risk Management & Capital Protector")]
		public bool EnableRiskManagement { get; set; }

		[Range(0, 500)]
		[Display(Name = "Max Trades Per Day (0 = Unlimited)", Description = "Maximum allowed trade entries per trading day. Once reached, disables placing new orders until Next Day Open (default = 3)", Order = 1, GroupName = "12. Risk Management & Capital Protector")]
		public int MaxTradesPerDay { get; set; }

		[Range(0.0, 10000000.0)]
		[Display(Name = "Per-Day Profit Target Amount (0 = Off)", Description = "Daily profit target in account currency. Once reached, locks in daily target and disables placing new orders until Next Day Open (default = 500)", Order = 2, GroupName = "12. Risk Management & Capital Protector")]
		public double DailyProfitTargetAmount { get; set; }

		[Range(0.0, 10000000.0)]
		[Display(Name = "Per-Day StopLoss Amount (0 = Off)", Description = "Maximum daily loss allowed in account currency. Once reached, stops loss and disables placing new orders until Next Day Open (default = 300)", Order = 3, GroupName = "12. Risk Management & Capital Protector")]
		public double DailyMaxLossAmount { get; set; }

		[Display(Name = "Auto-Close Open Trade When Daily Target/SL Hits", Description = "Automatically flatten open position and cancel working orders the instant Daily Profit Target or Daily StopLoss is reached", Order = 4, GroupName = "12. Risk Management & Capital Protector")]
		public bool AutoFlattenOnDailyLimit { get; set; }

		[Display(Name = "Enable Capital Protector (Spike & News Shield)", Description = "God-mode real-time Capital Protector: caps SL risk, auto-attaches emergency SL if missing, and flattens on tick-level news spikes / big adverse candles if loss reaches Capital Protector Amount", Order = 5, GroupName = "12. Risk Management & Capital Protector")]
		public bool EnableCapitalProtector { get; set; }

		[Range(1.0, 10000000.0)]
		[Display(Name = "Capital Protector Max Loss Amount", Description = "Maximum capital risk allowed on an open position during news spikes or big candle formations — triggers immediate tick-level protective flatten & hard SL cap (default = 200)", Order = 6, GroupName = "12. Risk Management & Capital Protector")]
		public double CapitalProtectorAmount { get; set; }

		[Display(Name = "Lock Day If Capital Protector Triggers", Description = "When enabled, disables new order placement until Next Day Open if Capital Protector emergency flatten is triggered during a spike/news event", Order = 7, GroupName = "12. Risk Management & Capital Protector")]
		public bool CapitalProtectorLockDayOnTrigger { get; set; }

		// ==================== 11. Multi-TF Closed-Candle Signals (Current TF 3m + Higher TF 9m) ====================
		[Display(Name = "Enable Chart Signals (Master)", Description = "Show confirmed Swing High/Low triangles and 3m + 9m Supply/Demand Zone Reversal signals on closed candles only (also toggleable via the top Signal icon)", Order = 0, GroupName = "11. Multi-TF Closed-Candle Signals")]
		public bool EnableChartSignals { get; set; }

		[Display(Name = "Show Confirmed Swing High / Low (▼ / ▲)", Description = "Show confirmed Chart Swing High (pink ▼) and Swing Low (green ▲) triangles once all confirmation candles have closed", Order = 1, GroupName = "11. Multi-TF Closed-Candle Signals")]
		public bool SignalShowSwingTriangles { get; set; }

		[Display(Name = "Show Zone Reversal Entry Signals (Buy / Sell)", Description = "Show immediate closed-candle Bullish & Bearish reversal setups (Engulfing, Hammer/Shooting Star, Doji Breakout/Breakdown) inside Current TF (3m) + Higher TF (9m) Supply & Demand zones", Order = 2, GroupName = "11. Multi-TF Closed-Candle Signals")]
		public bool SignalShowZoneReversalEntries { get; set; }

		[Range(2, 12)]
		[Display(Name = "Higher Timeframe Multiplier (3× = 9m on 3m)", Description = "Higher timeframe multiplier relative to the current chart timeframe (default = 3, so Current TF 3m -> Higher TF 9m)", Order = 3, GroupName = "11. Multi-TF Closed-Candle Signals")]
		public int SignalHigherTfMultiplier { get; set; }

		[Range(1, 30)]
		[Display(Name = "Swing Left Bars", Description = "Left bars required for confirmed Swing High / Low triangles (PineScript default = 10)", Order = 4, GroupName = "11. Multi-TF Closed-Candle Signals")]
		public int SignalSwingLeftBars { get; set; }

		[Range(1, 30)]
		[Display(Name = "Swing Right Bars", Description = "Right closed bars required to confirm Swing High / Low triangles (PineScript default = 10)", Order = 5, GroupName = "11. Multi-TF Closed-Candle Signals")]
		public int SignalSwingRightBars { get; set; }

		[Range(2, 40)]
		[Display(Name = "Current TF (3m) Zone Pivot Bars", Description = "Left & Right bars for Current TF Supply & Demand zone pivots (PineScript default = 20)", Order = 6, GroupName = "11. Multi-TF Closed-Candle Signals")]
		public int SignalZonePivotBarsLtf { get; set; }

		[Range(2, 40)]
		[Display(Name = "Higher TF (9m) Zone Pivot Bars", Description = "Left & Right bars for Higher TF Supply & Demand zone pivots (PineScript default = 20)", Order = 7, GroupName = "11. Multi-TF Closed-Candle Signals")]
		public int SignalZonePivotBarsHtf { get; set; }

		[Range(1, 50)]
		[Display(Name = "Zone ATR Length", Description = "Wilder ATR period for 3m & 9m Supply/Demand zone thickness (PineScript default = 14)", Order = 8, GroupName = "11. Multi-TF Closed-Candle Signals")]
		public int SignalAtrLength { get; set; }

		[Range(0.05, 1.50)]
		[Display(Name = "Zone Thickness (ATR Multiplier)", Description = "Zone thickness as multiple of ATR (PineScript default = 0.30)", Order = 9, GroupName = "11. Multi-TF Closed-Candle Signals")]
		public double SignalZoneAtrMultiplier { get; set; }

		[Range(2, 5)]
		[Display(Name = "Reactions For Strong Zone", Description = "Rejections required to upgrade a zone to Strong (PineScript default = 2)", Order = 10, GroupName = "11. Multi-TF Closed-Candle Signals")]
		public int SignalReactionsForStrong { get; set; }

		[Range(0.01, 0.35)]
		[Display(Name = "Doji Max Body / Range", Description = "Maximum body-to-range ratio for Doji Breakout / Breakdown setups (PineScript default = 0.20)", Order = 11, GroupName = "11. Multi-TF Closed-Candle Signals")]
		public double SignalDojiMaxBodyPct { get; set; }

		[Range(0.0, 1.0)]
		[Display(Name = "Pattern Zone Buffer (ATR Ratio)", Description = "Proximity buffer around Supply/Demand zones & PDH/PDL scaled to ATR so it adapts cleanly across Gold, Nifty, Indices, and Forex (default = 0.25)", Order = 12, GroupName = "11. Multi-TF Closed-Candle Signals")]
		public double SignalZoneBufferAtrRatio { get; set; }
		#endregion

		#region Lifecycle & Dual Data Feed Engine (Orderflow + 45-Day 1M Market Profile Backfill)
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "AK10XBigSort — Unified Buyer & Seller Orderflow Chart with Global Market Profile Zones (Value Areas, POCs, Single Prints, Poor/Day/Weekly Virgin Levels) & Right-Side Bar Values";
				Name = "AK10XBigSort";
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
				BarVAPercentage = 70;
				ShowImbalance = true;
				ImbalancePercentage = 300.0;
				ImbalanceFilter = 3;
				ShowImbalanceSRZones = false;
				ImbalanceSRConsecutiveLevels = 3;

				UseSessionWiseOrderflow = true;
				AsiaTicksPerLevel = 2;
				AsiaImbalancePercentage = 300.0;
				AsiaMinVolumePerLevel = 5;
				LondonTicksPerLevel = 1;
				LondonImbalancePercentage = 300.0;
				LondonMinVolumePerLevel = 8;
				NewYorkTicksPerLevel = 1;
				NewYorkImbalancePercentage = 400.0;
				NewYorkMinVolumePerLevel = 15;

				ShowBarDelta = true;
				ShowStatisticGrid = true;
				MomentumMaxWickPct = 25;

				EnableMarketProfileZones = true;
				ShowSessionHeaderFooter = true;
				ShowChartButtons = false;
				ShowRightSideValues = true;
				ShowValueAreaZoneFill = true;
				ShowSessionDividers = true;
				TpoSize = 0;
				TpoIntervalMinutes = 30;
				ValueAreaPercent = 70;
				InitialBalanceMinutes = 60;
				DaysToDisplay = 45;
				ProfileMode = MarketProfilePeriodMode.Daily;

				ShowAsia = true;
				AsiaFromTime = "03:30 AM";
				AsiaToTime = "01:30 PM";

				ShowLondon = true;
				LondonFromTime = "01:30 PM";
				LondonToTime = "07:00 PM";

				ShowNewYork = true;
				NewYorkFromTime = "07:00 PM";
				NewYorkToTime = "02:30 AM";

				ShowIndianSession = false;
				MarketOpen = 91500;
				MarketClose = 153000;
				SessionLabel = "NSE Cash";

				MergeAsiaAndLondon = false;
				MergeLondonAndNewYork = false;
				MergeAllDaySessions = false;

				VirginityBreakRule = VirginityBreakRuleMode.CrossBased;
				ShowSinglePrints = true;
				ShowPoorHighLow = true;
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

				EnableSrZones = true;
				ShowMinorSrZones = true;
				ShowMajorSrZones = true;
				ShowSrFlipLines = true;
				EnableSrPastAnalysis = false;
				ShowSrWeakZones = true;
				ShowSrDayHighLow = true;
				ShowSrWeekHighLow = true;
				ShowSrMonthHighLow = true;
				SrMinorPivotBars = 8;
				SrMajorPivotBars = 8;
				SrAtrLength = 14;
				SrZoneAtrMultiplier = 0.14;
				SrReactionsForStrong = 2;
				SrMaxZonesPerSide = 10;

				RrRiskPerTradeAmount = 500.0;
				RrAutoCalculateQty = true;
				RrFixedQuantity = 1;
				RrDefaultRatio = 3.0;

				EnableTradePanel = true;
				EnableLiveChartOrders = true;
				HideBuiltInChartTrader = true;
				DefaultOrderQuantity = 1;
				DefaultSlTicks = 20;
				DefaultTpTicks = 60;
				DefaultEnableBracketSlTp = true;
				InitialOrderRrRatio = 3.0;

				EnableRiskManagement = true;
				MaxTradesPerDay = 3;
				DailyProfitTargetAmount = 500.0;
				DailyMaxLossAmount = 300.0;
				AutoFlattenOnDailyLimit = true;
				EnableCapitalProtector = true;
				CapitalProtectorAmount = 200.0;
				CapitalProtectorLockDayOnTrigger = true;

				EnableChartSignals = false;
				SignalShowSwingTriangles = true;
				SignalShowZoneReversalEntries = true;
				SignalHigherTfMultiplier = 3;
				SignalSwingLeftBars = 10;
				SignalSwingRightBars = 10;
				SignalZonePivotBarsLtf = 20;
				SignalZonePivotBarsHtf = 20;
				SignalAtrLength = 14;
				SignalZoneAtrMultiplier = 0.30;
				SignalReactionsForStrong = 2;
				SignalDojiMaxBodyPct = 0.20;
				SignalZoneBufferAtrRatio = 0.25;
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
					lastComputedSessionCfgHash = 0;
				}
				lock (profileLock)
				{
					mpMinuteMap.Clear();
					rawMinuteSamples.Clear();
					profiles.Clear();
					mergedWithNextKeys.Clear();
					unmergedOverrideKeys.Clear();
					activeSessions = GetProfileSessions();
					cachedPriceStep = 0;
					mp1MBackfillLoaded = false;
					mpSeededChartBarCount = -1;
					highestBackfillDaysRequested = 0;
				}
				lock (srLock)
				{
					srDailyHistory.Clear();
					cachedSrSnapshot = null;
					srCacheChartBarCount = -1;
					srCacheMinuteCount = -1;
					srCacheDailyCount = -1;
					srCacheCloseTickBucket = long.MinValue;
					srCacheConfigHash = 0;
				}
				lock (signalLock)
				{
					cachedSignalSnapshot = null;
					lockedClosedBarSignals.Clear();
					lockedSignalsMaxClosedIdx = -1;
					lockedSignalsFirstBarTicks = 0L;
					sigCacheChartBarCount = -1;
					sigCacheLastClosedIdx = -2;
					sigCacheMinuteCount = -1;
					sigCacheDailyCount = -1;
					sigCacheConfigHash = 0;
				}

				runtimeDataFeedOverride = null;
				runtimeOrderflowMode = FootprintMode;
				runtimeLightTheme = UseLightTheme;
				runtimeTicksPerLevel = Math.Max(1, TicksPerLevel);
				lastEffectiveAutoTicks = runtimeTicksPerLevel;
				runtimeAutoStep = AutoScalePriceStep;
				runtimeSessionStepActive = UseSessionWiseOrderflow;
				runtimeShowStatGrid = ShowStatisticGrid;
				runtimeShowImbZones = ShowImbalanceSRZones;
				runtimeShowHalfpack = ShowHalfpack;
				runtimeShowMpZones = EnableMarketProfileZones;
				runtimeShowSrZones = EnableSrZones;
				runtimeShowSrAnalysis = EnableSrPastAnalysis;
				runtimeShowSignals = EnableChartSignals;
				runtimeYZoomFactor = 1.0;
				isFree360View = false;
				freeViewMinPrice = double.NaN;
				freeViewMaxPrice = double.NaN;
				freeRightPanOffsetPx = 0f;
				lastRenderedMinPrice = double.NaN;
				lastRenderedMaxPrice = double.NaN;
				lastRenderedChartScale = null;
				focusedCandleBarIndex = -1;
				pendingRrToolMode = 0;
				hoveredRrBoxId = -1;
				activeRrDragMode = RrDragMode.None;
				activeDragRrBox = null;
				runtimeShowTradePanel = EnableTradePanel;
				orderQtyValue = Math.Max(1, DefaultOrderQuantity);
				orderSlEnabled = DefaultEnableBracketSlTp;
				orderTpEnabled = DefaultEnableBracketSlTp;
				scalpUseAutoBracket = DefaultEnableBracketSlTp;
				orderSlTicks = Math.Max(1, DefaultSlTicks);
				double initRr = InitialOrderRrRatio > 0.1 ? InitialOrderRrRatio : 3.0;
				orderTpTicks = (DefaultTpTicks > 0 && Math.Abs(DefaultTpTicks - 40) > 0 && Math.Abs(initRr - 3.0) < 1e-6)
					? Math.Max(1, DefaultTpTicks)
					: Math.Max(1, (int)Math.Round(orderSlTicks * initRr));
				orderTpManuallyCustomized = false;
				orderCustomEntrySeeded = false;
				activeLiveOrderDragMode = LiveOrderDragMode.None;
				activeDraggedWorkingOrder = null;
				activeDraggedPendingSpec = null;
				hoveredLiveHitRegionIdx = -1;
				EnsureSharedRiskDisciplineFileLoaded();
				DisposeLogoBitmap();
				liveStreamInitialized = true;

				EnsureMouseEventsSubscribed();
				EnsureTradePanelAttachedAsync();
				AttachSelectedDataFeedStream();
				int reqDays = EffectiveProfileMode == MarketProfilePeriodMode.Monthly ? Math.Max(95, DaysToDisplay) : Math.Max(75, DaysToDisplay);
				RequestSelectedDataFeedBarsAsync(reqDays);
			}
			else if (State == State.Realtime)
			{
				if (!liveStreamInitialized)
				{
					liveStreamInitialized = true;
					AttachSelectedDataFeedStream();
					int reqDays = EffectiveProfileMode == MarketProfilePeriodMode.Monthly ? Math.Max(95, DaysToDisplay) : Math.Max(75, DaysToDisplay);
					RequestSelectedDataFeedBarsAsync(reqDays);
				}
			}
			else if (State == State.Terminated)
			{
				DisposeLogoBitmap();
				DetachSelectedDataFeedStream();
				DetachTradePanelAndAccountEvents();
				UnsubscribeMouseEvents();
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

		private SharpDX.Direct2D1.Bitmap GetOrCreateLogoBitmap(RenderTarget rt, bool lightTheme)
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
					FormatConvertedBitmap conv = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Pbgra32, null, 0);
					int w = conv.PixelWidth;
					int h = conv.PixelHeight;
					int stride = w * 4;
					byte[] pixels = new byte[stride * h];
					conv.CopyPixels(pixels, stride, 0);

					// Smooth circular premultiplied-alpha feather matching AK10X Market Profile HUD logo
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
						cachedLogoIsLight = lightTheme;
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

		private bool IsIntradayMinuteChart()
		{
			try
			{
				if (BarsPeriod != null)
					return BarsPeriod.BarsPeriodType == BarsPeriodType.Minute;
				if (ChartBars != null && ChartBars.Properties != null && ChartBars.Properties.BarsPeriod != null)
					return ChartBars.Properties.BarsPeriod.BarsPeriodType == BarsPeriodType.Minute;
			}
			catch { }
			return true;
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
			lock (profileLock)
			{
				mpMinuteMap.Clear();
				rawMinuteSamples.Clear();
				profiles.Clear();
				mp1MBackfillLoaded = false;
				mpSeededChartBarCount = -1;
				highestBackfillDaysRequested = 0;
			}
			lock (srLock)
			{
				srDailyHistory.Clear();
				cachedSrSnapshot = null;
				srCacheChartBarCount = -1;
			}
			lock (signalLock)
			{
				cachedSignalSnapshot = null;
				lockedClosedBarSignals.Clear();
				lockedSignalsMaxClosedIdx = -1;
				lockedSignalsFirstBarTicks = 0L;
				sigCacheChartBarCount = -1;
				sigCacheLastClosedIdx = -2;
			}
			AttachSelectedDataFeedStream();
			int reqDays = EffectiveProfileMode == MarketProfilePeriodMode.Monthly ? Math.Max(95, DaysToDisplay) : Math.Max(75, DaysToDisplay);
			RequestSelectedDataFeedBarsAsync(reqDays);
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
					EnsureAccelpixProviderConnected();

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

			try
			{
				BarsRequest reqD = activeFeedDailyReq;
				activeFeedDailyReq = null;
				if (reqD != null)
				{
					reqD.Dispose();
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
					int reqDays = EffectiveProfileMode == MarketProfilePeriodMode.Monthly ? Math.Max(95, DaysToDisplay) : Math.Max(45, DaysToDisplay);
					RequestSelectedDataFeedBarsAsync(reqDays);
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

		private void RequestSelectedDataFeedBarsAsync(int targetDays = 45, bool onlyMinuteBackfill = false)
		{
			try
			{
				Instrument inst = Instrument;
				if (inst == null || State == State.Terminated)
					return;

				int daysToFetch = Math.Max(45, targetDays);
				lock (profileLock)
				{
					if (onlyMinuteBackfill && daysToFetch <= highestBackfillDaysRequested)
						return;
					if (daysToFetch > highestBackfillDaysRequested)
						highestBackfillDaysRequested = daysToFetch;
				}

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

						if (!useAccelpix && !onlyMinuteBackfill)
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

						DateTime startDate = endDate.AddDays(-daysToFetch);
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

						// Request 400-day Daily OHLC bars (~270 bars) so Previous Day / Week / Month High & Low are 100% available
						try
						{
							BarsRequest reqD = new BarsRequest(inst, endDate.AddDays(-400), endDate)
							{
								BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Day, Value = 1 },
								MergePolicy = chartMerge,
								TradingHours = reqTh
							};
							BarsRequest oldD = activeFeedDailyReq;
							activeFeedDailyReq = reqD;
							if (oldD != null)
							{
								try { oldD.Dispose(); } catch { }
							}
							reqD.Request((dBars, dErr, dMsg) =>
							{
								try
								{
									if (State == State.Terminated || dErr != ErrorCode.NoError || dBars == null || dBars.Bars == null || dBars.Bars.Count <= 0)
										return;
									IngestDailyHorizonBars(dBars.Bars);
								}
								catch { }
							});
						}
						catch { }
					}
					catch { }
				});
			}
			catch { }
		}

		private void IngestDailyHorizonBars(Bars bDaily)
		{
			if (bDaily == null || bDaily.Count <= 0)
				return;

			lock (srLock)
			{
				for (int i = 0; i < bDaily.Count; i++)
				{
					DateTime dt = bDaily.GetTime(i).Date;
					double h = bDaily.GetHigh(i);
					double l = bDaily.GetLow(i);
					double c = bDaily.GetClose(i);
					if (h <= 0 || l <= 0 || h < l)
						continue;

					DailyHorizonSample s;
					if (!srDailyHistory.TryGetValue(dt, out s))
					{
						s = new DailyHorizonSample { Date = dt };
						srDailyHistory[dt] = s;
					}
					if (h > s.High)
					{
						s.High = h;
						if (s.HighTime == DateTime.MinValue) s.HighTime = dt.AddHours(12);
					}
					if (l < s.Low)
					{
						s.Low = l;
						if (s.LowTime == DateTime.MinValue) s.LowTime = dt.AddHours(12);
					}
					s.Close = c;
				}
				cachedSrSnapshot = null;
			}

			RequestImmediateOrderflowRedraw();
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

			List<MinuteSample> extractedSamples = new List<MinuteSample>(count);
			DateTime subCutoff = DateTime.Now.Date.AddDays(-12);
			Dictionary<DateTime, List<SubBar1M>> builtSubBars = new Dictionary<DateTime, List<SubBar1M>>();

			DateTime prevDate = DateTime.MinValue;
			long prevCumVol = 0;

			for (int i = 0; i < count; i++)
			{
				DateTime t = b1m.GetTime(i);
				double open = b1m.GetOpen(i);
				double high = b1m.GetHigh(i);
				double low = b1m.GetLow(i);
				double close = b1m.GetClose(i);
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

				extractedSamples.Add(new MinuteSample
				{
					Time = t,
					Open = open,
					High = high,
					Low = low,
					Close = close,
					Volume = Math.Max(1.0, (double)barVol),
					BarIndex = i
				});

				if (t >= subCutoff)
				{
					DateTime slot = GetBarSlotKey(t);
					if (!builtSubBars.TryGetValue(slot, out List<SubBar1M> list))
					{
						list = new List<SubBar1M>(5);
						builtSubBars[slot] = list;
					}
					list.Add(new SubBar1M
					{
						Time = t,
						Open = open,
						High = high,
						Low = low,
						Close = close,
						Volume = Math.Max(1L, barVol)
					});
				}
			}

			lock (dataLock)
			{
				subBarsBySlot.Clear();
				foreach (var kv in builtSubBars)
					subBarsBySlot[kv.Key] = kv.Value;
				barCache.Clear();
				lastComputedBarCount = -1;
			}

			lock (profileLock)
			{
				mpMinuteMap.Clear();
				for (int i = 0; i < extractedSamples.Count; i++)
					mpMinuteMap[extractedSamples[i].Time] = extractedSamples[i];

				rawMinuteSamples.Clear();
				rawMinuteSamples.AddRange(mpMinuteMap.Values);
				activeSessions = GetProfileSessions();
				mp1MBackfillLoaded = true;
				cachedPriceStep = ComputeAutoOrManualPriceStep();
				RebuildAllSessionProfiles(cachedPriceStep);
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
				lastFeed1MUpdateUtc = DateTime.UtcNow;

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

				lock (profileLock)
				{
					bool hadSameMin = mpMinuteMap.TryGetValue(t, out MinuteSample prevMin);
					double deltaVol = hadSameMin ? Math.Max(0.0, rawVol - prevMin.Volume) : rawVol;

					MinuteSample updatedMin = new MinuteSample
					{
						Time = t,
						Open = open,
						High = high,
						Low = low,
						Close = close,
						Volume = rawVol,
						BarIndex = maxIdx
					};
					mpMinuteMap[t] = updatedMin;

					if (rawMinuteSamples.Count > 0 && rawMinuteSamples[rawMinuteSamples.Count - 1].Time == t)
						rawMinuteSamples[rawMinuteSamples.Count - 1] = updatedMin;
					else if (rawMinuteSamples.Count == 0 || t > rawMinuteSamples[rawMinuteSamples.Count - 1].Time)
						rawMinuteSamples.Add(updatedMin);

					if (activeSessions == null || activeSessions.Length == 0)
						activeSessions = GetProfileSessions();
					if (cachedPriceStep <= 0)
						cachedPriceStep = ComputeAutoOrManualPriceStep();

					MinuteSample incSample = updatedMin;
					incSample.Volume = deltaVol;
					AccumulateSampleIntoSessions(incSample, cachedPriceStep, isLiveTickUpdate: hadSameMin);
				}

				RequestThrottledChartRefresh(false);
			}
			catch { }
		}

		private void EnsureMarketProfileSeededFromChartBars()
		{
			if (ChartBars == null || ChartBars.Bars == null || ChartBars.Bars.Count <= 0)
				return;

			lock (profileLock)
			{
				if (mp1MBackfillLoaded && profiles.Count > 0)
					return;

				Bars bars = ChartBars.Bars;
				int count = bars.Count;
				if (profiles.Count > 0 && count == mpSeededChartBarCount)
					return;

				mpSeededChartBarCount = count;
				mpMinuteMap.Clear();
				rawMinuteSamples.Clear();

				for (int i = 0; i < count; i++)
				{
					DateTime t = bars.GetTime(i);
					MinuteSample s = new MinuteSample
					{
						Time = t,
						Open = bars.GetOpen(i),
						High = bars.GetHigh(i),
						Low = bars.GetLow(i),
						Close = bars.GetClose(i),
						Volume = Math.Max(1.0, (double)bars.GetVolume(i)),
						BarIndex = i
					};
					mpMinuteMap[t] = s;
					rawMinuteSamples.Add(s);
				}

				activeSessions = GetProfileSessions();
				cachedPriceStep = ComputeAutoOrManualPriceStep();
				RebuildAllSessionProfiles(cachedPriceStep);
			}
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
		#endregion

		#region Mouse, Smooth Zoom, Margin & Toolbar Interaction
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

		private void SyncOneBoxRightGapMargin(ChartControl cc, float barSpacing, bool immediateOnUiThread)
		{
			if (cc == null || cc.Properties == null)
				return;

			int baseMargin;
			if ((runtimeShowMpZones || runtimeShowSrZones) && ShowRightSideValues)
			{
				float boxHalf = barSpacing >= CandlestickZoomThresholdPx
					? Math.Min(56f, barSpacing * 0.48f)
					: Math.Min(16f, barSpacing * 0.40f);
				baseMargin = (int)Math.Ceiling(RightValuesLaneWidth + boxHalf + 18f);
			}
			else
			{
				int minRightMargin = runtimeShowStatGrid ? 92 : 28;
				baseMargin = Math.Max(minRightMargin, Math.Min(150, (int)Math.Round(barSpacing * 1.18f)));
			}

			int extraPanPx = (int)Math.Round(Math.Max(0f, freeRightPanOffsetPx));
			int desiredMargin = baseMargin + extraPanPx;
			int tolerance = immediateOnUiThread ? 0 : 6;

			if (Math.Abs(cc.Properties.BarMarginRightUser - desiredMargin) <= tolerance)
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

		private bool IsOfInteractiveUiControlSource(object originalSource)
		{
			try
			{
				if (tradePanelRoot != null && tradePanelRoot.IsMouseOver)
					return true;

				DependencyObject cur = originalSource as DependencyObject;
				int guard = 0;
				while (cur != null && guard++ < 24)
				{
					if ((tradePanelRoot != null && ReferenceEquals(cur, tradePanelRoot)) ||
						cur is System.Windows.Controls.Primitives.Popup ||
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
						if (ofPreviewKeyDownHandler == null)
							ofPreviewKeyDownHandler = new KeyEventHandler(OnChartPreviewKeyDown);

						if (!mouseSubscribed || hookedOrderflowCC != cc)
						{
							if (hookedOrderflowCC != null)
							{
								try { hookedOrderflowCC.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, ofMouseLeftDownHandler); } catch { }
								try { hookedOrderflowCC.RemoveHandler(UIElement.PreviewMouseWheelEvent, ofMouseWheelHandler); } catch { }
								hookedOrderflowCC.PreviewMouseMove -= OnChartMouseMove;
								hookedOrderflowCC.PreviewMouseLeftButtonUp -= OnChartMouseLeftButtonUp;
								hookedOrderflowCC.MouseLeave -= OnChartMouseLeave;
								hookedOrderflowCC.PreviewKeyDown -= ofPreviewKeyDownHandler;
							}
							cc.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, ofMouseLeftDownHandler);
							cc.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, ofMouseLeftDownHandler, true);
							cc.RemoveHandler(UIElement.PreviewMouseWheelEvent, ofMouseWheelHandler);
							cc.AddHandler(UIElement.PreviewMouseWheelEvent, ofMouseWheelHandler, true);
							cc.PreviewMouseMove += OnChartMouseMove;
							cc.PreviewMouseLeftButtonUp += OnChartMouseLeftButtonUp;
							cc.MouseLeave += OnChartMouseLeave;
							cc.PreviewKeyDown += ofPreviewKeyDownHandler;
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
						if (ofPreviewKeyDownHandler != null)
						{
							try { cc.PreviewKeyDown -= ofPreviewKeyDownHandler; } catch { }
						}
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

		private void EnsureAutoYAxisFor360View()
		{
			try
			{
				ChartScale cs = lastRenderedChartScale;
				if (cs != null && cs.Properties != null && cs.Properties.YAxisRangeType != YAxisRangeType.Automatic)
				{
					if (!isFree360View && cs.MaxValue > cs.MinValue)
					{
						freeViewMinPrice = cs.MinValue;
						freeViewMaxPrice = cs.MaxValue;
						lastRenderedMinPrice = cs.MinValue;
						lastRenderedMaxPrice = cs.MaxValue;
						isFree360View = true;
					}
					cs.Properties.YAxisRangeType = YAxisRangeType.Automatic;
				}
			}
			catch { }
		}

		private void Reset360ViewToAutoFit(ChartControl cc, bool snapLiveIfAlreadyFit)
		{
			bool wasCustomView = isFree360View || freeRightPanOffsetPx > 0.5f || Math.Abs(runtimeYZoomFactor - 1.0) > 0.01;
			isFree360View = false;
			freeViewMinPrice = double.NaN;
			freeViewMaxPrice = double.NaN;
			runtimeYZoomFactor = 1.0;
			freeRightPanOffsetPx = 0f;
			focusedCandleBarIndex = -1;

			lock (dataLock)
			{
				lastComputedUniformStep = -1.0;
			}

			try
			{
				if (lastRenderedChartScale != null && lastRenderedChartScale.Properties != null)
					lastRenderedChartScale.Properties.YAxisRangeType = YAxisRangeType.Automatic;
			}
			catch { }

			if (cc != null && cc.Properties != null)
			{
				float curDist = cc.Properties.BarDistance > 3f ? cc.Properties.BarDistance : BarSpacePx;
				SyncOneBoxRightGapMargin(cc, curDist, true);
				TriggerChartScrollbarRefresh();

				if (!wasCustomView && snapLiveIfAlreadyFit)
				{
					var sb = hookedOfScrollBar ?? (hookedOfScrollBar = ResolveOrderflowScrollBar(cc));
					if (sb != null && sb.Maximum > sb.Minimum)
						sb.Value = sb.Maximum;
				}
			}

			RequestImmediateOrderflowRedraw();
		}

		private int FindCandleAtPoint(ChartControl cc, float pxX, float pxY)
		{
			if (cc == null || ChartPanel == null || ChartBars == null || ChartBars.Bars == null || ChartBars.Count <= 0)
				return -1;

			ChartScale cs = lastRenderedChartScale;
			if (cs == null)
				return -1;

			float panelLeft = (float)ChartPanel.X;
			float panelY = (float)ChartPanel.Y;
			float panelW = (float)ChartPanel.W;
			float panelH = (float)ChartPanel.H;
			float panelRight = panelLeft + panelW;

			bool anyZones = runtimeShowMpZones || runtimeShowSrZones;
			bool showHdrFtr = anyZones && ShowSessionHeaderFooter;
			float toolbarBottomY = panelY + (panelW < 764f ? 64f : 38f);
			float canvasTopY = showHdrFtr ? (toolbarBottomY + 29f) : (toolbarBottomY + 2f);
			float dateFooterY = (float)Math.Round(panelY + panelH - 23f);
			float bottomAfterFooterY = showHdrFtr ? (dateFooterY - 1f) : (float)Math.Round(panelY + panelH - 16f);
			float chartBottomY = runtimeShowStatGrid ? (bottomAfterFooterY - 61f) : (bottomAfterFooterY - 1f);
			float rightLimitX = (anyZones && ShowRightSideValues) ? (panelRight - RightValuesLaneWidth) : (panelRight - 4f);

			if (pxX < panelLeft || pxX > rightLimitX || pxY < canvasTopY || pxY > chartBottomY)
				return -1;

			float barSpacing = (cc.Properties != null && cc.Properties.BarDistance > 3f) ? cc.Properties.BarDistance : BarSpacePx;
			bool isFootprintView = (barSpacing >= CandlestickZoomThresholdPx);
			float halfHitW = isFootprintView
				? Math.Max(14f, barSpacing * 0.49f)
				: Math.Max(7.0f, barSpacing * 0.56f);

			int firstIdx = Math.Max(0, ChartBars.FromIndex - 1);
			int lastIdx = Math.Min(ChartBars.Count - 1, ChartBars.ToIndex + 1);
			int bestIdx = -1;
			float bestDx = float.MaxValue;

			for (int idx = firstIdx; idx <= lastIdx; idx++)
			{
				float cx = (float)cc.GetXByBarIndex(ChartBars, idx);
				float dx = Math.Abs(pxX - cx);
				if (dx > halfHitW || dx >= bestDx)
					continue;

				double low = ChartBars.Bars.GetLow(idx);
				double high = ChartBars.Bars.GetHigh(idx);
				if (isFootprintView)
				{
					lock (dataLock)
					{
						if (barCache.TryGetValue(idx, out FootprintBarData fp) && fp != null && fp.Levels != null && fp.Levels.Count > 0)
						{
							double hs = fp.EffectivePriceStep * 0.5;
							low = Math.Min(low, fp.Levels[0].Price - hs);
							high = Math.Max(high, fp.Levels[fp.Levels.Count - 1].Price + hs);
						}
					}
				}

				float y1 = (float)cs.GetYByValue(high);
				float y2 = (float)cs.GetYByValue(low);
				float yTop = Math.Min(y1, y2) - 16f;
				float yBot = Math.Max(y1, y2) + 20f;

				if (pxY >= yTop && pxY <= yBot)
				{
					bestDx = dx;
					bestIdx = idx;
				}
			}

			return bestIdx;
		}

		private void FocusCandleInImbalanceView(ChartControl cc, int targetBarIdx)
		{
			if (cc == null || ChartBars == null || ChartBars.Bars == null || targetBarIdx < 0 || targetBarIdx >= ChartBars.Count)
				return;

			EnsureAutoYAxisFor360View();

			// 1. Switch immediately into Imbalance View & highlight target candle
			runtimeOrderflowMode = AK10XOrderflowMode.Imbalance;
			FootprintMode = AK10XOrderflowMode.Imbalance;
			focusedCandleBarIndex = targetBarIdx;
			runtimeYZoomFactor = 1.0;
			freeRightPanOffsetPx = 0f;

			// 2. Zoom horizontally to spacious Inside-Box Imbalance width so Bid x Ask numbers & Halfpack are crisp
			float curDist = (cc.Properties != null && cc.Properties.BarDistance > 3f) ? cc.Properties.BarDistance : BarSpacePx;
			float targetDist = curDist >= 88f ? curDist : Math.Max(98f, (float)BarSpacePx);

			if (cc.Properties != null)
			{
				cc.Properties.BarDistance = targetDist;
				SyncOneBoxRightGapMargin(cc, targetDist, true);
				TriggerChartScrollbarRefresh();
			}

			// 3. Frame vertical price scale cleanly around the clicked candle so every Imbalance row is tall & readable
			double tick = TickSize > 0 ? TickSize : 0.05;
			double cLow = ChartBars.Bars.GetLow(targetBarIdx);
			double cHigh = ChartBars.Bars.GetHigh(targetBarIdx);
			double barStep = tick * Math.Max(1, runtimeTicksPerLevel);
			double focusCenterHint = 0.5 * (cHigh + cLow);
			lock (dataLock)
			{
				if (barCache.TryGetValue(targetBarIdx, out FootprintBarData cachedFp) && cachedFp != null && cachedFp.EffectivePriceStep > 0)
				{
					barStep = cachedFp.EffectivePriceStep;
					if (cachedFp.PocPrice >= cLow - barStep && cachedFp.PocPrice <= cHigh + barStep)
						focusCenterHint = cachedFp.PocPrice;
				}
				else if (UseSessionWiseOrderflow)
				{
					OrderflowSessionWindows win = BuildOrderflowSessionWindows();
					ResolveBarOrderflowParams(ChartBars.Bars.GetTime(targetBarIdx), win, barStep, tick, out double sStep, out _, out _, out _);
					if (sStep > 0) barStep = sStep;
				}
			}

			double cRange = Math.Max(barStep * 6.0, cHigh - cLow);

			int ctxStart = Math.Max(0, targetBarIdx - 2);
			int ctxEnd = Math.Min(ChartBars.Count - 1, targetBarIdx + 2);
			double ctxLow = cLow;
			double ctxHigh = cHigh;
			for (int i = ctxStart; i <= ctxEnd; i++)
			{
				double l = ChartBars.Bars.GetLow(i);
				double h = ChartBars.Bars.GetHigh(i);
				if (l < ctxLow) ctxLow = l;
				if (h > ctxHigh) ctxHigh = h;
			}

			float panelH = ChartPanel != null ? ChartPanel.H : 600f;
			float panelW = ChartPanel != null ? ChartPanel.W : 1200f;
			bool anyZones = runtimeShowMpZones || runtimeShowSrZones;
			bool showHdrFtr = anyZones && ShowSessionHeaderFooter;
			float footerReserve = 24f;
			float deltaBarReserve = footerReserve + (runtimeShowStatGrid ? 62f : 0f) + 14f;
			float topHeaderH = (panelW < 606f ? 66f : 40f) + (showHdrFtr ? 27f : 0f) + 18f;
			float usableH = Math.Max(150f, panelH - topHeaderH - deltaBarReserve);

			// Guarantee each Imbalance price level is >= 10.5px tall so 1-tick & 2-tick session numbers render crisply
			double maxReadableSpan = Math.Max(barStep * 10.0, (usableH / 10.5) * barStep);
			double focusLow;
			double focusHigh;
			if ((ctxHigh - ctxLow) <= cRange * 1.85 && (ctxHigh - ctxLow) * 1.24 <= maxReadableSpan)
			{
				double pad = Math.Max(barStep * 2.5, (ctxHigh - ctxLow) * 0.12);
				focusLow = ctxLow - pad;
				focusHigh = ctxHigh + pad;
			}
			else if (cRange * 1.18 <= maxReadableSpan)
			{
				double pad = Math.Min((maxReadableSpan - cRange) * 0.5, Math.Max(barStep * 2.5, cRange * 0.18));
				focusLow = cLow - pad;
				focusHigh = cHigh + pad;
			}
			else
			{
				double halfSpan = maxReadableSpan * 0.5;
				double mid = Math.Max(cLow + Math.Min(halfSpan, cRange * 0.5), Math.Min(cHigh - Math.Min(halfSpan, cRange * 0.5), focusCenterHint));
				focusLow = mid - halfSpan;
				focusHigh = mid + halfSpan;
			}

			double focusSpan = Math.Max(barStep * 8.0, focusHigh - focusLow);
			double pricePerPx = focusSpan / usableH;
			freeViewMaxPrice = focusHigh + topHeaderH * pricePerPx;
			freeViewMinPrice = focusLow - deltaBarReserve * pricePerPx;
			lastRenderedMinPrice = freeViewMinPrice;
			lastRenderedMaxPrice = freeViewMaxPrice;
			isFree360View = true;

			lock (dataLock)
			{
				lastComputedUniformStep = -1.0;
			}

			// 4. Center targetBarIdx horizontally in the chart canvas
			if (ChartPanel != null && cc.Properties != null)
			{
				float panelLeft = (float)ChartPanel.X;
				float panelRight = (float)(ChartPanel.X + ChartPanel.W);
				float rightLimitX = (anyZones && ShowRightSideValues) ? (panelRight - RightValuesLaneWidth) : (panelRight - 4f);
				float desiredCenterX = panelLeft + (rightLimitX - panelLeft) * 0.50f;
				float rightAnchorX = panelRight - Math.Max(28f, cc.Properties.BarMarginRightUser);

				var sb = hookedOfScrollBar ?? (hookedOfScrollBar = ResolveOrderflowScrollBar(cc));
				if (sb != null && sb.Maximum > sb.Minimum)
				{
					double barsFromRightAnchor = (rightAnchorX - desiredCenterX) / targetDist;
					double barsBackFromEnd = (ChartBars.Count - 1) - (targetBarIdx + barsFromRightAnchor);
					if (barsBackFromEnd < 0)
					{
						sb.Value = sb.Maximum;
						float overPx = Math.Min((rightLimitX - panelLeft) * 0.38f, (float)(-barsBackFromEnd * targetDist));
						if (overPx > 1f)
						{
							freeRightPanOffsetPx = overPx;
							SyncOneBoxRightGapMargin(cc, targetDist, true);
							TriggerChartScrollbarRefresh();
							if (sb.Maximum > sb.Minimum) sb.Value = sb.Maximum;
						}
					}
					else
					{
						sb.Value = Math.Max(sb.Minimum, Math.Min(sb.Maximum, sb.Maximum - barsBackFromEnd));
					}
				}

				int capturedBarIdx = targetBarIdx;
				float capturedCenterX = desiredCenterX;
				float capturedDist = targetDist;
				try
				{
					cc.Dispatcher.InvokeAsync(() =>
					{
						try
						{
							if (ChartBars == null || capturedBarIdx < 0 || capturedBarIdx >= ChartBars.Count)
								return;
							var sbVerify = hookedOfScrollBar ?? (hookedOfScrollBar = ResolveOrderflowScrollBar(cc));
							if (sbVerify != null && sbVerify.Maximum > sbVerify.Minimum && freeRightPanOffsetPx < 1f)
							{
								float actualX = (float)cc.GetXByBarIndex(ChartBars, capturedBarIdx);
								float errPx = actualX - capturedCenterX;
								if (Math.Abs(errPx) > capturedDist * 0.30f)
								{
									double corrected = Math.Max(sbVerify.Minimum, Math.Min(sbVerify.Maximum, sbVerify.Value + (errPx / capturedDist)));
									if (Math.Abs(corrected - sbVerify.Value) >= 0.02)
										sbVerify.Value = corrected;
								}
							}
							RequestImmediateOrderflowRedraw();
						}
						catch { }
					});
				}
				catch { }
			}

			RequestImmediateOrderflowRedraw();
		}

		private bool IsPointOverRightScaleLane(float pxX, float pxY)
		{
			if (ChartPanel == null || (!runtimeShowMpZones && !runtimeShowSrZones) || !ShowRightSideValues)
				return false;
			float panelRight = (float)(ChartPanel.X + ChartPanel.W);
			float rightLaneLeft = panelRight - RightValuesLaneWidth;
			float panelTop = (float)ChartPanel.Y;
			float panelBot = (float)(ChartPanel.Y + ChartPanel.H);
			return pxX > rightLaneLeft && pxX <= panelRight - 2f && pxY >= panelTop + 34f && pxY <= panelBot - 22f;
		}

		private bool BeginPotential360Interaction(ChartControl cc, float pxX, float pxY, int clickCount)
		{
			if (ChartPanel == null || cc == null)
				return false;

			float panelLeft = (float)ChartPanel.X;
			float panelRight = (float)(ChartPanel.X + ChartPanel.W);
			float panelTop = (float)ChartPanel.Y;
			float panelBot = (float)(ChartPanel.Y + ChartPanel.H);
			if (pxX < panelLeft || pxX > panelRight - 2f || pxY < panelTop + 4f || pxY > panelBot - 4f)
				return false;

			// 0A. Live Position / Working TP / SL / Entry Drag & Quick Action Pills Priority
			if (pxY >= panelTop + 34f && TryBeginLiveOrderCanvasInteraction(cc, pxX, pxY))
				return true;

			// 0B. Interactive Long / Short R:R Position Placement & Handle Scaling Priority
			if (pxY >= panelTop + 34f && TryBeginRrBoxInteraction(cc, pxX, pxY, clickCount))
				return true;

			DateTime nowUtc = DateTime.UtcNow;
			if ((nowUtc - lastDoubleClickHandledUtc).TotalMilliseconds < 180.0)
				return true;

			double dtClickMs = (nowUtc - lastCanvasClickUtc).TotalMilliseconds;
			if (clickCount < 2 && dtClickMs >= 0.0 && dtClickMs < 42.0)
				return false;

			bool isDoubleClick = clickCount >= 2 ||
				(dtClickMs >= 50.0 && dtClickMs <= 380.0 &&
				 Math.Abs(pxX - lastCanvasClickX) <= 12f &&
				 Math.Abs(pxY - lastCanvasClickY) <= 12f);

			if (isDoubleClick && pxY >= panelTop + 34f)
			{
				lastDoubleClickHandledUtc = nowUtc;
				lastCanvasClickUtc = DateTime.MinValue;
				isPendingOfCanvasDrag = false;
				isDraggingFootprintCanvas = false;
				isPendingVerticalScaleDrag = false;
				isDraggingVerticalScale = false;

				int hitCandleIdx = FindCandleAtPoint(cc, pxX, pxY);
				if (hitCandleIdx >= 0)
				{
					FocusCandleInImbalanceView(cc, hitCandleIdx);
				}
				else
				{
					Reset360ViewToAutoFit(cc, snapLiveIfAlreadyFit: true);
				}
				return true;
			}

			lastCanvasClickUtc = nowUtc;
			lastCanvasClickX = pxX;
			lastCanvasClickY = pxY;

			EnsureAutoYAxisFor360View();

			var sb = hookedOfScrollBar ?? (hookedOfScrollBar = ResolveOrderflowScrollBar(cc));
			ofDragStartMouseX = pxX;
			ofDragStartMouseY = pxY;
			ofDragStartScrollVal = (sb != null) ? sb.Value : 0.0;
			ofDragStartRightOffsetPx = freeRightPanOffsetPx;

			double curMin = !double.IsNaN(lastRenderedMinPrice) ? lastRenderedMinPrice : MinValue;
			double curMax = !double.IsNaN(lastRenderedMaxPrice) ? lastRenderedMaxPrice : MaxValue;
			if (double.IsNaN(curMin) || double.IsNaN(curMax) || curMax <= curMin)
			{
				curMin = 0.0;
				curMax = 100.0;
			}
			ofDragStartMinPrice = curMin;
			ofDragStartMaxPrice = curMax;

			if (IsPointOverRightScaleLane(pxX, pxY))
			{
				isPendingVerticalScaleDrag = true;
				isDraggingVerticalScale = false;
				isPendingOfCanvasDrag = false;
				isDraggingFootprintCanvas = false;
			}
			else
			{
				isPendingOfCanvasDrag = true;
				isDraggingFootprintCanvas = false;
				isPendingVerticalScaleDrag = false;
				isDraggingVerticalScale = false;
			}
			return false;
		}

		private bool Update360DragMove(ChartControl cc, float curPxX, float curPxY)
		{
			if (cc == null)
				return false;

			if (activeLiveOrderDragMode != LiveOrderDragMode.None)
				return UpdateLiveOrderCanvasDrag(cc, curPxX, curPxY);

			if (activeRrDragMode != RrDragMode.None && activeDragRrBox != null)
				return UpdateRrBoxDragMove(cc, curPxX, curPxY);

			float deltaX = curPxX - ofDragStartMouseX;
			float deltaY = curPxY - ofDragStartMouseY;
			double tick = TickSize > 0 ? TickSize : 0.05;

			// 1. Right-Side MP ZONE VALUES Lane: Smooth Vertical Price Stretch / Compress
			if (isPendingVerticalScaleDrag || isDraggingVerticalScale)
			{
				if (!isDraggingVerticalScale)
				{
					if (Math.Abs(deltaY) < 2.5f)
						return false;
					isDraggingVerticalScale = true;
					isPendingVerticalScaleDrag = false;
				}

				double startSpan = Math.Max(tick * 6.0, ofDragStartMaxPrice - ofDragStartMinPrice);
				double midPrice = 0.5 * (ofDragStartMaxPrice + ofDragStartMinPrice);
				double scaleFactor = Math.Pow(1.0065, deltaY);
				double newSpan = Math.Max(tick * 8.0, Math.Min(startSpan * 25.0, startSpan * scaleFactor));

				freeViewMinPrice = midPrice - 0.5 * newSpan;
				freeViewMaxPrice = midPrice + 0.5 * newSpan;
				isFree360View = true;
				EnsureAutoYAxisFor360View();
				RequestImmediateOrderflowRedraw();
				return true;
			}

			// 2. Main Canvas: Smooth 360-Degree Omni-Directional Pan (Horizontal X + Vertical Y simultaneously)
			if (isPendingOfCanvasDrag || isDraggingFootprintCanvas)
			{
				if (!isDraggingFootprintCanvas)
				{
					if (Math.Abs(deltaX) < 3.0f && Math.Abs(deltaY) < 3.0f)
						return false;
					isDraggingFootprintCanvas = true;
					isPendingOfCanvasDrag = false;
				}

				bool changed = false;
				float barDist = (cc.Properties != null && cc.Properties.BarDistance > 3f) ? cc.Properties.BarDistance : 48f;
				float maxRightPanPx = ChartPanel != null ? Math.Max(240f, ChartPanel.W * 0.65f) : 640f;
				var sb = hookedOfScrollBar ?? (hookedOfScrollBar = ResolveOrderflowScrollBar(cc));

				// Horizontal (X) Pan — works across history AND smoothly into right-hand space past live candle
				if (sb == null || sb.Maximum <= sb.Minimum)
				{
					float targetRight = Math.Max(0f, Math.Min(maxRightPanPx, ofDragStartRightOffsetPx - deltaX));
					if (Math.Abs(targetRight - freeRightPanOffsetPx) >= 0.75f)
					{
						freeRightPanOffsetPx = targetRight;
						SyncOneBoxRightGapMargin(cc, barDist, true);
						TriggerChartScrollbarRefresh();
						changed = true;
					}
				}
				else if (ofDragStartRightOffsetPx > 0.5f)
				{
					float rawRight = ofDragStartRightOffsetPx - deltaX;
					if (rawRight > 0f)
					{
						float clampedRight = Math.Min(maxRightPanPx, rawRight);
						if (Math.Abs(clampedRight - freeRightPanOffsetPx) >= 0.75f)
						{
							freeRightPanOffsetPx = clampedRight;
							SyncOneBoxRightGapMargin(cc, barDist, true);
							TriggerChartScrollbarRefresh();
							if (sb.Maximum > sb.Minimum) sb.Value = sb.Maximum;
							changed = true;
						}
					}
					else
					{
						if (freeRightPanOffsetPx > 0.1f)
						{
							freeRightPanOffsetPx = 0f;
							SyncOneBoxRightGapMargin(cc, barDist, true);
							TriggerChartScrollbarRefresh();
							changed = true;
						}
						double remBars = (-rawRight) / barDist;
						double targetVal = Math.Max(sb.Minimum, Math.Min(sb.Maximum, sb.Maximum - remBars));
						if (Math.Abs(sb.Value - targetVal) >= 0.01)
						{
							sb.Value = targetVal;
							changed = true;
						}
					}
				}
				else
				{
					double unclampedVal = ofDragStartScrollVal - (deltaX / barDist);
					if (unclampedVal <= sb.Maximum)
					{
						if (freeRightPanOffsetPx > 0.1f)
						{
							freeRightPanOffsetPx = 0f;
							SyncOneBoxRightGapMargin(cc, barDist, true);
							TriggerChartScrollbarRefresh();
							changed = true;
						}
						double targetVal = Math.Max(sb.Minimum, unclampedVal);
						if (Math.Abs(sb.Value - targetVal) >= 0.01)
						{
							sb.Value = targetVal;
							changed = true;
						}
					}
					else
					{
						if (Math.Abs(sb.Value - sb.Maximum) >= 0.01)
						{
							sb.Value = sb.Maximum;
							changed = true;
						}
						float overPx = (float)((unclampedVal - sb.Maximum) * barDist);
						float clampedRight = Math.Max(0f, Math.Min(maxRightPanPx, overPx));
						if (Math.Abs(clampedRight - freeRightPanOffsetPx) >= 0.75f)
						{
							freeRightPanOffsetPx = clampedRight;
							SyncOneBoxRightGapMargin(cc, barDist, true);
							TriggerChartScrollbarRefresh();
							if (sb.Maximum > sb.Minimum) sb.Value = sb.Maximum;
							changed = true;
						}
					}
				}

				// Vertical (Y) Pan — locked 1:1 in world price coordinates for true 360-degree glide
				float effPanelH = Math.Max(140f, lastRenderedPanelH > 50f ? lastRenderedPanelH : (ChartPanel != null ? ChartPanel.H : 600f));
				double startSpan = Math.Max(tick * 6.0, ofDragStartMaxPrice - ofDragStartMinPrice);
				double priceShift = (deltaY / effPanelH) * startSpan;
				double nextMin = ofDragStartMinPrice + priceShift;
				double nextMax = ofDragStartMaxPrice + priceShift;

				if (!isFree360View ||
					double.IsNaN(freeViewMinPrice) ||
					Math.Abs(nextMin - freeViewMinPrice) >= tick * 0.05)
				{
					freeViewMinPrice = nextMin;
					freeViewMaxPrice = nextMax;
					isFree360View = true;
					EnsureAutoYAxisFor360View();
					changed = true;
				}

				if (changed)
					RequestImmediateOrderflowRedraw();
				return true;
			}

			return false;
		}

		private void End360Interaction(ChartControl cc)
		{
			if (activeLiveOrderDragMode != LiveOrderDragMode.None)
				CommitLiveOrderCanvasDrag(cc);

			bool wasDragging = isDraggingFootprintCanvas || isDraggingVerticalScale || activeRrDragMode != RrDragMode.None || activeLiveOrderDragMode != LiveOrderDragMode.None;
			isPendingOfCanvasDrag = false;
			isDraggingFootprintCanvas = false;
			isPendingVerticalScaleDrag = false;
			isDraggingVerticalScale = false;
			activeRrDragMode = RrDragMode.None;
			activeDragRrBox = null;
			activeLiveOrderDragMode = LiveOrderDragMode.None;
			activeDraggedWorkingOrder = null;
			try
			{
				if (cc != null)
				{
					if (cc.IsMouseCaptured) cc.ReleaseMouseCapture();
					if (cc.Cursor == Cursors.SizeWE || cc.Cursor == Cursors.SizeAll || cc.Cursor == Cursors.SizeNS)
						cc.Cursor = null;
				}
			}
			catch { }

			if (wasDragging)
				RequestImmediateOrderflowRedraw();
		}

		private void ApplyVerticalPriceWheelZoom(int rawDelta, float cursorPxY, bool hasCursorY)
		{
			EnsureAutoYAxisFor360View();
			double tick = TickSize > 0 ? TickSize : 0.05;
			double curMin = !double.IsNaN(lastRenderedMinPrice) ? lastRenderedMinPrice : MinValue;
			double curMax = !double.IsNaN(lastRenderedMaxPrice) ? lastRenderedMaxPrice : MaxValue;
			if (double.IsNaN(curMin) || double.IsNaN(curMax) || curMax <= curMin)
				return;

			double curSpan = Math.Max(tick * 6.0, curMax - curMin);
			float pY = lastRenderedPanelY;
			float pH = Math.Max(140f, lastRenderedPanelH > 50f ? lastRenderedPanelH : (ChartPanel != null ? ChartPanel.H : 600f));
			double relFromTop = (hasCursorY && cursorPxY >= pY && cursorPxY <= pY + pH)
				? Math.Max(0.05, Math.Min(0.95, (cursorPxY - pY) / pH))
				: 0.5;

			double anchorPrice = curMax - relFromTop * curSpan;
			double notches = Math.Max(-3.5, Math.Min(3.5, rawDelta / 120.0));
			if (Math.Abs(notches) < 0.04)
				notches = rawDelta > 0 ? 0.20 : -0.20;

			double newSpan = Math.Max(tick * 8.0, Math.Min(curSpan * 25.0, curSpan / Math.Pow(1.14, notches)));
			freeViewMaxPrice = anchorPrice + relFromTop * newSpan;
			freeViewMinPrice = freeViewMaxPrice - newSpan;
			isFree360View = true;
			RequestImmediateOrderflowRedraw();
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

				BeginPotential360Interaction(cc, pxX, pxY, e.Clicks);
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

			if (!isDraggingFootprintCanvas && !isPendingOfCanvasDrag && !isDraggingVerticalScale && !isPendingVerticalScaleDrag && activeRrDragMode == RrDragMode.None && activeLiveOrderDragMode == LiveOrderDragMode.None)
			{
				int overIdx = HitTestToolbarButtonAt(curPxX, curPxY);
				int overLiveIdx = overIdx < 0 ? HitTestLiveOrderRegionIndex(curPxX, curPxY, out _) : -1;
				int overRrId = (overIdx < 0 && overLiveIdx < 0) ? ResolveHoveredRrBoxId(cc, curPxX, curPxY) : -1;
				bool needRedraw = false;
				if (overIdx != hoveredToolbarBtnIndex)
				{
					hoveredToolbarBtnIndex = overIdx;
					needRedraw = true;
				}
				if (overLiveIdx != hoveredLiveHitRegionIdx)
				{
					hoveredLiveHitRegionIdx = overLiveIdx;
					needRedraw = true;
				}
				if (overRrId != hoveredRrBoxId)
				{
					hoveredRrBoxId = overRrId;
					needRedraw = true;
				}
				if (pendingRrToolMode != 0 && (Math.Abs(curPxX - rrPreviewMouseX) >= 1.5f || Math.Abs(curPxY - rrPreviewMouseY) >= 1.5f))
				{
					rrPreviewMouseX = curPxX;
					rrPreviewMouseY = curPxY;
					needRedraw = true;
				}
				if (needRedraw)
					RequestImmediateOrderflowRedraw();
				return;
			}

			if ((e.Button & System.Windows.Forms.MouseButtons.Left) != System.Windows.Forms.MouseButtons.Left)
			{
				End360Interaction(cc);
				return;
			}

			try
			{
				Update360DragMove(cc, curPxX, curPxY);
			}
			catch { }
		}

		private void OnD2DFormMouseUp(object sender, System.Windows.Forms.MouseEventArgs e)
		{
			End360Interaction(hookedOrderflowCC ?? ChartControl);
		}

		private void OnD2DFormMouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
		{
			if (e == null || e.Delta == 0)
				return;
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (!IsOrderflowChartControlActive(cc))
				return;

			ProcessUniversalSmoothWheel(cc, e.Delta, e.X, e.Y, true);
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
				if (tradePanelRoot != null && tradePanelRoot.IsMouseOver)
					return;
				System.Windows.Point pt = e.GetPosition(cc);
				if (pt.X < -20 || pt.Y < -20 || pt.X > cc.ActualWidth || pt.Y > cc.ActualHeight + 40)
					return;
				float pxX = ConvertOfHorizontalPx(cc, pt.X);
				float pxY = ConvertOfVerticalPx(cc, pt.Y);
				if (ProcessUniversalSmoothWheel(cc, e.Delta, pxX, pxY, true))
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

		private void OnChartPreviewKeyDown(object sender, KeyEventArgs e)
		{
			if (e == null)
				return;
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (!IsOrderflowChartControlActive(cc))
				return;

			if (e.Key == Key.Escape)
			{
				if (activeLiveOrderDragMode != LiveOrderDragMode.None)
				{
					activeLiveOrderDragMode = LiveOrderDragMode.None;
					activeDraggedWorkingOrder = null;
					e.Handled = true;
					RequestImmediateOrderflowRedraw();
				}
				else if (pendingRrToolMode != 0)
				{
					pendingRrToolMode = 0;
					rrPreviewMouseX = -9999f;
					rrPreviewMouseY = -9999f;
					e.Handled = true;
					RequestImmediateOrderflowRedraw();
				}
				else if (selectedRrBoxId >= 0)
				{
					selectedRrBoxId = -1;
					e.Handled = true;
					RequestImmediateOrderflowRedraw();
				}
			}
			else if ((e.Key == Key.Delete || e.Key == Key.Back) && selectedRrBoxId >= 0)
			{
				for (int i = rrPositionBoxes.Count - 1; i >= 0; i--)
				{
					if (rrPositionBoxes[i].Id == selectedRrBoxId)
					{
						rrPositionBoxes.RemoveAt(i);
						selectedRrBoxId = -1;
						activeRrDragMode = RrDragMode.None;
						activeDragRrBox = null;
						e.Handled = true;
						RequestImmediateOrderflowRedraw();
						break;
					}
				}
			}
		}

		private float SlotToCanvasX(ChartControl cc, double barSlot, float barSpacing)
		{
			if (cc == null || ChartBars == null || ChartBars.Count == 0)
				return 0f;

			int lastIdx = ChartBars.Count - 1;
			if (barSlot <= lastIdx)
			{
				int idx = Math.Max(0, Math.Min(lastIdx, (int)Math.Round(barSlot)));
				return (float)cc.GetXByBarIndex(ChartBars, idx);
			}

			float xLast = (float)cc.GetXByBarIndex(ChartBars, lastIdx);
			return xLast + (float)(barSlot - lastIdx) * Math.Max(4f, barSpacing);
		}

		private double CanvasXToBarSlot(ChartControl cc, float pxX, float barSpacing)
		{
			if (cc == null || ChartBars == null || ChartBars.Count == 0)
				return 0.0;

			int lastIdx = ChartBars.Count - 1;
			float safeSpacing = Math.Max(4f, barSpacing);
			float xLast = (float)cc.GetXByBarIndex(ChartBars, lastIdx);
			if (pxX >= xLast + safeSpacing * 0.45f)
			{
				return lastIdx + Math.Max(1.0, Math.Round((pxX - xLast) / safeSpacing));
			}

			int fIdx = Math.Max(0, ChartBars.FromIndex - 3);
			int tIdx = Math.Min(lastIdx, ChartBars.ToIndex + 3);
			int bestIdx = lastIdx;
			float bestDist = Math.Abs(pxX - xLast);
			for (int i = fIdx; i <= tIdx; i++)
			{
				float bx = (float)cc.GetXByBarIndex(ChartBars, i);
				float d = Math.Abs(pxX - bx);
				if (d < bestDist)
				{
					bestDist = d;
					bestIdx = i;
				}
			}
			return bestIdx;
		}

		private RrDragMode HitTestSingleRrBox(ChartControl cc, RrPositionBox box, float pxX, float pxY, float barSpacing, out bool hitDeleteBtn)
		{
			hitDeleteBtn = false;
			ChartScale cs = lastRenderedChartScale;
			if (cc == null || cs == null || box == null || ChartBars == null || ChartBars.Count == 0)
				return RrDragMode.None;

			if (box.Id == selectedRrBoxId && box.LastDeleteBtnRect.Width > 0f && ContainsPtPadded(box.LastDeleteBtnRect, pxX, pxY, 3f))
			{
				hitDeleteBtn = true;
				return RrDragMode.None;
			}

			float xLeft = SlotToCanvasX(cc, box.StartBarSlot, barSpacing);
			float xRight = Math.Max(xLeft + 24f, SlotToCanvasX(cc, box.EndBarSlot, barSpacing));
			float yEntry = (float)cs.GetYByValue(box.EntryPrice);
			float yTarget = (float)cs.GetYByValue(box.TargetPrice);
			float yStop = (float)cs.GetYByValue(box.StopPrice);
			float yTop = Math.Min(yTarget, yStop);
			float yBot = Math.Max(yTarget, yStop);

			// 1. 4 Circular TradingView Handles (highest priority)
			if (Math.Abs(pxX - xLeft) <= 9.5f && Math.Abs(pxY - yTarget) <= 9.5f)
				return RrDragMode.ScaleTarget;
			if (Math.Abs(pxX - xLeft) <= 9.5f && Math.Abs(pxY - yStop) <= 9.5f)
				return RrDragMode.ScaleStop;
			if (Math.Abs(pxX - xLeft) <= 9.5f && Math.Abs(pxY - yEntry) <= 9.5f)
				return RrDragMode.ScaleEntryAndStart;
			if (Math.Abs(pxX - xRight) <= 9.5f && Math.Abs(pxY - yEntry) <= 9.5f)
				return RrDragMode.ScaleEnd;

			// 2. Horizontal Target / Stop Edges & Right Vertical Edge
			if (pxX >= xLeft - 5f && pxX <= xRight + 5f)
			{
				if (Math.Abs(pxY - yTarget) <= 6.5f)
					return RrDragMode.ScaleTarget;
				if (Math.Abs(pxY - yStop) <= 6.5f)
					return RrDragMode.ScaleStop;
			}
			if (Math.Abs(pxX - xRight) <= 6.5f && pxY >= yTop - 4f && pxY <= yBot + 4f)
				return RrDragMode.ScaleEnd;

			// 3. Interior Box Body / Entry Line
			if (pxX >= xLeft && pxX <= xRight && pxY >= yTop && pxY <= yBot)
				return RrDragMode.PendingMove;

			return RrDragMode.None;
		}

		private int ResolveHoveredRrBoxId(ChartControl cc, float pxX, float pxY)
		{
			if (rrPositionBoxes.Count == 0 || cc == null || ChartPanel == null || pxY < ChartPanel.Y + 34f || IsPointOverRightScaleLane(pxX, pxY))
				return -1;

			float barSpacing = (cc.Properties != null && cc.Properties.BarDistance > 3f) ? cc.Properties.BarDistance : BarSpacePx;
			for (int pass = 0; pass < 2; pass++)
			{
				for (int i = rrPositionBoxes.Count - 1; i >= 0; i--)
				{
					RrPositionBox box = rrPositionBoxes[i];
					bool isSel = (box.Id == selectedRrBoxId);
					if ((pass == 0 && !isSel) || (pass == 1 && isSel))
						continue;

					if (box.LastFixedRrRect.Width > 0f && ContainsPtPadded(box.LastFixedRrRect, pxX, pxY, 3f))
						return box.Id;

					RrDragMode mode = HitTestSingleRrBox(cc, box, pxX, pxY, barSpacing, out bool hitDel);
					if (hitDel || mode != RrDragMode.None)
						return box.Id;
				}
			}
			return -1;
		}

		private Cursor ResolveRrHoverCursor(ChartControl cc, float pxX, float pxY)
		{
			if (ChartPanel == null || pxY < ChartPanel.Y + 34f || IsPointOverRightScaleLane(pxX, pxY))
				return null;

			if (pendingRrToolMode != 0)
				return Cursors.Cross;

			if (rrPositionBoxes.Count == 0 || cc == null)
				return null;

			float barSpacing = (cc.Properties != null && cc.Properties.BarDistance > 3f) ? cc.Properties.BarDistance : BarSpacePx;

			// Check selected box first, then reverse order
			for (int pass = 0; pass < 2; pass++)
			{
				for (int i = rrPositionBoxes.Count - 1; i >= 0; i--)
				{
					RrPositionBox box = rrPositionBoxes[i];
					bool isSel = (box.Id == selectedRrBoxId);
					if ((pass == 0 && !isSel) || (pass == 1 && isSel))
						continue;

					RrDragMode mode = HitTestSingleRrBox(cc, box, pxX, pxY, barSpacing, out bool hitDel);
					if (hitDel)
						return Cursors.Hand;
					if (mode == RrDragMode.ScaleTarget || mode == RrDragMode.ScaleStop)
						return Cursors.SizeNS;
					if (mode == RrDragMode.ScaleEnd)
						return Cursors.SizeWE;
					if (mode == RrDragMode.ScaleEntryAndStart || mode == RrDragMode.PendingMove)
						return Cursors.SizeAll;
				}
			}

			return null;
		}

		private bool TryBeginRrBoxInteraction(ChartControl cc, float pxX, float pxY, int clickCount)
		{
			ChartScale cs = lastRenderedChartScale;
			if (cc == null || cs == null || ChartBars == null || ChartBars.Count == 0 || IsPointOverRightScaleLane(pxX, pxY))
				return false;

			DateTime nowUtc = DateTime.UtcNow;
			if ((nowUtc - lastRrMouseDownUtc).TotalMilliseconds < 45.0)
				return (pendingRrToolMode != 0 || activeRrDragMode != RrDragMode.None);

			float barSpacing = (cc.Properties != null && cc.Properties.BarDistance > 3f) ? cc.Properties.BarDistance : BarSpacePx;
			double tick = TickSize > 0 ? TickSize : 0.05;

			// 1. Place Armed Long / Short Position Box on Single Click
			if (pendingRrToolMode != 0)
			{
				lastRrMouseDownUtc = nowUtc;
				bool placeLong = (pendingRrToolMode == 1);
				double entryPrice = RoundToInstrumentTick(cs.GetValueByY(pxY));
				double startSlot = CanvasXToBarSlot(cc, pxX, barSpacing);
				int lastIdx = ChartBars.Count - 1;
				bool nearLive = (startSlot >= lastIdx - 1);
				double endSlot = nearLive ? Math.Max(startSlot + 10.0, lastIdx + 8.0) : (startSlot + 12.0);

				double visSpan = (!double.IsNaN(lastRenderedMaxPrice) && !double.IsNaN(lastRenderedMinPrice) && lastRenderedMaxPrice > lastRenderedMinPrice)
					? (lastRenderedMaxPrice - lastRenderedMinPrice)
					: Math.Abs(cs.MaxValue - cs.MinValue);
				double slDist = Math.Max(tick * 5.0, RoundToInstrumentTick(visSpan * 0.10));
				double rrMult = Math.Max(0.5, RrDefaultRatio);
				double tpDist = Math.Max(tick * 5.0, RoundToInstrumentTick(slDist * rrMult));

				var newBox = new RrPositionBox
				{
					Id = nextRrBoxId++,
					IsLong = placeLong,
					StartBarSlot = startSlot,
					EndBarSlot = endSlot,
					EntryPrice = entryPrice,
					TargetPrice = placeLong ? RoundToInstrumentTick(entryPrice + tpDist) : RoundToInstrumentTick(entryPrice - tpDist),
					StopPrice = placeLong ? RoundToInstrumentTick(entryPrice - slDist) : RoundToInstrumentTick(entryPrice + slDist),
					AutoExtendLive = nearLive
				};

				rrPositionBoxes.Add(newBox);
				selectedRrBoxId = newBox.Id;
				pendingRrToolMode = 0;
				rrPreviewMouseX = -9999f;
				rrPreviewMouseY = -9999f;

				// Ensure comfortable right-hand space when placing a live order tracker at the right edge
				if (nearLive && freeRightPanOffsetPx < barSpacing * 4f)
				{
					freeRightPanOffsetPx = Math.Max(freeRightPanOffsetPx, Math.Min(320f, barSpacing * 5f));
					SyncOneBoxRightGapMargin(cc, barSpacing, true);
					TriggerChartScrollbarRefresh();
				}

				EnsureAutoYAxisFor360View();
				RequestImmediateOrderflowRedraw();
				return true;
			}

			if (rrPositionBoxes.Count == 0)
				return false;

			// 2. Hit-Test Existing R:R Boxes (Selected Box First)
			for (int pass = 0; pass < 2; pass++)
			{
				for (int i = rrPositionBoxes.Count - 1; i >= 0; i--)
				{
					RrPositionBox box = rrPositionBoxes[i];
					bool isSel = (box.Id == selectedRrBoxId);
					if ((pass == 0 && !isSel) || (pass == 1 && isSel))
						continue;

					RrDragMode hitMode = HitTestSingleRrBox(cc, box, pxX, pxY, barSpacing, out bool hitDel);
					if (hitDel)
					{
						lastRrMouseDownUtc = nowUtc;
						rrPositionBoxes.RemoveAt(i);
						selectedRrBoxId = -1;
						activeRrDragMode = RrDragMode.None;
						activeDragRrBox = null;
						RequestImmediateOrderflowRedraw();
						return true;
					}

					if (hitMode != RrDragMode.None)
					{
						// Allow double-clicking a candle inside the shaded interior (not on a handle/edge) to still open Imbalance View
						if (clickCount >= 2 && hitMode == RrDragMode.PendingMove)
							return false;

						lastRrMouseDownUtc = nowUtc;
						selectedRrBoxId = box.Id;
						activeDragRrBox = box;
						activeRrDragMode = hitMode;
						rrDragStartMouseX = pxX;
						rrDragStartMouseY = pxY;
						rrDragOrigStartSlot = box.StartBarSlot;
						rrDragOrigEndSlot = box.EndBarSlot;
						rrDragOrigEntry = box.EntryPrice;
						rrDragOrigTarget = box.TargetPrice;
						rrDragOrigStop = box.StopPrice;
						EnsureAutoYAxisFor360View();
						RequestImmediateOrderflowRedraw();
						return true;
					}
				}
			}

			// Clicked empty canvas -> deselect active R:R box cleanly without blocking 360° pan
			if (selectedRrBoxId >= 0)
			{
				selectedRrBoxId = -1;
				RequestImmediateOrderflowRedraw();
			}

			return false;
		}

		private bool UpdateRrBoxDragMove(ChartControl cc, float curPxX, float curPxY)
		{
			ChartScale cs = lastRenderedChartScale;
			RrPositionBox box = activeDragRrBox;
			if (cc == null || cs == null || box == null || ChartBars == null || ChartBars.Count == 0)
				return false;

			double tick = TickSize > 0 ? TickSize : 0.05;
			float barSpacing = (cc.Properties != null && cc.Properties.BarDistance > 3f) ? cc.Properties.BarDistance : BarSpacePx;

			if (activeRrDragMode == RrDragMode.PendingMove)
			{
				if (Math.Abs(curPxX - rrDragStartMouseX) < 3.0f && Math.Abs(curPxY - rrDragStartMouseY) < 3.0f)
					return false;
				activeRrDragMode = RrDragMode.MoveWholeBox;
			}

			switch (activeRrDragMode)
			{
				case RrDragMode.ScaleTarget:
				{
					double p = RoundToInstrumentTick(cs.GetValueByY(curPxY));
					box.TargetPrice = box.IsLong
						? Math.Max(RoundToInstrumentTick(box.EntryPrice + tick), p)
						: Math.Min(RoundToInstrumentTick(box.EntryPrice - tick), p);
					break;
				}
				case RrDragMode.ScaleStop:
				{
					double p = RoundToInstrumentTick(cs.GetValueByY(curPxY));
					box.StopPrice = box.IsLong
						? Math.Min(RoundToInstrumentTick(box.EntryPrice - tick), p)
						: Math.Max(RoundToInstrumentTick(box.EntryPrice + tick), p);
					break;
				}
				case RrDragMode.ScaleEntryAndStart:
				{
					double p = RoundToInstrumentTick(cs.GetValueByY(curPxY));
					if (box.IsLong)
						box.EntryPrice = Math.Max(RoundToInstrumentTick(box.StopPrice + tick), Math.Min(RoundToInstrumentTick(box.TargetPrice - tick), p));
					else
						box.EntryPrice = Math.Max(RoundToInstrumentTick(box.TargetPrice + tick), Math.Min(RoundToInstrumentTick(box.StopPrice - tick), p));

					double nextStart = Math.Min(box.EndBarSlot - 1.0, Math.Max(0.0, CanvasXToBarSlot(cc, curPxX, barSpacing)));
					box.StartBarSlot = nextStart;
					break;
				}
				case RrDragMode.ScaleEnd:
				{
					double nextEnd = Math.Max(box.StartBarSlot + 1.0, CanvasXToBarSlot(cc, curPxX, barSpacing));
					box.EndBarSlot = nextEnd;
					box.AutoExtendLive = (nextEnd >= ChartBars.Count - 1);
					break;
				}
				case RrDragMode.MoveWholeBox:
				{
					double dPrice = RoundToInstrumentTick(cs.GetValueByY(curPxY) - cs.GetValueByY(rrDragStartMouseY));
					double curSlot = CanvasXToBarSlot(cc, curPxX, barSpacing);
					double origMouseSlot = CanvasXToBarSlot(cc, rrDragStartMouseX, barSpacing);
					double dSlot = curSlot - origMouseSlot;
					double widthSlots = Math.Max(1.0, rrDragOrigEndSlot - rrDragOrigStartSlot);

					box.StartBarSlot = Math.Max(0.0, rrDragOrigStartSlot + dSlot);
					box.EndBarSlot = box.StartBarSlot + widthSlots;
					box.EntryPrice = RoundToInstrumentTick(rrDragOrigEntry + dPrice);
					box.TargetPrice = RoundToInstrumentTick(rrDragOrigTarget + dPrice);
					box.StopPrice = RoundToInstrumentTick(rrDragOrigStop + dPrice);
					box.AutoExtendLive = (box.EndBarSlot >= ChartBars.Count - 1);
					break;
				}
			}

			RequestImmediateOrderflowRedraw();
			return true;
		}

		private void OnChartMouseLeave(object sender, MouseEventArgs e)
		{
			if (hoveredToolbarBtnIndex != -1 || hoveredRrBoxId != -1 || hoveredLiveHitRegionIdx != -1 || rrPreviewMouseX > -1000f)
			{
				hoveredToolbarBtnIndex = -1;
				hoveredRrBoxId = -1;
				hoveredLiveHitRegionIdx = -1;
				rrPreviewMouseX = -9999f;
				rrPreviewMouseY = -9999f;
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
				if (BeginPotential360Interaction(cc, pxX, pxY, e.ClickCount))
				{
					lastHandledOfMouseTimestamp = e.Timestamp;
					e.Handled = true;
				}
			}
			catch { }
		}

		private void OnChartMouseMove(object sender, MouseEventArgs e)
		{
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (!IsOrderflowChartControlActive(cc) || e == null)
				return;

			if (!isDraggingFootprintCanvas && !isPendingOfCanvasDrag && !isDraggingVerticalScale && !isPendingVerticalScaleDrag && activeRrDragMode == RrDragMode.None && activeLiveOrderDragMode == LiveOrderDragMode.None)
			{
				try
				{
					System.Windows.Point pt = e.GetPosition(cc);
					float pxX = ConvertOfHorizontalPx(cc, pt.X);
					float pxY = ConvertOfVerticalPx(cc, pt.Y);

					int overIdx = ResolveToolbarButtonFromMouseEvent(cc, e);
					Cursor liveCursor = null;
					int overLiveIdx = overIdx < 0 ? HitTestLiveOrderRegionIndex(pxX, pxY, out liveCursor) : -1;
					int overRrId = (overIdx < 0 && overLiveIdx < 0) ? ResolveHoveredRrBoxId(cc, pxX, pxY) : -1;
					bool needRedraw = false;
					if (overIdx != hoveredToolbarBtnIndex)
					{
						hoveredToolbarBtnIndex = overIdx;
						needRedraw = true;
					}
					if (overLiveIdx != hoveredLiveHitRegionIdx)
					{
						hoveredLiveHitRegionIdx = overLiveIdx;
						needRedraw = true;
					}
					if (overRrId != hoveredRrBoxId)
					{
						hoveredRrBoxId = overRrId;
						needRedraw = true;
					}
					if (pendingRrToolMode != 0 && (Math.Abs(pxX - rrPreviewMouseX) >= 1.5f || Math.Abs(pxY - rrPreviewMouseY) >= 1.5f))
					{
						rrPreviewMouseX = pxX;
						rrPreviewMouseY = pxY;
						needRedraw = true;
					}
					if (needRedraw)
						RequestImmediateOrderflowRedraw();

					bool overScaleLane = overIdx < 0 && overLiveIdx < 0 && IsPointOverRightScaleLane(pxX, pxY);
					Cursor rrCursor = (overIdx < 0 && overLiveIdx < 0) ? ResolveRrHoverCursor(cc, pxX, pxY) : null;

					if (overIdx >= 0)
					{
						if (cc.Cursor != Cursors.Hand) cc.Cursor = Cursors.Hand;
					}
					else if (liveCursor != null)
					{
						if (cc.Cursor != liveCursor) cc.Cursor = liveCursor;
					}
					else if (rrCursor != null)
					{
						if (cc.Cursor != rrCursor) cc.Cursor = rrCursor;
					}
					else if (overScaleLane)
					{
						if (cc.Cursor != Cursors.SizeNS) cc.Cursor = Cursors.SizeNS;
					}
					else if (cc.Cursor == Cursors.Hand || cc.Cursor == Cursors.SizeWE || cc.Cursor == Cursors.SizeAll || cc.Cursor == Cursors.SizeNS || cc.Cursor == Cursors.Cross)
					{
						cc.Cursor = null;
					}
				}
				catch { }
				return;
			}

			if (e.LeftButton != MouseButtonState.Pressed)
			{
				End360Interaction(cc);
				return;
			}

			try
			{
				System.Windows.Point pt = e.GetPosition(cc);
				float curPxX = ConvertOfHorizontalPx(cc, pt.X);
				float curPxY = ConvertOfVerticalPx(cc, pt.Y);
				bool wasDragging = isDraggingFootprintCanvas || isDraggingVerticalScale || activeLiveOrderDragMode != LiveOrderDragMode.None || (activeRrDragMode != RrDragMode.None && activeRrDragMode != RrDragMode.PendingMove);

				if (Update360DragMove(cc, curPxX, curPxY))
				{
					if (!wasDragging)
					{
						try { cc.CaptureMouse(); } catch { }
					}
					Cursor desiredCursor = (activeLiveOrderDragMode != LiveOrderDragMode.None || activeRrDragMode == RrDragMode.ScaleTarget || activeRrDragMode == RrDragMode.ScaleStop || isDraggingVerticalScale)
						? Cursors.SizeNS
						: (activeRrDragMode == RrDragMode.ScaleEnd ? Cursors.SizeWE : Cursors.SizeAll);
					if (cc.Cursor != desiredCursor)
						cc.Cursor = desiredCursor;
					e.Handled = true;
				}
			}
			catch { }
		}

		private void OnChartMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
		{
			End360Interaction(hookedOrderflowCC ?? ChartControl);
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

			try
			{
				if (sb != null && sb.Maximum > sb.Minimum && ChartPanel != null && cursorPxX >= ChartPanel.X && cursorPxX <= ChartPanel.X + ChartPanel.W)
				{
					float rightAnchorX = ChartPanel.X + ChartPanel.W - Math.Max(28f, cc.Properties.BarMarginRightUser);
					if (wasPinnedToLiveEdge && cursorPxX >= ChartPanel.X + ChartPanel.W * 0.62f)
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

		private bool ProcessUniversalSmoothWheel(ChartControl cc, int rawDelta, float cursorPxX, float cursorPxY, bool hasCursorPt)
		{
			if (cc == null || rawDelta == 0)
				return false;

			long nowTicks = System.Diagnostics.Stopwatch.GetTimestamp();
			long minGapTicks = System.Diagnostics.Stopwatch.Frequency / 120;
			if (nowTicks - lastWheelHandledTicks < minGapTicks)
				return true;
			lastWheelHandledTicks = nowTicks;

			try
			{
				ModifierKeys mods = Keyboard.Modifiers;
				if ((mods & ModifierKeys.Shift) == ModifierKeys.Shift)
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

				if ((mods & ModifierKeys.Alt) == ModifierKeys.Alt)
				{
					EnsureAutoYAxisFor360View();
					double curMin = !double.IsNaN(lastRenderedMinPrice) ? lastRenderedMinPrice : MinValue;
					double curMax = !double.IsNaN(lastRenderedMaxPrice) ? lastRenderedMaxPrice : MaxValue;
					if (!double.IsNaN(curMin) && !double.IsNaN(curMax) && curMax > curMin)
					{
						double shift = (curMax - curMin) * 0.08 * (rawDelta > 0 ? 1.0 : -1.0);
						freeViewMinPrice = curMin + shift;
						freeViewMaxPrice = curMax + shift;
						isFree360View = true;
						RequestImmediateOrderflowRedraw();
					}
					return true;
				}

				bool overRightLane = hasCursorPt && IsPointOverRightScaleLane(cursorPxX, cursorPxY);
				if ((mods & ModifierKeys.Control) == ModifierKeys.Control || overRightLane)
				{
					ApplyVerticalPriceWheelZoom(rawDelta, cursorPxY, hasCursorPt);
					return true;
				}

				if (cc.Properties != null)
				{
					float cur = cc.Properties.BarDistance > 3f ? cc.Properties.BarDistance : BarSpacePx;
					float next = ComputeSmoothNextZoomDistance(cur, rawDelta);
					ApplyHorizontalZoom(cc, next, hasCursorPt ? cursorPxX : -1f);
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
			float pxY = -1f;
			try
			{
				System.Windows.Point pt = e.GetPosition(cc);
				pxX = ConvertOfHorizontalPx(cc, pt.X);
				pxY = ConvertOfVerticalPx(cc, pt.Y);
			}
			catch { }

			if (ProcessUniversalSmoothWheel(cc, e.Delta, pxX, pxY, pxX >= 0f))
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
				case 0: // Footprint
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeOrderflowMode = AK10XOrderflowMode.VolumeFootprint;
					FootprintMode = runtimeOrderflowMode;
					if (cc != null && cc.Properties != null && cc.Properties.BarDistance < CandlestickZoomThresholdPx)
						ApplyHorizontalZoom(cc, Math.Max(88f, BarSpacePx));
					else
						RequestImmediateOrderflowRedraw();
					return true;

				case 1: // Imbalance
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeOrderflowMode = AK10XOrderflowMode.Imbalance;
					FootprintMode = runtimeOrderflowMode;
					if (cc != null && cc.Properties != null && cc.Properties.BarDistance < CandlestickZoomThresholdPx)
						ApplyHorizontalZoom(cc, Math.Max(88f, BarSpacePx));
					else
						RequestImmediateOrderflowRedraw();
					return true;

				case 2: // Long Position Tool (Click Chart to Place)
					lastBtnClickUtc = DateTime.UtcNow;
					pendingRrToolMode = (pendingRrToolMode == 1) ? 0 : 1;
					RequestImmediateOrderflowRedraw();
					return true;

				case 3: // Short Position Tool (Click Chart to Place)
					lastBtnClickUtc = DateTime.UtcNow;
					pendingRrToolMode = (pendingRrToolMode == 2) ? 0 : 2;
					RequestImmediateOrderflowRedraw();
					return true;

				case 4: // Candlestick <-> Orderflow
					lastBtnClickUtc = DateTime.UtcNow;
					if (cc != null && cc.Properties != null)
					{
						bool isCandleNow = cc.Properties.BarDistance < CandlestickZoomThresholdPx;
						if (!isCandleNow)
						{
							isFree360View = false;
							freeViewMinPrice = double.NaN;
							freeViewMaxPrice = double.NaN;
							freeRightPanOffsetPx = 0f;
							focusedCandleBarIndex = -1;
						}
						ApplyHorizontalZoom(cc, isCandleNow ? Math.Max(88f, BarSpacePx) : 14f);
					}
					return true;

				case 5: // Step -
					lastBtnClickUtc = DateTime.UtcNow;
					if ((UseSessionWiseOrderflow && runtimeSessionStepActive) || runtimeAutoStep)
					{
						runtimeTicksPerLevel = Math.Max(1, ResolveToolbarCurrentTicks());
						runtimeSessionStepActive = false;
						runtimeAutoStep = false;
					}
					runtimeTicksPerLevel = Math.Max(1, runtimeTicksPerLevel <= 3 ? (runtimeTicksPerLevel - 1) : (int)Math.Round(runtimeTicksPerLevel * 0.72));
					InvalidateFootprintCache();
					return true;

				case 6: // Session-Wise / Auto / Manual Step Toggle
					lastBtnClickUtc = DateTime.UtcNow;
					if (UseSessionWiseOrderflow)
					{
						if (!runtimeSessionStepActive && !runtimeAutoStep)
						{
							runtimeSessionStepActive = true;
							runtimeAutoStep = false;
						}
						else if (runtimeSessionStepActive)
						{
							runtimeSessionStepActive = false;
							runtimeAutoStep = true;
						}
						else
						{
							runtimeSessionStepActive = true;
							runtimeAutoStep = false;
						}
					}
					else
					{
						runtimeAutoStep = !runtimeAutoStep;
						if (!runtimeAutoStep) runtimeTicksPerLevel = Math.Max(1, lastEffectiveAutoTicks);
					}
					InvalidateFootprintCache();
					return true;

				case 7: // Step +
					lastBtnClickUtc = DateTime.UtcNow;
					if ((UseSessionWiseOrderflow && runtimeSessionStepActive) || runtimeAutoStep)
					{
						runtimeTicksPerLevel = Math.Max(1, ResolveToolbarCurrentTicks());
						runtimeSessionStepActive = false;
						runtimeAutoStep = false;
					}
					runtimeTicksPerLevel = Math.Min(500, Math.Max(runtimeTicksPerLevel + 1, (int)Math.Ceiling(runtimeTicksPerLevel * 1.32)));
					InvalidateFootprintCache();
					return true;

				case 8: // S/R Zones (Minor TF + Major TF×TF + Day/Week/Month High & Low)
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeShowSrZones = !runtimeShowSrZones;
					EnableSrZones = runtimeShowSrZones;
					if (cc != null && cc.Properties != null)
					{
						float curDist = cc.Properties.BarDistance > 3f ? cc.Properties.BarDistance : BarSpacePx;
						SyncOneBoxRightGapMargin(cc, curDist, true);
						TriggerChartScrollbarRefresh();
					}
					RequestImmediateOrderflowRedraw();
					return true;

				case 11: // Toggle Right-Side Tradesea Order & Scalp Execution Panel
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeShowTradePanel = !runtimeShowTradePanel;
					EnableTradePanel = runtimeShowTradePanel;
					UpdateTradePanelVisibilityImmediate();
					RequestImmediateOrderflowRedraw();
					return true;

				case 10: // Dark / White Theme
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeLightTheme = !runtimeLightTheme;
					UseLightTheme = runtimeLightTheme;
					RequestImmediateOrderflowRedraw();
					return true;

				case 9: // Multi-TF Closed-Candle Signals Toggle (Current TF 3m + Higher TF 9m)
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeShowSignals = !runtimeShowSignals;
					EnableChartSignals = runtimeShowSignals;
					RequestImmediateOrderflowRedraw();
					return true;

				case 16: // Analysis Mode Toggle (Show/Hide Past Invalidated S/R Lines)
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeShowSrAnalysis = !runtimeShowSrAnalysis;
					EnableSrPastAnalysis = runtimeShowSrAnalysis;
					if (runtimeShowSrAnalysis && !runtimeShowSrZones)
					{
						runtimeShowSrZones = true;
						EnableSrZones = true;
					}
					RequestImmediateOrderflowRedraw();
					return true;

				case 12: // Z-
					lastBtnClickUtc = DateTime.UtcNow;
					if (cc != null && cc.Properties != null)
					{
						float cur = cc.Properties.BarDistance > 3f ? cc.Properties.BarDistance : BarSpacePx;
						ApplyHorizontalZoom(cc, ComputeSmoothNextZoomDistance(cur, -240));
					}
					return true;

				case 13: // Z+
					lastBtnClickUtc = DateTime.UtcNow;
					if (cc != null && cc.Properties != null)
					{
						float cur = cc.Properties.BarDistance > 3f ? cc.Properties.BarDistance : BarSpacePx;
						ApplyHorizontalZoom(cc, ComputeSmoothNextZoomDistance(cur, 240));
					}
					return true;

				case 14: // Fit (Reset 360° Free View back to Auto-Fit & Live Edge)
					lastBtnClickUtc = DateTime.UtcNow;
					Reset360ViewToAutoFit(cc, snapLiveIfAlreadyFit: true);
					return true;

				case 15: // MP Zones & Right-Side Values Toggle
					lastBtnClickUtc = DateTime.UtcNow;
					runtimeShowMpZones = !runtimeShowMpZones;
					EnableMarketProfileZones = runtimeShowMpZones;
					if (cc != null && cc.Properties != null)
					{
						float curDist = cc.Properties.BarDistance > 3f ? cc.Properties.BarDistance : BarSpacePx;
						SyncOneBoxRightGapMargin(cc, curDist, true);
						TriggerChartScrollbarRefresh();
					}
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

		private int ResolveToolbarCurrentTicks()
		{
			double tick = TickSize > 0 ? TickSize : 0.05;
			try
			{
				if (ChartBars != null && ChartBars.Bars != null && ChartBars.Count > 0)
				{
					int refIdx = (focusedCandleBarIndex >= 0 && focusedCandleBarIndex < ChartBars.Count)
						? focusedCandleBarIndex
						: Math.Max(0, Math.Min(ChartBars.Count - 1, ChartBars.ToIndex));
					lock (dataLock)
					{
						if (barCache.TryGetValue(refIdx, out FootprintBarData fp) && fp != null && fp.EffectivePriceStep > 0)
							return Math.Max(1, (int)Math.Round(fp.EffectivePriceStep / tick));
					}
				}
			}
			catch { }
			return runtimeAutoStep ? Math.Max(1, lastEffectiveAutoTicks) : Math.Max(1, runtimeTicksPerLevel);
		}

		private void InvalidateFootprintCache()
		{
			lock (dataLock)
			{
				barCache.Clear();
				lastComputedBarCount = -1;
				lastComputedUniformStep = -1.0;
				lastComputedSessionCfgHash = 0;
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

		#region Real-Time Market Data & Orderflow Bar Computation
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

				// Real-time live tick update into running Market Profile session when 1M feed update hasn't just fired
				if (isRealtime && (DateTime.UtcNow - lastFeed1MUpdateUtc).TotalSeconds > 2.5)
				{
					DateTime tt = e.Time > DateTime.MinValue ? e.Time : DateTime.Now;
					DateTime minClose = new DateTime(tt.Year, tt.Month, tt.Day, tt.Hour, tt.Minute, 0, tt.Kind);
					if (tt.Second > 0 || tt.Millisecond > 0)
						minClose = minClose.AddMinutes(1);

					lock (profileLock)
					{
						if (activeSessions == null || activeSessions.Length == 0)
							activeSessions = GetProfileSessions();
						if (cachedPriceStep <= 0)
							cachedPriceStep = ComputeAutoOrManualPriceStep();

						MinuteSample tickSample = new MinuteSample
						{
							Time = minClose,
							Open = e.Price,
							High = e.Price,
							Low = e.Price,
							Close = e.Price,
							Volume = vol,
							BarIndex = barIdx
						};
						AccumulateSampleIntoSessions(tickSample, cachedPriceStep, isLiveTickUpdate: true);
					}
				}

				if (isRealtime)
				{
					UpdateLiveTrailingStopsOnTick(e.Price);
					EnforceRiskAndCapitalProtectorOnTick(e.Price);
					RequestThrottledChartRefresh(false);
				}
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

		private struct OrderflowSessionWindows
		{
			public int AsiaOpenSec;
			public int AsiaCloseSec;
			public int LondonOpenSec;
			public int LondonCloseSec;
			public int NyOpenSec;
			public int NyCloseSec;
			public bool SubtractCloseStampSec;
		}

		private OrderflowSessionWindows BuildOrderflowSessionWindows()
		{
			int asiaO, asiaC, lonO, lonC, nyO, nyC;
			if (!TryParseSessionTime(AsiaFromTime, out asiaO)) asiaO = 3 * 3600 + 30 * 60;
			if (!TryParseSessionTime(AsiaToTime, out asiaC)) asiaC = 13 * 3600 + 30 * 60;
			if (!TryParseSessionTime(LondonFromTime, out lonO)) lonO = 13 * 3600 + 30 * 60;
			if (!TryParseSessionTime(LondonToTime, out lonC)) lonC = 19 * 3600;
			if (!TryParseSessionTime(NewYorkFromTime, out nyO)) nyO = 19 * 3600;
			if (!TryParseSessionTime(NewYorkToTime, out nyC)) nyC = 2 * 3600 + 30 * 60;

			bool subSec = false;
			try
			{
				BarsPeriod bp = BarsPeriod ?? (ChartBars != null && ChartBars.Properties != null ? ChartBars.Properties.BarsPeriod : null);
				if (bp != null && (bp.BarsPeriodType == BarsPeriodType.Minute || bp.BarsPeriodType == BarsPeriodType.Second))
					subSec = true;
			}
			catch { }

			return new OrderflowSessionWindows
			{
				AsiaOpenSec = asiaO,
				AsiaCloseSec = asiaC,
				LondonOpenSec = lonO,
				LondonCloseSec = lonC,
				NyOpenSec = nyO,
				NyCloseSec = nyC,
				SubtractCloseStampSec = subSec
			};
		}

		private static bool IsSecWithinSessionWindow(int sec, int openSec, int closeSec)
		{
			if (openSec == closeSec)
				return false;
			return closeSec > openSec
				? (sec >= openSec && sec < closeSec)
				: (sec >= openSec || sec < closeSec);
		}

		private void ResolveBarOrderflowParams(
			DateTime barTime,
			OrderflowSessionWindows win,
			double fallbackUniformStep,
			double tick,
			out double barStep,
			out double imbPct,
			out int minVol,
			out string sessTag)
		{
			if (!UseSessionWiseOrderflow)
			{
				barStep = fallbackUniformStep;
				imbPct = ImbalancePercentage;
				minVol = ImbalanceFilter;
				sessTag = string.Empty;
				return;
			}

			DateTime probe = win.SubtractCloseStampSec ? barTime.AddSeconds(-1) : barTime;
			int sec = probe.Hour * 3600 + probe.Minute * 60 + probe.Second;

			int sessTicks;
			if (IsSecWithinSessionWindow(sec, win.AsiaOpenSec, win.AsiaCloseSec))
			{
				sessTicks = Math.Max(1, AsiaTicksPerLevel);
				imbPct = AsiaImbalancePercentage;
				minVol = Math.Max(0, AsiaMinVolumePerLevel);
				sessTag = "Asia";
			}
			else if (IsSecWithinSessionWindow(sec, win.LondonOpenSec, win.LondonCloseSec))
			{
				sessTicks = Math.Max(1, LondonTicksPerLevel);
				imbPct = LondonImbalancePercentage;
				minVol = Math.Max(0, LondonMinVolumePerLevel);
				sessTag = "Lon";
			}
			else if (IsSecWithinSessionWindow(sec, win.NyOpenSec, win.NyCloseSec))
			{
				sessTicks = Math.Max(1, NewYorkTicksPerLevel);
				imbPct = NewYorkImbalancePercentage;
				minVol = Math.Max(0, NewYorkMinVolumePerLevel);
				sessTag = "NY";
			}
			else
			{
				sessTicks = Math.Max(1, TicksPerLevel);
				imbPct = ImbalancePercentage;
				minVol = Math.Max(0, ImbalanceFilter);
				sessTag = "Sess";
			}

			barStep = runtimeSessionStepActive
				? Math.Max(tick, sessTicks * tick)
				: fallbackUniformStep;
		}

		private int ComputeOrderflowSessionCfgHash(OrderflowSessionWindows win)
		{
			unchecked
			{
				int h = 17;
				h = h * 31 + (UseSessionWiseOrderflow ? 1 : 0);
				h = h * 31 + (runtimeSessionStepActive ? 1 : 0);
				h = h * 31 + AsiaTicksPerLevel;
				h = h * 31 + (int)Math.Round(AsiaImbalancePercentage * 10.0);
				h = h * 31 + AsiaMinVolumePerLevel;
				h = h * 31 + LondonTicksPerLevel;
				h = h * 31 + (int)Math.Round(LondonImbalancePercentage * 10.0);
				h = h * 31 + LondonMinVolumePerLevel;
				h = h * 31 + NewYorkTicksPerLevel;
				h = h * 31 + (int)Math.Round(NewYorkImbalancePercentage * 10.0);
				h = h * 31 + NewYorkMinVolumePerLevel;
				h = h * 31 + TicksPerLevel;
				h = h * 31 + (int)Math.Round(ImbalancePercentage * 10.0);
				h = h * 31 + ImbalanceFilter;
				h = h * 31 + win.AsiaOpenSec;
				h = h * 31 + win.AsiaCloseSec;
				h = h * 31 + win.LondonOpenSec;
				h = h * 31 + win.LondonCloseSec;
				h = h * 31 + win.NyOpenSec;
				h = h * 31 + win.NyCloseSec;
				h = h * 31 + (win.SubtractCloseStampSec ? 1 : 0);
				return h;
			}
		}

		private void EnsureBarsComputed(ChartBars chartBars)
		{
			if (chartBars == null || chartBars.Bars == null)
				return;

			Bars bars = chartBars.Bars;
			int count = bars.Count;
			if (count <= 0)
				return;

			double tick = TickSize > 0 ? TickSize : 0.05;
			double lastClose = bars.GetClose(count - 1);
			long lastVol = bars.GetVolume(count - 1);
			OrderflowSessionWindows sessWin = BuildOrderflowSessionWindows();
			int sessCfgHash = ComputeOrderflowSessionCfgHash(sessWin);
			bool sessionStepOn = UseSessionWiseOrderflow && runtimeSessionStepActive;

			int visFirst = Math.Max(0, chartBars.FromIndex);
			int visLast = Math.Min(count - 1, chartBars.ToIndex);
			double visMin = double.MaxValue;
			double visMax = double.MinValue;
			if (!sessionStepOn)
			{
				for (int i = visFirst; i <= visLast; i++)
				{
					double l = bars.GetLow(i);
					double h = bars.GetHigh(i);
					if (l < visMin) visMin = l;
					if (h > visMax) visMax = h;
				}
			}
			double visibleSpan = (visMax > visMin && visMin < double.MaxValue) ? (visMax - visMin) : (tick * 20);
			if (Math.Abs(runtimeYZoomFactor - 1.0) > 0.01)
				visibleSpan /= runtimeYZoomFactor;

			float panelH = ChartPanel != null ? ChartPanel.H : 620f;
			float usableH = Math.Max(180f, panelH - 90f);
			bool isActivelyDragging = isDraggingFootprintCanvas || isDraggingVerticalScale;

			if (!sessionStepOn && !isActivelyDragging && isFree360View &&
				!double.IsNaN(freeViewMinPrice) && !double.IsNaN(freeViewMaxPrice) &&
				freeViewMaxPrice > freeViewMinPrice)
			{
				visibleSpan = Math.Max(tick * 10, (freeViewMaxPrice - freeViewMinPrice) * (usableH / Math.Max(180f, panelH)));
			}

			lock (dataLock)
			{
				double uniformStep;
				if (sessionStepOn)
				{
					uniformStep = Math.Max(tick, TicksPerLevel * tick);
				}
				else if (isActivelyDragging && lastComputedUniformStep > 0 && barCache.Count > 0)
				{
					uniformStep = lastComputedUniformStep;
				}
				else
				{
					int sampleStart = Math.Max(0, count - 80);
					List<double> ranges = new List<double>(count - sampleStart);
					for (int i = sampleStart; i < count; i++)
					{
						double r = bars.GetHigh(i) - bars.GetLow(i);
						if (r > 0) ranges.Add(r);
					}
					ranges.Sort();
					double medianRange = ranges.Count > 0 ? ranges[ranges.Count / 2] : (tick * 15);
					uniformStep = ComputeReadablePriceStep(medianRange, visibleSpan, usableH);
				}

				if (count == lastComputedBarCount &&
					Math.Abs(lastClose - lastComputedLastClose) < 1e-9 &&
					lastVol == lastComputedLastVol &&
					Math.Abs(uniformStep - lastComputedUniformStep) < 1e-9 &&
					sessCfgHash == lastComputedSessionCfgHash &&
					barCache.Count > 0)
				{
					return;
				}

				long sessionCumDelta = 0;
				DateTime currentSessionDate = DateTime.MinValue;

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
					}

					ResolveBarOrderflowParams(t, sessWin, uniformStep, tick,
						out double barStep, out double barImbPct, out int barMinVol, out string barSessTag);

					if (!barCache.TryGetValue(i, out FootprintBarData fpBar) ||
						i == count - 1 ||
						Math.Abs(fpBar.EffectivePriceStep - barStep) > 1e-9 ||
						Math.Abs(fpBar.AppliedImbalancePct - barImbPct) > 1e-6 ||
						fpBar.AppliedMinVolume != barMinVol)
					{
						fpBar = BuildFootprintBar(bars, i, barStep, barImbPct, barMinVol, barSessTag);
						barCache[i] = fpBar;
					}
					else
					{
						fpBar.SessionTag = barSessTag;
					}

					sessionCumDelta += fpBar.Delta;
					fpBar.CumulativeDelta = sessionCumDelta;
				}

				lastComputedBarCount = count;
				lastComputedLastClose = lastClose;
				lastComputedLastVol = lastVol;
				lastComputedUniformStep = uniformStep;
				lastComputedSessionCfgHash = sessCfgHash;
			}
		}

		private FootprintBarData BuildFootprintBar(Bars bars, int barIdx, double step, double imbPct, int minVol, string sessTag)
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

			long rawLevelSpan = maxBucket - minBucket + 1;
			if (rawLevelSpan > 2500)
			{
				step *= Math.Ceiling(rawLevelSpan / 2500.0);
				minBucket = (long)Math.Floor((low + 1e-9) / step);
				maxBucket = (long)Math.Floor((high - 1e-9) / step);
				if (maxBucket < minBucket) maxBucket = minBucket;
			}

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
				EffectivePriceStep = step,
				AppliedImbalancePct = imbPct,
				AppliedMinVolume = minVol,
				SessionTag = sessTag
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

			double ratio = Math.Max(1.01, imbPct / 100.0);
			long minFilter = Math.Max(0, minVol);
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
		#endregion

		#region Market Profile Session & Virgin Zones Analytics Engine (Copied from AK10XGlobalMarketProfile)
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

		private double GetMpPriceStep()
		{
			return cachedPriceStep > 0 ? cachedPriceStep : ComputeAutoOrManualPriceStep();
		}

		private void RebuildAllSessionProfiles(double step)
		{
			cachedPriceStep = step;
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
						StartTime = day.EffectiveOpenTime.AddMinutes(periodIndex * bracketMinutes),
						EndTime = day.EffectiveOpenTime.AddMinutes((periodIndex + 1) * bracketMinutes),
						FirstSampleTime = sample.Time,
						LastSampleTime = sample.Time,
						Open = prevSessionClose
					};
					day.PeriodSlices.Add(periodIndex, slice);
				}
				if (sample.Time < slice.FirstSampleTime || slice.FirstSampleTime == DateTime.MinValue)
					slice.FirstSampleTime = sample.Time;
				if (sample.Time > slice.LastSampleTime)
					slice.LastSampleTime = sample.Time;

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

		private string FormatMpPrice(double price)
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

		private double RoundToInstrumentTick(double rawPrice)
		{
			double tick = (Instrument == null || Instrument.MasterInstrument == null || Instrument.MasterInstrument.TickSize <= 0)
				? 0.01
				: Instrument.MasterInstrument.TickSize;
			if (Instrument != null && Instrument.MasterInstrument != null && tick > 0)
				return Instrument.MasterInstrument.RoundToTickSize(rawPrice);
			return Math.Round(rawPrice / tick, MidpointRounding.AwayFromZero) * tick;
		}

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

			return RoundToInstrumentTick(rawPrice);
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

		private static List<SinglePrintZone> ComputeSinglePrintZones(DayProfile day, int minBin, int maxBin, int pocBin)
		{
			List<SinglePrintZone> zones = new List<SinglePrintZone>();
			if (day == null || maxBin - minBin < 4)
				return zones;

			int bodyBottomBin = minBin;
			while (bodyBottomBin <= maxBin)
			{
				PriceRow r;
				if (day.Rows.TryGetValue(bodyBottomBin, out r) && r.Periods.Count >= 2)
					break;
				bodyBottomBin++;
			}

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
					tpoUnits.AddRange(groupSessions);

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
				for (int gs = 0; gs < groupSessions.Count; gs++)
					bucket.SourceProfileKeys.Add(groupSessions[gs].ProfileKey);

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
							StartTime = u.EffectiveOpenTime,
							EndTime = u.SessionCloseTime,
							FirstSampleTime = u.EffectiveOpenTime,
							LastSampleTime = u.LastSampleTime,
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
								StartTime = srcSlice.StartTime,
								EndTime = srcSlice.EndTime,
								FirstSampleTime = srcSlice.FirstSampleTime,
								LastSampleTime = srcSlice.LastSampleTime,
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
							dstSlice.EndTime = srcSlice.EndTime;
							if (srcSlice.LastSampleTime > dstSlice.LastSampleTime)
								dstSlice.LastSampleTime = srcSlice.LastSampleTime;
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
				if (s.LastSampleTime > merged.LastSampleTime) merged.LastSampleTime = s.LastSampleTime;

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
					if (!merged.PeriodSlices.ContainsKey(shiftedIdx))
					{
						PeriodSlice dstSlice = new PeriodSlice
						{
							PeriodIndex = shiftedIdx,
							StartTime = srcSlice.StartTime,
							EndTime = srcSlice.EndTime,
							FirstSampleTime = srcSlice.FirstSampleTime,
							LastSampleTime = srcSlice.LastSampleTime,
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

		private void EnsureSessionRowContinuity(DayProfile src, double baseStep)
		{
			if (src == null || src.Rows.Count == 0)
			{
				if (src != null)
					src.EffectiveStep = baseStep;
				return;
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
			src.EffectiveStep = baseStep;
		}

		private DateTime GetReferenceNow(List<DayProfile> sessions = null)
		{
			bool isPlayback = false;
			try
			{
				isPlayback = Connection.PlaybackConnection != null
					&& Connection.PlaybackConnection.Status == ConnectionStatus.Connected;
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

		private bool IsSessionRunning(DayProfile day, bool isLatestSession, DateTime refNow)
		{
			if (!isLatestSession || day == null)
				return false;

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
			if (slice.EndTime > DateTime.MinValue)
				return refNow >= slice.EndTime;
			if (session.SessionCloseTime > DateTime.MinValue && refNow >= session.SessionCloseTime)
				return true;
			return false;
		}

		private static bool IsRawTpoSliceClosed(DayProfile rawSess, PeriodSlice slice, bool isRawRunning, DateTime refNow)
		{
			if (rawSess == null || slice == null)
				return false;
			if (!isRawRunning)
				return true;
			if (slice.PeriodIndex < rawSess.LastPeriod)
				return true;
			if (slice.EndTime > DateTime.MinValue)
				return refNow >= slice.EndTime;
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

		// Maps Market Profile session timestamps cleanly onto Orderflow ChartBars X coordinates
		private int FindFirstChartBarAfterOpen(Bars bars, DateTime openTime)
		{
			if (bars == null || bars.Count == 0)
				return -1;
			int count = bars.Count;
			bool minuteCloseStamped = IsIntradayMinuteChart();
			DateTime firstTime = bars.GetTime(0);
			int pMin = Math.Max(1, GetChartPeriodMinutes());

			if (openTime < firstTime.AddMinutes(-pMin))
				return -1; // Started prior to loaded chart history

			int lo = 0, hi = count - 1, ans = count - 1;
			while (lo <= hi)
			{
				int mid = (lo + hi) >> 1;
				DateTime mt = bars.GetTime(mid);
				bool match = minuteCloseStamped ? (mt > openTime) : (mt >= openTime);
				if (match)
				{
					ans = mid;
					hi = mid - 1;
				}
				else
				{
					lo = mid + 1;
				}
			}
			return ans;
		}

		private int FindLastChartBarAtOrBeforeClose(Bars bars, DateTime closeTime)
		{
			if (bars == null || bars.Count == 0)
				return -1;
			int count = bars.Count;
			int pMin = Math.Max(1, GetChartPeriodMinutes());
			if (closeTime < bars.GetTime(0).AddMinutes(-pMin))
				return -1;

			int lo = 0, hi = count - 1, ans = 0;
			while (lo <= hi)
			{
				int mid = (lo + hi) >> 1;
				if (bars.GetTime(mid) <= closeTime)
				{
					ans = mid;
					lo = mid + 1;
				}
				else
				{
					hi = mid - 1;
				}
			}
			return ans;
		}

		private void GetSessionChartXSpan(
			ChartControl cc,
			Bars chartBars,
			DayProfile day,
			bool isRunning,
			float barSpacing,
			float panelLeft,
			out float rawStartX,
			out float rawEndX)
		{
			rawStartX = panelLeft - 40f;
			rawEndX = panelLeft - 20f;
			if (cc == null || chartBars == null || chartBars.Count == 0 || day == null)
				return;

			int sIdx = FindFirstChartBarAfterOpen(chartBars, day.EffectiveOpenTime);
			if (sIdx >= 0)
			{
				GetBarGridColumnBounds(cc, sIdx, barSpacing, out rawStartX, out _, out _);
			}

			if (isRunning)
			{
				GetBarGridColumnBounds(cc, chartBars.Count - 1, barSpacing, out _, out rawEndX, out _);
			}
			else
			{
				DateTime effClose = day.SessionCloseTime > DateTime.MinValue ? day.SessionCloseTime : day.LastSampleTime;
				int eIdx = FindLastChartBarAtOrBeforeClose(chartBars, effClose);
				if (eIdx >= 0)
				{
					GetBarGridColumnBounds(cc, eIdx, barSpacing, out _, out rawEndX, out _);
				}
			}

			if (rawEndX < rawStartX && sIdx >= 0)
				rawEndX = rawStartX + Math.Max(6f, barSpacing * 0.8f);
		}

		private float ResolveBreakEndXOnChart(
			ChartControl cc,
			Bars chartBars,
			float barSpacing,
			float panelLeft,
			float rightLimitX,
			float xOriginEnd,
			DateTime breakWinStart,
			DateTime breakWinEnd,
			double levelPrice,
			LevelBreakMode breakMode,
			int originSide,
			bool isCrossRule)
		{
			if (cc == null || chartBars == null || chartBars.Count == 0)
				return panelLeft - 20f;

			int pMin = Math.Max(1, GetChartPeriodMinutes());
			if (breakWinEnd < chartBars.GetTime(0).AddMinutes(-pMin))
				return panelLeft - 24f; // Broke before the start of loaded chart history

			int targetBarIdx = -1;
			if (isCrossRule && breakWinStart <= chartBars.GetTime(chartBars.Count - 1))
			{
				int b0 = Math.Max(0, FindFirstChartBarAfterOpen(chartBars, breakWinStart));
				int b1 = Math.Min(chartBars.Count - 1, Math.Max(b0, FindLastChartBarAtOrBeforeClose(chartBars, breakWinEnd.AddMinutes(pMin))));
				for (int b = b0; b <= b1; b++)
				{
					if (DoesCrossBreakLevel(chartBars.GetHigh(b), chartBars.GetLow(b), levelPrice, breakMode, originSide))
					{
						targetBarIdx = b;
						break;
					}
				}
			}

			if (targetBarIdx < 0)
			{
				targetBarIdx = FindLastChartBarAtOrBeforeClose(chartBars, breakWinEnd.AddMinutes(pMin * 0.5));
				if (targetBarIdx < 0)
					return panelLeft - 24f;
			}

			GetBarGridColumnBounds(cc, targetBarIdx, barSpacing, out _, out _, out float breakCx);
			return Math.Min(rightLimitX, Math.Max(xOriginEnd, breakCx));
		}

		private bool GetSessionLevelExtensionOnChart(
			ChartControl cc,
			Bars chartBars,
			float barSpacing,
			List<DayProfile> sessions,
			int currentIdx,
			double levelPrice,
			float xOriginEnd,
			float panelLeft,
			float rightLimitX,
			DateTime refNow,
			double eps,
			LevelBreakMode breakMode,
			out float drawEndX)
		{
			DayProfile origin = (sessions != null && currentIdx >= 0 && currentIdx < sessions.Count) ? sessions[currentIdx] : null;
			double originClose = origin != null ? origin.Close : levelPrice;
			double originMid = origin != null ? 0.5 * (origin.High + origin.Low) : levelPrice;
			int side = originClose > levelPrice + eps ? 1 : (originClose < levelPrice - eps ? -1 : 0);
			int initialSide = side;
			bool useCloseRule = (VirginityBreakRule == VirginityBreakRuleMode.CloseBased);

			for (int j = currentIdx + 1; j < sessions.Count; j++)
			{
				DayProfile next = sessions[j];
				if (!useCloseRule)
				{
					if (DoesCrossBreakLevel(next.High, next.Low, levelPrice, breakMode, initialSide))
					{
						DateTime winStart = next.EffectiveOpenTime;
						DateTime winEnd = next.LastSampleTime > DateTime.MinValue ? next.LastSampleTime : next.SessionCloseTime;
						if (next.PeriodSlices != null && next.PeriodSlices.Count > 0)
						{
							foreach (KeyValuePair<int, PeriodSlice> kv in next.PeriodSlices)
							{
								PeriodSlice sl = kv.Value;
								if (DoesCrossBreakLevel(sl.High, sl.Low, levelPrice, breakMode, initialSide))
								{
									winStart = sl.StartTime > DateTime.MinValue ? sl.StartTime : next.EffectiveOpenTime;
									winEnd = sl.LastSampleTime > DateTime.MinValue ? sl.LastSampleTime : (sl.EndTime > DateTime.MinValue ? sl.EndTime : winEnd);
									break;
								}
							}
						}
						drawEndX = ResolveBreakEndXOnChart(cc, chartBars, barSpacing, panelLeft, rightLimitX, xOriginEnd, winStart, winEnd, levelPrice, breakMode, initialSide, true);
						return false;
					}
				}
				else
				{
					bool nextRunning = IsSessionRunning(next, j == sessions.Count - 1, refNow);
					if (next.PeriodSlices != null && next.PeriodSlices.Count > 0)
					{
						foreach (KeyValuePair<int, PeriodSlice> kv in next.PeriodSlices)
						{
							PeriodSlice sl = kv.Value;
							if (!IsTpoSliceClosed(next, sl, nextRunning, refNow))
								continue;
							if (DoesClosedTpoBreakLevel(sl.Open, sl.High, sl.Low, sl.Close, levelPrice, eps, breakMode, ref side, originMid))
							{
								DateTime winStart = sl.StartTime > DateTime.MinValue ? sl.StartTime : next.EffectiveOpenTime;
								DateTime winEnd = sl.LastSampleTime > DateTime.MinValue ? sl.LastSampleTime : (sl.EndTime > DateTime.MinValue ? sl.EndTime : next.SessionCloseTime);
								drawEndX = ResolveBreakEndXOnChart(cc, chartBars, barSpacing, panelLeft, rightLimitX, xOriginEnd, winStart, winEnd, levelPrice, breakMode, initialSide, false);
								return false;
							}
						}
					}
					else if (!nextRunning)
					{
						if (DoesClosedTpoBreakLevel(next.Open, next.High, next.Low, next.Close, levelPrice, eps, breakMode, ref side, originMid))
						{
							DateTime winEnd = next.LastSampleTime > DateTime.MinValue ? next.LastSampleTime : next.SessionCloseTime;
							drawEndX = ResolveBreakEndXOnChart(cc, chartBars, barSpacing, panelLeft, rightLimitX, xOriginEnd, next.EffectiveOpenTime, winEnd, levelPrice, breakMode, initialSide, false);
							return false;
						}
					}
				}
			}

			drawEndX = rightLimitX;
			return true;
		}

		private bool GetRawLevelExtensionOnChart(
			ChartControl cc,
			Bars chartBars,
			float barSpacing,
			List<DayProfile> rawSessions,
			int afterRawIdx,
			double levelPrice,
			float xOriginEnd,
			float panelLeft,
			float rightLimitX,
			DateTime refNow,
			double eps,
			LevelBreakMode breakMode,
			out float drawEndX)
		{
			DayProfile origin = (rawSessions != null && afterRawIdx >= 0 && afterRawIdx < rawSessions.Count) ? rawSessions[afterRawIdx] : null;
			double originClose = origin != null ? origin.Close : levelPrice;
			double originMid = origin != null ? 0.5 * (origin.High + origin.Low) : levelPrice;
			int side = originClose > levelPrice + eps ? 1 : (originClose < levelPrice - eps ? -1 : 0);
			int initialSide = side;
			bool useCloseRule = (VirginityBreakRule == VirginityBreakRuleMode.CloseBased);

			for (int k = afterRawIdx + 1; k < rawSessions.Count; k++)
			{
				DayProfile nextRaw = rawSessions[k];
				if (!useCloseRule)
				{
					if (DoesCrossBreakLevel(nextRaw.High, nextRaw.Low, levelPrice, breakMode, initialSide))
					{
						DateTime winStart = nextRaw.EffectiveOpenTime;
						DateTime winEnd = nextRaw.LastSampleTime > DateTime.MinValue ? nextRaw.LastSampleTime : nextRaw.SessionCloseTime;
						if (nextRaw.PeriodSlices != null && nextRaw.PeriodSlices.Count > 0)
						{
							foreach (KeyValuePair<int, PeriodSlice> kv in nextRaw.PeriodSlices)
							{
								PeriodSlice sl = kv.Value;
								if (DoesCrossBreakLevel(sl.High, sl.Low, levelPrice, breakMode, initialSide))
								{
									winStart = sl.StartTime > DateTime.MinValue ? sl.StartTime : nextRaw.EffectiveOpenTime;
									winEnd = sl.LastSampleTime > DateTime.MinValue ? sl.LastSampleTime : (sl.EndTime > DateTime.MinValue ? sl.EndTime : winEnd);
									break;
								}
							}
						}
						drawEndX = ResolveBreakEndXOnChart(cc, chartBars, barSpacing, panelLeft, rightLimitX, xOriginEnd, winStart, winEnd, levelPrice, breakMode, initialSide, true);
						return false;
					}
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
								DateTime winStart = sl.StartTime > DateTime.MinValue ? sl.StartTime : nextRaw.EffectiveOpenTime;
								DateTime winEnd = sl.LastSampleTime > DateTime.MinValue ? sl.LastSampleTime : (sl.EndTime > DateTime.MinValue ? sl.EndTime : nextRaw.SessionCloseTime);
								drawEndX = ResolveBreakEndXOnChart(cc, chartBars, barSpacing, panelLeft, rightLimitX, xOriginEnd, winStart, winEnd, levelPrice, breakMode, initialSide, false);
								return false;
							}
						}
					}
					else if (!nextRunning)
					{
						if (DoesClosedTpoBreakLevel(nextRaw.Open, nextRaw.High, nextRaw.Low, nextRaw.Close, levelPrice, eps, breakMode, ref side, originMid))
						{
							DateTime winEnd = nextRaw.LastSampleTime > DateTime.MinValue ? nextRaw.LastSampleTime : nextRaw.SessionCloseTime;
							drawEndX = ResolveBreakEndXOnChart(cc, chartBars, barSpacing, panelLeft, rightLimitX, xOriginEnd, nextRaw.EffectiveOpenTime, winEnd, levelPrice, breakMode, initialSide, false);
							return false;
						}
					}
				}
			}

			drawEndX = rightLimitX;
			return true;
		}
		#endregion

		#region SharpDX Rendering (Market Profile Zones + Orderflow Footprints/Candles + Right-Side Bar Values)
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
			float panelW = ChartPanel != null ? ChartPanel.W : 1200f;
			bool anyZones = runtimeShowMpZones || runtimeShowSrZones;
			bool showHdrFtr = anyZones && ShowSessionHeaderFooter;
			float footerReserve = 24f;
			float deltaBarReserve = footerReserve + (runtimeShowStatGrid ? 62f : 0f) + 14f;
			float topHeaderH = (panelW < 606f ? 66f : 40f) + (showHdrFtr ? 27f : 0f) + 18f;
			float usableH = Math.Max(150f, panelH - topHeaderH - deltaBarReserve);

			double pricePerPx = span / usableH;
			double autoMax = paddedMax + topHeaderH * pricePerPx;
			double autoMin = paddedMin - deltaBarReserve * pricePerPx;

			if (isFree360View &&
				!double.IsNaN(freeViewMinPrice) &&
				!double.IsNaN(freeViewMaxPrice) &&
				freeViewMaxPrice > freeViewMinPrice)
			{
				MinValue = freeViewMinPrice;
				MaxValue = freeViewMaxPrice;
			}
			else
			{
				MinValue = autoMin;
				MaxValue = autoMax;
			}

			lastRenderedMinPrice = MinValue;
			lastRenderedMaxPrice = MaxValue;
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
							BarBrushes.Set(idx, System.Windows.Media.Brushes.Transparent);
						if (CandleOutlineBrushes.Get(idx) == null)
							CandleOutlineBrushes.Set(idx, System.Windows.Media.Brushes.Transparent);
					}
				}
			}
			catch { }
		}

		protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			if (chartControl == null || chartScale == null || ChartBars == null || RenderTarget == null || IsInHitTest)
				return;

			EnsureVisibleChartCandlesHidden();
			EnsureMouseEventsSubscribed();
			EnsureTradePanelAttachedAsync();

			try
			{
				lastRenderTargetW = RenderTarget.Size.Width;
				lastRenderTargetH = RenderTarget.Size.Height;
			}
			catch { }

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
			bool anyZonesActive = runtimeShowMpZones || runtimeShowSrZones;
			if (anyZonesActive || runtimeShowSignals)
				EnsureMarketProfileSeededFromChartBars();

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
			float panelRight = panelX + panelW;

			lastRenderedChartScale = chartScale;
			lastRenderedPanelY = panelY;
			lastRenderedPanelH = panelH;
			if (!isFree360View && chartScale.MaxValue > chartScale.MinValue)
			{
				lastRenderedMinPrice = chartScale.MinValue;
				lastRenderedMaxPrice = chartScale.MaxValue;
			}

			bool wrapToolbarTwoRows = (panelW < 606f);
			float toolbarBottomY = panelY + (wrapToolbarTwoRows ? 66f : 40f);
			bool showHdrFtr = anyZonesActive && ShowSessionHeaderFooter;
			float topSessionBarH = 28f;
			float topSessionBarY = toolbarBottomY + 3f;
			float canvasTopY = showHdrFtr ? (topSessionBarY + topSessionBarH + 2f) : (toolbarBottomY + 2f);

			float dateFooterH = 26f;
			float dateFooterY = (float)Math.Round(panelY + panelH - dateFooterH - 1f);
			float bottomAfterFooterY = dateFooterY - 1f;

			float deltaStripH = 60f;
			float stripTopY = (float)Math.Round(bottomAfterFooterY - deltaStripH);
			float chartBottomY = runtimeShowStatGrid ? (stripTopY - 1f) : (bottomAfterFooterY - 1f);

			bool light = runtimeLightTheme;
			bool reserveRightTags = anyZonesActive && ShowRightSideValues;
			float rightZoneLimitX = reserveRightTags ? (panelRight - RightValuesLaneWidth) : (panelRight - 4f);

			var oldAntialias = RenderTarget.AntialiasMode;
			var oldTextAntialias = RenderTarget.TextAntialiasMode;
			RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;
			RenderTarget.TextAntialiasMode = SharpDX.Direct2D1.TextAntialiasMode.Cleartype;

			try
			{
				if (light)
				{
					using (var whiteCanvas = new DxBrush(RenderTarget, new Color4(0.975f, 0.982f, 0.992f, 1.0f)))
					{
						RenderTarget.FillRectangle(new RectangleF(panelX, panelY, panelW, panelH), whiteCanvas);
					}
				}
				else
				{
					// AK10X Market Profile Studio Deep Midnight Navy Gradient Backdrop + Subtle Studio Grid & Right Dock
					var bgStops = new SharpDX.Direct2D1.GradientStop[]
					{
						new SharpDX.Direct2D1.GradientStop { Position = 0.0f, Color = new SharpDX.Color(6, 10, 19, 255) },
						new SharpDX.Direct2D1.GradientStop { Position = 0.52f, Color = new SharpDX.Color(11, 18, 33, 255) },
						new SharpDX.Direct2D1.GradientStop { Position = 1.0f, Color = new SharpDX.Color(7, 12, 22, 255) }
					};
					using (var stopCol = new SharpDX.Direct2D1.GradientStopCollection(RenderTarget, bgStops))
					using (var bgGrad = new SharpDX.Direct2D1.LinearGradientBrush(
						RenderTarget,
						new SharpDX.Direct2D1.LinearGradientBrushProperties
						{
							StartPoint = new Vector2(panelX, panelY),
							EndPoint = new Vector2(panelX, panelY + panelH)
						},
						stopCol))
					using (var studioGridBrush = new DxBrush(RenderTarget, new SharpDX.Color(148, 163, 184, 16)))
					using (var rightDockBrush = new DxBrush(RenderTarget, new SharpDX.Color(7, 12, 22, 238)))
					using (var rightDockLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(56, 189, 248, 68)))
					{
						RenderTarget.FillRectangle(new RectangleF(panelX, panelY, panelW, panelH), bgGrad);
						for (float gy = canvasTopY + 28f; gy < chartBottomY - 12f; gy += 54f)
						{
							RenderTarget.DrawLine(new Vector2(panelX + 2f, gy), new Vector2(rightZoneLimitX, gy), studioGridBrush, 1.0f);
						}
						if (reserveRightTags && panelRight - rightZoneLimitX > 20f)
						{
							RenderTarget.FillRectangle(new RectangleF(rightZoneLimitX, canvasTopY - 2f, panelRight - rightZoneLimitX, Math.Max(10f, chartBottomY - canvasTopY + 3f)), rightDockBrush);
							RenderTarget.DrawLine(new Vector2(rightZoneLimitX, canvasTopY - 2f), new Vector2(rightZoneLimitX, chartBottomY + 1f), rightDockLineBrush, 1.0f);
						}
					}
				}

				using (var brushBuyerCandle = new DxBrush(RenderTarget, new Color4(0.063f, 0.808f, 0.565f, 1.0f)))    // #10CE90 AK10X Studio Emerald
				using (var brushSellerCandle = new DxBrush(RenderTarget, new Color4(0.957f, 0.247f, 0.369f, 1.0f)))   // #F43F5E AK10X Studio Crimson-Rose
				using (var brushBoxBg = new DxBrush(RenderTarget, light
					? new Color4(0.965f, 0.975f, 0.988f, 0.90f)
					: new Color4(0.047f, 0.078f, 0.141f, 0.88f)))
				using (var brushSpineLaneBg = new DxBrush(RenderTarget, light
					? new Color4(0.925f, 0.942f, 0.965f, 0.94f)
					: new Color4(0.031f, 0.051f, 0.094f, 0.95f)))
				using (var brushBoxBorder = new DxBrush(RenderTarget, light
					? new Color4(0.74f, 0.80f, 0.87f, 0.90f)
					: new Color4(0.220f, 0.741f, 0.973f, 0.32f)))
				using (var brushRowGrid = new DxBrush(RenderTarget, light
					? new Color4(0.82f, 0.86f, 0.91f, 0.65f)
					: new Color4(0.220f, 0.450f, 0.680f, 0.24f)))
				using (var brushAskImbBlock = new DxBrush(RenderTarget, new Color4(0.045f, 0.635f, 0.435f, 0.92f)))
				using (var brushBidImbBlock = new DxBrush(RenderTarget, new Color4(0.890f, 0.195f, 0.295f, 0.92f)))
				using (var brushImbWhiteText = new DxBrush(RenderTarget, new Color4(1.0f, 1.0f, 1.0f, 1.0f)))
				using (var brushNormalText = new DxBrush(RenderTarget, light
					? new Color4(0.12f, 0.16f, 0.22f, 0.98f)
					: new Color4(0.91f, 0.95f, 0.99f, 0.98f)))
				using (var brushZeroText = new DxBrush(RenderTarget, light
					? new Color4(0.45f, 0.52f, 0.60f, 0.82f)
					: new Color4(0.52f, 0.60f, 0.70f, 0.80f)))
				using (var brushPocOutline = new DxBrush(RenderTarget, light
					? new Color4(0.85f, 0.45f, 0.04f, 0.92f)
					: new Color4(0.984f, 0.749f, 0.141f, 0.95f)))
				using (var brushHalfpackLine = new DxBrush(RenderTarget, light
					? new Color4(0.06f, 0.10f, 0.18f, 0.98f)
					: new Color4(1.0f, 1.0f, 1.0f, 0.98f)))
				using (var brushAskVolShade = new DxBrush(RenderTarget, new Color4(0.063f, 0.800f, 0.545f, light ? 0.22f : 0.30f)))
				using (var brushBidVolShade = new DxBrush(RenderTarget, new Color4(0.957f, 0.263f, 0.247f, light ? 0.22f : 0.30f)))
				using (var brushDeltaBadgePosBg = new DxBrush(RenderTarget, new Color4(0.045f, 0.38f, 0.28f, 0.92f)))
				using (var brushDeltaBadgeNegBg = new DxBrush(RenderTarget, new Color4(0.46f, 0.13f, 0.15f, 0.92f)))
				using (var brushHeightBadgeBg = new DxBrush(RenderTarget, light
					? new Color4(0.91f, 0.94f, 0.98f, 0.95f)
					: new Color4(0.09f, 0.13f, 0.20f, 0.94f)))
				using (var brushHeightBadgeBorder = new DxBrush(RenderTarget, light
					? new Color4(0.68f, 0.76f, 0.86f, 0.90f)
					: new Color4(0.22f, 0.31f, 0.45f, 0.88f)))
				using (var brushHeightBadgeText = new DxBrush(RenderTarget, light
					? new Color4(0.10f, 0.18f, 0.30f, 0.98f)
					: new Color4(0.84f, 0.92f, 1.0f, 0.98f)))
				using (var brushBidImbZone = new DxBrush(RenderTarget, new Color4(0.94f, 0.24f, 0.24f, 0.14f)))
				using (var brushAskImbZone = new DxBrush(RenderTarget, new Color4(0.06f, 0.78f, 0.53f, 0.14f)))
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

					FootprintBarData toolbarRefFp = visibleBars.Count > 0 ? visibleBars[visibleBars.Count - 1] : null;
					if (focusedCandleBarIndex >= 0)
					{
						for (int vIdx = 0; vIdx < visibleBars.Count; vIdx++)
						{
							if (visibleBars[vIdx].BarIndex == focusedCandleBarIndex)
							{
								toolbarRefFp = visibleBars[vIdx];
								break;
							}
						}
					}
					float sampleStep = toolbarRefFp != null ? (float)toolbarRefFp.EffectivePriceStep : (float)(TickSize * 10);
					string sampleSessTag = toolbarRefFp != null ? (toolbarRefFp.SessionTag ?? string.Empty) : string.Empty;
					double midP = toolbarRefFp != null ? toolbarRefFp.Close : 1000.0;
					Func<double, float> priceToY = (double price) => (float)chartScale.GetYByValue(price);

					float rawRowH = Math.Abs(chartScale.GetYByValue(midP) - chartScale.GetYByValue(midP + sampleStep));
					for (int vIdx = 0; vIdx < visibleBars.Count; vIdx++)
					{
						float bStep = (float)visibleBars[vIdx].EffectivePriceStep;
						if (bStep > 0f)
						{
							float bRowH = Math.Abs(chartScale.GetYByValue(visibleBars[vIdx].Close) - chartScale.GetYByValue(visibleBars[vIdx].Close + bStep));
							if (bRowH > rawRowH)
								rawRowH = bRowH;
						}
					}
					bool isCandlestickZoom = (barSpacing < CandlestickZoomThresholdPx);

					// =========================================================================================
					// LAYER 1: Market Profile Zones, Multi-TF S/R Zones & Right-Side Bar Values Column
					// =========================================================================================
					if (anyZonesActive)
					{
						RenderMarketProfileZonesAndRightValues(
							chartControl,
							priceToY,
							barSpacing,
							panelX,
							canvasTopY,
							panelRight,
							rightZoneLimitX,
							chartBottomY,
							light);
					}

					// =========================================================================================
					// LAYER 2: Orderflow Chart (Candlesticks / Inside-Box Footprints, clipped cleanly to canvas)
					// =========================================================================================
					float candleClipW = Math.Max(40f, rightZoneLimitX - panelX);
					RenderTarget.PushAxisAlignedClip(new RectangleF(panelX, canvasTopY, candleClipW, Math.Max(40f, chartBottomY - canvasTopY - 1f)), AntialiasMode.PerPrimitive);
					try
					{
						if (runtimeShowImbZones && visibleBars.Count > 0)
						{
							RenderStackedImbalanceZones(chartControl, priceToY, visibleBars, lastBar, barSpacing, chartBottomY,
								brushBidImbZone, brushAskImbZone, brushSellerCandle, brushBuyerCandle);
						}

						// Pass 2A (Underlay): Draw live SL/TP/Entry lines & soft R:R shading BEHIND candles so candles are never hidden!
						if (EnableLiveChartOrders)
						{
							RenderLiveOrdersAndDraftPreviewOnCanvas(
								chartControl,
								priceToY,
								panelX,
								canvasTopY,
								candleClipW,
								chartBottomY,
								light,
								drawUnderlayPass: true,
								drawOverlayPillsPass: false);
						}
						else
						{
							liveChartHitRegions.Clear();
						}

						if (isCandlestickZoom)
						{
							RenderCleanCandlesticks(chartControl, priceToY, visibleBars, barSpacing, panelX, candleClipW, brushBuyerCandle, brushSellerCandle);
						}
						else
						{
							RenderInsideBoxCandleFootprints(
								chartControl, priceToY, visibleBars, barSpacing, rawRowH,
								panelX, canvasTopY, candleClipW, chartBottomY,
								brushBuyerCandle, brushSellerCandle,
								brushBoxBg, brushSpineLaneBg, brushBoxBorder, brushRowGrid,
								brushAskImbBlock, brushBidImbBlock, brushImbWhiteText,
								brushNormalText, brushZeroText, brushPocOutline, brushHalfpackLine,
								brushAskVolShade, brushBidVolShade,
								brushDeltaBadgePosBg, brushDeltaBadgeNegBg,
								brushHeightBadgeBg, brushHeightBadgeBorder, brushHeightBadgeText);
						}

						if (runtimeShowSignals)
						{
							RenderChartSignalsOnCanvas(
								chartControl,
								priceToY,
								visibleBars,
								barSpacing,
								isCandlestickZoom,
								panelX,
								canvasTopY,
								candleClipW,
								chartBottomY,
								light);
						}

						if (rrPositionBoxes.Count > 0 || pendingRrToolMode != 0)
						{
							RenderRrPositionBoxesOnCanvas(
								chartControl,
								priceToY,
								barSpacing,
								panelX,
								canvasTopY,
								candleClipW,
								chartBottomY,
								light);
						}

						// Pass 2B (Overlay): Draw crisp interactive right-side SL/TP/Position pills on top without covering candles
						if (EnableLiveChartOrders)
						{
							RenderLiveOrdersAndDraftPreviewOnCanvas(
								chartControl,
								priceToY,
								panelX,
								canvasTopY,
								candleClipW,
								chartBottomY,
								light,
								drawUnderlayPass: false,
								drawOverlayPillsPass: true);
						}
					}
					finally
					{
						RenderTarget.PopAxisAlignedClip();
					}

					// =========================================================================================
					// LAYER 3: 3-Row Bottom Table (Momentum | Strength | Delta)
					// =========================================================================================
					if (runtimeShowStatGrid && visibleBars.Count > 0)
					{
						RenderThreeRowBottomTable(chartControl, visibleBars, panelX, panelW, rightZoneLimitX, reserveRightTags, stripTopY, deltaStripH, barSpacing, light);
					}

					// =========================================================================================
					// LAYER 4: Top Session Header Strip, Session Base Lines & Bottom Trading Date Footer Strip
					// =========================================================================================
					if (showHdrFtr)
					{
						RenderSessionHeaderAndDateFooter(
							chartControl,
							barSpacing,
							panelX,
							panelRight,
							rightZoneLimitX,
							topSessionBarY,
							topSessionBarH,
							runtimeShowStatGrid ? stripTopY : dateFooterY,
							dateFooterY,
							dateFooterH,
							light);
					}

					// =========================================================================================
					// LAYER 4B: Bold High-Visibility Timeframe Candle Countdown (Bottom-Right Cap)
					// =========================================================================================
					float cdCapW = (panelRight - rightZoneLimitX > 30f)
						? Math.Max(20f, panelRight - rightZoneLimitX - 2f)
						: Math.Min(154f, Math.Max(92f, panelW - 8f));
					float cdCapX = (panelRight - rightZoneLimitX > 30f)
						? (rightZoneLimitX + 1f)
						: (panelRight - cdCapW - 2f);
					RenderBottomRightTimeframeCountdownBox(new RectangleF(cdCapX, dateFooterY, cdCapW, dateFooterH), light);

					// =========================================================================================
					// LAYER 5: Sleek Top-Left AK10X BigSort Toolbar
					// =========================================================================================
					RenderSleekTopToolbar(panelX, panelY, barSpacing, sampleStep, isCandlestickZoom, sampleSessTag);
				}
			}
			catch { }
			finally
			{
				RenderTarget.TextAntialiasMode = oldTextAntialias;
				RenderTarget.AntialiasMode = oldAntialias;
			}
		}

		private void ComputeAlignedSessionAndDateSpans(
			ChartControl chartControl,
			Bars chartBars,
			List<DayProfile> allSessions,
			DateTime refNow,
			float barSpacing,
			float panelLeft,
			float rightLimitX,
			out List<DateSpanGroup> dateGroups)
		{
			dateGroups = new List<DateSpanGroup>();
			if (allSessions == null || allSessions.Count == 0)
				return;

			for (int i = 0; i < allSessions.Count; i++)
			{
				DayProfile d = allSessions[i];
				bool isRunning = IsSessionRunning(d, i == allSessions.Count - 1, refNow);
				GetSessionChartXSpan(chartControl, chartBars, d, isRunning, barSpacing, panelLeft, out d.ChartStartX, out d.ChartEndX);
				d.HeaderLeftX = d.ChartStartX;
				d.HeaderRightX = d.ChartEndX;
			}

			float maxSnapGap = Math.Max(28f, barSpacing * 1.75f);
			for (int i = 0; i < allSessions.Count - 1; i++)
			{
				DayProfile cur = allSessions[i];
				DayProfile next = allSessions[i + 1];
				if (cur.ChartEndX > panelLeft - 18f && next.ChartStartX > panelLeft - 18f)
				{
					float gap = next.ChartStartX - cur.ChartEndX;
					if (gap >= -8f && gap <= maxSnapGap)
					{
						float midX = (float)Math.Round(0.5f * (cur.ChartEndX + next.ChartStartX));
						cur.HeaderRightX = midX;
						next.HeaderLeftX = midX;
					}
				}
			}

			DayProfile latest = allSessions[allSessions.Count - 1];
			if (latest.HeaderRightX > panelLeft && latest.HeaderRightX < rightLimitX && (rightLimitX - latest.HeaderRightX) <= Math.Max(42f, barSpacing * 0.9f))
			{
				latest.HeaderRightX = rightLimitX;
			}

			for (int i = 0; i < allSessions.Count; i++)
			{
				DayProfile s = allSessions[i];
				DateTime dt = s.SessionDate.Date;
				if (dateGroups.Count > 0 && dateGroups[dateGroups.Count - 1].Date == dt)
				{
					DateSpanGroup curGrp = dateGroups[dateGroups.Count - 1];
					curGrp.Sessions.Add(s);
					curGrp.RightX = Math.Max(curGrp.RightX, s.HeaderRightX);
				}
				else
				{
					DateSpanGroup grp = new DateSpanGroup
					{
						Date = dt,
						LeftX = s.HeaderLeftX,
						RightX = s.HeaderRightX
					};
					grp.Sessions.Add(s);
					dateGroups.Add(grp);
				}
			}
		}

		private void RenderMarketProfileZonesAndRightValues(
			ChartControl chartControl,
			Func<double, float> priceToY,
			float barSpacing,
			float panelLeft,
			float canvasTopY,
			float panelRight,
			float rightLimitX,
			float chartBottomY,
			bool light)
		{
			if (ChartBars == null || ChartBars.Bars == null || ChartBars.Bars.Count == 0)
				return;

			Bars chartBars = ChartBars.Bars;
			float canvasBotY = chartBottomY - 2f;
			if (canvasBotY <= canvasTopY + 20f)
				return;

			SrComputedSnapshot srSnapshot = runtimeShowSrZones ? EnsureSrZonesComputed(chartBars) : null;

			bool enteredProfileLock = System.Threading.Monitor.TryEnter(profileLock, 2);
			try
			{
				List<DayProfile> rawSessions;
				List<DayProfile> allSessions;
				double step = GetMpPriceStep();

				if (enteredProfileLock)
				{
					rawSessions = new List<DayProfile>(profiles.Count);
					foreach (var kvp in profiles)
					{
						if (kvp.Value != null && kvp.Value.Rows.Count > 0)
							rawSessions.Add(kvp.Value);
					}
					allSessions = rawSessions.Count > 0
						? BuildDisplaySessions(rawSessions)
						: new List<DayProfile>();

					if (runtimeShowMpZones)
					{
						for (int i = 0; i < allSessions.Count; i++)
						{
							DayProfile d = allSessions[i];
							bool isLiveIdx = (i == allSessions.Count - 1);
							if (!d.ContinuityChecked || isLiveIdx)
							{
								EnsureSessionRowContinuity(d, step);
								d.ContinuityChecked = true;
							}
							if (!d.AnalyticsValid || Math.Abs(d.AnalyticsStep - step) > 1e-9 || Math.Abs(d.AnalyticsVaPct - ValueAreaPercent) > 1e-9)
							{
								CalculateTpoValueArea(d, ValueAreaPercent, out d.PocBin, out d.VahBin, out d.ValBin);
								double maxV = 1e-9;
								double bestVol = -1.0;
								int bestVolBin = d.PocBin;
								int bestDist = int.MaxValue;
								int minB = int.MaxValue;
								int maxB = int.MinValue;
								foreach (KeyValuePair<int, PriceRow> rkv in d.Rows)
								{
									int rk = rkv.Key;
									if (rk < minB) minB = rk;
									if (rk > maxB) maxB = rk;
									double rv = rkv.Value.Volume;
									if (rv > maxV) maxV = rv;
									int dist = Math.Abs(rk - d.PocBin);
									if (rv > bestVol || (Math.Abs(rv - bestVol) <= 1e-9 && dist < bestDist))
									{
										bestVol = rv;
										bestVolBin = rk;
										bestDist = dist;
									}
								}
								d.MaxRowVolume = maxV;
								d.VolumePocBin = bestVolBin;
								CalculateVolumeValueArea(d, ValueAreaPercent, d.VolumePocBin, out d.VolumeVahBin, out d.VolumeValBin);
								d.SinglePrintZones = minB <= maxB ? ComputeSinglePrintZones(d, minB, maxB, d.PocBin) : new List<SinglePrintZone>();
								d.AnalyticsStep = step;
								d.AnalyticsVaPct = ValueAreaPercent;
								d.AnalyticsValid = true;
							}
						}
					}
					lastMpRenderSessionsSnapshot = allSessions;
				}
				else
				{
					rawSessions = lastMpRenderSessionsSnapshot ?? new List<DayProfile>();
					allSessions = rawSessions;
				}

				if (allSessions.Count == 0 && srSnapshot == null)
					return;

				DateTime refNow = rawSessions.Count > 0 ? GetReferenceNow(rawSessions) : chartBars.GetTime(chartBars.Count - 1);
				if (allSessions.Count > 0)
					ComputeAlignedSessionAndDateSpans(chartControl, chartBars, allSessions, refNow, barSpacing, panelLeft, rightLimitX, out _);
				double breakEps = GetVirginBreakEpsilon();
				List<RightEdgeTag> rightEdgeTags = new List<RightEdgeTag>();

				var dashProps = new StrokeStyleProperties
				{
					DashStyle = SharpDX.Direct2D1.DashStyle.Custom,
					DashCap = CapStyle.Round,
					StartCap = CapStyle.Round,
					EndCap = CapStyle.Round
				};

				DxBrush[] sessionLineBrushes = new DxBrush[SessionColors.Length];
				DxBrush[] sessionGlowBrushes = new DxBrush[SessionColors.Length];
				try
				{
					for (int sIdx = 0; sIdx < SessionColors.Length; sIdx++)
					{
						SharpDX.Color sc = SessionColors[sIdx];
						sessionLineBrushes[sIdx] = new DxBrush(RenderTarget, light
							? new SharpDX.Color((int)(sc.R * 0.72f), (int)(sc.G * 0.68f), (int)(sc.B * 0.72f), 220)
							: new SharpDX.Color(sc.R, sc.G, sc.B, (byte)205));
						sessionGlowBrushes[sIdx] = new DxBrush(RenderTarget, light
							? new SharpDX.Color((int)(sc.R * 0.75f), (int)(sc.G * 0.70f), (int)(sc.B * 0.75f), 26)
							: new SharpDX.Color(sc.R, sc.G, sc.B, (byte)32));
					}

				using (var dashedStroke = new StrokeStyle(Globals.D2DFactory, dashProps, new float[] { 4f, 3f }))
				using (var sessionBaseDashStroke = new StrokeStyle(Globals.D2DFactory, dashProps, new float[] { 7f, 4f }))
				using (var orangeBrush = new DxBrush(RenderTarget, new SharpDX.Color(251, 191, 36)))
				using (var yellowBrush = new DxBrush(RenderTarget, new SharpDX.Color(253, 224, 71)))
				using (var highExtremeBrush = new DxBrush(RenderTarget, new SharpDX.Color(52, 211, 153)))
				using (var lowExtremeBrush = new DxBrush(RenderTarget, new SharpDX.Color(251, 113, 133)))
				using (var highSwingLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(52, 211, 153, 205)))
				using (var lowSwingLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(251, 113, 133, 205)))
				using (var highExtremeBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(52, 211, 153, 215)))
				using (var lowExtremeBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(251, 113, 133, 215)))
				using (var vaFillBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(14, 116, 168, 36) : new SharpDX.Color(14, 116, 144, 58)))
				using (var vaBorderBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(18, 118, 168, 210) : new SharpDX.Color(56, 189, 248, 232)))
				using (var vaVirginBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(10, 132, 188) : new SharpDX.Color(88, 204, 248)))
				using (var singleBuyZoneRayBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(16, 175, 88, 56) : new SharpDX.Color(16, 185, 129, 72)))
				using (var singleSellZoneRayBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(220, 38, 56, 56) : new SharpDX.Color(244, 63, 94, 72)))
				using (var singleBuyBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(48, 246, 128, 250)))
				using (var singleSellBorderBrush = new DxBrush(RenderTarget, new SharpDX.Color(255, 86, 98, 250)))
				using (var spBuyPillBgBrush = new DxBrush(RenderTarget, new SharpDX.Color(6, 48, 32, 248)))
				using (var spSellPillBgBrush = new DxBrush(RenderTarget, new SharpDX.Color(60, 14, 26, 248)))
				using (var pocLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(245, 158, 11)))
				using (var vppocLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(250, 204, 21)))
				using (var midLineBrush = new DxBrush(RenderTarget, new SharpDX.Color(218, 128, 255)))
				using (var pillBgBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(244, 248, 252, 250) : new SharpDX.Color(13, 21, 36, 248)))
				using (var cardBorderBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(148, 168, 192, 215) : new SharpDX.Color(56, 189, 248, 92)))
				using (var rightLaneLineBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(165, 178, 194, 190) : new SharpDX.Color(56, 189, 248, 90)))
				using (var labelRightFormat = new DxTextFormat(Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 9.3f))
				{
					labelRightFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
					labelRightFormat.ParagraphAlignment = ParagraphAlignment.Center;
					labelRightFormat.WordWrapping = WordWrapping.NoWrap;

					RenderTarget.PushAxisAlignedClip(new RectangleF(panelLeft, canvasTopY, Math.Max(40f, rightLimitX - panelLeft), Math.Max(40f, chartBottomY - canvasTopY - 1f)), AntialiasMode.PerPrimitive);
					try
					{
						// 1A. Closed Monday-Friday WeeklyHigh & WeeklyLow Virgin Lines
						if (runtimeShowMpZones && (ShowWeeklyHigh || ShowWeeklyLow) && rawSessions.Count > 0)
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
									GetSessionChartXSpan(chartControl, chartBars, rawSessions[highRawIdx], false, barSpacing, panelLeft, out float rawOriginX, out float rawOriginEndX);
									float yWkHigh = priceToY(wkHigh);
									if (yWkHigh >= canvasTopY && yWkHigh <= canvasBotY && rawOriginX <= rightLimitX)
									{
										bool isWkHighVirgin = GetRawLevelExtensionOnChart(
											chartControl, chartBars, barSpacing, rawSessions, lastWkRawIdx,
											wkHigh, rawOriginEndX, panelLeft, rightLimitX, refNow, breakEps, LevelBreakMode.CloseAbove, out float wkHighEndX);
										float drawStartX = Math.Max(panelLeft + 2f, rawOriginX);
										if (wkHighEndX > drawStartX + 1.5f)
											DrawHairlineHorizontal(drawStartX, wkHighEndX, yWkHigh, highExtremeBrush, 1.35f);

										if (isWkHighVirgin)
										{
											rightEdgeTags.Add(new RightEdgeTag
											{
												Price = wkHigh,
												IdealY = yWkHigh - 7.5f,
												Height = 15f,
												Text = "WeeklyHigh (" + wkLabel + " " + FormatMpPrice(wkHigh) + ")",
												Brush = highExtremeBrush,
												BorderBrush = highExtremeBorderBrush,
												Priority = 5
											});
										}
									}
								}

								if (ShowWeeklyLow && wkLow < double.MaxValue)
								{
									GetSessionChartXSpan(chartControl, chartBars, rawSessions[lowRawIdx], false, barSpacing, panelLeft, out float rawOriginX, out float rawOriginEndX);
									float yWkLow = priceToY(wkLow);
									if (yWkLow >= canvasTopY && yWkLow <= canvasBotY && rawOriginX <= rightLimitX)
									{
										bool isWkLowVirgin = GetRawLevelExtensionOnChart(
											chartControl, chartBars, barSpacing, rawSessions, lastWkRawIdx,
											wkLow, rawOriginEndX, panelLeft, rightLimitX, refNow, breakEps, LevelBreakMode.CloseBelow, out float wkLowEndX);
										float drawStartX = Math.Max(panelLeft + 2f, rawOriginX);
										if (wkLowEndX > drawStartX + 1.5f)
											DrawHairlineHorizontal(drawStartX, wkLowEndX, yWkLow, lowExtremeBrush, 1.35f);

										if (isWkLowVirgin)
										{
											rightEdgeTags.Add(new RightEdgeTag
											{
												Price = wkLow,
												IdealY = yWkLow - 7.5f,
												Height = 15f,
												Text = "WeeklyLow (" + wkLabel + " " + FormatMpPrice(wkLow) + ")",
												Brush = lowExtremeBrush,
												BorderBrush = lowExtremeBorderBrush,
												Priority = 5
											});
										}
									}
								}
							}
						}

						// 1B. Closed-Day DayHigh & DayLow Virgin Lines (never on running day or running session)
						if (runtimeShowMpZones && (ShowSwingHigh || ShowSwingLow) && rawSessions.Count > 0)
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
									GetSessionChartXSpan(chartControl, chartBars, rawSessions[highRawIdx], false, barSpacing, panelLeft, out float rawOriginX, out float rawOriginEndX);
									float yDayHigh = priceToY(dHigh);
									if (yDayHigh >= canvasTopY && yDayHigh <= canvasBotY && rawOriginX <= rightLimitX)
									{
										bool isDayHighVirgin = GetRawLevelExtensionOnChart(
											chartControl, chartBars, barSpacing, rawSessions, lastDayRawIdx,
											dHigh, rawOriginEndX, panelLeft, rightLimitX, refNow, breakEps, LevelBreakMode.CloseAbove, out float dHighEndX);
										float drawStartX = Math.Max(panelLeft + 2f, rawOriginX);
										if (dHighEndX > drawStartX + 1.5f)
											DrawHairlineHorizontal(drawStartX, dHighEndX, yDayHigh, highSwingLineBrush, 0.90f);

										if (isDayHighVirgin)
										{
											rightEdgeTags.Add(new RightEdgeTag
											{
												Price = dHigh,
												IdealY = yDayHigh - 7.5f,
												Height = 15f,
												Text = "DayHigh (" + dayDateLbl + " " + FormatMpPrice(dHigh) + ")",
												Brush = highExtremeBrush,
												Priority = 3
											});
										}
									}
								}

								if (ShowSwingLow && dLow < double.MaxValue)
								{
									GetSessionChartXSpan(chartControl, chartBars, rawSessions[lowRawIdx], false, barSpacing, panelLeft, out float rawOriginX, out float rawOriginEndX);
									float yDayLow = priceToY(dLow);
									if (yDayLow >= canvasTopY && yDayLow <= canvasBotY && rawOriginX <= rightLimitX)
									{
										bool isDayLowVirgin = GetRawLevelExtensionOnChart(
											chartControl, chartBars, barSpacing, rawSessions, lastDayRawIdx,
											dLow, rawOriginEndX, panelLeft, rightLimitX, refNow, breakEps, LevelBreakMode.CloseBelow, out float dLowEndX);
										float drawStartX = Math.Max(panelLeft + 2f, rawOriginX);
										if (dLowEndX > drawStartX + 1.5f)
											DrawHairlineHorizontal(drawStartX, dLowEndX, yDayLow, lowSwingLineBrush, 0.90f);

										if (isDayLowVirgin)
										{
											rightEdgeTags.Add(new RightEdgeTag
											{
												Price = dLow,
												IdealY = yDayLow - 7.5f,
												Height = 15f,
												Text = "DayLow (" + dayDateLbl + " " + FormatMpPrice(dLow) + ")",
												Brush = lowExtremeBrush,
												Priority = 3
											});
										}
									}
								}
							}
						}

						// 1C. Per-Session Value Area Boxes, Single Prints, PoorHigh/Low, VAH/VAL, VPoC/VPPoC & MidLine
						DateTime prevSessDate = DateTime.MinValue;
						for (int globalIdx = 0; globalIdx < allSessions.Count; globalIdx++)
						{
							DayProfile day = allSessions[globalIdx];
							bool isLatestSession = (globalIdx == allSessions.Count - 1);
							bool isRunningSession = IsSessionRunning(day, isLatestSession, refNow);
							double dStep = day.EffectiveStep > 0 ? day.EffectiveStep : step;

							float rawSessStartX = day.ChartStartX;
							float rawSessEndX = day.ChartEndX;
							if (rawSessStartX > rightLimitX)
								continue;

							bool isNewDate = (day.SessionDate.Date != prevSessDate);
							prevSessDate = day.SessionDate.Date;

							float divX = (float)Math.Round(day.HeaderLeftX) + 0.5f;
							if (ShowSessionDividers && divX >= panelLeft + 2f && divX <= rightLimitX - 2f)
							{
								int sColIdx = Math.Max(0, Math.Min(day.SessionId, sessionLineBrushes.Length - 1));
								RenderTarget.FillRectangle(new RectangleF(divX - 1.8f, canvasTopY, 3.6f, canvasBotY - canvasTopY), sessionGlowBrushes[sColIdx]);
								RenderTarget.DrawLine(new Vector2(divX, canvasTopY), new Vector2(divX, canvasBotY), sessionLineBrushes[sColIdx], 1.75f, sessionBaseDashStroke);
							}

							if (!runtimeShowMpZones)
								continue;

							int maxBin = day.Rows.Keys.Max();
							int minBin = day.Rows.Keys.Min();

							double vahPrice = RoundToInstrumentTick(day.VahBin * dStep);
							double valPrice = RoundToInstrumentTick(day.ValBin * dStep);
							float yVahLine = priceToY(vahPrice);
							float yValLine = priceToY(valPrice);

							bool allowVah = ShowVirginVAH && (!isRunningSession || (EnableRunningSessionLevels && ShowRunningSessionVAH));
							bool allowVal = ShowVirginVAL && (!isRunningSession || (EnableRunningSessionLevels && ShowRunningSessionVAL));

							// Session Value Area Shaded Box (from session start to session end)
							if (ShowValueAreaZoneFill && (allowVah || allowVal) && rawSessEndX >= panelLeft + 2f)
							{
								float boxLeftX = Math.Max(panelLeft + 2f, rawSessStartX);
								float boxRightX = Math.Min(rightLimitX, rawSessEndX);
								float yTopVa = Math.Min(yVahLine, yValLine);
								float yBotVa = Math.Max(yVahLine, yValLine);
								float clTopVa = Math.Max(canvasTopY, yTopVa);
								float clBotVa = Math.Min(canvasBotY, yBotVa);

								if (boxRightX > boxLeftX + 2f && clBotVa > clTopVa + 1.5f)
								{
									RectangleF vaBoxRect = new RectangleF(boxLeftX, clTopVa, boxRightX - boxLeftX, clBotVa - clTopVa);
									RenderTarget.FillRoundedRectangle(new RoundedRectangle { Rect = vaBoxRect, RadiusX = 4f, RadiusY = 4f }, vaFillBrush);
									RenderTarget.FillRectangle(new RectangleF(boxLeftX, clTopVa, 2.4f, vaBoxRect.Height), vaBorderBrush);
									if (allowVah && yVahLine >= canvasTopY && yVahLine <= canvasBotY)
										RenderTarget.DrawLine(new Vector2(boxLeftX, yVahLine), new Vector2(boxRightX, yVahLine), vaBorderBrush, 1.35f, dashedStroke);
									if (allowVal && yValLine >= canvasTopY && yValLine <= canvasBotY)
										RenderTarget.DrawLine(new Vector2(boxLeftX, yValLine), new Vector2(boxRightX, yValLine), vaBorderBrush, 1.35f, dashedStroke);
								}
							}

							// 1. Extending Buying & Selling Single Print Zones + Bold SL Trigger Lines
							if (ShowSinglePrints && day.SinglePrintZones.Count > 0)
							{
								for (int z = 0; z < day.SinglePrintZones.Count; z++)
								{
									SinglePrintZone spZone = day.SinglePrintZones[z];
									double spSlPrice = GetSinglePrintStopLossPrice(spZone, dStep);
									double spOppPrice = RoundToInstrumentTick(spZone.IsBuying
										? (spZone.EndBin * dStep + dStep * 0.5)
										: (spZone.StartBin * dStep - dStep * 0.5));

									float ySpSlLine = priceToY(spSlPrice);
									float ySpOppLine = priceToY(spOppPrice);
									float ySpTop = Math.Min(ySpSlLine, ySpOppLine);
									float ySpBot = Math.Max(ySpSlLine, ySpOppLine);

									float spOriginX = rawSessStartX;
									if (day.PeriodSlices.TryGetValue(spZone.PeriodIndex, out PeriodSlice spSlice) && spSlice.StartTime > DateTime.MinValue)
									{
										int spBarIdx = FindFirstChartBarAfterOpen(chartBars, spSlice.StartTime);
										if (spBarIdx >= 0)
											GetBarGridColumnBounds(chartControl, spBarIdx, barSpacing, out spOriginX, out _, out _);
									}

									bool isSpVirgin = GetSessionLevelExtensionOnChart(
										chartControl, chartBars, barSpacing, allSessions, globalIdx, spSlPrice,
										rawSessEndX, panelLeft, rightLimitX, refNow, breakEps,
										spZone.IsBuying ? LevelBreakMode.CloseBelow : LevelBreakMode.CloseAbove,
										out float drawEndX);

									if (ySpBot >= canvasTopY && ySpTop <= canvasBotY)
									{
										float drawStartX = Math.Max(panelLeft + 2f, spOriginX);
										if (drawEndX > drawStartX + 2f)
										{
											DxBrush spBorder = spZone.IsBuying ? singleBuyBorderBrush : singleSellBorderBrush;
											float clRayTop = Math.Max(canvasTopY, ySpTop);
											float clRayBot = Math.Min(canvasBotY, ySpBot);
											if (clRayBot > clRayTop)
											{
												RectangleF spRayRect = new RectangleF(drawStartX, clRayTop, drawEndX - drawStartX, Math.Max(2f, clRayBot - clRayTop));
												RenderTarget.FillRectangle(spRayRect, spZone.IsBuying ? singleBuyZoneRayBrush : singleSellZoneRayBrush);
											}
											if (ySpOppLine >= canvasTopY && ySpOppLine <= canvasBotY)
											{
												RenderTarget.DrawLine(new Vector2(drawStartX, ySpOppLine), new Vector2(drawEndX, ySpOppLine), spBorder, 1.05f, dashedStroke);
											}
											if (ySpSlLine >= canvasTopY && ySpSlLine <= canvasBotY)
											{
												RenderTarget.DrawLine(new Vector2(drawStartX, ySpSlLine), new Vector2(drawEndX, ySpSlLine), spBorder, 1.9f);
											}
										}

										if (isSpVirgin && ySpSlLine >= canvasTopY && ySpSlLine <= canvasBotY)
										{
											string spType = spZone.IsBuying ? "Buying SP" : "Selling SP";
											rightEdgeTags.Add(new RightEdgeTag
											{
												Price = spSlPrice,
												IdealY = ySpSlLine - 7.5f,
												Height = 15f,
												Text = spType + " (" + day.SessionName + " " + FormatMpPrice(spSlPrice) + ")",
												Brush = spZone.IsBuying ? singleBuyBorderBrush : singleSellBorderBrush,
												FillBrush = spZone.IsBuying ? spBuyPillBgBrush : spSellPillBgBrush,
												BorderBrush = spZone.IsBuying ? singleBuyBorderBrush : singleSellBorderBrush,
												Priority = 3
											});
										}
									}
								}
							}

							// 2. Extending PoorHigh / ABPoorHigh & PoorLow / ABPoorLow Levels (Closed sessions only)
							PriceRow topRow = day.Rows[maxBin];
							bool isAbPoorHigh = topRow.Periods.Contains(0) && topRow.Periods.Contains(1);
							bool isPoorHigh = !isAbPoorHigh && topRow.Periods.Count >= 2;
							bool enablePoorHigh = !isRunningSession && ShowPoorHighLow && ShowPoorHigh && (isAbPoorHigh || isPoorHigh);

							PriceRow botRow = day.Rows[minBin];
							bool isAbPoorLow = botRow.Periods.Contains(0) && botRow.Periods.Contains(1);
							bool isPoorLow = !isAbPoorLow && botRow.Periods.Count >= 2;
							bool enablePoorLow = !isRunningSession && ShowPoorHighLow && ShowPoorLow && (isAbPoorLow || isPoorLow);

							float yPoorHigh = priceToY(day.High);
							float yPoorLow = priceToY(day.Low);

							if (enablePoorHigh && yPoorHigh >= canvasTopY && yPoorHigh <= canvasBotY)
							{
								string highLevelName = isAbPoorHigh ? "ABPoorHigh" : "PoorHigh";
								bool isHighVirgin = GetSessionLevelExtensionOnChart(
									chartControl, chartBars, barSpacing, allSessions, globalIdx, day.High,
									rawSessEndX, panelLeft, rightLimitX, refNow, breakEps, LevelBreakMode.CloseAbove, out float drawEndX);
								float drawStartX = Math.Max(panelLeft + 2f, rawSessStartX);
								if (drawEndX > drawStartX + 1.5f)
									DrawHairlineHorizontal(drawStartX, drawEndX, yPoorHigh, highExtremeBrush, 0.85f, dashedStroke);

								if (isHighVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = day.High,
										IdealY = yPoorHigh - 7.5f,
										Height = 15f,
										Text = highLevelName + " (" + day.SessionName + " " + FormatMpPrice(day.High) + ")",
										Brush = highExtremeBrush,
										BorderBrush = highExtremeBorderBrush,
										Priority = 4
									});
								}
							}

							if (enablePoorLow && yPoorLow >= canvasTopY && yPoorLow <= canvasBotY)
							{
								string lowLevelName = isAbPoorLow ? "ABPoorLow" : "PoorLow";
								bool isLowVirgin = GetSessionLevelExtensionOnChart(
									chartControl, chartBars, barSpacing, allSessions, globalIdx, day.Low,
									rawSessEndX, panelLeft, rightLimitX, refNow, breakEps, LevelBreakMode.CloseBelow, out float drawEndX);
								float drawStartX = Math.Max(panelLeft + 2f, rawSessStartX);
								if (drawEndX > drawStartX + 1.5f)
									DrawHairlineHorizontal(drawStartX, drawEndX, yPoorLow, lowExtremeBrush, 0.85f, dashedStroke);

								if (isLowVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = day.Low,
										IdealY = yPoorLow - 7.5f,
										Height = 15f,
										Text = lowLevelName + " (" + day.SessionName + " " + FormatMpPrice(day.Low) + ")",
										Brush = lowExtremeBrush,
										BorderBrush = lowExtremeBorderBrush,
										Priority = 4
									});
								}
							}

							// 3. Extending ValueAreaHigh (VAH) & ValueAreaLow (VAL) Virgin Lines
							if (allowVah && yVahLine >= canvasTopY && yVahLine <= canvasBotY)
							{
								bool isVahVirgin = GetSessionLevelExtensionOnChart(
									chartControl, chartBars, barSpacing, allSessions, globalIdx, vahPrice,
									rawSessEndX, panelLeft, rightLimitX, refNow, breakEps, LevelBreakMode.CrossClose, out float vahDrawEndX);
								float vahStartX = Math.Max(panelLeft + 2f, ShowValueAreaZoneFill ? rawSessEndX : rawSessStartX);
								if (vahDrawEndX > vahStartX + 1f)
								{
									RenderTarget.DrawLine(new Vector2(vahStartX, yVahLine), new Vector2(vahDrawEndX, yVahLine), vaVirginBrush, 1.15f, dashedStroke);
								}
								if (isVahVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = vahPrice,
										IdealY = yVahLine - 7.5f,
										Height = 15f,
										Text = "ValueAreaHigh (" + day.SessionName + " " + FormatMpPrice(vahPrice) + ")",
										Brush = vaVirginBrush,
										Priority = 2
									});
								}
							}

							if (allowVal && yValLine >= canvasTopY && yValLine <= canvasBotY)
							{
								bool isValVirgin = GetSessionLevelExtensionOnChart(
									chartControl, chartBars, barSpacing, allSessions, globalIdx, valPrice,
									rawSessEndX, panelLeft, rightLimitX, refNow, breakEps, LevelBreakMode.CrossClose, out float valDrawEndX);
								float valStartX = Math.Max(panelLeft + 2f, ShowValueAreaZoneFill ? rawSessEndX : rawSessStartX);
								if (valDrawEndX > valStartX + 1f)
								{
									RenderTarget.DrawLine(new Vector2(valStartX, yValLine), new Vector2(valDrawEndX, yValLine), vaVirginBrush, 1.15f, dashedStroke);
								}
								if (isValVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = valPrice,
										IdealY = yValLine - 7.5f,
										Height = 15f,
										Text = "ValueAreaLow (" + day.SessionName + " " + FormatMpPrice(valPrice) + ")",
										Brush = vaVirginBrush,
										Priority = 2
									});
								}
							}

							// 4. Extending VPoC (Volume PoC) & VPPoC (TPO PoC) Lines
							double vpocPrice = RoundToInstrumentTick(day.VolumePocBin * dStep);
							double tpoPocPrice = RoundToInstrumentTick(day.PocBin * dStep);
							float yVpoc = priceToY(vpocPrice);
							float yTpoPoc = priceToY(tpoPocPrice);
							bool allowVpoc = ShowVirginVPoC && (!isRunningSession || (EnableRunningSessionLevels && ShowRunningSessionPOC));
							bool allowVppoc = ShowVirginVPPoC && (!isRunningSession || (EnableRunningSessionLevels && ShowRunningSessionPPOC));

							if (allowVpoc && yVpoc >= canvasTopY && yVpoc <= canvasBotY)
							{
								bool isVpocVirgin = GetSessionLevelExtensionOnChart(
									chartControl, chartBars, barSpacing, allSessions, globalIdx, vpocPrice,
									rawSessEndX, panelLeft, rightLimitX, refNow, breakEps, LevelBreakMode.CrossClose, out float vpocDrawEndX);
								float vpocStartX = Math.Max(panelLeft + 2f, rawSessStartX);
								if (vpocDrawEndX > vpocStartX + 1f)
								{
									RenderTarget.DrawLine(new Vector2(vpocStartX, yVpoc), new Vector2(vpocDrawEndX, yVpoc), pocLineBrush, 1.45f);
								}
								if (isVpocVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = vpocPrice,
										IdealY = yVpoc - 7.5f,
										Height = 15f,
										Text = "VPoC " + FormatMpPrice(vpocPrice) + " (" + day.SessionName + ")",
										Brush = orangeBrush,
										Priority = 3
									});
								}
							}

							if (allowVppoc && (day.PocBin != day.VolumePocBin || !allowVpoc) && yTpoPoc >= canvasTopY && yTpoPoc <= canvasBotY)
							{
								bool isVppocVirgin = GetSessionLevelExtensionOnChart(
									chartControl, chartBars, barSpacing, allSessions, globalIdx, tpoPocPrice,
									rawSessEndX, panelLeft, rightLimitX, refNow, breakEps, LevelBreakMode.CrossClose, out float tpoDrawEndX);
								float vppocStartX = Math.Max(panelLeft + 2f, rawSessStartX);
								if (tpoDrawEndX > vppocStartX + 1f)
								{
									RenderTarget.DrawLine(new Vector2(vppocStartX, yTpoPoc), new Vector2(tpoDrawEndX, yTpoPoc), vppocLineBrush, 1.15f, dashedStroke);
								}
								if (isVppocVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = tpoPocPrice,
										IdealY = yTpoPoc - 7.5f,
										Height = 15f,
										Text = "VPPoC " + FormatMpPrice(tpoPocPrice) + " (" + day.SessionName + ")",
										Brush = yellowBrush,
										Priority = 2
									});
								}
							}

							// 5. Session MidLine & Extending Virgin MidLine (Closed sessions only)
							double midPrice = RoundToInstrumentTick((day.High + day.Low) * 0.5);
							float yMid = priceToY(midPrice);
							if (ShowVirginMidLine && !isRunningSession && yMid >= canvasTopY && yMid <= canvasBotY)
							{
								bool isMidVirgin = GetSessionLevelExtensionOnChart(
									chartControl, chartBars, barSpacing, allSessions, globalIdx, midPrice,
									rawSessEndX, panelLeft, rightLimitX, refNow, breakEps, LevelBreakMode.CrossClose, out float midDrawEndX);
								float midStartX = Math.Max(panelLeft + 2f, rawSessStartX);
								if (midDrawEndX > midStartX + 1f)
								{
									RenderTarget.DrawLine(new Vector2(midStartX, yMid), new Vector2(midDrawEndX, yMid), midLineBrush, 1.15f, dashedStroke);
								}
								if (isMidVirgin)
								{
									rightEdgeTags.Add(new RightEdgeTag
									{
										Price = midPrice,
										IdealY = yMid - 7.5f,
										Height = 15f,
										Text = "MidLine (" + day.SessionName + " " + FormatMpPrice(midPrice) + ")",
										Brush = midLineBrush,
										Priority = 2
									});
								}
							}
						}
						// 1D. Multi-Timeframe S/R Zones (Minor TF, Major TF×TF, Day / Week / Month High & Low)
						if (runtimeShowSrZones && srSnapshot != null)
						{
							RenderMultiTimeframeSrZonesOnCanvas(
								chartControl,
								chartBars,
								srSnapshot,
								priceToY,
								barSpacing,
								panelLeft,
								canvasTopY,
								rightLimitX,
								canvasBotY,
								light,
								rightEdgeTags);
						}
					}
					finally
					{
						RenderTarget.PopAxisAlignedClip();
					}

					// Right-Side Bar Values Column (De-overlapped Virgin Level & Zone Tags)
					if (ShowRightSideValues)
					{
						RenderTarget.DrawLine(new Vector2(rightLimitX, canvasTopY - 2f), new Vector2(rightLimitX, chartBottomY - 1f), rightLaneLineBrush, 1.0f);
						RenderDeoverlappedRightEdgeTags(
							rightEdgeTags,
							step,
							panelRight,
							canvasTopY + 2f,
							chartBottomY - 4f,
							pillBgBrush,
							cardBorderBrush,
							labelRightFormat);
					}
				}
				}
				finally
				{
					for (int sIdx = 0; sIdx < sessionLineBrushes.Length; sIdx++)
					{
						if (sessionLineBrushes[sIdx] != null) sessionLineBrushes[sIdx].Dispose();
						if (sessionGlowBrushes[sIdx] != null) sessionGlowBrushes[sIdx].Dispose();
					}
				}
			}
			finally
			{
				if (enteredProfileLock)
					System.Threading.Monitor.Exit(profileLock);
			}
		}

		private void GetLiveTimeframeCountdownInfo(out string tfBadge, out string timerStr, out bool isUrgent, out bool isLiveActive)
		{
			tfBadge = "TF";
			timerStr = "00:00";
			isUrgent = false;
			isLiveActive = false;

			Bars bars = (ChartBars != null) ? ChartBars.Bars : null;
			BarsPeriod bp = (bars != null && bars.BarsPeriod != null) ? bars.BarsPeriod : BarsPeriod;
			if (bp == null)
				return;

			BarsPeriodType pType = bp.BarsPeriodType;
			int pVal = Math.Max(1, bp.Value);
			if (pType == BarsPeriodType.HeikenAshi || pType == BarsPeriodType.Volumetric)
			{
				pType = bp.BaseBarsPeriodType;
				pVal = Math.Max(1, bp.BaseBarsPeriodValue);
			}

			switch (pType)
			{
				case BarsPeriodType.Minute:
					tfBadge = (pVal >= 60 && pVal % 60 == 0)
						? ((pVal / 60).ToString(CultureInfo.InvariantCulture) + "h")
						: (pVal.ToString(CultureInfo.InvariantCulture) + "m");
					break;
				case BarsPeriodType.Second:
					tfBadge = pVal.ToString(CultureInfo.InvariantCulture) + "s";
					break;
				case BarsPeriodType.Day:
					tfBadge = pVal.ToString(CultureInfo.InvariantCulture) + "D";
					break;
				case BarsPeriodType.Week:
					tfBadge = pVal.ToString(CultureInfo.InvariantCulture) + "W";
					break;
				case BarsPeriodType.Month:
					tfBadge = pVal.ToString(CultureInfo.InvariantCulture) + "M";
					break;
				case BarsPeriodType.Tick:
					tfBadge = pVal.ToString(CultureInfo.InvariantCulture) + "T";
					break;
				case BarsPeriodType.Volume:
					tfBadge = FormatGoChartingVol(pVal) + "V";
					break;
				case BarsPeriodType.Range:
					tfBadge = pVal.ToString(CultureInfo.InvariantCulture) + "R";
					break;
				default:
					tfBadge = pVal.ToString(CultureInfo.InvariantCulture);
					break;
			}

			if (bars == null || bars.Count <= 0)
				return;

			DateTime nowNt;
			bool isPlaybackConnected = false;
			try
			{
				if (Connection.PlaybackConnection != null && Connection.PlaybackConnection.Status == ConnectionStatus.Connected)
				{
					isPlaybackConnected = true;
					nowNt = Connection.PlaybackConnection.Now;
				}
				else
				{
					nowNt = Globals.Now;
				}
			}
			catch
			{
				nowNt = DateTime.Now;
			}

			bool streamLive = (State == State.Realtime || isPlaybackConnected);
			int lastIdx = bars.Count - 1;

			if (pType == BarsPeriodType.Minute || pType == BarsPeriodType.Second)
			{
				long periodSec = (pType == BarsPeriodType.Minute) ? (long)pVal * 60L : (long)pVal;
				DateTime barClose = bars.GetTime(lastIdx);
				double rawDiffSec = (barClose - nowNt).TotalSeconds;
				long remSec = 0L;

				if (rawDiffSec >= 0.0 && rawDiffSec <= periodSec + 5.0)
				{
					remSec = Math.Min(periodSec, Math.Max(0L, (long)Math.Ceiling(rawDiffSec)));
					isLiveActive = true;
				}
				else if (rawDiffSec < 0.0)
				{
					double elapsedPast = -rawDiffSec;
					if (streamLive && elapsedPast < Math.Max(periodSec * 3.0, 300.0))
					{
						long intervals = ((long)Math.Floor(elapsedPast) / periodSec) + 1L;
						DateTime nextClose = barClose.AddSeconds(intervals * periodSec);
						remSec = Math.Min(periodSec, Math.Max(0L, (long)Math.Ceiling((nextClose - nowNt).TotalSeconds)));
						isLiveActive = true;
					}
					else
					{
						remSec = 0L;
						isLiveActive = false;
					}
				}
				else if (streamLive)
				{
					long todSec = (long)Math.Max(0.0, Math.Floor(nowNt.TimeOfDay.TotalSeconds));
					remSec = periodSec - (todSec % periodSec);
					isLiveActive = true;
				}

				long urgentThresh = Math.Min(10L, Math.Max(3L, periodSec / 3L));
				isUrgent = isLiveActive && remSec > 0L && remSec <= urgentThresh;

				if (periodSec >= 3600L || remSec >= 3600L)
				{
					timerStr = string.Format(
						CultureInfo.InvariantCulture,
						"{0:00}:{1:00}:{2:00}",
						remSec / 3600L,
						(remSec % 3600L) / 60L,
						remSec % 60L);
				}
				else
				{
					timerStr = string.Format(
						CultureInfo.InvariantCulture,
						"{0:00}:{1:00}",
						remSec / 60L,
						remSec % 60L);
				}
				return;
			}

			if (pType == BarsPeriodType.Day)
			{
				DateTime barDate = bars.GetTime(lastIdx).Date;
				DateTime dayClose = GetTradingDayCloseTime(barDate);
				if (dayClose <= nowNt)
					dayClose = nowNt.Date.AddDays(1);
				long remSec = streamLive ? Math.Max(0L, (long)Math.Ceiling((dayClose - nowNt).TotalSeconds)) : 0L;
				isLiveActive = streamLive && remSec > 0L;
				timerStr = string.Format(
					CultureInfo.InvariantCulture,
					"{0:00}:{1:00}:{2:00}",
					remSec / 3600L,
					(remSec % 3600L) / 60L,
					remSec % 60L);
				return;
			}

			if (pType == BarsPeriodType.Week || pType == BarsPeriodType.Month)
			{
				DateTime targetEnd = (pType == BarsPeriodType.Week)
					? GetTradingDayCloseTime(GetMondayOfWeek(nowNt).AddDays(4))
					: new DateTime(nowNt.Year, nowNt.Month, DateTime.DaysInMonth(nowNt.Year, nowNt.Month), 23, 59, 59);
				double hrs = streamLive ? Math.Max(0.0, (targetEnd - nowNt).TotalHours) : 0.0;
				isLiveActive = streamLive && hrs > 0.0;
				int d = (int)(hrs / 24.0);
				int h = (int)Math.Floor(hrs % 24.0);
				timerStr = string.Format(CultureInfo.InvariantCulture, "{0}d {1:00}h", d, h);
				return;
			}

			if (pType == BarsPeriodType.Volume)
			{
				long curVol = bars.GetVolume(lastIdx);
				long remVol = Math.Max(0L, (long)pVal - curVol);
				isLiveActive = streamLive;
				isUrgent = isLiveActive && remVol > 0L && remVol <= Math.Max(1L, pVal / 10);
				timerStr = FormatGoChartingVol(remVol) + " vol";
				return;
			}

			if (pType == BarsPeriodType.Tick)
			{
				double pct = 0.0;
				try { pct = bars.PercentComplete; } catch { }
				int remTicks = Math.Max(0, (int)Math.Round(pVal * (1.0 - Math.Max(0.0, Math.Min(1.0, pct)))));
				isLiveActive = streamLive;
				isUrgent = isLiveActive && remTicks > 0 && remTicks <= Math.Max(1, pVal / 10);
				timerStr = remTicks.ToString(CultureInfo.InvariantCulture) + " tks";
				return;
			}

			if (pType == BarsPeriodType.Range)
			{
				double tick = TickSize > 0 ? TickSize : 0.05;
				int curSpan = (int)Math.Round(Math.Max(0.0, bars.GetHigh(lastIdx) - bars.GetLow(lastIdx)) / tick);
				int remRng = Math.Max(0, pVal - curSpan);
				isLiveActive = streamLive;
				isUrgent = isLiveActive && remRng > 0 && remRng <= Math.Max(1, pVal / 6);
				timerStr = remRng.ToString(CultureInfo.InvariantCulture) + " tks";
				return;
			}
		}

		private void RenderBottomRightTimeframeCountdownBox(RectangleF boxRect, bool light)
		{
			if (RenderTarget == null || boxRect.Width < 36f || boxRect.Height < 14f)
				return;

			GetLiveTimeframeCountdownInfo(out string tfBadge, out string timerStr, out bool isUrgent, out bool isLiveActive);

			SharpDX.Color bgCol;
			SharpDX.Color borderCol;
			SharpDX.Color chipFillCol;
			SharpDX.Color chipBorderCol;
			SharpDX.Color chipTextCol;
			SharpDX.Color timerTextCol;
			SharpDX.Color iconCol;

			if (isUrgent)
			{
				bgCol = light ? new SharpDX.Color(255, 243, 212, 254) : new SharpDX.Color(48, 28, 8, 254);
				borderCol = light ? new SharpDX.Color(217, 119, 6, 245) : new SharpDX.Color(251, 191, 36, 245);
				chipFillCol = light ? new SharpDX.Color(245, 158, 11, 46) : new SharpDX.Color(251, 191, 36, 48);
				chipBorderCol = light ? new SharpDX.Color(217, 119, 6, 185) : new SharpDX.Color(251, 191, 36, 185);
				chipTextCol = light ? new SharpDX.Color(146, 64, 14, 255) : new SharpDX.Color(253, 230, 138, 255);
				timerTextCol = light ? new SharpDX.Color(154, 52, 18, 255) : new SharpDX.Color(254, 240, 138, 255);
				iconCol = light ? new SharpDX.Color(217, 119, 6, 255) : new SharpDX.Color(251, 191, 36, 255);
			}
			else if (isLiveActive)
			{
				bgCol = light ? new SharpDX.Color(230, 242, 252, 254) : new SharpDX.Color(10, 22, 35, 254);
				borderCol = light ? new SharpDX.Color(13, 148, 136, 225) : new SharpDX.Color(20, 184, 166, 220);
				chipFillCol = light ? new SharpDX.Color(2, 132, 199, 34) : new SharpDX.Color(16, 204, 139, 44);
				chipBorderCol = light ? new SharpDX.Color(2, 132, 199, 150) : new SharpDX.Color(16, 204, 139, 165);
				chipTextCol = light ? new SharpDX.Color(3, 105, 161, 255) : new SharpDX.Color(52, 211, 153, 255);
				timerTextCol = light ? new SharpDX.Color(9, 17, 30, 255) : new SharpDX.Color(255, 255, 255, 255);
				iconCol = light ? new SharpDX.Color(13, 148, 136, 255) : new SharpDX.Color(34, 211, 238, 255);
			}
			else
			{
				bgCol = light ? new SharpDX.Color(235, 241, 248, 252) : new SharpDX.Color(12, 20, 30, 252);
				borderCol = light ? new SharpDX.Color(156, 172, 192, 225) : new SharpDX.Color(56, 76, 98, 230);
				chipFillCol = light ? new SharpDX.Color(100, 116, 139, 32) : new SharpDX.Color(56, 189, 248, 36);
				chipBorderCol = light ? new SharpDX.Color(100, 116, 139, 130) : new SharpDX.Color(56, 189, 248, 135);
				chipTextCol = light ? new SharpDX.Color(30, 64, 115, 255) : new SharpDX.Color(125, 211, 252, 255);
				timerTextCol = light ? new SharpDX.Color(30, 41, 59, 255) : new SharpDX.Color(226, 232, 240, 255);
				iconCol = light ? new SharpDX.Color(71, 85, 105, 245) : new SharpDX.Color(125, 211, 252, 245);
			}

			var dw = Globals.DirectWriteFactory;
			using (var bgBrush = new DxBrush(RenderTarget, bgCol))
			using (var borderBrush = new DxBrush(RenderTarget, borderCol))
			using (var chipFillBrush = new DxBrush(RenderTarget, chipFillCol))
			using (var chipBorderBrush = new DxBrush(RenderTarget, chipBorderCol))
			using (var chipTextBrush = new DxBrush(RenderTarget, chipTextCol))
			using (var timerTextBrush = new DxBrush(RenderTarget, timerTextCol))
			using (var iconBrush = new DxBrush(RenderTarget, iconCol))
			using (var tfChipFmt = new DxTextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.ExtraBold, SharpDX.DirectWrite.FontStyle.Normal, 10.6f))
			using (var timerFmt = new DxTextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.ExtraBold, SharpDX.DirectWrite.FontStyle.Normal, 12.2f))
			{
				tfChipFmt.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfChipFmt.ParagraphAlignment = ParagraphAlignment.Center;
				tfChipFmt.WordWrapping = WordWrapping.NoWrap;

				timerFmt.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
				timerFmt.ParagraphAlignment = ParagraphAlignment.Center;
				timerFmt.WordWrapping = WordWrapping.NoWrap;

				var boxRr = new RoundedRectangle { Rect = boxRect, RadiusX = 4.5f, RadiusY = 4.5f };
				RenderTarget.FillRoundedRectangle(boxRr, bgBrush);
				RenderTarget.DrawRoundedRectangle(boxRr, borderBrush, isUrgent ? 1.4f : 1.15f);

				float chipTextW;
				float timerTextW;
				using (var tlChip = new SharpDX.DirectWrite.TextLayout(dw, tfBadge, tfChipFmt, 140f, boxRect.Height))
				using (var tlTimer = new SharpDX.DirectWrite.TextLayout(dw, timerStr, timerFmt, 200f, boxRect.Height))
				{
					chipTextW = (float)Math.Ceiling(tlChip.Metrics.WidthIncludingTrailingWhitespace);
					timerTextW = (float)Math.Ceiling(tlTimer.Metrics.WidthIncludingTrailingWhitespace);
				}

				float chipPadX = 5.5f;
				float chipW = Math.Max(24f, chipTextW + chipPadX * 2f);
				float chipH = Math.Min(boxRect.Height - 5f, 16f);
				bool showIcon = (boxRect.Width >= chipW + timerTextW + 30f);
				float iconSpan = showIcon ? 12f : 0f;
				float gap1 = showIcon ? 5.5f : 0f;
				float gap2 = 6.5f;

				float totalContentW = iconSpan + gap1 + chipW + gap2 + timerTextW;
				float curX = (float)Math.Round(boxRect.X + Math.Max(4f, (boxRect.Width - totalContentW) * 0.5f));
				float midY = boxRect.Y + boxRect.Height * 0.5f;

				RenderTarget.PushAxisAlignedClip(boxRect, AntialiasMode.PerPrimitive);
				try
				{
					if (showIcon)
					{
						float icx = curX + 5.5f;
						float icy = midY + 0.4f;
						float ir = 5.1f;
						RenderTarget.DrawEllipse(new Ellipse(new Vector2(icx, icy), ir, ir), iconBrush, 1.55f);
						// Stopwatch top crown tick
						RenderTarget.DrawLine(new Vector2(icx - 1.8f, icy - ir - 1.5f), new Vector2(icx + 1.8f, icy - ir - 1.5f), iconBrush, 1.45f);
						// Hands
						RenderTarget.DrawLine(new Vector2(icx, icy), new Vector2(icx, icy - 3.0f), iconBrush, 1.45f);
						RenderTarget.DrawLine(new Vector2(icx, icy), new Vector2(icx + 2.4f, icy + 1.4f), iconBrush, 1.45f);
						curX += iconSpan + gap1;
					}

					var chipRect = new RectangleF(curX, (float)Math.Round(midY - chipH * 0.5f), chipW, chipH);
					var chipRr = new RoundedRectangle { Rect = chipRect, RadiusX = 3.2f, RadiusY = 3.2f };
					RenderTarget.FillRoundedRectangle(chipRr, chipFillBrush);
					RenderTarget.DrawRoundedRectangle(chipRr, chipBorderBrush, 0.95f);
					RenderTarget.DrawText(tfBadge, tfChipFmt, chipRect, chipTextBrush);

					curX += chipW + gap2;
					var timerRect = new RectangleF(curX, boxRect.Y - 0.5f, Math.Max(24f, timerTextW + 6f), boxRect.Height + 0.5f);
					RenderTarget.DrawText(timerStr, timerFmt, timerRect, timerTextBrush);
				}
				finally
				{
					RenderTarget.PopAxisAlignedClip();
				}
			}
		}

		private void RenderSessionHeaderAndDateFooter(
			ChartControl chartControl,
			float barSpacing,
			float panelLeft,
			float panelRight,
			float rightLimitX,
			float topSessionBarY,
			float topSessionBarH,
			float stripTopY,
			float dateFooterY,
			float dateFooterH,
			bool light)
		{
			if (ChartBars == null || ChartBars.Bars == null || ChartBars.Bars.Count == 0)
				return;

			Bars chartBars = ChartBars.Bars;

			lock (profileLock)
			{
				if (profiles.Count == 0)
					return;

				List<DayProfile> rawSessions = profiles.Values.Where(p => p.Rows.Count > 0).ToList();
				List<DayProfile> allSessions = BuildDisplaySessions(rawSessions);
				if (allSessions.Count == 0)
					return;

				DateTime refNow = GetReferenceNow(rawSessions);
				ComputeAlignedSessionAndDateSpans(chartControl, chartBars, allSessions, refNow, barSpacing, panelLeft, rightLimitX, out List<DateSpanGroup> dateGroups);
				if (dateGroups.Count == 0)
					return;

				var dashProps = new StrokeStyleProperties
				{
					DashStyle = SharpDX.Direct2D1.DashStyle.Custom,
					DashCap = CapStyle.Flat,
					StartCap = CapStyle.Flat,
					EndCap = CapStyle.Flat
				};

				DxBrush[] sessionBrushes = new DxBrush[SessionColors.Length];
				DxBrush[] sessionTintBrushes = new DxBrush[SessionColors.Length];
				DxBrush[] sessionLineBrushes = new DxBrush[SessionColors.Length];
				try
				{
					for (int i = 0; i < SessionColors.Length; i++)
					{
						SharpDX.Color sc = SessionColors[i];
						sessionBrushes[i] = new DxBrush(RenderTarget, light
							? new SharpDX.Color((int)(sc.R * 0.62f), (int)(sc.G * 0.56f), (int)(sc.B * 0.62f), 255)
							: sc);
						sessionTintBrushes[i] = new DxBrush(RenderTarget, light
							? new SharpDX.Color(sc.R, sc.G, sc.B, (byte)32)
							: new SharpDX.Color(sc.R, sc.G, sc.B, (byte)26));
						sessionLineBrushes[i] = new DxBrush(RenderTarget, light
							? new SharpDX.Color((int)(sc.R * 0.72f), (int)(sc.G * 0.68f), (int)(sc.B * 0.72f), 225)
							: new SharpDX.Color(sc.R, sc.G, sc.B, (byte)215));
					}

					using (var sessionDashStroke = new StrokeStyle(Globals.D2DFactory, dashProps, new float[] { 6f, 3f }))
					using (var dateBannerBgBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(235, 241, 248, 252) : new SharpDX.Color(8, 14, 25, 252)))
					using (var pillBgBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(246, 250, 254, 252) : new SharpDX.Color(13, 21, 36, 248)))
					using (var hdrCardFillBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(230, 240, 252, 254) : new SharpDX.Color(12, 24, 44, 252)))
					using (var hdrAccentBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(12, 124, 168, 225) : new SharpDX.Color(56, 189, 248, 225)))
					using (var cardBorderBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(168, 182, 200, 225) : new SharpDX.Color(56, 189, 248, 88)))
					using (var daySepBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(96, 120, 148, 235) : new SharpDX.Color(56, 189, 248, 130)))
					using (var whiteBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(20, 30, 44) : new SharpDX.Color(248, 250, 252)))
					using (var mutedBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(75, 92, 114) : new SharpDX.Color(156, 174, 194)))
					using (var cyanBrush = new DxBrush(RenderTarget, light ? new SharpDX.Color(12, 124, 168) : new SharpDX.Color(56, 189, 248)))
					using (var headerFormat = new DxTextFormat(Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10.4f))
					using (var dateAxisFormat = new DxTextFormat(Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10.8f))
					using (var btnCenterFormat = new DxTextFormat(Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.ExtraBold, SharpDX.DirectWrite.FontStyle.Normal, 10.2f))
					{
						headerFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
						headerFormat.ParagraphAlignment = ParagraphAlignment.Center;
						headerFormat.WordWrapping = WordWrapping.NoWrap;

						dateAxisFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
						dateAxisFormat.ParagraphAlignment = ParagraphAlignment.Center;
						dateAxisFormat.WordWrapping = WordWrapping.NoWrap;

						btnCenterFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
						btnCenterFormat.ParagraphAlignment = ParagraphAlignment.Center;
						btnCenterFormat.WordWrapping = WordWrapping.NoWrap;

						float stripLeft = panelLeft + 2f;
						float mainStripW = Math.Max(40f, rightLimitX - stripLeft);

						// =========================================================================
						// 1. FIXED TOP SESSION HEADER BAR (Market Profile Studio Pill Style)
						// =========================================================================
						RectangleF topStripRect = new RectangleF(stripLeft, topSessionBarY, mainStripW, topSessionBarH);
						RenderTarget.FillRectangle(topStripRect, dateBannerBgBrush);
						RenderTarget.DrawRectangle(topStripRect, cardBorderBrush, 1.0f);

						if (ShowRightSideValues && panelRight - rightLimitX > 30f)
						{
							float rcX = rightLimitX + 2f;
							float rcW = Math.Max(20f, panelRight - rightLimitX - 4f);
							RectangleF rightCapRect = new RectangleF(rcX, topSessionBarY, rcW, topSessionBarH);
							var rightCapRr = new RoundedRectangle { Rect = rightCapRect, RadiusX = 5.0f, RadiusY = 5.0f };
							RenderTarget.FillRoundedRectangle(rightCapRr, hdrCardFillBrush);
							RenderTarget.DrawRoundedRectangle(rightCapRr, hdrAccentBrush, 1.15f);
							RenderTarget.FillRectangle(new RectangleF(rcX + 10f, topSessionBarY + topSessionBarH - 2.4f, Math.Max(20f, rcW - 20f), 1.6f), hdrAccentBrush);
							string capLabel = (runtimeShowMpZones && runtimeShowSrZones)
								? "MP & S/R VALUES"
								: (runtimeShowSrZones ? "S/R ZONE VALUES" : "MP ZONE VALUES");
							DrawMpText(capLabel, rcX + 4f, topSessionBarY, rcW - 8f, topSessionBarH, btnCenterFormat, cyanBrush);
						}

						DateTime prevHdrDate = DateTime.MinValue;
						for (int globalIdx = 0; globalIdx < allSessions.Count; globalIdx++)
						{
							DayProfile day = allSessions[globalIdx];
							bool isLatestSession = (globalIdx == allSessions.Count - 1);
							bool isRunningSession = IsSessionRunning(day, isLatestSession, refNow);

							bool isNewDate = (day.SessionDate.Date != prevHdrDate);
							prevHdrDate = day.SessionDate.Date;

							if (day.HeaderRightX < stripLeft + 4f || day.HeaderLeftX > rightLimitX - 4f)
								continue;

							int sessionColorIdx = Math.Max(0, Math.Min(day.SessionId, sessionBrushes.Length - 1));
							float clLeft = Math.Max(stripLeft + 1f, day.HeaderLeftX);
							float clRight = Math.Min(rightLimitX - 1f, day.HeaderRightX);
							float cellW = clRight - clLeft;

							if (cellW >= 6f)
							{
								RectangleF sessionCellRect = new RectangleF(clLeft, topSessionBarY + 1f, cellW, topSessionBarH - 2f);
								RenderTarget.FillRectangle(sessionCellRect, sessionTintBrushes[sessionColorIdx]);
								RenderTarget.DrawLine(
									new Vector2(clLeft + 1f, topSessionBarY + topSessionBarH - 1.5f),
									new Vector2(clRight - 1f, topSessionBarY + topSessionBarH - 1.5f),
									sessionLineBrushes[sessionColorIdx],
									1.6f);
							}

							float divX = (float)Math.Round(day.HeaderLeftX) + 0.5f;
							if (divX >= stripLeft + 1f && divX <= rightLimitX - 1f)
							{
								RenderTarget.DrawLine(
									new Vector2(divX, topSessionBarY),
									new Vector2(divX, topSessionBarY + topSessionBarH + (ShowSessionDividers ? 2f : 0f)),
									isNewDate ? daySepBrush : sessionLineBrushes[sessionColorIdx],
									isNewDate ? 1.8f : 1.4f);

								// Extend Session Base Line seamlessly through the 3-Row Bottom Table down to Date Footer
								if (ShowSessionDividers && dateFooterY > stripTopY + 2f)
								{
									RenderTarget.DrawLine(new Vector2(divX, stripTopY - 3f), new Vector2(divX, dateFooterY), sessionLineBrushes[sessionColorIdx], 1.5f, sessionDashStroke);
								}
							}

							if (cellW < 28f)
								continue;

							float availTextW = Math.Max(16f, cellW - 16f);
							string dateShort = day.SessionDate.ToString("dd MMM", CultureInfo.InvariantCulture);
							string shortName = day.SessionName == "New York" ? "NY" : (day.SessionName == "London" ? "Lon" : day.SessionName);

							GetSessionHeaderOrderflowSpec(day, out int sessTpl, out double sessImbPct);
							string imbStr = sessImbPct.ToString("0.#", CultureInfo.InvariantCulture) + "%";
							string srStatsFull = string.Format(CultureInfo.InvariantCulture, "Imb: {0} · Tick/Lvl: {1}", imbStr, sessTpl);
							string srStatsShort = string.Format(CultureInfo.InvariantCulture, "{0} · {1}T", imbStr, sessTpl);

							string sessionHeader;
							if (!runtimeShowMpZones)
							{
								// S/R Mode: Show Session Name + Date + Imbalance % & Tick Per Level (no TPO details)
								if (EffectiveProfileMode != MarketProfilePeriodMode.Daily)
								{
									sessionHeader = availTextW >= 180f
										? (day.SessionName + " · " + srStatsFull)
										: day.SessionName;
								}
								else if (availTextW >= 215f)
								{
									sessionHeader = isRunningSession
										? string.Format(CultureInfo.InvariantCulture, "{0} · {1}  ● LIVE  {2}", day.SessionName, dateShort, srStatsFull)
										: string.Format(CultureInfo.InvariantCulture, "{0} · {1}  {2}", day.SessionName, dateShort, srStatsFull);
								}
								else if (availTextW >= 135f)
								{
									sessionHeader = isRunningSession
										? string.Format(CultureInfo.InvariantCulture, "{0} · {1} ● LIVE · {2}", shortName, dateShort, srStatsShort)
										: string.Format(CultureInfo.InvariantCulture, "{0} · {1}  {2}", day.SessionName, dateShort, srStatsShort);
								}
								else if (availTextW >= 86f)
								{
									sessionHeader = string.Format(CultureInfo.InvariantCulture, "{0} · {1}  {2}", shortName, day.SessionDate.ToString("dd", CultureInfo.InvariantCulture), srStatsShort);
								}
								else if (availTextW >= 46f)
								{
									sessionHeader = shortName + " " + day.SessionDate.ToString("dd", CultureInfo.InvariantCulture);
								}
								else
								{
									sessionHeader = shortName.ToUpperInvariant();
								}
							}
							else
							{
								int expectedTpos = Math.Max(1, day.ExpectedPeriodCount);
								int printedTpos = Math.Min(expectedTpos, Math.Max(day.PeriodSlices.Count, day.LastPeriod + 1));
								string tpoStatus = isRunningSession
									? string.Format(CultureInfo.InvariantCulture, "● LIVE {0}/{1} TPO", printedTpos, expectedTpos)
									: string.Format(CultureInfo.InvariantCulture, "{0}/{1} TPO", printedTpos, expectedTpos);

								if (EffectiveProfileMode != MarketProfilePeriodMode.Daily)
								{
									sessionHeader = availTextW >= 150f
										? (day.SessionName + " · " + tpoStatus)
										: day.SessionName;
								}
								else if (runtimeShowSrZones && availTextW >= 275f)
								{
									sessionHeader = string.Format(CultureInfo.InvariantCulture, "{0} · {1}  {2} · {3}", day.SessionName, dateShort, srStatsFull, tpoStatus);
								}
								else if (availTextW >= 165f)
								{
									sessionHeader = string.Format(CultureInfo.InvariantCulture, "{0} · {1}  {2}", day.SessionName, dateShort, tpoStatus);
								}
								else if (availTextW >= 96f)
								{
									sessionHeader = isRunningSession
										? string.Format(CultureInfo.InvariantCulture, "{0} · {1} ● LIVE", shortName, dateShort)
										: string.Format(CultureInfo.InvariantCulture, "{0} · {1}", day.SessionName, dateShort);
								}
								else if (availTextW >= 50f)
								{
									sessionHeader = shortName + " " + day.SessionDate.ToString("dd", CultureInfo.InvariantCulture);
								}
								else
								{
									sessionHeader = shortName.ToUpperInvariant();
								}
							}

							float pillW = Math.Min(cellW - 8f, Math.Max(46f, sessionHeader.Length * 6.3f + 20f));
							float pillH = topSessionBarH - 7f;
							float pillX = clLeft + 4f;
							float pillY = topSessionBarY + 3.5f;
							RectangleF hdrPillRect = new RectangleF(pillX, pillY, pillW, pillH);
							var hdrPillRr = new RoundedRectangle { Rect = hdrPillRect, RadiusX = 4.5f, RadiusY = 4.5f };
							RenderTarget.FillRoundedRectangle(hdrPillRr, pillBgBrush);
							RenderTarget.DrawRoundedRectangle(hdrPillRr, cardBorderBrush, 0.9f);
							RenderTarget.FillRectangle(new RectangleF(pillX + 1.6f, pillY + 2.6f, 3.0f, Math.Max(2f, pillH - 5.2f)), sessionBrushes[sessionColorIdx]);
							DrawMpText(sessionHeader, pillX + 8f, pillY, Math.Max(12f, pillW - 11f), pillH, headerFormat, sessionBrushes[sessionColorIdx]);
						}

						// =========================================================================
						// 2. BOTTOM TRADING DATE FOOTER BAR (Market Profile Studio Banner Style)
						// =========================================================================
						RectangleF footerFullRect = new RectangleF(stripLeft, dateFooterY, mainStripW, dateFooterH);
						RenderTarget.FillRectangle(footerFullRect, dateBannerBgBrush);
						RenderTarget.DrawRectangle(footerFullRect, cardBorderBrush, 1.0f);

						MarketProfilePeriodMode curMode = EffectiveProfileMode;
						for (int g = 0; g < dateGroups.Count; g++)
						{
							DateSpanGroup group = dateGroups[g];
							if (group.RightX < stripLeft + 4f || group.LeftX > rightLimitX - 4f)
								continue;

							if (group.LeftX >= stripLeft + 1f && group.LeftX <= rightLimitX - 1f)
							{
								RenderTarget.DrawLine(new Vector2(group.LeftX, dateFooterY), new Vector2(group.LeftX, dateFooterY + dateFooterH), daySepBrush, 1.6f);
							}
							if (group.RightX >= stripLeft + 1f && group.RightX <= rightLimitX - 1f)
							{
								RenderTarget.DrawLine(new Vector2(group.RightX, dateFooterY), new Vector2(group.RightX, dateFooterY + dateFooterH), daySepBrush, 1.6f);
							}

							float clBannerLeft = Math.Max(stripLeft + 2f, group.LeftX + 1f);
							float clBannerRight = Math.Min(rightLimitX - 2f, group.RightX - 1f);
							float bannerW = clBannerRight - clBannerLeft;
							if (bannerW < 42f)
								continue;

							RectangleF dateBannerRect = new RectangleF(clBannerLeft, dateFooterY, bannerW, dateFooterH);
							RenderTarget.FillRectangle(dateBannerRect, dateBannerBgBrush);
							RenderTarget.DrawRectangle(dateBannerRect, cardBorderBrush, 0.9f);

							float safeTextLeft = Math.Max(clBannerLeft + 6f, panelLeft + 140f);
							float availDateW = (clBannerRight - 6f) - safeTextLeft;
							if (availDateW < 38f)
								continue;

							if (curMode == MarketProfilePeriodMode.Daily)
							{
								List<string> sessionNames = new List<string>();
								for (int si = 0; si < activeSessions.Length; si++)
								{
									sessionNames.Add(activeSessions[si].Name);
								}

								string fullSummary = sessionNames.Count > 0
									? string.Format(CultureInfo.InvariantCulture, "   [ {0} Sessions · {1} ]",
										activeSessions.Length, string.Join(" | ", sessionNames))
									: "   [ 3 Sessions · Asia | London | New York ]";
								string shortSummary = string.Format(CultureInfo.InvariantCulture, "  [ {0} Sessions ]",
									Math.Max(1, activeSessions.Length));

								string dateBannerText;
								if (availDateW >= 380f)
									dateBannerText = "Date: " + group.Date.ToString("dd MMM yyyy (ddd)", CultureInfo.InvariantCulture) + fullSummary;
								else if (availDateW >= 210f)
									dateBannerText = "Date: " + group.Date.ToString("dd MMM yyyy (ddd)", CultureInfo.InvariantCulture) + shortSummary;
								else if (availDateW >= 122f)
									dateBannerText = group.Date.ToString("dd MMM yyyy (ddd)", CultureInfo.InvariantCulture);
								else if (availDateW >= 78f)
									dateBannerText = group.Date.ToString("dd MMM (ddd)", CultureInfo.InvariantCulture);
								else
									dateBannerText = group.Date.ToString("dd MMM", CultureInfo.InvariantCulture);

								DrawMpText(dateBannerText, safeTextLeft, dateFooterY + 1f, availDateW, dateFooterH - 2f, dateAxisFormat, whiteBrush);
							}
							else if (curMode == MarketProfilePeriodMode.Monthly)
							{
								string mShort = group.Date.ToString("MMM yyyy", CultureInfo.InvariantCulture);
								string mLong = group.Date.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
								string bannerSummary = availDateW >= 240f
									? string.Format(CultureInfo.InvariantCulture, "Month: {0}  [ Monthly ]", mLong)
									: mShort;
								DrawMpText(bannerSummary, safeTextLeft, dateFooterY + 1f, availDateW, dateFooterH - 2f, dateAxisFormat, whiteBrush);
							}
							else
							{
								DayProfile firstInGrp = group.Sessions.Count > 0 ? group.Sessions[0] : null;
								string wkTitle = firstInGrp != null && !string.IsNullOrEmpty(firstInGrp.SessionName)
									? firstInGrp.SessionName
									: ("Wk " + group.Date.ToString("dd MMM", CultureInfo.InvariantCulture));
								string bannerSummary = availDateW >= 220f
									? string.Format(CultureInfo.InvariantCulture, "{0}  [ Weekly ]", wkTitle)
									: wkTitle;
								DrawMpText(bannerSummary, safeTextLeft, dateFooterY + 1f, availDateW, dateFooterH - 2f, dateAxisFormat, whiteBrush);
							}
						}
					}
				}
				finally
				{
					for (int i = 0; i < sessionBrushes.Length; i++)
					{
						if (sessionBrushes[i] != null) sessionBrushes[i].Dispose();
						if (sessionTintBrushes[i] != null) sessionTintBrushes[i].Dispose();
						if (sessionLineBrushes[i] != null) sessionLineBrushes[i].Dispose();
					}
				}
			}
		}

		#region Multi-Timeframe Support & Resistance Engine (Clean Lines, Full History Until Validated, S/R Flip & D/W/M)
		private DateTime ResolveSrTradingDate(DateTime ts)
		{
			if (!ShowIndianSession && ShowAsia)
			{
				int asiaOpenSec;
				if (!TryParseSessionTime(AsiaFromTime, out asiaOpenSec))
					asiaOpenSec = 3 * 3600 + 30 * 60;
				int curSec = ts.Hour * 3600 + ts.Minute * 60 + ts.Second;
				if (curSec < asiaOpenSec)
					return ts.Date.AddDays(-1);
			}
			return ts.Date;
		}

		private SrComputedSnapshot EnsureSrZonesComputed(Bars chartBars)
		{
			if (chartBars == null || chartBars.Count < 6)
				return null;

			double tick = TickSize > 0 ? TickSize : 0.05;
			int chartCount = chartBars.Count;
			double refHigh = chartBars.GetHigh(chartCount - 1);
			double refLow = chartBars.GetLow(chartCount - 1);
			long highBucket = (long)Math.Round(refHigh / Math.Max(1e-6, tick));
			long lowBucket = (long)Math.Round(refLow / Math.Max(1e-6, tick));

			int minSampleCount = rawMinuteSamples.Count;

			int dailyHistCount;
			int cfgHash =
				(ShowMinorSrZones ? 1 : 0) |
				(ShowMajorSrZones ? 2 : 0) |
				(ShowSrFlipLines ? 4 : 0) |
				(ShowSrDayHighLow ? 8 : 0) |
				(ShowSrWeekHighLow ? 16 : 0) |
				(ShowSrMonthHighLow ? 32 : 0) |
				(SrMinorPivotBars << 6) ^
				(SrMajorPivotBars << 12) ^
				(SrAtrLength << 18) ^
				((int)Math.Round(SrZoneAtrMultiplier * 100.0) << 23);

			DateTime nowUtc = DateTime.UtcNow;
			lock (srLock)
			{
				dailyHistCount = srDailyHistory.Count;
				if (cachedSrSnapshot != null &&
					srCacheConfigHash == cfgHash &&
					srCacheDailyCount == dailyHistCount)
				{
					bool sameChartBar = (srCacheChartBarCount == chartCount);
					bool noRangeExpansion = (highBucket <= srCacheLastHighBucket && lowBucket >= srCacheLastLowBucket);
					bool throttledIntraBar = sameChartBar && (nowUtc - lastSrRealtimeRecomputeUtc).TotalMilliseconds < 1200.0;
					if ((sameChartBar && noRangeExpansion) || throttledIntraBar)
					{
						return cachedSrSnapshot;
					}
				}
			}

			List<MinuteSample> minuteCopy = null;
			lock (profileLock)
			{
				if (rawMinuteSamples.Count > 0)
					minuteCopy = new List<MinuteSample>(rawMinuteSamples);
			}

			SortedDictionary<DateTime, DailyHorizonSample> mergedDaily = new SortedDictionary<DateTime, DailyHorizonSample>();
			lock (srLock)
			{
				foreach (var kv in srDailyHistory)
				{
					mergedDaily[kv.Key] = new DailyHorizonSample
					{
						Date = kv.Value.Date,
						High = kv.Value.High,
						HighTime = kv.Value.HighTime,
						Low = kv.Value.Low,
						LowTime = kv.Value.LowTime,
						Close = kv.Value.Close
					};
				}
			}

			if (minuteCopy != null && minuteCopy.Count > 0)
			{
				for (int i = 0; i < minuteCopy.Count; i++)
				{
					MinuteSample ms = minuteCopy[i];
					if (ms.High <= 0 || ms.Low <= 0 || ms.High < ms.Low)
						continue;
					DateTime dDate = ResolveSrTradingDate(ms.Time);
					DailyHorizonSample ds;
					if (!mergedDaily.TryGetValue(dDate, out ds))
					{
						ds = new DailyHorizonSample { Date = dDate };
						mergedDaily[dDate] = ds;
					}
					if (ms.High > ds.High)
					{
						ds.High = ms.High;
						ds.HighTime = ms.Time;
					}
					if (ms.Low < ds.Low)
					{
						ds.Low = ms.Low;
						ds.LowTime = ms.Time;
					}
					ds.Close = ms.Close;
				}
			}

			for (int i = 0; i < chartCount; i++)
			{
				DateTime bt = chartBars.GetTime(i);
				double bh = chartBars.GetHigh(i);
				double bl = chartBars.GetLow(i);
				double bc = chartBars.GetClose(i);
				if (bh <= 0 || bl <= 0 || bh < bl)
					continue;
				DateTime dDate = ResolveSrTradingDate(bt);
				DailyHorizonSample ds;
				if (!mergedDaily.TryGetValue(dDate, out ds))
				{
					ds = new DailyHorizonSample { Date = dDate };
					mergedDaily[dDate] = ds;
				}
				if (bh > ds.High)
				{
					ds.High = bh;
					ds.HighTime = bt;
				}
				if (bl < ds.Low)
				{
					ds.Low = bl;
					ds.LowTime = bt;
				}
				ds.Close = bc;
			}

			BuildMinorAndMajorTimeframeBars(
				chartBars,
				minuteCopy,
				out List<SrOhlcBar> minorBars,
				out List<SrOhlcBar> majorBars);

			SrComputedSnapshot snap = new SrComputedSnapshot();
			DateTime latestBarTime = chartBars.GetTime(chartCount - 1);
			DateTime cutoffTime = latestBarTime.AddDays(-120);
			int atrLen = Math.Max(1, SrAtrLength);
			double breakAtrMult = Math.Max(0.02, Math.Min(0.60, SrZoneAtrMultiplier));

			List<SrRawPivot> minorPivots = new List<SrRawPivot>();
			List<SrRawPivot> majorPivots = new List<SrRawPivot>();

			if (ShowMinorSrZones && minorBars.Count >= 8)
			{
				int pMin = Math.Max(2, Math.Min(SrMinorPivotBars, (minorBars.Count - 3) / 3));
				minorPivots = DetectConfirmedSwingPivots(minorBars, pMin, pMin, atrLen);
			}

			if (ShowMajorSrZones && majorBars.Count >= 8)
			{
				int pMaj = Math.Max(2, Math.Min(SrMajorPivotBars, (majorBars.Count - 3) / 3));
				majorPivots = DetectConfirmedSwingPivots(majorBars, pMaj, pMaj, atrLen);
			}

			snap.LineTracks = BuildHistoricalAndActiveSrLineTracks(
				chartBars,
				minorPivots,
				majorPivots,
				minorBars,
				cutoffTime,
				tick,
				breakAtrMult,
				ShowSrFlipLines);

			snap.HorizonLevels = BuildDayWeekMonthHorizonLevels(mergedDaily, latestBarTime);

			lock (srLock)
			{
				cachedSrSnapshot = snap;
				srCacheChartBarCount = chartCount;
				srCacheMinuteCount = minSampleCount;
				srCacheDailyCount = dailyHistCount;
				srCacheLastHighBucket = highBucket;
				srCacheLastLowBucket = lowBucket;
				lastSrRealtimeRecomputeUtc = DateTime.UtcNow;
				srCacheConfigHash = cfgHash;
			}

			return snap;
		}

		private void BuildMinorAndMajorTimeframeBars(
			Bars chartBars,
			List<MinuteSample> minuteCopy,
			out List<SrOhlcBar> minorBars,
			out List<SrOhlcBar> majorBars)
		{
			minorBars = new List<SrOhlcBar>();
			majorBars = new List<SrOhlcBar>();

			BarsPeriod bp = BarsPeriod ?? (ChartBars != null && ChartBars.Properties != null ? ChartBars.Properties.BarsPeriod : null);
			BarsPeriodType pType = bp != null ? bp.BarsPeriodType : BarsPeriodType.Minute;
			int pVal = (bp != null && bp.Value > 0) ? bp.Value : 3;

			if (pType == BarsPeriodType.Minute)
			{
				int minorMin = Math.Max(1, pVal);
				int majorMin = minorMin == 1 ? 3 : Math.Min(10080, minorMin * minorMin);
				DateTime firstChartTime = chartBars.GetTime(0);

				if (minuteCopy != null && minuteCopy.Count > 0 && minuteCopy[0].Time < firstChartTime.AddMinutes(-minorMin))
				{
					long curBucket = long.MinValue;
					SrOhlcBar cur = default(SrOhlcBar);
					bool hasCur = false;
					DateTime epoch = new DateTime(2020, 1, 1);

					for (int i = 0; i < minuteCopy.Count; i++)
					{
						MinuteSample ms = minuteCopy[i];
						if (ms.Time >= firstChartTime)
							break;
						long b = (long)Math.Floor((ms.Time - epoch).TotalMinutes / minorMin);
						if (b != curBucket)
						{
							if (hasCur) minorBars.Add(cur);
							curBucket = b;
							cur = new SrOhlcBar { Time = ms.Time, HighTime = ms.Time, LowTime = ms.Time, Open = ms.Open, High = ms.High, Low = ms.Low, Close = ms.Close };
							hasCur = true;
						}
						else
						{
							if (ms.High > cur.High) { cur.High = ms.High; cur.HighTime = ms.Time; }
							if (ms.Low < cur.Low) { cur.Low = ms.Low; cur.LowTime = ms.Time; }
							cur.Close = ms.Close;
							cur.Time = ms.Time;
						}
					}
					if (hasCur) minorBars.Add(cur);
				}

				for (int i = 0; i < chartBars.Count; i++)
				{
					DateTime bt = chartBars.GetTime(i);
					minorBars.Add(new SrOhlcBar
					{
						Time = bt,
						HighTime = bt,
						LowTime = bt,
						Open = chartBars.GetOpen(i),
						High = chartBars.GetHigh(i),
						Low = chartBars.GetLow(i),
						Close = chartBars.GetClose(i)
					});
				}

				DateTime majEpoch = new DateTime(2020, 1, 1);
				if (minuteCopy != null && minuteCopy.Count > chartBars.Count)
				{
					long curBucket = long.MinValue;
					SrOhlcBar cur = default(SrOhlcBar);
					bool hasCur = false;
					DateTime last1M = DateTime.MinValue;

					for (int i = 0; i < minuteCopy.Count; i++)
					{
						MinuteSample ms = minuteCopy[i];
						last1M = ms.Time;
						long b = (long)Math.Floor((ms.Time - majEpoch).TotalMinutes / majorMin);
						if (b != curBucket)
						{
							if (hasCur) majorBars.Add(cur);
							curBucket = b;
							cur = new SrOhlcBar { Time = ms.Time, HighTime = ms.Time, LowTime = ms.Time, Open = ms.Open, High = ms.High, Low = ms.Low, Close = ms.Close };
							hasCur = true;
						}
						else
						{
							if (ms.High > cur.High) { cur.High = ms.High; cur.HighTime = ms.Time; }
							if (ms.Low < cur.Low) { cur.Low = ms.Low; cur.LowTime = ms.Time; }
							cur.Close = ms.Close;
							cur.Time = ms.Time;
						}
					}
					if (hasCur) majorBars.Add(cur);

					for (int i = 0; i < chartBars.Count; i++)
					{
						DateTime ct = chartBars.GetTime(i);
						if (ct <= last1M) continue;
						long b = (long)Math.Floor((ct - majEpoch).TotalMinutes / majorMin);
						if (majorBars.Count > 0)
						{
							long prevB = (long)Math.Floor((majorBars[majorBars.Count - 1].Time - majEpoch).TotalMinutes / majorMin);
							if (b == prevB)
							{
								SrOhlcBar tail = majorBars[majorBars.Count - 1];
								double ch = chartBars.GetHigh(i);
								double cl = chartBars.GetLow(i);
								if (ch > tail.High) { tail.High = ch; tail.HighTime = ct; }
								if (cl < tail.Low) { tail.Low = cl; tail.LowTime = ct; }
								tail.Close = chartBars.GetClose(i);
								tail.Time = ct;
								majorBars[majorBars.Count - 1] = tail;
								continue;
							}
						}
						majorBars.Add(new SrOhlcBar
						{
							Time = ct,
							HighTime = ct,
							LowTime = ct,
							Open = chartBars.GetOpen(i),
							High = chartBars.GetHigh(i),
							Low = chartBars.GetLow(i),
							Close = chartBars.GetClose(i)
						});
					}
				}
				else
				{
					long curBucket = long.MinValue;
					SrOhlcBar cur = default(SrOhlcBar);
					bool hasCur = false;
					for (int i = 0; i < minorBars.Count; i++)
					{
						SrOhlcBar mb = minorBars[i];
						long b = (long)Math.Floor((mb.Time - majEpoch).TotalMinutes / majorMin);
						if (b != curBucket)
						{
							if (hasCur) majorBars.Add(cur);
							curBucket = b;
							cur = mb;
							hasCur = true;
						}
						else
						{
							if (mb.High > cur.High) { cur.High = mb.High; cur.HighTime = mb.HighTime; }
							if (mb.Low < cur.Low) { cur.Low = mb.Low; cur.LowTime = mb.LowTime; }
							cur.Close = mb.Close;
							cur.Time = mb.Time;
						}
					}
					if (hasCur) majorBars.Add(cur);
				}
				return;
			}

			int groupMult = pVal <= 1 ? 3 : Math.Min(16, pVal);
			for (int i = 0; i < chartBars.Count; i++)
			{
				DateTime bt = chartBars.GetTime(i);
				minorBars.Add(new SrOhlcBar
				{
					Time = bt,
					HighTime = bt,
					LowTime = bt,
					Open = chartBars.GetOpen(i),
					High = chartBars.GetHigh(i),
					Low = chartBars.GetLow(i),
					Close = chartBars.GetClose(i)
				});
			}

			for (int i = 0; i < minorBars.Count; i += groupMult)
			{
				int end = Math.Min(minorBars.Count - 1, i + groupMult - 1);
				SrOhlcBar agg = minorBars[i];
				for (int k = i + 1; k <= end; k++)
				{
					if (minorBars[k].High > agg.High) { agg.High = minorBars[k].High; agg.HighTime = minorBars[k].HighTime; }
					if (minorBars[k].Low < agg.Low) { agg.Low = minorBars[k].Low; agg.LowTime = minorBars[k].LowTime; }
					agg.Close = minorBars[k].Close;
					agg.Time = minorBars[k].Time;
				}
				majorBars.Add(agg);
			}
		}

		private static List<SrRawPivot> DetectConfirmedSwingPivots(List<SrOhlcBar> bars, int leftBars, int rightBars, int atrLen)
		{
			List<SrRawPivot> pivots = new List<SrRawPivot>();
			int n = bars.Count;
			if (n < leftBars + rightBars + 2)
				return pivots;

			double[] atr = new double[n];
			double sumTr = 0.0;
			int initCount = Math.Min(n, atrLen);
			for (int i = 0; i < n; i++)
			{
				double tr = (i == 0)
					? (bars[0].High - bars[0].Low)
					: Math.Max(
						bars[i].High - bars[i].Low,
						Math.Max(Math.Abs(bars[i].High - bars[i - 1].Close), Math.Abs(bars[i].Low - bars[i - 1].Close)));

				if (i < initCount)
				{
					sumTr += tr;
					atr[i] = sumTr / (i + 1);
				}
				else
				{
					atr[i] = ((atr[i - 1] * (atrLen - 1)) + tr) / atrLen;
				}
			}

			int maxPivotIdx = n - 2 - rightBars;
			for (int i = leftBars; i <= maxPivotIdx; i++)
			{
				double ph = bars[i].High;
				bool isPivotHigh = true;
				for (int l = i - leftBars; l < i; l++)
				{
					if (bars[l].High > ph + 1e-9) { isPivotHigh = false; break; }
				}
				if (isPivotHigh)
				{
					for (int r = i + 1; r <= i + rightBars; r++)
					{
						if (bars[r].High >= ph - 1e-9) { isPivotHigh = false; break; }
					}
				}
				if (isPivotHigh && atr[i] > 0)
				{
					DateTime exactOrigin = bars[i].HighTime > DateTime.MinValue ? bars[i].HighTime : bars[i].Time;
					pivots.Add(new SrRawPivot
					{
						IsHigh = true,
						Price = ph,
						Atr = atr[i],
						Time = exactOrigin,
						ConfirmTime = bars[i + rightBars].Time
					});
				}

				double pl = bars[i].Low;
				bool isPivotLow = true;
				for (int l = i - leftBars; l < i; l++)
				{
					if (bars[l].Low < pl - 1e-9) { isPivotLow = false; break; }
				}
				if (isPivotLow)
				{
					for (int r = i + 1; r <= i + rightBars; r++)
					{
						if (bars[r].Low <= pl + 1e-9) { isPivotLow = false; break; }
					}
				}
				if (isPivotLow && atr[i] > 0)
				{
					DateTime exactOrigin = bars[i].LowTime > DateTime.MinValue ? bars[i].LowTime : bars[i].Time;
					pivots.Add(new SrRawPivot
					{
						IsHigh = false,
						Price = pl,
						Atr = atr[i],
						Time = exactOrigin,
						ConfirmTime = bars[i + rightBars].Time
					});
				}
			}

			return pivots;
		}

		private static int FindFirstMinorBarIndexAfter(List<SrOhlcBar> minorBars, DateTime t)
		{
			int lo = 0, hi = minorBars.Count - 1, ans = minorBars.Count;
			while (lo <= hi)
			{
				int mid = (lo + hi) >> 1;
				if (minorBars[mid].Time > t)
				{
					ans = mid;
					hi = mid - 1;
				}
				else
				{
					lo = mid + 1;
				}
			}
			return ans;
		}

		private List<SrLineTrack> BuildHistoricalAndActiveSrLineTracks(
			Bars chartBars,
			List<SrRawPivot> minorPivots,
			List<SrRawPivot> majorPivots,
			List<SrOhlcBar> minorBars,
			DateTime cutoffTime,
			double tick,
			double breakAtrMult,
			bool allowMajorFlip)
		{
			List<SrLineTrack> tracks = new List<SrLineTrack>();
			if (minorBars == null || minorBars.Count < 4)
				return tracks;

			List<KeyValuePair<SrRawPivot, bool>> orderedPivots = new List<KeyValuePair<SrRawPivot, bool>>(majorPivots.Count + minorPivots.Count);
			for (int i = 0; i < majorPivots.Count; i++)
			{
				if (majorPivots[i].Time >= cutoffTime)
					orderedPivots.Add(new KeyValuePair<SrRawPivot, bool>(majorPivots[i], true));
			}
			for (int i = 0; i < minorPivots.Count; i++)
			{
				if (minorPivots[i].Time >= cutoffTime)
					orderedPivots.Add(new KeyValuePair<SrRawPivot, bool>(minorPivots[i], false));
			}

			int lastClosedBarIdx = Math.Max(0, minorBars.Count - 2);
			DateTime firstChartTime = (chartBars != null && chartBars.Count > 0) ? chartBars.GetTime(0) : DateTime.MaxValue;

			for (int p = 0; p < orderedPivots.Count; p++)
			{
				SrRawPivot pv = orderedPivots[p].Key;
				bool isMaj = orderedPivots[p].Value;
				double levelPrice = RoundToInstrumentTick(pv.Price);
				double dedupTol = Math.Max(tick * 3.0, pv.Atr * 0.12);
				DateTime refinedOrigin = pv.Time;

				// Snap Major pivot OriginTime & Price pixel-perfectly onto the exact visible ChartBars swing wick
				if (isMaj && chartBars != null && chartBars.Count > 0 && refinedOrigin >= firstChartTime)
				{
					int lo = 0, hi = chartBars.Count - 1, baseIdx = chartBars.Count - 1;
					while (lo <= hi)
					{
						int mid = (lo + hi) >> 1;
						if (chartBars.GetTime(mid) >= refinedOrigin)
						{
							baseIdx = mid;
							hi = mid - 1;
						}
						else
						{
							lo = mid + 1;
						}
					}
					int sIdx = Math.Max(0, baseIdx - 2);
					int eIdx = Math.Min(chartBars.Count - 1, baseIdx + 2);
					int bestBar = baseIdx;
					double bestDiff = double.MaxValue;
					for (int k = sIdx; k <= eIdx; k++)
					{
						double ext = pv.IsHigh ? chartBars.GetHigh(k) : chartBars.GetLow(k);
						double diff = Math.Abs(ext - pv.Price);
						if (diff < bestDiff)
						{
							bestDiff = diff;
							bestBar = k;
						}
					}
					if (bestDiff <= Math.Max(tick * 4.0, pv.Atr * 0.15))
					{
						refinedOrigin = chartBars.GetTime(bestBar);
						levelPrice = RoundToInstrumentTick(pv.IsHigh ? chartBars.GetHigh(bestBar) : chartBars.GetLow(bestBar));
					}
				}

				// Skip Minor duplicate if a Major track already anchors the same swing extreme & lifespan
				bool mergedIntoExistingMajor = false;
				for (int e = 0; e < tracks.Count; e++)
				{
					SrLineTrack ex = tracks[e];
					if (ex.InitialIsResistance != pv.IsHigh)
						continue;
					if (Math.Abs(ex.Price - levelPrice) > dedupTol)
						continue;

					DateTime exEnd = ex.HasFlipped
						? (ex.IsFlipBroken ? ex.ValidationEndTime : DateTime.MaxValue)
						: DateTime.MaxValue;
					if (Math.Abs((refinedOrigin - ex.OriginTime).TotalMinutes) <= 45.0 ||
						(refinedOrigin >= ex.OriginTime && refinedOrigin <= exEnd))
					{
						if (isMaj && !ex.IsMajor)
							ex.IsMajor = true;
						mergedIntoExistingMajor = true;
						break;
					}
				}
				if (mergedIntoExistingMajor)
					continue;

				double breakBuf = Math.Max(tick * 2.0, pv.Atr * breakAtrMult);
				int startEvalIdx = FindFirstMinorBarIndexAfter(minorBars, pv.ConfirmTime > refinedOrigin ? pv.ConfirmTime : refinedOrigin);

				bool hasBrokenPhase1 = false;
				DateTime break1Time = DateTime.MinValue;
				int break1BarIdx = -1;

				// Phase 1: Original Support or Resistance — valid until a closed bar decisively breaks through it
				for (int b = startEvalIdx; b <= lastClosedBarIdx; b++)
				{
					double c = minorBars[b].Close;
					bool broken = pv.IsHigh
						? (c > levelPrice + breakBuf)
						: (c < levelPrice - breakBuf);
					if (broken)
					{
						hasBrokenPhase1 = true;
						break1Time = minorBars[b].Time;
						break1BarIdx = b;
						break;
					}
				}

				bool canFlipThisTrack = allowMajorFlip && isMaj; // Flip ONLY for Major S/R (never Minor)
				bool isFlipBroken = false;
				DateTime validationEndTime = DateTime.MinValue;

				if (hasBrokenPhase1)
				{
					if (canFlipThisTrack)
					{
						bool flipIsResistance = !pv.IsHigh;
						for (int b = break1BarIdx + 1; b <= lastClosedBarIdx; b++)
						{
							double c = minorBars[b].Close;
							bool flipInvalidated = flipIsResistance
								? (c > levelPrice + breakBuf)
								: (c < levelPrice - breakBuf);
							if (flipInvalidated)
							{
								isFlipBroken = true;
								validationEndTime = minorBars[b].Time;
								break;
							}
						}
					}
					else
					{
						// Minor S/R (or Major when flip toggle is disabled) ends validation right at break1Time
						isFlipBroken = true;
						validationEndTime = break1Time;
					}
				}

				string activeTag = null;
				string tierPrefix = isMaj ? "Major" : "Minor";
				if (!hasBrokenPhase1)
				{
					activeTag = string.Format(
						CultureInfo.InvariantCulture,
						"{0} {1} ({2})",
						tierPrefix,
						pv.IsHigh ? "Res" : "Sup",
						FormatMpPrice(levelPrice));
				}
				else if (canFlipThisTrack && !isFlipBroken)
				{
					bool flipIsRes = !pv.IsHigh;
					activeTag = string.Format(
						CultureInfo.InvariantCulture,
						"Major Flip {0} ({1})",
						flipIsRes ? "Res" : "Sup",
						FormatMpPrice(levelPrice));
				}

				tracks.Add(new SrLineTrack
				{
					Price = levelPrice,
					InitialIsResistance = pv.IsHigh,
					IsMajor = isMaj,
					OriginTime = refinedOrigin,
					HasFlipped = hasBrokenPhase1,
					FlipTime = break1Time,
					IsFlipBroken = isFlipBroken,
					ValidationEndTime = validationEndTime,
					ActiveRightTagText = activeTag
				});
			}

			return tracks;
		}

		private List<SrHorizonLevel> BuildDayWeekMonthHorizonLevels(
			SortedDictionary<DateTime, DailyHorizonSample> mergedDaily,
			DateTime latestTime)
		{
			List<SrHorizonLevel> levels = new List<SrHorizonLevel>();
			if (mergedDaily == null || mergedDaily.Count == 0)
				return levels;

			DateTime curTradingDate = ResolveSrTradingDate(latestTime);
			DateTime curWeekMon = GetMondayOfWeek(curTradingDate);
			DateTime curMonthStart = new DateTime(curTradingDate.Year, curTradingDate.Month, 1);

			// 1. Previous Completed Trading Day High & Low
			if (ShowSrDayHighLow)
			{
				DailyHorizonSample prevDay = null;
				foreach (var kv in mergedDaily)
				{
					if (kv.Key < curTradingDate && kv.Value.High > double.MinValue && kv.Value.Low < double.MaxValue)
						prevDay = kv.Value;
				}
				if (prevDay != null)
				{
					string dLbl = prevDay.Date.ToString("dd MMM", CultureInfo.InvariantCulture);
					double pdh = RoundToInstrumentTick(prevDay.High);
					double pdl = RoundToInstrumentTick(prevDay.Low);
					levels.Add(new SrHorizonLevel
					{
						KeyLabel = "DayHigh",
						RightTagText = string.Format(CultureInfo.InvariantCulture, "DayHigh ({0} {1})", dLbl, FormatMpPrice(pdh)),
						Price = pdh,
						OriginTime = prevDay.HighTime > DateTime.MinValue ? prevDay.HighTime : prevDay.Date,
						Kind = 0,
						IsHigh = true
					});
					levels.Add(new SrHorizonLevel
					{
						KeyLabel = "DayLow",
						RightTagText = string.Format(CultureInfo.InvariantCulture, "DayLow ({0} {1})", dLbl, FormatMpPrice(pdl)),
						Price = pdl,
						OriginTime = prevDay.LowTime > DateTime.MinValue ? prevDay.LowTime : prevDay.Date,
						Kind = 0,
						IsHigh = false
					});
				}
			}

			// 2. Previous Completed Trading Week High & Low
			if (ShowSrWeekHighLow)
			{
				DateTime targetWeekMon = DateTime.MinValue;
				foreach (var kv in mergedDaily)
				{
					DateTime wm = GetMondayOfWeek(kv.Key);
					if (wm < curWeekMon && wm > targetWeekMon)
						targetWeekMon = wm;
				}
				if (targetWeekMon > DateTime.MinValue)
				{
					double wkHigh = double.MinValue;
					DateTime wkHighTime = targetWeekMon;
					double wkLow = double.MaxValue;
					DateTime wkLowTime = targetWeekMon;
					foreach (var kv in mergedDaily)
					{
						if (GetMondayOfWeek(kv.Key) != targetWeekMon)
							continue;
						if (kv.Value.High > wkHigh)
						{
							wkHigh = kv.Value.High;
							wkHighTime = kv.Value.HighTime > DateTime.MinValue ? kv.Value.HighTime : kv.Key;
						}
						if (kv.Value.Low < wkLow)
						{
							wkLow = kv.Value.Low;
							wkLowTime = kv.Value.LowTime > DateTime.MinValue ? kv.Value.LowTime : kv.Key;
						}
					}
					if (wkHigh > double.MinValue && wkLow < double.MaxValue)
					{
						string wLbl = FormatWeekRangeLabel(targetWeekMon, targetWeekMon.AddDays(4));
						double pwh = RoundToInstrumentTick(wkHigh);
						double pwl = RoundToInstrumentTick(wkLow);
						levels.Add(new SrHorizonLevel
						{
							KeyLabel = "WeekHigh",
							RightTagText = string.Format(CultureInfo.InvariantCulture, "WeekHigh ({0} {1})", wLbl, FormatMpPrice(pwh)),
							Price = pwh,
							OriginTime = wkHighTime,
							Kind = 1,
							IsHigh = true
						});
						levels.Add(new SrHorizonLevel
						{
							KeyLabel = "WeekLow",
							RightTagText = string.Format(CultureInfo.InvariantCulture, "WeekLow ({0} {1})", wLbl, FormatMpPrice(pwl)),
							Price = pwl,
							OriginTime = wkLowTime,
							Kind = 1,
							IsHigh = false
						});
					}
				}
			}

			// 3. Previous Completed Calendar Month High & Low
			if (ShowSrMonthHighLow)
			{
				DateTime targetMonth = DateTime.MinValue;
				foreach (var kv in mergedDaily)
				{
					DateTime ms = new DateTime(kv.Key.Year, kv.Key.Month, 1);
					if (ms < curMonthStart && ms > targetMonth)
						targetMonth = ms;
				}
				if (targetMonth > DateTime.MinValue)
				{
					double mHigh = double.MinValue;
					DateTime mHighTime = targetMonth;
					double mLow = double.MaxValue;
					DateTime mLowTime = targetMonth;
					foreach (var kv in mergedDaily)
					{
						if (kv.Key.Year != targetMonth.Year || kv.Key.Month != targetMonth.Month)
							continue;
						if (kv.Value.High > mHigh)
						{
							mHigh = kv.Value.High;
							mHighTime = kv.Value.HighTime > DateTime.MinValue ? kv.Value.HighTime : kv.Key;
						}
						if (kv.Value.Low < mLow)
						{
							mLow = kv.Value.Low;
							mLowTime = kv.Value.LowTime > DateTime.MinValue ? kv.Value.LowTime : kv.Key;
						}
					}
					if (mHigh > double.MinValue && mLow < double.MaxValue)
					{
						string mLbl = targetMonth.ToString("MMM yyyy", CultureInfo.InvariantCulture);
						double pmh = RoundToInstrumentTick(mHigh);
						double pml = RoundToInstrumentTick(mLow);
						levels.Add(new SrHorizonLevel
						{
							KeyLabel = "MonthHigh",
							RightTagText = string.Format(CultureInfo.InvariantCulture, "MonthHigh ({0} {1})", mLbl, FormatMpPrice(pmh)),
							Price = pmh,
							OriginTime = mHighTime,
							Kind = 2,
							IsHigh = true
						});
						levels.Add(new SrHorizonLevel
						{
							KeyLabel = "MonthLow",
							RightTagText = string.Format(CultureInfo.InvariantCulture, "MonthLow ({0} {1})", mLbl, FormatMpPrice(pml)),
							Price = pml,
							OriginTime = mLowTime,
							Kind = 2,
							IsHigh = false
						});
					}
				}
			}

			return levels;
		}

		private float ResolveSrUnclampedTimeX(ChartControl cc, Bars chartBars, float panelLeft, DateTime ts)
		{
			if (cc == null || chartBars == null || chartBars.Count == 0)
				return panelLeft + 2f;

			DateTime firstT = chartBars.GetTime(0);
			if (ts < firstT)
				return panelLeft - 50f;

			int lo = 0, hi = chartBars.Count - 1, ans = chartBars.Count - 1;
			while (lo <= hi)
			{
				int mid = (lo + hi) >> 1;
				if (chartBars.GetTime(mid) >= ts)
				{
					ans = mid;
					hi = mid - 1;
				}
				else
				{
					lo = mid + 1;
				}
			}
			return (float)cc.GetXByBarIndex(ChartBars, ans);
		}

		private float ResolveSrOriginXOnChart(ChartControl cc, Bars chartBars, float barSpacing, float panelLeft, float rightLimitX, DateTime originTime)
		{
			float rawX = ResolveSrUnclampedTimeX(cc, chartBars, panelLeft, originTime);
			return Math.Max(panelLeft + 2f, Math.Min(rightLimitX - 16f, rawX));
		}

		private void DrawFlipDiamondMarker(float cx, float cy, float r, Color4 fillCol, Color4 borderCol, DxBrush dynBrush)
		{
			float snappedY = (float)Math.Floor(cy) + 0.5f;
			float snappedX = (float)Math.Floor(cx) + 0.5f;
			dynBrush.Color = fillCol;
			RenderTarget.FillEllipse(new Ellipse(new Vector2(snappedX, snappedY), r, r), dynBrush);
			dynBrush.Color = borderCol;
			RenderTarget.DrawEllipse(new Ellipse(new Vector2(snappedX, snappedY), r + 0.8f, r + 0.8f), dynBrush, 1.15f);
		}

		private void RenderMultiTimeframeSrZonesOnCanvas(
			ChartControl cc,
			Bars chartBars,
			SrComputedSnapshot snap,
			Func<double, float> priceToY,
			float barSpacing,
			float panelLeft,
			float canvasTopY,
			float rightLimitX,
			float canvasBotY,
			bool light,
			List<RightEdgeTag> rightEdgeTags)
		{
			if (snap == null || rightLimitX <= panelLeft + 24f)
				return;

			var dashProps = new StrokeStyleProperties
			{
				DashStyle = SharpDX.Direct2D1.DashStyle.Custom,
				DashCap = CapStyle.Flat,
				StartCap = CapStyle.Flat,
				EndCap = CapStyle.Flat
			};

			using (var flipMajorDash = new StrokeStyle(Globals.D2DFactory, dashProps, new float[] { 8f, 4f }))
			using (var flipMinorDash = new StrokeStyle(Globals.D2DFactory, dashProps, new float[] { 5f, 3f }))
			using (var weekDash = new StrokeStyle(Globals.D2DFactory, dashProps, new float[] { 9f, 4f }))
			using (var dynBrush = new DxBrush(RenderTarget, Color4.White))
			using (var flipTagTf = new DxTextFormat(Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 8.2f))
			{
				flipTagTf.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				flipTagTf.ParagraphAlignment = ParagraphAlignment.Center;
				flipTagTf.WordWrapping = WordWrapping.NoWrap;

				Color4 supActiveMajor = light ? new Color4(0.02f, 0.58f, 0.36f, 0.96f) : new Color4(0.06f, 0.86f, 0.54f, 0.96f);
				Color4 supActiveMinor = light ? new Color4(0.05f, 0.64f, 0.40f, 0.86f) : new Color4(0.20f, 0.84f, 0.58f, 0.86f);
				Color4 supHistMajor   = light ? new Color4(0.02f, 0.58f, 0.36f, 0.72f) : new Color4(0.06f, 0.86f, 0.54f, 0.74f);
				Color4 supHistMinor   = light ? new Color4(0.05f, 0.64f, 0.40f, 0.60f) : new Color4(0.20f, 0.84f, 0.58f, 0.62f);

				Color4 resActiveMajor = light ? new Color4(0.80f, 0.12f, 0.22f, 0.96f) : new Color4(0.98f, 0.28f, 0.36f, 0.96f);
				Color4 resActiveMinor = light ? new Color4(0.84f, 0.22f, 0.30f, 0.86f) : new Color4(0.98f, 0.42f, 0.48f, 0.86f);
				Color4 resHistMajor   = light ? new Color4(0.80f, 0.12f, 0.22f, 0.72f) : new Color4(0.98f, 0.28f, 0.36f, 0.74f);
				Color4 resHistMinor   = light ? new Color4(0.84f, 0.22f, 0.30f, 0.60f) : new Color4(0.98f, 0.42f, 0.48f, 0.62f);

				Color4 flipNodeBorder = light ? new Color4(0.10f, 0.15f, 0.22f, 0.95f) : new Color4(1.0f, 0.96f, 0.84f, 0.95f);
				Color4 flipBadgeBg    = light ? new Color4(0.95f, 0.97f, 1.0f, 0.92f) : new Color4(0.07f, 0.10f, 0.15f, 0.90f);

				// 1. Render Minor & Major S/R Lines (Origin Major Highlighted + Full History + Flip ONLY on Major)
				for (int i = 0; i < snap.LineTracks.Count; i++)
				{
					SrLineTrack tr = snap.LineTracks[i];
					float y = priceToY(tr.Price);
					if (y < canvasTopY || y > canvasBotY)
						continue;

					float rawXOrigin = ResolveSrUnclampedTimeX(cc, chartBars, panelLeft, tr.OriginTime);
					float origW = tr.IsMajor ? 2.1f : 1.20f;

					if (!tr.HasFlipped)
					{
						// Unbroken Original S/R Line extending from Origin -> rightLimitX
						if (rawXOrigin <= rightLimitX - 2f)
						{
							float x0 = Math.Max(panelLeft + 2f, rawXOrigin);
							Color4 col = tr.InitialIsResistance
								? (tr.IsMajor ? resActiveMajor : resActiveMinor)
								: (tr.IsMajor ? supActiveMajor : supActiveMinor);
							dynBrush.Color = col;
							DrawHairlineHorizontal(x0, rightLimitX, y, dynBrush, origW);

							// Origin Major Support / Resistance Anchor & Badge right at the origin candle wick
							if (tr.IsMajor && rawXOrigin >= panelLeft + 4f && rawXOrigin <= rightLimitX - 10f)
							{
								DrawFlipDiamondMarker(rawXOrigin, y, 3.0f, col, flipNodeBorder, dynBrush);
								if ((rightLimitX - rawXOrigin) >= 58f && barSpacing >= 6f)
								{
									string origLbl = tr.InitialIsResistance ? "Major Res" : "Major Sup";
									float bTop = tr.InitialIsResistance ? (y - 14.5f) : (y + 2.5f);
									if (bTop >= canvasTopY + 1f && bTop + 12f <= canvasBotY - 1f)
									{
										RectangleF origBadge = new RectangleF(rawXOrigin + 4f, bTop, 52f, 12f);
										dynBrush.Color = flipBadgeBg;
										RenderTarget.FillRectangle(origBadge, dynBrush);
										dynBrush.Color = col;
										RenderTarget.DrawRectangle(origBadge, dynBrush, 0.75f);
										DrawMpText(origLbl, origBadge.X, origBadge.Y, origBadge.Width, origBadge.Height, flipTagTf, dynBrush);
									}
								}
							}

							if (!string.IsNullOrEmpty(tr.ActiveRightTagText))
							{
								rightEdgeTags.Add(new RightEdgeTag
								{
									Price = tr.Price,
									IdealY = y - 7.5f,
									Height = 15f,
									Text = tr.ActiveRightTagText,
									TextColor = col,
									FillColor = tr.InitialIsResistance
										? (light ? new Color4(1.0f, 0.93f, 0.94f, 0.98f) : new Color4(0.20f, 0.05f, 0.08f, 0.96f))
										: (light ? new Color4(0.92f, 0.99f, 0.95f, 0.98f) : new Color4(0.04f, 0.18f, 0.12f, 0.96f)),
									BorderColor = col,
									Priority = tr.IsMajor ? 4 : 2
								});
							}
						}
						continue;
					}

					// Phase 1: Historical Invalidated Original S/R Segment — ONLY shown when Analysis mode is Active!
					float rawXFlip = ResolveSrUnclampedTimeX(cc, chartBars, panelLeft, tr.FlipTime);
					if (runtimeShowSrAnalysis && rawXFlip >= panelLeft + 2f && rawXOrigin <= rightLimitX - 2f)
					{
						float x0 = Math.Max(panelLeft + 2f, rawXOrigin);
						float x1 = Math.Min(rightLimitX, rawXFlip);
						Color4 col1 = tr.InitialIsResistance
							? (tr.IsMajor ? resActiveMajor : resHistMinor)
							: (tr.IsMajor ? supActiveMajor : supHistMinor);

						if (x1 > x0 + 1.5f)
						{
							dynBrush.Color = col1;
							DrawHairlineHorizontal(x0, x1, y, dynBrush, origW);

							// Minor S/R ends validation right here (no flip) — draw clean end tick at x1
							if (!tr.IsMajor && rawXFlip >= panelLeft + 3f && rawXFlip <= rightLimitX - 3f)
							{
								RenderTarget.DrawLine(new Vector2(x1, y - 3f), new Vector2(x1, y + 3f), dynBrush, 1.15f);
							}
						}

						// Show Origin Major Support / Resistance Anchor & Badge right at the origin candle wick
						if (tr.IsMajor && rawXOrigin >= panelLeft + 4f && rawXOrigin <= rightLimitX - 10f)
						{
							DrawFlipDiamondMarker(rawXOrigin, y, 2.9f, col1, flipNodeBorder, dynBrush);
							if ((x1 - rawXOrigin) >= 58f && barSpacing >= 6f)
							{
								string origLbl = tr.InitialIsResistance ? "Major Res" : "Major Sup";
								float bTop = tr.InitialIsResistance ? (y - 14.5f) : (y + 2.5f);
								if (bTop >= canvasTopY + 1f && bTop + 12f <= canvasBotY - 1f)
								{
									RectangleF origBadge = new RectangleF(rawXOrigin + 4f, bTop, 52f, 12f);
									dynBrush.Color = flipBadgeBg;
									RenderTarget.FillRectangle(origBadge, dynBrush);
									dynBrush.Color = col1;
									RenderTarget.DrawRectangle(origBadge, dynBrush, 0.75f);
									DrawMpText(origLbl, origBadge.X, origBadge.Y, origBadge.Width, origBadge.Height, flipTagTf, dynBrush);
								}
							}
						}
					}

					// Phase 2: Role-Reversal Flip Segment — ONLY for Major Support & Resistance (never Minor)
					if (!tr.IsMajor || !ShowSrFlipLines)
						continue;

					bool flipIsRes = !tr.InitialIsResistance;
					float flipW = 1.90f;
					StrokeStyle flipStroke = flipMajorDash;

					if (!tr.IsFlipBroken)
					{
						// Active Unbroken Major Flip Line extending from FlipTime -> rightLimitX
						if (rawXFlip <= rightLimitX - 2f)
						{
							float fx0 = Math.Max(panelLeft + 2f, rawXFlip);
							Color4 fCol = flipIsRes ? resActiveMajor : supActiveMajor;

							if (fx0 < rightLimitX - 1.5f)
							{
								dynBrush.Color = fCol;
								DrawHairlineHorizontal(fx0, rightLimitX, y, dynBrush, flipW, flipStroke);
							}

							if (rawXFlip >= panelLeft + 4f && rawXFlip <= rightLimitX - 6f)
							{
								DrawFlipDiamondMarker(rawXFlip, y, 2.9f, fCol, flipNodeBorder, dynBrush);
								if ((rightLimitX - rawXFlip) >= 48f && barSpacing >= 6f)
								{
									string flipMini = flipIsRes ? "S▸R" : "R▸S";
									float fbTop = flipIsRes ? (y - 14f) : (y + 2.5f);
									if (fbTop >= canvasTopY + 1f && fbTop + 11.5f <= canvasBotY - 1f)
									{
										RectangleF badgeRect = new RectangleF(rawXFlip + 4f, fbTop, 26f, 11.5f);
										dynBrush.Color = flipBadgeBg;
										RenderTarget.FillRectangle(badgeRect, dynBrush);
										dynBrush.Color = fCol;
										RenderTarget.DrawRectangle(badgeRect, dynBrush, 0.75f);
										DrawMpText(flipMini, badgeRect.X, badgeRect.Y, badgeRect.Width, badgeRect.Height, flipTagTf, dynBrush);
									}
								}
							}

							if (!string.IsNullOrEmpty(tr.ActiveRightTagText))
							{
								rightEdgeTags.Add(new RightEdgeTag
								{
									Price = tr.Price,
									IdealY = y - 7.5f,
									Height = 15f,
									Text = tr.ActiveRightTagText,
									TextColor = fCol,
									FillColor = flipIsRes
										? (light ? new Color4(1.0f, 0.93f, 0.94f, 0.98f) : new Color4(0.20f, 0.05f, 0.08f, 0.96f))
										: (light ? new Color4(0.92f, 0.99f, 0.95f, 0.98f) : new Color4(0.04f, 0.18f, 0.12f, 0.96f)),
									BorderColor = fCol,
									Priority = 4
								});
							}
						}
					}
					else if (runtimeShowSrAnalysis)
					{
						// Historical Completed Major Flip Line (FlipTime -> ValidationEndTime where flip validation finished) — Analysis Mode Only!
						float rawXEnd = ResolveSrUnclampedTimeX(cc, chartBars, panelLeft, tr.ValidationEndTime);
						if (rawXEnd >= panelLeft + 2f && rawXFlip <= rightLimitX - 2f)
						{
							float fx0 = Math.Max(panelLeft + 2f, rawXFlip);
							float fx1 = Math.Min(rightLimitX, rawXEnd);
							Color4 fHistCol = flipIsRes ? resHistMajor : supHistMajor;

							if (fx1 > fx0 + 1.5f)
							{
								dynBrush.Color = fHistCol;
								DrawHairlineHorizontal(fx0, fx1, y, dynBrush, flipW, flipStroke);

								if (rawXEnd >= panelLeft + 3f && rawXEnd <= rightLimitX - 3f)
								{
									RenderTarget.DrawLine(new Vector2(fx1, y - 3.5f), new Vector2(fx1, y + 3.5f), dynBrush, 1.35f);
								}
							}

							if (rawXFlip >= panelLeft + 4f && rawXFlip <= rightLimitX - 6f)
							{
								DrawFlipDiamondMarker(rawXFlip, y, 2.7f, fHistCol, flipNodeBorder, dynBrush);
								if ((fx1 - fx0) >= 44f && barSpacing >= 6f)
								{
									string flipMini = flipIsRes ? "S▸R" : "R▸S";
									float fbTop = flipIsRes ? (y - 14f) : (y + 2.5f);
									if (fbTop >= canvasTopY + 1f && fbTop + 11.5f <= canvasBotY - 1f)
									{
										RectangleF badgeRect = new RectangleF(rawXFlip + 4f, fbTop, 26f, 11.5f);
										dynBrush.Color = flipBadgeBg;
										RenderTarget.FillRectangle(badgeRect, dynBrush);
										dynBrush.Color = fHistCol;
										RenderTarget.DrawRectangle(badgeRect, dynBrush, 0.75f);
										DrawMpText(flipMini, badgeRect.X, badgeRect.Y, badgeRect.Width, badgeRect.Height, flipTagTf, dynBrush);
									}
								}
							}
						}
					}
				}

				// 2. Render Day High/Low, Week High/Low, Month High/Low Lines
				for (int i = 0; i < snap.HorizonLevels.Count; i++)
				{
					SrHorizonLevel hl = snap.HorizonLevels[i];
					float y = priceToY(hl.Price);
					if (y < canvasTopY || y > canvasBotY)
						continue;

					float x0 = ResolveSrOriginXOnChart(cc, chartBars, barSpacing, panelLeft, rightLimitX, hl.OriginTime);
					Color4 lineCol;
					Color4 pillFill;
					float lineW;
					StrokeStyle hStroke = null;
					int prio;

					if (hl.Kind == 2) // Month High / Low (Royal Violet)
					{
						lineCol = light ? new Color4(0.52f, 0.16f, 0.82f, 0.95f) : new Color4(0.78f, 0.52f, 0.99f, 0.96f);
						pillFill = light ? new Color4(0.96f, 0.92f, 1.0f, 0.98f) : new Color4(0.16f, 0.08f, 0.26f, 0.97f);
						lineW = 1.85f;
						prio = 6;
					}
					else if (hl.Kind == 1) // Week High / Low (Vivid Gold-Amber)
					{
						lineCol = light ? new Color4(0.74f, 0.44f, 0.04f, 0.95f) : new Color4(0.98f, 0.80f, 0.18f, 0.95f);
						pillFill = light ? new Color4(1.0f, 0.97f, 0.88f, 0.98f) : new Color4(0.22f, 0.16f, 0.04f, 0.97f);
						lineW = 1.65f;
						hStroke = weekDash;
						prio = 5;
					}
					else // Day High / Low (Crisp Platinum-Silver / Slate)
					{
						lineCol = light ? new Color4(0.12f, 0.18f, 0.26f, 0.94f) : new Color4(0.88f, 0.92f, 0.97f, 0.92f);
						pillFill = light ? new Color4(0.92f, 0.95f, 0.98f, 0.98f) : new Color4(0.10f, 0.15f, 0.22f, 0.97f);
						lineW = 1.55f;
						prio = 5;
					}

					dynBrush.Color = lineCol;
					DrawHairlineHorizontal(x0, rightLimitX, y, dynBrush, lineW, hStroke);

					rightEdgeTags.Add(new RightEdgeTag
					{
						Price = hl.Price,
						IdealY = y - 7.5f,
						Height = 15f,
						Text = hl.RightTagText,
						TextColor = lineCol,
						FillColor = pillFill,
						BorderColor = lineCol,
						Priority = prio
					});
				}
			}
		}
		#endregion

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
			if (rawTags == null || rawTags.Count == 0 || maxY <= minY + 14f)
				return;

			List<RightEdgeTag> ordered = rawTags
				.OrderBy(t => t.IdealY)
				.ThenByDescending(t => t.Priority)
				.ToList();

			List<RightEdgeTag> tags = new List<RightEdgeTag>();
			for (int i = 0; i < ordered.Count; i++)
			{
				RightEdgeTag candidate = ordered[i];
				if (candidate == null || string.IsNullOrEmpty(candidate.Text))
					continue;

				bool isExactSameLabel = false;
				for (int j = 0; j < tags.Count; j++)
				{
					// Only deduplicate if the exact same text label was emitted twice at the same price;
					// NEVER hide MP Zone tags when S/R Zone tags exist at the same price (show BOTH stacked cleanly!)
					if (Math.Abs(tags[j].Price - candidate.Price) <= step * 0.45 &&
						string.Equals(tags[j].Text, candidate.Text, StringComparison.OrdinalIgnoreCase))
					{
						if (candidate.Priority > tags[j].Priority)
							tags[j] = candidate;
						isExactSameLabel = true;
						break;
					}
				}
				if (!isExactSameLabel)
					tags.Add(candidate);
			}

			tags.Sort((a, b) =>
			{
				int cmp = a.IdealY.CompareTo(b.IdealY);
				return cmp != 0 ? cmp : b.Priority.CompareTo(a.Priority);
			});

			float curBottom = minY;
			for (int i = 0; i < tags.Count; i++)
			{
				RightEdgeTag t = tags[i];
				float y = Math.Max(curBottom, t.IdealY);
				t.DrawY = y;
				curBottom = y + t.Height + 2.5f;
			}

			if (curBottom > maxY)
			{
				float nextTop = maxY;
				for (int i = tags.Count - 1; i >= 0; i--)
				{
					RightEdgeTag t = tags[i];
					if (t.DrawY + t.Height > nextTop)
						t.DrawY = Math.Max(minY, nextTop - t.Height);
					nextTop = t.DrawY - 2.5f;
				}
			}

			float laneDividerX = panelRight - RightValuesLaneWidth;
			float boxWidth = 178f;
			float boxLeft = panelRight - boxWidth - 3f;
			using (var customBrush = new DxBrush(RenderTarget, Color4.White))
			{
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
					var tagRr = new RoundedRectangle { Rect = bgRect, RadiusX = 4.0f, RadiusY = 4.0f };

					// Draw crisp connector tick from exact price line Y on the divider to the shifted tag box
					float anchorLineY = Math.Max(minY, Math.Min(maxY, t.IdealY + t.Height * 0.5f));
					float tagMidY = t.DrawY + t.Height * 0.5f;
					if (boxLeft > laneDividerX + 1.5f)
					{
						if (t.BorderColor.HasValue)
						{
							customBrush.Color = t.BorderColor.Value;
							RenderTarget.DrawLine(new Vector2(laneDividerX + 0.5f, anchorLineY), new Vector2(boxLeft, tagMidY), customBrush, 1.0f);
						}
						else if (t.BorderBrush != null || t.Brush != null)
						{
							RenderTarget.DrawLine(new Vector2(laneDividerX + 0.5f, anchorLineY), new Vector2(boxLeft, tagMidY), t.BorderBrush ?? t.Brush, 1.0f);
						}
					}

					if (t.FillColor.HasValue)
					{
						customBrush.Color = t.FillColor.Value;
						RenderTarget.FillRoundedRectangle(tagRr, customBrush);
					}
					else
					{
						RenderTarget.FillRoundedRectangle(tagRr, t.FillBrush ?? bgBrush);
					}

					if (t.BorderColor.HasValue)
					{
						customBrush.Color = t.BorderColor.Value;
						RenderTarget.DrawRoundedRectangle(tagRr, customBrush, 1.0f);
					}
					else
					{
						RenderTarget.DrawRoundedRectangle(tagRr, t.BorderBrush ?? borderBrush, t.BorderBrush != null ? 1.0f : 0.85f);
					}

					// AK10X Market Profile signature left vertical color accent bar inside each right-edge level pill
					RectangleF accentBarRect = new RectangleF(boxLeft + 1.6f, t.DrawY + 2.4f, 2.8f, Math.Max(2f, t.Height - 4.8f));
					if (t.TextColor.HasValue)
					{
						customBrush.Color = t.TextColor.Value;
						RenderTarget.FillRectangle(accentBarRect, customBrush);
						DrawMpText(dispTagText, boxLeft + 6f, t.DrawY, boxWidth - 9f, t.Height, rightFormat, customBrush);
					}
					else if (t.Brush != null)
					{
						RenderTarget.FillRectangle(accentBarRect, t.Brush);
						DrawMpText(dispTagText, boxLeft + 6f, t.DrawY, boxWidth - 9f, t.Height, rightFormat, t.Brush);
					}
				}
			}
		}

		private void DrawHairlineHorizontal(float x0, float x1, float rawY, DxBrush brush, float width = 0.7f, StrokeStyle stroke = null)
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

		private void DrawMpText(string text, float x, float y, float width, float height, DxTextFormat format, DxBrush brush)
		{
			if (width <= 0 || height <= 0 || string.IsNullOrEmpty(text))
				return;
			RenderTarget.DrawText(text, format, new RectangleF(x, y, width, height), brush, DrawTextOptions.NoSnap | DrawTextOptions.Clip);
		}

		#region Multi-Timeframe Closed-Candle Signal Engine (PineScript Raj 3m + 9m Zones & Swings — Strictly Closed Candles Only)
		private int GetLastClosedChartBarIndex(Bars chartBars)
		{
			if (chartBars == null || chartBars.Count <= 0)
				return -1;

			int lastIdx = chartBars.Count - 1;
			bool isPlayback = false;
			DateTime nowNt;
			try
			{
				if (Connection.PlaybackConnection != null && Connection.PlaybackConnection.Status == ConnectionStatus.Connected)
				{
					isPlayback = true;
					nowNt = Connection.PlaybackConnection.Now;
				}
				else
				{
					nowNt = Globals.Now;
				}
			}
			catch
			{
				nowNt = DateTime.Now;
			}

			if (State == State.Realtime || isPlayback)
			{
				BarsPeriod bp = chartBars.BarsPeriod ?? BarsPeriod;
				BarsPeriodType pType = bp != null ? bp.BarsPeriodType : BarsPeriodType.Minute;
				if (pType == BarsPeriodType.HeikenAshi || pType == BarsPeriodType.Volumetric)
					pType = bp.BaseBarsPeriodType;

				if (pType == BarsPeriodType.Minute || pType == BarsPeriodType.Second)
				{
					DateTime barCloseTime = chartBars.GetTime(lastIdx);
					if (nowNt < barCloseTime)
						return lastIdx - 1;
					return lastIdx;
				}

				return lastIdx - 1;
			}

			DateTime histCloseTime = chartBars.GetTime(lastIdx);
			if (histCloseTime > nowNt.AddSeconds(1))
				return lastIdx - 1;
			return lastIdx;
		}

		private static int GetSignalZonePriority(SignalActiveZone z, int neededReactions)
		{
			if ((z.SourceMask & 2) != 0)
				return 3; // Contains Higher TF (9m)
			if ((z.SourceMask & 1) != 0)
				return 2; // Contains Current TF (3m)
			if (z.VeryStrong || z.Reactions >= neededReactions)
				return 1;
			return 0;
		}

		private static int PruneSignalZoneIndex(List<SignalActiveZone> zones, int neededReactions, bool supplySide)
		{
			int weakestIndex = 0;
			int weakestPrio = GetSignalZonePriority(zones[0], neededReactions);
			double pruneEdge = supplySide ? -zones[0].Bottom : zones[0].Bottom;

			for (int j = 1; j < zones.Count; j++)
			{
				int prio = GetSignalZonePriority(zones[j], neededReactions);
				double candEdge = supplySide ? -zones[j].Bottom : zones[j].Bottom;
				if (prio < weakestPrio || (prio == weakestPrio && candEdge < pruneEdge))
				{
					weakestIndex = j;
					weakestPrio = prio;
					pruneEdge = candEdge;
				}
			}
			return weakestIndex;
		}

		private static void UpsertAndMergeSignalZone(
			List<SignalActiveZone> zones,
			double newTop,
			double newBottom,
			DateTime newLeftTime,
			int newSourceMask,
			int maxZonesPerSide,
			int neededReactions,
			bool supplySide)
		{
			double zTop = newTop;
			double zBot = newBottom;
			DateTime zLeft = newLeftTime;
			int zMask = newSourceMask;
			bool expanded = true;

			while (expanded && zones.Count > 0)
			{
				double expTop = zTop;
				double expBot = zBot;
				for (int i = 0; i < zones.Count; i++)
				{
					SignalActiveZone ex = zones[i];
					if (zBot <= ex.Top && zTop >= ex.Bottom)
					{
						zMask |= ex.SourceMask;
						if (ex.Top > expTop) expTop = ex.Top;
						if (ex.Bottom < expBot) expBot = ex.Bottom;
						if (ex.LeftTime < zLeft) zLeft = ex.LeftTime;
					}
				}
				expanded = (expTop != zTop || expBot != zBot);
				zTop = expTop;
				zBot = expBot;
			}

			int keepIdx = -1;
			int k = 0;
			while (k < zones.Count)
			{
				SignalActiveZone ex = zones[k];
				bool intersects = (zBot <= ex.Top && zTop >= ex.Bottom);
				if (intersects && keepIdx == -1)
				{
					keepIdx = k;
					ex.Top = zTop;
					ex.Bottom = zBot;
					ex.LeftTime = zLeft;
					ex.SourceMask = zMask;
					ex.VeryStrong = true;
					ex.WasInZone = false;
					k++;
				}
				else if (intersects)
				{
					zones.RemoveAt(k);
				}
				else
				{
					k++;
				}
			}

			if (keepIdx == -1)
			{
				zones.Add(new SignalActiveZone
				{
					Top = zTop,
					Bottom = zBot,
					LeftTime = zLeft,
					SourceMask = zMask,
					Reactions = 0,
					WasInZone = false,
					VeryStrong = false
				});
			}

			while (zones.Count > maxZonesPerSide)
			{
				int dropIdx = PruneSignalZoneIndex(zones, neededReactions, supplySide);
				zones.RemoveAt(dropIdx);
			}
		}

		private static double[] ComputeWilderRmaAtr(List<SignalOhlcBar> bars, int atrLen)
		{
			int n = bars.Count;
			double[] atr = new double[n];
			if (n == 0)
				return atr;

			int len = Math.Max(1, atrLen);
			double sumTr = 0.0;
			int initCount = Math.Min(n, len);
			for (int i = 0; i < n; i++)
			{
				double tr = (i == 0)
					? Math.Max(0.0, bars[0].High - bars[0].Low)
					: Math.Max(
						bars[i].High - bars[i].Low,
						Math.Max(Math.Abs(bars[i].High - bars[i - 1].Close), Math.Abs(bars[i].Low - bars[i - 1].Close)));

				if (i < initCount)
				{
					sumTr += tr;
					atr[i] = sumTr / (i + 1);
				}
				else
				{
					atr[i] = ((atr[i - 1] * (len - 1)) + tr) / len;
				}
			}
			return atr;
		}

		private static List<SignalRawPivot> DetectClosedSignalPivots(List<SignalOhlcBar> bars, double[] atr, int leftBars, int rightBars, double eps)
		{
			List<SignalRawPivot> list = new List<SignalRawPivot>();
			int n = bars.Count;
			if (n < leftBars + rightBars + 1)
				return list;

			// Every bar in `bars` is already strictly closed, so maxPivotIdx = n - 1 - rightBars!
			int maxPivotIdx = n - 1 - rightBars;
			for (int i = leftBars; i <= maxPivotIdx; i++)
			{
				double ph = bars[i].High;
				bool okHigh = true;
				for (int l = i - leftBars; l < i; l++)
				{
					if (bars[l].High > ph + eps) { okHigh = false; break; }
				}
				if (okHigh)
				{
					for (int r = i + 1; r <= i + rightBars; r++)
					{
						if (bars[r].High >= ph - eps) { okHigh = false; break; }
					}
				}
				if (okHigh && atr[i] > 0.0)
				{
					list.Add(new SignalRawPivot
					{
						IsHigh = true,
						Price = ph,
						Atr = atr[i],
						PivotTime = bars[i].HighTime > DateTime.MinValue ? bars[i].HighTime : bars[i].Time,
						ConfirmTime = bars[i + rightBars].Time
					});
				}

				double pl = bars[i].Low;
				bool okLow = true;
				for (int l = i - leftBars; l < i; l++)
				{
					if (bars[l].Low < pl - eps) { okLow = false; break; }
				}
				if (okLow)
				{
					for (int r = i + 1; r <= i + rightBars; r++)
					{
						if (bars[r].Low <= pl + eps) { okLow = false; break; }
					}
				}
				if (okLow && atr[i] > 0.0)
				{
					list.Add(new SignalRawPivot
					{
						IsHigh = false,
						Price = pl,
						Atr = atr[i],
						PivotTime = bars[i].LowTime > DateTime.MinValue ? bars[i].LowTime : bars[i].Time,
						ConfirmTime = bars[i + rightBars].Time
					});
				}
			}
			return list;
		}

		private void BuildClosedSignalLtfAndHtfBars(
			Bars chartBars,
			int lastClosedBarIdx,
			List<MinuteSample> minuteCopy,
			int htfMult,
			out List<SignalOhlcBar> ltfBars,
			out List<SignalOhlcBar> htfBars)
		{
			ltfBars = new List<SignalOhlcBar>();
			htfBars = new List<SignalOhlcBar>();
			if (chartBars == null || lastClosedBarIdx < 0)
				return;

			BarsPeriod bp = chartBars.BarsPeriod ?? BarsPeriod;
			BarsPeriodType pType = bp != null ? bp.BarsPeriodType : BarsPeriodType.Minute;
			int pVal = (bp != null && bp.Value > 0) ? bp.Value : 3;
			if (pType == BarsPeriodType.HeikenAshi || pType == BarsPeriodType.Volumetric)
			{
				pType = bp.BaseBarsPeriodType;
				pVal = Math.Max(1, bp.BaseBarsPeriodValue);
			}

			int mult = Math.Max(2, htfMult);
			DateTime epoch = new DateTime(2020, 1, 1, 0, 0, 0);

			if (pType == BarsPeriodType.Minute)
			{
				int ltfMin = Math.Max(1, pVal);
				int htfMin = ltfMin * mult; // e.g., 3 min * 3 = 9 min Higher Timeframe
				DateTime firstChartTime = chartBars.GetTime(0);
				DateTime lastClosedChartTime = chartBars.GetTime(lastClosedBarIdx);

				// 1. Prepend 1-minute backfill aggregated into closed LTF (e.g. 3m) buckets prior to firstChartTime
				if (minuteCopy != null && minuteCopy.Count > 0 && minuteCopy[0].Time < firstChartTime.AddMinutes(-ltfMin))
				{
					long curBucket = long.MinValue;
					SignalOhlcBar cur = default(SignalOhlcBar);
					bool hasCur = false;

					for (int i = 0; i < minuteCopy.Count; i++)
					{
						MinuteSample ms = minuteCopy[i];
						if (ms.Time >= firstChartTime)
							break;
						long b = (long)Math.Floor((ms.Time.AddSeconds(-1) - epoch).TotalMinutes / ltfMin);
						DateTime bucketClose = epoch.AddMinutes((b + 1) * ltfMin);
						if (bucketClose >= firstChartTime)
							break;

						if (b != curBucket)
						{
							if (hasCur) ltfBars.Add(cur);
							curBucket = b;
							cur = new SignalOhlcBar
							{
								Time = bucketClose,
								HighTime = ms.Time,
								LowTime = ms.Time,
								Open = ms.Open,
								High = ms.High,
								Low = ms.Low,
								Close = ms.Close,
								ChartBarIndex = -1
							};
							hasCur = true;
						}
						else
						{
							if (ms.High > cur.High) { cur.High = ms.High; cur.HighTime = ms.Time; }
							if (ms.Low < cur.Low) { cur.Low = ms.Low; cur.LowTime = ms.Time; }
							cur.Close = ms.Close;
						}
					}
					if (hasCur) ltfBars.Add(cur);
				}

				// 2. Append strictly CLOSED chart bars (0 .. lastClosedBarIdx)
				for (int i = 0; i <= lastClosedBarIdx; i++)
				{
					DateTime bt = chartBars.GetTime(i);
					ltfBars.Add(new SignalOhlcBar
					{
						Time = bt,
						HighTime = bt,
						LowTime = bt,
						Open = chartBars.GetOpen(i),
						High = chartBars.GetHigh(i),
						Low = chartBars.GetLow(i),
						Close = chartBars.GetClose(i),
						ChartBarIndex = i
					});
				}

				// 3. Aggregate LTF closed bars into strictly CLOSED Higher-TF (e.g. 9m) buckets
				long curHtfB = long.MinValue;
				SignalOhlcBar curHtf = default(SignalOhlcBar);
				bool hasHtf = false;

				for (int i = 0; i < ltfBars.Count; i++)
				{
					SignalOhlcBar lb = ltfBars[i];
					long hb = (long)Math.Floor((lb.Time.AddSeconds(-1) - epoch).TotalMinutes / htfMin);
					DateTime htfCloseTime = epoch.AddMinutes((hb + 1) * htfMin);

					if (hb != curHtfB)
					{
						if (hasHtf) htfBars.Add(curHtf);
						curHtfB = hb;
						curHtf = new SignalOhlcBar
						{
							Time = htfCloseTime,
							HighTime = lb.HighTime,
							LowTime = lb.LowTime,
							Open = lb.Open,
							High = lb.High,
							Low = lb.Low,
							Close = lb.Close,
							ChartBarIndex = -1
						};
						hasHtf = true;
					}
					else
					{
						if (lb.High > curHtf.High) { curHtf.High = lb.High; curHtf.HighTime = lb.HighTime; }
						if (lb.Low < curHtf.Low) { curHtf.Low = lb.Low; curHtf.LowTime = lb.LowTime; }
						curHtf.Close = lb.Close;
					}
				}
				// Only include the final 9m HTF candle if its full 9m interval has CLOSED (htfCloseTime <= lastClosedChartTime)!
				if (hasHtf && curHtf.Time <= lastClosedChartTime)
					htfBars.Add(curHtf);
				return;
			}

			// Fallback for Second / Tick / Volume / Range / Day charts: group `mult` closed chart bars per HTF bar
			for (int i = 0; i <= lastClosedBarIdx; i++)
			{
				DateTime bt = chartBars.GetTime(i);
				ltfBars.Add(new SignalOhlcBar
				{
					Time = bt,
					HighTime = bt,
					LowTime = bt,
					Open = chartBars.GetOpen(i),
					High = chartBars.GetHigh(i),
					Low = chartBars.GetLow(i),
					Close = chartBars.GetClose(i),
					ChartBarIndex = i
				});
			}

			for (int i = 0; i + mult - 1 < ltfBars.Count; i += mult)
			{
				SignalOhlcBar agg = ltfBars[i];
				for (int k = i + 1; k < i + mult; k++)
				{
					if (ltfBars[k].High > agg.High) { agg.High = ltfBars[k].High; agg.HighTime = ltfBars[k].HighTime; }
					if (ltfBars[k].Low < agg.Low) { agg.Low = ltfBars[k].Low; agg.LowTime = ltfBars[k].LowTime; }
					agg.Close = ltfBars[k].Close;
					agg.Time = ltfBars[k].Time;
				}
				htfBars.Add(agg);
			}
		}

		private ChartSignalMarker GetOrAddChartSignalMarker(Dictionary<int, ChartSignalMarker> map, int barIdx)
		{
			if (!map.TryGetValue(barIdx, out ChartSignalMarker m))
			{
				m = new ChartSignalMarker { BarIndex = barIdx };
				map[barIdx] = m;
			}
			return m;
		}

		private SignalComputedSnapshot EnsureChartSignalsComputed(Bars chartBars)
		{
			if (chartBars == null || chartBars.Count < 4)
				return null;

			int lastClosedIdx = GetLastClosedChartBarIndex(chartBars);
			if (lastClosedIdx < 2)
				return null;

			int chartCount = chartBars.Count;
			long firstBarTicks = chartBars.GetTime(0).Ticks;
			int minSampleCount;
			bool backfillReady;
			lock (profileLock)
			{
				minSampleCount = rawMinuteSamples.Count;
				backfillReady = mp1MBackfillLoaded;
			}

			int dailyHistCount;
			lock (srLock) { dailyHistCount = srDailyHistory.Count; }

			int cfgHash =
				(SignalShowSwingTriangles ? 1 : 0) |
				(SignalShowZoneReversalEntries ? 2 : 0) |
				(Math.Max(2, SignalHigherTfMultiplier) << 2) ^
				(Math.Max(1, SignalSwingLeftBars) << 6) ^
				(Math.Max(1, SignalSwingRightBars) << 11) ^
				(Math.Max(2, SignalZonePivotBarsLtf) << 16) ^
				(Math.Max(2, SignalZonePivotBarsHtf) << 21) ^
				(Math.Max(1, SignalAtrLength) << 25) ^
				((int)Math.Round(SignalZoneAtrMultiplier * 100.0) * 397) ^
				((int)Math.Round(SignalDojiMaxBodyPct * 100.0) * 7919) ^
				((int)Math.Round(SignalZoneBufferAtrRatio * 100.0) * 1543) ^
				(Math.Max(2, SignalReactionsForStrong) * 31);

			lock (signalLock)
			{
				if (sigCacheConfigHash != cfgHash ||
					lockedSignalsFirstBarTicks != firstBarTicks ||
					lastClosedIdx < lockedSignalsMaxClosedIdx)
				{
					lockedClosedBarSignals.Clear();
					lockedSignalsMaxClosedIdx = -1;
					lockedSignalsFirstBarTicks = firstBarTicks;
				}

				// Once 1M backfill is seeded, any closed bar is immutable — cache depends ONLY on lastClosedIdx & cfgHash!
				if (cachedSignalSnapshot != null &&
					sigCacheLastClosedIdx == lastClosedIdx &&
					sigCacheConfigHash == cfgHash &&
					(lockedSignalsMaxClosedIdx >= 0 || (sigCacheMinuteCount == minSampleCount && sigCacheDailyCount == dailyHistCount)))
				{
					return cachedSignalSnapshot;
				}
			}

			List<MinuteSample> minuteCopy = null;
			lock (profileLock)
			{
				if (rawMinuteSamples.Count > 0)
					minuteCopy = new List<MinuteSample>(rawMinuteSamples);
			}

			// Build Previous Trading Day High/Low (PDH / PDL) lookup per trading date (strictly closed bars only)
			SortedDictionary<DateTime, DailyHorizonSample> dailyByDate = new SortedDictionary<DateTime, DailyHorizonSample>();
			lock (srLock)
			{
				foreach (var kv in srDailyHistory)
				{
					dailyByDate[kv.Key] = new DailyHorizonSample
					{
						Date = kv.Value.Date,
						High = kv.Value.High,
						Low = kv.Value.Low,
						Close = kv.Value.Close
					};
				}
			}
			if (minuteCopy != null)
			{
				DateTime cutoffChartClose = chartBars.GetTime(lastClosedIdx);
				for (int i = 0; i < minuteCopy.Count; i++)
				{
					MinuteSample ms = minuteCopy[i];
					if (ms.Time > cutoffChartClose || ms.High <= 0 || ms.Low <= 0 || ms.High < ms.Low)
						continue;
					DateTime dDate = ResolveSrTradingDate(ms.Time);
					if (!dailyByDate.TryGetValue(dDate, out DailyHorizonSample ds))
					{
						ds = new DailyHorizonSample { Date = dDate };
						dailyByDate[dDate] = ds;
					}
					if (ms.High > ds.High) ds.High = ms.High;
					if (ms.Low < ds.Low) ds.Low = ms.Low;
					ds.Close = ms.Close;
				}
			}
			for (int i = 0; i <= lastClosedIdx; i++)
			{
				DateTime bt = chartBars.GetTime(i);
				double bh = chartBars.GetHigh(i);
				double bl = chartBars.GetLow(i);
				if (bh <= 0 || bl <= 0 || bh < bl) continue;
				DateTime dDate = ResolveSrTradingDate(bt);
				if (!dailyByDate.TryGetValue(dDate, out DailyHorizonSample ds))
				{
					ds = new DailyHorizonSample { Date = dDate };
					dailyByDate[dDate] = ds;
				}
				if (bh > ds.High) ds.High = bh;
				if (bl < ds.Low) ds.Low = bl;
				ds.Close = chartBars.GetClose(i);
			}

			Dictionary<DateTime, KeyValuePair<double, double>> prevDayHiLoByDate = new Dictionary<DateTime, KeyValuePair<double, double>>();
			DailyHorizonSample prevCompletedDay = null;
			foreach (var kv in dailyByDate)
			{
				if (prevCompletedDay != null && prevCompletedDay.High > double.MinValue && prevCompletedDay.Low < double.MaxValue)
					prevDayHiLoByDate[kv.Key] = new KeyValuePair<double, double>(prevCompletedDay.High, prevCompletedDay.Low);
				prevCompletedDay = kv.Value;
			}

			double tick = TickSize > 0 ? TickSize : 0.05;
			double eps = Math.Max(1e-9, tick * 0.05);

			BuildClosedSignalLtfAndHtfBars(
				chartBars,
				lastClosedIdx,
				minuteCopy,
				Math.Max(2, SignalHigherTfMultiplier),
				out List<SignalOhlcBar> ltfBars,
				out List<SignalOhlcBar> htfBars);

			SignalComputedSnapshot snap = new SignalComputedSnapshot { LastClosedBarIdx = lastClosedIdx };
			if (ltfBars.Count < 3)
				return snap;

			double[] ltfAtr = ComputeWilderRmaAtr(ltfBars, Math.Max(1, SignalAtrLength));
			double[] htfAtr = ComputeWilderRmaAtr(htfBars, Math.Max(1, SignalAtrLength));

			// Strictly CAUSAL fast & structural pivots (confirmed in 2-3 closed bars — ZERO backfill, ZERO negative offset!)
			int fastLeftLtf = Math.Max(3, Math.Min(7, SignalZonePivotBarsLtf / 3));
			int majLeftLtf = Math.Max(8, SignalZonePivotBarsLtf);
			int fastLeftHtf = Math.Max(3, Math.Min(6, SignalZonePivotBarsHtf / 4));
			int majLeftHtf = Math.Max(7, SignalZonePivotBarsHtf / 2);

			List<SignalRawPivot> ltfFastPivots = DetectClosedSignalPivots(ltfBars, ltfAtr, fastLeftLtf, 2, eps);
			List<SignalRawPivot> ltfMajorPivots = DetectClosedSignalPivots(ltfBars, ltfAtr, majLeftLtf, 3, eps);
			List<SignalRawPivot> htfFastPivots = DetectClosedSignalPivots(htfBars, htfAtr, fastLeftHtf, 2, eps);
			List<SignalRawPivot> htfMajorPivots = DetectClosedSignalPivots(htfBars, htfAtr, majLeftHtf, 2, eps);

			List<SignalRawPivot> allLtfPivots = new List<SignalRawPivot>(ltfFastPivots.Count + ltfMajorPivots.Count);
			allLtfPivots.AddRange(ltfFastPivots);
			allLtfPivots.AddRange(ltfMajorPivots);
			allLtfPivots.Sort((a, b) => a.ConfirmTime.CompareTo(b.ConfirmTime));

			List<SignalRawPivot> allHtfPivots = new List<SignalRawPivot>(htfFastPivots.Count + htfMajorPivots.Count);
			allHtfPivots.AddRange(htfFastPivots);
			allHtfPivots.AddRange(htfMajorPivots);
			allHtfPivots.Sort((a, b) => a.ConfirmTime.CompareTo(b.ConfirmTime));

			const int maxZonesPerSide = 12;
			double zoneAtrMult = Math.Max(0.05, SignalZoneAtrMultiplier);
			int reactionsReq = Math.Max(2, SignalReactionsForStrong);
			double dojiMaxRatio = Math.Max(0.01, Math.Min(0.35, SignalDojiMaxBodyPct));
			double bufAtrRatio = Math.Max(0.05, SignalZoneBufferAtrRatio);
			int swLookback = Math.Max(6, SignalSwingLeftBars);
			int swingCooldownBars = Math.Max(4, swLookback / 2);

			List<SignalActiveZone> supplyZones = new List<SignalActiveZone>();
			List<SignalActiveZone> demandZones = new List<SignalActiveZone>();

			int nextLtfPiv = 0;
			int nextHtfPiv = 0;
			int htfCursor = -1;

			bool demandPatternVisitActive = false;
			bool demandEntryMarkedThisVisit = false;
			bool supplyPatternVisitActive = false;
			bool supplyEntryMarkedThisVisit = false;

			bool prevIsDoji = false;
			bool prevCandleInDemand = false;
			int prevDemandStrength = 0;
			bool prevCandleInSupply = false;
			int prevSupplyStrength = 0;

			int lastSwingHighLtfIdx = -9999;
			double lastSwingHighPeak = double.MinValue;
			int lastSwingLowLtfIdx = -9999;
			double lastSwingLowTrough = double.MaxValue;
			int lastBuyEntryLtfIdx = -9999;
			double lastBuyEntryLow = double.MaxValue;
			int lastSellEntryLtfIdx = -9999;
			double lastSellEntryHigh = double.MinValue;

			// Active setup direction & candle range guard so opposite signals NEVER fire inside an active BUY/SELL setup candle
			int activeSetupDir = 0; // +1 = active BUY setup, -1 = active SELL setup
			int activeSetupLtfIdx = -9999;
			double activeSetupHigh = 0.0;
			double activeSetupLow = 0.0;
			double activeSetupClose = 0.0;

			Dictionary<int, ChartSignalMarker> computedMap = new Dictionary<int, ChartSignalMarker>();

			// Strictly forward-only causal loop: bar `i` ONLY sees data with timestamp <= `ltfBars[i].Time`
			// and ONLY writes signals onto `ltfBars[i].ChartBarIndex` itself (ZERO past-bar modification!).
			for (int i = 0; i < ltfBars.Count; i++)
			{
				SignalOhlcBar bar = ltfBars[i];
				DateTime curTime = bar.Time;
				DateTime oneYearCutoff = curTime.AddDays(-365);

				// Advance closed 9m HTF bar pointer strictly up to curTime (never reading an unclosed 9m bar!)
				while (htfCursor + 1 < htfBars.Count && htfBars[htfCursor + 1].Time <= curTime)
					htfCursor++;

				// Ingest Current-TF (3m) pivots whose confirmation bar has already closed at or before curTime
				while (nextLtfPiv < allLtfPivots.Count && allLtfPivots[nextLtfPiv].ConfirmTime <= curTime)
				{
					SignalRawPivot piv = allLtfPivots[nextLtfPiv++];
					if (piv.PivotTime >= oneYearCutoff)
					{
						if (piv.IsHigh && piv.Price >= bar.Close)
						{
							double zTop = piv.Price;
							double zBot = piv.Price - piv.Atr * zoneAtrMult;
							UpsertAndMergeSignalZone(supplyZones, zTop, zBot, piv.PivotTime, 1, maxZonesPerSide, reactionsReq, true);
						}
						else if (!piv.IsHigh && piv.Price <= bar.Close)
						{
							double zBot = piv.Price;
							double zTop = piv.Price + piv.Atr * zoneAtrMult;
							UpsertAndMergeSignalZone(demandZones, zTop, zBot, piv.PivotTime, 1, maxZonesPerSide, reactionsReq, false);
						}
					}
				}

				// Ingest Higher-TF (9m) pivots whose confirmation bar has already closed at or before curTime
				while (nextHtfPiv < allHtfPivots.Count && allHtfPivots[nextHtfPiv].ConfirmTime <= curTime)
				{
					SignalRawPivot piv = allHtfPivots[nextHtfPiv++];
					if (piv.PivotTime >= oneYearCutoff)
					{
						if (piv.IsHigh)
						{
							double zTop = piv.Price;
							double zBot = piv.Price - piv.Atr * zoneAtrMult;
							UpsertAndMergeSignalZone(supplyZones, zTop, zBot, piv.PivotTime, 2, maxZonesPerSide, reactionsReq, true);
						}
						else
						{
							double zBot = piv.Price;
							double zTop = piv.Price + piv.Atr * zoneAtrMult;
							UpsertAndMergeSignalZone(demandZones, zTop, zBot, piv.PivotTime, 2, maxZonesPerSide, reactionsReq, false);
						}
					}
				}

				supplyZones.RemoveAll(z => z.LeftTime < oneYearCutoff);
				demandZones.RemoveAll(z => z.LeftTime < oneYearCutoff);

				// Evaluate Supply Zone Flip -> Demand & rejection upgrades
				for (int s = supplyZones.Count - 1; s >= 0; s--)
				{
					SignalActiveZone sz = supplyZones[s];
					bool invalidated = bar.Close > sz.Top;
					bool overlaps = bar.High >= sz.Bottom && bar.Low <= sz.Top;
					bool rejection = overlaps && bar.Close < (sz.Top + sz.Bottom) * 0.5;

					if (invalidated)
					{
						supplyZones.RemoveAt(s);
						sz.Reactions = 0;
						sz.WasInZone = overlaps;
						sz.VeryStrong = false;
						demandZones.Add(sz);
						while (demandZones.Count > maxZonesPerSide)
							demandZones.RemoveAt(PruneSignalZoneIndex(demandZones, reactionsReq, false));
					}
					else
					{
						if (rejection && !sz.WasInZone)
							sz.Reactions++;
						sz.WasInZone = overlaps;
					}
				}

				// Evaluate Demand Zone Flip -> Supply & rejection upgrades
				for (int d = demandZones.Count - 1; d >= 0; d--)
				{
					SignalActiveZone dz = demandZones[d];
					bool invalidated = bar.Close < dz.Bottom;
					bool overlaps = bar.High >= dz.Bottom && bar.Low <= dz.Top;
					bool rejection = overlaps && bar.Close > (dz.Top + dz.Bottom) * 0.5;

					if (invalidated)
					{
						demandZones.RemoveAt(d);
						dz.Reactions = 0;
						dz.WasInZone = overlaps;
						dz.VeryStrong = false;
						supplyZones.Add(dz);
						while (supplyZones.Count > maxZonesPerSide)
							supplyZones.RemoveAt(PruneSignalZoneIndex(supplyZones, reactionsReq, true));
					}
					else
					{
						if (rejection && !dz.WasInZone)
							dz.Reactions++;
						dz.WasInZone = overlaps;
					}
				}

				double curAtr = ltfAtr[i] > 0 ? ltfAtr[i] : (tick * 10.0);
				double patternZoneBuffer = curAtr * bufAtrRatio;

				double candleRange = bar.High - bar.Low;
				double candleBody = Math.Abs(bar.Close - bar.Open);
				double lowerWick = Math.Min(bar.Open, bar.Close) - bar.Low;
				double upperWick = bar.High - Math.Max(bar.Open, bar.Close);
				bool isDoji = candleRange > 0.0 && candleBody <= candleRange * dojiMaxRatio;
				bool meaningfulBar = candleRange >= curAtr * 0.25;

				bool hasPrev = (i > 0);
				SignalOhlcBar prevBar = hasPrev ? ltfBars[i - 1] : bar;

				bool bullishEngulfing = hasPrev && meaningfulBar && bar.Close > bar.Open && prevBar.Close < prevBar.Open &&
					(bar.Close >= prevBar.Open || bar.Close > prevBar.High) && candleBody >= candleRange * 0.38;
				bool bullishHammer = meaningfulBar && bar.Close > bar.Open &&
					lowerWick >= Math.Max(candleBody * 1.4, candleRange * 0.35) && bar.Close >= bar.Low + candleRange * 0.60;

				bool bearishEngulfing = hasPrev && meaningfulBar && bar.Close < bar.Open && prevBar.Close > prevBar.Open &&
					(bar.Close <= prevBar.Open || bar.Close < prevBar.Low) && candleBody >= candleRange * 0.38;
				bool bearishRejection = meaningfulBar && bar.Close < bar.Open &&
					upperWick >= Math.Max(candleBody * 1.4, candleRange * 0.35) && bar.Close <= bar.Low + candleRange * 0.40;

				// Causal Rolling Swing High / Low Window (zero future bars!)
				int winStart = Math.Max(0, i - swLookback);
				double winHigh = bar.High;
				double winLow = bar.Low;
				for (int w = winStart; w < i; w++)
				{
					if (ltfBars[w].High > winHigh) winHigh = ltfBars[w].High;
					if (ltfBars[w].Low < winLow) winLow = ltfBars[w].Low;
				}
				double winSpan = winHigh - winLow;
				bool meaningfulSwing = winSpan >= curAtr * 1.15;
				bool atRecentPeak = (bar.High >= winHigh - eps) ||
					(i >= 1 && ltfBars[i - 1].High >= winHigh - eps) ||
					(i >= 2 && ltfBars[i - 2].High >= winHigh - eps);
				bool atRecentTrough = (bar.Low <= winLow + eps) ||
					(i >= 1 && ltfBars[i - 1].Low <= winLow + eps) ||
					(i >= 2 && ltfBars[i - 2].Low <= winLow + eps);

				bool htfBullishCtx = true;
				bool htfBearishCtx = true;
				if (htfCursor >= 0)
				{
					SignalOhlcBar hb = htfBars[htfCursor];
					double hRng = Math.Max(eps, hb.High - hb.Low);
					double hLoWick = Math.Min(hb.Open, hb.Close) - hb.Low;
					double hUpWick = hb.High - Math.Max(hb.Open, hb.Close);
					htfBullishCtx = (hb.Close >= hb.Open) || (hLoWick >= hRng * 0.32) || (bar.Close > hb.Close);
					htfBearishCtx = (hb.Close <= hb.Open) || (hUpWick >= hRng * 0.32) || (bar.Close < hb.Close);
				}

				DateTime trDate = ResolveSrTradingDate(curTime);
				bool hasPd = prevDayHiLoByDate.TryGetValue(trDate, out KeyValuePair<double, double> pdHiLo);
				double pdh = hasPd ? pdHiLo.Key : double.NaN;
				double pdl = hasPd ? pdHiLo.Value : double.NaN;

				bool nearPdh = !double.IsNaN(pdh) && (bar.High >= pdh - patternZoneBuffer && bar.Low <= pdh + patternZoneBuffer || (hasPrev && prevBar.High >= pdh - patternZoneBuffer && prevBar.Low <= pdh + patternZoneBuffer));
				bool nearPdl = !double.IsNaN(pdl) && (bar.High >= pdl - patternZoneBuffer && bar.Low <= pdl + patternZoneBuffer || (hasPrev && prevBar.High >= pdl - patternZoneBuffer && prevBar.Low <= pdl + patternZoneBuffer));
				bool priorDaySupport = (nearPdh && bar.Close >= pdh) || (nearPdl && bar.Close >= pdl);
				bool priorDayResistance = (nearPdh && bar.Close < pdh) || (nearPdl && bar.Close < pdl);

				bool candleInDemand = priorDaySupport;
				int demandStrengthScore = priorDaySupport ? 2 : 0;
				for (int d = 0; d < demandZones.Count; d++)
				{
					SignalActiveZone dz = demandZones[d];
					bool curTouches = bar.High >= dz.Bottom - patternZoneBuffer && bar.Low <= dz.Top + patternZoneBuffer;
					bool prevTouches = hasPrev && prevBar.High >= dz.Bottom - patternZoneBuffer && prevBar.Low <= dz.Top + patternZoneBuffer;
					if (curTouches || prevTouches)
					{
						candleInDemand = true;
						int zs = (dz.VeryStrong || dz.SourceMask == 3) ? 3 : (dz.Reactions >= reactionsReq || (dz.SourceMask & 2) != 0 ? 2 : 1);
						if (zs > demandStrengthScore) demandStrengthScore = zs;
					}
				}

				bool candleInSupply = priorDayResistance;
				int supplyStrengthScore = priorDayResistance ? 2 : 0;
				for (int s = 0; s < supplyZones.Count; s++)
				{
					SignalActiveZone sz = supplyZones[s];
					bool curTouches = bar.High >= sz.Bottom - patternZoneBuffer && bar.Low <= sz.Top + patternZoneBuffer;
					bool prevTouches = hasPrev && prevBar.High >= sz.Bottom - patternZoneBuffer && prevBar.Low <= sz.Top + patternZoneBuffer;
					if (curTouches || prevTouches)
					{
						candleInSupply = true;
						int zs = (sz.VeryStrong || sz.SourceMask == 3) ? 3 : (sz.Reactions >= reactionsReq || (sz.SourceMask & 2) != 0 ? 2 : 1);
						if (zs > supplyStrengthScore) supplyStrengthScore = zs;
					}
				}

				// Swing-half location & active-setup candle conflict guard:
				// - Never allow a SELL / ▼ signal inside or overlapping an active BUY signal candle's range, or at a swing trough before price extends.
				// - Never allow a BUY / ▲ signal inside or overlapping an active SELL signal candle's range, or at a swing peak before price extends.
				bool inUpperSwingHalf = (winSpan < curAtr * 0.75) || (!atRecentTrough && bar.High >= winLow + winSpan * 0.48);
				bool inLowerSwingHalf = (winSpan < curAtr * 0.75) || (!atRecentPeak && bar.Low <= winHigh - winSpan * 0.48);

				bool insideActiveBuyCandle = (activeSetupDir == 1) &&
					(bar.High <= activeSetupHigh + curAtr * 0.50 && bar.Low >= activeSetupLow - curAtr * 0.35);
				bool buySetupResolvedForSell = (activeSetupDir != 1) ||
					(!insideActiveBuyCandle && (
						(bar.High >= activeSetupClose + curAtr * 1.50 && bar.High > activeSetupHigh + curAtr * 0.50) ||
						(i - activeSetupLtfIdx >= 4 && bar.Close < activeSetupLow - curAtr * 0.35)));

				bool insideActiveSellCandle = (activeSetupDir == -1) &&
					(bar.Low >= activeSetupLow - curAtr * 0.50 && bar.High <= activeSetupHigh + curAtr * 0.35);
				bool sellSetupResolvedForBuy = (activeSetupDir != -1) ||
					(!insideActiveSellCandle && (
						(bar.Low <= activeSetupClose - curAtr * 1.50 && bar.Low < activeSetupLow - curAtr * 0.50) ||
						(i - activeSetupLtfIdx >= 4 && bar.Close > activeSetupHigh + curAtr * 0.35)));

				bool allowBullOnBar = inLowerSwingHalf && sellSetupResolvedForBuy;
				bool allowBearOnBar = inUpperSwingHalf && buySetupResolvedForSell;
				bool firedBullOnBar = false;
				bool firedBearOnBar = false;

				// 1. Causal 3m + 9m Zone / Exhaustion Reversal BUY & SELL Signals (fires IMMEDIATELY on bar `i` close!)
				if (SignalShowZoneReversalEntries)
				{
					bool freshLowExhaustion = meaningfulSwing && atRecentTrough && winSpan >= curAtr * 1.65 && htfBullishCtx;
					bool demandContext = candleInDemand || freshLowExhaustion;
					bool dojiBreakout = hasPrev && prevIsDoji && (candleInDemand || prevCandleInDemand) && bar.Close > prevBar.High && bar.Close > bar.Open;
					bool bullishEntryCandle = (demandContext && (bullishEngulfing || bullishHammer)) || dojiBreakout;

					if (!demandContext)
					{
						demandPatternVisitActive = false;
						demandEntryMarkedThisVisit = false;
					}
					else if (!demandPatternVisitActive)
					{
						demandPatternVisitActive = true;
						demandEntryMarkedThisVisit = false;
					}

					if (allowBullOnBar && bullishEntryCandle &&
						(!demandEntryMarkedThisVisit || bar.Low < lastBuyEntryLow - curAtr * 0.30) &&
						(i - lastBuyEntryLtfIdx >= 4 || bar.Low < lastBuyEntryLow - curAtr * 0.30))
					{
						demandEntryMarkedThisVisit = true;
						lastBuyEntryLtfIdx = i;
						lastBuyEntryLow = Math.Min(bar.Low, prevBar.Low);
						activeSetupDir = 1;
						activeSetupLtfIdx = i;
						activeSetupLow = Math.Min(bar.Low, prevBar.Low);
						activeSetupHigh = Math.Max(bar.High, prevBar.High);
						activeSetupClose = bar.Close;
						firedBullOnBar = true;
						if (bar.ChartBarIndex >= 0)
						{
							int effStr = demandStrengthScore > 0 ? demandStrengthScore : (dojiBreakout ? Math.Max(1, prevDemandStrength) : 2);
							ChartSignalMarker m = GetOrAddChartSignalMarker(computedMap, bar.ChartBarIndex);
							m.HasBullishEntry = true;
							m.BullishStrength = effStr;
							m.BullishPatternShort = bullishEngulfing ? "ENGULF" : (bullishHammer ? "HAMMER" : "DOJI");
							m.BullishPatternFull = bullishEngulfing ? "BUY · ENGULF" : (bullishHammer ? "BUY · HAMMER" : "BUY · DOJI");
						}
					}

					bool freshHighExhaustion = meaningfulSwing && atRecentPeak && winSpan >= curAtr * 1.65 && htfBearishCtx;
					bool supplyContext = candleInSupply || freshHighExhaustion;
					bool dojiBreakdown = hasPrev && prevIsDoji && (candleInSupply || prevCandleInSupply) && bar.Close < prevBar.Low && bar.Close < bar.Open;
					bool bearishEntryCandle = (supplyContext && (bearishEngulfing || bearishRejection)) || dojiBreakdown;

					if (!supplyContext)
					{
						supplyPatternVisitActive = false;
						supplyEntryMarkedThisVisit = false;
					}
					else if (!supplyPatternVisitActive)
					{
						supplyPatternVisitActive = true;
						supplyEntryMarkedThisVisit = false;
					}

					if (!firedBullOnBar && allowBearOnBar && bearishEntryCandle &&
						(!supplyEntryMarkedThisVisit || bar.High > lastSellEntryHigh + curAtr * 0.30) &&
						(i - lastSellEntryLtfIdx >= 4 || bar.High > lastSellEntryHigh + curAtr * 0.30))
					{
						supplyEntryMarkedThisVisit = true;
						lastSellEntryLtfIdx = i;
						lastSellEntryHigh = Math.Max(bar.High, prevBar.High);
						activeSetupDir = -1;
						activeSetupLtfIdx = i;
						activeSetupLow = Math.Min(bar.Low, prevBar.Low);
						activeSetupHigh = Math.Max(bar.High, prevBar.High);
						activeSetupClose = bar.Close;
						firedBearOnBar = true;
						if (bar.ChartBarIndex >= 0)
						{
							int effStr = supplyStrengthScore > 0 ? supplyStrengthScore : (dojiBreakdown ? Math.Max(1, prevSupplyStrength) : 2);
							ChartSignalMarker m = GetOrAddChartSignalMarker(computedMap, bar.ChartBarIndex);
							m.HasBearishEntry = true;
							m.BearishStrength = effStr;
							m.BearishPatternShort = bearishEngulfing ? "ENGULF" : (bearishRejection ? "STAR" : "DOJI");
							m.BearishPatternFull = bearishEngulfing ? "SELL · ENGULF" : (bearishRejection ? "SELL · STAR" : "SELL · DOJI");
						}
					}
				}

				// 2. Causal Swing Turn ▼ / ▲ Triangles (fires IMMEDIATELY on bar `i` close — ZERO back-plotting, never conflicting with active opposite setup!)
				if (SignalShowSwingTriangles)
				{
					bool bearTurnConfirm = bar.Close < bar.Open && meaningfulBar &&
						(bearishEngulfing || bearishRejection || (hasPrev && bar.Close < prevBar.Low && candleBody >= candleRange * 0.45));
					if (!firedBullOnBar && allowBearOnBar && meaningfulSwing && atRecentPeak && bearTurnConfirm &&
						(i - lastSwingHighLtfIdx >= swingCooldownBars || winHigh > lastSwingHighPeak + curAtr * 0.35))
					{
						lastSwingHighLtfIdx = i;
						lastSwingHighPeak = winHigh;
						firedBearOnBar = true;
						if (bar.ChartBarIndex >= 0)
						{
							ChartSignalMarker m = GetOrAddChartSignalMarker(computedMap, bar.ChartBarIndex);
							m.HasSwingHigh = true;
							m.SwingHighHtfConfluence = htfBearishCtx && candleInSupply;
						}
					}

					bool bullTurnConfirm = bar.Close > bar.Open && meaningfulBar &&
						(bullishEngulfing || bullishHammer || (hasPrev && bar.Close > prevBar.High && candleBody >= candleRange * 0.45));
					if (!firedBearOnBar && allowBullOnBar && meaningfulSwing && atRecentTrough && bullTurnConfirm &&
						(i - lastSwingLowLtfIdx >= swingCooldownBars || winLow < lastSwingLowTrough - curAtr * 0.35))
					{
						lastSwingLowLtfIdx = i;
						lastSwingLowTrough = winLow;
						if (bar.ChartBarIndex >= 0)
						{
							ChartSignalMarker m = GetOrAddChartSignalMarker(computedMap, bar.ChartBarIndex);
							m.HasSwingLow = true;
							m.SwingLowHtfConfluence = htfBullishCtx && candleInDemand;
						}
					}
				}

				prevIsDoji = isDoji;
				prevCandleInDemand = candleInDemand;
				prevDemandStrength = demandStrengthScore;
				prevCandleInSupply = candleInSupply;
				prevSupplyStrength = supplyStrengthScore;
			}

			// Permanently lock every closed chart bar's signal decision once backfill is ready so NO closed bar can ever repaint!
			lock (signalLock)
			{
				if (lockedSignalsMaxClosedIdx >= 0)
				{
					// Preserve 100% of previously closed bars untouched; only append newly closed bars (lockedSignalsMaxClosedIdx + 1 .. lastClosedIdx)!
					foreach (var kv in lockedClosedBarSignals)
						snap.ByBar[kv.Key] = kv.Value;

					for (int bIdx = lockedSignalsMaxClosedIdx + 1; bIdx <= lastClosedIdx; bIdx++)
					{
						if (computedMap.TryGetValue(bIdx, out ChartSignalMarker newSig))
						{
							lockedClosedBarSignals[bIdx] = newSig;
							snap.ByBar[bIdx] = newSig;
						}
					}
					lockedSignalsMaxClosedIdx = lastClosedIdx;
				}
				else
				{
					foreach (var kv in computedMap)
						snap.ByBar[kv.Key] = kv.Value;

					if (backfillReady || State == State.Realtime)
					{
						lockedClosedBarSignals.Clear();
						foreach (var kv in computedMap)
							lockedClosedBarSignals[kv.Key] = kv.Value;
						lockedSignalsMaxClosedIdx = lastClosedIdx;
					}
				}

				cachedSignalSnapshot = snap;
				sigCacheChartBarCount = chartCount;
				sigCacheLastClosedIdx = lastClosedIdx;
				sigCacheMinuteCount = minSampleCount;
				sigCacheDailyCount = dailyHistCount;
				sigCacheConfigHash = cfgHash;
			}
			return snap;
		}

		private void DrawFilledSignalTriangle(float tipX, float tipY, float halfBaseW, float triH, bool pointDown, DxBrush fillBrush, DxBrush outlineBrush, float outlineWidth = 1.05f)
		{
			if (RenderTarget == null || fillBrush == null)
				return;

			float baseY = pointDown ? (tipY - triH) : (tipY + triH);
			using (var geo = new PathGeometry(Globals.D2DFactory))
			{
				using (GeometrySink sink = geo.Open())
				{
					sink.BeginFigure(new Vector2(tipX, tipY), FigureBegin.Filled);
					sink.AddLine(new Vector2(tipX - halfBaseW, baseY));
					sink.AddLine(new Vector2(tipX + halfBaseW, baseY));
					sink.EndFigure(FigureEnd.Closed);
					sink.Close();
				}
				RenderTarget.FillGeometry(geo, fillBrush);
				if (outlineBrush != null && outlineWidth > 0f)
					RenderTarget.DrawGeometry(geo, outlineBrush, outlineWidth);
			}
		}

		private void RenderChartSignalsOnCanvas(
			ChartControl chartControl,
			Func<double, float> priceToY,
			List<FootprintBarData> visibleBars,
			float barSpacing,
			bool isCandlestickZoom,
			float panelX,
			float canvasTopY,
			float candleClipW,
			float chartBottomY,
			bool light)
		{
			if (chartControl == null || ChartBars == null || ChartBars.Bars == null || visibleBars == null || visibleBars.Count == 0)
				return;

			SignalComputedSnapshot snap = EnsureChartSignalsComputed(ChartBars.Bars);
			if (snap == null || snap.ByBar.Count == 0)
				return;

			float clipRight = panelX + candleClipW;
			float triHalfW = isCandlestickZoom ? Math.Max(4.2f, Math.Min(5.8f, barSpacing * 0.36f)) : 6.4f;
			float triH = isCandlestickZoom ? Math.Max(6.0f, Math.Min(8.0f, triHalfW * 1.38f)) : 8.6f;

			var dw = Globals.DirectWriteFactory;
			using (var swingHighFill = new DxBrush(RenderTarget, new SharpDX.Color(230, 35, 140, 255))) // Exact PineScript color.rgb(230, 35, 140)
			using (var swingLowFill = new DxBrush(RenderTarget, light ? new SharpDX.Color(12, 152, 74, 255) : new SharpDX.Color(16, 204, 112, 255)))
			using (var triOutline = new DxBrush(RenderTarget, light ? new SharpDX.Color(255, 255, 255, 235) : new SharpDX.Color(10, 15, 24, 235)))
			using (var htfHaloHigh = new DxBrush(RenderTarget, new SharpDX.Color(244, 114, 182, 220)))
			using (var htfHaloLow = new DxBrush(RenderTarget, new SharpDX.Color(52, 211, 153, 220)))
			using (var bullStrongFill = new DxBrush(RenderTarget, new SharpDX.Color(6, 95, 70, 246)))
			using (var bullNormFill = new DxBrush(RenderTarget, new SharpDX.Color(5, 150, 105, 242)))
			using (var bullBorder = new DxBrush(RenderTarget, new SharpDX.Color(52, 211, 153, 245)))
			using (var bearStrongFill = new DxBrush(RenderTarget, new SharpDX.Color(127, 29, 29, 246)))
			using (var bearNormFill = new DxBrush(RenderTarget, new SharpDX.Color(220, 38, 38, 242)))
			using (var bearBorder = new DxBrush(RenderTarget, new SharpDX.Color(251, 113, 133, 245)))
			using (var pillTxtWhite = new DxBrush(RenderTarget, new SharpDX.Color(255, 255, 255, 255)))
			using (var tfSig = new DxTextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.ExtraBold, SharpDX.DirectWrite.FontStyle.Normal, isCandlestickZoom ? 9.2f : 9.8f))
			{
				tfSig.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfSig.ParagraphAlignment = ParagraphAlignment.Center;
				tfSig.WordWrapping = WordWrapping.NoWrap;

				float lastTopPillRightX = -9999f;
				float lastBotPillRightX = -9999f;

				for (int i = 0; i < visibleBars.Count; i++)
				{
					FootprintBarData fp = visibleBars[i];
					// Strictly enforce: NEVER draw any signal on a running (unclosed) candle!
					if (fp.BarIndex > snap.LastClosedBarIdx)
						continue;

					if (!snap.ByBar.TryGetValue(fp.BarIndex, out ChartSignalMarker sig))
						continue;

					float cx = (float)chartControl.GetXByBarIndex(ChartBars, fp.BarIndex);
					if (cx < panelX - 20f || cx > clipRight + 20f)
						continue;

					double highEdge = fp.High;
					double lowEdge = fp.Low;
					if (!isCandlestickZoom && fp.Levels != null && fp.Levels.Count > 0)
					{
						double hs = fp.EffectivePriceStep * 0.5;
						lowEdge = Math.Min(lowEdge, fp.Levels[0].Price - hs);
						highEdge = Math.Max(highEdge, fp.Levels[fp.Levels.Count - 1].Price + hs);
					}

					float yHigh = priceToY(highEdge);
					float yLow = priceToY(lowEdge);
					float topClearance = (!isCandlestickZoom && ShowBarDelta && barSpacing >= 46f) ? 21.5f : 5.0f;
					float botClearance = (!isCandlestickZoom && ShowBarDelta && barSpacing >= 46f) ? 21.5f : 5.0f;

					float curTopTipY = yHigh - topClearance;
					float curBotTipY = yLow + botClearance;

					// 1A. Confirmed Swing High ▼ (Magenta-Pink — skipped if this bar already renders a SELL Entry arrow+pill)
					if (sig.HasSwingHigh && !sig.HasBearishEntry)
					{
						float clampedTipY = Math.Max(canvasTopY + triH + 2f, Math.Min(chartBottomY - 4f, curTopTipY));
						DrawFilledSignalTriangle(cx, clampedTipY, triHalfW, triH, true, swingHighFill, sig.SwingHighHtfConfluence ? htfHaloHigh : triOutline, sig.SwingHighHtfConfluence ? 1.45f : 1.0f);
						curTopTipY = clampedTipY - triH - 4f;
					}

					// 1B. Confirmed Swing Low ▲ (Signal Green — skipped if this bar already renders a BUY Entry arrow+pill)
					if (sig.HasSwingLow && !sig.HasBullishEntry)
					{
						float clampedTipY = Math.Min(chartBottomY - triH - 2f, Math.Max(canvasTopY + 4f, curBotTipY));
						DrawFilledSignalTriangle(cx, clampedTipY, triHalfW, triH, false, swingLowFill, sig.SwingLowHtfConfluence ? htfHaloLow : triOutline, sig.SwingLowHtfConfluence ? 1.45f : 1.0f);
						curBotTipY = clampedTipY + triH + 4f;
					}

					// 2A. Bearish 3m/9m Supply Zone Reversal Entry Signal (Immediate on Closed Candle!)
					if (sig.HasBearishEntry)
					{
						float arrowTipY = Math.Max(canvasTopY + 22f, Math.Min(chartBottomY - 6f, curTopTipY));
						DxBrush fillBr = sig.BearishStrength >= 2 ? bearStrongFill : bearNormFill;
						DrawFilledSignalTriangle(cx, arrowTipY, 4.6f, 5.8f, true, bearBorder, triOutline, 0.9f);

						string lbl = barSpacing >= 54f
							? (sig.BearishStrength >= 3 ? ("★ " + sig.BearishPatternFull) : sig.BearishPatternFull)
							: (barSpacing >= 24f ? ("▼ " + sig.BearishPatternShort) : "SELL");
						float pw = Math.Max(34f, lbl.Length * 5.8f + 12f);
						float ph = 15.5f;
						float px = Math.Max(panelX + 3f, Math.Min(clipRight - pw - 3f, cx - pw * 0.5f));
						float py = Math.Max(canvasTopY + 2f, arrowTipY - 6.8f - ph);

						if (px > lastTopPillRightX + 3f)
						{
							var pillRect = new RectangleF(px, py, pw, ph);
							var pillRr = new RoundedRectangle { Rect = pillRect, RadiusX = 3.2f, RadiusY = 3.2f };
							RenderTarget.FillRoundedRectangle(pillRr, fillBr);
							RenderTarget.DrawRoundedRectangle(pillRr, bearBorder, 1.05f);
							RenderTarget.DrawText(lbl, tfSig, pillRect, pillTxtWhite);
							lastTopPillRightX = px + pw;
						}
					}

					// 2B. Bullish 3m/9m Demand Zone Reversal Entry Signal (Immediate on Closed Candle!)
					if (sig.HasBullishEntry)
					{
						float arrowTipY = Math.Min(chartBottomY - 22f, Math.Max(canvasTopY + 6f, curBotTipY));
						DxBrush fillBr = sig.BullishStrength >= 2 ? bullStrongFill : bullNormFill;
						DrawFilledSignalTriangle(cx, arrowTipY, 4.6f, 5.8f, false, bullBorder, triOutline, 0.9f);

						string lbl = barSpacing >= 54f
							? (sig.BullishStrength >= 3 ? ("★ " + sig.BullishPatternFull) : sig.BullishPatternFull)
							: (barSpacing >= 24f ? ("▲ " + sig.BullishPatternShort) : "BUY");
						float pw = Math.Max(32f, lbl.Length * 5.8f + 12f);
						float ph = 15.5f;
						float px = Math.Max(panelX + 3f, Math.Min(clipRight - pw - 3f, cx - pw * 0.5f));
						float py = Math.Min(chartBottomY - ph - 2f, arrowTipY + 6.8f);

						if (px > lastBotPillRightX + 3f)
						{
							var pillRect = new RectangleF(px, py, pw, ph);
							var pillRr = new RoundedRectangle { Rect = pillRect, RadiusX = 3.2f, RadiusY = 3.2f };
							RenderTarget.FillRoundedRectangle(pillRr, fillBr);
							RenderTarget.DrawRoundedRectangle(pillRr, bullBorder, 1.05f);
							RenderTarget.DrawText(lbl, tfSig, pillRect, pillTxtWhite);
							lastBotPillRightX = px + pw;
						}
					}
				}
			}
		}
		#endregion

		private void RenderCleanCandlesticks(
			ChartControl chartControl,
			Func<double, float> priceToY,
			List<FootprintBarData> visibleBars,
			float barSpacing,
			float panelX,
			float clipW,
			DxBrush brushBuyer,
			DxBrush brushSeller)
		{
			float bodyHalfW = Math.Max(1.3f, Math.Min(13f, (float)Math.Floor(barSpacing * 0.35f)));
			float wickWidth = barSpacing >= 10f ? 1.8f : 1.2f;
			float clipRight = panelX + clipW;

			for (int i = 0; i < visibleBars.Count; i++)
			{
				FootprintBarData fp = visibleBars[i];
				float cx = (float)chartControl.GetXByBarIndex(ChartBars, fp.BarIndex);
				if (cx + bodyHalfW < panelX || cx - bodyHalfW > clipRight)
					continue;

				float yHigh = priceToY(fp.High);
				float yLow = priceToY(fp.Low);
				float yOpen = priceToY(fp.Open);
				float yClose = priceToY(fp.Close);

				bool isBuyer = fp.Close >= fp.Open;
				DxBrush brush = isBuyer ? brushBuyer : brushSeller;

				RenderTarget.DrawLine(new Vector2(cx, yHigh), new Vector2(cx, yLow), brush, wickWidth);

				float top = Math.Min(yOpen, yClose);
				float h = Math.Max(2.5f, Math.Abs(yClose - yOpen));
				var bodyRect = new RectangleF(cx - bodyHalfW, top, bodyHalfW * 2f, h);
				RenderTarget.FillRectangle(bodyRect, brush);
			}
		}

		private void RenderInsideBoxCandleFootprints(
			ChartControl chartControl,
			Func<double, float> priceToY,
			List<FootprintBarData> visibleBars,
			float barSpacing,
			float rawRowH,
			float panelX,
			float panelY,
			float clipW,
			float chartBottomY,
			DxBrush brushBuyerCandle,
			DxBrush brushSellerCandle,
			DxBrush brushBoxBg,
			DxBrush brushSpineLaneBg,
			DxBrush brushBoxBorder,
			DxBrush brushRowGrid,
			DxBrush brushAskImbBlock,
			DxBrush brushBidImbBlock,
			DxBrush brushImbWhiteText,
			DxBrush brushNormalText,
			DxBrush brushZeroText,
			DxBrush brushPocOutline,
			DxBrush brushHalfpackLine,
			DxBrush brushAskVolShade,
			DxBrush brushBidVolShade,
			DxBrush brushDeltaBadgePosBg,
			DxBrush brushDeltaBadgeNegBg,
			DxBrush brushHeightBadgeBg,
			DxBrush brushHeightBadgeBorder,
			DxBrush brushHeightBadgeText)
		{
			bool isTwoSide = (runtimeOrderflowMode == AK10XOrderflowMode.Imbalance || runtimeOrderflowMode == AK10XOrderflowMode.VolumeFootprint);
			float rowH = Math.Max(8f, Math.Min(28f, rawRowH));

			float candleBodyHalfW = Math.Max(2.2f, Math.Min(3.4f, barSpacing * 0.032f));
			float centerLaneHalfW = candleBodyHalfW + 2.6f;
			float approxSideW = Math.Max(12f, Math.Min(48f, (barSpacing * 0.46f) - centerLaneHalfW));

			float fontSize = Math.Max(8.6f, Math.Min(11.6f, Math.Min(rowH * 0.64f, (isTwoSide ? approxSideW : approxSideW * 1.8f) * 0.34f)));
			float clipRight = panelX + clipW;

			var sharedDw = Globals.DirectWriteFactory;
			using (var tfReg = new DxTextFormat(sharedDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.SemiBold, SharpDX.DirectWrite.FontStyle.Normal, fontSize))
			using (var tfBold = new DxTextFormat(sharedDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, fontSize))
			using (var tfBadge = new DxTextFormat(sharedDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, Math.Max(8.5f, Math.Min(10.2f, fontSize * 0.94f))))
			using (var brushFocusGlow = new DxBrush(RenderTarget, new Color4(0.08f, 0.78f, 0.95f, 0.16f)))
			using (var brushFocusBorder = new DxBrush(RenderTarget, new Color4(0.14f, 0.84f, 0.98f, 0.96f)))
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
					if (rightBound < panelX || leftBound > clipRight || fp.Levels.Count == 0)
						continue;

					float curSideW = Math.Max(10f, Math.Min(48f, ((rightBound - leftBound) * 0.5f) - centerLaneHalfW - 1.5f));
					float boxLeft = (float)Math.Round(cx - centerLaneHalfW - curSideW);
					float centerLeft = (float)Math.Round(cx - centerLaneHalfW);
					float centerRight = (float)Math.Round(cx + centerLaneHalfW);
					float boxRight = (float)Math.Round(cx + centerLaneHalfW + curSideW);

					float step = (float)fp.EffectivePriceStep;
					float halfStep = step * 0.5f;
					float barRawRowH = Math.Abs(priceToY(fp.Close) - priceToY(fp.Close + step));
					bool showBarNumbers = (barSpacing >= 50f && barRawRowH >= 8.5f);

					float t0 = (float)Math.Round(priceToY(fp.Levels[fp.Levels.Count - 1].Price + halfStep));
					float b0 = (float)Math.Round(priceToY(fp.Levels[0].Price - halfStep));
					float barTopY = Math.Min(t0, b0);
					float barBotY = Math.Max(t0, b0);
					float boxH = Math.Max(4f, barBotY - barTopY);

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

							if (showBarNumbers)
							{
								var leftTxtRect = new RectangleF(boxLeft, yTop, centerLeft - boxLeft, cellH);
								var rightTxtRect = new RectangleF(centerRight, yTop, boxRight - centerRight, cellH);

								DxBrush bidFg = bidImb ? brushImbWhiteText : (lv.BidVolume == 0 ? brushZeroText : brushNormalText);
								DxBrush askFg = askImb ? brushImbWhiteText : (lv.AskVolume == 0 ? brushZeroText : brushNormalText);

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
							if (showBarNumbers)
							{
								string dStr = (lv.Delta > 0 ? "+" : "") + FormatGoChartingVol(lv.Delta);
								DxBrush fg = hasImb ? brushImbWhiteText : (lv.Delta > 0 ? brushBuyerCandle : (lv.Delta < 0 ? brushSellerCandle : brushZeroText));
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
							if (showBarNumbers)
							{
								RenderTarget.DrawText(FormatGoChartingVol(lv.TotalVolume), lv.IsPoc ? tfBold : tfReg, new RectangleF(centerRight, yTop, boxRight - centerRight, cellH), brushNormalText);
								string dStr = (lv.Delta > 0 ? "+" : "") + FormatGoChartingVol(lv.Delta);
								RenderTarget.DrawText(dStr, tfReg, new RectangleF(boxLeft, yTop, centerLeft - boxLeft, cellH), lv.Delta >= 0 ? brushBuyerCandle : brushSellerCandle);
							}
						}
					}

					RenderTarget.DrawLine(new Vector2(centerLeft, barTopY), new Vector2(centerLeft, barBotY), brushBoxBorder, 0.9f);
					RenderTarget.DrawLine(new Vector2(centerRight, barTopY), new Vector2(centerRight, barBotY), brushBoxBorder, 0.9f);
					RenderTarget.DrawRectangle(outerBoxRect, brushBoxBorder, 1.0f);

					if (fp.BarIndex == focusedCandleBarIndex)
					{
						var focusRect = new RectangleF(outerBoxRect.X - 2f, outerBoxRect.Y - 2f, outerBoxRect.Width + 4f, outerBoxRect.Height + 4f);
						var focusRr = new RoundedRectangle { Rect = focusRect, RadiusX = 3f, RadiusY = 3f };
						RenderTarget.DrawRoundedRectangle(focusRr, brushFocusGlow, 4.2f);
						RenderTarget.DrawRoundedRectangle(focusRr, brushFocusBorder, 1.75f);
					}

					bool isBuyerCandle = fp.Close >= fp.Open;
					DxBrush vividCandleBrush = isBuyerCandle ? brushBuyerCandle : brushSellerCandle;

					float yHigh = Math.Max(barTopY + 1f, Math.Min(barBotY - 1f, priceToY(fp.High)));
					float yLow = Math.Max(barTopY + 1f, Math.Min(barBotY - 1f, priceToY(fp.Low)));
					float yOpen = Math.Max(barTopY + 1f, Math.Min(barBotY - 1f, priceToY(fp.Open)));
					float yClose = Math.Max(barTopY + 1f, Math.Min(barBotY - 1f, priceToY(fp.Close)));

					RenderTarget.DrawLine(new Vector2(cx, yHigh), new Vector2(cx, yLow), vividCandleBrush, 2.0f);

					float bodyTop = Math.Min(yOpen, yClose);
					float bodyH = Math.Max(3.5f, Math.Abs(yClose - yOpen));
					var insideCandleRect = new RectangleF(cx - candleBodyHalfW, bodyTop, candleBodyHalfW * 2f, bodyH);
					RenderTarget.FillRectangle(insideCandleRect, vividCandleBrush);

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
								float x1 = showBarNumbers ? centerLeft : boxLeft;
								float x2 = showBarNumbers ? centerRight : boxRight;
								RenderTarget.DrawLine(new Vector2(x1, yHp), new Vector2(x2, yHp), brushHalfpackLine, 2.2f);
							}
						}
					}

					if (ShowBarDelta && barSpacing >= 46f)
					{
						float pillW = Math.Min(boxRight - boxLeft, Math.Max(36f, (boxRight - boxLeft) * 0.78f));

						if (barTopY - 17.5f >= panelY + 2f)
						{
							string fdStr = (fp.Delta > 0 ? "+" : "") + FormatGoChartingVol(fp.Delta);
							var topPillRect = new RectangleF(cx - pillW * 0.5f, barTopY - 17.5f, pillW, 14.5f);
							var topPillRr = new RoundedRectangle { Rect = topPillRect, RadiusX = 3f, RadiusY = 3f };
							RenderTarget.FillRoundedRectangle(topPillRr, fp.Delta >= 0 ? brushDeltaBadgePosBg : brushDeltaBadgeNegBg);
							RenderTarget.DrawText(fdStr, tfBadge, topPillRect, fp.Delta >= 0 ? brushBuyerCandle : brushSellerCandle);
						}

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
			if (isGreenCandle)
			{
				if (hasBuyImbBelow && hasBuyImbAbove)
				{
					strengthLabel = "Buyer";
					strengthTone = 1;
				}
				else if (!hasBuyImbBelow && hasBuyImbAbove)
				{
					strengthLabel = "Buyer";
					strengthTone = -1;
				}
			}
			else
			{
				if (hasSellImbAbove && hasSellImbBelow)
				{
					strengthLabel = "Seller";
					strengthTone = -1;
				}
				else if (!hasSellImbAbove && hasSellImbBelow)
				{
					strengthLabel = "Seller";
					strengthTone = 1;
				}
			}

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
						momentumTone = 1;
					}
				}
				else if (!isGreenCandle && hasSellImbAbove && hasSellImbBelow)
				{
					double lowerWick = Math.Max(0.0, Math.Min(fp.Open, fp.Close) - fp.Low);
					if (lowerWick <= barRange * maxWickRatio + 1e-9)
					{
						momentumLabel = "SMC";
						momentumTone = -1;
					}
				}
			}
		}

		private void RenderThreeRowBottomTable(
			ChartControl chartControl,
			List<FootprintBarData> visibleBars,
			float panelX,
			float panelW,
			float rightBarClipX,
			bool alignRightLane,
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

			float panelRight = panelX + panelW;
			float titleColLeft;
			float titleColW;
			if (alignRightLane && panelRight - rightBarClipX > 40f)
			{
				titleColLeft = rightBarClipX + 1f;
				titleColW = Math.Max(40f, panelRight - titleColLeft - 1f);
			}
			else
			{
				titleColW = Math.Min(82f, Math.Max(60f, panelW * 0.08f));
				titleColLeft = (float)Math.Round(panelRight - titleColW - 1f);
			}
			float cellClipRight = Math.Min(titleColLeft - 1f, rightBarClipX);
			double maxWickRatio = Math.Max(0.05, Math.Min(0.45, MomentumMaxWickPct / 100.0));

			var dw = Globals.DirectWriteFactory;
			float fontSz = Math.Max(8.4f, Math.Min(10.2f, Math.Min(rowH * 0.56f, barSpacing * 0.13f)));
			using (var tfCell = new DxTextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, fontSz))
			using (var tfTitle = new DxTextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.ExtraBold, SharpDX.DirectWrite.FontStyle.Normal, 9.8f))
			using (var stripBg = new DxBrush(RenderTarget, light ? new Color4(0.95f, 0.965f, 0.98f, 0.98f) : new Color4(0.031f, 0.055f, 0.098f, 0.98f)))
			using (var gridLineBrush = new DxBrush(RenderTarget, light ? new Color4(0.75f, 0.80f, 0.86f, 0.95f) : new Color4(0.220f, 0.741f, 0.973f, 0.30f)))
			using (var sepBrush = new DxBrush(RenderTarget, light ? new Color4(0.84f, 0.88f, 0.92f, 0.90f) : new Color4(0.085f, 0.135f, 0.220f, 0.90f)))
			using (var titleBgBrush = new DxBrush(RenderTarget, light ? new Color4(0.88f, 0.92f, 0.96f, 0.99f) : new Color4(0.047f, 0.094f, 0.173f, 0.99f)))
			using (var titleBorderBrush = new DxBrush(RenderTarget, light ? new Color4(0.45f, 0.54f, 0.66f, 0.95f) : new Color4(0.220f, 0.741f, 0.973f, 0.55f)))
			using (var titleAccentBrush = new DxBrush(RenderTarget, light ? new Color4(0.05f, 0.48f, 0.66f, 1.0f) : new Color4(0.220f, 0.741f, 0.973f, 0.92f)))
			using (var titleTextBrush = new DxBrush(RenderTarget, light ? new Color4(0.08f, 0.13f, 0.20f, 1.0f) : new Color4(0.90f, 0.96f, 1.0f, 1.0f)))
			using (var greenSignalBg = new DxBrush(RenderTarget, new Color4(0.063f, 0.660f, 0.455f, 0.96f)))
			using (var redSignalBg = new DxBrush(RenderTarget, new Color4(0.885f, 0.210f, 0.295f, 0.96f)))
			using (var txtWhite = new DxBrush(RenderTarget, new Color4(1.0f, 1.0f, 1.0f, 1.0f)))
			using (var txtDark = new DxBrush(RenderTarget, new Color4(0.08f, 0.12f, 0.16f, 1.0f)))
			using (var cellBrush = new DxBrush(RenderTarget, new Color4(0.1f, 0.6f, 0.3f, 1.0f)))
			{
				tfCell.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfCell.ParagraphAlignment = ParagraphAlignment.Center;
				tfCell.WordWrapping = WordWrapping.NoWrap;

				tfTitle.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfTitle.ParagraphAlignment = ParagraphAlignment.Center;
				tfTitle.WordWrapping = WordWrapping.NoWrap;

				RenderTarget.FillRectangle(new RectangleF(panelX, stripTopY, panelW, stripH), stripBg);

				RenderTarget.PushAxisAlignedClip(new RectangleF(panelX, stripTopY, Math.Max(10f, cellClipRight - panelX), stripH), AntialiasMode.PerPrimitive);
				try
				{
					for (int i = 0; i < visibleBars.Count; i++)
					{
						FootprintBarData fp = visibleBars[i];
						float cx = (float)chartControl.GetXByBarIndex(ChartBars, fp.BarIndex);
						float halfSpan = Math.Max(2f, barSpacing * 0.5f);
						float cLeft = (float)Math.Round(cx - halfSpan);
						float cRight = (float)Math.Round(cx + halfSpan);
						if (cRight < panelX || cLeft > cellClipRight)
							continue;

						float cWidth = Math.Max(2f, cRight - cLeft - 1f);

						EvaluateHalfpackSignals(fp, maxWickRatio,
							out string strengthLabel, out int strengthTone,
							out string momentumLabel, out int momentumTone);

						if (momentumTone != 0)
						{
							var momRect = new RectangleF(cLeft, yMom + 1f, cWidth, rowH - 1.5f);
							RenderTarget.FillRectangle(momRect, momentumTone > 0 ? greenSignalBg : redSignalBg);
							if (cWidth >= 28f)
								RenderTarget.DrawText(momentumLabel, tfCell, momRect, txtWhite);
							else if (cWidth >= 16f)
								RenderTarget.DrawText(momentumTone > 0 ? "BM" : "SM", tfCell, momRect, txtWhite);
						}

						if (strengthTone != 0)
						{
							var strRect = new RectangleF(cLeft, yStr + 1f, cWidth, rowH - 1.5f);
							RenderTarget.FillRectangle(strRect, strengthTone > 0 ? greenSignalBg : redSignalBg);
							if (cWidth >= 36f)
								RenderTarget.DrawText(strengthLabel, tfCell, strRect, txtWhite);
							else if (cWidth >= 16f)
								RenderTarget.DrawText(strengthLabel.StartsWith("B", StringComparison.Ordinal) ? "B" : "S", tfCell, strRect, txtWhite);
						}

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

						RenderTarget.DrawLine(new Vector2(cRight, stripTopY), new Vector2(cRight, stripTopY + stripH), sepBrush, 1.0f);
					}
				}
				finally
				{
					RenderTarget.PopAxisAlignedClip();
				}

				RenderTarget.DrawLine(new Vector2(panelX, stripTopY), new Vector2(panelX + panelW, stripTopY), gridLineBrush, 1.0f);
				RenderTarget.DrawLine(new Vector2(panelX, yStr), new Vector2(panelX + panelW, yStr), gridLineBrush, 1.0f);
				RenderTarget.DrawLine(new Vector2(panelX, yDel), new Vector2(panelX + panelW, yDel), gridLineBrush, 1.0f);
				RenderTarget.DrawLine(new Vector2(panelX, stripTopY + stripH), new Vector2(panelX + panelW, stripTopY + stripH), gridLineBrush, 1.0f);

				var titleBlockRect = new RectangleF(titleColLeft + 1f, stripTopY, Math.Max(20f, titleColW - 2f), stripH);
				var titleBlockRr = new RoundedRectangle { Rect = titleBlockRect, RadiusX = 4.5f, RadiusY = 4.5f };
				RenderTarget.FillRoundedRectangle(titleBlockRr, titleBgBrush);
				RenderTarget.DrawRoundedRectangle(titleBlockRr, titleBorderBrush, 1.15f);
				RenderTarget.FillRectangle(new RectangleF(titleBlockRect.X + 1.5f, yMom + 3f, 2.5f, Math.Max(4f, rowH - 6f)), titleAccentBrush);
				RenderTarget.FillRectangle(new RectangleF(titleBlockRect.X + 1.5f, yStr + 3f, 2.5f, Math.Max(4f, rowH - 6f)), titleAccentBrush);
				RenderTarget.FillRectangle(new RectangleF(titleBlockRect.X + 1.5f, yDel + 3f, 2.5f, Math.Max(4f, lastRowH - 6f)), titleAccentBrush);
				RenderTarget.DrawLine(new Vector2(titleBlockRect.X + 4f, yStr), new Vector2(titleBlockRect.Right - 4f, yStr), titleBorderBrush, 0.9f);
				RenderTarget.DrawLine(new Vector2(titleBlockRect.X + 4f, yDel), new Vector2(titleBlockRect.Right - 4f, yDel), titleBorderBrush, 0.9f);

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

		private void RenderStackedImbalanceZones(
			ChartControl chartControl,
			Func<double, float> priceToY,
			List<FootprintBarData> visibleBars,
			int lastVisibleBarIdx,
			float barSpacing,
			float chartBottomY,
			DxBrush brushBidZone,
			DxBrush brushAskZone,
			DxBrush brushBidBorder,
			DxBrush brushAskBorder)
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

		private void GetSessionHeaderOrderflowSpec(DayProfile day, out int ticksPerLvl, out double imbPct)
		{
			if (UseSessionWiseOrderflow && day != null)
			{
				if (day.SessionId == 0 || string.Equals(day.SessionName, "Asia", StringComparison.OrdinalIgnoreCase))
				{
					ticksPerLvl = runtimeSessionStepActive ? Math.Max(1, AsiaTicksPerLevel) : (runtimeAutoStep ? Math.Max(1, lastEffectiveAutoTicks) : Math.Max(1, runtimeTicksPerLevel));
					imbPct = AsiaImbalancePercentage;
					return;
				}
				if (day.SessionId == 1 || string.Equals(day.SessionName, "London", StringComparison.OrdinalIgnoreCase))
				{
					ticksPerLvl = runtimeSessionStepActive ? Math.Max(1, LondonTicksPerLevel) : (runtimeAutoStep ? Math.Max(1, lastEffectiveAutoTicks) : Math.Max(1, runtimeTicksPerLevel));
					imbPct = LondonImbalancePercentage;
					return;
				}
				if (day.SessionId == 2 || string.Equals(day.SessionName, "New York", StringComparison.OrdinalIgnoreCase))
				{
					ticksPerLvl = runtimeSessionStepActive ? Math.Max(1, NewYorkTicksPerLevel) : (runtimeAutoStep ? Math.Max(1, lastEffectiveAutoTicks) : Math.Max(1, runtimeTicksPerLevel));
					imbPct = NewYorkImbalancePercentage;
					return;
				}
			}

			ticksPerLvl = runtimeAutoStep ? Math.Max(1, lastEffectiveAutoTicks) : Math.Max(1, runtimeTicksPerLevel);
			imbPct = ImbalancePercentage;
		}

		private void RenderSleekTopToolbar(float panelX, float panelY, float barSpacing, float effectiveStep, bool isCandlestickZoom, string activeSessionTag = "")
		{
			for (int i = 0; i < toolbarBtnRects.Length; i++)
				toolbarBtnRects[i] = default(RectangleF);

			bool isSessionStep = UseSessionWiseOrderflow && runtimeSessionStepActive;
			bool stepPillActive = isSessionStep || runtimeAutoStep;
			string stepNumStr = FormatOrderflowPrice(effectiveStep);
			string stepModeTooltip = isSessionStep
				? ("Session Step: " + (!string.IsNullOrEmpty(activeSessionTag) ? (activeSessionTag + " ") : "") + stepNumStr + " (Click: Auto)")
				: (runtimeAutoStep ? ("Auto Step: " + stepNumStr + " (Click: Manual/Session)") : ("Manual Step: " + stepNumStr + " (Click: Session/Auto)"));
			bool isCustom360Active = isFree360View || freeRightPanOffsetPx > 0.5f || Math.Abs(runtimeYZoomFactor - 1.0) > 0.01;

			var hdrDw = Globals.DirectWriteFactory;
			using (var tfStep = new DxTextFormat(hdrDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 9.6f))
			using (var tfTip = new DxTextFormat(hdrDw, "Segoe UI", SharpDX.DirectWrite.FontWeight.SemiBold, SharpDX.DirectWrite.FontStyle.Normal, 9.4f))
			using (var barBg = new DxBrush(RenderTarget, runtimeLightTheme ? new Color4(0.95f, 0.96f, 0.98f, 0.97f) : new Color4(0.039f, 0.063f, 0.110f, 0.97f)))
			using (var barTopSheen = new DxBrush(RenderTarget, runtimeLightTheme ? new Color4(1f, 1f, 1f, 0.45f) : new Color4(0.220f, 0.741f, 0.973f, 0.09f)))
			using (var barBorder = new DxBrush(RenderTarget, runtimeLightTheme ? new Color4(0.75f, 0.80f, 0.86f, 0.95f) : new Color4(0.220f, 0.741f, 0.973f, 0.42f)))
			using (var logoRingGlow = new DxBrush(RenderTarget, new Color4(0.055f, 0.647f, 0.914f, 0.24f)))
			using (var logoRingBorder = new DxBrush(RenderTarget, new Color4(0.220f, 0.741f, 0.973f, 0.88f)))
			using (var sepLineBrush = new DxBrush(RenderTarget, runtimeLightTheme ? new Color4(0.78f, 0.83f, 0.89f, 0.90f) : new Color4(0.220f, 0.741f, 0.973f, 0.25f)))
			using (var brandBrush = new DxBrush(RenderTarget, new Color4(0.220f, 0.741f, 0.973f, 1.0f)))
			using (var btnBg = new DxBrush(RenderTarget, runtimeLightTheme ? new Color4(1.0f, 1.0f, 1.0f, 0.98f) : new Color4(0.075f, 0.118f, 0.188f, 0.98f)))
			using (var btnHoverBg = new DxBrush(RenderTarget, runtimeLightTheme ? new Color4(0.88f, 0.93f, 0.98f, 1.0f) : new Color4(0.078f, 0.215f, 0.345f, 1.0f)))
			using (var btnActiveBg = new DxBrush(RenderTarget, new Color4(0.047f, 0.290f, 0.431f, 0.98f)))
			using (var btnBorder = new DxBrush(RenderTarget, runtimeLightTheme ? new Color4(0.75f, 0.80f, 0.86f, 0.95f) : new Color4(0.220f, 0.741f, 0.973f, 0.30f)))
			using (var btnHoverBorder = new DxBrush(RenderTarget, new Color4(0.220f, 0.741f, 0.973f, 0.96f)))
			using (var fgActive = new DxBrush(RenderTarget, new Color4(0.490f, 0.827f, 0.988f, 1.0f)))
			using (var fgInactive = new DxBrush(RenderTarget, runtimeLightTheme ? new Color4(0.15f, 0.20f, 0.28f, 1.0f) : new Color4(0.86f, 0.92f, 0.98f, 1.0f)))
			{
				tfStep.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfStep.ParagraphAlignment = ParagraphAlignment.Center;
				tfStep.WordWrapping = WordWrapping.NoWrap;

				tfTip.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfTip.ParagraphAlignment = ParagraphAlignment.Center;
				tfTip.WordWrapping = WordWrapping.NoWrap;

				float boxX = panelX + 8f;
				float boxY = panelY + 6f;
				float bh = 24f;
				float padY = 5.0f;

				bool wrapTwoRows = (ChartPanel != null && ChartPanel.W < 606f);
				float boxH = wrapTwoRows ? 60f : 34f;
				float maxAvailW = ChartPanel != null ? Math.Max(260f, ChartPanel.W - 20f) : 590f;
				float boxW = wrapTwoRows ? Math.Min(maxAvailW, 368f) : Math.Min(maxAvailW, 590f);

				var barRr = new RoundedRectangle { Rect = new RectangleF(boxX, boxY, boxW, boxH), RadiusX = 7.0f, RadiusY = 7.0f };
				RenderTarget.FillRoundedRectangle(barRr, barBg);
				RenderTarget.FillRoundedRectangle(new RoundedRectangle
				{
					Rect = new RectangleF(boxX + 1.5f, boxY + 1.5f, boxW - 3f, Math.Min(14f, boxH * 0.42f)),
					RadiusX = 5.5f,
					RadiusY = 5.5f
				}, barTopSheen);
				RenderTarget.DrawRoundedRectangle(barRr, barBorder, 1.15f);

				float cx = boxX + 6f;
				float cy = boxY + padY;

				// AK10X Circular Brand Logo with Market Profile Glowing Cyan Ring
				float logoSz = 26f;
				float logoY = boxY + (34f - logoSz) * 0.5f;
				Vector2 logoCenter = new Vector2(cx + logoSz * 0.5f, logoY + logoSz * 0.5f);
				RenderTarget.FillEllipse(new Ellipse(logoCenter, logoSz * 0.54f, logoSz * 0.54f), logoRingGlow);
				SharpDX.Direct2D1.Bitmap logoBmp = GetOrCreateLogoBitmap(RenderTarget, runtimeLightTheme);
				if (logoBmp != null)
				{
					RenderTarget.DrawBitmap(logoBmp, new RectangleF(cx, logoY, logoSz, logoSz), 1.0f, BitmapInterpolationMode.Linear);
				}
				else
				{
					RenderTarget.FillEllipse(new Ellipse(logoCenter, logoSz * 0.5f, logoSz * 0.5f), btnActiveBg);
				}
				RenderTarget.DrawEllipse(new Ellipse(logoCenter, logoSz * 0.50f, logoSz * 0.50f), logoRingBorder, 1.15f);
				cx += logoSz + 5f;
				RenderTarget.DrawLine(new Vector2(cx, cy + 2f), new Vector2(cx, cy + bh - 2f), sepLineBrush, 1.0f);
				cx += 5f;

				// 1. Footprint & Imbalance Mode Icons
				bool act0 = !isCandlestickZoom && runtimeOrderflowMode == AK10XOrderflowMode.VolumeFootprint;
				toolbarBtnRects[0] = DrawToolbarIconBtn(cx, cy, 27f, bh, 0, string.Empty, act0, hoveredToolbarBtnIndex == 0, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, act0 ? fgActive : fgInactive);
				cx += 30f;

				bool act1 = !isCandlestickZoom && runtimeOrderflowMode == AK10XOrderflowMode.Imbalance;
				toolbarBtnRects[1] = DrawToolbarIconBtn(cx, cy, 27f, bh, 1, string.Empty, act1, hoveredToolbarBtnIndex == 1, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, act1 ? fgActive : fgInactive);
				cx += 31f;

				RenderTarget.DrawLine(new Vector2(cx, cy + 2f), new Vector2(cx, cy + bh - 2f), sepLineBrush, 1.0f);
				cx += 5f;

				// 2. Zoom Out | Candle <-> Orderflow | Zoom In | Auto-Fit Icons
				toolbarBtnRects[12] = DrawToolbarIconBtn(cx, cy, 25f, bh, 12, string.Empty, false, hoveredToolbarBtnIndex == 12, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, fgInactive);
				cx += 28f;

				toolbarBtnRects[4] = DrawToolbarIconBtn(cx, cy, 28f, bh, 4, string.Empty, isCandlestickZoom, hoveredToolbarBtnIndex == 4, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, isCandlestickZoom ? fgActive : fgInactive);
				cx += 31f;

				toolbarBtnRects[13] = DrawToolbarIconBtn(cx, cy, 25f, bh, 13, string.Empty, false, hoveredToolbarBtnIndex == 13, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, fgInactive);
				cx += 28f;

				toolbarBtnRects[14] = DrawToolbarIconBtn(cx, cy, 25f, bh, 14, string.Empty, isCustom360Active, hoveredToolbarBtnIndex == 14, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, isCustom360Active ? fgActive : fgInactive);
				cx += 29f;

				if (wrapTwoRows)
				{
					cx = boxX + 8f;
					cy = boxY + 30f;
				}
				else
				{
					RenderTarget.DrawLine(new Vector2(cx, cy + 2f), new Vector2(cx, cy + bh - 2f), sepLineBrush, 1.0f);
					cx += 5f;
				}

				// 3. Price Step Controls: [ - ] [ ≡ 0.2 ] [ + ]
				toolbarBtnRects[5] = DrawToolbarIconBtn(cx, cy, 22f, bh, 5, string.Empty, false, hoveredToolbarBtnIndex == 5, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, fgInactive);
				cx += 25f;

				toolbarBtnRects[6] = DrawToolbarIconBtn(cx, cy, 48f, bh, 6, stepNumStr, stepPillActive, hoveredToolbarBtnIndex == 6, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, stepPillActive ? fgActive : fgInactive);
				cx += 51f;

				toolbarBtnRects[7] = DrawToolbarIconBtn(cx, cy, 22f, bh, 7, string.Empty, false, hoveredToolbarBtnIndex == 7, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, fgInactive);
				cx += 26f;

				RenderTarget.DrawLine(new Vector2(cx, cy + 2f), new Vector2(cx, cy + bh - 2f), sepLineBrush, 1.0f);
				cx += 5f;

				// 4. Long & Short Position R:R Tools (Backtest & Live Order Tracking)
				bool actLong = (pendingRrToolMode == 1);
				toolbarBtnRects[2] = DrawToolbarIconBtn(cx, cy, 27f, bh, 2, string.Empty, actLong, hoveredToolbarBtnIndex == 2, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, actLong ? fgActive : fgInactive);
				cx += 30f;

				bool actShort = (pendingRrToolMode == 2);
				toolbarBtnRects[3] = DrawToolbarIconBtn(cx, cy, 27f, bh, 3, string.Empty, actShort, hoveredToolbarBtnIndex == 3, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, actShort ? fgActive : fgInactive);
				cx += 31f;

				RenderTarget.DrawLine(new Vector2(cx, cy + 2f), new Vector2(cx, cy + bh - 2f), sepLineBrush, 1.0f);
				cx += 5f;

				// 5. MP Zones | S/R Zones | Order & Scalp Panel | Theme Icons
				toolbarBtnRects[15] = DrawToolbarIconBtn(cx, cy, 27f, bh, 15, string.Empty, runtimeShowMpZones, hoveredToolbarBtnIndex == 15, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, runtimeShowMpZones ? fgActive : fgInactive);
				cx += 30f;

				toolbarBtnRects[8] = DrawToolbarIconBtn(cx, cy, 27f, bh, 8, string.Empty, runtimeShowSrZones, hoveredToolbarBtnIndex == 8, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, runtimeShowSrZones ? fgActive : fgInactive);
				cx += 30f;

				toolbarBtnRects[11] = DrawToolbarIconBtn(cx, cy, 27f, bh, 11, string.Empty, runtimeShowTradePanel, hoveredToolbarBtnIndex == 11, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, runtimeShowTradePanel ? fgActive : fgInactive);
				cx += 30f;

				toolbarBtnRects[10] = DrawToolbarIconBtn(cx, cy, 25f, bh, 10, string.Empty, runtimeLightTheme, hoveredToolbarBtnIndex == 10, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, runtimeLightTheme ? fgActive : fgInactive);
				cx += 28f;

				toolbarBtnRects[9] = DrawToolbarIconBtn(cx, cy, 27f, bh, 9, string.Empty, runtimeShowSignals, hoveredToolbarBtnIndex == 9, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, runtimeShowSignals ? fgActive : fgInactive);
				cx += 30f;

				toolbarBtnRects[16] = DrawToolbarIconBtn(cx, cy, 27f, bh, 16, string.Empty, runtimeShowSrAnalysis, hoveredToolbarBtnIndex == 16, tfStep, btnBg, btnHoverBg, btnActiveBg, btnBorder, btnHoverBorder, runtimeShowSrAnalysis ? fgActive : fgInactive);

				// Crisp Hover Tooltip Badge below Hovered Icon Button
				if (hoveredToolbarBtnIndex >= 0 && hoveredToolbarBtnIndex < toolbarBtnRects.Length)
				{
					RectangleF hRect = toolbarBtnRects[hoveredToolbarBtnIndex];
					if (hRect.Width > 0f)
					{
						string tipText = GetToolbarButtonTooltip(hoveredToolbarBtnIndex, isCandlestickZoom, stepModeTooltip);
						if (!string.IsNullOrEmpty(tipText))
						{
							float tipW = Math.Max(68f, tipText.Length * 5.9f + 16f);
							float tipH = 20f;
							float tipX = Math.Max(panelX + 6f, hRect.X + hRect.Width * 0.5f - tipW * 0.5f);
							float tipY = boxY + boxH + 4f;
							var tipRr = new RoundedRectangle { Rect = new RectangleF(tipX, tipY, tipW, tipH), RadiusX = 4f, RadiusY = 4f };
							RenderTarget.FillRoundedRectangle(tipRr, barBg);
							RenderTarget.DrawRoundedRectangle(tipRr, btnHoverBorder, 1.0f);
							RenderTarget.DrawText(tipText, tfTip, tipRr.Rect, fgInactive);
						}
					}
				}
			}
		}

		private string GetToolbarButtonTooltip(int btnIdx, bool isCandlestickZoom, string stepModeTooltip)
		{
			switch (btnIdx)
			{
				case 0: return "Footprint View";
				case 1: return "Imbalance View";
				case 2: return pendingRrToolMode == 1 ? "Cancel Long Tool (Esc)" : "Long Position R:R (Click Chart)";
				case 3: return pendingRrToolMode == 2 ? "Cancel Short Tool (Esc)" : "Short Position R:R (Click Chart)";
				case 4: return isCandlestickZoom ? "Expand to Orderflow" : "Switch to Candlesticks";
				case 5: return "Decrease Price Step (-)";
				case 6: return stepModeTooltip;
				case 7: return "Increase Price Step (+)";
				case 8: return "S/R Zones";
				case 9:
				{
					int curMin = Math.Max(1, GetChartPeriodMinutes());
					int htfMin = curMin * Math.Max(2, SignalHigherTfMultiplier);
					string tfPair = IsIntradayMinuteChart() ? (" (" + curMin + "m + " + htfMin + "m)") : string.Empty;
					return (runtimeShowSignals ? "Hide Signals" : "Show Signals") + tfPair;
				}
				case 11: return runtimeShowTradePanel ? "Hide Order & Scalp Panel" : "Show Order & Scalp Panel";
				case 10: return runtimeLightTheme ? "Dark Theme" : "White Theme";
				case 12: return "Zoom Out";
				case 13: return "Zoom In";
				case 14: return "Auto-Fit / Live Edge";
				case 15: return "Market Profile Zones";
				case 16: return runtimeShowSrAnalysis ? "Hide Analysis (Active S/R Only)" : "Analysis (Show Past Invalid S/R Lines)";
				default: return string.Empty;
			}
		}

		private RectangleF DrawToolbarIconBtn(
			float x, float y, float w, float h, int iconId, string auxNumber, bool active, bool hovered,
			DxTextFormat tf, DxBrush bg, DxBrush hoverBg, DxBrush activeBg,
			DxBrush border, DxBrush hoverBorder, DxBrush fg)
		{
			var r = new RectangleF(x, y, w, h);
			var rr = new RoundedRectangle { Rect = r, RadiusX = 4.5f, RadiusY = 4.5f };
			DxBrush curFill = hovered ? hoverBg : (active ? activeBg : bg);
			RenderTarget.FillRoundedRectangle(rr, curFill);
			if (active || hovered)
			{
				using (var btnSheen = new DxBrush(RenderTarget, new Color4(1f, 1f, 1f, active ? 0.08f : 0.05f)))
				{
					RenderTarget.FillRoundedRectangle(new RoundedRectangle
					{
						Rect = new RectangleF(x + 1f, y + 1f, Math.Max(2f, w - 2f), Math.Max(2f, h * 0.44f)),
						RadiusX = 3.5f,
						RadiusY = 3.5f
					}, btnSheen);
				}
			}
			RenderTarget.DrawRoundedRectangle(rr, (hovered || active) ? hoverBorder : border, (hovered || active) ? 1.2f : 0.9f);

			float icx = x + w * 0.5f;
			float icy = y + h * 0.5f;

			switch (iconId)
			{
				case 0: // Footprint Icon (Center Candle Spine + Left Bid & Right Ask Volume Bars)
				{
					RenderTarget.DrawLine(new Vector2(icx, icy - 6.0f), new Vector2(icx, icy + 6.0f), fg, 1.4f);
					RenderTarget.FillRectangle(new RectangleF(icx - 1.1f, icy - 2.6f, 2.2f, 5.2f), fg);

					RenderTarget.DrawLine(new Vector2(icx - 2.2f, icy - 4.0f), new Vector2(icx - 6.2f, icy - 4.0f), fg, 1.7f);
					RenderTarget.DrawLine(new Vector2(icx - 2.2f, icy), new Vector2(icx - 7.4f, icy), fg, 1.7f);
					RenderTarget.DrawLine(new Vector2(icx - 2.2f, icy + 4.0f), new Vector2(icx - 5.2f, icy + 4.0f), fg, 1.7f);

					RenderTarget.DrawLine(new Vector2(icx + 2.2f, icy - 4.0f), new Vector2(icx + 7.2f, icy - 4.0f), fg, 1.7f);
					RenderTarget.DrawLine(new Vector2(icx + 2.2f, icy), new Vector2(icx + 5.4f, icy), fg, 1.7f);
					RenderTarget.DrawLine(new Vector2(icx + 2.2f, icy + 4.0f), new Vector2(icx + 7.4f, icy + 4.0f), fg, 1.7f);
					break;
				}
				case 1: // Imbalance Icon (Staggered Diagonal Ask/Bid Blocks + Diagonal Imbalance Vector)
				{
					RenderTarget.FillRectangle(new RectangleF(icx + 1.0f, icy - 5.8f, 5.8f, 3.3f), fg);
					RenderTarget.FillRectangle(new RectangleF(icx - 6.8f, icy + 2.5f, 5.8f, 3.3f), fg);
					RenderTarget.DrawLine(new Vector2(icx - 3.6f, icy + 3.2f), new Vector2(icx + 3.6f, icy - 3.2f), fg, 1.65f);
					RenderTarget.DrawLine(new Vector2(icx + 0.6f, icy - 3.2f), new Vector2(icx + 3.6f, icy - 3.2f), fg, 1.5f);
					RenderTarget.DrawLine(new Vector2(icx + 3.6f, icy - 3.2f), new Vector2(icx + 3.6f, icy - 0.2f), fg, 1.5f);
					break;
				}
				case 2: // Long Position R:R Icon (TradingView 3-Line Projection + Entry Node + Diagonal Up + 'L')
				{
					float lx0 = icx - 6.4f, lx1 = icx + 6.6f;
					RenderTarget.DrawLine(new Vector2(lx0 + 2.4f, icy - 5.0f), new Vector2(lx1, icy - 5.0f), fg, 1.5f);
					RenderTarget.DrawLine(new Vector2(lx0, icy + 0.2f), new Vector2(lx1, icy + 0.2f), fg, 1.5f);
					RenderTarget.DrawLine(new Vector2(lx0 + 2.4f, icy + 5.0f), new Vector2(lx1, icy + 5.0f), fg, 1.5f);
					RenderTarget.FillEllipse(new Ellipse(new Vector2(lx0 + 1.8f, icy + 0.2f), 1.8f, 1.8f), fg);
					RenderTarget.DrawLine(new Vector2(lx0 + 2.6f, icy - 0.5f), new Vector2(lx1 - 1.5f, icy - 4.6f), fg, 1.15f);
					// Crisp micro 'L' on upper-left
					RenderTarget.DrawLine(new Vector2(lx0, icy - 6.0f), new Vector2(lx0, icy - 2.3f), fg, 1.35f);
					RenderTarget.DrawLine(new Vector2(lx0, icy - 2.3f), new Vector2(lx0 + 2.5f, icy - 2.3f), fg, 1.35f);
					break;
				}
				case 3: // Short Position R:R Icon (TradingView 3-Line Projection + Entry Node + Diagonal Down + 'S')
				{
					float sx0 = icx - 6.4f, sx1 = icx + 6.6f;
					RenderTarget.DrawLine(new Vector2(sx0 + 2.4f, icy - 5.0f), new Vector2(sx1, icy - 5.0f), fg, 1.5f);
					RenderTarget.DrawLine(new Vector2(sx0, icy - 0.2f), new Vector2(sx1, icy - 0.2f), fg, 1.5f);
					RenderTarget.DrawLine(new Vector2(sx0 + 2.4f, icy + 5.0f), new Vector2(sx1, icy + 5.0f), fg, 1.5f);
					RenderTarget.FillEllipse(new Ellipse(new Vector2(sx0 + 1.8f, icy - 0.2f), 1.8f, 1.8f), fg);
					RenderTarget.DrawLine(new Vector2(sx0 + 2.6f, icy + 0.5f), new Vector2(sx1 - 1.5f, icy + 4.6f), fg, 1.15f);
					// Crisp micro 'S' on lower-left
					RenderTarget.DrawLine(new Vector2(sx0 + 2.5f, icy + 2.0f), new Vector2(sx0, icy + 2.0f), fg, 1.2f);
					RenderTarget.DrawLine(new Vector2(sx0, icy + 2.0f), new Vector2(sx0, icy + 3.8f), fg, 1.2f);
					RenderTarget.DrawLine(new Vector2(sx0, icy + 3.8f), new Vector2(sx0 + 2.5f, icy + 3.8f), fg, 1.2f);
					RenderTarget.DrawLine(new Vector2(sx0 + 2.5f, icy + 3.8f), new Vector2(sx0 + 2.5f, icy + 5.7f), fg, 1.2f);
					RenderTarget.DrawLine(new Vector2(sx0 + 2.5f, icy + 5.7f), new Vector2(sx0, icy + 5.7f), fg, 1.2f);
					break;
				}
				case 4: // Candlestick <-> Orderflow Toggle (Two Japanese Candlesticks)
				{
					float lx = icx - 3.6f;
					float rx = icx + 3.6f;
					RenderTarget.DrawLine(new Vector2(lx, icy - 6.0f), new Vector2(lx, icy + 6.0f), fg, 1.25f);
					RenderTarget.FillRectangle(new RectangleF(lx - 2.1f, icy - 3.2f, 4.2f, 6.4f), fg);

					RenderTarget.DrawLine(new Vector2(rx, icy - 5.5f), new Vector2(rx, icy + 6.0f), fg, 1.25f);
					var rBody = new RectangleF(rx - 2.1f, icy - 1.8f, 4.2f, 5.6f);
					RenderTarget.FillRectangle(rBody, curFill);
					RenderTarget.DrawRectangle(rBody, fg, 1.35f);
					break;
				}
				case 5: // Minus (-)
				{
					RenderTarget.DrawLine(new Vector2(icx - 4.2f, icy), new Vector2(icx + 4.2f, icy), fg, 1.85f);
					break;
				}
				case 6: // Stacked Price Step Icon + Numeric Value (Zero Text Words)
				{
					float lx0 = x + 4.5f;
					float lx1 = x + 11.5f;
					RenderTarget.DrawLine(new Vector2(lx0, icy - 3.6f), new Vector2(lx1, icy - 3.6f), fg, 1.45f);
					RenderTarget.DrawLine(new Vector2(lx0, icy), new Vector2(lx1 + 1.5f, icy), fg, 1.45f);
					RenderTarget.DrawLine(new Vector2(lx0, icy + 3.6f), new Vector2(lx1, icy + 3.6f), fg, 1.45f);
					RenderTarget.DrawText(auxNumber, tf, new RectangleF(x + 13.5f, y, w - 15.5f, h), fg);
					break;
				}
				case 7: // Plus (+)
				{
					RenderTarget.DrawLine(new Vector2(icx - 4.2f, icy), new Vector2(icx + 4.2f, icy), fg, 1.85f);
					RenderTarget.DrawLine(new Vector2(icx, icy - 4.2f), new Vector2(icx, icy + 4.2f), fg, 1.85f);
					break;
				}
				case 8: // S/R Zones (Top Resistance Line + Bottom Support Line + Price Bounce Wave)
				{
					RenderTarget.DrawLine(new Vector2(icx - 6.8f, icy - 5.2f), new Vector2(icx + 6.8f, icy - 5.2f), fg, 1.6f);
					RenderTarget.DrawLine(new Vector2(icx - 6.8f, icy + 5.2f), new Vector2(icx + 6.8f, icy + 5.2f), fg, 1.6f);
					RenderTarget.DrawLine(new Vector2(icx - 5.6f, icy + 4.2f), new Vector2(icx - 1.4f, icy - 4.2f), fg, 1.25f);
					RenderTarget.DrawLine(new Vector2(icx - 1.4f, icy - 4.2f), new Vector2(icx + 2.0f, icy + 2.2f), fg, 1.25f);
					RenderTarget.DrawLine(new Vector2(icx + 2.0f, icy + 2.2f), new Vector2(icx + 5.6f, icy - 4.2f), fg, 1.25f);
					break;
				}
				case 9: // Chart Signals Toggle Icon (Buy ▲ + Sell ▼ Signal Triangles & Pulse Wave)
				{
					using (var sigBuyBr = new DxBrush(RenderTarget, active ? new Color4(1f, 1f, 1f, 1f) : new Color4(0.08f, 0.84f, 0.52f, 1f)))
					using (var sigSellBr = new DxBrush(RenderTarget, active ? new Color4(1f, 0.88f, 0.96f, 1f) : new Color4(0.92f, 0.20f, 0.58f, 1f)))
					{
						// Connecting signal pulse zig-zag
						RenderTarget.DrawLine(new Vector2(icx - 6.6f, icy + 1.8f), new Vector2(icx - 2.2f, icy - 3.0f), fg, 1.25f);
						RenderTarget.DrawLine(new Vector2(icx - 2.2f, icy - 3.0f), new Vector2(icx + 2.2f, icy + 3.0f), fg, 1.25f);
						RenderTarget.DrawLine(new Vector2(icx + 2.2f, icy + 3.0f), new Vector2(icx + 6.6f, icy - 1.8f), fg, 1.25f);

						// Bottom-left Buy ▲ triangle
						DrawFilledSignalTriangle(icx - 4.5f, icy + 1.8f, 3.0f, 4.2f, false, sigBuyBr, null, 0f);
						// Top-right Sell ▼ triangle
						DrawFilledSignalTriangle(icx + 4.5f, icy - 1.8f, 3.0f, 4.2f, true, sigSellBr, null, 0f);
					}
					break;
				}
				case 16: // Analysis Mode Icon (Past Broken S/R Lines + Analytics Inspection Lens)
				{
					// Top active S/R line + middle broken past S/R segment with end tick '|'
					RenderTarget.DrawLine(new Vector2(icx - 6.8f, icy - 4.8f), new Vector2(icx + 6.4f, icy - 4.8f), fg, 1.45f);
					RenderTarget.DrawLine(new Vector2(icx - 6.8f, icy + 0.6f), new Vector2(icx - 0.8f, icy + 0.6f), fg, 1.45f);
					RenderTarget.DrawLine(new Vector2(icx - 0.8f, icy - 1.6f), new Vector2(icx - 0.8f, icy + 2.8f), fg, 1.35f);
					RenderTarget.DrawLine(new Vector2(icx - 6.8f, icy + 5.0f), new Vector2(icx - 2.2f, icy + 5.0f), fg, 1.35f);

					// Crisp analytics magnifying lens on bottom-right
					float lcx = icx + 2.6f;
					float lcy = icy + 1.6f;
					RenderTarget.DrawEllipse(new Ellipse(new Vector2(lcx, lcy), 3.4f, 3.4f), fg, 1.5f);
					RenderTarget.DrawLine(new Vector2(lcx + 2.4f, lcy + 2.4f), new Vector2(icx + 6.8f, icy + 5.8f), fg, 1.75f);
					break;
				}
				case 11: // Order & Scalp Execution Panel Icon (Tradesea Dual Buy/Sell Arrows + Ticket Sidebar)
				{
					var frameRect = new RectangleF(icx - 6.2f, icy - 5.4f, 12.4f, 10.8f);
					RenderTarget.DrawRectangle(frameRect, fg, 1.35f);
					RenderTarget.DrawLine(new Vector2(icx + 1.8f, icy - 5.4f), new Vector2(icx + 1.8f, icy + 5.4f), fg, 1.25f);
					// Up/Down Buy & Sell arrows on left pane
					RenderTarget.DrawLine(new Vector2(icx - 2.6f, icy + 2.8f), new Vector2(icx - 2.6f, icy - 2.8f), fg, 1.45f);
					RenderTarget.DrawLine(new Vector2(icx - 4.4f, icy - 1.0f), new Vector2(icx - 2.6f, icy - 2.8f), fg, 1.35f);
					RenderTarget.DrawLine(new Vector2(icx - 0.8f, icy - 1.0f), new Vector2(icx - 2.6f, icy - 2.8f), fg, 1.35f);
					break;
				}
				case 10: // Theme Toggle (Crescent Moon in Dark Mode / Radiant Sun in Light Mode)
				{
					if (runtimeLightTheme)
					{
						RenderTarget.FillEllipse(new Ellipse(new Vector2(icx, icy), 3.1f, 3.1f), fg);
						for (int k = 0; k < 8; k++)
						{
							double ang = k * (Math.PI * 0.25);
							float c = (float)Math.Cos(ang);
							float s = (float)Math.Sin(ang);
							RenderTarget.DrawLine(new Vector2(icx + c * 4.5f, icy + s * 4.5f), new Vector2(icx + c * 6.5f, icy + s * 6.5f), fg, 1.35f);
						}
					}
					else
					{
						RenderTarget.FillEllipse(new Ellipse(new Vector2(icx - 0.6f, icy + 0.4f), 5.2f, 5.2f), fg);
						RenderTarget.FillEllipse(new Ellipse(new Vector2(icx + 2.3f, icy - 1.8f), 4.4f, 4.4f), curFill);
					}
					break;
				}
				case 12: // Zoom Out (Magnifying Glass with Minus)
				{
					float mcx = icx - 1.2f;
					float mcy = icy - 1.2f;
					RenderTarget.DrawEllipse(new Ellipse(new Vector2(mcx, mcy), 4.5f, 4.5f), fg, 1.5f);
					RenderTarget.DrawLine(new Vector2(mcx + 3.2f, mcy + 3.2f), new Vector2(icx + 5.8f, icy + 5.8f), fg, 1.8f);
					RenderTarget.DrawLine(new Vector2(mcx - 2.3f, mcy), new Vector2(mcx + 2.3f, mcy), fg, 1.45f);
					break;
				}
				case 13: // Zoom In (Magnifying Glass with Plus)
				{
					float mcx = icx - 1.2f;
					float mcy = icy - 1.2f;
					RenderTarget.DrawEllipse(new Ellipse(new Vector2(mcx, mcy), 4.5f, 4.5f), fg, 1.5f);
					RenderTarget.DrawLine(new Vector2(mcx + 3.2f, mcy + 3.2f), new Vector2(icx + 5.8f, icy + 5.8f), fg, 1.8f);
					RenderTarget.DrawLine(new Vector2(mcx - 2.3f, mcy), new Vector2(mcx + 2.3f, mcy), fg, 1.45f);
					RenderTarget.DrawLine(new Vector2(mcx, mcy - 2.3f), new Vector2(mcx, mcy + 2.3f), fg, 1.45f);
					break;
				}
				case 14: // Auto-Fit (4-Corner Framing Brackets + Center Target Dot)
				{
					float fx0 = icx - 5.6f, fx1 = icx + 5.6f;
					float fy0 = icy - 5.0f, fy1 = icy + 5.0f;
					float arm = 3.3f;
					RenderTarget.DrawLine(new Vector2(fx0, fy0 + arm), new Vector2(fx0, fy0), fg, 1.5f);
					RenderTarget.DrawLine(new Vector2(fx0, fy0), new Vector2(fx0 + arm, fy0), fg, 1.5f);

					RenderTarget.DrawLine(new Vector2(fx1 - arm, fy0), new Vector2(fx1, fy0), fg, 1.5f);
					RenderTarget.DrawLine(new Vector2(fx1, fy0), new Vector2(fx1, fy0 + arm), fg, 1.5f);

					RenderTarget.DrawLine(new Vector2(fx0, fy1 - arm), new Vector2(fx0, fy1), fg, 1.5f);
					RenderTarget.DrawLine(new Vector2(fx0, fy1), new Vector2(fx0 + arm, fy1), fg, 1.5f);

					RenderTarget.DrawLine(new Vector2(fx1 - arm, fy1), new Vector2(fx1, fy1), fg, 1.5f);
					RenderTarget.DrawLine(new Vector2(fx1, fy1), new Vector2(fx1, fy1 - arm), fg, 1.5f);

					RenderTarget.FillEllipse(new Ellipse(new Vector2(icx, icy), 1.35f, 1.35f), fg);
					break;
				}
				case 15: // Market Profile Zones (Sideways Bell-Curve TPO Histogram)
				{
					float sx = icx - 5.8f;
					RenderTarget.DrawLine(new Vector2(sx, icy - 5.8f), new Vector2(sx, icy + 5.8f), fg, 1.4f);
					RenderTarget.DrawLine(new Vector2(sx + 0.8f, icy - 4.6f), new Vector2(sx + 5.0f, icy - 4.6f), fg, 1.65f);
					RenderTarget.DrawLine(new Vector2(sx + 0.8f, icy - 2.3f), new Vector2(sx + 8.6f, icy - 2.3f), fg, 1.65f);
					RenderTarget.DrawLine(new Vector2(sx + 0.8f, icy), new Vector2(sx + 11.8f, icy), fg, 1.85f);
					RenderTarget.DrawLine(new Vector2(sx + 0.8f, icy + 2.3f), new Vector2(sx + 8.0f, icy + 2.3f), fg, 1.65f);
					RenderTarget.DrawLine(new Vector2(sx + 0.8f, icy + 4.6f), new Vector2(sx + 4.6f, icy + 4.6f), fg, 1.65f);
					break;
				}
			}

			return r;
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
				using (var bgBrush = new DxBrush(RenderTarget, new Color4(0.055f, 0.075f, 0.105f, 1.0f)))
				using (var pillBg = new DxBrush(RenderTarget, new Color4(0.08f, 0.12f, 0.18f, 0.96f)))
				using (var pillBorder = new DxBrush(RenderTarget, new Color4(0.031f, 0.600f, 0.506f, 0.80f)))
				using (var txtBrush = new DxBrush(RenderTarget, new Color4(0.96f, 0.98f, 1.0f, 1.0f)))
				using (var tf = new DxTextFormat(Globals.DirectWriteFactory, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 12.5f))
				{
					RenderTarget.FillRectangle(new RectangleF(px, py, pw, ph), bgBrush);
					tf.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
					tf.ParagraphAlignment = ParagraphAlignment.Center;
					float bw = Math.Min(360f, pw - 32f);
					float bh = 36f;
					var badgeRect = new RectangleF(px + (pw - bw) * 0.5f, py + (ph - bh) * 0.5f, bw, bh);
					var rr = new RoundedRectangle { Rect = badgeRect, RadiusX = 5f, RadiusY = 5f };
					RenderTarget.FillRoundedRectangle(rr, pillBg);
					RenderTarget.DrawRoundedRectangle(rr, pillBorder, 1.1f);
					RenderTarget.DrawText("Loading AK10XBigSort — " + sym + "...", tf, badgeRect, txtBrush);
				}
			}
			catch { }
		}

		private RectangleF DrawPillBtn(
			float x, float y, float w, float h, string text, bool active, bool hovered,
			DxTextFormat tf, DxBrush bg, DxBrush hoverBg, DxBrush activeBg,
			DxBrush border, DxBrush hoverBorder, DxBrush fg)
		{
			var r = new RectangleF(x, y, w, h);
			var rr = new RoundedRectangle { Rect = r, RadiusX = 3.5f, RadiusY = 3.5f };
			RenderTarget.FillRoundedRectangle(rr, hovered ? hoverBg : (active ? activeBg : bg));
			RenderTarget.DrawRoundedRectangle(rr, hovered ? hoverBorder : border, hovered ? 1.25f : 0.9f);
			RenderTarget.DrawText(text, tf, r, fg);
			return r;
		}

		private static string FormatRrMoney(double val)
		{
			double abs = Math.Abs(val);
			if (abs >= 1000000.0)
				return (val / 1000000.0).ToString("0.##M", CultureInfo.InvariantCulture);
			if (abs >= 10000.0)
				return (val / 1000.0).ToString("0.##K", CultureInfo.InvariantCulture);
			return val.ToString("0.##", CultureInfo.InvariantCulture);
		}

		private void RenderRrPositionBoxesOnCanvas(
			ChartControl cc,
			Func<double, float> priceToY,
			float barSpacing,
			float panelX,
			float canvasTopY,
			float clipW,
			float chartBottomY,
			bool light)
		{
			if (cc == null || ChartBars == null || ChartBars.Bars == null || ChartBars.Count == 0)
				return;

			Bars bars = ChartBars.Bars;
			int barCount = bars.Count;
			int lastIdx = barCount - 1;
			float clipRight = panelX + clipW;
			double tick = TickSize > 0 ? TickSize : 0.05;
			double pointVal = (Instrument != null && Instrument.MasterInstrument != null && Instrument.MasterInstrument.PointValue > 0)
				? Instrument.MasterInstrument.PointValue
				: 1.0;

			var dashProps = new StrokeStyleProperties
			{
				DashStyle = SharpDX.Direct2D1.DashStyle.Custom,
				DashCap = CapStyle.Flat,
				StartCap = CapStyle.Flat,
				EndCap = CapStyle.Flat
			};

			var dw = Globals.DirectWriteFactory;
			using (var pathDash = new StrokeStyle(Globals.D2DFactory, dashProps, new float[] { 4f, 3f }))
			using (var tfBadge = new DxTextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 9.6f))
			using (var tfCenter = new DxTextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 9.5f))
			using (var tfFixedRr = new DxTextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10.2f))
			using (var fixedRrBg = new DxBrush(RenderTarget, light ? new Color4(0.96f, 0.98f, 1.0f, 0.97f) : new Color4(0.075f, 0.105f, 0.155f, 0.96f)))
			using (var fixedRrTextBrush = new DxBrush(RenderTarget, light ? new Color4(0.10f, 0.15f, 0.22f, 1.0f) : new Color4(0.96f, 0.98f, 1.0f, 1.0f)))
			using (var tpZoneFill = new DxBrush(RenderTarget, new Color4(0.031f, 0.600f, 0.506f, light ? 0.19f : 0.24f)))
			using (var slZoneFill = new DxBrush(RenderTarget, new Color4(0.90f, 0.24f, 0.28f, light ? 0.16f : 0.21f)))
			using (var tpLineBrush = new DxBrush(RenderTarget, new Color4(0.031f, 0.630f, 0.525f, 0.96f)))
			using (var slLineBrush = new DxBrush(RenderTarget, new Color4(0.95f, 0.26f, 0.30f, 0.95f)))
			using (var entryLineBrush = new DxBrush(RenderTarget, light ? new Color4(0.21f, 0.23f, 0.29f, 0.96f) : new Color4(0.82f, 0.85f, 0.90f, 0.95f)))
			using (var selBorderBrush = new DxBrush(RenderTarget, new Color4(0.16f, 0.38f, 1.0f, 0.85f)))
			using (var tpPillBg = new DxBrush(RenderTarget, new Color4(0.031f, 0.600f, 0.506f, 0.97f)))
			using (var slPillBg = new DxBrush(RenderTarget, light ? new Color4(0.32f, 0.35f, 0.42f, 0.97f) : new Color4(0.26f, 0.29f, 0.35f, 0.97f)))
			using (var slHitPillBg = new DxBrush(RenderTarget, new Color4(0.78f, 0.18f, 0.23f, 0.97f)))
			using (var neutralPillBg = new DxBrush(RenderTarget, light ? new Color4(0.28f, 0.31f, 0.38f, 0.95f) : new Color4(0.22f, 0.25f, 0.31f, 0.95f)))
			using (var txtWhite = new DxBrush(RenderTarget, new Color4(1.0f, 1.0f, 1.0f, 1.0f)))
			using (var handleFill = new DxBrush(RenderTarget, new Color4(1.0f, 1.0f, 1.0f, 1.0f)))
			using (var handleRing = new DxBrush(RenderTarget, new Color4(0.16f, 0.38f, 1.0f, 1.0f)))
			using (var delBg = new DxBrush(RenderTarget, new Color4(0.84f, 0.18f, 0.22f, 0.96f)))
			{
				tfBadge.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfBadge.ParagraphAlignment = ParagraphAlignment.Center;
				tfBadge.WordWrapping = WordWrapping.NoWrap;

				tfCenter.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfCenter.ParagraphAlignment = ParagraphAlignment.Center;
				tfCenter.WordWrapping = WordWrapping.NoWrap;

				tfFixedRr.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
				tfFixedRr.ParagraphAlignment = ParagraphAlignment.Center;
				tfFixedRr.WordWrapping = WordWrapping.NoWrap;

				// 1. Live Ghost Crosshair Preview when Long / Short Tool is Armed
				if (pendingRrToolMode != 0 && rrPreviewMouseX >= panelX && rrPreviewMouseX <= clipRight && rrPreviewMouseY >= canvasTopY && rrPreviewMouseY <= chartBottomY && lastRenderedChartScale != null)
				{
					double prevEntry = RoundToInstrumentTick(lastRenderedChartScale.GetValueByY(rrPreviewMouseY));
					double prevSlot = CanvasXToBarSlot(cc, rrPreviewMouseX, barSpacing);
					float gx = SlotToCanvasX(cc, prevSlot, barSpacing);
					float gy = priceToY(prevEntry);
					bool pLong = (pendingRrToolMode == 1);

					RenderTarget.DrawLine(new Vector2(panelX + 2f, gy), new Vector2(clipRight - 2f, gy), pLong ? tpLineBrush : slLineBrush, 1.1f, pathDash);
					RenderTarget.DrawLine(new Vector2(gx, canvasTopY + 2f), new Vector2(gx, chartBottomY - 2f), pLong ? tpLineBrush : slLineBrush, 1.0f, pathDash);
					RenderTarget.FillEllipse(new Ellipse(new Vector2(gx, gy), 3.6f, 3.6f), pLong ? tpPillBg : slHitPillBg);

					string ghostTag = (pLong ? "Click to Place LONG @ " : "Click to Place SHORT @ ") + FormatOrderflowPrice(prevEntry);
					float gw = Math.Max(155f, ghostTag.Length * 6.0f + 16f);
					float gxPill = Math.Max(panelX + 6f, Math.Min(clipRight - gw - 6f, gx + 10f));
					float gyPill = Math.Max(canvasTopY + 6f, gy - 24f);
					var gRr = new RoundedRectangle { Rect = new RectangleF(gxPill, gyPill, gw, 19f), RadiusX = 3.5f, RadiusY = 3.5f };
					RenderTarget.FillRoundedRectangle(gRr, pLong ? tpPillBg : slHitPillBg);
					RenderTarget.DrawText(ghostTag, tfBadge, gRr.Rect, txtWhite);
				}

				// 2. Render All Placed Long & Short R:R Position Boxes
				for (int i = 0; i < rrPositionBoxes.Count; i++)
				{
					RrPositionBox box = rrPositionBoxes[i];
					box.LastDeleteBtnRect = default(RectangleF);
					box.LastFixedRrRect = default(RectangleF);

					int startIdx = Math.Max(0, Math.Min(lastIdx, (int)Math.Round(box.StartBarSlot)));
					int scanEndIdx = Math.Max(startIdx, Math.Min(lastIdx, (int)Math.Round(box.EndBarSlot)));

					// Evaluate Backtest / Live Order Tracking Outcome across [startIdx .. scanEndIdx]
					int outcome = 0; // 0 = Open/Running, 1 = TP Hit, -1 = SL Hit, 2 = Historical Window Closed
					int exitBarIdx = scanEndIdx;
					double exitOrLivePrice = bars.GetClose(scanEndIdx);

					for (int b = startIdx + 1; b <= scanEndIdx; b++)
					{
						double bh = bars.GetHigh(b);
						double bl = bars.GetLow(b);
						double bo = bars.GetOpen(b);

						if (box.IsLong)
						{
							bool hitSl = (bl <= box.StopPrice + 1e-9);
							bool hitTp = (bh >= box.TargetPrice - 1e-9);
							if (hitSl && hitTp)
							{
								outcome = (Math.Abs(bo - box.StopPrice) <= Math.Abs(bo - box.TargetPrice)) ? -1 : 1;
								exitBarIdx = b;
								exitOrLivePrice = (outcome == 1) ? box.TargetPrice : box.StopPrice;
								break;
							}
							if (hitSl)
							{
								outcome = -1;
								exitBarIdx = b;
								exitOrLivePrice = box.StopPrice;
								break;
							}
							if (hitTp)
							{
								outcome = 1;
								exitBarIdx = b;
								exitOrLivePrice = box.TargetPrice;
								break;
							}
						}
						else
						{
							bool hitSl = (bh >= box.StopPrice - 1e-9);
							bool hitTp = (bl <= box.TargetPrice + 1e-9);
							if (hitSl && hitTp)
							{
								outcome = (Math.Abs(bo - box.StopPrice) <= Math.Abs(bo - box.TargetPrice)) ? -1 : 1;
								exitBarIdx = b;
								exitOrLivePrice = (outcome == 1) ? box.TargetPrice : box.StopPrice;
								break;
							}
							if (hitSl)
							{
								outcome = -1;
								exitBarIdx = b;
								exitOrLivePrice = box.StopPrice;
								break;
							}
							if (hitTp)
							{
								outcome = 1;
								exitBarIdx = b;
								exitOrLivePrice = box.TargetPrice;
								break;
							}
						}
					}

					// Auto-extend live open trade as new real-time candles form until TP or SL hits
					if (outcome == 0 && box.AutoExtendLive && lastIdx >= box.EndBarSlot - 1.0)
						box.EndBarSlot = lastIdx + 4.0;

					bool isOpenLive = (outcome == 0 && box.EndBarSlot >= lastIdx);
					if (isOpenLive)
					{
						exitBarIdx = lastIdx;
						exitOrLivePrice = (State == State.Realtime && lastTradePrice > 0) ? lastTradePrice : bars.GetClose(lastIdx);
					}
					else if (outcome == 0)
					{
						outcome = 2;
						exitBarIdx = scanEndIdx;
						exitOrLivePrice = bars.GetClose(scanEndIdx);
					}

					float xLeft = SlotToCanvasX(cc, box.StartBarSlot, barSpacing);
					float xRight = Math.Max(xLeft + 26f, SlotToCanvasX(cc, box.EndBarSlot, barSpacing));
					if (xRight < panelX + 2f || xLeft > clipRight - 2f)
						continue;

					float yEntry = priceToY(box.EntryPrice);
					float yTarget = priceToY(box.TargetPrice);
					float yStop = priceToY(box.StopPrice);

					float clLeft = Math.Max(panelX + 2f, xLeft);
					float clRight = Math.Min(clipRight - 2f, xRight);
					float drawW = clRight - clLeft;
					if (drawW <= 2f)
						continue;

					// Shaded Target (TP) Zone
					float tpTop = Math.Max(canvasTopY, Math.Min(yEntry, yTarget));
					float tpBot = Math.Min(chartBottomY, Math.Max(yEntry, yTarget));
					if (tpBot > tpTop + 0.5f)
						RenderTarget.FillRectangle(new RectangleF(clLeft, tpTop, drawW, tpBot - tpTop), tpZoneFill);

					// Shaded Stop (SL) Zone
					float slTop = Math.Max(canvasTopY, Math.Min(yEntry, yStop));
					float slBot = Math.Min(chartBottomY, Math.Max(yEntry, yStop));
					if (slBot > slTop + 0.5f)
						RenderTarget.FillRectangle(new RectangleF(clLeft, slTop, drawW, slBot - slTop), slZoneFill);

					// Crisp Horizontal TP, SL, and Entry Lines
					if (yTarget >= canvasTopY && yTarget <= chartBottomY)
						DrawHairlineHorizontal(clLeft, clRight, yTarget, tpLineBrush, 1.45f);
					if (yStop >= canvasTopY && yStop <= chartBottomY)
						DrawHairlineHorizontal(clLeft, clRight, yStop, slLineBrush, 1.45f);
					if (yEntry >= canvasTopY && yEntry <= chartBottomY)
						DrawHairlineHorizontal(clLeft, clRight, yEntry, entryLineBrush, 1.65f);

					bool isSelected = (box.Id == selectedRrBoxId);
					if (isSelected)
					{
						float bTop = Math.Max(canvasTopY, Math.Min(yTarget, yStop));
						float bBot = Math.Min(chartBottomY, Math.Max(yTarget, yStop));
						if (xLeft >= panelX + 2f && xLeft <= clipRight - 2f && bBot > bTop)
							RenderTarget.DrawLine(new Vector2(xLeft, bTop), new Vector2(xLeft, bBot), selBorderBrush, 1.0f);
						if (xRight >= panelX + 2f && xRight <= clipRight - 2f && bBot > bTop)
							RenderTarget.DrawLine(new Vector2(xRight, bTop), new Vector2(xRight, bBot), selBorderBrush, 1.0f);
					}

					// Dashed Diagonal Trade Tracking Path Line (Entry -> TP / SL / Live Price)
					float xPathEnd = SlotToCanvasX(cc, exitBarIdx, barSpacing);
					float yPathEnd = priceToY(exitOrLivePrice);
					if (xPathEnd >= xLeft + 2f)
					{
						DxBrush pathBrush = (outcome == 1) ? tpLineBrush : (outcome == -1 ? slLineBrush : entryLineBrush);
						RenderTarget.DrawLine(new Vector2(xLeft, yEntry), new Vector2(xPathEnd, yPathEnd), pathBrush, 1.35f, pathDash);
						RenderTarget.FillEllipse(new Ellipse(new Vector2(xPathEnd, yPathEnd), isOpenLive ? 3.6f : 3.0f, isOpenLive ? 3.6f : 3.0f), pathBrush);
					}

					// Compute R:R, Ticks, %, Qty, and PnL Amounts
					double tpDist = Math.Max(tick, Math.Abs(box.TargetPrice - box.EntryPrice));
					double slDist = Math.Max(tick, Math.Abs(box.EntryPrice - box.StopPrice));
					double rrRatio = tpDist / slDist;
					double tpPct = (tpDist / Math.Max(1e-9, Math.Abs(box.EntryPrice))) * 100.0;
					double slPct = (slDist / Math.Max(1e-9, Math.Abs(box.EntryPrice))) * 100.0;
					long tpTicks = Math.Max(1L, (long)Math.Round(tpDist / tick));
					long slTicks = Math.Max(1L, (long)Math.Round(slDist / tick));

					int qty = RrAutoCalculateQty
						? Math.Max(1, (int)Math.Floor(Math.Max(1.0, RrRiskPerTradeAmount) / Math.Max(1e-6, slDist * pointVal)))
						: Math.Max(1, RrFixedQuantity);

					double tpRewardAmt = qty * tpDist * pointVal;
					double slRiskAmt = qty * slDist * pointVal;
					double pnlPts = box.IsLong ? (exitOrLivePrice - box.EntryPrice) : (box.EntryPrice - exitOrLivePrice);
					double pnlAmt = qty * pnlPts * pointVal;
					string pnlSign = pnlPts >= 0 ? "+" : "";

					float midBoxX = 0.5f * (clLeft + clRight);

					// A. Target Outer Pill Badge (TradingView Teal Badge)
					string tpText = string.Format(
						CultureInfo.InvariantCulture,
						"Target: {0} ({1:0.00}%) {2:N0}T, Amount: {3}",
						FormatOrderflowPrice(tpDist), tpPct, tpTicks, FormatRrMoney(tpRewardAmt));
					float tpPillW = Math.Min(clipW - 12f, Math.Max(168f, tpText.Length * 5.85f + 16f));
					float tpPillH = 19f;
					float tpPillX = Math.Max(panelX + 4f, Math.Min(clipRight - tpPillW - 4f, midBoxX - tpPillW * 0.5f));
					float tpPillY = box.IsLong ? (yTarget - tpPillH - 4f) : (yTarget + 4f);
					tpPillY = Math.Max(canvasTopY + 2f, Math.Min(chartBottomY - tpPillH - 2f, tpPillY));
					var tpRr = new RoundedRectangle { Rect = new RectangleF(tpPillX, tpPillY, tpPillW, tpPillH), RadiusX = 3.5f, RadiusY = 3.5f };
					RenderTarget.FillRoundedRectangle(tpRr, tpPillBg);
					RenderTarget.DrawText(tpText, tfBadge, tpRr.Rect, txtWhite);

					// B. Stop Outer Pill Badge
					string slText = string.Format(
						CultureInfo.InvariantCulture,
						"Stop: {0} ({1:0.00}%) {2:N0}T, Amount: {3}",
						FormatOrderflowPrice(slDist), slPct, slTicks, FormatRrMoney(slRiskAmt));
					float slPillW = Math.Min(clipW - 12f, Math.Max(164f, slText.Length * 5.85f + 16f));
					float slPillH = 19f;
					float slPillX = Math.Max(panelX + 4f, Math.Min(clipRight - slPillW - 4f, midBoxX - slPillW * 0.5f));
					float slPillY = box.IsLong ? (yStop + 4f) : (yStop - slPillH - 4f);
					slPillY = Math.Max(canvasTopY + 2f, Math.Min(chartBottomY - slPillH - 2f, slPillY));
					var slRr = new RoundedRectangle { Rect = new RectangleF(slPillX, slPillY, slPillW, slPillH), RadiusX = 3.5f, RadiusY = 3.5f };
					RenderTarget.FillRoundedRectangle(slRr, outcome == -1 ? slHitPillBg : slPillBg);
					RenderTarget.DrawText(slText, tfBadge, slRr.Rect, txtWhite);

					// C1. Fixed Compact "RR : X.XX" Box Right Beside the Entry Line (Never Hides Candles)
					string fixedRrText = string.Format(CultureInfo.InvariantCulture, "RR : {0:0.##}", rrRatio);
					float fRrW = Math.Max(66f, fixedRrText.Length * 6.3f + 16f);
					float fRrH = 21f;
					float fRrX;
					float fRrY;
					if (xRight + 8f + fRrW <= clipRight - 3f)
					{
						fRrX = Math.Max(panelX + 4f, xRight + 8f);
						fRrY = yEntry - fRrH * 0.5f;
					}
					else
					{
						fRrX = Math.Max(panelX + 4f, clipRight - fRrW - 6f);
						fRrY = box.IsLong ? (yEntry - fRrH - 5f) : (yEntry + 5f);
					}
					fRrY = Math.Max(canvasTopY + 2f, Math.Min(chartBottomY - fRrH - 2f, fRrY));
					var fRrRect = new RectangleF(fRrX, fRrY, fRrW, fRrH);
					box.LastFixedRrRect = fRrRect;
					var fRrRound = new RoundedRectangle { Rect = fRrRect, RadiusX = 3.5f, RadiusY = 3.5f };
					DxBrush fRrBorder = (rrRatio >= 1.0) ? tpLineBrush : slLineBrush;
					RenderTarget.FillRoundedRectangle(fRrRound, fixedRrBg);
					RenderTarget.DrawRoundedRectangle(fRrRound, fRrBorder, 1.35f);
					RenderTarget.DrawText(fixedRrText, tfFixedRr, fRrRect, fixedRrTextBrush);

					// C2. Middle 2-Line PnL + Risk/Reward Ratio Box — Shown ONLY on Mouse Hover (or Active Scaling Drag)
					bool isBoxHoveredOrDragged = (box.Id == hoveredRrBoxId) ||
						(activeDragRrBox != null && activeDragRrBox.Id == box.Id && activeRrDragMode != RrDragMode.None);
					if (isBoxHoveredOrDragged)
					{
						string statusPrefix = isOpenLive
							? "Open PnL: "
							: (outcome == 1 ? "Closed PnL (TP): " : (outcome == -1 ? "Closed PnL (SL): " : "Closed PnL: "));
						string centerLine1 = string.Format(
							CultureInfo.InvariantCulture,
							"{0}{1}{2}, Qty: {3}",
							statusPrefix, pnlSign, FormatOrderflowPrice(pnlPts), qty);
						string centerLine2 = string.Format(
							CultureInfo.InvariantCulture,
							"Risk/reward ratio: {0:0.##}",
							rrRatio);
						string centerCombined = centerLine1 + "\n" + centerLine2;

						int maxChars = Math.Max(centerLine1.Length, centerLine2.Length);
						float cPillW = Math.Min(Math.Max(120f, drawW - 6f), Math.Max(148f, maxChars * 6.0f + 18f));
						float cPillH = 32f;
						float cPillX = Math.Max(panelX + 4f, Math.Min(clipRight - cPillW - 4f, midBoxX - cPillW * 0.5f));
						float cPillY = box.IsLong ? (yEntry - cPillH - 4f) : (yEntry + 4f);
						if (Math.Abs(yTarget - yEntry) < cPillH + 8f)
							cPillY = yEntry - cPillH * 0.5f;
						cPillY = Math.Max(canvasTopY + 2f, Math.Min(chartBottomY - cPillH - 2f, cPillY));

						DxBrush centerBg = (outcome == 1 || (isOpenLive && pnlPts >= 0) || (outcome == 2 && pnlPts > 0))
							? tpPillBg
							: (outcome == -1 ? slHitPillBg : neutralPillBg);
						var cRr = new RoundedRectangle { Rect = new RectangleF(cPillX, cPillY, cPillW, cPillH), RadiusX = 4f, RadiusY = 4f };
						RenderTarget.FillRoundedRectangle(cRr, centerBg);
						RenderTarget.DrawText(centerCombined, tfCenter, cRr.Rect, txtWhite);
					}

					// D. 4 Blue-Ringed Circular Scaling Handles + [✕] Delete Button when Selected (or Always Subtle Handles)
					float hRad = isSelected ? 4.6f : 3.6f;
					DrawRrHandleCircle(xLeft, yTarget, hRad, handleFill, handleRing);
					DrawRrHandleCircle(xLeft, yStop, hRad, handleFill, handleRing);
					DrawRrHandleCircle(xLeft, yEntry, hRad, handleFill, handleRing);
					DrawRrHandleCircle(xRight, yEntry, hRad, handleFill, handleRing);

					if (isSelected)
					{
						float delW = 19f, delH = 18f;
						float delX = Math.Max(clLeft + 4f, Math.Min(clipRight - delW - 4f, clRight - delW - 4f));
						float delY = Math.Max(canvasTopY + 3f, Math.Min(chartBottomY - delH - 3f, Math.Min(yTarget, yStop) + 4f));
						var delRect = new RectangleF(delX, delY, delW, delH);
						box.LastDeleteBtnRect = delRect;
						var delRr = new RoundedRectangle { Rect = delRect, RadiusX = 3f, RadiusY = 3f };
						RenderTarget.FillRoundedRectangle(delRr, delBg);
						float dcx = delX + delW * 0.5f;
						float dcy = delY + delH * 0.5f;
						RenderTarget.DrawLine(new Vector2(dcx - 3.6f, dcy - 3.6f), new Vector2(dcx + 3.6f, dcy + 3.6f), txtWhite, 1.55f);
						RenderTarget.DrawLine(new Vector2(dcx + 3.6f, dcy - 3.6f), new Vector2(dcx - 3.6f, dcy + 3.6f), txtWhite, 1.55f);
					}
				}
			}
		}

		private void DrawRrHandleCircle(float cx, float cy, float radius, DxBrush fill, DxBrush ring)
		{
			var el = new Ellipse(new Vector2(cx, cy), radius, radius);
			RenderTarget.FillEllipse(el, fill);
			RenderTarget.DrawEllipse(el, ring, 1.75f);
		}

		private static string FormatGoChartingVol(long val)
		{
			long abs = Math.Abs(val);
			if (abs >= 1000000)
				return (val / 1000000.0).ToString("0.#M", CultureInfo.InvariantCulture);
			if (abs >= 1000)
				return (val / 1000.0).ToString("0.#K", CultureInfo.InvariantCulture);
			return val.ToString(CultureInfo.InvariantCulture);
		}

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

		#region Tradesea Order & Scalp Execution Panel + Live Chart Order Drag Engine
		private WpfPrimitives.Popup tradeAccountPopup = null;
		private WpfControls.StackPanel tradeAccountPopupList = null;
		private WpfControls.TextBlock txtSelectedAccountLabel = null;
		private DateTime tradeToastExpireUtc = DateTime.MinValue;
		private bool scalpUseAutoBracket = true;

		private static WpfMedia.SolidColorBrush MakeFrozenBrush(byte r, byte g, byte b, byte a = 255)
		{
			var br = new WpfMedia.SolidColorBrush(WpfMedia.Color.FromArgb(a, r, g, b));
			br.Freeze();
			return br;
		}

		private string GetAccountCurrencyPrefix(Account acc)
		{
			try
			{
				if (acc != null)
				{
					string denom = acc.Denomination.ToString();
					if (denom.IndexOf("Inr", StringComparison.OrdinalIgnoreCase) >= 0 ||
						denom.IndexOf("Rupee", StringComparison.OrdinalIgnoreCase) >= 0)
						return "₹";
					if (denom.IndexOf("Eur", StringComparison.OrdinalIgnoreCase) >= 0)
						return "€";
					if (denom.IndexOf("Gbp", StringComparison.OrdinalIgnoreCase) >= 0)
						return "£";
				}
			}
			catch { }
			return "$";
		}

		private bool InstrumentMatchesChart(Instrument candidate)
		{
			Instrument chartInst = Instrument;
			if (candidate == null || chartInst == null)
				return false;
			if (ReferenceEquals(candidate, chartInst))
				return true;
			if (string.Equals(candidate.FullName, chartInst.FullName, StringComparison.OrdinalIgnoreCase))
				return true;
			try
			{
				if (candidate.MasterInstrument != null && chartInst.MasterInstrument != null &&
					string.Equals(candidate.MasterInstrument.Name, chartInst.MasterInstrument.Name, StringComparison.OrdinalIgnoreCase) &&
					candidate.Expiry == chartInst.Expiry)
				{
					return true;
				}
			}
			catch { }
			return false;
		}

		private static bool IsOrderWorkingState(OrderState st)
		{
			return st == OrderState.Working ||
				st == OrderState.Accepted ||
				st == OrderState.Submitted ||
				st == OrderState.PartFilled ||
				st == OrderState.ChangePending ||
				st == OrderState.ChangeSubmitted ||
				st == OrderState.TriggerPending;
		}

		private void ResolveLiveBidAskLast(out double bid, out double ask, out double last)
		{
			double tick = TickSize > 0 ? TickSize : 0.05;
			double fallbackClose = 0.0;
			try
			{
				if (ChartBars != null && ChartBars.Bars != null && ChartBars.Bars.Count > 0)
					fallbackClose = ChartBars.Bars.GetClose(ChartBars.Bars.Count - 1);
			}
			catch { }

			last = lastTradePrice > 0 ? lastTradePrice : fallbackClose;
			bid = lastBidPrice;
			ask = lastAskPrice;

			try
			{
				Instrument inst = Instrument;
				if (inst != null && inst.MarketData != null)
				{
					if (bid <= 0 && inst.MarketData.Bid != null && inst.MarketData.Bid.Price > 0)
						bid = inst.MarketData.Bid.Price;
					if (ask <= 0 && inst.MarketData.Ask != null && inst.MarketData.Ask.Price > 0)
						ask = inst.MarketData.Ask.Price;
					if (last <= 0 && inst.MarketData.Last != null && inst.MarketData.Last.Price > 0)
						last = inst.MarketData.Last.Price;
				}
			}
			catch { }

			if (last <= 0)
				last = fallbackClose > 0 ? fallbackClose : 100.0;
			if (bid <= 0)
				bid = RoundToInstrumentTick(last);
			if (ask <= 0 || ask < bid)
				ask = RoundToInstrumentTick(bid + tick);
		}

		private double GetCurrentOrderReferenceEntryPrice()
		{
			ResolveLiveBidAskLast(out double bid, out double ask, out double last);
			if (selectedOrderTypeIdx == 0)
				return isOrderSideBuy ? (ask > 0 ? ask : last) : (bid > 0 ? bid : last);

			if (!orderCustomEntrySeeded || orderCustomEntryPrice <= 0)
			{
				SeedDefaultCustomEntryPrice();
			}
			return RoundToInstrumentTick(orderCustomEntryPrice > 0 ? orderCustomEntryPrice : last);
		}

		private void SeedDefaultCustomEntryPrice()
		{
			double tick = TickSize > 0 ? TickSize : 0.05;
			ResolveLiveBidAskLast(out double bid, out double ask, out double last);
			if (selectedOrderTypeIdx == 1) // Limit
			{
				orderCustomEntryPrice = isOrderSideBuy
					? RoundToInstrumentTick(bid - tick * 4.0)
					: RoundToInstrumentTick(ask + tick * 4.0);
			}
			else if (selectedOrderTypeIdx == 2) // Stop
			{
				orderCustomEntryPrice = isOrderSideBuy
					? RoundToInstrumentTick(ask + tick * 4.0)
					: RoundToInstrumentTick(bid - tick * 4.0);
			}
			else
			{
				orderCustomEntryPrice = RoundToInstrumentTick(last);
			}
			orderCustomEntrySeeded = true;
		}

		private List<Account> GetAvailableTradeAccounts()
		{
			List<Account> result = new List<Account>();
			try
			{
				if (Account.All != null)
				{
					Account[] snap;
					lock (Account.All)
					{
						snap = Account.All.ToArray();
					}
					for (int i = 0; i < snap.Length; i++)
					{
						Account a = snap[i];
						if (a == null || string.IsNullOrWhiteSpace(a.Name))
							continue;
						if (string.Equals(a.Name, "Backtest", StringComparison.OrdinalIgnoreCase))
							continue;
						result.Add(a);
					}
				}
			}
			catch { }
			return result;
		}

		private Account EnsureActiveTradeAccountSelected()
		{
			if (activeTradeAccount != null)
			{
				EnsureAccountEventsSubscribed(activeTradeAccount);
				return activeTradeAccount;
			}

			try
			{
				ChartControl cc = hookedOrderflowCC ?? ChartControl;
				if (cc != null && cc.OwnerChart != null && cc.OwnerChart.ChartTrader != null && cc.OwnerChart.ChartTrader.Account != null)
				{
					activeTradeAccount = cc.OwnerChart.ChartTrader.Account;
				}
			}
			catch { }

			if (activeTradeAccount == null)
			{
				List<Account> all = GetAvailableTradeAccounts();
				for (int i = 0; i < all.Count; i++)
				{
					if (string.Equals(all[i].Name, "Sim101", StringComparison.OrdinalIgnoreCase))
					{
						activeTradeAccount = all[i];
						break;
					}
				}
				if (activeTradeAccount == null && all.Count > 0)
					activeTradeAccount = all[0];
			}

			EnsureAccountEventsSubscribed(activeTradeAccount);
			return activeTradeAccount;
		}

		private void SelectTradeAccount(Account nextAcc)
		{
			if (nextAcc == null)
				return;
			activeTradeAccount = nextAcc;
			EnsureAccountEventsSubscribed(nextAcc);

			try
			{
				ChartControl cc = hookedOrderflowCC ?? ChartControl;
				if (cc != null && cc.OwnerChart != null && cc.OwnerChart.ChartTrader != null)
					cc.OwnerChart.ChartTrader.Account = nextAcc;
			}
			catch { }

			SyncTradePanelUiFromState();
			RequestImmediateOrderflowRedraw();
		}

		private void EnsureAccountEventsSubscribed(Account acc)
		{
			if (ReferenceEquals(subscribedTradeAccount, acc))
				return;

			UnsubscribeAccountEventsOnly();
			if (acc == null)
				return;

			try
			{
				acc.OrderUpdate += OnSelectedAccountOrderUpdate;
				acc.PositionUpdate += OnSelectedAccountPositionUpdate;
				acc.AccountItemUpdate += OnSelectedAccountItemUpdate;
				subscribedTradeAccount = acc;
			}
			catch { }
		}

		private void UnsubscribeAccountEventsOnly()
		{
			Account prev = subscribedTradeAccount;
			subscribedTradeAccount = null;
			if (prev != null)
			{
				try { prev.OrderUpdate -= OnSelectedAccountOrderUpdate; } catch { }
				try { prev.PositionUpdate -= OnSelectedAccountPositionUpdate; } catch { }
				try { prev.AccountItemUpdate -= OnSelectedAccountItemUpdate; } catch { }
			}
		}

		private Position GetCurrentInstrumentPosition(Account acc, bool nonBlocking = false)
		{
			if (acc == null || acc.Positions == null || Instrument == null)
				return null;
			try
			{
				Position[] snap = null;
				if (nonBlocking)
				{
					bool entered = false;
					try
					{
						System.Threading.Monitor.TryEnter(acc.Positions, 0, ref entered);
						if (entered)
							snap = acc.Positions.ToArray();
					}
					finally
					{
						if (entered)
							System.Threading.Monitor.Exit(acc.Positions);
					}
					if (snap == null)
						return cachedLastKnownPosition;
				}
				else
				{
					lock (acc.Positions)
					{
						snap = acc.Positions.ToArray();
					}
				}

				Position found = null;
				for (int i = 0; i < snap.Length; i++)
				{
					Position p = snap[i];
					if (p != null && p.MarketPosition != MarketPosition.Flat && p.Quantity > 0 && InstrumentMatchesChart(p.Instrument))
					{
						found = p;
						break;
					}
				}
				cachedLastKnownPosition = found;
				return found;
			}
			catch { }
			return nonBlocking ? cachedLastKnownPosition : null;
		}

		private List<Order> GetCurrentInstrumentWorkingOrders(Account acc, bool nonBlocking = false)
		{
			List<Order> list = new List<Order>();
			if (acc == null || acc.Orders == null || Instrument == null)
				return list;
			try
			{
				Order[] snap = null;
				if (nonBlocking)
				{
					bool entered = false;
					try
					{
						System.Threading.Monitor.TryEnter(acc.Orders, 0, ref entered);
						if (entered)
							snap = acc.Orders.ToArray();
					}
					finally
					{
						if (entered)
							System.Threading.Monitor.Exit(acc.Orders);
					}
					if (snap == null)
						return cachedLastKnownWorkingOrders ?? list;
				}
				else
				{
					lock (acc.Orders)
					{
						snap = acc.Orders.ToArray();
					}
				}

				for (int i = 0; i < snap.Length; i++)
				{
					Order o = snap[i];
					if (o != null && IsOrderWorkingState(o.OrderState) && InstrumentMatchesChart(o.Instrument))
						list.Add(o);
				}
				cachedLastKnownWorkingOrders = list;
			}
			catch
			{
				if (nonBlocking && cachedLastKnownWorkingOrders != null)
					return cachedLastKnownWorkingOrders;
			}
			return list;
		}

		private static bool IsOrderBuySide(OrderAction act)
		{
			return act == OrderAction.Buy || act == OrderAction.BuyToCover;
		}

		private static bool IsStopOrderType(OrderType ot)
		{
			return ot == OrderType.StopMarket || ot == OrderType.StopLimit;
		}

		private static double GetWorkingOrderEffectivePrice(Order o)
		{
			if (o == null)
				return 0.0;
			if (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)
				return o.StopPrice > 0 ? o.StopPrice : o.LimitPrice;
			return o.LimitPrice > 0 ? o.LimitPrice : o.StopPrice;
		}

		private int ClassifyWorkingOrderRole(Order o, Position pos)
		{
			// Returns: 1 = Protective TP, 2 = Protective SL, 0 = Working Entry
			if (o == null)
				return 0;

			string nm = o.Name ?? string.Empty;
			if (string.Equals(nm, "TP", StringComparison.OrdinalIgnoreCase) ||
				nm.IndexOf("AK10X_TP", StringComparison.OrdinalIgnoreCase) >= 0 ||
				nm.IndexOf("Profit", StringComparison.OrdinalIgnoreCase) >= 0 ||
				nm.IndexOf("Target", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return 1;
			}
			if (string.Equals(nm, "SL", StringComparison.OrdinalIgnoreCase) ||
				nm.IndexOf("AK10X_SL", StringComparison.OrdinalIgnoreCase) >= 0 ||
				nm.IndexOf("Stop", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return 2;
			}

			if (pos != null && pos.MarketPosition != MarketPosition.Flat)
			{
				bool posLong = (pos.MarketPosition == MarketPosition.Long);
				bool orderBuy = IsOrderBuySide(o.OrderAction);
				if (posLong != orderBuy)
				{
					if (IsStopOrderType(o.OrderType))
						return 2;
					if (o.OrderType == OrderType.Limit || o.OrderType == OrderType.MIT)
						return 1;
				}
			}

			return 0;
		}

		private static string GetRiskDisciplineStateFilePath()
		{
			string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AK10X");
			return Path.Combine(dir, "BigSortRiskDiscipline_v1.txt");
		}

		private static void EnsureSharedRiskDisciplineFileLoaded()
		{
			if (sharedRiskFileLoaded)
				return;
			lock (riskPersistLock)
			{
				if (sharedRiskFileLoaded)
					return;
				sharedRiskFileLoaded = true;
				try
				{
					string path = GetRiskDisciplineStateFilePath();
					if (!File.Exists(path))
						return;
					string[] lines = File.ReadAllLines(path);
					DateTime nowLocal = DateTime.Now;
					for (int i = 0; i < lines.Length; i++)
					{
						string ln = lines[i];
						if (string.IsNullOrWhiteSpace(ln))
							continue;
						string[] parts = ln.Split('\t');
						if (parts.Length < 4)
							continue;
						string key = parts[0];
						if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long unlockTicks))
							continue;
						DateTime unlockAt = new DateTime(unlockTicks, DateTimeKind.Local);
						if (unlockAt <= nowLocal.AddHours(-24))
							continue;
						bool isLck = string.Equals(parts[2], "1", StringComparison.Ordinal);
						string reason = parts[3] ?? string.Empty;
						var rec = new DailyRiskDisciplineRecord
						{
							AccountAndDayKey = key,
							UnlockLocalTime = unlockAt,
							IsLocked = isLck && nowLocal < unlockAt,
							LockReason = reason
						};
						if (parts.Length >= 5 && !string.IsNullOrEmpty(parts[4]))
						{
							string[] ids = parts[4].Split(',');
							for (int k = 0; k < ids.Length; k++)
							{
								if (!string.IsNullOrWhiteSpace(ids[k]))
									rec.FilledEntryOrderIds.Add(ids[k].Trim());
							}
						}
						SharedDailyRiskRecords[key] = rec;
					}
				}
				catch { }
			}
		}

		private static void SaveSharedRiskDisciplineFile()
		{
			lock (riskPersistLock)
			{
				try
				{
					string path = GetRiskDisciplineStateFilePath();
					string dir = Path.GetDirectoryName(path);
					if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
						Directory.CreateDirectory(dir);
					DateTime cutoff = DateTime.Now.AddHours(-18);
					List<string> outLines = new List<string>();
					foreach (var kv in SharedDailyRiskRecords)
					{
						DailyRiskDisciplineRecord r = kv.Value;
						if (r == null || r.UnlockLocalTime < cutoff)
							continue;
						string joinedIds = string.Join(",", r.FilledEntryOrderIds);
						outLines.Add(string.Join("\t",
							r.AccountAndDayKey ?? kv.Key,
							r.UnlockLocalTime.Ticks.ToString(CultureInfo.InvariantCulture),
							r.IsLocked ? "1" : "0",
							(r.LockReason ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '),
							joinedIds));
					}
					File.WriteAllLines(path, outLines.ToArray());
				}
				catch { }
			}
		}

		private void ResolveCurrentTradingDayWindow(DateTime nowLocal, out string dayKey, out DateTime dayOpenLocal, out DateTime nextDayOpenLocal)
		{
			int openSec;
			if (ShowIndianSession && IsValidTime(MarketOpen))
				openSec = ToSeconds(MarketOpen);
			else if (!TryParseSessionTime(AsiaFromTime, out openSec))
				openSec = 3 * 3600 + 30 * 60; // 03:30 AM IST Global Asia Open

			int curSec = nowLocal.Hour * 3600 + nowLocal.Minute * 60 + nowLocal.Second;
			DateTime tradingDate = (curSec < openSec) ? nowLocal.Date.AddDays(-1) : nowLocal.Date;
			dayOpenLocal = tradingDate.AddSeconds(openSec);

			DateTime nextDate = tradingDate.AddDays(1);
			while (nextDate.DayOfWeek == DayOfWeek.Saturday || nextDate.DayOfWeek == DayOfWeek.Sunday)
				nextDate = nextDate.AddDays(1);
			nextDayOpenLocal = nextDate.AddSeconds(openSec);
			dayKey = tradingDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
		}

		private DailyRiskDisciplineRecord GetOrCreateTodayRiskRecord(Account acc, DateTime nowLocal, out DateTime dayOpenLocal, out DateTime nextDayOpenLocal)
		{
			EnsureSharedRiskDisciplineFileLoaded();
			ResolveCurrentTradingDayWindow(nowLocal, out string dayKey, out dayOpenLocal, out nextDayOpenLocal);
			string accName = (acc != null && !string.IsNullOrWhiteSpace(acc.Name)) ? acc.Name.Trim() : "Default";
			string mapKey = accName + "|" + dayKey;
			DateTime capturedNextOpen = nextDayOpenLocal;

			DailyRiskDisciplineRecord rec = SharedDailyRiskRecords.GetOrAdd(mapKey, _ => new DailyRiskDisciplineRecord
			{
				AccountAndDayKey = mapKey,
				UnlockLocalTime = capturedNextOpen,
				IsLocked = false,
				LockReason = string.Empty
			});

			if (nowLocal >= rec.UnlockLocalTime && rec.IsLocked)
			{
				rec.IsLocked = false;
				rec.LockReason = string.Empty;
			}
			rec.UnlockLocalTime = nextDayOpenLocal;
			return rec;
		}

		private int RefreshTodayEntryTradeCountNonBlocking(Account acc, DailyRiskDisciplineRecord rec, DateTime dayOpenLocal)
		{
			if (rec == null)
				return 0;
			if (acc != null && acc.Executions != null)
			{
				bool entered = false;
				Execution[] execSnap = null;
				try
				{
					System.Threading.Monitor.TryEnter(acc.Executions, 0, ref entered);
					if (entered)
						execSnap = acc.Executions.ToArray();
				}
				catch { }
				finally
				{
					if (entered)
						System.Threading.Monitor.Exit(acc.Executions);
				}

				if (execSnap != null)
				{
					bool addedAny = false;
					for (int i = 0; i < execSnap.Length; i++)
					{
						Execution ex = execSnap[i];
						if (ex == null || ex.Order == null || ex.Time < dayOpenLocal)
							continue;
						OrderAction oa = ex.Order.OrderAction;
						if (oa == OrderAction.Buy || oa == OrderAction.SellShort)
						{
							string oid = !string.IsNullOrWhiteSpace(ex.Order.OrderId)
								? ex.Order.OrderId
								: (ex.Order.Id > 0 ? ex.Order.Id.ToString(CultureInfo.InvariantCulture) : ex.ExecutionId);
							if (!string.IsNullOrWhiteSpace(oid))
							{
								lock (riskPersistLock)
								{
									if (rec.FilledEntryOrderIds.Add(oid))
										addedAny = true;
								}
							}
						}
					}
					if (addedAny)
						SaveSharedRiskDisciplineFile();
				}
			}
			lock (riskPersistLock)
			{
				return rec.FilledEntryOrderIds.Count;
			}
		}

		private void LatchDailyTradingLockout(DailyRiskDisciplineRecord rec, string reason, DateTime nextDayOpenLocal)
		{
			if (rec == null)
				return;
			bool changed = false;
			lock (riskPersistLock)
			{
				if (!rec.IsLocked)
				{
					rec.IsLocked = true;
					rec.LockReason = reason;
					rec.UnlockLocalTime = nextDayOpenLocal;
					changed = true;
				}
			}
			if (changed)
			{
				SaveSharedRiskDisciplineFile();
				ShowTradeToast("🔒 " + reason + " — Locked until " + nextDayOpenLocal.ToString("hh:mm tt", CultureInfo.InvariantCulture), true);
				RequestTradeUiSyncAsync();
				RequestImmediateOrderflowRedraw();
			}
		}

		private void TriggerEmergencyFlattenPosition(Account acc, Instrument inst, string toastMessage)
		{
			if (acc == null || inst == null)
				return;
			DateTime nowUtc = DateTime.UtcNow;
			if ((nowUtc - lastEmergencyFlattenUtc).TotalSeconds < 1.5)
				return;
			lastEmergencyFlattenUtc = nowUtc;

			try
			{
				lock (bracketLock)
				{
					pendingEntryBrackets.Clear();
					activeTrailingOrders.Clear();
				}
				acc.Flatten(new Instrument[] { inst });
				ShowTradeToast(toastMessage, true);
				RequestTradeUiSyncAsync();
				RequestImmediateOrderflowRedraw();
			}
			catch { }
		}

		private int ComputeCapitalProtectorMaxSlTicks(int qty)
		{
			if (!EnableCapitalProtector || CapitalProtectorAmount <= 0)
				return 100000;
			double tick = TickSize > 0 ? TickSize : 0.05;
			double pv = (Instrument != null && Instrument.MasterInstrument != null && Instrument.MasterInstrument.PointValue > 0)
				? Instrument.MasterInstrument.PointValue
				: 1.0;
			double riskPerTick = Math.Max(1e-6, Math.Max(1, qty) * tick * pv);
			return Math.Max(1, (int)Math.Floor((CapitalProtectorAmount + 1e-6) / riskPerTick));
		}

		private void EnforceRiskAndCapitalProtectorOnTick(double liveTickPrice)
		{
			Account acc = activeTradeAccount ?? EnsureActiveTradeAccountSelected();
			Instrument inst = Instrument;
			if (acc == null || inst == null)
				return;

			DateTime nowLocal = DateTime.Now;
			try
			{
				if (Connection.PlaybackConnection != null && Connection.PlaybackConnection.Status == ConnectionStatus.Connected)
					nowLocal = Connection.PlaybackConnection.Now;
			}
			catch { }

			DailyRiskDisciplineRecord rec = GetOrCreateTodayRiskRecord(acc, nowLocal, out DateTime dayOpenLocal, out DateTime nextDayOpenLocal);
			int tradesDone = RefreshTodayEntryTradeCountNonBlocking(acc, rec, dayOpenLocal);

			Position pos = GetCurrentInstrumentPosition(acc, nonBlocking: true);
			bool hasOpenPos = (pos != null && pos.MarketPosition != MarketPosition.Flat && pos.Quantity > 0);
			if (hasOpenPos)
			{
				if (posOpenedObservedUtc == DateTime.MinValue)
					posOpenedObservedUtc = DateTime.UtcNow;
			}
			else
			{
				posOpenedObservedUtc = DateTime.MinValue;
			}

			ResolveLiveBidAskLast(out double bid, out double ask, out double last);
			double pxRef = liveTickPrice > 0 ? liveTickPrice : last;
			double pv = (inst.MasterInstrument != null && inst.MasterInstrument.PointValue > 0) ? inst.MasterInstrument.PointValue : 1.0;
			double tick = TickSize > 0 ? TickSize : 0.05;
			string curSym = GetAccountCurrencyPrefix(acc);

			double rpl = 0.0;
			try { rpl = acc.Get(AccountItem.RealizedProfitLoss, acc.Denomination); } catch { }

			double openUpl = 0.0;
			if (hasOpenPos && pxRef > 0)
			{
				bool pLong = (pos.MarketPosition == MarketPosition.Long);
				double exitEvalPx = pLong ? (bid > 0 ? bid : pxRef) : (ask > 0 ? ask : pxRef);
				openUpl = (pLong ? (exitEvalPx - pos.AveragePrice) : (pos.AveragePrice - exitEvalPx)) * pos.Quantity * pv;
			}
			double dailyNetPnl = rpl + openUpl;

			cachedTradesDoneToday = tradesDone;
			cachedDailyNetPnl = dailyNetPnl;
			cachedUnlockLocalTime = nextDayOpenLocal;

			// 1. GOD-MODE CAPITAL PROTECTOR (Instant Sub-Millisecond Spike / News / Big-Candle Capital Shield)
			if (EnableCapitalProtector && CapitalProtectorAmount > 0 && hasOpenPos)
			{
				if (openUpl <= -CapitalProtectorAmount - 1e-6)
				{
					string capMsg = string.Format(CultureInfo.InvariantCulture, "🛡 Capital Protector Saved ({0}{1:0.##} Limit)", curSym, CapitalProtectorAmount);
					TriggerEmergencyFlattenPosition(acc, inst, capMsg + " — Emergency Flatten!");
					if (CapitalProtectorLockDayOnTrigger)
						LatchDailyTradingLockout(rec, capMsg, nextDayOpenLocal);
					cachedIsTradingLocked = rec.IsLocked;
					cachedLockReason = rec.LockReason;
					return;
				}

				// Auto-inject / clamp server-side protective StopMarket if open position is missing SL for > 2.2s (protects against naked spikes!)
				DateTime nowUtc = DateTime.UtcNow;
				if ((nowUtc - posOpenedObservedUtc).TotalSeconds >= 2.2 && (nowUtc - lastMissingSlGuardCheckUtc).TotalSeconds >= 2.0)
				{
					lastMissingSlGuardCheckUtc = nowUtc;
					List<Order> wos = GetCurrentInstrumentWorkingOrders(acc, nonBlocking: true);
					Order existingSl = null;
					for (int i = 0; i < wos.Count; i++)
					{
						if (ClassifyWorkingOrderRole(wos[i], pos) == 2)
						{
							existingSl = wos[i];
							break;
						}
					}

					int maxTicks = ComputeCapitalProtectorMaxSlTicks(pos.Quantity);
					bool pLong = (pos.MarketPosition == MarketPosition.Long);
					double maxAllowedSlPx = RoundToInstrumentTick(pLong ? (pos.AveragePrice - maxTicks * tick) : (pos.AveragePrice + maxTicks * tick));

					if (existingSl == null)
					{
						// Check no pending bracket is still mid-flight
						bool hasPendingInFlight = false;
						lock (bracketLock) { hasPendingInFlight = pendingEntryBrackets.Count > 0; }
						if (!hasPendingInFlight)
						{
							double safeSl = pLong
								? ((bid > 0 && maxAllowedSlPx >= bid) ? RoundToInstrumentTick(bid - tick) : maxAllowedSlPx)
								: ((ask > 0 && maxAllowedSlPx <= ask) ? RoundToInstrumentTick(ask + tick) : maxAllowedSlPx);
							try
							{
								Order guardSl = acc.CreateOrder(
									inst,
									pLong ? OrderAction.Sell : OrderAction.BuyToCover,
									OrderType.StopMarket,
									OrderEntry.Manual,
									selectedOrderTif,
									pos.Quantity,
									0.0,
									safeSl,
									string.Empty,
									"SL",
									Globals.MaxDate,
									null);
								if (guardSl != null)
								{
									acc.Submit(new[] { guardSl });
									ShowTradeToast("🛡 Capital Protector attached hard SL @ " + FormatOrderflowPrice(safeSl));
								}
							}
							catch { }
						}
					}
					else
					{
						double curSlPx = GetWorkingOrderEffectivePrice(existingSl);
						if (curSlPx > 0)
						{
							bool exceedsCap = pLong ? (curSlPx < maxAllowedSlPx - tick * 0.5) : (curSlPx > maxAllowedSlPx + tick * 0.5);
							if (exceedsCap)
								ModifyLiveWorkingOrderPrice(acc, existingSl, maxAllowedSlPx, silentToast: true);
						}
					}
				}
			}

			// 2. DAILY DISCIPLINE ENGINE (Max Trades Per Day, Per-Day Profit Target, Per-Day StopLoss -> Lock Until Next Day Open)
			if (EnableRiskManagement)
			{
				if (DailyProfitTargetAmount > 0 && dailyNetPnl >= DailyProfitTargetAmount - 1e-6)
				{
					string reason = string.Format(CultureInfo.InvariantCulture, "Daily Target Reached (+{0}{1:0.##})", curSym, dailyNetPnl);
					if (hasOpenPos && AutoFlattenOnDailyLimit)
						TriggerEmergencyFlattenPosition(acc, inst, "🎯 " + reason + " — Auto-Closed!");
					LatchDailyTradingLockout(rec, reason, nextDayOpenLocal);
				}
				else if (DailyMaxLossAmount > 0 && dailyNetPnl <= -Math.Abs(DailyMaxLossAmount) + 1e-6)
				{
					string reason = string.Format(CultureInfo.InvariantCulture, "Daily StopLoss Hit (-{0}{1:0.##})", curSym, Math.Abs(dailyNetPnl));
					if (hasOpenPos && AutoFlattenOnDailyLimit)
						TriggerEmergencyFlattenPosition(acc, inst, "🛑 " + reason + " — Auto-Closed!");
					LatchDailyTradingLockout(rec, reason, nextDayOpenLocal);
				}
				else if (MaxTradesPerDay > 0 && tradesDone >= MaxTradesPerDay && !hasOpenPos)
				{
					string reason = string.Format(CultureInfo.InvariantCulture, "Max Trades Done ({0}/{1})", tradesDone, MaxTradesPerDay);
					LatchDailyTradingLockout(rec, reason, nextDayOpenLocal);
				}
			}

			cachedIsTradingLocked = rec.IsLocked;
			cachedLockReason = rec.LockReason;
		}

		private bool IsNewOrderBlockedByRiskDiscipline(Account acc, out string blockReason, out DateTime unlockAtLocal)
		{
			blockReason = string.Empty;
			unlockAtLocal = DateTime.MinValue;
			if (!EnableRiskManagement && !EnableCapitalProtector)
				return false;

			DateTime nowLocal = DateTime.Now;
			try
			{
				if (Connection.PlaybackConnection != null && Connection.PlaybackConnection.Status == ConnectionStatus.Connected)
					nowLocal = Connection.PlaybackConnection.Now;
			}
			catch { }

			DailyRiskDisciplineRecord rec = GetOrCreateTodayRiskRecord(acc, nowLocal, out DateTime dayOpenLocal, out DateTime nextDayOpenLocal);
			unlockAtLocal = nextDayOpenLocal;

			if (rec.IsLocked && nowLocal < nextDayOpenLocal)
			{
				blockReason = !string.IsNullOrEmpty(rec.LockReason) ? rec.LockReason : "Daily Limit Reached";
				return true;
			}

			if (!EnableRiskManagement)
				return false;

			int tradesDone = RefreshTodayEntryTradeCountNonBlocking(acc, rec, dayOpenLocal);
			Position pos = GetCurrentInstrumentPosition(acc, nonBlocking: true);
			List<Order> working = GetCurrentInstrumentWorkingOrders(acc, nonBlocking: true);
			int pendingEntryCount = 0;
			for (int i = 0; i < working.Count; i++)
			{
				if (ClassifyWorkingOrderRole(working[i], pos) == 0)
					pendingEntryCount++;
			}

			if (MaxTradesPerDay > 0 && (tradesDone + pendingEntryCount) >= MaxTradesPerDay)
			{
				blockReason = tradesDone >= MaxTradesPerDay
					? string.Format(CultureInfo.InvariantCulture, "Max Trades Reached ({0}/{1})", tradesDone, MaxTradesPerDay)
					: string.Format(CultureInfo.InvariantCulture, "Max Trades Allocated ({0} Done + {1} Working = {2}/{2})", tradesDone, pendingEntryCount, MaxTradesPerDay);
				if (tradesDone >= MaxTradesPerDay && (pos == null || pos.MarketPosition == MarketPosition.Flat))
					LatchDailyTradingLockout(rec, blockReason, nextDayOpenLocal);
				return true;
			}

			string curSym = GetAccountCurrencyPrefix(acc);
			double rpl = 0.0, upl = 0.0;
			try { if (acc != null) rpl = acc.Get(AccountItem.RealizedProfitLoss, acc.Denomination); } catch { }
			if (pos != null && pos.MarketPosition != MarketPosition.Flat && pos.Quantity > 0)
			{
				ResolveLiveBidAskLast(out _, out _, out double last);
				try { upl = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency, last); } catch { }
			}
			double netPnl = rpl + upl;

			if (DailyProfitTargetAmount > 0 && netPnl >= DailyProfitTargetAmount - 1e-6)
			{
				blockReason = string.Format(CultureInfo.InvariantCulture, "Daily Target Reached (+{0}{1:0.##})", curSym, netPnl);
				LatchDailyTradingLockout(rec, blockReason, nextDayOpenLocal);
				return true;
			}

			if (DailyMaxLossAmount > 0 && netPnl <= -Math.Abs(DailyMaxLossAmount) + 1e-6)
			{
				blockReason = string.Format(CultureInfo.InvariantCulture, "Daily StopLoss Hit (-{0}{1:0.##})", curSym, Math.Abs(netPnl));
				LatchDailyTradingLockout(rec, blockReason, nextDayOpenLocal);
				return true;
			}

			return false;
		}

		private PendingBracketSpec FindPendingBracketForEntryOrder(Order entryOrd)
		{
			if (entryOrd == null)
				return null;
			lock (bracketLock)
			{
				for (int i = pendingEntryBrackets.Count - 1; i >= 0; i--)
				{
					PendingBracketSpec sp = pendingEntryBrackets[i];
					if (sp == null)
						continue;
					if (ReferenceEquals(sp.EntryOrder, entryOrd) ||
						(sp.EntryOrder != null && !string.IsNullOrEmpty(sp.EntryOrder.OrderId) && string.Equals(sp.EntryOrder.OrderId, entryOrd.OrderId, StringComparison.Ordinal)))
					{
						return sp;
					}
				}
			}
			return null;
		}

		private void ShowTradeToast(string msg, bool isError = false)
		{
			tradeToastExpireUtc = DateTime.UtcNow.AddSeconds(4.5);
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (cc == null || txtTradeStatusToast == null)
				return;

			try
			{
				cc.Dispatcher.InvokeAsync(() =>
				{
					if (txtTradeStatusToast == null)
						return;
					txtTradeStatusToast.Text = msg ?? string.Empty;
					txtTradeStatusToast.Foreground = isError
						? MakeFrozenBrush(248, 113, 113)
						: MakeFrozenBrush(52, 211, 153);
				});
			}
			catch { }
		}

		private void OnSelectedAccountItemUpdate(object sender, AccountItemEventArgs e)
		{
			if (State == State.Terminated)
				return;
			RequestTradeUiSyncAsync();
		}

		private void OnSelectedAccountPositionUpdate(object sender, PositionEventArgs e)
		{
			if (State == State.Terminated || e == null)
				return;

			try
			{
				if (e.Position != null && InstrumentMatchesChart(e.Position.Instrument) && e.MarketPosition == MarketPosition.Flat)
				{
					// Position closed -> automatically cancel any leftover AK10X protective SL/TP exit orders so they cannot reverse!
					Account acc = e.Position.Account ?? activeTradeAccount;
					if (acc != null)
					{
						List<Order> working = GetCurrentInstrumentWorkingOrders(acc);
						List<Order> orphanExits = new List<Order>();
						for (int i = 0; i < working.Count; i++)
						{
							Order wo = working[i];
							string nm = wo.Name ?? string.Empty;
							if (nm.StartsWith("AK10X_SL", StringComparison.OrdinalIgnoreCase) ||
								nm.StartsWith("AK10X_TP", StringComparison.OrdinalIgnoreCase) ||
								string.Equals(nm, "SL", StringComparison.OrdinalIgnoreCase) ||
								string.Equals(nm, "TP", StringComparison.OrdinalIgnoreCase))
							{
								orphanExits.Add(wo);
							}
						}
						if (orphanExits.Count > 0)
						{
							try { acc.Cancel(orphanExits.ToArray()); } catch { }
						}
					}

					lock (bracketLock)
					{
						activeTrailingOrders.Clear();
					}
				}
			}
			catch { }

			RequestTradeUiSyncAsync();
			RequestThrottledChartRefresh(true);
		}

		private void OnSelectedAccountOrderUpdate(object sender, OrderEventArgs e)
		{
			if (State == State.Terminated || e == null || e.Order == null)
				return;

			try
			{
				if (InstrumentMatchesChart(e.Order.Instrument))
				{
					if ((e.OrderState == OrderState.Filled || e.OrderState == OrderState.PartFilled) &&
						(e.Order.OrderAction == OrderAction.Buy || e.Order.OrderAction == OrderAction.SellShort))
					{
						Account ordAcc = e.Order.Account ?? activeTradeAccount;
						DateTime nowLoc = DateTime.Now;
						try
						{
							if (Connection.PlaybackConnection != null && Connection.PlaybackConnection.Status == ConnectionStatus.Connected)
								nowLoc = Connection.PlaybackConnection.Now;
						}
						catch { }
						DailyRiskDisciplineRecord rec = GetOrCreateTodayRiskRecord(ordAcc, nowLoc, out _, out _);
						string oid = !string.IsNullOrWhiteSpace(e.Order.OrderId)
							? e.Order.OrderId
							: (e.Order.Id > 0 ? e.Order.Id.ToString(CultureInfo.InvariantCulture) : string.Empty);
						if (!string.IsNullOrEmpty(oid))
						{
							bool added = false;
							lock (riskPersistLock) { added = rec.FilledEntryOrderIds.Add(oid); }
							if (added) SaveSharedRiskDisciplineFile();
						}
					}

					TryProcessPendingBracketOnOrderUpdate(e);
				}
			}
			catch { }

			EnforceRiskAndCapitalProtectorOnTick(0.0);

			RequestTradeUiSyncAsync();
			RequestThrottledChartRefresh(true);
		}

		private void TryProcessPendingBracketOnOrderUpdate(OrderEventArgs e)
		{
			Order ord = e.Order;
			if (ord == null)
				return;

			PendingBracketSpec matched = null;
			int deltaQtyToBracket = 0;

			lock (bracketLock)
			{
				for (int i = pendingEntryBrackets.Count - 1; i >= 0; i--)
				{
					PendingBracketSpec sp = pendingEntryBrackets[i];
					if ((DateTime.UtcNow - sp.CreatedUtc).TotalHours > 12.0)
					{
						pendingEntryBrackets.RemoveAt(i);
						continue;
					}

					bool sameOrder = ReferenceEquals(sp.EntryOrder, ord) ||
						(sp.EntryOrder != null && !string.IsNullOrEmpty(sp.EntryOrder.OrderId) && string.Equals(sp.EntryOrder.OrderId, ord.OrderId, StringComparison.Ordinal)) ||
						(!string.IsNullOrEmpty(sp.EntryTag) && string.Equals(ord.Name, sp.EntryTag, StringComparison.Ordinal));
					if (!sameOrder)
						continue;

					if (e.OrderState == OrderState.Cancelled || e.OrderState == OrderState.Rejected)
					{
						pendingEntryBrackets.RemoveAt(i);
						break;
					}

					if ((e.OrderState == OrderState.Filled || e.OrderState == OrderState.PartFilled) && e.Filled > sp.BracketedQty)
					{
						deltaQtyToBracket = e.Filled - sp.BracketedQty;
						sp.BracketedQty = e.Filled;
						matched = sp;
						if (e.OrderState == OrderState.Filled || sp.BracketedQty >= sp.TotalQty)
							pendingEntryBrackets.RemoveAt(i);
						break;
					}
				}
			}

			if (matched == null || deltaQtyToBracket <= 0 || (!matched.HasSl && !matched.HasTp))
				return;

			Account acc = matched.Account ?? activeTradeAccount;
			Instrument inst = matched.Instrument ?? Instrument;
			if (acc == null || inst == null)
				return;

			// Confirm we actually hold an open position in the entry direction (avoid orphaned brackets if the order merely flattened an opposite position)
			Position pos = GetCurrentInstrumentPosition(acc);
			int attachQty = deltaQtyToBracket;
			if (pos != null)
			{
				bool posIsLong = (pos.MarketPosition == MarketPosition.Long);
				if (posIsLong != matched.IsLong)
					return;
				attachQty = Math.Min(deltaQtyToBracket, Math.Max(1, pos.Quantity));
			}

			double tick = TickSize > 0 ? TickSize : 0.05;
			ResolveLiveBidAskLast(out double bid, out double ask, out double last);
			double fillAnchor = e.AverageFillPrice > 0 ? e.AverageFillPrice : (ord.AverageFillPrice > 0 ? ord.AverageFillPrice : last);

			OrderAction exitAction = matched.IsLong ? OrderAction.Sell : OrderAction.BuyToCover;
			string ocoId = (matched.HasSl && matched.HasTp)
				? ("AK10X_OCO_" + Guid.NewGuid().ToString("N").Substring(0, 10))
				: string.Empty;

			List<Order> toSubmit = new List<Order>(2);
			Order createdSlOrder = null;

			if (matched.HasSl && matched.SlTicks > 0)
			{
				double rawSl = matched.IsLong
					? (fillAnchor - matched.SlTicks * tick)
					: (fillAnchor + matched.SlTicks * tick);
				// Keep StopMarket safely on valid side of market
				if (matched.IsLong && bid > 0 && rawSl >= bid)
					rawSl = bid - tick;
				else if (!matched.IsLong && ask > 0 && rawSl <= ask)
					rawSl = ask + tick;
				double slPx = RoundToInstrumentTick(rawSl);

				createdSlOrder = acc.CreateOrder(
					inst,
					exitAction,
					OrderType.StopMarket,
					OrderEntry.Manual,
					matched.Tif,
					attachQty,
					0.0,
					slPx,
					ocoId,
					"SL",
					Globals.MaxDate,
					null);
				if (createdSlOrder != null)
					toSubmit.Add(createdSlOrder);
			}

			if (matched.HasTp && matched.TpTicks > 0)
			{
				double rawTp = matched.IsLong
					? (fillAnchor + matched.TpTicks * tick)
					: (fillAnchor - matched.TpTicks * tick);
				if (matched.IsLong && ask > 0 && rawTp <= ask)
					rawTp = ask + tick;
				else if (!matched.IsLong && bid > 0 && rawTp >= bid)
					rawTp = bid - tick;
				double tpPx = RoundToInstrumentTick(rawTp);

				Order tpOrd = acc.CreateOrder(
					inst,
					exitAction,
					OrderType.Limit,
					OrderEntry.Manual,
					matched.Tif,
					attachQty,
					tpPx,
					0.0,
					ocoId,
					"TP",
					Globals.MaxDate,
					null);
				if (tpOrd != null)
					toSubmit.Add(tpOrd);
			}

			if (toSubmit.Count > 0)
			{
				acc.Submit(toSubmit.ToArray());

				if (createdSlOrder != null && matched.TrailEnabled && matched.TrailTicks > 0)
				{
					lock (bracketLock)
					{
						activeTrailingOrders.Add(new ActiveTrailingOrderState
						{
							StopOrder = createdSlOrder,
							Account = acc,
							IsLong = matched.IsLong,
							TrailTicks = matched.TrailTicks,
							BestExtremePrice = fillAnchor,
							LastChangeSentUtc = DateTime.UtcNow
						});
					}
				}
			}
		}

		private void UpdateLiveTrailingStopsOnTick(double tickPrice)
		{
			if (tickPrice <= 0)
				return;

			List<Tuple<Order, Account, double>> pendingMoves = null;
			double tick = TickSize > 0 ? TickSize : 0.05;

			lock (bracketLock)
			{
				if (activeTrailingOrders.Count == 0)
					return;

				DateTime nowUtc = DateTime.UtcNow;
				for (int i = activeTrailingOrders.Count - 1; i >= 0; i--)
				{
					ActiveTrailingOrderState tr = activeTrailingOrders[i];
					if (tr.StopOrder == null || tr.Account == null ||
						tr.StopOrder.OrderState == OrderState.Filled ||
						tr.StopOrder.OrderState == OrderState.Cancelled ||
						tr.StopOrder.OrderState == OrderState.Rejected)
					{
						activeTrailingOrders.RemoveAt(i);
						continue;
					}

					if (tr.StopOrder.OrderState != OrderState.Working && tr.StopOrder.OrderState != OrderState.Accepted)
						continue;

					if (tr.IsLong)
					{
						if (tickPrice > tr.BestExtremePrice)
							tr.BestExtremePrice = tickPrice;
						double desiredStop = RoundToInstrumentTick(tr.BestExtremePrice - tr.TrailTicks * tick);
						if (desiredStop >= tr.StopOrder.StopPrice + tick - 1e-9 && (nowUtc - tr.LastChangeSentUtc).TotalMilliseconds >= 350.0)
						{
							tr.LastChangeSentUtc = nowUtc;
							if (pendingMoves == null) pendingMoves = new List<Tuple<Order, Account, double>>();
							pendingMoves.Add(Tuple.Create(tr.StopOrder, tr.Account, desiredStop));
						}
					}
					else
					{
						if (tr.BestExtremePrice <= 0 || tickPrice < tr.BestExtremePrice)
							tr.BestExtremePrice = tickPrice;
						double desiredStop = RoundToInstrumentTick(tr.BestExtremePrice + tr.TrailTicks * tick);
						if (desiredStop <= tr.StopOrder.StopPrice - tick + 1e-9 && (nowUtc - tr.LastChangeSentUtc).TotalMilliseconds >= 350.0)
						{
							tr.LastChangeSentUtc = nowUtc;
							if (pendingMoves == null) pendingMoves = new List<Tuple<Order, Account, double>>();
							pendingMoves.Add(Tuple.Create(tr.StopOrder, tr.Account, desiredStop));
						}
					}
				}
			}

			if (pendingMoves != null)
			{
				for (int i = 0; i < pendingMoves.Count; i++)
				{
					ModifyLiveWorkingOrderPrice(pendingMoves[i].Item2, pendingMoves[i].Item1, pendingMoves[i].Item3, silentToast: true);
				}
			}
		}

		private bool ModifyLiveWorkingOrderPrice(Account acc, Order ord, double rawNewPrice, bool silentToast = false)
		{
			if (acc == null || ord == null || rawNewPrice <= 0)
				return false;

			try
			{
				double newPrice = RoundToInstrumentTick(rawNewPrice);
				double curPrice = GetWorkingOrderEffectivePrice(ord);
				double tick = TickSize > 0 ? TickSize : 0.05;
				if (Math.Abs(newPrice - curPrice) < tick * 0.25)
					return false;

				ord.QuantityChanged = ord.Quantity;
				if (ord.OrderType == OrderType.StopMarket)
				{
					ord.StopPriceChanged = newPrice;
					ord.LimitPriceChanged = 0.0;
				}
				else if (ord.OrderType == OrderType.StopLimit)
				{
					ord.StopPriceChanged = newPrice;
					ord.LimitPriceChanged = newPrice;
				}
				else
				{
					ord.LimitPriceChanged = newPrice;
					ord.StopPriceChanged = ord.StopPrice;
				}

				acc.Change(new[] { ord });

				// If this working Entry order has a pending bracket spec, keep it linked seamlessly!
				if (!silentToast)
				{
					ShowTradeToast("Modified " + (ord.OrderType == OrderType.StopMarket ? "Stop" : "Limit") + " → " + FormatOrderflowPrice(newPrice));
				}
				RequestImmediateOrderflowRedraw();
				return true;
			}
			catch (Exception ex)
			{
				if (!silentToast)
					ShowTradeToast("Modify failed: " + ex.Message, true);
				return false;
			}
		}

		private void SubmitProtectiveExitOrderFromChart(bool isTakeProfit, double rawTargetPrice)
		{
			Account acc = EnsureActiveTradeAccountSelected();
			Instrument inst = Instrument;
			if (acc == null || inst == null)
			{
				ShowTradeToast("No active NinjaTrader account selected", true);
				return;
			}

			Position pos = GetCurrentInstrumentPosition(acc);
			if (pos == null || pos.MarketPosition == MarketPosition.Flat || pos.Quantity <= 0)
			{
				ShowTradeToast("No open position to attach " + (isTakeProfit ? "TP" : "SL"), true);
				return;
			}

			double tick = TickSize > 0 ? TickSize : 0.05;
			ResolveLiveBidAskLast(out double bid, out double ask, out _);
			bool posLong = (pos.MarketPosition == MarketPosition.Long);
			OrderAction exitAct = posLong ? OrderAction.Sell : OrderAction.BuyToCover;
			double px = RoundToInstrumentTick(rawTargetPrice);

			// Check if a working protective order of this same role already exists -> modify it instead of duplicating!
			List<Order> working = GetCurrentInstrumentWorkingOrders(acc);
			for (int i = 0; i < working.Count; i++)
			{
				int role = ClassifyWorkingOrderRole(working[i], pos);
				if ((isTakeProfit && role == 1) || (!isTakeProfit && role == 2))
				{
					ModifyLiveWorkingOrderPrice(acc, working[i], px);
					return;
				}
			}

			try
			{
				if (isTakeProfit)
				{
					if (posLong && ask > 0 && px <= ask) px = RoundToInstrumentTick(ask + tick);
					else if (!posLong && bid > 0 && px >= bid) px = RoundToInstrumentTick(bid - tick);

					Order tpOrd = acc.CreateOrder(
						inst, exitAct, OrderType.Limit, OrderEntry.Manual, selectedOrderTif,
						pos.Quantity, px, 0.0, string.Empty, "TP", Globals.MaxDate, null);
					acc.Submit(new[] { tpOrd });
					ShowTradeToast("Placed TP Limit @ " + FormatOrderflowPrice(px));
				}
				else
				{
					if (posLong && bid > 0 && px >= bid) px = RoundToInstrumentTick(bid - tick);
					else if (!posLong && ask > 0 && px <= ask) px = RoundToInstrumentTick(ask + tick);

					Order slOrd = acc.CreateOrder(
						inst, exitAct, OrderType.StopMarket, OrderEntry.Manual, selectedOrderTif,
						pos.Quantity, 0.0, px, string.Empty, "SL", Globals.MaxDate, null);
					acc.Submit(new[] { slOrd });
					ShowTradeToast("Placed SL Stop @ " + FormatOrderflowPrice(px));
				}
				RequestImmediateOrderflowRedraw();
			}
			catch (Exception ex)
			{
				ShowTradeToast("Order error: " + ex.Message, true);
			}
		}

		private void ExecuteSubmitOrderCore(
			bool isBuy,
			OrderType ordType,
			double entryLimitOrStopPx,
			int qty,
			bool attachSl,
			int slTicks,
			bool attachTp,
			int tpTicks,
			bool enableTrail,
			int trailTicks)
		{
			Account acc = EnsureActiveTradeAccountSelected();
			Instrument inst = Instrument;
			if (acc == null || inst == null)
			{
				ShowTradeToast("Select a connected NinjaTrader Account first", true);
				return;
			}

			if (IsNewOrderBlockedByRiskDiscipline(acc, out string lockReason, out DateTime unlockAt))
			{
				ShowTradeToast("🔒 Order Blocked: " + lockReason + " (Unlocks " + unlockAt.ToString("hh:mm tt", CultureInfo.InvariantCulture) + ")", true);
				RequestTradeUiSyncAsync();
				return;
			}

			int safeQty = Math.Max(1, qty);
			if (EnableCapitalProtector && CapitalProtectorAmount > 0)
			{
				int maxCapSlTicks = ComputeCapitalProtectorMaxSlTicks(safeQty);
				if (!attachSl || slTicks <= 0)
				{
					attachSl = true;
					slTicks = Math.Min(Math.Max(1, DefaultSlTicks), maxCapSlTicks);
				}
				else if (slTicks > maxCapSlTicks)
				{
					slTicks = maxCapSlTicks;
					ShowTradeToast("🛡 Capital Protector capped SL to " + slTicks + "T (" + GetAccountCurrencyPrefix(acc) + CapitalProtectorAmount.ToString("0.##", CultureInfo.InvariantCulture) + " max)");
				}
			}

			Position curPos = GetCurrentInstrumentPosition(acc);

			OrderAction action;
			if (isBuy)
				action = (curPos != null && curPos.MarketPosition == MarketPosition.Short) ? OrderAction.BuyToCover : OrderAction.Buy;
			else
				action = (curPos != null && curPos.MarketPosition == MarketPosition.Long) ? OrderAction.Sell : OrderAction.SellShort;

			double lmtPx = 0.0;
			double stpPx = 0.0;
			if (ordType == OrderType.Limit)
				lmtPx = RoundToInstrumentTick(entryLimitOrStopPx);
			else if (ordType == OrderType.StopMarket)
				stpPx = RoundToInstrumentTick(entryLimitOrStopPx);

			string entryTag = isBuy ? "Buy" : "Sell";

			try
			{
				Order entryOrd = acc.CreateOrder(
					inst,
					action,
					ordType,
					OrderEntry.Manual,
					selectedOrderTif,
					safeQty,
					lmtPx,
					stpPx,
					string.Empty,
					entryTag,
					Globals.MaxDate,
					null);

				if (entryOrd == null)
				{
					ShowTradeToast("Failed to create order", true);
					return;
				}

				if (attachSl || attachTp)
				{
					lock (bracketLock)
					{
						pendingEntryBrackets.Add(new PendingBracketSpec
						{
							EntryOrder = entryOrd,
							EntryTag = string.Empty,
							Account = acc,
							Instrument = inst,
							IsLong = isBuy,
							TotalQty = safeQty,
							BracketedQty = 0,
							Tif = selectedOrderTif,
							HasSl = attachSl && slTicks > 0,
							SlTicks = Math.Max(1, slTicks),
							HasTp = attachTp && tpTicks > 0,
							TpTicks = Math.Max(1, tpTicks),
							TrailEnabled = enableTrail && attachSl,
							TrailTicks = Math.Max(1, trailTicks),
							CreatedUtc = DateTime.UtcNow
						});
					}
				}

				acc.Submit(new[] { entryOrd });

				string sideStr = isBuy ? "BUY" : "SELL";
				string typeStr = ordType == OrderType.Market
					? "MKT"
					: ((ordType == OrderType.Limit ? "LMT @ " : "STP @ ") + FormatOrderflowPrice(entryLimitOrStopPx));
				ShowTradeToast("Submitted " + sideStr + " " + safeQty + " " + typeStr);
				RequestImmediateOrderflowRedraw();
			}
			catch (Exception ex)
			{
				ShowTradeToast("Submit error: " + ex.Message, true);
			}
		}

		private void ExecuteMoveStopToBreakeven()
		{
			Account acc = EnsureActiveTradeAccountSelected();
			if (acc == null) return;
			Position pos = GetCurrentInstrumentPosition(acc);
			if (pos == null || pos.MarketPosition == MarketPosition.Flat)
			{
				ShowTradeToast("No open position for Breakeven", true);
				return;
			}

			double bePrice = RoundToInstrumentTick(pos.AveragePrice);
			List<Order> working = GetCurrentInstrumentWorkingOrders(acc);
			for (int i = 0; i < working.Count; i++)
			{
				if (ClassifyWorkingOrderRole(working[i], pos) == 2)
				{
					ModifyLiveWorkingOrderPrice(acc, working[i], bePrice);
					ShowTradeToast("Moved SL to Breakeven @ " + FormatOrderflowPrice(bePrice));
					return;
				}
			}

			SubmitProtectiveExitOrderFromChart(false, bePrice);
		}

		private void ExecuteReverseCurrentPosition()
		{
			Account acc = EnsureActiveTradeAccountSelected();
			Instrument inst = Instrument;
			if (acc == null || inst == null) return;

			Position pos = GetCurrentInstrumentPosition(acc);
			if (pos == null || pos.MarketPosition == MarketPosition.Flat || pos.Quantity <= 0)
			{
				ShowTradeToast("No open position to reverse", true);
				return;
			}

			if (IsNewOrderBlockedByRiskDiscipline(acc, out string revBlockReason, out DateTime revUnlockAt))
			{
				ShowTradeToast("🔒 Reverse Blocked: " + revBlockReason + " (Unlocks " + revUnlockAt.ToString("hh:mm tt", CultureInfo.InvariantCulture) + ")", true);
				return;
			}

			bool wasLong = (pos.MarketPosition == MarketPosition.Long);
			int qty = pos.Quantity;

			try
			{
				CancelAllInstrumentWorkingOrders(silentToast: true);
				acc.Flatten(new Instrument[] { inst });

				// Re-open same quantity in opposite direction on slight dispatcher delay after flatten
				ChartControl cc = hookedOrderflowCC ?? ChartControl;
				if (cc != null)
				{
					cc.Dispatcher.InvokeAsync(async () =>
					{
						await System.Threading.Tasks.Task.Delay(180);
						ExecuteSubmitOrderCore(
							!wasLong,
							OrderType.Market,
							0.0,
							qty,
							orderSlEnabled,
							orderSlTicks,
							orderTpEnabled,
							orderTpTicks,
							orderTrailEnabled,
							orderTrailTicks);
					});
				}
				ShowTradeToast("Reversing to " + (!wasLong ? "LONG " : "SHORT ") + qty);
			}
			catch (Exception ex)
			{
				ShowTradeToast("Reverse error: " + ex.Message, true);
			}
		}

		private void ExecuteFlattenCurrentInstrument()
		{
			Account acc = EnsureActiveTradeAccountSelected();
			Instrument inst = Instrument;
			if (acc == null || inst == null) return;

			try
			{
				lock (bracketLock)
				{
					pendingEntryBrackets.Clear();
					activeTrailingOrders.Clear();
				}
				acc.Flatten(new Instrument[] { inst });
				ShowTradeToast("Closed position & cancelled working orders");
				RequestImmediateOrderflowRedraw();
			}
			catch (Exception ex)
			{
				ShowTradeToast("Flatten error: " + ex.Message, true);
			}
		}

		private void CancelAllInstrumentWorkingOrders(bool silentToast = false)
		{
			Account acc = EnsureActiveTradeAccountSelected();
			if (acc == null) return;

			List<Order> working = GetCurrentInstrumentWorkingOrders(acc);
			if (working.Count == 0)
			{
				if (!silentToast)
					ShowTradeToast("Cancelled draft / no working orders");
				return;
			}

			try
			{
				acc.Cancel(working.ToArray());
				if (!silentToast)
					ShowTradeToast("Cancelled " + working.Count + " working order(s)");
				RequestImmediateOrderflowRedraw();
			}
			catch (Exception ex)
			{
				if (!silentToast)
					ShowTradeToast("Cancel error: " + ex.Message, true);
			}
		}

		// ====================================================================================================
		// DOCKED RIGHT-SIDE TRADESEA WPF PANEL (Scalp | Order Tabs — Zero DOM, Docked Outside D2D Airspace)
		// ====================================================================================================
		private void EnsureTradePanelAttachedAsync()
		{
			ChartControl cc = ChartControl;
			if (cc == null || State == State.Terminated)
				return;

			EnsureActiveTradeAccountSelected();

			if (tradePanelRoot != null && hookedTradeChartWindow == cc.OwnerChart)
				return;

			if (tradePanelAttachQueued)
				return;
			tradePanelAttachQueued = true;

			try
			{
				cc.Dispatcher.InvokeAsync(() =>
				{
					tradePanelAttachQueued = false;
					if (State == State.Terminated || ChartControl == null)
						return;
					AttachTradePanelOnUiThread(ChartControl);
				});
			}
			catch
			{
				tradePanelAttachQueued = false;
			}
		}

		private WpfControls.Grid ResolveChartHostGrid(NinjaTrader.Gui.Chart.Chart chartWin)
		{
			if (chartWin == null)
				return null;
			try
			{
				FieldInfo fi = typeof(NinjaTrader.Gui.Chart.Chart).GetField("grdChart", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				if (fi != null)
				{
					var g = fi.GetValue(chartWin) as WpfControls.Grid;
					if (g != null)
						return g;
				}
			}
			catch { }

			try
			{
				if (chartWin.MainTabControl != null)
				{
					var parent = WpfMedia.VisualTreeHelper.GetParent(chartWin.MainTabControl) as WpfControls.Grid;
					if (parent != null)
						return parent;
				}
			}
			catch { }
			return null;
		}

		private void AttachTradePanelOnUiThread(ChartControl cc)
		{
			if (cc == null || cc.OwnerChart == null)
				return;

			NinjaTrader.Gui.Chart.Chart chartWin = cc.OwnerChart;
			if (tradePanelRoot != null && ReferenceEquals(hookedTradeChartWindow, chartWin))
			{
				UpdateTradePanelVisibilityImmediate();
				return;
			}

			DetachTradePanelOnlyOnUiThread();

			WpfControls.Grid hostGrid = ResolveChartHostGrid(chartWin);
			if (hostGrid == null)
				return;

			// Inherit selected Account from NinjaTrader's ChartTrader before hiding ChartTrader
			try
			{
				if (chartWin.ChartTrader != null && chartWin.ChartTrader.Account != null && activeTradeAccount == null)
					activeTradeAccount = chartWin.ChartTrader.Account;
			}
			catch { }
			EnsureActiveTradeAccountSelected();

			tradePanelRoot = BuildTradeseaRightPanelVisualTree();
			tradePanelColumnDef = new WpfControls.ColumnDefinition
			{
				Width = new GridLength(0, GridUnitType.Pixel)
			};

			hostGrid.ColumnDefinitions.Add(tradePanelColumnDef);
			int colIdx = hostGrid.ColumnDefinitions.Count - 1;
			WpfControls.Grid.SetColumn(tradePanelRoot, colIdx);
			if (hostGrid.RowDefinitions != null && hostGrid.RowDefinitions.Count > 1)
				WpfControls.Grid.SetRowSpan(tradePanelRoot, hostGrid.RowDefinitions.Count);

			hostGrid.Children.Add(tradePanelRoot);
			hookedTradeChartWindow = chartWin;
			hookedChartMainGrid = hostGrid;

			try
			{
				if (chartWin.MainTabControl != null)
				{
					hookedChartTabCtrl = chartWin.MainTabControl;
					hookedChartTabCtrl.SelectionChanged -= OnOwnerChartTabSelectionChanged;
					hookedChartTabCtrl.SelectionChanged += OnOwnerChartTabSelectionChanged;
				}
			}
			catch { }

			if (tradeUiTimer == null)
			{
				tradeUiTimer = new System.Windows.Threading.DispatcherTimer(
					TimeSpan.FromMilliseconds(260),
					System.Windows.Threading.DispatcherPriority.Background,
					(s, e) =>
					{
						if (State == State.Terminated)
							return;
						UpdateTradePanelVisibilityImmediate();
						if (tradePanelRoot != null && tradePanelRoot.Visibility == Visibility.Visible)
							SyncTradePanelUiFromState();

						int curSec = DateTime.UtcNow.Second;
						if (curSec != lastCountdownTickSec)
						{
							lastCountdownTickSec = curSec;
							ChartControl activeCc = hookedOrderflowCC ?? ChartControl;
							if (activeCc != null && IsOrderflowChartControlActive(activeCc))
								RequestImmediateOrderflowRedraw();
						}
					},
					cc.Dispatcher);
			}
			tradeUiTimer.Start();

			UpdateTradePanelVisibilityImmediate();
			SyncTradePanelUiFromState();
		}

		private void OnOwnerChartTabSelectionChanged(object sender, WpfControls.SelectionChangedEventArgs e)
		{
			UpdateTradePanelVisibilityImmediate();
		}

		private void UpdateTradePanelVisibilityImmediate()
		{
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (cc == null)
				return;

			Action applyAct = () =>
			{
				try
				{
					if (tradePanelRoot == null || tradePanelColumnDef == null)
					{
						if (runtimeShowTradePanel && State != State.Terminated && ChartControl != null)
							AttachTradePanelOnUiThread(ChartControl);
						return;
					}

					bool isActiveTab = IsOrderflowChartControlActive(cc);
					bool showNow = runtimeShowTradePanel && isActiveTab && State != State.Terminated;

					if (showNow && HideBuiltInChartTrader && hookedTradeChartWindow != null && hookedTradeChartWindow.ChartTrader != null)
					{
						try
						{
							if (hookedTradeChartWindow.ChartTrader.ChartTraderVisibility == ChartTraderVisibility.Visible)
							{
								if (hookedTradeChartWindow.ChartTrader.Account != null && activeTradeAccount == null)
									SelectTradeAccount(hookedTradeChartWindow.ChartTrader.Account);
								hookedTradeChartWindow.ChartTrader.ChartTraderVisibility = ChartTraderVisibility.Collapsed;
							}
						}
						catch { }
					}

					double targetW = showNow ? 288.0 : 0.0;
					Visibility targetVis = showNow ? Visibility.Visible : Visibility.Collapsed;
					if (tradePanelRoot.Visibility != targetVis)
						tradePanelRoot.Visibility = targetVis;
					if (Math.Abs(tradePanelColumnDef.Width.Value - targetW) > 0.5)
						tradePanelColumnDef.Width = new GridLength(targetW, GridUnitType.Pixel);

					if (showNow)
						SyncTradePanelUiFromState();
				}
				catch { }
			};

			if (cc.Dispatcher.CheckAccess())
				applyAct();
			else
				cc.Dispatcher.InvokeAsync(applyAct);
		}

		private void DetachTradePanelAndAccountEvents()
		{
			UnsubscribeAccountEventsOnly();
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (cc != null && cc.Dispatcher != null)
			{
				try
				{
					if (cc.Dispatcher.CheckAccess())
						DetachTradePanelOnlyOnUiThread();
					else
						cc.Dispatcher.InvokeAsync(DetachTradePanelOnlyOnUiThread);
				}
				catch { }
			}
		}

		private void DetachTradePanelOnlyOnUiThread()
		{
			try
			{
				if (tradeUiTimer != null)
				{
					tradeUiTimer.Stop();
					tradeUiTimer = null;
				}
			}
			catch { }

			try
			{
				if (tradeAccountPopup != null)
				{
					tradeAccountPopup.IsOpen = false;
					tradeAccountPopup = null;
				}
			}
			catch { }

			try
			{
				if (hookedChartTabCtrl != null)
				{
					hookedChartTabCtrl.SelectionChanged -= OnOwnerChartTabSelectionChanged;
					hookedChartTabCtrl = null;
				}
			}
			catch { }

			try
			{
				if (hookedChartMainGrid != null)
				{
					if (tradePanelRoot != null && hookedChartMainGrid.Children.Contains(tradePanelRoot))
						hookedChartMainGrid.Children.Remove(tradePanelRoot);
					if (tradePanelColumnDef != null && hookedChartMainGrid.ColumnDefinitions.Contains(tradePanelColumnDef))
						hookedChartMainGrid.ColumnDefinitions.Remove(tradePanelColumnDef);
				}
			}
			catch { }

			tradePanelRoot = null;
			tradePanelColumnDef = null;
			hookedChartMainGrid = null;
			hookedTradeChartWindow = null;
		}

		private void RequestTradeUiSyncAsync()
		{
			ChartControl cc = hookedOrderflowCC ?? ChartControl;
			if (cc == null || tradePanelRoot == null)
				return;
			if (System.Threading.Interlocked.CompareExchange(ref uiTradeSyncQueued, 1, 0) != 0)
				return;
			try
			{
				cc.Dispatcher.InvokeAsync(() =>
				{
					System.Threading.Interlocked.Exchange(ref uiTradeSyncQueued, 0);
					SyncTradePanelUiFromState();
				}, System.Windows.Threading.DispatcherPriority.Background);
			}
			catch
			{
				System.Threading.Interlocked.Exchange(ref uiTradeSyncQueued, 0);
			}
		}

		private readonly WpfControls.Button[] slPresetBtns = new WpfControls.Button[4];
		private readonly WpfControls.Button[] tpPresetBtns = new WpfControls.Button[4];

		private WpfControls.TextBox CreateDarkInputBox(string initialText, double fontSize = 13.0, System.Windows.TextAlignment align = System.Windows.TextAlignment.Left)
		{
			var tb = new WpfControls.TextBox
			{
				Style = null,
				MinWidth = 0,
				MinHeight = 0,
				Text = initialText,
				FontFamily = new WpfMedia.FontFamily("Segoe UI"),
				FontSize = fontSize,
				FontWeight = FontWeights.Bold,
				Foreground = MakeFrozenBrush(248, 250, 252),
				CaretBrush = MakeFrozenBrush(56, 189, 248),
				SelectionBrush = MakeFrozenBrush(37, 99, 235),
				Background = WpfMedia.Brushes.Transparent,
				BorderThickness = new Thickness(0),
				VerticalAlignment = VerticalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Center,
				TextAlignment = align,
				Padding = new Thickness(2, 0, 2, 0)
			};

			// Clean custom template so NinjaTrader's global TextBox chrome/padding never clips digits
			var tmpl = new WpfControls.ControlTemplate(typeof(WpfControls.TextBox));
			var hostFactory = new FrameworkElementFactory(typeof(WpfControls.ScrollViewer));
			hostFactory.Name = "PART_ContentHost";
			hostFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
			hostFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
			tmpl.VisualTree = hostFactory;
			tb.Template = tmpl;
			return tb;
		}

		private WpfControls.CheckBox CreateDarkCheckBox(string labelText, bool initialChecked, Action<bool> onChanged)
		{
			var cb = new WpfControls.CheckBox
			{
				Style = null,
				MinWidth = 0,
				MinHeight = 0,
				Content = labelText,
				FontFamily = new WpfMedia.FontFamily("Segoe UI"),
				FontSize = 12.5,
				FontWeight = FontWeights.Bold,
				Foreground = MakeFrozenBrush(241, 245, 249),
				IsChecked = initialChecked,
				VerticalAlignment = VerticalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Center,
				Cursor = Cursors.Hand,
				FocusVisualStyle = null
			};

			var tmpl = new WpfControls.ControlTemplate(typeof(WpfControls.CheckBox));
			var stackFactory = new FrameworkElementFactory(typeof(WpfControls.StackPanel));
			stackFactory.SetValue(WpfControls.StackPanel.OrientationProperty, WpfControls.Orientation.Horizontal);
			stackFactory.SetValue(WpfControls.Panel.BackgroundProperty, WpfMedia.Brushes.Transparent);

			var boxFactory = new FrameworkElementFactory(typeof(WpfControls.Border));
			boxFactory.Name = "chkBox";
			boxFactory.SetValue(FrameworkElement.WidthProperty, 16.0);
			boxFactory.SetValue(FrameworkElement.HeightProperty, 16.0);
			boxFactory.SetValue(WpfControls.Border.CornerRadiusProperty, new CornerRadius(3.5));
			boxFactory.SetValue(WpfControls.Border.BackgroundProperty, MakeFrozenBrush(22, 29, 42));
			boxFactory.SetValue(WpfControls.Border.BorderBrushProperty, MakeFrozenBrush(100, 116, 139));
			boxFactory.SetValue(WpfControls.Border.BorderThicknessProperty, new Thickness(1.5));
			boxFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

			var markFactory = new FrameworkElementFactory(typeof(WpfControls.TextBlock));
			markFactory.Name = "chkMark";
			markFactory.SetValue(FrameworkElement.StyleProperty, null);
			markFactory.SetValue(WpfControls.TextBlock.TextProperty, "✓");
			markFactory.SetValue(WpfControls.TextBlock.FontSizeProperty, 11.2);
			markFactory.SetValue(WpfControls.TextBlock.FontWeightProperty, FontWeights.Bold);
			markFactory.SetValue(WpfControls.TextBlock.ForegroundProperty, WpfMedia.Brushes.White);
			markFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
			markFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
			markFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(0, -1, 0, 0));
			markFactory.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
			boxFactory.AppendChild(markFactory);

			var lblFactory = new FrameworkElementFactory(typeof(WpfControls.TextBlock));
			lblFactory.SetValue(FrameworkElement.StyleProperty, null);
			lblFactory.SetBinding(WpfControls.TextBlock.TextProperty, new System.Windows.Data.Binding("Content") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
			lblFactory.SetBinding(WpfControls.TextBlock.FontFamilyProperty, new System.Windows.Data.Binding("FontFamily") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
			lblFactory.SetBinding(WpfControls.TextBlock.FontSizeProperty, new System.Windows.Data.Binding("FontSize") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
			lblFactory.SetBinding(WpfControls.TextBlock.FontWeightProperty, new System.Windows.Data.Binding("FontWeight") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
			lblFactory.SetBinding(WpfControls.TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
			lblFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
			lblFactory.SetValue(FrameworkElement.MarginProperty, new Thickness(7, 0, 0, 0));

			stackFactory.AppendChild(boxFactory);
			stackFactory.AppendChild(lblFactory);
			tmpl.VisualTree = stackFactory;

			var checkedTrig = new Trigger { Property = WpfPrimitives.ToggleButton.IsCheckedProperty, Value = true };
			checkedTrig.Setters.Add(new Setter(WpfControls.Border.BackgroundProperty, MakeFrozenBrush(16, 185, 129), "chkBox"));
			checkedTrig.Setters.Add(new Setter(WpfControls.Border.BorderBrushProperty, MakeFrozenBrush(52, 211, 153), "chkBox"));
			checkedTrig.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "chkMark"));
			tmpl.Triggers.Add(checkedTrig);

			cb.Template = tmpl;
			cb.Checked += (s, e) => { if (!isSyncingTradePanelUi) onChanged?.Invoke(true); };
			cb.Unchecked += (s, e) => { if (!isSyncingTradePanelUi) onChanged?.Invoke(false); };
			return cb;
		}

		private WpfControls.Button CreateFlatDarkButton(string text, WpfMedia.Brush bg, WpfMedia.Brush border, WpfMedia.Brush fg, double fontSize = 11.0, double cornerRadius = 5.0, Thickness? pad = null)
		{
			var btn = new WpfControls.Button
			{
				Style = null,
				MinWidth = 0,
				MinHeight = 0,
				Content = text,
				FontFamily = new WpfMedia.FontFamily("Segoe UI"),
				FontSize = fontSize,
				FontWeight = FontWeights.SemiBold,
				Foreground = fg,
				Background = bg,
				BorderBrush = border,
				BorderThickness = new Thickness(1.0),
				Padding = pad ?? new Thickness(6, 4, 6, 4),
				HorizontalContentAlignment = HorizontalAlignment.Center,
				VerticalContentAlignment = VerticalAlignment.Center,
				Cursor = Cursors.Hand,
				FocusVisualStyle = null
			};

			var template = new WpfControls.ControlTemplate(typeof(WpfControls.Button));
			var bdFactory = new FrameworkElementFactory(typeof(WpfControls.Border));
			bdFactory.Name = "bd";
			bdFactory.SetValue(WpfControls.Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
			bdFactory.SetBinding(WpfControls.Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
			bdFactory.SetBinding(WpfControls.Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
			bdFactory.SetBinding(WpfControls.Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
			bdFactory.SetBinding(WpfControls.Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });

			// Explicit style-free centered TextBlock so NinjaTrader's global implicit styles never push button text off-center
			var txtFactory = new FrameworkElementFactory(typeof(WpfControls.TextBlock));
			txtFactory.SetValue(FrameworkElement.StyleProperty, null);
			txtFactory.SetBinding(WpfControls.TextBlock.TextProperty, new System.Windows.Data.Binding("Content") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
			txtFactory.SetBinding(WpfControls.TextBlock.FontFamilyProperty, new System.Windows.Data.Binding("FontFamily") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
			txtFactory.SetBinding(WpfControls.TextBlock.FontSizeProperty, new System.Windows.Data.Binding("FontSize") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
			txtFactory.SetBinding(WpfControls.TextBlock.FontWeightProperty, new System.Windows.Data.Binding("FontWeight") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
			txtFactory.SetBinding(WpfControls.TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
			txtFactory.SetValue(WpfControls.TextBlock.TextAlignmentProperty, System.Windows.TextAlignment.Center);
			txtFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
			txtFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
			bdFactory.AppendChild(txtFactory);

			var hoverTrig = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
			hoverTrig.Setters.Add(new Setter(UIElement.OpacityProperty, 0.88, "bd"));
			template.Triggers.Add(hoverTrig);

			template.VisualTree = bdFactory;
			btn.Template = template;
			return btn;
		}

		private WpfControls.Border BuildTradeseaRightPanelVisualTree()
		{
			var bgPanel = MakeFrozenBrush(13, 16, 23);         // #0D1017 Tradesea Deep Obsidian
			var bgHeader = MakeFrozenBrush(9, 12, 18);         // #090C12 Header
			var bgInput = MakeFrozenBrush(21, 27, 38);         // #151B26 Elevated Readable Input Card
			var borderSubtle = MakeFrozenBrush(51, 65, 85);    // #334155 High-Clarity Slate Border
			var txtMuted = MakeFrozenBrush(168, 181, 199);     // #A8B5C7 Readable Crisp Secondary Label
			var txtBright = MakeFrozenBrush(248, 250, 252);    // #F8FAFC Pure Crisp White
			var greenTradesea = MakeFrozenBrush(15, 159, 114); // #0F9F72 Tradesea Confirm Teal-Green
			var redTradesea = MakeFrozenBrush(225, 29, 72);    // #E11D48 Vivid Crimson

			var root = new WpfControls.Border
			{
				Background = bgPanel,
				BorderBrush = MakeFrozenBrush(38, 49, 66),
				BorderThickness = new Thickness(1, 0, 0, 0),
				UseLayoutRounding = true,
				SnapsToDevicePixels = true
			};
			WpfMedia.TextOptions.SetTextFormattingMode(root, WpfMedia.TextFormattingMode.Display);
			WpfMedia.TextOptions.SetTextRenderingMode(root, WpfMedia.TextRenderingMode.ClearType);
			WpfMedia.RenderOptions.SetClearTypeHint(root, WpfMedia.ClearTypeHint.Enabled);
			// Shield all child TextBlocks from NinjaTrader's global implicit TextBlock alignment/margin styles
			root.Resources[typeof(WpfControls.TextBlock)] = new Style(typeof(WpfControls.TextBlock));

			// Prevent chart shortcut keys from firing when typing numbers inside panel inputs
			root.PreviewKeyDown += (s, e) =>
			{
				if (e.OriginalSource is WpfPrimitives.TextBoxBase)
					e.Handled = false;
			};

			var outerGrid = new WpfControls.Grid();
			outerGrid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = GridLength.Auto }); // 0: Top Account & Bal/RP&L/UP&L
			outerGrid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = GridLength.Auto }); // 1: Scalp | Order Tabs
			outerGrid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 2: Scrollable Body
			outerGrid.RowDefinitions.Add(new WpfControls.RowDefinition { Height = GridLength.Auto }); // 3: Pinned Confirm / Cancel Footer
			root.Child = outerGrid;

			// =========================================================================
			// ROW 0: Top Account Selector Pill + Live Bal / RP&L / UP&L Header
			// =========================================================================
			var hdrBorder = new WpfControls.Border
			{
				Background = bgHeader,
				BorderBrush = borderSubtle,
				BorderThickness = new Thickness(0, 0, 0, 1),
				Padding = new Thickness(10, 7, 10, 7)
			};
			var hdrStack = new WpfControls.StackPanel();
			hdrBorder.Child = hdrStack;

			var acctTopRow = new WpfControls.Grid();
			acctTopRow.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			acctTopRow.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = GridLength.Auto });

			var acctPickerBtn = new WpfControls.Border
			{
				Background = bgInput,
				BorderBrush = borderSubtle,
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(5),
				Padding = new Thickness(8, 4, 8, 4),
				Cursor = Cursors.Hand,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				Margin = new Thickness(0, 0, 6, 0)
			};
			var acctBtnInner = new WpfControls.DockPanel();
			var greenDot = new WpfShapes.Ellipse
			{
				Width = 7,
				Height = 7,
				Fill = MakeFrozenBrush(16, 185, 129),
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 6, 0)
			};
			WpfControls.DockPanel.SetDock(greenDot, WpfControls.Dock.Left);
			acctBtnInner.Children.Add(greenDot);

			var acctChevron = new WpfControls.TextBlock
			{
				Text = "▾",
				FontSize = 11,
				Foreground = txtMuted,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(6, 0, 0, 0)
			};
			WpfControls.DockPanel.SetDock(acctChevron, WpfControls.Dock.Right);
			acctBtnInner.Children.Add(acctChevron);

			txtSelectedAccountLabel = new WpfControls.TextBlock
			{
				Text = "Sim101",
				FontFamily = new WpfMedia.FontFamily("Segoe UI"),
				FontSize = 11.8,
				FontWeight = FontWeights.Bold,
				Foreground = txtBright,
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis
			};
			acctBtnInner.Children.Add(txtSelectedAccountLabel);
			acctPickerBtn.Child = acctBtnInner;

			tradeAccountPopupList = new WpfControls.StackPanel();
			tradeAccountPopup = new WpfPrimitives.Popup
			{
				PlacementTarget = acctPickerBtn,
				Placement = WpfPrimitives.PlacementMode.Bottom,
				StaysOpen = false,
				AllowsTransparency = true,
				Child = new WpfControls.Border
				{
					MinWidth = 205,
					MaxHeight = 240,
					Background = MakeFrozenBrush(18, 23, 33),
					BorderBrush = MakeFrozenBrush(48, 60, 82),
					BorderThickness = new Thickness(1),
					CornerRadius = new CornerRadius(6),
					Padding = new Thickness(4),
					Child = new WpfControls.ScrollViewer
					{
						VerticalScrollBarVisibility = WpfControls.ScrollBarVisibility.Auto,
						Content = tradeAccountPopupList
					}
				}
			};

			acctPickerBtn.MouseLeftButtonDown += (s, e) =>
			{
				PopulateAccountPopupItems();
				tradeAccountPopup.IsOpen = !tradeAccountPopup.IsOpen;
				e.Handled = true;
			};
			WpfControls.Grid.SetColumn(acctPickerBtn, 0);
			acctTopRow.Children.Add(acctPickerBtn);

			var btnCollapsePanel = CreateFlatDarkButton(
				"⇥", bgInput, borderSubtle, txtBright, 12.0, 5.0, new Thickness(8, 3, 8, 3));
			btnCollapsePanel.Width = 32;
			btnCollapsePanel.ToolTip = "Collapse Order Panel (Re-open from Top Toolbar)";
			btnCollapsePanel.Click += (s, e) =>
			{
				runtimeShowTradePanel = false;
				EnableTradePanel = false;
				UpdateTradePanelVisibilityImmediate();
				RequestImmediateOrderflowRedraw();
			};
			WpfControls.Grid.SetColumn(btnCollapsePanel, 1);
			acctTopRow.Children.Add(btnCollapsePanel);
			hdrStack.Children.Add(acctTopRow);

			// Live Bal | RP&L | UP&L Metrics Bar (Tradesea Top Strip)
			var metricsGrid = new WpfControls.Grid { Margin = new Thickness(0, 6, 0, 0) };
			metricsGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
			metricsGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
			metricsGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

			WpfControls.StackPanel MakeMetricCol(string title, out WpfControls.TextBlock valOut, HorizontalAlignment hAlign)
			{
				var sp = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal, HorizontalAlignment = hAlign };
				sp.Children.Add(new WpfControls.TextBlock
				{
					Text = title + " ",
					FontSize = 10.8,
					FontWeight = FontWeights.SemiBold,
					Foreground = txtMuted,
					VerticalAlignment = VerticalAlignment.Center
				});
				valOut = new WpfControls.TextBlock
				{
					Text = "$--",
					FontSize = 11.4,
					FontWeight = FontWeights.Bold,
					Foreground = txtBright,
					VerticalAlignment = VerticalAlignment.Center
				};
				sp.Children.Add(valOut);
				return sp;
			}

			var colBal = MakeMetricCol("Bal", out txtBalVal, HorizontalAlignment.Left);
			var colRpl = MakeMetricCol("RP&L", out txtRplVal, HorizontalAlignment.Center);
			var colUpl = MakeMetricCol("UP&L", out txtUplVal, HorizontalAlignment.Right);
			WpfControls.Grid.SetColumn(colBal, 0);
			WpfControls.Grid.SetColumn(colRpl, 1);
			WpfControls.Grid.SetColumn(colUpl, 2);
			metricsGrid.Children.Add(colBal);
			metricsGrid.Children.Add(colRpl);
			metricsGrid.Children.Add(colUpl);
			hdrStack.Children.Add(metricsGrid);

			WpfControls.Grid.SetRow(hdrBorder, 0);
			outerGrid.Children.Add(hdrBorder);

			// =========================================================================
			// ROW 1: Mode Tabs Bar — [ Scalp ]  [ Order ] (Zero DOM Tab, Blue Underline)
			// =========================================================================
			var tabsBorder = new WpfControls.Border
			{
				Background = bgPanel,
				BorderBrush = borderSubtle,
				BorderThickness = new Thickness(0, 0, 0, 1),
				Padding = new Thickness(10, 2, 10, 0)
			};
			var tabsGrid = new WpfControls.Grid();
			tabsGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			tabsGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			WpfControls.Border MakeModeTab(string title, out WpfControls.TextBlock tOut, out WpfControls.Border underlineOut, Action onClick)
			{
				var tabBd = new WpfControls.Border { Background = WpfMedia.Brushes.Transparent, Cursor = Cursors.Hand, Padding = new Thickness(0, 6, 0, 0) };
				var st = new WpfControls.StackPanel();
				tOut = new WpfControls.TextBlock
				{
					Text = title,
					FontSize = 12.2,
					FontWeight = FontWeights.SemiBold,
					HorizontalAlignment = HorizontalAlignment.Center,
					Margin = new Thickness(0, 0, 0, 6)
				};
				underlineOut = new WpfControls.Border
				{
					Height = 2.2,
					CornerRadius = new CornerRadius(1),
					Background = WpfMedia.Brushes.Transparent
				};
				st.Children.Add(tOut);
				st.Children.Add(underlineOut);
				tabBd.Child = st;
				tabBd.MouseLeftButtonDown += (s, e) => { onClick?.Invoke(); e.Handled = true; };
				return tabBd;
			}

			tabBtnScalp = MakeModeTab("Scalp", out tabTxtScalp, out tabLineScalp, () =>
			{
				isTradeTabScalp = true;
				SyncTradePanelUiFromState();
				RequestImmediateOrderflowRedraw();
			});
			tabBtnOrder = MakeModeTab("Order", out tabTxtOrder, out tabLineOrder, () =>
			{
				isTradeTabScalp = false;
				SyncTradePanelUiFromState();
				RequestImmediateOrderflowRedraw();
			});
			WpfControls.Grid.SetColumn(tabBtnScalp, 0);
			WpfControls.Grid.SetColumn(tabBtnOrder, 1);
			tabsGrid.Children.Add(tabBtnScalp);
			tabsGrid.Children.Add(tabBtnOrder);
			tabsBorder.Child = tabsGrid;

			WpfControls.Grid.SetRow(tabsBorder, 1);
			outerGrid.Children.Add(tabsBorder);

			// =========================================================================
			// ROW 2: Scrollable Main Content Container (Order Tab + Scalp Tab)
			// =========================================================================
			var bodyScroll = new WpfControls.ScrollViewer
			{
				VerticalScrollBarVisibility = WpfControls.ScrollBarVisibility.Auto,
				HorizontalScrollBarVisibility = WpfControls.ScrollBarVisibility.Disabled,
				Padding = new Thickness(11, 10, 11, 8)
			};
			var bodyHost = new WpfControls.StackPanel();
			bodyScroll.Content = bodyHost;

			// Symbol + TIF Header Row (Shared above tabs, matching "COMEX:MGC" in Image 2)
			var symRow = new WpfControls.DockPanel { Margin = new Thickness(0, 0, 0, 8) };
			btnTifToggle = CreateFlatDarkButton("DAY", bgInput, borderSubtle, txtMuted, 10.2, 4.0, new Thickness(7, 2, 7, 2));
			btnTifToggle.ToolTip = "Toggle Time-In-Force (DAY / GTC)";
			btnTifToggle.Click += (s, e) =>
			{
				selectedOrderTif = (selectedOrderTif == TimeInForce.Day) ? TimeInForce.Gtc : TimeInForce.Day;
				SyncTradePanelUiFromState();
			};
			WpfControls.DockPanel.SetDock(btnTifToggle, WpfControls.Dock.Right);
			symRow.Children.Add(btnTifToggle);

			txtSymbolHeader = new WpfControls.TextBlock
			{
				Text = "MGC",
				FontSize = 12.0,
				FontWeight = FontWeights.SemiBold,
				Foreground = MakeFrozenBrush(203, 213, 225),
				VerticalAlignment = VerticalAlignment.Center
			};
			symRow.Children.Add(txtSymbolHeader);
			bodyHost.Children.Add(symRow);

			// Live Open Position Quick-Management Banner (Visible whenever holding an open position on this symbol)
			livePosBanner = new WpfControls.Border
			{
				Background = MakeFrozenBrush(17, 25, 39),
				BorderBrush = MakeFrozenBrush(59, 76, 104),
				BorderThickness = new Thickness(1.2),
				CornerRadius = new CornerRadius(6),
				Padding = new Thickness(10, 8, 10, 8),
				Margin = new Thickness(0, 0, 0, 10),
				Visibility = Visibility.Collapsed
			};
			var livePosStack = new WpfControls.StackPanel();
			var livePosTop = new WpfControls.DockPanel { Margin = new Thickness(0, 0, 0, 7) };
			txtLivePosPnl = new WpfControls.TextBlock
			{
				Style = null,
				Text = "+$0.00",
				FontSize = 11.6,
				FontWeight = FontWeights.SemiBold,
				Foreground = greenTradesea,
				VerticalAlignment = VerticalAlignment.Center
			};
			WpfControls.DockPanel.SetDock(txtLivePosPnl, WpfControls.Dock.Right);
			livePosTop.Children.Add(txtLivePosPnl);

			txtLivePosBanner = new WpfControls.TextBlock
			{
				Style = null,
				Text = "LONG +1",
				FontSize = 11.4,
				FontWeight = FontWeights.SemiBold,
				Foreground = txtBright,
				VerticalAlignment = VerticalAlignment.Center
			};
			livePosTop.Children.Add(txtLivePosBanner);
			livePosStack.Children.Add(livePosTop);

			var livePosActions = new WpfControls.Grid();
			livePosActions.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
			livePosActions.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
			livePosActions.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1.10, GridUnitType.Star) });
			livePosActions.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
			livePosActions.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });

			var btnQuickBe = CreateFlatDarkButton("BE", MakeFrozenBrush(26, 35, 50), borderSubtle, txtBright, 10.6, 5.0, new Thickness(4, 4, 4, 4));
			btnQuickBe.ToolTip = "Move Stop Loss to Breakeven (Entry Price)";
			btnQuickBe.Click += (s, e) => ExecuteMoveStopToBreakeven();

			var btnQuickRev = CreateFlatDarkButton("Reverse", MakeFrozenBrush(26, 35, 50), borderSubtle, txtBright, 10.6, 5.0, new Thickness(4, 4, 4, 4));
			btnQuickRev.Click += (s, e) => ExecuteReverseCurrentPosition();

			var btnQuickFlat = CreateFlatDarkButton("Close ✕", MakeFrozenBrush(145, 24, 42), MakeFrozenBrush(244, 63, 94), WpfMedia.Brushes.White, 10.6, 5.0, new Thickness(4, 4, 4, 4));
			btnQuickFlat.Click += (s, e) => ExecuteFlattenCurrentInstrument();

			btnQuickRevRef = btnQuickRev;
			WpfControls.Grid.SetColumn(btnQuickBe, 0);
			WpfControls.Grid.SetColumn(btnQuickRev, 2);
			WpfControls.Grid.SetColumn(btnQuickFlat, 4);
			livePosActions.Children.Add(btnQuickBe);
			livePosActions.Children.Add(btnQuickRev);
			livePosActions.Children.Add(btnQuickFlat);
			livePosStack.Children.Add(livePosActions);
			livePosBanner.Child = livePosStack;
			bodyHost.Children.Add(livePosBanner);

			// =========================================================================
			// VIEW A: ORDER TAB (Exact 1:1 Tradesea Layout)
			// =========================================================================
			viewOrderTab = new WpfControls.StackPanel();

			// 1. Dual Buy (BID) | Sell (ASK) Cards
			var sideGrid = new WpfControls.Grid { Margin = new Thickness(0, 0, 0, 12) };
			sideGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			sideGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
			sideGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			WpfControls.Border MakeBuySellCard(string sideTitle, string quotePrefix, out WpfControls.TextBlock quoteValOut, Action onSelect)
			{
				var card = new WpfControls.Border
				{
					CornerRadius = new CornerRadius(6),
					BorderThickness = new Thickness(1.4),
					Padding = new Thickness(8, 8, 8, 8),
					Cursor = Cursors.Hand
				};
				var sp = new WpfControls.StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
				sp.Children.Add(new WpfControls.TextBlock
				{
					Text = sideTitle,
					FontSize = 13.5,
					FontWeight = FontWeights.Bold,
					Foreground = txtBright,
					HorizontalAlignment = HorizontalAlignment.Center
				});
				var qRow = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
				qRow.Children.Add(new WpfControls.TextBlock
				{
					Text = quotePrefix + " ",
					FontSize = 10.2,
					FontWeight = FontWeights.SemiBold,
					Foreground = txtMuted,
					VerticalAlignment = VerticalAlignment.Center
				});
				quoteValOut = new WpfControls.TextBlock
				{
					Text = "--",
					FontSize = 11.2,
					FontWeight = FontWeights.Bold,
					Foreground = txtBright,
					VerticalAlignment = VerticalAlignment.Center
				};
				qRow.Children.Add(quoteValOut);
				sp.Children.Add(qRow);
				card.Child = sp;
				card.MouseLeftButtonDown += (s, e) => { onSelect?.Invoke(); e.Handled = true; };
				return card;
			}

			cardBuySide = MakeBuySellCard("Buy", "BID", out txtBuyBidVal, () =>
			{
				if (!isOrderSideBuy)
				{
					isOrderSideBuy = true;
					if (selectedOrderTypeIdx != 0) SeedDefaultCustomEntryPrice();
					SyncTradePanelUiFromState();
					RequestImmediateOrderflowRedraw();
				}
			});
			cardSellSide = MakeBuySellCard("Sell", "ASK", out txtSellAskVal, () =>
			{
				if (isOrderSideBuy)
				{
					isOrderSideBuy = false;
					if (selectedOrderTypeIdx != 0) SeedDefaultCustomEntryPrice();
					SyncTradePanelUiFromState();
					RequestImmediateOrderflowRedraw();
				}
			});
			WpfControls.Grid.SetColumn(cardBuySide, 0);
			WpfControls.Grid.SetColumn(cardSellSide, 2);
			sideGrid.Children.Add(cardBuySide);
			sideGrid.Children.Add(cardSellSide);
			viewOrderTab.Children.Add(sideGrid);

			// 2. Order type: [ Market ] [ Limit ] [ Stop ] Pills
			var ordTypeRow = new WpfControls.Grid { Margin = new Thickness(0, 0, 0, 12) };
			ordTypeRow.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = GridLength.Auto });
			ordTypeRow.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			ordTypeRow.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			ordTypeRow.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

			var lblOrdType = new WpfControls.TextBlock
			{
				Text = "Order type:",
				FontSize = 11.2,
				Foreground = txtMuted,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 6, 0)
			};
			WpfControls.Grid.SetColumn(lblOrdType, 0);
			ordTypeRow.Children.Add(lblOrdType);

			WpfControls.Border MakeOrderTypePill(string text, int idx, out WpfControls.TextBlock tOut)
			{
				var p = new WpfControls.Border
				{
					CornerRadius = new CornerRadius(12),
					BorderThickness = new Thickness(1),
					Padding = new Thickness(6, 4, 6, 4),
					Margin = new Thickness(2, 0, 2, 0),
					Cursor = Cursors.Hand
				};
				tOut = new WpfControls.TextBlock
				{
					Text = text,
					FontSize = 11.2,
					FontWeight = FontWeights.SemiBold,
					HorizontalAlignment = HorizontalAlignment.Center
				};
				p.Child = tOut;
				p.MouseLeftButtonDown += (s, e) =>
				{
					selectedOrderTypeIdx = idx;
					if (idx != 0) SeedDefaultCustomEntryPrice();
					SyncTradePanelUiFromState();
					RequestImmediateOrderflowRedraw();
					e.Handled = true;
				};
				return p;
			}

			pillTypeMkt = MakeOrderTypePill("Market", 0, out txtTypeMkt);
			pillTypeLmt = MakeOrderTypePill("Limit", 1, out txtTypeLmt);
			pillTypeStp = MakeOrderTypePill("Stop", 2, out txtTypeStp);
			WpfControls.Grid.SetColumn(pillTypeMkt, 1);
			WpfControls.Grid.SetColumn(pillTypeLmt, 2);
			WpfControls.Grid.SetColumn(pillTypeStp, 3);
			ordTypeRow.Children.Add(pillTypeMkt);
			ordTypeRow.Children.Add(pillTypeLmt);
			ordTypeRow.Children.Add(pillTypeStp);
			viewOrderTab.Children.Add(ordTypeRow);

			// 3. Market Price / Limit Price / Stop Price Display & Input
			lblEntryPriceTitle = new WpfControls.TextBlock
			{
				Text = "Market Price",
				FontSize = 11.2,
				Foreground = txtMuted,
				Margin = new Thickness(0, 0, 0, 4)
			};
			viewOrderTab.Children.Add(lblEntryPriceTitle);

			txtMarketPriceBig = new WpfControls.TextBlock
			{
				Text = "--",
				FontSize = 15.2,
				FontWeight = FontWeights.SemiBold,
				Foreground = txtBright,
				Margin = new Thickness(0, 0, 0, 12)
			};
			viewOrderTab.Children.Add(txtMarketPriceBig);

			grpCustomEntryInput = new WpfControls.Grid { Margin = new Thickness(0, 0, 0, 12), Visibility = Visibility.Collapsed };
			grpCustomEntryInput.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grpCustomEntryInput.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
			grpCustomEntryInput.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = GridLength.Auto });

			var entryBoxBorder = new WpfControls.Border
			{
				Background = bgInput,
				BorderBrush = borderSubtle,
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(6),
				Height = 34,
				Padding = new Thickness(6, 0, 6, 0)
			};
			txtCustomEntryPrice = CreateDarkInputBox("0.00", 13.0);
			txtCustomEntryPrice.LostFocus += (s, e) =>
			{
				if (double.TryParse(txtCustomEntryPrice.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) && v > 0)
				{
					orderCustomEntryPrice = RoundToInstrumentTick(v);
					orderCustomEntrySeeded = true;
					SyncTradePanelUiFromState();
					RequestImmediateOrderflowRedraw();
				}
			};
			txtCustomEntryPrice.KeyDown += (s, e) =>
			{
				if (e.Key == Key.Enter && double.TryParse(txtCustomEntryPrice.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) && v > 0)
				{
					orderCustomEntryPrice = RoundToInstrumentTick(v);
					orderCustomEntrySeeded = true;
					SyncTradePanelUiFromState();
					RequestImmediateOrderflowRedraw();
				}
			};
			entryBoxBorder.Child = txtCustomEntryPrice;
			WpfControls.Grid.SetColumn(entryBoxBorder, 0);
			grpCustomEntryInput.Children.Add(entryBoxBorder);

			var entryStepBorder = new WpfControls.Border
			{
				Background = bgInput,
				BorderBrush = borderSubtle,
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(6),
				Height = 34
			};
			var entryStepRow = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal };
			var btnEntryMinus = CreateFlatDarkButton("−", WpfMedia.Brushes.Transparent, WpfMedia.Brushes.Transparent, txtMuted, 14.0, 4.0, new Thickness(10, 0, 10, 0));
			btnEntryMinus.Click += (s, e) =>
			{
				double tick = TickSize > 0 ? TickSize : 0.05;
				orderCustomEntryPrice = RoundToInstrumentTick(GetCurrentOrderReferenceEntryPrice() - tick);
				orderCustomEntrySeeded = true;
				SyncTradePanelUiFromState();
				RequestImmediateOrderflowRedraw();
			};
			var btnEntryPlus = CreateFlatDarkButton("+", WpfMedia.Brushes.Transparent, WpfMedia.Brushes.Transparent, txtBright, 14.0, 4.0, new Thickness(10, 0, 10, 0));
			btnEntryPlus.Click += (s, e) =>
			{
				double tick = TickSize > 0 ? TickSize : 0.05;
				orderCustomEntryPrice = RoundToInstrumentTick(GetCurrentOrderReferenceEntryPrice() + tick);
				orderCustomEntrySeeded = true;
				SyncTradePanelUiFromState();
				RequestImmediateOrderflowRedraw();
			};
			entryStepRow.Children.Add(btnEntryMinus);
			entryStepRow.Children.Add(new WpfControls.Border { Width = 1, Background = borderSubtle, Margin = new Thickness(0, 6, 0, 6) });
			entryStepRow.Children.Add(btnEntryPlus);
			entryStepBorder.Child = entryStepRow;
			WpfControls.Grid.SetColumn(entryStepBorder, 2);
			grpCustomEntryInput.Children.Add(entryStepBorder);
			viewOrderTab.Children.Add(grpCustomEntryInput);

			// 4. Quantity Section (Input Box + [ − | + ] Stepper Pill)
			viewOrderTab.Children.Add(new WpfControls.TextBlock
			{
				Text = "Quantity",
				FontSize = 11.2,
				Foreground = txtMuted,
				Margin = new Thickness(0, 0, 0, 5)
			});

			var qtyGrid = new WpfControls.Grid { Margin = new Thickness(0, 0, 0, 13) };
			qtyGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			qtyGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
			qtyGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = GridLength.Auto });

			var qtyBoxBd = new WpfControls.Border
			{
				Background = bgInput,
				BorderBrush = borderSubtle,
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(6),
				Height = 34,
				Padding = new Thickness(6, 0, 6, 0)
			};
			txtOrderQty = CreateDarkInputBox(orderQtyValue.ToString(CultureInfo.InvariantCulture), 13.0);
			txtOrderQty.LostFocus += (s, e) =>
			{
				if (int.TryParse(txtOrderQty.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int q) && q >= 1)
					orderQtyValue = Math.Min(100000, q);
				SyncTradePanelUiFromState();
			};
			qtyBoxBd.Child = txtOrderQty;
			WpfControls.Grid.SetColumn(qtyBoxBd, 0);
			qtyGrid.Children.Add(qtyBoxBd);

			var qtyStepBd = new WpfControls.Border
			{
				Background = bgInput,
				BorderBrush = borderSubtle,
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(6),
				Height = 34
			};
			var qtyStepRow = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal };
			var btnQtyMinus = CreateFlatDarkButton("−", WpfMedia.Brushes.Transparent, WpfMedia.Brushes.Transparent, txtMuted, 14.0, 4.0, new Thickness(10, 0, 10, 0));
			btnQtyMinus.Click += (s, e) =>
			{
				orderQtyValue = Math.Max(1, orderQtyValue - 1);
				SyncTradePanelUiFromState();
			};
			var btnQtyPlus = CreateFlatDarkButton("+", WpfMedia.Brushes.Transparent, WpfMedia.Brushes.Transparent, txtBright, 14.0, 4.0, new Thickness(10, 0, 10, 0));
			btnQtyPlus.Click += (s, e) =>
			{
				orderQtyValue = Math.Min(100000, orderQtyValue + 1);
				SyncTradePanelUiFromState();
			};
			qtyStepRow.Children.Add(btnQtyMinus);
			qtyStepRow.Children.Add(new WpfControls.Border { Width = 1, Background = borderSubtle, Margin = new Thickness(0, 6, 0, 6) });
			qtyStepRow.Children.Add(btnQtyPlus);
			qtyStepBd.Child = qtyStepRow;
			WpfControls.Grid.SetColumn(qtyStepBd, 2);
			qtyGrid.Children.Add(qtyStepBd);
			viewOrderTab.Children.Add(qtyGrid);

			// Dashed Divider (Exact Tradesea Divider)
			viewOrderTab.Children.Add(new WpfShapes.Line
			{
				X1 = 0,
				X2 = 240,
				Stroke = borderSubtle,
				StrokeThickness = 1,
				StrokeDashArray = new WpfMedia.DoubleCollection { 3, 3 },
				Stretch = WpfMedia.Stretch.Fill,
				Margin = new Thickness(0, 0, 0, 12)
			});

			// 5. Stop Loss + Trail + [ Price | Ticks ▾ ] + [ 10 | 25 | 50 | 100 ] Presets
			var slHdrDock = new WpfControls.DockPanel { Margin = new Thickness(0, 0, 0, 6) };
			var trailRightStack = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			chkTrailEnable = CreateDarkCheckBox("Trail", orderTrailEnabled, isOn =>
			{
				orderTrailEnabled = isOn;
				if (isOn && !orderSlEnabled) orderSlEnabled = true;
				SyncTradePanelUiFromState();
				RequestImmediateOrderflowRedraw();
			});
			trailRightStack.Children.Add(chkTrailEnable);

			var trailTicksBd = new WpfControls.Border
			{
				Background = bgInput,
				BorderBrush = borderSubtle,
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(4),
				Margin = new Thickness(6, 0, 0, 0),
				Padding = new Thickness(5, 2, 6, 2)
			};
			var trailTicksInner = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal };
			txtTrailTicks = CreateDarkInputBox(orderTrailTicks.ToString(CultureInfo.InvariantCulture), 11.8, System.Windows.TextAlignment.Center);
			txtTrailTicks.Width = 30;
			txtTrailTicks.LostFocus += (s, e) =>
			{
				if (int.TryParse(txtTrailTicks.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int tv) && tv >= 1)
					orderTrailTicks = Math.Min(10000, tv);
				SyncTradePanelUiFromState();
			};
			trailTicksInner.Children.Add(txtTrailTicks);
			trailTicksInner.Children.Add(new WpfControls.TextBlock
			{
				Text = "Ticks",
				FontSize = 10.5,
				FontWeight = FontWeights.SemiBold,
				Foreground = txtMuted,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(2, 0, 0, 0)
			});
			trailTicksBd.Child = trailTicksInner;
			trailRightStack.Children.Add(trailTicksBd);
			WpfControls.DockPanel.SetDock(trailRightStack, WpfControls.Dock.Right);
			slHdrDock.Children.Add(trailRightStack);

			chkSlEnable = CreateDarkCheckBox("Stop Loss", orderSlEnabled, isOn =>
			{
				orderSlEnabled = isOn;
				if (!isOn) orderTrailEnabled = false;
				SyncTradePanelUiFromState();
				RequestImmediateOrderflowRedraw();
			});
			slHdrDock.Children.Add(chkSlEnable);
			viewOrderTab.Children.Add(slHdrDock);

			var slDualBd = new WpfControls.Border
			{
				Background = bgInput,
				BorderBrush = borderSubtle,
				BorderThickness = new Thickness(1.1),
				CornerRadius = new CornerRadius(6),
				Height = 36,
				Padding = new Thickness(8, 0, 6, 0),
				Margin = new Thickness(0, 0, 0, 7)
			};
			var slDualGrid = new WpfControls.Grid();
			slDualGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1.18, GridUnitType.Star) });
			slDualGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = GridLength.Auto });
			slDualGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

			var slLeftDock = new WpfControls.DockPanel();
			var lblSlPx = new WpfControls.TextBlock { Style = null, Text = "Price", FontSize = 10.8, FontWeight = FontWeights.SemiBold, Foreground = txtMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
			WpfControls.DockPanel.SetDock(lblSlPx, WpfControls.Dock.Right);
			slLeftDock.Children.Add(lblSlPx);
			txtSlPrice = CreateDarkInputBox("00.00", 12.8);
			txtSlPrice.LostFocus += (s, e) => OnUserEditedSlPriceText();
			txtSlPrice.KeyDown += (s, e) => { if (e.Key == Key.Enter) OnUserEditedSlPriceText(); };
			slLeftDock.Children.Add(txtSlPrice);
			WpfControls.Grid.SetColumn(slLeftDock, 0);
			slDualGrid.Children.Add(slLeftDock);

			var slDiv = new WpfControls.Border { Width = 1, Background = borderSubtle, Margin = new Thickness(4, 6, 6, 6) };
			WpfControls.Grid.SetColumn(slDiv, 1);
			slDualGrid.Children.Add(slDiv);

			var slRightDock = new WpfControls.DockPanel();
			btnSlUnit = CreateFlatDarkButton("Ticks ▾", WpfMedia.Brushes.Transparent, WpfMedia.Brushes.Transparent, txtMuted, 10.8, 3.0, new Thickness(4, 0, 2, 0));
			btnSlUnit.Click += (s, e) =>
			{
				orderSlUnitIsCurrency = !orderSlUnitIsCurrency;
				SyncTradePanelUiFromState();
			};
			WpfControls.DockPanel.SetDock(btnSlUnit, WpfControls.Dock.Right);
			slRightDock.Children.Add(btnSlUnit);
			txtSlOffset = CreateDarkInputBox(orderSlTicks.ToString(CultureInfo.InvariantCulture), 12.8);
			txtSlOffset.LostFocus += (s, e) => OnUserEditedSlOffsetText();
			txtSlOffset.KeyDown += (s, e) => { if (e.Key == Key.Enter) OnUserEditedSlOffsetText(); };
			slRightDock.Children.Add(txtSlOffset);
			WpfControls.Grid.SetColumn(slRightDock, 2);
			slDualGrid.Children.Add(slRightDock);
			slDualBd.Child = slDualGrid;
			viewOrderTab.Children.Add(slDualBd);

			var slPresetsGrid = new WpfPrimitives.UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(0, 0, 0, 14) };
			int[] slPresetVals = new int[] { 10, 25, 50, 100 };
			for (int pIdx = 0; pIdx < slPresetVals.Length; pIdx++)
			{
				int captured = slPresetVals[pIdx];
				var pb = CreateFlatDarkButton(
					captured.ToString(CultureInfo.InvariantCulture),
					MakeFrozenBrush(36, 18, 26),
					MakeFrozenBrush(136, 38, 56),
					MakeFrozenBrush(252, 165, 165),
					11.4, 5.0, new Thickness(2, 4, 2, 4));
				pb.Margin = new Thickness(pIdx == 0 ? 0 : 2.5, 0, pIdx == 3 ? 0 : 2.5, 0);
				pb.Click += (s, e) =>
				{
					orderSlEnabled = true;
					orderSlUnitIsCurrency = false;
					int maxCapT = ComputeCapitalProtectorMaxSlTicks(orderQtyValue);
					orderSlTicks = Math.Min(captured, maxCapT);
					if (!orderTpManuallyCustomized)
					{
						double rrInit = InitialOrderRrRatio > 0.1 ? InitialOrderRrRatio : 3.0;
						orderTpEnabled = true;
						orderTpTicks = Math.Max(1, (int)Math.Round(orderSlTicks * rrInit));
					}
					SyncTradePanelUiFromState();
					RequestImmediateOrderflowRedraw();
				};
				slPresetBtns[pIdx] = pb;
				slPresetsGrid.Children.Add(pb);
			}
			viewOrderTab.Children.Add(slPresetsGrid);

			// 6. Take Profit + [ Price | Ticks ▾ ] + [ 10 | 25 | 50 | 100 ] Presets
			var tpHdrDock = new WpfControls.DockPanel { Margin = new Thickness(0, 0, 0, 6) };
			txtOrderRrSummary = new WpfControls.TextBlock
			{
				Style = null,
				Text = string.Empty,
				FontSize = 11.2,
				FontWeight = FontWeights.Bold,
				Foreground = MakeFrozenBrush(52, 211, 153),
				VerticalAlignment = VerticalAlignment.Center
			};
			WpfControls.DockPanel.SetDock(txtOrderRrSummary, WpfControls.Dock.Right);
			tpHdrDock.Children.Add(txtOrderRrSummary);

			chkTpEnable = CreateDarkCheckBox("Take Profit", orderTpEnabled, isOn =>
			{
				orderTpEnabled = isOn;
				SyncTradePanelUiFromState();
				RequestImmediateOrderflowRedraw();
			});
			tpHdrDock.Children.Add(chkTpEnable);
			viewOrderTab.Children.Add(tpHdrDock);

			var tpDualBd = new WpfControls.Border
			{
				Background = bgInput,
				BorderBrush = borderSubtle,
				BorderThickness = new Thickness(1.1),
				CornerRadius = new CornerRadius(6),
				Height = 36,
				Padding = new Thickness(8, 0, 6, 0),
				Margin = new Thickness(0, 0, 0, 7)
			};
			var tpDualGrid = new WpfControls.Grid();
			tpDualGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1.18, GridUnitType.Star) });
			tpDualGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = GridLength.Auto });
			tpDualGrid.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

			var tpLeftDock = new WpfControls.DockPanel();
			var lblTpPx = new WpfControls.TextBlock { Style = null, Text = "Price", FontSize = 10.8, FontWeight = FontWeights.SemiBold, Foreground = txtMuted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
			WpfControls.DockPanel.SetDock(lblTpPx, WpfControls.Dock.Right);
			tpLeftDock.Children.Add(lblTpPx);
			txtTpPrice = CreateDarkInputBox("00.00", 12.8);
			txtTpPrice.LostFocus += (s, e) => OnUserEditedTpPriceText();
			txtTpPrice.KeyDown += (s, e) => { if (e.Key == Key.Enter) OnUserEditedTpPriceText(); };
			tpLeftDock.Children.Add(txtTpPrice);
			WpfControls.Grid.SetColumn(tpLeftDock, 0);
			tpDualGrid.Children.Add(tpLeftDock);

			var tpDiv = new WpfControls.Border { Width = 1, Background = borderSubtle, Margin = new Thickness(4, 6, 6, 6) };
			WpfControls.Grid.SetColumn(tpDiv, 1);
			tpDualGrid.Children.Add(tpDiv);

			var tpRightDock = new WpfControls.DockPanel();
			btnTpUnit = CreateFlatDarkButton("Ticks ▾", WpfMedia.Brushes.Transparent, WpfMedia.Brushes.Transparent, txtMuted, 10.8, 3.0, new Thickness(4, 0, 2, 0));
			btnTpUnit.Click += (s, e) =>
			{
				orderTpUnitIsCurrency = !orderTpUnitIsCurrency;
				SyncTradePanelUiFromState();
			};
			WpfControls.DockPanel.SetDock(btnTpUnit, WpfControls.Dock.Right);
			tpRightDock.Children.Add(btnTpUnit);
			txtTpOffset = CreateDarkInputBox(orderTpTicks.ToString(CultureInfo.InvariantCulture), 12.8);
			txtTpOffset.LostFocus += (s, e) => OnUserEditedTpOffsetText();
			txtTpOffset.KeyDown += (s, e) => { if (e.Key == Key.Enter) OnUserEditedTpOffsetText(); };
			tpRightDock.Children.Add(txtTpOffset);
			WpfControls.Grid.SetColumn(tpRightDock, 2);
			tpDualGrid.Children.Add(tpRightDock);
			tpDualBd.Child = tpDualGrid;
			viewOrderTab.Children.Add(tpDualBd);

			var tpPresetsGrid = new WpfPrimitives.UniformGrid { Rows = 1, Columns = 4, Margin = new Thickness(0, 0, 0, 8) };
			int[] tpPresetVals = new int[] { 10, 25, 50, 100 };
			for (int pIdx = 0; pIdx < tpPresetVals.Length; pIdx++)
			{
				int captured = tpPresetVals[pIdx];
				var pb = CreateFlatDarkButton(
					captured.ToString(CultureInfo.InvariantCulture),
					MakeFrozenBrush(14, 34, 29),
					MakeFrozenBrush(24, 114, 86),
					MakeFrozenBrush(110, 231, 183),
					11.4, 5.0, new Thickness(2, 4, 2, 4));
				pb.Margin = new Thickness(pIdx == 0 ? 0 : 2.5, 0, pIdx == 3 ? 0 : 2.5, 0);
				pb.Click += (s, e) =>
				{
					orderTpEnabled = true;
					orderTpUnitIsCurrency = false;
					orderTpTicks = captured;
					orderTpManuallyCustomized = true;
					SyncTradePanelUiFromState();
					RequestImmediateOrderflowRedraw();
				};
				tpPresetBtns[pIdx] = pb;
				tpPresetsGrid.Children.Add(pb);
			}
			viewOrderTab.Children.Add(tpPresetsGrid);
			bodyHost.Children.Add(viewOrderTab);

			// =========================================================================
			// VIEW B: SCALP TAB (1-Click High-Speed Scalping + Working Orders Monitor)
			// =========================================================================
			viewScalpTab = new WpfControls.StackPanel { Visibility = Visibility.Collapsed };

			viewScalpTab.Children.Add(new WpfControls.TextBlock
			{
				Text = "1-Click Scalp Quantity",
				FontSize = 11.0,
				Foreground = txtMuted,
				Margin = new Thickness(0, 0, 0, 5)
			});

			var scQtyRow = new WpfControls.Grid { Margin = new Thickness(0, 0, 0, 6) };
			scQtyRow.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			scQtyRow.ColumnDefinitions.Add(new WpfControls.ColumnDefinition { Width = GridLength.Auto });
			var scQtyBd = new WpfControls.Border
			{
				Background = bgInput,
				BorderBrush = borderSubtle,
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(6),
				Height = 32,
				Margin = new Thickness(0, 0, 6, 0),
				Padding = new Thickness(6, 0, 6, 0)
			};
			txtScalpQty = CreateDarkInputBox(orderQtyValue.ToString(CultureInfo.InvariantCulture), 12.5);
			txtScalpQty.LostFocus += (s, e) =>
			{
				if (int.TryParse(txtScalpQty.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int q) && q >= 1)
					orderQtyValue = Math.Min(100000, q);
				SyncTradePanelUiFromState();
			};
			scQtyBd.Child = txtScalpQty;
			WpfControls.Grid.SetColumn(scQtyBd, 0);
			scQtyRow.Children.Add(scQtyBd);

			var scQuickLots = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal };
			foreach (int lq in new int[] { 1, 2, 5, 10 })
			{
				int capLq = lq;
				var lb = CreateFlatDarkButton(capLq.ToString(CultureInfo.InvariantCulture), bgInput, borderSubtle, txtBright, 10.5, 4.0, new Thickness(7, 4, 7, 4));
				lb.Margin = new Thickness(2, 0, 0, 0);
				lb.Click += (s, e) => { orderQtyValue = capLq; SyncTradePanelUiFromState(); };
				scQuickLots.Children.Add(lb);
			}
			WpfControls.Grid.SetColumn(scQuickLots, 1);
			scQtyRow.Children.Add(scQuickLots);
			viewScalpTab.Children.Add(scQtyRow);

			var scBrkRow = new WpfControls.DockPanel { Margin = new Thickness(0, 2, 0, 10) };
			txtScalpBracketInfo = new WpfControls.TextBlock
			{
				Text = "SL 20T / TP 40T",
				FontSize = 10.2,
				FontWeight = FontWeights.SemiBold,
				Foreground = txtMuted,
				VerticalAlignment = VerticalAlignment.Center
			};
			WpfControls.DockPanel.SetDock(txtScalpBracketInfo, WpfControls.Dock.Right);
			scBrkRow.Children.Add(txtScalpBracketInfo);

			chkScalpAutoBracket = CreateDarkCheckBox("Auto Bracket (SL/TP)", scalpUseAutoBracket, isOn =>
			{
				scalpUseAutoBracket = isOn;
				SyncTradePanelUiFromState();
			});
			scBrkRow.Children.Add(chkScalpAutoBracket);
			viewScalpTab.Children.Add(scBrkRow);

			// 1-Click Buy / Sell Market & Bid / Ask Grid
			var scExecGrid = new WpfPrimitives.UniformGrid { Rows = 3, Columns = 2, Margin = new Thickness(0, 0, 0, 10) };

			var btnScBuyMkt = CreateFlatDarkButton("Buy Market", greenTradesea, MakeFrozenBrush(20, 184, 132), WpfMedia.Brushes.White, 12.0, 6.0, new Thickness(4, 9, 4, 9));
			btnScBuyMkt.Margin = new Thickness(0, 0, 3, 6);
			btnScBuyMkt.Click += (s, e) => TriggerScalpExecution(true, OrderType.Market, 0);

			var btnScSellMkt = CreateFlatDarkButton("Sell Market", redTradesea, MakeFrozenBrush(244, 63, 94), WpfMedia.Brushes.White, 12.0, 6.0, new Thickness(4, 9, 4, 9));
			btnScSellMkt.Margin = new Thickness(3, 0, 0, 6);
			btnScSellMkt.Click += (s, e) => TriggerScalpExecution(false, OrderType.Market, 0);

			var btnScBuyBid = CreateFlatDarkButton("Buy Bid", MakeFrozenBrush(12, 42, 34), MakeFrozenBrush(16, 185, 129), MakeFrozenBrush(110, 231, 183), 11.2, 5.0, new Thickness(4, 7, 4, 7));
			btnScBuyBid.Margin = new Thickness(0, 0, 3, 6);
			btnScBuyBid.Click += (s, e) => { ResolveLiveBidAskLast(out double b, out _, out _); TriggerScalpExecution(true, OrderType.Limit, b); };

			var btnScSellAsk = CreateFlatDarkButton("Sell Ask", MakeFrozenBrush(46, 16, 24), MakeFrozenBrush(239, 68, 68), MakeFrozenBrush(252, 165, 165), 11.2, 5.0, new Thickness(4, 7, 4, 7));
			btnScSellAsk.Margin = new Thickness(3, 0, 0, 6);
			btnScSellAsk.Click += (s, e) => { ResolveLiveBidAskLast(out _, out double a, out _); TriggerScalpExecution(false, OrderType.Limit, a); };

			var btnScBuyAsk = CreateFlatDarkButton("Buy Ask", bgInput, borderSubtle, MakeFrozenBrush(110, 231, 183), 11.0, 5.0, new Thickness(4, 7, 4, 7));
			btnScBuyAsk.Margin = new Thickness(0, 0, 3, 0);
			btnScBuyAsk.Click += (s, e) => { ResolveLiveBidAskLast(out _, out double a, out _); TriggerScalpExecution(true, OrderType.Limit, a); };

			var btnScSellBid = CreateFlatDarkButton("Sell Bid", bgInput, borderSubtle, MakeFrozenBrush(252, 165, 165), 11.0, 5.0, new Thickness(4, 7, 4, 7));
			btnScSellBid.Margin = new Thickness(3, 0, 0, 0);
			btnScSellBid.Click += (s, e) => { ResolveLiveBidAskLast(out double b, out _, out _); TriggerScalpExecution(false, OrderType.Limit, b); };

			scalpEntryButtons.Clear();
			scalpEntryButtons.Add(btnScBuyMkt);
			scalpEntryButtons.Add(btnScSellMkt);
			scalpEntryButtons.Add(btnScBuyBid);
			scalpEntryButtons.Add(btnScSellAsk);
			scalpEntryButtons.Add(btnScBuyAsk);
			scalpEntryButtons.Add(btnScSellBid);

			scExecGrid.Children.Add(btnScBuyMkt);
			scExecGrid.Children.Add(btnScSellMkt);
			scExecGrid.Children.Add(btnScBuyBid);
			scExecGrid.Children.Add(btnScSellAsk);
			scExecGrid.Children.Add(btnScBuyAsk);
			scExecGrid.Children.Add(btnScSellBid);
			viewScalpTab.Children.Add(scExecGrid);

			// 1-Click Scalp Position Actions
			var scManageGrid = new WpfPrimitives.UniformGrid { Rows = 2, Columns = 2, Margin = new Thickness(0, 0, 0, 10) };
			var btnScBe = CreateFlatDarkButton("Breakeven", bgInput, borderSubtle, txtBright, 11.0, 5.0, new Thickness(4, 7, 4, 7));
			btnScBe.Margin = new Thickness(0, 0, 3, 6);
			btnScBe.Click += (s, e) => ExecuteMoveStopToBreakeven();

			var btnScRev = CreateFlatDarkButton("Reverse", bgInput, borderSubtle, txtBright, 11.0, 5.0, new Thickness(4, 7, 4, 7));
			btnScRev.Margin = new Thickness(3, 0, 0, 6);
			btnScRev.Click += (s, e) => ExecuteReverseCurrentPosition();
			btnScalpRevRef = btnScRev;

			var btnScCxl = CreateFlatDarkButton("Cancel All", bgInput, borderSubtle, txtMuted, 11.0, 5.0, new Thickness(4, 7, 4, 7));
			btnScCxl.Margin = new Thickness(0, 0, 3, 0);
			btnScCxl.Click += (s, e) => CancelAllInstrumentWorkingOrders();

			var btnScFlat = CreateFlatDarkButton("Flatten All", MakeFrozenBrush(72, 18, 28), MakeFrozenBrush(180, 40, 60), WpfMedia.Brushes.White, 11.0, 5.0, new Thickness(4, 7, 4, 7));
			btnScFlat.Margin = new Thickness(3, 0, 0, 0);
			btnScFlat.Click += (s, e) => ExecuteFlattenCurrentInstrument();

			scManageGrid.Children.Add(btnScBe);
			scManageGrid.Children.Add(btnScRev);
			scManageGrid.Children.Add(btnScCxl);
			scManageGrid.Children.Add(btnScFlat);
			viewScalpTab.Children.Add(scManageGrid);

			viewScalpTab.Children.Add(new WpfControls.TextBlock
			{
				Text = "Working Orders (Drag TP/SL on Chart)",
				FontSize = 10.6,
				FontWeight = FontWeights.SemiBold,
				Foreground = txtMuted,
				Margin = new Thickness(0, 2, 0, 5)
			});
			pnlScalpWorkingOrders = new WpfControls.StackPanel();
			viewScalpTab.Children.Add(pnlScalpWorkingOrders);

			bodyHost.Children.Add(viewScalpTab);
			WpfControls.Grid.SetRow(bodyScroll, 2);
			outerGrid.Children.Add(bodyScroll);

			// =========================================================================
			// ROW 3: Pinned Bottom Risk & Discipline Guard + Confirm & Cancel Footer
			// =========================================================================
			var footerBd = new WpfControls.Border
			{
				Background = bgPanel,
				BorderBrush = borderSubtle,
				BorderThickness = new Thickness(0, 1, 0, 0),
				Padding = new Thickness(11, 7, 11, 10)
			};
			var footerStack = new WpfControls.StackPanel();

			// Live AK10X Risk & Discipline + Capital Protector Guardrail Card (Pinned right above Confirm/Cancel)
			riskGuardCardBorder = new WpfControls.Border
			{
				Background = MakeFrozenBrush(14, 22, 36),
				BorderBrush = MakeFrozenBrush(34, 150, 198),
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(6),
				Padding = new Thickness(8, 6, 8, 6),
				Margin = new Thickness(0, 0, 0, 7)
			};
			var rgStack = new WpfControls.StackPanel();
			var rgHdr = new WpfControls.DockPanel { Margin = new Thickness(0, 0, 0, 3) };

			riskGuardTradesPill = new WpfControls.Border
			{
				Background = MakeFrozenBrush(20, 34, 52),
				BorderBrush = MakeFrozenBrush(56, 189, 248),
				BorderThickness = new Thickness(0.9),
				CornerRadius = new CornerRadius(4),
				Padding = new Thickness(6, 1, 6, 1),
				VerticalAlignment = VerticalAlignment.Center
			};
			txtRiskGuardTrades = new WpfControls.TextBlock
			{
				Style = null,
				Text = "Trades 0/3",
				FontSize = 9.8,
				FontWeight = FontWeights.Bold,
				Foreground = MakeFrozenBrush(125, 211, 252),
				VerticalAlignment = VerticalAlignment.Center
			};
			riskGuardTradesPill.Child = txtRiskGuardTrades;
			WpfControls.DockPanel.SetDock(riskGuardTradesPill, WpfControls.Dock.Right);
			rgHdr.Children.Add(riskGuardTradesPill);

			txtRiskGuardTitle = new WpfControls.TextBlock
			{
				Style = null,
				Text = "🛡 RISK & CAPITAL GUARD",
				FontSize = 10.0,
				FontWeight = FontWeights.Bold,
				Foreground = MakeFrozenBrush(56, 189, 248),
				VerticalAlignment = VerticalAlignment.Center,
				TextTrimming = TextTrimming.CharacterEllipsis
			};
			rgHdr.Children.Add(txtRiskGuardTitle);
			rgStack.Children.Add(rgHdr);

			txtRiskGuardRow1 = new WpfControls.TextBlock
			{
				Style = null,
				Text = "Day P&L: $0.00  ·  Tgt $500 / SL -$300",
				FontSize = 9.8,
				FontWeight = FontWeights.SemiBold,
				Foreground = MakeFrozenBrush(226, 232, 240),
				TextTrimming = TextTrimming.CharacterEllipsis,
				Margin = new Thickness(0, 1, 0, 1)
			};
			rgStack.Children.Add(txtRiskGuardRow1);

			txtRiskGuardRow2 = new WpfControls.TextBlock
			{
				Style = null,
				Text = "Capital Protector: $200  ·  Base 1:3 R:R",
				FontSize = 9.6,
				FontWeight = FontWeights.SemiBold,
				Foreground = MakeFrozenBrush(52, 211, 153),
				TextTrimming = TextTrimming.CharacterEllipsis
			};
			rgStack.Children.Add(txtRiskGuardRow2);
			riskGuardCardBorder.Child = rgStack;
			footerStack.Children.Add(riskGuardCardBorder);

			txtTradeStatusToast = new WpfControls.TextBlock
			{
				Text = string.Empty,
				FontSize = 10.2,
				FontWeight = FontWeights.SemiBold,
				Foreground = MakeFrozenBrush(52, 211, 153),
				TextTrimming = TextTrimming.CharacterEllipsis,
				Margin = new Thickness(0, 0, 0, 5)
			};
			footerStack.Children.Add(txtTradeStatusToast);

			btnOrderConfirm = CreateFlatDarkButton(
				"Confirm",
				greenTradesea,
				MakeFrozenBrush(20, 184, 132),
				WpfMedia.Brushes.White,
				12.8, 6.0, new Thickness(8, 8, 8, 8));
			btnOrderConfirm.Margin = new Thickness(0, 0, 0, 6);
			btnOrderConfirm.Click += (s, e) => OnConfirmButtonClicked();
			footerStack.Children.Add(btnOrderConfirm);

			var btnOrderCancel = CreateFlatDarkButton(
				"Cancel",
				MakeFrozenBrush(38, 43, 53),
				MakeFrozenBrush(55, 65, 81),
				MakeFrozenBrush(226, 232, 240),
				12.0, 6.0, new Thickness(8, 6, 8, 6));
			btnOrderCancel.Click += (s, e) =>
			{
				CancelAllInstrumentWorkingOrders();
			};
			footerStack.Children.Add(btnOrderCancel);
			footerBd.Child = footerStack;

			WpfControls.Grid.SetRow(footerBd, 3);
			outerGrid.Children.Add(footerBd);

			return root;
		}

		private void TriggerScalpExecution(bool isBuy, OrderType ordType, double limitOrStopPx)
		{
			bool attachBrk = scalpUseAutoBracket;
			int slT = Math.Max(1, orderSlTicks > 0 ? orderSlTicks : DefaultSlTicks);
			int tpT = Math.Max(1, orderTpTicks > 0 ? orderTpTicks : DefaultTpTicks);
			ExecuteSubmitOrderCore(
				isBuy,
				ordType,
				limitOrStopPx,
				orderQtyValue,
				attachBrk,
				slT,
				attachBrk,
				tpT,
				attachBrk && orderTrailEnabled,
				orderTrailTicks);
		}

		private void OnConfirmButtonClicked()
		{
			if (isTradeTabScalp)
			{
				TriggerScalpExecution(isOrderSideBuy, OrderType.Market, 0.0);
				return;
			}

			// Commit any typed values right before submitting
			if (txtOrderQty != null && int.TryParse(txtOrderQty.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedQ) && parsedQ >= 1)
				orderQtyValue = Math.Min(100000, parsedQ);

			OrderType ot = selectedOrderTypeIdx == 1
				? OrderType.Limit
				: (selectedOrderTypeIdx == 2 ? OrderType.StopMarket : OrderType.Market);
			double entryPx = (ot == OrderType.Market) ? 0.0 : GetCurrentOrderReferenceEntryPrice();

			ExecuteSubmitOrderCore(
				isOrderSideBuy,
				ot,
				entryPx,
				orderQtyValue,
				orderSlEnabled,
				orderSlTicks,
				orderTpEnabled,
				orderTpTicks,
				orderTrailEnabled,
				orderTrailTicks);
		}

		private void OnUserEditedSlPriceText()
		{
			if (txtSlPrice == null || !double.TryParse(txtSlPrice.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double px) || px <= 0)
				return;
			double tick = TickSize > 0 ? TickSize : 0.05;
			double refEntry = GetCurrentOrderReferenceEntryPrice();
			int ticks = Math.Max(1, (int)Math.Round(Math.Abs(refEntry - RoundToInstrumentTick(px)) / tick));
			orderSlTicks = Math.Min(ComputeCapitalProtectorMaxSlTicks(orderQtyValue), Math.Min(100000, ticks));
			orderSlEnabled = true;
			if (!orderTpManuallyCustomized)
			{
				double rrInit = InitialOrderRrRatio > 0.1 ? InitialOrderRrRatio : 3.0;
				orderTpEnabled = true;
				orderTpTicks = Math.Max(1, (int)Math.Round(orderSlTicks * rrInit));
			}
			SyncTradePanelUiFromState();
			RequestImmediateOrderflowRedraw();
		}

		private void OnUserEditedSlOffsetText()
		{
			if (txtSlOffset == null || !double.TryParse(txtSlOffset.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double val) || val <= 0)
				return;
			double tick = TickSize > 0 ? TickSize : 0.05;
			double pv = (Instrument != null && Instrument.MasterInstrument != null && Instrument.MasterInstrument.PointValue > 0) ? Instrument.MasterInstrument.PointValue : 1.0;
			if (orderSlUnitIsCurrency)
			{
				double dollarPerTick = Math.Max(1e-6, orderQtyValue * tick * pv);
				orderSlTicks = Math.Max(1, Math.Min(100000, (int)Math.Round(val / dollarPerTick)));
			}
			else
			{
				orderSlTicks = Math.Max(1, Math.Min(100000, (int)Math.Round(val)));
			}
			orderSlTicks = Math.Min(orderSlTicks, ComputeCapitalProtectorMaxSlTicks(orderQtyValue));
			orderSlEnabled = true;
			if (!orderTpManuallyCustomized)
			{
				double rrInit = InitialOrderRrRatio > 0.1 ? InitialOrderRrRatio : 3.0;
				orderTpEnabled = true;
				orderTpTicks = Math.Max(1, (int)Math.Round(orderSlTicks * rrInit));
			}
			SyncTradePanelUiFromState();
			RequestImmediateOrderflowRedraw();
		}

		private void OnUserEditedTpPriceText()
		{
			if (txtTpPrice == null || !double.TryParse(txtTpPrice.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double px) || px <= 0)
				return;
			double tick = TickSize > 0 ? TickSize : 0.05;
			double refEntry = GetCurrentOrderReferenceEntryPrice();
			int ticks = Math.Max(1, (int)Math.Round(Math.Abs(RoundToInstrumentTick(px) - refEntry) / tick));
			orderTpTicks = Math.Min(100000, ticks);
			orderTpEnabled = true;
			orderTpManuallyCustomized = true;
			SyncTradePanelUiFromState();
			RequestImmediateOrderflowRedraw();
		}

		private void OnUserEditedTpOffsetText()
		{
			if (txtTpOffset == null || !double.TryParse(txtTpOffset.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out double val) || val <= 0)
				return;
			double tick = TickSize > 0 ? TickSize : 0.05;
			double pv = (Instrument != null && Instrument.MasterInstrument != null && Instrument.MasterInstrument.PointValue > 0) ? Instrument.MasterInstrument.PointValue : 1.0;
			if (orderTpUnitIsCurrency)
			{
				double dollarPerTick = Math.Max(1e-6, orderQtyValue * tick * pv);
				orderTpTicks = Math.Max(1, Math.Min(100000, (int)Math.Round(val / dollarPerTick)));
			}
			else
			{
				orderTpTicks = Math.Max(1, Math.Min(100000, (int)Math.Round(val)));
			}
			orderTpEnabled = true;
			orderTpManuallyCustomized = true;
			SyncTradePanelUiFromState();
			RequestImmediateOrderflowRedraw();
		}

		private void PopulateAccountPopupItems()
		{
			if (tradeAccountPopupList == null)
				return;
			tradeAccountPopupList.Children.Clear();
			List<Account> accounts = GetAvailableTradeAccounts();
			Account cur = EnsureActiveTradeAccountSelected();

			for (int i = 0; i < accounts.Count; i++)
			{
				Account acc = accounts[i];
				bool isSel = (cur != null && string.Equals(acc.Name, cur.Name, StringComparison.OrdinalIgnoreCase));
				var itemBd = new WpfControls.Border
				{
					Background = isSel ? MakeFrozenBrush(26, 38, 56) : WpfMedia.Brushes.Transparent,
					CornerRadius = new CornerRadius(4),
					Padding = new Thickness(8, 6, 8, 6),
					Cursor = Cursors.Hand
				};
				var row = new WpfControls.StackPanel { Orientation = WpfControls.Orientation.Horizontal };
				row.Children.Add(new WpfShapes.Ellipse
				{
					Width = 6,
					Height = 6,
					Fill = isSel ? MakeFrozenBrush(16, 185, 129) : MakeFrozenBrush(100, 116, 139),
					VerticalAlignment = VerticalAlignment.Center,
					Margin = new Thickness(0, 0, 7, 0)
				});
				row.Children.Add(new WpfControls.TextBlock
				{
					Text = !string.IsNullOrWhiteSpace(acc.DisplayName) ? acc.DisplayName : acc.Name,
					FontSize = 11.5,
					FontWeight = isSel ? FontWeights.Bold : FontWeights.SemiBold,
					Foreground = isSel ? MakeFrozenBrush(248, 250, 252) : MakeFrozenBrush(203, 213, 225)
				});
				itemBd.Child = row;
				Account capturedAcc = acc;
				itemBd.MouseLeftButtonDown += (s, e) =>
				{
					SelectTradeAccount(capturedAcc);
					if (tradeAccountPopup != null) tradeAccountPopup.IsOpen = false;
					e.Handled = true;
				};
				tradeAccountPopupList.Children.Add(itemBd);
			}
		}

		private void SyncTradePanelUiFromState()
		{
			if (tradePanelRoot == null || isSyncingTradePanelUi)
				return;

			isSyncingTradePanelUi = true;
			try
			{
				Account acc = EnsureActiveTradeAccountSelected();
				string curSym = GetAccountCurrencyPrefix(acc);
				ResolveLiveBidAskLast(out double bid, out double ask, out double last);
				double tick = TickSize > 0 ? TickSize : 0.05;
				double pv = (Instrument != null && Instrument.MasterInstrument != null && Instrument.MasterInstrument.PointValue > 0)
					? Instrument.MasterInstrument.PointValue
					: 1.0;

				// 1. Account Header & Bal / RP&L / UP&L
				if (txtSelectedAccountLabel != null)
					txtSelectedAccountLabel.Text = acc != null ? (!string.IsNullOrWhiteSpace(acc.DisplayName) ? acc.DisplayName : acc.Name) : "No Account";

				double bal = 0.0, rpl = 0.0;
				if (acc != null)
				{
					try
					{
						bal = acc.Get(AccountItem.CashValue, acc.Denomination);
						if (Math.Abs(bal) < 1e-6)
							bal = acc.Get(AccountItem.NetLiquidation, acc.Denomination);
						rpl = acc.Get(AccountItem.RealizedProfitLoss, acc.Denomination);
					}
					catch { }
				}

				Position pos = GetCurrentInstrumentPosition(acc, nonBlocking: true);
				double upl = 0.0;
				bool hasOpenPos = (pos != null && pos.MarketPosition != MarketPosition.Flat && pos.Quantity > 0);
				if (hasOpenPos)
				{
					try { upl = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency, last); } catch { }
				}

				if (txtBalVal != null)
					txtBalVal.Text = curSym + bal.ToString("N2", CultureInfo.InvariantCulture);

				if (txtRplVal != null)
				{
					string sign = rpl > 0.004 ? "+" : (rpl < -0.004 ? "-" : "");
					txtRplVal.Text = sign + curSym + Math.Abs(rpl).ToString("N2", CultureInfo.InvariantCulture);
					txtRplVal.Foreground = rpl > 0.004
						? MakeFrozenBrush(16, 185, 129)
						: (rpl < -0.004 ? MakeFrozenBrush(239, 68, 68) : MakeFrozenBrush(203, 213, 225));
				}

				if (txtUplVal != null)
				{
					if (!hasOpenPos)
					{
						txtUplVal.Text = curSym + "--";
						txtUplVal.Foreground = MakeFrozenBrush(148, 163, 184);
					}
					else
					{
						string sign = upl >= 0 ? "+" : "-";
						txtUplVal.Text = sign + curSym + Math.Abs(upl).ToString("N2", CultureInfo.InvariantCulture);
						txtUplVal.Foreground = upl >= 0 ? MakeFrozenBrush(16, 185, 129) : MakeFrozenBrush(239, 68, 68);
					}
				}

				// 2. Symbol & TIF
				if (txtSymbolHeader != null && Instrument != null)
				{
					string exch = (Instrument.MasterInstrument != null) ? Instrument.MasterInstrument.InstrumentType.ToString().ToUpperInvariant() : "";
					try
					{
						if (Instrument.MasterInstrument != null && Instrument.MasterInstrument.TradingHours != null)
						{
							string th = Instrument.MasterInstrument.TradingHours.Name ?? "";
							if (th.IndexOf("Comex", StringComparison.OrdinalIgnoreCase) >= 0) exch = "COMEX";
							else if (th.IndexOf("CME", StringComparison.OrdinalIgnoreCase) >= 0) exch = "CME";
							else if (th.IndexOf("Nymex", StringComparison.OrdinalIgnoreCase) >= 0) exch = "NYMEX";
							else if (th.IndexOf("Nse", StringComparison.OrdinalIgnoreCase) >= 0) exch = "NSE";
							else if (th.IndexOf("MCX", StringComparison.OrdinalIgnoreCase) >= 0) exch = "MCX";
						}
					}
					catch { }
					txtSymbolHeader.Text = (!string.IsNullOrEmpty(exch) ? (exch + ":") : "") + Instrument.FullName;
				}
				if (btnTifToggle != null)
					btnTifToggle.Content = (selectedOrderTif == TimeInForce.Gtc) ? "GTC" : "DAY";

				// 3. Live Position Banner
				if (livePosBanner != null)
				{
					livePosBanner.Visibility = hasOpenPos ? Visibility.Visible : Visibility.Collapsed;
					if (hasOpenPos)
					{
						bool pLong = (pos.MarketPosition == MarketPosition.Long);
						string panelRr = string.Empty;
						List<Order> wos = GetCurrentInstrumentWorkingOrders(acc, nonBlocking: true);
						double wTpSum = 0.0, wTpQty = 0.0, wSlSum = 0.0, wSlQty = 0.0;
						for (int wi = 0; wi < wos.Count; wi++)
						{
							int wr = ClassifyWorkingOrderRole(wos[wi], pos);
							double wp = GetWorkingOrderEffectivePrice(wos[wi]);
							if (wp <= 0.0) continue;
							double wq = Math.Max(1, wos[wi].Quantity);
							if (wr == 1) { wTpSum += wp * wq; wTpQty += wq; }
							else if (wr == 2) { wSlSum += wp * wq; wSlQty += wq; }
						}
						if (wTpQty > 0.0 && wSlQty > 0.0)
						{
							string rBadge = FormatLiveRiskRewardBadge(pos.AveragePrice, wSlSum / wSlQty, wTpSum / wTpQty, tick);
							if (!string.IsNullOrEmpty(rBadge))
								panelRr = " · " + rBadge;
						}
						txtLivePosBanner.Text = (pLong ? "LONG +" : "SHORT -") + pos.Quantity + " @ " + FormatOrderflowPrice(pos.AveragePrice) + panelRr;
						txtLivePosBanner.Foreground = pLong ? MakeFrozenBrush(52, 211, 153) : MakeFrozenBrush(248, 113, 113);

						string uSign = upl >= 0 ? "+" : "-";
						txtLivePosPnl.Text = uSign + curSym + Math.Abs(upl).ToString("N2", CultureInfo.InvariantCulture);
						txtLivePosPnl.Foreground = upl >= 0 ? MakeFrozenBrush(52, 211, 153) : MakeFrozenBrush(248, 113, 113);
					}
				}

				// 4. Tabs Visual State (Scalp vs Order)
				var blueTab = MakeFrozenBrush(43, 127, 255);
				var txtBright = MakeFrozenBrush(248, 250, 252);
				var txtMuted = MakeFrozenBrush(138, 148, 166);
				if (tabTxtScalp != null) tabTxtScalp.Foreground = isTradeTabScalp ? blueTab : txtMuted;
				if (tabLineScalp != null) tabLineScalp.Background = isTradeTabScalp ? blueTab : WpfMedia.Brushes.Transparent;
				if (tabTxtOrder != null) tabTxtOrder.Foreground = !isTradeTabScalp ? blueTab : txtMuted;
				if (tabLineOrder != null) tabLineOrder.Background = !isTradeTabScalp ? blueTab : WpfMedia.Brushes.Transparent;

				if (viewOrderTab != null) viewOrderTab.Visibility = !isTradeTabScalp ? Visibility.Visible : Visibility.Collapsed;
				if (viewScalpTab != null) viewScalpTab.Visibility = isTradeTabScalp ? Visibility.Visible : Visibility.Collapsed;

				// 5. Order Tab Controls
				if (txtBuyBidVal != null) txtBuyBidVal.Text = FormatOrderflowPrice(bid);
				if (txtSellAskVal != null) txtSellAskVal.Text = FormatOrderflowPrice(ask);

				if (cardBuySide != null)
				{
					cardBuySide.Background = isOrderSideBuy ? MakeFrozenBrush(11, 38, 32) : MakeFrozenBrush(20, 24, 32);
					cardBuySide.BorderBrush = isOrderSideBuy ? MakeFrozenBrush(16, 185, 129) : MakeFrozenBrush(34, 41, 54);
				}
				if (cardSellSide != null)
				{
					cardSellSide.Background = !isOrderSideBuy ? MakeFrozenBrush(42, 18, 23) : MakeFrozenBrush(20, 24, 32);
					cardSellSide.BorderBrush = !isOrderSideBuy ? MakeFrozenBrush(239, 68, 68) : MakeFrozenBrush(34, 41, 54);
				}

				void StyleTypePill(WpfControls.Border b, WpfControls.TextBlock t, bool active)
				{
					if (b == null || t == null) return;
					b.Background = active ? MakeFrozenBrush(26, 33, 46) : MakeFrozenBrush(16, 20, 28);
					b.BorderBrush = active ? MakeFrozenBrush(110, 130, 158) : MakeFrozenBrush(36, 44, 58);
					t.Foreground = active ? txtBright : txtMuted;
				}
				StyleTypePill(pillTypeMkt, txtTypeMkt, selectedOrderTypeIdx == 0);
				StyleTypePill(pillTypeLmt, txtTypeLmt, selectedOrderTypeIdx == 1);
				StyleTypePill(pillTypeStp, txtTypeStp, selectedOrderTypeIdx == 2);

				double refEntry = GetCurrentOrderReferenceEntryPrice();
				if (selectedOrderTypeIdx == 0)
				{
					if (lblEntryPriceTitle != null) lblEntryPriceTitle.Text = "Market Price";
					if (txtMarketPriceBig != null)
					{
						txtMarketPriceBig.Visibility = Visibility.Visible;
						txtMarketPriceBig.Text = FormatOrderflowPrice(refEntry);
					}
					if (grpCustomEntryInput != null) grpCustomEntryInput.Visibility = Visibility.Collapsed;
				}
				else
				{
					if (lblEntryPriceTitle != null)
						lblEntryPriceTitle.Text = selectedOrderTypeIdx == 1 ? "Limit Price (Drag on Chart)" : "Stop Price (Drag on Chart)";
					if (txtMarketPriceBig != null) txtMarketPriceBig.Visibility = Visibility.Collapsed;
					if (grpCustomEntryInput != null) grpCustomEntryInput.Visibility = Visibility.Visible;
					if (txtCustomEntryPrice != null && !txtCustomEntryPrice.IsKeyboardFocused)
						txtCustomEntryPrice.Text = FormatOrderflowPrice(refEntry);
				}

				if (txtOrderQty != null && !txtOrderQty.IsKeyboardFocused)
					txtOrderQty.Text = orderQtyValue.ToString(CultureInfo.InvariantCulture);
				if (txtScalpQty != null && !txtScalpQty.IsKeyboardFocused)
					txtScalpQty.Text = orderQtyValue.ToString(CultureInfo.InvariantCulture);

				// Stop Loss & Take Profit Synced Values
				if (chkSlEnable != null) chkSlEnable.IsChecked = orderSlEnabled;
				if (chkTrailEnable != null) chkTrailEnable.IsChecked = orderTrailEnabled;
				if (txtTrailTicks != null && !txtTrailTicks.IsKeyboardFocused)
					txtTrailTicks.Text = orderTrailTicks.ToString(CultureInfo.InvariantCulture);

				double calcSlPx = isOrderSideBuy
					? RoundToInstrumentTick(refEntry - orderSlTicks * tick)
					: RoundToInstrumentTick(refEntry + orderSlTicks * tick);
				double slDollarAmt = orderQtyValue * orderSlTicks * tick * pv;

				if (txtSlPrice != null && !txtSlPrice.IsKeyboardFocused)
				{
					txtSlPrice.Text = orderSlEnabled ? FormatOrderflowPrice(calcSlPx) : "00.00";
					txtSlPrice.Foreground = orderSlEnabled ? txtBright : MakeFrozenBrush(148, 163, 184);
				}
				if (btnSlUnit != null)
					btnSlUnit.Content = orderSlUnitIsCurrency ? (curSym + " ▾") : "Ticks ▾";
				if (txtSlOffset != null && !txtSlOffset.IsKeyboardFocused)
				{
					txtSlOffset.Text = orderSlUnitIsCurrency
						? slDollarAmt.ToString("0.##", CultureInfo.InvariantCulture)
						: orderSlTicks.ToString(CultureInfo.InvariantCulture);
					txtSlOffset.Foreground = orderSlEnabled ? txtBright : MakeFrozenBrush(186, 198, 214);
				}

				int[] presetNums = new int[] { 10, 25, 50, 100 };
				for (int pi = 0; pi < 4; pi++)
				{
					var sb = slPresetBtns[pi];
					if (sb != null)
					{
						bool sel = orderSlEnabled && !orderSlUnitIsCurrency && orderSlTicks == presetNums[pi];
						sb.Background = sel ? MakeFrozenBrush(153, 27, 45) : MakeFrozenBrush(36, 18, 26);
						sb.BorderBrush = sel ? MakeFrozenBrush(252, 165, 165) : MakeFrozenBrush(136, 38, 56);
						sb.Foreground = sel ? WpfMedia.Brushes.White : MakeFrozenBrush(252, 165, 165);
					}
				}

				if (chkTpEnable != null) chkTpEnable.IsChecked = orderTpEnabled;
				double calcTpPx = isOrderSideBuy
					? RoundToInstrumentTick(refEntry + orderTpTicks * tick)
					: RoundToInstrumentTick(refEntry - orderTpTicks * tick);
				double tpDollarAmt = orderQtyValue * orderTpTicks * tick * pv;

				if (txtTpPrice != null && !txtTpPrice.IsKeyboardFocused)
				{
					txtTpPrice.Text = orderTpEnabled ? FormatOrderflowPrice(calcTpPx) : "00.00";
					txtTpPrice.Foreground = orderTpEnabled ? txtBright : MakeFrozenBrush(148, 163, 184);
				}
				if (btnTpUnit != null)
					btnTpUnit.Content = orderTpUnitIsCurrency ? (curSym + " ▾") : "Ticks ▾";
				if (txtTpOffset != null && !txtTpOffset.IsKeyboardFocused)
				{
					txtTpOffset.Text = orderTpUnitIsCurrency
						? tpDollarAmt.ToString("0.##", CultureInfo.InvariantCulture)
						: orderTpTicks.ToString(CultureInfo.InvariantCulture);
					txtTpOffset.Foreground = orderTpEnabled ? txtBright : MakeFrozenBrush(186, 198, 214);
				}

				for (int pi = 0; pi < 4; pi++)
				{
					var tb = tpPresetBtns[pi];
					if (tb != null)
					{
						bool sel = orderTpEnabled && !orderTpUnitIsCurrency && orderTpTicks == presetNums[pi];
						tb.Background = sel ? MakeFrozenBrush(13, 124, 89) : MakeFrozenBrush(14, 34, 29);
						tb.BorderBrush = sel ? MakeFrozenBrush(110, 231, 183) : MakeFrozenBrush(24, 114, 86);
						tb.Foreground = sel ? WpfMedia.Brushes.White : MakeFrozenBrush(110, 231, 183);
					}
				}

				if (txtOrderRrSummary != null)
				{
					if (orderSlEnabled && orderTpEnabled && orderSlTicks > 0)
					{
						double rr = (double)orderTpTicks / orderSlTicks;
						txtOrderRrSummary.Text = string.Format(CultureInfo.InvariantCulture, "RR 1 : {0:0.##}", rr);
					}
					else
					{
						txtOrderRrSummary.Text = string.Empty;
					}
				}

				// Evaluate Live Risk & Discipline Guard State for Panel + Button Lockout
				EnforceRiskAndCapitalProtectorOnTick(last);
				bool isBlocked = IsNewOrderBlockedByRiskDiscipline(acc, out string lockReason, out DateTime unlockAt);
				double dayNet = rpl + (hasOpenPos ? upl : 0.0);

				if (riskGuardCardBorder != null)
				{
					bool showCard = EnableRiskManagement || EnableCapitalProtector;
					riskGuardCardBorder.Visibility = showCard ? Visibility.Visible : Visibility.Collapsed;
					if (showCard)
					{
						string trTxt = MaxTradesPerDay > 0
							? string.Format(CultureInfo.InvariantCulture, "Trades {0}/{1}", cachedTradesDoneToday, MaxTradesPerDay)
							: string.Format(CultureInfo.InvariantCulture, "Trades {0}", cachedTradesDoneToday);
						if (txtRiskGuardTrades != null)
							txtRiskGuardTrades.Text = trTxt;

						string dNetSign = dayNet >= 0 ? "+" : "-";
						string tgtPart = DailyProfitTargetAmount > 0 ? (curSym + DailyProfitTargetAmount.ToString("0.##", CultureInfo.InvariantCulture)) : "Off";
						string slPart = DailyMaxLossAmount > 0 ? ("-" + curSym + Math.Abs(DailyMaxLossAmount).ToString("0.##", CultureInfo.InvariantCulture)) : "Off";

						if (isBlocked)
						{
							riskGuardCardBorder.Background = MakeFrozenBrush(46, 16, 24);
							riskGuardCardBorder.BorderBrush = MakeFrozenBrush(244, 63, 94);
							if (riskGuardTradesPill != null)
							{
								riskGuardTradesPill.Background = MakeFrozenBrush(76, 20, 32);
								riskGuardTradesPill.BorderBrush = MakeFrozenBrush(251, 113, 133);
							}
							if (txtRiskGuardTrades != null)
								txtRiskGuardTrades.Foreground = MakeFrozenBrush(254, 205, 211);
							if (txtRiskGuardTitle != null)
							{
								txtRiskGuardTitle.Text = "🔒 DISCIPLINE LOCKED";
								txtRiskGuardTitle.Foreground = MakeFrozenBrush(251, 113, 133);
							}
							if (txtRiskGuardRow1 != null)
							{
								txtRiskGuardRow1.Text = lockReason;
								txtRiskGuardRow1.Foreground = MakeFrozenBrush(254, 226, 226);
							}
							if (txtRiskGuardRow2 != null)
							{
								txtRiskGuardRow2.Text = "Unlocks Next Day Open (" + unlockAt.ToString("dd MMM hh:mm tt", CultureInfo.InvariantCulture) + ")";
								txtRiskGuardRow2.Foreground = MakeFrozenBrush(251, 191, 36);
							}
						}
						else
						{
							riskGuardCardBorder.Background = MakeFrozenBrush(13, 22, 36);
							riskGuardCardBorder.BorderBrush = MakeFrozenBrush(34, 150, 198);
							if (riskGuardTradesPill != null)
							{
								riskGuardTradesPill.Background = MakeFrozenBrush(18, 34, 52);
								riskGuardTradesPill.BorderBrush = MakeFrozenBrush(56, 189, 248);
							}
							if (txtRiskGuardTrades != null)
								txtRiskGuardTrades.Foreground = MakeFrozenBrush(125, 211, 252);
							if (txtRiskGuardTitle != null)
							{
								txtRiskGuardTitle.Text = "🛡 RISK & CAPITAL GUARD";
								txtRiskGuardTitle.Foreground = MakeFrozenBrush(56, 189, 248);
							}
							if (txtRiskGuardRow1 != null)
							{
								txtRiskGuardRow1.Text = string.Format(CultureInfo.InvariantCulture, "Day {0}{1}{2:N2}  ·  Tgt {3} / SL {4}", dNetSign, curSym, Math.Abs(dayNet), tgtPart, slPart);
								txtRiskGuardRow1.Foreground = dayNet >= 0 ? MakeFrozenBrush(226, 232, 240) : MakeFrozenBrush(252, 165, 165);
							}
							if (txtRiskGuardRow2 != null)
							{
								string capPart = EnableCapitalProtector
									? string.Format(CultureInfo.InvariantCulture, "Cap Protector: {0}{1:0.##}", curSym, CapitalProtectorAmount)
									: "Cap Protector: Off";
								double dispRr = (orderSlEnabled && orderTpEnabled && orderSlTicks > 0)
									? ((double)orderTpTicks / orderSlTicks)
									: Math.Max(0.5, InitialOrderRrRatio);
								txtRiskGuardRow2.Text = string.Format(CultureInfo.InvariantCulture, "{0}  ·  R:R 1:{1:0.#}", capPart, dispRr);
								txtRiskGuardRow2.Foreground = MakeFrozenBrush(52, 211, 153);
							}
						}
					}
				}

				if (btnOrderConfirm != null)
				{
					btnOrderConfirm.IsEnabled = !isBlocked;
					btnOrderConfirm.Opacity = isBlocked ? 0.58 : 1.0;
					if (isBlocked)
					{
						btnOrderConfirm.Background = MakeFrozenBrush(51, 30, 38);
						btnOrderConfirm.BorderBrush = MakeFrozenBrush(159, 43, 68);
						btnOrderConfirm.Content = "🔒 Locked Until " + unlockAt.ToString("hh:mm tt", CultureInfo.InvariantCulture);
					}
					else
					{
						btnOrderConfirm.Background = isOrderSideBuy
							? MakeFrozenBrush(15, 159, 114)
							: MakeFrozenBrush(225, 29, 72);
						btnOrderConfirm.BorderBrush = isOrderSideBuy
							? MakeFrozenBrush(22, 192, 138)
							: MakeFrozenBrush(244, 63, 94);
						btnOrderConfirm.Content = isTradeTabScalp
							? (isOrderSideBuy ? "Quick Buy Market" : "Quick Sell Market")
							: "Confirm";
					}
				}

				for (int sbi = 0; sbi < scalpEntryButtons.Count; sbi++)
				{
					if (scalpEntryButtons[sbi] != null)
					{
						scalpEntryButtons[sbi].IsEnabled = !isBlocked;
						scalpEntryButtons[sbi].Opacity = isBlocked ? 0.45 : 1.0;
					}
				}
				if (btnQuickRevRef != null) { btnQuickRevRef.IsEnabled = !isBlocked; btnQuickRevRef.Opacity = isBlocked ? 0.45 : 1.0; }
				if (btnScalpRevRef != null) { btnScalpRevRef.IsEnabled = !isBlocked; btnScalpRevRef.Opacity = isBlocked ? 0.45 : 1.0; }

				// Scalp Tab Working Orders List
				if (chkScalpAutoBracket != null) chkScalpAutoBracket.IsChecked = scalpUseAutoBracket;
				if (txtScalpBracketInfo != null)
					txtScalpBracketInfo.Text = string.Format(CultureInfo.InvariantCulture, "SL {0}T / TP {1}T", orderSlTicks, orderTpTicks);

				if (isTradeTabScalp && pnlScalpWorkingOrders != null)
				{
					pnlScalpWorkingOrders.Children.Clear();
					List<Order> working = GetCurrentInstrumentWorkingOrders(acc);
					if (working.Count == 0)
					{
						pnlScalpWorkingOrders.Children.Add(new WpfControls.TextBlock
						{
							Text = "No active working orders",
							FontSize = 10.5,
							Foreground = MakeFrozenBrush(100, 116, 139)
						});
					}
					else
					{
						for (int i = 0; i < working.Count; i++)
						{
							Order wo = working[i];
							double wPx = GetWorkingOrderEffectivePrice(wo);
							int role = ClassifyWorkingOrderRole(wo, pos);
							string tag = role == 1 ? "TP " : (role == 2 ? "SL " : (IsOrderBuySide(wo.OrderAction) ? "BUY " : "SELL "));

							var rowBd = new WpfControls.Border
							{
								Background = MakeFrozenBrush(18, 22, 30),
								BorderBrush = MakeFrozenBrush(33, 40, 54),
								BorderThickness = new Thickness(1),
								CornerRadius = new CornerRadius(4),
								Padding = new Thickness(6, 3, 4, 3),
								Margin = new Thickness(0, 0, 0, 4)
							};
							var dp = new WpfControls.DockPanel();
							Order capWo = wo;
							var btnCxlOne = CreateFlatDarkButton("✕", WpfMedia.Brushes.Transparent, WpfMedia.Brushes.Transparent, MakeFrozenBrush(248, 113, 113), 10.2, 3.0, new Thickness(5, 1, 5, 1));
							btnCxlOne.Click += (s, e) =>
							{
								try { acc?.Cancel(new[] { capWo }); } catch { }
							};
							WpfControls.DockPanel.SetDock(btnCxlOne, WpfControls.Dock.Right);
							dp.Children.Add(btnCxlOne);

							dp.Children.Add(new WpfControls.TextBlock
							{
								Text = tag + wo.Quantity + " @ " + FormatOrderflowPrice(wPx),
								FontSize = 10.8,
								FontWeight = FontWeights.SemiBold,
								Foreground = role == 1 ? MakeFrozenBrush(52, 211, 153) : (role == 2 ? MakeFrozenBrush(248, 113, 113) : txtBright),
								VerticalAlignment = VerticalAlignment.Center
							});
							rowBd.Child = dp;
							pnlScalpWorkingOrders.Children.Add(rowBd);
						}
					}
				}

				if (txtTradeStatusToast != null && !string.IsNullOrEmpty(txtTradeStatusToast.Text) && DateTime.UtcNow > tradeToastExpireUtc)
				{
					txtTradeStatusToast.Text = string.Empty;
				}
			}
			catch { }
			finally
			{
				isSyncingTradePanelUi = false;
			}
		}

		// ====================================================================================================
		// DIRECT2D LIVE POSITION / WORKING ORDERS OVERLAY + INTERACTIVE DRAG TP / SL / ENTRY ON CHART
		// ====================================================================================================
		private int HitTestLiveOrderRegionIndex(float pxX, float pxY, out Cursor hoverCursor)
		{
			hoverCursor = null;
			if (!EnableLiveChartOrders || liveChartHitRegions.Count == 0 || IsPointOverRightScaleLane(pxX, pxY))
				return -1;

			// 1st Pass: Exact pill / action button rectangles (reverse draw order = topmost first)
			for (int i = liveChartHitRegions.Count - 1; i >= 0; i--)
			{
				LiveChartHitRegion r = liveChartHitRegions[i];
				if (r.Rect.Width > 0f && ContainsPtPadded(r.Rect, pxX, pxY, 2.0f))
				{
					hoverCursor = (r.Kind == LiveOrderHitKind.ClosePositionBtn ||
						r.Kind == LiveOrderHitKind.ReversePositionBtn ||
						r.Kind == LiveOrderHitKind.BreakevenPositionBtn ||
						r.Kind == LiveOrderHitKind.CancelWorkingOrderBtn ||
						r.Kind == LiveOrderHitKind.CancelPendingBracketTpBtn ||
						r.Kind == LiveOrderHitKind.CancelPendingBracketSlBtn)
						? Cursors.Hand
						: Cursors.SizeNS;
					return i;
				}
			}

			// 2nd Pass: Horizontal draggable order line hit-testing (±5.5px around TP / SL / Entry lines)
			for (int i = liveChartHitRegions.Count - 1; i >= 0; i--)
			{
				LiveChartHitRegion r = liveChartHitRegions[i];
				if (r.AllowLineGrab && pxX >= r.LineX0 && pxX <= r.LineX1 && Math.Abs(pxY - r.LineY) <= 5.5f)
				{
					hoverCursor = Cursors.SizeNS;
					return i;
				}
			}

			return -1;
		}

		private bool TryBeginLiveOrderCanvasInteraction(ChartControl cc, float pxX, float pxY)
		{
			if (!EnableLiveChartOrders || cc == null || lastRenderedChartScale == null)
				return false;

			int hitIdx = HitTestLiveOrderRegionIndex(pxX, pxY, out _);
			if (hitIdx < 0 || hitIdx >= liveChartHitRegions.Count)
				return false;

			LiveChartHitRegion hit = liveChartHitRegions[hitIdx];
			Account acc = EnsureActiveTradeAccountSelected();

			switch (hit.Kind)
			{
				case LiveOrderHitKind.ClosePositionBtn:
					ExecuteFlattenCurrentInstrument();
					return true;

				case LiveOrderHitKind.ReversePositionBtn:
					ExecuteReverseCurrentPosition();
					return true;

				case LiveOrderHitKind.BreakevenPositionBtn:
					ExecuteMoveStopToBreakeven();
					return true;

				case LiveOrderHitKind.CancelWorkingOrderBtn:
					if (hit.TargetOrder != null && acc != null)
					{
						try
						{
							acc.Cancel(new[] { hit.TargetOrder });
							ShowTradeToast("Cancelled order @ " + FormatOrderflowPrice(hit.InitialPrice));
						}
						catch (Exception ex)
						{
							ShowTradeToast("Cancel failed: " + ex.Message, true);
						}
						RequestImmediateOrderflowRedraw();
					}
					return true;

				case LiveOrderHitKind.AddTpDragChip:
					activeLiveOrderDragMode = LiveOrderDragMode.DragNewTpFromPosition;
					activeDraggedWorkingOrder = null;
					liveOrderDragStartPrice = hit.InitialPrice;
					liveOrderDragCurrentPrice = hit.InitialPrice;
					liveOrderDragStartMouseY = pxY;
					liveOrderDragMoved = false;
					EnsureAutoYAxisFor360View();
					RequestImmediateOrderflowRedraw();
					return true;

				case LiveOrderHitKind.AddSlDragChip:
					activeLiveOrderDragMode = LiveOrderDragMode.DragNewSlFromPosition;
					activeDraggedWorkingOrder = null;
					liveOrderDragStartPrice = hit.InitialPrice;
					liveOrderDragCurrentPrice = hit.InitialPrice;
					liveOrderDragStartMouseY = pxY;
					liveOrderDragMoved = false;
					EnsureAutoYAxisFor360View();
					RequestImmediateOrderflowRedraw();
					return true;

				case LiveOrderHitKind.WorkingOrderDrag:
					if (hit.TargetOrder == null)
						return false;
					activeLiveOrderDragMode = LiveOrderDragMode.DragWorkingOrder;
					activeDraggedWorkingOrder = hit.TargetOrder;
					activeDraggedOrderIsStop = hit.IsStopOrder;
					liveOrderDragStartPrice = hit.InitialPrice;
					liveOrderDragCurrentPrice = hit.InitialPrice;
					liveOrderDragStartMouseY = pxY;
					liveOrderDragMoved = false;
					EnsureAutoYAxisFor360View();
					RequestImmediateOrderflowRedraw();
					return true;

				case LiveOrderHitKind.DraftEntryDrag:
					activeLiveOrderDragMode = LiveOrderDragMode.DragDraftEntry;
					liveOrderDragStartPrice = hit.InitialPrice;
					liveOrderDragCurrentPrice = hit.InitialPrice;
					liveOrderDragStartMouseY = pxY;
					liveOrderDragMoved = false;
					EnsureAutoYAxisFor360View();
					RequestImmediateOrderflowRedraw();
					return true;

				case LiveOrderHitKind.DraftTpDrag:
					activeLiveOrderDragMode = LiveOrderDragMode.DragDraftTp;
					liveOrderDragStartPrice = hit.InitialPrice;
					liveOrderDragCurrentPrice = hit.InitialPrice;
					liveOrderDragStartMouseY = pxY;
					liveOrderDragMoved = false;
					EnsureAutoYAxisFor360View();
					RequestImmediateOrderflowRedraw();
					return true;

				case LiveOrderHitKind.DraftSlDrag:
					activeLiveOrderDragMode = LiveOrderDragMode.DragDraftSl;
					liveOrderDragStartPrice = hit.InitialPrice;
					liveOrderDragCurrentPrice = hit.InitialPrice;
					liveOrderDragStartMouseY = pxY;
					liveOrderDragMoved = false;
					EnsureAutoYAxisFor360View();
					RequestImmediateOrderflowRedraw();
					return true;

				case LiveOrderHitKind.PendingBracketTpDrag:
					if (hit.TargetPendingSpec == null)
						return false;
					activeLiveOrderDragMode = LiveOrderDragMode.DragPendingBracketTp;
					activeDraggedPendingSpec = hit.TargetPendingSpec;
					liveOrderDragStartPrice = hit.InitialPrice;
					liveOrderDragCurrentPrice = hit.InitialPrice;
					liveOrderDragStartMouseY = pxY;
					liveOrderDragMoved = false;
					EnsureAutoYAxisFor360View();
					RequestImmediateOrderflowRedraw();
					return true;

				case LiveOrderHitKind.PendingBracketSlDrag:
					if (hit.TargetPendingSpec == null)
						return false;
					activeLiveOrderDragMode = LiveOrderDragMode.DragPendingBracketSl;
					activeDraggedPendingSpec = hit.TargetPendingSpec;
					liveOrderDragStartPrice = hit.InitialPrice;
					liveOrderDragCurrentPrice = hit.InitialPrice;
					liveOrderDragStartMouseY = pxY;
					liveOrderDragMoved = false;
					EnsureAutoYAxisFor360View();
					RequestImmediateOrderflowRedraw();
					return true;

				case LiveOrderHitKind.CancelPendingBracketTpBtn:
					if (hit.TargetPendingSpec != null)
					{
						lock (bracketLock) { hit.TargetPendingSpec.HasTp = false; }
						ShowTradeToast("Removed pending Take Profit");
						RequestImmediateOrderflowRedraw();
					}
					return true;

				case LiveOrderHitKind.CancelPendingBracketSlBtn:
					if (hit.TargetPendingSpec != null)
					{
						if (EnableCapitalProtector && CapitalProtectorAmount > 0)
						{
							ShowTradeToast("🛡 Capital Protector keeps Stop Loss active", true);
						}
						else
						{
							lock (bracketLock) { hit.TargetPendingSpec.HasSl = false; }
							ShowTradeToast("Removed pending Stop Loss");
							RequestImmediateOrderflowRedraw();
						}
					}
					return true;
			}

			return false;
		}

		private bool UpdateLiveOrderCanvasDrag(ChartControl cc, float curPxX, float curPxY)
		{
			ChartScale cs = lastRenderedChartScale;
			if (cs == null || activeLiveOrderDragMode == LiveOrderDragMode.None)
				return false;

			if (Math.Abs(curPxY - liveOrderDragStartMouseY) >= 2.0f)
				liveOrderDragMoved = true;

			double tick = TickSize > 0 ? TickSize : 0.05;
			double rawPx = RoundToInstrumentTick(cs.GetValueByY(curPxY));
			if (rawPx <= 0)
				return false;

			liveOrderDragCurrentPrice = rawPx;

			// Real-time two-way sync when dragging pre-confirmation Draft Entry / TP / SL lines!
			if (activeLiveOrderDragMode == LiveOrderDragMode.DragDraftEntry)
			{
				orderCustomEntryPrice = rawPx;
				orderCustomEntrySeeded = true;
				RequestTradeUiSyncAsync();
			}
			else if (activeLiveOrderDragMode == LiveOrderDragMode.DragDraftTp)
			{
				double refEntry = GetCurrentOrderReferenceEntryPrice();
				double clamped = isOrderSideBuy ? Math.Max(refEntry + tick, rawPx) : Math.Min(refEntry - tick, rawPx);
				liveOrderDragCurrentPrice = RoundToInstrumentTick(clamped);
				orderTpTicks = Math.Max(1, (int)Math.Round(Math.Abs(liveOrderDragCurrentPrice - refEntry) / tick));
				orderTpManuallyCustomized = true;
				RequestTradeUiSyncAsync();
			}
			else if (activeLiveOrderDragMode == LiveOrderDragMode.DragDraftSl)
			{
				double refEntry = GetCurrentOrderReferenceEntryPrice();
				int maxCapTicks = ComputeCapitalProtectorMaxSlTicks(orderQtyValue);
				double clamped = isOrderSideBuy
					? Math.Max(refEntry - maxCapTicks * tick, Math.Min(refEntry - tick, rawPx))
					: Math.Min(refEntry + maxCapTicks * tick, Math.Max(refEntry + tick, rawPx));
				liveOrderDragCurrentPrice = RoundToInstrumentTick(clamped);
				orderSlTicks = Math.Max(1, (int)Math.Round(Math.Abs(refEntry - liveOrderDragCurrentPrice) / tick));
				RequestTradeUiSyncAsync();
			}
			else if (activeLiveOrderDragMode == LiveOrderDragMode.DragPendingBracketTp && activeDraggedPendingSpec != null)
			{
				double entPx = GetWorkingOrderEffectivePrice(activeDraggedPendingSpec.EntryOrder);
				if (entPx > 0)
				{
					double clamped = activeDraggedPendingSpec.IsLong ? Math.Max(entPx + tick, rawPx) : Math.Min(entPx - tick, rawPx);
					liveOrderDragCurrentPrice = RoundToInstrumentTick(clamped);
					int newTpT = Math.Max(1, (int)Math.Round(Math.Abs(liveOrderDragCurrentPrice - entPx) / tick));
					lock (bracketLock) { activeDraggedPendingSpec.TpTicks = newTpT; }
					orderTpTicks = newTpT;
					orderTpManuallyCustomized = true;
					RequestTradeUiSyncAsync();
				}
			}
			else if (activeLiveOrderDragMode == LiveOrderDragMode.DragPendingBracketSl && activeDraggedPendingSpec != null)
			{
				double entPx = GetWorkingOrderEffectivePrice(activeDraggedPendingSpec.EntryOrder);
				if (entPx > 0)
				{
					int maxCapTicks = ComputeCapitalProtectorMaxSlTicks(activeDraggedPendingSpec.TotalQty);
					double clamped = activeDraggedPendingSpec.IsLong
						? Math.Max(entPx - maxCapTicks * tick, Math.Min(entPx - tick, rawPx))
						: Math.Min(entPx + maxCapTicks * tick, Math.Max(entPx + tick, rawPx));
					liveOrderDragCurrentPrice = RoundToInstrumentTick(clamped);
					int newSlT = Math.Max(1, (int)Math.Round(Math.Abs(entPx - liveOrderDragCurrentPrice) / tick));
					lock (bracketLock) { activeDraggedPendingSpec.SlTicks = newSlT; }
					orderSlTicks = newSlT;
					RequestTradeUiSyncAsync();
				}
			}
			else if (activeLiveOrderDragMode == LiveOrderDragMode.DragWorkingOrder && activeDraggedWorkingOrder != null && EnableCapitalProtector && CapitalProtectorAmount > 0)
			{
				Account accDrag = activeTradeAccount ?? EnsureActiveTradeAccountSelected();
				Position posDrag = GetCurrentInstrumentPosition(accDrag, nonBlocking: true);
				if (posDrag != null && posDrag.MarketPosition != MarketPosition.Flat && ClassifyWorkingOrderRole(activeDraggedWorkingOrder, posDrag) == 2)
				{
					int maxCapTicks = ComputeCapitalProtectorMaxSlTicks(posDrag.Quantity);
					bool pL = (posDrag.MarketPosition == MarketPosition.Long);
					double minOrMaxSl = pL
						? RoundToInstrumentTick(posDrag.AveragePrice - maxCapTicks * tick)
						: RoundToInstrumentTick(posDrag.AveragePrice + maxCapTicks * tick);
					if (pL && liveOrderDragCurrentPrice < minOrMaxSl)
						liveOrderDragCurrentPrice = minOrMaxSl;
					else if (!pL && liveOrderDragCurrentPrice > minOrMaxSl)
						liveOrderDragCurrentPrice = minOrMaxSl;
				}
			}

			RequestImmediateOrderflowRedraw();
			return true;
		}

		private void CommitLiveOrderCanvasDrag(ChartControl cc)
		{
			LiveOrderDragMode mode = activeLiveOrderDragMode;
			Order targetOrd = activeDraggedWorkingOrder;
			double dropPrice = RoundToInstrumentTick(liveOrderDragCurrentPrice);
			bool didMove = liveOrderDragMoved;

			activeLiveOrderDragMode = LiveOrderDragMode.None;
			activeDraggedWorkingOrder = null;
			activeDraggedPendingSpec = null;
			liveOrderDragMoved = false;

			Account acc = EnsureActiveTradeAccountSelected();
			double tick = TickSize > 0 ? TickSize : 0.05;

			if (mode == LiveOrderDragMode.DragWorkingOrder && targetOrd != null && acc != null)
			{
				if (didMove && Math.Abs(dropPrice - liveOrderDragStartPrice) >= tick * 0.5)
				{
					ModifyLiveWorkingOrderPrice(acc, targetOrd, dropPrice);
				}
			}
			else if (mode == LiveOrderDragMode.DragNewTpFromPosition)
			{
				Position pos = GetCurrentInstrumentPosition(acc, nonBlocking: true);
				if (pos != null && pos.MarketPosition != MarketPosition.Flat)
				{
					bool pLong = (pos.MarketPosition == MarketPosition.Long);
					double tpPx = didMove
						? dropPrice
						: RoundToInstrumentTick(pLong ? (pos.AveragePrice + Math.Max(1, orderTpTicks) * tick) : (pos.AveragePrice - Math.Max(1, orderTpTicks) * tick));
					SubmitProtectiveExitOrderFromChart(true, tpPx);
				}
			}
			else if (mode == LiveOrderDragMode.DragNewSlFromPosition)
			{
				Position pos = GetCurrentInstrumentPosition(acc, nonBlocking: true);
				if (pos != null && pos.MarketPosition != MarketPosition.Flat)
				{
					bool pLong = (pos.MarketPosition == MarketPosition.Long);
					double slPx = didMove
						? dropPrice
						: RoundToInstrumentTick(pLong ? (pos.AveragePrice - Math.Max(1, orderSlTicks) * tick) : (pos.AveragePrice + Math.Max(1, orderSlTicks) * tick));
					SubmitProtectiveExitOrderFromChart(false, slPx);
				}
			}

			RequestTradeUiSyncAsync();
			RequestImmediateOrderflowRedraw();
		}

		private float ResolveLivePositionEntryStartX(ChartControl cc, Account acc, Position pos, float panelX, float clipRight)
		{
			if (cc == null || ChartBars == null || ChartBars.Bars == null || ChartBars.Bars.Count == 0 || pos == null)
				return Math.Max(panelX + 4f, clipRight - 320f);

			string posKey = string.Concat(
				acc != null ? acc.Name : string.Empty, "|",
				pos.MarketPosition.ToString(), "|",
				pos.Quantity.ToString(CultureInfo.InvariantCulture), "|",
				pos.AveragePrice.ToString("0.######", CultureInfo.InvariantCulture));

			DateTime entryTime = string.Equals(posKey, cachedPositionEntryKey, StringComparison.Ordinal)
				? cachedPositionEntryTime
				: DateTime.MinValue;

			if (entryTime == DateTime.MinValue)
			{
				try
				{
					if (pos.FirstEntry != null && pos.FirstEntry.Time > DateTime.MinValue)
						entryTime = pos.FirstEntry.Time;
				}
				catch { }
			}

			if (entryTime == DateTime.MinValue)
			{
				try
				{
					Execution[] entries = pos.Entries;
					if (entries != null && entries.Length > 0 && entries[0] != null && entries[0].Time > DateTime.MinValue)
						entryTime = entries[0].Time;
				}
				catch { }
			}

			if (entryTime == DateTime.MinValue && acc != null && acc.Orders != null)
			{
				try
				{
					Order[] snap = null;
					bool entered = false;
					try
					{
						System.Threading.Monitor.TryEnter(acc.Orders, 0, ref entered);
						if (entered)
							snap = acc.Orders.ToArray();
					}
					finally
					{
						if (entered)
							System.Threading.Monitor.Exit(acc.Orders);
					}
					if (snap != null)
					{
						for (int i = snap.Length - 1; i >= 0; i--)
						{
							Order o = snap[i];
							if (o != null && o.OrderState == OrderState.Filled && InstrumentMatchesChart(o.Instrument) && o.Time > DateTime.MinValue)
							{
								entryTime = o.Time;
								break;
							}
						}
					}
				}
				catch { }
			}

			if (entryTime > DateTime.MinValue)
			{
				cachedPositionEntryKey = posKey;
				cachedPositionEntryTime = entryTime;
				float rawX = ResolveSrUnclampedTimeX(cc, ChartBars.Bars, panelX, entryTime);
				return Math.Max(panelX + 2f, Math.Min(clipRight - 64f, rawX));
			}

			int fallbackIdx = Math.Max(0, ChartBars.Bars.Count - 4);
			float fbX = (float)cc.GetXByBarIndex(ChartBars, fallbackIdx);
			return Math.Max(panelX + 2f, Math.Min(clipRight - 64f, fbX));
		}

		private void DrawVectorDragGrip(float cx, float cy, DxBrush brush, StrokeStyle roundCapStyle = null)
		{
			// Smooth anti-aliased up/down chevron pair with rounded caps
			RenderTarget.DrawLine(new Vector2(cx - 2.4f, cy - 1.3f), new Vector2(cx, cy - 3.8f), brush, 1.25f, roundCapStyle);
			RenderTarget.DrawLine(new Vector2(cx + 2.4f, cy - 1.3f), new Vector2(cx, cy - 3.8f), brush, 1.25f, roundCapStyle);
			RenderTarget.DrawLine(new Vector2(cx - 2.4f, cy + 1.3f), new Vector2(cx, cy + 3.8f), brush, 1.25f, roundCapStyle);
			RenderTarget.DrawLine(new Vector2(cx + 2.4f, cy + 1.3f), new Vector2(cx, cy + 3.8f), brush, 1.25f, roundCapStyle);
		}

		private void DrawVectorCloseX(float cx, float cy, float halfSz, DxBrush brush, StrokeStyle roundCapStyle = null)
		{
			RenderTarget.DrawLine(new Vector2(cx - halfSz, cy - halfSz), new Vector2(cx + halfSz, cy + halfSz), brush, 1.35f, roundCapStyle);
			RenderTarget.DrawLine(new Vector2(cx + halfSz, cy - halfSz), new Vector2(cx - halfSz, cy + halfSz), brush, 1.35f, roundCapStyle);
		}

		private static float MeasurePillTextWidth(SharpDX.DirectWrite.Factory dw, DxTextFormat fmt, string text)
		{
			if (dw == null || fmt == null || string.IsNullOrEmpty(text))
				return 0f;
			using (var tl = new SharpDX.DirectWrite.TextLayout(dw, text, fmt, 2400f, 64f))
			{
				return tl.Metrics.WidthIncludingTrailingWhitespace;
			}
		}

		private static string FormatLiveRiskRewardBadge(double entryPx, double slPx, double tpPx, double tick)
		{
			if (entryPx <= 0.0 || slPx <= 0.0 || tpPx <= 0.0)
				return string.Empty;
			double slDist = Math.Abs(entryPx - slPx);
			double tpDist = Math.Abs(tpPx - entryPx);
			if (slDist < Math.Max(1e-6, tick * 0.25))
				return "R:R BE";
			double rr = tpDist / slDist;
			string rrNum = rr >= 1.0
				? rr.ToString("0.#", CultureInfo.InvariantCulture)
				: rr.ToString("0.##", CultureInfo.InvariantCulture);
			return "R:R 1:" + rrNum;
		}

		private void RenderLiveOrdersAndDraftPreviewOnCanvas(
			ChartControl cc,
			Func<double, float> priceToY,
			float panelX,
			float canvasTopY,
			float clipW,
			float chartBottomY,
			bool light,
			bool drawUnderlayPass = true,
			bool drawOverlayPillsPass = true)
		{
			if (drawOverlayPillsPass)
				liveChartHitRegions.Clear();
			if (cc == null || clipW <= 60f || chartBottomY <= canvasTopY + 30f)
				return;

			Account acc = EnsureActiveTradeAccountSelected();
			string curSym = GetAccountCurrencyPrefix(acc);
			double tick = TickSize > 0 ? TickSize : 0.05;
			double pv = (Instrument != null && Instrument.MasterInstrument != null && Instrument.MasterInstrument.PointValue > 0)
				? Instrument.MasterInstrument.PointValue
				: 1.0;
			ResolveLiveBidAskLast(out _, out _, out double lastPx);

			Position pos = GetCurrentInstrumentPosition(acc, nonBlocking: true);
			bool hasOpenPos = (pos != null && pos.MarketPosition != MarketPosition.Flat && pos.Quantity > 0);
			List<Order> workingOrders = GetCurrentInstrumentWorkingOrders(acc, nonBlocking: true);

			float clipRight = panelX + clipW;
			float pillRightAnchorX = clipRight - 8f;
			float entryAnchorX = hasOpenPos ? ResolveLivePositionEntryStartX(cc, acc, pos, panelX, clipRight) : (panelX + 2f);

			var dashProps = new StrokeStyleProperties
			{
				DashStyle = SharpDX.Direct2D1.DashStyle.Custom,
				DashCap = CapStyle.Round,
				StartCap = CapStyle.Round,
				EndCap = CapStyle.Round,
				LineJoin = LineJoin.Round
			};
			var roundSolidProps = new StrokeStyleProperties
			{
				DashStyle = SharpDX.Direct2D1.DashStyle.Solid,
				StartCap = CapStyle.Round,
				EndCap = CapStyle.Round,
				LineJoin = LineJoin.Round
			};

			AntialiasMode prevAa = RenderTarget.AntialiasMode;
			SharpDX.Direct2D1.TextAntialiasMode prevTextAa = RenderTarget.TextAntialiasMode;
			RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;
			RenderTarget.TextAntialiasMode = SharpDX.Direct2D1.TextAntialiasMode.Grayscale;

			var dw = Globals.DirectWriteFactory;
			try
			{
				using (var roundCap = new StrokeStyle(Globals.D2DFactory, roundSolidProps))
				using (var orderDash = new StrokeStyle(Globals.D2DFactory, dashProps, new float[] { 5f, 3.5f }))
				using (var draftDash = new StrokeStyle(Globals.D2DFactory, dashProps, new float[] { 4f, 4f }))
				using (var tfPill = new DxTextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.SemiBold, SharpDX.DirectWrite.FontStyle.Normal, 9.5f))
				using (var tfSmall = new DxTextFormat(dw, "Segoe UI", SharpDX.DirectWrite.FontWeight.SemiBold, SharpDX.DirectWrite.FontStyle.Normal, 8.9f))
				using (var posBgBrush = new DxBrush(RenderTarget, new Color4(0.055f, 0.082f, 0.128f, 0.96f)))
				using (var greenDarkPill = new DxBrush(RenderTarget, new Color4(0.028f, 0.215f, 0.165f, 0.96f)))
				using (var greenBright = new DxBrush(RenderTarget, new Color4(0.10f, 0.84f, 0.60f, 0.95f)))
				using (var redDarkPill = new DxBrush(RenderTarget, new Color4(0.265f, 0.058f, 0.105f, 0.96f)))
				using (var redCloseBg = new DxBrush(RenderTarget, new Color4(0.56f, 0.10f, 0.18f, 0.96f)))
				using (var redBright = new DxBrush(RenderTarget, new Color4(0.98f, 0.38f, 0.45f, 0.95f)))
				using (var blueDarkPill = new DxBrush(RenderTarget, new Color4(0.06f, 0.17f, 0.35f, 0.96f)))
				using (var blueEntry = new DxBrush(RenderTarget, new Color4(0.24f, 0.58f, 1.0f, 0.95f)))
				using (var amberDarkPill = new DxBrush(RenderTarget, new Color4(0.30f, 0.17f, 0.04f, 0.96f)))
				using (var amberEntry = new DxBrush(RenderTarget, new Color4(0.98f, 0.66f, 0.14f, 0.95f)))
				using (var chipBg = new DxBrush(RenderTarget, new Color4(0.085f, 0.125f, 0.190f, 0.96f)))
				using (var chipBorder = new DxBrush(RenderTarget, new Color4(0.38f, 0.49f, 0.64f, 0.92f)))
				using (var pillSheen = new DxBrush(RenderTarget, new Color4(1.0f, 1.0f, 1.0f, 0.06f)))
				using (var tpShade = new DxBrush(RenderTarget, new Color4(0.059f, 0.650f, 0.460f, light ? 0.08f : 0.10f)))
				using (var slShade = new DxBrush(RenderTarget, new Color4(0.900f, 0.160f, 0.260f, light ? 0.08f : 0.10f)))
				using (var txtWhite = new DxBrush(RenderTarget, new Color4(0.975f, 0.985f, 1.0f, 1.0f)))
				{
					tfPill.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
					tfPill.ParagraphAlignment = ParagraphAlignment.Center;
					tfPill.WordWrapping = WordWrapping.NoWrap;

					tfSmall.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
					tfSmall.ParagraphAlignment = ParagraphAlignment.Center;
					tfSmall.WordWrapping = WordWrapping.NoWrap;

					bool hasWorkingTp = false;
					bool hasWorkingSl = false;
					double workingTpPx = 0.0;
					double workingSlPx = 0.0;
					double tpWeightedSum = 0.0, tpQtySum = 0.0;
					double slWeightedSum = 0.0, slQtySum = 0.0;
					for (int i = 0; i < workingOrders.Count; i++)
					{
						Order woItem = workingOrders[i];
						int r = ClassifyWorkingOrderRole(woItem, pos);
						double p = (activeLiveOrderDragMode == LiveOrderDragMode.DragWorkingOrder && ReferenceEquals(activeDraggedWorkingOrder, woItem))
							? liveOrderDragCurrentPrice
							: GetWorkingOrderEffectivePrice(woItem);
						if (p <= 0.0)
							continue;
						double q = Math.Max(1, woItem.Quantity);
						if (r == 1)
						{
							if (!hasWorkingTp) { hasWorkingTp = true; workingTpPx = p; }
							tpWeightedSum += p * q;
							tpQtySum += q;
						}
						else if (r == 2)
						{
							if (!hasWorkingSl) { hasWorkingSl = true; workingSlPx = p; }
							slWeightedSum += p * q;
							slQtySum += q;
						}
					}

					double effTpPx = tpQtySum > 0.0 ? (tpWeightedSum / tpQtySum) : workingTpPx;
					double effSlPx = slQtySum > 0.0 ? (slWeightedSum / slQtySum) : workingSlPx;
					bool hasEffTp = hasWorkingTp && effTpPx > 0.0;
					bool hasEffSl = hasWorkingSl && effSlPx > 0.0;
					if (!hasEffTp && activeLiveOrderDragMode == LiveOrderDragMode.DragNewTpFromPosition && liveOrderDragCurrentPrice > 0.0)
					{
						hasEffTp = true;
						effTpPx = liveOrderDragCurrentPrice;
					}
					if (!hasEffSl && activeLiveOrderDragMode == LiveOrderDragMode.DragNewSlFromPosition && liveOrderDragCurrentPrice > 0.0)
					{
						hasEffSl = true;
						effSlPx = liveOrderDragCurrentPrice;
					}

					void DrawSmoothPillText(string text, DxTextFormat fmt, RectangleF rc, DxBrush brush)
					{
						if (string.IsNullOrEmpty(text))
							return;
						var textRc = new RectangleF(rc.X, rc.Y - 2f, rc.Width, rc.Height + 4f);
						RenderTarget.DrawText(text, fmt, textRc, brush, DrawTextOptions.NoSnap);
					}

					// 1. Live Position Shaded TP & SL Projection Zones — Drawn in Underlay Pass BEHIND Candles with NO Vertical Candle-Occluding Line!
					if (drawUnderlayPass && hasOpenPos)
					{
						float yPos = priceToY(pos.AveragePrice);
						float shadeLeft = entryAnchorX;
						float shadeRight = clipRight - 2f;
						float shadeW = Math.Max(16f, shadeRight - shadeLeft);

						if (hasWorkingTp && workingTpPx > 0)
						{
							float yTp = priceToY(workingTpPx);
							float t0 = Math.Max(canvasTopY, Math.Min(yPos, yTp));
							float b0 = Math.Min(chartBottomY, Math.Max(yPos, yTp));
							if (b0 > t0 + 1f)
								RenderTarget.FillRectangle(new RectangleF(shadeLeft, t0, shadeW, b0 - t0), tpShade);
						}
						if (hasWorkingSl && workingSlPx > 0)
						{
							float ySl = priceToY(workingSlPx);
							float t0 = Math.Max(canvasTopY, Math.Min(yPos, ySl));
							float b0 = Math.Min(chartBottomY, Math.Max(yPos, ySl));
							if (b0 > t0 + 1f)
								RenderTarget.FillRectangle(new RectangleF(shadeLeft, t0, shadeW, b0 - t0), slShade);
						}
					}

					// 2. Render Working Orders (Lines in Underlay Pass BEHIND Candles; Draggable Pills in Overlay Pass + Pre-Trigger 1:3 Pattern for Pending Limit/Stop!)
					float pendingEntryAnchorX = (ChartBars != null && ChartBars.Count > 0)
						? Math.Max(panelX + 2f, Math.Min(clipRight - 140f, (float)cc.GetXByBarIndex(ChartBars, Math.Max(0, ChartBars.Count - 1))))
						: (panelX + 2f);

					for (int i = 0; i < workingOrders.Count; i++)
					{
						Order wo = workingOrders[i];
						bool isDraggingThis = (activeLiveOrderDragMode == LiveOrderDragMode.DragWorkingOrder && ReferenceEquals(activeDraggedWorkingOrder, wo));
						double ordPrice = isDraggingThis ? liveOrderDragCurrentPrice : GetWorkingOrderEffectivePrice(wo);
						if (ordPrice <= 0)
							continue;

						float yOrd = priceToY(ordPrice);
						int role = ClassifyWorkingOrderRole(wo, pos);
						bool isBuyOrd = IsOrderBuySide(wo.OrderAction);
						PendingBracketSpec pendSpec = (!hasOpenPos && role == 0) ? FindPendingBracketForEntryOrder(wo) : null;

						// Auto-attach a live 1:3 PendingBracketSpec if a working Buy/Sell Limit or Stop entry exists without one yet!
						if (!hasOpenPos && role == 0 && pendSpec == null && DefaultEnableBracketSlTp)
						{
							int baseSlT = Math.Min(Math.Max(1, orderSlTicks > 0 ? orderSlTicks : DefaultSlTicks), ComputeCapitalProtectorMaxSlTicks(wo.Quantity));
							int baseTpT = Math.Max(1, orderTpTicks > 0 ? orderTpTicks : (int)Math.Round(baseSlT * Math.Max(0.5, InitialOrderRrRatio)));
							pendSpec = new PendingBracketSpec
							{
								EntryOrder = wo,
								Account = acc,
								Instrument = wo.Instrument ?? Instrument,
								IsLong = isBuyOrd,
								TotalQty = Math.Max(1, wo.Quantity),
								BracketedQty = 0,
								Tif = wo.TimeInForce,
								HasSl = true,
								SlTicks = baseSlT,
								HasTp = true,
								TpTicks = baseTpT,
								TrailEnabled = orderTrailEnabled,
								TrailTicks = Math.Max(1, orderTrailTicks),
								CreatedUtc = DateTime.UtcNow
							};
							lock (bracketLock) { pendingEntryBrackets.Add(pendSpec); }
						}

						double pendTpPx = 0.0;
						double pendSlPx = 0.0;
						bool showPendTp = (pendSpec != null && pendSpec.HasTp && pendSpec.TpTicks > 0);
						bool showPendSl = (pendSpec != null && pendSpec.HasSl && pendSpec.SlTicks > 0);
						if (showPendTp)
						{
							pendTpPx = (activeLiveOrderDragMode == LiveOrderDragMode.DragPendingBracketTp && ReferenceEquals(activeDraggedPendingSpec, pendSpec))
								? liveOrderDragCurrentPrice
								: RoundToInstrumentTick(pendSpec.IsLong ? (ordPrice + pendSpec.TpTicks * tick) : (ordPrice - pendSpec.TpTicks * tick));
						}
						if (showPendSl)
						{
							pendSlPx = (activeLiveOrderDragMode == LiveOrderDragMode.DragPendingBracketSl && ReferenceEquals(activeDraggedPendingSpec, pendSpec))
								? liveOrderDragCurrentPrice
								: RoundToInstrumentTick(pendSpec.IsLong ? (ordPrice - pendSpec.SlTicks * tick) : (ordPrice + pendSpec.SlTicks * tick));
						}

						// Pre-Trigger Shaded 1:3 R:R Projection Zones + Left-Extended Pending SL/TP Lines (BEHIND Candles!)
						if (drawUnderlayPass && pendSpec != null)
						{
							float pShadeW = Math.Max(16f, (clipRight - 2f) - pendingEntryAnchorX);
							if (showPendTp && pendTpPx > 0)
							{
								float yPTp = priceToY(pendTpPx);
								float t0 = Math.Max(canvasTopY, Math.Min(yOrd, yPTp));
								float b0 = Math.Min(chartBottomY, Math.Max(yOrd, yPTp));
								if (b0 > t0 + 1f)
									RenderTarget.FillRectangle(new RectangleF(pendingEntryAnchorX, t0, pShadeW, b0 - t0), tpShade);
								if (yPTp >= canvasTopY - 4f && yPTp <= chartBottomY + 4f)
									DrawHairlineHorizontal(panelX + 2f, clipRight - 2f, yPTp, greenBright, 1.2f, orderDash);
							}
							if (showPendSl && pendSlPx > 0)
							{
								float yPSl = priceToY(pendSlPx);
								float t0 = Math.Max(canvasTopY, Math.Min(yOrd, yPSl));
								float b0 = Math.Min(chartBottomY, Math.Max(yOrd, yPSl));
								if (b0 > t0 + 1f)
									RenderTarget.FillRectangle(new RectangleF(pendingEntryAnchorX, t0, pShadeW, b0 - t0), slShade);
								if (yPSl >= canvasTopY - 4f && yPSl <= chartBottomY + 4f)
									DrawHairlineHorizontal(panelX + 2f, clipRight - 2f, yPSl, redBright, 1.2f, orderDash);
							}
						}

						if (yOrd >= canvasTopY - 6f && yOrd <= chartBottomY + 6f)
						{
							float clampedY = Math.Max(canvasTopY + 11f, Math.Min(chartBottomY - 11f, yOrd));
							DxBrush lineBrush = role == 1
								? greenBright
								: (role == 2 ? redBright : (isBuyOrd ? blueEntry : amberEntry));
							DxBrush badgeFill = role == 1
								? greenDarkPill
								: (role == 2 ? redDarkPill : (isBuyOrd ? blueDarkPill : amberDarkPill));

							// Extend ONLY SL (role == 2) and TP (role == 1) lines to the left edge; Entry / Pending Limit starts cleanly from anchor!
							float lineStartX = (role == 1 || role == 2)
								? (panelX + 2f)
								: (hasOpenPos ? entryAnchorX : (pendSpec != null ? pendingEntryAnchorX : (panelX + 2f)));
							if (drawUnderlayPass)
								DrawHairlineHorizontal(lineStartX, clipRight - 2f, yOrd, lineBrush, isDraggingThis ? 1.75f : 1.25f, orderDash);

							if (drawOverlayPillsPass)
							{
								string labelText;
								if (hasOpenPos && (role == 1 || role == 2))
								{
									bool pLong = (pos.MarketPosition == MarketPosition.Long);
									double diffPts = pLong ? (ordPrice - pos.AveragePrice) : (pos.AveragePrice - ordPrice);
									long diffTicks = (long)Math.Round(diffPts / tick);
									double estDollar = wo.Quantity * diffPts * pv;
									string dSign = estDollar >= 0 ? "+" : "-";
									string tSign = diffTicks >= 0 ? "+" : "";
									string prefix = role == 1 ? "TP " : "SL ";
									labelText = string.Format(
										CultureInfo.InvariantCulture,
										"{0}{1} @ {2}  |  {3}{4}{5:0.##} ({6}{7}T)",
										prefix, wo.Quantity, FormatOrderflowPrice(ordPrice),
										dSign, curSym, Math.Abs(estDollar), tSign, diffTicks);
								}
								else
								{
									string actLbl = isBuyOrd ? "BUY " : "SELL ";
									string typLbl = IsStopOrderType(wo.OrderType) ? "STOP " : "LIMIT ";
									string pendRr = (showPendSl && showPendTp)
										? ("  |  " + FormatLiveRiskRewardBadge(ordPrice, pendSlPx, pendTpPx, tick))
										: string.Empty;
									labelText = actLbl + typLbl + wo.Quantity + " @ " + FormatOrderflowPrice(ordPrice) + pendRr;
								}

								float pillH = 20f;
								float cancelW = 20f;
								float gripW = 15f;
								float textW = MeasurePillTextWidth(dw, tfPill, labelText);
								float bodyW = Math.Max(128f, (float)Math.Ceiling(textW) + gripW + 14f);
								float totalW = bodyW + cancelW + 3f;
								float pillX = Math.Max(panelX + 6f, pillRightAnchorX - totalW);
								float pillY = clampedY - pillH * 0.5f;

								var bodyRect = new RectangleF(pillX, pillY, bodyW, pillH);
								var bodyTextRect = new RectangleF(pillX + gripW, pillY, bodyW - gripW - 4f, pillH);
								var cxlRect = new RectangleF(pillX + bodyW + 3f, pillY, cancelW, pillH);

								var bodyRr = new RoundedRectangle { Rect = bodyRect, RadiusX = 4.0f, RadiusY = 4.0f };
								RenderTarget.FillRoundedRectangle(bodyRr, badgeFill);
								RenderTarget.FillRectangle(new RectangleF(bodyRect.X + 2f, bodyRect.Y + 1f, bodyRect.Width - 4f, pillH * 0.42f), pillSheen);
								RenderTarget.DrawRoundedRectangle(bodyRr, lineBrush, isDraggingThis ? 1.65f : 1.15f);
								DrawVectorDragGrip(pillX + 8.0f, pillY + pillH * 0.5f, lineBrush, roundCap);
								DrawSmoothPillText(labelText, tfPill, bodyTextRect, txtWhite);

								var cxlRr = new RoundedRectangle { Rect = cxlRect, RadiusX = 4.0f, RadiusY = 4.0f };
								RenderTarget.FillRoundedRectangle(cxlRr, posBgBrush);
								RenderTarget.DrawRoundedRectangle(cxlRr, lineBrush, 1.1f);
								DrawVectorCloseX(cxlRect.X + cancelW * 0.5f, cxlRect.Y + pillH * 0.5f, 3.0f, txtWhite, roundCap);

								liveChartHitRegions.Add(new LiveChartHitRegion
								{
									Rect = bodyRect,
									LineY = yOrd,
									LineX0 = lineStartX,
									LineX1 = clipRight - 4f,
									AllowLineGrab = true,
									Kind = LiveOrderHitKind.WorkingOrderDrag,
									TargetOrder = wo,
									IsStopOrder = IsStopOrderType(wo.OrderType),
									InitialPrice = ordPrice
								});
								liveChartHitRegions.Add(new LiveChartHitRegion
								{
									Rect = cxlRect,
									Kind = LiveOrderHitKind.CancelWorkingOrderBtn,
									TargetOrder = wo,
									InitialPrice = ordPrice
								});
							}
						}

						// Pre-Trigger Draggable Pending TP & Pending SL Pills (Overlay Pass — Before Buy/Sell Limit Triggers!)
						if (drawOverlayPillsPass && pendSpec != null)
						{
							void DrawPendingBracketPill(bool isTp, double bPx, LiveOrderHitKind dragKind, LiveOrderHitKind cxlKind)
							{
								float yB = priceToY(bPx);
								if (yB < canvasTopY - 6f || yB > chartBottomY + 6f)
									return;
								float clB = Math.Max(canvasTopY + 11f, Math.Min(chartBottomY - 11f, yB));
								double diffPts = pendSpec.IsLong ? (bPx - ordPrice) : (ordPrice - bPx);
								long diffTicks = (long)Math.Round(diffPts / tick);
								double estDollar = pendSpec.TotalQty * diffPts * pv;
								string bText = string.Format(
									CultureInfo.InvariantCulture,
									"{0}{1} @ {2}  |  {3}{4}{5:0.##} ({6}{7}T)",
									isTp ? "TP " : "SL ",
									pendSpec.TotalQty,
									FormatOrderflowPrice(bPx),
									estDollar >= 0 ? "+" : "-", curSym, Math.Abs(estDollar),
									diffTicks >= 0 ? "+" : "", diffTicks);

								DxBrush bBr = isTp ? greenBright : redBright;
								DxBrush bFill = isTp ? greenDarkPill : redDarkPill;
								float pH = 20f, cW = 20f, gW = 15f;
								float tW = MeasurePillTextWidth(dw, tfPill, bText);
								float bW = Math.Max(128f, (float)Math.Ceiling(tW) + gW + 14f);
								float pX = Math.Max(panelX + 6f, pillRightAnchorX - (bW + cW + 3f));
								float pY = clB - pH * 0.5f;

								var bRect = new RectangleF(pX, pY, bW, pH);
								var cRect = new RectangleF(pX + bW + 3f, pY, cW, pH);
								var bRr = new RoundedRectangle { Rect = bRect, RadiusX = 4.0f, RadiusY = 4.0f };
								RenderTarget.FillRoundedRectangle(bRr, bFill);
								RenderTarget.FillRectangle(new RectangleF(bRect.X + 2f, bRect.Y + 1f, bRect.Width - 4f, pH * 0.42f), pillSheen);
								RenderTarget.DrawRoundedRectangle(bRr, bBr, 1.15f);
								DrawVectorDragGrip(pX + 8.0f, pY + pH * 0.5f, bBr, roundCap);
								DrawSmoothPillText(bText, tfPill, new RectangleF(pX + gW, pY, bW - gW - 4f, pH), txtWhite);

								var cRr = new RoundedRectangle { Rect = cRect, RadiusX = 4.0f, RadiusY = 4.0f };
								RenderTarget.FillRoundedRectangle(cRr, posBgBrush);
								RenderTarget.DrawRoundedRectangle(cRr, bBr, 1.1f);
								DrawVectorCloseX(cRect.X + cW * 0.5f, cRect.Y + pH * 0.5f, 3.0f, txtWhite, roundCap);

								liveChartHitRegions.Add(new LiveChartHitRegion
								{
									Rect = bRect,
									LineY = yB,
									LineX0 = panelX + 2f,
									LineX1 = clipRight - 4f,
									AllowLineGrab = true,
									Kind = dragKind,
									TargetPendingSpec = pendSpec,
									InitialPrice = bPx
								});
								liveChartHitRegions.Add(new LiveChartHitRegion
								{
									Rect = cRect,
									Kind = cxlKind,
									TargetPendingSpec = pendSpec,
									InitialPrice = bPx
								});
							}

							if (showPendTp && pendTpPx > 0)
								DrawPendingBracketPill(true, pendTpPx, LiveOrderHitKind.PendingBracketTpDrag, LiveOrderHitKind.CancelPendingBracketTpBtn);
							if (showPendSl && pendSlPx > 0)
								DrawPendingBracketPill(false, pendSlPx, LiveOrderHitKind.PendingBracketSlDrag, LiveOrderHitKind.CancelPendingBracketSlBtn);
						}
					}

					// 3. Render Live Open Position Line (Underlay BEHIND Candles) & Sleek Action Pill (Overlay)
					if (hasOpenPos)
					{
						bool pLong = (pos.MarketPosition == MarketPosition.Long);
						float yPos = priceToY(pos.AveragePrice);
						if (yPos >= canvasTopY - 4f && yPos <= chartBottomY + 4f)
						{
							double upl = 0.0;
							try { upl = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency, lastPx); } catch { }

							DxBrush posBorderBr = upl >= 0 ? greenBright : redBright;
							DxBrush posFillBr = upl >= 0 ? greenDarkPill : redDarkPill;

							// Entry line drawn BEHIND candles from Entry Candle to Right Edge (no occluding dot or vertical line over the candle!)
							if (drawUnderlayPass)
								DrawHairlineHorizontal(entryAnchorX, clipRight - 2f, yPos, posBorderBr, 1.35f);

							if (drawOverlayPillsPass)
							{
								float clPosY = Math.Max(canvasTopY + 11f, Math.Min(chartBottomY - 11f, yPos));
								long pnlTicks = (long)Math.Round(((pLong ? (lastPx - pos.AveragePrice) : (pos.AveragePrice - lastPx)) / tick));
								string uSign = upl >= 0 ? "+" : "-";
								string rrBadge = (hasEffSl && hasEffTp)
									? FormatLiveRiskRewardBadge(pos.AveragePrice, effSlPx, effTpPx, tick)
									: string.Empty;
								string posTxt = string.Format(
									CultureInfo.InvariantCulture,
									"{0} {1} @ {2}  |  {3}{4}{5:0.##} ({6}{7}T){8}",
									pLong ? "LONG" : "SHORT",
									pos.Quantity,
									FormatOrderflowPrice(pos.AveragePrice),
									uSign, curSym, Math.Abs(upl),
									pnlTicks >= 0 ? "+" : "", pnlTicks,
									!string.IsNullOrEmpty(rrBadge) ? ("  |  " + rrBadge) : string.Empty);

								float pillH = 20.5f;
								float py = clPosY - pillH * 0.5f;
								float curRight = pillRightAnchorX;

								RectangleF DrawMiniActionChip(string txt, float minW, DxBrush bg, DxBrush bd, bool isVectorClose = false)
								{
									float w = isVectorClose
										? minW
										: Math.Max(minW, (float)Math.Ceiling(MeasurePillTextWidth(dw, tfSmall, txt)) + 10f);
									curRight -= w;
									var rc = new RectangleF(curRight, py, w, pillH);
									var rr = new RoundedRectangle { Rect = rc, RadiusX = 4.0f, RadiusY = 4.0f };
									RenderTarget.FillRoundedRectangle(rr, bg);
									RenderTarget.DrawRoundedRectangle(rr, bd, 1.1f);
									if (isVectorClose)
										DrawVectorCloseX(rc.X + w * 0.5f, rc.Y + pillH * 0.5f, 3.1f, txtWhite, roundCap);
									else
										DrawSmoothPillText(txt, tfSmall, rc, txtWhite);
									curRight -= 2.5f;
									return rc;
								}

								// Close [✕]
								RectangleF rcClose = DrawMiniActionChip(string.Empty, 20.5f, redCloseBg, redBright, isVectorClose: true);
								liveChartHitRegions.Add(new LiveChartHitRegion { Rect = rcClose, Kind = LiveOrderHitKind.ClosePositionBtn });

								// Reverse [REV]
								RectangleF rcRev = DrawMiniActionChip("REV", 29f, chipBg, chipBorder);
								liveChartHitRegions.Add(new LiveChartHitRegion { Rect = rcRev, Kind = LiveOrderHitKind.ReversePositionBtn });

								// Breakeven [BE]
								RectangleF rcBe = DrawMiniActionChip("BE", 24f, chipBg, chipBorder);
								liveChartHitRegions.Add(new LiveChartHitRegion { Rect = rcBe, Kind = LiveOrderHitKind.BreakevenPositionBtn });

								// Drag-to-Create [+SL] chip (when no working SL exists yet)
								if (!hasWorkingSl)
								{
									RectangleF rcSl = DrawMiniActionChip("+SL", 30f, redDarkPill, redBright);
									liveChartHitRegions.Add(new LiveChartHitRegion
									{
										Rect = rcSl,
										Kind = LiveOrderHitKind.AddSlDragChip,
										InitialPrice = pos.AveragePrice
									});
								}

								// Drag-to-Create [+TP] chip (when no working TP exists yet)
								if (!hasWorkingTp)
								{
									RectangleF rcTp = DrawMiniActionChip("+TP", 30f, greenDarkPill, greenBright);
									liveChartHitRegions.Add(new LiveChartHitRegion
									{
										Rect = rcTp,
										Kind = LiveOrderHitKind.AddTpDragChip,
										InitialPrice = pos.AveragePrice
									});
								}

								// Main Position Info Pill (with Live R:R)
								float mainTextW = MeasurePillTextWidth(dw, tfPill, posTxt);
								float mainW = Math.Max(148f, (float)Math.Ceiling(mainTextW) + 18f);
								float mainX = Math.Max(panelX + 6f, curRight - mainW);
								var mainRect = new RectangleF(mainX, py, mainW, pillH);
								var mainRr = new RoundedRectangle { Rect = mainRect, RadiusX = 4.0f, RadiusY = 4.0f };
								RenderTarget.FillRoundedRectangle(mainRr, posFillBr);
								RenderTarget.FillRectangle(new RectangleF(mainRect.X + 2f, mainRect.Y + 1f, mainRect.Width - 4f, pillH * 0.42f), pillSheen);
								RenderTarget.DrawRoundedRectangle(mainRr, posBorderBr, 1.25f);
								DrawSmoothPillText(posTxt, tfPill, mainRect, txtWhite);
							}
						}

						// Live Ghost Line while dragging a brand-new +TP or +SL out of the Position Pill
						if (activeLiveOrderDragMode == LiveOrderDragMode.DragNewTpFromPosition || activeLiveOrderDragMode == LiveOrderDragMode.DragNewSlFromPosition)
						{
							bool isNewTp = (activeLiveOrderDragMode == LiveOrderDragMode.DragNewTpFromPosition);
							float yGhost = priceToY(liveOrderDragCurrentPrice);
							if (yGhost >= canvasTopY && yGhost <= chartBottomY)
							{
								DxBrush gBr = isNewTp ? greenBright : redBright;
								DxBrush gFill = isNewTp ? greenDarkPill : redDarkPill;
								if (drawUnderlayPass)
									DrawHairlineHorizontal(panelX + 2f, clipRight - 2f, yGhost, gBr, 1.55f, orderDash);
								if (drawOverlayPillsPass)
								{
									double dPts = pLong ? (liveOrderDragCurrentPrice - pos.AveragePrice) : (pos.AveragePrice - liveOrderDragCurrentPrice);
									double dAmt = pos.Quantity * dPts * pv;
									string ghostRr = (hasEffSl && hasEffTp)
										? ("  |  " + FormatLiveRiskRewardBadge(pos.AveragePrice, effSlPx, effTpPx, tick))
										: string.Empty;
									string gTxt = (isNewTp ? "Drop TP @ " : "Drop SL @ ") +
										FormatOrderflowPrice(liveOrderDragCurrentPrice) +
										string.Format(CultureInfo.InvariantCulture, "  ({0}{1}{2:0.##}){3}", dAmt >= 0 ? "+" : "-", curSym, Math.Abs(dAmt), ghostRr);
									float gw = Math.Max(155f, (float)Math.Ceiling(MeasurePillTextWidth(dw, tfPill, gTxt)) + 18f);
									var gRect = new RectangleF(Math.Max(panelX + 8f, pillRightAnchorX - gw), Math.Max(canvasTopY + 4f, Math.Min(chartBottomY - 22f, yGhost - 10f)), gw, 20f);
									var gRr = new RoundedRectangle { Rect = gRect, RadiusX = 4.0f, RadiusY = 4.0f };
									RenderTarget.FillRoundedRectangle(gRr, gFill);
									RenderTarget.DrawRoundedRectangle(gRr, gBr, 1.3f);
									DrawSmoothPillText(gTxt, tfPill, gRect, txtWhite);
								}
							}
						}
					}
					else if (runtimeShowTradePanel && !isTradeTabScalp && workingOrders.Count == 0)
					{
						// 4. Pre-Confirmation Interactive Draft Preview (Lines BEHIND Candles in Underlay Pass; Pills in Overlay Pass)
						double refEntry = GetCurrentOrderReferenceEntryPrice();
						bool hasDraftRr = (orderSlEnabled && orderSlTicks > 0 && orderTpEnabled && orderTpTicks > 0);
						string draftRrSuffix = string.Empty;
						if (hasDraftRr)
						{
							double dRr = (double)orderTpTicks / orderSlTicks;
							string dRrStr = dRr >= 1.0
								? dRr.ToString("0.#", CultureInfo.InvariantCulture)
								: dRr.ToString("0.##", CultureInfo.InvariantCulture);
							draftRrSuffix = "  |  R:R 1:" + dRrStr;
						}

						bool showDraftEntry = (selectedOrderTypeIdx != 0);
						float draftEntryStartX = (ChartBars != null && ChartBars.Count > 0)
							? Math.Max(panelX + 2f, Math.Min(clipRight - 140f, (float)cc.GetXByBarIndex(ChartBars, Math.Max(0, ChartBars.Count - 1))))
							: (panelX + 2f);

						// Shaded 1:3 R:R Draft Preview Zones BEHIND Candles (when configuring Buy/Sell Limit, Stop, or Market with SL/TP!)
						if (drawUnderlayPass && refEntry > 0 && showDraftEntry)
						{
							float yEntD = priceToY(refEntry);
							float dShadeW = Math.Max(16f, (clipRight - 2f) - draftEntryStartX);
							if (orderTpEnabled && orderTpTicks > 0)
							{
								double dTpP = isOrderSideBuy ? RoundToInstrumentTick(refEntry + orderTpTicks * tick) : RoundToInstrumentTick(refEntry - orderTpTicks * tick);
								float yTpD = priceToY(dTpP);
								float t0 = Math.Max(canvasTopY, Math.Min(yEntD, yTpD));
								float b0 = Math.Min(chartBottomY, Math.Max(yEntD, yTpD));
								if (b0 > t0 + 1f)
									RenderTarget.FillRectangle(new RectangleF(draftEntryStartX, t0, dShadeW, b0 - t0), tpShade);
							}
							if (orderSlEnabled && orderSlTicks > 0)
							{
								double dSlP = isOrderSideBuy ? RoundToInstrumentTick(refEntry - orderSlTicks * tick) : RoundToInstrumentTick(refEntry + orderSlTicks * tick);
								float ySlD = priceToY(dSlP);
								float t0 = Math.Max(canvasTopY, Math.Min(yEntD, ySlD));
								float b0 = Math.Min(chartBottomY, Math.Max(yEntD, ySlD));
								if (b0 > t0 + 1f)
									RenderTarget.FillRectangle(new RectangleF(draftEntryStartX, t0, dShadeW, b0 - t0), slShade);
							}
						}
						if (showDraftEntry && refEntry > 0)
						{
							float yDraftEnt = priceToY(refEntry);
							if (yDraftEnt >= canvasTopY && yDraftEnt <= chartBottomY)
							{
								if (drawUnderlayPass)
									DrawHairlineHorizontal(draftEntryStartX, clipRight - 2f, yDraftEnt, blueEntry, 1.3f, draftDash);
								if (drawOverlayPillsPass)
								{
									string dLbl = "Draft " + (isOrderSideBuy ? "Buy " : "Sell ") + (selectedOrderTypeIdx == 1 ? "Limit @ " : "Stop @ ") + FormatOrderflowPrice(refEntry) + draftRrSuffix;
									float dw2 = Math.Max(145f, (float)Math.Ceiling(MeasurePillTextWidth(dw, tfSmall, dLbl)) + 24f);
									var dRect = new RectangleF(Math.Max(panelX + 8f, pillRightAnchorX - dw2), Math.Max(canvasTopY + 2f, Math.Min(chartBottomY - 22f, yDraftEnt - 10f)), dw2, 20f);
									var dRr = new RoundedRectangle { Rect = dRect, RadiusX = 4.0f, RadiusY = 4.0f };
									RenderTarget.FillRoundedRectangle(dRr, blueDarkPill);
									RenderTarget.DrawRoundedRectangle(dRr, blueEntry, 1.15f);
									DrawVectorDragGrip(dRect.X + 8.0f, dRect.Y + 10f, blueEntry, roundCap);
									DrawSmoothPillText(dLbl, tfSmall, new RectangleF(dRect.X + 14f, dRect.Y, dRect.Width - 16f, dRect.Height), txtWhite);

									liveChartHitRegions.Add(new LiveChartHitRegion
									{
										Rect = dRect,
										LineY = yDraftEnt,
										LineX0 = draftEntryStartX,
										LineX1 = clipRight - 4f,
										AllowLineGrab = true,
										Kind = LiveOrderHitKind.DraftEntryDrag,
										InitialPrice = refEntry
									});
								}
							}
						}

						if (orderTpEnabled && orderTpTicks > 0 && refEntry > 0)
						{
							double dTpPx = isOrderSideBuy ? RoundToInstrumentTick(refEntry + orderTpTicks * tick) : RoundToInstrumentTick(refEntry - orderTpTicks * tick);
							float yDTp = priceToY(dTpPx);
							if (yDTp >= canvasTopY && yDTp <= chartBottomY)
							{
								if (drawUnderlayPass)
									DrawHairlineHorizontal(panelX + 2f, clipRight - 2f, yDTp, greenBright, 1.2f, draftDash);
								if (drawOverlayPillsPass)
								{
									double rAmt = orderQtyValue * orderTpTicks * tick * pv;
									string tpLbl = string.Format(
										CultureInfo.InvariantCulture,
										"Draft TP @ {0}  (+{1}{2:0.##} | {3}T){4}",
										FormatOrderflowPrice(dTpPx), curSym, rAmt, orderTpTicks,
										!showDraftEntry ? draftRrSuffix : string.Empty);
									float tw = Math.Max(150f, (float)Math.Ceiling(MeasurePillTextWidth(dw, tfSmall, tpLbl)) + 24f);
									var tRect = new RectangleF(Math.Max(panelX + 8f, pillRightAnchorX - tw), Math.Max(canvasTopY + 2f, Math.Min(chartBottomY - 22f, yDTp - 10f)), tw, 20f);
									var tRr = new RoundedRectangle { Rect = tRect, RadiusX = 4.0f, RadiusY = 4.0f };
									RenderTarget.FillRoundedRectangle(tRr, greenDarkPill);
									RenderTarget.DrawRoundedRectangle(tRr, greenBright, 1.15f);
									DrawVectorDragGrip(tRect.X + 8.0f, tRect.Y + 10f, greenBright, roundCap);
									DrawSmoothPillText(tpLbl, tfSmall, new RectangleF(tRect.X + 14f, tRect.Y, tRect.Width - 16f, tRect.Height), txtWhite);

									liveChartHitRegions.Add(new LiveChartHitRegion
									{
										Rect = tRect,
										LineY = yDTp,
										LineX0 = panelX + 2f,
										LineX1 = clipRight - 4f,
										AllowLineGrab = true,
										Kind = LiveOrderHitKind.DraftTpDrag,
										InitialPrice = dTpPx
									});
								}
							}
						}

						if (orderSlEnabled && orderSlTicks > 0 && refEntry > 0)
						{
							double dSlPx = isOrderSideBuy ? RoundToInstrumentTick(refEntry - orderSlTicks * tick) : RoundToInstrumentTick(refEntry + orderSlTicks * tick);
							float yDSl = priceToY(dSlPx);
							if (yDSl >= canvasTopY && yDSl <= chartBottomY)
							{
								if (drawUnderlayPass)
									DrawHairlineHorizontal(panelX + 2f, clipRight - 2f, yDSl, redBright, 1.2f, draftDash);
								if (drawOverlayPillsPass)
								{
									double sAmt = orderQtyValue * orderSlTicks * tick * pv;
									string slLbl = string.Format(CultureInfo.InvariantCulture, "Draft SL @ {0}  (-{1}{2:0.##} | {3}T)", FormatOrderflowPrice(dSlPx), curSym, sAmt, orderSlTicks);
									float sw = Math.Max(150f, (float)Math.Ceiling(MeasurePillTextWidth(dw, tfSmall, slLbl)) + 24f);
									var sRect = new RectangleF(Math.Max(panelX + 8f, pillRightAnchorX - sw), Math.Max(canvasTopY + 2f, Math.Min(chartBottomY - 22f, yDSl - 10f)), sw, 20f);
									var sRr = new RoundedRectangle { Rect = sRect, RadiusX = 4.0f, RadiusY = 4.0f };
									RenderTarget.FillRoundedRectangle(sRr, redDarkPill);
									RenderTarget.DrawRoundedRectangle(sRr, redBright, 1.15f);
									DrawVectorDragGrip(sRect.X + 8.0f, sRect.Y + 10f, redBright, roundCap);
									DrawSmoothPillText(slLbl, tfSmall, new RectangleF(sRect.X + 14f, sRect.Y, sRect.Width - 16f, sRect.Height), txtWhite);

									liveChartHitRegions.Add(new LiveChartHitRegion
									{
										Rect = sRect,
										LineY = yDSl,
										LineX0 = panelX + 2f,
										LineX1 = clipRight - 4f,
										AllowLineGrab = true,
										Kind = LiveOrderHitKind.DraftSlDrag,
										InitialPrice = dSlPx
									});
								}
							}
						}
					}
				}
			}
			finally
			{
				RenderTarget.AntialiasMode = prevAa;
				RenderTarget.TextAntialiasMode = prevTextAa;
			}
		}
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private AK10XBigSort[] cacheAK10XBigSort;
		public AK10XBigSort AK10XBigSort(AK10XOrderflowDataFeed dataFeed, AK10XOrderflowMode footprintMode, int candlestickZoomThresholdPx, bool useLightTheme, int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showHalfpack, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showBarDelta, bool showStatisticGrid, bool enableMarketProfileZones, double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, bool showSinglePrints)
		{
			return AK10XBigSort(Input, dataFeed, footprintMode, candlestickZoomThresholdPx, useLightTheme, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showHalfpack, showImbalance, imbalancePercentage, imbalanceFilter, showBarDelta, showStatisticGrid, enableMarketProfileZones, tpoSize, tpoIntervalMinutes, valueAreaPercent, showSinglePrints);
		}

		public AK10XBigSort AK10XBigSort(ISeries<double> input, AK10XOrderflowDataFeed dataFeed, AK10XOrderflowMode footprintMode, int candlestickZoomThresholdPx, bool useLightTheme, int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showHalfpack, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showBarDelta, bool showStatisticGrid, bool enableMarketProfileZones, double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, bool showSinglePrints)
		{
			if (cacheAK10XBigSort != null)
				for (int idx = 0; idx < cacheAK10XBigSort.Length; idx++)
					if (cacheAK10XBigSort[idx] != null && cacheAK10XBigSort[idx].DataFeed == dataFeed && cacheAK10XBigSort[idx].FootprintMode == footprintMode && cacheAK10XBigSort[idx].CandlestickZoomThresholdPx == candlestickZoomThresholdPx && cacheAK10XBigSort[idx].UseLightTheme == useLightTheme && cacheAK10XBigSort[idx].TicksPerLevel == ticksPerLevel && cacheAK10XBigSort[idx].AutoScalePriceStep == autoScalePriceStep && cacheAK10XBigSort[idx].BarSpacePx == barSpacePx && cacheAK10XBigSort[idx].ShowBarPOC == showBarPOC && cacheAK10XBigSort[idx].ShowHalfpack == showHalfpack && cacheAK10XBigSort[idx].ShowImbalance == showImbalance && cacheAK10XBigSort[idx].ImbalancePercentage == imbalancePercentage && cacheAK10XBigSort[idx].ImbalanceFilter == imbalanceFilter && cacheAK10XBigSort[idx].ShowBarDelta == showBarDelta && cacheAK10XBigSort[idx].ShowStatisticGrid == showStatisticGrid && cacheAK10XBigSort[idx].EnableMarketProfileZones == enableMarketProfileZones && cacheAK10XBigSort[idx].TpoSize == tpoSize && cacheAK10XBigSort[idx].TpoIntervalMinutes == tpoIntervalMinutes && cacheAK10XBigSort[idx].ValueAreaPercent == valueAreaPercent && cacheAK10XBigSort[idx].ShowSinglePrints == showSinglePrints && cacheAK10XBigSort[idx].EqualsInput(input))
						return cacheAK10XBigSort[idx];
			return CacheIndicator<AK10XBigSort>(new AK10XBigSort(){ DataFeed = dataFeed, FootprintMode = footprintMode, CandlestickZoomThresholdPx = candlestickZoomThresholdPx, UseLightTheme = useLightTheme, TicksPerLevel = ticksPerLevel, AutoScalePriceStep = autoScalePriceStep, BarSpacePx = barSpacePx, ShowBarPOC = showBarPOC, ShowHalfpack = showHalfpack, ShowImbalance = showImbalance, ImbalancePercentage = imbalancePercentage, ImbalanceFilter = imbalanceFilter, ShowBarDelta = showBarDelta, ShowStatisticGrid = showStatisticGrid, EnableMarketProfileZones = enableMarketProfileZones, TpoSize = tpoSize, TpoIntervalMinutes = tpoIntervalMinutes, ValueAreaPercent = valueAreaPercent, ShowSinglePrints = showSinglePrints }, input, ref cacheAK10XBigSort);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.AK10XBigSort AK10XBigSort(Indicators.AK10XOrderflowDataFeed dataFeed, Indicators.AK10XOrderflowMode footprintMode, int candlestickZoomThresholdPx, bool useLightTheme, int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showHalfpack, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showBarDelta, bool showStatisticGrid, bool enableMarketProfileZones, double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, bool showSinglePrints)
		{
			return indicator.AK10XBigSort(Input, dataFeed, footprintMode, candlestickZoomThresholdPx, useLightTheme, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showHalfpack, showImbalance, imbalancePercentage, imbalanceFilter, showBarDelta, showStatisticGrid, enableMarketProfileZones, tpoSize, tpoIntervalMinutes, valueAreaPercent, showSinglePrints);
		}

		public Indicators.AK10XBigSort AK10XBigSort(ISeries<double> input , Indicators.AK10XOrderflowDataFeed dataFeed, Indicators.AK10XOrderflowMode footprintMode, int candlestickZoomThresholdPx, bool useLightTheme, int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showHalfpack, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showBarDelta, bool showStatisticGrid, bool enableMarketProfileZones, double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, bool showSinglePrints)
		{
			return indicator.AK10XBigSort(input, dataFeed, footprintMode, candlestickZoomThresholdPx, useLightTheme, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showHalfpack, showImbalance, imbalancePercentage, imbalanceFilter, showBarDelta, showStatisticGrid, enableMarketProfileZones, tpoSize, tpoIntervalMinutes, valueAreaPercent, showSinglePrints);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.AK10XBigSort AK10XBigSort(Indicators.AK10XOrderflowDataFeed dataFeed, Indicators.AK10XOrderflowMode footprintMode, int candlestickZoomThresholdPx, bool useLightTheme, int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showHalfpack, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showBarDelta, bool showStatisticGrid, bool enableMarketProfileZones, double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, bool showSinglePrints)
		{
			return indicator.AK10XBigSort(Input, dataFeed, footprintMode, candlestickZoomThresholdPx, useLightTheme, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showHalfpack, showImbalance, imbalancePercentage, imbalanceFilter, showBarDelta, showStatisticGrid, enableMarketProfileZones, tpoSize, tpoIntervalMinutes, valueAreaPercent, showSinglePrints);
		}

		public Indicators.AK10XBigSort AK10XBigSort(ISeries<double> input , Indicators.AK10XOrderflowDataFeed dataFeed, Indicators.AK10XOrderflowMode footprintMode, int candlestickZoomThresholdPx, bool useLightTheme, int ticksPerLevel, bool autoScalePriceStep, int barSpacePx, bool showBarPOC, bool showHalfpack, bool showImbalance, double imbalancePercentage, int imbalanceFilter, bool showBarDelta, bool showStatisticGrid, bool enableMarketProfileZones, double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, bool showSinglePrints)
		{
			return indicator.AK10XBigSort(input, dataFeed, footprintMode, candlestickZoomThresholdPx, useLightTheme, ticksPerLevel, autoScalePriceStep, barSpacePx, showBarPOC, showHalfpack, showImbalance, imbalancePercentage, imbalanceFilter, showBarDelta, showStatisticGrid, enableMarketProfileZones, tpoSize, tpoIntervalMinutes, valueAreaPercent, showSinglePrints);
		}
	}
}

#endregion
