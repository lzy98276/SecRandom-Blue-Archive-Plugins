using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SecRandom.BlueArchiveFlip.Animation;
using SecRandom.BlueArchiveFlip.Controls;
using SecRandom.BlueArchiveFlip.Models;

namespace SecRandom.BlueArchiveFlip.Views;

/// <summary>
///     抽签结果舞台：面板从中央淡入放大，信件依次飞入，全部就位后逐张揭名。
///     <para>
///         两种用法共用同一套牌面与时间线：
///     </para>
///     <para>
///         ① <b>就地渲染</b>（桌面端点名 / 抽奖 / 快捷抽签）：用 <see cref="TakeHostedContent" /> 把内容
///         交给宿主结果区的插槽，面板直接出现在主程序自己的结果动画位置上——主程序不再开第二个窗口，
///         插件也不该再开一个浮窗压在上面。
///     </para>
///     <para>
///         ② <b>顶层窗口</b>（宿主没有插槽时的退路，例如移动端）：<see cref="Window.Show" /> 出来，
///         点任意位置（或按 Esc）关闭。
///     </para>
///     <para>
///         排版不写死每行几张、也不截断张数：牌面按结果区的可用宽度自动换行（<see cref="LettersPanel" />），
///         高到装不下就在 <see cref="Scroll" /> 里上下滚。
///     </para>
/// </summary>
public partial class RecruitOverlayWindow : Window
{
    /// <summary>面板最大宽度：再宽边框里也只是空荡荡一片，让牌面换行更好看。</summary>
    private const double MaxPanelWidth = 1160;

    /// <summary>面板最小宽度，和 XAML 里的 MinWidth 一致。</summary>
    private const double MinPanelWidth = 360;

    /// <summary>面板左右各留一点，别贴到结果区边上。</summary>
    private const double PanelSideMargin = 24;

    /// <summary>立绘 + 提示条 + 内边距大概占掉的高度，剩下的留给牌面。</summary>
    private const double ChromeHeight = 250;

    /// <summary>牌面区域的最小高度：可用高度很小时也至少露出一行牌。</summary>
    private const double MinLettersHeight = 150;

    /// <summary>卡片四周的留白：换行后行与行、列与列之间都靠它分开（倾斜的卡片需要这点余量）。</summary>
    private static readonly Thickness CardMargin = new(3, 8, 3, 12);

    /// <summary>不托管时提示条里追加的关闭说明。</summary>
    private const string DismissHint = "点击任意位置关闭";

    private readonly List<RecruitLetter> _letters = [];
    private CancellationTokenSource _cts = new();

    private bool _closed;

    /// <summary>就地托管时的内容根；为 null 表示这个实例还是自己开的顶层窗口。</summary>
    private Control? _hostRoot;

    /// <summary>这一轮演出是否还在进行：预览开始为 true，揭晓演完或被关掉为 false。</summary>
    private bool _roundOpen;

    /// <summary>预览阶段（飞入）那一串动画，揭晓阶段要接着它往下演。</summary>
    private Task _enterTask = Task.CompletedTask;

    private bool _previewPlayed;

    public RecruitOverlayWindow()
    {
        InitializeComponent();

        PART_Deco.IsVisible = true;
        ApplyStage(0);

        // 结果区大小变了（窗口拉伸、宿主换布局）就重新算一次面板宽度与牌面可视高度。
        PART_Backdrop.SizeChanged += (_, _) => UpdateViewport();
        UpdateViewport();

        Closed += (_, _) =>
        {
            _closed = true;
            _cts.Cancel();
        };
        KeyDown += OnKeyDown;
    }

    /// <summary>已经构造出来的信件，测试用来断言帧状态。</summary>
    public IReadOnlyList<RecruitLetter> Letters => _letters;

    /// <summary>面板本体，测试用来断言配色。</summary>
    public Border Panel => PART_Panel;

    /// <summary>信件容器：按可用宽度自动换行，不再写死每行几张。</summary>
    public WrapPanel LettersPanel => PART_Letters;

    /// <summary>信件区滚动容器：装不下时上下滚。</summary>
    public ScrollViewer Scroll => PART_Scroll;

    /// <summary>底部提示条里的文字，测试用来断言文案。</summary>
    public TextBlock HintText => PART_HintText;

    /// <summary>底部提示条。</summary>
    public Border Hint => PART_Hint;

    /// <summary>顶部立绘装饰。</summary>
    public Image Deco => PART_Deco;

    /// <summary>这一轮抽签是否已经演过预览（飞入）——主程序会反复触发预览，同一轮只该演一次。</summary>
    public bool HasPlayedPreview => _previewPlayed;

    /// <summary>内容是否已经交给宿主插槽就地渲染（此时不会再显示顶层窗口）。</summary>
    public bool IsHosted => _hostRoot is not null;

    /// <summary>这一轮演出是否还在进行。</summary>
    public bool IsRoundOpen => _roundOpen;

    /// <summary>
    ///     把内容从窗口里摘出来，交给主程序结果区的插槽就地渲染。
    ///     <para>
    ///         返回的控件由调用方负责挂进插槽；调用之后这个实例不再显示顶层窗口，
    ///     面板会出现在主程序自己的结果动画位置上，看起来就是"原本的动画被换成了招募动画"。
    ///     </para>
    /// </summary>
    public Control TakeHostedContent()
    {
        var root = PART_Backdrop;
        Content = null;
        root.IsVisible = false;
        _hostRoot = root;
        return root;
    }

    /// <summary>
    ///     开始新一轮：清掉上一轮的状态与牌面，让同一个实例能接着演下一轮
    ///     （就地托管时舞台常驻在结果区里，会被反复复用）。这一轮要到揭晓演完才算结束。
    /// </summary>
    public void BeginRound()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();

        _closed = false;
        _roundOpen = true;
        _previewPlayed = false;
        _enterTask = Task.CompletedTask;

        _letters.Clear();
        PART_Letters.Children.Clear();
        ApplyStage(0);
        UpdateViewport();

        if (_hostRoot is not null)
            _hostRoot.IsVisible = true;
    }

    /// <summary>让舞台露出来：就地托管时是显示内容根，否则开窗。</summary>
    private void ShowStage()
    {
        UpdateViewport();

        if (_hostRoot is not null)
        {
            _hostRoot.IsVisible = true;
            return;
        }

        if (!IsVisible)
            Show();
    }

    /// <summary>收起舞台：就地托管时只隐藏内容根（实例留着演下一轮），否则关窗。</summary>
    private void CloseStage()
    {
        if (_hostRoot is not null)
        {
            _hostRoot.IsVisible = false;
            return;
        }

        Close();
    }

    /// <summary>
    ///     预览阶段（"抽取中"）：按这一轮要开的张数摆好牌面，播面板入场 + 信件错峰飞入，
    ///     名字一律不揭。主程序的预览阶段会反复触发，所以同一轮只该调用一次。
    /// </summary>
    public Task PlayPreviewAsync(IReadOnlyList<RecruitItem> items, string sourceLabel, RecruitSettings settings)
    {
        if (_previewPlayed)
            return _enterTask;

        settings.Sanitized();
        BuildContent(items, sourceLabel, settings);
        _previewPlayed = true;
        _roundOpen = true;

        ShowStage();

        _enterTask = PlayEnterCoreAsync(settings);
        return _enterTask;
    }

    /// <summary>
    ///     揭晓阶段：接着已经飞入的牌面把这一轮真正的结果演出来。
    ///     张数一致就原地换名字（牌不再重飞一遍），张数变了才重建牌面。
    /// </summary>
    public async Task PlayRevealAsync(IReadOnlyList<RecruitItem> items, string sourceLabel, RecruitSettings settings)
    {
        settings.Sanitized();

        // 记下这一轮的取消源：被放弃的那一轮（用户又抽了一次）收尾时不能动新一轮的状态。
        var round = _cts;

        // 先把真正的名字填进牌面：飞入阶段名字本来就不显示，提前换掉不会穿帮，
        // 而且主程序可能在我们还在飞入时就切到揭晓。
        ApplyItems(items, sourceLabel, settings);

        if (_previewPlayed)
        {
            try
            {
                // 飞入还没演完就揭晓时要先等它，否则牌会从半空直接跳到位。
                await _enterTask;
            }
            catch (OperationCanceledException)
            {
                // 飞入途中被关掉了，下面按"牌已就位"继续演。
            }
        }
        else
        {
            // 没经过预览（例如宿主自己关掉了抽取动画）：面板和飞入都得从头演一遍。
            ShowStage();

            try
            {
                await Task.WhenAll(
                    AnimateStageAsync(true, settings, _cts.Token),
                    RecruitAnimator.PlayEnterAsync(_letters, settings, _cts.Token));
            }
            catch (OperationCanceledException)
            {
            }
        }

        try
        {
            await RecruitAnimator.PlayRevealAsync(_letters, settings, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // 用户在揭名途中关掉了窗口。
        }

        // 这一轮演完了。就地托管时舞台留在结果区里继续显示最终牌面（等下一轮预览再重置），
        // 所以"自动关闭"只对插件自己开的窗口生效。
        // 只有在还是同一轮的时候才收尾：新一轮已经用 BeginRound 换了取消源，这时不能把它的状态抹掉。
        if (!ReferenceEquals(_cts, round))
            return;

        _roundOpen = false;
        if (!IsHosted && settings.AutoCloseSeconds > 0 && !_closed)
            _ = AutoCloseAsync(settings.AutoCloseSeconds, _cts.Token);
    }

    private async Task PlayEnterCoreAsync(RecruitSettings settings)
    {
        try
        {
            await Task.WhenAll(
                AnimateStageAsync(true, settings, _cts.Token),
                RecruitAnimator.PlayEnterAsync(_letters, settings, _cts.Token));
        }
        catch (OperationCanceledException)
        {
            // 用户在飞入途中关掉了窗口。
        }
    }

    /// <summary>把这一轮真正的结果填进牌面；张数一致就地改名字，免得已经飞进来的牌重飞一遍。</summary>
    private void ApplyItems(IReadOnlyList<RecruitItem> items, string sourceLabel, RecruitSettings settings)
    {
        if (_letters.Count > 0 && _letters.Count == items.Count)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var letter = _letters[i];
                letter.DisplayName = items[i].Name;
                letter.SubText = items[i].SubText;
                letter.Rarity = items[i].Rarity;
            }

            ApplyPanelLook(settings);
            PART_HintText.Text = BuildHintText(sourceLabel);
            PART_Hint.IsVisible = PART_HintText.Text.Length > 0;
            ApplyDeco(settings.ShowDeco);
            return;
        }

        BuildContent(items, sourceLabel, settings);
    }

    /// <summary>
    ///     摆好内容并播完整条时间线。必须在 UI 线程调用。
    /// </summary>
    public async Task ShowRecruitAsync(IReadOnlyList<RecruitItem> items, string sourceLabel, RecruitSettings settings)
    {
        settings.Sanitized();
        BuildContent(items, sourceLabel, settings);
        _roundOpen = true;

        ShowStage();

        try
        {
            var enterTask = AnimateStageAsync(true, settings, _cts.Token);
            var letterTask = RecruitAnimator.PlayAsync(_letters, settings, _cts.Token);
            await Task.WhenAll(enterTask, letterTask);
        }
        catch (OperationCanceledException)
        {
            // 用户在动画途中关掉了窗口。
        }

        if (settings.AutoCloseSeconds > 0 && !_closed)
            _ = AutoCloseAsync(settings.AutoCloseSeconds, _cts.Token);
    }

    /// <summary>
    ///     只摆内容、直接定格到最终一帧，不播动画、不显示窗口。
    ///     设置页的"预览"和自动化测试都用它，保证和真实播放共用同一套排版与姿态。
    /// </summary>
    public void BuildSettled(IReadOnlyList<RecruitItem> items, string sourceLabel, RecruitSettings settings)
    {
        BuildContent(items, sourceLabel, settings.Sanitized());
        ApplyStage(1);
        RecruitAnimator.SettleAll(_letters);
    }

    /// <summary>不播退场动画，立刻收起（就地托管时只隐藏内容，实例留着演下一轮）。</summary>
    public void ForceClose()
    {
        _roundOpen = false;

        if (IsHosted)
        {
            _closed = true;
            _cts.Cancel();
            CloseStage();
            return;
        }

        if (_closed)
            return;

        _closed = true;
        _cts.Cancel();
        Close();
    }

    /// <summary>
    ///     把结果铺进面板：牌面按可用宽度自动换行，装不下就在滚动区里上下滚，张数不再截断。
    /// </summary>
    private void BuildContent(IReadOnlyList<RecruitItem> items, string sourceLabel, RecruitSettings settings)
    {
        PART_Letters.Children.Clear();
        _letters.Clear();

        foreach (var item in items)
        {
            var letter = new RecruitLetter
            {
                DisplayName = item.Name,
                SubText = item.SubText,
                Rarity = item.Rarity,
                Margin = CardMargin
            };

            letter.ResetToEnterStart();
            _letters.Add(letter);
            PART_Letters.Children.Add(letter);
        }

        ApplyPanelLook(settings);

        PART_HintText.Text = BuildHintText(sourceLabel);
        PART_Hint.IsVisible = PART_HintText.Text.Length > 0;
        ApplyDeco(settings.ShowDeco);
        UpdateViewport();
    }

    /// <summary>顶部立绘图源从程序集资源取；取不到就只是不显示装饰，不影响结果展示。</summary>
    private void ApplyDeco(bool show)
    {
        if (!show)
        {
            PART_Deco.IsVisible = false;
            return;
        }

        try
        {
            PART_Deco.Source ??= RecruitLetter.GetDecoBitmap();
            PART_Deco.IsVisible = true;
        }
        catch (Exception)
        {
            PART_Deco.IsVisible = false;
        }
    }

    /// <summary>
    ///     底部提示条的文案：显示这次结果的来源（点名 / 抽奖 · 名单名）。
    ///     只有插件自己开窗时才追加"点击任意位置关闭"——就地渲染时舞台就在结果区里，点它不该把结果收掉。
    /// </summary>
    private string BuildHintText(string sourceLabel)
    {
        var source = sourceLabel.Trim();
        if (IsHosted)
            return source;

        return source.Length == 0 ? DismissHint : $"{source} · {DismissHint}";
    }

    /// <summary>
    ///     按舞台的实际可用宽高刷新排版：面板最宽到可用宽度（卡片自动换行），
    ///     牌面区最高到可用高度减去立绘与提示条（装不下就出现滚动条）。
    ///     就地渲染时宿主给多大地方就用多大；退回窗口时整个屏幕都是可用空间。
    /// </summary>
    private void UpdateViewport()
    {
        var size = PART_Backdrop.Bounds;
        var width = size.Width > 0 ? size.Width : Bounds.Width;
        var height = size.Height > 0 ? size.Height : Bounds.Height;

        if (width > 0)
            PART_Panel.MaxWidth = Math.Max(MinPanelWidth, Math.Min(MaxPanelWidth, width - PanelSideMargin));

        if (height > 0)
            PART_Scroll.MaxHeight = Math.Max(MinLettersHeight, height - ChromeHeight);
    }

    private void ApplyPanelLook(RecruitSettings settings)
    {
        var background = ParseColor(settings.PanelBackground, Colors.White);
        var alpha = (byte)Math.Round(Math.Clamp(settings.PanelOpacity, 0.2, 1.0) * 255);
        PART_Panel.Background = new SolidColorBrush(Color.FromArgb(alpha, background.R, background.G, background.B));
        PART_Panel.BorderBrush = new SolidColorBrush(ParseColor(settings.PanelBorderColor, Color.FromRgb(0x66, 0xCC, 0xFF)));
    }

    private static Color ParseColor(string? text, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        return Color.TryParse(text, out var color) ? color : fallback;
    }

    /// <summary>面板整体入场 / 退场的逐帧推进，进度 0~1。</summary>
    private async Task AnimateStageAsync(bool entering, RecruitSettings settings, CancellationToken cancellationToken)
    {
        var duration = entering ? settings.PanelDurationMs : 200;
        if (duration <= 0)
        {
            ApplyStage(entering ? 1 : 0);
            return;
        }

        var clock = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var progress = Math.Clamp(clock.Elapsed.TotalMilliseconds / duration, 0, 1);
            ApplyStage(entering ? RecruitAnimator.EaseOutCubic(progress) : progress);

            if (progress >= 1)
                break;

            await Task.Delay(RecruitAnimator.FrameIntervalMs, cancellationToken);
        }
    }

    private void ApplyStage(double progress)
    {
        var t = Math.Clamp(progress, 0, 1);

        PART_Stage.Opacity = t;

        var scale = 0.85 + (0.15 * t);
        var scaleTransform = (ScaleTransform)StageGroup.Children[0];
        var translateTransform = (TranslateTransform)StageGroup.Children[1];

        scaleTransform.ScaleX = scale;
        scaleTransform.ScaleY = scale;
        translateTransform.Y = 16 * (1 - t);
    }

    // 变换对象不是控件，FindControl 拿不到，只能按 XAML 里声明的顺序从 TransformGroup 里取。
    private TransformGroup StageGroup => (TransformGroup)PART_Stage.RenderTransform!;

    private async Task AutoCloseAsync(int seconds, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_closed)
                _ = DismissAsync();
        });
    }

    /// <summary>播 200ms 退场再收起。</summary>
    public async Task DismissAsync()
    {
        if (_closed)
            return;

        _closed = true;
        _roundOpen = false;

        try
        {
            var settings = new RecruitSettings { PanelDurationMs = 200 };
            await AnimateStageAsync(false, settings, CancellationToken.None);
        }
        catch (Exception)
        {
            // 退场动画失败也要把窗口关掉。
        }

        _cts.Cancel();
        CloseStage();
    }

    private void OnBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        // 就地托管时舞台就是主程序结果区的一部分，点它不该把结果收掉。
        if (IsHosted)
            return;

        _ = DismissAsync();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter or Key.Space)
        {
            e.Handled = true;
            _ = DismissAsync();
        }
    }
}
