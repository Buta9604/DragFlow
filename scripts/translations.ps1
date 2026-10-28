# Writes docs\translations\template.txt: every text DragFlow shows, ready to translate.
# Copy it to <language>.txt (for example de.txt), fill in the "tr:" lines, and put it in the "lang" folder next to
# DragFlow.exe or in %APPDATA%\DragFlow\lang. Lines left empty stay English.
$root = Split-Path $PSScriptRoot -Parent
$texts = New-Object System.Collections.Generic.SortedSet[string]
$patterns = @(
    'L\.(?:T|F)\("((?:[^"\\]|\\.)*)"',                    # L.T("...") and L.F("...")
    '(?:Item|Group|Kid)\("[^"]*",\s*"((?:[^"\\]|\\.)*)"',  # dial actions: Item("id", "Label", ...)
    'Group\("((?:[^"\\]|\\.)*)",\s*G',                     # dial groups: Group("Label", glyph ...)
    'new\[\] \{ "[^"]*", "((?:[^"\\]|\\.)*)" \}'           # rule and recipe choices
)
foreach ($f in Get-ChildItem (Join-Path $root 'src') -Filter *.cs) {
    $code = Get-Content $f.FullName -Raw
    foreach ($p in $patterns) { foreach ($m in [regex]::Matches($code, $p)) { [void]$texts.Add($m.Groups[1].Value.Replace('\"', '"')) } }
}
$out = Join-Path $root 'docs\translations'
New-Item -ItemType Directory $out -Force | Out-Null
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# DragFlow translation template. Fill in each "tr:" line; leave it empty to keep the English.')
[void]$sb.AppendLine('# {0}, {1}... are blanks DragFlow fills in (numbers, names); keep them in the translation.')
[void]$sb.AppendLine('')
foreach ($t in $texts) { [void]$sb.AppendLine('en: ' + $t); [void]$sb.AppendLine('tr: '); [void]$sb.AppendLine('') }
Set-Content (Join-Path $out 'template.txt') $sb.ToString() -Encoding UTF8
Write-Host "$($texts.Count) texts written to $out\template.txt"
