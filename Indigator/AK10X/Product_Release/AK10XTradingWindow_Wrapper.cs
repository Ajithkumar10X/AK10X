// AK10X Suite - Protected Binary Add-On Bridge for NinjaTrader 8
// Core implementation (AK10XTradingWindow, MarketProfileScannerWindow, and all 6 AK10X Indicators)
// is compiled inside AK10X.Suite.dll with zero compile-time NinjaScript Editor reference dependencies.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using NinjaTrader.Core;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.AddOns
{
	internal static class AK10XSuiteRuntimeLoader
	{
		public const string ExpectedBuildStamp = "20261122_162556";
		private static readonly object SyncRoot = new object();
		private static Assembly cachedSuiteAssembly;
		private static bool resolveHooked;

		[ThreadStatic]
		private static bool isResolvingAssembly;

		static AK10XSuiteRuntimeLoader()
		{
			EnsureResolveHooked();
		}

		public static void EnsureResolveHooked()
		{
			lock (SyncRoot)
			{
				if (!resolveHooked)
				{
					resolveHooked = true;
					AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
				}
			}
		}

		private static string lastAutoReloadedStamp;

		private static bool RegisterSuiteInAssemblyRegistry(Assembly freshAsm)
		{
			if (freshAsm == null || Globals.AssemblyRegistry == null)
				return false;

			bool wasUpgraded = false;
			try
			{
				FieldInfo dictField = typeof(AssemblyRegistry).GetField("dictionary", BindingFlags.Instance | BindingFlags.NonPublic);
				FieldInfo lockField = typeof(AssemblyRegistry).GetField("lockObj", BindingFlags.Instance | BindingFlags.NonPublic);
				IDictionary regDict = dictField != null ? dictField.GetValue(Globals.AssemblyRegistry) as IDictionary : null;
				object regLock = lockField != null ? lockField.GetValue(Globals.AssemblyRegistry) : null;

				if (regDict != null)
				{
					Action updateDict = () =>
					{
						List<object> matchingKeys = new List<object>();
						foreach (DictionaryEntry de in regDict)
						{
							string kStr = de.Key as string;
							Assembly vAsm = de.Value as Assembly;
							bool isSuiteKey = (kStr != null && kStr.IndexOf("AK10X.Suite", StringComparison.OrdinalIgnoreCase) >= 0);
							bool isSuiteAsm = false;
							if (vAsm != null)
							{
								try { isSuiteAsm = string.Equals(vAsm.GetName().Name, "AK10X.Suite", StringComparison.OrdinalIgnoreCase); } catch { }
							}
							if (isSuiteKey || isSuiteAsm)
							{
								matchingKeys.Add(de.Key);
								if (!ReferenceEquals(vAsm, freshAsm))
									wasUpgraded = true;
							}
						}

						if (matchingKeys.Count > 0)
						{
							for (int i = 0; i < matchingKeys.Count; i++)
								regDict[matchingKeys[i]] = freshAsm;
						}
						else
						{
							regDict["AK10X.Suite"] = freshAsm;
						}
					};

					if (regLock != null)
					{
						lock (regLock) { updateDict(); }
					}
					else
					{
						updateDict();
					}
					return wasUpgraded;
				}
			}
			catch { }

			try
			{
				Globals.AssemblyRegistry.Remove("AK10X.Suite");
				Globals.AssemblyRegistry.Add("AK10X.Suite", freshAsm);
			}
			catch { }
			return wasUpgraded;
		}

		private static void ScheduleLiveChartReloadIfNeeded(string stamp)
		{
			if (string.Equals(lastAutoReloadedStamp, stamp, StringComparison.Ordinal))
				return;
			lastAutoReloadedStamp = stamp;

			try
			{
				Dispatcher disp = Globals.MainThreadDispatcher ?? (Application.Current != null ? Application.Current.Dispatcher : null);
				if (disp == null)
					return;

				DispatcherTimer reloadTimer = null;
				reloadTimer = new DispatcherTimer(
					TimeSpan.FromMilliseconds(450),
					DispatcherPriority.ApplicationIdle,
					(s, e) =>
					{
						try { if (reloadTimer != null) reloadTimer.Stop(); } catch { }
						try
						{
							if (Globals.AllWindows == null)
								return;

							List<Window> openWins = new List<Window>();
							lock (Globals.AllWindows)
							{
								foreach (Window w in Globals.AllWindows)
								{
									if (w != null) openWins.Add(w);
								}
							}

							for (int i = 0; i < openWins.Count; i++)
							{
								NinjaTrader.Gui.Chart.Chart chartWin = openWins[i] as NinjaTrader.Gui.Chart.Chart;
								if (chartWin == null)
									continue;

								try
								{
									NinjaTrader.Gui.Chart.ChartControl activeCc = chartWin.ActiveChartControl;
									if (activeCc != null)
										activeCc.OnReloadNinjaScriptHotKey(activeCc, null);
								}
								catch { }
							}
						}
						catch { }
					},
					disp);
				reloadTimer.Start();
			}
			catch { }
		}

		public static Assembly EnsureLoaded()
		{
			EnsureResolveHooked();

			lock (SyncRoot)
			{
				if (cachedSuiteAssembly != null && HasMatchingBuildStamp(cachedSuiteAssembly))
					return cachedSuiteAssembly;

				try
				{
					Assembly regAsm = null;

					if (Globals.AssemblyRegistry != null && Globals.AssemblyRegistry.Keys != null)
					{
						foreach (string key in Globals.AssemblyRegistry.Keys)
						{
							if (key != null && key.IndexOf("AK10X.Suite", StringComparison.OrdinalIgnoreCase) >= 0)
							{
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

					// Also check AppDomain loaded assemblies before falling back to disk
					foreach (Assembly domAsm in AppDomain.CurrentDomain.GetAssemblies())
					{
						try
						{
							string n = domAsm.GetName().Name;
							if (string.Equals(n, "AK10X.Suite", StringComparison.OrdinalIgnoreCase) && HasMatchingBuildStamp(domAsm))
							{
								cachedSuiteAssembly = domAsm;
								if (RegisterSuiteInAssemblyRegistry(domAsm))
									ScheduleLiveChartReloadIfNeeded(ExpectedBuildStamp);
								return cachedSuiteAssembly;
							}
						}
						catch { }
					}

					string dllPath = Path.Combine(Globals.UserDataDir, "bin", "Custom", "AK10X.Suite.dll");
					if (File.Exists(dllPath))
					{
						Assembly freshAsm = null;
						try
						{
							string shadowDir = Path.Combine(Path.GetTempPath(), "ak10x_suite_shadow");
							Directory.CreateDirectory(shadowDir);
							string shadowDll = Path.Combine(shadowDir, "AK10X.Suite_" + ExpectedBuildStamp + ".dll");
							if (!File.Exists(shadowDll))
								File.Copy(dllPath, shadowDll, true);

							string vendorSrc = Path.Combine(Globals.UserDataDir, "bin", "Custom", "NinjaTrader.Vendor.dll");
							string vendorShadow = Path.Combine(shadowDir, "NinjaTrader.Vendor.dll");
							if (File.Exists(vendorSrc) && !File.Exists(vendorShadow))
								File.Copy(vendorSrc, vendorShadow, true);

							freshAsm = Assembly.LoadFrom(shadowDll);
						}
						catch
						{
							byte[] rawBytes = File.ReadAllBytes(dllPath);
							freshAsm = Assembly.Load(rawBytes);
						}

						cachedSuiteAssembly = freshAsm;

						bool upgraded = RegisterSuiteInAssemblyRegistry(freshAsm);
						NinjaTrader.Code.Output.Process("AK10X Suite loaded build " + ExpectedBuildStamp + " (" + freshAsm.GetName().Version + ")", PrintTo.OutputTab1);
						if (upgraded)
							ScheduleLiveChartReloadIfNeeded(ExpectedBuildStamp);

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

		public static void EnsureInMemoryAddOnAuthorized()
		{
			try
			{
				PropertyInfo regProp = typeof(Globals).GetProperty("AddOnRegistry", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
				IDictionary dict = regProp != null ? regProp.GetValue(null, null) as IDictionary : null;
				Type authType = typeof(Globals).Assembly.GetType("NinjaTrader.Core.AddOnAuthorization", false);
				if (dict == null || authType == null)
					return;

				// AddOnAuthorization uses public fields (Name, IsAuthorized, Time) for XML serialization
				FieldInfo fName = authType.GetField("Name", BindingFlags.Public | BindingFlags.Instance);
				FieldInfo fAuth = authType.GetField("IsAuthorized", BindingFlags.Public | BindingFlags.Instance);
				FieldInfo fTime = authType.GetField("Time", BindingFlags.Public | BindingFlags.Instance);
				if (fName == null || fAuth == null || fTime == null)
					return;

				bool changed = false;

				// Clean up any corrupt entries that have an empty Name field
				List<object> badKeys = new List<object>();
				foreach (DictionaryEntry de in dict)
				{
					string kStr = de.Key as string;
					string valName = de.Value != null ? fName.GetValue(de.Value) as string : null;
					if (string.IsNullOrWhiteSpace(kStr) || string.IsNullOrWhiteSpace(valName))
						badKeys.Add(de.Key);
				}
				for (int i = 0; i < badKeys.Count; i++)
				{
					dict.Remove(badKeys[i]);
					changed = true;
				}

				string[] keys = new string[]
				{
					"AK10XTradingWindow",
					"AK10XTradingWindowAddOn",
					"AK10XMarketProfileScannerAddOn"
				};

				for (int i = 0; i < keys.Length; i++)
				{
					string k = keys[i];
					if (!dict.Contains(k))
					{
						object entry = Activator.CreateInstance(authType);
						fName.SetValue(entry, k);
						fAuth.SetValue(entry, true);
						fTime.SetValue(entry, DateTime.Now);
						dict[k] = entry;
						changed = true;
					}
					else
					{
						object existing = dict[k];
						if (existing != null)
						{
							string curName = fName.GetValue(existing) as string;
							bool curAuth = (bool)fAuth.GetValue(existing);
							if (!curAuth || string.IsNullOrEmpty(curName))
							{
								fName.SetValue(existing, k);
								fAuth.SetValue(existing, true);
								if ((DateTime)fTime.GetValue(existing) == DateTime.MinValue)
									fTime.SetValue(existing, DateTime.Now);
								changed = true;
							}
						}
					}
				}

				if (changed)
				{
					MethodInfo saveMi = typeof(Globals).GetMethod("AddOnRegistrySave", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
					if (saveMi != null)
						saveMi.Invoke(null, null);
				}
			}
			catch { }
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
			if (isResolvingAssembly || args == null || string.IsNullOrEmpty(args.Name))
				return null;

			int commaIdx = args.Name.IndexOf(',');
			string shortName = (commaIdx > 0 ? args.Name.Substring(0, commaIdx) : args.Name).Trim();
			if (string.IsNullOrEmpty(shortName) || shortName.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
				return null;

			if (string.Equals(shortName, "AK10X.Suite", StringComparison.OrdinalIgnoreCase) && cachedSuiteAssembly != null)
				return cachedSuiteAssembly;

			isResolvingAssembly = true;
			try
			{
				// Pure mscorlib lookup (zero NinjaTrader.Core JIT dependency inside OnAssemblyResolve!)
				Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < loaded.Length; i++)
				{
					Assembly a = loaded[i];
					if (a == null)
						continue;
					try
					{
						if (string.Equals(a.GetName().Name, shortName, StringComparison.OrdinalIgnoreCase))
							return a;
					}
					catch { }
				}

				return TryLoadFromCustomFolderFallback(shortName);
			}
			catch { }
			finally
			{
				isResolvingAssembly = false;
			}

			return null;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static Assembly TryLoadFromCustomFolderFallback(string shortName)
		{
			try
			{
				string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
				string cand = Path.Combine(docs, "NinjaTrader 8", "bin", "Custom", shortName + ".dll");
				if (File.Exists(cand))
					return Assembly.LoadFrom(cand);
			}
			catch { }
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
			Dispatcher disp = Globals.MainThreadDispatcher ?? (Application.Current != null ? Application.Current.Dispatcher : null);
			Action openAct = () =>
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
			};

			if (disp != null && !disp.CheckAccess())
				disp.BeginInvoke(openAct);
			else
				openAct();
		}

		public static List<ControlCenter> GetOpenControlCenters()
		{
			List<ControlCenter> list = new List<ControlCenter>();
			try
			{
				PropertyInfo instProp = typeof(ControlCenter).GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
				if (instProp != null)
				{
					ControlCenter ccInst = instProp.GetValue(null, null) as ControlCenter;
					if (ccInst != null && !list.Contains(ccInst))
						list.Add(ccInst);
				}
			}
			catch { }

			try
			{
				if (Application.Current != null && Application.Current.Dispatcher != null && Application.Current.Dispatcher.CheckAccess())
				{
					foreach (Window w in Application.Current.Windows)
					{
						ControlCenter cc = w as ControlCenter;
						if (cc != null && !list.Contains(cc))
							list.Add(cc);
					}
				}
			}
			catch { }

			return list;
		}

		public static NTMenuItem ResolveControlCenterNewMenu(ControlCenter cc)
		{
			if (cc == null)
				return null;

			try
			{
				NTMenuItem byAutoId = cc.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
				if (byAutoId != null)
					return byAutoId;
			}
			catch { }

			try
			{
				FieldInfo fi = typeof(ControlCenter).GetField("newMenuItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (fi != null)
				{
					NTMenuItem byField = fi.GetValue(cc) as NTMenuItem;
					if (byField != null)
						return byField;
				}
			}
			catch { }

			try
			{
				NTMenuItem byName = cc.FindName("newMenuItem") as NTMenuItem;
				if (byName != null)
					return byName;
			}
			catch { }

			return null;
		}

		public static Style ResolveMenuItemStyle(ControlCenter cc, NTMenuItem parentMenu)
		{
			Style itemStyle = null;
			try
			{
				if (cc != null)
					itemStyle = cc.TryFindResource("MainMenuItem") as Style;
			}
			catch { }

			if (itemStyle == null)
			{
				try
				{
					if (Application.Current != null && Application.Current.Dispatcher.CheckAccess())
						itemStyle = Application.Current.TryFindResource("MainMenuItem") as Style;
				}
				catch { }
			}

			if (itemStyle == null && parentMenu != null && parentMenu.Items != null)
			{
				try
				{
					foreach (object obj in parentMenu.Items)
					{
						NTMenuItem sample = obj as NTMenuItem;
						if (sample != null && sample.Style != null)
						{
							itemStyle = sample.Style;
							break;
						}
					}
				}
				catch { }
			}

			return itemStyle;
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
		private DispatcherTimer retryTimer;
		private int retryTicks;
		private bool isTerminated;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "AK10X Trading Screen Add-On for NinjaTrader 8";
				Name = "AK10XTradingWindow";
				AK10XSuiteRuntimeLoader.EnsureResolveHooked();
				AK10XSuiteRuntimeLoader.EnsureInMemoryAddOnAuthorized();
				AK10XSuiteRuntimeLoader.EnsureLoaded();
			}
			else if (State == State.Active)
			{
				isTerminated = false;
				AK10XSuiteRuntimeLoader.EnsureResolveHooked();
				AK10XSuiteRuntimeLoader.EnsureInMemoryAddOnAuthorized();
				AK10XSuiteRuntimeLoader.EnsureLoaded();
				StartAttachMonitor();
			}
			else if (State == State.Terminated)
			{
				isTerminated = true;
				StopAttachMonitor();
				DetachMenuItemSafe();
			}
		}

		private void StartAttachMonitor()
		{
			Dispatcher disp = Globals.MainThreadDispatcher ?? (Application.Current != null ? Application.Current.Dispatcher : null);
			if (disp == null)
				return;

			disp.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
			{
				if (isTerminated)
					return;

				if (TryAttachOpenControlCenters())
					return;

				StopAttachMonitor();
				retryTicks = 0;
				retryTimer = new DispatcherTimer(
					TimeSpan.FromMilliseconds(500),
					DispatcherPriority.Loaded,
					(s, e) =>
					{
						retryTicks++;
						if (isTerminated || TryAttachOpenControlCenters() || retryTicks >= 40)
							StopAttachMonitor();
					},
					disp);
				retryTimer.Start();
			}));
		}

		private void StopAttachMonitor()
		{
			try
			{
				if (retryTimer != null)
				{
					retryTimer.Stop();
					retryTimer = null;
				}
			}
			catch { }
		}

		private bool TryAttachOpenControlCenters()
		{
			List<ControlCenter> centers = AK10XSuiteRuntimeLoader.GetOpenControlCenters();
			bool attachedAny = false;
			for (int i = 0; i < centers.Count; i++)
			{
				if (AttachToControlCenter(centers[i]))
					attachedAny = true;
			}
			return attachedAny;
		}

		private bool AttachToControlCenter(ControlCenter cc)
		{
			if (cc == null || isTerminated)
				return false;

			if (!cc.Dispatcher.CheckAccess())
			{
				cc.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => AttachToControlCenter(cc)));
				return false;
			}

			NTMenuItem newMenu = AK10XSuiteRuntimeLoader.ResolveControlCenterNewMenu(cc);
			if (newMenu == null || newMenu.Items == null)
			{
				cc.Loaded -= OnControlCenterLoaded;
				cc.Loaded += OnControlCenterLoaded;
				return false;
			}

			existingMenu = newMenu;
			if (menuItem != null && existingMenu.Items.Contains(menuItem))
				return true;

			AK10XSuiteRuntimeLoader.RemoveDuplicateMenuItems(existingMenu, "AK10X Trading Screen", "AK10X 2-Screen Stacker");

			Style itemStyle = AK10XSuiteRuntimeLoader.ResolveMenuItemStyle(cc, existingMenu);
			menuItem = new NTMenuItem { Header = "AK10X Trading Screen", Style = itemStyle };
			menuItem.Click += OnMenuItemClick;
			existingMenu.Items.Add(menuItem);
			return true;
		}

		private void OnControlCenterLoaded(object sender, RoutedEventArgs e)
		{
			ControlCenter cc = sender as ControlCenter;
			if (cc != null)
			{
				cc.Loaded -= OnControlCenterLoaded;
				AttachToControlCenter(cc);
			}
		}

		private void DetachMenuItemSafe()
		{
			try
			{
				NTMenuItem m = menuItem;
				NTMenuItem parent = existingMenu;
				menuItem = null;
				if (m == null)
					return;

				Action rem = () =>
				{
					try
					{
						if (parent != null && parent.Items != null && parent.Items.Contains(m))
							parent.Items.Remove(m);
						m.Click -= OnMenuItemClick;
					}
					catch { }
				};

				if (m.Dispatcher != null && !m.Dispatcher.CheckAccess())
					m.Dispatcher.BeginInvoke(rem);
				else
					rem();
			}
			catch { }
		}

		protected override void OnWindowCreated(Window window)
		{
			ControlCenter cc = window as ControlCenter;
			if (cc == null) return;
			AttachToControlCenter(cc);
		}

		protected override void OnWindowDestroyed(Window window)
		{
			ControlCenter cc = window as ControlCenter;
			if (cc != null)
			{
				cc.Loaded -= OnControlCenterLoaded;
				DetachMenuItemSafe();
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
		private DispatcherTimer retryTimer;
		private int retryTicks;
		private bool isTerminated;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "AK10X Market Profile Live Scanner Add-On for NinjaTrader 8";
				Name = "AK10XMarketProfileScannerAddOn";
				AK10XSuiteRuntimeLoader.EnsureResolveHooked();
				AK10XSuiteRuntimeLoader.EnsureInMemoryAddOnAuthorized();
			}
			else if (State == State.Active)
			{
				isTerminated = false;
				AK10XSuiteRuntimeLoader.EnsureResolveHooked();
				AK10XSuiteRuntimeLoader.EnsureInMemoryAddOnAuthorized();
				AK10XSuiteRuntimeLoader.EnsureLoaded();
				StartAttachMonitor();
			}
			else if (State == State.Terminated)
			{
				isTerminated = true;
				StopAttachMonitor();
				DetachMenuItemSafe();
			}
		}

		private void StartAttachMonitor()
		{
			Dispatcher disp = Globals.MainThreadDispatcher ?? (Application.Current != null ? Application.Current.Dispatcher : null);
			if (disp == null)
				return;

			disp.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
			{
				if (isTerminated)
					return;

				if (TryAttachOpenControlCenters())
					return;

				StopAttachMonitor();
				retryTicks = 0;
				retryTimer = new DispatcherTimer(
					TimeSpan.FromMilliseconds(500),
					DispatcherPriority.Loaded,
					(s, e) =>
					{
						retryTicks++;
						if (isTerminated || TryAttachOpenControlCenters() || retryTicks >= 40)
							StopAttachMonitor();
					},
					disp);
				retryTimer.Start();
			}));
		}

		private void StopAttachMonitor()
		{
			try
			{
				if (retryTimer != null)
				{
					retryTimer.Stop();
					retryTimer = null;
				}
			}
			catch { }
		}

		private bool TryAttachOpenControlCenters()
		{
			List<ControlCenter> centers = AK10XSuiteRuntimeLoader.GetOpenControlCenters();
			bool attachedAny = false;
			for (int i = 0; i < centers.Count; i++)
			{
				if (AttachToControlCenter(centers[i]))
					attachedAny = true;
			}
			return attachedAny;
		}

		private bool AttachToControlCenter(ControlCenter cc)
		{
			if (cc == null || isTerminated)
				return false;

			if (!cc.Dispatcher.CheckAccess())
			{
				cc.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => AttachToControlCenter(cc)));
				return false;
			}

			NTMenuItem newMenu = AK10XSuiteRuntimeLoader.ResolveControlCenterNewMenu(cc);
			if (newMenu == null || newMenu.Items == null)
			{
				cc.Loaded -= OnControlCenterLoaded;
				cc.Loaded += OnControlCenterLoaded;
				return false;
			}

			existingMenu = newMenu;
			if (menuItem != null && existingMenu.Items.Contains(menuItem))
				return true;

			AK10XSuiteRuntimeLoader.RemoveDuplicateMenuItems(existingMenu, "AK10X MP Scanner (Live)");

			Style itemStyle = AK10XSuiteRuntimeLoader.ResolveMenuItemStyle(cc, existingMenu);
			menuItem = new NTMenuItem
			{
				Header = "AK10X MP Scanner (Live)",
				Style = itemStyle
			};
			menuItem.Click += OnMenuItemClick;
			existingMenu.Items.Add(menuItem);
			return true;
		}

		private void OnControlCenterLoaded(object sender, RoutedEventArgs e)
		{
			ControlCenter cc = sender as ControlCenter;
			if (cc != null)
			{
				cc.Loaded -= OnControlCenterLoaded;
				AttachToControlCenter(cc);
			}
		}

		private void DetachMenuItemSafe()
		{
			try
			{
				NTMenuItem m = menuItem;
				NTMenuItem parent = existingMenu;
				menuItem = null;
				if (m == null)
					return;

				Action rem = () =>
				{
					try
					{
						if (parent != null && parent.Items != null && parent.Items.Contains(m))
							parent.Items.Remove(m);
						m.Click -= OnMenuItemClick;
					}
					catch { }
				};

				if (m.Dispatcher != null && !m.Dispatcher.CheckAccess())
					m.Dispatcher.BeginInvoke(rem);
				else
					rem();
			}
			catch { }
		}

		protected override void OnWindowCreated(Window window)
		{
			ControlCenter cc = window as ControlCenter;
			if (cc == null) return;
			AttachToControlCenter(cc);
		}

		protected override void OnWindowDestroyed(Window window)
		{
			ControlCenter cc = window as ControlCenter;
			if (cc != null)
			{
				cc.Loaded -= OnControlCenterLoaded;
				DetachMenuItemSafe();
			}
		}

		private void OnMenuItemClick(object sender, RoutedEventArgs e)
		{
			AK10XSuiteRuntimeLoader.OpenScannerWindow();
		}
	}
}
