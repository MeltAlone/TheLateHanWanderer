using LateHan.Game.Domain;

namespace LateHan.Game.Simulation;

public enum CareerGoalStatus
{
    Active,
    Completed,
    Failed,
}

public sealed record CareerGoalState(
    string Id,
    string Title,
    string Description,
    int Progress,
    int Target,
    GameDate Deadline,
    CareerGoalStatus Status);

public sealed record CareerState(
    string BackgroundId,
    int Reputation,
    int Network,
    int FinancialPressure,
    int UpkeepPaid,
    CareerGoalState Goal);

public enum CareerOpportunityKind
{
    CareerPath,
    BranchIntervention,
}

public sealed record CareerOpportunity(
    string Id,
    CareerOpportunityKind Kind,
    string Title,
    string Description,
    int DurationDays,
    int MoneyCost,
    bool IsEnabled,
    string? BlockReason = null,
    string? BranchId = null);

public enum HistoricalBranchStatus
{
    Upcoming,
    Active,
    Resolved,
}

public enum HistoricalBranchOutcome
{
    Undecided,
    Bystander,
    PlayerInfluenced,
}

public sealed record HistoricalBranchState(
    string Id,
    string Title,
    string Description,
    GameDate OpensOn,
    GameDate ResolvesOn,
    HistoricalBranchStatus Status,
    HistoricalBranchOutcome Outcome,
    string? PlayerApproach,
    string Result);
