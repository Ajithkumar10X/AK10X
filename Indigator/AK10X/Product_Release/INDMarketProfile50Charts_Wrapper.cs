// AK10X Suite - Protected Binary Wrapper for NinjaTrader 8 (INDMarketProfile50Charts)
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
		private INDMarketProfile50Charts[] cacheINDMarketProfile50Charts;
		public INDMarketProfile50Charts INDMarketProfile50Charts(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return INDMarketProfile50Charts(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}

		public INDMarketProfile50Charts INDMarketProfile50Charts(ISeries<double> input, double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			if (cacheINDMarketProfile50Charts != null)
				for (int idx = 0; idx < cacheINDMarketProfile50Charts.Length; idx++)
					if (cacheINDMarketProfile50Charts[idx] != null && cacheINDMarketProfile50Charts[idx].TpoSize == tpoSize && cacheINDMarketProfile50Charts[idx].TpoIntervalMinutes == tpoIntervalMinutes && cacheINDMarketProfile50Charts[idx].ValueAreaPercent == valueAreaPercent && cacheINDMarketProfile50Charts[idx].InitialBalanceMinutes == initialBalanceMinutes && cacheINDMarketProfile50Charts[idx].DaysToDisplay == daysToDisplay && cacheINDMarketProfile50Charts[idx].FilterByTradingHours == filterByTradingHours && cacheINDMarketProfile50Charts[idx].MarketOpen == marketOpen && cacheINDMarketProfile50Charts[idx].MarketClose == marketClose && cacheINDMarketProfile50Charts[idx].SessionLabel == sessionLabel && cacheINDMarketProfile50Charts[idx].SplitLatestDay == splitLatestDay && cacheINDMarketProfile50Charts[idx].ShowVolumeProfile == showVolumeProfile && cacheINDMarketProfile50Charts[idx].ShowInitialBalance == showInitialBalance && cacheINDMarketProfile50Charts[idx].ShowSinglePrints == showSinglePrints && cacheINDMarketProfile50Charts[idx].ShowPoorHighLow == showPoorHighLow && cacheINDMarketProfile50Charts[idx].ProfileWidth == profileWidth && cacheINDMarketProfile50Charts[idx].FontSize == fontSize && cacheINDMarketProfile50Charts[idx].ShowPoorHigh == showPoorHigh && cacheINDMarketProfile50Charts[idx].ShowPoorLow == showPoorLow && cacheINDMarketProfile50Charts[idx].ShowSwingHigh == showSwingHigh && cacheINDMarketProfile50Charts[idx].ShowSwingLow == showSwingLow && cacheINDMarketProfile50Charts[idx].ShowVirginVAH == showVirginVAH && cacheINDMarketProfile50Charts[idx].ShowVirginVAL == showVirginVAL && cacheINDMarketProfile50Charts[idx].ShowVirginVPoC == showVirginVPoC && cacheINDMarketProfile50Charts[idx].ShowVirginVPPoC == showVirginVPPoC && cacheINDMarketProfile50Charts[idx].ShowSummary == showSummary && cacheINDMarketProfile50Charts[idx].FixSummaryBoxAtBottom == fixSummaryBoxAtBottom && cacheINDMarketProfile50Charts[idx].MinimizeSummaryBoxes == minimizeSummaryBoxes && cacheINDMarketProfile50Charts[idx].ShowTopLeftBox == showTopLeftBox && cacheINDMarketProfile50Charts[idx].ShowLogoInTopBox == showLogoInTopBox && cacheINDMarketProfile50Charts[idx].MinimizeTopLeftBox == minimizeTopLeftBox && cacheINDMarketProfile50Charts[idx].ShowChartButtons == showChartButtons && cacheINDMarketProfile50Charts[idx].EqualsInput(input))
						return cacheINDMarketProfile50Charts[idx];
			return CacheIndicator<INDMarketProfile50Charts>(new INDMarketProfile50Charts(){ TpoSize = tpoSize, TpoIntervalMinutes = tpoIntervalMinutes, ValueAreaPercent = valueAreaPercent, InitialBalanceMinutes = initialBalanceMinutes, DaysToDisplay = daysToDisplay, FilterByTradingHours = filterByTradingHours, MarketOpen = marketOpen, MarketClose = marketClose, SessionLabel = sessionLabel, SplitLatestDay = splitLatestDay, ShowVolumeProfile = showVolumeProfile, ShowInitialBalance = showInitialBalance, ShowSinglePrints = showSinglePrints, ShowPoorHighLow = showPoorHighLow, ProfileWidth = profileWidth, FontSize = fontSize, ShowPoorHigh = showPoorHigh, ShowPoorLow = showPoorLow, ShowSwingHigh = showSwingHigh, ShowSwingLow = showSwingLow, ShowVirginVAH = showVirginVAH, ShowVirginVAL = showVirginVAL, ShowVirginVPoC = showVirginVPoC, ShowVirginVPPoC = showVirginVPPoC, ShowSummary = showSummary, FixSummaryBoxAtBottom = fixSummaryBoxAtBottom, MinimizeSummaryBoxes = minimizeSummaryBoxes, ShowTopLeftBox = showTopLeftBox, ShowLogoInTopBox = showLogoInTopBox, MinimizeTopLeftBox = minimizeTopLeftBox, ShowChartButtons = showChartButtons }, input, ref cacheINDMarketProfile50Charts);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.INDMarketProfile50Charts INDMarketProfile50Charts(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return indicator.INDMarketProfile50Charts(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}

		public Indicators.INDMarketProfile50Charts INDMarketProfile50Charts(ISeries<double> input , double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return indicator.INDMarketProfile50Charts(input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.INDMarketProfile50Charts INDMarketProfile50Charts(double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return indicator.INDMarketProfile50Charts(Input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}

		public Indicators.INDMarketProfile50Charts INDMarketProfile50Charts(ISeries<double> input , double tpoSize, int tpoIntervalMinutes, int valueAreaPercent, int initialBalanceMinutes, int daysToDisplay, bool filterByTradingHours, int marketOpen, int marketClose, string sessionLabel, bool splitLatestDay, bool showVolumeProfile, bool showInitialBalance, bool showSinglePrints, bool showPoorHighLow, int profileWidth, int fontSize, bool showPoorHigh, bool showPoorLow, bool showSwingHigh, bool showSwingLow, bool showVirginVAH, bool showVirginVAL, bool showVirginVPoC, bool showVirginVPPoC, bool showSummary, bool fixSummaryBoxAtBottom, bool minimizeSummaryBoxes, bool showTopLeftBox, bool showLogoInTopBox, bool minimizeTopLeftBox, bool showChartButtons)
		{
			return indicator.INDMarketProfile50Charts(input, tpoSize, tpoIntervalMinutes, valueAreaPercent, initialBalanceMinutes, daysToDisplay, filterByTradingHours, marketOpen, marketClose, sessionLabel, splitLatestDay, showVolumeProfile, showInitialBalance, showSinglePrints, showPoorHighLow, profileWidth, fontSize, showPoorHigh, showPoorLow, showSwingHigh, showSwingLow, showVirginVAH, showVirginVAL, showVirginVPoC, showVirginVPPoC, showSummary, fixSummaryBoxAtBottom, minimizeSummaryBoxes, showTopLeftBox, showLogoInTopBox, minimizeTopLeftBox, showChartButtons);
		}
	}
}

#endregion
