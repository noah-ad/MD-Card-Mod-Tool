param(
    [string]$Assets = "E:\code\MD-MOD\发布\宣传素材",
    [string]$Output = "E:\code\MD-MOD\发布\MD卡图查看替换器\MD卡图查看替换器_介绍短片.mp4"
)

$ErrorActionPreference = 'Stop'
$overview = Join-Path $Assets '01_overview.png'
$overframe = Join-Path $Assets '02_overframe.png'
if (!(Test-Path -LiteralPath $overview) -or !(Test-Path -LiteralPath $overframe)) {
    throw '缺少宣传截图素材。'
}
New-Item -ItemType Directory -Path (Split-Path -Parent $Output) -Force | Out-Null

$font = 'C\:/Windows/Fonts/msyhbd.ttc'
$filter = @"
[0:v]scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080,gblur=sigma=22,eq=brightness=-0.38:saturation=0.85,drawtext=fontfile='$font':text='MD CARD STUDIO':fontcolor=0xF4F8FF:fontsize=92:x=(w-text_w)/2:y=h/2-105,drawtext=fontfile='$font':text='MASTER DUEL 卡图查看替换器':fontcolor=0x52D1F4:fontsize=40:x=(w-text_w)/2:y=h/2+20,drawbox=x=(iw-520)/2:y=ih/2+105:w=520:h=3:color=0xEFC268@0.85:t=fill,format=yuv420p,setpts=PTS-STARTPTS[v0];
[1:v]split=2[a1][b1];[a1]scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080,gblur=sigma=28,eq=brightness=-0.25[bg1];[b1]scale=1700:-2[fg1];[bg1][fg1]overlay=(W-w)/2:(H-h)/2,drawbox=x=0:y=ih-172:w=iw:h=172:color=0x07121F@0.86:t=fill,drawtext=fontfile='$font':text='海量资源  一眼分类':fontcolor=0xF4F8FF:fontsize=50:x=110:y=h-140,drawtext=fontfile='$font':text='卡图、异画、Token、游戏内图片与卡框统一检索':fontcolor=0x9EC7E7:fontsize=28:x=112:y=h-72,format=yuv420p,setpts=PTS-STARTPTS[v1];
[2:v]split=2[a2][b2];[a2]scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080,gblur=sigma=28,eq=brightness=-0.25[bg2];[b2]scale=1700:-2[fg2];[bg2][fg2]overlay=(W-w)/2:(H-h)/2,drawbox=x=0:y=ih-172:w=iw:h=172:color=0x07121F@0.86:t=fill,drawtext=fontfile='$font':text='替换、导出、超框  一站完成':fontcolor=0xF4F8FF:fontsize=50:x=110:y=h-140,drawtext=fontfile='$font':text='单卡卡框编辑与实时预览，让效果在进游戏前就可确认':fontcolor=0xEFC268:fontsize=28:x=112:y=h-72,format=yuv420p,setpts=PTS-STARTPTS[v2];
[3:v]scale=1920:1080:force_original_aspect_ratio=increase,crop=1920:1080,gblur=sigma=24,eq=brightness=-0.42:saturation=0.75,drawtext=fontfile='$font':text='自动备份  随时还原':fontcolor=0xF4F8FF:fontsize=72:x=(w-text_w)/2:y=h/2-90,drawtext=fontfile='$font':text='MD CARD STUDIO':fontcolor=0x52D1F4:fontsize=36:x=(w-text_w)/2:y=h/2+40,drawtext=fontfile='$font':text='为 Master Duel Mod 制作而生':fontcolor=0xEFC268:fontsize=28:x=(w-text_w)/2:y=h/2+102,format=yuv420p,setpts=PTS-STARTPTS[v3];
[v0][v1]xfade=transition=fade:duration=0.6:offset=3.4[x1];[x1][v2]xfade=transition=fade:duration=0.6:offset=8.8[x2];[x2][v3]xfade=transition=fade:duration=0.6:offset=15.2[v];
[4:a][5:a]amix=inputs=2:duration=longest,lowpass=f=900,volume=0.055,afade=t=in:st=0:d=1.2,afade=t=out:st=17.5:d=1.7[a]
"@

$arguments = @(
    '-y',
    '-loop', '1', '-t', '4', '-i', $overview,
    '-loop', '1', '-t', '6', '-i', $overview,
    '-loop', '1', '-t', '7', '-i', $overframe,
    '-loop', '1', '-t', '4', '-i', $overframe,
    '-f', 'lavfi', '-t', '19.2', '-i', 'sine=frequency=110:sample_rate=48000',
    '-f', 'lavfi', '-t', '19.2', '-i', 'sine=frequency=220:sample_rate=48000',
    '-filter_complex', $filter,
    '-map', '[v]', '-map', '[a]',
    '-r', '30', '-c:v', 'libx264', '-preset', 'medium', '-crf', '18', '-pix_fmt', 'yuv420p',
    '-c:a', 'aac', '-b:a', '160k', '-movflags', '+faststart', '-shortest', $Output
)

& ffmpeg @arguments
if ($LASTEXITCODE -ne 0) { throw "ffmpeg 生成失败：$LASTEXITCODE" }
Get-Item -LiteralPath $Output
