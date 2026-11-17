<#
.SYNOPSIS
    从本机 SPT 安装目录收集 GamePanelHUD 编译所需的引用 DLL。

.DESCRIPTION
    本仓库不包含 refs/ 下的程序集：它们大多是 EscapeFromTarkov 的游戏厂商版权文件，
    不可再分发。请用本脚本从你自己的 SPT 安装目录生成 refs/，再编译。

    这些 DLL 的来源只有三类：
      1. 游戏本体  <GameRoot>\EscapeFromTarkov_Data\Managed\
      2. BepInEx   <GameRoot>\BepInEx\core\
      3. 已装模组  <GameRoot>\BepInEx\plugins\

.PARAMETER GameRoot
    SPT 安装根目录（含 EscapeFromTarkov.exe 的那个目录）。
    例：-GameRoot "C:\SPT"

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\collect-refs.ps1 -GameRoot "C:\SPT"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$GameRoot
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$refs = Join-Path $repoRoot 'refs'

if (-not (Test-Path $GameRoot)) {
    throw "GameRoot 不存在: $GameRoot"
}

$managed = Join-Path $GameRoot 'EscapeFromTarkov_Data\Managed'
$bepCore = Join-Path $GameRoot 'BepInEx\core'
$bepPlugins = Join-Path $GameRoot 'BepInEx\plugins'

New-Item -ItemType Directory -Force -Path $refs | Out-Null

# 必需引用：名字 → 相对来源（在 Managed / core / plugins 下按顺序找）
$required = @(
    @{ Name = 'Assembly-CSharp.dll';        Root = 'managed' },
    @{ Name = 'UnityEngine.dll';            Root = 'managed' },
    @{ Name = 'UnityEngine.CoreModule.dll'; Root = 'managed' },
    @{ Name = 'UnityEngine.UI.dll';         Root = 'managed' },
    @{ Name = 'UnityEngine.UIModule.dll';   Root = 'managed' },
    @{ Name = 'UnityEngine.AnimationModule.dll'; Root = 'managed' },
    @{ Name = 'UnityEngine.AssetBundleModule.dll'; Root = 'managed' },
    @{ Name = 'UnityEngine.TextRenderingModule.dll'; Root = 'managed' },
    @{ Name = 'UnityEngine.IMGUIModule.dll'; Root = 'managed' },
    @{ Name = 'Unity.TextMeshPro.dll';      Root = 'managed' },
    @{ Name = 'Newtonsoft.Json.dll';        Root = 'managed' },
    @{ Name = 'Comfort.dll';                Root = 'managed' },
    @{ Name = 'BepInEx.dll';                Root = 'core' },
    @{ Name = '0Harmony.dll';               Root = 'core' },
    @{ Name = 'Mono.Cecil.dll';             Root = 'core' },
    @{ Name = 'MonoMod.Utils.dll';          Root = 'core' }
)

# 模组依赖：这些随模组分发，路径可能带子目录，递归找
$fromPlugins = @(
    @{ Name = 'KmyTarkovApi.dll';        Hint = 'kmyuhkyuk-KmyTarkovApi' },
    @{ Name = 'KmyTarkovReflection.dll'; Hint = 'kmyuhkyuk-KmyTarkovApi' },
    @{ Name = 'KmyTarkovUtils.dll';      Hint = 'kmyuhkyuk-KmyTarkovApi' },
    @{ Name = 'spt-common.dll';          Hint = 'spt' }
)

# Sirenix 系列（KmyTarkovApi 的可选序列化依赖，通常随游戏 Managed 一起分发）
$sirenix = @('Sirenix.Serialization.dll', 'Sirenix.Serialization.Config.dll',
             'Sirenix.Utilities.dll', 'Sirenix.OdinInspector.Attributes.dll')

$missing = @()
$copied = 0

function Copy-Ref {
    param([string]$Source, [string]$Name)
    Copy-Item $Source (Join-Path $refs $Name) -Force
    Write-Host ("  + {0}" -f $Name) -ForegroundColor Green
    $script:copied++
}

foreach ($entry in $required) {
    $name = $entry.Name
    $dir = switch ($entry.Root) {
        'managed' { $managed }
        'core'    { $bepCore }
        default   { throw "未知来源: $($entry.Root)" }
    }
    $path = Join-Path $dir $name
    if (Test-Path $path) { Copy-Ref $path $name }
    else { $missing += "$name  (期望位置: $dir)" }
}

foreach ($entry in $fromPlugins) {
    $name = $entry.Name
    $path = Join-Path $bepPlugins $name
    if (Test-Path $path) { Copy-Ref $path $name; continue }

    $hint = Join-Path $bepPlugins $entry.Hint
    if (Test-Path $hint) {
        $found = Get-ChildItem $hint -Recurse -File -Filter $name -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($found) { Copy-Ref $found.FullName $name; continue }
    }

    $missing += "$name  (期望在 $bepPlugins\$($entry.Hint) 或 $bepPlugins)"
}

foreach ($name in $sirenix) {
    $path = Join-Path $managed $name
    if (Test-Path $path) { Copy-Ref $path $name }
    else { $missing += "$name  (通常随 4.1 客户端 Managed 目录分发)" }
}

Write-Host ""
Write-Host ("已拷贝 {0} 个引用到 {1}" -f $copied, $refs) -ForegroundColor Cyan

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "缺少以下引用，编译会失败：" -ForegroundColor Yellow
    $missing | ForEach-Object { Write-Host ("  - {0}" -f $_) -ForegroundColor Yellow }
    Write-Host ""
    Write-Host "GamePanelHUDCompass 还需要 spt-common.dll；若未装 SPT 客户端组件，请从 SPT 安装包的 BepInEx\plugins\spt\ 取。" -ForegroundColor Yellow
    exit 1
}

Write-Host ""
Write-Host "完成。下一步：" -ForegroundColor Cyan
Write-Host "  dotnet build GamePanelHUD.sln -c Release"
Write-Host ""
Write-Host "注意：refs/ 已在 .gitignore 中，请勿提交——这些程序集受游戏厂商版权约束，不可再分发。"
