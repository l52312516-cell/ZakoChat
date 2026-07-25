$projDir = Split-Path -Parent $PSCommandPath
$exe = Join-Path $projDir "bin\ZakoChat.exe"
if (-not (Test-Path $exe)) {
    & (Join-Path $projDir "build.ps1")
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Start-Process -FilePath $exe
