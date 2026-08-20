using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public class EffectBreakdownViewModel
{
    public string Reason { get; set; } = string.Empty;
    public string Stat { get; set; } = string.Empty;
    public double Value { get; set; }

    /// <summary>UI 表示: 0 のときは空 (ヘッダ行用)、そうでなければ「+80」「-3」</summary>
    public string ValueDisplay =>
        Value == 0 ? string.Empty : (Value > 0 ? $"+{Value:0.#}" : $"{Value:0.#}");

    /// <summary>属性カラー (vo=赤系 / da=青系 / vi=黄系 / all=灰)</summary>
    public System.Windows.Media.Brush StatColor => Stat switch
    {
        "vo" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x8A)),
        "da" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x9F, 0xFF)),
        "vi" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xA3, 0x00)),
        _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66)),
    };
}

/// <summary>
/// アビリティまとめ (行動別) の1行。「授業終了 Vo+75 (45+30) ×6回  +450」形式で表示する。
/// </summary>
public class AbilitySummaryEntryViewModel
{
    public string TriggerName { get; set; } = string.Empty;
    public string Stat { get; set; } = string.Empty;
    public double PerFire { get; set; }
    public List<double> Parts { get; set; } = new();
    public int Fires { get; set; }
    public int? MaxCount { get; set; }
    public double Total { get; set; }

    private string StatLabel => Stat switch
    {
        "vo" => "Vo",
        "da" => "Da",
        "vi" => "Vi",
        "all" => "All",
        _ => Stat,
    };

    /// <summary>左側の式表示: 「授業終了 Vo+75 (45+30) ×6回（上限2回）」。parts が2件以上のとき内訳を、上限が効いているとき「（上限N回）」を併記。</summary>
    public string FormulaDisplay
    {
        get
        {
            var parts = Parts.Count > 1
                ? $" ({string.Join("+", Parts.Select(p => p.ToString("0.#")))})"
                : string.Empty;
            var cap = MaxCount.HasValue ? $"（上限{MaxCount}回）" : string.Empty;
            return $"{TriggerName} {StatLabel}+{PerFire:0.#}{parts} ×{Fires}回{cap}";
        }
    }

    /// <summary>右側の合計表示: 「+450」</summary>
    public string TotalDisplay => $"+{Total:0.#}";

    /// <summary>
    /// 合計値の表示カラー。アンバー背景で視認性を確保するため、テキスト用の濃色を使う
    /// (Vi=darkgoldenrod #B8860B。明色 #FFD36B は黄色背景で読めない)。Web版 --color-*-text と対応。
    /// 行動を取っていない (×0回) 項目は寄与0なので控えめなグレーで表示する。
    /// </summary>
    public System.Windows.Media.Brush StatColor => Total == 0
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9C, 0xA3, 0xAF))
        : Stat switch
    {
        "vo" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC2, 0x18, 0x5B)),
        "da" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x15, 0x65, 0xC0)),
        "vi" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB8, 0x86, 0x0B)),
        "all" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32)),
        _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66)),
    };
}

public class DeckCardViewModel : ViewModelBase
{
    private bool _isExpanded;

    public string CardId { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public string CardType { get; set; } = string.Empty;
    public string CardRarity { get; set; } = string.Empty;
    public string CardPlan { get; set; } = string.Empty;
    public int StatValue { get; set; }
    public int TeamBonusTotal { get; set; }
    public List<(string CardName, int Value)> TeamBonusContributors { get; set; } = new();
    public int RawVo { get; set; }
    public int RawDa { get; set; }
    public int RawVi { get; set; }

    /// <summary>クリック展開で表示する内訳行</summary>
    public ObservableCollection<EffectBreakdownViewModel> Breakdowns { get; set; } = new();

    /// <summary>クリックで展開・折りたたみ</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    /// <summary>クリック時に IsExpanded をトグル</summary>
    public System.Windows.Input.ICommand ToggleExpandCommand =>
        new RelayCommand(() => IsExpanded = !IsExpanded);

    /// <summary>UI 表示用: 「+71」または「+107 (+240)」形式 (詳細はクリック展開で見れる)</summary>
    public string StatValueDisplay =>
        TeamBonusTotal > 0
            ? $"+{StatValue} (+{TeamBonusTotal})"
            : $"+{StatValue}";
    public string DeckLabel { get; set; } = string.Empty;
    public string BreakdownText { get; set; } = string.Empty;
    public bool IsRental { get; set; }
    public bool IsRequired { get; set; }
    public bool HasSpRate { get; set; }
    public int UncapLevel { get; set; }

    /// <summary>表示用: レンタルは4凸借用、それ以外は所持凸数。"1凸"〜"4凸"。</summary>
    public string UncapDisplay => $"{(IsRental ? 4 : UncapLevel)}凸";

    public System.Windows.Visibility SpRateVisibility =>
        HasSpRate ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    // 除外ボタンは必須カード以外で表示
    public System.Windows.Visibility ExcludeButtonVisibility =>
        IsRequired ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public string CardTypeDisplay => CardType switch
    {
        "vo" => "Vo",
        "da" => "Da",
        "vi" => "Vi",
        "all" => "All",
        _ => CardType
    };

    public string CardPlanDisplay => CardPlan switch
    {
        "sense" => "セ",
        "logic" => "ロ",
        "anomaly" => "ア",
        "free" => "フ",
        _ => ""
    };

    // 属性バッジの色
    public System.Windows.Media.Brush TypeBadgeForeground => CardType switch
    {
        "vo" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x8A)),
        "da" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x9F, 0xFF)),
        "vi" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD3, 0x6B)),
        "all" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)),
        _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55)),
    };

    public System.Windows.Media.Brush TypeBadgeBackground => CardType switch
    {
        "vo" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xEB, 0xEE)),
        "da" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE3, 0xF2, 0xFD)),
        "vi" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xF8, 0xE1)),
        "all" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xF5, 0xE9)),
        _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF0, 0xF0, 0xF0)),
    };

    // カード名の色 (レンタル=オレンジ、必須=紫、通常=黒)
    public System.Windows.Media.Brush CardNameForeground =>
        IsRental ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEA, 0x58, 0x0C))
        : IsRequired ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7C, 0x3A, 0xED))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33));
}

public class PatternResultViewModel : ViewModelBase
{
    private bool _isSelected;

    public string Label { get; set; } = string.Empty;
    public ObservableCollection<DeckCardViewModel> Cards { get; set; } = new();
    public int Total => Cards.Sum(c => c.StatValue);
    public int Index { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
