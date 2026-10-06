# 碧蓝档案招募 · SecRandom 插件

把 SecRandom 的抽签演出换成碧蓝档案招募风格：抽取时白色面板淡入、信封错峰飞入，抽出结果后接着同一批牌依次揭名。
演出就在主程序自己的**结果区**里，不是浮窗；抽签规则、历史与凭证仍然全部由主程序负责。

## 安装

装这个文件：

```
BlueArchiveFlip\srpx\SecRandom.BlueArchiveFlip.srpx
```

1. 打开 SecRandom → **设置 → 插件**；
2. 点右上角 **＋**（导入插件），选中上面这个 `.srpx`；
3. 按提示重启主程序，设置里会出现 **碧蓝档案招募**。

手动安装：退出 SecRandom，把 srpx 放到 `<数据目录>\cache\plugin-packages\cn.sectl.bluearchive.srpx`，下次启动会自动装。
数据目录：便携版 `<主程序目录>\data`，安装版 `%LOCALAPPDATA%\SecRandom\data`。

卸载：退出 SecRandom 后删掉 `<数据目录>\plugins\cn.sectl.bluearchive` 和 `<数据目录>\config\plugins\cn.sectl.bluearchive`。

## 使用

**设置 → 默认抽取设置 → 动画样式 → 「碧蓝档案招募」**，选一次，点名 / 抽奖 / 快捷抽签都会用。
想在某一页单独用别的样式，先打开那一页的「覆盖动画设置」再选。之后照常点名 / 抽奖即可。

- 抽到几张就展示几张：牌面按结果区宽度自动换行，装不下可以在结果区里上下滚动；
- 牌面上的文字跟随主程序：点名 / 快捷抽签用「显示格式」，抽奖用抽奖显示模板；
- 设置页「碧蓝档案招募」里可以调：总开关、信封档位、面板外观、时间线、运行情况。

## 从源码构建

需要 .NET 10 SDK。

```powershell
dotnet build .\BlueArchiveFlip\SecRandom.BlueArchiveFlip.csproj -c Release
dotnet test  .\.tests\SecRandom.BlueArchiveFlip.Tests.csproj -c Release
```

第一条产出 `BlueArchiveFlip\srpx\SecRandom.BlueArchiveFlip.srpx`。主程序侧还没发布对应 SDK 时，加 `-p:UseLocalSdk=true` 改为引用本地客户端源码。

## 兼容性

- 主程序 3.1.0 起：「动画样式」下拉里会出现本插件（`IDrawResultPresenter` + `IDrawAnimationContribution`）；
- 主程序 3.2.0 起：就地演在主程序结果区；更早的宿主自动退回插件自己的全屏窗口；
- 主程序 3.2.0 起：插件被卸载或禁用后，指向它的「动画样式」会在下次启动时自动恢复成主程序内置动画；
- Windows / Linux / macOS 桌面版均可。

## 素材来源

`BlueArchiveFlip\Assets\` 里的信件底图与顶部立绘来自**蔚蓝档案官方**素材，版权归蔚蓝档案官方所有，此处仅用于风格演示；
本插件是非官方作品，与蔚蓝档案官方无关。

## 说明

- 插件的全部记忆写在主程序分配给它的配置目录（`state.json`），不会碰主程序的其他文件；
- 学生数据里没有稀有度字段，所以默认「统一档位」；想让不同学生分档，把 1 / 2 / 3 写进学生标签，再把「决定方式」切成「按标签猜」。
