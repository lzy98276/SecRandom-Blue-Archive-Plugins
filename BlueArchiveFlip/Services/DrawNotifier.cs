using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.BlueArchiveFlip.Models;
using SecRandom.BlueArchiveFlip.Views;
using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.BlueArchiveFlip.Services;

/// <summary>
///     把"抽签落库了"这件事翻译成一次招募动画。
///     <para>
///         优先就地渲染：主程序结果区里有插槽时，动画直接演在主程序原本演结果动画的位置上（见
///         <see cref="RecruitStageRegistry" />），看起来就是主程序自己的结果动画被换成了招募动画；
///         宿主没有插槽（老宿主、移动端、页面还没建出来）时才退回插件自己的顶层窗口。
///     </para>
///     <para>
///         抽签本身由主程序完成，插件只负责在提交之后把结果演出来。所以这里所有失败都被吞掉：
///         动画演不出来是小事，绝不能影响已经写进历史与凭证的抽签结果。
///     </para>
/// </summary>
public sealed class DrawNotifier
{
    private readonly PluginState _state;
    private readonly PluginStateStore _store;
    private readonly ILogger<DrawNotifier>? _logger;
    private readonly RecruitStageRegistry? _stages;

    private RecruitOverlayWindow? _current;

    public DrawNotifier(PluginState state, PluginStateStore store, ILogger<DrawNotifier>? logger = null,
        RecruitStageRegistry? stages = null)
    {
        _state = state;
        _store = store;
        _logger = logger;
        _stages = stages;
    }

    /// <summary>当前正在演的这一轮舞台（就地插槽里的，或插件自己的窗口），没有则为 null。</summary>
    public RecruitOverlayWindow? CurrentWindow => _current;

    /// <summary>
    ///     这条通道有没有就地舞台。有的话动画演在主程序结果区里，呈现决定就可以顺手把主程序自己的结果藏掉，
    ///     免得两套牌面叠在同一块地方。
    /// </summary>
    public bool UsesInPlaceStage(DrawPresentationChannel channel) => _stages?.Get(channel) is not null;

    /// <summary>
    ///     预览阶段（"抽取中"）：在结果区里按张数演一遍飞入。返回 false 表示这次交回给主程序自己播。
    ///     <para>主程序的预览阶段会反复触发，所以这里记着"这一轮已经在演了"，后续触发直接返回 true 不重播。</para>
    /// </summary>
    public bool StartPreview(int cardCount, RecruitSource source, string listName,
        DrawPresentationChannel channel = DrawPresentationChannel.RollCall)
    {
        var settings = _state.Recruit;
        if (!settings.Enabled || cardCount <= 0)
            return false;

        var label = BuildSourceLabel(source, listName);

        // 抽多少张就演多少张：上限交给排版（自动换行 + 滚动）解决，不在数据上截断。
        var count = Math.Max(1, cardCount);

        // 抽签可能发生在后台线程（快捷抽签、远程抽签），控件只能在 UI 线程上碰。
        if (Dispatcher.UIThread.CheckAccess())
            StartPreviewOnUiThread(count, label, channel);
        else
            Dispatcher.UIThread.Post(() => StartPreviewOnUiThread(count, label, channel));

        return true;
    }

    /// <summary>揭晓阶段：把这一轮真正的结果演出来（接着预览已经飞进来的牌面）。返回 false 表示交回给主程序。</summary>
    public bool StartReveal(IReadOnlyList<RecruitItem> items, RecruitSource source, string listName,
        DrawPresentationChannel channel = DrawPresentationChannel.RollCall)
    {
        if (!_state.Recruit.Enabled || items.Count == 0)
            return false;

        var label = BuildSourceLabel(source, listName);

        if (Dispatcher.UIThread.CheckAccess())
            StartRevealOnUiThread(items, label, channel);
        else
            Dispatcher.UIThread.Post(() => StartRevealOnUiThread(items, label, channel));

        return true;
    }

    private void StartPreviewOnUiThread(int cardCount, string label, DrawPresentationChannel channel)
    {
        try
        {
            var settings = _state.Recruit.Clone().Sanitized();

            // 主程序结果区里有插槽就地渲染：直接演在那儿。
            var stage = _stages?.Get(channel);
            if (stage is not null)
            {
                _current = stage;
                if (stage.IsRoundOpen)
                    return; // 同一轮预览的后续触发：动画已经在跑了。

                stage.BeginRound();
                var placeholders = BuildPlaceholders(cardCount);
                _ = stage.PlayPreviewAsync(placeholders, label, settings);
                _logger?.LogInformation("已在主程序结果区开始招募动画预览：{Count} 张，来源 {Source}。",
                    placeholders.Count, label);
                return;
            }

            var window = _current;
            if (window is not null && window.HasPlayedPreview)
                return; // 同一轮预览的后续触发：窗和动画都已经在跑了。

            window = EnsureWindow();

            var items = BuildPlaceholders(cardCount);
            _ = window.PlayPreviewAsync(items, label, settings);
            _logger?.LogInformation("已开始招募动画预览：{Count} 张，来源 {Source}。", items.Count, label);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "开始招募动画预览失败，已忽略。");
        }
    }

    private void StartRevealOnUiThread(IReadOnlyList<RecruitItem> items, string label,
        DrawPresentationChannel channel)
    {
        try
        {
            var settings = _state.Recruit.Clone().Sanitized();

            var stage = _stages?.Get(channel);
            if (stage is not null)
            {
                _current = stage;
                if (!stage.IsRoundOpen)
                    stage.BeginRound(); // 没有预览就直接揭晓（例如抽奖）：整套从头演一遍。

                _ = stage.PlayRevealAsync(items, label, settings);
                _logger?.LogInformation("已在主程序结果区展示招募结果：{Count} 张，来源 {Source}。", items.Count,
                    label);
            }
            else
            {
                var window = _current;

                if (window is null)
                {
                    window = EnsureWindow();
                    _ = window.ShowRecruitAsync(items, label, settings);
                }
                else
                {
                    _ = window.PlayRevealAsync(items, label, settings);
                }

                _logger?.LogInformation("已展示招募结果：{Count} 张，来源 {Source}。", items.Count, label);
            }

            _state.DrawCount++;
            _state.LastMessage = $"最近一次展示：{label}，{items.Count} 张";
            _store.Save(_state);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "展示招募结果失败，已忽略。");
        }
    }

    /// <summary>
    ///     预览阶段还不知道结果，先用一星占位牌把张数与版式摆出来，名字与真正的稀有度在揭晓时才换上去。
    /// </summary>
    private static List<RecruitItem> BuildPlaceholders(int cardCount)
    {
        var items = new List<RecruitItem>(cardCount);
        for (var i = 0; i < cardCount; i++)
            items.Add(new RecruitItem(string.Empty, LetterRarity.Blue));

        return items;
    }

    /// <summary>开一个全新的结果窗；上一轮没关掉的先强行关掉，避免叠出两个遮罩层。</summary>
    private RecruitOverlayWindow EnsureWindow()
    {
        _current?.ForceClose();

        var window = new RecruitOverlayWindow();
        _current = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_current, window))
                _current = null;
        };

        return window;
    }

    /// <summary>点名 / 快捷抽签的结果。</summary>
    public void NotifyStudents(IReadOnlyList<Student> winners, string listName)
    {
        if (winners is null || winners.Count == 0)
            return;

        var settings = _state.Recruit;
        if (!settings.Enabled)
            return;

        List<RecruitItem> items;
        try
        {
            items = winners.Select(w => RecruitItemFactory.FromStudent(w, settings)).ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "把点名结果翻译成招募卡片时出错，已跳过结果展示。");
            return;
        }

        Show(items, RecruitSource.RollCall, listName, DrawPresentationChannel.RollCall);
    }

    /// <summary>抽奖的结果：优先展示奖品，没有奖品就退回展示被指派的学生。</summary>
    public void NotifyLottery(
        IReadOnlyList<Prize> prizes,
        IReadOnlyList<Student>? assignedStudents,
        string prizeListName)
    {
        var settings = _state.Recruit;
        if (!settings.Enabled)
            return;

        List<RecruitItem> items;
        try
        {
            if (prizes is { Count: > 0 })
            {
                items = prizes.Select(p => RecruitItemFactory.FromPrize(p, settings)).ToList();
            }
            else if (assignedStudents is { Count: > 0 })
            {
                items = assignedStudents.Select(s => RecruitItemFactory.FromStudent(s, settings)).ToList();
            }
            else
            {
                return;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "把抽奖结果翻译成招募卡片时出错，已跳过结果展示。");
            return;
        }

        Show(items, RecruitSource.Lottery, prizeListName, DrawPresentationChannel.Lottery);
    }

    private void Show(List<RecruitItem> items, RecruitSource source, string listName, DrawPresentationChannel channel)
    {
        if (items.Count == 0)
            return;

        var settings = _state.Recruit.Clone().Sanitized();
        var label = BuildSourceLabel(source, listName);

        // 抽签可能发生在后台线程（快捷抽签、远程抽签），控件只能在 UI 线程上碰。
        Dispatcher.UIThread.Post(() => ShowOnUiThread(items, label, settings, channel));
    }

    private void ShowOnUiThread(List<RecruitItem> items, string label, RecruitSettings settings,
        DrawPresentationChannel channel)
    {
        try
        {
            var stage = _stages?.Get(channel);
            if (stage is not null)
            {
                _current = stage;
                stage.BeginRound();
                _ = stage.ShowRecruitAsync(items, label, settings);
                _logger?.LogInformation("已在主程序结果区展示招募结果：{Count} 张，来源 {Source}。", items.Count, label);
            }
            else
            {
                var window = EnsureWindow();

                _ = window.ShowRecruitAsync(items, label, settings);
                _logger?.LogInformation("已展示招募结果：{Count} 张，来源 {Source}。", items.Count, label);
            }

            _state.DrawCount++;
            _state.LastMessage = $"最近一次展示：{label}，{items.Count} 张";
            _store.Save(_state);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "展示招募结果失败，已忽略。");
        }
    }

    private static string BuildSourceLabel(RecruitSource source, string listName)
    {
        var name = string.IsNullOrWhiteSpace(listName) ? string.Empty : listName.Trim();
        var prefix = source == RecruitSource.Lottery ? "抽奖" : "点名";

        return name.Length == 0 ? prefix : $"{prefix} · {name}";
    }
}
