// AK10X Suite - Protected Binary Add-On Bridge for NinjaTrader 8
// Core implementation (AK10XTradingWindow, MarketProfileScannerWindow, and all 5 AK10X Indicators)
// is compiled inside AK10X.Suite.dll with zero compile-time NinjaScript Editor reference dependencies.
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using NinjaTrader.Core;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.AddOns
{
	internal static class AK10XSuiteRuntimeLoader
	{
		public const string ExpectedBuildStamp = "20261117_091834";
		private static readonly object SyncRoot = new object();
		private static Assembly cachedSuiteAssembly;
		private static bool resolveHooked;

		public static Assembly EnsureLoaded()
		{
			lock (SyncRoot)
			{
				if (!resolveHooked)
				{
					resolveHooked = true;
					AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
				}

				if (cachedSuiteAssembly != null && HasMatchingBuildStamp(cachedSuiteAssembly))
					return cachedSuiteAssembly;

				try
				{
					string matchedKey = null;
					Assembly regAsm = null;

					if (Globals.AssemblyRegistry != null && Globals.AssemblyRegistry.Keys != null)
					{
						foreach (string key in Globals.AssemblyRegistry.Keys)
						{
							if (key != null && key.IndexOf("AK10X.Suite", StringComparison.OrdinalIgnoreCase) >= 0)
							{
								matchedKey = key;
								Globals.AssemblyRegistry.TryGetValue(key, out regAsm);
								break;
							}
						}
					}

					if (regAsm != null && HasMatchingBuildStamp(regAsm))
					{
						cachedSuiteAssembly = regAsm;
						return cachedSuiteAssembly;
					}

					string dllPath = Path.Combine(Globals.UserDataDir, "bin", "Custom", "AK10X.Suite.dll");
					if (File.Exists(dllPath))
					{
						byte[] rawBytes = File.ReadAllBytes(dllPath);
						Assembly freshAsm = Assembly.Load(rawBytes);
						cachedSuiteAssembly = freshAsm;

						if (Globals.AssemblyRegistry != null)
						{
							string regKey = !string.IsNullOrEmpty(matchedKey) ? matchedKey : "AK10X.Suite";
							Globals.AssemblyRegistry[regKey] = freshAsm;
						}
						return cachedSuiteAssembly;
					}

					if (regAsm != null)
					{
						cachedSuiteAssembly = regAsm;
						return cachedSuiteAssembly;
					}
				}
				catch (Exception ex)
				{
					NinjaTrader.Code.Output.Process("AK10X Suite load warning: " + ex.Message, PrintTo.OutputTab1);
				}

				return cachedSuiteAssembly;
			}
		}

		private static bool HasMatchingBuildStamp(Assembly asm)
		{
			if (asm == null)
				return false;
			try
			{
				if (asm.GetType("NinjaTrader.NinjaScript.Indicators.AK10XGlobalOrderflow", false) == null)
					return false;

				Type stampType = asm.GetType("NinjaTrader.NinjaScript.AddOns.AK10XSuiteBuildInfo", false);
				if (stampType == null)
					return false;

				FieldInfo fi = stampType.GetField("Stamp", BindingFlags.Public | BindingFlags.Static);
				if (fi == null)
					return false;

				string val = fi.GetRawConstantValue() as string;
				return string.Equals(val, ExpectedBuildStamp, StringComparison.Ordinal);
			}
			catch
			{
				return false;
			}
		}

		private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
		{
			if (args == null || string.IsNullOrEmpty(args.Name))
				return null;

			if (args.Name.StartsWith("AK10X.Suite", StringComparison.OrdinalIgnoreCase))
				return cachedSuiteAssembly;

			return null;
		}

		public static void OpenTradingScreen()
		{
			try
			{
				Assembly asm = EnsureLoaded();
				if (asm == null)
					return;

				Type winType = asm.GetType("NinjaTrader.NinjaScript.AddOns.AK10XTradingWindow", false);
				if (winType == null)
					return;

				MethodInfo mi = winType.GetMethod("OpenFromControlCenterMenu", BindingFlags.Public | BindingFlags.Static);
				if (mi != null)
					mi.Invoke(null, null);
			}
			catch (Exception ex)
			{
				NinjaTrader.Code.Output.Process("Error opening AK10X Trading Screen: " + ex.Message, PrintTo.OutputTab1);
			}
		}

		public static void OpenScannerWindow()
		{
			Globals.MainThreadDispatcher.BeginInvoke(new Action(() =>
			{
				try
				{
					Assembly asm = EnsureLoaded();
					if (asm == null)
						return;

					Type scannerType = asm.GetType("NinjaTrader.NinjaScript.Indicators.MarketProfileScannerWindow", false);
					if (scannerType == null)
						return;

					Window win = Activator.CreateInstance(scannerType) as Window;
					if (win != null)
						win.Show();
				}
				catch (Exception ex)
				{
					NinjaTrader.Code.Output.Process("Error opening AK10X Scanner: " + ex.Message, PrintTo.OutputTab1);
				}
			}));
		}

		public static void RemoveDuplicateMenuItems(NTMenuItem parentMenu, params string[] headersToClean)
		{
			if (parentMenu == null || parentMenu.Items == null || headersToClean == null)
				return;

			for (int i = parentMenu.Items.Count - 1; i >= 0; i--)
			{
				NTMenuItem existing = parentMenu.Items[i] as NTMenuItem;
				if (existing == null || existing.Header == null)
					continue;

				string h = existing.Header.ToString();
				for (int j = 0; j < headersToClean.Length; j++)
				{
					if (string.Equals(h, headersToClean[j], StringComparison.OrdinalIgnoreCase))
					{
						parentMenu.Items.RemoveAt(i);
						break;
					}
				}
			}
		}
	}

	public class AK10XTradingWindowAddOn : AddOnBase
	{
		private NTMenuItem menuItem;
		private NTMenuItem existingMenu;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "AK10X Trading Screen Add-On for NinjaTrader 8";
				Name = "AK10XTradingWindow";
			}
			else if (State == State.Active)
			{
				AK10XSuiteRuntimeLoader.EnsureLoaded();
			}
		}

		protected override void OnWindowCreated(Window window)
		{
			ControlCenter cc = window as ControlCenter;
			if (cc == null) return;
			existingMenu = cc.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
			if (existingMenu == null) return;

			AK10XSuiteRuntimeLoader.RemoveDuplicateMenuItems(existingMenu, "AK10X Trading Screen", "AK10X 2-Screen Stacker");

			Style itemStyle = null;
			try
			{
				if (Application.Current != null && Application.Current.Dispatcher.CheckAccess())
					itemStyle = Application.Current.TryFindResource("MainMenuItem") as Style;
			}
			catch { }

			menuItem = new NTMenuItem { Header = "AK10X Trading Screen", Style = itemStyle };
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
			AK10XSuiteRuntimeLoader.OpenTradingScreen();
		}
	}

	public class AK10XMarketProfileScannerAddOn : AddOnBase
	{
		private NTMenuItem menuItem;
		private NTMenuItem existingMenu;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "AK10X Market Profile Live Scanner Add-On for NinjaTrader 8";
				Name = "AK10XMarketProfileScannerAddOn";
			}
			else if (State == State.Active)
			{
				AK10XSuiteRuntimeLoader.EnsureLoaded();
			}
		}

		protected override void OnWindowCreated(Window window)
		{
			ControlCenter cc = window as ControlCenter;
			if (cc == null) return;
			existingMenu = cc.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
			if (existingMenu == null) return;

			AK10XSuiteRuntimeLoader.RemoveDuplicateMenuItems(existingMenu, "AK10X MP Scanner (Live)");

			Style itemStyle = null;
			try
			{
				if (Application.Current != null && Application.Current.Dispatcher.CheckAccess())
					itemStyle = Application.Current.TryFindResource("MainMenuItem") as Style;
			}
			catch { }

			menuItem = new NTMenuItem
			{
				Header = "AK10X MP Scanner (Live)",
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
			AK10XSuiteRuntimeLoader.OpenScannerWindow();
		}
	}
}
