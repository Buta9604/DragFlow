# Records the demo GIF for the README (docs\images\demo.gif): a real Explorer window, the real mouse, the real dial.
# It drags a WebP photo onto "To JPG", then turns a sideways photo upright with Edit > Right.
# Only the file area of the window is recorded (no address bar or navigation pane, so no personal paths show).
# Needs ffmpeg on the PATH. DragFlow runs with a throwaway profile; the installed copy is paused and started again.
# Run:  powershell -ExecutionPolicy Bypass -File scripts\record-demo.ps1 [-Out docs\images\demo.gif] [-Width 880]
param(
    [string]$Out = (Join-Path $PSScriptRoot '..\docs\images\demo.gif'),
    [string]$Still = (Join-Path $PSScriptRoot '..\docs\images\dial-in-explorer.png'),
    [int]$Width = 880,
    [int]$Above = 0,
    [switch]$Keep,
    [string]$Exe = (Join-Path $PSScriptRoot '..\bin\DragFlow.exe'),
    [string]$Tests = (Join-Path $PSScriptRoot '..\bin\DragFlow.Tests.exe')
)
$ErrorActionPreference = 'Stop'
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) { throw 'ffmpeg is needed to make the GIF.' }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type -ReferencedAssemblies System.Drawing @"
using System; using System.IO; using System.Threading; using System.Drawing; using System.Drawing.Imaging;
using System.Runtime.InteropServices; using System.Collections.Generic; using System.Diagnostics;
public static class Demo {
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT pt; }
  [StructLayout(LayoutKind.Sequential)] struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }
  [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CURSORINFO ci);
  [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr h, out ICONINFO ii);
  [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int w, int h, int step, IntPtr brush, int flags);
  [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

  // The dial is the only square window DragFlow shows.
  public static List<int[]> Dials(uint pid) {
    var l = new List<int[]>();
    EnumWindows(delegate(IntPtr h, IntPtr x) {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) { RECT r; GetWindowRect(h, out r); int w = r.R - r.L, hh = r.B - r.T;
        if (w > 100 && Math.Abs(w - hh) < 4) l.Add(new int[] { r.L, r.T, r.R, r.B }); }
      return true; }, IntPtr.Zero);
    return l;
  }

  // Screen capture of one rectangle, with the mouse pointer drawn in, on its own thread.
  static Thread thread; static volatile bool stop;
  public static List<long> Times = new List<long>();
  public static void Start(int x, int y, int w, int h, string dir, int everyMs) {
    stop = false; Times.Clear();
    thread = new Thread(delegate() {
      var sw = Stopwatch.StartNew(); int n = 0;
      while (!stop) {
        long t0 = sw.ElapsedMilliseconds;
        using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb)) {
          using (var g = Graphics.FromImage(bmp)) {
            g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
            var ci = new CURSORINFO(); ci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
            if (GetCursorInfo(ref ci) && ci.flags == 1) {
              ICONINFO ii; int hx = 0, hy = 0;
              if (GetIconInfo(ci.hCursor, out ii)) { hx = ii.xHotspot; hy = ii.yHotspot; if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask); if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor); }
              IntPtr hdc = g.GetHdc();
              DrawIconEx(hdc, ci.pt.X - x - hx, ci.pt.Y - y - hy, ci.hCursor, 0, 0, 0, IntPtr.Zero, 3);
              g.ReleaseHdc(hdc);
            }
          }
          bmp.Save(Path.Combine(dir, "f" + n.ToString("00000") + ".png"), ImageFormat.Png);
        }
        lock (Times) Times.Add(t0);
        n++;
        long left = everyMs - (sw.ElapsedMilliseconds - t0);
        if (left > 0) Thread.Sleep((int)left);
      }
    });
    thread.IsBackground = true; thread.Start();
  }
  public static void Stop() { stop = true; if (thread != null) thread.Join(); }
}
"@
[void][Demo]::SetProcessDPIAware()
$vs = [System.Windows.Forms.SystemInformation]::VirtualScreen

function MouseTo($x, $y) {
    $nx = [int](($x - $vs.Left) * 65535 / ($vs.Width - 1)); $ny = [int](($y - $vs.Top) * 65535 / ($vs.Height - 1))
    [Demo]::mouse_event(0xC001, $nx, $ny, 0, [UIntPtr]::Zero)
}
function MouseDown { [Demo]::mouse_event(0x2, 0, 0, 0, [UIntPtr]::Zero) }
function MouseUp { [Demo]::mouse_event(0x4, 0, 0, 0, [UIntPtr]::Zero) }
# A human-looking move: slow at both ends.
function Glide($x0, $y0, $x1, $y1, $ms) {
    $steps = [math]::Max(6, [int]($ms / 14))
    for ($i = 1; $i -le $steps; $i++) {
        $t = $i / $steps; $e = $t * $t * (3 - 2 * $t)
        MouseTo ($x0 + ($x1 - $x0) * $e) ($y0 + ($y1 - $y0) * $e)
        Start-Sleep -Milliseconds 14
    }
}
function Layout($kind, $slice, $chip) {
    $h = @{}
    foreach ($line in (& $Tests --layout $kind $slice $chip)) {
        foreach ($kv in ($line -split ' ')) { $p = $kv -split '='; if ($p.Count -eq 2) { $h["$($line.Substring(0,4))$($p[0])"] = [double]$p[1] } }
    }
    return $h
}

# --- the scene: a few files in a fresh folder ---------------------------------------------------------------
$work = Join-Path $env:TEMP 'DragFlow demo'
$profileDir = Join-Path $env:TEMP 'dragflow-demo-profile'
$frames = Join-Path $env:TEMP 'dragflow-demo-frames'
foreach ($d in $work, $profileDir, $frames) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory $work, $frames, (Join-Path $profileDir 'roaming') -Force | Out-Null
New-Item -ItemType Directory (Join-Path $work 'Archive') | Out-Null
Set-Content (Join-Path $profileDir 'roaming\settings.ini') "FirstRunDone=1`r`nShelfEnabled=0`r`nLearning=0"
Set-Content (Join-Path $work 'notes.txt') "Trip notes`r`n- book the ferry`r`n- send photos to Sam"

function Scene($file, $w, $h, $sky1, $sky2, $sun, $hill1, $hill2, $sideways) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $sky = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $sky1, $sky2, 90
    $g.FillRectangle($sky, $rect)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $sun), [int]($w * 0.62), [int]($h * 0.22), [int]($h * 0.26), [int]($h * 0.26))
    $far = [System.Drawing.Point[]]@((New-Object System.Drawing.Point 0, ([int]($h * 0.62))), (New-Object System.Drawing.Point ([int]($w * 0.3)), ([int]($h * 0.42))),
        (New-Object System.Drawing.Point ([int]($w * 0.55)), ([int]($h * 0.58))), (New-Object System.Drawing.Point ([int]($w * 0.8)), ([int]($h * 0.40))),
        (New-Object System.Drawing.Point $w, ([int]($h * 0.55))), (New-Object System.Drawing.Point $w, $h), (New-Object System.Drawing.Point 0, $h))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush $hill1), $far)
    $near = [System.Drawing.Point[]]@((New-Object System.Drawing.Point 0, ([int]($h * 0.80))), (New-Object System.Drawing.Point ([int]($w * 0.4)), ([int]($h * 0.66))),
        (New-Object System.Drawing.Point $w, ([int]($h * 0.78))), (New-Object System.Drawing.Point $w, $h), (New-Object System.Drawing.Point 0, $h))
    $g.FillPolygon((New-Object System.Drawing.SolidBrush $hill2), $near)
    $g.Dispose()
    if ($sideways) { $bmp.RotateFlip([System.Drawing.RotateFlipType]::Rotate270FlipNone) }   # taken sideways: Edit > Right fixes it
    $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
$C = [System.Drawing.Color]
Scene (Join-Path $work 'beach.png') 1200 800 $C::FromArgb(255, 120, 190, 235) $C::FromArgb(255, 235, 245, 250) $C::FromArgb(255, 255, 236, 150) `
    $C::FromArgb(255, 70, 150, 120) $C::FromArgb(255, 230, 205, 150) $true
$tmpPng = Join-Path $frames 'holiday.png'
Scene $tmpPng 1200 800 $C::FromArgb(255, 60, 40, 110) $C::FromArgb(255, 250, 140, 90) $C::FromArgb(255, 255, 210, 120) `
    $C::FromArgb(255, 90, 50, 100) $C::FromArgb(255, 40, 25, 60) $false
& ffmpeg -hide_banner -loglevel error -y -i $tmpPng -quality 85 (Join-Path $work 'holiday.webp')
Remove-Item $tmpPng

$jpg = Layout 'webp' 'To JPG' $null
$rot = Layout 'image' 'Edit' 'Right'

# --- pause the installed DragFlow (its guard would start it again) and start the demo copy ---------------------
$realLocal = Join-Path $env:LOCALAPPDATA 'DragFlow'
$realExe = Join-Path $realLocal 'DragFlow.exe'
$realWasRunning = @(Get-Process DragFlow -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $realExe }).Count -gt 0
if (Test-Path $realLocal) { Set-Content (Join-Path $realLocal 'guard-stop.txt') 'session' }
$explorerHwnd = $null
try {
    Get-Process DragFlow -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    $app = Start-Process $Exe -ArgumentList @('--profile', "`"$profileDir`"") -PassThru
    Start-Sleep -Seconds 2

    Start-Process explorer.exe "`"$work`""
    $shell = New-Object -ComObject Shell.Application
    $sw = $null
    for ($i = 0; $i -lt 40 -and -not $sw; $i++) {
        Start-Sleep -Milliseconds 250
        foreach ($w in $shell.Windows()) { try { if ($w.Document.Folder.Self.Path -eq $work) { $sw = $w } } catch { } }
    }
    if (-not $sw) { throw 'Explorer window not found' }
    $explorerHwnd = [IntPtr]$sw.HWND
    $sw.Document.CurrentViewMode = 1      # icons
    $sw.Document.IconSize = 192           # big thumbnails, so the photos read in a small GIF
    $area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $ww = [int]($area.Width * 0.72); $wh = [int]($area.Height * 0.78)
    [void][Demo]::SetWindowPos($explorerHwnd, [IntPtr]::Zero, $area.Left + [int](($area.Width - $ww) / 2), $area.Top + [int](($area.Height - $wh) / 2), $ww, $wh, 0x40)
    [void][Demo]::SetForegroundWindow($explorerHwnd)
    Start-Sleep -Seconds 2

    $win = [System.Windows.Automation.AutomationElement]::FromHandle($explorerHwnd)
    $cond = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ClassNameProperty), 'UIItemsView'
    $view = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if (-not $view) { throw 'The file area of the Explorer window was not found' }
    $vr = $view.Current.BoundingRectangle
    $wr = $win.Current.BoundingRectangle
    $rx = [int]$vr.X; $ry = [int]$vr.Y - $Above
    $rw = [int]([math]::Min($vr.Right, $wr.Right - 4) - $rx); $rh = [int]([math]::Min($vr.Bottom, $wr.Bottom - 4) - $ry)
    $rh = [math]::Min($rh, [int]($rw * 0.42))   # the files and the dial; not the empty space under them
    $rw -= $rw % 2; $rh -= $rh % 2

    function ItemCenter($name, $down = 0.4) {
        $c = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::NameProperty), $name
        $el = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
        if (-not $el) { throw "Item $name not found" }
        $r = $el.Current.BoundingRectangle
        return @([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height * $down))
    }
    function DragAndPause($name) {
        # grabbed low, so the whole dial fits under the top of the recording
        $p = ItemCenter $name 0.8
        $cur = [System.Windows.Forms.Cursor]::Position
        Glide $cur.X $cur.Y $p[0] $p[1] 600; Start-Sleep -Milliseconds 350
        MouseDown; Start-Sleep -Milliseconds 120
        Glide $p[0] $p[1] ($p[0] + 24) ($p[1] + 18) 110
        $d = $null
        for ($i = 0; $i -lt 30 -and -not $d; $i++) { Start-Sleep -Milliseconds 50; $l = [Demo]::Dials($app.Id); if ($l.Count -eq 1) { $d = $l[0] } }
        if (-not $d) { MouseUp; throw "The dial did not open for $name" }
        Start-Sleep -Milliseconds 650   # let the viewer see the dial
        return $d
    }
    function At($d, $layout, $key) {
        $cx = ($d[0] + $d[2]) / 2; $cy = ($d[1] + $d[3]) / 2; $s = ($d[2] - $d[0]) / (2 * $layout['slichalf'])
        return @(($cx + $layout["$($key)x"] * $s), ($cy + $layout["$($key)y"] * $s))
    }

    $cur = [System.Windows.Forms.Cursor]::Position
    $start = ItemCenter 'notes.txt'
    MouseTo ($rx + [int]($rw * 0.55)) ($ry + [int]($rh * 0.85)); Start-Sleep -Milliseconds 400
    [Demo]::Start($rx, $ry, $rw, $rh, $frames, 70)
    Start-Sleep -Milliseconds 900

    # 1. a WebP photo -> To JPG
    $d = DragAndPause 'holiday.webp'
    $t = At $d $jpg 'slic'
    $cur = [System.Windows.Forms.Cursor]::Position
    Glide $cur.X $cur.Y $t[0] $t[1] 500; Start-Sleep -Milliseconds 700
    MouseUp
    Start-Sleep -Milliseconds 2200

    # 2. a sideways photo -> Edit > Right (rest on Edit, its options fan out, pick Right)
    $d = DragAndPause 'beach.png'
    $t = At $d $rot 'slic'
    $cur = [System.Windows.Forms.Cursor]::Position
    Glide $cur.X $cur.Y $t[0] $t[1] 450; Start-Sleep -Milliseconds 700
    $c = At $d $rot 'chip'
    Glide $t[0] $t[1] $c[0] $c[1] 450; Start-Sleep -Milliseconds 500
    $shot = New-Object System.Drawing.Bitmap $rw, $rh
    $gs = [System.Drawing.Graphics]::FromImage($shot); $gs.CopyFromScreen($rx, $ry, 0, 0, $shot.Size); $gs.Dispose()
    $stillTmp = Join-Path $env:TEMP 'dragflow-demo-still.png'
    $shot.Save($stillTmp); $shot.Dispose()
    Start-Sleep -Milliseconds 300
    MouseUp
    Start-Sleep -Milliseconds 2600
    $end = [System.Windows.Forms.Cursor]::Position
    Glide $end.X $end.Y ($rx + [int]($rw * 0.55)) ($ry + [int]($rh * 0.85)) 600   # rest on empty space: no tooltip
    Start-Sleep -Milliseconds 1200
    [Demo]::Stop()
}
finally {
    [Demo]::Stop()
    MouseUp
    if ($explorerHwnd) { try { $sw.Quit() } catch { } }
    Get-Process DragFlow -ErrorAction SilentlyContinue | Where-Object { $_.Path -ne $realExe } | Stop-Process -Force
    if ($realWasRunning -and (Test-Path $realExe)) { Start-Process $realExe }
    elseif (Test-Path (Join-Path $realLocal 'guard-stop.txt')) { Remove-Item (Join-Path $realLocal 'guard-stop.txt') }
}

$ok = (Test-Path (Join-Path $work 'holiday.jpg'))
$img = [System.Drawing.Image]::FromFile((Join-Path $work 'beach.png')); $upright = $img.Width -gt $img.Height; $img.Dispose()
"holiday.jpg made: $ok   beach.png upright: $upright   frames: $([Demo]::Times.Count)"
if (-not ($ok -and $upright)) { throw 'The demo did not do what it shows; no GIF written.' }

# --- frames -> GIF (frame times as captured, one shared palette tuned for the changing parts) -----------------
$list = Join-Path $frames 'list.txt'
$lines = New-Object System.Collections.Generic.List[string]
$times = [Demo]::Times
for ($i = 0; $i -lt $times.Count; $i++) {
    $next = if ($i + 1 -lt $times.Count) { $times[$i + 1] } else { $times[$i] + 1500 }
    $lines.Add("file 'f$($i.ToString('00000')).png'"); $lines.Add('duration ' + (($next - $times[$i]) / 1000.0).ToString([Globalization.CultureInfo]::InvariantCulture))
}
$lines.Add("file 'f$(($times.Count - 1).ToString('00000')).png'")
[IO.File]::WriteAllLines($list, $lines)
$filter = "fps=12,scale=$($Width):-2:flags=lanczos,split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle"
& ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i $list -vf $filter -loop 0 $Out
if ($LASTEXITCODE -ne 0) { throw 'ffmpeg could not make the GIF.' }
& ffmpeg -hide_banner -loglevel error -y -i $stillTmp -vf "scale=$($Width):-2:flags=lanczos" $Still
"GIF: $Out ($([math]::Round((Get-Item $Out).Length / 1MB, 2)) MB)"
"Still: $Still"
if (-not $Keep) { Remove-Item $frames, $work, $profileDir -Recurse -Force -ErrorAction SilentlyContinue }
Remove-Item $stillTmp -ErrorAction SilentlyContinue
