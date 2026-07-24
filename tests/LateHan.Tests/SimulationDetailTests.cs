using LateHan.Core;
using LateHan.Persistence;

namespace LateHan.Tests;

public sealed class SimulationDetailTests
{
    [Fact]
    public void PlayerTravelMarksOnlyAffectedAttentionNeighborhoodDirty()
    {
        var engine = RepositoryFixture.CreateEngine();
        _ = engine.RebalanceActorDetailLevels();

        _ = engine.BeginTravel(
            "person.player_clerk",
            "place.luoyang.west_market",
            TravelMode.Walk);

        Assert.Contains("person.player_clerk", engine.State.DetailDirtyActorIds);
        Assert.Contains("person.he_jin", engine.State.DetailDirtyActorIds);
        Assert.Contains("person.cao_cao", engine.State.DetailDirtyActorIds);
        Assert.Contains("person.yuan_shao", engine.State.DetailDirtyActorIds);
        Assert.DoesNotContain("person.chen_zhi", engine.State.DetailDirtyActorIds);
        Assert.DoesNotContain("person.sun_he", engine.State.DetailDirtyActorIds);
        Assert.True(engine.State.DetailDirtyActorIds.Count < engine.State.Actors.Count);
    }

    [Fact]
    public void DirtyRebalanceProcessesOnlyInvalidatedActors()
    {
        var engine = RepositoryFixture.CreateEngine();
        _ = engine.RebalanceActorDetailLevels();
        _ = engine.BeginTravel(
            "person.player_clerk",
            "place.luoyang.west_market",
            TravelMode.Walk);
        var dirtyIds = engine.State.DetailDirtyActorIds.ToArray();

        var result = engine.RebalanceDirtyActorDetailLevels();

        Assert.Equal(dirtyIds, result.Assessments.Select(item => item.ActorId));
        Assert.Empty(engine.State.DetailDirtyActorIds);
        Assert.Equal(SimulationDetailLevel.L2, engine.State.Actors["person.chen_zhi"].DetailLevel);
    }

    [Fact]
    public void SnapshotPreservesDetailDirtyActors()
    {
        var engine = RepositoryFixture.CreateEngine();
        _ = engine.RebalanceActorDetailLevels();
        _ = engine.BeginTravel("person.sun_he", "place.luoyang.west_market", TravelMode.Walk);
        var store = new WorldSnapshotStore();
        var path = Path.Combine(Path.GetTempPath(), $"latehan-detail-dirty-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(engine.State, path);
            var restored = store.Load(path);

            Assert.Equal(engine.State.DetailDirtyActorIds, restored.DetailDirtyActorIds);
            Assert.Contains("person.sun_he", restored.DetailDirtyActorIds);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists($"{path}.tmp")) File.Delete($"{path}.tmp");
        }
    }

    [Fact]
    public void MessageRetentionExpiryInvalidatesParticipants()
    {
        var engine = RepositoryFixture.CreateEngine();
        _ = engine.RebalanceActorDetailLevels();
        _ = engine.Tell(
            "person.player_clerk",
            "person.li_wen",
            "proposition.palace_credential_required");
        _ = engine.RebalanceDirtyActorDetailLevels();

        _ = engine.Wait(SimulationDetailPolicy.RecentMessageRetentionMinutes + 1);

        Assert.Contains("person.player_clerk", engine.State.DetailDirtyActorIds);
        Assert.Contains("person.li_wen", engine.State.DetailDirtyActorIds);
        Assert.Contains(engine.State.Events, item => item.Type == "detail_message_retention_expired");
    }

    [Fact]
    public void RebalanceUsesPlayerAttentionAndCausalDebt()
    {
        var engine = RepositoryFixture.CreateEngine();

        var result = engine.RebalanceActorDetailLevels();

        Assert.Equal(SimulationDetailLevel.L0, engine.State.Actors["person.player_clerk"].DetailLevel);
        Assert.Equal(SimulationDetailLevel.L0, engine.State.Actors["person.he_jin"].DetailLevel);
        Assert.Equal(SimulationDetailLevel.L1, engine.State.Actors["person.yuan_shao"].DetailLevel);
        Assert.Equal(SimulationDetailLevel.L2, engine.State.Actors["person.chen_zhi"].DetailLevel);
        Assert.Equal(SimulationDetailLevel.L2, engine.State.Actors["person.sun_he"].DetailLevel);
        Assert.Contains(result.Events, item =>
            item.SubjectIds.Contains("person.chen_zhi", StringComparer.Ordinal) &&
            item.Details["reasons"] == "background_named_actor" &&
            item.Details["policy_version"] == SimulationDetailPolicy.Version);
    }

    [Fact]
    public void ActiveTravelPromotesBackgroundActorToL1()
    {
        var engine = RepositoryFixture.CreateEngine();
        _ = engine.RebalanceActorDetailLevels();
        Assert.Equal(SimulationDetailLevel.L2, engine.State.Actors["person.sun_he"].DetailLevel);
        _ = engine.BeginTravel("person.sun_he", "place.luoyang.west_market", TravelMode.Walk);

        var result = engine.RebalanceActorDetailLevels(["person.sun_he"]);

        Assert.Equal(SimulationDetailLevel.L1, engine.State.Actors["person.sun_he"].DetailLevel);
        Assert.Single(result.Events);
        Assert.Contains("active_action", result.Events[0].Details["reasons"], StringComparison.Ordinal);
    }

    [Fact]
    public void RecentMessageRetainsPromotedActorAtL1AwayFromPlayer()
    {
        var engine = RepositoryFixture.CreateEngine();
        var promoted = engine.PromoteGroupMember("group.general_office_clerks");
        _ = engine.Tell(
            "person.player_clerk",
            promoted.Actor.Id,
            "proposition.palace_credential_required");
        _ = engine.Move("person.player_clerk", "place.luoyang.north_gate", TravelMode.Walk);

        var assessment = engine.AssessActorDetailLevel(promoted.Actor.Id);
        var result = engine.RebalanceActorDetailLevels([promoted.Actor.Id]);

        Assert.Equal(SimulationDetailLevel.L1, assessment.RecommendedLevel);
        Assert.Contains("recent_message", assessment.Reasons);
        Assert.Equal(SimulationDetailLevel.L1, promoted.Actor.DetailLevel);
        Assert.Single(result.Events);
    }

    [Fact]
    public void DetailRebalanceIsDeterministicAcrossIdenticalWorlds()
    {
        var first = RepositoryFixture.CreateEngine();
        var second = RepositoryFixture.CreateEngine();

        var firstResult = first.RebalanceActorDetailLevels();
        var secondResult = second.RebalanceActorDetailLevels();

        Assert.Equal(
            firstResult.Assessments.Select(item => (item.ActorId, item.RecommendedLevel, string.Join(',', item.Reasons))),
            secondResult.Assessments.Select(item => (item.ActorId, item.RecommendedLevel, string.Join(',', item.Reasons))));
        Assert.Equal(first.State.ComputeEventFingerprint(), second.State.ComputeEventFingerprint());
    }

    [Fact]
    public void PromotionConservesPopulationAndCopiesGroupBelief()
    {
        var engine = RepositoryFixture.CreateEngine();
        var group = engine.State.Groups["group.market_population"];
        var populationBefore = group.Count + engine.State.Actors.Count;

        var result = engine.PromoteGroupMember(group.Id);

        Assert.Equal(239, group.Count);
        Assert.Equal(populationBefore, group.Count + engine.State.Actors.Count);
        Assert.Equal(SimulationDetailLevel.L0, result.Actor.DetailLevel);
        Assert.Equal(group.Id, result.Actor.PromotedFromGroupId);
        Assert.Equal("place.luoyang.west_market", result.Actor.LocationId);
        Assert.NotNull(result.Actor.IdentitySeedHex);
        Assert.Contains(engine.State.Beliefs.Values, item =>
            item.HolderId == result.Actor.Id &&
            item.PropositionId == "proposition.north_gate_closed" &&
            item.SourceEventId == result.Event.Id);
    }

    [Fact]
    public void PromotionIsDeterministicAcrossIdenticalWorlds()
    {
        var first = RepositoryFixture.CreateEngine();
        var second = RepositoryFixture.CreateEngine();

        var firstResult = first.PromoteGroupMember("group.market_population");
        var secondResult = second.PromoteGroupMember("group.market_population");

        Assert.Equal(firstResult.Actor.Id, secondResult.Actor.Id);
        Assert.Equal(firstResult.Actor.Name, secondResult.Actor.Name);
        Assert.Equal(firstResult.Actor.IdentitySeedHex, secondResult.Actor.IdentitySeedHex);
        Assert.Equal(first.State.ComputeEventFingerprint(), second.State.ComputeEventFingerprint());
    }

    [Fact]
    public void CleanTemporaryPromotionCanRoundTripBackIntoGroup()
    {
        var engine = RepositoryFixture.CreateEngine();
        var group = engine.State.Groups["group.north_gate_guards"];
        var initialCount = group.Count;
        var initialFingerprint = engine.State.ComputeMaterialStateFingerprint();
        var promoted = engine.PromoteGroupMember(group.Id);

        var demoted = engine.DemotePromotedActor(promoted.Actor.Id);

        Assert.Equal(initialCount, group.Count);
        Assert.DoesNotContain(promoted.Actor.Id, engine.State.Actors.Keys);
        Assert.DoesNotContain(engine.State.Beliefs.Values, item => item.HolderId == promoted.Actor.Id);
        Assert.Equal("promoted_actor_demoted", demoted.Type);
        Assert.Equal(initialFingerprint, engine.State.ComputeMaterialStateFingerprint());
    }

    [Fact]
    public void IndependentMessagePreventsDemotion()
    {
        var engine = RepositoryFixture.CreateEngine();
        var promoted = engine.PromoteGroupMember("group.general_office_clerks");
        _ = engine.Tell(
            "person.player_clerk",
            promoted.Actor.Id,
            "proposition.palace_credential_required");

        var exception = Assert.Throws<DomainCommandException>(() =>
            engine.DemotePromotedActor(promoted.Actor.Id));

        Assert.Equal("actor_has_independent_state", exception.Code);
        Assert.Contains(promoted.Actor.Id, engine.State.Actors.Keys);
    }

    [Fact]
    public void SnapshotPreservesPromotedIdentityAndPopulationCursor()
    {
        var engine = RepositoryFixture.CreateEngine();
        var promoted = engine.PromoteGroupMember("group.market_population", detailLevel: SimulationDetailLevel.L1);
        var store = new WorldSnapshotStore();
        var path = Path.Combine(Path.GetTempPath(), $"latehan-lod-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(engine.State, path);
            var restored = store.Load(path);

            Assert.Equal(engine.State.PromotionSequenceCursor, restored.PromotionSequenceCursor);
            Assert.Equal(engine.State.Groups["group.market_population"].Count,
                restored.Groups["group.market_population"].Count);
            Assert.Equal(promoted.Actor.IdentitySeedHex, restored.Actors[promoted.Actor.Id].IdentitySeedHex);
            Assert.Equal(SimulationDetailLevel.L1, restored.Actors[promoted.Actor.Id].DetailLevel);
            Assert.Equal(engine.State.ComputeEventFingerprint(), restored.ComputeEventFingerprint());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists($"{path}.tmp")) File.Delete($"{path}.tmp");
        }
    }
}
