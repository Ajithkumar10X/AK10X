// AK10X Suite - Protected Binary Wrapper for NinjaTrader 8 (MarketProfileScanner50Charts)
// Core implementation is compiled inside AK10X.Suite.dll (Protected Mode - Source Code Hidden)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MarketProfileScanner50Charts[] cacheMarketProfileScanner50Charts;
		public MarketProfileScanner50Charts MarketProfileScanner50Charts(bool displayPrevDayValues, bool displayTodayValues, bool displayDailySma, int smaNumberOfDays, bool displayPrevDayMpLevels, bool displayIbValues, bool displayCurrentDayMpLevels, int atrNumberOfDays, double profileDistributionOneSigma, double minTickSizeParam, int initialBalanceCandleCount, bool autoRefreshEnabled, int autoRefreshSeconds, bool useClassicLightTheme, int scannerFontSize)
		{
			return MarketProfileScanner50Charts(Input, displayPrevDayValues, displayTodayValues, displayDailySma, smaNumberOfDays, displayPrevDayMpLevels, displayIbValues, displayCurrentDayMpLevels, atrNumberOfDays, profileDistributionOneSigma, minTickSizeParam, initialBalanceCandleCount, autoRefreshEnabled, autoRefreshSeconds, useClassicLightTheme, scannerFontSize);
		}

		public MarketProfileScanner50Charts MarketProfileScanner50Charts(ISeries<double> input, bool displayPrevDayValues, bool displayTodayValues, bool displayDailySma, int smaNumberOfDays, bool displayPrevDayMpLevels, bool displayIbValues, bool displayCurrentDayMpLevels, int atrNumberOfDays, double profileDistributionOneSigma, double minTickSizeParam, int initialBalanceCandleCount, bool autoRefreshEnabled, int autoRefreshSeconds, bool useClassicLightTheme, int scannerFontSize)
		{
			if (cacheMarketProfileScanner50Charts != null)
				for (int idx = 0; idx < cacheMarketProfileScanner50Charts.Length; idx++)
					if (cacheMarketProfileScanner50Charts[idx] != null && cacheMarketProfileScanner50Charts[idx].DisplayPrevDayValues == displayPrevDayValues && cacheMarketProfileScanner50Charts[idx].DisplayTodayValues == displayTodayValues && cacheMarketProfileScanner50Charts[idx].DisplayDailySma == displayDailySma && cacheMarketProfileScanner50Charts[idx].SmaNumberOfDays == smaNumberOfDays && cacheMarketProfileScanner50Charts[idx].DisplayPrevDayMpLevels == displayPrevDayMpLevels && cacheMarketProfileScanner50Charts[idx].DisplayIbValues == displayIbValues && cacheMarketProfileScanner50Charts[idx].DisplayCurrentDayMpLevels == displayCurrentDayMpLevels && cacheMarketProfileScanner50Charts[idx].AtrNumberOfDays == atrNumberOfDays && cacheMarketProfileScanner50Charts[idx].ProfileDistributionOneSigma == profileDistributionOneSigma && cacheMarketProfileScanner50Charts[idx].MinTickSizeParam == minTickSizeParam && cacheMarketProfileScanner50Charts[idx].InitialBalanceCandleCount == initialBalanceCandleCount && cacheMarketProfileScanner50Charts[idx].AutoRefreshEnabled == autoRefreshEnabled && cacheMarketProfileScanner50Charts[idx].AutoRefreshSeconds == autoRefreshSeconds && cacheMarketProfileScanner50Charts[idx].UseClassicLightTheme == useClassicLightTheme && cacheMarketProfileScanner50Charts[idx].ScannerFontSize == scannerFontSize && cacheMarketProfileScanner50Charts[idx].EqualsInput(input))
						return cacheMarketProfileScanner50Charts[idx];
			return CacheIndicator<MarketProfileScanner50Charts>(new MarketProfileScanner50Charts(){ DisplayPrevDayValues = displayPrevDayValues, DisplayTodayValues = displayTodayValues, DisplayDailySma = displayDailySma, SmaNumberOfDays = smaNumberOfDays, DisplayPrevDayMpLevels = displayPrevDayMpLevels, DisplayIbValues = displayIbValues, DisplayCurrentDayMpLevels = displayCurrentDayMpLevels, AtrNumberOfDays = atrNumberOfDays, ProfileDistributionOneSigma = profileDistributionOneSigma, MinTickSizeParam = minTickSizeParam, InitialBalanceCandleCount = initialBalanceCandleCount, AutoRefreshEnabled = autoRefreshEnabled, AutoRefreshSeconds = autoRefreshSeconds, UseClassicLightTheme = useClassicLightTheme, ScannerFontSize = scannerFontSize }, input, ref cacheMarketProfileScanner50Charts);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MarketProfileScanner50Charts MarketProfileScanner50Charts(bool displayPrevDayValues, bool displayTodayValues, bool displayDailySma, int smaNumberOfDays, bool displayPrevDayMpLevels, bool displayIbValues, bool displayCurrentDayMpLevels, int atrNumberOfDays, double profileDistributionOneSigma, double minTickSizeParam, int initialBalanceCandleCount, bool autoRefreshEnabled, int autoRefreshSeconds, bool useClassicLightTheme, int scannerFontSize)
		{
			return indicator.MarketProfileScanner50Charts(Input, displayPrevDayValues, displayTodayValues, displayDailySma, smaNumberOfDays, displayPrevDayMpLevels, displayIbValues, displayCurrentDayMpLevels, atrNumberOfDays, profileDistributionOneSigma, minTickSizeParam, initialBalanceCandleCount, autoRefreshEnabled, autoRefreshSeconds, useClassicLightTheme, scannerFontSize);
		}

		public Indicators.MarketProfileScanner50Charts MarketProfileScanner50Charts(ISeries<double> input , bool displayPrevDayValues, bool displayTodayValues, bool displayDailySma, int smaNumberOfDays, bool displayPrevDayMpLevels, bool displayIbValues, bool displayCurrentDayMpLevels, int atrNumberOfDays, double profileDistributionOneSigma, double minTickSizeParam, int initialBalanceCandleCount, bool autoRefreshEnabled, int autoRefreshSeconds, bool useClassicLightTheme, int scannerFontSize)
		{
			return indicator.MarketProfileScanner50Charts(input, displayPrevDayValues, displayTodayValues, displayDailySma, smaNumberOfDays, displayPrevDayMpLevels, displayIbValues, displayCurrentDayMpLevels, atrNumberOfDays, profileDistributionOneSigma, minTickSizeParam, initialBalanceCandleCount, autoRefreshEnabled, autoRefreshSeconds, useClassicLightTheme, scannerFontSize);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MarketProfileScanner50Charts MarketProfileScanner50Charts(bool displayPrevDayValues, bool displayTodayValues, bool displayDailySma, int smaNumberOfDays, bool displayPrevDayMpLevels, bool displayIbValues, bool displayCurrentDayMpLevels, int atrNumberOfDays, double profileDistributionOneSigma, double minTickSizeParam, int initialBalanceCandleCount, bool autoRefreshEnabled, int autoRefreshSeconds, bool useClassicLightTheme, int scannerFontSize)
		{
			return indicator.MarketProfileScanner50Charts(Input, displayPrevDayValues, displayTodayValues, displayDailySma, smaNumberOfDays, displayPrevDayMpLevels, displayIbValues, displayCurrentDayMpLevels, atrNumberOfDays, profileDistributionOneSigma, minTickSizeParam, initialBalanceCandleCount, autoRefreshEnabled, autoRefreshSeconds, useClassicLightTheme, scannerFontSize);
		}

		public Indicators.MarketProfileScanner50Charts MarketProfileScanner50Charts(ISeries<double> input , bool displayPrevDayValues, bool displayTodayValues, bool displayDailySma, int smaNumberOfDays, bool displayPrevDayMpLevels, bool displayIbValues, bool displayCurrentDayMpLevels, int atrNumberOfDays, double profileDistributionOneSigma, double minTickSizeParam, int initialBalanceCandleCount, bool autoRefreshEnabled, int autoRefreshSeconds, bool useClassicLightTheme, int scannerFontSize)
		{
			return indicator.MarketProfileScanner50Charts(input, displayPrevDayValues, displayTodayValues, displayDailySma, smaNumberOfDays, displayPrevDayMpLevels, displayIbValues, displayCurrentDayMpLevels, atrNumberOfDays, profileDistributionOneSigma, minTickSizeParam, initialBalanceCandleCount, autoRefreshEnabled, autoRefreshSeconds, useClassicLightTheme, scannerFontSize);
		}
	}
}

#endregion
