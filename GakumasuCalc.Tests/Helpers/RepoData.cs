using System.IO;
using GakumasuCalc.Models;
using GakumasuCalc.Services;
using GakumasuCalc.ViewModels;

namespace GakumasuCalc.Tests.Helpers;

/// <summary>
/// 実 YAML データ (リポジトリの Data/) を読み込むテスト用ローダ。
/// アプリ本体と同じローダサービスを使うので正規化挙動は一致する。結果はキャッシュ。
/// </summary>
public static class RepoData
{
    private static readonly object _lock = new();
    private static List<SupportCard>? _cards;
    private static List<TrainingPlan>? _plans;
    private static List<Character>? _characters;
    private static List<EventCountTemplate>? _templates;

    /// <summary>
    /// テストアセンブリの位置から真のリポジトリルートを探す。
    /// ビルド時に Data/ が bin にコピーされるため、Data/ ではなく
    /// ルート固有の GakumasuCalc.slnx を目印にする。
    /// </summary>
    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "GakumasuCalc.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("GakumasuCalc.slnx を含むリポジトリルートが見つかりません");
    }

    public static string DataDir() => Path.Combine(RepoRoot(), "Data");

    public static List<SupportCard> LoadAllCards()
    {
        lock (_lock)
        {
            if (_cards != null) return _cards;
            var yaml = new YamlDataService();
            var loader = new SupportCardLoaderService(yaml, Path.Combine(DataDir(), "SupportCards"));
            // あえて本番(デスクトップ)の読込順 (ファイル名順) のまま渡す。最適化器が内部で
            // ID 昇順に正準化するため、パリティが通れば「実装が入力順非依存」であることの証明になる。
            _cards = loader.LoadAllCards();
            return _cards;
        }
    }

    public static List<TrainingPlan> LoadPlans()
    {
        lock (_lock)
        {
            if (_plans != null) return _plans;
            var yaml = new YamlDataService();
            var loader = new PlanLoaderService(yaml, Path.Combine(DataDir(), "Plans"));
            _plans = loader.LoadAllPlans();
            return _plans;
        }
    }

    public static TrainingPlan LoadPlan(string id)
        => LoadPlans().FirstOrDefault(p => p.Id == id)
           ?? throw new KeyNotFoundException($"plan not found: {id}");

    public static SupportCard GetCard(string id)
        => LoadAllCards().FirstOrDefault(c => c.Id == id)
           ?? throw new KeyNotFoundException($"card not found: {id}");

    public static List<Character> LoadCharacters()
    {
        lock (_lock)
        {
            if (_characters != null) return _characters;
            var yaml = new YamlDataService();
            var loader = new CharacterLoaderService(yaml, Path.Combine(DataDir(), "Characters"));
            _characters = loader.LoadAll();
            return _characters;
        }
    }

    public static List<EventCountTemplate> LoadTemplates()
    {
        lock (_lock)
        {
            if (_templates != null) return _templates;
            var yaml = new YamlDataService();
            var file = yaml.LoadFromFile<EventCountTemplateFile>(
                Path.Combine(DataDir(), "Templates", "event_count_templates.yaml"));
            _templates = file?.Templates ?? new List<EventCountTemplate>();
            return _templates;
        }
    }

    /// <summary>(planId, name) でテンプレートの AdditionalCounts を取得 (Counts は既に AdditionalCounts 型)。</summary>
    public static AdditionalCounts TemplateCounts(string planId, string name)
    {
        var t = LoadTemplates().FirstOrDefault(x => x.PlanId == planId && x.Name == name)
            ?? throw new KeyNotFoundException($"template not found: {planId} / {name}");
        return t.Counts;
    }
}
