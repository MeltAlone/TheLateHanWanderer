using LateHan.Core;

internal static class SyntheticScaleWorldFactory
{
    public const int PlaceCount = 50;
    public const int CityCrisisL0Count = 50;
    public const int CityCrisisL1Count = 500;
    public const int CityCrisisGroupPopulation = 2_000_000;
    public const int MessageCarriersPerPlace = 4;
    public const string PlayerActorId = "person.synthetic.player";
    public const string PublicAccessRuleId = "access.synthetic.public";
    public const string OfficialPropositionId = "proposition.synthetic.official_notice";
    public const string CityCrisisDestinationPlaceId = "place.synthetic.city.25";

    public static WorldState CreateCityCrisisWorld()
    {
        var actors = new List<ActorState>
        {
            new(
                PlayerActorId,
                "Synthetic Player",
                PlaceId(0),
                transit: null,
                detailLevel: SimulationDetailLevel.L0),
        };

        for (var index = 1; index < CityCrisisL0Count; index++)
        {
            actors.Add(new ActorState(
                $"person.synthetic.l0.{index:D3}",
                $"Synthetic L0 {index:D3}",
                PlaceId(0),
                transit: null,
                detailLevel: SimulationDetailLevel.L0));
        }

        for (var index = 0; index < CityCrisisL1Count; index++)
        {
            actors.Add(new ActorState(
                $"person.synthetic.l1.{index:D3}",
                $"Synthetic L1 {index:D3}",
                PlaceId(index % PlaceCount),
                transit: null,
                detailLevel: SimulationDetailLevel.L1));
        }

        GroupState[] groups =
        [
            new(
                "group.synthetic.regional_population",
                "Synthetic Regional Population",
                "regional_population",
                CityCrisisGroupPopulation,
                PlaceId(49),
                organizationId: null,
                "synthetic-resident"),
        ];

        return CreateWorld("synthetic-b2-city-crisis", actors, groups: groups);
    }

    public static WorldState CreateMessageTopologyWorld()
    {
        var actors = new List<ActorState>
        {
            new(
                PlayerActorId,
                "Synthetic Player",
                PlaceId(0),
                transit: null,
                detailLevel: SimulationDetailLevel.L0),
        };

        for (var placeIndex = 0; placeIndex < PlaceCount; placeIndex++)
        {
            for (var carrierIndex = 0; carrierIndex < MessageCarriersPerPlace; carrierIndex++)
            {
                actors.Add(new ActorState(
                    CarrierId(placeIndex, carrierIndex),
                    $"Synthetic Carrier {placeIndex:D2}-{carrierIndex}",
                    PlaceId(placeIndex),
                    transit: null,
                    detailLevel: SimulationDetailLevel.L1));
            }
        }

        BeliefState[] beliefs =
        [
            new(
                "belief.synthetic.official_notice",
                PlayerActorId,
                OfficialPropositionId,
                confidenceBp: 10000,
                "official_source",
                acquiredAtMinute: 0,
                sourceEventId: "evidence.synthetic.official_notice"),
        ];

        return CreateWorld("synthetic-b3-message-topology", actors, beliefs: beliefs);
    }

    public static string PlaceId(int placeIndex) => $"place.synthetic.city.{placeIndex:D2}";

    public static string CarrierId(int placeIndex, int carrierIndex) =>
        $"person.synthetic.carrier.{placeIndex:D2}.{carrierIndex}";

    private static WorldState CreateWorld(
        string scenarioId,
        IEnumerable<ActorState> actors,
        IEnumerable<BeliefState>? beliefs = null,
        IEnumerable<GroupState>? groups = null)
    {
        var places = Enumerable.Range(0, PlaceCount)
            .Select(index => new PlaceDefinition(
                PlaceId(index),
                $"Synthetic City Place {index:D2}",
                PublicAccessRuleId,
                ControllerId: null))
            .ToArray();
        var routes = Enumerable.Range(0, PlaceCount)
            .Select(index => new RouteDefinition(
                $"route.synthetic.ring.{index:D2}",
                PlaceId(index),
                PlaceId((index + 1) % PlaceCount),
                distanceLiQ10: 10,
                bidirectional: true,
                new Dictionary<TravelMode, int>
                {
                    [TravelMode.Walk] = 5,
                    [TravelMode.Horse] = 3,
                    [TravelMode.WithGroup] = 8,
                }))
            .ToArray();
        AccessRuleDefinition[] accessRules =
        [
            new(PublicAccessRuleId, "Synthetic Public Access", [], mayQueue: false),
        ];

        return new WorldState(
            scenarioId,
            scenarioVersion: "1.0.0",
            rulesetVersion: "synthetic-scale.v1",
            rngVersion: RandomMetadata.Xoshiro256StarStarV1,
            EngineMetadata.Version,
            contentHash: "sha256:6e279896fc823b806ab19a54d22fb689869a790a70d0bb899b2332a5f396ffca",
            PlayerActorId,
            currentMinute: 0,
            actors,
            places,
            routes,
            items: [],
            commitments: [],
            beliefs: beliefs,
            accessRules: accessRules,
            groups: groups);
    }
}
