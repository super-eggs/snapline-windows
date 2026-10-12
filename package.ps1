param([string]$ISCC)
$ErrorActionPreference = 'Stop'
if (!$ISCC) {
    $candidates = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "${env:ProgramFiles}\Inno Setup 7\ISCC.exe")
    $ISCC = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$ISCC -or !(Test-Path -LiteralPath $ISCC)) { throw '请安装 Inno Setup，或使用 -ISCC 指定编译器路径。' }
$exe = Join-Path $PSScriptRoot 'dist\Snapline.exe'
if (!(Test-Path -LiteralPath $exe)) { throw '请先运行 .\build.ps1 -Test。' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
$appVersion = ([version]$version).ToString(3)
& $ISCC "/DAppVersion=$appVersion" (Join-Path $PSScriptRoot 'installer\Snapline.iss')
if ($LASTEXITCODE -ne 0) { throw '安装包编译失败。' }
$out = Join-Path $PSScriptRoot 'artifacts\release'
$portable = Join-Path $out 'Snapline-Portable-x64.zip'
Compress-Archive -LiteralPath @($exe, (Join-Path $PSScriptRoot 'dist\Snapline.exe.config'), (Join-Path $PSScriptRoot 'README.md')) -DestinationPath $portable -Force
$names = @('Snapline-Setup-x64.exe', 'Snapline-Portable-x64.zip')
$checksums = foreach ($name in $names) {
    $hash = Get-FileHash -LiteralPath (Join-Path $out $name) -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $name
}
$checksums | Set-Content -LiteralPath (Join-Path $out 'SHA256SUMS.txt') -Encoding ascii
Write-Host "已打包 Snapline $appVersion：$out"
