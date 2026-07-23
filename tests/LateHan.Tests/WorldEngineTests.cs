using LateHan.Core;

namespace LateHan.Tests;

public sealed class WorldEngineTests
{
    [Fact]
    public void InitialQueriesDoNotAdvanceTimeOrRevealRemoteActors()
    {
        var engine = RepositoryFixture.CreateEngine();

        var look = engine.Look();
        var status = engine.Status();

        Assert.Equal(0, engine.State.CurrentMinute);
        Assert.Empty(engine.State.Events);
        Assert.Contains(look.VisibleActors, actor => actor.Id == "person.he_jin");
        Assert.DoesNotContain(look.VisibleActors, actor => actor.Id == "person.zhang_rang");
        Assert.Contains(status.HeldItems, item => item.Id == "item.sealed_note_to_yuan_shao");
        Assert.Equal(2, status.OpenCommitments.Count);
    }

    [Fact]
    public void PlayerCanWalkDeliverTheDocumentAndReportBack()
    {
        var engine = RepositoryFixture.CreateEngine();

        var outbound = engine.Move("person.player_clerk", "place.luoyang.sili_office", TravelMode.Walk);
        var delivery = engine.Deliver(
            "person.player_clerk",
            "item.sealed_note_to_yuan_shao",
            "person.yuan_shao");
        var inbound = engine.Move("person.player_clerk", "place.luoyang.general_in_chief_office", TravelMode.Walk);
        var report = engine.Tell(
            "person.player_clerk",
            "person.li_wen",
            "proposition.general_office_requests_status");

        Assert.Equal(22, outbound.EndMinute - outbound.StartMinute);
        Assert.Equal(5, delivery.EndMinute - delivery.StartMinute);
        Assert.Equal(22, inbound.EndMinute - inbound.StartMinute);
        Assert.Equal(5, report.EndMinute - report.StartMinute);
        Assert.Equal(54, engine.State.CurrentMinute);
        Assert.Equal("person.yuan_shao", engine.State.Items["item.sealed_note_to_yuan_shao"].HolderId);
        Assert.Equal("completed", engine.State.Commitments["commitment.player.deliver_note"].Status);
        Assert.Equal("completed", engine.State.Commitments["commitment.player.report_back"].Status);
        Assert.Equal(
            [
                "travel_started",
                "travel_completed",
                "delivery_started",
                "item_transferred",
                "commitment_completed",
                "travel_started",
                "travel_completed",
                "proposition_told",
                "commitment_completed",
            ],
            engine.State.Events.Select(worldEvent => worldEvent.Type));
    }

    [Fact]
    public void InvalidMoveDoesNotAdvanceTimeOrCreateEvents()
    {
        var engine = RepositoryFixture.CreateEngine();

        var exception = Assert.Throws<DomainCommandException>(
            () => engine.Move("person.player_clerk", "place.missing", TravelMode.Walk));

        Assert.Equal("unknown_place", exception.Code);
        Assert.Equal(0, engine.State.CurrentMinute);
        Assert.Empty(engine.State.Events);
    }

    [Fact]
    public void RepeatedRunsProduceTheSameEventFingerprint()
    {
        var first = RunDeliveryPath();
        var second = RunDeliveryPath();

        Assert.Equal(first.State.CurrentMinute, second.State.CurrentMinute);
        Assert.Equal(first.State.ComputeEventFingerprint(), second.State.ComputeEventFingerprint());
    }

    private static WorldEngine RunDeliveryPath()
    {
        var engine = RepositoryFixture.CreateEngine();
        engine.Move("person.player_clerk", "place.luoyang.sili_office", TravelMode.Walk);
        engine.Deliver("person.player_clerk", "item.sealed_note_to_yuan_shao", "person.yuan_shao");
        engine.Move("person.player_clerk", "place.luoyang.general_in_chief_office", TravelMode.Walk);
        engine.Tell("person.player_clerk", "person.li_wen", "proposition.general_office_requests_status");
        return engine;
    }
}
