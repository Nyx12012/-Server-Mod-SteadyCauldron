$ErrorActionPreference = 'Stop'

$game = 'C:\Games\Alta\A Township Tale'
$mgd  = Join-Path $game 'A Township Tale_Data\Managed'
$ml   = Join-Path $game 'MelonLoader\net35'
$csc  = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = Join-Path $here 'build'
$dll  = Join-Path $out 'SteadyCauldron.dll'

New-Item -ItemType Directory -Force $out | Out-Null
$refs = @(Get-ChildItem (Join-Path $mgd '*.dll') | ForEach-Object { "/r:$($_.FullName)" })
$refs += "/r:$(Join-Path $ml 'MelonLoader.dll')"
$refs += "/r:$(Join-Path $ml '0Harmony.dll')"

& $csc /target:library /nostdlib+ /noconfig /unsafe- /warn:1 "/out:$dll" $refs (Join-Path $here 'SteadyCauldron.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
Get-Item $dll | Select-Object Name, Length, LastWriteTime
