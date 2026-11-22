using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleNavControl
{
    private static readonly Color ScopeBackground = Color.FromHex("#09191D");
    private static readonly Color Graduations = Color.FromHex("#427276");
    private static readonly Color Numerals = Color.FromHex("#8AB9B9");
    private static readonly Color WaypointColor = Color.FromHex("#F3BC78");
    private static readonly Color Phosphor = Color.FromHex("#68E4BF");
    private static readonly string[] BearingLabels = ["000", "090", "180", "270"];
    private const int SweepSegments = 16;
    private readonly DrawVertexUV2DColor[] _sweepVertices = new DrawVertexUV2DColor[SweepSegments + 2];
    private readonly string[] _ringLabels = new string[4];
    private int _labelRange = -1;
    private Vector2? _pointer;
    private (int Distance, int Bearing)? _lastMeasurement;
    private MapId _scannerMap = MapId.Nullspace;
    private Matrix3x2 _viewToMap;
    private MapCoordinates? _waypoint;
    private (int Distance, int Bearing)? _waypointReading;
    internal event Action<string?>? WaypointReadingChanged;

    private string _measurementText = string.Empty;

    public bool MassScannerDisplay { get; set; }
    public bool ShowSweep { get; set; } = true;

    protected override Vector2 MidPointVector => MassScannerDisplay ? (Vector2) PixelSize / 2 : base.MidPointVector;
    protected override int ScaledMinimapRadius => MassScannerDisplay
        ? Math.Max(1, Math.Min(PixelWidth, PixelHeight) / 2 - (int) (30 * UIScale))
        : base.ScaledMinimapRadius;

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (MassScannerDisplay)
            _pointer = args.RelativePosition;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _pointer = null;
    }

    private static Vector2 BearingDirection(float radians) => new(MathF.Sin(radians), -MathF.Cos(radians));

    private void DrawMassScanner(DrawingHandleScreen handle, bool hasSignal)
    {
        handle.DrawRect(PixelSizeBox, ScopeBackground);
        var center = MidPointVector;
        var radius = ScaledMinimapRadius;
        var range = (int) MathF.Round(WorldRange);
        if (_labelRange != range)
        {
            _labelRange = range;
            for (var i = 0; i < _ringLabels.Length; i++)
                _ringLabels[i] = Loc.GetString("fish-mass-scanner-distance", ("distance", (int) MathF.Round(WorldRange * (i + 1) / 4)));
        }

        for (var i = 0; i < 4; i++)
        {
            var ringRadius = radius * (i + 1) / 4f;
            handle.DrawCircle(center, ringRadius, Graduations.WithAlpha(i == 3 ? 0.85f : 0.46f), false);
            var labelPos = center + BearingDirection(0.72f) * ringRadius;
            handle.DrawString(Font, labelPos + new Vector2(3, 0), _ringLabels[i], UIScale * 0.8f, Numerals);
        }

        for (var i = 0; i < 72; i++)
        {
            var direction = BearingDirection(i * MathF.Tau / 72);
            var major = i % 6 == 0;
            var tip = center + direction * radius;
            handle.DrawLine(tip, tip - direction * (major ? 9 : 4) * UIScale, Graduations);
            if (i % 18 != 0)
                continue;

            handle.DrawLine(center + direction * 8 * UIScale, tip, Graduations.WithAlpha(0.4f));
            var text = BearingLabels[i / 18];
            var dimensions = handle.GetDimensions(Font, text, UIScale * 0.85f);
            handle.DrawString(Font, tip + direction * 15 * UIScale - dimensions / 2, text, UIScale * 0.85f, Numerals);
        }

        if (hasSignal && ShowSweep)
            DrawScannerSweep(handle, center, radius);
    }

    private float SweepAngle => (float) (Timing.RealTime.TotalSeconds % 6) * MathF.Tau / 6;

    private void DrawScannerEcho(DrawingHandleScreen handle, Vector2 contact, Color color)
    {
        var delta = contact - MidPointVector;
        if (delta.LengthSquared() > ScaledMinimapRadius * ScaledMinimapRadius)
            return;

        var age = (SweepAngle - MathF.Atan2(delta.X, -delta.Y) + MathF.Tau) % MathF.Tau;
        if (age < 0.8f)
            handle.DrawCircle(contact, (6 + age * 14) * UIScale, color.WithAlpha((1 - age / 0.8f) * 0.65f), false);
    }

    private void DrawScannerSweep(DrawingHandleScreen handle, Vector2 center, float radius)
    {
        var angle = SweepAngle;
        var color = Color.FromSrgb(Phosphor);
        _sweepVertices[0] = new(center, color.WithAlpha(0.035f));
        for (var i = 0; i <= SweepSegments; i++)
        {
            var progress = i / (float) SweepSegments;
            var direction = BearingDirection(angle - (1 - progress) * 0.62f);
            _sweepVertices[i + 1] = new(center + direction * radius, color.WithAlpha(progress * 0.17f));
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, Texture.White, _sweepVertices);
        handle.DrawLine(center, _sweepVertices[^1].Position, Phosphor.WithAlpha(0.65f));
    }

    internal void SetScannerWaypoint(Vector2 pixel)
    {
        if (_scannerMap != MapId.Nullspace && PixelSizeBox.Contains((Vector2i) pixel))
            _waypoint = new MapCoordinates(Vector2.Transform(pixel, _viewToMap), _scannerMap);
    }

    internal void ClearScannerWaypoint()
    {
        _waypoint = null;
        if (_waypointReading == null)
            return;

        _waypointReading = null;
        WaypointReadingChanged?.Invoke(null);
    }

    private void DrawScannerWaypoint(DrawingHandleScreen handle, MapId map, Matrix3x2 mapToView)
    {
        _scannerMap = Matrix3x2.Invert(mapToView, out _viewToMap) ? map : MapId.Nullspace;
        if (_waypoint is not { } waypoint)
            return;

        if (_scannerMap != waypoint.MapId)
        {
            ClearScannerWaypoint();
            return;
        }

        var offset = Vector2.Transform(waypoint.Position, mapToView) - MidPointVector;
        var reading = MeasureOffset(offset);
        if (_waypointReading != reading)
        {
            _waypointReading = reading;
            WaypointReadingChanged?.Invoke(FormatMeasurement(reading));
        }

        // Далёкая метка остаётся на краю экрана в направлении выбранной точки.
        var bounds = Vector2.Max(Vector2.One, MidPointVector - new Vector2(9 * UIScale));
        var end = MidPointVector + offset / Math.Max(1, Math.Max(Math.Abs(offset.X / bounds.X), Math.Abs(offset.Y / bounds.Y)));
        var color = WaypointColor;
        var dx = new Vector2(6 * UIScale, 0);
        var dy = new Vector2(0, 6 * UIScale);
        handle.DrawLine(MidPointVector, end, color.WithAlpha(0.45f));
        handle.DrawLine(end - dy, end + dx, color);
        handle.DrawLine(end + dx, end + dy, color);
        handle.DrawLine(end + dy, end - dx, color);
        handle.DrawLine(end - dx, end - dy, color);
    }

    private (int Distance, int Bearing) MeasureOffset(Vector2 offset)
    {
        var distance = (int) MathF.Round(offset.Length() / MinimapScale);
        var bearing = ((int) MathF.Round(MathF.Atan2(offset.X, -offset.Y) * 360 / MathF.Tau) + 360) % 360;
        return (distance, distance == 0 ? 0 : bearing);
    }

    private static string FormatMeasurement((int Distance, int Bearing) value) =>
        Loc.GetString("fish-mass-scanner-measurement", ("distance", value.Distance), ("bearing", value.Bearing.ToString("000")));

    private void DrawScannerRuler(DrawingHandleScreen handle)
    {
        if (_pointer is not { } pointer || MinimapScale <= 0)
            return;

        var end = pointer * UIScale;
        var relative = end - MidPointVector;
        if (relative.LengthSquared() < 1)
            return;

        var reading = MeasureOffset(relative);
        if (_lastMeasurement != reading)
        {
            _lastMeasurement = reading;
            _measurementText = FormatMeasurement(reading);
        }

        handle.DrawLine(MidPointVector, end, Numerals.WithAlpha(0.55f));
        handle.DrawCircle(end, 5 * UIScale, Phosphor, false);
        var padding = new Vector2(4, 2) * UIScale;
        var size = handle.GetDimensions(Font, _measurementText, UIScale) + padding * 2;
        var position = Vector2.Clamp(end + new Vector2(10) * UIScale, Vector2.Zero, Vector2.Max(Vector2.Zero, (Vector2) PixelSize - size));
        handle.DrawRect(UIBox2.FromDimensions(position, size), ScopeBackground);
        handle.DrawString(Font, position + padding, _measurementText, UIScale, Phosphor);
    }
}
