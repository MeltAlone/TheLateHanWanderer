using LateHan.Core;

internal static class SyntheticScaleWorldFactory
{
    public const int PlaceCount = 50;
    public const int CityCrisisL0Count = 50;
    public const int CityCrisisL1Count = 500;
    public const int CityCrisisGroupPopulation = 2_000_000;

    public const long CityCrisisInitialFoodStock = 5_000_000;

    public const int CityCrisisDailyFoodProductionPerThousand = 900;

    public const int CityCrisisDailyFoodConsumptionPerThousand = 1000;
    public const int MixedCityCrisisPlanCount = 10;
    public const int MixedCityCrisisVisitorCount = 20;
    public const int MixedCityCrisisMessageCount = 40;
    public const int MessageCarriersPerPlace = 4;
    public const string PlayerActorId = "person.synthetic.player";
    public const string PublicAccessRuleId = "access.synthetic.public";
    public const string QueuedAccessRuleId = "access.synthetic.queued";
    public const string OfficialPropositionId = "proposition.synthetic.official_notice";
    public const string ConflictTopicId = "topic.synthetic.gate_state";
    public const string ConflictReportPropositionId = "proposition.synthetic.gate_open_report";
    public const string ConflictUncertainPropositionId = "proposition.synthetic.gate_open_uncertain";
    public const string ConflictClosedRumorPropositionId = "proposition.synthetic.gate_closed_rumor";
    public const string ConflictConfirmedOpenPropositionId = "proposition.synthetic.gate_open_confirmed";
    public const string CityCrisisDestinationPlaceId = "place.synthetic.city.25";
    public const string MixedCityCrisisPlanDestinationPlaceId = "place.synthetic.city.24";
    public const string MixedCityCrisisVisitPlaceId = "place.synthetic.city.26";
    public const string RegionalPopulationGroupId = "group.synthetic.regional_population";

    public static WorldState CreateCityCrisisWorld()
    {
        return CreateWorld(
            "synthetic-b2-city-crisis",
            CreateCityCrisisActors(),
            groups: CreateCityCrisisGroups());
    }

    public static WorldState CreateMixedCityCrisisWorld()
    {
        var planOwnerIds = Enumerable.Range(0, MixedCityCrisisPlanCount)
            .Select(MixedCityCrisisPlanOwnerId)
            .ToArray();
        var items = planOwnerIds.Select((ownerId, index) => new ItemState(
            MixedCityCrisisPlanItemId(index),
            $"Synthetic Crisis Report {index:D2}",
            "written_report",
            ownerId,
            authorId: PlayerActorId,
            intendedRecipientId: ownerId,
            propositionIds: [OfficialPropositionId]))
            .ToArray();
        var plans = planOwnerIds.Select((ownerId, index) => new PlanState(
            MixedCityCrisisPlanId(index),
            ownerId,
            "inspect_crisis_destination",
            "awaiting_written_report",
            beliefRequirementIds: [],
            nextEvaluationMinute: 180,
            PlanEvaluationRule.WrittenReportInspection,
            triggerItemId: MixedCityCrisisPlanItemId(index),
            triggerPropositionId: OfficialPropositionId,
            destinationPlaceId: MixedCityCrisisPlanDestinationPlaceId,
            confidenceThresholdBp: 0,
            reevaluationIntervalMinutes: 60))
            .ToArray();
        BeliefState[] beliefs =
        [
            new(
                "belief.synthetic.mixed.official_notice",
                PlayerActorId,
                OfficialPropositionId,
                confidenceBp: 10000,
                "official_source",
                acquiredAtMinute: 0,
                sourceEventId: "evidence.synthetic.mixed.official_notice"),
        ];

        return CreateWorld(
            "synthetic-b2-city-crisis-mixed",
            CreateCityCrisisActors(),
            items: items,
            beliefs: beliefs,
            plans: plans,
            groups: CreateCityCrisisGroups());
    }

    public static WorldState CreateMessageTopologyWorld()
    {
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

        return CreateWorld("synthetic-b3-message-topology", CreateMessageActors(), beliefs: beliefs);
    }

    public static WorldState CreateConflictingMessageWorld()
    {
        PropositionDefinition[] propositions =
        [
            new(
                ConflictReportPropositionId,
                ConflictTopicId,
                "open",
                ConflictUncertainPropositionId,
                distortionChanceBp: 10000,
                retellingConfidenceLossBp: 250),
            new(
                ConflictUncertainPropositionId,
                ConflictTopicId,
                "open",
                ConflictClosedRumorPropositionId,
                distortionChanceBp: 10000,
                retellingConfidenceLossBp: 500),
            new(ConflictClosedRumorPropositionId, ConflictTopicId, "closed"),
            new(
                ConflictConfirmedOpenPropositionId,
                ConflictTopicId,
                "open",
                retellingConfidenceLossBp: 100),
        ];
        BeliefState[] beliefs =
        [
            new(
                "belief.synthetic.gate_open_report",
                PlayerActorId,
                ConflictReportPropositionId,
                confidenceBp: 10000,
                "official_source",
                acquiredAtMinute: 0,
                sourceEventId: "evidence.synthetic.gate_open_report"),
            new(
                "belief.synthetic.gate_open_confirmed",
                PlayerActorId,
                ConflictConfirmedOpenPropositionId,
                confidenceBp: 9500,
                "direct_observation",
                acquiredAtMinute: 0,
                sourceEventId: "evidence.synthetic.gate_open_confirmed"),
        ];

        return CreateWorld(
            "synthetic-b3-conflicting-messages",
            CreateMessageActors(),
            beliefs: beliefs,
            propositions: propositions);
    }

    public static string PlaceId(int placeIndex) => $"place.synthetic.city.{placeIndex:D2}";

    public static string CarrierId(int placeIndex, int carrierIndex) =>
        $"person.synthetic.carrier.{placeIndex:D2}.{carrierIndex}";

    public static string MixedCityCrisisPlanOwnerId(int index) => $"person.synthetic.l1.{index:D3}";

    public static string MixedCityCrisisPlanId(int index) => $"plan.synthetic.crisis.{index:D2}";

    private static string MixedCityCrisisPlanItemId(int index) => $"item.synthetic.crisis_report.{index:D2}";

    private static List<ActorState> CreateCityCrisisActors()
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

        return actors;
    }

    private static List<ActorState> CreateMessageActors()
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

        return actors;
    }

    private static GroupState[] CreateCityCrisisGroups() =>
    [
        new(
            RegionalPopulationGroupId,
            "Synthetic Regional Population",
            "regional_population",
            CityCrisisGroupPopulation,
            PlaceId(49),
            organizationId: null,
            "synthetic-resident",
            foodStockUnits: CityCrisisInitialFoodStock,
            dailyFoodProductionPerThousand: CityCrisisDailyFoodProductionPerThousand,
            dailyFoodConsumptionPerThousand: CityCrisisDailyFoodConsumptionPerThousand),
    ];

    private static WorldState CreateWorld(
        string scenarioId,
        IEnumerable<ActorState> actors,
        IEnumerable<ItemState>? items = null,
        IEnumerable<BeliefState>? beliefs = null,
        IEnumerable<PropositionDefinition>? propositions = null,
        IEnumerable<PlanState>? plans = null,
        IEnumerable<GroupState>? groups = null)
    {
        var places = Enumerable.Range(0, PlaceCount)
            .Select(index => new PlaceDefinition(
                PlaceId(index),
                $"Synthetic City Place {index:D2}",
                index == 26 ? QueuedAccessRuleId : PublicAccessRuleId,
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
            new(QueuedAccessRuleId, "Synthetic Queued Access", [], mayQueue: true),
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
            items: items ?? [],
            commitments: [],
            propositions: propositions ??
            [
                new PropositionDefinition(OfficialPropositionId, OfficialPropositionId, "affirmed"),
            ],
            beliefs: beliefs,
            plans: plans,
            accessRules: accessRules,
            groups: groups);
    }
}
