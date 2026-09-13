using System;

namespace KeyFlux.Settings.ViewModels;

/// <summary>
/// Claude 皮肤动效时长令牌 (C# 强类型真源) —— 与色值真源 <see cref="ClaudePalette"/> 同一先例:
/// XAML 一律经 <c>{x:Static vm:ClaudeMotion.*}</c> 引用, 勿写散落字面量。
/// 换肤 = 连同本类与皮肤 XAML 整组替换 (皮肤文件头「动效时长令牌」段有契约说明)。
///
/// 基线取生产率工具档 (design-motion-principles): 全部 ≤300ms;
/// 频率闸门 = 按压/悬停 (高频) 最短, 页面级过渡 (低频) 最长。
/// </summary>
public static class ClaudeMotion
{
    /// <summary>按压下沉等即时反馈 (80ms, QuadraticEaseOut)。</summary>
    public static readonly TimeSpan Press = TimeSpan.FromMilliseconds(80);

    /// <summary>悬停/选中等画刷变色微过渡 (120ms)。</summary>
    public static readonly TimeSpan Micro = TimeSpan.FromMilliseconds(120);

    /// <summary>面板级标准过渡: 页面切换 CrossFade / 分区展开 (200ms)。</summary>
    public static readonly TimeSpan Standard = TimeSpan.FromMilliseconds(200);

    /// <summary>入场级联单卡时长 (220ms; 级联错峰 30ms/卡在消费方定义)。</summary>
    public static readonly TimeSpan Enter = TimeSpan.FromMilliseconds(220);
}
