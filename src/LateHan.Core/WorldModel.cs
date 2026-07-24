using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LateHan.Core;

public static class EngineMetadata
{
    public const string Version = "1.0.0-spike";
}

public enum TravelMode
{
    Walk,
    Horse,
    WithGroup,
}

public sealed class ActorState
{
    private readonly IReadOnlyList<ActorMembership> _memberships;

    public ActorState(string id, string name, string locationId)
        : this(id, name, locationId, null, null)
    {
    }

    public ActorState(
        string id,
        string name,
        string? placeId,
        TransitPositionState? transit,
        IEnumerable<ActorMembership>? memberships = null,
        SimulationDetailLevel detailLevel = SimulationDetailLevel.L1,
        string? promotedFromGroupId = null,
        string? identitySeedHex = null,
        bool isTemporaryPromotion = false,
        long remoteCycleCount = 0,
        long? lastRemoteUpdateMinute = null,
        string? lastRemoteUpdateEventId = null)
    {
        if ((placeId is null) == (transit is null))
        {
            throw new ArgumentException("An actor must be at exactly one place or transit position.", nameof(placeId));
        }

        if (remoteCycleCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(remoteCycleCount));
        }

        Id = id;
        Name = name;
        PlaceId = placeId;
        Transit = transit;
        _memberships = new ReadOnlyCollection<ActorMembership>((memberships ?? []).ToArray());
        DetailLevel = detailLevel;
        PromotedFromGroupId = promotedFromGroupId;
        IdentitySeedHex = identitySeedHex;
        IsTemporaryPromotion = isTemporaryPromotion;
        RemoteCycleCount = remoteCycleCount;
        LastRemoteUpdateMinute = lastRemoteUpdateMinute;
        LastRemoteUpdateEventId = lastRemoteUpdateEventId;
    }

    public string Id { get; }

    public string Name { get; }

    public string? PlaceId { get; internal set; }

    public TransitPositionState? Transit { get; internal set; }

    public IReadOnlyList<ActorMembership> Memberships => _memberships;

    public SimulationDetailLevel DetailLevel { get; internal set; }

    public string? PromotedFromGroupId { get; }

    public string? IdentitySeedHex { get; }

    public bool IsTemporaryPromotion { get; }

    public long RemoteCycleCount { get; internal set; }

    public long? LastRemoteUpdateMinute { get; internal set; }

    public string? LastRemoteUpdateEventId { get; internal set; }

    public string LocationId
    {
        get => PlaceId ?? throw new InvalidOperationException($"Actor '{Id}' is currently in transit.");
        internal set
        {
            PlaceId = value;
            Transit = null;
        }
    }

    public bool IsInTransit => Transit is not null;

    internal void BeginTransit(TransitPositionState transit)
    {
        PlaceId = null;
        Transit = transit;
    }
}

public sealed record PlaceDefinition(string Id, string Name, string AccessRuleId, string? ControllerId);

public sealed class RouteDefinition
{
    private readonly IReadOnlyDictionary<TravelMode, int> _minutesByMode;

    public RouteDefinition(
        string id,
        string fromPlaceId,
        string toPlaceId,
        int distanceLiQ10,
        bool bidirectional,
        IReadOnlyDictionary<TravelMode, int> minutesByMode)
    {
        Id = id;
        FromPlaceId = fromPlaceId;
        ToPlaceId = toPlaceId;
        DistanceLiQ10 = distanceLiQ10;
        Bidirectional = bidirectional;
        _minutesByMode = new ReadOnlyDictionary<TravelMode, int>(
            new Dictionary<TravelMode, int>(minutesByMode));
    }

    public string Id { get; }

    public string FromPlaceId { get; }

    public string ToPlaceId { get; }

    public int DistanceLiQ10 { get; }

    public bool Bidirectional { get; }

    public IReadOnlyDictionary<TravelMode, int> MinutesByMode => _minutesByMode;

    public int GetMinutes(TravelMode mode)
    {
        if (!_minutesByMode.TryGetValue(mode, out var minutes))
        {
            throw new DomainCommandException("travel_mode_unavailable", $"Route '{Id}' does not support {mode}.");
        }

        return minutes;
    }
}

public sealed class ItemState
{
    private readonly IReadOnlyList<string> _propositionIds;
    private readonly IReadOnlyList<string> _validForAccessRuleIds;

    public ItemState(
        string id,
        string name,
        string kind,
        string holderId,
        string? authorId = null,
        string? intendedRecipientId = null,
        IEnumerable<string>? propositionIds = null,
        IEnumerable<string>? validForAccessRuleIds = null,
        long? expiresAtMinute = null,
        string readPolicy = "unreadable",
        string? sealBrokenEventId = null)
    {
        Id = id;
        Name = name;
        Kind = kind;
        HolderId = holderId;
        AuthorId = authorId;
        IntendedRecipientId = intendedRecipientId;
        _propositionIds = new ReadOnlyCollection<string>((propositionIds ?? []).ToArray());
        _validForAccessRuleIds = new ReadOnlyCollection<string>((validForAccessRuleIds ?? []).ToArray());
        ExpiresAtMinute = expiresAtMinute;
        ReadPolicy = readPolicy;
        SealBrokenEventId = sealBrokenEventId;
    }

    public string Id { get; }

    public string Name { get; }

    public string Kind { get; }

    public string HolderId { get; internal set; }

    public string? AuthorId { get; }

    public string? IntendedRecipientId { get; }

    public IReadOnlyList<string> PropositionIds => _propositionIds;

    public IReadOnlyList<string> ValidForAccessRuleIds => _validForAccessRuleIds;

    public long? ExpiresAtMinute { get; }

    public string ReadPolicy { get; }

    public string? SealBrokenEventId { get; internal set; }
}

public sealed class CommitmentState
{
    public CommitmentState(
        string id,
        string debtorId,
        string creditorId,
        string action,
        string targetId,
        string recipientId,
        long dueMinute,
        string status)
    {
        Id = id;
        DebtorId = debtorId;
        CreditorId = creditorId;
        Action = action;
        TargetId = targetId;
        RecipientId = recipientId;
        DueMinute = dueMinute;
        Status = status;
    }

    public string Id { get; }

    public string DebtorId { get; }

    public string CreditorId { get; }

    public string Action { get; }

    public string TargetId { get; }

    public string RecipientId { get; }

    public long DueMinute { get; }

    public string Status { get; internal set; }
}

public sealed record WorldEvent(
    long Sequence,
    string Id,
    string Type,
    long Minute,
    string? LocationId,
    IReadOnlyList<string> SubjectIds,
    IReadOnlyList<string> CauseIds,
    IReadOnlyDictionary<string, string> Details);

public sealed class WorldState
{
    private const int StackFingerprintBufferSize = 256;
    private static readonly byte[] FingerprintSeparator = [0];
    private readonly SortedDictionary<string, ActorState> _actors;
    private readonly SortedDictionary<string, PlaceDefinition> _places;
    private readonly SortedDictionary<string, RouteDefinition> _routes;
    private readonly SortedDictionary<string, ItemState> _items;
    private readonly SortedDictionary<string, CommitmentState> _commitments;
    private readonly List<WorldEvent> _events;
    private readonly SortedSet<ScheduledWorldEvent> _scheduledEvents;
    private readonly SortedDictionary<string, ActionInstanceState> _actions;
    private readonly SortedDictionary<string, BeliefState> _beliefs;
    private readonly SortedDictionary<string, PropositionDefinition> _propositions;
    private readonly SortedDictionary<string, PlanState> _plans;
    private readonly SortedDictionary<string, PlanResourceLockState> _planResourceLocks;
    private readonly SortedDictionary<string, AccessRuleDefinition> _accessRules;
    private readonly SortedDictionary<string, PlaceAccessState> _placeAccessStates;
    private readonly SortedDictionary<string, MessageState> _messages;
    private readonly SortedDictionary<string, GroupState> _groups;
    private readonly SortedSet<string> _detailDirtyActorIds;
    private readonly Dictionary<string, SortedSet<string>> _actorIdsByAttentionSpace;
    private readonly Dictionary<string, SortedSet<string>> _adjacentPlaceIds;
    private readonly Dictionary<string, RouteTraversal[]> _routeTraversalsByPlace;
    private readonly Dictionary<string, List<ActionInstanceState>> _actionsByActor;

    public WorldState(
        string scenarioId,
        string scenarioVersion,
        string rulesetVersion,
        string rngVersion,
        string engineVersion,
        string contentHash,
        string playerActorId,
        long currentMinute,
        IEnumerable<ActorState> actors,
        IEnumerable<PlaceDefinition> places,
        IEnumerable<RouteDefinition> routes,
        IEnumerable<ItemState> items,
        IEnumerable<CommitmentState> commitments,
        IEnumerable<WorldEvent>? events = null,
        long nextEventSequence = 1,
        string rngRootSeedHex = "0000000000000001",
        string rngDerivation = RandomMetadata.Sha256LittleEndianV1,
        IEnumerable<RandomStreamState>? randomStreams = null,
        IEnumerable<ScheduledWorldEvent>? scheduledEvents = null,
        long nextScheduledEventSequence = 1,
        bool replayModified = false,
        IEnumerable<ActionInstanceState>? actions = null,
        long nextActionSequence = 1,
        IEnumerable<BeliefState>? beliefs = null,
        IEnumerable<PlanState>? plans = null,
        IEnumerable<AccessRuleDefinition>? accessRules = null,
        IEnumerable<PlaceAccessState>? placeAccessStates = null,
        IEnumerable<MessageState>? messages = null,
        IEnumerable<PropositionDefinition>? propositions = null,
        IEnumerable<GroupState>? groups = null,
        long nextPromotionSequence = 1,
        IEnumerable<string>? detailDirtyActorIds = null,
        IEnumerable<PlanResourceLockState>? planResourceLocks = null)
    {
        ScenarioId = scenarioId;
        ScenarioVersion = scenarioVersion;
        RulesetVersion = rulesetVersion;
        RngVersion = rngVersion;
        EngineVersion = engineVersion;
        ContentHash = contentHash;
        PlayerActorId = playerActorId;
        CurrentMinute = currentMinute;
        _actors = new SortedDictionary<string, ActorState>(actors.ToDictionary(actor => actor.Id), StringComparer.Ordinal);
        _places = new SortedDictionary<string, PlaceDefinition>(places.ToDictionary(place => place.Id), StringComparer.Ordinal);
        _routes = new SortedDictionary<string, RouteDefinition>(routes.ToDictionary(route => route.Id), StringComparer.Ordinal);
        _items = new SortedDictionary<string, ItemState>(items.ToDictionary(item => item.Id), StringComparer.Ordinal);
        _commitments = new SortedDictionary<string, CommitmentState>(commitments.ToDictionary(item => item.Id), StringComparer.Ordinal);
        _events = events?.OrderBy(worldEvent => worldEvent.Sequence).ToList() ?? [];
        _scheduledEvents = new SortedSet<ScheduledWorldEvent>(scheduledEvents ?? [], ScheduledWorldEventComparer.Instance);
        _actions = new SortedDictionary<string, ActionInstanceState>(
            (actions ?? []).ToDictionary(action => action.Id),
            StringComparer.Ordinal);
        _beliefs = new SortedDictionary<string, BeliefState>(
            (beliefs ?? []).ToDictionary(belief => belief.Id),
            StringComparer.Ordinal);
        _propositions = new SortedDictionary<string, PropositionDefinition>(
            (propositions ?? []).ToDictionary(proposition => proposition.Id),
            StringComparer.Ordinal);
        _plans = new SortedDictionary<string, PlanState>(
            (plans ?? []).ToDictionary(plan => plan.Id),
            StringComparer.Ordinal);
        _planResourceLocks = new SortedDictionary<string, PlanResourceLockState>(
            (planResourceLocks ?? []).ToDictionary(item => item.ResourceId),
            StringComparer.Ordinal);
        if (_planResourceLocks.Values.Any(item =>
                !_plans.TryGetValue(item.PlanId, out var plan) ||
                !plan.RequiredResourceIds.Contains(item.ResourceId, StringComparer.Ordinal) ||
                plan.Status != PlanStatus.Running ||
                !_events.Any(worldEvent =>
                    string.Equals(worldEvent.Id, item.AcquiredEventId, StringComparison.Ordinal) &&
                    worldEvent.Type == "plan_resources_acquired")))
        {
            throw new ArgumentException("Plan resource locks must belong to running plans that require them.", nameof(planResourceLocks));
        }
        _accessRules = new SortedDictionary<string, AccessRuleDefinition>(
            (accessRules ?? []).ToDictionary(rule => rule.Id),
            StringComparer.Ordinal);
        _placeAccessStates = new SortedDictionary<string, PlaceAccessState>(
            (placeAccessStates ?? []).ToDictionary(item => item.PlaceId),
            StringComparer.Ordinal);
        _messages = new SortedDictionary<string, MessageState>(
            (messages ?? []).ToDictionary(message => message.Id),
            StringComparer.Ordinal);
        _groups = new SortedDictionary<string, GroupState>(
            (groups ?? []).ToDictionary(group => group.Id),
            StringComparer.Ordinal);
        if (_groups.Values.Any(group => group.LastRemoteSettlementMinute > currentMinute))
        {
            throw new ArgumentException(
                "A group remote settlement cursor cannot be later than the world minute.",
                nameof(groups));
        }
        ValidateCognitionReferences();
        _detailDirtyActorIds = new SortedSet<string>(detailDirtyActorIds ?? [], StringComparer.Ordinal);
        if (_detailDirtyActorIds.Any(actorId => !_actors.ContainsKey(actorId)))
        {
            throw new ArgumentException("Detail dirty set contains an unknown actor.", nameof(detailDirtyActorIds));
        }

        _actorIdsByAttentionSpace = BuildAttentionSpaceIndex(_actors.Values);
        _adjacentPlaceIds = BuildAdjacencyIndex(_places.Keys, _routes.Values);
        _routeTraversalsByPlace = BuildRouteTraversalIndex(_places.Keys, _routes.Values);
        _actionsByActor = BuildActorActionIndex(_actions.Values);
        if (_scheduledEvents.Any(item => item.DueMinute < currentMinute))
        {
            throw new ArgumentException("Scheduled events cannot be earlier than the current world minute.", nameof(scheduledEvents));
        }

        if (nextEventSequence <= _events.Select(item => item.Sequence).DefaultIfEmpty(0).Max())
        {
            throw new ArgumentOutOfRangeException(nameof(nextEventSequence), "Event sequence cursor must exceed every event sequence.");
        }

        if (nextScheduledEventSequence <= _scheduledEvents.Select(item => item.Sequence).DefaultIfEmpty(0).Max())
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextScheduledEventSequence),
                "Scheduled event sequence cursor must exceed every queued sequence.");
        }

        if (nextActionSequence <= _actions.Values.Select(item => item.Sequence).DefaultIfEmpty(0).Max())
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextActionSequence),
                "Action sequence cursor must exceed every action sequence.");
        }

        NextEventSequence = nextEventSequence;
        NextScheduledEventSequence = nextScheduledEventSequence;
        NextActionSequence = nextActionSequence;
        if (nextPromotionSequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(nextPromotionSequence));
        }

        NextPromotionSequence = nextPromotionSequence;
        RandomStreams = new RandomStreamRegistry(rngVersion, rngRootSeedHex, rngDerivation, randomStreams);
        ReplayModified = replayModified;
    }

    public string ScenarioId { get; }

    public string ScenarioVersion { get; }

    public string RulesetVersion { get; }

    public string RngVersion { get; }

    public string EngineVersion { get; }

    public string ContentHash { get; }

    public string PlayerActorId { get; }

    public long CurrentMinute { get; internal set; }

    public IReadOnlyDictionary<string, ActorState> Actors => _actors;

    public IReadOnlyDictionary<string, PlaceDefinition> Places => _places;

    public IReadOnlyDictionary<string, RouteDefinition> Routes => _routes;

    public IReadOnlyDictionary<string, ItemState> Items => _items;

    public IReadOnlyDictionary<string, CommitmentState> Commitments => _commitments;

    public IReadOnlyList<WorldEvent> Events => _events;

    public IReadOnlyList<ScheduledWorldEvent> ScheduledEvents => _scheduledEvents.ToArray();

    public IReadOnlyDictionary<string, ActionInstanceState> Actions => _actions;

    public IReadOnlyDictionary<string, BeliefState> Beliefs => _beliefs;

    public IReadOnlyDictionary<string, PropositionDefinition> Propositions => _propositions;

    public IReadOnlyDictionary<string, PlanState> Plans => _plans;

    public IReadOnlyDictionary<string, PlanResourceLockState> PlanResourceLocks => _planResourceLocks;

    public IReadOnlyDictionary<string, AccessRuleDefinition> AccessRules => _accessRules;

    public IReadOnlyDictionary<string, PlaceAccessState> PlaceAccessStates => _placeAccessStates;

    public IReadOnlyDictionary<string, MessageState> Messages => _messages;

    public IReadOnlyDictionary<string, GroupState> Groups => _groups;

    public IReadOnlyCollection<string> DetailDirtyActorIds => _detailDirtyActorIds.ToArray();

    public RandomStreamRegistry RandomStreams { get; }

    public bool ReplayModified { get; internal set; }

    public long EventSequenceCursor => NextEventSequence;

    public long ScheduledEventSequenceCursor => NextScheduledEventSequence;

    public long ActionSequenceCursor => NextActionSequence;

    public long PromotionSequenceCursor => NextPromotionSequence;

    internal long NextEventSequence { get; set; }

    internal long NextScheduledEventSequence { get; set; }

    internal long NextActionSequence { get; set; }

    internal long NextPromotionSequence { get; set; }

    internal void AddEvent(WorldEvent worldEvent) => _events.Add(worldEvent);

    internal void AddScheduledEvent(ScheduledWorldEvent scheduledEvent)
    {
        if (!_scheduledEvents.Add(scheduledEvent))
        {
            throw new InvalidOperationException($"Duplicate scheduled event '{scheduledEvent.Id}'.");
        }
    }

    internal ScheduledWorldEvent? PeekScheduledEvent() => _scheduledEvents.Count == 0 ? null : _scheduledEvents.Min;

    internal bool RemoveScheduledEvent(ScheduledWorldEvent scheduledEvent) => _scheduledEvents.Remove(scheduledEvent);

    internal bool RemoveScheduledEvent(string scheduledEventId)
    {
        var scheduledEvent = _scheduledEvents.FirstOrDefault(
            item => string.Equals(item.Id, scheduledEventId, StringComparison.Ordinal));
        return scheduledEvent is not null && _scheduledEvents.Remove(scheduledEvent);
    }

    internal void AddAction(ActionInstanceState action)
    {
        if (!_actions.TryAdd(action.Id, action))
        {
            throw new InvalidOperationException($"Duplicate action '{action.Id}'.");
        }

        AddActionToIndex(_actionsByActor, action);
    }

    internal IReadOnlyList<ActionInstanceState> GetActionsByActor(string actorId) =>
        _actionsByActor.TryGetValue(actorId, out var actions) ? actions : [];

    internal IReadOnlyList<RouteTraversal> GetRouteTraversals(string placeId) =>
        _routeTraversalsByPlace.TryGetValue(placeId, out var traversals) ? traversals : [];

    internal void AddActor(ActorState actor)
    {
        if (!_actors.TryAdd(actor.Id, actor))
        {
            throw new InvalidOperationException($"Duplicate actor '{actor.Id}'.");
        }

        AddActorToAttentionIndex(actor);
    }

    internal void RemoveActor(string actorId)
    {
        if (!_actors.TryGetValue(actorId, out var actor))
        {
            throw new InvalidOperationException($"Actor '{actorId}' is missing.");
        }

        RemoveActorFromAttentionIndex(actor);
        _actors.Remove(actorId);
        _detailDirtyActorIds.Remove(actorId);
    }

    internal void MoveActorToPlace(string actorId, string placeId)
    {
        var actor = _actors[actorId];
        RemoveActorFromAttentionIndex(actor);
        actor.LocationId = placeId;
        AddActorToAttentionIndex(actor);
    }

    internal void BeginActorTransit(string actorId, TransitPositionState transit)
    {
        var actor = _actors[actorId];
        RemoveActorFromAttentionIndex(actor);
        actor.BeginTransit(transit);
        AddActorToAttentionIndex(actor);
    }

    internal void MarkActorDetailDirty(string actorId)
    {
        if (!_actors.ContainsKey(actorId))
        {
            throw new InvalidOperationException($"Actor '{actorId}' is missing.");
        }

        _detailDirtyActorIds.Add(actorId);
    }

    internal void ClearActorDetailDirty(string actorId) => _detailDirtyActorIds.Remove(actorId);

    internal IReadOnlyCollection<string> GetActorIdsAtPlace(string placeId) =>
        GetActorIdsInAttentionSpace(PlaceAttentionKey(placeId));

    internal IReadOnlyCollection<string> GetActorIdsOnRoute(string routeId) =>
        GetActorIdsInAttentionSpace(RouteAttentionKey(routeId));

    internal IReadOnlyCollection<string> GetAdjacentPlaceIds(string placeId) =>
        _adjacentPlaceIds.TryGetValue(placeId, out var adjacent) ? adjacent.ToArray() : [];

    internal void AddBelief(BeliefState belief)
    {
        if (!_beliefs.TryAdd(belief.Id, belief))
        {
            throw new InvalidOperationException($"Duplicate belief '{belief.Id}'.");
        }
    }

    internal void RemoveBelief(string beliefId)
    {
        if (!_beliefs.Remove(beliefId))
        {
            throw new InvalidOperationException($"Belief '{beliefId}' is missing.");
        }
    }

    internal void AddMessage(MessageState message)
    {
        if (!_messages.TryAdd(message.Id, message))
        {
            throw new InvalidOperationException($"Duplicate message '{message.Id}'.");
        }
    }

    internal void AddPlaceAccessState(PlaceAccessState placeAccessState)
    {
        if (!_placeAccessStates.TryAdd(placeAccessState.PlaceId, placeAccessState))
        {
            throw new InvalidOperationException($"Duplicate place access state '{placeAccessState.PlaceId}'.");
        }
    }

    internal void AddPlanResourceLock(PlanResourceLockState resourceLock)
    {
        if (!_planResourceLocks.TryAdd(resourceLock.ResourceId, resourceLock))
        {
            throw new InvalidOperationException($"Plan resource '{resourceLock.ResourceId}' is already locked.");
        }
    }

    internal void RemovePlanResourceLock(string resourceId)
    {
        if (!_planResourceLocks.Remove(resourceId))
        {
            throw new InvalidOperationException($"Plan resource '{resourceId}' is not locked.");
        }
    }

    public string ComputeEventFingerprint()
        => WorldEventFingerprint.Compute(_events);

    public string ComputeMaterialStateFingerprint()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, CurrentMinute.ToString(CultureInfo.InvariantCulture));
        foreach (var actor in _actors.Values)
        {
            Append(hash, actor.Id);
            Append(hash, actor.PlaceId ?? string.Empty);
            Append(hash, actor.Transit?.RouteId ?? string.Empty);
            Append(hash, actor.DetailLevel.ToString());
            Append(hash, actor.PromotedFromGroupId ?? string.Empty);
            Append(hash, actor.IdentitySeedHex ?? string.Empty);
            Append(hash, actor.RemoteCycleCount.ToString(CultureInfo.InvariantCulture));
            Append(hash, actor.LastRemoteUpdateMinute?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            Append(hash, actor.LastRemoteUpdateEventId ?? string.Empty);
        }

        foreach (var group in _groups.Values)
        {
            Append(hash, group.Id);
            Append(hash, group.Count.ToString(CultureInfo.InvariantCulture));
            Append(hash, group.LocationId);
            Append(hash, group.FoodStockUnits.ToString(CultureInfo.InvariantCulture));
            Append(hash, group.DailyFoodProductionPerThousand.ToString(CultureInfo.InvariantCulture));
            Append(hash, group.DailyFoodConsumptionPerThousand.ToString(CultureInfo.InvariantCulture));
            Append(hash, group.LastRemoteSettlementMinute.ToString(CultureInfo.InvariantCulture));
            Append(hash, group.FoodProductionRemainder.ToString(CultureInfo.InvariantCulture));
            Append(hash, group.FoodDemandRemainder.ToString(CultureInfo.InvariantCulture));
            Append(hash, group.CumulativeFoodProduced.ToString(CultureInfo.InvariantCulture));
            Append(hash, group.CumulativeFoodDemand.ToString(CultureInfo.InvariantCulture));
            Append(hash, group.CumulativeFoodConsumed.ToString(CultureInfo.InvariantCulture));
            Append(hash, group.CumulativeFoodUnmet.ToString(CultureInfo.InvariantCulture));
            Append(hash, group.FoodShortageBp.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var item in _items.Values)
        {
            Append(hash, item.Id);
            Append(hash, item.HolderId);
            Append(hash, item.SealBrokenEventId ?? string.Empty);
        }

        foreach (var belief in _beliefs.Values)
        {
            Append(hash, belief.Id);
            Append(hash, belief.HolderId);
            Append(hash, belief.PropositionId);
            Append(hash, belief.ConfidenceBp.ToString(CultureInfo.InvariantCulture));
            Append(hash, belief.Source);
        }

        foreach (var proposition in _propositions.Values)
        {
            Append(hash, proposition.Id);
            Append(hash, proposition.TopicId);
            Append(hash, proposition.Stance);
            Append(hash, proposition.RetellingVariantId ?? string.Empty);
            Append(hash, proposition.DistortionChanceBp.ToString(CultureInfo.InvariantCulture));
            Append(hash, proposition.RetellingConfidenceLossBp.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var commitment in _commitments.Values)
        {
            Append(hash, commitment.Id);
            Append(hash, commitment.Status);
        }

        foreach (var plan in _plans.Values)
        {
            Append(hash, plan.Id);
            Append(hash, plan.Status.ToString());
            Append(hash, plan.Stage);
            Append(hash, plan.ActiveActionId ?? string.Empty);
        }

        foreach (var resourceLock in _planResourceLocks.Values)
        {
            Append(hash, resourceLock.ResourceId);
            Append(hash, resourceLock.PlanId);
            Append(hash, resourceLock.AcquiredMinute.ToString(CultureInfo.InvariantCulture));
            Append(hash, resourceLock.AcquiredEventId);
        }

        foreach (var placeAccess in _placeAccessStates.Values)
        {
            Append(hash, placeAccess.PlaceId);
            Append(hash, placeAccess.Open.ToString(CultureInfo.InvariantCulture));
            Append(hash, placeAccess.QueueCount.ToString(CultureInfo.InvariantCulture));
            Append(hash, placeAccess.SecurityPosture);
            Append(hash, placeAccess.ControllerId ?? string.Empty);
            Append(hash, placeAccess.LastAdmissionMinute?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            Append(hash, placeAccess.LastAdmittedActorId ?? string.Empty);
        }

        return $"sha256:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
    }

    private void ValidateCognitionReferences()
    {
        foreach (var proposition in _propositions.Values)
        {
            if (proposition.RetellingVariantId is not { } variantId)
            {
                continue;
            }

            if (!_propositions.TryGetValue(variantId, out var variant))
            {
                throw new ArgumentException(
                    $"Proposition '{proposition.Id}' references unknown retelling variant '{variantId}'.",
                    nameof(_propositions));
            }

            if (!string.Equals(proposition.TopicId, variant.TopicId, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Retelling variant '{variantId}' must share topic '{proposition.TopicId}'.",
                    nameof(_propositions));
            }
        }

        foreach (var belief in _beliefs.Values)
        {
            if (!_propositions.ContainsKey(belief.PropositionId))
            {
                throw new ArgumentException(
                    $"Belief '{belief.Id}' references unknown proposition '{belief.PropositionId}'.",
                    nameof(_beliefs));
            }
        }

        foreach (var message in _messages.Values)
        {
            if (!_propositions.ContainsKey(message.PropositionId) ||
                !_propositions.ContainsKey(message.SourcePropositionId))
            {
                throw new ArgumentException(
                    $"Message '{message.Id}' references an unknown proposition.",
                    nameof(_messages));
            }
        }
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount <= StackFingerprintBufferSize)
        {
            Span<byte> buffer = stackalloc byte[StackFingerprintBufferSize];
            var written = Encoding.UTF8.GetBytes(value, buffer);
            hash.AppendData(buffer[..written]);
        }
        else
        {
            var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                var written = Encoding.UTF8.GetBytes(value, buffer);
                hash.AppendData(buffer.AsSpan(0, written));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        hash.AppendData(FingerprintSeparator);
    }

    private static Dictionary<string, SortedSet<string>> BuildAttentionSpaceIndex(IEnumerable<ActorState> actors)
    {
        var index = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var actor in actors)
        {
            AddToIndex(index, GetAttentionKey(actor), actor.Id);
        }

        return index;
    }

    private static Dictionary<string, SortedSet<string>> BuildAdjacencyIndex(
        IEnumerable<string> placeIds,
        IEnumerable<RouteDefinition> routes)
    {
        var index = placeIds.ToDictionary(
            placeId => placeId,
            _ => new SortedSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (var route in routes)
        {
            index[route.FromPlaceId].Add(route.ToPlaceId);
            index[route.ToPlaceId].Add(route.FromPlaceId);
        }

        return index;
    }

    private static Dictionary<string, RouteTraversal[]> BuildRouteTraversalIndex(
        IEnumerable<string> placeIds,
        IEnumerable<RouteDefinition> routes)
    {
        var builders = placeIds.ToDictionary(
            placeId => placeId,
            _ => new List<RouteTraversal>(),
            StringComparer.Ordinal);
        foreach (var route in routes.OrderBy(route => route.Id, StringComparer.Ordinal))
        {
            builders[route.FromPlaceId].Add(new RouteTraversal(route.ToPlaceId, route));
            if (route.Bidirectional)
            {
                builders[route.ToPlaceId].Add(new RouteTraversal(route.FromPlaceId, route));
            }
        }

        return builders.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ToArray(),
            StringComparer.Ordinal);
    }

    private static Dictionary<string, List<ActionInstanceState>> BuildActorActionIndex(
        IEnumerable<ActionInstanceState> actions)
    {
        var index = new Dictionary<string, List<ActionInstanceState>>(StringComparer.Ordinal);
        foreach (var action in actions.OrderBy(action => action.Sequence))
        {
            AddActionToIndex(index, action);
        }

        return index;
    }

    private static void AddActionToIndex(
        Dictionary<string, List<ActionInstanceState>> index,
        ActionInstanceState action)
    {
        if (!index.TryGetValue(action.ActorId, out var actorActions))
        {
            actorActions = [];
            index.Add(action.ActorId, actorActions);
        }

        actorActions.Add(action);
    }

    private void AddActorToAttentionIndex(ActorState actor) =>
        AddToIndex(_actorIdsByAttentionSpace, GetAttentionKey(actor), actor.Id);

    private void RemoveActorFromAttentionIndex(ActorState actor)
    {
        var key = GetAttentionKey(actor);
        if (!_actorIdsByAttentionSpace.TryGetValue(key, out var actorIds) || !actorIds.Remove(actor.Id))
        {
            throw new InvalidOperationException($"Actor '{actor.Id}' is missing from attention index '{key}'.");
        }

        if (actorIds.Count == 0)
        {
            _actorIdsByAttentionSpace.Remove(key);
        }
    }

    private IReadOnlyCollection<string> GetActorIdsInAttentionSpace(string key) =>
        _actorIdsByAttentionSpace.TryGetValue(key, out var actorIds) ? actorIds.ToArray() : [];

    private static void AddToIndex(Dictionary<string, SortedSet<string>> index, string key, string actorId)
    {
        if (!index.TryGetValue(key, out var actorIds))
        {
            actorIds = new SortedSet<string>(StringComparer.Ordinal);
            index.Add(key, actorIds);
        }

        actorIds.Add(actorId);
    }

    private static string GetAttentionKey(ActorState actor) => actor.PlaceId is { } placeId
        ? PlaceAttentionKey(placeId)
        : RouteAttentionKey(actor.Transit!.RouteId);

    private static string PlaceAttentionKey(string placeId) => $"place:{placeId}";

    private static string RouteAttentionKey(string routeId) => $"route:{routeId}";
}

internal sealed record RouteTraversal(string NextPlaceId, RouteDefinition Route);

public sealed class DomainCommandException : Exception
{
    public DomainCommandException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
