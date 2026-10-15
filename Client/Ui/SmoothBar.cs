using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace AionDPS.Ui;

/// <summary>
/// A bar that slides to its new value instead of jumping (the overlay's player bars, Stacy's
/// "modern classic" look, 2026-10-15): bind <c>SmoothBar.Target</c> where <c>Value</c> was bound,
/// and every change eases there in a third of a second.
/// </summary>
public static class SmoothBar
{
    private static readonly Duration Slide = TimeSpan.FromMilliseconds(350);

    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(double), typeof(SmoothBar), new PropertyMetadata(0.0, OnTargetChanged));

    public static double GetTarget(DependencyObject element) => (double)element.GetValue(TargetProperty);

    public static void SetTarget(DependencyObject element, double value) => element.SetValue(TargetProperty, value);

    private static void OnTargetChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is RangeBase bar && e.NewValue is double target && double.IsFinite(target))
        {
            var slide = new DoubleAnimation(target, Slide) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            bar.BeginAnimation(RangeBase.ValueProperty, slide, HandoffBehavior.SnapshotAndReplace);
        }
    }
}
