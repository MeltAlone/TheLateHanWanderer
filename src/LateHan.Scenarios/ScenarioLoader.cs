using System.Text.Json;
using LateHan.Core;

namespace LateHan.Scenarios;

public sealed record LoadedScenario(WorldState World, string ComputedContentHash, string DeclaredContentHash);

public sealed class ScenarioLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public LoadedScenario Load(string scenarioDirectory)
    {
        var directory = Path.GetFullPath(scenarioDirectory);
        if (!Directory.Exists(directory))
        {
            throw new ScenarioValidationException([$"SCN-FILE-003 Scenario directory does not exist: {directory}"]);
        }

        var manifest = Read<ManifestDocument>(Path.Combine(directory, "manifest.json"));
        var requiredComponents = new[] { "world.json", "actors.json", "state.json" };
        var errors = new List<string>();
        foreach (var required in requiredComponents)
        {
            if (!manifest.Components.Contains(required, StringComparer.Ordinal))
            {
                errors.Add($"SCN-FILE-002 manifest.components is missing '{required}'.");
            }
        }

        foreach (var component in manifest.Components)
        {
            if (!File.Exists(Path.Combine(directory, component)))
            {
                errors.Add($"SCN-FILE-001 Declared component does not exist: {component}.");
            }
        }

        ThrowIfErrors(errors);

        var world = Read<WorldDocument>(Path.Combine(directory, "world.json"));
        var actors = Read<ActorsDocument>(Path.Combine(directory, "actors.json"));
        var state = Read<StateDocument>(Path.Combine(directory, "state.json"));
        ValidateComponentVersion(manifest, world, "world.json", errors);
        ValidateComponentVersion(manifest, actors, "actors.json", errors);
        ValidateComponentVersion(manifest, state, "state.json", errors);
        ValidateRandomConfiguration(manifest, errors);
        ValidateIdsAndReferences(manifest, world, actors, state, errors);
        ThrowIfErrors(errors);

        var computedHash = CanonicalJson.ComputeScenarioHash(directory, manifest.Components);
        if (!string.Equals(manifest.ContentHash, "pending-tooling", StringComparison.Ordinal) &&
            !string.Equals(manifest.ContentHash, computedHash, StringComparison.Ordinal))
        {
            throw new ScenarioValidationException(
                [$"SCN-HASH-001 Declared content hash '{manifest.ContentHash}' does not match '{computedHash}'."]);
        }

        var domainWorld = new WorldState(
            manifest.ScenarioId,
            manifest.ScenarioVersion,
            manifest.RulesetVersion,
            manifest.Rng.Version,
            EngineMetadata.Version,
            computedHash,
            manifest.PlayerActorId,
            manifest.Start.Minute,
            actors.Persons.Select(person => new ActorState(
                person.Id,
                person.Name,
                person.Location,
                null,
                person.Memberships.Select(item => new ActorMembership(item.Organization, item.Role)))),
            world.Places.Select(place => new PlaceDefinition(place.Id, place.Name, place.AccessRule, place.Controller)),
            world.Routes.Select(MapRoute),
            state.Items.Select(item => new ItemState(
                item.Id,
                item.Name,
                item.Kind,
                item.Holder,
                item.Author,
                item.IntendedRecipient,
                item.PropositionIds,
                item.ValidFor,
                item.ExpiresAtMinute)),
            state.Commitments.Select(commitment => new CommitmentState(
                commitment.Id,
                commitment.Debtor,
                commitment.Creditor,
                commitment.Action,
                commitment.Target,
                commitment.Recipient,
                commitment.DueMinute,
                commitment.Status)),
            rngRootSeedHex: manifest.Rng.RootSeedHex,
            rngDerivation: manifest.Rng.Derivation,
            propositions: state.Propositions.Select(proposition => new PropositionDefinition(
                proposition.Id,
                proposition.TopicId ?? proposition.Id,
                proposition.Stance,
                proposition.RetellingVariantId,
                proposition.DistortionChanceBp,
                proposition.RetellingConfidenceLossBp)),
            beliefs: state.Beliefs.Select(belief => new BeliefState(
                belief.Id,
                belief.Holder,
                belief.Proposition,
                belief.ConfidenceBp,
                belief.Source,
                belief.AcquiredAtMinute)),
            plans: state.Plans.Select(MapPlan),
            accessRules: world.AccessRules.Select(rule => new AccessRuleDefinition(
                rule.Id,
                rule.Name,
                rule.Requirements,
                rule.MayQueue)),
            placeAccessStates: state.PlaceStates.Select(item => new PlaceAccessState(
                item.Place,
                item.Open,
                item.QueueCount,
                item.SecurityPosture)),
            groups: actors.Groups.Select(group => new GroupState(
                group.Id,
                group.Name,
                group.Kind,
                group.Count,
                group.Location,
                group.Organization,
                group.PromotionProfile,
                group.FoodStockUnits,
                group.DailyFoodProductionPerThousand,
                group.DailyFoodConsumptionPerThousand,
                lastRemoteSettlementMinute: manifest.Start.Minute)));

        var engine = new WorldEngine(domainWorld);
        engine.InitializePlans();
        _ = engine.InitializeRemoteSimulation();

        return new LoadedScenario(domainWorld, computedHash, manifest.ContentHash);
    }

    private static PlanState MapPlan(PlanDocument plan)
    {
        var evaluationRule = plan.EvaluationRule switch
        {
            null or "" => PlanEvaluationRule.None,
            "written_report_inspection.v1" => PlanEvaluationRule.WrittenReportInspection,
            _ => throw new ScenarioValidationException(
                [$"SCN-PLAN-001 Unknown evaluation rule '{plan.EvaluationRule}' on '{plan.Id}'."]),
        };

        return new PlanState(
            plan.Id,
            plan.Owner,
            plan.Intent,
            plan.Stage,
            plan.BeliefRequirements,
            plan.NextEvaluationMinute,
            evaluationRule,
            plan.TriggerItemId,
            plan.TriggerPropositionId,
            plan.DestinationPlaceId,
            plan.ConfidenceThresholdBp,
            plan.ReevaluationIntervalMinutes,
            requiredResourceIds: plan.RequiredResourceIds,
            priority: plan.Priority,
            mayReplaceLowerPriority: plan.MayReplaceLowerPriority);
    }

    private static RouteDefinition MapRoute(RouteDocument route)
    {
        var modes = new Dictionary<TravelMode, int>();
        foreach (var pair in route.Minutes)
        {
            var mode = pair.Key switch
            {
                "walk" => TravelMode.Walk,
                "horse" => TravelMode.Horse,
                "with_group" => TravelMode.WithGroup,
                "with-group" => TravelMode.WithGroup,
                _ => throw new ScenarioValidationException([$"SCN-ROUTE-001 Unknown travel mode '{pair.Key}' on '{route.Id}'."]),
            };
            modes.Add(mode, pair.Value);
        }

        return new RouteDefinition(route.Id, route.From, route.To, route.DistanceLiQ10, route.Bidirectional, modes);
    }

    private static void ValidateRandomConfiguration(ManifestDocument manifest, ICollection<string> errors)
    {
        if (!string.Equals(manifest.Rng.Version, RandomMetadata.Xoshiro256StarStarV1, StringComparison.Ordinal))
        {
            errors.Add($"SCN-RNG-001 Unsupported rng.version '{manifest.Rng.Version}'.");
        }

        if (!string.Equals(manifest.Rng.Derivation, RandomMetadata.Sha256LittleEndianV1, StringComparison.Ordinal))
        {
            errors.Add($"SCN-RNG-002 Unsupported rng.derivation '{manifest.Rng.Derivation}'.");
        }

        if (!ulong.TryParse(
                manifest.Rng.RootSeedHex,
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture,
                out _))
        {
            errors.Add("SCN-RNG-003 rng.root_seed_hex must contain at most 16 hexadecimal digits.");
        }
    }

    private static void ValidateComponentVersion(
        ManifestDocument manifest,
        ComponentDocument component,
        string componentName,
        ICollection<string> errors)
    {
        if (!string.Equals(manifest.SchemaVersion, component.SchemaVersion, StringComparison.Ordinal))
        {
            errors.Add($"SCN-VERSION-002 {componentName} schema_version does not match manifest.");
        }

        if (!string.Equals(manifest.ScenarioId, component.ScenarioId, StringComparison.Ordinal))
        {
            errors.Add($"SCN-VERSION-001 {componentName} scenario_id does not match manifest.");
        }
    }

    private static void ValidateIdsAndReferences(
        ManifestDocument manifest,
        WorldDocument world,
        ActorsDocument actors,
        StateDocument state,
        ICollection<string> errors)
    {
        var allRecords = world.Organizations.Cast<NamedDocument>()
            .Concat(world.AccessRules)
            .Concat(world.Places)
            .Concat(world.Routes)
            .Concat(actors.Persons)
            .Concat(actors.Groups)
            .Concat(state.Items)
            .Concat(state.Propositions)
            .Concat(state.Beliefs)
            .Concat(state.Commitments)
            .Concat(state.Plans)
            .ToArray();
        var duplicateIds = allRecords
            .GroupBy(record => record.Id, StringComparer.Ordinal)
            .Where(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(id => id, StringComparer.Ordinal);
        foreach (var duplicateId in duplicateIds)
        {
            errors.Add($"SCN-ID-002 Duplicate or empty id '{duplicateId}'.");
        }

        var placeIds = world.Places.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var organizationIds = world.Organizations.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var accessRuleIds = world.AccessRules.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var actorIds = actors.Persons.Select(item => item.Id)
            .Concat(actors.Groups.Select(item => item.Id))
            .ToHashSet(StringComparer.Ordinal);
        var personIds = actors.Persons.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var holderIds = actorIds
            .Concat(world.Organizations.Select(item => item.Id))
            .ToHashSet(StringComparer.Ordinal);
        var allIds = allRecords.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var beliefIds = state.Beliefs.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var propositionIds = state.Propositions.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var propositionsById = state.Propositions
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var proposition in state.Propositions)
        {
            if (!string.IsNullOrWhiteSpace(proposition.RetellingVariantId))
            {
                Require(
                    propositionIds,
                    proposition.RetellingVariantId,
                    $"proposition '{proposition.Id}'.retelling_variant_id",
                    errors);
                if (propositionsById.TryGetValue(proposition.RetellingVariantId, out var variant) &&
                    !string.Equals(
                        proposition.TopicId ?? proposition.Id,
                        variant.TopicId ?? variant.Id,
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        $"SCN-COG-004 Proposition '{proposition.Id}' and retelling variant " +
                        $"'{variant.Id}' must share a topic_id.");
                }
            }

            if (string.IsNullOrWhiteSpace(proposition.TopicId ?? proposition.Id))
            {
                errors.Add($"SCN-COG-001 Proposition '{proposition.Id}' topic_id cannot be empty.");
            }

            if (string.IsNullOrWhiteSpace(proposition.Stance))
            {
                errors.Add($"SCN-COG-002 Proposition '{proposition.Id}' stance cannot be empty.");
            }

            if (proposition.DistortionChanceBp is < 0 or > 10000)
            {
                errors.Add(
                    $"SCN-NUM-005 Proposition '{proposition.Id}' distortion_chance_bp must be between 0 and 10000.");
            }

            if (proposition.RetellingConfidenceLossBp is < 0 or > 10000)
            {
                errors.Add(
                    $"SCN-NUM-006 Proposition '{proposition.Id}' retelling_confidence_loss_bp must be between 0 and 10000.");
            }

            if (string.IsNullOrWhiteSpace(proposition.RetellingVariantId) &&
                proposition.DistortionChanceBp != 0)
            {
                errors.Add(
                    $"SCN-COG-003 Proposition '{proposition.Id}' needs a retelling_variant_id when distortion_chance_bp is nonzero.");
            }
        }

        Require(personIds, manifest.PlayerActorId, "manifest.player_actor_id", errors);
        foreach (var place in world.Places)
        {
            Require(accessRuleIds, place.AccessRule, $"place '{place.Id}'.access_rule", errors);
        }

        foreach (var route in world.Routes)
        {
            Require(placeIds, route.From, $"route '{route.Id}'.from", errors);
            Require(placeIds, route.To, $"route '{route.Id}'.to", errors);
            if (route.DistanceLiQ10 < 0 || route.Minutes.Values.Any(value => value < 0))
            {
                errors.Add($"SCN-NUM-001 Route '{route.Id}' contains a negative distance or duration.");
            }
        }

        foreach (var person in actors.Persons)
        {
            Require(placeIds, person.Location, $"person '{person.Id}'.location", errors);
            foreach (var membership in person.Memberships)
            {
                Require(organizationIds, membership.Organization, $"person '{person.Id}'.memberships", errors);
            }
        }

        foreach (var group in actors.Groups)
        {
            Require(placeIds, group.Location, $"group '{group.Id}'.location", errors);
            if (!string.IsNullOrWhiteSpace(group.Organization))
            {
                Require(organizationIds, group.Organization, $"group '{group.Id}'.organization", errors);
            }

            if (group.Count < 0)
            {
                errors.Add($"SCN-NUM-005 Group '{group.Id}' count cannot be negative.");
            }

            if (group.FoodStockUnits < 0 ||
                group.DailyFoodProductionPerThousand < 0 ||
                group.DailyFoodConsumptionPerThousand < 0)
            {
                errors.Add($"SCN-NUM-007 Group '{group.Id}' remote food values cannot be negative.");
            }
        }

        foreach (var item in state.Items)
        {
            Require(holderIds, item.Holder, $"item '{item.Id}'.holder", errors);
            if (!string.IsNullOrWhiteSpace(item.Author))
            {
                Require(actorIds, item.Author, $"item '{item.Id}'.author", errors);
            }

            if (!string.IsNullOrWhiteSpace(item.IntendedRecipient))
            {
                Require(actorIds, item.IntendedRecipient, $"item '{item.Id}'.intended_recipient", errors);
            }

            foreach (var propositionId in item.PropositionIds)
            {
                Require(propositionIds, propositionId, $"item '{item.Id}'.proposition_ids", errors);
            }

            foreach (var accessRuleId in item.ValidFor)
            {
                Require(accessRuleIds, accessRuleId, $"item '{item.Id}'.valid_for", errors);
            }
        }

        foreach (var placeState in state.PlaceStates)
        {
            Require(placeIds, placeState.Place, "place_state.place", errors);
            if (placeState.QueueCount < 0)
            {
                errors.Add($"SCN-NUM-004 Place state '{placeState.Place}' queue_count cannot be negative.");
            }
        }

        foreach (var belief in state.Beliefs)
        {
            Require(actorIds, belief.Holder, $"belief '{belief.Id}'.holder", errors);
            Require(propositionIds, belief.Proposition, $"belief '{belief.Id}'.proposition", errors);
        }

        foreach (var commitment in state.Commitments)
        {
            Require(actorIds, commitment.Debtor, $"commitment '{commitment.Id}'.debtor", errors);
            Require(actorIds, commitment.Creditor, $"commitment '{commitment.Id}'.creditor", errors);
            Require(actorIds, commitment.Recipient, $"commitment '{commitment.Id}'.recipient", errors);
            Require(allIds, commitment.Target, $"commitment '{commitment.Id}'.target", errors);
        }

        foreach (var plan in state.Plans)
        {
            Require(actorIds, plan.Owner, $"plan '{plan.Id}'.owner", errors);
            foreach (var beliefId in plan.BeliefRequirements)
            {
                Require(beliefIds, beliefId, $"plan '{plan.Id}'.belief_requirements", errors);
            }

            if (!string.IsNullOrWhiteSpace(plan.TriggerItemId))
            {
                Require(allIds, plan.TriggerItemId, $"plan '{plan.Id}'.trigger_item_id", errors);
            }

            if (!string.IsNullOrWhiteSpace(plan.TriggerPropositionId))
            {
                Require(propositionIds, plan.TriggerPropositionId, $"plan '{plan.Id}'.trigger_proposition_id", errors);
            }

            if (!string.IsNullOrWhiteSpace(plan.DestinationPlaceId))
            {
                Require(placeIds, plan.DestinationPlaceId, $"plan '{plan.Id}'.destination_place_id", errors);
            }

            if (plan.ConfidenceThresholdBp is < 0 or > 10000)
            {
                errors.Add($"SCN-NUM-002 Plan '{plan.Id}' confidence_threshold_bp must be between 0 and 10000.");
            }

            if (plan.ReevaluationIntervalMinutes <= 0)
            {
                errors.Add($"SCN-NUM-003 Plan '{plan.Id}' reevaluation_interval_minutes must be positive.");
            }

            if (plan.RequiredResourceIds.Any(string.IsNullOrWhiteSpace) ||
                plan.RequiredResourceIds.Distinct(StringComparer.Ordinal).Count() != plan.RequiredResourceIds.Count)
            {
                errors.Add($"SCN-PLAN-002 Plan '{plan.Id}' required_resource_ids must contain unique non-empty IDs.");
            }
        }
    }

    private static void Require(IReadOnlySet<string> ids, string value, string path, ICollection<string> errors)
    {
        if (!ids.Contains(value))
        {
            errors.Add($"SCN-REF-001 {path}: unknown id '{value}'.");
        }
    }

    private static T Read<T>(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), JsonOptions)
                ?? throw new ScenarioValidationException([$"SCN-JSON-001 JSON document was empty: {path}"]);
        }
        catch (JsonException exception)
        {
            throw new ScenarioValidationException(
                [$"SCN-JSON-001 {path} line {exception.LineNumber}, byte {exception.BytePositionInLine}: {exception.Message}"],
                exception);
        }
    }

    private static void ThrowIfErrors(IReadOnlyCollection<string> errors)
    {
        if (errors.Count > 0)
        {
            throw new ScenarioValidationException(errors);
        }
    }
}

public sealed class ScenarioValidationException : Exception
{
    public ScenarioValidationException(IReadOnlyCollection<string> errors, Exception? innerException = null)
        : base(string.Join(Environment.NewLine, errors), innerException)
    {
        Errors = errors.ToArray();
    }

    public IReadOnlyList<string> Errors { get; }
}
