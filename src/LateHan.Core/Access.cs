using System.Collections.ObjectModel;

namespace LateHan.Core;

public sealed record ActorMembership(string OrganizationId, string RoleId);

public sealed class AccessRuleDefinition
{
    private readonly IReadOnlyList<string> _requirements;

    public AccessRuleDefinition(
        string id,
        string name,
        IEnumerable<string> requirements,
        bool mayQueue)
    {
        Id = id;
        Name = name;
        _requirements = new ReadOnlyCollection<string>(requirements.ToArray());
        MayQueue = mayQueue;
    }

    public string Id { get; }

    public string Name { get; }

    public IReadOnlyList<string> Requirements => _requirements;

    public bool MayQueue { get; }
}

public sealed class PlaceAccessState
{
    public PlaceAccessState(
        string placeId,
        bool open,
        int queueCount,
        string securityPosture,
        string? controllerId = null,
        long? lastAdmissionMinute = null,
        string? lastAdmittedActorId = null)
    {
        if (queueCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(queueCount));
        }

        PlaceId = placeId;
        Open = open;
        QueueCount = queueCount;
        SecurityPosture = securityPosture;
        ControllerId = controllerId;
        LastAdmissionMinute = lastAdmissionMinute;
        LastAdmittedActorId = lastAdmittedActorId;
    }

    public string PlaceId { get; }

    public bool Open { get; internal set; }

    public int QueueCount { get; internal set; }

    public string SecurityPosture { get; internal set; }

    public string? ControllerId { get; internal set; }

    public long? LastAdmissionMinute { get; internal set; }

    public string? LastAdmittedActorId { get; internal set; }
}

public static class AccessQueuePolicy
{
    public const string Version = "stable-access-queue.v1";

    public const int ReviewIntervalMinutes = 5;

    public const int MaximumWaitMinutes = 60;
}
