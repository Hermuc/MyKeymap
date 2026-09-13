using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KeyFlux.Settings.Models;
using KeyFlux.Settings.Services;
using KeyFlux.Settings.ViewModels;
using KeyFlux.Settings.Views;
using Xunit;

namespace KeyFlux.Settings.Tests;

/// <summary>
/// 设置页右列组件框三态守护 (用户报: 悬停只有文字周围灰框 / 侧边无特效):
/// ① 静止态: 边框 = 奶油色, 阴影 = ClaudeShadowCard 双层下坠影 (2026-09-13 起与插件卡统一,
///    原 DropShadowDirectionEffect 四周环境影被用户报与插件页观感不一致);
/// ② 悬停态: 边框换灰 #c9c7bd, 阴影换强档 ClaudeShadowCardDeep (去环版);
/// ③ 卡内控件单击获焦 → :focus-within 边框换橙 ClaudeCoral + 同款强影。
/// 断言读 BorderBrush/BoxShadow 的生效值 (样式优先级已折算; 三态阴影无过渡, 即时落定)。
/// </summary>
[Collection("I18nSerial")]
public sealed class SettingsCardEffectTests
{
    [AvaloniaFact]
    public void SettingsCard_States_Switch_Border_And_Effect()
    {
        var main = new MainViewModel(new BackendSessionOptions());
        main.Config = ConfigReadDefaults.Apply(new Config());
        var view = new SettingsPageView { DataContext = new SettingsPageViewModel(main) };
        var window = new Window { Width = 1500, Height = 950, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var cards = view.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("settingsCard")).ToList();
            Assert.True(cards.Count > 0,
                $"未找到 settingsCard; 全部 Border 类: " +
                string.Join(" | ", view.GetVisualDescendants().OfType<Border>()
                    .Select(b => string.Join("+", b.Classes)).Distinct()));
            var card = cards[0];

            // ① 静止态: 奶油边框 + ClaudeShadowCard 双层下坠影
            var restBrush = Assert.IsType<SolidColorBrush>(card.BorderBrush);
            Application.Current!.TryGetResource("ClaudeBorderCreamBrush", out var creamObj);
            Assert.Equal(((SolidColorBrush)creamObj!).Color, restBrush.Color);
            var cardShadow = (BoxShadows)view.FindResource("ClaudeShadowCard")!;
            Assert.Equal(cardShadow.ToString(), card.BoxShadow.ToString());

            // ② 悬停: headless 鼠标移到卡片中心 → :pointerover → 灰描边 + 强档阴影
            var pt = Avalonia.VisualExtensions.TranslatePoint(
                card, new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), window)!.Value;
            window.MouseMove(pt);
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.IsPointerOver, "悬停应命中卡片");
            var hoverBrush = (ISolidColorBrush)card.BorderBrush!;
            Assert.Equal(Color.Parse("#c9c7bd"), hoverBrush.Color);
            var deepShadow = (BoxShadows)view.FindResource("ClaudeShadowCardDeep")!;
            Assert.Equal(deepShadow.ToString(), card.BoxShadow.ToString());

            // ③ 单击卡内开关 (ToggleSwitch 获焦) → :focus-within → 橙描边 + 同款强影
            var toggle = card.GetVisualDescendants().OfType<ToggleSwitch>().First();
            var tp = Avalonia.VisualExtensions.TranslatePoint(
                toggle, new Point(toggle.Bounds.Width / 2, toggle.Bounds.Height / 2), window)!.Value;
            window.MouseDown(tp, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(tp, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            var focusBrush = (ISolidColorBrush)card.BorderBrush!;
            Application.Current!.TryGetResource("ClaudeCoralBrush", out var coralObj);
            Assert.Equal(((ISolidColorBrush)coralObj!).Color, focusBrush.Color);
            Assert.True(toggle.IsFocused || card.IsFocused);
        }
        finally
        {
            window.Close();
        }
    }
}
