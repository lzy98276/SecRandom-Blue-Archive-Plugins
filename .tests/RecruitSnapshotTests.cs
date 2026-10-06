using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SecRandom.BlueArchiveFlip.Animation;
using SecRandom.BlueArchiveFlip.Controls;
using SecRandom.BlueArchiveFlip.Models;
using SecRandom.BlueArchiveFlip.Views;

namespace SecRandom.BlueArchiveFlip.Tests;

/// <summary>把招募结果窗的几个关键帧渲染成 PNG，用来目视确认排版和姿态。</summary>
public class RecruitSnapshotTests
{
    private static readonly string Directory =
        Path.Combine(AppContext.BaseDirectory, "snapshots");

    private static void Save(TopLevel topLevel, string fileName)
    {
        System.IO.Directory.CreateDirectory(Directory);
        topLevel.CaptureRenderedFrame()!.Save(Path.Combine(Directory, fileName), PngBitmapEncoderOptions.Default);
    }

    private static RecruitItem[] Classmates(int count) =>
    [
        .. new[]
        {
            ("小鸟游星野", "阿拜多斯高中 · 对策委员会"),
            ("砂狼白子", "阿拜多斯高中 · 对策委员会"),
            ("陆八魔阿露", "格黑娜学园 · 便利屋68"),
            ("圣园弥香", "三一综合学园 · 修女会"),
            ("日奈", "格黑娜学园 · 风纪委员会"),
            ("纱织", "阿里乌斯分校"),
            ("真纪", "千年科学学园"),
            ("红叶", "百鬼夜行联合学院"),
            ("野乃美", "阿拜多斯高中"),
            ("白子", "阿拜多斯高中")
        }.Take(count).Select((s, i) => new RecruitItem(s.Item1, (LetterRarity)((i % 3) + 1), s.Item2))
    ];

    /// <summary>三种稀有度的单张信件并排，确认底图确实按档位切换。</summary>
    [AvaloniaFact]
    public void 渲染三种稀有度的信件()
    {
        var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 24 };
        foreach (var rarity in new[] { LetterRarity.Blue, LetterRarity.Gold, LetterRarity.Rainbow })
        {
            var letter = new RecruitLetter
            {
                DisplayName = rarity switch
                {
                    LetterRarity.Blue => "一星 · 蓝",
                    LetterRarity.Gold => "二星 · 金",
                    _ => "三星 · 虹"
                },
                SubText = rarity.ToString(),
                Rarity = rarity
            };
            letter.ApplySettled();
            panel.Children.Add(letter);
        }

        var window = new Window
        {
            Width = 760,
            Height = 300,
            Background = new SolidColorBrush(Color.Parse("#0B1330")),
            Content = new Border { Padding = new Thickness(24), Child = panel }
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Save(window, "recruit-rarities.png");
    }

    /// <summary>十连的定格画面，也就是用户最想看到的最终效果。</summary>
    [AvaloniaFact]
    public void 渲染十连招募定格画面()
    {
        var window = new RecruitOverlayWindow { Width = 1280, Height = 800 };
        window.BuildSettled(Classmates(10), "点名 · 阿拜多斯", new RecruitSettings());
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Save(window, "recruit-ten.png");
        window.Close();
    }

    /// <summary>三张卡的定格画面，用来核对面板留白与提示条。</summary>
    [AvaloniaFact]
    public void 渲染三张卡的定格画面()
    {
        var window = new RecruitOverlayWindow { Width = 1280, Height = 800 };
        window.BuildSettled(Classmates(3), "抽奖 · 一等奖", new RecruitSettings());
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Save(window, "recruit-three.png");
        window.Close();
    }

    /// <summary>把整条时间线抽几帧落盘，确认面板与信件是逐渐出现的。</summary>
    [AvaloniaFact]
    public async Task 渲染招募过程关键帧()
    {
        var settings = new RecruitSettings
        {
            CardDelayMs = 120,
            CardDurationMs = 600,
            RevealDelayMs = 100,
            RevealDurationMs = 300,
            PanelDurationMs = 400
        };

        var window = new RecruitOverlayWindow { Width = 1280, Height = 800 };
        var items = Classmates(5);

        // 先摆好内容，再手动逐帧推进，这样每一帧都能截到。
        window.BuildSettled(items, "点名 · 阿拜多斯", settings);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var letters = window.Letters;
        var total = RecruitAnimator.GetTotalDurationMs(letters.Count, settings);

        for (var frame = 0; frame < 8; frame++)
        {
            var elapsed = total * frame / 7.0;
            RecruitAnimator.ApplyAt(letters, settings, elapsed);
            Dispatcher.UIThread.RunJobs();
            Save(window, $"recruit-frame-{frame:00}.png");
        }

        await Task.CompletedTask;
        window.Close();
    }
}
