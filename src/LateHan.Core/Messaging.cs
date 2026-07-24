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
        string? parentMessageId = null)
    {
        if (confidenceBp is < 0 or > 10000)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceBp));
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
}
