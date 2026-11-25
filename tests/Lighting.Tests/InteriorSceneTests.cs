using NUnit.Framework;

namespace Lighting.Tests;

public class InteriorSceneTests
{
    [Test]
    public void AcceptedEditsInvalidateMapAndRejectedEditPreservesIt()
    {
        var scene = new EditorScene(new Room(8, 6), []);
        var map = new double[,] { { 12 } };
        var revision = scene.Revision;
        scene.TryPublishResult(revision, map);
        var opening = new WallOpening(0, 1, 1, 0, 2);

        Assert.That(scene.TrySetElement(null, opening, out var error), Is.True, error);
        Assert.That(scene.Result, Is.Null);
        Assert.That(scene.TryPublishResult(revision, map), Is.False);

        revision = scene.Revision;
        scene.TryPublishResult(revision, map);
        Assert.That(scene.TrySetElement(0, opening with { Offset = 8 }, out error), Is.False);
        Assert.That(error, Is.Not.Empty);
        Assert.That(scene.Elements.Single(), Is.EqualTo(opening));
        Assert.That(scene.Revision, Is.EqualTo(revision));
        Assert.That(scene.Result, Is.SameAs(map));

        var elevated = opening with { Bottom = 0.5 };
        Assert.That(scene.TrySetElement(0, elevated, out error), Is.True, error);
        Assert.That(scene.Elements.Single(), Is.EqualTo(elevated));
        Assert.That(scene.Result, Is.Null);
        scene.RemoveElement(0);
        Assert.That(scene.Elements, Is.Empty);
        Assert.That(scene.Revision, Is.EqualTo(revision + 2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReorderingContourPreservesOpeningLocation(bool reverse)
    {
        var room = new Room(8, 6);
        var scene = new EditorScene(room, []);
        var opening = new WallOpening(0, 1.25, 2, 0, 2);
        scene.TrySetElement(null, opening, out _);
        var points = room.Vertices.Skip(2).Concat(room.Vertices.Take(2)).ToArray();

        Assert.That(scene.TryApplyOutline(reverse ? points.Reverse() : points, 2.8, out var error), Is.True, error);
        var moved = (WallOpening)scene.Elements.Single();
        var span = scene.Room.Wall(moved.Wall);
        var edges = new[] { span.At(moved.Offset), span.At(moved.Offset + moved.Width) };
        Assert.That(edges.Select(point => point.X).Order(), Is.EqualTo(new[] { 1.25, 3.25 }).Within(1e-9));
        Assert.That(edges.All(point => point.Y == 0), Is.True);
    }

    [Test]
    public void RejectsSplittingMovingOrRemovingWallAndLoweringCeiling()
    {
        var room = new Room(8, 6);
        var scene = new EditorScene(room, []);
        var opening = new WallOpening(0, 1, 1, 0, 2);
        scene.TrySetElement(null, opening, out _);

        Assert.That(scene.TryApplyOutline([new(0, 0), new(4, 0), .. room.Vertices.Skip(1)], 2.8, out _), Is.False);
        Assert.That(scene.TryApplyOutline([new(0, -1), .. room.Vertices.Skip(1)], 2.8, out _), Is.False);
        Assert.That(scene.TryApplyOutline(room.Vertices.Skip(1), 2.8, out _), Is.False);
        Assert.That(scene.TryApplyOutline(room.Vertices, 1.8, out _), Is.False);
        Assert.That(scene.Room, Is.SameAs(room));
        Assert.That(scene.Elements.Single(), Is.EqualTo(opening));
    }

    [Test]
    public void RejectsOutlineWhenAnInteriorElementWouldBeOutside()
    {
        var room = new Room(8, 6);
        var scene = new EditorScene(room, []);
        var narrow = new Room(4, 6);
        Assert.That(scene.TrySetElement(null, new Partition(new(5, 1), new(6, 1), 2), out _), Is.True);
        Assert.That(scene.TryApplyOutline(narrow.Vertices, 2.8, out _), Is.False);
        scene.RemoveElement(0);
        Assert.That(scene.TrySetElement(null, new BoxObstacle(new(5, 1), new(6, 2), 2), out _), Is.True);
        Assert.That(scene.TryApplyOutline(narrow.Vertices, 2.8, out _), Is.False);
        Assert.That(scene.Room, Is.SameAs(room));
    }
}
