using NUnit.Framework;

namespace Lighting.Tests;

public class InteriorGeometryTests
{
    [Test]
    public void SubtractionLeavesExpectedAreaAndClearOpeningsOnDiagonalWall()
    {
        var room = new Room([new(0, 0), new(4, 3), new(0, 5)], 3);
        var wall = room.Wall(0);
        var door = new WallOpening(0, 0.5, 1, 0, 2);
        var window = new WallOpening(0, 1.5, 2, 1, 1.5);
        InteriorElement[] elements = [door, window];
        var panels = InteriorGeometry.WallPanels(room, 0, elements).ToArray();

        Assert.That(InteriorGeometry.Validate(room, elements), Is.Null);
        Assert.That(wall.Length, Is.EqualTo(5).Within(1e-9));
        Assert.That(wall.At(2.5), Is.EqualTo(new RoomPoint(2, 1.5)));
        Assert.That(panels.Sum(panel => panel.Area), Is.EqualTo(10).Within(1e-9));
        Assert.That(panels.All(panel => !panel.Overlaps(new(0.5, 1.5, 0, 2)) &&
                                       !panel.Overlaps(new(1.5, 3.5, 1, 2.5))), Is.True);
        Assert.That(panels.Any(panel => panel.Bottom == 0 && panel.Top == 1 && panel.Left <= 2 && panel.Right >= 2), Is.True);
        Assert.That(panels.Any(panel => panel.Bottom == 2.5 && panel.Top == 3 && panel.Left <= 2 && panel.Right >= 2), Is.True);
    }

    [Test]
    public void RejectsOverlappingAndOutOfBoundsOpenings()
    {
        var room = new Room(8, 6);
        var door = new WallOpening(0, 1, 2, 0, 2);
        Assert.That(InteriorGeometry.Validate(room, [door, new WallOpening(0, 2, 2, 1, 1)]), Is.Not.Null);
        Assert.That(InteriorGeometry.Validate(room, [door with { Offset = 7 }]), Is.Not.Null);
        Assert.That(InteriorGeometry.Validate(room, [door with { Bottom = 2 }]), Is.Not.Null);
        Assert.That(InteriorGeometry.Validate(room, [door with { Width = double.NaN }]), Is.Not.Null);
    }

    [Test]
    public void AllowsWallContactButRejectsDegenerateAndExteriorPartitions()
    {
        var room = new Room(RoomTests.Concave(), 3);
        var allowed = new Partition(new(0, 1), new(2, 1), 3);
        Assert.That(InteriorGeometry.Validate(room, [allowed]), Is.Null);
        Assert.That(InteriorGeometry.Validate(room, [allowed with { End = allowed.Start }]), Is.Not.Null);
        Assert.That(InteriorGeometry.Validate(room, [allowed with { End = new(0, 3) }]), Is.Not.Null);
        Assert.That(InteriorGeometry.Validate(room, [allowed with { Height = 0 }]), Is.Not.Null);
        Assert.That(InteriorGeometry.Validate(room, [new Partition(new(5, 1), new(1, 5), 2)]), Is.Not.Null);
    }

    [Test]
    public void RejectsObstacleAcrossNotchEvenWhenAllCornersAreInside()
    {
        var room = new Room([new(0, 0), new(6, 0), new(6, 6), new(4, 6),
            new(4, 2), new(2, 2), new(2, 6), new(0, 6)], 3);
        var obstacle = new BoxObstacle(new(1, 1), new(5, 5), 1);
        Assert.That(obstacle.Footprint().All(room.Contains), Is.True);
        Assert.That(InteriorGeometry.Validate(room, [obstacle]), Is.Not.Null);
        Assert.That(InteriorGeometry.Validate(room, [obstacle with { Opposite = new(2, 5) }]), Is.Null);
        Assert.That(InteriorGeometry.Validate(room, [obstacle with { Opposite = new(1, 5) }]), Is.Not.Null);
        Assert.That(InteriorGeometry.Validate(room, [obstacle with { Opposite = new(2, 5), Height = 4 }]), Is.Not.Null);
    }
}
