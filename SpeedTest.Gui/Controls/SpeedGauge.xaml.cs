using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SpeedTest.Gui.Controls;

/// <summary>
/// Tacho mit nichtlinearer 240°-Skala. <see cref="Value"/> (Mbit/s) fährt Nadel,
/// Fortschrittsbogen und große Zahl mit einer 250-ms-EaseOut-Animation nach;
/// <see cref="IsActive"/> schaltet den Akzent-Ring der Nabe.
/// </summary>
public partial class SpeedGauge : UserControl
{
    private const double CenterX = 160;
    private const double CenterY = 160;
    private const double ArcRadius = 130;
    private const double MinAngle = -120;
    private const double MaxAngle = 120;
    private const double TickOuterRadius = 120;
    private const double TickInnerRadius = 110;
    private const double LabelRadius = 93;

    private static readonly double[] ScaleMarks = [0, 5, 10, 25, 50, 100, 250, 500, 1000];

    /// <summary>Zielwert in Mbit/s; das Control animiert Nadel und Zahl selbst dorthin.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SpeedGauge),
        new PropertyMetadata(0.0, OnValueChanged));

    /// <summary>Zeigt über den Naben-Ring an, ob gerade gemessen wird.</summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(SpeedGauge),
        new PropertyMetadata(false, OnIsActiveChanged));

    // Die eigentlich animierten Größen; ihre Callbacks stellen Nadel, Bogen und Zahl.
    private static readonly DependencyProperty NeedleAngleProperty = DependencyProperty.Register(
        "NeedleAngle", typeof(double), typeof(SpeedGauge),
        new PropertyMetadata(MinAngle, OnNeedleAngleChanged));

    private static readonly DependencyProperty DisplayedSpeedProperty = DependencyProperty.Register(
        "DisplayedSpeed", typeof(double), typeof(SpeedGauge),
        new PropertyMetadata(0.0, OnDisplayedSpeedChanged));

    public SpeedGauge()
    {
        InitializeComponent();
        LiveValueText.Text = FormatValue(0);
        BuildScale();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>Anzeigeformat für Geschwindigkeiten und Zeiten: unter 100 eine
    /// Nachkommastelle, darüber keine.</summary>
    internal static string FormatValue(double value) => value < 100 ? $"{value:0.0}" : $"{value:0}";

    /// <summary>
    /// Bildet Mbit/s auf den Nadelwinkel ab: Die Skalenmarken sind gleichmäßig über die
    /// 240° verteilt, zwischen den Marken wird linear interpoliert; Werte über der letzten
    /// Marke klemmen auf den Endanschlag.
    /// </summary>
    private static double SpeedToAngle(double mbps)
    {
        if (mbps <= ScaleMarks[0])
            return MinAngle;
        if (mbps >= ScaleMarks[^1])
            return MaxAngle;

        var segmentSweep = (MaxAngle - MinAngle) / (ScaleMarks.Length - 1);
        for (var i = 1; i < ScaleMarks.Length; i++)
        {
            if (mbps <= ScaleMarks[i])
            {
                var t = (mbps - ScaleMarks[i - 1]) / (ScaleMarks[i] - ScaleMarks[i - 1]);
                return MinAngle + (i - 1 + t) * segmentSweep;
            }
        }

        return MaxAngle;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SpeedGauge)d).MoveTo((double)e.NewValue);

    private void MoveTo(double mbps)
    {
        // Vor der ersten Anzeige (und beim Offscreen-Rendern) direkt stellen,
        // damit die Nadel beim Start nicht erst über die Skala fährt.
        if (!IsLoaded)
        {
            BeginAnimation(NeedleAngleProperty, null);
            BeginAnimation(DisplayedSpeedProperty, null);
            SetValue(NeedleAngleProperty, SpeedToAngle(mbps));
            SetValue(DisplayedSpeedProperty, mbps);
            return;
        }

        // Ersetzt eine laufende Animation (SnapshotAndReplace) und startet dadurch
        // sanft vom aktuellen Zwischenwert; Zahl und Nadel laufen synchron.
        BeginAnimation(NeedleAngleProperty, CreateEaseOut(SpeedToAngle(mbps)));
        BeginAnimation(DisplayedSpeedProperty, CreateEaseOut(mbps));
    }

    private static DoubleAnimation CreateEaseOut(double to) =>
        new(to, TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };

    private static void OnNeedleAngleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SpeedGauge)d).ApplyNeedleAngle((double)e.NewValue);

    private static void OnDisplayedSpeedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SpeedGauge)d).LiveValueText.Text = FormatValue((double)e.NewValue);

    private static void OnIsActiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Ressourcen-Referenz statt aufgelöster Brush, damit ein Theme-Wechsel
        // den Ring auch im jeweiligen Zustand live umfärbt.
        var gauge = (SpeedGauge)d;
        gauge.HubEllipse.SetResourceReference(Shape.StrokeProperty, (bool)e.NewValue ? "AccentBrush" : "MutedBrush");
    }

    private void ApplyNeedleAngle(double angle)
    {
        NeedleRotation.Angle = angle;
        ProgressArcSegment.Point = PointOnArc(angle);
        ProgressArcSegment.IsLargeArc = angle - MinAngle > 180;
        ProgressArcPath.Visibility = angle > MinAngle + 0.15 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Point PointOnArc(double angle)
    {
        var rad = angle * Math.PI / 180;
        return new Point(CenterX + ArcRadius * Math.Sin(rad), CenterY - ArcRadius * Math.Cos(rad));
    }

    private void BuildScale()
    {
        foreach (var mark in ScaleMarks)
        {
            var rad = SpeedToAngle(mark) * Math.PI / 180;
            var (sin, cos) = (Math.Sin(rad), Math.Cos(rad));

            // Ressourcen-Referenzen statt aufgelöster Brushes, damit der Theme-Wechsel
            // auch die im Code erzeugten Skalen-Elemente live umfärbt.
            var tick = new Line
            {
                X1 = CenterX + TickOuterRadius * sin,
                Y1 = CenterY - TickOuterRadius * cos,
                X2 = CenterX + TickInnerRadius * sin,
                Y2 = CenterY - TickInnerRadius * cos,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            tick.SetResourceReference(Shape.StrokeProperty, "MutedBrush");
            GaugeCanvas.Children.Add(tick);

            var label = new TextBlock
            {
                Text = mark.ToString("0"),
                FontSize = 11,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, CenterX + LabelRadius * sin - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, CenterY - LabelRadius * cos - label.DesiredSize.Height / 2);
            GaugeCanvas.Children.Add(label);
        }
    }
}
