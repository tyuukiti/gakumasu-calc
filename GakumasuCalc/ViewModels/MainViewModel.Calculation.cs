using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public partial class MainViewModel
{
    /// <summary>レッスン週のみ main1/main2 配分を TurnChoices に適用 (中間前1:1・後2:1)。シード/一括配分で共通利用。</summary>
    private void DistributeLessonsInto(List<string> mainStats)
    {
        if (mainStats.Count < 2)
        {
            var onlyAction = (mainStats.Count > 0 ? mainStats[0] : "vo") switch
            {
                "vo" => ActionType.VoLesson,
                "da" => ActionType.DaLesson,
                _ => ActionType.ViLesson
            };
            foreach (var tc in TurnChoices)
            {
                if (!tc.IsFixedEvent && tc.AvailableActions.Contains(onlyAction))
                    tc.SelectedAction = onlyAction;
            }
            return;
        }

        var main1Action = mainStats[0] switch
        {
            "vo" => ActionType.VoLesson,
            "da" => ActionType.DaLesson,
            _ => ActionType.ViLesson
        };
        var main2Action = mainStats[1] switch
        {
            "vo" => ActionType.VoLesson,
            "da" => ActionType.DaLesson,
            _ => ActionType.ViLesson
        };

        // 中間試験の週を探す (無ければ 10 をフォールバック)
        var midExamWeek = _selectedPlan?.Schedule
            .Where(w => w.IsFixedEvent && w.EventName == "中間試験")
            .Select(w => w.Week)
            .FirstOrDefault() ?? 10;

        var lessonTurns = TurnChoices
            .Where(tc => !tc.IsFixedEvent && tc.AvailableActions.Any(a =>
                a is ActionType.VoLesson or ActionType.DaLesson or ActionType.ViLesson))
            .OrderBy(tc => tc.Week)
            .ToList();

        var beforeMid = lessonTurns.Where(tc => tc.Week < midExamWeek).ToList();
        var afterMid = lessonTurns.Where(tc => tc.Week > midExamWeek).ToList();

        // 中間前: メイン1:メイン2 = 1:1 (交互)
        bool toggle = false;
        foreach (var tc in beforeMid)
        {
            var action = toggle ? main2Action : main1Action;
            if (tc.AvailableActions.Contains(action))
                tc.SelectedAction = action;
            else if (tc.AvailableActions.Contains(toggle ? main1Action : main2Action))
                tc.SelectedAction = toggle ? main1Action : main2Action;
            toggle = !toggle;
        }

        // 中間後: メイン1:メイン2 = 2:1 (メイン1を多めに)
        int afterCount = 0;
        foreach (var tc in afterMid)
        {
            var action = (afterCount % 3 == 1) ? main2Action : main1Action;
            if (tc.AvailableActions.Contains(action))
                tc.SelectedAction = action;
            else
            {
                var fallback = (action == main2Action) ? main1Action : main2Action;
                if (tc.AvailableActions.Contains(fallback))
                    tc.SelectedAction = fallback;
            }
            afterCount++;
        }
    }

    private void OnPlanChanged()
    {
        // モードフラグをリセット (HIF/日程の取り違え防止)
        _isHifMode = false;
        _isScheduleMode = false;

        TurnChoices.Clear();
        if (_selectedPlan == null) return;

        foreach (var week in _selectedPlan.Schedule)
        {
            TurnChoices.Add(new TurnChoiceViewModel(week, _selectedPlan.ActivitySupply));
        }

        // 日程方式: 編集キャッシュがあれば復元、無ければ既定配分でシード。プリセットも読み込む。
        if (IsExplicitSchedulePlan && TurnChoices.Count > 0)
        {
            if (_scheduleSelectionCache.TryGetValue(_selectedPlan.Id, out var cached))
            {
                foreach (var tc in TurnChoices)
                {
                    if (!tc.IsFixedEvent && cached.TryGetValue(tc.Week, out var act)
                        && tc.AvailableActions.Contains(act))
                        tc.SelectedAction = act;
                }
            }
            else
            {
                // 既定シード: 全レッスンを bulkLessonStat、非レッスン週は優先度デフォルト（メイン1/2廃止）
                AutoAssignTurnChoices(
                    new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 },
                    new List<string> { ScheduleBulkLessonStat },
                    null);
            }
            LoadSchedulePresetsForCurrentPlan();
        }

        PopulateNiaAuditions();

        FilterEventCountTemplates();

        // 前プランの結果・パターンを完全にクリア (Web版 setSelectedPlanId と同等)。
        // 残すと別プランの古い _deckResults でパターン再適用される事故の温床になる。
        ClearResultState();
    }

    /// <summary>計算結果・選択パターン関連の表示状態を全てクリアする（プラン切替・条件プリセット読込で共用）。</summary>
    private void ClearResultState()
    {
        Result = null;
        DeckCards.Clear();
        DeckAbilitySummary.Clear();
        OnPropertyChanged(nameof(DeckAbilitySummaryVisibility));
        _deckResults = new List<CardScoringService.DeckResult>();
        PatternResults.Clear();
        _selectedPattern = null;
        OnPropertyChanged(nameof(SelectedPattern));
        OnPropertyChanged(nameof(PatternResults));
        OnPropertyChanged(nameof(DeckLabel));
        OnPropertyChanged(nameof(DeckTotal));
    }

    private void ExecuteCalculate()
    {
        if (_selectedPlan == null) return;

        // 日程方式 (初レジェンド / NIA) はユーザの日程を主入力にする別経路へ
        if (IsExplicitSchedulePlan)
        {
            ExecuteScheduleCalculate();
            return;
        }

        _isHifMode = false;
        _isScheduleMode = false;

        var lessonWeekCount = _selectedPlan.Schedule.Count(w => w.Lessons.Count > 0);

        // メイン属性リスト (メイン1が先、メイン1のレッスン回数が多い)
        var mainStats = new List<string>();
        if (VoRole == "メイン1") mainStats.Add("vo");
        if (DaRole == "メイン1") mainStats.Add("da");
        if (ViRole == "メイン1") mainStats.Add("vi");
        if (VoRole == "メイン2") mainStats.Add("vo");
        if (DaRole == "メイン2") mainStats.Add("da");
        if (ViRole == "メイン2") mainStats.Add("vi");

        // サブ属性を特定
        var subStat = new[] { "vo", "da", "vi" }.FirstOrDefault(s => !mainStats.Contains(s));
        if (subStat == null)
        {
            System.Windows.MessageBox.Show(
                "メイン1とメイン2に異なる属性を1つずつ設定してください。\nサブ属性が特定できません。",
                "属性設定エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // 追加カウント構築
        var additional = BuildAdditionalCounts();

        // 所持フィルタ適用
        var candidateCards = GetCandidateCards();
        var uncapLevels = BuildUncapLevels();

        // 所持モード時: 全カードをレンタルプールとして渡す（コンテストモード時はフィルタ適用）
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

        // 必須カード
        var requiredCardIds = RequiredCards.Select(c => c.Id).ToList();

        // 必須カードはコンテストモード等のフィルタを回避して候補に含める
        if (requiredCardIds.Count > 0)
        {
            var requiredIdSet = requiredCardIds.ToHashSet();
            var candidateIdSet = candidateCards.Select(c => c.Id).ToHashSet();

            if (OwnedOnly)
            {
                // 所持済み必須カードを candidateCards に追加
                var ownedIdSet = _inventory.Where(e => e.Owned).Select(e => e.CardId).ToHashSet();
                foreach (var card in _allCards.Where(c => requiredIdSet.Contains(c.Id) && ownedIdSet.Contains(c.Id)))
                {
                    if (!candidateIdSet.Contains(card.Id))
                        candidateCards.Add(card);
                }

                // 全必須カードを rentalPool に追加（未所持必須カードの検索用）
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
                // 全カード4凸モード: 必須カードを candidateCards に追加
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
                System.Windows.MessageBox.Show(
                    "未所持の必須カードは最大1枚です（レンタル枠使用）。",
                    "必須カード設定エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
        }

        // 複数パターン一括計算
        var patterns = _scoringService.SelectMultiplePatterns(
            _selectedPlan, candidateCards, mainStats, subStat, lessonWeekCount,
            spCounts: spCounts, planType: SelectedPlanType, additionalCounts: additional,
            uncapLevels: uncapLevels, rentalPool: rentalPool,
            requiredCardIds: requiredCardIds.Count > 0 ? requiredCardIds : null);

        _deckResults = patterns;
        _lastMainStats = mainStats;
        _lastLessonWeekCount = lessonWeekCount;

        PatternResults.Clear();
        int bestIndex = 0;
        int bestTotal = int.MinValue;

        for (int i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i];
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

            if (pattern.TotalValue > bestTotal)
            {
                bestTotal = pattern.TotalValue;
                bestIndex = i;
            }
        }

        OnPropertyChanged(nameof(PatternResults));

        // 最高スコアのパターンをデフォルト選択
        if (PatternResults.Count > 0)
            SelectedPattern = PatternResults[bestIndex];
    }

    /// <summary>
    /// 選択されたパターンで詳細計算を実行する
    /// </summary>
    private void ApplySelectedPattern(int patternIndex)
    {
        if (patternIndex < 0 || patternIndex >= _deckResults.Count)
            return;

        // HIFモード時は動的プラン。日程方式(NIA)はキャラの審査基準・流行でオーディション獲得を付与した有効プラン。
        var planForCalc = _isHifMode
            ? _hifDynamicPlan
            : (_isScheduleMode && _selectedPlan != null ? BuildNiaAuditionPlan(_selectedPlan) : _selectedPlan);
        if (planForCalc == null) return;
        // 「補正なし結果」用ベースプラン: オーディション獲得はキャラ依存のため素のプランを使う(NIA)。
        var baselinePlan = (_isScheduleMode && _selectedPlan != null) ? _selectedPlan : planForCalc;

        var pattern = _deckResults[patternIndex];

        List<TurnChoice> choices;
        if (_isHifMode)
        {
            // HIFはユーザが指定したスケジュール選択をそのまま使用
            choices = _hifTurnChoices;
        }
        else if (_isScheduleMode)
        {
            // 日程方式 (初レジェンド / NIA): ユーザ確定の日程をそのまま使用 (AutoAssign で上書きしない)
            choices = _scheduleTurnChoices;
        }
        else
        {
            // このパターンのレッスン配分を復元（通常モード）
            var allocation = BuildLessonAllocationFromPattern(pattern, _lastMainStats, _lastLessonWeekCount);
            AutoAssignTurnChoices(allocation, _lastMainStats, _selectedEventTemplate);
            choices = TurnChoices.Select(tc => tc.ToTurnChoice()).ToList();
        }

        var selectedCards = pattern.SelectedCards.Select(cs => cs.Card).ToList();
        var uncapLevels = BuildUncapLevels();
        // レンタルカードは4凸として計算
        foreach (var cs in pattern.SelectedCards.Where(cs => cs.IsRental))
            uncapLevels[cs.Card.Id] = 4;
        // HIFモードではデッキ選出時と同じ「HIFボーナス込みキャラ」で再計算しないと
        // 選出が想定する補正値と表示値がズレてしまう
        bool hasAnyHifBonus = false;
        var effectiveChar = _isHifMode
            ? GetHifEffectiveCharacter(out hasAnyHifBonus)
            : GetEffectiveCharacter();
        var memoryBonuses = BuildMemoryBonuses();
        // キャラ補正・メモリー補正・HIFボーナスのいずれかが有効なら「補正なし結果」を別途算出し差分表示に使う
        _resultWithoutCharacter = (_selectedCharacter != null || HasAnyMemoryBonus || hasAnyHifBonus)
            ? _calculationService.Calculate(baselinePlan, selectedCards, choices, uncapLevels, BuildAdditionalCounts(), null, null)
            : null;
        Result = _calculationService.Calculate(planForCalc, selectedCards, choices, uncapLevels, BuildAdditionalCounts(), effectiveChar, memoryBonuses);

        DeckCards.Clear();
        foreach (var cs in pattern.SelectedCards)
        {
            var suffix = (cs.IsRental ? " (レンタル)" : "") + (cs.IsRequired ? " (必須)" : "");
            var displayName = cs.Card.Name + suffix;
            var breakdown = string.Join("\n", cs.Breakdowns
                .Select(b => b.Value == 0 ? $"  {b.Reason}" : $"  {b.Reason} → {b.Value:+0.#;-0.#}"));
            DeckCards.Add(new DeckCardViewModel
            {
                CardId = cs.Card.Id,
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
                IsRental = cs.IsRental,
                IsRequired = cs.IsRequired,
                UncapLevel = cs.UncapLevel,
                HasSpRate = cs.Card.Effects.Any(e => e.Trigger == "equip" && e.ValueType == "sp_rate"),
            });
        }

        DeckAbilitySummary.Clear();
        foreach (var e in pattern.AbilitySummary)
        {
            DeckAbilitySummary.Add(new AbilitySummaryEntryViewModel
            {
                TriggerName = e.TriggerName,
                Stat = e.Stat,
                PerFire = e.PerFire,
                Parts = e.Parts,
                Fires = e.Fires,
                MaxCount = e.MaxCount,
                Total = e.Total,
            });
        }
        OnPropertyChanged(nameof(DeckAbilitySummaryVisibility));
        OnPropertyChanged(nameof(DeckLabel));
        OnPropertyChanged(nameof(DeckTotal));
    }

    /// <summary>
    /// パターンのラベルからレッスン配分を復元する
    /// </summary>
    private Dictionary<string, int> BuildLessonAllocationFromPattern(
        CardScoringService.DeckResult pattern, List<string> mainStats, int totalLessonWeeks)
    {
        // パターンラベルからカード枚数を取得し、それをレッスン配分として使う
        // ラベル例: "Dance 3 / Visual 2 / Vocal 1 編成"
        var allocation = new Dictionary<string, int> { ["vo"] = 0, ["da"] = 0, ["vi"] = 0 };

        foreach (var part in pattern.Label.Replace(" 編成", "").Split(" / "))
        {
            var tokens = part.Trim().Split(' ');
            if (tokens.Length == 2 && int.TryParse(tokens[1], out int count))
            {
                var stat = tokens[0] switch
                {
                    "Vocal" => "vo",
                    "Dance" => "da",
                    "Visual" => "vi",
                    _ => ""
                };
                if (!string.IsNullOrEmpty(stat))
                    allocation[stat] = count;
            }
        }

        // 残りをメインに配分
        int assigned = allocation.Values.Sum();
        int remaining = totalLessonWeeks - assigned;
        if (remaining > 0 && mainStats.Count > 0)
        {
            allocation[mainStats[0]] += remaining / 2;
            allocation[mainStats.Count > 1 ? mainStats[1] : mainStats[0]] += remaining - remaining / 2;
        }

        return allocation;
    }

    /// <summary>
    /// メイン/サブとSP枚数からレッスン配分を構築。
    /// SP枚数 = その属性のレッスンに割り当てる週数。
    /// 残りのレッスン週はメイン属性に均等配分。
    /// </summary>
    private Dictionary<string, int> BuildLessonAllocation(int totalLessonWeeks)
    {
        var allocation = new Dictionary<string, int>
        {
            ["vo"] = VoSpCount,
            ["da"] = DaSpCount,
            ["vi"] = ViSpCount
        };

        int assigned = allocation.Values.Sum();
        int remaining = totalLessonWeeks - assigned;

        // 残りをメイン属性に配分
        var mains = new List<string>();
        if (VoRole == "メイン") mains.Add("vo");
        if (DaRole == "メイン") mains.Add("da");
        if (ViRole == "メイン") mains.Add("vi");

        if (mains.Count > 0 && remaining > 0)
        {
            int perMain = remaining / mains.Count;
            int extra = remaining % mains.Count;
            foreach (var stat in mains)
            {
                allocation[stat] += perMain;
                if (extra > 0)
                {
                    allocation[stat]++;
                    extra--;
                }
            }
        }

        return allocation;
    }

    /// <summary>
    /// ターン選択を自動設定する。
    /// レッスン週: メイン属性のレッスンのみ選択（サブ属性のレッスンは選ばない）
    ///   中間前: メイン1:メイン2 = 1:1
    ///   中間後: メイン1:メイン2 = 1:2 (パラメータを早く伸ばすため)
    /// 授業週: サブ属性の授業を選択
    /// </summary>
    private void AutoAssignTurnChoices(Dictionary<string, int> allocation, List<string> mainStats, EventCountTemplate? template = null)
    {
        var subStat = new[] { "vo", "da", "vi" }.First(s => !mainStats.Contains(s));

        // サブの授業ActionType
        var subClassAction = subStat switch
        {
            "vo" => ActionType.VoClass,
            "da" => ActionType.DaClass,
            _ => ActionType.ViClass
        };

        // レッスン週の配分は DistributeLessonsInto に集約 (シード/一括配分と同一ソース)
        DistributeLessonsInto(mainStats);

        // 授業週: サブ属性の授業を選択
        foreach (var tc in TurnChoices)
        {
            if (tc.IsFixedEvent) continue;

            var hasLesson = tc.AvailableActions.Any(a =>
                a is ActionType.VoLesson or ActionType.DaLesson or ActionType.ViLesson);
            var hasClass = tc.AvailableActions.Any(a =>
                a is ActionType.VoClass or ActionType.DaClass or ActionType.ViClass);

            if (hasLesson) continue; // レッスン週は上で設定済み

            // 道中テンプレートで週ごとのアクションが指定されていれば優先
            if (template?.WeekActions != null
                && template.WeekActions.TryGetValue(tc.Week, out var overrideStr)
                && TurnChoiceViewModel.TryParseAction(overrideStr, out var overrideAction)
                && tc.AvailableActions.Contains(overrideAction))
            {
                tc.SelectedAction = overrideAction;
                continue;
            }

            if (hasClass && tc.AvailableActions.Contains(subClassAction))
            {
                tc.SelectedAction = subClassAction;
            }
            else if (tc.AvailableActions.Contains(ActionType.ActivitySupply))
                tc.SelectedAction = ActionType.ActivitySupply;
            else if (tc.AvailableActions.Contains(ActionType.Outing))
                tc.SelectedAction = ActionType.Outing;
            else if (tc.AvailableActions.Contains(ActionType.Consultation))
                tc.SelectedAction = ActionType.Consultation;
            else if (tc.AvailableActions.Contains(ActionType.SpecialTraining))
                tc.SelectedAction = ActionType.SpecialTraining;
            else if (hasClass)
            {
                // サブの授業がない場合、メイン属性の授業を選択
                var mainClassAction = mainStats[0] switch
                {
                    "vo" => ActionType.VoClass,
                    "da" => ActionType.DaClass,
                    _ => ActionType.ViClass
                };
                if (tc.AvailableActions.Contains(mainClassAction))
                    tc.SelectedAction = mainClassAction;
                else if (tc.AvailableActions.Count > 0)
                    tc.SelectedAction = tc.AvailableActions[0];
            }
            else if (tc.AvailableActions.Count > 0)
                tc.SelectedAction = tc.AvailableActions[0];
        }
    }

    /// <summary>
    /// サポカを変えずに現在のターン選択で再計算する
    /// </summary>
    private void ExecuteRecalcLesson()
    {
        if (_selectedPlan == null || _selectedPattern == null) return;
        if (_selectedPattern.Index < 0 || _selectedPattern.Index >= _deckResults.Count) return;

        var pattern = _deckResults[_selectedPattern.Index];
        var selectedCards = pattern.SelectedCards.Select(cs => cs.Card).ToList();

        // 現在のターン選択をそのまま使って再計算
        var choices = TurnChoices.Select(tc => tc.ToTurnChoice()).ToList();
        var effectiveChar = GetEffectiveCharacter();
        var memoryBonuses = BuildMemoryBonuses();
        _resultWithoutCharacter = (_selectedCharacter != null || HasAnyMemoryBonus)
            ? _calculationService.Calculate(_selectedPlan, selectedCards, choices, BuildUncapLevels(), BuildAdditionalCounts(), null, null)
            : null;
        Result = _calculationService.Calculate(_selectedPlan, selectedCards, choices, BuildUncapLevels(), BuildAdditionalCounts(), effectiveChar, memoryBonuses);
    }

    private void ExecuteReset()
    {
        VoRole = "サブ"; DaRole = "サブ"; ViRole = "サブ";
        VoSpCount = 0; DaSpCount = 0; ViSpCount = 0;
        PDrinkAcquire = 0; PItemAcquire = 0; SkillAcquire = 0; SkillSsrAcquire = 0;
        SkillEnhance = 0; SkillDelete = 0; SkillCustom = 0; SkillChange = 0;
        ActiveEnhance = 0; ActiveDelete = 0;
        MentalAcquire = 0; MentalEnhance = 0; MentalDelete = 0; ActiveAcquire = 0;
        GenkiAcquire = 0; GoodConditionAcquire = 0;
        GoodImpressionAcquire = 0; ConserveAcquire = 0;
        ConcentrateAcquire = 0; MotivationAcquire = 0;
        FullpowerAcquire = 0; AggressiveAcquire = 0; ConsultationDrink = 0;
        DeckCards.Clear();
        DeckAbilitySummary.Clear();
        OnPropertyChanged(nameof(DeckAbilitySummaryVisibility));
        RequiredCards.Clear();
        ExcludedCards.Clear();
        OnPropertyChanged(nameof(CanAddRequiredCard));
        // 日程方式: リセットは編集キャッシュも消して既定シードに戻す
        if (_selectedPlan != null) _scheduleSelectionCache.Remove(_selectedPlan.Id);
        OnPlanChanged();
    }
}
