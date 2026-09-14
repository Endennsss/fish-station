using System.Numerics;
using Content.Client._Fish.LightShafts;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Client._Fish.LightShafts;

[TestFixture]
public sealed class LightShaftGeometryTests
{
    [TestCase(0f, 0f, 1f, 0f, 2f)]
    [TestCase(0f, 1f, 1f, 0f, 2f)]
    [TestCase(0f, -1f, 1f, 0f, 2f)]
    [TestCase(0f, 2f, 1f, 0f, 10f)]
    [TestCase(0f, 0f, -1f, 0f, 10f)]
    [TestCase(2.5f, 0f, 1f, 0f, 0f)]
    [TestCase(2.5f, 2f, 0f, -1f, 1f)]
    public void ClipsParallelAndInsideRays(float x, float y, float dx, float dy, float expected)
    {
        var distance = LightShaftGeometry.ClipRay(new Vector2(x, y), new Vector2(dx, dy),
            new Box2(2f, -1f, 3f, 1f), 10f);
        Assert.That(distance, Is.EqualTo(expected).Within(0.00001f));
    }

    [Test]
    public void NearestBlockerWinsRegardlessOfOrder()
    {
        var near = new Box2(2f, -1f, 3f, 1f);
        var far = new Box2(5f, -1f, 6f, 1f);
        var first = LightShaftGeometry.ClipRay(Vector2.Zero, Vector2.UnitX, near, 10f);
        first = LightShaftGeometry.ClipRay(Vector2.Zero, Vector2.UnitX, far, first);
        var second = LightShaftGeometry.ClipRay(Vector2.Zero, Vector2.UnitX, far, 10f);
        second = LightShaftGeometry.ClipRay(Vector2.Zero, Vector2.UnitX, near, second);
        Assert.That(first, Is.EqualTo(2f));
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void BlockerBeyondLengthDoesNotExtendRay()
    {
        Assert.That(LightShaftGeometry.ClipRay(Vector2.Zero, Vector2.UnitX,
            new Box2(2f, -1f, 3f, 1f), 1f), Is.EqualTo(1f));
    }
}
