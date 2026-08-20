using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public partial class MainViewModel
{
    // 必須カード
    /// <summary>必須カードの最大登録枚数。デッキ全6枠を固定し、決め打ち編成の最終パラメータを直接評価する使い方も許容する</summary>
    public const int MaxRequiredCards = 6;

    public ObservableCollection<SupportCard> RequiredCards { get; } = new();
    public List<SupportCard> AvailableCardsForRequired => _allCards;

    public SupportCard? SelectedRequiredCard
    {
        get => _selectedRequiredCard;
        set => SetProperty(ref _selectedRequiredCard, value);
    }

    public bool CanAddRequiredCard => RequiredCards.Count < MaxRequiredCards;

    public ICommand AddRequiredCardCommand { get; private set; } = null!;
    public ICommand RemoveRequiredCardCommand { get; private set; } = null!;

    // 除外カード（編成候補から外す。枚数制限なし）
    public ObservableCollection<SupportCard> ExcludedCards { get; } = new();
    public List<SupportCard> AvailableCardsForExcluded => _allCards;

    public SupportCard? SelectedExcludedCard
    {
        get => _selectedExcludedCard;
        set => SetProperty(ref _selectedExcludedCard, value);
    }

    public ICommand AddExcludedCardCommand { get; private set; } = null!;
    public ICommand RemoveExcludedCardCommand { get; private set; } = null!;
    public ICommand ExcludeCardCommand { get; private set; } = null!;

    /// <summary>
    /// 所持フィルタ・コンテストモードフィルタを適用したカードリストを返す
    /// </summary>
    private List<SupportCard> GetCandidateCards()
    {
        IEnumerable<SupportCard> cards = _allCards;

        if (OwnedOnly)
        {
            var ownedIds = _inventory
                .Where(e => e.Owned)
                .Select(e => e.CardId)
                .ToHashSet();
            cards = cards.Where(c => ownedIds.Contains(c.Id));
        }

        if (ContestMode)
        {
            cards = cards.Where(c => c.Tag is not ("skill" or "exam_item"));
        }

        return cards.ToList();
    }

    /// <summary>
    /// 凸数辞書を構築する。所持モード時はインベントリの凸数、それ以外は全カード4凸。
    /// </summary>
    private Dictionary<string, int> BuildUncapLevels()
    {
        if (OwnedOnly)
            return _inventory.ToDictionary(e => e.CardId, e => e.Uncap);

        // 全カード4凸
        return _allCards.ToDictionary(c => c.Id, _ => 4);
    }

    private void ExecuteAddRequiredCard()
    {
        if (SelectedRequiredCard == null || RequiredCards.Count >= MaxRequiredCards) return;
        if (RequiredCards.Any(c => c.Id == SelectedRequiredCard.Id)) return;
        // 必須と除外は相互排他: 必須に追加したら除外から外す
        var dup = ExcludedCards.FirstOrDefault(c => c.Id == SelectedRequiredCard.Id);
        if (dup != null) ExcludedCards.Remove(dup);
        RequiredCards.Add(SelectedRequiredCard);
        SelectedRequiredCard = null;
        OnPropertyChanged(nameof(CanAddRequiredCard));
    }

    private void ExecuteRemoveRequiredCard(object? parameter)
    {
        if (parameter is SupportCard card)
        {
            RequiredCards.Remove(card);
            OnPropertyChanged(nameof(CanAddRequiredCard));
        }
    }

    private void ExecuteAddExcludedCard()
    {
        if (SelectedExcludedCard == null) return;
        if (ExcludedCards.Any(c => c.Id == SelectedExcludedCard.Id)) return;
        // 必須と除外は相互排他: 除外に追加したら必須から外す
        var dup = RequiredCards.FirstOrDefault(c => c.Id == SelectedExcludedCard.Id);
        if (dup != null)
        {
            RequiredCards.Remove(dup);
            OnPropertyChanged(nameof(CanAddRequiredCard));
        }
        ExcludedCards.Add(SelectedExcludedCard);
        SelectedExcludedCard = null;
    }

    private void ExecuteRemoveExcludedCard(object? parameter)
    {
        if (parameter is SupportCard card)
            ExcludedCards.Remove(card);
    }

    /// <summary>選択デッキのカードをワンクリックで除外し、再計算する</summary>
    private void ExecuteExcludeCard(object? parameter)
    {
        if (parameter is not string cardId || string.IsNullOrEmpty(cardId)) return;
        var card = _allCards.FirstOrDefault(c => c.Id == cardId);
        if (card == null) return;
        if (!ExcludedCards.Any(c => c.Id == cardId))
        {
            // 必須と除外は相互排他
            var dup = RequiredCards.FirstOrDefault(c => c.Id == cardId);
            if (dup != null)
            {
                RequiredCards.Remove(dup);
                OnPropertyChanged(nameof(CanAddRequiredCard));
            }
            ExcludedCards.Add(card);
        }
        // 除外後に次の候補を反映するため再計算
        if (_isHifMode) ExecuteHifCalculate();
        else ExecuteCalculate();
    }
}
