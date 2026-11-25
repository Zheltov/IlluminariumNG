using System.Numerics;
using NUnit.Framework;
using IlluminariumNG.Application;
using IlluminariumNG.Domain;

namespace IlluminariumNG.Tests;

public class SceneUndoTests
{
    [Test]
    public void UndoRestoresRemovedPartitionWithOpeningsAndThenOriginalRoom()
    {
        var original = new Room(8, 6);
        var scene = new EditorScene(original, []);
        RoomPoint[] vertices = [new(0, 0), new(8, 0), new(8, 4), new(3, 4), new(3, 6), new(0, 6)];
        Assert.That(scene.TryApplyOutline(vertices, 3.4, out var error), Is.True, error);
        var partition = new Partition(new(1, 1), new(5, 1), 2.5) { Openings = [new(1, 0.9, 0, 2)] };
        Assert.That(scene.TrySetElement(null, partition, out error), Is.True, error);
        scene.RemoveElement(0);

        Assert.That(scene.TryUndo(), Is.True);
        Assert.That(scene.Elements.Single(), Is.EqualTo(partition));
        Assert.That(scene.Room.Height, Is.EqualTo(3.4));
        Assert.That(scene.Room.Vertices, Is.EqualTo(vertices));
        Assert.That(scene.TryUndo(), Is.True);
        Assert.That(scene.Elements, Is.Empty);
        Assert.That(scene.TryUndo(), Is.True);
        Assert.That(scene.Room, Is.SameAs(original));
        Assert.That(scene.IsDirty, Is.False);
    }

    [Test]
    public void MultipleMovesInOneGestureAreUndoneTogether()
    {
        var lamp = new Luminaire(new Vector2(2, 2), Photometry.Sample);
        var scene = new EditorScene(new Room(8, 6), [lamp]);
        scene.BeginGesture();
        Assert.That(scene.TryMoveLight(0, new(2.12f, 2.48f), snapToGrid: false), Is.True);
        Assert.That(scene.Lights[0].Position, Is.EqualTo(new Vector2(2.12f, 2.48f)));
        Assert.That(scene.TryMoveLight(0, new(3, 3)), Is.True);
        scene.EndGesture();
        scene.EndGesture();

        Assert.That(scene.TryUndo(), Is.True);
        Assert.That(scene.Lights.Single(), Is.EqualTo(lamp));
        Assert.That(scene.TryUndo(), Is.False);
    }

    [Test]
    public void UndoDuringGestureClosesItAndRestoresPreDragState()
    {
        var lamp = new Luminaire(new Vector2(2, 2), Photometry.Sample);
        var scene = new EditorScene(new Room(8, 6), [lamp]);
        scene.BeginGesture();
        scene.TryMoveLight(0, new(3, 3));

        Assert.That(scene.TryUndo(), Is.True);
        scene.EndGesture();

        Assert.That(scene.Lights.Single(), Is.EqualTo(lamp));
        Assert.That(scene.TryUndo(), Is.False);
        Assert.That(scene.IsDirty, Is.False);
    }

    [Test]
    public void RejectedAndEmptyGesturesDoNotAddHistory()
    {
        var scene = new EditorScene(new Room(8, 6), [new Luminaire(new Vector2(2, 2), Photometry.Sample)]);
        var originalId = scene.StateId;
        Assert.That(scene.TryApplyOutline([new(0, 0), new(3, 3), new(0, 3), new(3, 0)], 2.8, out _), Is.False);
        Assert.That(scene.TryApplyOutline(scene.Room.Vertices, scene.Room.Height, out _), Is.True);
        scene.BeginGesture();
        Assert.That(scene.TryMoveLight(0, new(-1, -1)), Is.False);
        scene.EndGesture();
        scene.BeginGesture();
        Assert.That(scene.TryMoveLight(0, new(3, 3)), Is.True);
        Assert.That(scene.TryMoveLight(0, new(2, 2)), Is.True);
        scene.EndGesture();

        Assert.That(scene.StateId, Is.EqualTo(originalId));
        Assert.That(scene.IsDirty, Is.False);
        Assert.That(scene.TryUndo(), Is.False);
    }

    [Test]
    public async Task LateSaveTracksWrittenStateAndUndoRestoresCleanState()
    {
        var scene = new EditorScene(new Room(8, 6), []);
        Assert.That(scene.TryAddLight(Photometry.Sample), Is.True);
        var file = new PendingProjectFile();
        var save = scene.SaveAsync(file, "project.lighting.json");
        Assert.That(scene.TryMoveLight(0, new(3, 3)), Is.True);
        var inFlight = scene.Revision;
        scene.TryPublishResult(inFlight, new double[,] { { 1 } });
        file.Complete();
        Assert.That(await save, Is.False);
        Assert.That(file.Snapshot!.Lights.Single().Position, Is.EqualTo(new Vector2(4, 3)));

        Assert.That(scene.IsDirty, Is.True);
        Assert.That(scene.TryUndo(), Is.True);
        Assert.That(scene.IsDirty, Is.False);
        Assert.That(scene.Result, Is.Null);
        Assert.That(scene.Revision, Is.GreaterThan(inFlight));
        Assert.That(scene.TryPublishResult(inFlight, new double[,] { { 1 } }), Is.False);
        Assert.That(scene.TryUndo(), Is.True);
        Assert.That(scene.Lights, Is.Empty);
        Assert.That(scene.IsDirty, Is.True);
    }

    [Test]
    public void OpeningAnotherProjectClearsHistoryAndPendingGesture()
    {
        var scene = new EditorScene(new Room(8, 6), []);
        scene.TryAddLight(Photometry.Sample);
        scene.BeginGesture();
        scene.TryMoveLight(0, new(3, 3));

        var replacement = new Room(4, 4);
        scene.Replace(replacement, []);
        scene.EndGesture();

        Assert.That(scene.Room, Is.SameAs(replacement));
        Assert.That(scene.IsDirty, Is.False);
        Assert.That(scene.TryUndo(), Is.False);
        Assert.That(scene.Lights, Is.Empty);
    }
    private sealed class PendingProjectFile : IProjectFile
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public LightingProject? Snapshot { get; private set; }

        public Task<LightingProject> ReadAsync(Stream stream) => throw new NotSupportedException();

        public Task SaveAsync(string path, LightingProject project)
        {
            Snapshot = project;
            return _completion.Task;
        }

        public void Complete() => _completion.SetResult();
    }
}
