$ErrorActionPreference = 'Stop'
$dotnet = 'C:\Users\Aytac\.dotnet\dotnet.exe'
& $dotnet build Sacred.World.Renderer.Terminal -v quiet
if ($LASTEXITCODE) { throw 'Build failed' }
$scenes = @(@('waldburg',1715,3397),@('cemetery',3050,2725),@('tower',3378,2522),@('bellevue',3425,2583),@('dungeon',4555,990))
foreach ($scene in $scenes) {
  foreach ($state in @('closed','open')) {
    $arguments = @('run','--no-build','--project','Sacred.World.Renderer.Terminal','--','--output',"_scratch/doors-$($scene[0])-$state",'--world-x',"$($scene[1])",'--world-y',"$($scene[2])",'--width','1600','--height','900','--zoom','0.5')
    if ($state -eq 'open') { $arguments += '--open-doors' }
    & $dotnet @arguments > "_scratch/doors-$($scene[0])-$state.log"
    if ($LASTEXITCODE) { throw "Render failed: $scene $state" }
    Write-Output "Rendered $($scene[0]) $state"
  }
}
