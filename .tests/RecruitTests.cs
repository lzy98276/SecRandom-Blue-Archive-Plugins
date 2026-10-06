using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.BlueArchiveFlip.Animation;
using SecRandom.BlueArchiveFlip.Controls;
using SecRandom.BlueArchiveFlip.Models;
using SecRandom.BlueArchiveFlip.Services;
using SecRandom.BlueArchiveFlip.Views;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Core.Abstraction.Services.Views;
using SecRandom.Shared.Models.Profile;

[assembly: AvaloniaTestApplication(typeof(SecRandom.BlueArchiveFlip.Tests.TestAppBuilder))]

namespace SecRandom.BlueArchiveFlip.Tests;

public class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
}

/// <summary>
///     插件自测：确认招募结果窗的排版、时间线和"接管抽签提交"的装饰器都按预期工作。
/// </summary>
public class RecruitTests
{
    private static RecruitSettings Settings() => new()
    {
        CardDelayMs = 120,
        CardDurationMs = 600,
        RevealDelayMs = 100,
        RevealDurationMs = 300,
        PanelDurationMs = 700
    };

    private static RecruitItem[] Items(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new RecruitItem($"学生{i + 1}", LetterRarity.Rainbow))];

    private static List<RecruitLetter> Letters(int count)
    {
        var letters = new List<RecruitLetter>();
        for (var i = 0; i < count; i++)
        {
            var letter = new RecruitLetter { DisplayName = $"学生{i + 1}" };
            letter.ResetToEnterStart();
            letters.Add(letter);
        }

        return letters;
    }

    [AvaloniaFact]
    public void 参数越界会被夹回安全区间()
    {
        var settings = new RecruitSettings
        {
            PanelOpacity = 9,
            CardDelayMs = -50,
            CardDurationMs = 5,
            RevealDelayMs = 99999,
            RevealDurationMs = 1,
            PanelDurationMs = -1,
            AutoCloseSeconds = 9999,
            PanelBackground = "不是颜色",
            PanelBorderColor = "#GGGGGG"
        }.Sanitized();

        Assert.Equal(1.0, settings.PanelOpacity, 6);
        Assert.Equal(0, settings.CardDelayMs);
        Assert.Equal(RecruitSettings.CardDurationRange.Min, settings.CardDurationMs);
        Assert.Equal(RecruitSettings.RevealDelayRange.Max, settings.RevealDelayMs);
        Assert.Equal(RecruitSettings.RevealDurationRange.Min, settings.RevealDurationMs);
        Assert.Equal(0, settings.PanelDurationMs);
        Assert.Equal(RecruitSettings.AutoCloseRange.Max, settings.AutoCloseSeconds);
        Assert.Equal("#FFFFFF", settings.PanelBackground);
        Assert.Equal("#66CCFF", settings.PanelBorderColor);
    }

    [AvaloniaFact]
    public void 统一档位时所有信都用同一个档位()
    {
        var settings = new RecruitSettings { RarityMode = RarityMode.Uniform, UniformRarity = LetterRarity.Gold };
        var student = new Student { Name = "小鸟游星野", Tags = "★3" };

        var item = RecruitItemFactory.FromStudent(student, settings);

        Assert.Equal("小鸟游星野", item.Name);
        Assert.Equal(LetterRarity.Gold, item.Rarity);
    }

    [AvaloniaFact]
    public void 按标签猜稀有度()
    {
        var settings = new RecruitSettings { RarityMode = RarityMode.ByTag, UniformRarity = LetterRarity.Blue };

        Assert.Equal(LetterRarity.Rainbow, RecruitItemFactory.FromStudent(new Student { Name = "A", Tags = "★3" }, settings).Rarity);
        Assert.Equal(LetterRarity.Rainbow, RecruitItemFactory.FromStudent(new Student { Name = "B", Tags = "三星" }, settings).Rarity);
        Assert.Equal(LetterRarity.Gold, RecruitItemFactory.FromStudent(new Student { Name = "C", Tags = "★2" }, settings).Rarity);
        Assert.Equal(LetterRarity.Blue, RecruitItemFactory.FromStudent(new Student { Name = "D", Tags = "★1" }, settings).Rarity);

        // 猜不出来时退回统一档位，而不是抛异常。
        Assert.Equal(LetterRarity.Blue, RecruitItemFactory.ResolveRarity("对策委员会", settings));
        Assert.Equal(LetterRarity.Blue, RecruitItemFactory.ResolveRarity(null, settings));
    }

    [AvaloniaFact]
    public void 抽奖结果按奖品名展示()
    {
        var item = RecruitItemFactory.FromPrize(new Prize { Name = "一等奖" }, new RecruitSettings());

        Assert.Equal("一等奖", item.Name);
        Assert.Equal(LetterRarity.Rainbow, item.Rarity);
    }

    [AvaloniaFact]
    public void 未命名结果不会显示空白名牌()
    {
        var item = RecruitItemFactory.FromStudent(new Student { Name = "   " }, new RecruitSettings());

        Assert.False(string.IsNullOrWhiteSpace(item.Name));
    }

    /// <summary>
    /// 素材必须是程序集资源（EmbeddedResource），不能是 avares://：
    /// 宿主会把插件装进独立的 AssemblyLoadContext，avares:// 在那种情况下可能解析不到。
    /// 这条断言同时挡住"忘了把 Assets\** 打进程序集"和"资源逻辑名写错"。
    /// </summary>
    [AvaloniaFact]
    public void 素材以程序集资源内嵌并且能解码()
    {
        var assembly = typeof(RecruitLetter).Assembly;
        var names = assembly.GetManifestResourceNames();

        Assert.Contains("SecRandom.BlueArchiveFlip.Assets.Letter_Blue.png", names);
        Assert.Contains("SecRandom.BlueArchiveFlip.Assets.Letter_Gold.png", names);
        Assert.Contains("SecRandom.BlueArchiveFlip.Assets.Letter_Rainbow.png", names);
        Assert.Contains("SecRandom.BlueArchiveFlip.Assets.Arona_Plana.png", names);

        Assert.NotNull(RecruitLetter.GetLetterBitmap(LetterRarity.Blue));
        Assert.NotNull(RecruitLetter.GetLetterBitmap(LetterRarity.Gold));
        Assert.NotNull(RecruitLetter.GetLetterBitmap(LetterRarity.Rainbow));
        Assert.NotNull(RecruitLetter.GetDecoBitmap());
    }

    [AvaloniaFact]
    public void 信件入场起点是放大且透明的()
    {
        var letter = Letters(1)[0];

        Assert.Equal(RecruitLetter.CardEnterScale, CardScaleOf(letter).ScaleX, 6);
        Assert.Equal(RecruitLetter.CardEnterOffsetY, CardTranslateOf(letter).Y, 6);
        Assert.Equal(0d, letter.Opacity, 6);
        Assert.Equal(0d, NameLayerOf(letter).Opacity, 6);
    }

    [AvaloniaFact]
    public void 信件定格后名牌完全展开()
    {
        var shortName = Letters(1)[0];
        shortName.ApplySettled();

        Assert.Equal(1d, shortName.Opacity, 6);
        Assert.Equal(1d, CardScaleOf(shortName).ScaleX, 6);
        Assert.Equal(RecruitLetter.CardTiltDegrees, CardRotateOf(shortName).Angle, 6);
        Assert.Equal(1d, NameLayerOf(shortName).Opacity, 6);
        Assert.Equal(1d, NameScaleOf(shortName).ScaleX, 6);
        Assert.Equal(0d, NameTranslateOf(shortName).Y, 6);
        Assert.Equal(21d, NameTextOf(shortName).FontSize, 6);

        // 名字越长字号越小，长名字不会溢出名牌（卡片与名牌一起改小，字号阈值同步下调）。
        var midName = new RecruitLetter { DisplayName = "阿拜多斯高中一年级学生" };
        midName.ApplySettled();
        Assert.Equal(15d, NameTextOf(midName).FontSize, 6);

        var longName = new RecruitLetter { DisplayName = "阿拜多斯高级中学三年级对策委员会" };
        longName.ApplySettled();
        Assert.Equal(13d, NameTextOf(longName).FontSize, 6);
    }

    [AvaloniaFact]
    public void 时间线总时长与错峰一致()
    {
        var settings = Settings();
        settings.Sanitized();

        // 10 张：(9*120+600) + 100 + (9*120+300) = 1680 + 100 + 1380 = 3160
        Assert.Equal(3160, RecruitAnimator.GetTotalDurationMs(10, settings));
        Assert.Equal(0, RecruitAnimator.GetTotalDurationMs(0, settings));
    }

    [AvaloniaFact]
    public void 逐帧推进时信件按错峰依次飞入再揭名()
    {
        var settings = Settings();
        settings.Sanitized();
        var letters = Letters(3);

        RecruitAnimator.ApplyAt(letters, settings, 0);
        foreach (var letter in letters)
        {
            Assert.Equal(0d, letter.Opacity, 6);
            Assert.Equal(0d, NameLayerOf(letter).Opacity, 6);
        }

        // 100ms 时只有第一张开始飞入，第二、三张还没动。
        RecruitAnimator.ApplyAt(letters, settings, 100);
        Assert.InRange(letters[0].Opacity, 0.01, 0.99);
        Assert.Equal(0d, letters[1].Opacity, 6);
        Assert.Equal(0d, letters[2].Opacity, 6);

        // 600ms 时第一张已经就位，后面两张还在路上：越靠后越淡、越放大。
        RecruitAnimator.ApplyAt(letters, settings, 600);
        Assert.Equal(1d, letters[0].Opacity, 6);
        Assert.InRange(letters[1].Opacity, 0.01, 0.999);
        Assert.InRange(letters[2].Opacity, 0.01, 0.999);
        Assert.True(letters[1].Opacity > letters[2].Opacity);
        Assert.True(CardScaleOf(letters[2]).ScaleX > 1d);

        // 全部就位之后才逐张揭名：揭名从"最后一张飞完 + RevealDelayMs"开始。
        var allCardsSettledMs = (letters.Count - 1) * settings.CardDelayMs + settings.CardDurationMs;
        RecruitAnimator.ApplyAt(letters, settings, allCardsSettledMs);
        foreach (var letter in letters)
        {
            Assert.Equal(1d, letter.Opacity, 6);
            Assert.Equal(0d, NameLayerOf(letter).Opacity, 6);
        }

        // 第一张的名字已经开始揭，第二张还差着错峰间隔没轮到（取半个错峰，保证仍早于第二张）。
        var revealStart = allCardsSettledMs + settings.RevealDelayMs;
        RecruitAnimator.ApplyAt(letters, settings, revealStart + (settings.CardDelayMs / 2d));
        Assert.InRange(NameLayerOf(letters[0]).Opacity, 0.01, 0.999);
        Assert.Equal(0d, NameLayerOf(letters[1]).Opacity, 6);

        RecruitAnimator.ApplyAt(letters, settings, RecruitAnimator.GetTotalDurationMs(3, settings));
        foreach (var letter in letters)
        {
            Assert.Equal(1d, letter.Opacity, 6);
            Assert.Equal(1d, NameLayerOf(letter).Opacity, 6);
        }
    }

    [AvaloniaFact]
    public void 抽到多少张就摆多少张不再截断()
    {
        var window = new RecruitOverlayWindow();
        window.BuildSettled(Items(20), "点名 · 名单", Settings());

        Assert.Equal(20, window.Letters.Count);
        Assert.Equal(20, window.LettersPanel.Children.Count);
    }

    [AvaloniaFact]
    public void 牌面按宽度自动换行并且装不下能上下滚()
    {
        var window = new RecruitOverlayWindow
        {
            WindowState = WindowState.Normal,
            Width = 900,
            Height = 560
        };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        window.BuildSettled(Items(20), "点名 · 名单", Settings());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(20, window.LettersPanel.Children.Count);

        // 同一行的卡片 Top 相同，换行就是把 Top 分成好几档。
        var rows = window.LettersPanel.Children
            .Cast<Control>()
            .Select(control => Math.Round(control.Bounds.Top, 1))
            .Distinct()
            .Count();
        Assert.True(rows >= 2, $"20 张牌在结果区里应该换行排布，实际只有 {rows} 行。");

        // 牌面比结果区高时滚动区就能滚（Auto 策略下滚动条只在需要时出现）。
        Assert.True(window.Scroll.Extent.Height > window.Scroll.Viewport.Height,
            "牌面装不下时应该可以上下滚动。");

        window.Close();
    }

    [AvaloniaFact]
    public void 提示条会带上来源标签()
    {
        var window = new RecruitOverlayWindow();
        window.BuildSettled(Items(2), "抽奖 · 奖池", Settings());

        Assert.True(window.Hint.IsVisible);
        Assert.Equal("抽奖 · 奖池 · 点击任意位置关闭", window.HintText.Text);

        // 就地渲染时舞台就是主程序结果区的一部分，点它不该把结果收掉，提示条也就不写这句。
        var hosted = new RecruitOverlayWindow();
        hosted.TakeHostedContent();
        hosted.BuildSettled(Items(2), "抽奖 · 奖池", Settings());

        Assert.Equal("抽奖 · 奖池", hosted.HintText.Text);
    }

    [AvaloniaFact]
    public void 牌面文字优先用宿主拼好的展示文本()
    {
        var settings = Settings();
        var student = new Student { Name = "小鸟游星野" };

        // 宿主的「显示格式」是"编号和名称"时，牌面上就该是"07 小鸟游星野"。
        Assert.Equal("07 小鸟游星野", RecruitItemFactory.FromStudent(student, settings, "07 小鸟游星野").Name);
        Assert.Equal("SP·星野", RecruitItemFactory.FromPrize(new Prize { Name = "星野" }, settings, "SP·星野").Name);

        // 宿主没给（老版本 / 手机端）或者给的是空白，就退回数据里的名字。
        Assert.Equal("小鸟游星野", RecruitItemFactory.FromStudent(student, settings).Name);
        Assert.Equal("小鸟游星野", RecruitItemFactory.FromStudent(student, settings, "   ").Name);
    }

    [AvaloniaFact]
    public void 不显示装饰图时顶部立绘收起来()
    {
        var window = new RecruitOverlayWindow();
        window.BuildSettled(Items(2), "点名 · 名单", new RecruitSettings { ShowDeco = false });

        Assert.False(window.Deco.IsVisible);
    }

    [AvaloniaFact]
    public void 面板配色按设置生效()
    {
        var window = new RecruitOverlayWindow();
        window.BuildSettled(
            Items(1),
            "点名 · 名单",
            new RecruitSettings { PanelOpacity = 0.5, PanelBackground = "#112233", PanelBorderColor = "#AABBCC" });

        var background = Assert.IsType<SolidColorBrush>(window.Panel.Background);
        Assert.Equal("#112233", $"#{background.Color.R:X2}{background.Color.G:X2}{background.Color.B:X2}");
        Assert.Equal((byte)128, background.Color.A);

        var border = Assert.IsType<SolidColorBrush>(window.Panel.BorderBrush);
        Assert.Equal("#AABBCC", $"#{border.Color.R:X2}{border.Color.G:X2}{border.Color.B:X2}");
    }

    [AvaloniaFact]
    public void 预览与揭晓两个阶段都会接管并且能交回主程序()
    {
        var provider = BuildPresenterProvider();
        var presenter = ActivatorUtilities.CreateInstance<RecruitResultPresenter>(provider);

        var reveal = new DrawPresentationRequest
        {
            Channel = DrawPresentationChannel.RollCall,
            Phase = DrawPresentationPhase.Reveal,
            Students = [new Student { Name = "小鸟游星野" }],
            ListName = "名单"
        };

        Assert.True(presenter.CanPresent(reveal));

        // 预览阶段（抽取中）也要接管：整条演出都在插件手里。
        var preview = reveal with { Phase = DrawPresentationPhase.Preview, Students = [], RequestedCount = 3 };
        Assert.True(presenter.CanPresent(preview));

        // 连要开几张牌都不知道就摆不出牌面，交回主程序。
        Assert.False(presenter.CanPresent(preview with { RequestedCount = 0 }));

        // 一次结果都没有时不接管。
        Assert.False(presenter.CanPresent(reveal with { Students = [] }));

        // 用户在插件设置里关掉结果展示后，界面交回主程序。
        provider.GetRequiredService<PluginState>().Recruit.Enabled = false;
        Assert.False(presenter.CanPresent(reveal));
        Assert.False(presenter.CanPresent(preview));
    }

    [AvaloniaFact]
    public void 预览只开一次窗揭晓接着同一批牌往下演()
    {
        var provider = BuildPresenterProvider();
        var presenter = ActivatorUtilities.CreateInstance<RecruitResultPresenter>(provider);
        var notifier = provider.GetRequiredService<DrawNotifier>();
        var state = provider.GetRequiredService<PluginState>();

        var preview = new DrawPresentationRequest
        {
            Channel = DrawPresentationChannel.RollCall,
            Phase = DrawPresentationPhase.Preview,
            // 主程序的预览阶段也是把滚动中的候选学生塞进请求里的，张数就是这一轮要抽的张数。
            Students = [new Student { Name = "滚动中的学生甲" }, new Student { Name = "滚动中的学生乙" }],
            RequestedCount = 2,
            ListName = "名单"
        };

        Assert.True(presenter.PresentAsync(preview).GetAwaiter().GetResult().Handled);
        Dispatcher.UIThread.RunJobs();

        var window = notifier.CurrentWindow;
        Assert.NotNull(window);
        Assert.True(window!.HasPlayedPreview);
        Assert.Equal(2, window.Letters.Count);
        // 预览阶段还不算"展示过结果"。
        Assert.Equal(0, state.DrawCount);

        // 主程序的预览阶段会反复触发：第二次既不能重播，也不能换窗。
        Assert.True(presenter.PresentAsync(preview).GetAwaiter().GetResult().Handled);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(window, notifier.CurrentWindow);
        Assert.Equal(2, window.Letters.Count);

        // 揭晓接着预览那一批牌：张数一致就原地换名字，牌不再重飞。
        var reveal = preview with
        {
            Phase = DrawPresentationPhase.Reveal,
            Students = [new Student { Name = "小鸟游星野" }, new Student { Name = "砂狼白子" }]
        };
        Assert.True(presenter.PresentAsync(reveal).GetAwaiter().GetResult().Handled);

        Assert.Same(window, notifier.CurrentWindow);
        Assert.Equal(2, window.Letters.Count);
        Assert.Equal("小鸟游星野", window.Letters[0].DisplayName);
        Assert.Equal("砂狼白子", window.Letters[1].DisplayName);
        Assert.Equal(1, state.DrawCount);

        window.ForceClose();
    }

    [AvaloniaFact]
    public void 两段式推进时飞入段不揭名揭名段从头开始()
    {
        var settings = Settings();
        settings.Sanitized();
        var letters = Letters(3);

        // 飞入段：牌在动，名字一律不动。
        RecruitAnimator.ApplyEnterAt(letters, settings, 600);
        Assert.Equal(1d, letters[0].Opacity, 6);
        foreach (var letter in letters)
            Assert.Equal(0d, NameLayerOf(letter).Opacity, 6);

        // 揭名段的 0 时刻：所有牌都钉在就位状态，名字还没揭。
        RecruitAnimator.ApplyRevealAt(letters, settings, 0);
        foreach (var letter in letters)
        {
            Assert.Equal(1d, letter.Opacity, 6);
            Assert.Equal(0d, NameLayerOf(letter).Opacity, 6);
        }

        // 揭名段推进半个错峰：第一张开始揭，第二张还没轮到。
        RecruitAnimator.ApplyRevealAt(letters, settings, settings.RevealDelayMs + (settings.CardDelayMs / 2d));
        Assert.InRange(NameLayerOf(letters[0]).Opacity, 0.01, 0.999);
        Assert.Equal(0d, NameLayerOf(letters[1]).Opacity, 6);

        // 两段之和必须正好等于整条时间线，否则分段播放会头尾错位。
        Assert.Equal(
            RecruitAnimator.GetTotalDurationMs(3, settings),
            RecruitAnimator.GetEnterDurationMs(3, settings) + RecruitAnimator.GetRevealDurationMs(3, settings));
    }

    [AvaloniaFact]
    public void 呈现器接管揭晓后会告诉主程序跳过它自己的动画()
    {
        var provider = BuildPresenterProvider();
        var presenter = ActivatorUtilities.CreateInstance<RecruitResultPresenter>(provider);

        var request = new DrawPresentationRequest
        {
            Channel = DrawPresentationChannel.Lottery,
            Phase = DrawPresentationPhase.Reveal,
            Prizes = [new Prize { Name = "一等奖" }],
            PrizeListName = "奖池"
        };

        Assert.True(presenter.CanPresent(request));

        var decision = presenter.PresentAsync(request).GetAwaiter().GetResult();

        Assert.True(decision.Handled);
        // hideHostResult 保持 false：关掉插件结果窗之后，主程序界面上还留着结果。
        Assert.False(decision.HideHostResult);
    }

    [AvaloniaFact]
    public void 抽奖结果会优先展示奖品没有奖品才退回学生()
    {
        var provider = BuildPresenterProvider();
        var presenter = ActivatorUtilities.CreateInstance<RecruitResultPresenter>(provider);

        var prizesOnly = new DrawPresentationRequest
        {
            Channel = DrawPresentationChannel.Lottery,
            Phase = DrawPresentationPhase.Reveal,
            Prizes = [new Prize { Name = "一等奖" }],
            AssignedStudents = [new Student { Name = "小鸟游星野" }]
        };
        var studentsOnly = new DrawPresentationRequest
        {
            Channel = DrawPresentationChannel.Lottery,
            Phase = DrawPresentationPhase.Reveal,
            AssignedStudents = [new Student { Name = "小鸟游星野" }]
        };

        Assert.True(presenter.CanPresent(prizesOnly));
        Assert.True(presenter.CanPresent(studentsOnly));
        Assert.True(presenter.PresentAsync(studentsOnly).GetAwaiter().GetResult().Handled);
    }

    private static ServiceProvider BuildPresenterProvider(bool withStages = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new PluginState());
        services.AddSingleton(new PluginStateStore(Path.Combine(Path.GetTempPath(), "ba-flip-tests")));
        if (withStages)
            services.AddSingleton(new RecruitStageRegistry());

        services.AddSingleton<DrawNotifier>();
        return services.BuildServiceProvider();
    }

    [AvaloniaFact]
    public void 插槽贡献项按通道映射到三条结果区插槽()
    {
        var registry = new RecruitStageRegistry();

        Assert.Equal(HostUiSlots.RollCallResultExtra,
            new RecruitStageContribution(registry, DrawPresentationChannel.RollCall).SlotId);
        Assert.Equal(HostUiSlots.LotteryResultExtra,
            new RecruitStageContribution(registry, DrawPresentationChannel.Lottery).SlotId);
        Assert.Equal(HostUiSlots.QuickDrawResultExtra,
            new RecruitStageContribution(registry, DrawPresentationChannel.QuickDraw).SlotId);

        // 远程与移动端没有结果区插槽：贡献项落到空串上，宿主会忽略它，动画走插件自己的窗口。
        Assert.Equal(string.Empty,
            new RecruitStageContribution(registry, DrawPresentationChannel.RemoteDraw).SlotId);

        var contribution = new RecruitStageContribution(registry, DrawPresentationChannel.RollCall);
        Assert.Equal(UiSlotKind.Append, contribution.Kind);
        Assert.Contains("stage.RollCall", contribution.Id);
    }

    [AvaloniaFact]
    public void 舞台登记表换新时收掉旧舞台并且注销不误伤()
    {
        var registry = new RecruitStageRegistry();
        var first = new RecruitOverlayWindow();
        first.TakeHostedContent();
        var second = new RecruitOverlayWindow();
        second.TakeHostedContent();

        registry.Register(DrawPresentationChannel.RollCall, first);
        Assert.Same(first, registry.Get(DrawPresentationChannel.RollCall));
        Assert.True(registry.HasAny);

        registry.Register(DrawPresentationChannel.RollCall, second);
        Assert.Same(second, registry.Get(DrawPresentationChannel.RollCall));

        // 页面销毁时旧舞台才回来注销，不能把新舞台顶掉。
        registry.Unregister(DrawPresentationChannel.RollCall, first);
        Assert.Same(second, registry.Get(DrawPresentationChannel.RollCall));

        registry.Unregister(DrawPresentationChannel.RollCall, second);
        Assert.Null(registry.Get(DrawPresentationChannel.RollCall));
        Assert.False(registry.HasAny);
    }

    [AvaloniaFact]
    public void 就地舞台挂进结果区预览与揭晓都演在那里()
    {
        var provider = BuildPresenterProvider(withStages: true);
        var registry = provider.GetRequiredService<RecruitStageRegistry>();
        var notifier = provider.GetRequiredService<DrawNotifier>();
        var presenter = ActivatorUtilities.CreateInstance<RecruitResultPresenter>(provider);
        var state = provider.GetRequiredService<PluginState>();

        // 宿主插槽就是这么用贡献项的：CreateContent 拿控件挂进结果区。
        var contribution = new RecruitStageContribution(registry, DrawPresentationChannel.RollCall);
        var root = contribution.CreateContent(new UiSlotContext(contribution.SlotId));
        Assert.NotNull(root);

        var stage = registry.Get(DrawPresentationChannel.RollCall);
        Assert.NotNull(stage);
        Assert.True(stage!.IsHosted);
        Assert.False(root!.IsVisible); // 没抽签的时候舞台是隐形的

        var preview = new DrawPresentationRequest
        {
            Channel = DrawPresentationChannel.RollCall,
            Phase = DrawPresentationPhase.Preview,
            Students = [new Student { Name = "滚动中的学生甲" }, new Student { Name = "滚动中的学生乙" }],
            RequestedCount = 2,
            ListName = "名单"
        };

        var decision = presenter.PresentAsync(preview).GetAwaiter().GetResult();
        Assert.True(decision.Handled);
        // 就地渲染要请主程序藏掉它自己的结果，否则两套牌面叠在同一块地方。
        Assert.True(decision.HideHostResult);

        Assert.Same(stage, notifier.CurrentWindow);
        Assert.True(stage.IsRoundOpen);
        Assert.True(root.IsVisible);
        Assert.False(stage.IsVisible); // 就地渲染不开插件自己的窗口
        Assert.Equal(2, stage.Letters.Count);
        Assert.Equal(0, state.DrawCount); // 预览还不算"展示过结果"

        // 揭晓接着预览那一批牌往下演。
        var reveal = preview with
        {
            Phase = DrawPresentationPhase.Reveal,
            Students = [new Student { Name = "小鸟游星野" }, new Student { Name = "砂狼白子" }]
        };
        Assert.True(presenter.PresentAsync(reveal).GetAwaiter().GetResult().Handled);
        Assert.Equal("小鸟游星野", stage.Letters[0].DisplayName);
        Assert.Equal("砂狼白子", stage.Letters[1].DisplayName);
        Assert.Equal(1, state.DrawCount);

        // 演完留在结果区里（跟主程序自己的结果一样）。
        Assert.True(root.IsVisible);

        // 下一轮揭晓（张数不同）：重新摆牌，不叠在上一轮的牌面上。
        var next = reveal with { Students = [new Student { Name = "小涂真纪" }] };
        Assert.True(presenter.PresentAsync(next).GetAwaiter().GetResult().Handled);
        Assert.Single(stage.Letters);
        Assert.Equal("小涂真纪", stage.Letters[0].DisplayName);
        Assert.Equal(2, state.DrawCount);
        Assert.False(stage.IsVisible); // 全程都没有开插件自己的窗口

        // 收起就地舞台：只把内容藏起来，实例留着演下一轮。
        stage.ForceClose();
        Assert.False(root.IsVisible);
        stage.BeginRound();
        Assert.True(root.IsVisible);
        Assert.True(stage.IsRoundOpen);
    }

    [AvaloniaFact]
    public void 宿主没有插槽时退回插件自己的窗口()
    {
        var provider = BuildPresenterProvider();
        var notifier = provider.GetRequiredService<DrawNotifier>();
        var presenter = ActivatorUtilities.CreateInstance<RecruitResultPresenter>(provider);

        Assert.False(notifier.UsesInPlaceStage(DrawPresentationChannel.RollCall));

        var request = new DrawPresentationRequest
        {
            Channel = DrawPresentationChannel.Lottery,
            Phase = DrawPresentationPhase.Reveal,
            Prizes = [new Prize { Name = "一等奖" }],
            PrizeListName = "奖池"
        };

        var decision = presenter.PresentAsync(request).GetAwaiter().GetResult();
        Assert.True(decision.Handled);
        // 没有插槽时主程序的结果留在下面，关掉插件窗口界面上还有东西。
        Assert.False(decision.HideHostResult);

        Dispatcher.UIThread.RunJobs();
        var window = notifier.CurrentWindow;
        Assert.NotNull(window);
        Assert.False(window!.IsHosted);
        window.ForceClose();
    }
    /// <summary>
    ///     回归用：定格之后必须真的画出东西。
    ///     之前 XAML 里给 PART_Root 写了 Opacity=0，而代码淡入的是控件自身，
    ///     两层透明度相乘导致卡片永远全透明——属性断言全过，画面却是空的。
    /// </summary>
    [AvaloniaFact]
    public void 定格的信件会真的画出来()
    {
        var letter = new RecruitLetter { DisplayName = "小鸟游星野", Rarity = LetterRarity.Rainbow };
        letter.ApplySettled();

        var window = new Window
        {
            Width = 260,
            Height = 200,
            Background = new SolidColorBrush(Color.Parse("#FF0B1330")),
            Content = letter
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var bright = CountBrightPixels(window);

        // 信封底图与白底名牌都是浅色，深蓝背景上应当占到相当一块。
        Assert.True(bright > 2000, $"定格画面里亮色像素只有 {bright} 个，卡片很可能没画出来。");
        window.Close();
    }

    private static int CountBrightPixels(Window window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        var size = frame.PixelSize;
        var stride = size.Width * 4;
        var buffer = new byte[stride * size.Height];

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            frame.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), buffer.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        var bright = 0;
        for (var i = 0; i + 3 < buffer.Length; i += 4)
        {
            // BGRA
            if (buffer[i] > 180 && buffer[i + 1] > 180 && buffer[i + 2] > 180)
                bright++;
        }

        return bright;
    }

    private sealed class FakeCommitService : IDrawCommitService
    {
        public static int StudentCommits;
        public static int LotteryCommits;

        public string CommitStudentDraw(StudentDrawCommit commit)
        {
            StudentCommits++;
            return "round-1";
        }

        public string CommitLotteryDraw(LotteryDrawCommit commit)
        {
            LotteryCommits++;
            return "round-2";
        }
    }

    private static TransformGroup GroupOf(RecruitLetter letter, string name) =>
        (TransformGroup)letter.FindControl<Grid>(name)!.RenderTransform!;

    private static ScaleTransform CardScaleOf(RecruitLetter letter) => (ScaleTransform)GroupOf(letter, "PART_Root").Children[0];

    private static TranslateTransform CardTranslateOf(RecruitLetter letter) => (TranslateTransform)GroupOf(letter, "PART_Root").Children[1];

    private static RotateTransform CardRotateOf(RecruitLetter letter) => (RotateTransform)GroupOf(letter, "PART_Root").Children[2];

    private static ScaleTransform NameScaleOf(RecruitLetter letter) => (ScaleTransform)GroupOf(letter, "PART_NameLayer").Children[0];

    private static TranslateTransform NameTranslateOf(RecruitLetter letter) => (TranslateTransform)GroupOf(letter, "PART_NameLayer").Children[1];

    private static Control NameLayerOf(RecruitLetter letter) => letter.FindControl<Grid>("PART_NameLayer")!;

    private static TextBlock NameTextOf(RecruitLetter letter) => letter.FindControl<TextBlock>("PART_Name")!;
}
