using System.Numerics;
using NUnit.Framework;

namespace Lighting.Tests;

public class EditorSceneTests
{
    private static readonly double[,] Map = { { 12 } };

    [Test]
    public void AcceptingDraftCopiesVerticesAndClearsMap()
    {
        var scene = new EditorScene(new Room(8, 6), []);
        var revision = scene.Revision;
        Assert.That(scene.TryPublishResult(revision, Map), Is.True);
        var draft = RoomTests.Concave();

        Assert.That(scene.TryApplyOutline(draft, 3.4, out var error), Is.True, error);
        draft[0] = new(99, 99);

        Assert.That(scene.Room.Vertices, Is.EqualTo(RoomTests.Concave()));
        Assert.That(scene.Room.Height, Is.EqualTo(3.4));
        Assert.That(scene.Result, Is.Null);
        Assert.That(scene.Revision, Is.EqualTo(revision + 1));
        Assert.That(scene.TryPublishResult(revision, Map), Is.False);
    }

    [Test]
    public void AcceptsInsertionMovementAndRemovalOfVertex()
    {
        var scene = new EditorScene(new Room(8, 6), []);
        var vertices = scene.Room.Vertices.ToList();
        vertices.Insert(1, new RoomPoint(4, 0));
        Assert.That(scene.TryApplyOutline(vertices, 2.8, out var error), Is.True, error);
        Assert.That(scene.Room.Vertices.Count, Is.EqualTo(5));

        vertices[1] = new RoomPoint(4, -1);
        Assert.That(scene.TryApplyOutline(vertices, 2.8, out error), Is.True, error);
        Assert.That(scene.Room.Vertices[1], Is.EqualTo(new RoomPoint(4, -1)));

        vertices.RemoveAt(1);
        Assert.That(scene.TryApplyOutline(vertices, 2.8, out error), Is.True, error);
        Assert.That(scene.Room.Vertices, Is.EqualTo(new Room(8, 6).Vertices));
        Assert.That(scene.Revision, Is.EqualTo(3));
    }

    [TestCase("invalid contour")]
    [TestCase("invalid height")]
    [TestCase("excluded luminaire")]
    public void RejectedEditLeavesSceneAndResultIntact(string reason)
    {
        var original = new Room(8, 6);
        var lamp = new Luminaire(new Vector2(6, 5), Photometry.Sample);
        var scene = new EditorScene(original, [lamp]);
        scene.TryPublishResult(scene.Revision, Map);

        (IEnumerable<RoomPoint> Outline, double Height) candidate = reason switch
        {
            "invalid contour" => (new RoomPoint[] { new(0, 0), new(3, 3), new(0, 3), new(3, 0) }, 3),
            "invalid height" => (original.Vertices, 0.8),
            _ => (RoomTests.Concave(), 3)
        };

        Assert.That(scene.TryApplyOutline(candidate.Outline, candidate.Height, out var error), Is.False);
        Assert.That(error, Is.Not.Empty);
        Assert.That(scene.Room, Is.SameAs(original));
        Assert.That(scene.Lights.Single(), Is.SameAs(lamp));
        Assert.That(scene.Result, Is.SameAs(Map));
        Assert.That(scene.Revision, Is.Zero);
    }

    [Test]
    public void ReapplyingSameContourPreservesRevisionAndMap()
    {
        var room = new Room(8, 6);
        var scene = new EditorScene(room, []);
        scene.TryPublishResult(scene.Revision, Map);
        Assert.That(scene.TryApplyOutline(room.Vertices.ToArray(), room.Height, out _), Is.True);
        Assert.That(scene.Revision, Is.Zero);
        Assert.That(scene.Result, Is.SameAs(Map));
    }

    [Test]
    public void PlacesImportedPhotometryInsideConcaveRoomAndRejectsExteriorDrag()
    {
        var room = new Room(RoomTests.Concave(), 3.4);
        var scene = new EditorScene(room, []);
        Assert.That(room.Contains(new RoomPoint(3, 3)), Is.False);
        var distribution = Photometry.Read("""
            IESNA:LM-63-2002
            TILT=NONE
            1 1000 1 2 1 1 2 0 0 0 1 1 10
            0 90
            0
            780 0
            """);

        Assert.That(scene.TryAddLight(distribution), Is.True);
        Assert.That(room.Contains(scene.Lights.Single().Position), Is.True);
        Assert.That(scene.Lights.Single().Distribution, Is.SameAs(distribution));

        Assert.That(scene.TryMoveLight(0, new Vector2(1.13f, 3.38f)), Is.True);
        Assert.That(scene.Lights.Single().Position, Is.EqualTo(new Vector2(1.25f, 3.5f)));
        scene.TryPublishResult(scene.Revision, Map);
        Assert.That(scene.TryMoveLight(0, new Vector2(3, 3)), Is.False);
        Assert.That(scene.Lights.Single().Position, Is.EqualTo(new Vector2(1.25f, 3.5f)));
        Assert.That(scene.Result, Is.SameAs(Map));
    }

    [Test]
    public async Task OpeningValidProjectReplacesRoomAndLuminairesAndClearsMap()
    {
        var scene = new EditorScene(new Room(8, 6), [new Luminaire(new Vector2(6, 5), Photometry.Sample)]);
        var revision = scene.Revision;
        scene.TryPublishResult(revision, Map);
        using var file = ProjectFileTests.Json("""
            {"formatVersion":1,"width":4,"depth":3,"lights":[]}
            """);

        var project = await ProjectFile.ReadAsync(file);
        scene.Replace(project.Room, project.Lights);

        Assert.That(scene.Room.Width, Is.EqualTo(4));
        Assert.That(scene.Room.Depth, Is.EqualTo(3));
        Assert.That(scene.Lights, Is.Empty);
        Assert.That(scene.Result, Is.Null);
        Assert.That(scene.TryPublishResult(revision, Map), Is.False);
    }
}
