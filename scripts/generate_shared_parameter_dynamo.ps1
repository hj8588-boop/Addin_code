param(
    [string]$PythonScriptPath = 'C:\Users\user\Desktop\codex\shared_parameter_to_project_parameter.py',
    [string]$OutputPath = 'C:\Users\user\Desktop\codex\shared_parameter_to_project_parameter.dyn'
)

$pythonCode = [System.IO.File]::ReadAllText($PythonScriptPath, [System.Text.Encoding]::UTF8)

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

$fileNode = [pscustomobject]@{
    ConcreteType = 'CoreNodeModels.Input.Filename, CoreNodeModels'
    Id = 'a1000000000000000000000000000001'
    NodeType = 'ExtensionNode'
    Inputs = @()
    Outputs = @(
        (New-Port -Id 'a1000000000000000000000000001001' -Name '' -Description 'File Path')
    )
    Replication = 'Disabled'
    Description = 'Allows you to select a file on the system and returns its file path'
    HintPath = 'C:\shared-parameters.txt'
    InputValue = '.\shared-parameters.txt'
}

$groupNode = [pscustomobject]@{
    ConcreteType = 'CoreNodeModels.Input.StringInput, CoreNodeModels'
    SerializedWidth = 160.0
    SerializedHeight = 22.0
    Id = 'a1000000000000000000000000000002'
    NodeType = 'StringInputNode'
    Inputs = @()
    Outputs = @(
        (New-Port -Id 'a1000000000000000000000000001002' -Name '' -Description 'String')
    )
    Replication = 'Disabled'
    Description = 'Creates a string.'
    InputValue = 'PG_DATA'
}

$instanceNode = [pscustomobject]@{
    ConcreteType = 'Dynamo.Graph.Nodes.CodeBlockNodeModel, DynamoCore'
    Id = 'a1000000000000000000000000000003'
    NodeType = 'CodeBlockNode'
    Inputs = @()
    Outputs = @(
        (New-Port -Id 'a1000000000000000000000000001003' -Name '' -Description 'Value of expression at line 1')
    )
    Replication = 'Disabled'
    Description = 'Allows for DesignScript code to be authored directly'
    Code = 'true;'
}

$pythonNode = [pscustomobject]@{
    ConcreteType = 'PythonNodeModels.PythonNode, PythonNodeModels'
    Code = $pythonCode
    Engine = 'PythonNet3'
    VariableInputPorts = $true
    Id = 'a1000000000000000000000000000004'
    NodeType = 'PythonScriptNode'
    Inputs = @(
        (New-Port -Id 'a1000000000000000000000000002001' -Name 'IN[0]' -Description 'Input #0'),
        (New-Port -Id 'a1000000000000000000000000002002' -Name 'IN[1]' -Description 'Input #1'),
        (New-Port -Id 'a1000000000000000000000000002003' -Name 'IN[2]' -Description 'Input #2')
    )
    Outputs = @(
        (New-Port -Id 'a1000000000000000000000000003001' -Name 'OUT' -Description 'Result of the python script')
    )
    Replication = 'Disabled'
    Description = 'Runs an embedded Python script.'
}

$graph = [ordered]@{
    Uuid = 'a1000000-0000-0000-0000-000000000001'
    IsCustomNode = $false
    Description = 'Load a shared parameter txt file and bind all definitions as shared project parameters.'
    Name = 'SharedParameterToProjectParameter'
    ElementResolver = @{
        ResolutionMap = @{}
    }
    Inputs = @()
    Outputs = @()
    Nodes = @(
        $fileNode,
        $groupNode,
        $instanceNode,
        $pythonNode
    )
    Connectors = @(
        @{
            Start = 'a1000000000000000000000000001001'
            End = 'a1000000000000000000000000002001'
            Id = 'a1000000000000000000000000004001'
            IsHidden = 'False'
        },
        @{
            Start = 'a1000000000000000000000000001002'
            End = 'a1000000000000000000000000002002'
            Id = 'a1000000000000000000000000004002'
            IsHidden = 'False'
        },
        @{
            Start = 'a1000000000000000000000000001003'
            End = 'a1000000000000000000000000002003'
            Id = 'a1000000000000000000000000004003'
            IsHidden = 'False'
        }
    )
    Dependencies = @()
    NodeLibraryDependencies = @()
    Bindings = @()
    View = @{
        Dynamo = @{
            ScaleFactor = 1.0
            HasRunWithoutCrash = $true
            IsVisibleInDynamoLibrary = $true
            Version = '2.16.1.2727'
            RunType = 'Manual'
            RunPeriod = '1000'
        }
        Camera = @{
            Name = 'Background Preview'
            EyeX = -17.0
            EyeY = 24.0
            EyeZ = 60.0
            LookX = 0.0
            LookY = 0.0
            LookZ = 0.0
            UpX = 0.0
            UpY = 1.0
            UpZ = 0.0
        }
        ConnectorPins = @()
        NodeViews = @(
            (New-NodeView -Id 'a1000000000000000000000000000001' -Name 'File Path' -X 80.0 -Y 140.0),
            (New-NodeView -Id 'a1000000000000000000000000000002' -Name 'String' -X 80.0 -Y 290.0),
            (New-NodeView -Id 'a1000000000000000000000000000003' -Name 'Code Block' -X 80.0 -Y 410.0),
            (New-NodeView -Id 'a1000000000000000000000000000004' -Name 'Python Script' -X 390.0 -Y 160.0)
        )
        Annotations = @(
            @{
                Id = 'a1000000000000000000000000005001'
                Title = 'Inputs'
                Nodes = @(
                    'a1000000000000000000000000000001',
                    'a1000000000000000000000000000002',
                    'a1000000000000000000000000000003'
                )
            },
            @{
                Id = 'a1000000000000000000000000005002'
                Title = 'Shared Project Parameter'
                Nodes = @(
                    'a1000000000000000000000000000004'
                )
            }
        )
        X = 0.0
        Y = 0.0
        Zoom = 1.0
    }
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($OutputPath, ($graph | ConvertTo-Json -Depth 100), $utf8NoBom)
Write-Output $OutputPath
