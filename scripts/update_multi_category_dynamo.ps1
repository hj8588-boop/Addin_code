param(
    [Parameter(Mandatory = $true)]
    [string]$DynPath,

    [Parameter(Mandatory = $true)]
    [string]$PythonPath
)

$utf8 = [System.Text.Encoding]::UTF8
$dynText = [System.IO.File]::ReadAllText($DynPath, $utf8)
$json = $dynText | ConvertFrom-Json
$pythonCode = [System.IO.File]::ReadAllText($PythonPath, $utf8)

$json.Description = "Load a shared parameter txt file and bind all definitions as shared project parameters for selected Revit categories."

$pythonNode = $json.Nodes | Where-Object Id -eq 'a1000000000000000000000000000004'
$pythonNode.Code = $pythonCode

if (($pythonNode.Inputs | Where-Object Id -eq 'a1000000000000000000000000002004').Count -eq 0) {
    $pythonNode.Inputs += [pscustomobject]@{
        Id = 'a1000000000000000000000000002004'
        Name = 'IN[3]'
        Description = 'Input #3'
        UsingDefaultValue = $false
        Level = 2
        UseLevels = $false
        KeepListStructure = $false
    }
}

$newCategoryNodes = @(
    @{ Id = 'a1000000000000000000000000000005'; Output = 'a1000000000000000000000000001005'; Selected = 'OST_Walls'; Index = 0; X = 80; Y = 530 },
    @{ Id = 'a1000000000000000000000000000006'; Output = 'a1000000000000000000000000001006'; Selected = 'OST_Floors'; Index = 0; X = 80; Y = 650 },
    @{ Id = 'a1000000000000000000000000000007'; Output = 'a1000000000000000000000000001007'; Selected = 'OST_Doors'; Index = 0; X = 80; Y = 770 },
    @{ Id = 'a1000000000000000000000000000008'; Output = 'a1000000000000000000000000001008'; Selected = 'OST_Windows'; Index = 0; X = 80; Y = 890 },
    @{ Id = 'a1000000000000000000000000000009'; Output = 'a1000000000000000000000000001009'; Selected = 'OST_GenericModel'; Index = 0; X = 80; Y = 1010 }
)

$existingNodeIds = @($json.Nodes | ForEach-Object { $_.Id })

foreach ($spec in $newCategoryNodes) {
    if ($existingNodeIds -contains $spec.Id) {
        continue
    }

    $json.Nodes += [pscustomobject]@{
        ConcreteType = 'DSRevitNodesUI.Categories, DSRevitNodesUI'
        SelectedIndex = $spec.Index
        SelectedString = $spec.Selected
        Id = $spec.Id
        NodeType = 'ExtensionNode'
        Inputs = @()
        Outputs = @(
            [pscustomobject]@{
                Id = $spec.Output
                Name = 'Category'
                Description = 'The selected Category.'
                UsingDefaultValue = $false
                Level = 2
                UseLevels = $false
                KeepListStructure = $false
            }
        )
        Replication = 'Disabled'
        Description = 'All built-in categories.'
    }
}

if (($json.Nodes | Where-Object Id -eq 'a1000000000000000000000000000010').Count -eq 0) {
    $json.Nodes += [pscustomobject]@{
        ConcreteType = 'CoreNodeModels.CreateList, CoreNodeModels'
        VariableInputPorts = $true
        Id = 'a1000000000000000000000000000010'
        NodeType = 'ExtensionNode'
        Inputs = @(
            [pscustomobject]@{ Id='a1000000000000000000000000002010'; Name='item0'; Description='Item Index #0'; UsingDefaultValue=$false; Level=2; UseLevels=$false; KeepListStructure=$false },
            [pscustomobject]@{ Id='a1000000000000000000000000002011'; Name='item1'; Description='Item Index #1'; UsingDefaultValue=$false; Level=2; UseLevels=$false; KeepListStructure=$false },
            [pscustomobject]@{ Id='a1000000000000000000000000002012'; Name='item2'; Description='Item Index #2'; UsingDefaultValue=$false; Level=2; UseLevels=$false; KeepListStructure=$false },
            [pscustomobject]@{ Id='a1000000000000000000000000002013'; Name='item3'; Description='Item Index #3'; UsingDefaultValue=$false; Level=2; UseLevels=$false; KeepListStructure=$false },
            [pscustomobject]@{ Id='a1000000000000000000000000002014'; Name='item4'; Description='Item Index #4'; UsingDefaultValue=$false; Level=2; UseLevels=$false; KeepListStructure=$false }
        )
        Outputs = @(
            [pscustomobject]@{
                Id = 'a1000000000000000000000000003010'
                Name = 'list'
                Description = 'A list'
                UsingDefaultValue = $false
                Level = 2
                UseLevels = $false
                KeepListStructure = $false
            }
        )
        Replication = 'Disabled'
        Description = 'Makes a new list from the given inputs'
    }
}

$json.Connectors = @($json.Connectors | Where-Object { $_.Id -ne 'a1000000000000000000000000004004' })

$connectorSpecs = @(
    @{ Id='a1000000000000000000000000004010'; Start='a1000000000000000000000000001005'; End='a1000000000000000000000000002010' },
    @{ Id='a1000000000000000000000000004011'; Start='a1000000000000000000000000001006'; End='a1000000000000000000000000002011' },
    @{ Id='a1000000000000000000000000004012'; Start='a1000000000000000000000000001007'; End='a1000000000000000000000000002012' },
    @{ Id='a1000000000000000000000000004013'; Start='a1000000000000000000000000001008'; End='a1000000000000000000000000002013' },
    @{ Id='a1000000000000000000000000004014'; Start='a1000000000000000000000000001009'; End='a1000000000000000000000000002014' },
    @{ Id='a1000000000000000000000000004015'; Start='a1000000000000000000000000003010'; End='a1000000000000000000000000002004' }
)

$existingConnectorIds = @($json.Connectors | ForEach-Object { $_.Id })
foreach ($spec in $connectorSpecs) {
    if ($existingConnectorIds -contains $spec.Id) {
        continue
    }

    $json.Connectors += [pscustomobject]@{
        IsHidden = 'False'
        End = $spec.End
        Id = $spec.Id
        Start = $spec.Start
    }
}

$nodeViews = $json.View.NodeViews

function Ensure-NodeView {
    param(
        [string]$Id,
        [string]$Name,
        [double]$X,
        [double]$Y
    )

    $existing = $nodeViews | Where-Object Id -eq $Id
    if ($existing) {
        $existing.X = $X
        $existing.Y = $Y
        return
    }

    $json.View.NodeViews += [pscustomobject]@{
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

Ensure-NodeView -Id 'a1000000000000000000000000000001' -Name 'File Path' -X 80 -Y 140
Ensure-NodeView -Id 'a1000000000000000000000000000002' -Name 'String' -X 80 -Y 290
Ensure-NodeView -Id 'a1000000000000000000000000000003' -Name 'Code Block' -X 80 -Y 410
Ensure-NodeView -Id 'a1000000000000000000000000000005' -Name 'Categories' -X 80 -Y 530
Ensure-NodeView -Id 'a1000000000000000000000000000006' -Name 'Categories' -X 80 -Y 650
Ensure-NodeView -Id 'a1000000000000000000000000000007' -Name 'Categories' -X 80 -Y 770
Ensure-NodeView -Id 'a1000000000000000000000000000008' -Name 'Categories' -X 80 -Y 890
Ensure-NodeView -Id 'a1000000000000000000000000000009' -Name 'Categories' -X 80 -Y 1010
Ensure-NodeView -Id 'a1000000000000000000000000000010' -Name 'List Create' -X 300 -Y 760
Ensure-NodeView -Id 'a1000000000000000000000000000004' -Name 'Python Script' -X 540 -Y 380

$inputsAnnotation = $json.View.Annotations | Where-Object Id -eq 'a1000000000000000000000000005001'
if ($inputsAnnotation) {
    $inputsAnnotation.Nodes = @(
        'a1000000000000000000000000000001',
        'a1000000000000000000000000000002',
        'a1000000000000000000000000000003',
        'a1000000000000000000000000000005',
        'a1000000000000000000000000000006',
        'a1000000000000000000000000000007',
        'a1000000000000000000000000000008',
        'a1000000000000000000000000000009',
        'a1000000000000000000000000000010'
    )
}

$outText = $json | ConvertTo-Json -Depth 100
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($DynPath, $outText, $utf8NoBom)
