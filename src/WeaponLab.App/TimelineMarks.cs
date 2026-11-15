using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using WeaponLab.Core.Viewport;

namespace WeaponLab.App;

/// <summary>
/// Event ticks above the viewport's timeline: sound events blue, gameplay
/// events amber, each at its frame. Drawn from the control's current width
/// on every render, so the ticks always line up with the slider beneath
/// (which spans the same width); hovering names the nearest event.
/// </summary>
public sealed class TimelineMarks : Control
{
    /// <summary>How far the slider's track starts in from each side (half its thumb).</summary>
    public const double Inset = 10;

    private static readonly IBrush SoundBrush = new SolidColorBrush(Color.Parse("#7FB8FF"));
    private static readonly IBrush GameplayBrush = new SolidColorBrush(Color.Parse("#E8A33D"));

    private ViewportClip? _clip;

    public ViewportClip? Animation
    {
        get => _clip;
        set
        {
            _clip = value;
            InvalidateVisual();
        }
    }

    private double X(double frame) => Inset + (Bounds.Width - 2 * Inset) * Math.Clamp(frame / Math.Max(1, (_clip?.FrameCount ?? 2) - 1), 0, 1);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // Hit-testable for the hover names.
        foreach (var mark in _clip?.Events ?? [])
        {
            context.FillRectangle(mark.IsSound ? SoundBrush : GameplayBrush, new Rect(X(mark.Frame) - 1, 0, 2, Bounds.Height));
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var x = e.GetPosition(this).X;
        var nearest = (_clip?.Events ?? []).OrderBy(mark => Math.Abs(X(mark.Frame) - x)).FirstOrDefault();
        ToolTip.SetTip(this, nearest is not null && Math.Abs(X(nearest.Frame) - x) < 6
            ? $"{(nearest.IsSound ? "Sound" : "Gameplay")} · frame {nearest.Frame:0.#} · {nearest.Name}"
            : null);
    }
}
