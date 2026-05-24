using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DragonOS.AudioConfigurator.WPF.Views;

/// <summary>
/// A lightweight <see cref="Decorator"/> that renders a chamfered (diagonally-clipped)
/// panel background with optional corner accent lines, matching the Dragon OS / Wyvern
/// visual language where the top-left and bottom-right corners are cut at 45 degrees.
/// </summary>
/// <remarks>
/// <para>
/// WPF's built-in <see cref="Border"/> only supports rounded corners (<see cref="Border.CornerRadius"/>),
/// not chamfer cuts. This control fills that gap with a <see cref="PathGeometry"/>-based
/// background rendered directly in <see cref="OnRender"/>, so the chamfer size is always
/// in device-independent pixels regardless of the element's layout size.
/// </para>
/// <para>
/// The corner accent lines (top-right and bottom-left) replicate the <c>::before</c>
/// and <c>::after</c> pseudo-element accents used across the Dragon OS CSS family.
/// </para>
/// </remarks>
public sealed class ChamferBorder : Decorator
{
    // ─────────────────────────────────────────────────────────────────────────
    // Dependency Properties
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The fill brush for the chamfered polygon background.</summary>
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(Brushes.Transparent,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The stroke (outline) brush drawn along the chamfered perimeter.</summary>
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(Brushes.Transparent,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Thickness of the perimeter stroke in device-independent pixels.</summary>
    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(1.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// The diagonal cut size in device-independent pixels applied to the
    /// top-left and bottom-right corners.
    /// </summary>
    public static readonly DependencyProperty ChamferSizeProperty =
        DependencyProperty.Register(nameof(ChamferSize), typeof(double), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(8.0,
                FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Inner padding between the chamfered border and the child element.
    /// Mirrors <see cref="Border.Padding"/> semantics.
    /// </summary>
    public static readonly DependencyProperty PaddingProperty =
        DependencyProperty.Register(nameof(Padding), typeof(Thickness), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(default(Thickness),
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>
    /// When <see langword="true"/> (the default), draws short accent lines at the
    /// top-right and bottom-left corners — replicating the Dragon OS CSS
    /// <c>::before</c> / <c>::after</c> corner decoration.
    /// Set to <see langword="false"/> for buttons and compact elements where the
    /// decoration would be visually cluttered.
    /// </summary>
    public static readonly DependencyProperty ShowCornerAccentsProperty =
        DependencyProperty.Register(nameof(ShowCornerAccents), typeof(bool), typeof(ChamferBorder),
            new FrameworkPropertyMetadata(true,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush     Fill              { get => (Brush)GetValue(FillProperty);             set => SetValue(FillProperty, value); }
    public Brush     Stroke            { get => (Brush)GetValue(StrokeProperty);           set => SetValue(StrokeProperty, value); }
    public double    StrokeThickness   { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public double    ChamferSize       { get => (double)GetValue(ChamferSizeProperty);     set => SetValue(ChamferSizeProperty, value); }
    public Thickness Padding           { get => (Thickness)GetValue(PaddingProperty);      set => SetValue(PaddingProperty, value); }
    public bool      ShowCornerAccents { get => (bool)GetValue(ShowCornerAccentsProperty); set => SetValue(ShowCornerAccentsProperty, value); }

    // ─────────────────────────────────────────────────────────────────────────
    // Layout overrides
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null) return Size.Empty;

        Child.Measure(new Size(
            Math.Max(0, constraint.Width  - Padding.Left - Padding.Right),
            Math.Max(0, constraint.Height - Padding.Top  - Padding.Bottom)));

        return new Size(
            Child.DesiredSize.Width  + Padding.Left + Padding.Right,
            Child.DesiredSize.Height + Padding.Top  + Padding.Bottom);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(
            Padding.Left,
            Padding.Top,
            Math.Max(0, finalSize.Width  - Padding.Left - Padding.Right),
            Math.Max(0, finalSize.Height - Padding.Top  - Padding.Bottom)));

        return finalSize;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rendering
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext dc)
    {
        var c    = ChamferSize;
        var w    = ActualWidth;
        var h    = ActualHeight;
        var st   = StrokeThickness;
        var half = st * 0.5; // Inset so stroke renders inside the layout bounds.

        // ── Chamfered polygon ──────────────────────────────────────────────
        // Shape: top-left and bottom-right corners are diagonally cut.
        // Vertices (clockwise from top-left cut):
        //   (c, 0) → (w, 0) → (w, h-c) → (w-c, h) → (0, h) → (0, c) → close
        var fig = new PathFigure
        {
            IsClosed    = true,
            StartPoint  = new Point(c + half, half),
        };
        fig.Segments.Add(new LineSegment(new Point(w - half, half),    true));
        fig.Segments.Add(new LineSegment(new Point(w - half, h - c),   true));
        fig.Segments.Add(new LineSegment(new Point(w - c,    h - half), true));
        fig.Segments.Add(new LineSegment(new Point(half,     h - half), true));
        fig.Segments.Add(new LineSegment(new Point(half,     c + half), true));

        var geom = new PathGeometry();
        geom.Figures.Add(fig);

        var pen = st > 0 ? new Pen(Stroke, st) : null;
        dc.DrawGeometry(Fill, pen, geom);

        // ── Corner accent lines ────────────────────────────────────────────
        // Top-right: horizontal line along top edge + vertical line down right edge.
        // Bottom-left: vertical line up left edge + horizontal line along bottom edge.
        // Replicates Dragon OS CSS ::before (top:0; right:0) and ::after (bottom:0; left:0).
        if (!ShowCornerAccents) return;

        var accentPen = new Pen(new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)), 1.5);
        const double AccentLength = 18.0;

        // Top-right
        dc.DrawLine(accentPen, new Point(w - AccentLength, 0), new Point(w, 0));
        dc.DrawLine(accentPen, new Point(w, 0), new Point(w, AccentLength));

        // Bottom-left
        dc.DrawLine(accentPen, new Point(0, h - AccentLength), new Point(0, h));
        dc.DrawLine(accentPen, new Point(0, h), new Point(AccentLength, h));
    }
}
