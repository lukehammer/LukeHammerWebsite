<#
.SYNOPSIS
    Copies the photo shoot planner's local dev data (shoots, settings, reference images) into the
    private "photoshoot" blob container, using the same layout as BlobPhotoShootStore.

.DESCRIPTION
    Local layout (LocalPhotoShootStore):      Blob layout (container "photoshoot"):
      <root>\shoots\<shootId>.json        ->    shoots/<shootId>.json   (application/json)
      <root>\settings.json                ->    settings.json
      <root>\images\<imageId>             ->    images/<imageId>        (content type from <imageId>.type)

    Image ids are kept identical because shots and references store only the id.
    Refuses to run if the container already holds any shoot document, unless -AllowExistingShoots.
    Existing blobs are never overwritten.

    The connection string is read from $env:AZURE_STORAGE_CONNECTION_STRING and never printed.
    Requires the Azure CLI.

.EXAMPLE
    $env:AZURE_STORAGE_CONNECTION_STRING = az storage account show-connection-string -n <account> -g <rg> --subscription <id> --query connectionString -o tsv
    ./scripts/Copy-PhotoShootLocalToBlob.ps1
#>
param(
    [string]$SourceDir = (Join-Path ([IO.Path]::GetTempPath()) "lukehammer-photoshoot"),
    [string]$Container = "photoshoot",
    [switch]$AllowExistingShoots
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:AZURE_STORAGE_CONNECTION_STRING)) {
    throw "Set `$env:AZURE_STORAGE_CONNECTION_STRING first (it is read from the environment so it never appears on a command line)."
}

$shootDir = Join-Path $SourceDir "shoots"
$imageDir = Join-Path $SourceDir "images"
$settingsFile = Join-Path $SourceDir "settings.json"
if (-not (Test-Path $shootDir)) {
    throw "No shoots folder at $shootDir"
}

function Invoke-Az {
    $output = & az @args --only-show-errors
    if ($LASTEXITCODE -ne 0) {
        throw "az $($args[0..2] -join ' ') failed (exit $LASTEXITCODE)."
    }
    $output
}

# Windows PowerShell 5.1's ConvertFrom-Json emits a JSON array as a single object; unroll it.
function Get-AzList {
    $json = (Invoke-Az @args -o json) -join "`n"
    @(($json | ConvertFrom-Json) | ForEach-Object { $_ })
}

# Private container, same as BlobPhotoShootStore.CreateIfNotExistsAsync(PublicAccessType.None).
Invoke-Az storage container create --name $Container --public-access off | Out-Null

$existingShoots = Get-AzList storage blob list --container-name $Container --prefix "shoots/" --query "[].name"
if ($existingShoots.Count -gt 0 -and -not $AllowExistingShoots) {
    throw "Container '$Container' already has shoot documents ($($existingShoots -join ', ')). Nothing uploaded."
}

$existing = @{}
foreach ($name in Get-AzList storage blob list --container-name $Container --num-results "*" --query "[].name") {
    $existing[$name] = $true
}

$uploads = @()
foreach ($file in Get-ChildItem $shootDir -Filter "*.json" -File) {
    $uploads += [pscustomobject]@{ Path = $file.FullName; Name = "shoots/$($file.Name)"; Type = "application/json" }
}
if (Test-Path $settingsFile) {
    $uploads += [pscustomobject]@{ Path = $settingsFile; Name = "settings.json"; Type = "application/json" }
}
if (Test-Path $imageDir) {
    foreach ($file in Get-ChildItem $imageDir -File | Where-Object { $_.Extension -ne ".type" }) {
        $typeFile = "$($file.FullName).type"
        $type = if (Test-Path $typeFile) { (Get-Content $typeFile -Raw).Trim() } else { "application/octet-stream" }
        $uploads += [pscustomobject]@{ Path = $file.FullName; Name = "images/$($file.Name)"; Type = $type }
    }
}

$uploaded = 0
$skipped = 0
foreach ($item in $uploads) {
    if ($existing.ContainsKey($item.Name)) {
        Write-Host "skip (exists): $($item.Name)"
        $skipped++
        continue
    }
    Invoke-Az storage blob upload --container-name $Container --name $item.Name --file $item.Path --content-type $item.Type --no-progress | Out-Null
    $uploaded++
    if ($uploaded % 20 -eq 0) {
        Write-Host "uploaded $uploaded / $($uploads.Count)"
    }
}
Write-Host "Uploaded $uploaded, skipped $skipped existing."

# Verify: every local file has a blob with the same size, MD5 and content type.
$blobs = @{}
foreach ($blob in Get-AzList storage blob list --container-name $Container --num-results "*" --query "[].{name:name, size:properties.contentLength, md5:properties.contentSettings.contentMd5, type:properties.contentSettings.contentType}") {
    $blobs[$blob.name] = $blob
}

$failures = @()
foreach ($item in $uploads) {
    $blob = $blobs[$item.Name]
    if ($null -eq $blob) {
        $failures += "missing: $($item.Name)"
        continue
    }
    $localSize = (Get-Item $item.Path).Length
    $localMd5 = [Convert]::ToBase64String([Security.Cryptography.MD5]::Create().ComputeHash([IO.File]::ReadAllBytes($item.Path)))
    if ($blob.size -ne $localSize) { $failures += "size mismatch: $($item.Name)" }
    if ($blob.md5 -and $blob.md5 -ne $localMd5) { $failures += "md5 mismatch: $($item.Name)" }
    if ($blob.type -ne $item.Type) { $failures += "content type mismatch: $($item.Name) ($($blob.type))" }
}

$localImages = @($uploads | Where-Object { $_.Name -like "images/*" }).Count
$blobImages = @($blobs.Keys | Where-Object { $_ -like "images/*" }).Count
$localShoots = @($uploads | Where-Object { $_.Name -like "shoots/*" }).Count
$blobShoots = @($blobs.Keys | Where-Object { $_ -like "shoots/*" }).Count
Write-Host "Shoots: local $localShoots, blob $blobShoots. Images: local $localImages, blob $blobImages."

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host $_ }
    throw "Verification failed for $($failures.Count) item(s)."
}
Write-Host "Verified: all $($uploads.Count) local files match their blobs (size, MD5, content type)."
