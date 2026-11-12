$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet test .\tests\Yifan.Core.Tests\Yifan.Core.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet publish .\src\Yifan.App\Yifan.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:Platform=x64 `
  -p:WindowsPackageType=None `
  -p:WindowsAppSDKSelfContained=true `
  -p:PublishReadyToRun=false
if ($LASTEXITCODE -ne 0) {
  $obj = Join-Path $root "src\Yifan.App\obj"
  if (Test-Path $obj) {
    $outputs = @(Get-ChildItem -Path $obj -Recurse -Filter output.json -ErrorAction SilentlyContinue)
    Write-Host "output.json count: $($outputs.Count)"
    foreach ($file in $outputs) {
      Write-Host "OUTPUT size=$($file.Length) $($file.FullName)"
      try {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        $found = [regex]::Matches($text, '"(?:ErrorCode|ErrorMessage|Message|File|LineNumber)"\s*:\s*"(?:[^"\\]|\\.){0,500}"')
        if ($found.Count -eq 0) {
          $slice = if ($text.Length -gt 2000) { $text.Substring(0, 2000) } else { $text }
          Write-Host $slice
        } else {
          $found | Select-Object -First 60 | ForEach-Object { Write-Host $_.Value }
        }
      } catch {
        Write-Host $_.Exception.Message
      }
    }
  }
  exit $LASTEXITCODE
}

$publishCandidates = @(
  (Join-Path $root "src\Yifan.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"),
  (Join-Path $root "src\Yifan.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish")
)
$publish = $publishCandidates | Where-Object { Test-Path (Join-Path $_ "Yifan.exe") } | Select-Object -First 1
if (-not $publish) {
  $found = Get-ChildItem -Path (Join-Path $root "src\Yifan.App\bin") -Recurse -Filter Yifan.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\publish\\' } |
    Select-Object -First 1
  if (-not $found) { throw "找不到发布目录里的 Yifan.exe" }
  $publish = $found.DirectoryName
}
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist "Yifan-0.1.0-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $publish "*") -DestinationPath $zip
Write-Host "已打包 $zip"
Write-Host "这是免安装的自包含目录压缩包。解压后运行 Yifan.exe。不要自动安装到别人的电脑上。"
