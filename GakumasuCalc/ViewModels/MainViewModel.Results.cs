using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public partial class MainViewModel
{
    private PatternResultViewModel? _selectedPattern;
    public PatternResultViewModel? SelectedPattern
    {
        get => _selectedPattern;
        set
        {
            if (SetProperty(ref _selectedPattern, value) && value != null)
            {
                // 選択状態の更新
                foreach (var p in PatternResults)
                    p.IsSelected = (p == value);
                ApplySelectedPattern(value.Index);
            }
        }
    }

    // 計算結果
    public CalculationResult? Result
    {
        get => _result;
        private set
        {
            SetProperty(ref _result, value);
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(ResultVoRaw));
            OnPropertyChanged(nameof(ResultDaRaw));
            OnPropertyChanged(nameof(ResultViRaw));
            OnPropertyChanged(nameof(ResultVo));
            OnPropertyChanged(nameof(ResultDa));
            OnPropertyChanged(nameof(ResultVi));
            OnPropertyChanged(nameof(ResultVoOverflow));
            OnPropertyChanged(nameof(ResultDaOverflow));
            OnPropertyChanged(nameof(ResultViOverflow));
            OnPropertyChanged(nameof(ResultVoOverflowText));
            OnPropertyChanged(nameof(ResultDaOverflowText));
            OnPropertyChanged(nameof(ResultViOverflowText));
            OnPropertyChanged(nameof(ResultTotal));
            OnPropertyChanged(nameof(ResultTotalUncapped));
            OnPropertyChanged(nameof(ResultTotalOverflow));
            OnPropertyChanged(nameof(ResultTotalOverflowText));
            OnPropertyChanged(nameof(VoBarColumn));
            OnPropertyChanged(nameof(DaBarColumn));
            OnPropertyChanged(nameof(ViBarColumn));
            OnPropertyChanged(nameof(VoBarColumnRemain));
            OnPropertyChanged(nameof(DaBarColumnRemain));
            OnPropertyChanged(nameof(ViBarColumnRemain));
            OnPropertyChanged(nameof(IsVoAtCap));
            OnPropertyChanged(nameof(IsDaAtCap));
            OnPropertyChanged(nameof(IsViAtCap));
            RaiseBasePropertyChanged();
        }
    }

    private void RaiseBasePropertyChanged()
    {
        OnPropertyChanged(nameof(ResultVoBaseRaw));
        OnPropertyChanged(nameof(ResultDaBaseRaw));
        OnPropertyChanged(nameof(ResultViBaseRaw));
        OnPropertyChanged(nameof(ResultVoBase));
        OnPropertyChanged(nameof(ResultDaBase));
        OnPropertyChanged(nameof(ResultViBase));
        OnPropertyChanged(nameof(ResultTotalBase));
        OnPropertyChanged(nameof(VoBarColumnBase));
        OnPropertyChanged(nameof(DaBarColumnBase));
        OnPropertyChanged(nameof(ViBarColumnBase));
        OnPropertyChanged(nameof(VoBarColumnBaseRemain));
        OnPropertyChanged(nameof(DaBarColumnBaseRemain));
        OnPropertyChanged(nameof(ViBarColumnBaseRemain));
        OnPropertyChanged(nameof(HasCharacterBonus));
        OnPropertyChanged(nameof(ResultVoDelta));
        OnPropertyChanged(nameof(ResultDaDelta));
        OnPropertyChanged(nameof(ResultViDelta));
        OnPropertyChanged(nameof(ResultVoDeltaText));
        OnPropertyChanged(nameof(ResultDaDeltaText));
        OnPropertyChanged(nameof(ResultViDeltaText));
        OnPropertyChanged(nameof(ResultTotalBaseText));
    }

    public bool HasResult => Result != null;
    // 属性ごと: 表示値は cap 適用後 (実ゲームの見え方と一致)。生数値は ResultVoRaw 等で別途公開。
    public int ResultVoRaw => Result?.FinalStatus.Vo ?? 0;
    public int ResultDaRaw => Result?.FinalStatus.Da ?? 0;
    public int ResultViRaw => Result?.FinalStatus.Vi ?? 0;
    public int ResultVo => Math.Min(ResultVoRaw, StatCap);
    public int ResultDa => Math.Min(ResultDaRaw, StatCap);
    public int ResultVi => Math.Min(ResultViRaw, StatCap);
    public int ResultVoOverflow => ResultVoRaw - ResultVo;
    public int ResultDaOverflow => ResultDaRaw - ResultDa;
    public int ResultViOverflow => ResultViRaw - ResultVi;
    public string ResultVoOverflowText => ResultVoOverflow > 0 ? $"元 {ResultVoRaw}" : string.Empty;
    public string ResultDaOverflowText => ResultDaOverflow > 0 ? $"元 {ResultDaRaw}" : string.Empty;
    public string ResultViOverflowText => ResultViOverflow > 0 ? $"元 {ResultViRaw}" : string.Empty;

    // 合計も cap 適用後で表示する (algorithm の選出基準と一致させる)。
    public int ResultTotal => ResultVo + ResultDa + ResultVi;
    public int ResultTotalUncapped => ResultVoRaw + ResultDaRaw + ResultViRaw;
    public int ResultTotalOverflow => ResultTotalUncapped - ResultTotal;
    public string ResultTotalOverflowText =>
        ResultTotalOverflow > 0 ? $"cap超過 −{ResultTotalOverflow}" : string.Empty;

    // キャラ補正を抜いた値（キャラ未選択時は通常結果と同値）
    private CalculationResult? ResultBase => _resultWithoutCharacter ?? _result;
    public int ResultVoBaseRaw => ResultBase?.FinalStatus.Vo ?? 0;
    public int ResultDaBaseRaw => ResultBase?.FinalStatus.Da ?? 0;
    public int ResultViBaseRaw => ResultBase?.FinalStatus.Vi ?? 0;
    public int ResultVoBase => Math.Min(ResultVoBaseRaw, StatCap);
    public int ResultDaBase => Math.Min(ResultDaBaseRaw, StatCap);
    public int ResultViBase => Math.Min(ResultViBaseRaw, StatCap);
    public int ResultTotalBase => ResultVoBase + ResultDaBase + ResultViBase;

    // キャラ補正・メモリー補正いずれかが有効なら true（差分バー/差分テキストの表示判定）
    public bool HasCharacterBonus => _resultWithoutCharacter != null;
    public int ResultVoDelta => ResultVo - ResultVoBase;
    public int ResultDaDelta => ResultDa - ResultDaBase;
    public int ResultViDelta => ResultVi - ResultViBase;
    public string ResultVoDeltaText => HasCharacterBonus && ResultVoDelta != 0 ? FormatDelta(ResultVoDelta) : string.Empty;
    public string ResultDaDeltaText => HasCharacterBonus && ResultDaDelta != 0 ? FormatDelta(ResultDaDelta) : string.Empty;
    public string ResultViDeltaText => HasCharacterBonus && ResultViDelta != 0 ? FormatDelta(ResultViDelta) : string.Empty;
    public string ResultTotalBaseText => HasCharacterBonus ? $"補正なし: {ResultTotalBase:#,0}" : string.Empty;
    private static string FormatDelta(int v) => v >= 0 ? $"+{v}" : v.ToString();

    // HIFモード時は本戦上限増加 (FinalStatLimit) を加算済みの dynamicPlan の上限を使う
    private int StatCap => _isHifMode && _hifDynamicPlan != null
        ? _hifDynamicPlan.StatusLimit
        : (_selectedPlan?.StatusLimit ?? 2800);
    public bool IsVoAtCap => ResultVo >= StatCap;
    public bool IsDaAtCap => ResultDa >= StatCap;
    public bool IsViAtCap => ResultVi >= StatCap;

    // バー幅は Grid.ColumnDefinitions の Star 比率で表現し、親要素いっぱいに伸びるようにする。
    // [ratio*][1-ratio*] の2列構成で 1列目にバーを描画。比率はプランの StatusLimit を分母にして算出。
    private double VoRatio => StatCap > 0 ? Math.Min((double)ResultVo / StatCap, 1.0) : 0;
    private double DaRatio => StatCap > 0 ? Math.Min((double)ResultDa / StatCap, 1.0) : 0;
    private double ViRatio => StatCap > 0 ? Math.Min((double)ResultVi / StatCap, 1.0) : 0;
    private double VoRatioBase => StatCap > 0 ? Math.Min((double)ResultVoBase / StatCap, 1.0) : 0;
    private double DaRatioBase => StatCap > 0 ? Math.Min((double)ResultDaBase / StatCap, 1.0) : 0;
    private double ViRatioBase => StatCap > 0 ? Math.Min((double)ResultViBase / StatCap, 1.0) : 0;

    public System.Windows.GridLength VoBarColumn => new(VoRatio, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength DaBarColumn => new(DaRatio, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength ViBarColumn => new(ViRatio, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength VoBarColumnRemain => new(Math.Max(1.0 - VoRatio, 0), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength DaBarColumnRemain => new(Math.Max(1.0 - DaRatio, 0), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength ViBarColumnRemain => new(Math.Max(1.0 - ViRatio, 0), System.Windows.GridUnitType.Star);

    public System.Windows.GridLength VoBarColumnBase => new(VoRatioBase, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength DaBarColumnBase => new(DaRatioBase, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength ViBarColumnBase => new(ViRatioBase, System.Windows.GridUnitType.Star);
    public System.Windows.GridLength VoBarColumnBaseRemain => new(Math.Max(1.0 - VoRatioBase, 0), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength DaBarColumnBaseRemain => new(Math.Max(1.0 - DaRatioBase, 0), System.Windows.GridUnitType.Star);
    public System.Windows.GridLength ViBarColumnBaseRemain => new(Math.Max(1.0 - ViRatioBase, 0), System.Windows.GridUnitType.Star);

    public string DeckLabel
    {
        get
        {
            if (DeckCards.Count == 0) return string.Empty;
            return DeckCards.FirstOrDefault()?.DeckLabel ?? string.Empty;
        }
    }

    public int DeckTotal => DeckCards.Sum(c => c.StatValue);
}
