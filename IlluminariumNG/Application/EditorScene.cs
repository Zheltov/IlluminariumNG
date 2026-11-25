using System.Numerics;
using IlluminariumNG.Domain;

namespace IlluminariumNG.Application;

internal sealed class EditorScene(Room room, IEnumerable<Luminaire> lights)
{
    private readonly List<Luminaire> _lights = lights.ToList();
    private readonly Stack<SceneState> _undo = new();
    private SceneState? _gestureStart;
    private long _nextStateId;

    private sealed record SceneState(Room Room, Luminaire[] Lights, IReadOnlyList<InteriorElement> Elements, long Id);

    public Room Room { get; private set; } = room;
    public IReadOnlyList<Luminaire> Lights => _lights;
    public IReadOnlyList<InteriorElement> Elements { get; private set; } = Array.Empty<InteriorElement>();
    public double[,]? Result { get; private set; }
    public int Revision { get; private set; }
    public long StateId { get; private set; }
    public long SavedStateId { get; private set; }
    public bool IsDirty => StateId != SavedStateId;
    public bool CanUndo => _undo.Count > 0;

    public LightingProject SnapshotProject() => new(Room, _lights.ToArray()) { Elements = Elements };

    public async Task<bool> SaveAsync(IProjectFile file, string path)
    {
        var snapshot = SnapshotProject();
        var stateId = StateId;
        await file.SaveAsync(path, snapshot);
        MarkSaved(stateId);
        return !IsDirty;
    }

    public async Task<double[,]?> CalculateAsync(CancellationToken cancellation)
    {
        var room = Room;
        var lights = _lights.ToArray();
        var revision = Revision;
        var result = await Task.Run(() => Illuminance.Calculate(room, lights, cancellation));
        return !cancellation.IsCancellationRequested && TryPublishResult(revision, result) ? result : null;
    }

    private void MarkSaved(long stateId) => SavedStateId = stateId;

    public void BeginGesture() => _gestureStart ??= Capture();

    public void EndGesture()
    {
        if (_gestureStart is not { } start)
            return;

        _gestureStart = null;
        if (start.Id == StateId)
            return;
        if (ReferenceEquals(Room, start.Room) && ReferenceEquals(Elements, start.Elements) &&
            _lights.SequenceEqual(start.Lights))
        {
            StateId = start.Id;
            return;
        }
        _undo.Push(start);
    }

    public bool TryUndo()
    {
        EndGesture();
        if (!_undo.TryPop(out var previous))
            return false;

        Room = previous.Room;
        Elements = previous.Elements;
        _lights.Clear();
        _lights.AddRange(previous.Lights);
        StateId = previous.Id;
        InvalidateResult();
        return true;
    }

    private SceneState Capture() => new(Room, _lights.ToArray(), Elements, StateId);

    private void RecordEdit()
    {
        if (_gestureStart is null)
            _undo.Push(Capture());
        StateId = ++_nextStateId;
    }

    public bool TryApplyOutline(IEnumerable<RoomPoint> vertices, double height, out string? error)
    {
        Room candidate;
        try
        {
            candidate = new Room(vertices, height);
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }

        if (_lights.Any(light => !candidate.Contains(light.Position)))
        {
            error = "Новый контур оставляет светильник за пределами помещения.";
            return false;
        }

        if (candidate.Height != Room.Height || !candidate.Vertices.SequenceEqual(Room.Vertices))
        {
            var elements = new InteriorElement[Elements.Count];
            for (var i = 0; i < elements.Length; i++)
            {
                if (Elements[i] is not WallOpening opening)
                {
                    elements[i] = Elements[i];
                    continue;
                }

                var original = Room.Wall(opening.Wall);
                var wall = Enumerable.Range(0, candidate.Vertices.Count).FirstOrDefault(index =>
                {
                    var span = candidate.Wall(index);
                    return span == original || (span.Start == original.End && span.End == original.Start);
                }, -1);
                if (wall < 0)
                {
                    error = "Сначала измените или удалите проём на изменяемой стене.";
                    return false;
                }

                var offset = candidate.Wall(wall) == original
                    ? opening.Offset
                    : original.Length - (opening.Offset + opening.Width);
                elements[i] = opening with { Wall = wall, Offset = offset };
            }

            error = InteriorGeometry.Validate(candidate, elements);
            if (error is not null)
                return false;

            RecordEdit();
            Room = candidate;
            Elements = Array.AsReadOnly(elements);
            InvalidateResult();
        }

        error = null;
        return true;
    }

    public bool TrySetElement(int? index, InteriorElement element, out string? error)
    {
        var elements = Elements.ToList();
        if (index is { } existing)
            elements[existing] = element;
        else
            elements.Add(element);

        error = InteriorGeometry.Validate(Room, elements);
        if (error is not null)
            return false;

        if (index is null || element != Elements[index.Value])
        {
            RecordEdit();
            Elements = elements.AsReadOnly();
            InvalidateResult();
        }
        return true;
    }

    public bool TrySetPartitionOpening(int partitionIndex, int? openingIndex, OpeningCut opening, out string? error)
    {
        var partition = (Partition)Elements[partitionIndex];
        var openings = partition.Openings;
        if (openingIndex is { } index && openings[index] == opening)
        {
            error = null;
            return true;
        }

        return TrySetElement(partitionIndex, partition with
        {
            Openings = openingIndex is { } existing ? openings.SetItem(existing, opening) : openings.Add(opening)
        }, out error);
    }

    public void RemovePartitionOpening(int partitionIndex, int openingIndex)
    {
        var partition = (Partition)Elements[partitionIndex];
        var elements = Elements.ToList();
        elements[partitionIndex] = partition with { Openings = partition.Openings.RemoveAt(openingIndex) };
        RecordEdit();
        Elements = elements.AsReadOnly();
        InvalidateResult();
    }

    public void RemoveElement(int index)
    {
        var elements = Elements.ToList();
        elements.RemoveAt(index);
        RecordEdit();
        Elements = elements.AsReadOnly();
        InvalidateResult();
    }

    public bool TryAddLight(Photometry distribution)
    {
        var point = Room.InteriorPoint();
        var position = new Vector2((float)point.X, (float)point.Y);
        if (!Room.Contains(position))
            return false;

        RecordEdit();
        _lights.Add(new Luminaire(position, distribution));
        InvalidateResult();
        return true;
    }

    public bool TryMoveLight(int index, Vector2 position, bool snapToGrid = true)
    {
        var snapped = snapToGrid ? new Vector2((float)PlanSnap.Round(position.X), (float)PlanSnap.Round(position.Y)) : position;
        if (!Room.Contains(snapped) || snapped == _lights[index].Position)
            return false;

        RecordEdit();
        _lights[index] = _lights[index] with { Position = snapped };
        InvalidateResult();
        return true;
    }

    public void RemoveLight(int index)
    {
        RecordEdit();
        _lights.RemoveAt(index);
        InvalidateResult();
    }

    public void Replace(Room replacement, IEnumerable<Luminaire> lights, IEnumerable<InteriorElement>? elements = null)
    {
        var snapshot = lights.ToArray();
        var elementSnapshot = elements?.ToArray() ?? [];
        _undo.Clear();
        _gestureStart = null;
        StateId = ++_nextStateId;
        MarkSaved(StateId);
        Room = replacement;
        Elements = Array.AsReadOnly(elementSnapshot);
        _lights.Clear();
        _lights.AddRange(snapshot);
        InvalidateResult();
    }

    public bool TryPublishResult(int revision, double[,] result)
    {
        if (revision != Revision)
            return false;

        Result = result;
        return true;
    }

    private void InvalidateResult()
    {
        Revision++;
        Result = null;
    }
}
