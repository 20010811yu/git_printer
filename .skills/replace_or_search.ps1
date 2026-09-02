<#
 ============================================================================
  replace_or_search.ps1 —— 可靠的搜索 / 替换命令行工具
  用途：替代自带 search_codebase（规则13）和 editor 替换模式（规则6）
  特点：
   1) 字面量匹配，不把 . * ( ) 当正则/通配符，中文稳定
   2) 用 .NET File API 读写，编码显式可控，不依赖 Set-Content 默认 ANSI
   3) 支持 -DryRun 预览，不落地
  运行：在 run_commands 中调用（PowerShell）
 ============================================================================
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('search', 'replace', 'defs')]
    [string]$Action,

    [Parameter(Mandatory = $true)]
    [string]$Pattern,

    [string]$Replacement = '',

    [string]$Path = '.',

    [string]$Filter = '*.cs',

    [switch]$Recurse,

    [switch]$DryRun,

    [string]$EncodingName = 'utf8'
)

# ---------- 选择编码 ----------
$enc = $null
switch ($EncodingName.ToLower()) {
    'utf8bom'  { $enc = New-Object System.Text.UTF8Encoding($true)  }
    'utf8'     { $enc = New-Object System.Text.UTF8Encoding($false) }
    'ascii'    { $enc = New-Object System.Text.ASCIIEncoding }
    'default'  { $enc = [System.Text.Encoding]::Default }
    'gbk'      { $enc = [System.Text.Encoding]::GetEncoding('gb2312') }
    default    { $enc = New-Object System.Text.UTF8Encoding($false) }
}

# ---------- 收集目标文件 ----------
$items = @()
if ($Recurse) {
    $items = Get-ChildItem -Path $Path -Filter $Filter -Recurse -File
} else {
    $items = Get-ChildItem -Path $Path -Filter $Filter -File
}
if (-not $items) {
    Write-Host "[!] 未找到匹配文件：$Path  filter=$Filter"
    exit 1
}

$total = 0

foreach ($f in $items) {
    if ($Action -eq 'defs') {
        # ---------- 定位方法定义（排除调用点/事件连线/注释） ----------
        $lines = [System.IO.File]::ReadAllLines($f.FullName, $enc)
        $pat = [regex]::Escape($Pattern) + '\s*\('
        $hit = $false
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $trim = $line.Trim()
            if ($trim -eq '' -or $trim.StartsWith('//') -or $trim.StartsWith('/*') -or $trim.StartsWith('*')) { continue }
            if ($line -notmatch $pat) { continue }
            $isDef = $trim -match '^(public|private|protected|internal|static|virtual|override|sealed|abstract|extern)\b'
            if (-not $isDef) { $isDef = $trim -match '^\w+(\[\])?\s+\w+\s*\(' }
            if ($isDef) {
                $hit = $true
                $n = $i + 1
                Write-Host ("{0}:{1}: {2}" -f $f.FullName, $n, $line.Trim())
            }
        }
        if ($hit) { $total++ }
        continue
    }

    if ($Action -eq 'search') {
        # ---------- 搜索：逐行字面量 Contains 匹配 ----------
        $lines = [System.IO.File]::ReadAllLines($f.FullName, $enc)
        $hit = $false
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i].Contains($Pattern)) {
                $hit = $true
                $n = $i + 1
                Write-Host ("{0}:{1}: {2}" -f $f.FullName, $n, $lines[$i].Trim())
            }
        }
        if ($hit) { $total++ }
        continue
    }

    # ---------- 替换：字面量 Replace ----------
    $content = [System.IO.File]::ReadAllText($f.FullName, $enc)
    if ($content.Contains($Pattern)) {
        $total++
        $new = $content.Replace($Pattern, $Replacement)
        if ($DryRun) {
            Write-Host "[DRY-RUN] $($f.FullName) : $Pattern -> $Replacement"
        } else {
            [System.IO.File]::WriteAllText($f.FullName, $new, $enc)
            Write-Host "[OK] $($f.FullName)"
        }
    }
}

# ---------- 汇总 ----------
if ($Action -eq 'search') {
    Write-Host "`n== 搜索完成：$total 个文件包含 '$Pattern' =="
} elseif ($Action -eq 'defs') {
    Write-Host "`n== 定义定位完成：$total 处 '$Pattern' =="
} else {
    Write-Host "`n== 替换完成：$total 处  ($Pattern -> $Replacement)  dry-run=$DryRun =="
}
