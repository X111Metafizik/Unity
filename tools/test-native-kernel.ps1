param([string]$NewtonsoftPath)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
if(-not $NewtonsoftPath){
    $package=Get-ChildItem -Path (Join-Path $projectRoot 'Library/PackageCache/com.unity.nuget.newtonsoft-json@*') -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    if(-not $package){throw 'Import this project in Unity first, or pass -NewtonsoftPath with the Unity Newtonsoft.Json.dll location.'}
    $NewtonsoftPath=Join-Path $package.FullName 'Runtime/Newtonsoft.Json.dll'
}
dotnet run --project (Join-Path $PSScriptRoot 'terrain-parity/terrain-parity.csproj') --configuration Release "-p:NewtonsoftPath=$NewtonsoftPath" '-p:UseSharedCompilation=false' -- $projectRoot
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
node (Join-Path $PSScriptRoot 'verify-port.mjs')
exit $LASTEXITCODE
