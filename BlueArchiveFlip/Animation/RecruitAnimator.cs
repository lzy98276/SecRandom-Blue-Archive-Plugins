using System.Diagnostics;
using SecRandom.BlueArchiveFlip.Controls;
using SecRandom.BlueArchiveFlip.Models;

namespace SecRandom.BlueArchiveFlip.Animation;

/// <summary>
///     招募结果窗的时间线：信件错峰飞入 → 全部就位 → 依次揭名。
///     <para>
///         用逐帧推进而不是宿主那套 Storyboard 式动画，原因有两个：
///         一是错峰与揭名的相位要能精确算出来（测试要断言关键帧），
///         二是同一份代码可以把进度直接定格，方便"跳过动画"和截图比对。
///     </para>
///     <para>必须在 UI 线程调用：方法内部靠 <see cref="Task.Delay" /> 让出，续体回到同一个同步上下文。</para>
/// </summary>
public static class RecruitAnimator
{
    /// <summary>逐帧推进的间隔，约 60fps。</summary>
    public const int FrameIntervalMs = 16;

    /// <summary>和参考实现一致的缓动：先快后慢。</summary>
    public static double EaseOutCubic(double t)
    {
        var clamped = Math.Clamp(t, 0, 1);
        return 1 - Math.Pow(1 - clamped, 3);
    }

    /// <summary>整条时间线的总时长（毫秒）。</summary>
    public static int GetTotalDurationMs(int letterCount, RecruitSettings settings)
    {
        if (letterCount <= 0)
            return 0;

        return GetEnterDurationMs(letterCount, settings) + GetRevealDurationMs(letterCount, settings);
    }

    /// <summary>
    ///     飞入阶段的总时长：最后一张牌就位的那一刻。
    ///     <para>主程序把抽签拆成"抽取中（预览）"和"揭晓"两段，插件接管预览时第一段就播到这里为止。</para>
    /// </summary>
    public static int GetEnterDurationMs(int letterCount, RecruitSettings settings)
    {
        if (letterCount <= 0)
            return 0;

        return ((letterCount - 1) * settings.CardDelayMs) + settings.CardDurationMs;
    }

    /// <summary>揭名阶段的总时长：先停 <see cref="RecruitSettings.RevealDelayMs" />，再逐张揭名。</summary>
    public static int GetRevealDurationMs(int letterCount, RecruitSettings settings)
    {
        if (letterCount <= 0)
            return 0;

        return settings.RevealDelayMs + ((letterCount - 1) * settings.CardDelayMs) + settings.RevealDurationMs;
    }

    /// <summary>把某一时刻的进度写进每张牌（飞入与揭名共用一条时间线）。</summary>
    public static void ApplyAt(IReadOnlyList<RecruitLetter> letters, RecruitSettings settings, double elapsedMs)
    {
        if (letters.Count == 0)
            return;

        var enterTotal = GetEnterDurationMs(letters.Count, settings);
        if (elapsedMs <= enterTotal)
        {
            ApplyEnterAt(letters, settings, elapsedMs);
            return;
        }

        ApplyRevealAt(letters, settings, elapsedMs - enterTotal);
    }

    /// <summary>
    ///     只推进"飞入"这一段：<paramref name="elapsedMs" /> 从飞入开始算。
    ///     名牌一律不动，所以可以单独拿来当预览动画（抽取中）。
    /// </summary>
    public static void ApplyEnterAt(IReadOnlyList<RecruitLetter> letters, RecruitSettings settings, double elapsedMs)
    {
        var stagger = settings.CardDelayMs;

        for (var i = 0; i < letters.Count; i++)
        {
            var enterLocal = elapsedMs - (i * stagger);
            letters[i].ApplyCardEnter(EaseOutCubic(enterLocal / settings.CardDurationMs));
        }
    }

    /// <summary>
    ///     只推进"揭名"这一段：<paramref name="elapsedMs" /> 从揭名阶段开始算
    ///     （即先经过 <see cref="RecruitSettings.RevealDelayMs" /> 的停顿，再逐张揭名）。
    /// </summary>
    public static void ApplyRevealAt(IReadOnlyList<RecruitLetter> letters, RecruitSettings settings, double elapsedMs)
    {
        var stagger = settings.CardDelayMs;

        for (var i = 0; i < letters.Count; i++)
        {
            var letter = letters[i];
            var revealLocal = elapsedMs - settings.RevealDelayMs - (i * stagger);

            // 到了揭晓阶段，所有牌早就飞到位了，这里只是把这一点钉死。
            letter.ApplyCardEnter(1);

            if (revealLocal <= 0)
            {
                letter.ApplyNameReveal(0);
                continue;
            }

            letter.ApplyNameReveal(EaseOutCubic(revealLocal / settings.RevealDurationMs));
        }
    }

    /// <summary>播完整条时间线。</summary>
    public static async Task PlayAsync(
        IReadOnlyList<RecruitLetter> letters,
        RecruitSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (letters.Count == 0)
            return;

        foreach (var letter in letters)
            letter.ResetToEnterStart();

        var total = GetTotalDurationMs(letters.Count, settings);
        var clock = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var elapsed = clock.Elapsed.TotalMilliseconds;
            ApplyAt(letters, settings, elapsed);

            if (elapsed >= total)
                break;

            await Task.Delay(FrameIntervalMs, cancellationToken);
        }

        SettleAll(letters);
    }

    /// <summary>
    ///     只播"飞入"这一段（抽取中的预览动画）：牌飞进来并停住，名字一律不揭。
    ///     <para>主程序的预览阶段会反复触发，所以调用方要保证同一轮抽签只播一次。</para>
    /// </summary>
    public static async Task PlayEnterAsync(
        IReadOnlyList<RecruitLetter> letters,
        RecruitSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (letters.Count == 0)
            return;

        foreach (var letter in letters)
            letter.ResetToEnterStart();

        var total = GetEnterDurationMs(letters.Count, settings);
        var clock = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var elapsed = clock.Elapsed.TotalMilliseconds;
            ApplyEnterAt(letters, settings, elapsed);

            if (elapsed >= total)
                break;

            await Task.Delay(FrameIntervalMs, cancellationToken);
        }
    }

    /// <summary>只播"揭名"这一段（接着已经飞入的牌面）。</summary>
    public static async Task PlayRevealAsync(
        IReadOnlyList<RecruitLetter> letters,
        RecruitSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (letters.Count == 0)
            return;

        var total = GetRevealDurationMs(letters.Count, settings);
        var clock = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var elapsed = clock.Elapsed.TotalMilliseconds;
            ApplyRevealAt(letters, settings, elapsed);

            if (elapsed >= total)
                break;

            await Task.Delay(FrameIntervalMs, cancellationToken);
        }

        SettleAll(letters);
    }

    /// <summary>跳过动画，直接定格在最终姿态。</summary>
    public static void SettleAll(IReadOnlyList<RecruitLetter> letters)
    {
        foreach (var letter in letters)
            letter.ApplySettled();
    }
}
