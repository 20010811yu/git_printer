<#
 ============================================================================
  install.ps1 —— 把 replace-search skill 一键安装到目标项目
  用法：
    powershell -NoProfile -ExecutionPolicy Bypass -File install.ps1 -Target "D:\目标项目根目录"
    （加 -NoRules 则只复制脚本、不注入规则片段）

  安装后目标项目结构：
    <目标>\skill\replace_or_search.ps1         工具脚本
    <目标>\skill\问题汇总.md                   使用问题记录
    <目标>\.clinerules\工作区规则.md              若存在则追加规则片段，否则新建
 ============================================================================
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Target,

    [switch]$NoRules
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$enc = New-Object System.Text.UTF8Encoding($false)
$newline = "`r`n"

Write-Host "== 目标：$Target =="

# ---------- 1) 校验目标目录 ----------
if (-not (Test-Path -LiteralPath $Target)) {
    Write-Host "[!] 目标目录不存在：$Target"
    exit 1
}

# ---------- 2) 复制工具脚本 ----------
$guideDir = Join-Path $Target 'skill'
New-Item -ItemType Directory -Force -Path $guideDir | Out-Null
$srcScript = Join-Path $scriptDir 'replace_or_search.ps1'
$dstScript = Join-Path $guideDir 'replace_or_search.ps1'
Copy-Item -LiteralPath $srcScript -Destination $dstScript -Force
Write-Host "[OK] 脚本 → $dstScript"

$srcLog = Join-Path $scriptDir '问题汇总.md'
$dstLog = Join-Path $guideDir '问题汇总.md'
if (Test-Path -LiteralPath $srcLog) {
    Copy-Item -LiteralPath $srcLog -Destination $dstLog -Force
    Write-Host "[OK] 问题汇总 → $dstLog"
}

# ---------- 3) 注入规则片段 ----------
if (-not $NoRules) {
    $ruleFile = Join-Path $Target '.clinerules\工作区规则.md'
    $fragment = Join-Path $scriptDir '规则片段.md'

    if (Test-Path -LiteralPath $ruleFile) {
        $cur = [System.IO.File]::ReadAllText($ruleFile, $enc)
        if ($cur.Contains('可靠搜索替代')) {
            Write-Host "[跳过] 目标规则文件已包含该 skill 内容"
        } else {
            $frag = [System.IO.File]::ReadAllText($fragment, $enc)
            $append = $newline + $newline + $frag
            [System.IO.File]::AppendAllText($ruleFile, $append, $enc)
            Write-Host "[OK] 规则片段已追加 → $ruleFile"
        }
    } else {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ruleFile) | Out-Null
        Copy-Item -LiteralPath $fragment -Destination $ruleFile -Force
        Write-Host "[OK] 新建规则文件 → $ruleFile"
    }
}

Write-Host ""
Write-Host "== 安装完成 =="
Write-Host "  提示：以后可直接用  powershell -NoProfile -ExecutionPolicy Bypass -File 'skill\replace_or_search.ps1' -Action search/replace"
