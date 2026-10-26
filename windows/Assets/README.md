# Windows application icon

`AppIcon.source.png` is the transparent cutout of the Android artwork at
`mobile/assets/icon/app_icon.png`. The Android original is unchanged.

The cutout was made with the built-in `image_gen` tool using a transparent
background and this final prompt:

> 精确背景移除任务。只把输入图的纯黑背景和黑色负空间变成 alpha=0 的透明区域，所有粉白前景沿用原始像素的外观、柔和粉色、形状、位置、比例和完整方形留白。包括耳机拱内天线旁、两侧耳罩与电视之间的黑色缝隙。严禁重新设计或重绘标志；严禁提升饱和度、加描边、加光晕、生成洋红色彩边或留下白色噪点。轮廓应连续、光滑、自然抗锯齿，透明区域干净。

Run `./scripts/Sync-WindowsAppIcon.ps1` from the repository root to regenerate
the package PNGs and the multi-resolution `AppIcon.ico`. Generated files retain
the cutout's alpha channel. The source PNG itself is not a runtime dependency.
