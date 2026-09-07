param([switch]$Ui)

$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDir = Join-Path $projectDir 'dist'
$artifactsDir = Join-Path $projectDir 'artifacts\verification'
$toolsDir = Join-Path $projectDir 'tools'
$productName = ([char]0x5FEB).ToString() + ([char]0x622A).ToString() + '-' + `
    ([char]0x6807).ToString() + ([char]0x51C6).ToString() + ([char]0x7248).ToString()
$exeName = $productName + '.exe'
$zipName = $productName + '.zip'
$exePath = Join-Path $distDir $exeName
$zipPath = Join-Path $distDir $zipName

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $projectDir 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }

$resultPath = Join-Path $artifactsDir 'self-test.txt'
$process = Start-Process -FilePath $exePath `
    -ArgumentList @('--self-test', ('"' + $resultPath + '"')) `
    -WorkingDirectory $distDir -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Clipboard self-test failed with exit code $($process.ExitCode)." }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    if ($entries.Count -ne 1 -or $entries[0] -ne $exeName) {
        throw 'ZIP must contain exactly the standard executable.'
    }
} finally { $archive.Dispose() }

$distEntries = @(Get-ChildItem -LiteralPath $distDir -Force)
if ($distEntries.Count -ne 2 -or @($distEntries | Where-Object { $_.PSIsContainer }).Count -ne 0) {
    throw 'dist must contain exactly one executable and one ZIP file.'
}

if ($Ui) {
    $compilerCandidates = @(
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
        (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
    )
    $compiler = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
    if (-not $compiler) { throw 'Windows C# compiler was not found.' }
    $harness = Join-Path $toolsDir 'UiSmokeTest.exe'
    & $compiler /nologo /target:exe /optimize+ /codepage:65001 `
        "/out:$harness" /reference:System.dll /reference:System.Drawing.dll `
        /reference:System.Windows.Forms.dll /reference:System.Core.dll `
        (Join-Path $projectDir 'tests\UiSmokeTest.cs')
    if ($LASTEXITCODE -ne 0) { throw 'UI smoke-test compilation failed.' }
    $uiResult = Join-Path $artifactsDir 'ui-test.txt'
    $uiProcess = Start-Process -FilePath $harness `
        -ArgumentList @(('"' + $exePath + '"'), ('"' + $uiResult + '"')) `
        -WorkingDirectory $projectDir -Wait -PassThru
    if ($uiProcess.ExitCode -ne 0) { throw 'Interactive UI test failed.' }
}

Write-Host 'All standard edition checks passed.'
