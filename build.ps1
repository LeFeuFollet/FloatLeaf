[CmdletBinding()]
param([switch]$Package)

$ErrorActionPreference = 'Stop'
$frameworkRoot = @('Framework64', 'Framework') | ForEach-Object {
    Join-Path $env:WINDIR "Microsoft.NET\$_\v4.0.30319"
} | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'csc.exe') } | Select-Object -First 1

if (-not $frameworkRoot) {
    throw 'Install .NET Framework 4.8 for Windows, then run this script again.'
}

$references = @(
    'System.dll'
    'System.Core.dll'
    'System.Xml.dll'
    'System.Xml.Linq.dll'
    'System.IO.Compression.dll'
    'System.IO.Compression.FileSystem.dll'
    'System.Drawing.dll'
    'System.Windows.Forms.dll'
    'System.Xaml.dll'
    'WPF\WindowsBase.dll'
    'WPF\PresentationCore.dll'
    'WPF\PresentationFramework.dll'
    'WPF\UIAutomationTypes.dll'
    'WPF\UIAutomationProvider.dll'
)

$outputPath = Join-Path $PSScriptRoot 'FloatLeaf.exe'
$sourceDir = Join-Path $PSScriptRoot 'src'
$compilerArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/langversion:5'
    '/codepage:65001'
    "/out:$outputPath"
    "/win32manifest:$(Join-Path $sourceDir 'app.manifest')"
    "/win32icon:$(Join-Path $PSScriptRoot 'assets\FloatLeaf.ico')"
)

foreach ($reference in $references) {
    $assembly = Join-Path $frameworkRoot $reference
    if (-not (Test-Path -LiteralPath $assembly)) {
        throw "Missing .NET Framework component: $assembly"
    }
    $compilerArgs += "/reference:$assembly"
}

$sources = @(Get-ChildItem -LiteralPath $sourceDir -Filter '*.cs' -File | Sort-Object Name)
if (-not $sources) {
    throw 'No C# files were found in src.'
}

$compiler = Join-Path $frameworkRoot 'csc.exe'
$fontDirectory = Join-Path $PSScriptRoot 'assets\fonts'
$resourceFile = [IO.Path]::GetTempFileName()
try {
    $writer = [Resources.ResourceWriter]::new($resourceFile)
    try {
        foreach ($file in (Get-ChildItem -LiteralPath $fontDirectory -File)) {
            if ($file.Extension -notin '.otf', '.ttf', '.txt') { continue }
            $name = 'assets/fonts/' + $file.Name.ToLowerInvariant()
            $writer.AddResource($name, [IO.File]::OpenRead($file.FullName), $true)
        }
    } finally {
        $writer.Dispose()
    }
    $compilerArgs += "/resource:$resourceFile,FloatLeaf.g.resources"
    & $compiler @compilerArgs @($sources.FullName)
    if ($LASTEXITCODE -ne 0) {
        throw "FloatLeaf compilation failed (exit $LASTEXITCODE)."
    }
} finally {
    Remove-Item -LiteralPath $resourceFile
}

Write-Host "Built $outputPath"

if ($Package) {
    $dist = Join-Path $PSScriptRoot 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $archive = Join-Path $dist 'FloatLeaf-Windows.zip'
    $files = @($outputPath) + @(Get-ChildItem -LiteralPath $fontDirectory -Filter 'OFL-*.txt' -File | ForEach-Object FullName)
    $license = Join-Path $PSScriptRoot 'LICENSE'
    if (Test-Path -LiteralPath $license) { $files += $license }
    Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
    Write-Host "Packaged $archive"
}
