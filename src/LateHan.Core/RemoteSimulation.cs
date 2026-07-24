using System.Globalization;

namespace LateHan.Core;

public static class RemoteSimulationPolicy
{
    public const string Version = "distant-batch.v1";

    public const long FlowDenominator = 1000L * 24 * 60;
}

public sealed partial class WorldEngine
{
    public IReadOnlyList<ScheduledWorldEvent> InitializeRemoteSimulation(long cadenceMinutes = 24 * 60)
    {
        if (cadenceMinutes <= 0)
        {
            throw new DomainCommandException("invalid_remote_cadence", "Remote simulation cadence must be positive.");
        }

        var scheduledEvents = new List<ScheduledWorldEvent>();
        foreach (var group in State.Groups.Values.Where(group =>
                     group.DailyFoodProductionPerThousand > 0 ||
                     group.DailyFoodConsumptionPerThousand > 0))
        {
            var alreadyScheduled = State.ScheduledEvents.Any(item =>
                item.Kind == "remote_world_tick" &&
                string.Equals(item.StableSubjectId, group.Id, StringComparison.Ordinal));
            if (alreadyScheduled)
            {
                continue;
            }

            scheduledEvents.Add(ScheduleRecurringRemoteTick(
                group,
                checked(State.CurrentMinute + cadenceMinutes),
                cadenceMinutes,
                []));
        }

        return scheduledEvents;
    }

    private IReadOnlyList<WorldEvent> ProcessRemoteWorldTick(ScheduledWorldEvent scheduled)
    {
        if (!State.Groups.TryGetValue(scheduled.StableSubjectId, out var group))
        {
            throw new InvalidOperationException(
                $"Remote world tick '{scheduled.Id}' references unknown group '{scheduled.StableSubjectId}'.");
        }

        if (scheduled.DueMinute < group.LastRemoteSettlementMinute)
        {
            throw new InvalidOperationException(
                $"Remote world tick '{scheduled.Id}' precedes group '{group.Id}' settlement cursor.");
        }

        var recurringCadenceMinutes = GetRecurringCadenceMinutes(scheduled);
        long? nextRecurringDueMinute = recurringCadenceMinutes is null
            ? null
            : checked(scheduled.DueMinute + recurringCadenceMinutes.Value);

        var details = scheduled.Details.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        details["scheduled_event_id"] = scheduled.Id;
        details["scheduled_phase"] = ToPhaseId(scheduled.Phase);
        var events = SettleRemoteGroup(
            group,
            scheduled.DueMinute,
            scheduled.Kind,
            scheduled.CauseIds,
            details);
        var settled = events[0];
        if (nextRecurringDueMinute is { } nextDueMinute && recurringCadenceMinutes is { } cadenceMinutes)
        {
            _ = ScheduleRecurringRemoteTick(group, nextDueMinute, cadenceMinutes, [settled.Id]);
        }

        return events;
    }

    private IReadOnlyList<WorldEvent> SettleRemoteGroupBeforePopulationChange(
        GroupState group,
        IReadOnlyList<string> causeIds)
    {
        if (State.CurrentMinute == group.LastRemoteSettlementMinute ||
            group.DailyFoodProductionPerThousand == 0 && group.DailyFoodConsumptionPerThousand == 0)
        {
            return [];
        }

        return SettleRemoteGroup(
            group,
            State.CurrentMinute,
            "remote_world_settled_before_population_change",
            causeIds,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["settlement_reason"] = "population_change",
            });
    }

    private IReadOnlyList<WorldEvent> SettleRemoteGroup(
        GroupState group,
        long settlementMinute,
        string eventType,
        IReadOnlyList<string> causeIds,
        Dictionary<string, string> details)
    {
        var elapsedMinutes = settlementMinute - group.LastRemoteSettlementMinute;
        var production = CalculateFoodFlow(
            group.Count,
            group.DailyFoodProductionPerThousand,
            elapsedMinutes,
            group.FoodProductionRemainder);
        var demand = CalculateFoodFlow(
            group.Count,
            group.DailyFoodConsumptionPerThousand,
            elapsedMinutes,
            group.FoodDemandRemainder);
        var openingStock = group.FoodStockUnits;
        var cumulativeProduced = checked(group.CumulativeFoodProduced + production.Units);
        var cumulativeDemand = checked(group.CumulativeFoodDemand + demand.Units);
        var initialFoodSupply = (Int128)openingStock + group.CumulativeFoodConsumed - group.CumulativeFoodProduced;
        var cumulativeAvailableFood = initialFoodSupply + cumulativeProduced;
        var cumulativeConsumed = checked((long)(cumulativeAvailableFood < cumulativeDemand
            ? cumulativeAvailableFood
            : cumulativeDemand));
        var cumulativeUnmet = cumulativeDemand - cumulativeConsumed;
        var consumedFood = cumulativeConsumed - group.CumulativeFoodConsumed;
        var closingStock = checked((long)(cumulativeAvailableFood - cumulativeConsumed));
        var unmetFoodChange = cumulativeUnmet - group.CumulativeFoodUnmet;
        var shortageRatio = cumulativeDemand == 0
            ? 0
            : (Int128)cumulativeUnmet * 10000 / cumulativeDemand;
        var shortageBp = checked((int)(shortageRatio > 10000 ? 10000 : shortageRatio));
        if (initialFoodSupply < 0 || consumedFood < 0 || closingStock < 0 ||
            openingStock + production.Units != consumedFood + closingStock ||
            cumulativeDemand != cumulativeConsumed + cumulativeUnmet)
        {
            throw new InvalidOperationException($"Remote food ledger for group '{group.Id}' is not conserved.");
        }

        var managedActors = elapsedMinutes == 0 || group.OrganizationId is null
            ? []
            : State.GetActorIdsAtPlace(group.LocationId)
                .Select(actorId => State.Actors[actorId])
                .Where(actor => actor.DetailLevel == SimulationDetailLevel.L2)
                .Where(actor => actor.Memberships.Any(membership => string.Equals(
                    membership.OrganizationId,
                    group.OrganizationId,
                    StringComparison.Ordinal)))
                .OrderBy(actor => actor.Id, StringComparer.Ordinal)
                .ToArray();
        if (managedActors.Any(actor => actor.RemoteCycleCount == long.MaxValue))
        {
            throw new InvalidOperationException("A remote actor cycle cursor cannot advance further.");
        }

        group.FoodStockUnits = closingStock;
        group.LastRemoteSettlementMinute = settlementMinute;
        group.FoodProductionRemainder = production.Remainder;
        group.FoodDemandRemainder = demand.Remainder;
        group.CumulativeFoodProduced = cumulativeProduced;
        group.CumulativeFoodDemand = cumulativeDemand;
        group.CumulativeFoodConsumed = cumulativeConsumed;
        group.CumulativeFoodUnmet = cumulativeUnmet;
        group.FoodShortageBp = shortageBp;

        details["policy_version"] = RemoteSimulationPolicy.Version;
        details["elapsed_minutes"] = elapsedMinutes.ToString(CultureInfo.InvariantCulture);
        details["population_count"] = group.Count.ToString(CultureInfo.InvariantCulture);
        details["opening_food_units"] = openingStock.ToString(CultureInfo.InvariantCulture);
        details["produced_food_units"] = production.Units.ToString(CultureInfo.InvariantCulture);
        details["demanded_food_units"] = demand.Units.ToString(CultureInfo.InvariantCulture);
        details["consumed_food_units"] = consumedFood.ToString(CultureInfo.InvariantCulture);
        details["unmet_food_change_units"] = unmetFoodChange.ToString(CultureInfo.InvariantCulture);
        details["outstanding_unmet_food_units"] = cumulativeUnmet.ToString(CultureInfo.InvariantCulture);
        details["closing_food_units"] = closingStock.ToString(CultureInfo.InvariantCulture);
        details["food_shortage_bp"] = shortageBp.ToString(CultureInfo.InvariantCulture);
        details["managed_l2_actor_count"] = managedActors.Length.ToString(CultureInfo.InvariantCulture);
        details["material_balance"] = "conserved";
        var settled = AppendEvent(
            eventType,
            settlementMinute,
            group.LocationId,
            [group.Id],
            causeIds,
            details);
        if (managedActors.Length == 0)
        {
            return [settled];
        }

        var actorsUpdated = AppendEvent(
            "remote_named_actor_batch_updated",
            settlementMinute,
            group.LocationId,
            [group.Id],
            [settled.Id],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["actor_count"] = managedActors.Length.ToString(CultureInfo.InvariantCulture),
                ["elapsed_minutes"] = elapsedMinutes.ToString(CultureInfo.InvariantCulture),
                ["organization_id"] = group.OrganizationId!,
                ["policy_version"] = RemoteSimulationPolicy.Version,
            });
        foreach (var actor in managedActors)
        {
            actor.RemoteCycleCount++;
            actor.LastRemoteUpdateMinute = settlementMinute;
            actor.LastRemoteUpdateEventId = actorsUpdated.Id;
        }

        return [settled, actorsUpdated];
    }

    private static bool WouldUpdateActorInRemoteSettlement(ActorState actor, GroupState group, long settlementMinute)
    {
        return settlementMinute > group.LastRemoteSettlementMinute &&
               group.OrganizationId is not null &&
               actor.DetailLevel == SimulationDetailLevel.L2 &&
               string.Equals(actor.PlaceId, group.LocationId, StringComparison.Ordinal) &&
               actor.Memberships.Any(membership => string.Equals(
                   membership.OrganizationId,
                   group.OrganizationId,
                   StringComparison.Ordinal));
    }

    private static long? GetRecurringCadenceMinutes(ScheduledWorldEvent scheduled)
    {
        if (!scheduled.Details.TryGetValue("recurring", out var recurring) || recurring != "true")
        {
            return null;
        }

        if (!scheduled.Details.TryGetValue("cadence_minutes", out var cadenceText) ||
            !long.TryParse(cadenceText, NumberStyles.None, CultureInfo.InvariantCulture, out var cadenceMinutes) ||
            cadenceMinutes <= 0)
        {
            throw new InvalidOperationException($"Recurring remote tick '{scheduled.Id}' has an invalid cadence.");
        }

        return cadenceMinutes;
    }

    private ScheduledWorldEvent ScheduleRecurringRemoteTick(
        GroupState group,
        long dueMinute,
        long cadenceMinutes,
        IReadOnlyList<string> causeIds)
    {
        return Schedule(
            dueMinute,
            ScheduledEventPhase.SummaryAndNotification,
            group.Id,
            "remote_world_tick",
            group.LocationId,
            causeIds: causeIds,
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cadence_minutes"] = cadenceMinutes.ToString(CultureInfo.InvariantCulture),
                ["recurring"] = "true",
            });
    }

    private static FoodFlowResult CalculateFoodFlow(
        int populationCount,
        int dailyUnitsPerThousand,
        long elapsedMinutes,
        long priorRemainder)
    {
        var numerator = (Int128)populationCount * dailyUnitsPerThousand * elapsedMinutes + priorRemainder;
        var units = checked((long)(numerator / RemoteSimulationPolicy.FlowDenominator));
        var remainder = checked((long)(numerator % RemoteSimulationPolicy.FlowDenominator));
        return new FoodFlowResult(units, remainder);
    }

    private sealed record FoodFlowResult(long Units, long Remainder);
}
