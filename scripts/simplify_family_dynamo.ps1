param(
    [string]$SourcePath = "",
    [string]$OutputPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function New-GuidString {
    [guid]::NewGuid().ToString("N")
}

function New-Port {
    param(
        [string]$Name,
        [string]$Description = "",
        [bool]$UsingDefaultValue = $false
    )

    [pscustomobject]@{
        Id = New-GuidString
        Name = $Name
        Description = $Description
        UsingDefaultValue = $UsingDefaultValue
        Level = 2
        UseLevels = $false
        KeepListStructure = $false
    }
}

function Clone-NodeView {
    param(
        [string]$Id,
        [string]$Name,
        [double]$X,
        [double]$Y
    )

    [pscustomobject]@{
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

function Normalize-CodeLiteral {
    param([string]$Code)

    if ([string]::IsNullOrWhiteSpace($Code)) {
        return '""'
    }

    return ($Code -replace ";\s*$", "").Trim()
}

function New-CodeBlockNode {
    param(
        [pscustomobject]$Template,
        [string]$Code
    )

    [pscustomobject]@{
        ConcreteType = $Template.ConcreteType
        Id = New-GuidString
        NodeType = $Template.NodeType
        Inputs = @()
        Outputs = @(
            New-Port -Name $Template.Outputs[0].Name -Description $Template.Outputs[0].Description
        )
        Replication = $Template.Replication
        Description = $Template.Description
        Code = $Code
    }
}

function New-ListCreateNode {
    param(
        [pscustomobject]$Template,
        [int]$InputCount
    )

    $inputs = @()
    for ($i = 0; $i -lt $InputCount; $i++) {
        $inputs += New-Port -Name ("item{0}" -f $i) -Description ("Item Index #{0}" -f $i)
    }

    [pscustomobject]@{
        ConcreteType = $Template.ConcreteType
        VariableInputPorts = $true
        Id = New-GuidString
        NodeType = $Template.NodeType
        Inputs = $inputs
        Outputs = @(
            New-Port -Name $Template.Outputs[0].Name -Description $Template.Outputs[0].Description
        )
        Replication = $Template.Replication
        Description = $Template.Description
    }
}

function New-ElementsNode {
    param(
        [pscustomobject]$Template
    )

    $inputs = @()
    foreach ($inputPort in $Template.Inputs) {
        $inputs += New-Port -Name $inputPort.Name -Description $inputPort.Description
    }

    [pscustomobject]@{
        ConcreteType = $Template.ConcreteType
        Id = New-GuidString
        NodeType = $Template.NodeType
        Inputs = $inputs
        Outputs = @(
            New-Port -Name $Template.Outputs[0].Name -Description $Template.Outputs[0].Description
        )
        Replication = $Template.Replication
        Description = $Template.Description
    }
}

function New-SetParameterNode {
    param(
        [pscustomobject]$Template
    )

    $inputs = @()
    foreach ($inputPort in $Template.Inputs) {
        $inputs += New-Port -Name $inputPort.Name -Description $inputPort.Description
    }

    [pscustomobject]@{
        ConcreteType = $Template.ConcreteType
        Id = New-GuidString
        NodeType = $Template.NodeType
        Inputs = $inputs
        Outputs = @(
            New-Port -Name $Template.Outputs[0].Name -Description $Template.Outputs[0].Description
        )
        FunctionSignature = $Template.FunctionSignature
        Replication = $Template.Replication
        Description = $Template.Description
    }
}

function New-Connector {
    param(
        [string]$Start,
        [string]$End
    )

    [pscustomobject]@{
        Start = $Start
        End = $End
        Id = New-GuidString
        IsHidden = "False"
    }
}

function Get-InputNode {
    param(
        [pscustomobject]$Node,
        [string]$InputName,
        [array]$Connectors,
        [hashtable]$NodeByOutput
    )

    $port = $Node.Inputs | Where-Object { $_.Name -eq $InputName } | Select-Object -First 1
    if (-not $port) {
        return $null
    }

    $connector = $Connectors | Where-Object { $_.End -eq $port.Id } | Select-Object -First 1
    if (-not $connector) {
        return $null
    }

    return $NodeByOutput[$connector.Start]
}

function Resolve-SelectionNames {
    param(
        [pscustomobject]$Node,
        [string]$Kind,
        [array]$Connectors,
        [hashtable]$NodeByOutput
    )

    if ($null -eq $Node) {
        return @()
    }

    if ($Node.ConcreteType -like "*CreateList*") {
        $items = @()
        foreach ($port in $Node.Inputs) {
            $connector = $Connectors | Where-Object { $_.End -eq $port.Id } | Select-Object -First 1
            if ($connector) {
                $child = $NodeByOutput[$connector.Start]
                $items += Resolve-SelectionNames -Node $child -Kind $Kind -Connectors $Connectors -NodeByOutput $NodeByOutput
            }
        }
        return $items
    }

    if ($Kind -eq "Family" -and $Node.ConcreteType -like "*FamilyTypes*") {
        return @($Node.SelectedString)
    }

    if ($Kind -eq "Category" -and $Node.ConcreteType -like "*Categories*") {
        return @($Node.SelectedString)
    }

    return @()
}

function Resolve-Targets {
    param(
        [pscustomobject]$Node,
        [array]$Connectors,
        [hashtable]$NodeByOutput
    )

    if ($null -eq $Node) {
        return @()
    }

    if ($Node.ConcreteType -like "*CreateList*") {
        $items = @()
        foreach ($port in $Node.Inputs) {
            $connector = $Connectors | Where-Object { $_.End -eq $port.Id } | Select-Object -First 1
            if ($connector) {
                $child = $NodeByOutput[$connector.Start]
                $items += Resolve-Targets -Node $child -Connectors $Connectors -NodeByOutput $NodeByOutput
            }
        }
        return $items
    }

    if ($Node.ConcreteType -like "*ElementsOfFamilyType*") {
        $familyInput = Get-InputNode -Node $Node -InputName "Family Type" -Connectors $Connectors -NodeByOutput $NodeByOutput
        $names = Resolve-SelectionNames -Node $familyInput -Kind "Family" -Connectors $Connectors -NodeByOutput $NodeByOutput
        return @($names | Where-Object { $_ -ne $null } | ForEach-Object {
            [pscustomobject]@{ Type = "Family"; Name = $_ }
        })
    }

    if ($Node.ConcreteType -like "*ElementsOfCategory*") {
        $categoryInput = Get-InputNode -Node $Node -InputName "Category" -Connectors $Connectors -NodeByOutput $NodeByOutput
        $names = Resolve-SelectionNames -Node $categoryInput -Kind "Category" -Connectors $Connectors -NodeByOutput $NodeByOutput
        return @($names | Where-Object { $_ -ne $null } | ForEach-Object {
            [pscustomobject]@{ Type = "Category"; Name = $_ }
        })
    }

    return @()
}

function Build-NestedCode {
    param([array]$Groups)

    $outer = foreach ($group in $Groups) {
        $inner = foreach ($item in $group) {
            Normalize-CodeLiteral $item
        }
        "{0}" -f ("{" + ($inner -join ", ") + "}")
    }

    return ("{" + ($outer -join ", ") + "};")
}

$workspaceRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $sourceItem = Get-ChildItem -Path (Join-Path $workspaceRoot "*.dyn") -File | Select-Object -First 1
}
else {
    $sourceItem = Get-Item -LiteralPath $SourcePath
}

if (-not $sourceItem) {
    throw "Source .dyn file was not found."
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputFullPath = Join-Path $workspaceRoot "parameter_family_simplified.dyn"
}
else {
    $outputFullPath = $OutputPath
}

$sourceFullPath = $sourceItem.FullName

$text = [System.IO.File]::ReadAllText($sourceFullPath, [System.Text.Encoding]::UTF8)
$graph = $text | ConvertFrom-Json

$nodeByOutput = @{}
foreach ($node in $graph.Nodes) {
    foreach ($outputPort in $node.Outputs) {
        $nodeByOutput[$outputPort.Id] = $node
    }
}

$familyRows = New-Object System.Collections.ArrayList
$categoryRows = New-Object System.Collections.ArrayList

foreach ($setNode in ($graph.Nodes | Where-Object { $_.PSObject.Properties["FunctionSignature"] -and $_.FunctionSignature -like "*SetParameterByName*" })) {
    $elementSource = Get-InputNode -Node $setNode -InputName "element" -Connectors $graph.Connectors -NodeByOutput $nodeByOutput
    $parameterNode = Get-InputNode -Node $setNode -InputName "parameterName" -Connectors $graph.Connectors -NodeByOutput $nodeByOutput
    $valueNode = Get-InputNode -Node $setNode -InputName "value" -Connectors $graph.Connectors -NodeByOutput $nodeByOutput
    $targets = @(Resolve-Targets -Node $elementSource -Connectors $graph.Connectors -NodeByOutput $nodeByOutput)

    $familyTargets = @($targets | Where-Object { $_.Type -eq "Family" -and -not [string]::IsNullOrWhiteSpace($_.Name) })
    $categoryTargets = @($targets | Where-Object { $_.Type -eq "Category" -and -not [string]::IsNullOrWhiteSpace($_.Name) })

    $rowTargetNames = @()
    $rowTargetNames += @($familyTargets | ForEach-Object { $_.Name })
    $rowTargetNames += @($categoryTargets | ForEach-Object { $_.Name })

    $row = [pscustomobject]@{
        Targets = $rowTargetNames
        Parameter = $parameterNode.Code
        Value = $valueNode.Code
    }

    if ($familyTargets.Count -gt 0) {
        [void]$familyRows.Add($row)
    }
    elseif ($categoryTargets.Count -gt 0) {
        [void]$categoryRows.Add($row)
    }
}

function Group-Rows {
    param([System.Collections.ArrayList]$Rows)

    $groups = New-Object System.Collections.ArrayList
    $indexByKey = @{}

    foreach ($row in $Rows) {
        $targetKey = $row.Targets -join "|"
        if (-not $indexByKey.ContainsKey($targetKey)) {
            $group = [pscustomobject]@{
                Targets = @($row.Targets)
                ParameterValues = New-Object System.Collections.ArrayList
            }
            $indexByKey[$targetKey] = $groups.Count
            [void]$groups.Add($group)
        }

        $groupRef = $groups[$indexByKey[$targetKey]]
        $pairKey = "{0}|{1}" -f $row.Parameter, $row.Value
        $existingKeys = @($groupRef.ParameterValues | ForEach-Object { "{0}|{1}" -f $_.Parameter, $_.Value })
        if ($existingKeys -notcontains $pairKey) {
            [void]$groupRef.ParameterValues.Add([pscustomobject]@{
                Parameter = $row.Parameter
                Value = $row.Value
            })
        }
    }

    return $groups
}

$familyGroups = Group-Rows -Rows $familyRows
$categoryGroups = Group-Rows -Rows $categoryRows

$familyNodeMap = @{}
foreach ($node in ($graph.Nodes | Where-Object { $_.ConcreteType -like "*FamilyTypes*" })) {
    if (-not $familyNodeMap.ContainsKey($node.SelectedString)) {
        $familyNodeMap[$node.SelectedString] = $node
    }
}

$categoryNodeMap = @{}
foreach ($node in ($graph.Nodes | Where-Object { $_.ConcreteType -like "*Categories*" })) {
    if (-not $categoryNodeMap.ContainsKey($node.SelectedString)) {
        $categoryNodeMap[$node.SelectedString] = $node
    }
}

$listTemplate = $graph.Nodes | Where-Object { $_.ConcreteType -like "*CreateList*" } | Select-Object -First 1
$codeTemplate = $graph.Nodes | Where-Object { $_.ConcreteType -like "*CodeBlockNodeModel*" } | Select-Object -First 1
$familyElementsTemplate = $graph.Nodes | Where-Object { $_.ConcreteType -like "*ElementsOfFamilyType*" } | Select-Object -First 1
$categoryElementsTemplate = $graph.Nodes | Where-Object { $_.ConcreteType -like "*ElementsOfCategory*" } | Select-Object -First 1
$setTemplate = $graph.Nodes | Where-Object { $_.PSObject.Properties["FunctionSignature"] -and $_.FunctionSignature -like "*SetParameterByName*" } | Select-Object -First 1

$newNodes = New-Object System.Collections.ArrayList
$newConnectors = New-Object System.Collections.ArrayList
$newNodeViews = New-Object System.Collections.ArrayList
$usedNodeIds = New-Object System.Collections.Generic.HashSet[string]

function Add-ReusedNode {
    param(
        [pscustomobject]$Node,
        [double]$X,
        [double]$Y,
        [string]$ViewName
    )

    if (-not $usedNodeIds.Contains($Node.Id)) {
        [void]$usedNodeIds.Add($Node.Id)
        [void]$newNodes.Add($Node)
    }
    [void]$newNodeViews.Add((Clone-NodeView -Id $Node.Id -Name $ViewName -X $X -Y $Y))
}

$familySelectorOrder = New-Object System.Collections.ArrayList
foreach ($group in $familyGroups) {
    foreach ($name in $group.Targets) {
        if ($familySelectorOrder -notcontains $name) {
            [void]$familySelectorOrder.Add($name)
        }
    }
}

$categorySelectorOrder = New-Object System.Collections.ArrayList
foreach ($group in $categoryGroups) {
    foreach ($name in $group.Targets) {
        if ($categorySelectorOrder -notcontains $name) {
            [void]$categorySelectorOrder.Add($name)
        }
    }
}

$familyNodeByName = @{}
$familyX = -1450.0
$familyY = -900.0
$familySpacingY = 150.0
for ($i = 0; $i -lt $familySelectorOrder.Count; $i++) {
    $name = [string]$familySelectorOrder[$i]
    $node = $familyNodeMap[$name]
    if (-not $node) {
        throw "Missing Family Types node for '$name'."
    }
    $familyNodeByName[$name] = $node
    Add-ReusedNode -Node $node -X $familyX -Y ($familyY + ($i * $familySpacingY)) -ViewName "Family Types"
}

$categoryNodeByName = @{}
$categoryX = -1450.0
$categoryY = 2800.0
$categorySpacingY = 170.0
for ($i = 0; $i -lt $categorySelectorOrder.Count; $i++) {
    $name = [string]$categorySelectorOrder[$i]
    $node = $categoryNodeMap[$name]
    if (-not $node) {
        throw "Missing Categories node for '$name'."
    }
    $categoryNodeByName[$name] = $node
    Add-ReusedNode -Node $node -X $categoryX -Y ($categoryY + ($i * $categorySpacingY)) -ViewName "Categories"
}

$familyMultiGroupNodes = @{}
$familyGroupList = New-Object System.Collections.ArrayList
$familyInnerX = -980.0
$familyInnerBaseY = -650.0
$familyInnerStepY = 240.0

for ($i = 0; $i -lt $familyGroups.Count; $i++) {
    $group = $familyGroups[$i]
    if ($group.Targets.Count -gt 1) {
        $listNode = New-ListCreateNode -Template $listTemplate -InputCount $group.Targets.Count
        [void]$newNodes.Add($listNode)
        [void]$newNodeViews.Add((Clone-NodeView -Id $listNode.Id -Name "List Create" -X $familyInnerX -Y ($familyInnerBaseY + ($i * $familyInnerStepY))))
        for ($j = 0; $j -lt $group.Targets.Count; $j++) {
            $familyNode = $familyNodeByName[$group.Targets[$j]]
            [void]$newConnectors.Add((New-Connector -Start $familyNode.Outputs[0].Id -End $listNode.Inputs[$j].Id))
        }
        $familyMultiGroupNodes[$i] = $listNode
        [void]$familyGroupList.Add($listNode)
    }
    else {
        [void]$familyGroupList.Add($familyNodeByName[$group.Targets[0]])
    }
}

$familyOuterList = New-ListCreateNode -Template $listTemplate -InputCount $familyGroups.Count
[void]$newNodes.Add($familyOuterList)
[void]$newNodeViews.Add((Clone-NodeView -Id $familyOuterList.Id -Name "List Create" -X -520.0 -Y 1200.0))
for ($i = 0; $i -lt $familyGroupList.Count; $i++) {
    $sourceNode = $familyGroupList[$i]
    [void]$newConnectors.Add((New-Connector -Start $sourceNode.Outputs[0].Id -End $familyOuterList.Inputs[$i].Id))
}

$familyParamCode = Build-NestedCode -Groups @($familyGroups | ForEach-Object {
    @($_.ParameterValues | ForEach-Object { $_.Parameter })
})
$familyValueCode = Build-NestedCode -Groups @($familyGroups | ForEach-Object {
    @($_.ParameterValues | ForEach-Object { $_.Value })
})

$familyParamNode = New-CodeBlockNode -Template $codeTemplate -Code $familyParamCode
$familyValueNode = New-CodeBlockNode -Template $codeTemplate -Code $familyValueCode
[void]$newNodes.Add($familyParamNode)
[void]$newNodes.Add($familyValueNode)
[void]$newNodeViews.Add((Clone-NodeView -Id $familyParamNode.Id -Name "Code Block" -X -540.0 -Y -250.0))
[void]$newNodeViews.Add((Clone-NodeView -Id $familyValueNode.Id -Name "Code Block" -X -540.0 -Y 150.0))

$familyElementsNode = New-ElementsNode -Template $familyElementsTemplate
$familySetNode = New-SetParameterNode -Template $setTemplate
[void]$newNodes.Add($familyElementsNode)
[void]$newNodes.Add($familySetNode)
[void]$newNodeViews.Add((Clone-NodeView -Id $familyElementsNode.Id -Name "All Elements of Family Type" -X 40.0 -Y 520.0))
[void]$newNodeViews.Add((Clone-NodeView -Id $familySetNode.Id -Name "Element.SetParameterByName" -X 420.0 -Y 500.0))
[void]$newConnectors.Add((New-Connector -Start $familyOuterList.Outputs[0].Id -End $familyElementsNode.Inputs[0].Id))
[void]$newConnectors.Add((New-Connector -Start $familyElementsNode.Outputs[0].Id -End $familySetNode.Inputs[0].Id))
[void]$newConnectors.Add((New-Connector -Start $familyParamNode.Outputs[0].Id -End $familySetNode.Inputs[1].Id))
[void]$newConnectors.Add((New-Connector -Start $familyValueNode.Outputs[0].Id -End $familySetNode.Inputs[2].Id))

$categoryInnerList = New-ListCreateNode -Template $listTemplate -InputCount 2
[void]$newNodes.Add($categoryInnerList)
[void]$newNodeViews.Add((Clone-NodeView -Id $categoryInnerList.Id -Name "List Create" -X -980.0 -Y 3000.0))
[void]$newConnectors.Add((New-Connector -Start $categoryNodeByName["OST_CableTray"].Outputs[0].Id -End $categoryInnerList.Inputs[0].Id))
[void]$newConnectors.Add((New-Connector -Start $categoryNodeByName["OST_CableTrayFitting"].Outputs[0].Id -End $categoryInnerList.Inputs[1].Id))

$categoryOuterList = New-ListCreateNode -Template $listTemplate -InputCount 2
[void]$newNodes.Add($categoryOuterList)
[void]$newNodeViews.Add((Clone-NodeView -Id $categoryOuterList.Id -Name "List Create" -X -520.0 -Y 3300.0))
[void]$newConnectors.Add((New-Connector -Start $categoryInnerList.Outputs[0].Id -End $categoryOuterList.Inputs[0].Id))
[void]$newConnectors.Add((New-Connector -Start $categoryNodeByName["OST_Conduit"].Outputs[0].Id -End $categoryOuterList.Inputs[1].Id))

$categoryParamCode = Build-NestedCode -Groups @($categoryGroups | ForEach-Object {
    @($_.ParameterValues | ForEach-Object { $_.Parameter })
})
$categoryValueCode = Build-NestedCode -Groups @($categoryGroups | ForEach-Object {
    @($_.ParameterValues | ForEach-Object { $_.Value })
})

$categoryParamNode = New-CodeBlockNode -Template $codeTemplate -Code $categoryParamCode
$categoryValueNode = New-CodeBlockNode -Template $codeTemplate -Code $categoryValueCode
[void]$newNodes.Add($categoryParamNode)
[void]$newNodes.Add($categoryValueNode)
[void]$newNodeViews.Add((Clone-NodeView -Id $categoryParamNode.Id -Name "Code Block" -X -540.0 -Y 2450.0))
[void]$newNodeViews.Add((Clone-NodeView -Id $categoryValueNode.Id -Name "Code Block" -X -540.0 -Y 2850.0))

$categoryElementsNode = New-ElementsNode -Template $categoryElementsTemplate
$categorySetNode = New-SetParameterNode -Template $setTemplate
[void]$newNodes.Add($categoryElementsNode)
[void]$newNodes.Add($categorySetNode)
[void]$newNodeViews.Add((Clone-NodeView -Id $categoryElementsNode.Id -Name "All Elements of Category" -X 40.0 -Y 3200.0))
[void]$newNodeViews.Add((Clone-NodeView -Id $categorySetNode.Id -Name "Element.SetParameterByName" -X 420.0 -Y 3180.0))
[void]$newConnectors.Add((New-Connector -Start $categoryOuterList.Outputs[0].Id -End $categoryElementsNode.Inputs[0].Id))
[void]$newConnectors.Add((New-Connector -Start $categoryElementsNode.Outputs[0].Id -End $categorySetNode.Inputs[0].Id))
[void]$newConnectors.Add((New-Connector -Start $categoryParamNode.Outputs[0].Id -End $categorySetNode.Inputs[1].Id))
[void]$newConnectors.Add((New-Connector -Start $categoryValueNode.Outputs[0].Id -End $categorySetNode.Inputs[2].Id))

$newGraph = $graph.PSObject.Copy()
$newGraph.Name = [System.IO.Path]::GetFileNameWithoutExtension($outputFullPath)
$newGraph.Description = "Simplified graph generated from the repeated family/category parameter blocks."
$newGraph.Nodes = @($newNodes)
$newGraph.Connectors = @($newConnectors)
$newGraph.Bindings = @()
$newGraph.View.NodeViews = @($newNodeViews)
$newGraph.View.Annotations = @()
$newGraph.View.X = -1700.0
$newGraph.View.Y = -1200.0
$newGraph.View.Zoom = 0.55

$jsonText = $newGraph | ConvertTo-Json -Depth 100
[System.IO.File]::WriteAllText($outputFullPath, $jsonText, [System.Text.Encoding]::UTF8)

[pscustomobject]@{
    Source = $sourceFullPath
    Output = $outputFullPath
    NewNodeCount = $newGraph.Nodes.Count
    NewConnectorCount = $newGraph.Connectors.Count
    FamilyGroupCount = $familyGroups.Count
    CategoryGroupCount = $categoryGroups.Count
} | ConvertTo-Json -Depth 4
