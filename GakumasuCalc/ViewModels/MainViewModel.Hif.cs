using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public partial class MainViewModel
{
    public ICommand HifCalculateCommand { get; private set; } = null!;

    /// <summary>
    /// HIFモードの計算実行。HifVm.ScheduleItems の選択を元に動的な TrainingPlan を構築し、
    /// 既存の SelectMultiplePatterns / Calculate に渡す。
    /// </summary>
    private void ExecuteHifCalculate()
    {
        var hifPlan = HifVm.HifPlan;
        if (hifPlan == null)
        {
            HifVm.ErrorMessage = "HIFプランが読み込まれていません";
            return;
        }

        // 動的TrainingPlan + TurnChoices を構築
        var (dynamicPlan, turnChoices) = BuildHifPlanAndChoices(hifPlan);
        if (turnChoices.Count == 0)
        {
            HifVm.ErrorMessage = "スケジュールが未設定です";
            return;
        }

        // mainStats 自動推論 (出現数 desc 上位2属性、PostOptimize の保護対象計算に使う)
        var mainStats = InferMainStatsFromTurnChoices(turnChoices);

        var lessonWeekCount = dynamicPlan.Schedule.Count(w => w.Lessons.Count > 0);

        // 追加カウント
        var additional = BuildAdditionalCounts();

        // 候補カード
        var candidateCards = GetCandidateCards();
        var uncapLevels = BuildUncapLevels();

        List<SupportCard>? rentalPool = null;
        if (OwnedOnly)
        {
            rentalPool = ContestMode
                ? _allCards.Where(c => c.Tag is not ("skill" or "exam_item")).ToList()
                : _allCards;
        }

        // 除外カードを候補・レンタルプールから除去（必須カードは相互排他のため除外集合に含まれない）
        if (ExcludedCards.Count > 0)
        {
            var excludedIdSet = ExcludedCards.Select(c => c.Id).ToHashSet();
            candidateCards = candidateCards.Where(c => !excludedIdSet.Contains(c.Id)).ToList();
            if (rentalPool != null)
                rentalPool = rentalPool.Where(c => !excludedIdSet.Contains(c.Id)).ToList();
        }

        var requiredCardIds = RequiredCards.Select(c => c.Id).ToList();
        if (requiredCardIds.Count > 0)
        {
            var requiredIdSet = requiredCardIds.ToHashSet();
            var candidateIdSet = candidateCards.Select(c => c.Id).ToHashSet();

            if (OwnedOnly)
            {
                var ownedIdSet = _inventory.Where(e => e.Owned).Select(e => e.CardId).ToHashSet();
                foreach (var card in _allCards.Where(c => requiredIdSet.Contains(c.Id) && ownedIdSet.Contains(c.Id)))
                {
                    if (!candidateIdSet.Contains(card.Id))
                        candidateCards.Add(card);
                }

                if (rentalPool != null)
                {
                    var rentalIdSet = rentalPool.Select(c => c.Id).ToHashSet();
                    foreach (var card in _allCards.Where(c => requiredIdSet.Contains(c.Id)))
                    {
                        if (!rentalIdSet.Contains(card.Id))
                            rentalPool.Add(card);
                    }
                }
            }
            else
            {
                foreach (var card in _allCards.Where(c => requiredIdSet.Contains(c.Id)))
                {
                    if (!candidateIdSet.Contains(card.Id))
                        candidateCards.Add(card);
                }
            }
        }

        // SP率カード枚数
        var spCounts = new Dictionary<string, int>();
        if (VoSpCount > 0) spCounts["vo"] = VoSpCount;
        if (DaSpCount > 0) spCounts["da"] = DaSpCount;
        if (ViSpCount > 0) spCounts["vi"] = ViSpCount;

        // バリデーション: OwnedOnly時、未所持必須カードが2枚以上ならエラー
        if (OwnedOnly && requiredCardIds.Count > 0)
        {
            var ownedIds = _inventory.Where(e => e.Owned).Select(e => e.CardId).ToHashSet();
            int notOwnedCount = requiredCardIds.Count(id => !ownedIds.Contains(id));
            if (notOwnedCount > 1)
            {
                HifVm.ErrorMessage = "未所持の必須カードは最大1枚です（レンタル枠使用）";
                return;
            }
        }

        // HIFモード状態を設定
        _isHifMode = true;
        _hifTurnChoices = turnChoices;
        _lastMainStats = mainStats;
        _lastLessonWeekCount = lessonWeekCount;

        // ユーザのスケジュールから実際のレッスン配分を集計
        var hifLessonAllocation = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };
        foreach (var tc in turnChoices)
        {
            switch (tc.ChosenAction)
            {
                case ActionType.VoLesson: hifLessonAllocation["vo"]++; break;
                case ActionType.DaLesson: hifLessonAllocation["da"]++; break;
                case ActionType.ViLesson: hifLessonAllocation["vi"]++; break;
            }
        }

        // HIF専用パターン計算 (Vo×3 / Da×3 / Vi×3 / オールフリー)
        // キャラ補正・メモリーは PostOptimize の評価値と最終表示値を一致させるため渡す
        var hifMemoryBonuses = BuildMemoryBonuses();
        var hifEffectiveChar = GetHifEffectiveCharacter(out _);
        int hifFinalCapBonus = HifBonusTables.GetFinalCapBonus(HifVm.BonusLevels.FinalStatLimitLevel);

        // status_limit に本戦上限増加を加算 (hifCap は後段で dynamicPlan.StatusLimit から取得される)
        if (hifFinalCapBonus > 0)
        {
            dynamicPlan = new TrainingPlan
            {
                Id = dynamicPlan.Id,
                Name = dynamicPlan.Name,
                Description = dynamicPlan.Description,
                TotalWeeks = dynamicPlan.TotalWeeks,
                StatusLimit = dynamicPlan.StatusLimit + hifFinalCapBonus,
                BaseStatus = dynamicPlan.BaseStatus,
                Schedule = dynamicPlan.Schedule,
                ActivitySupply = dynamicPlan.ActivitySupply,
            };
        }
        // MAX判定はキャップボーナス込みの動的プランで行うため、ボーナス加算後に保持する
        _hifDynamicPlan = dynamicPlan;

        // MAX大幅超過時の再抽選オプション (ON のときだけ × 2 overflow罰則を有効化)
        var overflowPenaltyConfig = HifVm.OverflowPenaltyEnabled
            ? new CardScoringService.OverflowPenaltyConfig { Threshold = HifVm.OverflowPenaltyThreshold }
            : null;

        var patterns = _scoringService.SelectMultiplePatternsHif(
            dynamicPlan, candidateCards, mainStats, hifLessonAllocation,
            spCounts: spCounts, planType: SelectedPlanType, additionalCounts: additional,
            uncapLevels: uncapLevels, rentalPool: rentalPool,
            requiredCardIds: requiredCardIds.Count > 0 ? requiredCardIds : null,
            character: hifEffectiveChar, memoryBonuses: hifMemoryBonuses,
            turnChoicesOverride: turnChoices,
            overflowPenalty: overflowPenaltyConfig);

        _deckResults = patterns;

        // パターン選出はキャラ補正込みのキャップ後合計で比較
        // (TotalValue はキャラなしのカード寄与合計で、キャラの偏りを反映しないため)
        var hifCap = dynamicPlan.StatusLimit;
        PatternResults.Clear();
        int bestIndex = 0;
        int bestEffectiveTotal = int.MinValue;

        for (int i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i];

            // パターンの実効キャップ後合計を算出
            var pCards = pattern.SelectedCards.Select(cs => cs.Card).ToList();
            var pUncap = new Dictionary<string, int>(uncapLevels ?? new());
            foreach (var cs in pattern.SelectedCards.Where(cs => cs.IsRental))
                pUncap[cs.Card.Id] = 4;
            var pFs = _calculationService.Calculate(dynamicPlan, pCards, turnChoices, pUncap, additional, hifEffectiveChar, hifMemoryBonuses).FinalStatus;
            int cappedTotal = Math.Min(pFs.Vo, hifCap) + Math.Min(pFs.Da, hifCap) + Math.Min(pFs.Vi, hifCap);
            // overflow罰則: 合計overflowが閾値超過時のみ × 2 罰則をパターン選択にも適用
            int effectiveScore = cappedTotal;
            if (overflowPenaltyConfig != null)
            {
                int pOverflow = Math.Max(0, pFs.Vo - hifCap) + Math.Max(0, pFs.Da - hifCap) + Math.Max(0, pFs.Vi - hifCap);
                if (pOverflow > overflowPenaltyConfig.Threshold)
                {
                    effectiveScore -= pOverflow * 2;
                }
            }

            var vm = new PatternResultViewModel { Label = pattern.Label, Index = i };
            foreach (var cs in pattern.SelectedCards)
            {
                var suffix = (cs.IsRental ? "（レンタル）" : "") + (cs.IsRequired ? "（必須）" : "");
                var displayName = cs.Card.Name + suffix;
                var breakdown = string.Join("\n", cs.Breakdowns
                    .Select(b => b.Value == 0 ? $"  {b.Reason}" : $"  {b.Reason} → {b.Value:+0.#;-0.#}"));
                vm.Cards.Add(new DeckCardViewModel
                {
                    CardName = displayName,
                    CardType = cs.Card.Type,
                    CardRarity = cs.Card.Rarity,
                    CardPlan = cs.Card.Plan,
                    StatValue = cs.TotalValue,
                    TeamBonusTotal = cs.TeamBonusTotal,
                    TeamBonusContributors = cs.TeamBonusContributors.Select(c => (c.CardName, c.Value)).ToList(),
                    Breakdowns = new ObservableCollection<EffectBreakdownViewModel>(
                        cs.Breakdowns.Select(b => new EffectBreakdownViewModel { Reason = b.Reason, Stat = b.Stat, Value = b.Value })),
                    RawVo = cs.RawVo,
                    RawDa = cs.RawDa,
                    RawVi = cs.RawVi,
                    DeckLabel = pattern.Label,
                    BreakdownText = $"Vo:{cs.RawVo} Da:{cs.RawDa} Vi:{cs.RawVi}\n{breakdown}",
                    HasSpRate = cs.Card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate"),
                });
            }
            PatternResults.Add(vm);

            if (effectiveScore > bestEffectiveTotal)
            {
                bestEffectiveTotal = effectiveScore;
                bestIndex = i;
            }
        }

        OnPropertyChanged(nameof(PatternResults));

        HifVm.ErrorMessage = null;

        if (PatternResults.Count > 0)
            SelectedPattern = PatternResults[bestIndex];
        else
            HifVm.ErrorMessage = "有効な編成パターンが見つかりませんでした";
    }

    /// <summary>
    /// HifVm.ScheduleItems の選択を元に動的TrainingPlanとTurnChoice配列を構築。
    /// 公開レッスン日はユーザのメイン/サブ選択を sp_bonus に反映する。
    /// </summary>
    private (TrainingPlan plan, List<TurnChoice> turnChoices) BuildHifPlanAndChoices(TrainingPlan hifPlan)
    {
        // schedule を deep copy しつつ、公開レッスン日は sp_bonus を上書き
        var newSchedule = new List<WeekSchedule>();
        var itemsByWeek = HifVm.ScheduleItems.ToDictionary(it => it.Week);

        foreach (var w in hifPlan.Schedule)
        {
            // 試験日: 基礎値(全属性同値) + ユーザ配分値 を status_gain に反映
            if (w.IsFixedEvent && ((w.HifExamBase ?? 0) > 0 || (w.HifExamDistributed ?? 0) > 0))
            {
                if (itemsByWeek.TryGetValue(w.Week, out var examItem) && examItem.IsExam)
                {
                    int baseVal = examItem.ExamBase;
                    int voGain = baseVal + Math.Max(0, examItem.ExamVoAlloc);
                    int daGain = baseVal + Math.Max(0, examItem.ExamDaAlloc);
                    int viGain = baseVal + Math.Max(0, examItem.ExamViAlloc);
                    newSchedule.Add(new WeekSchedule
                    {
                        Week = w.Week,
                        Type = w.Type,
                        AvailableActions = w.AvailableActions,
                        Lessons = w.Lessons,
                        Classes = w.Classes,
                        EventName = w.EventName,
                        StatusGain = new StatusValues(voGain, daGain, viGain),
                        OutingEffect = w.OutingEffect,
                        ClassEffect = w.ClassEffect,
                        ConsultationEffect = w.ConsultationEffect,
                        SpecialTrainingEffect = w.SpecialTrainingEffect,
                        HifSubValue = w.HifSubValue,
                        HifExamBase = w.HifExamBase,
                        HifExamDistributed = w.HifExamDistributed,
                    });
                    continue;
                }
            }

            if (w.Type == "public_lesson" && itemsByWeek.TryGetValue(w.Week, out var item) && item.MainStat != null && item.SubStat != null)
            {
                var mainStat = item.MainStat;
                var subStat = item.SubStat;
                var mainValue = w.Lessons.FirstOrDefault(l => l.Type == mainStat) is { } ml
                    ? (mainStat switch { "vo" => ml.SpBonus.Vo, "da" => ml.SpBonus.Da, _ => ml.SpBonus.Vi })
                    : 0;
                var subValue = w.HifSubValue ?? 0;

                var newLessons = w.Lessons.Select(l =>
                {
                    if (l.Type != mainStat) return l;
                    int spVo = 0, spDa = 0, spVi = 0;
                    if (mainStat == "vo") spVo = mainValue;
                    else if (mainStat == "da") spDa = mainValue;
                    else if (mainStat == "vi") spVi = mainValue;

                    if (subStat == "vo") spVo += subValue;
                    else if (subStat == "da") spDa += subValue;
                    else if (subStat == "vi") spVi += subValue;

                    return new LessonConfig { Type = l.Type, SpBonus = new StatusValues(spVo, spDa, spVi) };
                }).ToList();

                newSchedule.Add(new WeekSchedule
                {
                    Week = w.Week,
                    Type = w.Type,
                    AvailableActions = w.AvailableActions,
                    Lessons = newLessons,
                    Classes = w.Classes,
                    EventName = w.EventName,
                    StatusGain = w.StatusGain,
                    OutingEffect = w.OutingEffect,
                    ClassEffect = w.ClassEffect,
                    ConsultationEffect = w.ConsultationEffect,
                    SpecialTrainingEffect = w.SpecialTrainingEffect,
                    HifSubValue = w.HifSubValue,
                });
            }
            else
            {
                newSchedule.Add(w);
            }
        }

        var newPlan = new TrainingPlan
        {
            Id = hifPlan.Id,
            Name = hifPlan.Name,
            Description = hifPlan.Description,
            TotalWeeks = hifPlan.TotalWeeks,
            StatusLimit = hifPlan.StatusLimit,
            BaseStatus = hifPlan.BaseStatus,
            Schedule = newSchedule,
            ActivitySupply = hifPlan.ActivitySupply,
        };

        // TurnChoices 構築 (固定日 audition はスキップ)
        var turnChoices = new List<TurnChoice>();
        foreach (var w in newSchedule)
        {
            if (w.IsFixedEvent) continue;
            // 選択肢なしの日 (本戦インターバル等) は計算対象外。
            // 相談/特別指導はユーザ操作でありサポート効果が発動しないため。
            if (w.AvailableActions.Count == 0) continue;
            if (!itemsByWeek.TryGetValue(w.Week, out var item)) continue;
            if (string.IsNullOrEmpty(item.SelectedAction)) continue;
            var actionType = ParseActionType(item.SelectedAction);
            if (actionType != null)
                turnChoices.Add(new TurnChoice { Week = w.Week, ChosenAction = actionType.Value });
        }

        return (newPlan, turnChoices);
    }

    /// <summary>
    /// TurnChoice 配列から mainStats を自動推論 (vo_lesson/da_lesson/vi_lesson の出現数 desc 上位2属性)。
    /// タイブレーク: vo > da > vi。
    /// </summary>
    private static List<string> InferMainStatsFromTurnChoices(List<TurnChoice> choices)
    {
        var counts = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };
        foreach (var tc in choices)
        {
            switch (tc.ChosenAction)
            {
                case ActionType.VoLesson: counts["vo"]++; break;
                case ActionType.DaLesson: counts["da"]++; break;
                case ActionType.ViLesson: counts["vi"]++; break;
            }
        }
        var order = new[] { "vo", "da", "vi" };
        return order
            .OrderByDescending(s => counts[s])
            .ThenBy(s => Array.IndexOf(order, s))
            .Take(2)
            .ToList();
    }

    private static ActionType? ParseActionType(string s) => s switch
    {
        "vo_lesson" => ActionType.VoLesson,
        "da_lesson" => ActionType.DaLesson,
        "vi_lesson" => ActionType.ViLesson,
        "vo_class" => ActionType.VoClass,
        "da_class" => ActionType.DaClass,
        "vi_class" => ActionType.ViClass,
        "outing" => ActionType.Outing,
        "rest" => ActionType.Rest,
        "consultation" => ActionType.Consultation,
        "activity_supply" => ActionType.ActivitySupply,
        "special_training" => ActionType.SpecialTraining,
        _ => null,
    };
}
