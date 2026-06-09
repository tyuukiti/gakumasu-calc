<#
.SYNOPSIS
  カード選択ロジックのテストを Web版(TS/Vitest) と C#版(xUnit) の両方で実行し、
  結果を test-results/ に保存する。

.DESCRIPTION
  出力物 (test-results/):
    vitest.txt          … TS テストの全テスト名+合否 (verbose コンソール出力の写し)
    vitest-junit.xml    … TS テストの JUnit XML (CI/ツール用、vite.config.ts が出力)
    dotnet.txt          … C# テストの全テスト名+合否 (detailed コンソール出力の写し)
    dotnet.trx          … C# テストの TRX (Visual Studio / CI で開ける)

  どんなテストがあるかは TESTS.md を参照。

.EXAMPLE
  pwsh ./run-tests.ps1
#>
$ErrorActionPreference = 'Continue'

# 子プロセス(node/dotnet)の UTF-8 出力を正しく復号・保存する (日本語テスト名の文字化け防止)
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$root = $PSScriptRoot
$out  = Join-Path $root 'test-results'
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host ''
Write-Host '===== Web版 (TypeScript / Vitest) =====' -ForegroundColor Cyan
Push-Location (Join-Path $root 'web')
try {
    & npm test 2>&1 | Tee-Object -FilePath (Join-Path $out 'vitest.txt') -Encoding utf8
    $tsExit = $LASTEXITCODE
} finally {
    Pop-Location
}

Write-Host ''
Write-Host '===== デスクトップ版 (C# / xUnit) =====' -ForegroundColor Cyan
& dotnet test (Join-Path $root 'GakumasuCalc.Tests') `
    --logger 'console;verbosity=detailed' `
    --logger "trx;LogFileName=dotnet.trx" `
    --results-directory $out 2>&1 | Tee-Object -FilePath (Join-Path $out 'dotnet.txt') -Encoding utf8
$csExit = $LASTEXITCODE

Write-Host ''
Write-Host '===== サマリ =====' -ForegroundColor Cyan
Write-Host ("Web版(TS)      : {0}" -f ($(if ($tsExit -eq 0) { 'PASS' } else { 'FAIL' })))
Write-Host ("デスクトップ(C#): {0}" -f ($(if ($csExit -eq 0) { 'PASS' } else { 'FAIL' })))
Write-Host ("結果ファイル    : {0}" -f $out)

if ($tsExit -ne 0 -or $csExit -ne 0) { exit 1 }
exit 0
