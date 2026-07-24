using LateHan.Core;
using LateHan.Persistence;

namespace LateHan.Tests;

public sealed class RemoteSimulationTests
{
    [Fact]
    public void RemoteTickSettlesConservedFoodLedgerAndUpdatesManagedL2Actor()
    {
        var engine = CreateEngine(
            organizationId: "organization.remote",
            foodStockUnits: 100,
            dailyProductionPerThousand: 1000,
            dailyConsumptionPerThousand: 2000);
        var initialized = engine.InitializeRemoteSimulation();
        var duplicateInitialization = engine.InitializeRemoteSimulation();
        var populationBefore = engine.State.Actors.Count + engine.State.Groups["group.remote"].Count;

        _ = engine.Wait(1440);

        var group = engine.State.Groups["group.remote"];
        var actor = engine.State.Actors["person.remote"];
        var settlement = Assert.Single(engine.State.Events, item => item.Type == "remote_world_tick");
        var actorBatch = Assert.Single(engine.State.Events, item => item.Type == "remote_named_actor_batch_updated");
        Assert.Single(initialized);
        Assert.Empty(duplicateInitialization);
        var nextTick = Assert.Single(engine.State.ScheduledEvents);
        Assert.Equal(2880, nextTick.DueMinute);
        Assert.Contains(settlement.Id, nextTick.CauseIds);
        Assert.Equal(0, group.FoodStockUnits);
        Assert.Equal(1000, group.CumulativeFoodProduced);
        Assert.Equal(2000, group.CumulativeFoodDemand);
        Assert.Equal(1100, group.CumulativeFoodConsumed);
        Assert.Equal(900, group.CumulativeFoodUnmet);
        Assert.Equal(4500, group.FoodShortageBp);
        Assert.Equal(1440, group.LastRemoteSettlementMinute);
        Assert.Equal(1, actor.RemoteCycleCount);
        Assert.Equal(1440, actor.LastRemoteUpdateMinute);
        Assert.Equal(actorBatch.Id, actor.LastRemoteUpdateEventId);
        Assert.Equal(RemoteSimulationPolicy.Version, settlement.Details["policy_version"]);
        Assert.Equal("conserved", settlement.Details["material_balance"]);
        Assert.Equal("900", settlement.Details["outstanding_unmet_food_units"]);
        Assert.Equal(populationBefore, engine.State.Actors.Count + group.Count);
    }

    [Fact]
    public void RemoteFoodSettlementIsInvariantToBatchPartitioning()
    {
        var daily = CreateEngine(
            organizationId: null,
            foodStockUnits: 5000,
            dailyProductionPerThousand: 333,
            dailyConsumptionPerThousand: 777,
            groupCount: 1234);
        var partitioned = CreateEngine(
            organizationId: null,
            foodStockUnits: 5000,
            dailyProductionPerThousand: 333,
            dailyConsumptionPerThousand: 777,
            groupCount: 1234);
        daily.Schedule(1440, ScheduledEventPhase.SummaryAndNotification, "group.remote", "remote_world_tick");
        foreach (var minute in new long[] { 480, 960, 1440 })
        {
            partitioned.Schedule(minute, ScheduledEventPhase.SummaryAndNotification, "group.remote", "remote_world_tick");
        }

        _ = daily.Wait(1440);
        _ = partitioned.Wait(1440);

        var dailyGroup = daily.State.Groups["group.remote"];
        var partitionedGroup = partitioned.State.Groups["group.remote"];
        Assert.Equal(dailyGroup.FoodStockUnits, partitionedGroup.FoodStockUnits);
        Assert.Equal(dailyGroup.FoodProductionRemainder, partitionedGroup.FoodProductionRemainder);
        Assert.Equal(dailyGroup.FoodDemandRemainder, partitionedGroup.FoodDemandRemainder);
        Assert.Equal(dailyGroup.CumulativeFoodProduced, partitionedGroup.CumulativeFoodProduced);
        Assert.Equal(dailyGroup.CumulativeFoodDemand, partitionedGroup.CumulativeFoodDemand);
        Assert.Equal(dailyGroup.CumulativeFoodConsumed, partitionedGroup.CumulativeFoodConsumed);
        Assert.Equal(dailyGroup.CumulativeFoodUnmet, partitionedGroup.CumulativeFoodUnmet);
        Assert.Equal(dailyGroup.FoodShortageBp, partitionedGroup.FoodShortageBp);
        Assert.Equal(daily.State.ComputeMaterialStateFingerprint(), partitioned.State.ComputeMaterialStateFingerprint());
    }

    [Fact]
    public void SnapshotPreservesRemoteLedgerAndDeterministicContinuation()
    {
        var continuous = CreateEngine(
            organizationId: "organization.remote",
            foodStockUnits: 2000,
            dailyProductionPerThousand: 850,
            dailyConsumptionPerThousand: 1000);
        continuous.Schedule(720, ScheduledEventPhase.SummaryAndNotification, "group.remote", "remote_world_tick");
        continuous.Schedule(1440, ScheduledEventPhase.SummaryAndNotification, "group.remote", "remote_world_tick");
        _ = continuous.Wait(720);
        var store = new WorldSnapshotStore();
        var path = Path.Combine(Path.GetTempPath(), $"latehan-remote-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(continuous.State, path);
            var restored = new WorldEngine(store.Load(path));

            _ = continuous.Wait(720);
            _ = restored.Wait(720);

            Assert.Equal(continuous.State.ComputeMaterialStateFingerprint(), restored.State.ComputeMaterialStateFingerprint());
            Assert.Equal(continuous.State.ComputeEventFingerprint(), restored.State.ComputeEventFingerprint());
            Assert.Equal(
                continuous.State.Actors["person.remote"].LastRemoteUpdateEventId,
                restored.State.Actors["person.remote"].LastRemoteUpdateEventId);
            Assert.Equal(2, restored.State.Actors["person.remote"].RemoteCycleCount);
            Assert.Equal(1440, restored.State.Groups["group.remote"].LastRemoteSettlementMinute);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists($"{path}.tmp")) File.Delete($"{path}.tmp");
        }
    }

    [Fact]
    public void RemoteCyclePreventsTemporaryActorFromLosingIndependentHistoryOnDemotion()
    {
        var engine = CreateEngine(
            organizationId: "organization.remote",
            foodStockUnits: 1000,
            dailyProductionPerThousand: 1000,
            dailyConsumptionPerThousand: 1000);
        var group = engine.State.Groups["group.remote"];
        var promoted = engine.PromoteGroupMember(group.Id, detailLevel: SimulationDetailLevel.L2);
        _ = engine.InitializeRemoteSimulation();

        _ = engine.Wait(1440);
        var exception = Assert.Throws<DomainCommandException>(() =>
            engine.DemotePromotedActor(promoted.Actor.Id));

        Assert.Equal(1, promoted.Actor.RemoteCycleCount);
        Assert.Equal("actor_has_independent_state", exception.Code);
        Assert.Contains(promoted.Actor.Id, engine.State.Actors.Keys);
    }

    [Fact]
    public void NonzeroWorldStartDoesNotSettleTimeBeforeTheScenario()
    {
        var engine = CreateEngine(
            organizationId: null,
            foodStockUnits: 1000,
            dailyProductionPerThousand: 1000,
            dailyConsumptionPerThousand: 1000,
            currentMinute: 10_000);
        _ = engine.InitializeRemoteSimulation();

        _ = engine.Wait(1440);

        var group = engine.State.Groups["group.remote"];
        var settlement = Assert.Single(engine.State.Events, item => item.Type == "remote_world_tick");
        Assert.Equal("1440", settlement.Details["elapsed_minutes"]);
        Assert.Equal(1000, group.CumulativeFoodProduced);
        Assert.Equal(1000, group.CumulativeFoodDemand);
        Assert.Equal(11_440, group.LastRemoteSettlementMinute);
    }

    [Fact]
    public void PopulationChangeSettlesThePriorIntervalBeforeChangingGroupCount()
    {
        var engine = CreateEngine(
            organizationId: null,
            foodStockUnits: 1000,
            dailyProductionPerThousand: 1000,
            dailyConsumptionPerThousand: 1000);
        _ = engine.InitializeRemoteSimulation();
        var populationBefore = engine.State.Actors.Count + engine.State.Groups["group.remote"].Count;
        _ = engine.Wait(720);

        var promoted = engine.PromoteGroupMember("group.remote");
        _ = engine.Wait(720);

        var group = engine.State.Groups["group.remote"];
        var boundarySettlement = Assert.Single(
            engine.State.Events,
            item => item.Type == "remote_world_settled_before_population_change");
        Assert.Contains(boundarySettlement.Id, promoted.Event.CauseIds);
        Assert.Equal(999, group.CumulativeFoodProduced);
        Assert.Equal(999, group.CumulativeFoodDemand);
        Assert.Equal(720_000, group.FoodProductionRemainder);
        Assert.Equal(720_000, group.FoodDemandRemainder);
        Assert.Equal(populationBefore, engine.State.Actors.Count + group.Count);
    }

    private static WorldEngine CreateEngine(
        string? organizationId,
        long foodStockUnits,
        int dailyProductionPerThousand,
        int dailyConsumptionPerThousand,
        int groupCount = 1000,
        long currentMinute = 0)
    {
        var remoteMemberships = organizationId is null
            ? Array.Empty<ActorMembership>()
            : [new ActorMembership(organizationId, "remote_official")];
        ActorState[] actors =
        [
            new("person.player", "Player", "place.local", null, detailLevel: SimulationDetailLevel.L0),
            new("person.remote", "Remote Actor", "place.remote", null, remoteMemberships, SimulationDetailLevel.L2),
        ];
        PlaceDefinition[] places =
        [
            new("place.local", "Local", "access.public", null),
            new("place.remote", "Remote", "access.public", null),
        ];
        GroupState[] groups =
        [
            new(
                "group.remote",
                "Remote Population",
                "regional_population",
                groupCount,
                "place.remote",
                organizationId,
                "remote-resident",
                foodStockUnits,
                dailyProductionPerThousand,
                dailyConsumptionPerThousand,
                lastRemoteSettlementMinute: currentMinute),
        ];
        var world = new WorldState(
            "scenario.remote-test",
            "1.0.0",
            "remote-test.v1",
            RandomMetadata.Xoshiro256StarStarV1,
            EngineMetadata.Version,
            "sha256:remote-test",
            "person.player",
            currentMinute,
            actors,
            places,
            routes: [],
            items: [],
            commitments: [],
            accessRules: [new AccessRuleDefinition("access.public", "Public", [], false)],
            groups: groups);
        return new WorldEngine(world);
    }
}
