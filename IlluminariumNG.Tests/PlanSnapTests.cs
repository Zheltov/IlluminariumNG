using NUnit.Framework;
using IlluminariumNG.Application;
using IlluminariumNG.Domain;

namespace IlluminariumNG.Tests;

public class PlanSnapTests
{
    [Test]
    public void GridRoundsNegativeWorldCoordinates()
    {
        Assert.That(PlanSnap.Point(new(-0.37, -1.39), true, false, [], 0.2),
            Is.EqualTo(new RoomPoint(-0.25, -1.5)));
    }

    [Test]
    public void ClosestGeometryWinsAndTiesAreStable()
    {
        RoomPoint[] candidates = [new(1.125, 1), new(0.875, 1), new(1, 1.25)];
        Assert.That(PlanSnap.Point(new(1, 1), true, true, candidates, 0.25), Is.EqualTo(candidates[0]));
        Assert.That(PlanSnap.Point(new(1, 1), true, true, candidates, 0.25, candidates[0]), Is.EqualTo(candidates[1]));
        Assert.That(PlanSnap.Point(new(1, 1), true, true, candidates, 0.1), Is.EqualTo(new RoomPoint(1, 1)));
    }

    [Test]
    public void TurningBothOptionsOffLeavesPointUnchanged()
    {
        var input = new RoomPoint(-1.334, 2.111);
        Assert.That(PlanSnap.Point(input, false, false, [new(-1.3, 2.1)], 0.2), Is.EqualTo(input));
    }

    [Test]
    public void DiagonalHostSnapsAlongItsOwnAxis()
    {
        var span = new WallSpan(new(-1, 2), new(3, 5));
        RoomPoint[] candidates = [new(0.1, 3), span.At(1.76)];
        Assert.That(PlanSnap.Along(span, 1.69, 4.1, true, false, candidates, 0.2), Is.EqualTo(1.75));
        Assert.That(PlanSnap.Along(span, 1.69, 4.1, true, true, candidates, 0.2), Is.EqualTo(1.76).Within(1e-9));
        Assert.That(PlanSnap.Along(span, 4.9, 4.1, true, true, candidates, 0.2), Is.EqualTo(4.1));
    }
}
