using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.BlueArchiveFlip.Services;
using SecRandom.BlueArchiveFlip.ViewModels;
using SecRandom.BlueArchiveFlip.Views;
using SecRandom.BlueArchiveFlip.Views.SettingsPages;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Core.Abstraction.Services.Views;
using SecRandom.Core.Extensions.Registry;
using SecRandom.PluginSdk;

namespace SecRandom.BlueArchiveFlip;

/// <summary>
///     插件入口。
///     <para>
///         宿主按 <c>manifest.yml</c> 的 <c>entranceAssembly</c> 找到入口程序集，取其中第一个非抽象的
///         <see cref="PluginBase" /> 实现并实例化，所以这个类必须有公开无参构造函数。
///     </para>
///     <para>
///         插件不注册主页面：它挂在主程序自己的点名 / 抽奖流程上。挂载点有两个——
///         ① 主程序 3.1 起的插件结果呈现扩展点
///         <see cref="SecRandom.Core.Abstraction.Services.Presentation.IDrawResultPresenter" />：
///         点名、抽奖、快捷抽签、移动端四个通道共用同一个入口，插件接管「预览 + 揭晓」两步，
///         主程序便跳过它自己的两段动画；
///         ② 主程序 3.2 起的结果区插槽（见 <see cref="RecruitStageContribution" />）：招募动画直接演在
///         主程序结果区里，看起来就是主程序自己的结果动画被换掉了。没有插槽（老宿主、移动端）时
///         退回插件自己的全屏窗口，两条路都不影响抽签结果本身。
///     </para>
/// </summary>
public sealed class Plugin : PluginBase
{
    /// <summary>设置页面 id，必须全局唯一。</summary>
    public const string SettingsPageId = "plugin.cn.sectl.bluearchive.settings";

    /// <summary>界面上显示的插件名。</summary>
    public const string DisplayName = "碧蓝档案招募";

    private const string AnimationContributionTypeName =
        "SecRandom.Core.Abstraction.Services.Presentation.IDrawAnimationContribution";

    private const string UiContributionTypeName =
        "SecRandom.Core.Abstraction.Services.Views.IUiContentContribution";

    /// <summary>要挂就地舞台的通道：桌面端点名 / 抽奖 / 快捷抽签各有一条结果区插槽。</summary>
    private static readonly DrawPresentationChannel[] StageChannels =
    [
        DrawPresentationChannel.RollCall,
        DrawPresentationChannel.Lottery,
        DrawPresentationChannel.QuickDraw
    ];

    private PluginStateStore? _stateStore;
    private bool _presenterRegistered;
    private bool _animationContributionRegistered;
    private bool _stageContributionsRegistered;

    /// <summary>插件状态（加载次数、展示次数、结果动画参数）。</summary>
    public PluginState State { get; private set; } = new();

    /// <summary>插件配置目录下的状态文件路径，设置页会展示它，方便排查问题。</summary>
    public string StateFilePath => _stateStore?.FilePath ?? string.Empty;

    /// <summary>结果呈现器是否已注册；没注册上插件只剩设置页。</summary>
    public bool IsPresenterRegistered => _presenterRegistered;

    /// <summary>「动画样式」贡献项是否已注册；老宿主没有这个扩展点时保持 false。</summary>
    public bool IsAnimationContributionRegistered => _animationContributionRegistered;

    /// <summary>结果区插槽贡献项是否已注册；注册成功说明动画是就地演在主程序结果区里的。</summary>
    public bool IsStageContributionRegistered => _stageContributionsRegistered;

    /// <summary>当前挂在屏幕上的招募结果窗；没有结果在展示时为 null。</summary>
    public RecruitOverlayWindow? ActiveRecruitWindow => IAppHost.TryGetService<DrawNotifier>()?.CurrentWindow;

    /// <summary>立刻收起正在展示的结果窗（设置页与诊断用）。</summary>
    public void CloseActiveRecruitWindow()
    {
        ActiveRecruitWindow?.ForceClose();
    }

    private static ILogger<Plugin>? Logger => IAppHost.TryGetService<ILogger<Plugin>>();

    /// <summary>
    ///     宿主是否带「动画样式」扩展点。
    ///     <para>
    ///         只能用反射问：直接写 <c>typeof(IDrawAnimationContribution)</c> 或泛型调用会在方法被 JIT 时
    ///         抛 <c>TypeLoadException</c>，那个异常发生在编译期，方法内的 try/catch 也拦不住，
    ///         所以必须"探测"与"使用"分在两个方法里。
    ///     </para>
    /// </summary>
    private static bool HostSupportsAnimationContribution()
    {
        try
        {
            if (Type.GetType($"{AnimationContributionTypeName}, SecRandom.Core", throwOnError: false) is not null)
                return true;

            return AppDomain.CurrentDomain.GetAssemblies().Any(
                assembly => assembly.GetType(AnimationContributionTypeName, throwOnError: false) is not null);
        }
        catch (Exception)
        {
            // 探测本身不该影响插件加载。
            return false;
        }
    }

    /// <summary>
    ///     注册动画样式贡献项。<b>只允许</b>在 <see cref="HostSupportsAnimationContribution" /> 为 true 时调用：
    ///     方法体里引用了宿主新类型，一旦在不支持的宿主上被 JIT 就会抛异常。
    /// </summary>
    private static void RegisterAnimationContribution(IServiceCollection services)
    {
        services.AddSingleton<IDrawAnimationContribution, RecruitAnimationContribution>();
    }

    /// <summary>
    ///     宿主是否有结果区插槽扩展点（3.2 起）。理由同 <see cref="HostSupportsAnimationContribution" />：反射探测。
    /// </summary>
    private static bool HostSupportsUiSlots()
    {
        try
        {
            if (Type.GetType($"{UiContributionTypeName}, SecRandom.Core", throwOnError: false) is not null)
                return true;

            return AppDomain.CurrentDomain.GetAssemblies().Any(
                assembly => assembly.GetType(UiContributionTypeName, throwOnError: false) is not null);
        }
        catch (Exception)
        {
            // 探测本身不该影响插件加载。
            return false;
        }
    }

    /// <summary>
    ///     注册三个结果区插槽贡献项（点名 / 抽奖 / 快捷抽签）。<b>只允许</b>在 <see cref="HostSupportsUiSlots" />
    ///     为 true 时调用：方法体与参数类型里引用了宿主新类型，不支持的宿主上被 JIT 会抛异常。
    /// </summary>
    private static void RegisterStageContributions(IServiceCollection services, RecruitStageRegistry registry)
    {
        foreach (var channel in StageChannels)
            services.AddSingleton<IUiContentContribution>(new RecruitStageContribution(registry, channel));
    }

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // Info / PluginConfigFolder 由宿主在调用 Initialize 之前赋值。
        _stateStore = new PluginStateStore(PluginConfigFolder);
        State = _stateStore.Load();
        State.LoadCount++;
        State.LastMessage = $"插件已加载（第 {State.LoadCount} 次）";
        _stateStore.Save(State);

        // 状态存储与共享状态都是单例：抽签钩子和设置页操作的是同一份对象。
        services.AddSingleton(_stateStore);
        services.AddSingleton(State);

        // 就地舞台登记表：插槽建好舞台后登记在这里，抽签时按通道取用。
        var stageRegistry = new RecruitStageRegistry();
        services.AddSingleton(stageRegistry);

        // 结果展示本身。
        services.AddSingleton<DrawNotifier>();

        // 关键一步：注册结果呈现器，接管点名 / 抽奖 / 快捷抽签 / 移动端的「预览 + 揭晓」。
        services.AddSingleton<IDrawResultPresenter, RecruitResultPresenter>();
        _presenterRegistered = true;

        // 把招募动画接进「动画样式」下拉框（设置 → 默认抽取设置）：选它才接管，否则走主程序内置动画。
        // 老宿主（3.1 之前没有这个扩展点）上类型根本不存在，硬引用会在 JIT 时抛 TypeLoadException
        // 把整个插件带崩，所以先用反射探一下，再交给单独的方法去注册。
        if (HostSupportsAnimationContribution())
        {
            RegisterAnimationContribution(services);
            _animationContributionRegistered = true;
        }

        // 结果区插槽（3.2 起）：把舞台挂进主程序结果区，动画就地渲染而不是另开浮窗。
        if (HostSupportsUiSlots())
        {
            RegisterStageContributions(services, stageRegistry);
            _stageContributionsRegistered = true;
        }

        services.AddSingleton<RecruitSettingsViewModel>();
        services.AddSettingsPage<RecruitSettingsPage>(DisplayName);
    }

    public override void OnAppStarted()
    {
        // 此时 Host 已构建完成，可以解析宿主服务。
        if (_presenterRegistered)
        {
            Logger?.LogInformation(
                "{DisplayName} 已启动：结果呈现器已注册，动画样式贡献项 {AnimationState}，就地渲染 {StageState}，设置页 {SettingsPageId} 已注册。",
                DisplayName,
                _animationContributionRegistered ? "已注册" : "未注册（宿主没有这个扩展点）",
                _stageContributionsRegistered ? "已启用（演在主程序结果区）" : "未启用（退回插件窗口）",
                SettingsPageId);
        }
        else
        {
            Logger?.LogWarning(
                "{DisplayName} 没能注册结果呈现器，插件暂时不会展示结果。",
                DisplayName);
        }
    }

    public override void OnAppStopping()
    {
        if (_stateStore is null)
            return;

        State.LastMessage = "应用正在退出";
        _stateStore.Save(State);
    }

    public override ValueTask DisposeAsync()
    {
        // 插件没有长期持有的窗口或句柄，不需要额外释放。
        return ValueTask.CompletedTask;
    }
}
