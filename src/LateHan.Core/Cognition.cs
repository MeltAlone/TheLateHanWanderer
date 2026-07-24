using System.Collections.ObjectModel;

namespace LateHan.Core;

public sealed class BeliefState
{
    public BeliefState(
        string id,
        string holderId,
        string propositionId,
        int confidenceBp,
        string source,
        long acquiredAtMinute,
        string? sourceEventId = null)
    {
        if (confidenceBp is < 0 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceBp));
        }

        Id = id;
        HolderId = holderId;
        PropositionId = propositionId;
        ConfidenceBp = confidenceBp;
        Source = source;
        AcquiredAtMinute = acquiredAtMinute;
        SourceEventId = sourceEventId;
    }

    public string Id { get; }

    public string HolderId { get; }

    public string PropositionId { get; }

    public int ConfidenceBp { get; internal set; }

    public string Source { get; internal set; }

    public long AcquiredAtMinute { get; internal set; }

    public string? SourceEventId { get; internal set; }
}

public enum PlanEvaluationRule
{
    None,
    WrittenReportInspection,
}

public enum PlanStatus
{
    Active,
    Running,
    Completed,
    Cancelled,
}

public sealed class PlanState
{
    private readonly IReadOnlyList<string> _beliefRequirementIds;

    public PlanState(
        string id,
        string ownerId,
        string intent,
        string stage,
        IEnumerable<string> beliefRequirementIds,
        long nextEvaluationMinute,
        PlanEvaluationRule evaluationRule,
        string? triggerItemId,
        string? triggerPropositionId,
        string? destinationPlaceId,
        int confidenceThresholdBp,
        int reevaluationIntervalMinutes,
        PlanStatus status = PlanStatus.Active,
        string? pendingScheduledEventId = null,
        string? activeActionId = null,
        long? lastEvaluationMinute = null)
    {
        if (confidenceThresholdBp is < 0 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceThresholdBp));
        }

        if (reevaluationIntervalMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reevaluationIntervalMinutes));
        }

        Id = id;
        OwnerId = ownerId;
        Intent = intent;
        Stage = stage;
        _beliefRequirementIds = new ReadOnlyCollection<string>(beliefRequirementIds.ToArray());
        NextEvaluationMinute = nextEvaluationMinute;
        EvaluationRule = evaluationRule;
        TriggerItemId = triggerItemId;
        TriggerPropositionId = triggerPropositionId;
        DestinationPlaceId = destinationPlaceId;
        ConfidenceThresholdBp = confidenceThresholdBp;
        ReevaluationIntervalMinutes = reevaluationIntervalMinutes;
        Status = status;
        PendingScheduledEventId = pendingScheduledEventId;
        ActiveActionId = activeActionId;
        LastEvaluationMinute = lastEvaluationMinute;
    }

    public string Id { get; }

    public string OwnerId { get; }

    public string Intent { get; }

    public string Stage { get; internal set; }

    public IReadOnlyList<string> BeliefRequirementIds => _beliefRequirementIds;

    public long NextEvaluationMinute { get; internal set; }

    public PlanEvaluationRule EvaluationRule { get; }

    public string? TriggerItemId { get; }

    public string? TriggerPropositionId { get; }

    public string? DestinationPlaceId { get; }

    public int ConfidenceThresholdBp { get; }

    public int ReevaluationIntervalMinutes { get; }

    public PlanStatus Status { get; internal set; }

    public string? PendingScheduledEventId { get; internal set; }

    public string? ActiveActionId { get; internal set; }

    public long? LastEvaluationMinute { get; internal set; }
}
