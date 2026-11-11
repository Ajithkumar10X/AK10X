// AK10X Suite - Protected Binary Wrapper for NinjaTrader 8 (AK10XOrderflow)
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
