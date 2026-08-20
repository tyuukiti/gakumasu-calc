using System.Collections.ObjectModel;

namespace GakumasuCalc.ViewModels;

/// <summary>
/// プリセット4種（メモリー/イベント回数/HIF条件/スケジュール）で共通の
/// 読込・保存可否判定・上書き挿入・永続化。種別ごとの挙動差
/// （上限到達ダイアログの有無・保存失敗時の通知方法など）は呼び出し側に残す。
/// </summary>
internal static class PresetOps
{
    /// <summary>サービスから読み込んでコレクションを入れ替える。成功時のみ onLoaded を呼び、失敗時は Debug 出力のみ。</summary>
    public static void LoadInto<T>(ObservableCollection<T> target, Func<List<T>> load, string errorLabel, Action? onLoaded = null)
    {
        try
        {
            var presets = load();
            target.Clear();
            foreach (var p in presets)
                target.Add(p);
            onLoaded?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{errorLabel}: {ex.Message}");
        }
    }

    /// <summary>名前が空でなく、同名上書きか上限未満なら保存可。</summary>
    public static bool CanSave<T>(ObservableCollection<T> items, string newName, Func<T, string> getName, int max)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;
        // 同名は上書き扱い。同名が無くて上限超過なら不可
        var trimmed = newName.Trim();
        var existing = items.FirstOrDefault(p => getName(p) == trimmed);
        return existing != null || items.Count < max;
    }

    /// <summary>同名なら差し替え、無ければ上限未満のときだけ追加。上限到達で追加できなければ false。</summary>
    public static bool Upsert<T>(ObservableCollection<T> items, T preset, string name, Func<T, string> getName, int max)
    {
        var existing = items.FirstOrDefault(p => getName(p) == name);
        if (existing != null)
        {
            // 同名は上書き
            items[items.IndexOf(existing)] = preset;
            return true;
        }
        if (items.Count >= max) return false;
        items.Add(preset);
        return true;
    }

    /// <summary>永続化を実行し、失敗時はエラーダイアログを出して false。</summary>
    public static bool Persist<T>(ObservableCollection<T> items, Action<List<T>> save, string debugLabel, string dialogText, string dialogTitle)
    {
        try
        {
            save(items.ToList());
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"{debugLabel}: {ex}");
            System.Windows.MessageBox.Show(
                $"{dialogText}\n\n{ex.Message}",
                dialogTitle, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>永続化を実行し、失敗時は Debug 出力のみ（スケジュールプリセット用）。</summary>
    public static void PersistQuiet<T>(ObservableCollection<T> items, Action<List<T>> save, string errorLabel)
    {
        try { save(items.ToList()); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"{errorLabel}: {ex.Message}"); }
    }
}
