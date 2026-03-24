using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace CardInventoryManager.ViewModels;

public class InventoryViewModel : INotifyPropertyChanged
{
    private readonly InventoryService _inventoryService;
    private readonly SupportCardLoaderService _cardLoader;
    private readonly string _imagesDir;
    private string _filterText = string.Empty;
    private string _filterRarity = "すべて";
    private TypeFilterOption _filterType = null!;
    private string _filterOwned = "すべて";
    private string _statusMessage = string.Empty;

    public ObservableCollection<CardInventoryItemViewModel> AllItems { get; } = new();
    public ObservableCollection<CardInventoryItemViewModel> FilteredItems { get; } = new();

    public List<string> RarityOptions { get; } = new() { "すべて", "SSR", "SR", "R" };
    public List<TypeFilterOption> TypeOptions { get; } = new()
    {
        new("すべて", "すべて"), new("vo", "Vo"), new("da", "Da"), new("vi", "Vi"), new("as", "As")
    };
    public List<string> OwnedOptions { get; } = new() { "すべて", "所持", "未所持" };
    public List<int> UncapOptions { get; } = new() { 0, 1, 2, 3, 4 };
    public string FilterText
    {
        get => _filterText;
        set { _filterText = value; OnPropertyChanged(); ApplyFilter(); }
    }

    public string FilterRarity
    {
        get => _filterRarity;
        set { _filterRarity = value; OnPropertyChanged(); ApplyFilter(); }
    }

    public TypeFilterOption FilterType
    {
        get => _filterType;
        set { _filterType = value; OnPropertyChanged(); ApplyFilter(); }
    }

    public string FilterOwned
    {
        get => _filterOwned;
        set { _filterOwned = value; OnPropertyChanged(); ApplyFilter(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public int OwnedCount => AllItems.Count(i => i.Owned);
    public int TotalCount => AllItems.Count;
    public int FilteredCount => FilteredItems.Count;

    public ICommand SaveCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }

    public InventoryViewModel()
    {
        _filterType = TypeOptions[0];
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dataDir = FindDataDir(baseDir);

        var yamlService = new YamlDataService();
        _cardLoader = new SupportCardLoaderService(yamlService, Path.Combine(dataDir, "SupportCards"));
        _inventoryService = new InventoryService(Path.Combine(dataDir, "Inventory", "inventory.yaml"));
        _imagesDir = Path.Combine(dataDir, "Images");

        SaveCommand = new RelayCommand(Save);
        SelectAllCommand = new RelayCommand(() =>
        {
            foreach (var item in FilteredItems) item.Owned = true;
            UpdateCounts();
        });
        DeselectAllCommand = new RelayCommand(() =>
        {
            foreach (var item in FilteredItems) item.Owned = false;
            UpdateCounts();
        });

        LoadData();
    }

    private void LoadData()
    {
        try
        {
            var allCards = _cardLoader.LoadAllCards();
            var existing = _inventoryService.Load();
            var entries = _inventoryService.InitializeFromCards(allCards, existing);

            var cardMap = allCards.ToDictionary(c => c.Id);

            // 画像マッピング読み込み (カード名 → ファイル名)
            var imageMap = LoadImageMapping();

            AllItems.Clear();
            foreach (var entry in entries)
            {
                if (!cardMap.TryGetValue(entry.CardId, out var card)) continue;
                var imagePath = ResolveImagePath(card.Name, imageMap);
                var vm = new CardInventoryItemViewModel(entry, card, imagePath);
                vm.PropertyChanged += (_, _) => UpdateCounts();
                AllItems.Add(vm);
            }

            ApplyFilter();
            UpdateCounts();
            var imgCount = AllItems.Count(i => i.ImagePath != null);
            StatusMessage = $"{allCards.Count}枚のカードを読み込み (画像 {imgCount}枚)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"読み込みエラー: {ex.Message}";
        }
    }

    private Dictionary<string, string> LoadImageMapping()
    {
        var map = new Dictionary<string, string>();
        var tsvPath = Path.Combine(_imagesDir, "_mapping.tsv");
        if (!File.Exists(tsvPath)) return map;

        foreach (var line in File.ReadAllLines(tsvPath, System.Text.Encoding.UTF8))
        {
            if (line.StartsWith("card_id")) continue; // ヘッダー
            var parts = line.Split('\t');
            if (parts.Length >= 3)
            {
                var cardName = parts[1].Trim();
                var filename = parts[2].Trim();
                if (!string.IsNullOrEmpty(cardName) && filename != "ERROR")
                    map[cardName] = filename;
            }
        }
        return map;
    }

    private string? ResolveImagePath(string cardName, Dictionary<string, string> imageMap)
    {
        // 完全一致
        if (imageMap.TryGetValue(cardName, out var filename))
        {
            var path = Path.Combine(_imagesDir, filename);
            if (File.Exists(path)) return path;
        }

        // 表記ゆれ対応: ～⇔〜, 全角半角, !⇔！ 等を正規化してマッチ
        var normalized = NormalizeName(cardName);
        foreach (var kvp in imageMap)
        {
            if (NormalizeName(kvp.Key) == normalized)
            {
                var path = Path.Combine(_imagesDir, kvp.Value);
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }

    private static string NormalizeName(string name)
    {
        return name
            .Replace("～", "~").Replace("〜", "~")
            .Replace("！", "!").Replace("？", "?")
            .Replace("♡", "").Replace("♪", "")
            .Replace("&#9825;", "")
            .Replace("☆", "").Replace("★", "")
            .Replace("\u3000", " ")
            .Trim();
    }

    private static int RarityOrder(string rarity) => rarity switch
    {
        "SSR" => 0, "SR" => 1, "R" => 2, _ => 3
    };

    private static int TypeOrder(string type) => type switch
    {
        "vo" => 0, "da" => 1, "vi" => 2, _ => 3
    };

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        var filtered = AllItems.Where(item =>
        {
            if (!string.IsNullOrEmpty(FilterText) &&
                !item.CardName.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
                return false;

            if (FilterRarity != "すべて" && item.Rarity != FilterRarity)
                return false;

            if (FilterType.Value != "すべて" && item.CardType != FilterType.Value)
                return false;

            if (FilterOwned == "所持" && !item.Owned) return false;
            if (FilterOwned == "未所持" && item.Owned) return false;

            return true;
        })
        .OrderBy(i => RarityOrder(i.Rarity))
        .ThenBy(i => TypeOrder(i.CardType));

        foreach (var item in filtered)
            FilteredItems.Add(item);
        OnPropertyChanged(nameof(FilteredCount));
    }

    private void Save()
    {
        try
        {
            var entries = AllItems.Select(i => new CardInventoryEntry
            {
                CardId = i.CardId,
                Owned = i.Owned,
                Uncap = i.Uncap
            }).ToList();

            _inventoryService.Save(entries);
            StatusMessage = $"保存しました ({OwnedCount}/{TotalCount}枚所持)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}";
        }
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(OwnedCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(FilteredCount));
    }

    /// <summary>
    /// リポジトリルートのDataディレクトリを優先的に探す。見つからなければ出力ディレクトリのDataを使う。
    /// </summary>
    private static string FindDataDir(string baseDir)
    {
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GakumasuCalc.slnx")))
                return Path.Combine(dir.FullName, "Data");
            dir = dir.Parent;
        }
        return Path.Combine(baseDir, "Data");
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class CardInventoryItemViewModel : INotifyPropertyChanged
{
    private bool _owned;
    private int _uncap;

    public string CardId { get; }
    public string CardName { get; }
    public string Rarity { get; }
    public string CardType { get; }
    public string Plan { get; }
    public string? ImagePath { get; }

    public bool Owned
    {
        get => _owned;
        set { _owned = value; OnPropertyChanged(); OnPropertyChanged(nameof(OpacityValue)); OnPropertyChanged(nameof(OwnedBorderBrush)); OnPropertyChanged(nameof(OwnedBorderThickness)); }
    }

    public int Uncap
    {
        get => _uncap;
        set { _uncap = Math.Clamp(value, 0, 4); OnPropertyChanged(); OnPropertyChanged(nameof(UncapDisplay)); }
    }

    public double OpacityValue => Owned ? 1.0 : 0.35;
    public string UncapDisplay => Uncap > 0 ? $"{Uncap}凸" : "-";
    public string OwnedBorderBrush => Owned ? "#4CAF50" : "Transparent";
    public string OwnedBorderThickness => Owned ? "2" : "0";

    public string TypeDisplay => CardType switch
    {
        "vo" => "Vo", "da" => "Da", "vi" => "Vi", "as" => "As", _ => CardType
    };

    public string PlanDisplay => Plan switch
    {
        "sense" => "センス", "logic" => "ロジック", "anomaly" => "アノマリー",
        "free" => "フリー", _ => Plan
    };

    public CardInventoryItemViewModel(CardInventoryEntry entry, SupportCard card, string? imagePath)
    {
        CardId = entry.CardId;
        CardName = card.Name;
        Rarity = card.Rarity;
        CardType = card.Type;
        Plan = card.Plan;
        ImagePath = imagePath;
        _owned = entry.Owned;
        _uncap = entry.Uncap;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class CardTypeToBrushConverter : System.Windows.Data.IValueConverter
{
    public static readonly CardTypeToBrushConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var type = value?.ToString() ?? "";
        return type switch
        {
            "vo" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xE0, 0xEC)), // ピンク
            "da" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xEE, 0xFF)), // 青
            "vi" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xF9, 0xE0)), // 黄色
            _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xF5, 0xE9))    // 黄緑 (free等)
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute) : this(_ => execute()) { }
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
}

public class TypeFilterOption
{
    public string Value { get; }
    public string Display { get; }
    public TypeFilterOption(string value, string display) { Value = value; Display = display; }
    public override string ToString() => Display;
}
