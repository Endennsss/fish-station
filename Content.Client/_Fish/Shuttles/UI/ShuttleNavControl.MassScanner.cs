using System.Numerics;
using Content.Shared.Shuttles.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleNavControl
{
    private static readonly Color ScopeBackground = Color.FromHex("#17212D");
    private static readonly Color Graduations = Color.FromHex("#64768B");
    private static readonly Color Numerals = Color.FromHex("#B9C6D5");
    private static readonly Color WaypointColor = Color.FromHex("#EFC785");
    private static readonly Color CursorColor = Color.FromHex("#93CAFF");
    private static readonly string[] BearingLabels = ["000", "090", "180", "270"];
    private static readonly Color BezelColor = Color.FromHex("#0E141D");
    private static readonly Color HullColor = Color.FromHex("#ADB8C8");
    private static readonly Color OwnHullColor = Color.FromHex("#79B7EA");
    private const int RimSegments = 160;
    private readonly Vector2[] _bezel = new Vector2[(RimSegments + 1) * 2];
    private Vector2i _bezelSize;
    private int _bezelRadius;
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

    private void DrawMassScanner(DrawingHandleScreen handle)
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
            if (i == 3)
                continue;
            var labelPos = center + BearingDirection(-0.65f) * ringRadius;
            handle.DrawString(Font, labelPos + new Vector2(3, 0), _ringLabels[i], UIScale * 0.8f, Numerals);
        }

        handle.DrawLine(center - Vector2.UnitX * radius, center + Vector2.UnitX * radius, Graduations.WithAlpha(0.28f));
        handle.DrawLine(center - Vector2.UnitY * radius, center + Vector2.UnitY * radius, Graduations.WithAlpha(0.28f));
    }

    private bool WithinScope(Vector2 pixel) => Vector2.DistanceSquared(pixel, MidPointVector) <= ScaledMinimapRadius * ScaledMinimapRadius;

    private Vector2 ConstrainToScope(Vector2 offset, float margin)
    {
        var radius = Math.Max(0, ScaledMinimapRadius - margin);
        var length = offset.Length();
        return MidPointVector + (length > radius ? offset * (radius / length) : offset);
    }

    private static Color ScannerHullColor(Color iff) => iff == IFFComponent.IFFColor
        ? HullColor
        : Color.InterpolateBetween(iff, HullColor, 0.35f);

    private void DrawScannerBezel(DrawingHandleScreen handle)
    {
        // Маска закрывает всё за окружностью одним вызовом, включая подписи, лучи выстрелов и причалы.
        var center = MidPointVector;
        var radius = ScaledMinimapRadius;
        if (_bezelSize != PixelSize || _bezelRadius != radius)
        {
            _bezelSize = PixelSize;
            _bezelRadius = radius;
            var outer = center.Length() + 2;
            for (var i = 0; i <= RimSegments; i++)
            {
                var dir = BearingDirection(i * MathF.Tau / RimSegments);
                _bezel[2 * i] = center + dir * radius;
                _bezel[2 * i + 1] = center + dir * outer;
            }
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _bezel, BezelColor);
        handle.DrawCircle(center, radius, Graduations, false);
        for (var i = 0; i < 72; i++)
        {
            var dir = BearingDirection(i * MathF.Tau / 72);
            var tip = center + dir * radius;
            handle.DrawLine(tip, tip + dir * (i % 6 == 0 ? 7 : 3) * UIScale, Graduations);
            if (i % 18 != 0)
                continue;
            var text = BearingLabels[i / 18];
            var dimensions = handle.GetDimensions(Font, text, UIScale * 0.85f);
            handle.DrawString(Font, tip + dir * 17 * UIScale - dimensions / 2, text, UIScale * 0.85f, Numerals);
        }
    }

    internal void SetScannerWaypoint(Vector2 pixel)
    {
        if (_scannerMap != MapId.Nullspace && WithinScope(pixel))
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
        var end = ConstrainToScope(offset, 8 * UIScale);
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
        if (relative.LengthSquared() < 1 || !WithinScope(end))
            return;

        var reading = MeasureOffset(relative);
        if (_lastMeasurement != reading)
        {
            _lastMeasurement = reading;
            _measurementText = FormatMeasurement(reading);
        }

        handle.DrawLine(MidPointVector, end, Numerals.WithAlpha(0.55f));
        handle.DrawCircle(end, 5 * UIScale, CursorColor, false);
        var padding = new Vector2(4, 2) * UIScale;
        var size = handle.GetDimensions(Font, _measurementText, UIScale) + padding * 2;
        var position = ConstrainToScope(relative + new Vector2(10) * UIScale + size / 2, size.Length() / 2 + 2) - size / 2;
        handle.DrawRect(UIBox2.FromDimensions(position, size), ScopeBackground);
        handle.DrawString(Font, position + padding, _measurementText, UIScale, CursorColor);
    }
}
