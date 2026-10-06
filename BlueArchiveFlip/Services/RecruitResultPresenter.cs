using Microsoft.Extensions.Logging;
using SecRandom.BlueArchiveFlip.Models;
using SecRandom.Core.Abstraction.Services.Presentation;

namespace SecRandom.BlueArchiveFlip.Services;

/// <summary>
///     结果呈现器：主程序抽签时由插件接管整条演出，在结果区里就地演招募式翻牌。
///     <para>
///         这是主程序 3.1 新增的插件扩展点：在 <c>PluginBase.Initialize</c> 里
///         <c>services.AddSingleton&lt;IDrawResultPresenter, RecruitResultPresenter&gt;()</c> 注册即可，
///         覆盖点名、抽奖、快捷抽签与移动端全部通道。
///     </para>
///     <para>
///         预览（抽取中）与揭晓两个阶段都接管：预览阶段在主程序结果区里摆牌并演"牌飞进来"，
///         揭晓阶段接着同一批牌逐张揭名。两次都返回 <see cref="DrawPresentationDecision.Taken" />，
///         主程序便跳过它自己的预览动画与结果动画，整条流程都由插件演。
///     </para>
///     <para>
///         演在哪里由 <see cref="DrawNotifier" /> 决定：有结果区插槽就就地渲染（此时让主程序藏掉它自己的
///         结果），没有插槽（老宿主、移动端）就退回插件自己的全屏窗口。
///     </para>
///     <para>
///         两个阶段都必须"立刻返回"：动画在 UI 线程上自己跑，呈现器绝不能阻塞主程序的抽签流程。
///     </para>
/// </summary>
public sealed class RecruitResultPresenter : IDrawResultPresenter
{
    private readonly DrawNotifier _notifier;
    private readonly PluginState _state;
    private readonly ILogger<RecruitResultPresenter>? _logger;

    public RecruitResultPresenter(
        PluginState state,
        DrawNotifier notifier,
        ILogger<RecruitResultPresenter>? logger = null)
    {
        _state = state;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>呈现器 id：也是「动画样式」下拉里那一项的 id，两者必须一致。</summary>
    public const string PresenterId = "plugin.cn.sectl.bluearchive.presenter";

    /// <inheritdoc />
    public string Id => PresenterId;

    /// <inheritdoc />
    public string DisplayName => Plugin.DisplayName;

    /// <inheritdoc />
    /// <remarks>数值越大越优先；插件只有自己一个呈现器，给个高值方便以后调试。</remarks>
    public int Priority => 100;

    /// <inheritdoc />
    public bool CanPresent(DrawPresentationRequest request)
    {
        // 用户在插件设置里关掉了结果展示，就把界面还给主程序。
        if (!_state.Recruit.Enabled)
            return false;

        // 预览阶段拿不到最终结果，只要知道这一轮要开几张牌就能接管整条动画。
        if (request.Phase == DrawPresentationPhase.Preview)
            return ResolvePreviewCount(request) > 0;

        if (request.Phase != DrawPresentationPhase.Reveal)
            return false;

        return request.Students.Count > 0
               || request.Prizes.Count > 0
               || request.AssignedStudents.Count > 0;
    }

    /// <inheritdoc />
    public Task<DrawPresentationDecision> PresentAsync(
        DrawPresentationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var handled = request.Phase == DrawPresentationPhase.Preview
                ? PresentPreview(request)
                : PresentReveal(request);

            if (!handled)
                return Task.FromResult(DrawPresentationDecision.NotHandled);

            // 就地渲染时舞台就盖在主程序结果区上面，得让主程序把它自己的卡片藏掉，免得两套牌面叠在一起；
            // 退回插件自己的窗口（全屏遮罩）时保持 false，主程序的结果留在下面更安全。
            return Task.FromResult(DrawPresentationDecision.Taken(
                hideHostResult: _notifier.UsesInPlaceStage(request.Channel)));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "展示招募结果时出错，这次交回给主程序自己显示。");
            return Task.FromResult(DrawPresentationDecision.NotHandled);
        }
    }

    /// <summary>预览阶段：按这一轮的张数摆牌播飞入；同一轮里主程序会反复触发，重播由通知器自己吞掉。</summary>
    private bool PresentPreview(DrawPresentationRequest request)
    {
        var count = ResolvePreviewCount(request);
        return count > 0
               && _notifier.StartPreview(count, ResolveSource(request.Channel), request.ScopeName, request.Channel);
    }

    /// <summary>揭晓阶段：把这一轮真正的结果接着预览已经飞进来的牌面演出来。</summary>
    private bool PresentReveal(DrawPresentationRequest request)
    {
        var items = BuildItems(request);
        return items.Count > 0
               && _notifier.StartReveal(items, ResolveSource(request.Channel), request.ScopeName, request.Channel);
    }

    /// <summary>预览阶段还没有最终结果，只能按张数摆牌：优先本轮结果条数，退回请求张数。</summary>
    private static int ResolvePreviewCount(DrawPresentationRequest request)
    {
        var count = request.ItemCount > 0 ? request.ItemCount : request.RequestedCount;
        return count > 0 ? count : 0;
    }

    private static RecruitSource ResolveSource(DrawPresentationChannel channel)
        => channel == DrawPresentationChannel.Lottery ? RecruitSource.Lottery : RecruitSource.RollCall;

    /// <summary>
    ///     抽奖优先展示奖品，没有奖品退回被指派的学生；点名 / 快捷抽签展示学生。
    ///     <para>
    ///         牌面上的字优先用宿主拼好的 <see cref="DrawPresentationRequest.DisplayTitles" />（同名次一一对应），
    ///         这样点名的「编号和名称」、抽奖的显示模板都能原样搬上牌面；宿主没给（老版本 / 手机端）就退回名字。
    ///     </para>
    /// </summary>
    private List<RecruitItem> BuildItems(DrawPresentationRequest request)
    {
        var settings = _state.Recruit;
        var titles = request.DisplayTitles;

        if (request.Prizes.Count > 0)
            return request.Prizes
                .Select((prize, index) => RecruitItemFactory.FromPrize(prize, settings, TitleAt(titles, index)))
                .ToList();

        if (request.AssignedStudents.Count > 0)
            return request.AssignedStudents
                .Select((student, index) => RecruitItemFactory.FromStudent(student, settings, TitleAt(titles, index)))
                .ToList();

        return request.Students
            .Select((student, index) => RecruitItemFactory.FromStudent(student, settings, TitleAt(titles, index)))
            .ToList();
    }

    /// <summary>取第 <paramref name="index" /> 张牌对应的展示文本；宿主没给这么多条就返回 null。</summary>
    private static string? TitleAt(IReadOnlyList<string> titles, int index)
        => index >= 0 && index < titles.Count ? titles[index] : null;
}
