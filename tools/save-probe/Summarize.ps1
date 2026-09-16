#Requires -Version 7.0
#Requires -PSEdition Core
param([string]$Evidence = "$PSScriptRoot/../../build/save-attribution-20260916")
$ErrorActionPreference = 'Stop'
$rows = foreach ($dir in Get-ChildItem -LiteralPath $Evidence -Directory) {
    $eventsPath = Join-Path $dir.FullName 'events.jsonl'
    if (!(Test-Path -LiteralPath $eventsPath)) { continue }
    $events = @(Get-Content -LiteralPath $eventsPath | ForEach-Object { $_ | ConvertFrom-Json })
    $completed = @($events | Where-Object name -EQ 'probe.complete').Count -eq 1
    foreach ($reward in $events | Where-Object name -EQ 'rewards.visible') {
        $trial = @($events | Where-Object trial -EQ $reward.trial)
        $start = $trial | Where-Object { $_.name -eq 'save.start' -and $_.data.victory } | Select-Object -Last 1
        $local = $trial | Where-Object { $_.name -eq 'local.write-complete' -and $_.ms -ge $start.ms } | Select-Object -First 1
        $request = $trial | Where-Object { $_.name -eq 'steam.request' -and $_.ms -ge $start.ms } | Select-Object -First 1
        $callback = $trial | Where-Object { $_.name -eq 'steam.callback' -and $_.ms -ge $request.ms } | Select-Object -First 1
        $save = $trial | Where-Object { $_.name -eq 'save.complete' -and $_.ms -ge $start.ms } | Select-Object -First 1
        [pscustomobject]@{
            run=$dir.Name; completed=$completed; trial=$reward.trial
            mode=($trial | Where-Object name -EQ 'combat.finish-start' | Select-Object -First 1).data.mode
            localMs=[Math]::Round($local.data.durationMs,2)
            callbackMs=[Math]::Round($callback.ms-$request.ms,2)
            callbackFrames=$callback.frames-$request.frames
            saveMs=[Math]::Round($save.ms-$start.ms,2)
            rewardAfterSaveMs=[Math]::Round($reward.ms-$save.ms,2)
            wins=$reward.data.wins; callbackResult=$callback.data.result
        }
    }
}
$rows | Export-Csv (Join-Path $Evidence 'victory-timings.csv') -NoTypeInformation -Encoding utf8
$summary = $rows | Group-Object run | ForEach-Object {
    $g = $_.Group
    [pscustomobject]@{
        run=$_.Name; completed=$g[0].completed; victories=$g.Count
        maxLocalMs=($g.localMs | Measure-Object -Maximum).Maximum
        maxCallbackMs=($g.callbackMs | Measure-Object -Maximum).Maximum
        maxSaveMs=($g.saveMs | Measure-Object -Maximum).Maximum
        minCallbackFrames=($g.callbackFrames | Measure-Object -Minimum).Minimum
        maxRewardAfterSaveMs=($g.rewardAfterSaveMs | Measure-Object -Maximum).Maximum
    }
}
$summary | ConvertTo-Json | Set-Content (Join-Path $Evidence 'summary.json') -Encoding utf8
$summary | Format-Table -AutoSize
