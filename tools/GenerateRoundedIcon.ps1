param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$PngPath,
    [Parameter(Mandatory = $true)][string]$IcoPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-RoundedBitmap([System.Drawing.Image]$source, [int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

        $radius = [Math]::Max(3, [int]($size * 0.18))
        $diameter = $radius * 2
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        try {
            $path.AddArc(0, 0, $diameter, $diameter, 180, 90)
            $path.AddArc($size - $diameter - 1, 0, $diameter, $diameter, 270, 90)
            $path.AddArc($size - $diameter - 1, $size - $diameter - 1, $diameter, $diameter, 0, 90)
            $path.AddArc(0, $size - $diameter - 1, $diameter, $diameter, 90, 90)
            $path.CloseFigure()
            $graphics.SetClip($path)

            $scale = [Math]::Max($size / $source.Width, $size / $source.Height)
            $width = [int][Math]::Ceiling($source.Width * $scale)
            $height = [int][Math]::Ceiling($source.Height * $scale)
            $x = [int](($size - $width) / 2)
            $y = [int](($size - $height) / 2)
            $graphics.DrawImage($source, [System.Drawing.Rectangle]::new($x, $y, $width, $height))
        }
        finally {
            $path.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
    }
    return $bitmap
}

$pngDirectory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($PngPath))
$icoDirectory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($IcoPath))
[System.IO.Directory]::CreateDirectory($pngDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory($icoDirectory) | Out-Null

$source = [System.Drawing.Image]::FromFile([System.IO.Path]::GetFullPath($InputPath))
try {
    $large = New-RoundedBitmap $source 256
    try {
        $large.Save([System.IO.Path]::GetFullPath($PngPath), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $large.Dispose()
    }

    $sizes = @(16, 24, 32, 48, 64, 128, 256)
    $images = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $bitmap = New-RoundedBitmap $source $size
        try {
            $stream = [System.IO.MemoryStream]::new()
            try {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
                $images.Add($stream.ToArray())
            }
            finally {
                $stream.Dispose()
            }
        }
        finally {
            $bitmap.Dispose()
        }
    }

    $file = [System.IO.File]::Create([System.IO.Path]::GetFullPath($IcoPath))
    $writer = [System.IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($index = 0; $index -lt $sizes.Count; $index++) {
            $size = $sizes[$index]
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$images[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $images[$index].Length
        }
        foreach ($image in $images) {
            $writer.Write($image)
        }
    }
    finally {
        $writer.Dispose()
        $file.Dispose()
    }
}
finally {
    $source.Dispose()
}

Write-Output "PNG=$([System.IO.Path]::GetFullPath($PngPath))"
Write-Output "ICO=$([System.IO.Path]::GetFullPath($IcoPath))"
