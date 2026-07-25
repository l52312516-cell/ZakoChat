$ErrorActionPreference = "Stop"

$projDir = Split-Path -Parent $PSCommandPath
$outDir = Join-Path $projDir "bin"
$tmpDir = $outDir
$releaseDir = Join-Path $projDir "release"
$version = "1.0.0"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$sources = @(Get-ChildItem -Path $projDir -Filter "*.cs" | ForEach-Object { $_.FullName })
$manifest = Join-Path $projDir "ZakoChat.exe.manifest"
$icon = Join-Path $projDir "ico\ZakoChat.ico"
$webView2Dir = Join-Path $projDir "packages\Microsoft.Web.WebView2.1.0.2849.39"
$webView2Core = Join-Path $webView2Dir "lib\net462\Microsoft.Web.WebView2.Core.dll"
$webView2WinForms = Join-Path $webView2Dir "lib\net462\Microsoft.Web.WebView2.WinForms.dll"
$webView2Loader = Join-Path $webView2Dir "runtimes\win-x64\native\WebView2Loader.dll"
$out = Join-Path $tmpDir "ZakoChat.build.exe"
$release = Join-Path $releaseDir "ZakoChat-V$version.exe"

if (-not (Test-Path $csc)) {
    Write-Host "Error: C# compiler not found: $csc" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $icon)) {
    Write-Host "Error: app icon not found: $icon" -ForegroundColor Red
    exit 1
}

foreach ($webView2File in @($webView2Core, $webView2WinForms, $webView2Loader)) {
    if (-not (Test-Path $webView2File)) {
        Write-Host "Error: WebView2 dependency not found: $webView2File" -ForegroundColor Red
        exit 1
    }
}

Add-Type -AssemblyName System.Drawing
try {
    $checkIcon = New-Object System.Drawing.Icon($icon)
    $checkIcon.Dispose()
    Write-Host "Using icon: $icon" -ForegroundColor Cyan
} catch {
    Write-Host "Error: invalid icon: $icon" -ForegroundColor Red
    exit 1
}

Write-Host "Building Zako Chat $version..." -ForegroundColor Cyan
New-Item -Path $tmpDir -ItemType Directory -Force | Out-Null
New-Item -Path $outDir -ItemType Directory -Force | Out-Null
New-Item -Path $releaseDir -ItemType Directory -Force | Out-Null

& $csc /noconfig /codepage:65001 /optimize+ /win32manifest:$manifest /win32icon:$icon /target:winexe /out:$out /reference:System.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:$webView2Core /reference:$webView2WinForms /resource:$webView2Core,ZakoChat.Resources.Microsoft.Web.WebView2.Core.dll /resource:$webView2WinForms,ZakoChat.Resources.Microsoft.Web.WebView2.WinForms.dll /resource:$webView2Loader,ZakoChat.Resources.WebView2Loader.dll $sources

if ($LASTEXITCODE -eq 0) {
    Copy-Item -Path $out -Destination (Join-Path $outDir "ZakoChat.exe") -Force
    Copy-Item -Path $out -Destination $release -Force
    $resolvedReleaseDir = [System.IO.Path]::GetFullPath($releaseDir)
    $resolvedRelease = [System.IO.Path]::GetFullPath($release)
    Get-ChildItem -LiteralPath $resolvedReleaseDir -Force | Where-Object {
        [System.IO.Path]::GetFullPath($_.FullName) -ne $resolvedRelease
    } | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
    Remove-Item -Path $out -Force -ErrorAction SilentlyContinue
    Write-Host "Build succeeded: " -ForegroundColor Green -NoNewline
    Write-Host (Join-Path $outDir "ZakoChat.exe") -ForegroundColor White
    Write-Host "Release: " -ForegroundColor Green -NoNewline
    Write-Host $release -ForegroundColor White
} else {
    Write-Host "Build failed, exit code: $LASTEXITCODE" -ForegroundColor Red
    exit 1
}
