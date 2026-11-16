[CmdletBinding()]
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$packageRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) { & (Join-Path $packageRoot 'build.ps1') }

$executablePath = Join-Path $packageRoot 'LumaTranslate.exe'
$bytes = [IO.File]::ReadAllBytes($executablePath)
if ($bytes.Length -lt 4096) { throw 'Executable is missing or unexpectedly small.' }
if ($bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw 'Missing DOS executable header.' }
$peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
if ([BitConverter]::ToUInt32($bytes, $peOffset) -ne 0x00004550) { throw 'Missing PE signature.' }
if ([BitConverter]::ToUInt16($bytes, $peOffset + 4) -ne 0x8664) { throw 'Executable must target x64.' }
$optionalHeaderOffset = $peOffset + 24
if ([BitConverter]::ToUInt16($bytes, $optionalHeaderOffset) -ne 0x20B) { throw 'Expected a PE32+ image.' }
if ([BitConverter]::ToUInt16($bytes, $optionalHeaderOffset + 68) -ne 2) { throw 'Executable must use the Windows GUI subsystem.' }

$asciiImage = [Text.Encoding]::UTF8.GetString($bytes)
if (-not $asciiImage.Contains('PerMonitorV2')) { throw 'Embedded per-monitor DPI manifest is missing.' }
if (-not $asciiImage.Contains('requestedExecutionLevel')) { throw 'Embedded privilege manifest is missing.' }

[xml]$config = Get-Content -LiteralPath ($executablePath + '.config') -Raw
if ($config.configuration.startup.supportedRuntime.sku -ne '.NETFramework,Version=v4.8') {
    throw 'The application runtime config does not target .NET Framework 4.8.'
}
$assemblyName = [Reflection.AssemblyName]::GetAssemblyName($executablePath)
if ($assemblyName.Name -ne 'LumaTranslate') { throw 'Unexpected managed assembly identity.' }

$iconBytes = [IO.File]::ReadAllBytes((Join-Path $packageRoot 'assets\luma.ico'))
if ([BitConverter]::ToUInt16($iconBytes, 2) -ne 1 -or [BitConverter]::ToUInt16($iconBytes, 4) -lt 4) {
    throw 'The app icon must contain multiple sizes for Windows taskbar and notification area.'
}

Write-Host 'PASS: x64 managed Windows GUI executable, embedded DPI and privilege manifests, .NET 4.8 config, and multi-resolution icon.'
Write-Host 'This check does not open the app or exercise live keyboard, screen capture, OCR, network, or notification-area behavior.'
