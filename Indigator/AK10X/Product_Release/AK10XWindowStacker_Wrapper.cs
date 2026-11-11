// AK10X Suite - Protected Binary Add-On Wrapper for NinjaTrader 8
// Core implementation (AK10XChartStackerWindow & MarketProfileScannerWindow) is compiled inside AK10X.Suite.dll
using System;
using System.Windows;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.AddOns
{
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
			AK10XChartStackerWindow.OpenFromControlCenterMenu();
		}
	}

	public class MarketProfileScanner50ChartsAddOn : AddOnBase
	{
		private NTMenuItem menuItem;
		private NTMenuItem existingMenu;

		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description = "AK10X Market Profile Live Scanner Add-On for NinjaTrader 8";
				Name = "MarketProfileScanner50ChartsAddOn";
			}
		}

		protected override void OnWindowCreated(Window window)
		{
			ControlCenter cc = window as ControlCenter;
			if (cc == null) return;
			existingMenu = cc.FindFirst("ControlCenterMenuItemNew") as NTMenuItem;
			if (existingMenu == null) return;

			menuItem = new NTMenuItem
			{
				Header = "AK10X MP Scanner (Live)",
				Style = Application.Current != null ? Application.Current.TryFindResource("MainMenuItem") as Style : null
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
			NinjaTrader.Core.Globals.MainThreadDispatcher.BeginInvoke(new Action(() =>
			{
				var win = new NinjaTrader.NinjaScript.Indicators.MarketProfileScannerWindow();
				win.Show();
			}));
		}
	}
}