param(
    [string]$ISCC,
    [string]$BuildDirectory = 'dist',
    [string]$OutputDirectory = 'artifacts\release',
    [string]$OcrBuildDirectory
)
$ErrorActionPreference = 'Stop'
if (!$ISCC) {
    $candidates = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "${env:ProgramFiles}\Inno Setup 7\ISCC.exe")
    $ISCC = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$ISCC -or !(Test-Path -LiteralPath $ISCC)) { throw '请安装 Inno Setup，或使用 -ISCC 指定编译器路径。' }
function Resolve-ProjectPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return $Path }
    return Join-Path $PSScriptRoot $Path
}

$build = Resolve-ProjectPath $BuildDirectory
$out = Resolve-ProjectPath $OutputDirectory
New-Item -ItemType Directory -Force -Path $out | Out-Null
$exe = Join-Path $build 'Snapline.exe'
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw '请先运行 .\build.ps1 -Test。' }
$standard = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
if ($standard.ProductName -ne 'Snapline') { throw '纯截图版需要使用 .\build.ps1 构建。' }
$appVersion = ([version]$standard.FileVersion).ToString(3)
$editions = @(@{ Build = $build; Prefix = 'Snapline'; Ocr = $false })
if ($OcrBuildDirectory) {
    $ocrBuild = Resolve-ProjectPath $OcrBuildDirectory
    $ocrExe = Join-Path $ocrBuild 'Snapline.exe'
    if (!(Test-Path -LiteralPath $ocrExe -PathType Leaf)) { throw '请先运行 .\build.ps1 -Ocr。' }
    $ocr = [Diagnostics.FileVersionInfo]::GetVersionInfo($ocrExe)
    if ($ocr.ProductName -ne 'Snapline OCR' -or $ocr.FileVersion -ne $standard.FileVersion) {
        throw '请构建同版本的文字识别版。'
    }
    foreach ($name in @('recognition\python.exe', 'recognition\recognize.py', 'recognition\protocol-v2')) {
        if (!(Test-Path -LiteralPath (Join-Path $ocrBuild $name) -PathType Leaf)) { throw '请先运行 .\build-recognition.ps1。' }
    }
    $editions += @{ Build = $ocrBuild; Prefix = 'Snapline-OCR'; Ocr = $true }
}

$names = foreach ($edition in $editions) {
    $compilerArguments = @('/Q', "/DAppVersion=$appVersion", "/DSnaplineBuildDir=$($edition.Build)", "/O$out")
    $portableFiles = @((Join-Path $edition.Build 'Snapline.exe'), (Join-Path $edition.Build 'Snapline.exe.config'), (Join-Path $PSScriptRoot 'README.md'))
    if ($edition.Ocr) {
        $compilerArguments += '/DSnaplineOcr=1'
        $portableFiles += Join-Path $edition.Build 'recognition'
    }
    & $ISCC @compilerArguments (Join-Path $PSScriptRoot 'installer\Snapline.iss') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw '安装包编译失败。' }
    $portableName = "$($edition.Prefix)-Portable-$appVersion-x64.zip"
    Compress-Archive -LiteralPath $portableFiles -DestinationPath (Join-Path $out $portableName) -Force
    "$($edition.Prefix)-Setup-$appVersion-x64.exe"
    $portableName
}
$checksums = foreach ($name in $names) {
    $hash = Get-FileHash -LiteralPath (Join-Path $out $name) -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $name
}
$checksums | Set-Content -LiteralPath (Join-Path $out 'SHA256SUMS.txt') -Encoding ascii
Write-Host "已打包 Snapline $appVersion：$out"
