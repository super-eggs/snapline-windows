param([switch]$Ocr, [string]$BuildDirectory, [string]$PackageDirectory = 'artifacts/release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$edition = if ($Ocr) { 'ocr' } else { 'standard' }
$prefix = if ($Ocr) { 'Snapline-OCR' } else { 'Snapline' }
if (!$BuildDirectory) { $BuildDirectory = if ($Ocr) { 'artifacts/ocr-build' } else { 'dist' } }
$build = if ([IO.Path]::IsPathRooted($BuildDirectory)) { $BuildDirectory } else { Join-Path $root $BuildDirectory }
$packages = if ([IO.Path]::IsPathRooted($PackageDirectory)) { $PackageDirectory } else { Join-Path $root $PackageDirectory }
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{BBFEDE84-A60E-4F11-9CC5-1EE29BA57425}_is1'
if (Test-Path $uninstallKey) { throw '已有安装，不能使用当前用户执行隔离安装测试。请在干净的 Windows 账户中运行。' }
$testDir = Join-Path $env:TEMP ('Snapline-install-test-' + [guid]::NewGuid().ToString('N'))
$group = 'Snapline'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) ($group + '\Snapline.lnk')
if (Test-Path $shortcut) { throw '已有 Snapline 开始菜单快捷方式，请在干净的 Windows 账户中测试。' }
$appVersion = ([version][Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $build 'Snapline.exe')).FileVersion).ToString(3)
$setup = Join-Path $packages "$prefix-Setup-$appVersion-x64.exe"
$setupVersion = ([version][Diagnostics.FileVersionInfo]::GetVersionInfo($setup).FileVersion).ToString(3)
if ($setupVersion -ne $appVersion) { throw "安装包文件版本与程序版本不一致：$setupVersion / $appVersion" }
try {
    $log = Join-Path $root "artifacts\install-test-$edition.log"
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/NOCLOSEAPPLICATIONS', '/TASKS=""', ('/LOG="' + $log + '"'), ('/DIR="' + $testDir + '"'), ('/GROUP="' + $group + '"'))
    $process = Start-Process $setup -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) { throw '安装失败。' }
    $names = @('Snapline.exe', 'Snapline.exe.config', 'README.md')
    if ($Ocr) { $names += @('recognition\python.exe', 'recognition\recognize.py', 'recognition\protocol-v2') }
    foreach ($name in $names) {
        $installed = (Get-FileHash (Join-Path $testDir $name)).Hash
        $source = (Get-FileHash (Join-Path $build $name)).Hash
        if ($installed -ne $source) { throw "安装文件校验失败：$name" }
    }
    if ((Test-Path (Join-Path $testDir 'recognition')) -ne $Ocr.IsPresent) { throw '安装包内的文字识别组件与版本不符。' }
    if (!(Test-Path $shortcut)) { throw "快捷方式缺失：$shortcut" }
    if (!(Test-Path $uninstallKey)) { throw '卸载注册缺失。' }
    if ((Get-ItemPropertyValue -Path $uninstallKey -Name DisplayVersion) -ne $appVersion) { throw '安装注册版本与程序版本不一致。' }
    $report = Join-Path $root "artifacts\installed-smoke-test-$edition.txt"
    $app = Start-Process (Join-Path $testDir 'Snapline.exe') -ArgumentList @('--smoke-test', ('"' + $report + '"')) -PassThru -WindowStyle Hidden
    if (!$app.WaitForExit(90000)) { Stop-Process -Id $app.Id; throw '安装后启动超时。' }
    Get-Content -LiteralPath $report
    if ($app.ExitCode -ne 0) { throw '安装后窗口检查失败。' }
    Write-Host "PASS 安装包与安装注册版本 $appVersion、安装文件哈希、开始菜单快捷方式、安装后托盘版本与窗口启动"
} finally {
    $uninstaller = Join-Path $testDir 'unins000.exe'
    if (Test-Path $uninstaller) {
        $process = Start-Process $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru -WindowStyle Hidden
        if ($process.ExitCode -ne 0) { throw '测试安装卸载失败。' }
    }
}
if ((Test-Path (Join-Path $testDir 'Snapline.exe')) -or (Test-Path (Join-Path $testDir 'recognition')) -or (Test-Path $uninstallKey) -or (Test-Path $shortcut)) { throw '卸载残留程序或注册信息。' }
Write-Host 'PASS 卸载移除程序、快捷方式和卸载注册'
