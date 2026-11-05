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
		public const int WM_NCACTIVATE = 0x0086;
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
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool BringWindowToTop(IntPtr hWnd);

		[DllImport("user32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);
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
		private ComboBox cmbPane1Charts;
		private ComboBox cmbPane2Charts;

		private IntPtr stackerHwnd = IntPtr.Zero;

		private IntPtr dockedChart1Hwnd = IntPtr.Zero;
		private string dockedChart1Title = null;
		public NinjaTrader.Gui.Chart.Chart dockedChart1Instance = null;

		private IntPtr dockedChart2Hwnd = IntPtr.Zero;
		private string dockedChart2Title = null;
		public NinjaTrader.Gui.Chart.Chart dockedChart2Instance = null;

		private bool isStackedLayout = false;
		private bool isPane1Maximized = false;
		private bool isPane2Maximized = false;
		private Button btnMaxPane1;
		private Button btnMaxPane2;
		private bool isSuppressSymbolSelectionChanged = false;

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

		private void SyncD2DForm(int paneIndex, bool visible)
		{
			try
			{
				var chart = paneIndex == 1 ? dockedChart1Instance : dockedChart2Instance;
				if (chart == null || chart.Form == null || !chart.Form.IsHandleCreated)
					return;

				IntPtr d2dHwnd = chart.Form.Handle;
				if (d2dHwnd == IntPtr.Zero)
					return;

				if (!visible)
				{
					StackerWin32.ShowWindow(d2dHwnd, StackerWin32.SW_HIDE);
					return;
				}

				if (stackerHwnd == IntPtr.Zero)
					stackerHwnd = new WindowInteropHelper(this).Handle;

				// CRITICAL Z-ORDER FIX: Make stackerHwnd the Win32 Owner (GWLP_HWNDPARENT) of Direct2DForm!
				// When chartHwnd is converted to WS_CHILD, it can no longer act as a top-level owner for Direct2DForm.
				// Setting GWLP_HWNDPARENT to stackerHwnd guarantees Windows ALWAYS keeps Direct2DForm above stackerHwnd
				// even when the user clicks the chart canvas, bottom scrollbar, toolbar, or Stacker header!
				if (stackerHwnd != IntPtr.Zero)
				{
					IntPtr curOwner = StackerWin32.GetWindow(d2dHwnd, StackerWin32.GW_OWNER);
					if (curOwner != stackerHwnd)
					{
						StackerWin32.SetWindowLongPtr(d2dHwnd, StackerWin32.GWLP_HWNDPARENT, stackerHwnd);
					}
				}

				if (!StackerWin32.IsWindowVisible(d2dHwnd))
					StackerWin32.ShowWindow(d2dHwnd, StackerWin32.SW_SHOWNOACTIVATE);

				StackerWin32.SetWindowPos(d2dHwnd, IntPtr.Zero, 0, 0, 0, 0,
					StackerWin32.SWP_NOMOVE | StackerWin32.SWP_NOSIZE | StackerWin32.SWP_NOACTIVATE | StackerWin32.SWP_SHOWWINDOW);

				var moveResize = chart.Form.GetType().GetMethod("MoveResizeForm", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
				if (moveResize != null)
					moveResize.Invoke(chart.Form, new object[] { true, true });

				if (chart.ActiveChartControl != null)
					chart.ActiveChartControl.InvalidateVisual();
			}
			catch { }
		}

		private void RefreshDockedChartVisuals(int paneIndex = 0)
		{
			try
			{
				if (paneIndex == 0 || paneIndex == 1)
					SyncD2DForm(1, !isPane2Maximized && WindowState != WindowState.Minimized);
				if (paneIndex == 0 || paneIndex == 2)
					SyncD2DForm(2, !isPane1Maximized && WindowState != WindowState.Minimized);
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
				RefreshDockedChartVisuals();

				Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
				{
					try
					{
						InvalidatePaneBoundsCache();
						UpdateLayout();
						if (mainSplitGrid != null) mainSplitGrid.UpdateLayout();
						SyncBothPanes();
						RefreshDockedChartVisuals();
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

			Caption = "AK10X 2-Screen Stacker | Screen 1: Market Profile | Screen 2: 5-Min Candles";
			Title = "AK10X 2-Screen Stacker | Screen 1: Market Profile | Screen 2: 5-Min Candles";
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

			SourceInitialized += (s, e) =>
			{
				try
				{
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
					RefreshDockedChartVisuals();
				}
				catch { }
			};

			PreviewMouseDown += (s, e) =>
			{
				try
				{
					Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => RefreshDockedChartVisuals()));
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
					RefreshDockedChartVisuals();
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
		}

		private IntPtr OnStackerWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
		{
			if (msg == StackerWin32.WM_PARENTNOTIFY ||
				msg == StackerWin32.WM_MOUSEACTIVATE ||
				(msg == StackerWin32.WM_ACTIVATE && wParam != IntPtr.Zero) ||
				(msg == StackerWin32.WM_NCACTIVATE && wParam != IntPtr.Zero))
			{
				try
				{
					RefreshDockedChartVisuals();
					Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
					{
						try { RefreshDockedChartVisuals(); } catch { }
					}));
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

			// Brand & Badges
			StackPanel brandPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			TextBlock txtBrand = new TextBlock
			{
				Text = "⚡ AK10X 2-SCREEN STACKER",
				FontWeight = FontWeights.Bold,
				FontSize = 14,
				Foreground = new SolidColorBrush(Color.FromRgb(255, 179, 0)), // Gold
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 12, 0)
			};
			Border badgeProfile = CreateBadge("SCREEN 1: MARKET PROFILE (1M)", Color.FromRgb(0, 229, 255), Color.FromRgb(6, 36, 46));
			Border badgeCandles = CreateBadge("SCREEN 2: 5-MIN CANDLES", Color.FromRgb(0, 230, 118), Color.FromRgb(8, 42, 22));
			badgeCandles.Margin = new Thickness(8, 0, 0, 0);

			brandPanel.Children.Add(txtBrand);
			brandPanel.Children.Add(badgeProfile);
			brandPanel.Children.Add(badgeCandles);
			Grid.SetColumn(brandPanel, 0);
			headerGrid.Children.Add(brandPanel);

			// Middle Controls: Active Symbol Selector + Split presets + Layout toggle
			StackPanel layoutPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };

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
				Width = 140,
				Height = 24,
				IsEditable = true,
				FontSize = 11,
				FontWeight = FontWeights.SemiBold,
				Background = new SolidColorBrush(Color.FromRgb(16, 20, 28)),
				Foreground = Brushes.White,
				Margin = new Thickness(0, 0, 12, 0)
			};
			string[] defaultSyms = new string[] {
				"NIFTY_I", "BANKNIFTY_I", "FINNIFTY_I", "RELIANCE_I", "HDFCBANK_I", "AXISBANK_I",
				"BRITANNIA_I", "ICICIBANK_I", "INFY_I", "SBIN_I", "TCS_I", "TATAMOTORS_I",
				"TATASTEEL_I", "LT_I", "MARUTI_I", "SUNPHARMA_I", "BAJFINANCE_I", "BHARTIARTL_I",
				"ADANIPORTS_I", "ASIANPAINT_I", "BAJAJ-AUTO_I", "BAJAJFINSV_I", "BPCL_I", "CIPLA_I",
				"COALINDIA_I", "DIVISLAB_I", "DRREDDY_I", "EICHERMOT_I", "GAIL_I", "GRASIM_I",
				"HCLTECH_I", "HDFCLIFE_I", "HEROMOTOCO_I", "HINDALCO_I", "HINDUNILVR_I", "INDUSINDBK_I",
				"IOC_I", "ITC_I", "JSWSTEEL_I", "KOTAKBANK_I", "M&M_I", "NESTLEIND_I", "NTPC_I",
				"ONGC_I", "POWERGRID_I", "SBILIFE_I", "TECHM_I", "TITAN_I", "ULTRACEMCO_I", "WIPRO_I"
			};
			foreach (string s in defaultSyms)
				cmbSymbol.Items.Add(s);
			isSuppressSymbolSelectionChanged = true;
			cmbSymbol.SelectedIndex = 0;
			isSuppressSymbolSelectionChanged = false;

			cmbSymbol.SelectionChanged += (s, e) =>
			{
				if (isSuppressSymbolSelectionChanged) return;
				if (cmbSymbol.SelectedItem is string selectedSym && !string.IsNullOrWhiteSpace(selectedSym))
				{
					LoadUnifiedInstrumentPair(selectedSym);
				}
			};
			cmbSymbol.KeyDown += (s, e) =>
			{
				if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(cmbSymbol.Text))
				{
					LoadUnifiedInstrumentPair(cmbSymbol.Text.Trim());
					e.Handled = true;
				}
			};

			TextBlock lblSplit = new TextBlock
			{
				Text = "Split:",
				Foreground = new SolidColorBrush(Color.FromRgb(140, 150, 170)),
				FontSize = 11,
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0, 0, 6, 0)
			};
			Button btn5050 = CreateHeaderButton("50 / 50", (s, e) => ApplySplit(0.5, 0.5));
			Button btn6040 = CreateHeaderButton("60 / 40", (s, e) => ApplySplit(0.6, 0.4));
			Button btn7030 = CreateHeaderButton("70 / 30", (s, e) => ApplySplit(0.7, 0.3));
			Button btnToggleLayout = CreateHeaderButton("Layout ⊞", (s, e) => ToggleLayout());
			Button btnSwap = CreateHeaderButton("Swap Panes ⇄", (s, e) => SwapPanes());

			layoutPanel.Children.Add(lblSymbol);
			layoutPanel.Children.Add(cmbSymbol);
			layoutPanel.Children.Add(lblSplit);
			layoutPanel.Children.Add(btn5050);
			layoutPanel.Children.Add(btn6040);
			layoutPanel.Children.Add(btn7030);
			layoutPanel.Children.Add(btnToggleLayout);
			layoutPanel.Children.Add(btnSwap);
			Grid.SetColumn(layoutPanel, 2);
			headerGrid.Children.Add(layoutPanel);

			// Right Actions: Launch 2-Screen, Scanner Switcher, Auto-Dock & Undock All
			StackPanel actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
			Button btnAutoLaunch = CreateAccentButton("⚡ Launch 2-Screen", (s, e) => LoadUnifiedInstrumentPair(GetSelectedSymbol()));
			Button btnScanner = CreateHeaderButton("📊 Scanner", (s, e) => ActivateScannerWindow());
			btnScanner.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
			btnScanner.FontWeight = FontWeights.Bold;
			Button btnAutoDock = CreateHeaderButton("Auto-Dock Open", (s, e) => AutoDockOpenCharts());
			Button btnUndockAll = CreateHeaderButton("Undock All ↗", (s, e) => UndockAll());

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
				Text = paneIndex == 1 ? "SCREEN 1: MARKET PROFILE (1M TPO)" : "SCREEN 2: 5-MIN CANDLES",
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
				Margin = new Thickness(0, 0, 6, 0)
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
			StackPanel p = new StackPanel
			{
				VerticalAlignment = VerticalAlignment.Center,
				HorizontalAlignment = HorizontalAlignment.Center
			};

			TextBlock icon = new TextBlock
			{
				Text = paneIndex == 1 ? "📊" : "🕯️",
				FontSize = 36,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0, 0, 0, 8)
			};
			TextBlock title = new TextBlock
			{
				Text = paneIndex == 1 ? "Screen 1: Market Profile" : "Screen 2: 5-Minute Candlestick",
				FontWeight = FontWeights.Bold,
				FontSize = 16,
				Foreground = Brushes.White,
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0, 0, 0, 4)
			};
			TextBlock desc = new TextBlock
			{
				Text = paneIndex == 1
					? "Dalton Market Profile (1-Min timeframe, 70% Value Area, Initial Balance, POC)"
					: "Fast 5-Min Price Action & Order Execution (Integrated Chart Trader)",
				FontSize = 12,
				Foreground = new SolidColorBrush(Color.FromRgb(140, 150, 170)),
				HorizontalAlignment = HorizontalAlignment.Center,
				Margin = new Thickness(0, 0, 0, 14)
			};

			Button btnLaunch = CreateAccentButton(
				paneIndex == 1 ? "+ Open Market Profile Chart" : "+ Open 5-Min Candles Chart",
				(s, e) => LaunchChartForPane(paneIndex));
			btnLaunch.HorizontalAlignment = HorizontalAlignment.Center;

			p.Children.Add(icon);
			p.Children.Add(title);
			p.Children.Add(desc);
			p.Children.Add(btnLaunch);
			return p;
		}

		#region Native Win32 Child Window Embedding & Docking Mechanics
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
								RefreshDockedChartVisuals(targetPane);
							}
							catch { }
						};
						chartInstance.Activated += (s, ev) =>
						{
							try
							{
								RefreshDockedChartVisuals();
							}
							catch { }
						};
						chartInstance.PreviewMouseDown += (s, ev) =>
						{
							try
							{
								RefreshDockedChartVisuals();
								Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => RefreshDockedChartVisuals()));
							}
							catch { }
						};
						chartInstance.PreviewMouseUp += (s, ev) =>
						{
							try
							{
								RefreshDockedChartVisuals();
								Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => RefreshDockedChartVisuals()));
							}
							catch { }
						};
					}
					catch { }
				}

				// 1. Strip top-level borders/caption and set WS_CHILD | WS_CLIPCHILDREN | WS_CLIPSIBLINGS
				int style = StackerWin32.GetWindowLong(hwnd, StackerWin32.GWL_STYLE);
				style &= ~unchecked((int)(StackerWin32.WS_POPUP | StackerWin32.WS_CAPTION | StackerWin32.WS_THICKFRAME | StackerWin32.WS_MINIMIZEBOX | StackerWin32.WS_MAXIMIZEBOX | StackerWin32.WS_SYSMENU));
				style |= unchecked((int)(StackerWin32.WS_CHILD | StackerWin32.WS_CLIPCHILDREN | StackerWin32.WS_CLIPSIBLINGS));
				StackerWin32.SetWindowLong(hwnd, StackerWin32.GWL_STYLE, style);

				// 2. Make it a true Win32 child window of stackerHwnd! (NEVER floats over Scanner or desktop!)
				StackerWin32.SetParent(hwnd, stackerHwnd);

				StackerWin32.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
					StackerWin32.SWP_NOMOVE | StackerWin32.SWP_NOSIZE | StackerWin32.SWP_NOZORDER | StackerWin32.SWP_FRAMECHANGED);

				if (paneIndex == 1)
				{
					if (dockedChart1Hwnd != IntPtr.Zero && dockedChart1Hwnd != hwnd)
						UndockPane(1);

					dockedChart1Hwnd = hwnd;
					dockedChart1Title = title;
					dockedChart1Instance = chartInstance;
					lastX1 = -99999; lastY1 = -99999; lastW1 = -99999; lastH1 = -99999;

					txtPane1Title.Text = "SCREEN 1: " + title;
					placeholderPane1.Visibility = Visibility.Collapsed;
					SyncPaneWindow(1);
				}
				else
				{
					if (dockedChart2Hwnd != IntPtr.Zero && dockedChart2Hwnd != hwnd)
						UndockPane(2);

					dockedChart2Hwnd = hwnd;
					dockedChart2Title = title;
					dockedChart2Instance = chartInstance;
					lastX2 = -99999; lastY2 = -99999; lastW2 = -99999; lastH2 = -99999;

					txtPane2Title.Text = "SCREEN 2: " + title;
					placeholderPane2.Visibility = Visibility.Collapsed;
					SyncPaneWindow(2);
				}

				UpdateWindowTitle();
				RefreshAvailableCharts();

				// Follow-up sync pass after NTWindow post-load events settle
				int targetIdx = paneIndex;
				var settleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
				int settleTicks = 0;
				settleTimer.Tick += (s, ev) =>
				{
					settleTicks++;
					if (targetIdx == 1) { lastX1 = -99999; lastY1 = -99999; lastW1 = -99999; lastH1 = -99999; }
					else { lastX2 = -99999; lastY2 = -99999; lastW2 = -99999; lastH2 = -99999; }
					SyncPaneWindow(targetIdx);
					if (settleTicks >= 3)
						settleTimer.Stop();
				};
				settleTimer.Start();
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

					if (c1 != null && c1.Form != null && c1.Form.IsHandleCreated)
						StackerWin32.SetWindowLongPtr(c1.Form.Handle, StackerWin32.GWLP_HWNDPARENT, hwnd);

					txtPane1Title.Text = "SCREEN 1: MARKET PROFILE (1M TPO)";
					placeholderPane1.Visibility = Visibility.Visible;
				}
				else if (paneIndex == 2 && dockedChart2Hwnd != IntPtr.Zero)
				{
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

					if (c2 != null && c2.Form != null && c2.Form.IsHandleCreated)
						StackerWin32.SetWindowLongPtr(c2.Form.Handle, StackerWin32.GWLP_HWNDPARENT, hwnd);

					txtPane2Title.Text = "SCREEN 2: 5-MIN CANDLES";
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

		private void SwapPanes()
		{
			IntPtr h1 = dockedChart1Hwnd;
			string t1 = dockedChart1Title;
			var i1 = dockedChart1Instance;

			IntPtr h2 = dockedChart2Hwnd;
			string t2 = dockedChart2Title;
			var i2 = dockedChart2Instance;

			UndockAll();

			if (h2 != IntPtr.Zero) DockChartByHwnd(h2, t2, 1, i2);
			if (h1 != IntPtr.Zero) DockChartByHwnd(h1, t1, 2, i1);
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

				// CRITICAL ANTI-SHAKING & QUOTA PROTECTION CHECK:
				if (paneIndex == 1)
				{
					if (x == lastX1 && y == lastY1 && w == lastW1 && h == lastH1)
					{
						SyncD2DForm(1, true);
						return;
					}
					lastX1 = x; lastY1 = y; lastW1 = w; lastH1 = h;
				}
				else
				{
					if (x == lastX2 && y == lastY2 && w == lastW2 && h == lastH2)
					{
						SyncD2DForm(2, true);
						return;
					}
					lastX2 = x; lastY2 = y; lastW2 = w; lastH2 = h;
				}

				StackerWin32.MoveWindow(hwnd, x, y, w, h, true);
				StackerWin32.ShowWindow(hwnd, StackerWin32.SW_SHOW);
				SyncD2DForm(paneIndex, true);
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
					ApplySplit(0.5, 0.5);
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

					if (!isStackedLayout)
					{
						colPane1.MinWidth = 0;
						colPane2.MinWidth = 0;
						colSplitter.Width = new GridLength(0);
						colPane1.Width = new GridLength(1, GridUnitType.Star);
						colPane2.Width = new GridLength(0);
						borderPane1.Visibility = Visibility.Visible;
						borderPane2.Visibility = Visibility.Collapsed;
						gridSplitter.Visibility = Visibility.Collapsed;
					}
					else
					{
						rowPane1.MinHeight = 0;
						rowPane2.MinHeight = 0;
						rowSplitter.Height = new GridLength(0);
						rowPane1.Height = new GridLength(1, GridUnitType.Star);
						rowPane2.Height = new GridLength(0);
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
					ApplySplit(0.5, 0.5);
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

					if (!isStackedLayout)
					{
						colPane1.MinWidth = 0;
						colPane2.MinWidth = 0;
						colSplitter.Width = new GridLength(0);
						colPane1.Width = new GridLength(0);
						colPane2.Width = new GridLength(1, GridUnitType.Star);
						borderPane1.Visibility = Visibility.Collapsed;
						borderPane2.Visibility = Visibility.Visible;
						gridSplitter.Visibility = Visibility.Collapsed;
					}
					else
					{
						rowPane1.MinHeight = 0;
						rowPane2.MinHeight = 0;
						rowSplitter.Height = new GridLength(0);
						rowPane1.Height = new GridLength(0);
						rowPane2.Height = new GridLength(1, GridUnitType.Star);
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

			mainSplitGrid.ColumnDefinitions.Clear();
			mainSplitGrid.RowDefinitions.Clear();
			mainSplitGrid.Children.Clear();

			if (!isStackedLayout)
			{
				colPane1 = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 };
				colSplitter = new ColumnDefinition { Width = new GridLength(6) };
				colPane2 = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 200 };

				mainSplitGrid.ColumnDefinitions.Add(colPane1);
				mainSplitGrid.ColumnDefinitions.Add(colSplitter);
				mainSplitGrid.ColumnDefinitions.Add(colPane2);

				gridSplitter.Width = 6;
				gridSplitter.Height = double.NaN;
				gridSplitter.Cursor = Cursors.SizeWE;
				gridSplitter.HorizontalAlignment = HorizontalAlignment.Center;
				gridSplitter.VerticalAlignment = VerticalAlignment.Stretch;

				Grid.SetColumn(borderPane1, 0);
				Grid.SetRow(borderPane1, 0);

				Grid.SetColumn(gridSplitter, 1);
				Grid.SetRow(gridSplitter, 0);

				Grid.SetColumn(borderPane2, 2);
				Grid.SetRow(borderPane2, 0);
			}
			else
			{
				rowPane1 = new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 150 };
				rowSplitter = new RowDefinition { Height = new GridLength(6) };
				rowPane2 = new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 150 };

				mainSplitGrid.RowDefinitions.Add(rowPane1);
				mainSplitGrid.RowDefinitions.Add(rowSplitter);
				mainSplitGrid.RowDefinitions.Add(rowPane2);

				gridSplitter.Width = double.NaN;
				gridSplitter.Height = 6;
				gridSplitter.Cursor = Cursors.SizeNS;
				gridSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
				gridSplitter.VerticalAlignment = VerticalAlignment.Center;

				Grid.SetColumn(borderPane1, 0);
				Grid.SetRow(borderPane1, 0);

				Grid.SetColumn(gridSplitter, 0);
				Grid.SetRow(gridSplitter, 1);

				Grid.SetColumn(borderPane2, 0);
				Grid.SetRow(borderPane2, 2);
			}

			mainSplitGrid.Children.Add(borderPane1);
			mainSplitGrid.Children.Add(gridSplitter);
			mainSplitGrid.Children.Add(borderPane2);

			SyncBothPanes();
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

			if (openCharts.Count > 0 && dockedChart1Hwnd == IntPtr.Zero)
			{
				DockChartByHwnd(openCharts[0].Hwnd, openCharts[0].Title, 1, openCharts[0].Instance);
			}
			if (openCharts.Count > 1 && dockedChart2Hwnd == IntPtr.Zero)
			{
				DockChartByHwnd(openCharts[1].Hwnd, openCharts[1].Title, 2, openCharts[1].Instance);
			}
		}

		private void UpdateWindowTitle()
		{
			string p1 = !string.IsNullOrEmpty(dockedChart1Title) ? dockedChart1Title : "[Empty]";
			string p2 = !string.IsNullOrEmpty(dockedChart2Title) ? dockedChart2Title : "[Empty]";
			Caption = string.Format("AK10X 2-Screen Stacker | Screen 1: {0} | Screen 2: {1}", p1, p2);
			Title = Caption;
		}

		private string GetSelectedSymbol()
		{
			if (cmbSymbol != null && !string.IsNullOrWhiteSpace(cmbSymbol.Text))
				return cmbSymbol.Text.Trim();
			return "NIFTY_I";
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

					Instrument inst = Instrument.GetInstrument(clean, false);
					if (inst == null) inst = Instrument.GetInstrument(clean, true);
					if (inst == null) inst = Instrument.GetInstrument(clean);
					if (inst == null) inst = Instrument.GetInstrumentFuzzy(clean);
					if (inst == null && clean.EndsWith("_I", StringComparison.OrdinalIgnoreCase))
						inst = Instrument.GetInstrument(clean.Substring(0, clean.Length - 2));

					if (inst != null)
						return inst;
				}

				string[] candidates = new string[] { "NIFTY_I", "BANKNIFTY_I", "FINNIFTY_I", "RELIANCE_I", "HDFCBANK_I", "GC 12-26", "NQ 12-26", "MNQ 12-26", "ES 12-26" };
				foreach (var c in candidates)
				{
					try
					{
						Instrument inst = Instrument.GetInstrument(c, false);
						if (inst != null)
							return inst;
					}
					catch { }
				}

				if (Instrument.All != null)
				{
					foreach (var it in Instrument.All)
					{
						if (it != null && it.FullName.EndsWith("_I", StringComparison.OrdinalIgnoreCase))
							return it;
					}
					foreach (var it in Instrument.All)
					{
						if (it != null && it.FullName.IndexOf("NIFTY", StringComparison.OrdinalIgnoreCase) >= 0)
							return it;
					}
					foreach (var it in Instrument.All)
					{
						if (it != null)
							return it;
					}
				}
			}
			catch { }

			return null;
		}

		private NinjaTrader.Gui.Chart.Chart CreateConfiguredChart(string instrumentName, int periodMinutes, bool applyMarketProfile)
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
					DaysBack = applyMarketProfile ? 30 : 15,
					BarsBack = 500,
					From = DateTime.Today.AddDays(-35),
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
				}
				catch
				{
					var cs = new NinjaTrader.NinjaScript.ChartStyles.CandleStyle();
					cs.SetState(State.SetDefaults);
					cs.SetState(State.Configure);
					bp.ChartStyle = cs;
				}

				ChartObjectCollection<BarsProperties> col = new ChartObjectCollection<BarsProperties>();
				col.Add(bp);
				NinjaTrader.Gui.Chart.Chart newChart = new NinjaTrader.Gui.Chart.Chart(col);
				newChart.WindowStartupLocation = WindowStartupLocation.Manual;
				newChart.Left = 0;
				newChart.Top = 0;
				newChart.Width = 900;
				newChart.Height = 700;
				newChart.ShowActivated = false;

				// If Screen 1 Market Profile is requested, automatically attach INDMarketProfile50Charts
				if (applyMarketProfile)
				{
					var initTimer = new System.Windows.Threading.DispatcherTimer(
						TimeSpan.FromMilliseconds(80),
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
									if (initTries >= 40)
										initTimer.Stop();
									return;
								}
								bool exists = false;
								foreach (var ind in cc.Indicators)
								{
									if (ind != null && ind.GetType().Name.Contains("MarketProfile"))
									{
										exists = true;
										break;
									}
								}
								if (!exists)
								{
									EnsureD2DFactoryForCurrentThread();
									var mp = new NinjaTrader.NinjaScript.Indicators.INDMarketProfile50Charts();
									mp.SetState(State.SetDefaults);
									mp.ChartControl = cc;
									mp.ChartBars = cc.PrimaryBars;
									mp.ChartPanel = cc.ChartPanels[0];
									mp.Panel = 0;
									cc.Indicators.Add(mp);

									var refreshMethod = typeof(ChartControl).GetMethod("RefreshIndicators", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
									if (refreshMethod != null)
										refreshMethod.Invoke(cc, new object[] { true, true });

									var applyMethod = typeof(ChartControl).GetMethod("ApplyNinjaScripts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
									if (applyMethod != null)
										applyMethod.Invoke(cc, null);

									cc.InvalidateVisual();
									initTimer.Stop();
									return;
								}
								else
								{
									initTimer.Stop();
									return;
								}
							}
						}
						catch { }

						if (initTries >= 40)
							initTimer.Stop();
					};
					initTimer.Start();
				}

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
					string inst = !string.IsNullOrWhiteSpace(preferredSymbol) ? preferredSymbol : GetSelectedSymbol();
					bool isProfile = (paneIndex == 1);
					int period = isProfile ? 1 : 5; // Chart 1 = 1-Min MP, Chart 2 = 5-Min Candles

					NinjaTrader.Gui.Chart.Chart newChart = CreateConfiguredChart(inst, period, isProfile);
					newChart.Show();

					int attempts = 0;
					var timer = new System.Windows.Threading.DispatcherTimer();
					timer.Interval = TimeSpan.FromMilliseconds(50);
					timer.Tick += (s, e) =>
					{
						attempts++;
						try
						{
							IntPtr hwnd = new WindowInteropHelper(newChart).Handle;
							if (hwnd != IntPtr.Zero)
							{
								timer.Stop();
								string title = newChart.Title;
								DockChartByHwnd(hwnd, !string.IsNullOrWhiteSpace(title) ? title : (paneIndex == 1 ? inst + " (1-Min MP)" : inst + " (5-Min Candles)"), paneIndex, newChart);
								return;
							}
						}
						catch { }

						if (attempts >= 25)
						{
							timer.Stop();
							try
							{
								IntPtr hwnd = new WindowInteropHelper(newChart).Handle;
								if (hwnd != IntPtr.Zero)
									DockChartByHwnd(hwnd, paneIndex == 1 ? "Market Profile" : "5-Min Candles", paneIndex, newChart);
							}
							catch { }
						}
					};
					timer.Start();
				}
				catch (Exception ex)
				{
					NinjaTrader.Code.Output.Process("AK10X Launch Chart Inner Error: " + ex.Message, PrintTo.OutputTab1);
				}
			};

			if (AK10XChartStackerWindow.StackerDispatcher != null && !AK10XChartStackerWindow.StackerDispatcher.CheckAccess())
				AK10XChartStackerWindow.StackerDispatcher.BeginInvoke(openAction);
			else if (Dispatcher.CheckAccess())
				openAction();
			else
				Dispatcher.BeginInvoke(openAction);
		}

		private static bool TryUpdateChartInstrumentInPlace(NinjaTrader.Gui.Chart.Chart chartWin, Instrument inst, int periodMinutes)
		{
			if (chartWin == null || inst == null) return false;
			try
			{
				int targetDays = (periodMinutes == 1) ? 30 : 15;
				if (chartWin.ActiveChartControl != null)
				{
					if (chartWin.ActiveChartControl.BarsPropertiesCollection != null)
					{
						foreach (BarsProperties bpItem in chartWin.ActiveChartControl.BarsPropertiesCollection)
						{
							if (bpItem != null && bpItem.DaysBack < targetDays)
								bpItem.DaysBack = targetDays;
						}
					}
					if (chartWin.ActiveChartControl.BarsArray != null)
					{
						foreach (ChartBars cbItem in chartWin.ActiveChartControl.BarsArray)
						{
							if (cbItem != null && cbItem.Properties != null && cbItem.Properties.DaysBack < targetDays)
								cbItem.Properties.DaysBack = targetDays;
						}
					}
				}

				// 1. Primary official NinjaTrader ChartTab IInstrumentProvider setter (updates toolbar, tab, title, and bars in-place)
				if (chartWin.MainTabControl != null && chartWin.MainTabControl.SelectedItem is NinjaTrader.Gui.Tools.IInstrumentProvider ip)
				{
					ip.Instrument = inst;
					return true;
				}
				// 2. Fallback to ApplyDataSeriesChangeToSelected
				if (chartWin.ActiveChartControl != null && chartWin.ActiveChartControl.BarsArray != null && chartWin.ActiveChartControl.BarsArray.Count > 0)
				{
					var applyMethod = typeof(NinjaTrader.Gui.Chart.Chart).GetMethod("ApplyDataSeriesChangeToSelected", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
					if (applyMethod != null)
					{
						applyMethod.Invoke(chartWin, new object[] {
							chartWin.ActiveChartControl,
							inst,
							new BarsPeriod { BarsPeriodType = BarsPeriodType.Minute, Value = periodMinutes }
						});
						return true;
					}
				}
			}
			catch { }
			return false;
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

		/// <summary>
		/// Common unified chart loader:
		/// Chart 1: 1-Min Timeframe with INDMarketProfile50Charts attached.
		/// Chart 2: 5-Min Timeframe Candlestick chart.
		/// Docks both seamlessly into Screen 1 & Screen 2 of the 2-Screen Stacker in Maximized view.
		/// </summary>
		public static void LoadUnifiedInstrumentPair(string symbol)
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

					// Ensure Stacker window is created and visible on UI thread
					if (Instance == null || !Instance.IsLoaded)
					{
						Instance = new AK10XChartStackerWindow();
						Instance.Show();
					}

					// Fill 100% of screen work area in WindowState.Normal so child Direct2DForms never lose coordinates
					Instance.MaximizeStackerToWorkArea();

					IntPtr sHwnd = new WindowInteropHelper(Instance).EnsureHandle();
					if (sHwnd != IntPtr.Zero)
					{
						StackerWin32.ShowWindow(sHwnd, StackerWin32.SW_SHOWNORMAL);
						StackerWin32.SetForegroundWindow(sHwnd);
						StackerWin32.BringWindowToTop(sHwnd);
					}

					Instance.Activate();
					Instance.Focus();

					if (Instance.cmbSymbol != null)
					{
						Instance.isSuppressSymbolSelectionChanged = true;
						if (!Instance.cmbSymbol.Items.Contains(clean))
							Instance.cmbSymbol.Items.Insert(0, clean);
						Instance.cmbSymbol.SelectedItem = clean;
						Instance.cmbSymbol.Text = clean;
						Instance.isSuppressSymbolSelectionChanged = false;
					}

					Instrument inst = ResolveBestInstrument(clean);

					// FAST & SMOOTH IN-PLACE UPDATE: If both charts are already docked and alive, update instrument directly with ZERO screen blinking!
					bool updatedInPlace = false;
					if (Instance.dockedChart1Instance != null && Instance.dockedChart2Instance != null &&
						Instance.dockedChart1Hwnd != IntPtr.Zero && Instance.dockedChart2Hwnd != IntPtr.Zero &&
						StackerWin32.IsWindow(Instance.dockedChart1Hwnd) && StackerWin32.IsWindow(Instance.dockedChart2Hwnd) &&
						inst != null)
					{
						try
						{
							bool ok1 = TryUpdateChartInstrumentInPlace(Instance.dockedChart1Instance, inst, 1);
							bool ok2 = TryUpdateChartInstrumentInPlace(Instance.dockedChart2Instance, inst, 5);
							if (ok1 && ok2)
							{
								Instance.dockedChart1Title = inst.FullName + " (1-Min MP)";
								Instance.dockedChart2Title = inst.FullName + " (5-Min Candles)";
								Instance.txtPane1Title.Text = "SCREEN 1: " + inst.FullName + " (1-Min Market Profile)";
								Instance.txtPane2Title.Text = "SCREEN 2: " + inst.FullName + " (5-Min Candlestick)";
								Instance.UpdateWindowTitle();
								Instance.ForceLayoutAndSyncPanes();
								updatedInPlace = true;
							}
						}
						catch { }
					}

					if (!updatedInPlace)
					{
						Instance.CloseAllDockedCharts();

						// Launch Chart 1: 1-Min Timeframe with INDMarketProfile50Charts
						Instance.LaunchChartForPane(1, clean);

						// Launch Chart 2: 5-Min Timeframe Candles after 150ms stagger
						var timer = new System.Windows.Threading.DispatcherTimer();
						timer.Interval = TimeSpan.FromMilliseconds(150);
						timer.Tick += (s, e) =>
						{
							timer.Stop();
							Instance.LaunchChartForPane(2, clean);
						};
						timer.Start();
					}

					Instance.UpdateWindowTitle();
				}
				catch (Exception ex)
				{
					NinjaTrader.Code.Output.Process("AK10X LoadInstrumentPair Error: " + ex.Message, PrintTo.OutputTab1);
				}
			};

			if (AK10XChartStackerWindow.StackerDispatcher != null && !AK10XChartStackerWindow.StackerDispatcher.CheckAccess())
				AK10XChartStackerWindow.StackerDispatcher.BeginInvoke(act);
			else
				act();
		}

		public void CloseAllDockedCharts()
		{
			try
			{
				IntPtr h1 = dockedChart1Hwnd;
				var c1 = dockedChart1Instance;
				dockedChart1Hwnd = IntPtr.Zero;
				dockedChart1Title = null;
				dockedChart1Instance = null;
				lastX1 = -99999; lastY1 = -99999; lastW1 = -99999; lastH1 = -99999;

				IntPtr h2 = dockedChart2Hwnd;
				var c2 = dockedChart2Instance;
				dockedChart2Hwnd = IntPtr.Zero;
				dockedChart2Title = null;
				dockedChart2Instance = null;
				lastX2 = -99999; lastY2 = -99999; lastW2 = -99999; lastH2 = -99999;

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
			catch { }
		}

		protected override void OnClosing(CancelEventArgs e)
		{
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
			Button btn = new Button
			{
				Content = text,
				FontSize = 11,
				Padding = new Thickness(8, 3, 8, 3),
				Margin = new Thickness(0, 0, 6, 0),
				Background = new SolidColorBrush(Color.FromRgb(28, 34, 46)),
				Foreground = Brushes.White,
				BorderBrush = new SolidColorBrush(Color.FromRgb(48, 56, 76)),
				BorderThickness = new Thickness(1),
				Cursor = Cursors.Hand
			};
			btn.Click += onClick;
			return btn;
		}

		private static Button CreateAccentButton(string text, RoutedEventHandler onClick)
		{
			Button btn = new Button
			{
				Content = text,
				FontSize = 11,
				FontWeight = FontWeights.Bold,
				Padding = new Thickness(10, 4, 10, 4),
				Margin = new Thickness(0, 0, 6, 0),
				Background = new SolidColorBrush(Color.FromRgb(255, 179, 0)), // Gold Accent
				Foreground = Brushes.Black,
				BorderThickness = new Thickness(0),
				Cursor = Cursors.Hand
			};
			btn.Click += onClick;
			return btn;
		}

		private static Button CreatePaneButton(string text, RoutedEventHandler onClick)
		{
			Button btn = new Button
			{
				Content = text,
				FontSize = 10,
				Padding = new Thickness(6, 2, 6, 2),
				Margin = new Thickness(0, 0, 4, 0),
				Background = new SolidColorBrush(Color.FromRgb(28, 34, 46)),
				Foreground = new SolidColorBrush(Color.FromRgb(180, 190, 210)),
				BorderBrush = new SolidColorBrush(Color.FromRgb(44, 52, 70)),
				BorderThickness = new Thickness(1),
				Cursor = Cursors.Hand
			};
			btn.Click += onClick;
			return btn;
		}
		#endregion

		public class ChartItem
		{
			public IntPtr Hwnd { get; set; }
			public string Title { get; set; }
			public NinjaTrader.Gui.Chart.Chart Instance { get; set; }
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
				Description = "AK10X 2-Screen Window Stacker Add-On for NinjaTrader 8";
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
				Header = "AK10X 2-Screen Stacker",
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
			Action openAction = () =>
			{
				try
				{
					AK10XChartStackerWindow win = AK10XChartStackerWindow.Instance;
					if (win == null || !win.IsLoaded)
					{
						win = new AK10XChartStackerWindow();
						win.Show();
					}
					win.MaximizeStackerToWorkArea();
					win.Activate();
					win.Focus();
				}
				catch (Exception ex)
				{
					NinjaTrader.Code.Output.Process("Error opening AK10X Stacker: " + ex.Message, PrintTo.OutputTab1);
				}
			};

			if (AK10XChartStackerWindow.StackerDispatcher != null && !AK10XChartStackerWindow.StackerDispatcher.CheckAccess())
				AK10XChartStackerWindow.StackerDispatcher.BeginInvoke(openAction);
			else
				openAction();
		}
	}
	#endregion
}