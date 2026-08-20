using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public partial class MainViewModel
{
    public Character? SelectedCharacter
    {
        get => _selectedCharacter;
        set
        {
            if (SetProperty(ref _selectedCharacter, value))
            {
                // 選択キャラごとに保持したトグル値を反映（3凸=既定OFF / STEP4=既定ON）
                _uncap3BonusEnabled = value != null
                    && _uncap3BonusByChar.TryGetValue(value.Id, out var u) && u;
                _step4BonusEnabled = value == null
                    || !_step4BonusByChar.TryGetValue(value.Id, out var s) || s;
                OnPropertyChanged(nameof(Uncap3BonusEnabled));
                OnPropertyChanged(nameof(Step4BonusEnabled));
                OnPropertyChanged(nameof(HasSelectedCharacter));
                OnPropertyChanged(nameof(SelectedCharacterDisplay));
                OnPropertyChanged(nameof(CharacterBonusSummary));
                OnPropertyChanged(nameof(HasUncap3Bonus));
                OnPropertyChanged(nameof(HasStep4Bonus));
                foreach (var tile in CharacterTiles)
                    tile.IsSelected = (tile.Character == value);
                // NIAオーディション獲得プレビューを選択キャラで更新
                RefreshNiaAuditionPreviews();
                // 計算済みなら選択中パターンで再計算
                if (Result != null && _selectedPattern != null && _deckResults.Count > 0)
                    ApplySelectedPattern(_selectedPattern.Index);
            }
        }
    }

    public bool HasSelectedCharacter => _selectedCharacter != null;

    public string SelectedCharacterDisplay =>
        _selectedCharacter != null ? $": {_selectedCharacter.Name}" : "";

    public string CharacterBonusSummary
    {
        get
        {
            if (_selectedCharacter == null) return string.Empty;
            var b = EffectiveBaseStatus(_selectedCharacter);
            var p = EffectiveParaBonus(_selectedCharacter);
            return $"基礎+{b.Vo}/{b.Da}/{b.Vi}  パラボ Vo+{p.Vo:0.#}% Da+{p.Da:0.#}% Vi+{p.Vi:0.#}%";
        }
    }

    /// <summary>
    /// 実効パラボを返す。3凸OFFなら uncap3_bonus 分を減算、STEP4 ONなら step4_bonus.para_bonus を加算する。
    /// </summary>
    private StatBonusPercent EffectiveParaBonus(Character c)
    {
        var p = c.ParaBonus;
        if (!_uncap3BonusEnabled && c.Uncap3Bonus != null)
            p = p.Subtract(c.Uncap3Bonus);
        if (_step4BonusEnabled && c.Step4Bonus != null)
            p = p.Add(c.Step4Bonus.ParaBonus);
        return p;
    }

    /// <summary>
    /// 実効基礎ステータス。STEP4 ONなら step4_bonus.base_status_bonus を加算する。
    /// </summary>
    private StatusValues EffectiveBaseStatus(Character c)
    {
        if (_step4BonusEnabled && c.Step4Bonus != null)
            return c.BaseStatusBonus.Add(c.Step4Bonus.BaseStatusBonus);
        return c.BaseStatusBonus;
    }

    /// <summary>
    /// 計算で実際に渡すキャラ。3凸OFF時はパラボから3凸分を減算し、STEP4 ON時は基礎・パラボに加算した一時オブジェクトを返す。
    /// </summary>
    private Character? GetEffectiveCharacter()
    {
        if (_selectedCharacter == null) return null;
        var c = _selectedCharacter;
        bool adjustUncap3 = !_uncap3BonusEnabled && c.Uncap3Bonus != null;
        bool adjustStep4 = _step4BonusEnabled && c.Step4Bonus != null;
        if (!adjustUncap3 && !adjustStep4)
            return c;
        return new Character
        {
            Id = c.Id,
            Name = c.Name,
            Color = c.Color,
            Initial = c.Initial,
            BaseStatusBonus = EffectiveBaseStatus(c),
            ParaBonus = EffectiveParaBonus(c),
            Uncap3Bonus = c.Uncap3Bonus,
            Step4Bonus = c.Step4Bonus,
        };
    }

    /// <summary>
    /// HIFボーナス (Vo/Da/Vi 上昇パネル) をキャラ補正に合算した Character を返す。
    /// デッキ選出と最終表示で同じキャラを使うために共通化。
    /// HIFボーナスが全て0かつキャラ未選択なら null を返す。
    /// </summary>
    private Character? GetHifEffectiveCharacter(out bool hasAnyHifBonus)
    {
        var baseChar = GetEffectiveCharacter();
        var bl = HifVm.BonusLevels;
        int bonusVoFlat = HifBonusTables.GetStatUpFlat(bl.VoUpLevel);
        int bonusDaFlat = HifBonusTables.GetStatUpFlat(bl.DaUpLevel);
        int bonusViFlat = HifBonusTables.GetStatUpFlat(bl.ViUpLevel);
        int bonusVoPara = HifBonusTables.GetStatUpPara(bl.VoUpLevel);
        int bonusDaPara = HifBonusTables.GetStatUpPara(bl.DaUpLevel);
        int bonusViPara = HifBonusTables.GetStatUpPara(bl.ViUpLevel);
        hasAnyHifBonus = bonusVoFlat > 0 || bonusDaFlat > 0 || bonusViFlat > 0
                       || bonusVoPara > 0 || bonusDaPara > 0 || bonusViPara > 0;
        if (!hasAnyHifBonus) return baseChar;

        return new Character
        {
            Id = baseChar?.Id ?? "__hif_bonus__",
            Name = baseChar?.Name ?? "HIF Bonus",
            Color = baseChar?.Color ?? "#000000",
            Initial = baseChar?.Initial ?? "",
            BaseStatusBonus = new StatusValues(
                (baseChar?.BaseStatusBonus.Vo ?? 0) + bonusVoFlat,
                (baseChar?.BaseStatusBonus.Da ?? 0) + bonusDaFlat,
                (baseChar?.BaseStatusBonus.Vi ?? 0) + bonusViFlat
            ),
            ParaBonus = new StatBonusPercent
            {
                Vo = (baseChar?.ParaBonus.Vo ?? 0) + bonusVoPara,
                Da = (baseChar?.ParaBonus.Da ?? 0) + bonusDaPara,
                Vi = (baseChar?.ParaBonus.Vi ?? 0) + bonusViPara,
            },
            Uncap3Bonus = baseChar?.Uncap3Bonus,
        };
    }

    // トグルはキャラごとに保持（セッション内）。3凸=既定OFF / STEP4=既定ON。
    private readonly Dictionary<string, bool> _uncap3BonusByChar = new();
    private readonly Dictionary<string, bool> _step4BonusByChar = new();

    private bool _uncap3BonusEnabled = false;
    public bool Uncap3BonusEnabled
    {
        get => _uncap3BonusEnabled;
        set
        {
            if (SetProperty(ref _uncap3BonusEnabled, value))
            {
                if (_selectedCharacter != null)
                    _uncap3BonusByChar[_selectedCharacter.Id] = value;
                OnPropertyChanged(nameof(CharacterBonusSummary));
                if (Result != null && _selectedPattern != null && _deckResults.Count > 0)
                    ApplySelectedPattern(_selectedPattern.Index);
            }
        }
    }

    public bool HasUncap3Bonus => _selectedCharacter?.Uncap3Bonus != null;

    // STEP4 はデフォルト ON（開放済み前提）。OFF にするとパラボ・基礎の加算を外す。
    private bool _step4BonusEnabled = true;
    public bool Step4BonusEnabled
    {
        get => _step4BonusEnabled;
        set
        {
            if (SetProperty(ref _step4BonusEnabled, value))
            {
                if (_selectedCharacter != null)
                    _step4BonusByChar[_selectedCharacter.Id] = value;
                OnPropertyChanged(nameof(CharacterBonusSummary));
                if (Result != null && _selectedPattern != null && _deckResults.Count > 0)
                    ApplySelectedPattern(_selectedPattern.Index);
            }
        }
    }

    public bool HasStep4Bonus => _selectedCharacter?.Step4Bonus != null;

    public ICommand SelectCharacterCommand { get; }
}
