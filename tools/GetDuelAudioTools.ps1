param([string]$Destination = (Join-Path $PSScriptRoot '../MdCardModTool/Assets/tools/vgmstream'))
$ErrorActionPreference = 'Stop'
$version = 'r2117'
$expected = '6C4A8A3813864FEFED081BBD337DBC0AD93BF88E0B92F5DB98D7AB258B22DC6C'
$staging = Join-Path ([IO.Path]::GetTempPath()) ('md-vgmstream-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
$zip = Join-Path $staging 'vgmstream-win64.zip'
Invoke-WebRequest -Uri "https://github.com/vgmstream/vgmstream/releases/download/$version/vgmstream-win64.zip" -OutFile $zip
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $expected) { throw 'vgmstream archive SHA-256 mismatch.' }
Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $staging 'unpacked')
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Copy-Item -Path (Join-Path $staging 'unpacked/*') -Destination $Destination -Force
$licenses = @('LibAtrac9.LICENSE','celt-0.11.0.COPYING','celt-0.6.1.COPYING','ffmpeg.COPYING.LGPLv2.1','ffmpeg.COPYING.LGPLv3','libogg-1.3.5.COPYING','libvorbis-1.3.7.COPYING','mpg123-1.31.1.COPYING','opus-1.3.1.COPYING','speex-1.2.1.COPYING')
foreach ($license in $licenses) {
    Invoke-WebRequest -Uri "https://raw.githubusercontent.com/vgmstream/vgmstream/$version/ext_libs/licenses/$license" -OutFile (Join-Path $Destination $license)
}
Write-Output "Installed vgmstream $version; verified archive retained at $zip"
