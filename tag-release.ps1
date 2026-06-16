<#
.SYNOPSIS
    リリースタグを打って公開ワークフローをトリガーする。
.DESCRIPTION
    main を最新化し、GakumasuCalc.csproj の <Version> からタグ (X.Y.Z) を作成して push する。
    タグ push で .github/workflows/release.yml が起動し、GitHub Release が自動作成される。

    実行前提: バージョンUP + releases/release_vXXX.md を含む変更が main にマージ済みであること。
    (このスクリプトはバージョンUP自体は行わない。マージ後のタグ付けのみを自動化する)
.EXAMPLE
    .\tag-release.ps1              # csproj の <Version> からタグを自動決定して push
.EXAMPLE
    .\tag-release.ps1 -Version 2.8.1   # バージョンを明示指定
.EXAMPLE
    .\tag-release.ps1 -DryRun     # checkout/pull と各種チェックだけ実行し tag/push はしない
#>
param(
    [string]$Version,
    [string]$Remote = "origin",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
Set-Location $RepoRoot

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments)] [string[]]$GitArgs)
    Write-Host "> git $($GitArgs -join ' ')" -ForegroundColor DarkGray
    & git @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw "git $($GitArgs -join ' ') が失敗しました (exit $LASTEXITCODE)"
    }
}

$Csproj = Join-Path $RepoRoot "GakumasuCalc\GakumasuCalc.csproj"

# 1. main へ切り替え & 最新化 (削除済みリモートブランチも prune)
Write-Host "=== main を最新化 ===" -ForegroundColor Cyan
Invoke-Git checkout main
Invoke-Git pull --prune

# 2. バージョン決定 (引数がなければ csproj の <Version> から)
if (-not $Version) {
    if (-not (Test-Path $Csproj)) { throw "csproj が見つかりません: $Csproj" }
    $m = [regex]::Match((Get-Content $Csproj -Raw), '<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>')
    if (-not $m.Success) { throw "csproj から <Version> を取得できませんでした: $Csproj" }
    $Version = $m.Groups[1].Value
    Write-Host "csproj から検出したバージョン: $Version" -ForegroundColor Green
}

# 3. フォーマット検証 (release.yml のトリガー条件と同じ X.Y.Z)
if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
    throw "バージョン形式が不正です: '$Version' (期待: X.Y.Z)"
}

# 4. リリースノート存在チェック (workflow が必須とするため事前に弾く)
$compact = $Version -replace '\.', ''
$notes = "releases/release_v$compact.md"
if (-not (Test-Path (Join-Path $RepoRoot $notes))) {
    throw "リリースノートがありません: $notes`n  -> バージョンUPの変更が main にマージ済みか確認してください"
}
Write-Host "リリースノート: $notes (OK)" -ForegroundColor Green

# 5. タグ重複チェック (local / remote)
& git rev-parse -q --verify "refs/tags/$Version" 2>&1 | Out-Null
if ($LASTEXITCODE -eq 0) {
    throw "タグ '$Version' は既にローカルに存在します (git tag -d $Version で削除可)"
}
if (& git ls-remote --tags $Remote $Version) {
    throw "タグ '$Version' は既に $Remote に存在します"
}

# 6. タグ作成 & push
if ($DryRun) {
    Write-Host "[DRY-RUN] git tag $Version / git push $Remote $Version は実行しません" -ForegroundColor Yellow
    return
}
Write-Host "=== タグ $Version を作成して push ===" -ForegroundColor Cyan
Invoke-Git tag $Version
Invoke-Git push $Remote $Version

# 7. 案内
$slug = ((& git remote get-url $Remote) -replace '\.git$', '') -replace '^.*github\.com[:/]', ''
Write-Host ""
Write-Host "OK: タグ $Version を push しました。リリースワークフローが起動します。" -ForegroundColor Green
Write-Host "   Actions : https://github.com/$slug/actions"
Write-Host "   Releases: https://github.com/$slug/releases"
