using System;
using System.Collections.Generic;

namespace GakumasuCalc.ViewModels;

/// <summary>
/// N.I.Aオーディション1件ぶんの種別選択UI行。種別変更時にコールバックで再計算＆プレビュー更新する。
/// </summary>
public class NiaAuditionViewModel : ViewModelBase
{
    private readonly Action<int, string> _onTierChanged;

    public int Week { get; }
    public string EventName { get; }
    public List<string> TierNames { get; }

    private string _selectedTierName;
    public string SelectedTierName
    {
        get => _selectedTierName;
        set
        {
            if (SetProperty(ref _selectedTierName, value) && value != null)
                _onTierChanged(Week, value);
        }
    }

    private string _gainText = string.Empty;
    public string GainText
    {
        get => _gainText;
        set => SetProperty(ref _gainText, value);
    }

    public NiaAuditionViewModel(
        int week, string eventName, List<string> tierNames, string selected, Action<int, string> onTierChanged)
    {
        Week = week;
        EventName = eventName;
        TierNames = tierNames;
        _selectedTierName = selected;
        _onTierChanged = onTierChanged;
    }
}
