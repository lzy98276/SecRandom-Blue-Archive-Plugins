using SecRandom.Core.Abstraction.Services.Presentation;

namespace SecRandom.BlueArchiveFlip.Services;

/// <summary>
///     把插件的翻牌动画接进主程序「动画样式」下拉框（设置 → 抽签设置 → 动画样式）。
///     <para>
///         主程序 3.1 起的插件动画扩展点：在 <c>PluginBase.Initialize</c> 里
///         <c>services.AddSingleton&lt;IDrawAnimationContribution, RecruitAnimationContribution&gt;()</c>
///         注册，下拉框就会多出一项；用户选中它以后，主程序只把「揭晓」交给
///         <see cref="RecruitResultPresenter" />（两者的 id 必须一致），没选中时走主程序内置动画。
///     </para>
/// </summary>
public sealed class RecruitAnimationContribution : IDrawAnimationContribution
{
    /// <summary>下拉框里显示的名字。</summary>
    public const string OptionName = "碧蓝档案招募";

    /// <inheritdoc />
    public string Id => RecruitResultPresenter.PresenterId;

    /// <inheritdoc />
    public string DisplayName => OptionName;

    /// <inheritdoc />
    public int Priority => 100;
}
