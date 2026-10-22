using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WindowsApp.Ninja.Models;

public class DomLevel : INotifyPropertyChanged
{
    private double _price;
    private string _formattedPrice = string.Empty;
    private int _bidVolume;
    private int _askVolume;
    private long _tradedVolume;
    private double _volumeProfilePercentage;
    private int _workingBuyQuantity;
    private int _workingSellQuantity;
    private bool _isInsideBid;
    private bool _isInsideAsk;
    private bool _isLastTraded;

    public double Price
    {
        get => _price;
        set { _price = value; OnPropertyChanged(); }
    }

    public string FormattedPrice
    {
        get => _formattedPrice;
        set { _formattedPrice = value; OnPropertyChanged(); }
    }

    public int BidVolume
    {
        get => _bidVolume;
        set { _bidVolume = value; OnPropertyChanged(); }
    }

    public int AskVolume
    {
        get => _askVolume;
        set { _askVolume = value; OnPropertyChanged(); }
    }

    public long TradedVolume
    {
        get => _tradedVolume;
        set { _tradedVolume = value; OnPropertyChanged(); }
    }

    public double VolumeProfilePercentage
    {
        get => _volumeProfilePercentage;
        set { _volumeProfilePercentage = value; OnPropertyChanged(); }
    }

    public int WorkingBuyQuantity
    {
        get => _workingBuyQuantity;
        set { _workingBuyQuantity = value; OnPropertyChanged(); }
    }

    public int WorkingSellQuantity
    {
        get => _workingSellQuantity;
        set { _workingSellQuantity = value; OnPropertyChanged(); }
    }

    public bool IsInsideBid
    {
        get => _isInsideBid;
        set { _isInsideBid = value; OnPropertyChanged(); }
    }

    public bool IsInsideAsk
    {
        get => _isInsideAsk;
        set { _isInsideAsk = value; OnPropertyChanged(); }
    }

    public bool IsLastTraded
    {
        get => _isLastTraded;
        set { _isLastTraded = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
