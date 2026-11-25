using NUnit.Framework;

namespace Lighting.Tests;

public class RoomTests
{
    internal static RoomPoint[] Concave() =>
        [new(0, 0), new(6, 0), new(5, 2), new(2, 2), new(2, 6), new(0, 6)];

    [Test]
    public void RectangleHasExpectedDimensionsAndArea()
    {
        var room = new Room(8, 6);
        Assert.That(room.Area, Is.EqualTo(48).Within(1e-9));
        Assert.That(room.Height, Is.EqualTo(2.8));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ConcaveContourSupportsBothWindings(bool reversed)
    {
        var points = Concave();
        var room = new Room(reversed ? points.Reverse() : points, 3.4);
        Assert.Multiple(() =>
        {
            Assert.That(room.Area, Is.EqualTo(19).Within(1e-9));
            Assert.That(room.Contains(new RoomPoint(1, 4)), Is.True);
            Assert.That(room.Contains(new RoomPoint(3, 3)), Is.False);
            Assert.That(room.Contains(new RoomPoint(5.5, 1)), Is.True);
            Assert.That(room.Contains(room.InteriorPoint()), Is.True);
        });
    }

    private static IEnumerable<TestCaseData> InvalidContours()
    {
        yield return new TestCaseData(new RoomPoint[] { new(0, 0), new(3, 0) }).SetName("TooFewVertices");
        yield return new TestCaseData(new RoomPoint[] { new(0, 0), new(3, 0), new(0, 3), new(3, 0) }).SetName("DuplicateVertex");
        yield return new TestCaseData(new RoomPoint[] { new(0, 0), new(4, 0), new(4, 4), new(2, 0), new(0, 4) }).SetName("NonAdjacentEdgeTouch");
        yield return new TestCaseData(new RoomPoint[] { new(0, 0), new(4, 4), new(4, 0), new(0, 4) }).SetName("CrossedEdges");
        yield return new TestCaseData(new RoomPoint[] { new(0, 0), new(1, 0), new(2, 0) }).SetName("ZeroArea");
        yield return new TestCaseData(new RoomPoint[] { new(0, 0), new(double.PositiveInfinity, 0), new(0, 3) }).SetName("NonFiniteVertex");
    }

    [TestCaseSource(nameof(InvalidContours))]
    public void InvalidContourIsRejected(object points) =>
        Assert.Throws<ArgumentException>(() => new Room((RoomPoint[])points, 3));

    [TestCase(0.8)]
    [TestCase(-1)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void InvalidHeightIsRejected(double height) =>
        Assert.Throws<ArgumentException>(() => new Room(Concave(), height));

    [Test]
    public void RoomOwnsASnapshotOfItsVertices()
    {
        var points = Concave();
        var room = new Room(points, 3);
        points[0] = new(7, 7);
        Assert.That(room.Vertices[0], Is.EqualTo(new RoomPoint(0, 0)));
    }
}
