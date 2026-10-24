$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$toolsDirectory = Join-Path $PSScriptRoot '.tools'
New-Item -ItemType Directory -Force -Path $toolsDirectory | Out-Null
$archive = Join-Path $toolsDirectory 'dotnet-sdk.zip'
$expected = 'ae4fd528cb7e9f6af6aaf7de36715a69f761f183744f1bb3025d19f5a92447090ec561410e46266c1d10a63781ed6f3de771760bc76c876d87daabc367b898cd'
if (!(Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $expected) {
    Invoke-WebRequest -Uri 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.402/dotnet-sdk-10.0.402-win-x64.zip' -OutFile $archive -UseBasicParsing
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $expected) { throw 'Microsoft SDK checksum did not match.' }
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $toolsDirectory 'dotnet') -Force
Write-Output 'Build tools are ready inside .tools. No system installation was changed.'
