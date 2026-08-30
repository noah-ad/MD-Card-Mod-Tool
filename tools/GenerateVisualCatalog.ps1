param(
    [Parameter(Mandatory = $true)]
    [string]$CsvPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$wallpaperExclusions = @(
    'wallpapericon',
    'wallpaperthumb',
    'gui_wallpaperbg',
    'productthumbbgwallpaperprofile',
    'sactx-0-2048x1024-bc7',
    'wallpapersale',
    'wallpapertopicsthumb'
)

function Get-VisualCategory([string]$name) {
    $normalized = $name.ToLowerInvariant()
    if ($normalized.StartsWith('shopbgbase')) { return '大厅背景' }
    if ($normalized.Contains('basecolor') -and $normalized.Contains('mat_')) { return '决斗场地' }
    if ($normalized.Contains('coin01tex') -or $normalized.Contains('cointossicon')) { return '硬币' }
    if ($normalized.Contains('deckcase')) { return '卡盒' }
    if ($normalized.Contains('profileframe')) { return '头像框' }
    if ($normalized.Contains('profileicon')) { return '头像' }
    if ($normalized.Contains('protectoricon')) { return '卡套' }
    if ($normalized.Contains('wallpaper')) {
        foreach ($excluded in $wallpaperExclusions) {
            if ($normalized.Contains($excluded)) { return $null }
        }
        return '大厅壁纸'
    }
    return $null
}

$entries = foreach ($row in Import-Csv -LiteralPath $CsvPath) {
    $hash = ([string]$row.'File ID').Trim().ToLowerInvariant()
    $name = ([string]$row.'ITEM ID').Trim()
    $category = Get-VisualCategory $name
    if ($category -and $hash -match '^[0-9a-f]{8}$' -and $name -notmatch "[\t\r\n]") {
        [pscustomobject]@{
            Hash = $hash
            Name = $name
            Category = $category
        }
    }
}

$lines = @('# Astellar visual asset catalog v1', "# hash`tname`tcategory")
$lines += $entries |
    Sort-Object Hash, Name, Category -Unique |
    ForEach-Object { "{0}`t{1}`t{2}" -f $_.Hash, $_.Name, $_.Category }

$outputDirectory = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
[IO.File]::WriteAllLines([IO.Path]::GetFullPath($OutputPath), $lines, [Text.UTF8Encoding]::new($false))

Write-Output ("Generated {0:N0} entries at {1}" -f ($lines.Count - 2), ([IO.Path]::GetFullPath($OutputPath)))
