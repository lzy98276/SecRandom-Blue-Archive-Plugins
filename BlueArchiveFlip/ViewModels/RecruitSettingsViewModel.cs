using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.BlueArchiveFlip.Models;

namespace SecRandom.BlueArchiveFlip.ViewModels;

/// <summary>
///     设置页的视图模型。
///     <para>
///         页面上改的每一项都会立刻写回插件共享的那份 <see cref="RecruitSettings" />（所以下一次抽签马上生效），
///         但只有 <see cref="Save" /> 才会落盘——拖动滑杆不该每帧都写一次 state.json。
///     </para>
/// </summary>
public sealed partial class RecruitSettingsViewModel : ObservableObject
{
    private readonly PluginState _state;
    private readonly PluginStateStore _store;
    private bool _loading;

    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private bool _rarityByTag;
    [ObservableProperty] private int _uniformRarityIndex = 2;
    [ObservableProperty] private double _panelOpacity = 0.9;
    [ObservableProperty] private string _panelBackground = "#FFFFFF";
    [ObservableProperty] private string _panelBorderColor = "#66CCFF";
    [ObservableProperty] private bool _showDeco = true;
    [ObservableProperty] private double _cardDelayMs = 120;
    [ObservableProperty] private double _cardDurationMs = 600;
    [ObservableProperty] private double _revealDelayMs = 100;
    [ObservableProperty] private double _revealDurationMs = 300;
    [ObservableProperty] private double _panelDurationMs = 700;
    [ObservableProperty] private double _autoCloseSeconds;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public RecruitSettingsViewModel(PluginState state, PluginStateStore store)
    {
        _state = state;
        _store = store;
        Reload();
    }

    /// <summary>滑杆的上下限，直接绑到 XAML 上，省得两边各写一份数字。</summary>
    public double OpacityMin => RecruitSettings.OpacityRange.Min / 100d;
    public double OpacityMax => RecruitSettings.OpacityRange.Max / 100d;
    public double CardDelayMax => RecruitSettings.CardDelayRange.Max;
    public double CardDurationMin => RecruitSettings.CardDurationRange.Min;
    public double CardDurationMax => RecruitSettings.CardDurationRange.Max;
    public double RevealDelayMax => RecruitSettings.RevealDelayRange.Max;
    public double RevealDurationMin => RecruitSettings.RevealDurationRange.Min;
    public double RevealDurationMax => RecruitSettings.RevealDurationRange.Max;
    public double PanelDurationMax => RecruitSettings.PanelDurationRange.Max;
    public double AutoCloseMax => RecruitSettings.AutoCloseRange.Max;

    /// <summary>
    ///     一行摘要，给用户一个"这些数字大概意味着多长"的直观感受。
    ///     按 10 张牌估算：牌面不写死每行几张、也不再截断张数，实际时长只跟张数与错峰有关。
    /// </summary>
    public string TimelineSummary
    {
        get
        {
            var settings = BuildSettings();
            var total = Animation.RecruitAnimator.GetTotalDurationMs(10, settings);
            return $"10 张牌大约播 {total / 1000d:0.0} 秒。";
        }
    }

    public int LoadCount => _state.LoadCount;
    public int DrawCount => _state.DrawCount;
    public string LastMessage => _state.LastMessage;
    public string StateFilePath => _store.FilePath;

    /// <summary>稀有度决定方式的下拉框索引：0 = 统一档位，1 = 按标签猜。</summary>
    public int RarityModeIndex
    {
        get => RarityByTag ? 1 : 0;
        set => RarityByTag = value == 1;
    }

    /// <summary>从共享状态里把当前值读进视图模型。</summary>
    public void Reload()
    {
        _loading = true;
        try
        {
            var settings = _state.Recruit.Sanitized();

            Enabled = settings.Enabled;
            RarityByTag = settings.RarityMode == RarityMode.ByTag;
            UniformRarityIndex = settings.UniformRarity switch
            {
                LetterRarity.Blue => 0,
                LetterRarity.Gold => 1,
                _ => 2
            };
            PanelOpacity = settings.PanelOpacity;
            PanelBackground = settings.PanelBackground;
            PanelBorderColor = settings.PanelBorderColor;
            ShowDeco = settings.ShowDeco;
            CardDelayMs = settings.CardDelayMs;
            CardDurationMs = settings.CardDurationMs;
            RevealDelayMs = settings.RevealDelayMs;
            RevealDurationMs = settings.RevealDurationMs;
            PanelDurationMs = settings.PanelDurationMs;
            AutoCloseSeconds = settings.AutoCloseSeconds;
            StatusMessage = $"上次保存：{(settings.Enabled ? "结果动画已开启" : "结果动画已关闭")}";
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>把视图模型的值写回共享设置，并落盘。</summary>
    public void Save()
    {
        var settings = BuildSettings();
        Apply(_state.Recruit, settings);
        _store.Save(_state);
        StatusMessage = $"已保存（{DateTime.Now:HH:mm:ss}）";
    }

    /// <summary>恢复默认参数，并立刻落盘。</summary>
    public void RestoreDefaults()
    {
        var defaults = new RecruitSettings().Sanitized();
        Apply(_state.Recruit, defaults);
        _store.Save(_state);
        Reload();
        StatusMessage = "已恢复默认参数。";
    }

    /// <summary>把当前界面的值组装成一份设置（不落盘）。</summary>
    public RecruitSettings BuildSettings()
    {
        var rarity = UniformRarityIndex switch
        {
            0 => LetterRarity.Blue,
            1 => LetterRarity.Gold,
            _ => LetterRarity.Rainbow
        };

        return new RecruitSettings
        {
            Enabled = Enabled,
            RarityMode = RarityByTag ? RarityMode.ByTag : RarityMode.Uniform,
            UniformRarity = rarity,
            PanelOpacity = PanelOpacity,
            PanelBackground = PanelBackground,
            PanelBorderColor = PanelBorderColor,
            ShowDeco = ShowDeco,
            CardDelayMs = (int)Math.Round(CardDelayMs),
            CardDurationMs = (int)Math.Round(CardDurationMs),
            RevealDelayMs = (int)Math.Round(RevealDelayMs),
            RevealDurationMs = (int)Math.Round(RevealDurationMs),
            PanelDurationMs = (int)Math.Round(PanelDurationMs),
            AutoCloseSeconds = (int)Math.Round(AutoCloseSeconds)
        }.Sanitized();
    }

    private static void Apply(RecruitSettings target, RecruitSettings source)
    {
        target.Enabled = source.Enabled;
        target.RarityMode = source.RarityMode;
        target.UniformRarity = source.UniformRarity;
        target.PanelOpacity = source.PanelOpacity;
        target.PanelBackground = source.PanelBackground;
        target.PanelBorderColor = source.PanelBorderColor;
        target.ShowDeco = source.ShowDeco;
        target.CardDelayMs = source.CardDelayMs;
        target.CardDurationMs = source.CardDurationMs;
        target.RevealDelayMs = source.RevealDelayMs;
        target.RevealDurationMs = source.RevealDurationMs;
        target.PanelDurationMs = source.PanelDurationMs;
        target.AutoCloseSeconds = source.AutoCloseSeconds;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_loading)
            return;

        // 界面一变就把值同步进共享设置，下一次抽签立刻用新参数；落盘交给 Save()。
        Apply(_state.Recruit, BuildSettings());

        if (e.PropertyName is nameof(RarityByTag))
            OnPropertyChanged(nameof(RarityModeIndex));

        if (e.PropertyName is nameof(CardDelayMs) or nameof(CardDurationMs) or nameof(RevealDelayMs)
            or nameof(RevealDurationMs) or nameof(PanelDurationMs))
        {
            OnPropertyChanged(nameof(TimelineSummary));
        }
    }
}
