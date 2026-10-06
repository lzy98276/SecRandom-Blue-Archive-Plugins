using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SecRandom.BlueArchiveFlip.Models;

namespace SecRandom.BlueArchiveFlip.Controls;

/// <summary>
///     招募结果里的一张信件卡片。
///     <para>
///         这个控件只负责"长什么样"和"某一帧该摆在哪"，时间线由
///         <see cref="Animation.RecruitAnimator" /> 驱动，所以同一套视觉既能播动画也能直接定格。
///     </para>
/// </summary>
public partial class RecruitLetter : UserControl
{
    /// <summary>入场起始缩放。</summary>
    public const double CardEnterScale = 2.5;

    /// <summary>入场起始纵向偏移。</summary>
    public const double CardEnterOffsetY = -24;

    /// <summary>卡片统一的倾斜角度。</summary>
    public const double CardTiltDegrees = 15;

    /// <summary>揭名起始缩放。</summary>
    public const double NameEnterScale = 0.96;

    /// <summary>揭名起始纵向偏移。</summary>
    public const double NameEnterOffsetY = 12;

    /// <summary>
    /// 程序集资源名前缀。资源用 EmbeddedResource 内嵌（不是 avares://），
    /// 这样插件无论被装进哪个 AssemblyLoadContext 都能取到图。
    /// </summary>
    private const string AssetPrefix = "SecRandom.BlueArchiveFlip.Assets.";

    private static readonly object BitmapGate = new();
    private static readonly Dictionary<LetterRarity, Bitmap> BitmapCache = new();
    private static Bitmap? _decoBitmap;

    public static readonly StyledProperty<string> DisplayNameProperty =
        AvaloniaProperty.Register<RecruitLetter, string>(nameof(DisplayName), string.Empty);

    public static readonly StyledProperty<string> SubTextProperty =
        AvaloniaProperty.Register<RecruitLetter, string>(nameof(SubText), string.Empty);

    public static readonly StyledProperty<LetterRarity> RarityProperty =
        AvaloniaProperty.Register<RecruitLetter, LetterRarity>(nameof(Rarity), LetterRarity.Rainbow);

    static RecruitLetter()
    {
        DisplayNameProperty.Changed.AddClassHandler<RecruitLetter>((letter, _) => letter.RefreshNameStyle());
        RarityProperty.Changed.AddClassHandler<RecruitLetter>((letter, _) => letter.RefreshLetterSource());
    }

    public RecruitLetter()
    {
        InitializeComponent();
        RefreshNameStyle();
        RefreshLetterSource();
        ResetToEnterStart();
    }

    /// <summary>牌面上的名字。</summary>
    public string DisplayName
    {
        get => GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    /// <summary>副标题（分组 / 课程之类），只做提示，不画在牌面上。</summary>
    public string SubText
    {
        get => GetValue(SubTextProperty);
        set => SetValue(SubTextProperty, value);
    }

    /// <summary>决定用哪张信封底图。</summary>
    public LetterRarity Rarity
    {
        get => GetValue(RarityProperty);
        set => SetValue(RarityProperty, value);
    }

    /// <summary>回到"还没飞进来"的状态：整张透明、放大、偏上，名牌不显示。</summary>
    public void ResetToEnterStart()
    {
        ApplyCardEnter(0);
        ApplyNameReveal(0);
    }

    /// <summary>入场进度，0 = 起始（放大且透明），1 = 就位。</summary>
    public void ApplyCardEnter(double progress)
    {
        var t = Math.Clamp(progress, 0, 1);

        var scale = CardEnterScale + ((1 - CardEnterScale) * t);
        CardScale.ScaleX = scale;
        CardScale.ScaleY = scale;

        CardTranslate.Y = CardEnterOffsetY + ((0 - CardEnterOffsetY) * t);
        CardRotate.Angle = CardTiltDegrees;

        Opacity = t;
    }

    /// <summary>揭名进度，0 = 名牌不可见，1 = 名牌完全展开。</summary>
    public void ApplyNameReveal(double progress)
    {
        var t = Math.Clamp(progress, 0, 1);

        NameLayer.Opacity = t;

        var scale = NameEnterScale + ((1 - NameEnterScale) * t);
        NameScale.ScaleX = scale;
        NameScale.ScaleY = scale;

        NameTranslate.Y = NameEnterOffsetY * (1 - t);
    }

    /// <summary>直接把整张牌定格在最终姿态，不播动画。</summary>
    public void ApplySettled()
    {
        ApplyCardEnter(1);
        ApplyNameReveal(1);
    }

    private Grid Root => this.FindControl<Grid>("PART_Root")!;

    private Grid NameLayer => this.FindControl<Grid>("PART_NameLayer")!;

    private TextBlock NameText => this.FindControl<TextBlock>("PART_Name")!;

    private Image LetterImage => this.FindControl<Image>("PART_Letter")!;

    // 变换对象不是控件，FindControl 拿不到，只能按 XAML 里声明的顺序从 TransformGroup 里取。
    private ScaleTransform CardScale => (ScaleTransform)CardGroup.Children[0];

    private TranslateTransform CardTranslate => (TranslateTransform)CardGroup.Children[1];

    private RotateTransform CardRotate => (RotateTransform)CardGroup.Children[2];

    private ScaleTransform NameScale => (ScaleTransform)NameGroup.Children[0];

    private TranslateTransform NameTranslate => (TranslateTransform)NameGroup.Children[1];

    private TransformGroup CardGroup => (TransformGroup)Root.RenderTransform!;

    private TransformGroup NameGroup => (TransformGroup)NameLayer.RenderTransform!;

    private void RefreshNameStyle()
    {
        // 名牌比原来小了（134×82），字号阈值跟着往下调，长一点的名字（含「编号 名称」）也不会顶破。
        var name = DisplayName ?? string.Empty;
        NameText.FontSize = name.Length > 14 ? 13d : name.Length > 9 ? 15d : name.Length > 5 ? 18d : 21d;
        ToolTip.SetTip(this, BuildToolTip());
    }

    private string BuildToolTip()
    {
        var name = DisplayName ?? string.Empty;
        var sub = SubText ?? string.Empty;
        if (sub.Length == 0)
            return name;

        return $"{name}\n{sub}";
    }

    private void RefreshLetterSource()
    {
        try
        {
            LetterImage.Source = GetLetterBitmap(Rarity);
        }
        catch (Exception)
        {
            // 底图取不到时退化成没有信封的白牌，至少把名字显示出来。
            LetterImage.Source = null;
        }
    }

    /// <summary>按稀有度取信封底图；三张图各只解码一次。</summary>
    public static Bitmap GetLetterBitmap(LetterRarity rarity)
    {
        lock (BitmapGate)
        {
            if (BitmapCache.TryGetValue(rarity, out var cached))
                return cached;

            var file = rarity switch
            {
                LetterRarity.Blue => "Letter_Blue.png",
                LetterRarity.Gold => "Letter_Gold.png",
                _ => "Letter_Rainbow.png"
            };

            var bitmap = DecodeAsset(file);
            BitmapCache[rarity] = bitmap;
            return bitmap;
        }
    }

    /// <summary>顶部立绘装饰图。</summary>
    public static Bitmap GetDecoBitmap()
    {
        lock (BitmapGate)
        {
            return _decoBitmap ??= DecodeAsset("Arona_Plana.png");
        }
    }

    /// <summary>从本程序集的清单资源里解码一张图。</summary>
    private static Bitmap DecodeAsset(string fileName)
    {
        var assembly = typeof(RecruitLetter).Assembly;
        var stream = assembly.GetManifestResourceStream(AssetPrefix + fileName)
                     ?? throw new InvalidOperationException(
                         $"插件资源 {AssetPrefix}{fileName} 不存在。现有资源：{string.Join(", ", assembly.GetManifestResourceNames())}");
        using (stream)
        {
            return new Bitmap(stream);
        }
    }
}
