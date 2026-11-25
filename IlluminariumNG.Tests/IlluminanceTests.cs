using System.Numerics;
using NUnit.Framework;
using IlluminariumNG.Domain;

namespace IlluminariumNG.Tests;

public class IlluminanceTests
{
    private static readonly Luminaire Lamp = new(new Vector2(2, 2), Photometry.Sample);

    [Test]
    public void RectangleMatchesLegacyFormulaAtEveryCell()
    {
        var map = Illuminance.Calculate(new Room(8, 6), [Lamp], default);
        for (var x = 0; x < Illuminance.Columns; x++)
        for (var y = 0; y < Illuminance.Rows; y++)
        {
            var dx = (x + 0.5) * 8 / Illuminance.Columns - 2;
            var dy = (y + 0.5) * 6 / Illuminance.Rows - 2;
            var distance = Math.Sqrt(dx * dx + dy * dy + 4);
            var cosine = 2 / distance;
            var intensity = Lamp.Distribution.Intensity(Math.Acos(cosine) * 180 / Math.PI, 0);
            var expected = intensity * cosine / (distance * distance);
            Assert.That(map[x, y], Is.EqualTo(expected).Within(Math.Max(1e-9, 1e-9 * expected)), $"Cell ({x}, {y})");
        }
    }

    [Test]
    public void HigherCeilingChangesIlluminanceBelowLamp()
    {
        var low = new Room(8, 6);
        var high = new Room(low.Vertices, 4);
        Assert.That(Illuminance.Calculate(high, [Lamp], default)[8, 8],
            Is.LessThan(Illuminance.Calculate(low, [Lamp], default)[8, 8]));
    }

    [Test]
    public void ConcaveMapMasksExteriorCells()
    {
        var map = Illuminance.Calculate(new Room(RoomTests.Concave(), 3.4), [Lamp], default);
        Assert.That(map[16, 12], Is.NaN);
        Assert.That(double.IsFinite(map[2, 12]), Is.True);
    }

    [Test]
    public void MeanIgnoresMaskedCells() =>
        Assert.That(Illuminance.Mean(new double[,] { { 10, double.NaN }, { 20, double.NaN } }), Is.EqualTo(15).Within(1e-9));

    [Test]
    public void CancellationStopsCalculation() =>
        Assert.Throws<OperationCanceledException>(() =>
            Illuminance.Calculate(new Room(8, 6), [Lamp], new CancellationToken(true)));
}
