[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) ('LumaTranslate.NativeTests-' + [Guid]::NewGuid().ToString('N'))),
    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$packageRoot = Split-Path -Parent $PSScriptRoot
$applicationPath = Join-Path $packageRoot 'LumaTranslate.exe'
if ($Rebuild -or -not (Test-Path -LiteralPath $applicationPath -PathType Leaf)) {
    & (Join-Path $packageRoot 'build.ps1')
}

# Test the supplied application assembly. No SDK or reference-assembly package is
# needed unless -Rebuild is requested; csc.exe ships with .NET Framework.
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw 'Windows x64 with .NET Framework 4.8 is required to compile and run these native checks.'
}
$testOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Path $testOutput -Force
$assemblyCopy = Join-Path $testOutput 'LumaTranslate.exe'
if (-not [String]::Equals([System.IO.Path]::GetFullPath($applicationPath), $assemblyCopy, [StringComparison]::OrdinalIgnoreCase)) {
    Copy-Item -LiteralPath $applicationPath -Destination $assemblyCopy -Force
}
$testExecutable = Join-Path $testOutput 'native-tests.exe'
Copy-Item -LiteralPath ($applicationPath + '.config') -Destination ($testExecutable + '.config') -Force
$compilerArguments = @(
    '/nologo', '/target:exe', '/platform:x64', '/langversion:5', '/utf8output',
    ('/out:' + $testExecutable), ('/reference:' + $assemblyCopy),
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll',
    ('/reference:' + (Join-Path (Split-Path -Parent $compilerPath) 'WPF\WindowsBase.dll')),
    ('/reference:' + (Join-Path (Split-Path -Parent $compilerPath) 'WPF\PresentationCore.dll')),
    ('/reference:' + (Join-Path (Split-Path -Parent $compilerPath) 'WPF\PresentationFramework.dll')),
    '/reference:System.Xaml.dll',
    (Join-Path $PSScriptRoot 'native-tests.cs')
)
& $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Native test compilation failed.' }

Write-Host 'Running hidden shortcut registration and generated-image OCR checks...'
Write-Host 'No visible app windows, screen capture, clipboard changes, or network requests are used.'
$testLines = @(& $testExecutable $testOutput (Join-Path $packageRoot 'src\Native.cs') 2>&1)
$testExitCode = $LASTEXITCODE
$testLines | Set-Content -LiteralPath (Join-Path $testOutput 'native-test-results.txt') -Encoding UTF8
$testLines | Write-Output
Write-Host ('Test evidence: ' + $testOutput)
if ($testExitCode -ne 0) { throw "Native tests failed (exit code $testExitCode). See native-test-results.txt." }
