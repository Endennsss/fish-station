using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._Fish.Medical.Surgery;

/// <summary>Draws a navigation arrow tinted by the surgery stylesheet.</summary>
public sealed class FishSurgeryArrow : Control
{
    /// <summary>Points toward the previous page instead of the next context.</summary>
    public bool PointLeft { get; set; }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var tipX = PointLeft ? 0.15f : 0.85f;
        var backX = 1f - tipX;
        var tip = new Vector2(tipX, 0.5f) * PixelSize;
        var top = new Vector2(backX, 0.1f) * PixelSize;
        var bottom = new Vector2(backX, 0.9f) * PixelSize;
        handle.DrawLine(tip, top, Color.White);
        handle.DrawLine(top, bottom, Color.White);
        handle.DrawLine(bottom, tip, Color.White);
    }
}
