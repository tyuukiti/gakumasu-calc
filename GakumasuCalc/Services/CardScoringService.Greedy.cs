using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

public partial class CardScoringService
{
    /// <summary>
    /// チェックポイント状態からグリーディに所持枠を埋める。
    /// </summary>
    private (List<CardScore> Selected, HashSet<string> UsedIds, int AccVo, int AccDa, int AccVi)
        GreedyFillOwned(
            List<CardScore> contributions,
            List<CardScore> selectedInit,
            HashSet<string> usedIdsInit,
            int accVoInit, int accDaInit, int accViInit,
            Dictionary<string, int> remainingSlotsInit,
            int remainingFreeInit,
            int ownedSlots,
            int statCap,
            Character? character = null,
            OverflowPenaltyConfig? overflowPenalty = null)
    {
        var sel = new List<CardScore>(selectedInit);
        var used = new HashSet<string>(usedIdsInit);
        int aVo = accVoInit, aDa = accDaInit, aVi = accViInit;
        double voMul = 1.0 + (character?.ParaBonus.Vo ?? 0) / 100.0;
        double daMul = 1.0 + (character?.ParaBonus.Da ?? 0) / 100.0;
        double viMul = 1.0 + (character?.ParaBonus.Vi ?? 0) / 100.0;

        // 属性枠
        foreach (var kvp in remainingSlotsInit.OrderByDescending(k => k.Value))
        {
            var type = kvp.Key;
            int count = kvp.Value;
            if (count <= 0) continue;

            var candidates = contributions
                .Where(cs => (cs.Card.Type == type || cs.Card.Type == "all" || cs.Card.Type == "as")
                             && !used.Contains(cs.Card.Id))
                .ToList();

            for (int i = 0; i < count && sel.Count < ownedSlots; i++)
            {
                var best = SelectBestCard(candidates, used, aVo, aDa, aVi, statCap, character, overflowPenalty);
                if (best == null) break;
                sel.Add(best);
                used.Add(best.Card.Id);
                aVo += (int)(best.RawVo * voMul);
                aDa += (int)(best.RawDa * daMul);
                aVi += (int)(best.RawVi * viMul);
            }
        }

        // フリー枠
        for (int i = 0; i < remainingFreeInit && sel.Count < ownedSlots; i++)
        {
            var freeCandidates = contributions
                .Where(cs => !used.Contains(cs.Card.Id))
                .ToList();
            var best = SelectBestCard(freeCandidates, used, aVo, aDa, aVi, statCap, character, overflowPenalty);
            if (best == null) break;
            sel.Add(best);
            used.Add(best.Card.Id);
            aVo += (int)(best.RawVo * voMul);
            aDa += (int)(best.RawDa * daMul);
            aVi += (int)(best.RawVi * viMul);
        }

        // 補充
        if (sel.Count < ownedSlots)
        {
            var remaining = contributions
                .Where(cs => !used.Contains(cs.Card.Id))
                .ToList();
            while (sel.Count < ownedSlots)
            {
                var best = SelectBestCard(remaining, used, aVo, aDa, aVi, statCap, character, overflowPenalty);
                if (best == null) break;
                sel.Add(best);
                used.Add(best.Card.Id);
                aVo += (int)(best.RawVo * voMul);
                aDa += (int)(best.RawDa * daMul);
                aVi += (int)(best.RawVi * viMul);
            }
        }

        return (sel, used, aVo, aDa, aVi);
    }
}
