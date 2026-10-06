using SecRandom.BlueArchiveFlip.Views;
using SecRandom.Core.Abstraction.Services.Presentation;

namespace SecRandom.BlueArchiveFlip.Services;

/// <summary>
///     就地舞台登记表：主程序结果区里的插槽建好舞台后登记到这里，抽签时按通道取用。
///     <para>
///         桌面端点名 / 抽奖 / 快捷抽签三个页面各有一条结果区插槽，插件把招募动画挂进去就地渲染，
///     看起来就是"主程序原本的结果动画被换成了招募动画"；取不到舞台（老宿主没有插槽、移动端、
///     页面还没建出来）时调用方退回插件自己的结果窗口。
///     </para>
///     <para>
///         抽签可能从后台线程触发（快捷抽签、远程抽签），所以读写都加了锁。
///     </para>
/// </summary>
public sealed class RecruitStageRegistry
{
    private readonly Dictionary<DrawPresentationChannel, RecruitOverlayWindow> _stages = [];
    private readonly object _gate = new();

    /// <summary>登记（或替换）某个通道的就地舞台；被替换掉的旧舞台会立刻收起来。</summary>
    public void Register(DrawPresentationChannel channel, RecruitOverlayWindow stage)
    {
        RecruitOverlayWindow? previous;
        lock (_gate)
        {
            _stages.TryGetValue(channel, out previous);
            _stages[channel] = stage;
        }

        if (previous is not null && !ReferenceEquals(previous, stage))
        {
            try
            {
                previous.ForceClose();
            }
            catch (Exception)
            {
                // 收起旧舞台失败不影响新舞台。
            }
        }
    }

    /// <summary>注销某个通道的就地舞台（页面被销毁时）；只有登记的还是这个实例时才移除。</summary>
    public void Unregister(DrawPresentationChannel channel, RecruitOverlayWindow stage)
    {
        lock (_gate)
        {
            if (_stages.TryGetValue(channel, out var current) && ReferenceEquals(current, stage))
                _stages.Remove(channel);
        }
    }

    /// <summary>取某个通道的就地舞台；没有则返回 null，调用方走窗口退路。</summary>
    public RecruitOverlayWindow? Get(DrawPresentationChannel channel)
    {
        lock (_gate)
        {
            return _stages.TryGetValue(channel, out var stage) ? stage : null;
        }
    }

    /// <summary>当前是否已经有就地舞台挂在主程序界面上。</summary>
    public bool HasAny
    {
        get
        {
            lock (_gate)
            {
                return _stages.Count > 0;
            }
        }
    }
}
