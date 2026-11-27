param([string]$AppDirectory = 'artifacts/ocr-build')
$ErrorActionPreference = 'Stop'
$stage = if ([IO.Path]::IsPathRooted($AppDirectory)) { $AppDirectory } else { Join-Path $PSScriptRoot $AppDirectory }
$app = Join-Path $stage 'Snapline.exe'
if (!(Test-Path -LiteralPath $app -PathType Leaf) -or [Diagnostics.FileVersionInfo]::GetVersionInfo($app).ProductName -ne 'Snapline OCR') {
    throw '请先使用 .\build.ps1 -Ocr 构建文字识别版。'
}
if (!(Get-Command uv -ErrorAction SilentlyContinue)) { throw '构建识别组件需要 uv。' }
$component = Join-Path $stage 'recognition'
New-Item -ItemType Directory -Force -Path $component | Out-Null

$requirements = Join-Path $PSScriptRoot 'recognition\requirements-ocr.txt'
$pythonVersion = '3.11.15'
$stamp = Join-Path $stage 'recognition-runtime.txt'
$rapidOcr = 'rapidocr==3.10.0'
$runtimeSignature = "$pythonVersion $rapidOcr " + (Get-FileHash -LiteralPath $requirements -Algorithm SHA256).Hash
$python = Join-Path $component 'python.exe'
if (!(Test-Path -LiteralPath $stamp) -or !(Test-Path -LiteralPath $python) -or
    (Get-Content -LiteralPath $stamp -Raw).Trim() -ne $runtimeSignature) {
    uv python install --no-bin $pythonVersion
    if ($LASTEXITCODE -ne 0) { throw '获取 Windows Python 运行时失败。' }
    $runtime = Split-Path (uv python find --managed-python $pythonVersion)
    if ($LASTEXITCODE -ne 0) { throw '找不到组件所需的 Windows Python 运行时。' }
    foreach ($name in @('python.exe', 'python3.dll', 'python311.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'LICENSE.txt', 'DLLs')) {
        Copy-Item -LiteralPath (Join-Path $runtime $name) -Destination $component -Recurse -Force
    }
    $lib = Join-Path $component 'Lib'
    New-Item -ItemType Directory -Force -Path $lib | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $runtime 'Lib') | Where-Object {
        $_.Name -notin @('site-packages', 'test', 'tkinter', 'turtledemo', 'idlelib', 'ensurepip', '__pycache__')
    } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $lib -Recurse -Force }
    uv pip install --index-url https://pypi.org/simple --python $python --target (Join-Path $lib 'site-packages') --upgrade --link-mode copy -r $requirements
    if ($LASTEXITCODE -ne 0) { throw '安装识别组件依赖失败。' }
    Set-Content -LiteralPath $stamp -Value $runtimeSignature -Encoding ascii
}

$worker = Join-Path $component 'recognize.py'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'recognition\recognize.py') -Destination $worker -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'recognition\THIRD-PARTY.md') -Destination $component -Force
Set-Content -LiteralPath (Join-Path $component 'protocol-v2') -Value 'Snapline OCR protocol 2' -Encoding ascii

Write-Host "已准备文字识别版的离线组件：$component"
