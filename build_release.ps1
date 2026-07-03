$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root "src\HeliSightBuilder\HeliSightBuilder.Native.csproj"
$output = Join-Path $root "release"

dotnet publish $project `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:IncludeAllContentForSelfExtract=false `
  -p:EnableCompressionInSingleFile=false `
  -p:PublishTrimmed=false `
  -p:PublishReadyToRun=false `
  --output $output

if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

Get-FileHash (Join-Path $output "HeliSightBuilder.exe") -Algorithm SHA256
