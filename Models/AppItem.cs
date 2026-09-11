using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace AppLauncher.Models;

public enum AppStatus
{
    Active = 0,
    Hidden = 1,
    Blacklisted = 2
}

public class AppItem : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string? _customName;
    private string _exePath = string.Empty;
    private string _searchableFileName = string.Empty;
    private string _workingDirectory = string.Empty;
    private string? _iconCachePath;
    private long _lastWriteTimeUtcTicks;
    private long _fileSize;
    private string _source = "Scanner";
    private AppStatus _status = AppStatus.Active;
    private bool _isPinned;
    private int _pinOrder = -1;
    private int _launchCount;
    private DateTime? _lastLaunchedUtc;
    private BitmapSource? _cachedIconImage;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Name
    {
        // Name is the stable discovered name persisted in the cache.
        // DisplayName applies the optional user alias for presentation.
        get => _name;
        set
        {
            if (SetField(ref _name, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string? CustomName
    {
        get => _customName;
        set
        {
            if (SetField(ref _customName, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(_customName) ? _name : _customName;

    [JsonIgnore]
    public string SearchableFileName => _searchableFileName;

    public string ExePath
    {
        get => _exePath;
        set
        {
            if (SetField(ref _exePath, value))
            {
                _searchableFileName = string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFileNameWithoutExtension(value);
            }
        }
    }

    public string WorkingDirectory
    {
        get => string.IsNullOrWhiteSpace(_workingDirectory) && !string.IsNullOrWhiteSpace(_exePath) 
            ? (Path.GetDirectoryName(_exePath) ?? string.Empty) 
            : _workingDirectory;
        set => SetField(ref _workingDirectory, value);
    }

    public string? IconCachePath
    {
        get => _iconCachePath;
        set
        {
            if (SetField(ref _iconCachePath, value))
            {
                _cachedIconImage = null;
                OnPropertyChanged(nameof(IconImage));
            }
        }
    }

    public long LastWriteTimeUtcTicks
    {
        get => _lastWriteTimeUtcTicks;
        set => SetField(ref _lastWriteTimeUtcTicks, value);
    }

    public long FileSize
    {
        get => _fileSize;
        set => SetField(ref _fileSize, value);
    }

    public string Source
    {
        get => _source;
        set => SetField(ref _source, value);
    }

    public AppStatus Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetField(ref _isPinned, value);
    }

    public int PinOrder
    {
        get => _pinOrder;
        set => SetField(ref _pinOrder, value);
    }

    public int LaunchCount
    {
        get => _launchCount;
        set => SetField(ref _launchCount, value);
    }

    public DateTime? LastLaunchedUtc
    {
        get => _lastLaunchedUtc;
        set => SetField(ref _lastLaunchedUtc, value);
    }

    [JsonIgnore]
    public BitmapSource? IconImage
    {
        get
        {
            if (_cachedIconImage == null && !string.IsNullOrEmpty(_iconCachePath) && File.Exists(_iconCachePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    bitmap.DecodePixelWidth = 64;
                    bitmap.UriSource = new Uri(_iconCachePath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    _cachedIconImage = bitmap;
                }
                catch
                {
                    _cachedIconImage = null;
                }
            }
            return _cachedIconImage;
        }
        set => SetField(ref _cachedIconImage, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
