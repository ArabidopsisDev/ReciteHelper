$ErrorActionPreference = 'Stop'
$base = 'D:\Projects\OpenRecite\competition\LuxinCup-2026\revision2'
$env:TEMP = Join-Path $base 'work\tmp'
$env:TMP = $env:TEMP
$word = $null
$document = $null
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $word.AutomationSecurity = 3
    $inputPath = Join-Path $base 'output\汇智云学_鲁信杯项目计划书_规范扩展版.docx'
    $document = $word.Documents.Open($inputPath, $false, $false)
    $null = $document.Fields.Update()
    foreach ($toc in $document.TablesOfContents) {
        $toc.Update()
        $toc.Range.Font.NameFarEast = '宋体'
        $toc.Range.Font.Color = 0
    }
    $document.Repaginate()
    foreach ($toc in $document.TablesOfContents) {$toc.UpdatePageNumbers()}
    $document.Repaginate()
    $pageCount = $document.ComputeStatistics(2)
    $map = @()
    for ($pageNumber = 1; $pageNumber -le $pageCount; $pageNumber++) {
        $start = $document.GoTo(1, 1, $pageNumber).Start
        if ($pageNumber -lt $pageCount) {$end = $document.GoTo(1, 1, ($pageNumber + 1)).Start}
        else {$end = $document.Content.End}
        $range = $document.Range($start, $end)
        $map += [PSCustomObject]@{Page = $pageNumber; Text = $range.Text}
    }
    $map | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $base 'qa\word-pages.json') -Encoding utf8
    $document.ExportAsFixedFormat((Join-Path $base 'qa\word-render.pdf'), 17)
    $document.Save()
    Write-Output "Word export complete. Pages: $pageCount. TOC fields: $($document.TablesOfContents.Count)"
} finally {
    if ($null -ne $document) {$document.Close(0);[void][Runtime.InteropServices.Marshal]::ReleaseComObject($document)}
    if ($null -ne $word) {$word.Quit();[void][Runtime.InteropServices.Marshal]::ReleaseComObject($word)}
}
