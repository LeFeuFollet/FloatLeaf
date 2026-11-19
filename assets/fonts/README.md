# 内置正文字体

浮页将以下八份常规字重的静态字体和七份对应的许可证文本一起嵌入可执行文件。两款 Noto 字体共用一份许可文件。下表的版本取自随包字体的名称表。七款字体声明使用 SIL Open Font License 1.1，Fira Mono 声明使用 1.1 或更高版本。

| 分类 | 字体（版本） | 上游 | 许可 |
| --- | --- | --- | --- |
| 中文 | Noto Serif SC 2.02 | [Noto CJK](https://github.com/notofonts/noto-cjk) | [OFL](OFL-NotoCJK.txt) |
| 中文 | Noto Sans SC 2.002 | [Noto CJK](https://github.com/notofonts/noto-cjk) | [OFL](OFL-NotoCJK.txt) |
| 中文 | 霞鹜文楷 GB Lite 1.521 | [LXGW WenKai GB Lite](https://github.com/lxgw/LxgwWenkaiGB-Lite) | [OFL](OFL-WenKai.txt) |
| 中文 | 朱雀仿宋（预览测试版）0.212 | [朱雀仿宋](https://github.com/TrionesType/zhuque) | [OFL](OFL-Zhuque.txt) |
| 英文 | Source Serif 4（4.005） | [Adobe Source Serif](https://github.com/adobe-fonts/source-serif/blob/80d3f8894c09c937bebfa9011247d2e1c79fd6f4/OTF/SourceSerif4-Regular.otf) | [OFL](OFL-SourceSerif.txt) |
| 英文 | Crimson Pro 1.001 | [Crimson Pro](https://ctan.org/pkg/crimsonpro) | [OFL](OFL-CrimsonPro.txt) |
| 英文 | Inter 4.000 | [Inter](https://rsms.me/inter/) | [OFL](OFL-Inter.txt) |
| 英文 | Fira Mono 3.201 | [Fira](https://ctan.org/pkg/fira) | [OFL 1.1 或更高版本](OFL-FiraMono.txt) |

Noto Serif SC 的静态文件由字体内标记为 `Version 2.02;241114204558;non-release` 的 NotoSerifSC-VF.ttf 提取而得。输入 SHA-256 为 `a4aed9985a5916fbf6690456f8732a9fccd517938e353165d4142b4f11a39280`。使用 fontTools 4.65.0 将 `wght` 固定为 400，更新了静态字体的名称记录并保留完整字符映射。其余字体保留取得时的原始字节。
