<#
.SYNOPSIS
    GakumasuCalc リリースビルド & ZIP作成スクリプト
.DESCRIPTION
    Release構成でビルドし、配布用ZIPファイルを2種類作成します。
    - self-contained版: .NETランタイム同梱（PCにランタイム不要）
      3アプリでランタイムDLLを共有し、runtime/サブフォルダに格納。
    - framework-dependent版: ランタイム別途必要（軽量）
    カード画像はData/Imagesから_mapping.tsv以外を除外します。
.EXAMPLE
    .\build-release.ps1
    .\build-release.ps1 -Version "1.0.0"
#>
param(
    [string]$Version = "0.0.0"
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot
$OutputDir = Join-Path $RepoRoot "release"
$Projects = @("GakumasuCalc", "CardInventoryManager", "SupportCardEditor")

Write-Host "=== GakumasuCalc Release Build v$Version ===" -ForegroundColor Cyan

# Clean
if (Test-Path $OutputDir) {
    try {
        Remove-Item $OutputDir -Recurse -Force -ErrorAction Stop
    } catch {
        Write-Host "Warning: Could not fully clean $OutputDir, retrying..." -ForegroundColor Yellow
        Start-Sleep -Seconds 2
        Remove-Item $OutputDir -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $OutputDir | Out-Null

function Stage-DataFiles($StageDir) {
    $DataSrc = Join-Path $RepoRoot "Data"
    $DataDst = Join-Path $StageDir "Data"

    foreach ($sub in @("Plans", "SupportCards", "Templates")) {
        $dst = Join-Path $DataDst $sub
        New-Item -ItemType Directory -Path $dst -Force | Out-Null
        Get-ChildItem (Join-Path $DataSrc $sub) -Filter "*.yaml" | Copy-Item -Destination $dst
    }

    $ImgDst = Join-Path $DataDst "Images"
    New-Item -ItemType Directory -Path $ImgDst -Force | Out-Null
    $MappingFile = Join-Path (Join-Path $DataSrc "Images") "_mapping.tsv"
    if (Test-Path $MappingFile) {
        Copy-Item $MappingFile -Destination $ImgDst
    }

    $InvDst = Join-Path $DataDst "Inventory"
    New-Item -ItemType Directory -Path $InvDst -Force | Out-Null

    Copy-Item (Join-Path $RepoRoot "README.md") -Destination $StageDir
}

# DLLをruntime/サブフォルダに移動し、deps.json/runtimeconfig.jsonをパッチ
function Reorganize-Runtime($StageDir) {
    $RuntimeDir = Join-Path $StageDir "runtime"
    New-Item -ItemType Directory -Path $RuntimeDir -Force | Out-Null

    # ルートに残すネイティブホストDLL
    $keepNative = @("hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "clrjit.dll")

    # アプリ自身のDLL名を収集
    $appDlls = Get-ChildItem $StageDir -Filter "*.exe" -File |
        ForEach-Object { $_.BaseName + ".dll" }

    $keepSet = New-Object System.Collections.Generic.HashSet[string]
    foreach ($n in $keepNative) { $keepSet.Add($n) | Out-Null }
    foreach ($n in $appDlls)   { $keepSet.Add($n) | Out-Null }

    # exe, json, 指定DLL以外を全てruntime/へ移動
    $moved = 0
    Get-ChildItem $StageDir -File | Where-Object {
        $_.Extension -notin @(".exe", ".json") -and
        -not $keepSet.Contains($_.Name)
    } | ForEach-Object {
        Move-Item $_.FullName -Destination $RuntimeDir
        $moved++
    }
    Write-Host "  Moved $moved files to runtime/" -ForegroundColor Gray

    # Pythonスクリプトでdeps.json/runtimeconfig.jsonをパッチ
    $patchScript = Join-Path $RepoRoot "scripts\patch-deps-json.py"
    python3 $patchScript $StageDir
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  Failed to patch JSON files" -ForegroundColor Red
        exit 1
    }
}

# --- Build 1: self-contained (shared runtime) ---
Write-Host "`n[1/5] Building self-contained..." -ForegroundColor Yellow
$BuildSC = Join-Path $OutputDir "build-sc"
foreach ($proj in $Projects) {
    $csproj = Join-Path $RepoRoot "$proj\$proj.csproj"
    if (Test-Path $csproj) {
        dotnet publish $csproj -c Release -o $BuildSC --self-contained -r win-x64
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Build failed: $proj" -ForegroundColor Red
            exit 1
        }
    }
}
Write-Host "Self-contained build succeeded." -ForegroundColor Green

# --- Build 2: framework-dependent ---
Write-Host "`n[2/5] Building framework-dependent..." -ForegroundColor Yellow
$BuildFD = Join-Path $OutputDir "build-fd"
foreach ($proj in $Projects) {
    $csproj = Join-Path $RepoRoot "$proj\$proj.csproj"
    if (Test-Path $csproj) {
        dotnet publish $csproj -c Release -o $BuildFD --no-self-contained
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Build failed: $proj" -ForegroundColor Red
            exit 1
        }
    }
}
Write-Host "Framework-dependent build succeeded." -ForegroundColor Green

# --- Stage & ZIP: self-contained ---
Write-Host "`n[3/5] Staging self-contained..." -ForegroundColor Yellow
$StageSC = Join-Path $OutputDir "GakumasuCalc-v$Version-win-x64"
New-Item -ItemType Directory -Path $StageSC | Out-Null
Get-ChildItem $BuildSC -File | Where-Object { $_.Extension -ne ".pdb" } |
    Copy-Item -Destination $StageSC
Stage-DataFiles $StageSC
Reorganize-Runtime $StageSC

$ZipSC = Join-Path $OutputDir "GakumasuCalc-v$Version-win-x64.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($StageSC, $ZipSC, [System.IO.Compression.CompressionLevel]::Optimal, $true)
Write-Host "Created: $ZipSC" -ForegroundColor Green

# --- Stage & ZIP: framework-dependent ---
Write-Host "`n[4/5] Staging framework-dependent..." -ForegroundColor Yellow
$StageFD = Join-Path $OutputDir "GakumasuCalc-v$Version-dotnet-required"
New-Item -ItemType Directory -Path $StageFD | Out-Null
Get-ChildItem $BuildFD -File | Where-Object { $_.Extension -ne ".pdb" } |
    Copy-Item -Destination $StageFD
Stage-DataFiles $StageFD

$ZipFD = Join-Path $OutputDir "GakumasuCalc-v$Version-dotnet-required.zip"
[System.IO.Compression.ZipFile]::CreateFromDirectory($StageFD, $ZipFD, [System.IO.Compression.CompressionLevel]::Optimal, $true)
Write-Host "Created: $ZipFD" -ForegroundColor Green

# --- Summary ---
Write-Host "`n[5/5] Summary" -ForegroundColor Yellow
foreach ($zip in @($ZipSC, $ZipFD)) {
    $size = [math]::Round((Get-Item $zip).Length / 1MB, 2)
    $name = Split-Path $zip -Leaf
    Write-Host "  $name  (${size} MB)"
}
Write-Host "`nDone!" -ForegroundColor Cyan
