using LateHan.Game.Domain;

namespace LateHan.Game.Simulation;

public sealed record SettlementResourceLedger(
    string SettlementId,
    int Population,
    int Grain,
    int Treasury,
    int Labor,
    string LastCause)
{
    public SettlementResourceLedger Apply(
        int population,
        int grain,
        int treasury,
        int labor,
        string cause) => new(
            SettlementId,
            Clamp(Population + population),
            Clamp(Grain + grain),
            Clamp(Treasury + treasury),
            Clamp(Labor + labor),
            cause);

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}

public enum OrganizationKind
{
    Government,
    ScholarNetwork,
    MerchantGuild,
    LocalMilitia,
}

public sealed record OrganizationState(
    string Id,
    string Name,
    OrganizationKind Kind,
    string SettlementId,
    string UrbanLocationId,
    int Treasury,
    int Grain,
    int Personnel,
    int Influence)
{
    public OrganizationState Apply(int treasury, int grain, int personnel, int influence) => this with
    {
        Treasury = Clamp(Treasury + treasury),
        Grain = Clamp(Grain + grain),
        Personnel = Clamp(Personnel + personnel),
        Influence = Clamp(Influence + influence),
    };

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}

public enum OrganizationNeedKind
{
    Grain,
    Treasury,
    Labor,
    RoadSecurity,
}

public enum OrganizationCommissionStatus
{
    Accepted,
    Completed,
    Failed,
}

public sealed record OrganizationCommissionState(
    string Id,
    string OrganizationId,
    OrganizationNeedKind Need,
    string Title,
    string Description,
    string SettlementId,
    string UrbanLocationId,
    GameDate AcceptedOn,
    GameDate DueDate,
    int DurationDays,
    int RewardMoney,
    OrganizationCommissionStatus Status,
    string Result);
