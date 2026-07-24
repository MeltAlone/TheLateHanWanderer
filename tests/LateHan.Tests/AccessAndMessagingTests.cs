using LateHan.Core;
using LateHan.Persistence;

namespace LateHan.Tests;

public sealed class AccessAndMessagingTests
{
    [Fact]
    public void NonAdjacentEntryIsBlockedWithoutSideEffects()
    {
        var engine = RepositoryFixture.CreateEngine();

        var exception = Assert.Throws<DomainCommandException>(() => engine.Enter(
            "person.player_clerk",
            "place.luoyang.changle_palace"));

        Assert.Equal("not_adjacent", exception.Code);
        Assert.Equal(0, engine.State.CurrentMinute);
        Assert.Empty(engine.State.Events);
    }

    [Fact]
    public void PalaceInnerEntryWaitsAndRefusesWithoutLeakingOccupants()
    {
        var engine = RepositoryFixture.CreateEngine();
        engine.Schedule(
            0,
            ScheduledEventPhase.ArrivalAndDeparture,
            "person.player_clerk",
            "actor_relocated",
            details: new Dictionary<string, string>
            {
                ["destination_place_id"] = "place.luoyang.north_palace",
            });
        _ = engine.Wait(1);
        var startMinute = engine.State.CurrentMinute;

        var result = engine.Enter("person.player_clerk", "place.luoyang.changle_palace");

        Assert.Equal(ActionStatus.Refused, result.Status);
        Assert.Equal(5, engine.State.CurrentMinute - startMinute);
        Assert.Equal("place.luoyang.north_palace", engine.State.Actors["person.player_clerk"].LocationId);
        var refused = result.Events.Last();
        Assert.Equal("access_refused", refused.Type);
        Assert.Equal("explicit_palace_escort_required", refused.Details["reason"]);
        Assert.DoesNotContain("person.zhang_rang", refused.Details.Values);
        Assert.DoesNotContain("person.empress_dowager_he", refused.Details.Values);
    }

    [Fact]
    public void ValidCredentialAllowsOfficialOfficeEntry()
    {
        var engine = RepositoryFixture.CreateEngine();

        var result = engine.Enter("person.player_clerk", "place.luoyang.sili_office");

        Assert.Equal(ActionStatus.Completed, result.Status);
        Assert.Equal(5, engine.State.CurrentMinute);
        Assert.Equal("place.luoyang.sili_office", engine.State.Actors["person.player_clerk"].LocationId);
        Assert.Equal("place_entered", result.Events.Last().Type);
    }

    [Fact]
    public void TravelCannotBypassPalaceAccessRule()
    {
        var engine = RepositoryFixture.CreateEngine();

        var exception = Assert.Throws<DomainCommandException>(() => engine.BeginTravel(
            "person.player_clerk",
            "place.luoyang.changle_palace",
            TravelMode.Walk));

        Assert.Equal("access_denied", exception.Code);
        Assert.Equal(0, engine.State.CurrentMinute);
        Assert.Empty(engine.State.Actions);
    }

    [Fact]
    public void SameMinuteAccessChangePreventsArrival()
    {
        var engine = RepositoryFixture.CreateEngine();
        var action = engine.BeginTravel(
            "person.player_clerk",
            "place.luoyang.sili_office",
            TravelMode.Walk);
        engine.Schedule(
            22,
            ScheduledEventPhase.AccessAndControlChange,
            "place.luoyang.sili_office",
            "place_access_changed",
            details: new Dictionary<string, string>
            {
                ["place_id"] = "place.luoyang.sili_office",
                ["open"] = "false",
                ["security_posture"] = "closed_by_order",
            });

        var result = engine.AdvanceAction(action.Id);

        Assert.Equal(ActionStatus.Blocked, result.Status);
        Assert.Equal("place.luoyang.general_in_chief_office", engine.State.Actors["person.player_clerk"].LocationId);
        Assert.Contains(result.Events, item => item.Type == "place_access_changed");
        Assert.Contains(result.Events, item => item.Type == "travel_access_blocked");
    }

    [Fact]
    public void FalseGateMessageChangesOnlyRecipientBelief()
    {
        var engine = RepositoryFixture.CreateEngine();
        _ = engine.Move("person.player_clerk", "place.luoyang.east_market", TravelMode.Walk);

        var result = engine.Tell(
            "person.player_clerk",
            "person.sun_he",
            "proposition.north_gate_closed");

        Assert.True(engine.State.PlaceAccessStates["place.luoyang.north_gate"].Open);
        var belief = engine.State.Beliefs.Values.Single(item =>
            item.HolderId == "person.sun_he" && item.PropositionId == "proposition.north_gate_closed");
        Assert.Equal("direct_message", belief.Source);
        Assert.Equal(6000, belief.ConfidenceBp);
        Assert.Contains(result.Events, item => item.Type == "message_created");
        Assert.Contains(result.Events, item => item.Type == "message_delivered");
        Assert.Contains(result.Events, item => item.Type == "belief_updated");
        Assert.DoesNotContain(engine.State.Beliefs.Values, item =>
            item.HolderId == "person.chen_zhi" && item.ConfidenceBp == 6000);
    }

    [Fact]
    public void RetellingPreservesMessageLineage()
    {
        var engine = RepositoryFixture.CreateEngine();
        _ = engine.Move("person.player_clerk", "place.luoyang.east_market", TravelMode.Walk);
        _ = engine.Tell("person.player_clerk", "person.sun_he", "proposition.north_gate_closed");
        _ = engine.Move("person.chen_zhi", "place.luoyang.east_market", TravelMode.Walk);

        _ = engine.Tell("person.sun_he", "person.chen_zhi", "proposition.north_gate_closed");

        Assert.Equal(2, engine.State.Messages.Count);
        var messages = engine.State.Messages.Values.OrderBy(item => item.CreatedAtMinute).ThenBy(item => item.Id).ToArray();
        Assert.Equal(messages[0].Id, messages[1].ParentMessageId);
        Assert.Equal(messages[0].ConfidenceBp, messages[1].ConfidenceBp);
    }

    [Fact]
    public void MessageIsNotDeliveredAfterRecipientLeaves()
    {
        var engine = RepositoryFixture.CreateEngine();
        engine.Schedule(
            3,
            ScheduledEventPhase.ArrivalAndDeparture,
            "person.li_wen",
            "actor_relocated",
            details: new Dictionary<string, string>
            {
                ["destination_place_id"] = "place.luoyang.sili_office",
            });

        var result = engine.Tell(
            "person.player_clerk",
            "person.li_wen",
            "proposition.palace_credential_required");

        Assert.Equal(ActionStatus.Blocked, result.Status);
        Assert.Empty(engine.State.Messages);
        Assert.Contains(result.Events, item => item.Type == "message_created");
        Assert.Contains(result.Events, item => item.Type == "message_delivery_failed");
        Assert.DoesNotContain(engine.State.Beliefs.Values, item =>
            item.HolderId == "person.li_wen" && item.PropositionId == "proposition.palace_credential_required");
    }

    [Fact]
    public void DocumentIsNotTransferredAfterRecipientLeaves()
    {
        var engine = RepositoryFixture.CreateEngine();
        _ = engine.Move("person.player_clerk", "place.luoyang.sili_office", TravelMode.Walk);
        engine.Schedule(
            24,
            ScheduledEventPhase.ArrivalAndDeparture,
            "person.yuan_shao",
            "actor_relocated",
            details: new Dictionary<string, string>
            {
                ["destination_place_id"] = "place.luoyang.henan_office",
            });

        var result = engine.Deliver(
            "person.player_clerk",
            "item.sealed_note_to_yuan_shao",
            "person.yuan_shao");

        Assert.Equal(ActionStatus.Blocked, result.Status);
        Assert.Equal("person.player_clerk", engine.State.Items["item.sealed_note_to_yuan_shao"].HolderId);
        Assert.Equal("open", engine.State.Commitments["commitment.player.deliver_note"].Status);
        Assert.Contains(result.Events, item => item.Type == "delivery_failed");
        Assert.DoesNotContain(result.Events, item => item.Type == "item_transferred");
    }

    [Fact]
    public void SnapshotPreservesAccessAndMessageState()
    {
        var engine = RepositoryFixture.CreateEngine();
        _ = engine.Move("person.player_clerk", "place.luoyang.east_market", TravelMode.Walk);
        _ = engine.Tell("person.player_clerk", "person.sun_he", "proposition.north_gate_closed");
        var store = new WorldSnapshotStore();
        var path = Path.Combine(Path.GetTempPath(), $"latehan-access-{Guid.NewGuid():N}.json");

        try
        {
            store.Save(engine.State, path);
            var restored = store.Load(path);

            Assert.Equal(engine.State.ComputeEventFingerprint(), restored.ComputeEventFingerprint());
            Assert.Equal(engine.State.Messages.Keys, restored.Messages.Keys);
            Assert.Equal(engine.State.AccessRules.Keys, restored.AccessRules.Keys);
            Assert.Equal(engine.State.PlaceAccessStates["place.luoyang.north_gate"].Open,
                restored.PlaceAccessStates["place.luoyang.north_gate"].Open);
            Assert.Contains(restored.Beliefs.Values, item =>
                item.HolderId == "person.sun_he" && item.PropositionId == "proposition.north_gate_closed");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists($"{path}.tmp")) File.Delete($"{path}.tmp");
        }
    }
}
