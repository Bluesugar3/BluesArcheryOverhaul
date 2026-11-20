# Play-test helper for QuickshotRing 0.15.0. Every button writes the [TestBuild] section of the SERVER's QuickshotRing.cfg in
# one write; the running server re-reads that file within 2 seconds.
#   A test button  -> Buy = <orbs>, BuyRequest = <new number>, Orbs = "", Without = "": the server REALLY sells and buys
#                     archery orbs so [TestBuild] Player owns exactly those (saved; points, level and experience unchanged).
#   Spawn buttons  -> Spawn = "<Kind> x3", SpawnRequest = <new number>: creatures in a line ahead of the player, frozen after 2 s.
#   Remove         -> ClearRequest = <new number>: removes every creature the tool spawned.
#   Clear          -> Orbs = "", Without = "": stops lending orbs; buys nothing.
# The player must be online and fully loaded at the press (else the server refuses and drops it). A number the server started
# with is never acted on. A player name with a double quote, a backslash or a line break is refused (nothing is written).
#   TestBuildMenu.ps1                                -> the window
#   TestBuildMenu.ps1 -Preset Clear [-Cfg <file>]    -> apply one button without a window (also used to check the script)
#   H4 (range review C-1, minor 10): -Preset Reset and -Preset Everyone:<buy preset> change saved progress of other players and write
#   nothing without -Confirm (the window asks Yes/No instead). -SnapshotFile <name> picks the file -Preset Restore puts back.
param(
    [string]$Preset,
    [string]$Player,
    [string]$Cfg = 'C:\Users\dmand\OneDrive\Desktop\A Township Tale(server\UserData\QuickshotRing.cfg',
    [switch]$Confirm,
    [string]$SnapshotFile = ''
)

# key, button text, what it writes: Buy (orb list) | Spawn (kind xN) | Remove | Clear
$Presets = [ordered]@{
    'AutoDock'  = @('Auto Dock: Quick 1-3 + Auto Dock',            'Buy',    'Quick1;Quick2;Quick3;AutoDock')
    'Ricochet'  = @('Ricochet: Volley 1-3 + Burst + Ricochet',     'Buy',    'Volley;FullVolley;Burst;Ricochet')
    'Pierce'    = @('Piercing line: Quick 1-3 + Quickdraw 4',      'Buy',    'Quick1;Quick2;Quick3;Quickdraw4')
    'Charge3'   = @('Charge Shot 3: Charge 1-3',                   'Buy',    'Charge1;Charge2;Charge3')
    'Shatter'   = @('Shatter: Charge 1-3 + Shatter',               'Buy',    'Charge1;Charge2;Charge3;Shatter')
    'HolyArrow' = @('Holy Arrow: Charge 1-3 + Holy Arrow',         'Buy',    'Charge1;Charge2;Charge3;HolyArrow')
    'Homing'    = @('Homing: Volley 1-3 + Burst + Seeking Volley', 'Buy',    'Volley;FullVolley;Burst;SeekingVolley')
    'HomingRic' = @('Homing + Ricochet',                           'Buy',    'Volley;FullVolley;Burst;SeekingVolley;Ricochet')
    # fixF (final review M-9): off-path builds that fit the 8-slot cap (all professions), Quick main (4 orbs) in both
    'OffShatter'  = @('Off-path Shatter: Quick 1-4 + Charge 1-3 + Shatter',  'Buy', 'Quick1;Quick2;Quick3;Quickdraw4;Charge1;Charge2;Charge3;Shatter')
    'OffRicochet' = @('Off-path Ricochet: Quick 1-4 + Volley 1-3 + Ricochet', 'Buy', 'Quick1;Quick2;Quick3;Quickdraw4;Volley;FullVolley;Burst;Ricochet')
    'SellAll'   = @('Sell all archery orbs',                       'Buy',    '')
    'Turabadas' = @('Spawn 3 Turabadas',                           'Spawn',  'Turabada x3')
    'Turabada4' = @('Spawn 4 Turabadas (side by side)',            'Spawn',  'Turabada x4')
    # H3: the same spawn, never frozen (they walk, run and attack): homing following a moving creature
    'Moving'    = @('Spawn 3 moving Turabadas',                    'Spawn',  'Turabada x3 moving')
    'Goteras'   = @('Spawn 3 Birch Goteras',                       'Spawn',  'GoteraBirch x3')
    'Remove'    = @('Remove test enemies',                         'Remove', '')
    'Clear'     = @('Clear: stop lending orbs (buys nothing)',     'Clear',  '')
}

function Get-Section([string]$text) {
    # the header on its own line: other settings' descriptions mention "[TestBuild]" too
    $head = [regex]::Match($text, '(?m)^\[TestBuild\]\r?$')
    if (-not $head.Success) { throw "no [TestBuild] section in $Cfg. Start the server once on this version (0.15.0) first." }
    $start = $head.Index
    $next = $text.IndexOf("`n[", $start + 1, [StringComparison]::Ordinal)
    if ($next -lt 0) { $next = $text.Length } else { $next = $next + 1 }
    return @($start, $next)
}

function Get-KeyRegex([string]$key) {
    # key = "..." on its own line; group 1 = up to the value, group 2 = the line's own CR (kept as it was)
    return [regex]('(?m)^(' + [regex]::Escape($key) + ' = )".*?"(\r?)$')
}

function Apply-Preset([string]$name, [string]$player) {
    if (-not $Presets.Contains($name)) { throw "unknown preset '$name'; known: $($Presets.Keys -join ', ')" }
    $kind = $Presets[$name][1]
    $value = $Presets[$name][2]
    $stamp = [DateTime]::UtcNow.Ticks.ToString()
    $set = [ordered]@{}
    if ($kind -eq 'Buy')    { $set['Orbs'] = ''; $set['Without'] = ''; $set['Buy'] = $value; $set['BuyRequest'] = $stamp }
    if ($kind -eq 'Spawn')  { $set['Spawn'] = $value; $set['SpawnRequest'] = $stamp }
    if ($kind -eq 'Remove') { $set['ClearRequest'] = $stamp }
    if ($kind -eq 'Clear')  { $set['Orbs'] = ''; $set['Without'] = '' }
    if ($player -and $player.Trim()) {
        $p = $player.Trim()
        # fix batch D (fixB-review M-2): a double quote, a backslash, a line break or any other control character would break
        # the settings file (the next server start would put every setting back to its default); refused, nothing is written
        if ($p -match '["\\\p{Cc}]') { throw "the player name must not contain a double quote, a backslash or a line break: nothing was written" }
        $set['Player'] = $p
    }

    $text = [IO.File]::ReadAllText($Cfg)
    $range = Get-Section $text
    $start = $range[0]; $next = $range[1]
    $section = $text.Substring($start, $next - $start)
    $missing = @($set.Keys | Where-Object { -not (Get-KeyRegex $_).Match($section).Success })
    if ($missing.Count -gt 0) {
        throw "the server's settings file has no [TestBuild] $($missing -join ', ') yet. Start the server once on this version (0.15.0) first, then press again."
    }
    foreach ($key in $set.Keys) {
        $v = $set[$key]
        $section = (Get-KeyRegex $key).Replace($section, { param($x) $x.Groups[1].Value + '"' + $v + '"' + $x.Groups[2].Value }, 1)
    }
    # one write, UTF-8 without a byte-order mark; everything outside the section is copied as it was
    [IO.File]::WriteAllText($Cfg, $text.Substring(0, $start) + $section + $text.Substring($next), (New-Object Text.UTF8Encoding($false)))

    if ($kind -eq 'Buy') {
        if ($value) { return "Buying: $($value -replace ';', ', ') (everything else sold). Server log: 'TEST BUILD: bought'." }
        return "Selling every archery orb (the gatherers stay). Server log: 'TEST BUILD: bought'."
    }
    if ($kind -eq 'Spawn' -and $value -match ' moving$') { return "Spawning $value ahead of you; they are never frozen (they walk and fight)." }
    if ($kind -eq 'Spawn')  { return "Spawning $value ahead of you; they freeze after 2 seconds." }
    if ($kind -eq 'Remove') { return 'Removing every test enemy.' }
    return 'Cleared: no orbs lent; your real skills apply.'
}

function Get-Player {
    $m = [regex]::Match([IO.File]::ReadAllText($Cfg), '(?ms)^\[TestBuild\]\r?$.*?^Player = "(.*?)"')
    if ($m.Success) { return $m.Groups[1].Value } else { return '' }
}

# ---------------------------------------------------------------- the play-test range (QuickshotRing TestRange*.cs)
# Each range button writes the [TestRange] section of the same file (and [TestBuild] Player) in one write; the server must have
# [TestRange] Enabled = true or it refuses every press. The server answers in UserData\QuickshotRing\range-status.txt (next to
# this cfg), shown under the range buttons. The last-shot panel reads the server's MelonLoader\Latest.log only (never the game's
# own logs under %APPDATA%).
#   key, button text, kind, value. Request: <value> = <new number>. Set: the listed keys. Named: ResetPlayer = the named player
#   + <value> = <new number> (Restore also writes RestoreFile). Reset: Named, after a confirmation. Confirm: a Request, after a confirmation. 'Everyone:<buy preset key>' buys
#   that preset for everyone online, after a confirmation naming how many players the server sees (range-online.txt); never a preset
#   that sells everything.
$RangePresets = [ordered]@{
    'Build'       = @('Build test range here',            'Request', 'BuildRequest')
    'RemoveRange' = @('Remove test range',                'Request', 'RemoveRequest')
    'Refill'      = @('Refill shelves',                   'Request', 'RefillRequest')
    'KillAll'     = @('Kill all test enemies',            'Request', 'KillRequest')
    'Frozen'      = @('Enemies: frozen',                  'Set',     'Frozen=true')
    'Free'        = @('Enemies: free',                    'Set',     'Frozen=false')
    'Respawn3'    = @('Respawn: 3 s',                     'Set',     'RespawnSeconds=3.0')
    'Respawn10'   = @('Respawn: 10 s',                    'Set',     'RespawnSeconds=10.0')
    'Respawn30'   = @('Respawn: 30 s',                    'Set',     'RespawnSeconds=30.0')
    'RespawnOff'  = @('Respawn: off',                     'Set',     'RespawnSeconds=0.0')
    'PointsOn'    = @('Everyone gets points: on',         'Set',     'EveryonePoints=true')
    'PointsOff'   = @('Everyone gets points: off',        'Set',     'EveryonePoints=false')
    'Snapshot'    = @('Snapshot archery (named)',         'Named',   'SnapshotRequest')
    'Restore'     = @('Restore snapshot (named)',         'Named',   'RestoreRequest')
    'Reset'       = @('Reset archery progress...',        'Reset',   'ResetRequest')
    'Heal'        = @('Heal me',                          'Request', 'HealRequest')
    'Noon'        = @('Noon',                             'Request', 'NoonRequest')
    'Teleport'    = @('Teleport to the archery shrine',   'Request', 'TeleportRequest')
    'Collect'     = @('Collect loose arrows',             'Request', 'CollectRequest')
    'Clean'       = @('Clean up loot and junk...',        'Confirm', 'CleanRequest')
    'Feed'        = @('Fill everyone''s hunger',          'Request', 'FeedRequest')
}

function Set-CfgKeys([hashtable]$bySection) {
    # $bySection: section name -> ordered key -> value; a quoted value stays quoted, a bare one (bool, number) stays bare
    $text = [IO.File]::ReadAllText($Cfg)
    foreach ($sec in $bySection.Keys) {
        $head = [regex]::Match($text, '(?m)^\[' + [regex]::Escape($sec) + '\]\r?$')
        if (-not $head.Success) { throw "no [$sec] section in $Cfg. Start the server once on this version first." }
        $start = $head.Index
        $next = $text.IndexOf("`n[", $start + 1, [StringComparison]::Ordinal)
        if ($next -lt 0) { $next = $text.Length } else { $next = $next + 1 }
        $section = $text.Substring($start, $next - $start)
        foreach ($key in $bySection[$sec].Keys) {
            $rx = [regex]('(?m)^(' + [regex]::Escape($key) + ' = )(.*?)(\r?)$')
            $m = $rx.Match($section)
            if (-not $m.Success) { throw "the server's settings file has no [$sec] $key yet. Start the server once on this version first, then press again." }
            $v = [string]$bySection[$sec][$key]
            if ($m.Groups[2].Value.StartsWith('"')) { $v = '"' + $v + '"' }
            $section = $rx.Replace($section, { param($x) $x.Groups[1].Value + $v + $x.Groups[3].Value }, 1)
        }
        $text = $text.Substring(0, $start) + $section + $text.Substring($next)
    }
    [IO.File]::WriteAllText($Cfg, $text, (New-Object Text.UTF8Encoding($false)))
}

function Test-Name([string]$name) {
    if ($name -match '["\\\p{Cc}]') { throw "a player name must not contain a double quote, a backslash or a line break: nothing was written" }
    return $name.Trim()
}

# H4 (range minor 9): a file the server replaces whole; read with every share flag (its replace never waits on us), a few tries
function Read-Shared([string]$path) {
    for ($i = 0; $i -lt 3; $i++) {
        if (-not (Test-Path -LiteralPath $path)) { return $null }
        try {
            $fs = New-Object IO.FileStream($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
            try { return (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
        } catch { Start-Sleep -Milliseconds 30 }
    }
    return $null
}

# H4 (range C-1): the server's list of who is online (written while [TestRange] Enabled, when it changes): a hashtable
# (state, ready, names, notready, time), or $null
function Get-Online {
    $text = Read-Shared (Join-Path (Split-Path -Parent $Cfg) 'QuickshotRing\range-online.txt')
    if ($null -eq $text) { return $null }
    $d = @{}
    foreach ($l in $text.Split("`n")) { $eq = $l.IndexOf('='); if ($eq -gt 0) { $d[$l.Substring(0, $eq)] = $l.Substring($eq + 1).TrimEnd("`r") } }
    return $d
}

# Writes one range button; returns @(the request number or '', the text to show). $confirmed: the window asked Yes/No, or -Confirm.
function Apply-Range([string]$name, [string]$player, [string]$named, [string]$file, [bool]$confirmed) {
    $stamp = [DateTime]::UtcNow.Ticks.ToString()
    $range = [ordered]@{}; $build = [ordered]@{}
    if ($player -and $player.Trim()) { $build['Player'] = Test-Name $player }
    if ($name.StartsWith('Everyone:')) {
        $key = $name.Substring(9)
        if (-not $Presets.Contains($key) -or $Presets[$key][1] -ne 'Buy') { throw "unknown buy preset '$key'" }
        # H4 (range C-1): "sell everything" is never sent to everyone (the server refuses it too)
        if (-not $Presets[$key][2]) { throw "'$($Presets[$key][0])' is never sent to everyone (it sells every archery orb): nothing was written" }
        if (-not $confirmed) { throw "Buy for all changes the saved archery progress of every online player: add -Confirm (the window asks first). Nothing was written." }
        $range['BuyEveryone'] = $Presets[$key][2]; $range['BuyEveryoneRequest'] = $stamp
        Set-CfgKeys @{ 'TestRange' = $range; 'TestBuild' = $build }
        return @($stamp, "Buying '$($Presets[$key][0])' for everyone online and loaded (a snapshot of each first)...")
    }
    if (-not $RangePresets.Contains($name)) { throw "unknown range preset '$name'; known: $($RangePresets.Keys -join ', '), Everyone:<buy preset>" }
    $def = $RangePresets[$name]
    $kind = $def[1]; $value = $def[2]
    if ($kind -eq 'Reset' -and -not $confirmed) { throw "Reset destroys saved archery progress: add -Confirm (the window asks first). Nothing was written." }
    if ($kind -eq 'Confirm' -and -not $confirmed) { throw "$($def[0].TrimEnd('.')) removes saved things from the world: add -Confirm (the window asks first). Nothing was written." }
    if ($kind -eq 'Request' -or $kind -eq 'Confirm') { $range[$value] = $stamp }
    if ($kind -eq 'Named' -or $kind -eq 'Reset') {
        $who = if ($named -and $named.Trim()) { $named } else { $player }
        if (-not ($who -and $who.Trim())) { throw "name the player first" }
        $range['ResetPlayer'] = Test-Name $who; $range[$value] = $stamp
        if ($value -eq 'RestoreRequest') {
            $f = if ($file) { $file.Trim() } else { '' }
            if ($f -match '["\\/:\p{Cc}]') { throw "the snapshot file is a file name only (no folder, quote or line break): nothing was written" }
            $range['RestoreFile'] = $f
        }
    }
    if ($kind -eq 'Set') {
        foreach ($pair in $value.Split(';')) { $kv = $pair.Split('=', 2); $range[$kv[0]] = $kv[1] }
        $stamp = ''
    }
    Set-CfgKeys @{ 'TestRange' = $range; 'TestBuild' = $build }
    if ($stamp) { return @($stamp, "$($def[0]): sent; waiting for the server...") }
    return @('', "$($def[0]): set (the server applies it within 2 seconds).")
}

if ($Preset -and ($RangePresets.Contains($Preset) -or $Preset.StartsWith('Everyone:'))) { (Apply-Range $Preset $Player $Player $SnapshotFile $Confirm.IsPresent)[1]; return }

if ($Preset) { Apply-Preset $Preset $Player; return }

# one window at a time (the game opens it at start with [TestBuild] MenuOnStart): a second copy exits quietly
$createdNew = $false
$mutex = New-Object Threading.Mutex($true, 'QuickshotRingTestMenu', [ref]$createdNew)
if (-not $createdNew) { $mutex.Dispose(); return }

Add-Type -AssemblyName System.Windows.Forms, System.Drawing
$rows = $Presets.Count
$form = New-Object Windows.Forms.Form -Property @{
    Text = 'Archery test tools'; TopMost = $true; FormBorderStyle = 'FixedToolWindow'
    StartPosition = 'Manual'; Location = '40,40'; ClientSize = "340,$(150 + 38 * $rows)"; Font = (New-Object Drawing.Font('Segoe UI', 10))
}
# Which build is installed, and whether a newer one is waiting for the game and the server to close. The installer
# (tools\install-when-closed.sh) keeps this small text file next to this script; read every 5 seconds, never written here.
$install = New-Object Windows.Forms.Label -Property @{ Location = "10,$(108 + 38 * $rows)"; Size = '320,38'; ForeColor = 'DimGray' }
$form.Controls.Add($install)
function Update-Install {
    try {
        $f = Join-Path $PSScriptRoot 'QuickshotRing-install-status.txt'
        if (-not (Test-Path -LiteralPath $f)) { $install.Text = ''; return }
        $inst = ''; $wait = ''
        foreach ($l in [IO.File]::ReadAllLines($f)) {
            if ($l.StartsWith('installed=')) { $inst = $l.Substring(10) }
            if ($l.StartsWith('waiting=')) { $wait = $l.Substring(8) }
        }
        if ($wait -and -not $inst.StartsWith($wait + ' ') -and $inst -ne $wait) {
            $install.Text = "Version $wait is ready: close the game and the server to install it."; $install.ForeColor = 'DarkOrange'
        } else { $install.Text = "Installed: $inst"; $install.ForeColor = 'DimGray' }
    } catch { }
}
Update-Install
$timer = New-Object Windows.Forms.Timer -Property @{ Interval = 5000 }
$timer.Add_Tick({ Update-Install })
$timer.Start()
$form.Controls.Add((New-Object Windows.Forms.Label -Property @{ Text = 'Player'; Location = '10,13'; AutoSize = $true }))
$who = New-Object Windows.Forms.TextBox -Property @{ Location = '70,10'; Width = 260 }
try { $who.Text = Get-Player } catch { }
$form.Controls.Add($who)
$status = New-Object Windows.Forms.Label -Property @{ Location = "10,$(50 + 38 * $rows)"; Size = '320,55'; Text = 'Be online in game, then press a button. The server picks it up within 2 seconds.' }
$form.Controls.Add($status)
$y = 45
foreach ($key in $Presets.Keys) {
    $b = New-Object Windows.Forms.Button -Property @{ Text = $Presets[$key][0]; Location = "10,$y"; Size = '320,34'; Tag = $key }
    $b.Add_Click({
        try { $status.Text = Apply-Preset $this.Tag $who.Text; $status.ForeColor = 'DarkGreen' }
        catch { $status.Text = "Failed: $($_.Exception.Message)"; $status.ForeColor = 'Firebrick' }
    })
    $form.Controls.Add($b)
    $y += 38
}
# ---------------------------------------------------------------- the play-test range column and the last-shot panel
# H4 (range minor 8): the window fits the screen it opens on (a 768 px high laptop too); what does not fit scrolls
$need = [Math]::Max(150 + 38 * $rows, 700)
$area = [Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$form.AutoScroll = $true
$form.Location = "$([Math]::Max(0, [Math]::Min(40, $area.Width - 1150))),$([Math]::Max(0, [Math]::Min(40, $area.Height - $need - 40)))"
$form.ClientSize = "$([Math]::Min(1130, $area.Width - 20)),$([Math]::Min($need, $area.Height - 50))"
$form.Controls.Add((New-Object Windows.Forms.Label -Property @{ Text = 'Play-test range (server: [TestRange] Enabled = true)'; Location = '350,13'; AutoSize = $true }))
$script:rangePending = ''; $script:rangeSentAt = [DateTime]::MinValue
$rangeStatus = New-Object Windows.Forms.Label -Property @{ Location = '350,562'; Size = '330,130'; Text = 'The server answers here.' }
$namedBox = New-Object Windows.Forms.TextBox -Property @{ Location = '350,490'; Width = 330 }
$fileBox = New-Object Windows.Forms.TextBox -Property @{ Location = '350,532'; Width = 330 }
$i = 0
foreach ($key in $RangePresets.Keys) {
    $b = New-Object Windows.Forms.Button -Property @{ Text = $RangePresets[$key][0]; Location = "$(350 + 168 * ($i % 2)),$(40 + 33 * [Math]::Floor($i / 2))"; Size = '162,30'; Tag = $key }
    $b.Add_Click({
        try {
            $k = $this.Tag
            $sure = $false
            if ($RangePresets[$k][1] -eq 'Reset') {
                $target = if ($namedBox.Text.Trim()) { $namedBox.Text.Trim() } else { $who.Text.Trim() }
                $q = "Reset ALL archery progress of '$target'?`n`nEvery archery orb (the game's own too) is sold, the archery level, experience and points go to 0, the main path and practised orbs are forgotten, and the save is written. A new snapshot is written first (never over an older one): 'Restore snapshot' puts it back."
                if ([Windows.Forms.MessageBox]::Show($q, 'Reset archery progress', 'YesNo', 'Warning') -ne 'Yes') { $rangeStatus.Text = 'Reset not sent.'; return }
                $sure = $true
            }
            if ($RangePresets[$k][1] -eq 'Confirm') {
                $q = "Clean up around the range?`n`nRemoves every SAVED thing lying loose within the server's [TestRange] CleanMetres (40 m) of the range: loot the test creatures dropped (ore, stone, flint, wood...), dropped bows, pouches and anything else on the ground, and anything named in CleanNames. Never a player, a living creature, or anything held, worn, docked or in a bag. What is removed is gone for good once the world saves."
                if ([Windows.Forms.MessageBox]::Show($q, 'Clean up loot and junk', 'YesNo', 'Warning') -ne 'Yes') { $rangeStatus.Text = 'Clean-up not sent.'; return }
                $sure = $true
            }
            $r = Apply-Range $k $who.Text $namedBox.Text $fileBox.Text $sure
            $script:rangePending = $r[0]; $script:rangeSentAt = [DateTime]::Now
            $rangeStatus.Text = $r[1]; $rangeStatus.ForeColor = 'DarkGreen'
        }
        catch { $rangeStatus.Text = "Failed: $($_.Exception.Message)"; $rangeStatus.ForeColor = 'Firebrick' }
    })
    $form.Controls.Add($b)
    $i++
}
$form.Controls.Add((New-Object Windows.Forms.Label -Property @{ Text = 'Buy for everyone online:'; Location = '350,433'; AutoSize = $true }))
$everyone = New-Object Windows.Forms.ComboBox -Property @{ Location = '350,452'; Width = 240; DropDownStyle = 'DropDownList' }
# H4 (range C-1): never a preset that sells everything; nothing is pre-selected, so a stray click sends nothing
foreach ($key in $Presets.Keys) { if ($Presets[$key][1] -eq 'Buy' -and $Presets[$key][2]) { [void]$everyone.Items.Add($key) } }
$form.Controls.Add($everyone)
$everyoneButton = New-Object Windows.Forms.Button -Property @{ Text = 'Buy for all'; Location = '598,450'; Size = '82,26' }
$everyoneButton.Add_Click({
    try {
        if (-not $everyone.SelectedItem) { $rangeStatus.Text = 'Pick a preset for everyone first.'; $rangeStatus.ForeColor = 'DarkOrange'; return }
        $key = [string]$everyone.SelectedItem
        $on = Get-Online
        if ($on -and $on['state'] -eq 'on') {
            $count = "The server sees $($on['ready']) player(s) online and loaded: $($on['names']) (as of $($on['time']))."
            if ($on['notready']) { $count += " Not loaded, skipped: $($on['notready'])." }
        } elseif ($on) { $count = "The server says the range is '$($on['state'])' (as of $($on['time'])): it will refuse." }
        else { $count = 'How many players are online is not known (the server writes it only while [TestRange] Enabled is on).' }
        $q = "Buy '$($Presets[$key][0])' for EVERY online player?`n`n$count`n`nFor each of them every archery orb not in this preset is sold and these are bought, and their save is written. A snapshot of each is written first: 'Restore snapshot (named)' with their name puts their own progress back."
        if ([Windows.Forms.MessageBox]::Show($q, 'Buy for all', 'YesNo', 'Warning') -ne 'Yes') { $rangeStatus.Text = 'Buy for all not sent.'; return }
        $r = Apply-Range ('Everyone:' + $key) $who.Text '' '' $true
        $script:rangePending = $r[0]; $script:rangeSentAt = [DateTime]::Now; $rangeStatus.Text = $r[1]; $rangeStatus.ForeColor = 'DarkGreen'
    }
    catch { $rangeStatus.Text = "Failed: $($_.Exception.Message)"; $rangeStatus.ForeColor = 'Firebrick' }
})
$form.Controls.Add($everyoneButton)
$form.Controls.Add((New-Object Windows.Forms.Label -Property @{ Text = 'Named player for reset / snapshot / restore (empty = Player):'; Location = '350,473'; AutoSize = $true }))
$form.Controls.Add($namedBox)
$form.Controls.Add((New-Object Windows.Forms.Label -Property @{ Text = 'Snapshot file for Restore (empty = newest):'; Location = '350,515'; AutoSize = $true }))
$form.Controls.Add($fileBox)
$form.Controls.Add($rangeStatus)
$form.Controls.Add((New-Object Windows.Forms.Label -Property @{ Text = "Last shot (the server's MelonLoader\Latest.log)"; Location = '695,13'; AutoSize = $true }))
$readout = New-Object Windows.Forms.TextBox -Property @{ Location = '695,35'; Size = "425,$([Math]::Max(300, $form.ClientSize.Height - 45))"; Multiline = $true; ReadOnly = $true; ScrollBars = 'Both'; WordWrap = $false; Font = (New-Object Drawing.Font('Consolas', 8)) }
$form.Controls.Add($readout)

$statusFile = Join-Path (Split-Path -Parent $Cfg) 'QuickshotRing\range-status.txt'
$serverLog = Join-Path (Split-Path -Parent (Split-Path -Parent $Cfg)) 'MelonLoader\Latest.log'
$script:logOffset = -1L; $script:logPartial = ''; $script:logLines = New-Object Collections.Generic.List[string]
function Update-RangeStatus {
    try {
        $text = Read-Shared $statusFile
        if ($null -eq $text) { return }
        $d = @{}
        foreach ($l in $text.Split("`n")) { $eq = $l.IndexOf('='); if ($eq -gt 0) { $d[$l.Substring(0, $eq)] = $l.Substring($eq + 1).TrimEnd("`r") } }
        if ($script:rangePending -and $d['number'] -ne $script:rangePending) {
            if (([DateTime]::Now - $script:rangeSentAt).TotalSeconds -gt 10) { $rangeStatus.Text = 'No answer from the server after 10 s: is it running, and on this version?'; $rangeStatus.ForeColor = 'DarkOrange' }
            return
        }
        $res = $d['result']
        $prefix = if ($res -eq 'refused') { 'REFUSED: ' } elseif ($res -eq 'partly') { 'PARTLY: ' } else { '' }
        $rangeStatus.Text = "$($d['time']) $prefix$($d['text'])`n`n$($d['state'])"
        $rangeStatus.ForeColor = if ($res -eq 'refused') { 'Firebrick' } elseif ($res -eq 'partly') { 'DarkOrange' } else { 'DarkGreen' }
        $script:rangePending = ''
    } catch { }
}
function Update-Readout {
    try {
        if (-not (Test-Path -LiteralPath $serverLog)) { $readout.Text = "No server log at $serverLog"; return }
        # H4 (range minor 11): only what was added since the last read (the last 400 KB the first time, or after a restart)
        $fs = New-Object IO.FileStream($serverLog, 'Open', 'Read', 'ReadWrite')
        try {
            $len = $fs.Length
            if ($len -eq $script:logOffset) { return }
            if ($script:logOffset -lt 0 -or $len -lt $script:logOffset) {
                $start = [Math]::Max(0L, $len - 400000L); $script:logLines.Clear(); $script:logPartial = ''
            } else { $start = $script:logOffset }
            [void]$fs.Seek($start, 'Begin')
            $take = [int]($len - $start)
            $buf = New-Object byte[] $take
            $n = $fs.Read($buf, 0, $take)
            $script:logOffset = $start + $n
        } finally { $fs.Dispose() }
        $parts = ($script:logPartial + [Text.Encoding]::UTF8.GetString($buf, 0, $n)).Split("`n")
        $script:logPartial = $parts[$parts.Count - 1]
        for ($j = 0; $j -lt $parts.Count - 1; $j++) { $script:logLines.Add($parts[$j]) }
        if ($script:logLines.Count -gt 6000) { $script:logLines.RemoveRange(0, $script:logLines.Count - 6000) }
        $lines = $script:logLines
        $id = ''
        for ($j = $lines.Count - 1; $j -ge 0; $j--) {
            $m = [regex]::Match($lines[$j], "\[Archery\] Release(?: \([^)]*\))?: arrow (\d+) '")
            if ($m.Success) { $id = $m.Groups[1].Value; break }
        }
        if (-not $id) { $readout.Text = 'No release in the server log yet (the Release line is written at [Log] Level Summary or Debug).'; return }
        $mine = [regex]('\[Archery\] .*\barrow ' + $id + "(\b|')")
        $out = New-Object Collections.Generic.List[string]
        $damage = [ordered]@{}
        foreach ($l in $lines) {
            if (-not $mine.IsMatch($l)) { continue }
            $t = $l.TrimEnd("`r")
            $h = [regex]::Match($t, "Hit: arrow \d+ on '([^']*)' ([0-9.]+) damage")
            if ($h.Success) { $c = $h.Groups[1].Value; if (-not $damage.Contains($c)) { $damage[$c] = @(0.0, 0) }; $damage[$c] = @(($damage[$c][0] + [double]::Parse($h.Groups[2].Value, [Globalization.CultureInfo]::InvariantCulture)), ($damage[$c][1] + 1)) }
            $k = $t.IndexOf('[Archery] ')
            $out.Add($t.Substring(0, [Math]::Min(14, $t.Length)) + ' ' + $(if ($k -ge 0) { $t.Substring($k + 10) } else { $t }))
        }
        $head = "Last release: arrow $id ($($out.Count) lines)"
        foreach ($c in $damage.Keys) { $head += "`r`n  real-arrow damage on '$c': $([Math]::Round($damage[$c][0], 2)) in $($damage[$c][1]) hit(s)" }
        if ($damage.Count -eq 0) { $head += "`r`n  (no 'Hit:' lines: those are written at [Log] Level Debug)" }
        $readout.Text = $head + "`r`n`r`n" + ($out -join "`r`n")
    } catch { $readout.Text = "Reading the server log failed: $($_.Exception.Message)" }
}
Update-RangeStatus
Update-Readout
$rangeTimer = New-Object Windows.Forms.Timer -Property @{ Interval = 2000 }
$rangeTimer.Add_Tick({ Update-RangeStatus; Update-Readout })
$rangeTimer.Start()
$form.Add_FormClosed({ $rangeTimer.Stop(); $rangeTimer.Dispose() })

try { [void]$form.ShowDialog() }
finally { $timer.Stop(); $timer.Dispose(); $mutex.ReleaseMutex(); $mutex.Dispose() }
