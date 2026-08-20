using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using GakumasuCalc.Models;
using GakumasuCalc.Services;

namespace GakumasuCalc.ViewModels;

public partial class MainViewModel
{
    public ICommand CheckUpdateCommand { get; private set; } = null!;
    public ICommand OpenReleasePageCommand { get; private set; } = null!;
    public ICommand DismissUpdateBannerCommand { get; private set; } = null!;
    // --- バージョンチェック関連 ---
    public string CurrentVersion { get; }

    private string _latestVersion = string.Empty;
    public string LatestVersion
    {
        get => _latestVersion;
        private set { if (SetProperty(ref _latestVersion, value)) OnPropertyChanged(nameof(UpdateMessage)); }
    }

    private string _latestReleaseUrl = string.Empty;
    public string LatestReleaseUrl
    {
        get => _latestReleaseUrl;
        private set => SetProperty(ref _latestReleaseUrl, value);
    }

    private bool _hasUpdate;
    public bool HasUpdate
    {
        get => _hasUpdate;
        private set
        {
            if (SetProperty(ref _hasUpdate, value))
            {
                OnPropertyChanged(nameof(IsUpdateBannerVisible));
                OnPropertyChanged(nameof(UpdateMessage));
            }
        }
    }

    private bool _isUpdateBannerDismissed;
    public bool IsUpdateBannerVisible => _hasUpdate && !_isUpdateBannerDismissed;

    private string _versionCheckStatus = string.Empty;
    public string VersionCheckStatus
    {
        get => _versionCheckStatus;
        private set => SetProperty(ref _versionCheckStatus, value);
    }

    public string UpdateMessage =>
        string.IsNullOrEmpty(_latestVersion)
            ? string.Empty
            : $"新しいバージョン v{_latestVersion} が公開されています";

    /// <summary>
    /// GitHub から最新リリースを取得して、現在バージョンと比較する。
    /// manual=true なら結果をステータステキストに反映（手動チェック用）。
    /// </summary>
    private async Task CheckUpdateAsync(bool manual)
    {
        if (manual)
            VersionCheckStatus = "確認中...";

        var latest = await _versionCheckService.GetLatestAsync();
        if (latest == null)
        {
            if (manual)
                VersionCheckStatus = "確認失敗（ネットワークまたは GitHub 側エラー）";
            return;
        }

        var isNewer = VersionCheckService.IsNewer(latest.NormalizedVersion, CurrentVersion);
        LatestVersion = latest.NormalizedVersion;
        LatestReleaseUrl = latest.HtmlUrl;
        HasUpdate = isNewer;
        // 手動チェックなら毎回バナーを再表示
        if (manual)
        {
            _isUpdateBannerDismissed = false;
            OnPropertyChanged(nameof(IsUpdateBannerVisible));
            VersionCheckStatus = isNewer
                ? $"v{latest.NormalizedVersion} が公開されています"
                : "最新版を利用中です";
        }
    }

    private void OpenReleasePage()
    {
        var url = string.IsNullOrEmpty(_latestReleaseUrl)
            ? "https://github.com/tyuukiti/gakumasu-calc/releases/latest"
            : _latestReleaseUrl;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"リリースページ起動エラー: {ex.Message}");
        }
    }
}
