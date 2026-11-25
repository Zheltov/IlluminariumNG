using NUnit.Framework;

namespace Lighting.Tests;

public class PartitionOpeningTests
{
    [Test]
    public void CutsDoorAndRaisedOpeningFromDiagonalPartition()
    {
        var room = new Room(8, 6);
        var partition = new Partition(new(1, 1), new(5, 4), 2.5)
        {
            Openings = [new(0.5, 1, 0, 2), new(1.5, 2, 1, 1)]
        };
        Assert.That(InteriorGeometry.Validate(room, [partition]), Is.Null);
        var panels = InteriorGeometry.SurfacePanels(partition.Span.Length, partition.Height, partition.Openings).ToArray();
        Assert.That(panels.Sum(panel => panel.Area), Is.EqualTo(8.5).Within(1e-9));
        Assert.That(panels.All(panel => partition.Openings.All(opening => !panel.Overlaps(opening.Bounds))), Is.True);
        Assert.That(panels.Any(panel => panel.Left <= 2 && panel.Right >= 2 && panel.Bottom == 0 && panel.Top == 1), Is.True);
    }

    [TestCase(1, 1, 0, 2)]
    [TestCase(3.75, 1, 0, 2)]
    [TestCase(2.5, 1, 1, 2)]
    public void RejectsOverlapAndOpeningsOutsidePartition(double offset, double width, double bottom, double height)
    {
        var partition = new Partition(new(1, 1), new(5, 1), 2.5)
        {
            Openings = [new(0.5, 1, 0, 2), new(offset, width, bottom, height)]
        };
        Assert.That(InteriorGeometry.Validate(new Room(8, 6), [partition]), Is.Not.Null);
    }

    [Test]
    public void MovingPartitionPreservesOpeningsAndShrinkingItIsAtomic()
    {
        var scene = new EditorScene(new Room(8, 6), []);
        Assert.That(scene.TrySetElement(null, new Partition(new(1, 1), new(5, 1), 2.5), out var error), Is.True, error);
        var cut = new OpeningCut(2, 1, 0, 2);
        Assert.That(scene.TrySetPartitionOpening(0, null, cut, out error), Is.True, error);

        var partition = (Partition)scene.Elements.Single();
        var moved = partition with { Start = new(2, 1), End = new(2, 5) };
        Assert.That(scene.TrySetElement(0, moved, out error), Is.True, error);
        Assert.That(((Partition)scene.Elements[0]).Openings.Single(), Is.EqualTo(cut));
        var map = new double[,] { { 10 } };
        var revision = scene.Revision;
        scene.TryPublishResult(revision, map);
        Assert.That(scene.TrySetElement(0, moved with { End = new(2, 3) }, out _), Is.False);
        Assert.That(scene.TrySetElement(0, moved with { Height = 1.5 }, out _), Is.False);
        Assert.That(scene.Revision, Is.EqualTo(revision));
        Assert.That(scene.Result, Is.SameAs(map));
        Assert.That(scene.Elements[0], Is.EqualTo(moved));

        var raised = cut with { Bottom = 0.25 };
        Assert.That(scene.TrySetPartitionOpening(0, 0, raised, out error), Is.True, error);
        Assert.That(((Partition)scene.Elements[0]).Openings.Single(), Is.EqualTo(raised));
        Assert.That(scene.Result, Is.Null);
        scene.RemovePartitionOpening(0, 0);
        Assert.That(((Partition)scene.Elements[0]).Openings, Is.Empty);
        Assert.That(scene.TrySetPartitionOpening(0, null, cut, out error), Is.True, error);
        scene.RemoveElement(0);
        Assert.That(scene.Elements, Is.Empty);
    }
}
