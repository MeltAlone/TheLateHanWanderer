namespace LateHan.Game.Domain;

public enum Gender
{
    Male,
    Female,
}

public enum AbilityDomain
{
    Command,
    Martial,
    Strategy,
    Administration,
    Diplomacy,
    Learning,
}

[Flags]
public enum CharacterRole
{
    None = 0,
    Ruler = 1,
    General = 2,
    Official = 4,
    Scholar = 8,
    Merchant = 16,
    Ranger = 32,
    LocalNotable = 64,
    Commoner = 128,
}

public sealed record Abilities(
    int Command,
    int Martial,
    int Strategy,
    int Administration,
    int Diplomacy,
    int Learning)
{
    public Abilities ImproveLearning(int amount) => this with { Learning = Math.Clamp(Learning + amount, 1, 100) };

    public Abilities Improve(AbilityDomain domain, int amount) => domain switch
    {
        AbilityDomain.Command => this with { Command = Math.Clamp(Command + amount, 1, 100) },
        AbilityDomain.Martial => this with { Martial = Math.Clamp(Martial + amount, 1, 100) },
        AbilityDomain.Strategy => this with { Strategy = Math.Clamp(Strategy + amount, 1, 100) },
        AbilityDomain.Administration => this with { Administration = Math.Clamp(Administration + amount, 1, 100) },
        AbilityDomain.Diplomacy => this with { Diplomacy = Math.Clamp(Diplomacy + amount, 1, 100) },
        _ => this with { Learning = Math.Clamp(Learning + amount, 1, 100) },
    };
}

public sealed record Character(
    string Id,
    string Name,
    string CourtesyName,
    Gender Gender,
    CharacterRole Roles,
    string Identity,
    Abilities Abilities,
    IReadOnlyList<string> Traits,
    IReadOnlyList<string> Motivations,
    string SettlementId,
    string UrbanLocationId,
    string? Affiliation = null);

public sealed record PlayerBackground(
    string Id,
    string Name,
    string Description,
    string Identity,
    int StartingMoney,
    Abilities StartingAbilities,
    IReadOnlyList<string> StartingTraits);

public sealed record ConversationTopic(
    string Id,
    string Title,
    string Summary);

public sealed record GameScenario(
    string Id,
    string Name,
    string Description,
    GameDate StartDate,
    string StartSettlementId,
    string StartUrbanLocationId,
    WorldMap Map,
    IReadOnlyList<Character> Characters,
    IReadOnlyList<PlayerBackground> Backgrounds,
    IReadOnlyList<ConversationTopic> Topics,
    IReadOnlyList<SettlementConditionSeed> SettlementConditions,
    IReadOnlyList<RoadConditionSeed> RoadConditions);
