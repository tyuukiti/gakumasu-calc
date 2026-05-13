using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using GakumasuCalc.Models;

namespace GakumasuCalc.Services;

/// <summary>
/// GitHub Releases API を叩いて最新バージョンを取得し、現在バージョンと比較する。
/// オフライン環境や GitHub 障害時にアプリ起動を妨げないよう、例外はすべて null 返却で吸収する。
/// </summary>
public class VersionCheckService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/tyuukiti/gakumasu-calc/releases/latest";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>現在実行中のアセンブリのバージョン（例: "1.5.0"）。</summary>
    public static string GetCurrentVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        // .csproj の <Version> から AssemblyInformationalVersionAttribute に入る値を優先
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            // "1.5.0+commitHash" 形式の場合は + 以降を捨てる
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
        // フォールバック: AssemblyVersion (Major.Minor.Build.Revision → Major.Minor.Build)
        var v = asm.GetName().Version;
        return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>GitHub から最新リリース情報を取得する。失敗時は null。</summary>
    public async Task<ReleaseInfo?> GetLatestAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = Timeout };
            // GitHub API は User-Agent ヘッダを必須にしている
            client.DefaultRequestHeaders.Add("User-Agent", "GakumasuCalc");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

            var json = await client.GetStringAsync(LatestReleaseUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new ReleaseInfo
            {
                TagName = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "",
                Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                HtmlUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "",
            };
        }
        catch
        {
            // ネットワーク不通、レート制限、レスポンス異常等はすべて静かに無視
            return null;
        }
    }

    /// <summary>
    /// セマンティックバージョン文字列 (例 "1.5.0", "v1.5.0", "1.5.0-beta") を比較する。
    /// latest &gt; current のとき true。比較不能なら false。
    /// </summary>
    public static bool IsNewer(string latest, string current)
    {
        var l = ParseVersion(latest);
        var c = ParseVersion(current);
        if (l == null || c == null) return false;
        return l > c;
    }

    private static Version? ParseVersion(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s[1..];
        // プレリリース・ビルドメタデータを切り捨て (1.5.0-beta+abc → 1.5.0)
        var dash = s.IndexOfAny(new[] { '-', '+' });
        if (dash >= 0) s = s[..dash];
        return Version.TryParse(s, out var v) ? v : null;
    }
}
