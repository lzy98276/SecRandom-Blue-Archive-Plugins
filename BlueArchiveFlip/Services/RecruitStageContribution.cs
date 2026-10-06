using Avalonia.Controls;
using SecRandom.BlueArchiveFlip.Views;
using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Core.Abstraction.Services.Views;

namespace SecRandom.BlueArchiveFlip.Services;

/// <summary>
///     把招募舞台挂进主程序结果区的插槽：插件不开自己的窗口，动画就演在主程序原本演结果动画的那块地方。
///     <para>
///         一个实例对应一条通道（点名 / 抽奖 / 快捷抽签），四条边角通道（远程、移动端）没有插槽就不注册。
///     </para>
///     <para>
///         每次 <see cref="CreateContent" /> 都新建一个舞台实例：插槽重建时会重新调用本方法，复用同一个
///         控件会撞上"控件已有父级"的问题。
///     </para>
/// </summary>
public sealed class RecruitStageContribution : IUiContentContribution
{
    /// <summary>贡献项 id 前缀，后面接通道名。</summary>
    public const string IdPrefix = "plugin.cn.sectl.bluearchive.stage.";

    private readonly RecruitStageRegistry _registry;
    private readonly DrawPresentationChannel _channel;

    public RecruitStageContribution(RecruitStageRegistry registry, DrawPresentationChannel channel)
    {
        _registry = registry;
        _channel = channel;
    }

    /// <inheritdoc />
    public string Id => IdPrefix + _channel;

    /// <inheritdoc />
    public string SlotId => ResolveSlotId(_channel);

    /// <inheritdoc />
    /// <remarks>只往插槽里加内容：宿主自己的结果由抽签时的呈现决定控制，不动插槽默认内容。</remarks>
    public UiSlotKind Kind => UiSlotKind.Append;

    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public Control? CreateContent(UiSlotContext context)
    {
        var stage = new RecruitOverlayWindow();
        var root = stage.TakeHostedContent();
        _registry.Register(_channel, stage);
        root.DetachedFromVisualTree += (_, _) =>
        {
            _registry.Unregister(_channel, stage);
            stage.ForceClose();
        };

        return root;
    }

    /// <summary>通道 → 主程序结果区插槽；没有对应插槽的通道返回空串（宿主会忽略这个贡献项）。</summary>
    public static string ResolveSlotId(DrawPresentationChannel channel) => channel switch
    {
        DrawPresentationChannel.RollCall => HostUiSlots.RollCallResultExtra,
        DrawPresentationChannel.Lottery => HostUiSlots.LotteryResultExtra,
        DrawPresentationChannel.QuickDraw => HostUiSlots.QuickDrawResultExtra,
        _ => string.Empty
    };
}
