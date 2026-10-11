param([switch]$Test)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows .NET Framework 4.8 编译器。' }
$destination = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$references = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Runtime.Serialization.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/warn:4',('/out:' + (Join-Path $destination 'Snapline.exe')),('/win32manifest:' + (Join-Path $PSScriptRoot 'src\app.manifest')))
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $framework $reference) }
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\Snapline.exe.config') -Destination $destination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $destination
if ($Test) {
    $report = Join-Path $PSScriptRoot 'artifacts\self-test.txt'
    New-Item -ItemType Directory -Force -Path (Split-Path $report) | Out-Null
    $testProcess = Start-Process -FilePath (Join-Path $destination 'Snapline.exe') -ArgumentList @('--self-test', ('"' + $report + '"')) -WindowStyle Hidden -Wait -PassThru
    Get-Content -LiteralPath $report
    if ($testProcess.ExitCode -ne 0) { throw '自检失败。' }
    $smokeReport = Join-Path $PSScriptRoot 'artifacts\smoke-test.txt'
    $smokeProcess = Start-Process -FilePath (Join-Path $destination 'Snapline.exe') -ArgumentList @('--smoke-test', ('"' + $smokeReport + '"')) -WindowStyle Hidden -PassThru
    if (!$smokeProcess.WaitForExit(15000)) { Stop-Process -Id $smokeProcess.Id; throw '启动检查超时。' }
    Get-Content -LiteralPath $smokeReport
    if ($smokeProcess.ExitCode -ne 0) { throw '启动检查失败。' }
}
Write-Host ('已构建：' + (Join-Path $destination 'Snapline.exe'))
