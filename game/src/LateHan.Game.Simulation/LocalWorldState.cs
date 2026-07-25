using LateHan.Game.Domain;

namespace LateHan.Game.Simulation;

public sealed record SettlementState(
    string SettlementId,
    int Security,
    int GrainPrice,
    int Prosperity,
    int GovernmentControl)
{
    public SettlementState Apply(LocalPressure pressure) => new(
        SettlementId,
        Clamp(Security + pressure.Security),
        Clamp(GrainPrice + pressure.GrainPrice),
        Clamp(Prosperity + pressure.Prosperity),
        Clamp(GovernmentControl + pressure.GovernmentControl));

    private static int Clamp(int value) => Math.Clamp(value, 0, 100);
}

public sealed record RoadState(string RoadId, int Risk)
{
    public RoadState Apply(int delta) => this with { Risk = Math.Clamp(Risk + delta, 0, 100) };
}

public sealed record LocalPressure(
    int Security,
    int GrainPrice,
    int Prosperity,
    int GovernmentControl,
    IReadOnlyList<string> Sources)
{
    public static LocalPressure None { get; } = new(0, 0, 0, 0, []);

    public LocalPressure Add(int security, int grainPrice, int prosperity, int control, string source) => new(
        Security + security,
        GrainPrice + grainPrice,
        Prosperity + prosperity,
        GovernmentControl + control,
        Sources.Append(source).Distinct(StringComparer.Ordinal).ToArray());
}

public enum CharacterGoal
{
    MaintainOrder,
    SecureSupplies,
    BuildInfluence,
    SeekOpportunity,
}

public sealed record CharacterPlanState(
    string CharacterId,
    CharacterGoal Goal,
    IReadOnlyList<string> KnownSettlementIds,
    string LastIntent);
