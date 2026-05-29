$ErrorActionPreference = 'Stop'

$src = (Get-ChildItem -LiteralPath 'C:\Users\user\Desktop\codex' -Recurse -Filter '@final_select edge_fit_family_nrbscurve_dwg_position_rotate_zonly.dyn' | Select-Object -First 1).FullName
$dir = Split-Path -Parent $src
$backup = Join-Path $dir 'backup_@final_select edge_fit_family_nrbscurve_dwg_position_rotate_zonly.dyn'
$target = Join-Path $dir '@player_final_select edge_fit_family_nrbscurve_dwg_position_rotate_zonly.dyn'

Copy-Item -LiteralPath $src -Destination $backup -Force

$text = [System.IO.File]::ReadAllText($src, [System.Text.Encoding]::UTF8)
$json = $text | ConvertFrom-Json

$nameMap = @{
    '28853605db3447dd98df6fcca3ae1a11' = 'Spacing (mm)'
    '2d0ce39007f74ca3a6146da2c3f8bd83' = 'Family Type'
    'ef20cd9c3db0432bab05827c1cf972ad' = 'Rotate Mode'
    'a9ecd268c75f48eab79adae089926d74' = 'Tolerance (mm)'
    'ff4260d43152479aa9fbcc404819ed5c' = 'Repick'
    '062d4ec3eb754e06ba2d6289f4b52dad' = 'Perp Offset (mm)'
    'eee04474f1ef4accad5e1ba427d5c392' = 'Parallel Offset (mm)'
}

foreach($nv in $json.View.NodeViews){
    if($nameMap.ContainsKey($nv.Id)){
        $nv.Name = $nameMap[$nv.Id]
        $nv.IsSetAsInput = $true
    }
}

$json.Name = 'player_final_select_edge_fit_family_nrbscurve_dwg_position_rotate_zonly'

$out = $json | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($target, $out, [System.Text.Encoding]::UTF8)
(Get-Item -LiteralPath $target).LastWriteTime = Get-Date
Get-Item -LiteralPath $backup, $target | Select-Object Name, Length, LastWriteTime
