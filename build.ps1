[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Run this script from any directory. No package restore, administrator rights,
# network access, Visual Studio, or .NET SDK are needed on the supplied machine.
$packageRoot = $PSScriptRoot
$sourceRoot = Join-Path $packageRoot 'src'
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkRoot 'csc.exe'
$programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
$referenceRoot = Join-Path $programFilesX86 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
$metadataRoot = Join-Path $programFilesX86 'Windows Kits\10\UnionMetadata'

if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw 'The 64-bit .NET Framework C# compiler was not found. Windows 10/11 with .NET Framework 4.8 is required.'
}
if (-not (Test-Path -LiteralPath $referenceRoot -PathType Container)) {
    throw '.NET Framework 4.8 reference assemblies were not found. Install the .NET Framework 4.8 Developer Pack to build; the supplied EXE does not need it.'
}
if (-not (Test-Path -LiteralPath $metadataRoot -PathType Container)) {
    throw 'Windows 10 SDK UnionMetadata was not found. The SDK is required to compile Windows OCR; the supplied EXE does not need it.'
}

$sdkVersions = @(Get-ChildItem -LiteralPath $metadataRoot -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending)
$windowsMetadata = $null
foreach ($sdkVersion in $sdkVersions) {
    $candidatePath = Join-Path $sdkVersion.FullName 'Windows.winmd'
    if (Test-Path -LiteralPath $candidatePath -PathType Leaf) {
        $windowsMetadata = $candidatePath
        break
    }
}
if (-not $windowsMetadata) {
    throw 'No versioned Windows SDK Windows.winmd file was found under UnionMetadata.'
}

$referenceNames = @(
    'mscorlib.dll',
    'System.dll',
    'System.Core.dll',
    'System.Configuration.dll',
    'System.Drawing.dll',
    'System.Net.Http.dll',
    'System.Runtime.Serialization.dll',
    'System.Web.Extensions.dll',
    'System.Windows.Forms.dll',
    'System.Xaml.dll',
    'System.Xml.dll',
    'System.Xml.Linq.dll',
    'WindowsBase.dll',
    'PresentationCore.dll',
    'PresentationFramework.dll',
    'UIAutomationClient.dll',
    'UIAutomationTypes.dll',
    'Facades\System.Runtime.dll',
    'Facades\System.Runtime.InteropServices.WindowsRuntime.dll',
    'Facades\System.ObjectModel.dll',
    'Facades\System.Collections.dll',
    'Facades\System.Threading.Tasks.dll'
)
$references = @($referenceNames | ForEach-Object { Join-Path $referenceRoot $_ })
$references += Join-Path $frameworkRoot 'System.Runtime.WindowsRuntime.dll'
$references += $windowsMetadata
foreach ($referencePath in $references) {
    if (-not (Test-Path -LiteralPath $referencePath -PathType Leaf)) {
        throw "Required compiler reference was not found: $referencePath"
    }
}

$sourceFiles = @(Get-ChildItem -LiteralPath $sourceRoot -Filter '*.cs' -File |
    Sort-Object Name | Select-Object -ExpandProperty FullName)
if ($sourceFiles.Count -eq 0) { throw "No C# source files found in $sourceRoot" }

$outputPath = Join-Path $packageRoot 'LumaTranslate.exe'
$compilerArguments = @(
    '/nologo', '/noconfig', '/nostdlib+', '/target:winexe', '/platform:x64',
    '/langversion:5', '/utf8output', '/warn:4',
    ('/out:' + $outputPath),
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')),
    ('/win32icon:' + (Join-Path $packageRoot 'assets\luma.ico')),
    ('/resource:' + (Join-Path $packageRoot 'assets\luma.ico') + ',LumaTranslate.luma.ico')
)
if ($Configuration -eq 'Debug') {
    $compilerArguments += @('/debug:full', '/optimize-', '/define:DEBUG;TRACE')
} else {
    $compilerArguments += @('/debug:pdbonly', '/optimize+', '/define:TRACE')
}
$compilerArguments += @($references | ForEach-Object { '/reference:' + $_ })
$compilerArguments += $sourceFiles

Write-Host 'Building Luma Translate for Windows x64...'
& $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed (exit code $LASTEXITCODE)." }
if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
    throw 'The compiler returned without producing LumaTranslate.exe.'
}
Copy-Item -LiteralPath (Join-Path $sourceRoot 'LumaTranslate.exe.config') -Destination ($outputPath + '.config') -Force
Write-Host "Build succeeded: $outputPath"
Write-Host 'Launch LumaTranslate.exe to open the desktop app. Closing its window keeps it in the notification area.'
