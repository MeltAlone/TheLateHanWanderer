using LateHan.Core;

namespace LateHan.Tests;

public sealed class DeterministicRandomTests
{
    [Fact]
    public void Xoshiro256StarStarMatchesReferenceVector()
    {
        var state = new RandomStreamState("reference", 1, 2, 3, 4);

        var values = Enumerable.Range(0, 5)
            .Select(_ => RandomStreamRegistry.Next(state))
            .ToArray();

        Assert.Equal(
            [
                11520UL,
                0UL,
                1509978240UL,
                1215971899390074240UL,
                1216172134540287360UL,
            ],
            values);
        Assert.Equal(5UL, state.DrawCount);
    }

    [Fact]
    public void DomainSeparatedStreamsAreRepeatableAndIndependent()
    {
        var first = CreateRegistry();
        var second = CreateRegistry();

        var expected = first.NextUInt64("travel", "person.player_clerk");
        _ = second.NextUInt64("weather", "place.luoyang");
        var actual = second.NextUInt64("travel", "person.player_clerk");

        Assert.Equal(expected, actual);
        Assert.NotEqual(expected, first.NextUInt64("travel", "person.yuan_shao"));
        Assert.Equal(2, first.Streams.Count);
        Assert.Equal(2, second.Streams.Count);
    }

    [Fact]
    public void PreviewDoesNotCreateOrAdvanceAStream()
    {
        var registry = CreateRegistry();

        var preview = registry.PreviewUInt64("decision", "person.dong_zhuo", 3);

        Assert.Empty(registry.Streams);
        Assert.Equal(preview[0], registry.NextUInt64("decision", "person.dong_zhuo"));
        Assert.Equal(1UL, registry.Streams["decision:person.dong_zhuo"].DrawCount);
    }

    private static RandomStreamRegistry CreateRegistry() => new(
        RandomMetadata.Xoshiro256StarStarV1,
        "18908D2400000001",
        RandomMetadata.Sha256LittleEndianV1);
}
