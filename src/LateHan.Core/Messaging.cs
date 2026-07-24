namespace LateHan.Core;

public sealed class MessageState
{
    public MessageState(
        string id,
        string propositionId,
        string senderId,
        string recipientId,
        int confidenceBp,
        long createdAtMinute,
        string createdEventId,
        string deliveredEventId,
        string? parentMessageId = null,
        string? sourcePropositionId = null,
        string propagationRuleVersion = MessagePropagationPolicy.Version,
        int? distortionDrawBp = null)
    {
        if (confidenceBp is < 0 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceBp));
        }


        if (string.IsNullOrWhiteSpace(sourcePropositionId ?? propositionId))
        {
            throw new ArgumentException("Source proposition ID cannot be empty.", nameof(sourcePropositionId));
        }

        if (string.IsNullOrWhiteSpace(propagationRuleVersion))
        {
            throw new ArgumentException("Propagation rule version cannot be empty.", nameof(propagationRuleVersion));
        }

        if (distortionDrawBp is < 0 or >= 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(distortionDrawBp));
        }

        Id = id;
        PropositionId = propositionId;
        SenderId = senderId;
        RecipientId = recipientId;
        ConfidenceBp = confidenceBp;
        CreatedAtMinute = createdAtMinute;
        CreatedEventId = createdEventId;
        DeliveredEventId = deliveredEventId;
        ParentMessageId = parentMessageId;
        SourcePropositionId = sourcePropositionId ?? propositionId;
        PropagationRuleVersion = propagationRuleVersion;
        DistortionDrawBp = distortionDrawBp;
    }

    public string Id { get; }

    public string PropositionId { get; }

    public string SenderId { get; }

    public string RecipientId { get; }

    public int ConfidenceBp { get; }

    public long CreatedAtMinute { get; }

    public string CreatedEventId { get; }

    public string DeliveredEventId { get; }

    public string? ParentMessageId { get; }

    public string SourcePropositionId { get; }

    public string PropagationRuleVersion { get; }

    public int? DistortionDrawBp { get; }

    public bool WasDistorted => !string.Equals(SourcePropositionId, PropositionId, StringComparison.Ordinal);
}
