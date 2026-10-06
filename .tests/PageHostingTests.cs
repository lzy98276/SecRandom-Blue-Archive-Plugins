using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.BlueArchiveFlip.Models;
using SecRandom.BlueArchiveFlip.Services;
using SecRandom.BlueArchiveFlip.ViewModels;
using SecRandom.BlueArchiveFlip.Views.SettingsPages;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.PluginSdk;

namespace SecRandom.BlueArchiveFlip.Tests;

/// <summary>
///     验证宿主真正会走的那条路：插件初始化时注册结果呈现器、注册设置页，
///     设置页由依赖注入创建、XAML 能加载、两种主题都看得清。
/// </summary>
public class PluginHostingTests
{
    private static readonly string SnapshotDirectory = Path.Combine(AppContext.BaseDirectory, "snapshots");

    /// <summary>
    ///     注意：宿主把页面注册表放在静态字段里，同一个插件 id 注册两次会抛
    ///     "此设置页面id ... 已经被占用"，所以整个测试进程里只驱动一次 <see cref="Plugin.Initialize" />。
    /// </summary>
    [AvaloniaFact]
    public void 插件初始化会注册结果呈现器并注册设置页()
    {
        FakeCommitService.StudentCommits = 0;
        var configFolder = NewConfigFolder();

        try
        {
            var plugin = new Plugin();
            SetPluginConfigFolder(plugin, configFolder);

            // 和宿主一样：先注册宿主自己的服务，再让插件往同一个集合里加东西。
            IServiceCollection? registered = null;
            using var host = new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddLogging();
                    services.AddSingleton<IDrawCommitService, FakeCommitService>();
                    plugin.Initialize(context, services);
                    registered = services;
                })
                .Build();

            // 插件自己的状态：状态文件落在宿主给的配置目录里，并记了一次加载。
            Assert.Equal(Path.Combine(configFolder, "state.json"), plugin.StateFilePath);
            Assert.True(File.Exists(plugin.StateFilePath));
            Assert.Equal(1, plugin.State.LoadCount);

            // 结果呈现器装上了：宿主会把插件注册的 IDrawResultPresenter 收集起来。
            Assert.True(plugin.IsPresenterRegistered);
            Assert.Contains(
                host.Services.GetServices<IDrawResultPresenter>(),
                presenter => presenter is RecruitResultPresenter);

            // 动画样式贡献项也装上了：宿主把它填进「动画样式」下拉，选中它才会来找呈现器。
            var contribution = Assert.Single(host.Services.GetServices<IDrawAnimationContribution>());
            Assert.IsType<RecruitAnimationContribution>(contribution);
            Assert.Equal(RecruitResultPresenter.PresenterId, contribution.Id);

            // 两处 id 必须一致，否则下拉里选中了也没人接管。
            var recruitPresenter = Assert.Single(
                host.Services.GetServices<IDrawResultPresenter>().OfType<RecruitResultPresenter>());
            Assert.Equal(recruitPresenter.Id, contribution.Id);
            Assert.False(string.IsNullOrWhiteSpace(contribution.DisplayName));

            // 设置页按插件 id 注册成带 key 的 UserControl，宿主就是按这个 key 取页面的。
            Assert.NotNull(registered);
            Assert.Contains(
                registered!,
                descriptor => descriptor.ServiceType == typeof(UserControl) && Equals(descriptor.ServiceKey, Plugin.SettingsPageId));

            var previousHost = IAppHost.Host;
            IAppHost.Host = host;
            try
            {
                var page = Assert.IsType<RecruitSettingsPage>(host.Services.GetRequiredKeyedService<UserControl>(Plugin.SettingsPageId));
                Assert.IsType<RecruitSettingsViewModel>(page.DataContext);

                var presenter = Assert.Single(
                    host.Services.GetServices<IDrawResultPresenter>().OfType<RecruitResultPresenter>());
                var request = new DrawPresentationRequest
                {
                    Channel = DrawPresentationChannel.RollCall,
                    Phase = DrawPresentationPhase.Reveal,
                    Students = [new SecRandom.Shared.Models.Profile.Student { Name = "小鸟游星野" }],
                    ListName = "名单"
                };

                // 关掉总开关时呈现器不接管，界面交回主程序。
                plugin.State.Recruit.Enabled = false;
                Assert.False(presenter.CanPresent(request));

                // 打开总开关后，呈现器接管并弹出招募结果窗，同时记一次抽签。
                plugin.State.Recruit.Enabled = true;
                plugin.State.Recruit.PanelDurationMs = 0;
                plugin.State.Recruit.CardDurationMs = RecruitSettings.CardDurationRange.Min;
                var decision = presenter.PresentAsync(request).GetAwaiter().GetResult();
                Assert.True(decision.Handled);

                Dispatcher.UIThread.RunJobs();

                Assert.Equal(1, plugin.State.DrawCount);
                Assert.NotNull(plugin.ActiveRecruitWindow);
                plugin.CloseActiveRecruitWindow();
            }
            finally
            {
                IAppHost.Host = previousHost;
            }
        }
        finally
        {
            DeleteConfigFolder(configFolder);
        }
    }

    /// <summary>设置页在浅色 / 深色两套主题下的样子。</summary>
    [AvaloniaFact]
    public void 设置页在两种主题下都能渲染()
    {
        var configFolder = NewConfigFolder();

        try
        {
            using var host = new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddSingleton(new PluginStateStore(configFolder));
                    services.AddSingleton(new PluginState());
                    services.AddSingleton<RecruitSettingsViewModel>();
                })
                .Build();

            var previousHost = IAppHost.Host;
            IAppHost.Host = host;
            try
            {
                var page = new RecruitSettingsPage();
                Assert.IsType<RecruitSettingsViewModel>(page.DataContext);

                var window = new Window
                {
                    Width = 1180,
                    Height = 940,
                    Content = page
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Directory.CreateDirectory(SnapshotDirectory);
                foreach (var (variant, fileName) in new[]
                         {
                             (ThemeVariant.Light, "settings-light.png"),
                             (ThemeVariant.Dark, "settings-dark.png")
                         })
                {
                    window.RequestedThemeVariant = variant;
                    Dispatcher.UIThread.RunJobs();
                    window.CaptureRenderedFrame()!.Save(Path.Combine(SnapshotDirectory, fileName));
                }

                window.Close();
            }
            finally
            {
                IAppHost.Host = previousHost;
            }
        }
        finally
        {
            DeleteConfigFolder(configFolder);
        }
    }

    /// <summary>宿主建造 Host 之前会先把配置目录塞进插件的这个属性（internal set，只能反射写）。</summary>
    private static void SetPluginConfigFolder(Plugin plugin, string folder)
    {
        var property = typeof(PluginBase).GetProperty(nameof(PluginBase.PluginConfigFolder))!;
        property.GetSetMethod(nonPublic: true)!.Invoke(plugin, [folder]);
    }

    private static string NewConfigFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "baflip-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void DeleteConfigFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录删不掉不影响测试结论。
        }
    }

    private sealed class FakeCommitService : IDrawCommitService
    {
        public static int StudentCommits;

        public string CommitStudentDraw(StudentDrawCommit commit)
        {
            StudentCommits++;
            return "round-1";
        }

        public string CommitLotteryDraw(LotteryDrawCommit commit) => "round-2";
    }
}
