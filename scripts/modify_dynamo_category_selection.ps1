param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$text = [System.IO.File]::ReadAllText($sourcePath, [System.Text.Encoding]::UTF8)
$json = $text | ConvertFrom-Json

function New-Port {
    param(
        [string]$Id,
        [string]$Name,
        [string]$Description
    )

    [pscustomobject]@{
        Id = $Id
        Name = $Name
        Description = $Description
        UsingDefaultValue = $false
        Level = 2
        UseLevels = $false
        KeepListStructure = $false
    }
}

function New-CodeBlockNode {
    param(
        [string]$NodeId,
        [string]$OutputId,
        [string]$Code
    )

    [pscustomobject]@{
        ConcreteType = 'Dynamo.Graph.Nodes.CodeBlockNodeModel, DynamoCore'
        Id = $NodeId
        NodeType = 'CodeBlockNode'
        Inputs = @()
        Outputs = @(
            (New-Port -Id $OutputId -Name '' -Description 'Value of expression at line 1')
        )
        Replication = 'Disabled'
        Description = 'Allows for DesignScript code to be authored directly'
        Code = $Code
    }
}

function New-Connector {
    param(
        [string]$Start,
        [string]$End,
        [string]$Id
    )

    [pscustomobject]@{
        Start = $Start
        End = $End
        Id = $Id
        IsHidden = 'False'
    }
}

function New-NodeView {
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

$existingCategoryListNodeId = '665c5ac84aa64c4689ed8d6877922d50'
$elementsOfCategoryNodeId = '3681be9c23b3440d8c09178c1bea16d7'
$elementsOfCategoryInputId = 'ca8fdbb5c7904f7aa8dfb6b655cf9d4e'
$existingCategoryListOutputId = '02a382b9552a499c8f539699fd99779e'

# Remove the direct connection so the category list can be filtered first.
$json.Connectors = @(
    $json.Connectors | Where-Object {
        -not ($_.Start -eq $existingCategoryListOutputId -and $_.End -eq $elementsOfCategoryInputId)
    }
)

$boolNodeSpecs = @(
    @{ NodeId = 'b101c1d4f3214b8dbeb0010000000001'; OutputId = 'b101c1d4f3214b8dbeb0010000001001'; X = -80.0; Y = -1008.98579295475; Code = 'true;'; Label = 'CableTrayFitting' },
    @{ NodeId = 'b101c1d4f3214b8dbeb0010000000002'; OutputId = 'b101c1d4f3214b8dbeb0010000001002'; X = -80.0; Y = -830.431203238368; Code = 'true;'; Label = 'CableTray' },
    @{ NodeId = 'b101c1d4f3214b8dbeb0010000000003'; OutputId = 'b101c1d4f3214b8dbeb0010000001003'; X = -80.0; Y = -698.77593167958514; Code = 'true;'; Label = 'LightingFixtures' },
    @{ NodeId = 'b101c1d4f3214b8dbeb0010000000004'; OutputId = 'b101c1d4f3214b8dbeb0010000001004'; X = -80.0; Y = -571.23212003269759; Code = 'true;'; Label = 'ElectricalEquipment' },
    @{ NodeId = 'b101c1d4f3214b8dbeb0010000000005'; OutputId = 'b101c1d4f3214b8dbeb0010000001005'; X = -80.0; Y = -428.63921931915252; Code = 'true;'; Label = 'ElectricalFixtures' },
    @{ NodeId = 'b101c1d4f3214b8dbeb0010000000006'; OutputId = 'b101c1d4f3214b8dbeb0010000001006'; X = -80.0; Y = -289.90881022902249; Code = 'true;'; Label = 'Conduit' }
)

$boolListNode = [pscustomobject]@{
    ConcreteType = 'CoreNodeModels.CreateList, CoreNodeModels'
    VariableInputPorts = $true
    Id = 'b101c1d4f3214b8dbeb0010000000100'
    NodeType = 'ExtensionNode'
    Inputs = @(
        (New-Port -Id 'b101c1d4f3214b8dbeb0010000001100' -Name 'item0' -Description 'Item Index #0'),
        (New-Port -Id 'b101c1d4f3214b8dbeb0010000001101' -Name 'item1' -Description 'Item Index #1'),
        (New-Port -Id 'b101c1d4f3214b8dbeb0010000001102' -Name 'item2' -Description 'Item Index #2'),
        (New-Port -Id 'b101c1d4f3214b8dbeb0010000001103' -Name 'item3' -Description 'Item Index #3'),
        (New-Port -Id 'b101c1d4f3214b8dbeb0010000001104' -Name 'item4' -Description 'Item Index #4'),
        (New-Port -Id 'b101c1d4f3214b8dbeb0010000001105' -Name 'item5' -Description 'Item Index #5')
    )
    Outputs = @(
        (New-Port -Id 'b101c1d4f3214b8dbeb0010000002100' -Name 'list' -Description 'A list')
    )
    Replication = 'Disabled'
    Description = 'Makes a new list from the given inputs'
}

$filterNode = [pscustomobject]@{
    ConcreteType = 'Dynamo.Graph.Nodes.ZeroTouch.DSFunction, DynamoCore'
    Id = 'b101c1d4f3214b8dbeb0010000000200'
    NodeType = 'FunctionNode'
    Inputs = @(
        (New-Port -Id 'b101c1d4f3214b8dbeb0010000001200' -Name 'list' -Description 'The list to filter.`n`nvar[]..[]'),
        (New-Port -Id 'b101c1d4f3214b8dbeb0010000001201' -Name 'mask' -Description 'The boolean mask used to filter the list.`n`nbool[]')
    )
    Outputs = @(
        (New-Port -Id 'b101c1d4f3214b8dbeb0010000002200' -Name 'in' -Description 'Items whose mask value is true'),
        (New-Port -Id 'b101c1d4f3214b8dbeb0010000002201' -Name 'out' -Description 'Items whose mask value is false')
    )
    FunctionSignature = 'DSCore.List.FilterByBoolMask@var[]..[],bool[]'
    Replication = 'Auto'
    Description = 'Filters a list using a list of booleans.'
}

$newNodes = @()
$newNodeViews = @()
$newConnectors = @()

foreach ($spec in $boolNodeSpecs) {
    $newNodes += New-CodeBlockNode -NodeId $spec.NodeId -OutputId $spec.OutputId -Code $spec.Code
    $newNodeViews += New-NodeView -Id $spec.NodeId -Name 'Code Block' -X $spec.X -Y $spec.Y
}

$newNodes += $boolListNode
$newNodes += $filterNode

$newNodeViews += New-NodeView -Id $boolListNode.Id -Name 'List Create' -X 110.0 -Y -1180.0
$newNodeViews += New-NodeView -Id $filterNode.Id -Name 'List.FilterByBoolMask' -X 345.0 -Y -1180.0

$boolInputIds = @(
    'b101c1d4f3214b8dbeb0010000001100',
    'b101c1d4f3214b8dbeb0010000001101',
    'b101c1d4f3214b8dbeb0010000001102',
    'b101c1d4f3214b8dbeb0010000001103',
    'b101c1d4f3214b8dbeb0010000001104',
    'b101c1d4f3214b8dbeb0010000001105'
)

for ($i = 0; $i -lt $boolNodeSpecs.Count; $i++) {
    $newConnectors += New-Connector -Start $boolNodeSpecs[$i].OutputId -End $boolInputIds[$i] -Id ("b101c1d4f3214b8dbeb0010000003{0:d3}" -f $i)
}

$newConnectors += New-Connector -Start $existingCategoryListOutputId -End 'b101c1d4f3214b8dbeb0010000001200' -Id 'b101c1d4f3214b8dbeb0010000003900'
$newConnectors += New-Connector -Start 'b101c1d4f3214b8dbeb0010000002100' -End 'b101c1d4f3214b8dbeb0010000001201' -Id 'b101c1d4f3214b8dbeb0010000003901'
$newConnectors += New-Connector -Start 'b101c1d4f3214b8dbeb0010000002200' -End $elementsOfCategoryInputId -Id 'b101c1d4f3214b8dbeb0010000003902'

$json.Nodes = @($json.Nodes) + $newNodes
$json.Connectors = @($json.Connectors) + $newConnectors
$json.View.NodeViews = @($json.View.NodeViews) + $newNodeViews

$annotation = [pscustomobject]@{
    Id = 'b101c1d4f3214b8dbeb0010000000400'
    Title = '카테고리 선택'
    Nodes = @(
        '5f0e46e45d3246de8bbd4ef6181648d3',
        '03fe5e9c02704b2787c36484c5288e03',
        'e05756c659134f85b42608ad6cb63a1f',
        '7a5f31c74ae14f69ae3628df4ccd2c85',
        '9fb1de1824be412da385b4a76f0423d3',
        '4e1bc5f6670343979b9b764ca3c03872',
        'b101c1d4f3214b8dbeb0010000000001',
        'b101c1d4f3214b8dbeb0010000000002',
        'b101c1d4f3214b8dbeb0010000000003',
        'b101c1d4f3214b8dbeb0010000000004',
        'b101c1d4f3214b8dbeb0010000000005',
        'b101c1d4f3214b8dbeb0010000000006',
        'b101c1d4f3214b8dbeb0010000000100',
        'b101c1d4f3214b8dbeb0010000000200'
    )
}

$json.View.Annotations = @($json.View.Annotations) + @($annotation)

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($outputPath, ($json | ConvertTo-Json -Depth 100), $utf8NoBom)
Write-Output $outputPath
