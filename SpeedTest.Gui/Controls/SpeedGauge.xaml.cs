using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SpeedTest.Gui.Controls;

/// <summary>
/// Tacho mit nichtlinearer 240°-Skala. <see cref="Value"/> (Mbit/s) ist der Zielwert;
/// ein Render-Loop zieht den angezeigten Wert jeden Frame exponentiell nach und stellt
/// daraus Nadel, Fortschrittsbogen und große Zahl. Die Nadel gleitet dadurch
/// kontinuierlich dem Ziel hinterher, statt jedem neuen Messwert einzeln
/// nachzuspringen. <see cref="IsActive"/> schaltet den Akzent-Ring der Nabe.
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

    // Zeitkonstante des Nachziehens: pro 200 ms schrumpft der Restabstand zum Ziel
    // auf 1/e. Klein genug, um der bereits über 1 s gemittelten Live-Kurve dicht zu
    // folgen, groß genug, um deren Restzappeln zu verschleifen.
    private const double ChaseTauSeconds = 0.2;

    // Unterhalb dieses Abstands (Mbit/s) gilt das Ziel als erreicht — der Unterschied
    // ist weder in der Zahl (eine Nachkommastelle) noch am Nadelwinkel erkennbar;
    // der Render-Loop hält dann an.
    private const double ChaseEpsilon = 0.05;

    private static readonly double[] ScaleMarks = [0, 5, 10, 25, 50, 100, 250, 500, 1000];

    /// <summary>Zielwert in Mbit/s; das Control zieht die Anzeige selbst dorthin nach.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(SpeedGauge),
        new PropertyMetadata(0.0, OnValueChanged));

    /// <summary>Zeigt über den Naben-Ring an, ob gerade gemessen wird.</summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(SpeedGauge),
        new PropertyMetadata(false, OnIsActiveChanged));

    private double _displayedSpeed;
    private bool _isChasing;
    private TimeSpan _lastRenderingTime;

    public SpeedGauge()
    {
        InitializeComponent();
        LiveValueText.Text = FormatValue(0);
        BuildScale();

        // Beim Entladen mitten in der Bewegung anhalten (das statische Rendering-Event
        // hielte das Control sonst am Leben); beim (Wieder-)Laden direkt aufs Ziel.
        Loaded += (_, _) => Snap(Value);
        Unloaded += (_, _) => StopChase();
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
            Snap(mbps);
            return;
        }

        StartChase();
    }

    private void StartChase()
    {
        if (_isChasing)
            return;

        _isChasing = true;
        _lastRenderingTime = TimeSpan.MinValue;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopChase()
    {
        if (!_isChasing)
            return;

        _isChasing = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering kann mehrfach pro Frame feuern; nur echte neue Frames bewegen.
        var renderingTime = ((RenderingEventArgs)e).RenderingTime;
        if (_lastRenderingTime == TimeSpan.MinValue)
        {
            _lastRenderingTime = renderingTime;
            return;
        }

        var dt = (renderingTime - _lastRenderingTime).TotalSeconds;
        if (dt <= 0)
            return;
        _lastRenderingTime = renderingTime;

        var target = Value;
        if (Math.Abs(target - _displayedSpeed) <= ChaseEpsilon)
        {
            Snap(target);
            return;
        }

        // Exponentielle Annäherung: framerate-unabhängig, überschwingt nie; nach einer
        // Render-Pause (großes dt, z. B. minimiert) landet sie in einem Schritt am Ziel.
        _displayedSpeed += (target - _displayedSpeed) * (1 - Math.Exp(-dt / ChaseTauSeconds));
        ApplyDisplayedSpeed();
    }

    /// <summary>Stellt die Anzeige ohne Bewegung auf <paramref name="mbps"/> und hält den Loop an.</summary>
    private void Snap(double mbps)
    {
        _displayedSpeed = mbps;
        ApplyDisplayedSpeed();
        StopChase();
    }

    private void ApplyDisplayedSpeed()
    {
        ApplyNeedleAngle(SpeedToAngle(_displayedSpeed));
        LiveValueText.Text = FormatValue(_displayedSpeed);
    }

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
