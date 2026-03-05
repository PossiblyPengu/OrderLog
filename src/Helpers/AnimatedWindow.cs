using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;

namespace OrderLog.Helpers;

/// <summary>
/// Base class for all application windows/dialogs.
/// Provides a shared fade + scale-down closing animation so that every
/// window dismisses consistently without each file duplicating the logic.
/// </summary>
public abstract class AnimatedWindow : Window
{
    private bool _isClosingAnimationPlaying;

    /// <summary>Duration of the closing animation in milliseconds. Override to customise.</summary>
    protected virtual double CloseAnimDurationMs => 150.0;

    /// <summary>Target scale at the end of the closing animation (0–1). Override to customise.</summary>
    protected virtual double CloseAnimScale => 0.93;

    /// <summary>
    /// Plays the closing animation then either sets <see cref="Window.DialogResult"/>
    /// (if <paramref name="dialogResult"/> is provided) or calls <see cref="Window.Close"/>.
    /// Safe to call multiple times — subsequent calls are ignored while animation is running.
    /// </summary>
    protected void BeginCloseAnimation(bool? dialogResult = null)
    {
        if (_isClosingAnimationPlaying) return;
        _isClosingAnimationPlaying = true;

        var dur   = new Duration(TimeSpan.FromMilliseconds(CloseAnimDurationMs));
        var ease  = new CubicEase { EasingMode = EasingMode.EaseIn };
        var scale = CloseAnimScale;

        var sb = new Storyboard { FillBehavior = FillBehavior.Stop };

        var fadeOut = new DoubleAnimation(1.0, 0.0, dur) { EasingFunction = ease };
        Storyboard.SetTarget(fadeOut, this);
        Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(fadeOut);

        // RootBorder carries the ScaleTransform added by each XAML file
        if (FindName("RootBorder") is UIElement rootBorder)
        {
            var scaleX = new DoubleAnimation(1.0, scale, dur) { EasingFunction = ease };
            Storyboard.SetTarget(scaleX, rootBorder);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("RenderTransform.ScaleX"));
            sb.Children.Add(scaleX);

            var scaleY = new DoubleAnimation(1.0, scale, dur) { EasingFunction = ease };
            Storyboard.SetTarget(scaleY, rootBorder);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("RenderTransform.ScaleY"));
            sb.Children.Add(scaleY);
        }

        sb.Completed += (s, e) =>
        {
            if (dialogResult.HasValue)
                DialogResult = dialogResult;
            else
                Close();
        };

        sb.Begin();
    }

    /// <inheritdoc/>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isClosingAnimationPlaying)
        {
            e.Cancel = true;
            BeginCloseAnimation();
            return;
        }
        base.OnClosing(e);
    }
}
