using LateHan.Core;
using LateHan.Persistence;

namespace LateHan.Tests;

public sealed class SimulationDetailTests
{
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
