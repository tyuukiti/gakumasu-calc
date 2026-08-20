using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public partial class MainViewModel
{
    // NIAオーディション: week → 選択種別名（未設定の週は先頭=最強種別）
    private readonly Dictionary<int, string> _niaAuditionTiers = new();
    /// <summary>NIAオーディション種別選択UIの行（nia時のみ）。</summary>
    public ObservableCollection<NiaAuditionViewModel> NiaAuditions { get; } = new();
    public bool HasNiaAuditions => NiaAuditions.Count > 0;

    /// <summary>
    /// オーディション1週ぶんの獲得ステータスを、キャラの審査基準・流行から算出。
    /// 1種別クリアで「流行1値→流行1属性 / 流行2値→流行2属性 / 流行3値→流行3属性」を同時加算。
    /// キャラ未選択・流行データ無し・種別データ無しなら null（＝獲得0）。
    /// </summary>
    private static StatusValues? ComputeNiaAuditionGain(WeekSchedule week, Character? character, string? tierName)
    {
        var tiers = week.NiaAuditionTiers;
        if (tiers == null || tiers.Count == 0) return null;
        if (character?.NiaTrend == null || character.NiaTrend.Count < 3) return null;
        var tier = tiers.FirstOrDefault(t => t.Name == (tierName ?? tiers[0].Name)) ?? tiers[0];
        var amounts = character.NiaCriteria == "concentrate" ? tier.Concentrate : tier.Balance;
        var ranks = new[] { amounts.T1, amounts.T2, amounts.T3 };
        int vo = 0, da = 0, vi = 0;
        for (int i = 0; i < 3; i++)
        {
            switch (character.NiaTrend[i])
            {
                case "vo": vo += ranks[i]; break;
                case "da": da += ranks[i]; break;
                case "vi": vi += ranks[i]; break;
            }
        }
        return new StatusValues(vo, da, vi);
    }

    private static WeekSchedule CloneWeekWithGain(WeekSchedule w, StatusValues gain) => new()
    {
        Week = w.Week,
        Type = w.Type,
        AvailableActions = w.AvailableActions,
        Lessons = w.Lessons,
        EventName = w.EventName,
        StatusGain = gain,
        OutingEffect = w.OutingEffect,
        Classes = w.Classes,
        ClassEffect = w.ClassEffect,
        ConsultationEffect = w.ConsultationEffect,
        SpecialTrainingEffect = w.SpecialTrainingEffect,
        HifSubValue = w.HifSubValue,
        HifExamBase = w.HifExamBase,
        HifExamDistributed = w.HifExamDistributed,
        NiaAuditionTiers = w.NiaAuditionTiers,
    };

    /// <summary>
    /// NIAのオーディション週へ、選択キャラ・種別から算出した status_gain を流し込んだプランを返す。
    /// 種別を持たない週(初レジェンド等)・キャラ未選択・流行なしは素のプランをそのまま返す。
    /// </summary>
    private TrainingPlan BuildNiaAuditionPlan(TrainingPlan basePlan)
    {
        bool any = basePlan.Schedule.Any(w => w.NiaAuditionTiers is { Count: > 0 });
        if (!any || _selectedCharacter?.NiaTrend == null || _selectedCharacter.NiaTrend.Count < 3)
            return basePlan;

        var newSchedule = new List<WeekSchedule>(basePlan.Schedule.Count);
        bool changed = false;
        foreach (var w in basePlan.Schedule)
        {
            if (w.NiaAuditionTiers is { Count: > 0 })
            {
                var tierName = _niaAuditionTiers.TryGetValue(w.Week, out var t) ? t : w.NiaAuditionTiers[0].Name;
                var gain = ComputeNiaAuditionGain(w, _selectedCharacter, tierName);
                if (gain != null)
                {
                    newSchedule.Add(CloneWeekWithGain(w, gain));
                    changed = true;
                    continue;
                }
            }
            newSchedule.Add(w);
        }
        if (!changed) return basePlan;

        return new TrainingPlan
        {
            Id = basePlan.Id,
            Name = basePlan.Name,
            Description = basePlan.Description,
            TotalWeeks = basePlan.TotalWeeks,
            StatusLimit = basePlan.StatusLimit,
            BaseStatus = basePlan.BaseStatus,
            Schedule = newSchedule,
            ActivitySupply = basePlan.ActivitySupply,
        };
    }

    /// <summary>選択プランの種別UI行を再構築（nia時のみ。初レジェンド等は空＝非表示）。</summary>
    private void PopulateNiaAuditions()
    {
        NiaAuditions.Clear();
        if (_selectedPlan != null)
        {
            foreach (var w in _selectedPlan.Schedule)
            {
                if (w.NiaAuditionTiers is not { Count: > 0 }) continue;
                var tierNames = w.NiaAuditionTiers.Select(t => t.Name).ToList();
                if (!_niaAuditionTiers.ContainsKey(w.Week)) _niaAuditionTiers[w.Week] = tierNames[0];
                NiaAuditions.Add(new NiaAuditionViewModel(
                    w.Week, w.EventName ?? $"Week {w.Week}", tierNames, _niaAuditionTiers[w.Week], OnNiaTierChanged));
            }
            RefreshNiaAuditionPreviews();
        }
        OnPropertyChanged(nameof(HasNiaAuditions));
    }

    private void OnNiaTierChanged(int week, string tierName)
    {
        _niaAuditionTiers[week] = tierName;
        RefreshNiaAuditionPreviews();
        // 結果が出ていれば同じデッキのまま再計算して反映
        if (Result != null && _selectedPattern != null && _deckResults.Count > 0)
            ApplySelectedPattern(_selectedPattern.Index);
    }

    private void RefreshNiaAuditionPreviews()
    {
        if (_selectedPlan == null) return;
        foreach (var vm in NiaAuditions)
        {
            var week = _selectedPlan.Schedule.FirstOrDefault(w => w.Week == vm.Week);
            if (week == null) continue;
            var gain = ComputeNiaAuditionGain(week, _selectedCharacter, vm.SelectedTierName);
            vm.GainText = gain != null
                ? $"Vo+{gain.Vo} / Da+{gain.Da} / Vi+{gain.Vi}"
                : (_selectedCharacter == null ? "キャラ未選択のため0" : "流行データ無しのため0");
        }
    }
}
