using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using WindowsApp.Ninja.OrderFlow.Models;

namespace WindowsApp.Ninja.OrderFlow.Services;

public class RawTick
{
    public double Price { get; set; }
    public long Volume { get; set; }
    public DateTime Time { get; set; }
    public double Bid { get; set; }
    public double Ask { get; set; }
}

public class NinjaTraderTickFeedService
{
    private static readonly Lazy<NinjaTraderTickFeedService> _instance = new(() => new NinjaTraderTickFeedService());
    public static NinjaTraderTickFeedService Instance => _instance.Value;

    public const string Nt8InstallDir = @"C:\Program Files\NinjaTrader 8\bin";
    public static readonly string Nt8DocumentsDbDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "NinjaTrader 8", "db", "tick");

    private Assembly? _ntCoreAssembly;
    private MethodInfo? _readMethod;
    private MethodInfo? _readReplayMethod;
    private PropertyInfo? _priceProp;
    private PropertyInfo? _volumeProp;
    private PropertyInfo? _timeProp;
    private PropertyInfo? _bidProp;
    private PropertyInfo? _askProp;
    private bool _isInitialized = false;

    public bool IsAvailable => Directory.Exists(Nt8DocumentsDbDir);

    public NinjaTraderTickFeedService()
    {
        InitializeNt8Assembly();
    }

    private void InitializeNt8Assembly()
    {
        if (_isInitialized) return;
        try
        {
            string coreDll = Path.Combine(Nt8InstallDir, "NinjaTrader.Core.dll");
            if (!File.Exists(coreDll)) return;

            AppDomain.CurrentDomain.AssemblyResolve += (s, args) =>
            {
                string name = args.Name.Split(',')[0];
                if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase)) return null;
                string p = Path.Combine(Nt8InstallDir, name + ".dll");
                if (File.Exists(p))
                {
                    try { return Assembly.LoadFrom(p); } catch { }
                }
                return null;
            };

            _ntCoreAssembly = Assembly.LoadFrom(coreDll);
            var bbType = _ntCoreAssembly.GetType("NinjaTrader.Data.BarsBytes");
            if (bbType != null)
            {
                _readMethod = bbType.GetMethod("Read", new[] { typeof(string) });
                _readReplayMethod = bbType.GetMethod("ReadReplay", new[] { typeof(string) });
            }

            var replayType = _ntCoreAssembly.GetType("NinjaTrader.Data.ReplayObject");
            if (replayType != null)
            {
                _priceProp = replayType.GetProperty("Price");
                _volumeProp = replayType.GetProperty("Volume");
                _timeProp = replayType.GetProperty("Time");
                _bidProp = replayType.GetProperty("Bid");
                _askProp = replayType.GetProperty("Ask");
            }

            _isInitialized = _readMethod != null && _readReplayMethod != null;
        }
        catch { }
    }

    public List<RawTick> LoadTicksForSymbol(string symbol, DateTime? targetDate = null)
    {
        var result = new List<RawTick>();
        if (!_isInitialized) InitializeNt8Assembly();
        if (!_isInitialized || _readMethod == null || _readReplayMethod == null) return result;

        string cleanSymbol = symbol.Split(' ')[0].Trim();
        string symbolDir = Path.Combine(Nt8DocumentsDbDir, cleanSymbol);
        if (!Directory.Exists(symbolDir))
        {
            // Try matching without _I
            string altSymbol = cleanSymbol.Replace("_I", "");
            symbolDir = Path.Combine(Nt8DocumentsDbDir, altSymbol);
            if (!Directory.Exists(symbolDir)) return result;
        }

        DateTime date = targetDate ?? DateTime.Today;
        string datePrefix = date.ToString("yyyyMMdd");

        var files = Directory.GetFiles(symbolDir, $"{datePrefix}*.Last.ncd")
                             .OrderBy(f => f)
                             .ToList();

        // If today has no files, try the most recent date in directory
        if (files.Count == 0)
        {
            var allNcd = Directory.GetFiles(symbolDir, "*.Last.ncd")
                                  .OrderBy(f => f)
                                  .ToList();
            if (allNcd.Count > 0)
            {
                string lastFileName = Path.GetFileName(allNcd.Last());
                if (lastFileName.Length >= 8)
                {
                    string fallbackPrefix = lastFileName.Substring(0, 8);
                    files = Directory.GetFiles(symbolDir, $"{fallbackPrefix}*.Last.ncd")
                                     .OrderBy(f => f)
                                     .ToList();
                }
            }
        }

        foreach (var file in files)
        {
            try
            {
                object? bb = _readMethod.Invoke(null, new object[] { file });
                if (bb == null) continue;

                object? tuple = _readReplayMethod.Invoke(bb, new object[] { file });
                if (tuple == null) continue;

                var tupleType = tuple.GetType();
                var item2Prop = tupleType.GetProperty("Item2");
                if (item2Prop?.GetValue(tuple) is System.Collections.IEnumerable rawList)
                {
                    foreach (var rawObj in rawList)
                    {
                        if (rawObj == null) continue;
                        double price = _priceProp?.GetValue(rawObj) is double p ? p : 0.0;
                        long vol = _volumeProp?.GetValue(rawObj) is long v ? v : 1;
                        DateTime time = _timeProp?.GetValue(rawObj) is DateTime t ? t : DateTime.Now;
                        double bid = _bidProp?.GetValue(rawObj) is double b ? b : price;
                        double ask = _askProp?.GetValue(rawObj) is double a ? a : price;

                        result.Add(new RawTick
                        {
                            Price = price,
                            Volume = vol,
                            Time = time,
                            Bid = bid,
                            Ask = ask
                        });
                    }
                }
            }
            catch { }
        }

        return result;
    }
}
