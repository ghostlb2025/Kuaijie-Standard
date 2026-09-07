$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$outputDir = Join-Path $projectDir 'dist'
$artifactsDir = Join-Path $projectDir 'artifacts\build'
$assetsDir = Join-Path $projectDir 'assets'
$toolsDir = Join-Path $projectDir 'tools'
$productName = ([char]0x5FEB).ToString() + ([char]0x622A).ToString() + '-' + `
    ([char]0x6807).ToString() + ([char]0x51C6).ToString() + ([char]0x7248).ToString()
$outputFile = Join-Path $outputDir ($productName + '.exe')
$zipFile = Join-Path $outputDir ($productName + '.zip')
$iconBuilder = Join-Path $toolsDir 'IconBuilder.exe'
$iconFile = Join-Path $assetsDir 'Miashot.ico'
$iconPreview = Join-Path $assetsDir 'MiashotIcon-preview.png'

$compilerCandidates = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $compiler) { throw 'Windows C# compiler was not found.' }

$normalizedProject = [System.IO.Path]::GetFullPath($projectDir).TrimEnd('\') + '\'
$normalizedOutput = [System.IO.Path]::GetFullPath($outputDir)
if (-not $normalizedOutput.StartsWith($normalizedProject,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean output outside project: $normalizedOutput"
}
if (Test-Path -LiteralPath $outputDir) {
    Remove-Item -LiteralPath $outputDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $outputDir,$artifactsDir | Out-Null

& $compiler /nologo /target:exe /optimize+ "/out:$iconBuilder" `
    /reference:System.dll /reference:System.Drawing.dll `
    (Join-Path $toolsDir 'IconBuilder.cs')
if ($LASTEXITCODE -ne 0) { throw "Icon build failed with exit code $LASTEXITCODE" }
& $iconBuilder $iconFile $iconPreview
if ($LASTEXITCODE -ne 0) { throw "Icon generation failed with exit code $LASTEXITCODE" }

$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectDir 'src') -Filter '*.cs' |
    Select-Object -ExpandProperty FullName
& $compiler /nologo /target:winexe /optimize+ /platform:x64 /codepage:65001 `
    "/out:$outputFile" "/win32icon:$iconFile" `
    "/win32manifest:$(Join-Path $projectDir 'app.manifest')" `
    /reference:System.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Core.dll $sourceFiles
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

Compress-Archive -LiteralPath $outputFile -DestinationPath $zipFile -Force
Get-Item -LiteralPath $outputFile,$zipFile | Select-Object FullName,Length,LastWriteTime
