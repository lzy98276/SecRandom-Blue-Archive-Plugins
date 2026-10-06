using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SecRandom.BlueArchiveFlip.ViewModels;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;

namespace SecRandom.BlueArchiveFlip.Views.SettingsPages;

/// <summary>
///     招募结果动画的设置页。
///     <para>
///         XAML 编译器要求页面类型有公开的无参构造函数，所以这里和官方示例插件一样：
///         无参构造 + 从 <see cref="IAppHost" /> 取视图模型。页面实例仍由宿主按需创建。
///     </para>
/// </summary>
[PageInfo(Plugin.SettingsPageId, FluentIcons.SettingsFilled, hidePageTitle: true)]
public partial class RecruitSettingsPage : UserControl
{
    private readonly RecruitSettingsViewModel? _viewModel;
    private readonly bool _ready;

    public RecruitSettingsPage()
    {
        _viewModel = IAppHost.TryGetService<RecruitSettingsViewModel>();

        // 先挂 DataContext 再 InitializeComponent，编译期绑定才能在加载时取到值。
        DataContext = _viewModel;
        InitializeComponent();

        // 初始化过程中控件会回写一遍选中项，这时候不该当成"用户改了参数"。
        _ready = true;
    }

    private void OnSliderCommit(object? sender, PointerCaptureLostEventArgs e)
    {
        Commit();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        Commit();
    }

    private void OnToggleCommit(object? sender, RoutedEventArgs e)
    {
        Commit();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.Save();
    }

    private void OnRestoreDefaultsClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.RestoreDefaults();
    }

    private void Commit()
    {
        if (!_ready)
            return;

        _viewModel?.Save();
    }
}
