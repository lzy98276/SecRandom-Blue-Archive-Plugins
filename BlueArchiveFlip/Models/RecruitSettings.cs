namespace SecRandom.BlueArchiveFlip.Models;

/// <summary>信件底图的稀有度档位，对应 Assets 里的三张图。</summary>
public enum LetterRarity
{
    /// <summary>一星，蓝色信封。</summary>
    Blue = 1,

    /// <summary>二星，金色信封。</summary>
    Gold = 2,

    /// <summary>三星，虹色信封。</summary>
    Rainbow = 3
}

/// <summary>怎么决定每封信的稀有度。</summary>
public enum RarityMode
{
    /// <summary>所有信都用同一个档位（默认）。宿主的学生数据里没有稀有度字段，这是最稳的做法。</summary>
    Uniform = 0,

    /// <summary>按学生的标签猜：标签里出现 3 / ★3 当三星，2 当二星，1 当一星，猜不出来就用统一档位。</summary>
    ByTag = 1
}

/// <summary>
///     招募结果动画的参数。全部字段都会经过 <see cref="Sanitized" /> 夹取，
///     所以手改配置文件也不会把动画弄坏。
/// </summary>
public sealed class RecruitSettings
{
    /// <summary>关掉之后抽签结束不再弹结果窗，回到主程序原样。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>稀有度的决定方式。</summary>
    public RarityMode RarityMode { get; set; } = RarityMode.Uniform;

    /// <summary>统一档位，也是按标签猜不出来时的兜底档位。</summary>
    public LetterRarity UniformRarity { get; set; } = LetterRarity.Rainbow;

    /// <summary>面板不透明度，0.2 ~ 1.0。</summary>
    public double PanelOpacity { get; set; } = 0.9;

    /// <summary>面板底色，白底是原味。</summary>
    public string PanelBackground { get; set; } = "#FFFFFF";

    /// <summary>面板描边色。</summary>
    public string PanelBorderColor { get; set; } = "#66CCFF";

    /// <summary>是否显示顶部立绘装饰。</summary>
    public bool ShowDeco { get; set; } = true;

    /// <summary>每封信之间的错峰延迟。</summary>
    public int CardDelayMs { get; set; } = 120;

    /// <summary>单封信飞入面板的时长。</summary>
    public int CardDurationMs { get; set; } = 600;

    /// <summary>全部就位后，等多久才开始揭名。</summary>
    public int RevealDelayMs { get; set; } = 100;

    /// <summary>揭名单封的时长。</summary>
    public int RevealDurationMs { get; set; } = 300;

    /// <summary>面板整体入场时长。</summary>
    public int PanelDurationMs { get; set; } = 700;

    /// <summary>自动关闭秒数，0 表示不自动关闭（默认）。只对插件自己开窗的退路生效。</summary>
    public int AutoCloseSeconds { get; set; }

    public static readonly (int Min, int Max) OpacityRange = (20, 100);
    public static readonly (int Min, int Max) CardDelayRange = (0, 1000);
    public static readonly (int Min, int Max) CardDurationRange = (120, 3000);
    public static readonly (int Min, int Max) RevealDelayRange = (0, 2000);
    public static readonly (int Min, int Max) RevealDurationRange = (80, 2000);
    public static readonly (int Min, int Max) PanelDurationRange = (0, 3000);
    public static readonly (int Min, int Max) AutoCloseRange = (0, 120);

    /// <summary>把所有字段夹回安全区间，返回自身，方便链式调用。</summary>
    public RecruitSettings Sanitized()
    {
        PanelOpacity = Math.Clamp(PanelOpacity, 0.2, 1.0);
        CardDelayMs = Math.Clamp(CardDelayMs, CardDelayRange.Min, CardDelayRange.Max);
        CardDurationMs = Math.Clamp(CardDurationMs, CardDurationRange.Min, CardDurationRange.Max);
        RevealDelayMs = Math.Clamp(RevealDelayMs, RevealDelayRange.Min, RevealDelayRange.Max);
        RevealDurationMs = Math.Clamp(RevealDurationMs, RevealDurationRange.Min, RevealDurationRange.Max);
        PanelDurationMs = Math.Clamp(PanelDurationMs, PanelDurationRange.Min, PanelDurationRange.Max);
        AutoCloseSeconds = Math.Clamp(AutoCloseSeconds, AutoCloseRange.Min, AutoCloseRange.Max);

        if (!Enum.IsDefined(RarityMode))
            RarityMode = RarityMode.Uniform;

        if (!Enum.IsDefined(UniformRarity))
            UniformRarity = LetterRarity.Rainbow;

        PanelBackground = NormalizeColor(PanelBackground, "#FFFFFF");
        PanelBorderColor = NormalizeColor(PanelBorderColor, "#66CCFF");

        return this;
    }

    public RecruitSettings Clone()
    {
        return new RecruitSettings
        {
            Enabled = Enabled,
            RarityMode = RarityMode,
            UniformRarity = UniformRarity,
            PanelOpacity = PanelOpacity,
            PanelBackground = PanelBackground,
            PanelBorderColor = PanelBorderColor,
            ShowDeco = ShowDeco,
            CardDelayMs = CardDelayMs,
            CardDurationMs = CardDurationMs,
            RevealDelayMs = RevealDelayMs,
            RevealDurationMs = RevealDurationMs,
            PanelDurationMs = PanelDurationMs,
            AutoCloseSeconds = AutoCloseSeconds
        };
    }

    private static string NormalizeColor(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var text = value.Trim();
        if (!text.StartsWith('#'))
            text = "#" + text;

        // 只接受 #RGB / #RRGGBB / #AARRGGBB，别的一律回退，避免手改配置把渲染搞崩。
        if (text.Length is not (4 or 7 or 9))
            return fallback;

        for (var i = 1; i < text.Length; i++)
        {
            if (!Uri.IsHexDigit(text[i]))
                return fallback;
        }

        return text.ToUpperInvariant();
    }
}
