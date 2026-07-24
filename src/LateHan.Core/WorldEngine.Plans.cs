using System.Globalization;

namespace LateHan.Core;

public sealed partial class WorldEngine
{
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

        plan.Stage = "traveling_to_inspect";
        plan.Status = PlanStatus.Running;
        InvalidateActorDetailLevels([plan.OwnerId]);
        var action = BeginTravel(plan.OwnerId, plan.DestinationPlaceId!, TravelMode.Walk, [evaluated.Id]);
        plan.ActiveActionId = action.Id;
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
        events.Add(AppendEvent(
            "plan_completed",
            travelCompleted.Minute,
            travelCompleted.LocationId,
            [plan.Id, plan.OwnerId],
            [travelCompleted.Id],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["destination"] = plan.DestinationPlaceId ?? string.Empty,
                ["rule"] = "written_report_inspection.v1",
            }));
    }
}
