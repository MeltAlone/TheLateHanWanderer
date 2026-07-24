using LateHan.Core;
using LateHan.Persistence;

namespace LateHan.Tests;

public sealed class ConflictingBeliefTests
{
    [Fact]
    public void RetellingCanDistortAClaimAndReduceConfidence()
    {
        var engine = CreateEngine();

        var result = engine.Tell("person.source", "person.recipient", "proposition.gate_open.report");

        var message = Assert.Single(engine.State.Messages.Values);
        Assert.Equal("proposition.gate_open.report", message.SourcePropositionId);
        Assert.Equal("proposition.gate_closed.rumor", message.PropositionId);
        Assert.Equal(8500, message.ConfidenceBp);
        Assert.True(message.WasDistorted);
        Assert.NotNull(message.DistortionDrawBp);
        Assert.Equal(MessagePropagationPolicy.Version, message.PropagationRuleVersion);
        var belief = Assert.Single(
            engine.State.Beliefs.Values,
            item => item.HolderId == "person.recipient");
        Assert.Equal("proposition.gate_closed.rumor", belief.PropositionId);
        Assert.Equal(8500, belief.ConfidenceBp);
        Assert.Contains(result.Events, item =>
            item.Type == "message_delivered" &&
            item.Details["source_proposition_id"] == "proposition.gate_open.report" &&
            item.Details["proposition_id"] == "proposition.gate_closed.rumor" &&
            item.Details["distorted"] == bool.TrueString);
    }

    [Fact]
    public void CompetingSourcesCreateAQueryableConflictWithCause()
    {
        var engine = CreateEngine();
        _ = engine.Tell("person.source", "person.recipient", "proposition.gate_open.report");

        var result = engine.Tell(
            "person.counter_source",
            "person.recipient",
            "proposition.gate_open.confirmed");

        var conflict = Assert.Single(engine.GetBeliefConflicts("person.recipient"));
        Assert.Equal("topic.gate_state", conflict.TopicId);
        Assert.Equal(
            ["proposition.gate_closed.rumor", "proposition.gate_open.confirmed"],
            conflict.Beliefs.Select(item => item.PropositionId).Order(StringComparer.Ordinal));
        var detected = Assert.Single(result.Events, item => item.Type == "belief_conflict_detected");
        Assert.Equal("topic.gate_state", detected.Details["topic_id"]);
        Assert.Single(detected.CauseIds);

        var repeated = engine.Tell(
            "person.counter_source",
            "person.recipient",
            "proposition.gate_open.confirmed");
        Assert.DoesNotContain(repeated.Events, item => item.Type == "belief_conflict_detected");
    }

    [Fact]
    public void SnapshotPreservesPropagationRulesMessagesAndConflicts()
    {
        var engine = CreateEngine();
        _ = engine.Tell("person.source", "person.recipient", "proposition.gate_open.report");
        _ = engine.Tell(
            "person.counter_source",
            "person.recipient",
            "proposition.gate_open.confirmed");
        var store = new WorldSnapshotStore();
        var path = Path.Combine(Path.GetTempPath(), $"latehan-conflict-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(engine.State, path);
            var restoredState = store.Load(path);
            var restored = new WorldEngine(restoredState);

            Assert.Equal(engine.State.ComputeEventFingerprint(), restoredState.ComputeEventFingerprint());
            Assert.Equal(engine.State.ComputeMaterialStateFingerprint(), restoredState.ComputeMaterialStateFingerprint());
            Assert.Equal(engine.State.Propositions.Keys, restoredState.Propositions.Keys);
            Assert.Equal(
                engine.State.Messages.Values.Select(item => (
                    item.SourcePropositionId,
                    item.PropositionId,
                    item.PropagationRuleVersion,
                    item.DistortionDrawBp)),
                restoredState.Messages.Values.Select(item => (
                    item.SourcePropositionId,
                    item.PropositionId,
                    item.PropagationRuleVersion,
                    item.DistortionDrawBp)));
            Assert.Single(restored.GetBeliefConflicts("person.recipient"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists($"{path}.tmp")) File.Delete($"{path}.tmp");
        }
    }

    private static WorldEngine CreateEngine()
    {
        ActorState[] actors =
        [
            new("person.source", "Source", "place.test"),
            new("person.counter_source", "Counter Source", "place.test"),
            new("person.recipient", "Recipient", "place.test"),
        ];
        PropositionDefinition[] propositions =
        [
            new(
                "proposition.gate_open.report",
                "topic.gate_state",
                "open",
                retellingVariantId: "proposition.gate_closed.rumor",
                distortionChanceBp: 10000,
                retellingConfidenceLossBp: 500),
            new("proposition.gate_closed.rumor", "topic.gate_state", "closed"),
            new("proposition.gate_open.confirmed", "topic.gate_state", "open"),
        ];
        BeliefState[] beliefs =
        [
            new(
                "belief.source.gate",
                "person.source",
                "proposition.gate_open.report",
                9000,
                "direct_observation",
                0),
            new(
                "belief.counter_source.gate",
                "person.counter_source",
                "proposition.gate_open.confirmed",
                9200,
                "direct_observation",
                0),
        ];
        var state = new WorldState(
            "scenario.test.conflicting-beliefs",
            "1.0.0",
            "test.v1",
            RandomMetadata.Xoshiro256StarStarV1,
            EngineMetadata.Version,
            "sha256:test",
            "person.source",
            0,
            actors,
            [new PlaceDefinition("place.test", "Test Place", "access.public", null)],
            [],
            [],
            [],
            beliefs: beliefs,
            propositions: propositions);
        return new WorldEngine(state);
    }
}
