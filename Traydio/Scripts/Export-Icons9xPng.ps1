param(
    [string]$IconDirectory = "P:\Visual Studio\Traydio\Traydio\Assets\Icons9x",
    [int[]]$Sizes = @(16, 24),
    [switch]$Overwrite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $IconDirectory)) {
    throw "Icon directory not found: $IconDirectory"
}

$icoFiles = Get-ChildItem -Path $IconDirectory -Filter "*.ico" | Sort-Object Name
if ($icoFiles.Count -eq 0) {
    throw "No .ico files found in $IconDirectory"
}

foreach ($icoFile in $icoFiles) {
    $baseName = [System.IO.Path]::GetFileNameWithoutExtension($icoFile.Name)

    foreach ($size in $Sizes) {
        $outputPath = Join-Path $IconDirectory ("{0}_{1}.png" -f $baseName, $size)
        if ((Test-Path $outputPath) -and -not $Overwrite.IsPresent) {
            continue
        }

        $icon = New-Object System.Drawing.Icon($icoFile.FullName, (New-Object System.Drawing.Size($size, $size)))
        try {
            $bitmap = $icon.ToBitmap()
            try {
                if ($bitmap.Width -ne $size -or $bitmap.Height -ne $size) {
                    $resized = New-Object System.Drawing.Bitmap($size, $size)
                    $graphics = [System.Drawing.Graphics]::FromImage($resized)
                    try {
                        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
                        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
                        $graphics.DrawImage($bitmap, 0, 0, $size, $size)
                    }
                    finally {
                        $graphics.Dispose()
                    }

                    $bitmap.Dispose()
                    $bitmap = $resized
                }

                $bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
                Write-Host "Exported $($icoFile.Name) -> $(Split-Path -Leaf $outputPath)"
            }
            finally {
                $bitmap.Dispose()
            }
        }
        finally {
            $icon.Dispose()
        }
    }
}

Write-Host "Done. PNG exports are in $IconDirectory"

