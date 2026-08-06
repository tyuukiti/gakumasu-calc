namespace GakumasuCalc.Models;

/// <summary>
/// UI表示状態 (ユーザーが調整したパネル高さ等) の永続化用ルート。
/// </summary>
public class UiStateFile
{
    /// <summary>パネルキー → ユーザーが調整した表示高さ(px)</summary>
    public Dictionary<string, double> PanelHeights { get; set; } = new();
}
