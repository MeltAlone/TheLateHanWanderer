using System.Globalization;

namespace LateHan.Core;

public sealed partial class WorldEngine
{
    public ActionResult CancelPlan(string planId, string reason = "cancelled_by_command") =>
        CancelPlan(planId, reason, []);

    private ActionResult CancelPlan(string planId, string reason, IReadOnlyList<string> causeIds)
    {
        if (!State.Plans.TryGetValue(planId, out var plan))
        {
            throw new DomainCommandException("unknown_plan", $"Unknown plan '{planId}'.");
        }

        if (plan.Status is PlanStatus.Completed or PlanStatus.Cancelled)
        {
            throw new DomainCommandException("plan_not_cancellable", $"Plan '{planId}' cannot be cancelled.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainCommandException("invalid_plan_cancellation", "Plan cancellation reason cannot be empty.");
        }

        var startMinute = State.CurrentMinute;
        var firstEventSequence = State.EventSequenceCursor;
        if (plan.ActiveActionId is { } actionId &&
            State.Actions.TryGetValue(actionId, out var action) &&
            action.Status is ActionStatus.Running or ActionStatus.Interrupted)
        {
            _ = CancelAction(actionId, reason, causeIds);
        }
        else
        {
            _ = CancelPlanState(plan, reason, causeIds);
        }

        return new ActionResult(
            startMinute,
            State.CurrentMinute,
            State.Events.Where(item => item.Sequence >= firstEventSequence).ToArray(),
            ActionStatus.Cancelled);
    }

    public void InitializePlans()
    {
        foreach (var plan in State.Plans.Values
                     .Where(IsSchedulablePlan)
                     .OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            SchedulePlanEvaluation(plan, Math.Max(State.CurrentMinute, plan.NextEvaluationMinute), []);
        }
    }

    private static bool IsSchedulablePlan(PlanState plan) =>
        plan.EvaluationRule != PlanEvaluationRule.None &&
        plan.Status == PlanStatus.Active &&
        plan.PendingScheduledEventId is null;

    private void SchedulePlanEvaluation(PlanState plan, long dueMinute, IReadOnlyList<string> causeIds)
    {
        plan.NextEvaluationMinute = dueMinute;
        var scheduled = Schedule(
            dueMinute,
            ScheduledEventPhase.PlanEvaluation,
            plan.OwnerId,
            "plan_evaluation_due",
            causeIds: causeIds,
            details: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["plan_id"] = plan.Id,
            });
        plan.PendingScheduledEventId = scheduled.Id;
    }

    private IReadOnlyList<WorldEvent> ProcessPlanEvaluation(ScheduledWorldEvent scheduled)
    {
        if (!scheduled.Details.TryGetValue("plan_id", out var planId) ||
            !State.Plans.TryGetValue(planId, out var plan))
        {
            throw new InvalidOperationException($"Scheduled plan event '{scheduled.Id}' has no valid plan ID.");
        }

        if (!string.Equals(plan.PendingScheduledEventId, scheduled.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Scheduled plan event '{scheduled.Id}' is not active.");
        }

        plan.PendingScheduledEventId = null;
        plan.LastEvaluationMinute = scheduled.DueMinute;
        return plan.EvaluationRule switch
        {
            PlanEvaluationRule.WrittenReportInspection => EvaluateWrittenReportInspection(plan, scheduled),
            _ => throw new InvalidOperationException($"Plan '{plan.Id}' has no executable evaluation rule."),
        };
    }

    private IReadOnlyList<WorldEvent> EvaluateWrittenReportInspection(
        PlanState plan,
        ScheduledWorldEvent scheduled)
    {
        var missing = new List<string>();
        var requiredBeliefs = plan.BeliefRequirementIds
            .Select(id => State.Beliefs.GetValueOrDefault(id))
            .ToArray();
        if (requiredBeliefs.Any(item => item is null))
        {
            missing.Add("required_belief");
        }
        else if (requiredBeliefs.Any(item => item!.ConfidenceBp < plan.ConfidenceThresholdBp))
        {
            missing.Add("belief_confidence");
        }

        var itemMatches = plan.TriggerItemId is { } itemId &&
            State.Items.TryGetValue(itemId, out var item) &&
            string.Equals(item.HolderId, plan.OwnerId, StringComparison.Ordinal) &&
            plan.TriggerPropositionId is { } propositionId &&
            item.PropositionIds.Contains(propositionId, StringComparer.Ordinal);
        if (!itemMatches)
        {
            missing.Add("written_report");
        }

        if (FindActiveAction(plan.OwnerId) is not null)
        {
            missing.Add("owner_busy");
        }

        if (plan.DestinationPlaceId is null)
        {
            missing.Add("destination");
        }

        var decision = missing.Count == 0 ? "inspect" : "wait";
        var evaluated = AppendEvent(
            "plan_evaluated",
            scheduled.DueMinute,
            State.Actors.GetValueOrDefault(plan.OwnerId)?.PlaceId,
            [plan.Id, plan.OwnerId],
            scheduled.CauseIds,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["decision"] = decision,
                ["missing_conditions"] = string.Join(',', missing),
                ["rule"] = "written_report_inspection.v1",
                ["scheduled_event_id"] = scheduled.Id,
                ["confidence_threshold_bp"] = plan.ConfidenceThresholdBp.ToString(CultureInfo.InvariantCulture),
            });

        if (missing.Count > 0)
        {
            plan.Stage = "awaiting_written_report";
            InvalidateActorDetailLevels([plan.OwnerId]);
            SchedulePlanEvaluation(
                plan,
                checked(State.CurrentMinute + plan.ReevaluationIntervalMinutes),
                [evaluated.Id]);
            return [evaluated];
        }

        var resourceEvents = new List<WorldEvent> { evaluated };
        if (!TryAcquirePlanResources(plan, evaluated, resourceEvents))
        {
            plan.Stage = "waiting_for_resources";
            InvalidateActorDetailLevels([plan.OwnerId]);
            SchedulePlanEvaluation(
                plan,
                checked(State.CurrentMinute + plan.ReevaluationIntervalMinutes),
                [resourceEvents[^1].Id]);
            return resourceEvents;
        }

        plan.Stage = "traveling_to_inspect";
        plan.Status = PlanStatus.Running;
        InvalidateActorDetailLevels([plan.OwnerId]);
        try
        {
            var startCauseId = plan.RequiredResourceIds.Count == 0 ? evaluated.Id : resourceEvents[^1].Id;
            var action = BeginTravel(plan.OwnerId, plan.DestinationPlaceId!, TravelMode.Walk, [startCauseId]);
            plan.ActiveActionId = action.Id;
        }
        catch (DomainCommandException exception)
        {
            plan.Status = PlanStatus.Active;
            plan.Stage = "waiting_to_start";
            var failed = AppendEvent(
                "plan_start_failed",
                State.CurrentMinute,
                State.Actors.GetValueOrDefault(plan.OwnerId)?.PlaceId,
                [plan.Id, plan.OwnerId],
                [resourceEvents[^1].Id],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["code"] = exception.Code,
                    ["rule"] = "written_report_inspection.v1",
                });
            foreach (var released in ReleasePlanResources(plan, "plan_start_failed", [failed.Id]))
            {
                resourceEvents.Add(released);
            }

            SchedulePlanEvaluation(
                plan,
                checked(State.CurrentMinute + plan.ReevaluationIntervalMinutes),
                [failed.Id]);
        }

        return State.Events
            .Where(item => item.Sequence >= evaluated.Sequence)
            .ToArray();
    }

    private void CompleteLinkedPlan(
        ActionInstanceState action,
        WorldEvent travelCompleted,
        ICollection<WorldEvent> events)
    {
        var plan = State.Plans.Values
            .Where(item => string.Equals(item.ActiveActionId, action.Id, StringComparison.Ordinal))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (plan is null)
        {
            return;
        }

        plan.Status = PlanStatus.Completed;
        plan.Stage = "inspection_completed";
        plan.ActiveActionId = null;
        InvalidateActorDetailLevels([plan.OwnerId]);
        var completed = AppendEvent(
            "plan_completed",
            travelCompleted.Minute,
            travelCompleted.LocationId,
            [plan.Id, plan.OwnerId],
            [travelCompleted.Id],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["destination"] = plan.DestinationPlaceId ?? string.Empty,
                ["rule"] = "written_report_inspection.v1",
            });
        events.Add(completed);
        foreach (var resourceEvent in ReleasePlanResources(plan, "plan_completed", [completed.Id]))
        {
            events.Add(resourceEvent);
        }
    }

    private bool TryAcquirePlanResources(
        PlanState plan,
        WorldEvent evaluated,
        ICollection<WorldEvent> events)
    {
        if (plan.RequiredResourceIds.Count == 0)
        {
            return true;
        }

        var acquisitionCauseIds = new List<string> { evaluated.Id };
        var conflicts = plan.RequiredResourceIds
            .Select(resourceId => State.PlanResourceLocks.GetValueOrDefault(resourceId))
            .Where(item => item is not null && !string.Equals(item.PlanId, plan.Id, StringComparison.Ordinal))
            .Cast<PlanResourceLockState>()
            .OrderBy(item => item.ResourceId, StringComparer.Ordinal)
            .ToArray();
        if (conflicts.Length > 0)
        {
            var conflictEvent = AppendEvent(
                "plan_resource_conflict",
                State.CurrentMinute,
                State.Actors.GetValueOrDefault(plan.OwnerId)?.PlaceId,
                [plan.Id, plan.OwnerId, .. conflicts.Select(item => item.PlanId).Distinct(StringComparer.Ordinal)],
                [evaluated.Id, .. conflicts.Select(item => item.AcquiredEventId)],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["policy_version"] = PlanResourcePolicy.Version,
                    ["resources"] = string.Join(',', conflicts.Select(item => item.ResourceId)),
                    ["holders"] = string.Join(',', conflicts.Select(item => item.PlanId)),
                });
            events.Add(conflictEvent);
            acquisitionCauseIds.Add(conflictEvent.Id);

            var holders = conflicts
                .Select(item => State.Plans[item.PlanId])
                .DistinctBy(item => item.Id)
                .OrderBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
            if (!plan.MayReplaceLowerPriority || holders.Any(item => item.Priority >= plan.Priority))
            {
                return false;
            }

            foreach (var holder in holders)
            {
                var replacement = CancelPlan(holder.Id, $"replaced_by:{plan.Id}", [conflictEvent.Id]);
                foreach (var worldEvent in replacement.Events)
                {
                    events.Add(worldEvent);
                    acquisitionCauseIds.Add(worldEvent.Id);
                }
            }
        }

        var acquired = AppendEvent(
            "plan_resources_acquired",
            State.CurrentMinute,
            State.Actors.GetValueOrDefault(plan.OwnerId)?.PlaceId,
            [plan.Id, plan.OwnerId],
            acquisitionCauseIds.Distinct(StringComparer.Ordinal).ToArray(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["policy_version"] = PlanResourcePolicy.Version,
                ["resources"] = string.Join(',', plan.RequiredResourceIds),
                ["priority"] = plan.Priority.ToString(CultureInfo.InvariantCulture),
            });
        foreach (var resourceId in plan.RequiredResourceIds)
        {
            State.AddPlanResourceLock(new PlanResourceLockState(resourceId, plan.Id, State.CurrentMinute, acquired.Id));
        }

        events.Add(acquired);
        return true;
    }

    private IReadOnlyList<WorldEvent> ReleasePlanResources(
        PlanState plan,
        string reason,
        IReadOnlyList<string> causeIds)
    {
        var resourceIds = State.PlanResourceLocks.Values
            .Where(item => string.Equals(item.PlanId, plan.Id, StringComparison.Ordinal))
            .Select(item => item.ResourceId)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (resourceIds.Length == 0)
        {
            return [];
        }

        foreach (var resourceId in resourceIds)
        {
            State.RemovePlanResourceLock(resourceId);
        }

        return
        [
            AppendEvent(
                "plan_resources_released",
                State.CurrentMinute,
                State.Actors.GetValueOrDefault(plan.OwnerId)?.PlaceId,
                [plan.Id, plan.OwnerId],
                causeIds,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["policy_version"] = PlanResourcePolicy.Version,
                    ["reason"] = reason,
                    ["resources"] = string.Join(',', resourceIds),
                }),
        ];
    }

    private IReadOnlyList<WorldEvent> CancelPlanState(
        PlanState plan,
        string reason,
        IReadOnlyList<string> causeIds)
    {
        if (plan.PendingScheduledEventId is { } pendingId)
        {
            _ = State.RemoveScheduledEvent(pendingId);
            plan.PendingScheduledEventId = null;
        }

        plan.Status = PlanStatus.Cancelled;
        plan.Stage = "cancelled";
        plan.ActiveActionId = null;
        InvalidateActorDetailLevels([plan.OwnerId]);
        var cancelled = AppendEvent(
            "plan_cancelled",
            State.CurrentMinute,
            State.Actors.GetValueOrDefault(plan.OwnerId)?.PlaceId,
            [plan.Id, plan.OwnerId],
            causeIds,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["reason"] = reason,
            });
        return [cancelled, .. ReleasePlanResources(plan, reason, [cancelled.Id])];
    }

    private IReadOnlyList<WorldEvent> CancelLinkedPlanForAction(
        ActionInstanceState action,
        WorldEvent actionCancelled,
        string reason,
        IReadOnlyList<string> causeIds)
    {
        var plan = State.Plans.Values.FirstOrDefault(item =>
            string.Equals(item.ActiveActionId, action.Id, StringComparison.Ordinal) &&
            item.Status == PlanStatus.Running);
        return plan is null
            ? []
            : CancelPlanState(plan, reason, [actionCancelled.Id, .. causeIds]);
    }
}
