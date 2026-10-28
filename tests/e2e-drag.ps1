# End-to-end check against a real Explorer window. It drives the real mouse for about half a minute:
# a slow drag-and-pause must show the dial, a drop on To PDF must create a PDF, Rotate > Right must rotate,
# a fast flick and a rubber-band selection must show nothing, Esc must close the dial, a missed button-up must not
# leave the dial stuck, and dropping on the shelf must park the file.
# DragFlow runs with a throwaway profile, so your own settings, learning and undo history are untouched.
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\bin\DragFlow.exe'),
    [string]$Tests = (Join-Path $PSScriptRoot '..\bin\DragFlow.Tests.exe'),
    [string]$Shot = (Join-Path $PSScriptRoot '..\bin\e2e-ring.png')
)
$ErrorActionPreference = 'Stop'
trap { [W]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero); Get-Process DragFlow -ErrorAction SilentlyContinue | Stop-Process -Force; throw }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  // The dial is the only square window DragFlow shows (notification cards and the shelf are not).
  public static List<int[]> Dials(uint pid) {
    var l = new List<int[]>();
    EnumWindows(delegate(IntPtr h, IntPtr x) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) { RECT r; GetWindowRect(h, out r); int w = r.R - r.L, hh = r.B - r.T;
        if (w > 100 && Math.Abs(w - hh) < 4) l.Add(new int[] { r.L, r.T, r.R, r.B }); }
      return true; }, IntPtr.Zero);
    return l;
  }
  // A shape's offer: a small, wide pill (much lower than the dial or a notification card).
  public static List<int[]> Bubbles(uint pid) {
    var l = new List<int[]>();
    EnumWindows(delegate(IntPtr h, IntPtr x) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) { RECT r; GetWindowRect(h, out r); int w = r.R - r.L, hh = r.B - r.T;
        if (hh >= 20 && hh <= 70 && w >= 60 && w <= 400) l.Add(new int[] { r.L, r.T, r.R, r.B }); }
      return true; }, IntPtr.Zero);
    return l;
  }
}
"@
[void][W]::SetProcessDPIAware()

$vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
# Real input events (SetCursorPos alone does not reach mouse hooks or start an Explorer drag).
function MouseTo($x, $y) {
    $nx = [int](($x - $vs.Left) * 65535 / ($vs.Width - 1)); $ny = [int](($y - $vs.Top) * 65535 / ($vs.Height - 1))
    [W]::mouse_event(0xC001, $nx, $ny, 0, [UIntPtr]::Zero)   # MOVE | ABSOLUTE | VIRTUALDESK
}
function MouseDown { [W]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero) }
function MouseUp { [W]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero) }
function Glide($x0, $y0, $x1, $y1, $steps, $delayMs) {
    for ($i = 1; $i -le $steps; $i++) {
        MouseTo ($x0 + ($x1 - $x0) * $i / $steps) ($y0 + ($y1 - $y0) * $i / $steps)
        Start-Sleep -Milliseconds $delayMs
    }
}
# Where a slice (and option) sits, in 96-dpi pixels from the dial centre, straight from the app's own layout code.
function Layout($slice, $chip) {
    $out = & $Tests --layout image $slice $chip
    $h = @{}
    foreach ($line in $out) { foreach ($kv in ($line -split ' ')) { $p = $kv -split '='; if ($p.Count -eq 2) { $h["$($line.Substring(0,4))$($p[0])"] = [double]$p[1] } } }
    return $h
}

$work = Join-Path $env:TEMP 'dragflow-e2e'
$profileDir = Join-Path $env:TEMP 'dragflow-e2e-profile'
foreach ($d in $work, $profileDir) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory $work | Out-Null
New-Item -ItemType Directory (Join-Path $work "out") | Out-Null
New-Item -ItemType Directory (Join-Path $profileDir 'roaming') -Force | Out-Null
Set-Content (Join-Path $profileDir 'roaming\settings.ini') "FirstRunDone=1`r`nShelfEnabled=0`r`nLearning=0"
foreach ($n in 'a', 'b', 'c') {
    $bmp = New-Object System.Drawing.Bitmap 120, 80
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::SteelBlue); $g.Dispose()
    $bmp.Save((Join-Path $work "$n.png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}

$pdfSlice = Layout 'To PDF' $null
$rot = Layout 'Edit' 'Right'

# The installed DragFlow (and its guard, which would bring it back) must not run during the test.
$realLocal = Join-Path $env:LOCALAPPDATA 'DragFlow'
$realExe = Join-Path $realLocal 'DragFlow.exe'
$realWasRunning = @(Get-Process DragFlow -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $realExe }).Count -gt 0
if (Test-Path $realLocal) { Set-Content (Join-Path $realLocal 'guard-stop.txt') 'session' }
function StartApp {
    Get-Process DragFlow -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
    $p = Start-Process $Exe -ArgumentList @('--profile', "`"$profileDir`"") -PassThru
    Start-Sleep -Seconds 2
    return $p
}
$app = StartApp
Start-Process explorer.exe $work
Start-Sleep -Seconds 3

$root = [System.Windows.Automation.AutomationElement]::RootElement
$win = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition) |
    Where-Object { $_.Current.ClassName -eq 'CabinetWClass' -and $_.Current.Name -like 'dragflow-e2e*' } | Select-Object -First 1
if (-not $win) { throw 'Explorer window not found' }
function ItemCenter($name) {
    $c = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $name
    $el = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
    if (-not $el) { throw "Item $name not found" }
    $r = $el.Current.BoundingRectangle
    return @([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
}
function StartDrag($name) {
    $p = ItemCenter $name
    MouseTo $p[0] $p[1]; Start-Sleep -Milliseconds 400
    MouseDown; Start-Sleep -Milliseconds 100
    Glide $p[0] $p[1] ($p[0] + 30) ($p[1] + 20) 6 10   # an ordinary, fairly quick drag start
    Start-Sleep -Milliseconds 700
    return $p
}
$results = [ordered]@{}

# 1. ordinary drag start -> dial appears over the item; drop on To PDF
StartDrag 'a.png' | Out-Null
$dial = [W]::Dials($app.Id)
$results['dial shown after an ordinary drag start'] = ($dial.Count -eq 1)
$img = New-Object System.Drawing.Bitmap $vs.Width, $vs.Height
$gg = [System.Drawing.Graphics]::FromImage($img); $gg.CopyFromScreen($vs.Left, $vs.Top, 0, 0, $img.Size); $gg.Dispose()
$img.Save($Shot); $img.Dispose()
if ($dial.Count -eq 1) {
    $d = $dial[0]
    $cx = ($d[0] + $d[2]) / 2; $cy = ($d[1] + $d[3]) / 2
    $s = ($d[2] - $d[0]) / (2 * $pdfSlice['slichalf'])
    $zx = $cx + $pdfSlice['slicx'] * $s; $zy = $cy + $pdfSlice['slicy'] * $s
    $cur = [System.Windows.Forms.Cursor]::Position
    Glide $cur.X $cur.Y $zx $zy 10 16
    Start-Sleep -Milliseconds 300
    $hov = New-Object System.Drawing.Bitmap ($d[2] - $d[0]), ($d[3] - $d[1])
    $gh = [System.Drawing.Graphics]::FromImage($hov); $gh.CopyFromScreen($d[0], $d[1], 0, 0, $hov.Size); $gh.Dispose()
    $hov.Save(($Shot -replace '\.png$', '-hover.png')); $hov.Dispose()
}
MouseUp
Start-Sleep -Seconds 2
$results['a.pdf created by dropping on To PDF'] = (Test-Path (Join-Path $work 'a.pdf'))
$results['original a.png still there'] = (Test-Path (Join-Path $work 'a.png'))
$results['dial gone after the drop'] = ([W]::Dials($app.Id).Count -eq 0)
$undo = Get-ChildItem (Join-Path $profileDir 'local\undo') -Filter *.undo -ErrorAction SilentlyContinue
$results['the PDF is in the undo journal'] = ($undo.Count -ge 1)

# 2. sub-option: hover the Edit slice, wait for its options, drop on "Right"
StartDrag 'b.png' | Out-Null
$dial = [W]::Dials($app.Id)
if ($dial.Count -eq 1) {
    $d = $dial[0]
    $cx = ($d[0] + $d[2]) / 2; $cy = ($d[1] + $d[3]) / 2; $s = ($d[2] - $d[0]) / (2 * $rot['slichalf'])
    $ix = $cx + $rot['slicx'] * $s; $iy = $cy + $rot['slicy'] * $s
    $cur = [System.Windows.Forms.Cursor]::Position
    Glide $cur.X $cur.Y $ix $iy 8 16
    Start-Sleep -Milliseconds 500
    $chipX = $cx + $rot['chipx'] * $s; $chipY = $cy + $rot['chipy'] * $s
    Glide $ix $iy $chipX $chipY 8 16
    Start-Sleep -Milliseconds 300
    $hov = New-Object System.Drawing.Bitmap ($d[2] - $d[0]), ($d[3] - $d[1])
    $gh = [System.Drawing.Graphics]::FromImage($hov); $gh.CopyFromScreen($d[0], $d[1], 0, 0, $hov.Size); $gh.Dispose()
    $hov.Save(($Shot -replace '\.png$', '-chips.png')); $hov.Dispose()
}
MouseUp
Start-Sleep -Seconds 2
$bytes = [System.IO.File]::ReadAllBytes((Join-Path $work 'b.png'))
$ms = New-Object System.IO.MemoryStream (, $bytes)
$im = [System.Drawing.Image]::FromStream($ms)
$results['b.png rotated via Edit > Right'] = ($im.Width -eq 80 -and $im.Height -eq 120)
$im.Dispose(); $ms.Dispose()
Start-Sleep -Milliseconds 400

# 3. fast straight flick -> no dial
$p = ItemCenter 'c.png'
MouseTo $p[0] $p[1]; Start-Sleep -Milliseconds 400
MouseDown; Start-Sleep -Milliseconds 60
$max = 0
for ($i = 1; $i -le 20; $i++) {
    MouseTo ($p[0] + $i * 45) ($p[1] + $i * 8); Start-Sleep -Milliseconds 8
    $max = [math]::Max($max, [W]::Dials($app.Id).Count)
}
MouseUp; Start-Sleep -Milliseconds 400
$results['no dial during a fast flick'] = ($max -eq 0)

# 4. Esc closes the dial mid-drag
StartDrag 'c.png' | Out-Null
$before = [W]::Dials($app.Id).Count
[W]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [W]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 500
$results['Esc closes the dial'] = ($before -eq 1 -and [W]::Dials($app.Id).Count -eq 0)
MouseUp; Start-Sleep -Milliseconds 600

# 5. rubber-band selection on empty space -> no dial
$r = $win.Current.BoundingRectangle
$ex = [int]($r.X + $r.Width - 120); $ey = [int]($r.Y + $r.Height - 120)
MouseTo $ex $ey; Start-Sleep -Milliseconds 300
MouseDown; Start-Sleep -Milliseconds 80
Glide $ex $ey ($ex - 40) ($ey - 30) 10 25
Start-Sleep -Milliseconds 700
$results['no dial during a rubber-band selection'] = ([W]::Dials($app.Id).Count -eq 0)
MouseUp
Start-Sleep -Milliseconds 400

# 6. the shelf: with it switched on, a drop at the screen edge parks the file
Set-Content (Join-Path $profileDir 'roaming\settings.ini') "FirstRunDone=1`r`nShelfEnabled=1`r`nLearning=0"
$app = StartApp
$p = ItemCenter 'c.png'
$screen = [System.Windows.Forms.Screen]::FromPoint((New-Object System.Drawing.Point $p[0], $p[1])).WorkingArea
MouseTo $p[0] $p[1]; Start-Sleep -Milliseconds 400
MouseDown; Start-Sleep -Milliseconds 100
Glide $p[0] $p[1] ($p[0] + 400) ($p[1] + 10) 12 12      # a decisive drag: the dial stays out of the way
Start-Sleep -Milliseconds 700
$zoneX = $screen.Right - 125; $zoneY = $screen.Top + $screen.Height / 2 - 40
$cur = [System.Windows.Forms.Cursor]::Position
Glide $cur.X $cur.Y $zoneX $zoneY 14 20
Start-Sleep -Milliseconds 600
MouseUp
Start-Sleep -Seconds 1
$shelfFile = Join-Path $profileDir 'roaming\shelf.txt'
$results['dropping on the shelf parks the file'] = ((Test-Path $shelfFile) -and ((Get-Content $shelfFile -Raw) -like '*c.png*'))
$results['parking leaves the file where it was'] = (Test-Path (Join-Path $work 'c.png'))

# 6b. drag the parked file back out of the shelf onto the "out" folder
$cy = $screen.Top + $screen.Height / 2
MouseTo ($screen.Right - 6) $cy; Start-Sleep -Milliseconds 600       # hover the folded tab: it opens
$rowX = $screen.Right - 125; $rowY = $cy + 12                          # first parked item
MouseTo $rowX $rowY; Start-Sleep -Milliseconds 300
$o = ItemCenter 'out'
MouseDown; Start-Sleep -Milliseconds 100
Glide $rowX $rowY $o[0] $o[1] 20 20
Start-Sleep -Milliseconds 600
MouseUp
Start-Sleep -Seconds 1
$results['dragging it off the shelf drops it into a folder'] = (Test-Path (Join-Path $work 'out\c.png'))
$results['the shelf forgets it once dragged out'] = -not ((Get-Content $shelfFile -Raw) -like '*c.png*')

# 6c. the dial without dragging: select a file, press Ctrl+Alt+D, then Esc
$p = ItemCenter 'a.png'
MouseTo $p[0] $p[1]; Start-Sleep -Milliseconds 300
MouseDown; Start-Sleep -Milliseconds 60; MouseUp; Start-Sleep -Milliseconds 500
[W]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero); [W]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
[W]::keybd_event(0x44, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50
[W]::keybd_event(0x44, 0, 2, [UIntPtr]::Zero); [W]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero); [W]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero)
$seen = 0
for ($i = 0; $i -lt 30 -and $seen -eq 0; $i++) { Start-Sleep -Milliseconds 100; $seen = [W]::Dials($app.Id).Count }
$results['Ctrl+Alt+D opens the dial for the selection'] = ($seen -eq 1)
if ($seen -eq 1) {
    $d = [W]::Dials($app.Id)[0]
    $shot2 = New-Object System.Drawing.Bitmap ($d[2] - $d[0]), ($d[3] - $d[1])
    $gk = [System.Drawing.Graphics]::FromImage($shot2); $gk.CopyFromScreen($d[0], $d[1], 0, 0, $shot2.Size); $gk.Dispose()
    $shot2.Save(($Shot -replace '\.png$', '-hotkey.png')); $shot2.Dispose()
}
[W]::keybd_event(0x1B, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50; [W]::keybd_event(0x1B, 0, 2, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 600
$results['Esc closes the keyboard dial'] = ([W]::Dials($app.Id).Count -eq 0)

# 6d. a shape: after the dial has gone, draw a Z while dragging; Zip appears beside the pointer; drop on it
$p = ItemCenter 'a.png'
MouseTo $p[0] $p[1]; Start-Sleep -Milliseconds 400
MouseDown; Start-Sleep -Milliseconds 100
Glide $p[0] $p[1] ($p[0] + 380) ($p[1] + 10) 12 12      # decisive: the dial stays away
Start-Sleep -Milliseconds 200
$x0 = $p[0] + 380; $y0 = $p[1] + 10
Glide $x0 $y0 ($x0 + 170) $y0 14 10
Glide ($x0 + 170) $y0 ($x0 + 20) ($y0 + 140) 16 10
Glide ($x0 + 20) ($y0 + 140) ($x0 + 190) ($y0 + 140) 14 10
Start-Sleep -Milliseconds 300
$b = [W]::Bubbles($app.Id)
$results['a Z drawn while dragging offers Zip'] = ($b.Count -eq 1)
if ($b.Count -eq 1) {
    $cur = [System.Windows.Forms.Cursor]::Position
    Glide $cur.X $cur.Y (($b[0][0] + $b[0][2]) / 2) (($b[0][1] + $b[0][3]) / 2) 10 16
    Start-Sleep -Milliseconds 300
}
MouseUp
Start-Sleep -Seconds 2
$results['dropping on it zips the file'] = (Test-Path (Join-Path $work 'a.zip'))

# 7. the log shows the hook was installed and nothing crashed
$log = Join-Path $profileDir 'local\logs\dragflow.log'
$text = if (Test-Path $log) { Get-Content $log -Raw } else { '' }
$results['log written, no errors'] = ($text -like '*hook installed*' -and $text -notlike '*ERROR*')

$win.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
Stop-Process $app -Force
# put the installed DragFlow back the way it was (it clears the guard's stop note when it starts)
if ($realWasRunning -and (Test-Path $realExe)) { Start-Process $realExe }
elseif (Test-Path (Join-Path $realLocal 'guard-stop.txt')) { Remove-Item (Join-Path $realLocal 'guard-stop.txt') }
$results.GetEnumerator() | ForEach-Object { '{0,-48} {1}' -f $_.Key, $_.Value }
