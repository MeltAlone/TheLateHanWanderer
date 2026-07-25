using LateHan.Game.Domain;

namespace LateHan.Game.Simulation;

public enum RecognitionLevel
{
    Unknown,
    HeardOf,
    Met,
    Acquainted,
    Trusted,
}

public sealed record RelationshipState(
    RecognitionLevel Recognition,
    int Favor,
    int Trust,
    int Obligation)
{
    public static RelationshipState Unknown { get; } = new(RecognitionLevel.Unknown, 0, 0, 0);

    public RelationshipState Improve(
        int favor,
        int trust,
        RecognitionLevel minimumRecognition) => this with
        {
            Recognition = Recognition < minimumRecognition ? minimumRecognition : Recognition,
            Favor = Math.Clamp(Favor + favor, -100, 100),
            Trust = Math.Clamp(Trust + trust, -100, 100),
        };
}

public enum CommitmentStatus
{
    Scheduled,
    Fulfilled,
    Missed,
    Cancelled,
}

public sealed record MeetingCommitment(
    string Id,
    string CharacterId,
    string SettlementId,
    string UrbanLocationId,
    GameDate DueDate,
    CommitmentStatus Status);

public enum InteractionActionKind
{
    Introduce,
    Visit,
    ScheduleMeeting,
    AttendMeeting,
    DiscussTopic,
    SupportPlan,
    DissuadePlan,
}

public sealed record AvailableAction(
    string Id,
    InteractionActionKind Kind,
    string CharacterId,
    string Title,
    string Description,
    int DurationDays,
    bool IsEnabled,
    string? BlockReason = null,
    string? TopicId = null,
    string? CommitmentId = null);
