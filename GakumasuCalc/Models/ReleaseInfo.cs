namespace GakumasuCalc.Models;

/// <summary>
/// GitHub Releases API から取得した最新リリース情報。
/// </summary>
public class ReleaseInfo
{
    /// <summary>タグ名（例: "1.5.0" や "v1.5.0"）。先頭の v は含む場合と含まない場合がある。</summary>
    public string TagName { get; set; } = string.Empty;
    /// <summary>リリース名（例: "v1.5.0"）。</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>ブラウザで開くためのリリースページURL。</summary>
    public string HtmlUrl { get; set; } = string.Empty;

    /// <summary>tag_name から先頭の "v" を除いた純粋なバージョン文字列（例: "1.5.0"）。</summary>
    public string NormalizedVersion =>
        TagName.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? TagName[1..] : TagName;
}
