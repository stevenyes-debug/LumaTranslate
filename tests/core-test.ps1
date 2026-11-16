param(
    [string]$OutputDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) ('LumaTranslate.Tests-' + [Guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework 4.8 is required to compile and run these tests.'
}

$testOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $testOutput -Force
$executable = Join-Path $testOutput 'core-tests.exe'
$source = Join-Path $PSScriptRoot '..\src\Services.cs'
$tests = Join-Path $PSScriptRoot 'core-tests.cs'
$compilerArguments = @('/nologo', '/target:exe', ('/out:' + $executable), '/r:System.Net.Http.dll', '/r:System.Web.Extensions.dll', $source, $tests)
& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Core test compilation failed.' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
Write-Host ('Test evidence: ' + $testOutput)
