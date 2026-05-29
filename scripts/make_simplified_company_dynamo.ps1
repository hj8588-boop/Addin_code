$ErrorActionPreference = 'Stop'

$src = (Get-ChildItem -LiteralPath 'C:\Users\user\Desktop\codex' -Recurse -Filter '@final_civil3d_dwg_explode_outer_top_single_run_proxy_and_place_.dyn' | Select-Object -First 1).FullName
$dir = Split-Path -Parent $src
$backup = Join-Path $dir 'backup_@final_civil3d_dwg_explode_outer_top_single_run_proxy_and_place_.dyn'
$target = Join-Path $dir '@final_civil3d_dwg_explode_outer_top_single_run_proxy_and_place_simple.dyn'

Copy-Item -LiteralPath $src -Destination $backup -Force

$text = [System.IO.File]::ReadAllText($src, [System.Text.Encoding]::UTF8)
$json = $text | ConvertFrom-Json

$pythonNode = $json.Nodes | Where-Object { $_.ConcreteType -like '*Python*' } | Select-Object -First 1
$code = [string]$pythonNode.Code
$code = $code.Replace(
@'
spacing_mm  = IN[0]
symbol_in   = IN[1]
tol_mm      = IN[2]
rotate_mode = IN[3]
repick      = IN[4]
off_perp_mm = IN[5]
off_par_mm  = IN[6]
'@,
@'
spacing_mm  = IN[0]
symbol_in   = IN[1]
tol_mm      = 100.0
rotate_mode = 1
repick      = IN[2]
off_perp_mm = IN[3]
off_par_mm  = IN[4]
'@
)
$pythonNode.Code = $code

$removeNodeIds = @(
    'ef20cd9c3db0432bab05827c1cf972ad', # rotate_mode slider
    'a9ecd268c75f48eab79adae089926d74'  # tol input
)

$json.Nodes = @($json.Nodes | Where-Object { $removeNodeIds -notcontains $_.Id })
$json.View.NodeViews = @($json.View.NodeViews | Where-Object { $removeNodeIds -notcontains $_.Id })

# Rewire remaining inputs to earlier Python ports.
foreach($c in $json.Connectors){
    switch ($c.Start) {
        'd95e457246b2470e845f3b841af7c92e' { $c.End = '234da2b303174182a98b354d3e28aa36' } # repick -> IN[2]
        'e4d53b40de514f19a423b3905a926055' { $c.End = 'e69cda59889d4391a9313d1b9ca72774' } # off_perp -> IN[3]
        '8bcb7628a3e24b3db5fb5e7186b4a897' { $c.End = 'ce758bfb6f4346aa8788734203ca3939' } # off_par -> IN[4]
    }
}

$json.Connectors = @($json.Connectors | Where-Object { $removeNodeIds -notcontains $_.Start })

$out = $json | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($target, $out, [System.Text.Encoding]::UTF8)
(Get-Item -LiteralPath $target).LastWriteTime = Get-Date
Get-Item -LiteralPath $backup, $target | Select-Object Name, Length, LastWriteTime
