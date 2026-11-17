param(
    [string]$TypstExecutable = 'D:\Typst\typst.exe',
    [string]$FontDirectory = 'D:\Temp\ReciteHelperPaper\fonts'
)

$env:TEMP = 'D:\Temp\ReciteHelperPaper'
$env:TMP = $env:TEMP
& $TypstExecutable compile --ignore-system-fonts --font-path $FontDirectory --root $PSScriptRoot (Join-Path $PSScriptRoot 'main.typ') (Join-Path $PSScriptRoot 'cFSRS_Arabidopsis.pdf')
exit $LASTEXITCODE
