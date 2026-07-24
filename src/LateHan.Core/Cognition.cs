using System.Collections.ObjectModel;

namespace LateHan.Core;

public static class MessagePropagationPolicy
{
    public const string Version = "retelling-distortion.v1";
}

public sealed class PropositionDefinition
{
    public PropositionDefinition(
        string id,
        string topicId,
        string stance,
        string? retellingVariantId = null,
        int distortionChanceBp = 0,
        int retellingConfidenceLossBp = 0)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Proposition ID cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(topicId))
        {
            throw new ArgumentException("Proposition topic cannot be empty.", nameof(topicId));
        }

        if (string.IsNullOrWhiteSpace(stance))
        {
            throw new ArgumentException("Proposition stance cannot be empty.", nameof(stance));
        }

        if (distortionChanceBp is < 0 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(distortionChanceBp));
        }

        if (retellingConfidenceLossBp is < 0 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(retellingConfidenceLossBp));
        }

        if (retellingVariantId is not null && string.IsNullOrWhiteSpace(retellingVariantId))
        {
            throw new ArgumentException("Retelling variant ID cannot be empty.", nameof(retellingVariantId));
        }

        if (retellingVariantId is null && distortionChanceBp != 0)
        {
            throw new ArgumentException(
                "A distortion chance requires a retelling variant.",
                nameof(distortionChanceBp));
        }

        Id = id;
        TopicId = topicId;
        Stance = stance;
        RetellingVariantId = retellingVariantId;
        DistortionChanceBp = distortionChanceBp;
        RetellingConfidenceLossBp = retellingConfidenceLossBp;
    }

    public string Id { get; }

    public string TopicId { get; }

    public string Stance { get; }

    public string? RetellingVariantId { get; }

    public int DistortionChanceBp { get; }

    public int RetellingConfidenceLossBp { get; }
}

public sealed record BeliefConflictView(
    string HolderId,
    string TopicId,
    IReadOnlyList<BeliefState> Beliefs);

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
    private readonly IReadOnlyList<string> _requiredResourceIds;

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
        long? lastEvaluationMinute = null,
        IEnumerable<string>? requiredResourceIds = null,
        int priority = 0,
        bool mayReplaceLowerPriority = false)
    {
        if (confidenceThresholdBp is < 0 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceThresholdBp));
        }

        if (reevaluationIntervalMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reevaluationIntervalMinutes));
        }

        var resourceIds = (requiredResourceIds ?? []).ToArray();
        if (resourceIds.Any(string.IsNullOrWhiteSpace) ||
            resourceIds.Distinct(StringComparer.Ordinal).Count() != resourceIds.Length)
        {
            throw new ArgumentException("Plan resource IDs must be unique and non-empty.", nameof(requiredResourceIds));
        }

        if (priority < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priority));
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
        _requiredResourceIds = new ReadOnlyCollection<string>(resourceIds.Order(StringComparer.Ordinal).ToArray());
        Priority = priority;
        MayReplaceLowerPriority = mayReplaceLowerPriority;
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

    public IReadOnlyList<string> RequiredResourceIds => _requiredResourceIds;

    public int Priority { get; }

    public bool MayReplaceLowerPriority { get; }
}

public sealed record PlanResourceLockState(
    string ResourceId,
    string PlanId,
    long AcquiredMinute,
    string AcquiredEventId);

public static class PlanResourcePolicy
{
    public const string Version = "exclusive-plan-resource.v1";
}
