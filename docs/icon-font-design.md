# Windows 图标字体的选择与行距

## 可以从历史确认的原因

仓库没有保存当时与 DeepSeek 的对话，以下结论来自提交和旧代码，不是对原对话的复述。

- `82ae5f0` 引入 SVG 资源和自定义 `SvgIcon : UserControl`。
- `ab27842` 改用 `IconPark.ttf` 和继承 `FontIcon` 的 `Icon`，提交说明明确提到主题前景色支持。
- `ab27842` 的上一版本 `MainWindow.xaml` 注释明确记录：
  `NavigationViewItem.Icon` 要求 `IconElement`，不能直接放入自定义的 `UserControl`；
  `PathIcon` 只能填充路径，不提供 `Stroke` 描边。
- 当时侧栏用 `ImageIcon` 加载 SVG，播放按钮用自定义 `SvgIcon`；部分 SVG 文件的描边颜色写死在文件内。

因此最有证据的动机是：让同一套图标接入原生 `IconElement` 控件，并用 `Foreground`
处理主题和控件状态。IconPark 的原始图形大量使用描边，直接把路径字符串交给
`PathIcon` 不能保留描边外观；构建时先把描边展开成填充轮廓，再打包进字体，解决了这一点。
统一 `Glyph`、`FontSize` 和资源引用也是这个方案的直接收益。

SVG 本身支持描边和清晰缩放，字体并非唯一方案。可以先将描边展开再生成 `PathIcon`
几何资源，或另做能处理主题颜色的 SVG 图标控件。历史记录没有证明性能是改用字体的主要原因。

## 9% 行距的来源

当前锁定的 `svg2ttf` 6.1.0 在 `lib/sfnt.js` 中默认计算：

```js
lineGap = (ascent - descent) * 0.09
```

这会写进 `OS/2.sTypoLineGap`，并计入 `OS/2.usWinAscent`。字体还设置了
`USE_TYPO_METRICS`，而 `hhea.lineGap` 本来就是 0。因此不能只看 `hhea` 判定没有额外行距。
这不是 WinUI 或 IconPark 图形本身硬编码的留白。

本项目的字体原为 1000 UPM、ascent 1000、descent 0、typo line gap 90、WinAscent 1090。
实际 WinUI 测量也吻合：20 DIP 的 `FontIcon` 内部 `TextBlock` 高约 21.8 DIP。
图形轮廓位于 em 方框内，额外行距导致逻辑行框的中心与图形中心分开。

该版本的公开转换 API 不读取 `lineGap` 参数，SVG 解析器也不读取 `line-gap` 属性。
所以仅传 `{ lineGap: 0 }` 不会生效。

## 构建时的修正

`windows/Tools/IconBuilder/font-metrics.mjs` 在转换结束后修正 TTF：

- 将 `OS/2.sTypoLineGap`、`hhea.lineGap` 设为 0。
- 从原有 ascent/descent 和全字体轮廓边界计算 WinAscent/WinDescent，保留越界图形的裁切空间。
- 重算各表校验值和 `head.checkSumAdjustment`。

不修改 em 大小、ascent/descent、任何字形轮廓、水平度量或码点映射；不修改依赖库，
也不在界面添加位移补偿。重新生成字体会自动应用修正。

```powershell
cd windows/Tools/IconBuilder
npm test
npm run build
```

测试检查默认 9% 行距的移除、轮廓及其他表不变、越界保护、校验值和重复处理的稳定性。
修正后应用实测，20 DIP 图标内部行高为 20，24 DIP 图标为 24；独立用 fontTools
对比，18 个图标的码点、轮廓坐标、轮廓标记和水平度量均未变化。
字体没有额外行距，并不代表任意文字或不对称图形的视觉中心都会等于逻辑中心；
字符本身的形状和像素取整仍会影响视觉位置。

参考：
[svg2ttf 源码](https://github.com/fontello/svg2ttf/blob/master/lib/sfnt.js)、
[Windows 图标控件](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/icons)、
[OpenType OS/2 度量](https://learn.microsoft.com/en-us/typography/opentype/spec/os2)、
[OpenType head 校验](https://learn.microsoft.com/en-us/typography/opentype/spec/head)。
