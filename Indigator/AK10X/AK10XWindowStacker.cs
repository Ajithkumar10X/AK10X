// AK10X 2-Screen Window Stacker for NinjaTrader 8
// Developed for AK10X Trading Systems.
// Provides a unified multi-timeframe split-screen window stacker:
//   - Screen 1 (Left / Primary): Chart with AK10X Market Profile (1M TPO / Dalton Profile)
//   - Screen 2 (Right / Secondary): 5-Minute Candlestick Chart for Active Day Trading & Order Execution
// Architecture:
//   - True Win32 Child Window Embedding (SetParent + WS_CHILD) - ZERO floating popups!
//   - 100% thread-safe: All cross-thread operations dispatched asynchronously via BeginInvoke
//   - Non-blocking window enumeration via SendMessageTimeout (SMTO_ABORTIFHUNG) - ZERO UI Freezes!
//   - Instant in-place instrument update on Scanner double-click with ApplyDataSeriesChangeToSelected
//   - Cached bounding rects to prevent redundant MoveWindow calls & eliminate desktop heap quota exhaustion
//   - Direct Candlestick & Market Profile rendering with proper CandleStyle initialization
//   - Accessible from Control Center -> New -> AK10X 2-Screen Stacker & Scanner row double-click

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.AddOns
{
	#region Win32 API Interop Helpers
	internal static class StackerWin32
	{
		public const int GWL_STYLE = -16;
		public const int GWL_EXSTYLE = -20;
		public const int GWLP_HWNDPARENT = -8;

		public const int WS_POPUP = unchecked((int)0x80000000);
		public const int WS_CAPTION = 0x00C00000;
		public const int WS_THICKFRAME = 0x00040000;
		public const int WS_MINIMIZEBOX = 0x00020000;
		public const int WS_MAXIMIZEBOX = 0x00010000;
		public const int WS_SYSMENU = 0x00080000;
		public const int WS_CHILD = 0x40000000;
		public const int WS_VISIBLE = 0x10000000;
		public const int WS_CLIPCHILDREN = 0x02000000;
		public const int WS_CLIPSIBLINGS = 0x04000000;

		public const int WS_EX_TRANSPARENT = 0x00000020;
		public const int WS_EX_TOOLWINDOW = 0x00000080;
		public const int WS_EX_LAYERED = 0x00080000;
		public const int WS_EX_COMPOSITED = 0x02000000;
		public const int WS_EX_NOACTIVATE = 0x08000000;
		public const uint LWA_ALPHA = 0x00000002;
		public const int DWMWA_CLOAK = 13;

		public const uint SWP_NOSIZE = 0x0001;
		public const uint SWP_NOMOVE = 0x0002;
		public const uint SWP_NOZORDER = 0x0004;
		public const uint SWP_NOACTIVATE = 0x0010;
		public const uint SWP_FRAMECHANGED = 0x0020;
		public const uint SWP_SHOWWINDOW = 0x0040;

		public const int SW_HIDE = 0;
		public const int SW_SHOWNORMAL = 1;
		public const int SW_SHOWMAXIMIZED = 3;
		public const int SW_SHOWNOACTIVATE = 4;
		public const int SW_SHOW = 5;

		public const uint SWP_NOCOPYBITS = 0x0100;
		public const uint WM_CLOSE = 0x0010;
		public const int WM_ACTIVATE = 0x0006;
		public const int WM_MOUSEACTIVATE = 0x0021;
		public const int WM_WINDOWPOSCHANGING = 0x0046;
		public const int WM_NCACTIVATE = 0x0086;
		public const int WM_LBUTTONDOWN = 0x0201;
		public const int WM_RBUTTONDOWN = 0x0204;
		public const int WM_MBUTTONDOWN = 0x0207;
		public const int WM_MOUSEWHEEL = 0x020A;
		public const int WM_PARENTNOTIFY = 0x0210;
		public const uint GW_OWNER = 4;
		public const uint RDW_INVALIDATE = 0x0001;
		public const uint RDW_ALLCHILDREN = 0x0080;
		public const uint RDW_UPDATENOW = 0x0100;
		public const uint RDW_FRAME = 0x0400;

		public const uint SMTO_ABORTIFHUNG = 0x0002;

		[DllImport("user32.dll", SetLastError = true)]
		public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern IntPtr GetParent(IntPtr hWnd);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
		private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

		[DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
		private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

		public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
		{
			if (IntPtr.Size == 8)
				return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
			else
				return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
		}

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

		public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

		[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
		public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, System.Text.StringBuilder lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

		public static string GetWindowTextSafe(IntPtr hWnd, int maxLen = 256)
		{
			try
			{
				System.Text.StringBuilder sb = new System.Text.StringBuilder(maxLen);
				UIntPtr result;
				IntPtr res = SendMessageTimeout(hWnd, 0x000D /* WM_GETTEXT */, (UIntPtr)maxLen, sb, SMTO_ABORTIFHUNG, 50, out result);
				if (res != IntPtr.Zero)
					return sb.ToString();
			}
			catch { }
			return string.Empty;
		}

		[DllImport("user32.dll", SetLastError = true)]
		public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsWindowVisible(IntPtr hWnd);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool IsWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetForegroundWindow(IntPtr hWnd);

		[DllImport("user32.dll")]
		public static extern IntPtr GetForegroundWindow();

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool BringWindowToTop(IntPtr hWnd);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

		[StructLayout(LayoutKind.Sequential)]
		public struct RECT
		{
			public int Left;
			public int Top;
			public int Right;
			public int Bottom;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct POINT
		{
			public int X;
			public int Y;
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct WINDOWPOS
		{
			public IntPtr hwnd;
			public IntPtr hwndInsertAfter;
			public int x;
			public int y;
			public int cx;
			public int cy;
			public uint flags;
		}

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

		[DllImport("user32.dll", SetLastError = true)]
		public static extern IntPtr SetFocus(IntPtr hWnd);

		[DllImport("user32.dll")]
		public static extern IntPtr GetFocus();

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool GetCursorPos(out POINT lpPoint);

		[DllImport("dwmapi.dll", PreserveSig = true)]
		public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

		public static void SetCloaked(IntPtr hwnd, bool cloaked)
		{
			if (hwnd == IntPtr.Zero) return;
			try
			{
				int val = cloaked ? 1 : 0;
				DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ref val, sizeof(int));
			}
			catch { }
		}
	}
	#endregion

	#region AK10XChartStackerWindow
	/// <summary>
	/// Unified 2-Screen Window Stacker hosting Screen 1 (Market Profile) and Screen 2 (5-Min Candles).
	/// True Win32 Child Window docking guarantees zero floating windows, zero shaking, and instant responsiveness.
	/// </summary>
	public class AK10XChartStackerWindow : NTWindow
	{
		public static AK10XChartStackerWindow Instance { get; private set; }

		private static System.Windows.Threading.Dispatcher stackerDispatcher;
		public static System.Windows.Threading.Dispatcher StackerDispatcher
		{
			get
			{
				if (Instance != null && Instance.Dispatcher != null && !Instance.Dispatcher.HasShutdownStarted && !Instance.Dispatcher.HasShutdownFinished)
					return Instance.Dispatcher;
				if (stackerDispatcher == null || stackerDispatcher.HasShutdownStarted || stackerDispatcher.HasShutdownFinished)
					stackerDispatcher = NinjaTrader.Core.Globals.RandomDispatcher;
				return stackerDispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;
			}
		}

		public static void EnsureD2DFactoryForCurrentThread()
		{
			try
			{
				var disp = System.Windows.Threading.Dispatcher.CurrentDispatcher;
				var fField = typeof(NinjaTrader.Core.Globals).GetField("factories", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
				if (fField != null)
				{
					var dict = fField.GetValue(null) as System.Collections.IDictionary;
					if (dict != null)
					{
						if (disp != null && !dict.Contains(disp))
							dict[disp] = new SharpDX.Direct2D1.Factory(SharpDX.Direct2D1.FactoryType.MultiThreaded);
						if (NinjaTrader.Core.Globals.MainThreadDispatcher != null && !dict.Contains(NinjaTrader.Core.Globals.MainThreadDispatcher))
							dict[NinjaTrader.Core.Globals.MainThreadDispatcher] = new SharpDX.Direct2D1.Factory(SharpDX.Direct2D1.FactoryType.MultiThreaded);
					}
				}
			}
			catch { }
		}

		private Grid rootGrid;
		private Grid mainSplitGrid;
		private GridSplitter gridSplitter;
		private ColumnDefinition colPane1;
		private ColumnDefinition colSplitter;
		private ColumnDefinition colPane2;
		private RowDefinition rowPane1;
		private RowDefinition rowSplitter;
		private RowDefinition rowPane2;

		private Border borderPane1;
		private Border borderPane2;
		private StackPanel placeholderPane1;
		private StackPanel placeholderPane2;

		private TextBlock txtPane1Title;
		private TextBlock txtPane2Title;
		public ComboBox cmbSymbol;
		private ComboBox cmbSplit;
		private ComboBox cmbPane1Charts;
		private ComboBox cmbPane2Charts;
		private double currentLeftSplitRatio = 0.5;
		private double currentRightSplitRatio = 0.5;
		private HwndSource hookedChildSource1;
		private HwndSource hookedChildSource2;

		private IntPtr stackerHwnd = IntPtr.Zero;

		private IntPtr dockedChart1Hwnd = IntPtr.Zero;
		private string dockedChart1Title = null;
		public NinjaTrader.Gui.Chart.Chart dockedChart1Instance = null;

		private IntPtr dockedChart2Hwnd = IntPtr.Zero;
		private string dockedChart2Title = null;
		public NinjaTrader.Gui.Chart.Chart dockedChart2Instance = null;

		private bool isStackedLayout = false;
		private bool isPanesSwapped = false;
		private Button btnSwapPanes;
		private bool isPane1Maximized = false;
		private bool isPane2Maximized = false;
		private Button btnMaxPane1;
		private Button btnMaxPane2;
		private bool isSuppressSymbolSelectionChanged = false;
		private bool isUpdatingInstrumentProgrammatically = false;
		private bool isWindowClosed = false;
		private string currentLoadedSymbol = "";
		private DateTime lastSymbolLoadTimestamp = DateTime.MinValue;
		private Button btnAccelpixStatus;
		private Border borderTransitionPill;
		private TextBlock txtTransitionStatus;
		private TextBlock txtPlaceholder1Title;
		private TextBlock txtPlaceholder1Desc;
		private Button btnPlaceholder1Open;
		private ProgressBar progressPlaceholder1;
		private TextBlock txtPlaceholder2Title;
		private TextBlock txtPlaceholder2Desc;
		private Button btnPlaceholder2Open;
		private ProgressBar progressPlaceholder2;
		private bool isCreatingPane1 = false;
		private bool isCreatingPane2 = false;
		private NinjaTrader.Gui.Chart.Chart stagedChart1 = null;
		private NinjaTrader.Gui.Chart.Chart stagedChart2 = null;
		private byte d2dCurrentAlpha1 = 255;
		private byte d2dCurrentAlpha2 = 255;
		private System.Windows.Threading.DispatcherTimer d2dFadeTimer;
		private string pendingSymbolPane1 = null;
		private string pendingSymbolPane2 = null;
		private IntPtr lastSyncedD2dHwnd1 = IntPtr.Zero;
		private IntPtr lastSyncedD2dHwnd2 = IntPtr.Zero;
		private System.Windows.Threading.DispatcherTimer readinessTimer;
		private int readinessTicks = 0;
		private int postReadyHealthTicks = 0;
		private string activeReadinessSymbol = "";
		private DateTime lastStackerInteractionUtc = DateTime.MinValue;
		private DateTime lastIntentionalScannerActivateUtc = DateTime.MinValue;
		private static System.Reflection.MethodInfo cachedMoveResizeMethod;
		private static System.Reflection.FieldInfo cachedD2dIsVisibleField;
		private static System.Reflection.MethodInfo cachedD2dHideMethod;

		public static void EnsureAccelpixConnected()
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

		private static bool IsAccelpixCurrentlyConnected()
		{
			try
			{
				lock (Connection.Connections)
				{
					foreach (Connection c in Connection.Connections)
					{
						if (c != null && c.Options != null &&
							(c.Options.Name.IndexOf("Accel", StringComparison.OrdinalIgnoreCase) >= 0 ||
							 c.Options.TypeName.IndexOf("SampleOpenConnect", StringComparison.OrdinalIgnoreCase) >= 0) &&
							c.Status == ConnectionStatus.Connected)
						{
							return true;
						}
					}
				}
			}
			catch { }
			return false;
		}

		public void ReloadBothDockedChartsHistoricalData(bool onlyIfEmpty = false)
		{
			try
			{
				Dispatcher.BeginInvoke(new Action(() =>
				{
					try
					{
						if (dockedChart1Instance != null && dockedChart1Instance.ActiveChartControl != null)
						{
							ChartControl cc1 = dockedChart1Instance.ActiveChartControl;
							cc1.Dispatcher.BeginInvoke(new Action(() =>
							{
								try
								{
									if (!onlyIfEmpty || cc1.PrimaryBars == null || cc1.PrimaryBars.Bars == null || cc1.PrimaryBars.Bars.Count == 0)
										cc1.OnReloadHistoricalDataHotKey(cc1, null);
								}
								catch { }
							}));
						}
						if (dockedChart2Instance != null && dockedChart2Instance.ActiveChartControl != null)
						{
							ChartControl cc2 = dockedChart2Instance.ActiveChartControl;
							cc2.Dispatcher.BeginInvoke(new Action(() =>
							{
								try
								{
									if (!onlyIfEmpty || cc2.PrimaryBars == null || cc2.PrimaryBars.Bars == null || cc2.PrimaryBars.Bars.Count == 0)
										cc2.OnReloadHistoricalDataHotKey(cc2, null);
								}
								catch { }
							}));
						}
					}
					catch { }
				}));
			}
			catch { }
		}

		private void OnStackerConnectionStatusUpdate(object sender, ConnectionStatusEventArgs e)
		{
			try
			{
				if (e == null || isWindowClosed)
					return;
				Dispatcher.BeginInvoke(new Action(() =>
				{
					try
					{
						UpdateAccelpixStatusButton();
						if (e.Status == ConnectionStatus.Connected)
						{
							var t1 = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
							t1.Tick += (s, args) =>
							{
								t1.Stop();
								ReloadBothDockedChartsHistoricalData(true);
							};
							t1.Start();
						}
					}
					catch { }
				}));
			}
			catch { }
		}

		private void UpdateAccelpixStatusButton()
		{
			if (btnAccelpixStatus == null)
				return;
			bool isConn = IsAccelpixCurrentlyConnected();
			btnAccelpixStatus.Content = isConn ? "● LIVE ACCELPIX" : "⚡ CONNECT ACCELPIX";
			btnAccelpixStatus.Foreground = new SolidColorBrush(isConn ? Color.FromRgb(0, 230, 118) : Color.FromRgb(255, 179, 0));
			btnAccelpixStatus.BorderBrush = new SolidColorBrush(isConn ? Color.FromRgb(0, 200, 100) : Color.FromRgb(255, 179, 0));
			btnAccelpixStatus.Background = new SolidColorBrush(isConn ? Color.FromRgb(8, 42, 22) : Color.FromRgb(42, 32, 8));
		}

		public static AK10XChartStackerWindow FindOrCreateActiveInstance()
		{
			if (Instance != null && !Instance.isWindowClosed)
				return Instance;

			try
			{
				var allWins = NinjaTrader.Core.Globals.AllWindows;
				if (allWins != null)
				{
					for (int i = 0; i < allWins.Count; i++)
					{
						Window w = null;
						try { w = allWins[i]; } catch { }
						if (w is AK10XChartStackerWindow existing && !existing.isWindowClosed)
						{
							Instance = existing;
							stackerDispatcher = existing.Dispatcher;
							return existing;
						}
					}
				}
			}
			catch { }

			Instance = new AK10XChartStackerWindow();
			Instance.Show();
			return Instance;
		}

		// Cached bounds to eliminate redundant MoveWindow calls & prevent UI shaking / quota exhaustion
		private int lastX1 = -1, lastY1 = -1, lastW1 = -1, lastH1 = -1;
		private int lastX2 = -1, lastY2 = -1, lastW2 = -1, lastH2 = -1;

		private void InvalidatePaneBoundsCache()
		{
			lastX1 = -99999; lastY1 = -99999; lastW1 = -99999; lastH1 = -99999;
			lastX2 = -99999; lastY2 = -99999; lastW2 = -99999; lastH2 = -99999;
		}

		private void EnsureParentClipChildren()
		{
			try
			{
				if (stackerHwnd == IntPtr.Zero)
					stackerHwnd = new WindowInteropHelper(this).Handle;
				if (stackerHwnd != IntPtr.Zero)
				{
					int style = StackerWin32.GetWindowLong(stackerHwnd, StackerWin32.GWL_STYLE);
					int needed = StackerWin32.WS_CLIPCHILDREN | StackerWin32.WS_CLIPSIBLINGS;
					if ((style & needed) != needed)
					{
						StackerWin32.SetWindowLong(stackerHwnd, StackerWin32.GWL_STYLE, style | needed);
					}
				}
			}
			catch { }
		}

		private void SyncD2DForm(int paneIndex, bool visible, bool isResizing = false, bool isMoving = false)
		{
			try
			{
				var chart = paneIndex == 1 ? dockedChart1Instance : dockedChart2Instance;
				if (chart == null || chart.Form == null || !chart.Form.IsHandleCreated)
					return;

				IntPtr d2dHwnd = chart.Form.Handle;
				if (d2dHwnd == IntPtr.Zero || !StackerWin32.IsWindow(d2dHwnd))
					return;

				Type formType = chart.Form.GetType();
				if (cachedMoveResizeMethod == null)
					cachedMoveResizeMethod = formType.GetMethod("MoveResizeForm", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
				if (cachedD2dIsVisibleField == null)
					cachedD2dIsVisibleField = formType.GetField("isVisible", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				if (cachedD2dHideMethod == null)
					cachedD2dHideMethod = formType.GetMethod("Hide", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);

				if (!visible)
				{
					try
					{
						if (cachedD2dHideMethod != null)
							cachedD2dHideMethod.Invoke(chart.Form, null);
					}
					catch { }
					if (StackerWin32.IsWindowVisible(d2dHwnd))
						StackerWin32.ShowWindow(d2dHwnd, StackerWin32.SW_HIDE);
					return;
				}

				if (stackerHwnd == IntPtr.Zero)
					stackerHwnd = new WindowInteropHelper(this).Handle;

				// Detect newly created Direct2DForm HWND (which spawns asynchronously after ChartControl finishes initializing)
				IntPtr prevSynced = paneIndex == 1 ? lastSyncedD2dHwnd1 : lastSyncedD2dHwnd2;
				if (d2dHwnd != prevSynced)
				{
					if (paneIndex == 1) lastSyncedD2dHwnd1 = d2dHwnd;
					else lastSyncedD2dHwnd2 = d2dHwnd;
					isResizing = true;
					isMoving = true;
				}

				// CRITICAL Z-ORDER & INPUT PASS-THROUGH FIX:
				// 1. Make stackerHwnd the Win32 Owner (GWLP_HWNDPARENT) so Direct2DForm renders above the WS_CHILD chart window.
				// 2. Make Direct2DForm 100% click-through (WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE with alpha=255)
				//    so EVERY mouse click, drag, scroll-wheel, crosshair, right-click menu, and Direct2D button hit goes straight
				//    through Direct2DForm into the underlying Chart / ChartControl!
				if (stackerHwnd != IntPtr.Zero)
				{
					IntPtr curOwner = StackerWin32.GetWindow(d2dHwnd, StackerWin32.GW_OWNER);
					if (curOwner != stackerHwnd)
					{
						StackerWin32.SetWindowLongPtr(d2dHwnd, StackerWin32.GWLP_HWNDPARENT, stackerHwnd);
						isResizing = true;
						isMoving = true;
					}
				}

				byte targetAlpha = paneIndex == 1 ? d2dCurrentAlpha1 : d2dCurrentAlpha2;
				int curEx = StackerWin32.GetWindowLong(d2dHwnd, StackerWin32.GWL_EXSTYLE);
				int requiredEx = StackerWin32.WS_EX_LAYERED | StackerWin32.WS_EX_TRANSPARENT | StackerWin32.WS_EX_NOACTIVATE;
				if ((curEx & requiredEx) != requiredEx || (curEx & StackerWin32.WS_EX_COMPOSITED) != 0)
				{
					int newEx = (curEx & ~StackerWin32.WS_EX_COMPOSITED) | requiredEx;
					StackerWin32.SetWindowLong(d2dHwnd, StackerWin32.GWL_EXSTYLE, newEx);
					StackerWin32.SetLayeredWindowAttributes(d2dHwnd, 0, targetAlpha, StackerWin32.LWA_ALPHA);
					isResizing = true;
					isMoving = true;
				}
				else if (d2dHwnd != prevSynced || targetAlpha < 255)
				{
					StackerWin32.SetLayeredWindowAttributes(d2dHwnd, 0, targetAlpha, StackerWin32.LWA_ALPHA);
				}

				// Ensure Direct2DForm's internal isVisible flag is true so RenderToTarget never aborts with a black screen
				if (cachedD2dIsVisibleField != null)
				{
					try
					{
						object curVis = cachedD2dIsVisibleField.GetValue(chart.Form);
						if (curVis is bool bVis && !bVis)
						{
							cachedD2dIsVisibleField.SetValue(chart.Form, true);
							isResizing = true;
							isMoving = true;
						}
					}
					catch { }
				}

				if (!StackerWin32.IsWindowVisible(d2dHwnd))
				{
					StackerWin32.ShowWindow(d2dHwnd, StackerWin32.SW_SHOWNOACTIVATE);
					isResizing = true;
					isMoving = true;
				}

				// Verify actual screen coordinates of Direct2DForm against ActiveChartControl so it can NEVER stay stranded off-pane!
				ChartControl cc = chart.ActiveChartControl;
				if (cc != null && cc.IsLoaded && cc.ActualWidth > 20 && cc.ActualHeight > 20)
				{
					PresentationSource ccSrc = PresentationSource.FromVisual(cc);
					if (ccSrc != null && ccSrc.CompositionTarget != null)
					{
						Point ccScreenPt = cc.PointToScreen(new Point(0, 0));
						double dpiX = ccSrc.CompositionTarget.TransformToDevice.M11;
						double dpiY = ccSrc.CompositionTarget.TransformToDevice.M22;
						int expectedW = (int)Math.Round(cc.ActualWidth * dpiX);
						int expectedH = (int)Math.Round(cc.ActualHeight * dpiY);

						StackerWin32.RECT d2dRect;
						if (StackerWin32.GetWindowRect(d2dHwnd, out d2dRect))
						{
							int actualW = d2dRect.Right - d2dRect.Left;
							int actualH = d2dRect.Bottom - d2dRect.Top;
							if (Math.Abs(d2dRect.Left - ccScreenPt.X) > 12 ||
								Math.Abs(d2dRect.Top - ccScreenPt.Y) > 40 ||
								Math.Abs(actualW - expectedW) > 28 ||
								Math.Abs(actualH - expectedH) > 56 ||
								actualW < 50 || actualH < 50)
							{
								isResizing = true;
								isMoving = true;
							}
						}
					}
				}

				// Only lift Direct2DForm to HWND_TOP when Stacker itself is active and no header dropdown popup is open!
				bool anyDropDownOpen = (cmbSymbol != null && cmbSymbol.IsDropDownOpen) || (cmbSplit != null && cmbSplit.IsDropDownOpen);
				uint zFlag = (IsActive && !anyDropDownOpen) ? 0u : StackerWin32.SWP_NOZORDER;
				StackerWin32.SetWindowPos(d2dHwnd, IntPtr.Zero, 0, 0, 0, 0,
					StackerWin32.SWP_NOMOVE | StackerWin32.SWP_NOSIZE | StackerWin32.SWP_NOACTIVATE | StackerWin32.SWP_SHOWWINDOW | zFlag);

				if (isResizing || isMoving)
				{
					if (cachedMoveResizeMethod != null)
						cachedMoveResizeMethod.Invoke(chart.Form, new object[] { true, true });

					if (cc != null)
						cc.InvalidateVisual();
					try { chart.Form.Invalidate(); } catch { }
				}
			}
			catch { }
		}

		private void SetTransitionStatus(string message, bool isReady)
		{
			if (borderTransitionPill == null || txtTransitionStatus == null)
				return;
			txtTransitionStatus.Text = message;
			if (isReady)
			{
				borderTransitionPill.Background = new SolidColorBrush(Color.FromRgb(8, 42, 26));
				borderTransitionPill.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 230, 118));
				txtTransitionStatus.Foreground = new SolidColorBrush(Color.FromRgb(0, 230, 118));
			}
			else
			{
				borderTransitionPill.Background = new SolidColorBrush(Color.FromRgb(10, 34, 52));
				borderTransitionPill.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
				txtTransitionStatus.Foreground = new SolidColorBrush(Color.FromRgb(125, 211, 252));
			}
		}

		private void UpdatePanePlaceholderState(int paneIndex, bool isLoading, string symbolName = null)
		{
			string sym = !string.IsNullOrWhiteSpace(symbolName) ? symbolName : GetSelectedSymbol();
			if (paneIndex == 1)
			{
				if (txtPlaceholder1Title != null)
					txtPlaceholder1Title.Text = isLoading ? ("⚡ Loading " + sym + " — Market Profile (1M)") : "Screen 1: Market Profile (1M TPO)";
				if (txtPlaceholder1Desc != null)
					txtPlaceholder1Desc.Text = isLoading
						? "Initializing Accelpix 1-Min stream, 70% Value Area, Initial Balance & Direct2D TPO Engine..."
						: "Dalton Market Profile (1-Min timeframe, 70% Value Area, Initial Balance, POC)";
				if (progressPlaceholder1 != null)
					progressPlaceholder1.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
				if (btnPlaceholder1Open != null)
					btnPlaceholder1Open.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
			}
			else
			{
				if (txtPlaceholder2Title != null)
					txtPlaceholder2Title.Text = isLoading ? ("⚡ Loading " + sym + " — Orderflow Footprint (5M)") : "Screen 2: Orderflow Footprint (5M)";
				if (txtPlaceholder2Desc != null)
					txtPlaceholder2Desc.Text = isLoading
						? "Initializing Accelpix 5-Min Bid x Ask Clusters, POC, Imbalances & Statistic Grid..."
						: "MZ-Style Orderflow Footprint (5-Min Clusters, POC, 70% VA, 400% Imbalances & Delta Grid)";
				if (progressPlaceholder2 != null)
					progressPlaceholder2.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
				if (btnPlaceholder2Open != null)
					btnPlaceholder2Open.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
			}
		}

		private void StartReadinessSync(string targetSymbol)
		{
			if (isWindowClosed)
				return;

			activeReadinessSymbol = !string.IsNullOrWhiteSpace(targetSymbol) ? targetSymbol : GetSelectedSymbol();
			readinessTicks = 0;
			postReadyHealthTicks = 0;
			SetTransitionStatus("⟳ Loading " + activeReadinessSymbol + "...", false);

			if (readinessTimer == null)
			{
				readinessTimer = new System.Windows.Threading.DispatcherTimer
				{
					Interval = TimeSpan.FromMilliseconds(32)
				};
				readinessTimer.Tick += OnReadinessTimerTick;
			}
			else
			{
				readinessTimer.Interval = TimeSpan.FromMilliseconds(32);
			}

			if (!readinessTimer.IsEnabled)
				readinessTimer.Start();
		}

		private static bool IsPaneChartFullyReady(NinjaTrader.Gui.Chart.Chart chart, bool isProfile)
		{
			if (chart == null || chart.Form == null || !chart.Form.IsHandleCreated)
				return false;
			ChartControl cc = chart.ActiveChartControl;
			if (cc == null || !cc.IsLoaded || cc.PrimaryBars == null || cc.ChartPanels == null || cc.ChartPanels.Count == 0)
				return false;
			EnsurePaneIndicatorAttached(cc, isProfile);
			return StackerWin32.IsWindowVisible(chart.Form.Handle);
		}

		private void OnReadinessTimerTick(object sender, EventArgs e)
		{
			if (isWindowClosed)
			{
				if (readinessTimer != null) readinessTimer.Stop();
				return;
			}

			try
			{
				readinessTicks++;
				EnsureParentClipChildren();
				SyncBothPanes();

				// Apply any symbol switch that was queued while a chart was still initializing
				if (!string.IsNullOrWhiteSpace(pendingSymbolPane1) && dockedChart1Instance != null && dockedChart1Instance.ActiveChartControl != null)
				{
					string sym1 = pendingSymbolPane1;
					Instrument inst1 = ResolveBestInstrument(sym1);
					if (inst1 != null && TryUpdateChartInstrumentInPlace(dockedChart1Instance, inst1, 1))
						pendingSymbolPane1 = null;
				}
				if (!string.IsNullOrWhiteSpace(pendingSymbolPane2) && dockedChart2Instance != null && dockedChart2Instance.ActiveChartControl != null)
				{
					string sym2 = pendingSymbolPane2;
					Instrument inst2 = ResolveBestInstrument(sym2);
					if (inst2 != null && TryUpdateChartInstrumentInPlace(dockedChart2Instance, inst2, 5))
						pendingSymbolPane2 = null;
				}

				bool p1Ready = isPane2Maximized || IsPaneChartFullyReady(dockedChart1Instance, true);
				bool p2Ready = isPane1Maximized || IsPaneChartFullyReady(dockedChart2Instance, false);

				if (p1Ready && p2Ready)
				{
					if (postReadyHealthTicks == 0)
					{
						RefreshDockedChartVisuals(0, true, true);
						SetTransitionStatus("● " + activeReadinessSymbol + " LIVE", true);
						readinessTimer.Interval = TimeSpan.FromMilliseconds(750);
					}
					postReadyHealthTicks++;
					if (postReadyHealthTicks >= 6)
						readinessTimer.Stop();
					return;
				}

				if (p1Ready || p2Ready)
				{
					SetTransitionStatus("⟳ " + activeReadinessSymbol + " (1/2 Ready)...", false);
				}

				// Safety timeout after ~16 seconds (at 32ms tick)
				if (readinessTicks >= 500)
				{
					RefreshDockedChartVisuals(0, true, true);
					SetTransitionStatus("● " + activeReadinessSymbol + " LIVE", true);
					readinessTimer.Stop();
				}
			}
			catch { }
		}

		private void RefreshDockedChartVisuals(int paneIndex = 0, bool isResizing = false, bool isMoving = false)
		{
			try
			{
				if (paneIndex == 0 || paneIndex == 1)
					SyncD2DForm(1, !isPane2Maximized && WindowState != WindowState.Minimized, isResizing, isMoving);
				if (paneIndex == 0 || paneIndex == 2)
					SyncD2DForm(2, !isPane1Maximized && WindowState != WindowState.Minimized, isResizing, isMoving);
			}
			catch { }
		}

		public void MaximizeStackerToWorkArea()
		{
			try
			{
				if (WindowState != WindowState.Normal)
					WindowState = WindowState.Normal;

				Rect wa = SystemParameters.WorkArea;
				if (Math.Abs(Left - wa.Left) > 1) Left = wa.Left;
				if (Math.Abs(Top - wa.Top) > 1) Top = wa.Top;
				if (Math.Abs(Width - wa.Width) > 1) Width = wa.Width;
				if (Math.Abs(Height - wa.Height) > 1) Height = wa.Height;
			}
			catch { }
		}

		private void ForceLayoutAndSyncPanes()
		{
			try
			{
				InvalidatePaneBoundsCache();
				UpdateLayout();
				if (mainSplitGrid != null) mainSplitGrid.UpdateLayout();
				SyncBothPanes();

				Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
				{
					try
					{
						UpdateLayout();
						if (mainSplitGrid != null) mainSplitGrid.UpdateLayout();
						SyncBothPanes();
					}
					catch { }
				}));
			}
			catch { }
		}

		public AK10XChartStackerWindow()
		{
			EnsureD2DFactoryForCurrentThread();
			Instance = this;
			stackerDispatcher = this.Dispatcher;

			Caption = "AK10X Trading Screen | Screen 1: Market Profile (1M) | Screen 2: Orderflow (5M)";
			Title = "AK10X Trading Screen | Screen 1: Market Profile (1M) | Screen 2: Orderflow (5M)";
			this.Resources[SystemParameters.DropShadowKey] = false;
			this.Resources[SystemParameters.ComboBoxPopupAnimationKey] = System.Windows.Controls.Primitives.PopupAnimation.None;
			this.FocusVisualStyle = null;
			Rect wa = SystemParameters.WorkArea;
			WindowStartupLocation = WindowStartupLocation.Manual;
			Left = wa.Left;
			Top = wa.Top;
			Width = wa.Width;
			Height = wa.Height;
			MinWidth = 800;
			MinHeight = 500;
			Background = new SolidColorBrush(Color.FromRgb(14, 17, 23)); // #0E1117

			BuildUI();
			EnsureAccelpixConnected();
			Connection.ConnectionStatusUpdate += OnStackerConnectionStatusUpdate;

			PreviewMouseDown += (s, e) => { lastStackerInteractionUtc = DateTime.UtcNow; };

			Deactivated += (s, e) =>
			{
				try
				{
					if (isWindowClosed || !IsVisible || WindowState == WindowState.Minimized)
						return;
					if ((DateTime.UtcNow - lastIntentionalScannerActivateUtc).TotalMilliseconds < 1200)
						return;
					if ((DateTime.UtcNow - lastStackerInteractionUtc).TotalMilliseconds < 550)
					{
						Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send, new Action(() =>
						{
							try
							{
								if (isWindowClosed || !IsVisible || WindowState == WindowState.Minimized)
									return;
								IntPtr fg = StackerWin32.GetForegroundWindow();
								if (fg != IntPtr.Zero && fg != stackerHwnd)
								{
									var allWins = NinjaTrader.Core.Globals.AllWindows;
									if (allWins != null)
									{
										for (int i = 0; i < allWins.Count; i++)
										{
											Window w = null;
											try { w = allWins[i]; } catch { }
											if (w is ControlCenter)
											{
												IntPtr ccHwnd = IntPtr.Zero;
												try { ccHwnd = new WindowInteropHelper(w).Handle; } catch { }
												if (ccHwnd != IntPtr.Zero && fg == ccHwnd)
												{
													FlushPopupArtifactsAndKeepFront(true);
													break;
												}
											}
										}
									}
								}
							}
							catch { }
						}));
					}
				}
				catch { }
			};

			SourceInitialized += (s, e) =>
			{
				try
				{
					Instance = this;
					stackerDispatcher = this.Dispatcher;
					stackerHwnd = new WindowInteropHelper(this).Handle;
					HwndSource src = HwndSource.FromHwnd(stackerHwnd);
					if (src != null)
						src.AddHook(OnStackerWndProc);
				}
				catch { }
			};

			Activated += (s, e) =>
			{
				try
				{
					Instance = this;
					stackerDispatcher = this.Dispatcher;
					RefreshDockedChartVisuals(0, false, false);
				}
				catch { }
			};

			Loaded += (s, e) =>
			{
				try
				{
					stackerHwnd = new WindowInteropHelper(this).Handle;
					MaximizeStackerToWorkArea();
					RefreshAvailableCharts();
					ForceLayoutAndSyncPanes();
				}
				catch { }
			};

			LocationChanged += (s, e) =>
			{
				try
				{
					RefreshDockedChartVisuals(0, false, true);
				}
				catch { }
			};

			SizeChanged += (s, e) =>
			{
				try
				{
					ForceLayoutAndSyncPanes();
				}
				catch { }
			};

			StateChanged += (s, e) =>
			{
				try
				{
					if (WindowState == WindowState.Minimized)
					{
						if (dockedChart1Hwnd != IntPtr.Zero) StackerWin32.ShowWindow(dockedChart1Hwnd, StackerWin32.SW_HIDE);
						if (dockedChart2Hwnd != IntPtr.Zero) StackerWin32.ShowWindow(dockedChart2Hwnd, StackerWin32.SW_HIDE);
						SyncD2DForm(1, false);
						SyncD2DForm(2, false);
					}
					else if (WindowState == WindowState.Maximized)
					{
						// Keep NTWindow in Normal state at 100% WorkArea bounds so child Direct2DForm coordinates never break!
						MaximizeStackerToWorkArea();
						ForceLayoutAndSyncPanes();
					}
					else
					{
						ForceLayoutAndSyncPanes();
					}
				}
				catch { }
			};

			PreviewMouseWheel += (s, e) =>
			{
				try
				{
					if ((cmbSymbol != null && cmbSymbol.IsDropDownOpen) || (cmbSplit != null && cmbSplit.IsDropDownOpen))
						return;

					// MacBook-style hover-to-scroll: route wheel focus + immediate scroll delta to whichever pane the cursor is hovering over!
					StackerWin32.POINT curPt;
					if (StackerWin32.GetCursorPos(out curPt))
					{
						if (dockedChart1Hwnd != IntPtr.Zero && StackerWin32.IsWindowVisible(dockedChart1Hwnd))
						{
							StackerWin32.RECT r1;
							if (StackerWin32.GetWindowRect(dockedChart1Hwnd, out r1) &&
								curPt.X >= r1.Left && curPt.X <= r1.Right && curPt.Y >= r1.Top && curPt.Y <= r1.Bottom)
							{
								FocusPaneChart(1);
								ForwardWheelToPaneChart(1, e.Delta, e.Timestamp);
								e.Handled = true;
								return;
							}
						}
						if (dockedChart2Hwnd != IntPtr.Zero && StackerWin32.IsWindowVisible(dockedChart2Hwnd))
						{
							StackerWin32.RECT r2;
							if (StackerWin32.GetWindowRect(dockedChart2Hwnd, out r2) &&
								curPt.X >= r2.Left && curPt.X <= r2.Right && curPt.Y >= r2.Top && curPt.Y <= r2.Bottom)
							{
								FocusPaneChart(2);
								ForwardWheelToPaneChart(2, e.Delta, e.Timestamp);
								e.Handled = true;
								return;
							}
						}
					}
				}
				catch { }
			};

			PreviewKeyDown += (s, e) =>
			{
				try
				{
					if ((cmbSymbol != null && cmbSymbol.IsKeyboardFocusWithin) ||
						(cmbSplit != null && cmbSplit.IsKeyboardFocusWithin))
						return;

					bool altDown = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
					if (e.Key == Key.PageUp || (altDown && e.Key == Key.Left))
					{
						StepSymbol(-1);
						e.Handled = true;
					}
					else if (e.Key == Key.PageDown || (altDown && e.Key == Key.Right))
					{
						StepSymbol(1);
						e.Handled = true;
					}
					else if (e.Key == Key.F5)
					{
						ReloadBothDockedChartsHistoricalData(false);
						StartReadinessSync(GetSelectedSymbol());
						e.Handled = true;
					}
				}
				catch { }
			};
		}

		private void ReleaseHeaderControlFocus()
		{
			try
			{
				Keyboard.ClearFocus();
				if (rootGrid != null)
				{
					rootGrid.Focusable = true;
					rootGrid.FocusVisualStyle = null;
					FocusManager.SetFocusedElement(this, rootGrid);
					Keyboard.Focus(rootGrid);
				}
			}
			catch { }
		}

		private void ConfigureCleanStackerComboBox(ComboBox cb)
		{
			if (cb == null)
				return;
			cb.FocusVisualStyle = null;
			cb.Resources[SystemParameters.DropShadowKey] = false;
			cb.Resources[SystemParameters.ComboBoxPopupAnimationKey] = System.Windows.Controls.Primitives.PopupAnimation.None;
			Action stripShadow = () =>
			{
				try
				{
					if (cb.Template != null)
					{
						var popup = cb.Template.FindName("PART_Popup", cb) as System.Windows.Controls.Primitives.Popup;
						if (popup != null)
						{
							popup.PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.None;
							if (popup.Child is FrameworkElement feChild)
							{
								feChild.Resources[SystemParameters.DropShadowKey] = false;
								var prop = feChild.GetType().GetProperty("Color");
								if (prop != null && feChild.GetType().Name.IndexOf("Shadow", StringComparison.OrdinalIgnoreCase) >= 0)
									prop.SetValue(feChild, Colors.Transparent, null);
							}
						}
					}
				}
				catch { }
			};
			cb.Loaded += (s, e) => stripShadow();
			cb.DropDownOpened += (s, e) =>
			{
				lastStackerInteractionUtc = DateTime.UtcNow;
				stripShadow();
			};
			cb.DropDownClosed += (s, e) =>
			{
				lastStackerInteractionUtc = DateTime.UtcNow;
				FlushPopupArtifactsAndKeepFront(true);
			};
		}

		private void FlushPopupArtifactsAndKeepFront(bool keepStackerForeground = true)
		{
			try
			{
				ReleaseHeaderControlFocus();
				if (isWindowClosed || !IsVisible || WindowState == WindowState.Minimized)
					return;

				if (stackerHwnd == IntPtr.Zero)
					stackerHwnd = new WindowInteropHelper(this).Handle;

				if (keepStackerForeground && stackerHwnd != IntPtr.Zero)
				{
					StackerWin32.BringWindowToTop(stackerHwnd);
					StackerWin32.SetForegroundWindow(stackerHwnd);
					Activate();
				}

				InvalidateVisual();
				if (stackerHwnd != IntPtr.Zero)
				{
					StackerWin32.RedrawWindow(
						stackerHwnd,
						IntPtr.Zero,
						IntPtr.Zero,
						StackerWin32.RDW_INVALIDATE | StackerWin32.RDW_FRAME | StackerWin32.RDW_ALLCHILDREN | StackerWin32.RDW_UPDATENOW);
				}
				RefreshDockedChartVisuals(0, false, true);

				var cleanupTimer = new System.Windows.Threading.DispatcherTimer(
					TimeSpan.FromMilliseconds(45),
					System.Windows.Threading.DispatcherPriority.Render,
					(ts, te) => { },
					Dispatcher);
				cleanupTimer.Tick += (ts, te) =>
				{
					cleanupTimer.Stop();
					try
					{
						if (isWindowClosed || !IsVisible)
							return;
						if (stackerHwnd != IntPtr.Zero)
						{
							StackerWin32.RedrawWindow(
								stackerHwnd,
								IntPtr.Zero,
								IntPtr.Zero,
								StackerWin32.RDW_INVALIDATE | StackerWin32.RDW_ALLCHILDREN | StackerWin32.RDW_UPDATENOW);
						}
						RefreshDockedChartVisuals(0, false, true);
					}
					catch { }
				};
				cleanupTimer.Start();
			}
			catch { }
		}

		private void FocusPaneChart(int paneIndex)
		{
			try
			{
				IntPtr hwnd = paneIndex == 1 ? dockedChart1Hwnd : dockedChart2Hwnd;
				var chart = paneIndex == 1 ? dockedChart1Instance : dockedChart2Instance;
				if (hwnd != IntPtr.Zero && StackerWin32.IsWindow(hwnd))
					StackerWin32.SetFocus(hwnd);
				if (chart != null && chart.ActiveChartControl != null)
				{
					ChartControl cc = chart.ActiveChartControl;
					cc.Dispatcher.BeginInvoke(new Action(() =>
					{
						try
						{
							cc.Focusable = true;
							Keyboard.Focus(cc);
							cc.Focus();
						}
						catch { }
					}));
				}
			}
			catch { }
		}

		private void StepSymbol(int delta)
		{
			if (cmbSymbol == null || cmbSymbol.Items.Count == 0)
				return;
			int count = cmbSymbol.Items.Count;
			int curIdx = cmbSymbol.SelectedIndex;
			if (curIdx < 0)
			{
				string curTxt = GetSelectedSymbol();
				for (int i = 0; i < count; i++)
				{
					if (string.Equals(cmbSymbol.Items[i] as string, curTxt, StringComparison.OrdinalIgnoreCase))
					{
						curIdx = i;
						break;
					}
				}
			}
			int nextIdx = ((curIdx < 0 ? 0 : curIdx) + delta) % count;
			if (nextIdx < 0) nextIdx += count;
			string nextSym = cmbSymbol.Items[nextIdx] as string;
			if (!string.IsNullOrWhiteSpace(nextSym))
			{
				LoadSymbolPairOnWindow(nextSym, false);
			}
		}

		private IntPtr OnStackerWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if ((msg == StackerWin32.WM_ACTIVATE && wParam != IntPtr.Zero) ||
				(msg == StackerWin32.WM_NCACTIVATE && wParam != IntPtr.Zero))
			{
				try
				{
					RefreshDockedChartVisuals(0, false, false);
				}
				catch { }
			}
			return IntPtr.Zero;
		}

		private void BuildUI()
		{
			rootGrid = new Grid();
			rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) }); // Ultra-slim top header
			rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Split Panes
			Content = rootGrid;

			// Top Header Bar
			BuildTopHeader();

			// Split Panes Grid
			BuildSplitPanes();
		}

		private void BuildTopHeader()
		{
			Border headerBorder = new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(20, 24, 33)), // #141821
				BorderBrush = new SolidColorBrush(Color.FromRgb(36, 42, 56)),
				BorderThickness = new Thickness(0, 0, 0, 1),
				Padding = new Thickness(12, 4, 12, 4)
			};
			Grid.SetRow(headerBorder, 0);
			rootGrid.Children.Add(headerBorder);

			Grid headerGrid = new Grid();
			headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Left Logo
			headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Spacer
			headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Layout & Symbol Controls
			headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Quick Launch Actions
			headerBorder.Child = headerGrid;

			// Brand & Badges (Clean, uncluttered header per user specification)
			StackPanel brandPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			TextBlock txtBrand = new TextBlock
			{
				Text = "⚡ AK10X TRADING SCREEN",
				FontWeight = FontWeights.Bold,
				FontSize = 13.5,
				Foreground = new SolidColorBrush(Color.FromRgb(255, 179, 0)), // Gold
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 10, 0)
			};
			Border badgeProfile = CreateBadge("SCREEN 1: MARKET PROFILE (1M)", Color.FromRgb(0, 229, 255), Color.FromRgb(6, 36, 46));
			Border badgeCandles = CreateBadge("SCREEN 2: ORDERFLOW (5M FOOTPRINT)", Color.FromRgb(0, 230, 118), Color.FromRgb(8, 42, 22));
			badgeCandles.Margin = new Thickness(6, 0, 0, 0);

			brandPanel.Children.Add(txtBrand);
			brandPanel.Children.Add(badgeProfile);
			brandPanel.Children.Add(badgeCandles);
			Grid.SetColumn(brandPanel, 0);
			headerGrid.Children.Add(brandPanel);

			// Middle Controls: Active Symbol Selector + Split Dropdown + Layout & Swap
			StackPanel layoutPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) };

			TextBlock lblSymbol = new TextBlock
			{
				Text = "Symbol:",
				Foreground = new SolidColorBrush(Color.FromRgb(255, 179, 0)), // Gold label
				FontWeight = FontWeights.Bold,
				FontSize = 11,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 6, 0)
			};

			cmbSymbol = new ComboBox
			{
				Width = 142,
				Height = 24,
				IsEditable = true,
				FontSize = 11,
				FontWeight = FontWeights.SemiBold,
				Background = new SolidColorBrush(Color.FromRgb(16, 20, 28)),
				Foreground = Brushes.White,
				VerticalContentAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 12, 0)
			};
			string[] defaultSyms = new string[] {
				"NIFTY_I", "BANKNIFTY_I", "FINNIFTY_I", "ABB_I", "ADANIPORTS_I", "ASIANPAINT_I",
				"AXISBANK_I", "BAJAJ_AUTO_I", "BAJAJFINSV_I", "BAJFINANCE_I", "BHARTIARTL_I", "BPCL_I",
				"BRITANNIA_I", "CIPLA_I", "COALINDIA_I", "DIVISLAB_I", "DRREDDY_I", "EICHERMOT_I",
				"GAIL_I", "GRASIM_I", "HCLTECH_I", "HDFCBANK_I", "HDFCLIFE_I", "HEROMOTOCO_I",
				"HINDALCO_I", "HINDUNILVR_I", "ICICIBANK_I", "INDUSINDBK_I", "INFY_I", "IOC_I",
				"ITC_I", "JSWSTEEL_I", "KOTAKBANK_I", "LT_I", "M_M_I", "MARUTI_I",
				"NESTLEIND_I", "NTPC_I", "ONGC_I", "PNB_I", "POWERGRID_I", "RELIANCE_I",
				"SBILIFE_I", "SBIN_I", "SHREECEM_I", "SUNPHARMA_I", "TATAMOTORS_I", "TATASTEEL_I",
				"TCS_I", "TECHM_I", "TITAN_I", "ULTRACEMCO_I", "UPL_I", "WIPRO_I"
			};
			foreach (string s in defaultSyms)
				cmbSymbol.Items.Add(s);
			isSuppressSymbolSelectionChanged = true;
			cmbSymbol.SelectedIndex = 0;
			isSuppressSymbolSelectionChanged = false;
			ConfigureCleanStackerComboBox(cmbSymbol);

			cmbSymbol.PreviewMouseWheel += (s, e) =>
			{
				if (!cmbSymbol.IsDropDownOpen)
					e.Handled = true;
			};
			cmbSymbol.SelectionChanged += (s, e) =>
			{
				if (isSuppressSymbolSelectionChanged || cmbSymbol.IsDropDownOpen) return;
				Instance = this;
				stackerDispatcher = this.Dispatcher;
				string selectedSym = cmbSymbol.SelectedItem as string;
				if (!string.IsNullOrWhiteSpace(selectedSym) && !string.Equals(selectedSym.Trim(), currentLoadedSymbol, StringComparison.OrdinalIgnoreCase))
				{
					ReleaseHeaderControlFocus();
					LoadSymbolPairOnWindow(selectedSym.Trim());
				}
			};
			cmbSymbol.DropDownClosed += (s, e) =>
			{
				if (isSuppressSymbolSelectionChanged) return;
				Instance = this;
				stackerDispatcher = this.Dispatcher;
				string sym = (cmbSymbol.SelectedItem as string) ?? cmbSymbol.Text;
				ReleaseHeaderControlFocus();
				if (!string.IsNullOrWhiteSpace(sym) && !string.Equals(sym.Trim(), currentLoadedSymbol, StringComparison.OrdinalIgnoreCase))
				{
					LoadSymbolPairOnWindow(sym.Trim());
				}
			};
			cmbSymbol.KeyDown += (s, e) =>
			{
				if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(cmbSymbol.Text))
				{
					Instance = this;
					stackerDispatcher = this.Dispatcher;
					string sym = cmbSymbol.Text.Trim();
					ReleaseHeaderControlFocus();
					LoadSymbolPairOnWindow(sym);
					e.Handled = true;
				}
			};

			TextBlock lblSplit = new TextBlock
			{
				Text = "Split:",
				Foreground = new SolidColorBrush(Color.FromRgb(165, 178, 198)),
				FontWeight = FontWeights.SemiBold,
				FontSize = 11,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 6, 0)
			};

			cmbSplit = new ComboBox
			{
				Width = 88,
				Height = 24,
				IsEditable = false,
				FontSize = 11,
				FontWeight = FontWeights.SemiBold,
				Background = new SolidColorBrush(Color.FromRgb(16, 20, 28)),
				Foreground = Brushes.White,
				VerticalContentAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 8, 0),
				ToolTip = "Screen 1 / Screen 2 Split Ratio"
			};
			cmbSplit.Items.Add("50 / 50");
			cmbSplit.Items.Add("60 / 40");
			cmbSplit.Items.Add("70 / 30");
			cmbSplit.Items.Add("40 / 60");
			cmbSplit.Items.Add("30 / 70");
			cmbSplit.SelectedIndex = 0;
			ConfigureCleanStackerComboBox(cmbSplit);
			cmbSplit.PreviewMouseWheel += (s, e) =>
			{
				if (!cmbSplit.IsDropDownOpen)
					e.Handled = true;
			};
			cmbSplit.SelectionChanged += (s, e) =>
			{
				string sel = cmbSplit.SelectedItem as string;
				FlushPopupArtifactsAndKeepFront(true);
				if (sel == "60 / 40") ApplySplit(0.6, 0.4);
				else if (sel == "70 / 30") ApplySplit(0.7, 0.3);
				else if (sel == "40 / 60") ApplySplit(0.4, 0.6);
				else if (sel == "30 / 70") ApplySplit(0.3, 0.7);
				else ApplySplit(0.5, 0.5);
			};

			Button btnToggleLayout = CreateHeaderButton("Layout ⊞", (s, e) => { ToggleLayout(); FlushPopupArtifactsAndKeepFront(true); });
			btnSwapPanes = CreateHeaderButton("Swap Panes ⇄", (s, e) => { SwapPanes(); FlushPopupArtifactsAndKeepFront(true); });

			layoutPanel.Children.Add(lblSymbol);
			layoutPanel.Children.Add(cmbSymbol);
			layoutPanel.Children.Add(lblSplit);
			layoutPanel.Children.Add(cmbSplit);
			layoutPanel.Children.Add(btnToggleLayout);
			layoutPanel.Children.Add(btnSwapPanes);
			Grid.SetColumn(layoutPanel, 2);
			headerGrid.Children.Add(layoutPanel);

			// Right Actions: Launch Trading Screen, Scanner Switcher, Auto-Dock & Undock All
			StackPanel actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			Button btnAutoLaunch = CreateAccentButton("⚡ Launch Trading Screen", (s, e) =>
			{
				Instance = this;
				stackerDispatcher = this.Dispatcher;
				LoadSymbolPairOnWindow(GetSelectedSymbol(), true);
				FlushPopupArtifactsAndKeepFront(true);
			});
			Button btnScanner = CreateHeaderButton("📊 Scanner", (s, e) =>
			{
				lastIntentionalScannerActivateUtc = DateTime.UtcNow;
				ReleaseHeaderControlFocus();
				ActivateScannerWindow();
			});
			btnScanner.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
			btnScanner.FontWeight = FontWeights.Bold;
			Button btnAutoDock = CreateHeaderButton("Auto-Dock Open", (s, e) => { AutoDockOpenCharts(); FlushPopupArtifactsAndKeepFront(true); });
			Button btnUndockAll = CreateHeaderButton("Undock All ↗", (s, e) => { UndockAll(); FlushPopupArtifactsAndKeepFront(true); });

			actionsPanel.Children.Add(btnAutoLaunch);
			actionsPanel.Children.Add(btnScanner);
			actionsPanel.Children.Add(btnAutoDock);
			actionsPanel.Children.Add(btnUndockAll);
			Grid.SetColumn(actionsPanel, 3);
			headerGrid.Children.Add(actionsPanel);
		}

		private void BuildSplitPanes()
		{
			mainSplitGrid = new Grid();
			Grid.SetRow(mainSplitGrid, 1);
			rootGrid.Children.Add(mainSplitGrid);

			// Columns for Side-by-Side (Default)
			colPane1 = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 };
			colSplitter = new ColumnDefinition { Width = new GridLength(6) };
			colPane2 = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 };

			mainSplitGrid.ColumnDefinitions.Add(colPane1);
			mainSplitGrid.ColumnDefinitions.Add(colSplitter);
			mainSplitGrid.ColumnDefinitions.Add(colPane2);

			gridSplitter = new GridSplitter
			{
				Width = 6,
				Background = new SolidColorBrush(Color.FromRgb(26, 30, 42)),
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Stretch,
				ShowsPreview = false,
				Cursor = Cursors.SizeWE
			};
			gridSplitter.DragDelta += (s, e) => SyncBothPanes();
			gridSplitter.DragCompleted += (s, e) => SyncBothPanes();
			Grid.SetColumn(gridSplitter, 1);
			mainSplitGrid.Children.Add(gridSplitter);

			// Pane 1 (Left - Red Accent Border matching reference design)
			borderPane1 = new Border
			{
				BorderBrush = new SolidColorBrush(Color.FromRgb(229, 57, 53)), // #E53935 Red Accent
				BorderThickness = new Thickness(2),
				CornerRadius = new CornerRadius(2),
				Margin = new Thickness(3, 3, 2, 3),
				Background = Brushes.Transparent
			};
			borderPane1.SizeChanged += (s, e) => SyncPaneWindow(1);
			Grid.SetColumn(borderPane1, 0);
			mainSplitGrid.Children.Add(borderPane1);

			// Pane 2 (Right - Emerald Green Accent Border)
			borderPane2 = new Border
			{
				BorderBrush = new SolidColorBrush(Color.FromRgb(0, 230, 118)), // #00E676 Emerald Green Accent
				BorderThickness = new Thickness(2),
				CornerRadius = new CornerRadius(2),
				Margin = new Thickness(2, 3, 3, 3),
				Background = Brushes.Transparent
			};
			borderPane2.SizeChanged += (s, e) => SyncPaneWindow(2);
			Grid.SetColumn(borderPane2, 2);
			mainSplitGrid.Children.Add(borderPane2);

			// Fill Pane 1
			Grid pane1Layout = new Grid();
			pane1Layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); // Header
			pane1Layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
			borderPane1.Child = pane1Layout;

			BuildPaneHeader(pane1Layout, 1, out txtPane1Title, out cmbPane1Charts);
			placeholderPane1 = CreatePlaceholder(1);
			Grid.SetRow(placeholderPane1, 1);
			pane1Layout.Children.Add(placeholderPane1);

			// Fill Pane 2
			Grid pane2Layout = new Grid();
			pane2Layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) }); // Header
			pane2Layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
			borderPane2.Child = pane2Layout;

			BuildPaneHeader(pane2Layout, 2, out txtPane2Title, out cmbPane2Charts);
			placeholderPane2 = CreatePlaceholder(2);
			Grid.SetRow(placeholderPane2, 1);
			pane2Layout.Children.Add(placeholderPane2);
		}

		private void BuildPaneHeader(Grid parentGrid, int paneIndex, out TextBlock txtTitle, out ComboBox cmbCharts)
		{
			Border headerBorder = new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(22, 26, 36)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(32, 38, 52)),
				BorderThickness = new Thickness(0, 0, 0, 1),
				Padding = new Thickness(8, 2, 8, 2)
			};
			Grid.SetRow(headerBorder, 0);
			parentGrid.Children.Add(headerBorder);

			Grid hGrid = new Grid();
			hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			hGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			headerBorder.Child = hGrid;

			txtTitle = new TextBlock
			{
				Text = paneIndex == 1 ? "SCREEN 1: MARKET PROFILE (1M TPO)" : "SCREEN 2: ORDERFLOW (5M FOOTPRINT)",
				Foreground = paneIndex == 1 ? new SolidColorBrush(Color.FromRgb(0, 229, 255)) : new SolidColorBrush(Color.FromRgb(0, 230, 118)),
				FontWeight = FontWeights.Bold,
				FontSize = 11,
				VerticalAlignment = VerticalAlignment.Center
			};
			Grid.SetColumn(txtTitle, 0);
			hGrid.Children.Add(txtTitle);

			StackPanel controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			cmbCharts = new ComboBox
			{
				Width = 170,
				Height = 22,
				FontSize = 11,
				Background = new SolidColorBrush(Color.FromRgb(16, 20, 28)),
				Foreground = Brushes.White,
				Margin = new Thickness(0, 0, 6, 0),
				Visibility = Visibility.Collapsed
			};
			cmbCharts.DropDownOpened += (s, e) => RefreshAvailableCharts();
			int pIdx = paneIndex;
			cmbCharts.SelectionChanged += (s, e) =>
			{
				ComboBox cb = s as ComboBox;
				if (cb != null && cb.SelectedItem is ChartItem item && item.Hwnd != IntPtr.Zero)
				{
					DockChartByHwnd(item.Hwnd, item.Title, pIdx, null);
				}
			};

			Button btnUndock = CreatePaneButton("Undock ↗", (s, e) => UndockPane(pIdx));
			btnUndock.Visibility = Visibility.Collapsed;
			Button btnMax = CreatePaneButton("Maximize ⤢", (s, e) => ToggleMaximize(pIdx));
			if (pIdx == 1) btnMaxPane1 = btnMax;
			else btnMaxPane2 = btnMax;

			headerBorder.MouseLeftButtonDown += (s, e) =>
			{
				if (e.ClickCount == 2)
				{
					ToggleMaximize(pIdx);
					e.Handled = true;
				}
			};

			controls.Children.Add(cmbCharts);
			controls.Children.Add(btnUndock);
			controls.Children.Add(btnMax);
			Grid.SetColumn(controls, 1);
			hGrid.Children.Add(controls);
		}

		private StackPanel CreatePlaceholder(int paneIndex)
		{
			StackPanel outer = new StackPanel
			{
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Center
			};

			Border glassCard = new Border
			{
				Background = new SolidColorBrush(Color.FromRgb(17, 23, 34)),
				BorderBrush = new SolidColorBrush(paneIndex == 1 ? Color.FromRgb(24, 92, 118) : Color.FromRgb(18, 96, 62)),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(10),
				Padding = new Thickness(32, 24, 32, 24),
				MinWidth = 360
			};

			StackPanel p = new StackPanel
			{
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Center
			};

			TextBlock icon = new TextBlock
			{
				Text = paneIndex == 1 ? "📊" : "🕯️",
				FontSize = 30,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0, 0, 0, 8)
			};
			TextBlock title = new TextBlock
			{
				Text = paneIndex == 1 ? "Screen 1: Market Profile (1M TPO)" : "Screen 2: Orderflow Footprint (5M)",
				FontWeight = FontWeights.Bold,
				FontSize = 15,
				Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0, 0, 0, 6)
			};
			TextBlock desc = new TextBlock
			{
				Text = paneIndex == 1
					? "Dalton Market Profile (1-Min timeframe, 70% Value Area, Initial Balance, POC)"
					: "MZ-Style Orderflow Footprint (5-Min Clusters, POC, 70% VA, 400% Imbalances & Delta Grid)",
				FontSize = 11.5,
				Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0, 0, 0, 14)
			};

			ProgressBar prog = new ProgressBar
			{
				Width = 260,
				Height = 4,
				IsIndeterminate = true,
				Background = new SolidColorBrush(Color.FromRgb(28, 36, 52)),
				BorderThickness = new Thickness(0),
				Foreground = new SolidColorBrush(paneIndex == 1 ? Color.FromRgb(0, 229, 255) : Color.FromRgb(0, 230, 118)),
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0, 2, 0, 10),
				Visibility = Visibility.Collapsed
			};

			Button btnLaunch = CreateAccentButton(
				paneIndex == 1 ? "+ Open Market Profile Chart" : "+ Open 5-Min Orderflow Chart",
				(s, e) => LaunchChartForPane(paneIndex));
			btnLaunch.HorizontalAlignment = HorizontalAlignment.Center;

			if (paneIndex == 1)
			{
				txtPlaceholder1Title = title;
				txtPlaceholder1Desc = desc;
				progressPlaceholder1 = prog;
				btnPlaceholder1Open = btnLaunch;
			}
			else
			{
				txtPlaceholder2Title = title;
				txtPlaceholder2Desc = desc;
				progressPlaceholder2 = prog;
				btnPlaceholder2Open = btnLaunch;
			}

			p.Children.Add(icon);
			p.Children.Add(title);
			p.Children.Add(desc);
			p.Children.Add(prog);
			p.Children.Add(btnLaunch);
			glassCard.Child = p;
			outer.Children.Add(glassCard);
			return outer;
		}

		#region Native Win32 Child Window Embedding & Docking Mechanics
		private void ForwardWheelToPaneChart(int paneIndex, int delta, int timestamp)
		{
			try
			{
				var chart = paneIndex == 1 ? dockedChart1Instance : dockedChart2Instance;
				if (chart == null || chart.ActiveChartControl == null) return;
				ChartControl cc = chart.ActiveChartControl;
				cc.Dispatcher.BeginInvoke(new Action(() =>
				{
					try
					{
						if (Mouse.PrimaryDevice != null)
						{
							var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, timestamp, delta)
							{
								RoutedEvent = UIElement.PreviewMouseWheelEvent,
								Source = cc
							};
							cc.RaiseEvent(args);
						}
					}
					catch { }
				}));
			}
			catch { }
		}

		private void StartSmoothPaneReveal(int paneIndex)
		{
			try
			{
				IntPtr chartHwnd = paneIndex == 1 ? dockedChart1Hwnd : dockedChart2Hwnd;
				var chart = paneIndex == 1 ? dockedChart1Instance : dockedChart2Instance;
				IntPtr d2dHwnd = (chart != null && chart.Form != null && chart.Form.IsHandleCreated) ? chart.Form.Handle : IntPtr.Zero;

				if (paneIndex == 1) d2dCurrentAlpha1 = 55;
				else d2dCurrentAlpha2 = 55;

				if (d2dHwnd != IntPtr.Zero)
				{
					StackerWin32.SetLayeredWindowAttributes(d2dHwnd, 0, 55, StackerWin32.LWA_ALPHA);
					StackerWin32.SetCloaked(d2dHwnd, false);
				}
				if (chartHwnd != IntPtr.Zero)
				{
					StackerWin32.SetCloaked(chartHwnd, false);
				}

				if (d2dFadeTimer == null)
				{
					d2dFadeTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render, Dispatcher)
					{
						Interval = TimeSpan.FromMilliseconds(16)
					};
					d2dFadeTimer.Tick += (s, e) =>
					{
						try
						{
							bool active = false;
							if (d2dCurrentAlpha1 < 255)
							{
								int step1 = Math.Max(28, (int)Math.Ceiling((255 - d2dCurrentAlpha1) * 0.45));
								d2dCurrentAlpha1 = (byte)Math.Min(255, d2dCurrentAlpha1 + step1);
								if (dockedChart1Instance?.Form != null && dockedChart1Instance.Form.IsHandleCreated)
									StackerWin32.SetLayeredWindowAttributes(dockedChart1Instance.Form.Handle, 0, d2dCurrentAlpha1, StackerWin32.LWA_ALPHA);
								if (d2dCurrentAlpha1 < 255) active = true;
							}
							if (d2dCurrentAlpha2 < 255)
							{
								int step2 = Math.Max(28, (int)Math.Ceiling((255 - d2dCurrentAlpha2) * 0.45));
								d2dCurrentAlpha2 = (byte)Math.Min(255, d2dCurrentAlpha2 + step2);
								if (dockedChart2Instance?.Form != null && dockedChart2Instance.Form.IsHandleCreated)
									StackerWin32.SetLayeredWindowAttributes(dockedChart2Instance.Form.Handle, 0, d2dCurrentAlpha2, StackerWin32.LWA_ALPHA);
								if (d2dCurrentAlpha2 < 255) active = true;
							}
							if (!active)
								d2dFadeTimer.Stop();
						}
						catch
						{
							d2dFadeTimer.Stop();
						}
					};
				}
				if (!d2dFadeTimer.IsEnabled)
					d2dFadeTimer.Start();
			}
			catch { }
		}

		private void AttachChildChartHook(IntPtr hwnd, int paneIndex)
		{
			try
			{
				DetachChildChartHook(paneIndex);
				if (hwnd == IntPtr.Zero) return;
				HwndSource src = HwndSource.FromHwnd(hwnd);
				if (src != null)
				{
					src.AddHook(OnDockedChartWndProc);
					if (paneIndex == 1) hookedChildSource1 = src;
					else hookedChildSource2 = src;
				}
			}
			catch { }
		}

		private void DetachChildChartHook(int paneIndex)
		{
			try
			{
				if (paneIndex == 1 && hookedChildSource1 != null)
				{
					hookedChildSource1.RemoveHook(OnDockedChartWndProc);
					hookedChildSource1 = null;
				}
				else if (paneIndex == 2 && hookedChildSource2 != null)
				{
					hookedChildSource2.RemoveHook(OnDockedChartWndProc);
					hookedChildSource2 = null;
				}
			}
			catch { }
		}

		private IntPtr OnDockedChartWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			try
			{
				int paneIdx = (hwnd == dockedChart1Hwnd) ? 1 : (hwnd == dockedChart2Hwnd ? 2 : 0);
				if (paneIdx == 0)
					return IntPtr.Zero;

				// 1. UNCONDITIONAL PARENT & BOUNDS LOCK:
				//    Even if NTWindow / Chart.SetOwner(Direct2DForm) attempts to re-own/detach hwnd away from stackerHwnd,
				//    immediately restore WS_CHILD parenting and clamp WINDOWPOS before a single frame is painted!
				if (msg == StackerWin32.WM_WINDOWPOSCHANGING && lParam != IntPtr.Zero && stackerHwnd != IntPtr.Zero)
				{
					if (StackerWin32.GetParent(hwnd) != stackerHwnd)
					{
						int style = StackerWin32.GetWindowLong(hwnd, StackerWin32.GWL_STYLE);
						style &= ~unchecked((int)(StackerWin32.WS_POPUP | StackerWin32.WS_CAPTION | StackerWin32.WS_THICKFRAME | StackerWin32.WS_MINIMIZEBOX | StackerWin32.WS_MAXIMIZEBOX | StackerWin32.WS_SYSMENU));
						style |= unchecked((int)(StackerWin32.WS_CHILD | StackerWin32.WS_CLIPCHILDREN | StackerWin32.WS_CLIPSIBLINGS));
						StackerWin32.SetWindowLong(hwnd, StackerWin32.GWL_STYLE, style);
						StackerWin32.SetParent(hwnd, stackerHwnd);
					}

					int tx = paneIdx == 1 ? lastX1 : lastX2;
					int ty = paneIdx == 1 ? lastY1 : lastY2;
					int tw = paneIdx == 1 ? lastW1 : lastW2;
					int th = paneIdx == 1 ? lastH1 : lastH2;

					if (tw >= 50 && th >= 50)
					{
						StackerWin32.WINDOWPOS wp = (StackerWin32.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(StackerWin32.WINDOWPOS));
						bool blockedBogusMove = false;
						if ((wp.flags & StackerWin32.SWP_NOMOVE) == 0 && (wp.x != tx || wp.y != ty))
						{
							wp.x = tx;
							wp.y = ty;
							blockedBogusMove = true;
						}
						if ((wp.flags & StackerWin32.SWP_NOSIZE) == 0 && (wp.cx != tw || wp.cy != th))
						{
							wp.cx = tw;
							wp.cy = th;
							blockedBogusMove = true;
						}
						if (blockedBogusMove)
						{
							Marshal.StructureToPtr(wp, lParam, false);
							int p = paneIdx;
							Dispatcher.BeginInvoke(new Action(() => RefreshDockedChartVisuals(p, true, true)));
						}
					}
				}
				// 2. MACBOOK HOVER-TO-SCROLL ROUTING:
				//    If Pane 1 has Win32 focus and the user hovers over Pane 2 and scrolls (or vice versa),
				//    redirect the wheel message seamlessly to the hovered pane without requiring a click!
				else if (msg == StackerWin32.WM_MOUSEWHEEL)
				{
					IntPtr otherHwnd = paneIdx == 1 ? dockedChart2Hwnd : dockedChart1Hwnd;
					if (otherHwnd != IntPtr.Zero && StackerWin32.IsWindowVisible(otherHwnd))
					{
						StackerWin32.POINT curPt;
						StackerWin32.RECT otherRect;
						if (StackerWin32.GetCursorPos(out curPt) &&
							StackerWin32.GetWindowRect(otherHwnd, out otherRect) &&
							curPt.X >= otherRect.Left && curPt.X <= otherRect.Right &&
							curPt.Y >= otherRect.Top && curPt.Y <= otherRect.Bottom)
						{
							StackerWin32.SetFocus(otherHwnd);
							StackerWin32.PostMessage(otherHwnd, StackerWin32.WM_MOUSEWHEEL, wParam, lParam);
							handled = true;
							return IntPtr.Zero;
						}
					}
				}
				// 3. INSTANT FOCUS ROUTING: Clicking anywhere inside the embedded chart transfers Win32 & WPF focus to it.
				else if (msg == StackerWin32.WM_LBUTTONDOWN || msg == StackerWin32.WM_RBUTTONDOWN || msg == StackerWin32.WM_MBUTTONDOWN)
				{
					if (StackerWin32.GetFocus() != hwnd)
					{
						StackerWin32.SetFocus(hwnd);
					}
				}
			}
			catch { }
			return IntPtr.Zero;
		}

		private static void RestoreUndockedD2DForm(NinjaTrader.Gui.Chart.Chart chart, IntPtr chartHwnd)
		{
			try
			{
				if (chart == null || chart.Form == null || !chart.Form.IsHandleCreated)
					return;
				IntPtr d2dHwnd = chart.Form.Handle;
				int curEx = StackerWin32.GetWindowLong(d2dHwnd, StackerWin32.GWL_EXSTYLE);
				int restoredEx = (curEx & ~(StackerWin32.WS_EX_LAYERED | StackerWin32.WS_EX_TRANSPARENT | StackerWin32.WS_EX_NOACTIVATE)) | StackerWin32.WS_EX_COMPOSITED;
				StackerWin32.SetWindowLong(d2dHwnd, StackerWin32.GWL_EXSTYLE, restoredEx);
				var setOwnerMethod = typeof(NinjaTrader.Gui.Chart.Chart).GetMethod("SetOwner", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
				if (setOwnerMethod != null)
					setOwnerMethod.Invoke(chart, new object[] { chart.Form });
				else
					StackerWin32.SetWindowLongPtr(d2dHwnd, StackerWin32.GWLP_HWNDPARENT, chartHwnd);
				if (cachedMoveResizeMethod != null)
					cachedMoveResizeMethod.Invoke(chart.Form, new object[] { true, true });
			}
			catch { }
		}

		public void DockChartByHwnd(IntPtr hwnd, string title, int paneIndex, NinjaTrader.Gui.Chart.Chart chartInstance = null)
		{
			if (hwnd == IntPtr.Zero) return;
			if (AK10XChartStackerWindow.StackerDispatcher != null && !AK10XChartStackerWindow.StackerDispatcher.CheckAccess())
			{
				AK10XChartStackerWindow.StackerDispatcher.BeginInvoke(new Action(() => DockChartByHwnd(hwnd, title, paneIndex, chartInstance)));
				return;
			}
			if (!Dispatcher.CheckAccess())
			{
				Dispatcher.BeginInvoke(new Action(() => DockChartByHwnd(hwnd, title, paneIndex, chartInstance)));
				return;
			}

			if (stackerHwnd == IntPtr.Zero)
				stackerHwnd = new WindowInteropHelper(this).EnsureHandle();

			try
			{
				if (chartInstance != null)
				{
					try
					{
						chartInstance.Owner = null;
						chartInstance.Resources[SystemParameters.DropShadowKey] = false;
						chartInstance.Resources[SystemParameters.ComboBoxPopupAnimationKey] = System.Windows.Controls.Primitives.PopupAnimation.None;
						chartInstance.PreviewMouseDown += (s, ev) => { lastStackerInteractionUtc = DateTime.UtcNow; };
						if (chartInstance.WindowState != WindowState.Normal)
							chartInstance.WindowState = WindowState.Normal;
						int targetPane = paneIndex;
						chartInstance.StateChanged += (s, ev) =>
						{
							try
							{
								if (chartInstance.WindowState == WindowState.Maximized)
								{
									chartInstance.WindowState = WindowState.Normal;
									ToggleMaximize(targetPane);
								}
								else if (chartInstance.WindowState == WindowState.Minimized)
								{
									chartInstance.WindowState = WindowState.Normal;
								}
							}
							catch { }
						};
						chartInstance.LocationChanged += (s, ev) =>
						{
							try
							{
								RefreshDockedChartVisuals(targetPane, false, true);
							}
							catch { }
						};
						chartInstance.Activated += (s, ev) =>
						{
							try
							{
								RefreshDockedChartVisuals(0, false, false);
							}
							catch { }
						};
						chartInstance.Loaded += (s, ev) =>
						{
							try
							{
								InvalidatePaneBoundsCache();
								SyncPaneWindow(targetPane);
								RefreshDockedChartVisuals(targetPane, true, true);
							}
							catch { }
						};
						chartInstance.ContentRendered += (s, ev) =>
						{
							try
							{
								InvalidatePaneBoundsCache();
								SyncPaneWindow(targetPane);
								RefreshDockedChartVisuals(targetPane, true, true);
							}
							catch { }
						};
					}
					catch { }
				}

				EnsureParentClipChildren();

				// Clear any lingering Win32 owner reference (e.g. ControlCenter) before converting to WS_CHILD
				StackerWin32.SetWindowLongPtr(hwnd, StackerWin32.GWLP_HWNDPARENT, IntPtr.Zero);

				// 1. Strip top-level borders/caption and set WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS
				int style = StackerWin32.GetWindowLong(hwnd, StackerWin32.GWL_STYLE);
				style &= ~unchecked((int)(StackerWin32.WS_POPUP | StackerWin32.WS_CAPTION | StackerWin32.WS_THICKFRAME | StackerWin32.WS_MINIMIZEBOX | StackerWin32.WS_MAXIMIZEBOX | StackerWin32.WS_SYSMENU));
				style |= unchecked((int)(StackerWin32.WS_CHILD | StackerWin32.WS_CLIPCHILDREN | StackerWin32.WS_CLIPSIBLINGS));
				StackerWin32.SetWindowLong(hwnd, StackerWin32.GWL_STYLE, style);

				// 2. Make it a true Win32 child window of stackerHwnd! (NEVER floats over Scanner or desktop!)
				StackerWin32.SetParent(hwnd, stackerHwnd);

				StackerWin32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
					StackerWin32.SWP_NOMOVE | StackerWin32.SWP_NOSIZE | StackerWin32.SWP_NOZORDER | StackerWin32.SWP_NOACTIVATE | StackerWin32.SWP_FRAMECHANGED);

				if (paneIndex == 1)
				{
					if (dockedChart1Hwnd != IntPtr.Zero && dockedChart1Hwnd != hwnd)
						CloseSingleDockedChart(1);

					isCreatingPane1 = false;
					dockedChart1Hwnd = hwnd;
					dockedChart1Title = title;
					dockedChart1Instance = chartInstance;
					lastSyncedD2dHwnd1 = IntPtr.Zero;
					lastX1 = -99999; lastY1 = -99999; lastW1 = -99999; lastH1 = -99999;

					txtPane1Title.Text = "SCREEN 1: " + title;
					UpdatePanePlaceholderState(1, false);
					placeholderPane1.Visibility = Visibility.Collapsed;
					HookChartInstrumentSelector(chartInstance, 1);
					SyncPaneWindow(1);
					AttachChildChartHook(hwnd, 1);
				}
				else
				{
					if (dockedChart2Hwnd != IntPtr.Zero && dockedChart2Hwnd != hwnd)
						CloseSingleDockedChart(2);

					isCreatingPane2 = false;
					dockedChart2Hwnd = hwnd;
					dockedChart2Title = title;
					dockedChart2Instance = chartInstance;
					lastSyncedD2dHwnd2 = IntPtr.Zero;
					lastX2 = -99999; lastY2 = -99999; lastW2 = -99999; lastH2 = -99999;

					txtPane2Title.Text = "SCREEN 2: " + title;
					UpdatePanePlaceholderState(2, false);
					placeholderPane2.Visibility = Visibility.Collapsed;
					HookChartInstrumentSelector(chartInstance, 2);
					SyncPaneWindow(2);
					AttachChildChartHook(hwnd, 2);
				}

				UpdateWindowTitle();
				RefreshAvailableCharts();
				StartReadinessSync(currentLoadedSymbol);
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process("AK10X Docking Error: " + ex.Message, PrintTo.OutputTab1);
			}
		}

		public void UndockPane(int paneIndex)
		{
			if (AK10XChartStackerWindow.StackerDispatcher != null && !AK10XChartStackerWindow.StackerDispatcher.CheckAccess())
			{
				AK10XChartStackerWindow.StackerDispatcher.BeginInvoke(new Action(() => UndockPane(paneIndex)));
				return;
			}
			if (!Dispatcher.CheckAccess())
			{
				Dispatcher.BeginInvoke(new Action(() => UndockPane(paneIndex)));
				return;
			}

			try
			{
				if (paneIndex == 1 && dockedChart1Hwnd != IntPtr.Zero)
				{
					DetachChildChartHook(1);
					IntPtr hwnd = dockedChart1Hwnd;
					var c1 = dockedChart1Instance;
					dockedChart1Hwnd = IntPtr.Zero;
					dockedChart1Title = null;
					dockedChart1Instance = null;
					lastX1 = -1; lastY1 = -1; lastW1 = -1; lastH1 = -1;

					// Restore top-level window hierarchy
					StackerWin32.SetParent(hwnd, IntPtr.Zero);

					int style = StackerWin32.GetWindowLong(hwnd, StackerWin32.GWL_STYLE);
					style &= ~unchecked((int)StackerWin32.WS_CHILD);
					style |= unchecked((int)(StackerWin32.WS_POPUP | StackerWin32.WS_CAPTION | StackerWin32.WS_THICKFRAME | StackerWin32.WS_MINIMIZEBOX | StackerWin32.WS_MAXIMIZEBOX | StackerWin32.WS_SYSMENU));
					StackerWin32.SetWindowLong(hwnd, StackerWin32.GWL_STYLE, style);

					StackerWin32.SetWindowPos(hwnd, IntPtr.Zero, 100, 100, 960, 680, 
						StackerWin32.SWP_NOZORDER | StackerWin32.SWP_FRAMECHANGED | StackerWin32.SWP_SHOWWINDOW);

					RestoreUndockedD2DForm(c1, hwnd);

					txtPane1Title.Text = "SCREEN 1: MARKET PROFILE (1M TPO)";
					placeholderPane1.Visibility = Visibility.Visible;
				}
				else if (paneIndex == 2 && dockedChart2Hwnd != IntPtr.Zero)
				{
					DetachChildChartHook(2);
					IntPtr hwnd = dockedChart2Hwnd;
					var c2 = dockedChart2Instance;
					dockedChart2Hwnd = IntPtr.Zero;
					dockedChart2Title = null;
					dockedChart2Instance = null;
					lastX2 = -1; lastY2 = -1; lastW2 = -1; lastH2 = -1;

					// Restore top-level window hierarchy
					StackerWin32.SetParent(hwnd, IntPtr.Zero);

					int style = StackerWin32.GetWindowLong(hwnd, StackerWin32.GWL_STYLE);
					style &= ~unchecked((int)StackerWin32.WS_CHILD);
					style |= unchecked((int)(StackerWin32.WS_POPUP | StackerWin32.WS_CAPTION | StackerWin32.WS_THICKFRAME | StackerWin32.WS_MINIMIZEBOX | StackerWin32.WS_MAXIMIZEBOX | StackerWin32.WS_SYSMENU));
					StackerWin32.SetWindowLong(hwnd, StackerWin32.GWL_STYLE, style);

					StackerWin32.SetWindowPos(hwnd, IntPtr.Zero, 140, 140, 960, 680, 
						StackerWin32.SWP_NOZORDER | StackerWin32.SWP_FRAMECHANGED | StackerWin32.SWP_SHOWWINDOW);

					RestoreUndockedD2DForm(c2, hwnd);

					txtPane2Title.Text = "SCREEN 2: ORDERFLOW (5M FOOTPRINT)";
					placeholderPane2.Visibility = Visibility.Visible;
				}

				UpdateWindowTitle();
				RefreshAvailableCharts();
			}
			catch { }
		}

		public void UndockAll()
		{
			if (AK10XChartStackerWindow.StackerDispatcher != null && !AK10XChartStackerWindow.StackerDispatcher.CheckAccess())
			{
				AK10XChartStackerWindow.StackerDispatcher.BeginInvoke(new Action(() => UndockAll()));
				return;
			}
			if (!Dispatcher.CheckAccess())
			{
				Dispatcher.BeginInvoke(new Action(() => UndockAll()));
				return;
			}

			UndockPane(1);
			UndockPane(2);
		}

		private void ApplyGridSlotPositions()
		{
			if (borderPane1 == null || borderPane2 == null || gridSplitter == null)
				return;

			int pane1Slot = isPanesSwapped ? 2 : 0;
			int pane2Slot = isPanesSwapped ? 0 : 2;

			if (!isStackedLayout)
			{
				Grid.SetColumn(borderPane1, pane1Slot);
				Grid.SetRow(borderPane1, 0);
				borderPane1.Margin = (pane1Slot == 0) ? new Thickness(3, 3, 2, 3) : new Thickness(2, 3, 3, 3);

				Grid.SetColumn(gridSplitter, 1);
				Grid.SetRow(gridSplitter, 0);

				Grid.SetColumn(borderPane2, pane2Slot);
				Grid.SetRow(borderPane2, 0);
				borderPane2.Margin = (pane2Slot == 0) ? new Thickness(3, 3, 2, 3) : new Thickness(2, 3, 3, 3);
			}
			else
			{
				Grid.SetColumn(borderPane1, 0);
				Grid.SetRow(borderPane1, pane1Slot);
				borderPane1.Margin = (pane1Slot == 0) ? new Thickness(3, 3, 3, 2) : new Thickness(3, 2, 3, 3);

				Grid.SetColumn(gridSplitter, 0);
				Grid.SetRow(gridSplitter, 1);

				Grid.SetColumn(borderPane2, 0);
				Grid.SetRow(borderPane2, pane2Slot);
				borderPane2.Margin = (pane2Slot == 0) ? new Thickness(3, 3, 3, 2) : new Thickness(3, 2, 3, 3);
			}

			if (btnSwapPanes != null)
			{
				btnSwapPanes.BorderBrush = new SolidColorBrush(isPanesSwapped ? Color.FromRgb(56, 189, 248) : Color.FromRgb(42, 49, 66));
				btnSwapPanes.Foreground = new SolidColorBrush(isPanesSwapped ? Color.FromRgb(125, 211, 252) : Color.FromRgb(220, 226, 236));
			}
		}

		private void SwapPanes()
		{
			if (!Dispatcher.CheckAccess())
			{
				Dispatcher.BeginInvoke(new Action(() => SwapPanes()));
				return;
			}

			isPanesSwapped = !isPanesSwapped;
			InvalidatePaneBoundsCache();
			ApplyGridSlotPositions();

			if (isPane1Maximized)
			{
				isPane1Maximized = false;
				ToggleMaximize(1);
			}
			else if (isPane2Maximized)
			{
				isPane2Maximized = false;
				ToggleMaximize(2);
			}
			else
			{
				ApplySplit(currentLeftSplitRatio, currentRightSplitRatio);
			}

			RefreshDockedChartVisuals(0, true, true);
		}

		private void SyncPaneWindow(int paneIndex)
		{
			if (AK10XChartStackerWindow.StackerDispatcher != null && !AK10XChartStackerWindow.StackerDispatcher.CheckAccess())
			{
				AK10XChartStackerWindow.StackerDispatcher.BeginInvoke(new Action(() => SyncPaneWindow(paneIndex)));
				return;
			}
			if (!Dispatcher.CheckAccess())
			{
				Dispatcher.BeginInvoke(new Action(() => SyncPaneWindow(paneIndex)));
				return;
			}

			IntPtr hwnd = paneIndex == 1 ? dockedChart1Hwnd : dockedChart2Hwnd;
			Border container = paneIndex == 1 ? borderPane1 : borderPane2;

			if (hwnd == IntPtr.Zero || container == null)
				return;

			if (!StackerWin32.IsWindow(hwnd))
			{
				if (paneIndex == 1)
				{
					dockedChart1Hwnd = IntPtr.Zero;
					dockedChart1Instance = null;
					lastSyncedD2dHwnd1 = IntPtr.Zero;
					if (placeholderPane1 != null) placeholderPane1.Visibility = Visibility.Visible;
				}
				else
				{
					dockedChart2Hwnd = IntPtr.Zero;
					dockedChart2Instance = null;
					lastSyncedD2dHwnd2 = IntPtr.Zero;
					if (placeholderPane2 != null) placeholderPane2.Visibility = Visibility.Visible;
				}
				return;
			}

			if (WindowState == WindowState.Minimized ||
				container.Visibility != Visibility.Visible ||
				(paneIndex == 1 && isPane2Maximized) ||
				(paneIndex == 2 && isPane1Maximized))
			{
				StackerWin32.ShowWindow(hwnd, StackerWin32.SW_HIDE);
				SyncD2DForm(paneIndex, false);
				if (paneIndex == 1) { lastX1 = -99999; lastY1 = -99999; lastW1 = -99999; lastH1 = -99999; }
				else { lastX2 = -99999; lastY2 = -99999; lastW2 = -99999; lastH2 = -99999; }
				return;
			}

			if (!container.IsLoaded || !IsVisible || container.ActualWidth < 10 || container.ActualHeight < 10)
				return;

			try
			{
				if (PresentationSource.FromVisual(this) == null)
					return;

				if (stackerHwnd == IntPtr.Zero)
					stackerHwnd = new WindowInteropHelper(this).Handle;

				// Ensure child window relationship wasn't reset by NTWindow lifecycle
				if (stackerHwnd != IntPtr.Zero && StackerWin32.GetParent(hwnd) != stackerHwnd)
				{
					int style = StackerWin32.GetWindowLong(hwnd, StackerWin32.GWL_STYLE);
					style &= ~unchecked((int)(StackerWin32.WS_POPUP | StackerWin32.WS_CAPTION | StackerWin32.WS_THICKFRAME | StackerWin32.WS_MINIMIZEBOX | StackerWin32.WS_MAXIMIZEBOX | StackerWin32.WS_SYSMENU));
					style |= unchecked((int)(StackerWin32.WS_CHILD | StackerWin32.WS_CLIPCHILDREN | StackerWin32.WS_CLIPSIBLINGS));
					StackerWin32.SetWindowLong(hwnd, StackerWin32.GWL_STYLE, style);
					StackerWin32.SetParent(hwnd, stackerHwnd);
				}

				// Point relative to this Window (RootVisual of stackerHwnd)
				Point winPt = container.TranslatePoint(new Point(0, 0), this);

				PresentationSource source = PresentationSource.FromVisual(this);
				double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
				double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

				int headerH = 28;
				int borderThickness = 2;

				int x = (int)Math.Round((winPt.X + borderThickness) * dpiX);
				int y = (int)Math.Round((winPt.Y + headerH) * dpiY);
				int w = Math.Max(50, (int)Math.Round((container.ActualWidth - (borderThickness * 2)) * dpiX));
				int h = Math.Max(50, (int)Math.Round((container.ActualHeight - headerH - borderThickness) * dpiY));

				// Verify ACTUAL Win32 child window screen rect so NTWindow.Loaded can NEVER leave hwnd at wrong coords!
				bool actualRectMatches = true;
				if (stackerHwnd != IntPtr.Zero)
				{
					StackerWin32.POINT expectedScreenPt = new StackerWin32.POINT { X = x, Y = y };
					StackerWin32.ClientToScreen(stackerHwnd, ref expectedScreenPt);
					StackerWin32.RECT actualRect;
					if (StackerWin32.GetWindowRect(hwnd, out actualRect))
					{
						int actualW = actualRect.Right - actualRect.Left;
						int actualH = actualRect.Bottom - actualRect.Top;
						if (Math.Abs(actualRect.Left - expectedScreenPt.X) > 2 ||
							Math.Abs(actualRect.Top - expectedScreenPt.Y) > 2 ||
							Math.Abs(actualW - w) > 2 ||
							Math.Abs(actualH - h) > 2)
						{
							actualRectMatches = false;
						}
					}
				}
				bool hwndVisible = StackerWin32.IsWindowVisible(hwnd);

				bool sizeChanged;
				bool posChanged;
				if (paneIndex == 1)
				{
					if (actualRectMatches && hwndVisible && x == lastX1 && y == lastY1 && w == lastW1 && h == lastH1)
					{
						SyncD2DForm(1, true, false, false);
						return;
					}
					sizeChanged = !actualRectMatches || (w != lastW1 || h != lastH1);
					posChanged = !actualRectMatches || (x != lastX1 || y != lastY1);
					lastX1 = x; lastY1 = y; lastW1 = w; lastH1 = h;
				}
				else
				{
					if (actualRectMatches && hwndVisible && x == lastX2 && y == lastY2 && w == lastW2 && h == lastH2)
					{
						SyncD2DForm(2, true, false, false);
						return;
					}
					sizeChanged = !actualRectMatches || (w != lastW2 || h != lastH2);
					posChanged = !actualRectMatches || (x != lastX2 || y != lastY2);
					lastX2 = x; lastY2 = y; lastW2 = w; lastH2 = h;
				}

				StackerWin32.MoveWindow(hwnd, x, y, w, h, true);
				if (!hwndVisible)
					StackerWin32.ShowWindow(hwnd, StackerWin32.SW_SHOWNOACTIVATE);
				SyncD2DForm(paneIndex, true, sizeChanged, posChanged);
			}
			catch { }
		}

		public void SyncBothPanes()
		{
			if (AK10XChartStackerWindow.StackerDispatcher != null && !AK10XChartStackerWindow.StackerDispatcher.CheckAccess())
			{
				AK10XChartStackerWindow.StackerDispatcher.BeginInvoke(new Action(() => SyncBothPanes()));
				return;
			}
			if (!Dispatcher.CheckAccess())
			{
				Dispatcher.BeginInvoke(new Action(() => SyncBothPanes()));
				return;
			}

			SyncPaneWindow(1);
			SyncPaneWindow(2);
		}

		private void ApplySplit(double leftRatio, double rightRatio)
		{
			currentLeftSplitRatio = leftRatio;
			currentRightSplitRatio = rightRatio;
			isPane1Maximized = false;
			isPane2Maximized = false;
			if (btnMaxPane1 != null) btnMaxPane1.Content = "Maximize ⤢";
			if (btnMaxPane2 != null) btnMaxPane2.Content = "Maximize ⤢";

			if (!isStackedLayout)
			{
				colPane1.MinWidth = 150;
				colPane2.MinWidth = 150;
				colSplitter.Width = new GridLength(6);
				colPane1.Width = new GridLength(leftRatio, GridUnitType.Star);
				colPane2.Width = new GridLength(rightRatio, GridUnitType.Star);
				borderPane1.Visibility = Visibility.Visible;
				borderPane2.Visibility = Visibility.Visible;
				gridSplitter.Visibility = Visibility.Visible;
			}
			else
			{
				rowPane1.MinHeight = 120;
				rowPane2.MinHeight = 120;
				rowSplitter.Height = new GridLength(6);
				rowPane1.Height = new GridLength(leftRatio, GridUnitType.Star);
				rowPane2.Height = new GridLength(rightRatio, GridUnitType.Star);
				borderPane1.Visibility = Visibility.Visible;
				borderPane2.Visibility = Visibility.Visible;
				gridSplitter.Visibility = Visibility.Visible;
			}

			ForceLayoutAndSyncPanes();
		}

		private void ToggleMaximize(int paneIndex)
		{
			if (paneIndex == 1)
			{
				if (isPane1Maximized)
				{
					ApplySplit(currentLeftSplitRatio, currentRightSplitRatio);
					return;
				}
				else
				{
					isPane1Maximized = true;
					isPane2Maximized = false;
					if (btnMaxPane1 != null) btnMaxPane1.Content = "Restore ⤡";
					if (btnMaxPane2 != null) btnMaxPane2.Content = "Maximize ⤢";

					if (dockedChart2Hwnd != IntPtr.Zero)
						StackerWin32.ShowWindow(dockedChart2Hwnd, StackerWin32.SW_HIDE);
					SyncD2DForm(2, false);

					bool pane1InFirstSlot = !isPanesSwapped;
					if (!isStackedLayout)
					{
						colPane1.MinWidth = 0;
						colPane2.MinWidth = 0;
						colSplitter.Width = new GridLength(0);
						colPane1.Width = pane1InFirstSlot ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
						colPane2.Width = pane1InFirstSlot ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
						borderPane1.Visibility = Visibility.Visible;
						borderPane2.Visibility = Visibility.Collapsed;
						gridSplitter.Visibility = Visibility.Collapsed;
					}
					else
					{
						rowPane1.MinHeight = 0;
						rowPane2.MinHeight = 0;
						rowSplitter.Height = new GridLength(0);
						rowPane1.Height = pane1InFirstSlot ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
						rowPane2.Height = pane1InFirstSlot ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
						borderPane1.Visibility = Visibility.Visible;
						borderPane2.Visibility = Visibility.Collapsed;
						gridSplitter.Visibility = Visibility.Collapsed;
					}
				}
			}
			else
			{
				if (isPane2Maximized)
				{
					ApplySplit(currentLeftSplitRatio, currentRightSplitRatio);
					return;
				}
				else
				{
					isPane2Maximized = true;
					isPane1Maximized = false;
					if (btnMaxPane2 != null) btnMaxPane2.Content = "Restore ⤡";
					if (btnMaxPane1 != null) btnMaxPane1.Content = "Maximize ⤢";

					if (dockedChart1Hwnd != IntPtr.Zero)
						StackerWin32.ShowWindow(dockedChart1Hwnd, StackerWin32.SW_HIDE);
					SyncD2DForm(1, false);

					bool pane2InFirstSlot = isPanesSwapped;
					if (!isStackedLayout)
					{
						colPane1.MinWidth = 0;
						colPane2.MinWidth = 0;
						colSplitter.Width = new GridLength(0);
						colPane1.Width = pane2InFirstSlot ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
						colPane2.Width = pane2InFirstSlot ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
						borderPane1.Visibility = Visibility.Collapsed;
						borderPane2.Visibility = Visibility.Visible;
						gridSplitter.Visibility = Visibility.Collapsed;
					}
					else
					{
						rowPane1.MinHeight = 0;
						rowPane2.MinHeight = 0;
						rowSplitter.Height = new GridLength(0);
						rowPane1.Height = pane2InFirstSlot ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
						rowPane2.Height = pane2InFirstSlot ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
						borderPane1.Visibility = Visibility.Collapsed;
						borderPane2.Visibility = Visibility.Visible;
						gridSplitter.Visibility = Visibility.Collapsed;
					}
				}
			}

			ForceLayoutAndSyncPanes();
		}

		private void ToggleLayout()
		{
			isStackedLayout = !isStackedLayout;
			InvalidatePaneBoundsCache();

			mainSplitGrid.ColumnDefinitions.Clear();
			mainSplitGrid.RowDefinitions.Clear();
			mainSplitGrid.Children.Clear();

			if (!isStackedLayout)
			{
				colPane1 = new ColumnDefinition { Width = new GridLength(currentLeftSplitRatio, GridUnitType.Star), MinWidth = 150 };
				colSplitter = new ColumnDefinition { Width = new GridLength(6) };
				colPane2 = new ColumnDefinition { Width = new GridLength(currentRightSplitRatio, GridUnitType.Star), MinWidth = 150 };

				mainSplitGrid.ColumnDefinitions.Add(colPane1);
				mainSplitGrid.ColumnDefinitions.Add(colSplitter);
				mainSplitGrid.ColumnDefinitions.Add(colPane2);

				gridSplitter.Width = 6;
				gridSplitter.Height = double.NaN;
				gridSplitter.Cursor = Cursors.SizeWE;
				gridSplitter.HorizontalAlignment = HorizontalAlignment.Center;
				gridSplitter.VerticalAlignment = VerticalAlignment.Stretch;
			}
			else
			{
				rowPane1 = new RowDefinition { Height = new GridLength(currentLeftSplitRatio, GridUnitType.Star), MinHeight = 120 };
				rowSplitter = new RowDefinition { Height = new GridLength(6) };
				rowPane2 = new RowDefinition { Height = new GridLength(currentRightSplitRatio, GridUnitType.Star), MinHeight = 120 };

				mainSplitGrid.RowDefinitions.Add(rowPane1);
				mainSplitGrid.RowDefinitions.Add(rowSplitter);
				mainSplitGrid.RowDefinitions.Add(rowPane2);

				gridSplitter.Width = double.NaN;
				gridSplitter.Height = 6;
				gridSplitter.Cursor = Cursors.SizeNS;
				gridSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
				gridSplitter.VerticalAlignment = VerticalAlignment.Center;
			}

			ApplyGridSlotPositions();

			mainSplitGrid.Children.Add(borderPane1);
			mainSplitGrid.Children.Add(gridSplitter);
			mainSplitGrid.Children.Add(borderPane2);

			if (isPane1Maximized)
			{
				isPane1Maximized = false;
				ToggleMaximize(1);
			}
			else if (isPane2Maximized)
			{
				isPane2Maximized = false;
				ToggleMaximize(2);
			}
			else
			{
				ApplySplit(currentLeftSplitRatio, currentRightSplitRatio);
			}

			RefreshDockedChartVisuals(0, true, true);
		}

		public void RefreshAvailableCharts()
		{
			if (AK10XChartStackerWindow.StackerDispatcher != null && !AK10XChartStackerWindow.StackerDispatcher.CheckAccess())
			{
				AK10XChartStackerWindow.StackerDispatcher.BeginInvoke(new Action(() => RefreshAvailableCharts()));
				return;
			}
			if (!Dispatcher.CheckAccess())
			{
				Dispatcher.BeginInvoke(new Action(() => RefreshAvailableCharts()));
				return;
			}

			try
			{
				List<ChartItem> openCharts = new List<ChartItem>();
				openCharts.Add(new ChartItem { Hwnd = IntPtr.Zero, Title = "-- Select Open Chart --" });

				var allWins = NinjaTrader.Core.Globals.AllWindows;
				if (allWins != null)
				{
					for (int idx = 0; idx < allWins.Count; idx++)
					{
						Window win = null;
						try { win = allWins[idx]; } catch { }
						if (win is NinjaTrader.Gui.Chart.Chart chartWin && win.Dispatcher != null && win.Dispatcher.CheckAccess() && win.IsLoaded)
						{
							IntPtr hWnd = new WindowInteropHelper(win).Handle;
							if (hWnd == IntPtr.Zero || hWnd == stackerHwnd) continue;
							if (hWnd == dockedChart1Hwnd || hWnd == dockedChart2Hwnd) continue;

							string title = win.Title;
							if (string.IsNullOrWhiteSpace(title) && chartWin.ActiveChartControl != null && chartWin.ActiveChartControl.Instrument != null)
								title = "Chart " + chartWin.ActiveChartControl.Instrument.FullName;
							if (string.IsNullOrWhiteSpace(title))
								title = "Chart";

							openCharts.Add(new ChartItem { Hwnd = hWnd, Title = title, Instance = chartWin });
						}
					}
				}

				if (cmbPane1Charts != null)
				{
					cmbPane1Charts.ItemsSource = openCharts;
					cmbPane1Charts.DisplayMemberPath = "Title";
					cmbPane1Charts.SelectedIndex = 0;
				}
				if (cmbPane2Charts != null)
				{
					List<ChartItem> list2 = new List<ChartItem>(openCharts);
					cmbPane2Charts.ItemsSource = list2;
					cmbPane2Charts.DisplayMemberPath = "Title";
					cmbPane2Charts.SelectedIndex = 0;
				}
			}
			catch { }
		}

		private static int ClassifyChartRole(NinjaTrader.Gui.Chart.Chart chartWin)
		{
			try
			{
				var cc = chartWin?.ActiveChartControl;
				if (cc != null && cc.Indicators != null)
				{
					foreach (var ind in cc.Indicators)
					{
						if (ind == null) continue;
						string name = ind.GetType().Name;
						if (name.IndexOf("MarketProfile", StringComparison.OrdinalIgnoreCase) >= 0)
							return 1;
						if (name.IndexOf("AK10XOrderflow", StringComparison.OrdinalIgnoreCase) >= 0)
							return 2;
					}
				}
				if (cc != null && cc.PrimaryBars != null && cc.PrimaryBars.Properties != null && cc.PrimaryBars.Properties.BarsPeriod != null &&
					cc.PrimaryBars.Properties.BarsPeriod.BarsPeriodType == BarsPeriodType.Minute)
				{
					if (cc.PrimaryBars.Properties.BarsPeriod.Value == 1) return 1;
					if (cc.PrimaryBars.Properties.BarsPeriod.Value == 5) return 2;
				}
			}
			catch { }
			return 0;
		}

		private void AutoDockOpenCharts()
		{
			List<ChartItem> openCharts = new List<ChartItem>();

			var allWins = NinjaTrader.Core.Globals.AllWindows;
			if (allWins != null)
			{
				for (int idx = 0; idx < allWins.Count; idx++)
				{
					Window win = null;
					try { win = allWins[idx]; } catch { }
					if (win is NinjaTrader.Gui.Chart.Chart chartWin && win.Dispatcher != null && win.Dispatcher.CheckAccess() && win.IsLoaded)
					{
						IntPtr hWnd = new WindowInteropHelper(win).Handle;
						if (hWnd == IntPtr.Zero || hWnd == stackerHwnd) continue;
						if (hWnd == dockedChart1Hwnd || hWnd == dockedChart2Hwnd) continue;

						string title = win.Title;
						if (string.IsNullOrWhiteSpace(title) && chartWin.ActiveChartControl != null && chartWin.ActiveChartControl.Instrument != null)
							title = "Chart " + chartWin.ActiveChartControl.Instrument.FullName;
						if (string.IsNullOrWhiteSpace(title))
							title = "Chart";

						openCharts.Add(new ChartItem { Hwnd = hWnd, Title = title, Instance = chartWin });
					}
				}
			}

			ChartItem bestForPane1 = null;
			ChartItem bestForPane2 = null;
			foreach (var item in openCharts)
			{
				int role = ClassifyChartRole(item.Instance);
				if (role == 1 && bestForPane1 == null) bestForPane1 = item;
				else if (role == 2 && bestForPane2 == null) bestForPane2 = item;
			}
			foreach (var item in openCharts)
			{
				if (item == bestForPane1 || item == bestForPane2) continue;
				if (bestForPane1 == null && ClassifyChartRole(item.Instance) != 2) bestForPane1 = item;
				else if (bestForPane2 == null && ClassifyChartRole(item.Instance) != 1) bestForPane2 = item;
			}

			if (bestForPane1 != null && dockedChart1Hwnd == IntPtr.Zero && !isCreatingPane1)
			{
				DockChartByHwnd(bestForPane1.Hwnd, bestForPane1.Title, 1, bestForPane1.Instance);
			}
			if (bestForPane2 != null && bestForPane2 != bestForPane1 && dockedChart2Hwnd == IntPtr.Zero && !isCreatingPane2)
			{
				DockChartByHwnd(bestForPane2.Hwnd, bestForPane2.Title, 2, bestForPane2.Instance);
			}

			ForceLayoutAndSyncPanes();
			RefreshDockedChartVisuals(0, true, true);
			StartReadinessSync(GetSelectedSymbol());
		}

		private void UpdateWindowTitle()
		{
			string p1 = !string.IsNullOrEmpty(dockedChart1Title) ? dockedChart1Title : "[Empty]";
			string p2 = !string.IsNullOrEmpty(dockedChart2Title) ? dockedChart2Title : "[Empty]";
			Caption = string.Format("AK10X Trading Screen | Screen 1: {0} | Screen 2: {1}", p1, p2);
			Title = Caption;
		}

		private string GetSelectedSymbol()
		{
			if (cmbSymbol != null && !string.IsNullOrWhiteSpace(cmbSymbol.Text))
				return cmbSymbol.Text.Trim();
			return "NIFTY_I";
		}

		private void HookChartInstrumentSelector(NinjaTrader.Gui.Chart.Chart chartInstance, int paneIndex)
		{
			if (chartInstance == null) return;
			try
			{
				var selField = typeof(NinjaTrader.Gui.Chart.Chart).GetField("instrumentSelector", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
				if (selField != null)
				{
					var selector = selField.GetValue(chartInstance) as NinjaTrader.Gui.Tools.InstrumentSelector;
					if (selector != null)
					{
						selector.InstrumentChanged += (s, e) =>
						{
							if (isUpdatingInstrumentProgrammatically) return;
							try
							{
								Instrument newInst = selector.Instrument;
								if (newInst != null && !string.IsNullOrWhiteSpace(newInst.FullName))
								{
									string sym = newInst.FullName.Trim();
									if (!string.Equals(sym, currentLoadedSymbol, StringComparison.OrdinalIgnoreCase))
									{
										Dispatcher.BeginInvoke(new Action(() =>
										{
											LoadSymbolPairOnWindow(sym, false);
										}));
									}
								}
							}
							catch { }
						};
					}
				}
			}
			catch { }
		}

		public static Instrument ResolveBestInstrument(string preferredName)
		{
			try
			{
				if (!string.IsNullOrWhiteSpace(preferredName))
				{
					string clean = preferredName.Trim();
					if (clean.StartsWith("^")) clean = clean.Substring(1).Trim();
					if (clean.Equals("NIFTY", StringComparison.OrdinalIgnoreCase)) clean = "NIFTY_I";
					if (clean.Equals("BANKNIFTY", StringComparison.OrdinalIgnoreCase)) clean = "BANKNIFTY_I";

					// 1. Check already-loaded Instrument.All for an exact match first
					try
					{
						if (Instrument.All != null)
						{
							foreach (var it in Instrument.All)
							{
								if (it != null && (string.Equals(it.FullName, clean, StringComparison.OrdinalIgnoreCase) ||
									(it.MasterInstrument != null && string.Equals(it.MasterInstrument.Name, clean, StringComparison.OrdinalIgnoreCase))))
								{
									return it;
								}
							}
						}
					}
					catch { }

					Instrument inst = null;
					try { inst = Instrument.GetInstrument(clean, false); } catch { }
					if (inst == null)
					{
						try { inst = Instrument.GetInstrument(clean, true); } catch { }
					}
					if (inst == null)
					{
						try
						{
							inst = Instrument.GetInstrument(clean);
						}
						catch { }
					}
					if (inst == null)
					{
						try
						{
							Instrument fuzzy = Instrument.GetInstrumentFuzzy(clean);
							if (fuzzy != null && (fuzzy.FullName.StartsWith(clean, StringComparison.OrdinalIgnoreCase) ||
								(fuzzy.MasterInstrument != null && string.Equals(fuzzy.MasterInstrument.Name, clean, StringComparison.OrdinalIgnoreCase))))
							{
								inst = fuzzy;
							}
						}
						catch { }
					}
					if (inst == null && clean.EndsWith("_I", StringComparison.OrdinalIgnoreCase))
					{
						try { inst = Instrument.GetInstrument(clean.Substring(0, clean.Length - 2), true); } catch { }
					}

					// Return inst (even if null) so CreateConfiguredChart uses the requested symbol name and NEVER falls back to an unrelated symbol!
					return inst;
				}

				string[] candidates = new string[] { "NIFTY_I", "BANKNIFTY_I", "FINNIFTY_I", "RELIANCE_I", "HDFCBANK_I", "GC 12-26", "NQ 12-26", "MNQ 12-26", "ES 12-26" };
				foreach (var c in candidates)
				{
					try
					{
						Instrument inst = Instrument.GetInstrument(c, true);
						if (inst != null)
							return inst;
					}
					catch { }
				}
			}
			catch { }

			return null;
		}

		private static void SilenceChartStyleCandles(NinjaTrader.NinjaScript.ChartStyles.ChartStyle style)
		{
			if (style == null) return;
			try
			{
				style.UpBrush = Brushes.Transparent;
				style.DownBrush = Brushes.Transparent;
				if (style.Stroke != null) style.Stroke.Brush = Brushes.Transparent;
				if (style.Stroke2 != null) style.Stroke2.Brush = Brushes.Transparent;
			}
			catch { }
		}

		private static void SilenceRawCandlesticksOnChart(ChartControl cc)
		{
			if (cc == null) return;
			try
			{
				if (cc.BarsPropertiesCollection != null)
				{
					foreach (BarsProperties bp in cc.BarsPropertiesCollection)
					{
						if (bp != null)
							SilenceChartStyleCandles(bp.ChartStyle);
					}
				}
				if (cc.BarsArray != null)
				{
					foreach (ChartBars cb in cc.BarsArray)
					{
						if (cb != null && cb.Properties != null)
							SilenceChartStyleCandles(cb.Properties.ChartStyle);
					}
				}
			}
			catch { }
		}

		private static void EnsurePaneIndicatorAttached(ChartControl cc, bool applyMarketProfile)
		{
			if (cc == null || cc.PrimaryBars == null || cc.ChartPanels == null || cc.ChartPanels.Count == 0 || cc.BarsArray == null || cc.BarsArray.Count == 0)
				return;

			try
			{
				EnsureD2DFactoryForCurrentThread();
				// Hide raw NinjaTrader green/red candlesticks so neither Market Profile nor Orderflow ever flashes raw candles during symbol switches
				SilenceRawCandlesticksOnChart(cc);

				string targetTypePart = applyMarketProfile ? "MarketProfile" : "AK10XOrderflow";
				string conflictingTypePart = applyMarketProfile ? "AK10XOrderflow" : "MarketProfile";
				bool exists = false;
				bool hasConflictingIndicator = false;
				foreach (var ind in cc.Indicators)
				{
					if (ind == null) continue;
					string indTypeName = ind.GetType().Name;
					if (indTypeName.IndexOf(targetTypePart, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						exists = true;
						break;
					}
					if (indTypeName.IndexOf(conflictingTypePart, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						hasConflictingIndicator = true;
					}
				}

				if (hasConflictingIndicator)
				{
					cc.InvalidateVisual();
					return;
				}

				if (!applyMarketProfile && cc.Properties != null)
				{
					if (cc.Properties.BarDistance < 75f)
						cc.Properties.BarDistance = 110f;
					// Keep one full Footprint box width of empty gap after the last bar on Screen 2
					cc.Properties.BarMarginRightUser = Math.Max(48, (int)Math.Round(cc.Properties.BarDistance * 1.5f));
				}

				if (!exists)
				{
					if (applyMarketProfile)
					{
						var mp = new NinjaTrader.NinjaScript.Indicators.INDMarketProfile50Charts();
						mp.SetState(State.SetDefaults);
						mp.ChartControl = cc;
						mp.ChartBars = cc.PrimaryBars;
						mp.ChartPanel = cc.ChartPanels[0];
						mp.Panel = 0;
						cc.Indicators.Add(mp);
					}
					else
					{
						var ofInd = new NinjaTrader.NinjaScript.Indicators.AK10XOrderflow();
						ofInd.SetState(State.SetDefaults);
						ofInd.ChartControl = cc;
						ofInd.ChartBars = cc.PrimaryBars;
						ofInd.ChartPanel = cc.ChartPanels[0];
						ofInd.Panel = 0;
						cc.Indicators.Add(ofInd);
					}

					var refreshMethod = typeof(ChartControl).GetMethod("RefreshIndicators", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					if (refreshMethod != null)
						refreshMethod.Invoke(cc, new object[] { true, true });

					var applyMethod = typeof(ChartControl).GetMethod("ApplyNinjaScripts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					if (applyMethod != null)
						applyMethod.Invoke(cc, null);
				}

				cc.InvalidateVisual();
			}
			catch { }
		}

		private Rect GetTargetPaneInitialDipBounds(int paneIndex)
		{
			try
			{
				Border container = paneIndex == 1 ? borderPane1 : borderPane2;
				if (container != null && container.IsLoaded && container.ActualWidth > 60 && container.ActualHeight > 60 && PresentationSource.FromVisual(this) != null)
				{
					Point winPt = container.TranslatePoint(new Point(2, 28), this);
					return new Rect(
						Left + winPt.X,
						Top + winPt.Y,
						Math.Max(320, container.ActualWidth - 4),
						Math.Max(240, container.ActualHeight - 30));
				}
			}
			catch { }

			Rect wa = SystemParameters.WorkArea;
			double halfW = Math.Max(400, wa.Width * (paneIndex == 1 ? currentLeftSplitRatio : currentRightSplitRatio) - 8);
			double startX = paneIndex == 1 ? (wa.Left + 4) : (wa.Left + wa.Width * currentLeftSplitRatio + 4);
			return new Rect(startX, wa.Top + 74, halfW, Math.Max(360, wa.Height - 80));
		}

		private NinjaTrader.Gui.Chart.Chart CreateConfiguredChart(string instrumentName, int periodMinutes, bool applyMarketProfile, int paneIndex = 1)
		{
			try
			{
				string target = !string.IsNullOrWhiteSpace(instrumentName) ? instrumentName.Trim() : GetSelectedSymbol();
				if (target.StartsWith("^")) target = target.Substring(1).Trim();
				if (target.Equals("NIFTY", StringComparison.OrdinalIgnoreCase)) target = "NIFTY_I";
				if (target.Equals("BANKNIFTY", StringComparison.OrdinalIgnoreCase)) target = "BANKNIFTY_I";

				Instrument instrument = ResolveBestInstrument(target);
				string instSymbol = instrument != null ? instrument.FullName : target;
				if (instSymbol.StartsWith("^")) instSymbol = instSymbol.Substring(1).Trim();

				string thName = "Nse";
				if (instrument != null && instrument.MasterInstrument != null && instrument.MasterInstrument.TradingHours != null)
				{
					thName = instrument.MasterInstrument.TradingHours.Name;
				}

				string newSeriesId = Guid.NewGuid().ToString("N");
				BarsProperties bp = new BarsProperties
				{
					Instrument = instSymbol,
					BarsPeriod = new BarsPeriod
					{
						BarsPeriodType = BarsPeriodType.Minute,
						Value = periodMinutes,
						MarketDataType = MarketDataType.Last,
						BaseBarsPeriodType = BarsPeriodType.Minute,
						BaseBarsPeriodValue = 1
					},
					RangeType = RangeType.Days,
					DaysBack = applyMarketProfile ? 8 : 5,
					BarsBack = 500,
					From = DateTime.Today.AddDays(applyMarketProfile ? -10 : -8),
					To = new DateTime(2099, 12, 1),
					TradingHoursSerializable = thName,
					AutoScale = true,
					IsStableSession = true,
					ChartStyleType = ChartStyleType.CandleStick,
					IsPrimarySeries = true,
					BarsSeriesId = newSeriesId,
					Id = newSeriesId
				};

				EnsureD2DFactoryForCurrentThread();

				try
				{
					bp.ChartStyle = NinjaTrader.NinjaScript.ChartStyles.ChartStyle.CreateInstance(ChartStyleType.CandleStick);
					if (bp.ChartStyle != null)
					{
						if (!applyMarketProfile)
							bp.ChartStyle.BarWidth = 1;
						SilenceChartStyleCandles(bp.ChartStyle);
					}
				}
				catch
				{
					try
					{
						var cs = NinjaTrader.Core.Globals.AssemblyRegistry["NinjaTrader.Custom"].CreateInstance("NinjaTrader.NinjaScript.ChartStyles.CandleStyle") as NinjaTrader.NinjaScript.ChartStyles.ChartStyle;
						if (cs != null)
						{
							cs.SetState(State.SetDefaults);
							cs.SetState(State.Configure);
							if (!applyMarketProfile)
								cs.BarWidth = 1;
							SilenceChartStyleCandles(cs);
							bp.ChartStyle = cs;
						}
					}
					catch { }
				}

				ChartObjectCollection<BarsProperties> col = new ChartObjectCollection<BarsProperties>();
				col.Add(bp);
				Rect initRect = GetTargetPaneInitialDipBounds(paneIndex);
				NinjaTrader.Gui.Chart.Chart newChart = new NinjaTrader.Gui.Chart.Chart(col);
				newChart.Owner = null;
				newChart.Resources[SystemParameters.DropShadowKey] = false;
				newChart.Resources[SystemParameters.ComboBoxPopupAnimationKey] = System.Windows.Controls.Primitives.PopupAnimation.None;
				newChart.WindowStartupLocation = WindowStartupLocation.Manual;
				newChart.Left = initRect.Left;
				newChart.Top = initRect.Top;
				newChart.Width = initRect.Width;
				newChart.Height = initRect.Height;
				newChart.Background = new SolidColorBrush(Color.FromRgb(18, 22, 29)); // #12161D institutional dark
				newChart.ShowActivated = false;

				// Automatically attach INDMarketProfile50Charts (Screen 1) or AK10XOrderflow (Screen 2)
				bool isProfileChart = applyMarketProfile;
				var initTimer = new System.Windows.Threading.DispatcherTimer(
					TimeSpan.FromMilliseconds(35),
					System.Windows.Threading.DispatcherPriority.Background,
					(s, e) => { },
					newChart.Dispatcher);
				int initTries = 0;
				initTimer.Tick += (s, e) =>
				{
					initTries++;
					try
					{
						if (newChart.ActiveChartControl != null)
						{
							ChartControl cc = newChart.ActiveChartControl;
							if (cc.PrimaryBars == null || cc.ChartPanels == null || cc.ChartPanels.Count == 0 || cc.BarsArray == null || cc.BarsArray.Count == 0)
							{
								if (initTries >= 120)
									initTimer.Stop();
								return;
							}
							EnsurePaneIndicatorAttached(cc, isProfileChart);
							initTimer.Stop();
							Dispatcher.BeginInvoke(new Action(() =>
							{
								try
								{
									SyncBothPanes();
									RefreshDockedChartVisuals(0, true, true);
								}
								catch { }
							}));
							return;
						}
					}
					catch { }

					if (initTries >= 120)
						initTimer.Stop();
				};
				initTimer.Start();

				return newChart;
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process("AK10X CreateChart Error: " + ex.Message, PrintTo.OutputTab1);
				return new NinjaTrader.Gui.Chart.Chart();
			}
		}

		public void LaunchChartForPane(int paneIndex, string preferredSymbol = null)
		{
			Action openAction = () =>
			{
				try
				{
					string inst = !string.IsNullOrWhiteSpace(preferredSymbol) ? preferredSymbol.Trim() : GetSelectedSymbol();

					// Strict single-flight launch guard: NEVER spawn duplicate/orphaned Chart windows!
					if (paneIndex == 1)
					{
						if (isCreatingPane1)
						{
							pendingSymbolPane1 = inst;
							return;
						}
						isCreatingPane1 = true;
						pendingSymbolPane1 = null;
					}
					else
					{
						if (isCreatingPane2)
						{
							pendingSymbolPane2 = inst;
							return;
						}
						isCreatingPane2 = true;
						pendingSymbolPane2 = null;
					}

					UpdatePanePlaceholderState(paneIndex, true, inst);
					StartReadinessSync(inst);

					bool isProfile = (paneIndex == 1);
					int period = isProfile ? 1 : 5; // Chart 1 = 1-Min MP, Chart 2 = 5-Min Orderflow Footprint

					NinjaTrader.Gui.Chart.Chart newChart = CreateConfiguredChart(inst, period, isProfile, paneIndex);
					if (paneIndex == 1) stagedChart1 = newChart;
					else stagedChart2 = newChart;

					// DWM HARDWARE CLOAK STAGING (MacBook-Grade Zero-Flicker Launch):
					// Cloak the WPF Chart window BEFORE Show() so ZERO empty black frames, ZERO taskbar flashes,
					// and ZERO Direct2DForm SetOwner jumps at (0, 520) can ever appear on the monitor!
					IntPtr preHwnd = new WindowInteropHelper(newChart).EnsureHandle();
					if (preHwnd != IntPtr.Zero)
					{
						int exStyle = StackerWin32.GetWindowLong(preHwnd, StackerWin32.GWL_EXSTYLE);
						StackerWin32.SetWindowLong(preHwnd, StackerWin32.GWL_EXSTYLE, exStyle | StackerWin32.WS_EX_TOOLWINDOW | StackerWin32.WS_EX_NOACTIVATE);
						StackerWin32.SetCloaked(preHwnd, true);
					}
					newChart.Show();
					if (preHwnd != IntPtr.Zero)
						StackerWin32.SetCloaked(preHwnd, true);

					string fallbackTitle = paneIndex == 1 ? (inst + " (1-Min Market Profile)") : (inst + " (5-Min Orderflow)");
					int stageTicks = 0;
					int d2dReadyTicks = 0;
					IntPtr cloakedD2dHwnd = IntPtr.Zero;
					var stageTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render, Dispatcher)
					{
						Interval = TimeSpan.FromMilliseconds(16)
					};
					stageTimer.Tick += (s, e) =>
					{
						stageTicks++;
						if (isWindowClosed)
						{
							stageTimer.Stop();
							try { newChart.Close(); } catch { }
							return;
						}

						try
						{
							IntPtr cHwnd = new WindowInteropHelper(newChart).Handle;
							if (cHwnd != IntPtr.Zero && stageTicks <= 4)
								StackerWin32.SetCloaked(cHwnd, true);

							// Immediately cloak Direct2DForm the microsecond NinjaTrader creates it so it can NEVER flash off-pane!
							bool hasD2D = newChart.Form != null && newChart.Form.IsHandleCreated;
							if (hasD2D)
							{
								IntPtr dHwnd = newChart.Form.Handle;
								if (dHwnd != IntPtr.Zero && dHwnd != cloakedD2dHwnd)
								{
									cloakedD2dHwnd = dHwnd;
									StackerWin32.SetCloaked(dHwnd, true);
									int curEx = StackerWin32.GetWindowLong(dHwnd, StackerWin32.GWL_EXSTYLE);
									int reqEx = (curEx & ~StackerWin32.WS_EX_COMPOSITED) | StackerWin32.WS_EX_LAYERED | StackerWin32.WS_EX_TRANSPARENT | StackerWin32.WS_EX_NOACTIVATE;
									StackerWin32.SetWindowLong(dHwnd, StackerWin32.GWL_EXSTYLE, reqEx);
									StackerWin32.SetLayeredWindowAttributes(dHwnd, 0, 0, StackerWin32.LWA_ALPHA);
								}
							}

							bool ccReady = false;
							bool hasBars = false;
							ChartControl cc = newChart.ActiveChartControl;
							if (cc != null && cc.IsLoaded && cc.PrimaryBars != null && cc.ChartPanels != null && cc.ChartPanels.Count > 0)
							{
								EnsurePaneIndicatorAttached(cc, isProfile);
								ccReady = true;
								hasBars = (cc.PrimaryBars.Count > 0);
							}

							if (hasD2D && ccReady)
								d2dReadyTicks++;

							// Wait until Chart.SetOwner(Direct2DForm) has completed AND either bars are ready (plus 2 VSync ticks to paint)
							// or safety timeout expires so offline/weekend charts also dock smoothly!
							bool readyToReveal = (hasD2D && ccReady && ((hasBars && d2dReadyTicks >= 2) || d2dReadyTicks >= 22)) || stageTicks >= 85;
							if (readyToReveal && cHwnd != IntPtr.Zero)
							{
								stageTimer.Stop();
								if (paneIndex == 1)
								{
									stagedChart1 = null;
									d2dCurrentAlpha1 = 0;
								}
								else
								{
									stagedChart2 = null;
									d2dCurrentAlpha2 = 0;
								}

								// Strip temporary staging WS_EX_TOOLWINDOW before converting to WS_CHILD
								int exClean = StackerWin32.GetWindowLong(cHwnd, StackerWin32.GWL_EXSTYLE);
								StackerWin32.SetWindowLong(cHwnd, StackerWin32.GWL_EXSTYLE, exClean & ~StackerWin32.WS_EX_TOOLWINDOW);

								// Dock while STILL cloaked so MoveWindow + MoveResizeForm land at exact pane bounds invisibly!
								DockChartByHwnd(cHwnd, fallbackTitle, paneIndex, newChart);
								SyncPaneWindow(paneIndex);
								RefreshDockedChartVisuals(paneIndex, true, true);

								// Uncloak & smoothly ramp Direct2D alpha (55 -> 255 in ~96ms at 60Hz)
								StartSmoothPaneReveal(paneIndex);
								return;
							}
						}
						catch { }
					};
					stageTimer.Start();
				}
				catch (Exception ex)
				{
					if (paneIndex == 1) isCreatingPane1 = false;
					else isCreatingPane2 = false;
					UpdatePanePlaceholderState(paneIndex, false);
					NinjaTrader.Code.Output.Process("AK10X Launch Chart Inner Error: " + ex.Message, PrintTo.OutputTab1);
				}
			};

			if (!Dispatcher.CheckAccess())
				Dispatcher.BeginInvoke(openAction);
			else
				openAction();
		}

		private bool TryUpdateChartInstrumentInPlace(NinjaTrader.Gui.Chart.Chart chartWin, Instrument inst, int periodMinutes)
		{
			if (chartWin == null || inst == null) return false;
			bool success = false;
			bool isProfile = (periodMinutes == 1);
			Action updateAct = () =>
			{
				isUpdatingInstrumentProgrammatically = true;
				try
				{
					int targetDays = isProfile ? 8 : 5;
					ChartControl cc = chartWin.ActiveChartControl;
					if (cc != null)
					{
						if (cc.BarsPropertiesCollection != null)
						{
							foreach (BarsProperties bpItem in cc.BarsPropertiesCollection)
							{
								if (bpItem != null)
									bpItem.DaysBack = targetDays;
							}
						}
						if (cc.BarsArray != null)
						{
							foreach (ChartBars cbItem in cc.BarsArray)
							{
								if (cbItem != null && cbItem.Properties != null)
									cbItem.Properties.DaysBack = targetDays;
							}
						}
					}

					// 1. Update Chart's built-in instrumentSelector and trigger in-place series switch
					var selField = typeof(NinjaTrader.Gui.Chart.Chart).GetField("instrumentSelector", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
					NinjaTrader.Gui.Tools.InstrumentSelector selector = selField != null
						? (selField.GetValue(chartWin) as NinjaTrader.Gui.Tools.InstrumentSelector)
						: null;

					if (selector != null)
					{
						selector.SuppressEvent = false;
						selector.Instrument = inst;
						success = true;

						if (cc != null && (cc.Instrument == null || !string.Equals(cc.Instrument.FullName, inst.FullName, StringComparison.OrdinalIgnoreCase)))
						{
							var applySeries = typeof(NinjaTrader.Gui.Chart.Chart).GetMethod("ApplyDataSeriesChangeToSelected", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
							BarsPeriod curPeriod = (cc.PrimaryBars != null && cc.PrimaryBars.Properties != null) ? cc.PrimaryBars.Properties.BarsPeriod : null;
							bool applied = false;
							if (applySeries != null && curPeriod != null)
							{
								try
								{
									object res = applySeries.Invoke(chartWin, new object[] { cc, inst, curPeriod });
									applied = (res is bool b && b);
								}
								catch { }
							}
							if (!applied)
							{
								var onInstChanged = typeof(NinjaTrader.Gui.Chart.Chart).GetMethod("OnInstrumentChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
								if (onInstChanged != null)
								{
									try { onInstChanged.Invoke(chartWin, new object[] { selector, new CancelEventArgs(false) }); } catch { }
								}
							}
						}
					}
					else if (cc != null && cc.ChartTab != null)
					{
						try
						{
							cc.ChartTab.Instrument = inst;
							success = true;
						}
						catch { }
					}

					if (success)
					{
						if (chartWin.ActiveChartControl != null)
						{
							SilenceRawCandlesticksOnChart(chartWin.ActiveChartControl);
							EnsurePaneIndicatorAttached(chartWin.ActiveChartControl, isProfile);
						}

						// Post-switch async indicator & Direct2D refresh passes as NT8 finishes loading the new BarsSeries
						int[] delaysMs = new int[] { 25, 90, 260, 600 };
						foreach (int dMs in delaysMs)
						{
							var postTimer = new System.Windows.Threading.DispatcherTimer(
								TimeSpan.FromMilliseconds(dMs),
								System.Windows.Threading.DispatcherPriority.Background,
								(ts, te) => { },
								chartWin.Dispatcher);
							postTimer.Tick += (ts, te) =>
							{
								postTimer.Stop();
								try
								{
									if (chartWin.ActiveChartControl != null)
									{
										EnsurePaneIndicatorAttached(chartWin.ActiveChartControl, isProfile);
										chartWin.ActiveChartControl.InvalidateVisual();
									}
									if (chartWin.Form != null)
										chartWin.Form.Invalidate();
								}
								catch { }
							};
							postTimer.Start();
						}
					}
				}
				catch { }
				finally
				{
					isUpdatingInstrumentProgrammatically = false;
				}
			};

			try
			{
				if (chartWin.Dispatcher != null && !chartWin.Dispatcher.CheckAccess())
					chartWin.Dispatcher.Invoke(updateAct);
				else
					updateAct();
			}
			catch { }

			return success;
		}

		public static void ActivateScannerWindow()
		{
			try
			{
				var allWins = NinjaTrader.Core.Globals.AllWindows;
				if (allWins != null)
				{
					for (int i = 0; i < allWins.Count; i++)
					{
						Window win = null;
						try { win = allWins[i]; } catch { }
						if (win != null && win.GetType().Name == "MarketProfileScannerWindow")
						{
							win.Dispatcher.BeginInvoke(new Action(() =>
							{
								try
								{
									if (win.WindowState == WindowState.Minimized)
										win.WindowState = WindowState.Maximized;
									win.Activate();
									win.Focus();
								}
								catch { }
							}));
							return;
						}
					}
				}
				// If not open yet, open a new Scanner window
				NinjaTrader.Core.Globals.RandomDispatcher.BeginInvoke(new Action(() =>
				{
					try
					{
						var scanner = new NinjaTrader.NinjaScript.Indicators.MarketProfileScannerWindow();
						scanner.WindowState = WindowState.Maximized;
						scanner.Show();
						scanner.Activate();
					}
					catch { }
				}));
			}
			catch { }
		}

		public void LoadSymbolPairOnWindow(string symbol, bool forceReload = false)
		{
			if (string.IsNullOrWhiteSpace(symbol)) symbol = "NIFTY_I";
			string clean = symbol.Trim();
			if (clean.StartsWith("^")) clean = clean.Substring(1).Trim();
			if (clean.Equals("NIFTY", StringComparison.OrdinalIgnoreCase)) clean = "NIFTY_I";
			if (clean.Equals("BANKNIFTY", StringComparison.OrdinalIgnoreCase)) clean = "BANKNIFTY_I";

			Action act = () =>
			{
				try
				{
					EnsureD2DFactoryForCurrentThread();
					Instance = this;
					stackerDispatcher = this.Dispatcher;

					// Fill 100% of screen work area in WindowState.Normal so child Direct2DForms never lose coordinates
					MaximizeStackerToWorkArea();
					try { UpdateLayout(); } catch { }

					IntPtr sHwnd = new WindowInteropHelper(this).EnsureHandle();
					if (sHwnd != IntPtr.Zero)
					{
						StackerWin32.ShowWindow(sHwnd, StackerWin32.SW_SHOWNORMAL);
						StackerWin32.SetForegroundWindow(sHwnd);
						StackerWin32.BringWindowToTop(sHwnd);
					}

					Activate();
					Focus();

					if (cmbSymbol != null)
					{
						isSuppressSymbolSelectionChanged = true;
						if (!cmbSymbol.Items.Contains(clean))
							cmbSymbol.Items.Insert(0, clean);
						cmbSymbol.SelectedItem = clean;
						cmbSymbol.Text = clean;
						isSuppressSymbolSelectionChanged = false;
					}

					// Deduplicate rapid single+double click bursts (even while initial launch is still in progress!)
					bool pane1Alive = dockedChart1Instance != null && dockedChart1Hwnd != IntPtr.Zero && StackerWin32.IsWindow(dockedChart1Hwnd);
					bool pane2Alive = dockedChart2Instance != null && dockedChart2Hwnd != IntPtr.Zero && StackerWin32.IsWindow(dockedChart2Hwnd);
					bool anyLaunching = isCreatingPane1 || isCreatingPane2;

					if (!forceReload &&
						string.Equals(currentLoadedSymbol, clean, StringComparison.OrdinalIgnoreCase) &&
						((pane1Alive && pane2Alive) || anyLaunching) &&
						(DateTime.Now - lastSymbolLoadTimestamp).TotalMilliseconds < 1500)
					{
						SyncBothPanes();
						RefreshDockedChartVisuals(0, false, false);
						StartReadinessSync(clean);
						return;
					}

					currentLoadedSymbol = clean;
					lastSymbolLoadTimestamp = DateTime.Now;

					dockedChart1Title = clean + " (1-Min Market Profile)";
					dockedChart2Title = clean + " (5-Min Orderflow)";
					if (txtPane1Title != null) txtPane1Title.Text = "SCREEN 1: " + dockedChart1Title;
					if (txtPane2Title != null) txtPane2Title.Text = "SCREEN 2: " + dockedChart2Title;
					UpdateWindowTitle();
					StartReadinessSync(clean);

					Instrument inst = ResolveBestInstrument(clean);

					// Pane 1: Update in-place if alive, queue if currently creating, or launch once
					if (!forceReload && pane1Alive)
					{
						if (inst != null && TryUpdateChartInstrumentInPlace(dockedChart1Instance, inst, 1))
							pendingSymbolPane1 = null;
						else
							pendingSymbolPane1 = clean;
					}
					else if (!forceReload && isCreatingPane1)
					{
						pendingSymbolPane1 = clean;
					}
					else
					{
						isCreatingPane1 = false;
						LaunchChartForPane(1, clean);
					}

					// Pane 2: Update in-place if alive, queue if currently creating, or launch once
					if (!forceReload && pane2Alive)
					{
						if (inst != null && TryUpdateChartInstrumentInPlace(dockedChart2Instance, inst, 5))
							pendingSymbolPane2 = null;
						else
							pendingSymbolPane2 = clean;
					}
					else if (!forceReload && isCreatingPane2)
					{
						pendingSymbolPane2 = clean;
					}
					else
					{
						isCreatingPane2 = false;
						LaunchChartForPane(2, clean);
					}

					ReleaseHeaderControlFocus();
					SyncBothPanes();
					RefreshDockedChartVisuals(0, true, true);
				}
				catch (Exception ex)
				{
					NinjaTrader.Code.Output.Process("AK10X LoadSymbolPairOnWindow Error: " + ex.Message, PrintTo.OutputTab1);
				}
			};

			if (!Dispatcher.CheckAccess())
				Dispatcher.BeginInvoke(act);
			else
				act();
		}

		/// <summary>
		/// Common unified chart loader:
		/// Chart 1: 1-Min Timeframe with INDMarketProfile50Charts attached.
		/// Chart 2: 5-Min Timeframe with AK10XOrderflow attached.
		/// Docks both seamlessly into Screen 1 and Screen 2 of the 2-Screen Stacker in Maximized view.
		/// </summary>
		public static void LoadUnifiedInstrumentPair(string symbol)
		{
			Action openAct = () =>
			{
				try
				{
					AK10XChartStackerWindow win = FindOrCreateActiveInstance();
					if (win != null)
					{
						win.LoadSymbolPairOnWindow(symbol, false);
					}
				}
				catch (Exception ex)
				{
					NinjaTrader.Code.Output.Process("AK10X LoadInstrumentPair Error: " + ex.Message, PrintTo.OutputTab1);
				}
			};

			if (AK10XChartStackerWindow.StackerDispatcher != null && !AK10XChartStackerWindow.StackerDispatcher.CheckAccess())
				AK10XChartStackerWindow.StackerDispatcher.BeginInvoke(openAct);
			else
				openAct();
		}

		private void CloseSingleDockedChart(int paneIndex)
		{
			try
			{
				if (paneIndex == 1)
				{
					DetachChildChartHook(1);
					var s1 = stagedChart1;
					stagedChart1 = null;
					if (s1 != null) { try { s1.Close(); } catch { } }
					IntPtr h1 = dockedChart1Hwnd;
					var c1 = dockedChart1Instance;
					dockedChart1Hwnd = IntPtr.Zero;
					dockedChart1Instance = null;
					lastX1 = -99999; lastY1 = -99999; lastW1 = -99999; lastH1 = -99999;

					if (h1 != IntPtr.Zero && StackerWin32.IsWindow(h1))
					{
						StackerWin32.ShowWindow(h1, StackerWin32.SW_HIDE);
						StackerWin32.SetParent(h1, IntPtr.Zero);
					}
					if (c1 != null)
					{
						try { c1.Close(); } catch { }
					}
					else if (h1 != IntPtr.Zero && StackerWin32.IsWindow(h1))
					{
						StackerWin32.PostMessage(h1, StackerWin32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
					}
				}
				else if (paneIndex == 2)
				{
					DetachChildChartHook(2);
					var s2 = stagedChart2;
					stagedChart2 = null;
					if (s2 != null) { try { s2.Close(); } catch { } }
					IntPtr h2 = dockedChart2Hwnd;
					var c2 = dockedChart2Instance;
					dockedChart2Hwnd = IntPtr.Zero;
					dockedChart2Instance = null;
					lastX2 = -99999; lastY2 = -99999; lastW2 = -99999; lastH2 = -99999;

					if (h2 != IntPtr.Zero && StackerWin32.IsWindow(h2))
					{
						StackerWin32.ShowWindow(h2, StackerWin32.SW_HIDE);
						StackerWin32.SetParent(h2, IntPtr.Zero);
					}
					if (c2 != null)
					{
						try { c2.Close(); } catch { }
					}
					else if (h2 != IntPtr.Zero && StackerWin32.IsWindow(h2))
					{
						StackerWin32.PostMessage(h2, StackerWin32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
					}
				}
			}
			catch { }
		}

		public void CloseAllDockedCharts()
		{
			CloseSingleDockedChart(1);
			CloseSingleDockedChart(2);
			dockedChart1Title = null;
			dockedChart2Title = null;
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			isWindowClosed = true;
			try { if (readinessTimer != null) readinessTimer.Stop(); } catch { }
			try { Connection.ConnectionStatusUpdate -= OnStackerConnectionStatusUpdate; } catch { }
			// Close both docked charts cleanly when AK10X 2-Screen Stacker closes (Item 3)
			CloseAllDockedCharts();
			if (Instance == this) Instance = null;
			base.OnClosing(e);
		}
		#endregion

		#region Helper UI Builders
		private static Border CreateBadge(string text, Color fgColor, Color bgColor)
		{
			Border b = new Border
			{
				Background = new SolidColorBrush(bgColor),
				BorderBrush = new SolidColorBrush(fgColor),
				BorderThickness = new Thickness(1),
				CornerRadius = new CornerRadius(3),
				Padding = new Thickness(6, 2, 6, 2),
				VerticalAlignment = VerticalAlignment.Center
			};
			b.Child = new TextBlock
			{
				Text = text,
				FontSize = 10,
				FontWeight = FontWeights.SemiBold,
				Foreground = new SolidColorBrush(fgColor)
			};
			return b;
		}

		private static Button CreateHeaderButton(string text, RoutedEventHandler onClick)
		{
			Brush normalBg = new SolidColorBrush(Color.FromRgb(28, 34, 46));
			Brush hoverBg = new SolidColorBrush(Color.FromRgb(42, 52, 72));
			Brush normalBorder = new SolidColorBrush(Color.FromRgb(48, 56, 76));
			Brush hoverBorder = new SolidColorBrush(Color.FromRgb(88, 130, 190));

			Button btn = new Button
			{
				Content = text,
				FontSize = 11,
				Padding = new Thickness(8, 3, 8, 3),
				Margin = new Thickness(0, 0, 6, 0),
				Background = normalBg,
				Foreground = Brushes.White,
				BorderBrush = normalBorder,
				BorderThickness = new Thickness(1),
				Cursor = Cursors.Hand,
				Focusable = false,
				FocusVisualStyle = null
			};
			btn.MouseEnter += (s, e) =>
			{
				if (btn.Content is string str && str.IndexOf("ACCELPIX", StringComparison.OrdinalIgnoreCase) >= 0) return;
				btn.Background = hoverBg;
				btn.BorderBrush = hoverBorder;
			};
			btn.MouseLeave += (s, e) =>
			{
				if (btn.Content is string str && str.IndexOf("ACCELPIX", StringComparison.OrdinalIgnoreCase) >= 0) return;
				btn.Background = normalBg;
				btn.BorderBrush = normalBorder;
			};
			btn.Click += (s, e) =>
			{
				if (!(btn.Content is string str && str.IndexOf("ACCELPIX", StringComparison.OrdinalIgnoreCase) >= 0))
				{
					btn.Background = normalBg;
					btn.BorderBrush = normalBorder;
				}
				onClick?.Invoke(s, e);
			};
			return btn;
		}

		private static Button CreateAccentButton(string text, RoutedEventHandler onClick)
		{
			Brush normalBg = new SolidColorBrush(Color.FromRgb(255, 179, 0));
			Brush hoverBg = new SolidColorBrush(Color.FromRgb(255, 202, 40));

			Button btn = new Button
			{
				Content = text,
				FontSize = 11,
				FontWeight = FontWeights.Bold,
				Padding = new Thickness(10, 4, 10, 4),
				Margin = new Thickness(0, 0, 6, 0),
				Background = normalBg,
				Foreground = Brushes.Black,
				BorderThickness = new Thickness(0),
				Cursor = Cursors.Hand,
				Focusable = false,
				FocusVisualStyle = null
			};
			btn.MouseEnter += (s, e) => btn.Background = hoverBg;
			btn.MouseLeave += (s, e) => btn.Background = normalBg;
			btn.Click += (s, e) =>
			{
				btn.Background = normalBg;
				onClick?.Invoke(s, e);
			};
			return btn;
		}

		private static Button CreatePaneButton(string text, RoutedEventHandler onClick)
		{
			Brush normalBg = new SolidColorBrush(Color.FromRgb(28, 34, 46));
			Brush hoverBg = new SolidColorBrush(Color.FromRgb(42, 52, 72));

			Button btn = new Button
			{
				Content = text,
				FontSize = 10,
				Padding = new Thickness(6, 2, 6, 2),
				Margin = new Thickness(0, 0, 4, 0),
				Background = normalBg,
				Foreground = new SolidColorBrush(Color.FromRgb(200, 210, 228)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(44, 52, 70)),
				BorderThickness = new Thickness(1),
				Cursor = Cursors.Hand,
				Focusable = false,
				FocusVisualStyle = null
			};
			btn.MouseEnter += (s, e) => btn.Background = hoverBg;
			btn.MouseLeave += (s, e) => btn.Background = normalBg;
			btn.Click += (s, e) =>
			{
				btn.Background = normalBg;
				onClick?.Invoke(s, e);
			};
			return btn;
		}
		#endregion

		public class ChartItem
		{
			public IntPtr Hwnd { get; set; }
			public string Title { get; set; }
			public NinjaTrader.Gui.Chart.Chart Instance { get; set; }
		}

		public static void OpenFromControlCenterMenu()
		{
			Action openAction = () =>
			{
				try
				{
					AK10XChartStackerWindow win = FindOrCreateActiveInstance();
					if (win != null)
					{
						win.MaximizeStackerToWorkArea();
						win.Activate();
						win.Focus();
						if (win.dockedChart1Instance == null && win.dockedChart2Instance == null && !win.isCreatingPane1 && !win.isCreatingPane2)
						{
							win.LoadSymbolPairOnWindow(win.cmbSymbol != null && !string.IsNullOrWhiteSpace(win.cmbSymbol.Text) ? win.cmbSymbol.Text.Trim() : "NIFTY_I", false);
						}
						else
						{
							win.ForceLayoutAndSyncPanes();
							win.RefreshDockedChartVisuals(0, true, true);
						}
					}
				}
				catch (Exception ex)
				{
					NinjaTrader.Code.Output.Process("Error opening AK10X Stacker: " + ex.Message, PrintTo.OutputTab1);
				}
			};

			if (StackerDispatcher != null && !StackerDispatcher.CheckAccess())
				StackerDispatcher.BeginInvoke(openAction);
			else
				openAction();
		}
	}
	#endregion

	#region AddOn Integration (Control Center -> New -> AK10X 2-Screen Stacker)
	public class AK10XWindowStackerAddOn : AddOnBase
	{
		private NTMenuItem menuItem;
		private NTMenuItem existingMenu;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "AK10X Trading Screen Add-On for NinjaTrader 8";
				Name = "AK10XWindowStacker";
			}
		}

		protected override void OnWindowCreated(Window window)
		{
			ControlCenter cc = window as ControlCenter;
			if (cc == null) return;

			existingMenu = cc.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
			if (existingMenu == null) return;

			Style itemStyle = null;
			try
			{
				if (Application.Current != null && Application.Current.Dispatcher.CheckAccess())
					itemStyle = Application.Current.TryFindResource("MainMenuItem") as Style;
			}
			catch { }

			menuItem = new NTMenuItem
			{
				Header = "AK10X Trading Screen",
				Style = itemStyle
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
			AK10XChartStackerWindow.OpenFromControlCenterMenu();
		}
	}
	#endregion
}