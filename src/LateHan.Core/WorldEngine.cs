using System.Collections.ObjectModel;
using System.Globalization;

namespace LateHan.Core;

public sealed record LocationView(
    long Minute,
    string PlaceId,
    string PlaceName,
    IReadOnlyList<(string Id, string Name)> VisibleActors);

public sealed record StatusView(
    long Minute,
    string ActorId,
    string ActorName,
    string PlaceId,
    string PlaceName,
    IReadOnlyList<(string Id, string Name)> HeldItems,
    IReadOnlyList<CommitmentState> OpenCommitments);

public sealed record ActionResult(long StartMinute, long EndMinute, IReadOnlyList<WorldEvent> Events);

public sealed class WorldEngine
{
    public WorldEngine(WorldState state)
    {
        State = state;
    }

    public WorldState State { get; }

    public LocationView Look(string? actorId = null)
    {
        var actor = GetActor(actorId ?? State.PlayerActorId);
        var place = GetPlace(actor.LocationId);
        var visibleActors = State.Actors.Values
            .Where(candidate => candidate.LocationId == actor.LocationId && candidate.Id != actor.Id)
            .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
            .Select(candidate => (candidate.Id, candidate.Name))
            .ToArray();

        return new LocationView(State.CurrentMinute, place.Id, place.Name, visibleActors);
    }

    public StatusView Status(string? actorId = null)
    {
        var actor = GetActor(actorId ?? State.PlayerActorId);
        var place = GetPlace(actor.LocationId);
        var heldItems = State.Items.Values
            .Where(item => item.HolderId == actor.Id)
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => (item.Id, item.Name))
            .ToArray();
        var commitments = State.Commitments.Values
            .Where(commitment => commitment.DebtorId == actor.Id && commitment.Status == "open")
            .OrderBy(commitment => commitment.DueMinute)
            .ThenBy(commitment => commitment.Id, StringComparer.Ordinal)
            .ToArray();

        return new StatusView(State.CurrentMinute, actor.Id, actor.Name, place.Id, place.Name, heldItems, commitments);
    }

    public ActionResult Move(string actorId, string destinationPlaceId, TravelMode mode)
    {
        var actor = GetActor(actorId);
        _ = GetPlace(destinationPlaceId);
        if (actor.LocationId == destinationPlaceId)
        {
            throw new DomainCommandException("already_at_destination", $"Actor '{actorId}' is already at '{destinationPlaceId}'.");
        }

        var path = FindShortestPath(actor.LocationId, destinationPlaceId, mode);
        var startMinute = State.CurrentMinute;
        var totalMinutes = path.Sum(route => route.GetMinutes(mode));
        var started = AppendEvent(
            "travel_started",
            startMinute,
            actor.LocationId,
            [actorId],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["destination"] = destinationPlaceId,
                ["mode"] = ToModeId(mode),
                ["route_ids"] = string.Join(',', path.Select(route => route.Id)),
                ["expected_minutes"] = totalMinutes.ToString(CultureInfo.InvariantCulture),
            });

        State.CurrentMinute += totalMinutes;
        actor.LocationId = destinationPlaceId;
        var completed = AppendEvent(
            "travel_completed",
            State.CurrentMinute,
            destinationPlaceId,
            [actorId],
            [started.Id],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["origin"] = started.LocationId ?? string.Empty,
                ["mode"] = ToModeId(mode),
                ["elapsed_minutes"] = totalMinutes.ToString(CultureInfo.InvariantCulture),
            });

        return new ActionResult(startMinute, State.CurrentMinute, [started, completed]);
    }

    public ActionResult Deliver(string actorId, string itemId, string recipientId)
    {
        var actor = GetActor(actorId);
        var recipient = GetActor(recipientId);
        if (actor.LocationId != recipient.LocationId)
        {
            throw new DomainCommandException("recipient_not_present", $"Recipient '{recipientId}' is not at '{actor.LocationId}'.");
        }

        if (!State.Items.TryGetValue(itemId, out var item))
        {
            throw new DomainCommandException("unknown_item", $"Unknown item '{itemId}'.");
        }

        if (item.HolderId != actorId)
        {
            throw new DomainCommandException("item_not_held", $"Actor '{actorId}' does not hold '{itemId}'.");
        }

        var startMinute = State.CurrentMinute;
        var started = AppendEvent(
            "delivery_started",
            startMinute,
            actor.LocationId,
            [actorId, recipientId, itemId],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal));

        State.CurrentMinute += 5;
        item.HolderId = recipientId;
        var transferred = AppendEvent(
            "item_transferred",
            State.CurrentMinute,
            actor.LocationId,
            [actorId, recipientId, itemId],
            [started.Id],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["from"] = actorId,
                ["to"] = recipientId,
            });

        var events = new List<WorldEvent> { started, transferred };
        var matchingCommitments = State.Commitments.Values
            .Where(commitment => commitment.Status == "open")
            .Where(commitment => commitment.DebtorId == actorId)
            .Where(commitment => commitment.Action == "deliver")
            .Where(commitment => commitment.TargetId == itemId && commitment.RecipientId == recipientId)
            .OrderBy(commitment => commitment.Id, StringComparer.Ordinal)
            .ToArray();

        foreach (var commitment in matchingCommitments)
        {
            commitment.Status = "completed";
            events.Add(AppendEvent(
                "commitment_completed",
                State.CurrentMinute,
                actor.LocationId,
                [commitment.Id, actorId, recipientId],
                [transferred.Id],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["completion_kind"] = "delivered",
                }));
        }

        return new ActionResult(startMinute, State.CurrentMinute, events);
    }

    public ActionResult Tell(string actorId, string recipientId, string propositionId)
    {
        var actor = GetActor(actorId);
        var recipient = GetActor(recipientId);
        if (actor.LocationId != recipient.LocationId)
        {
            throw new DomainCommandException("recipient_not_present", $"Recipient '{recipientId}' is not at '{actor.LocationId}'.");
        }

        var startMinute = State.CurrentMinute;
        State.CurrentMinute += 5;
        var told = AppendEvent(
            "proposition_told",
            State.CurrentMinute,
            actor.LocationId,
            [actorId, recipientId],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["proposition_id"] = propositionId,
            });

        var events = new List<WorldEvent> { told };
        var completedTargets = State.Commitments.Values
            .Where(commitment => commitment.Status == "completed")
            .Select(commitment => commitment.Id)
            .ToHashSet(StringComparer.Ordinal);
        var reportCommitments = State.Commitments.Values
            .Where(commitment => commitment.Status == "open")
            .Where(commitment => commitment.DebtorId == actorId && commitment.RecipientId == recipientId)
            .Where(commitment => commitment.Action == "report_delivery_result")
            .Where(commitment => completedTargets.Contains(commitment.TargetId))
            .OrderBy(commitment => commitment.Id, StringComparer.Ordinal)
            .ToArray();

        foreach (var commitment in reportCommitments)
        {
            commitment.Status = "completed";
            events.Add(AppendEvent(
                "commitment_completed",
                State.CurrentMinute,
                actor.LocationId,
                [commitment.Id, actorId, recipientId],
                [told.Id],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["completion_kind"] = "reported",
                }));
        }

        return new ActionResult(startMinute, State.CurrentMinute, events);
    }

    public ActionResult Wait(long minutes, string? actorId = null)
    {
        if (minutes <= 0)
        {
            throw new DomainCommandException("invalid_duration", "Wait duration must be positive.");
        }

        var actor = GetActor(actorId ?? State.PlayerActorId);
        var startMinute = State.CurrentMinute;
        var started = AppendEvent(
            "wait_started",
            startMinute,
            actor.LocationId,
            [actor.Id],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["duration_minutes"] = minutes.ToString(CultureInfo.InvariantCulture),
            });
        State.CurrentMinute += minutes;
        var completed = AppendEvent(
            "wait_completed",
            State.CurrentMinute,
            actor.LocationId,
            [actor.Id],
            [started.Id],
            new Dictionary<string, string>(StringComparer.Ordinal));

        return new ActionResult(startMinute, State.CurrentMinute, [started, completed]);
    }

    private ActorState GetActor(string actorId)
    {
        return State.Actors.TryGetValue(actorId, out var actor)
            ? actor
            : throw new DomainCommandException("unknown_actor", $"Unknown actor '{actorId}'.");
    }

    private PlaceDefinition GetPlace(string placeId)
    {
        return State.Places.TryGetValue(placeId, out var place)
            ? place
            : throw new DomainCommandException("unknown_place", $"Unknown place '{placeId}'.");
    }

    private IReadOnlyList<RouteDefinition> FindShortestPath(string startPlaceId, string destinationPlaceId, TravelMode mode)
    {
        var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [startPlaceId] = 0 };
        var previous = new Dictionary<string, (string PlaceId, RouteDefinition Route)>(StringComparer.Ordinal);
        var frontier = new SortedSet<PathCandidate>(PathCandidateComparer.Instance)
        {
            new(startPlaceId, 0),
        };

        while (frontier.Count > 0)
        {
            var current = frontier.Min!;
            frontier.Remove(current);
            if (current.Cost != distances[current.PlaceId])
            {
                continue;
            }

            if (current.PlaceId == destinationPlaceId)
            {
                break;
            }

            foreach (var edge in GetEdges(current.PlaceId, mode))
            {
                var nextCost = checked(current.Cost + edge.Route.GetMinutes(mode));
                if (distances.TryGetValue(edge.NextPlaceId, out var knownCost) && knownCost <= nextCost)
                {
                    continue;
                }

                distances[edge.NextPlaceId] = nextCost;
                previous[edge.NextPlaceId] = (current.PlaceId, edge.Route);
                frontier.Add(new PathCandidate(edge.NextPlaceId, nextCost));
            }
        }

        if (!distances.ContainsKey(destinationPlaceId))
        {
            throw new DomainCommandException("destination_unreachable", $"No {mode} route reaches '{destinationPlaceId}'.");
        }

        var path = new List<RouteDefinition>();
        var cursor = destinationPlaceId;
        while (cursor != startPlaceId)
        {
            var step = previous[cursor];
            path.Add(step.Route);
            cursor = step.PlaceId;
        }

        path.Reverse();
        return path;
    }

    private IEnumerable<(string NextPlaceId, RouteDefinition Route)> GetEdges(string placeId, TravelMode mode)
    {
        foreach (var route in State.Routes.Values.OrderBy(route => route.Id, StringComparer.Ordinal))
        {
            if (!route.MinutesByMode.ContainsKey(mode))
            {
                continue;
            }

            if (route.FromPlaceId == placeId)
            {
                yield return (route.ToPlaceId, route);
            }
            else if (route.Bidirectional && route.ToPlaceId == placeId)
            {
                yield return (route.FromPlaceId, route);
            }
        }
    }

    private WorldEvent AppendEvent(
        string type,
        long minute,
        string? locationId,
        IReadOnlyList<string> subjectIds,
        IReadOnlyList<string> causeIds,
        IReadOnlyDictionary<string, string> details)
    {
        var sequence = State.NextEventSequence++;
        var worldEvent = new WorldEvent(
            sequence,
            $"event.{sequence:D8}",
            type,
            minute,
            locationId,
            subjectIds.ToArray(),
            causeIds.ToArray(),
            new ReadOnlyDictionary<string, string>(
                new SortedDictionary<string, string>(
                    details.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                    StringComparer.Ordinal)));
        State.AddEvent(worldEvent);
        return worldEvent;
    }

    private static string ToModeId(TravelMode mode) => mode switch
    {
        TravelMode.Walk => "walk",
        TravelMode.Horse => "horse",
        TravelMode.WithGroup => "with-group",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private sealed record PathCandidate(string PlaceId, int Cost);

    private sealed class PathCandidateComparer : IComparer<PathCandidate>
    {
        public static PathCandidateComparer Instance { get; } = new();

        public int Compare(PathCandidate? x, PathCandidate? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var cost = x.Cost.CompareTo(y.Cost);
            return cost != 0 ? cost : StringComparer.Ordinal.Compare(x.PlaceId, y.PlaceId);
        }
    }
}
