param(
    [string]$GameDirectory = 'E:\SteamLibrary\steamapps\common\Sacred Gold',
    [string]$ScreenshotDirectory = 'C:\Users\Aytac\Pictures\Screenshots\Sacred\Particle Emitters',
    [string]$DotnetPath = 'C:\Users\Aytac\.dotnet\dotnet.exe'
)
$ErrorActionPreference = 'Stop'
$researchRoot = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $PSScriptRoot 'ParticleEmitterDataset.csproj'
& $DotnetPath run --project $projectPath -- $GameDirectory $ScreenshotDirectory $researchRoot
if ($LASTEXITCODE -ne 0) { throw 'Archive export failed.' }
python (Join-Path $PSScriptRoot 'prepare_dataset.py')
if ($LASTEXITCODE -ne 0) { throw 'Observation conversion failed.' }
& $DotnetPath run --no-build --project $projectPath -- $GameDirectory $ScreenshotDirectory $researchRoot --assets
if ($LASTEXITCODE -ne 0) { throw 'Fixture/crop export failed.' }
python (Join-Path $PSScriptRoot 'catalogue_executable.py') $GameDirectory
if ($LASTEXITCODE -ne 0) { throw 'Executable/byte catalogue failed.' }
& $DotnetPath run --no-build --project $projectPath -- $GameDirectory $ScreenshotDirectory $researchRoot --verify
if ($LASTEXITCODE -ne 0) { throw 'Crop verification failed.' }
python (Join-Path $PSScriptRoot 'write_documentation.py')
if ($LASTEXITCODE -ne 0) { throw 'Dataset verification failed.' }
