param([switch]$Test, [switch]$NativeTests)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
$env:APPDATA = Join-Path $PSScriptRoot '.tools\appdata'
$env:LOCALAPPDATA = Join-Path $PSScriptRoot '.tools\localappdata'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_NOLOGO = 'true'
New-Item -ItemType Directory -Force -Path $env:APPDATA,$env:LOCALAPPDATA | Out-Null

$sdk = Join-Path $PSScriptRoot '.tools\dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) {
    $installed = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($installed -and (& $installed.Source --list-sdks | Where-Object { $_ -match '^10\.' })) {
        $sdk = $installed.Source
    } else {
        throw 'Install the .NET 10 SDK or run .\setup-build-tools.ps1 to download a verified local SDK.'
    }
}

if ($Test -or $NativeTests) {
    & $sdk restore tests\WindowSnap.Tests\WindowSnap.Tests.csproj --configfile NuGet.Config --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Test restore failed.' }
    $arguments = @()
    if ($NativeTests) { $arguments += '--native' }
    & $sdk run --project tests\WindowSnap.Tests\WindowSnap.Tests.csproj -c Release --no-restore -- @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
& $sdk restore src\WindowSnap\WindowSnap.csproj --configfile NuGet.Config --nologo
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
& $sdk publish src\WindowSnap\WindowSnap.csproj -c Release --self-contained false --no-restore -p:DebugType=None -p:DebugSymbols=false -o dist --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$files = @('WindowSnap.exe', 'WindowSnap.dll', 'WindowSnap.deps.json', 'WindowSnap.runtimeconfig.json') | ForEach-Object { Join-Path $PSScriptRoot "dist\$_" }
Compress-Archive -LiteralPath $files -DestinationPath (Join-Path $PSScriptRoot 'dist\WindowSnap-portable.zip') -Force
Write-Output "Ready: $PSScriptRoot\dist\WindowSnap.exe"
