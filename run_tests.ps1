$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$tests = Join-Path $root "tests\HeliSightBuilder.Tests\HeliSightBuilder.Tests.csproj"
$resources = Join-Path $root "src\HeliSightBuilder\Resources"

dotnet run `
  --project $tests `
  --configuration Release `
  -- `
  (Join-Path $resources "source") `
  (Join-Path $resources "template")

if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

$app = Join-Path $root "src\HeliSightBuilder\bin\Release\net8.0-windows\HeliSightBuilder.dll"
$report = Join-Path $root "ui-quality-checks.txt"
dotnet $app --ui-test $report

if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

Get-Content $report
