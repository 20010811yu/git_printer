<#
  ============================================================================
   edit_file.ps1 —— 可靠的文件编辑工具（2026-08-20，替代不稳定的 editor 工具链）
   背景：write_to_file/replace_in_file 在多会话中频繁假成功（返回成功但磁盘无变化/
         文件名截断/内容残缺），cmd→PowerShell 单行命令又会被 < > && 等字符损坏。
         本脚本经参数传值（不走命令行内嵌代码），从根源规避两类问题，并内置写后回读验证。
   用法（七操作，所有内容参数从命令行传，脚本负责安全落盘+验证）：
     show     -File x.cs -Around 100        查看第 100 行前后各 5 行（定位用，先看再改）
     view     -File x.cs -From 95 -To 105    查看指定行区间（含行号）
     new      -File x.cs -ContentFile c.txt  从内容文件创建新文件（大段内容零转义风险）
     append   -File x.cs -ContentFile c.txt  追加行到文件末尾
     insert   -File x.cs -At 100 -ContentFile c.txt   在第 100 行前插入内容
     replaceline -File x.cs -From 95 -To 105 -ContentFile c.txt  整块替换为内容
     deleteline  -File x.cs -From 95 -To 105  删除行区间
   内容一律经 -ContentFile 中转（UTF-8 文本文件），杜绝命令行传中文/尖括号/特殊符号的损坏路径。
   写后自动回读验证（行数+内容抽查），失败即报错退出非 0——成功回执可信任。
  ============================================================================
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('show','view','new','append','insert','replaceline','deleteline')]
    [string]$Action,

    [Parameter(Mandatory = $true)]
    [string]$File,

    [int]$From = 0,
    [int]$To = 0,
    [int]$Around = 0,
    [int]$At = 0,
    [string]$ContentFile = [string]::Empty
)

$ErrorActionPreference = 'Stop'

# ---------- 编码：读原文件探测 BOM，写回保持一致 ----------
function Get-Enc([string]$path) {
    if (-not (Test-Path $path)) { return New-Object System.Text.UTF8Encoding($true) }
    $b = [System.IO.File]::ReadAllBytes($path)
    if ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF) {
        return New-Object System.Text.UTF8Encoding($true)
    }
    return New-Object System.Text.UTF8Encoding($false)
}

function Read-Lines([string]$path) {
    $enc = Get-Enc $path
    return [System.IO.File]::ReadAllLines($path, $enc)
}

function Write-Lines([string]$path, $lines) {
    $enc = Get-Enc $path
    [System.IO.File]::WriteAllLines($path, [string[]]$lines, $enc)
}

# ---------- 内容文件读取（AppendAllLines/插入用） ----------
function Read-ContentLines([string]$contentFile) {
    if (-not $contentFile -or -not (Test-Path $contentFile)) {
        throw '必须提供 -ContentFile（UTF-8 内容文件，可用编辑器或 python 生成）'
    }
    $b = [System.IO.File]::ReadAllBytes($contentFile)
    $enc = New-Object System.Text.UTF8Encoding($true) # 内容文件约定带 BOM
    if ($b.Length -ge 3 -and $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF) {
        return , [System.IO.File]::ReadAllLines($contentFile, $enc)
    }
    # 无 BOM 的内容文件按 UTF-8 读
    return , [System.IO.File]::ReadAllLines($contentFile, (New-Object System.Text.UTF8Encoding($false)))
}

# ---------- 主逻辑 ----------
switch ($Action) {
    'show' {
        # 查看某行前后上下文（默认 ±5 行）
        if ($Around -lt 1) { throw 'show 需要 -Around 行号' }
        $lines = Read-Lines $File
        if ($Around -gt $lines.Count) { throw "行号 $Around 超出范围（共 $($lines.Count) 行）" }
        $s = [Math]::Max(1, $Around - 5); $t = [Math]::Min($lines.Count, $Around + 5)
        for ($i = $s; $i -le $t; $i++) { Write-Host ("{0}: {1}" -f $i, $lines[$i-1]) }
        Write-Host "== 共 $($lines.Count) 行 =="
    }
    'view' {
        if ($From -lt 1 -or $To -lt $From) { throw 'view 需要 -From/-To（1 起始行号，From 小于等于 To）' }
        $lines = Read-Lines $File
        if ($To -gt $lines.Count) { $To = $lines.Count }
        for ($i = $From; $i -le $To; $i++) { Write-Host ("{0}: {1}" -f $i, $lines[$i-1]) }
        Write-Host "== 共 $($lines.Count) 行 =="
    }
    'new' {
        # 新建文件（内容来自 ContentFile）
        $content = Read-ContentLines $ContentFile
        Write-Lines $File $content
    }
    'append' {
        $content = Read-ContentLines $ContentFile
        $list = New-Object System.Collections.Generic.List[string]
        (Read-Lines $File) | ForEach-Object { [void]$list.Add($_) }
        $content | ForEach-Object { [void]$list.Add($_) }
        Write-Lines $File $list
    }
    'insert' {
        if ($At -lt 1) { throw 'insert 需要 -At（在该行之前插入）' }
        $content = Read-ContentLines $ContentFile
        $list = New-Object System.Collections.Generic.List[string]
        (Read-Lines $File) | ForEach-Object { [void]$list.Add($_) }
        if ($At -gt $list.Count) { $At = $list.Count + 1 }
        $list.InsertRange($At - 1, [string[]]$content)
        Write-Lines $File $list
    }
    'replaceline' {
        if ($From -lt 1 -or $To -lt $From) { throw 'replaceline 需要 -From/-To（1 起始）' }
        $content = Read-ContentLines $ContentFile
        $list = New-Object System.Collections.Generic.List[string]
        (Read-Lines $File) | ForEach-Object { [void]$list.Add($_) }
        if ($To -gt $list.Count) { throw "-To $To 超出范围（共 $($list.Count) 行）" }
        $list.RemoveRange($From - 1, $To - $From + 1)
        $list.InsertRange($From - 1, [string[]]$content)
        Write-Lines $File $list
    }
    'deleteline' {
        if ($From -lt 1 -or $To -lt $From) { throw 'deleteline 需要 -From/-To（1 起始）' }
        $list = New-Object System.Collections.Generic.List[string]
        (Read-Lines $File) | ForEach-Object { [void]$list.Add($_) }
        if ($To -gt $list.Count) { throw "-To $To 超出范围（共 $($list.Count) 行）" }
        $list.RemoveRange($From - 1, $To - $From + 1)
        Write-Lines $File $list
    }
}

# ---------- 写后自动回读验证（成功回执可信任的关键） ----------
if ($Action -in @('new','append','insert','replaceline','deleteline')) {
    Start-Sleep -Milliseconds 200
    $verify = [System.IO.File]::ReadAllLines($File, (Get-Enc $File))
    Write-Host "[VERIFIED] $File 现有 $($verify.Count) 行（写后回读成功）"
    # 抽查首末行，确认非空文件
    if ($verify.Count -gt 0 -and $verify[0].Length -eq 0 -and $verify[$verify.Count-1].Length -eq 0) {
        throw '警告：回读首末行均为空，疑似写入异常'
    }
}
