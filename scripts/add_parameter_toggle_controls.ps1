param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function New-GuidN {
    return [guid]::NewGuid().ToString("N")
}

function New-InputPort {
    param(
        [string]$Id,
        [string]$Name,
        [string]$Description
    )

    return [pscustomobject]@{
        Id = $Id
        Name = $Name
        Description = $Description
        UsingDefaultValue = $false
        Level = 2
        UseLevels = $false
        KeepListStructure = $false
    }
}

function New-OutputPort {
    param(
        [string]$Id,
        [string]$Name,
        [string]$Description
    )

    return [pscustomobject]@{
        Id = $Id
        Name = $Name
        Description = $Description
        UsingDefaultValue = $false
        Level = 2
        UseLevels = $false
        KeepListStructure = $false
    }
}

function New-BoolNode {
    param(
        [string]$NodeId,
        [string]$OutputId,
        [bool]$InputValue
    )

    return [pscustomobject]@{
        ConcreteType = "CoreNodeModels.Input.BoolSelector, CoreNodeModels"
        Id = $NodeId
        NodeType = "BooleanInputNode"
        Inputs = @()
        Outputs = @(
            (New-OutputPort -Id $OutputId -Name "" -Description "Boolean")
        )
        Replication = "Disabled"
        Description = "Enables selection between True and False"
        InputValue = $InputValue
    }
}

function New-CreateListNode {
    param(
        [string]$NodeId,
        [object[]]$Inputs,
        [string]$OutputId
    )

    return [pscustomobject]@{
        ConcreteType = "CoreNodeModels.CreateList, CoreNodeModels"
        VariableInputPorts = $true
        Id = $NodeId
        NodeType = "ExtensionNode"
        Inputs = $Inputs
        Outputs = @(
            (New-OutputPort -Id $OutputId -Name "list" -Description "A list")
        )
        Replication = "Disabled"
        Description = "Makes a new list from the given inputs"
    }
}

function New-Connector {
    param(
        [string]$Start,
        [string]$End
    )

    return [pscustomobject]@{
        Start = $Start
        End = $End
        Id = (New-GuidN)
        IsHidden = "False"
    }
}

function New-NodeView {
    param(
        [string]$Id,
        [string]$Name,
        [double]$X,
        [double]$Y
    )

    return [pscustomobject]@{
        Id = $Id
        Name = $Name
        IsSetAsInput = $false
        IsSetAsOutput = $false
        Excluded = $false
        ShowGeometry = $true
        X = $X
        Y = $Y
    }
}

function New-WatchNode {
    param(
        [string]$NodeId,
        [string]$InputId,
        [string]$OutputId
    )

    return [pscustomobject]@{
        ConcreteType = "CoreNodeModels.Watch, CoreNodeModels"
        WatchWidth = 260.0
        WatchHeight = 220.0
        Id = $NodeId
        NodeType = "ExtensionNode"
        Inputs = @(
            (New-InputPort -Id $InputId -Name "" -Description "Node to show output from")
        )
        Outputs = @(
            (New-OutputPort -Id $OutputId -Name "" -Description "Node output")
        )
        Replication = "Disabled"
        Description = "Visualizes a node's output"
    }
}

function Strip-CodeString {
    param([string]$Code)

    $text = $Code.Trim()
    if ($text.EndsWith(";")) {
        $text = $text.Substring(0, $text.Length - 1)
    }
    if ($text.StartsWith('"') -and $text.EndsWith('"')) {
        $text = $text.Substring(1, $text.Length - 2)
    }
    return $text
}

$resolvedPath = (Resolve-Path $Path).Path
$backupPath = [System.IO.Path]::ChangeExtension($resolvedPath, ".before_toggles.bak")

$text = [System.IO.File]::ReadAllText($resolvedPath, [System.Text.Encoding]::UTF8)
$json = $text | ConvertFrom-Json

$nodeById = @{}
$outputPortToNode = @{}
$inputPortToNode = @{}
$viewById = @{}

foreach ($view in $json.View.NodeViews) {
    $viewById[$view.Id] = $view
}

foreach ($node in $json.Nodes) {
    $nodeById[$node.Id] = $node
    foreach ($output in $node.Outputs) {
        $outputPortToNode[$output.Id] = $node.Id
    }
    foreach ($input in $node.Inputs) {
        $inputPortToNode[$input.Id] = $node.Id
    }
}

$setNodes = @(
    $json.Nodes |
    Where-Object {
        $_.PSObject.Properties.Name -contains "FunctionSignature" -and
        $_.FunctionSignature -eq "Revit.Elements.Element.SetParameterByName@string,var"
    } |
    Sort-Object { [double]$viewById[$_.Id].Y }
)

if ($setNodes.Count -eq 0) {
    throw "No SetParameterByName nodes were found."
}

$rows = @()
foreach ($setNode in $setNodes) {
    $setView = $viewById[$setNode.Id]
    $connections = @{}

    foreach ($input in $setNode.Inputs) {
        $connector = $json.Connectors | Where-Object End -eq $input.Id | Select-Object -First 1
        if ($null -ne $connector) {
            $connections[$input.Name] = $connector.Start
        }
    }

    $parameterNodeId = $outputPortToNode[$connections["parameterName"]]
    $valueNodeId = $outputPortToNode[$connections["value"]]

    $rows += [pscustomobject]@{
        SetNodeId = $setNode.Id
        SetNodeY = [double]$setView.Y
        ParameterNodeId = $parameterNodeId
        ValueNodeId = $valueNodeId
        ElementStartPortId = $connections["element"]
        ParameterName = Strip-CodeString -Code ([string]$nodeById[$parameterNodeId].Code)
    }
}

$flattenOutputPortId = $rows[0].ElementStartPortId

$removeNodeIds = @($rows | ForEach-Object SetNodeId)
$json.Nodes = @($json.Nodes | Where-Object { $removeNodeIds -notcontains $_.Id })
$json.View.NodeViews = @($json.View.NodeViews | Where-Object { $removeNodeIds -notcontains $_.Id })
$json.Connectors = @(
    $json.Connectors |
    Where-Object {
        $startOwner = $null
        $endOwner = $null
        if ($outputPortToNode.ContainsKey($_.Start)) { $startOwner = $outputPortToNode[$_.Start] }
        if ($inputPortToNode.ContainsKey($_.End)) { $endOwner = $inputPortToNode[$_.End] }
        ($removeNodeIds -notcontains $startOwner) -and ($removeNodeIds -notcontains $endOwner)
    }
)

$toggleNodes = @()
$toggleNodeViews = @()
$toggleConnectors = @()
$toggleListInputs = @()
$nameListInputs = @()
$valueListInputs = @()
$toggleNodeIds = @()

$toggleX = 1210.0
$nameListX = 1600.0
$valueListX = 1600.0
$toggleListX = 1600.0
$pythonX = 1940.0
$watchX = 2290.0

$minY = ($rows | Measure-Object -Property SetNodeY -Minimum).Minimum
$maxY = ($rows | Measure-Object -Property SetNodeY -Maximum).Maximum
$centerY = ($minY + $maxY) / 2.0

$rowIndex = 0
foreach ($row in $rows) {
    $rowIndex += 1

    $toggleNodeId = New-GuidN
    $toggleOutputId = New-GuidN
    $toggleNodes += New-BoolNode -NodeId $toggleNodeId -OutputId $toggleOutputId -InputValue $true
    $toggleNodeViews += New-NodeView -Id $toggleNodeId -Name ("Run " + $row.ParameterName) -X $toggleX -Y $row.SetNodeY
    $toggleNodeIds += $toggleNodeId

    $toggleInputId = New-GuidN
    $toggleListInputs += (New-InputPort -Id $toggleInputId -Name ("item" + ($rowIndex - 1)) -Description ("Item Index #" + ($rowIndex - 1)))
    $toggleConnectors += New-Connector -Start $toggleOutputId -End $toggleInputId

    $nameInputId = New-GuidN
    $nameListInputs += (New-InputPort -Id $nameInputId -Name ("item" + ($rowIndex - 1)) -Description ("Item Index #" + ($rowIndex - 1)))
    $toggleConnectors += New-Connector -Start ($nodeById[$row.ParameterNodeId].Outputs[0].Id) -End $nameInputId

    $valueInputId = New-GuidN
    $valueListInputs += (New-InputPort -Id $valueInputId -Name ("item" + ($rowIndex - 1)) -Description ("Item Index #" + ($rowIndex - 1)))
    $toggleConnectors += New-Connector -Start ($nodeById[$row.ValueNodeId].Outputs[0].Id) -End $valueInputId
}

$nameListNodeId = New-GuidN
$nameListOutputId = New-GuidN
$valueListNodeId = New-GuidN
$valueListOutputId = New-GuidN
$toggleListNodeId = New-GuidN
$toggleListOutputId = New-GuidN

$json.Nodes += New-CreateListNode -NodeId $nameListNodeId -Inputs $nameListInputs -OutputId $nameListOutputId
$json.Nodes += New-CreateListNode -NodeId $valueListNodeId -Inputs $valueListInputs -OutputId $valueListOutputId
$json.Nodes += New-CreateListNode -NodeId $toggleListNodeId -Inputs $toggleListInputs -OutputId $toggleListOutputId
$json.Nodes += $toggleNodes

$json.View.NodeViews += $toggleNodeViews
$json.View.NodeViews += (New-NodeView -Id $nameListNodeId -Name "Parameter Names" -X $nameListX -Y ($centerY - 150.0))
$json.View.NodeViews += (New-NodeView -Id $valueListNodeId -Name "Parameter Values" -X $valueListX -Y ($centerY + 80.0))
$json.View.NodeViews += (New-NodeView -Id $toggleListNodeId -Name "Apply Toggles" -X $toggleListX -Y ($centerY + 310.0))

$pythonNodeId = New-GuidN
$pythonInputElementsId = New-GuidN
$pythonInputNamesId = New-GuidN
$pythonInputValuesId = New-GuidN
$pythonInputTogglesId = New-GuidN
$pythonOutputId = New-GuidN
$pythonCode = "script_path = r""C:\Users\user\Desktop\codex\parameter_apply_with_toggles.py""`nnamespace = {""IN"": IN}`nwith open(script_path, ""r"", encoding=""utf-8"") as handle:`n    code = handle.read()`nexec(compile(code, script_path, ""exec""), namespace)`nOUT = namespace.get(""OUT"")`n"

$pythonNode = [pscustomobject]@{
    ConcreteType = "PythonNodeModels.PythonNode, PythonNodeModels"
    Code = $pythonCode
    Engine = "CPython3"
    EngineName = "CPython3"
    VariableInputPorts = $true
    Id = $pythonNodeId
    NodeType = "PythonScriptNode"
    Inputs = @(
        (New-InputPort -Id $pythonInputElementsId -Name "IN[0]" -Description "Input #0"),
        (New-InputPort -Id $pythonInputNamesId -Name "IN[1]" -Description "Input #1"),
        (New-InputPort -Id $pythonInputValuesId -Name "IN[2]" -Description "Input #2"),
        (New-InputPort -Id $pythonInputTogglesId -Name "IN[3]" -Description "Input #3")
    )
    Outputs = @(
        (New-OutputPort -Id $pythonOutputId -Name "OUT" -Description "Result of the python script")
    )
    Replication = "Disabled"
    Description = "Runs an embedded Python script."
}

$watchNodeId = New-GuidN
$watchInputId = New-GuidN
$watchOutputId = New-GuidN

$json.Nodes += $pythonNode
$json.Nodes += (New-WatchNode -NodeId $watchNodeId -InputId $watchInputId -OutputId $watchOutputId)

$json.View.NodeViews += (New-NodeView -Id $pythonNodeId -Name "Apply Parameters Python" -X $pythonX -Y ($centerY + 20.0))
$json.View.NodeViews += (New-NodeView -Id $watchNodeId -Name "Apply Result" -X $watchX -Y ($centerY + 20.0))

$json.Connectors += $toggleConnectors
$json.Connectors += (New-Connector -Start $flattenOutputPortId -End $pythonInputElementsId)
$json.Connectors += (New-Connector -Start $nameListOutputId -End $pythonInputNamesId)
$json.Connectors += (New-Connector -Start $valueListOutputId -End $pythonInputValuesId)
$json.Connectors += (New-Connector -Start $toggleListOutputId -End $pythonInputTogglesId)
$json.Connectors += (New-Connector -Start $pythonOutputId -End $watchInputId)

if ($null -eq $json.View.Annotations) {
    $json.View | Add-Member -NotePropertyName Annotations -NotePropertyValue @()
}

$annotationNodes = @()
$annotationNodes += $toggleNodeIds
$annotationNodes += $nameListNodeId
$annotationNodes += $valueListNodeId
$annotationNodes += $toggleListNodeId
$annotationNodes += $pythonNodeId
$annotationNodes += $watchNodeId

$annotation = [pscustomobject]@{
    Id = (New-GuidN)
    Title = "Parameter Toggle Control"
    DescriptionText = "Turn each Run toggle on or off to decide which parameter value is applied before executing the batch Python node."
    IsExpanded = $true
    WidthAdjustment = 0.0
    HeightAdjustment = 0.0
    Nodes = $annotationNodes
    HasNestedGroups = $false
    Left = 1180.0
    Top = ($minY - 120.0)
    Width = 1400.0
    Height = (($maxY - $minY) + 320.0)
    FontSize = 0.0
    GroupStyleId = "00000000-0000-0000-0000-000000000000"
    InitialTop = ($minY - 60.0)
    InitialHeight = (($maxY - $minY) + 200.0)
    TextblockHeight = 56.0
    Background = "#FFDCECC9"
}

$json.View.Annotations += $annotation

[System.IO.File]::WriteAllText($backupPath, $text, [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText($resolvedPath, ($json | ConvertTo-Json -Depth 100), [System.Text.Encoding]::UTF8)

Write-Output ("Updated: " + $resolvedPath)
Write-Output ("Backup:  " + $backupPath)
