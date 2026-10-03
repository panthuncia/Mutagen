using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Records.Assets.VoiceType;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim.Assets;

/// <summary>
/// Responses alike (the same speaker, or the same conditions in the same quest and scene) share their voices, worked out
/// once: each response's are what a lookup asked about it alone works out; on random load orders whose responses take
/// their conditions from a few sets, some with speakers, in a topic and a scene's topic.
/// </summary>
public class VoiceTypeLookupResponseTests
{
    public static TheoryData<int> Seeds() => [.. Enumerable.Range(1, 30)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Responses_alike_share_the_voices_each_would_have_alone(int seed)
    {
        var (linkCache, responses, _) = VoiceTypeLookupPreparationTests.LoadOrder(new Random(seed), conditionPool: 5, responseCount: 40);
        var assets = linkCache.CreateImmutableAssetLinkCache();
        var shared = new VoiceTypeAssetLookup();
        shared.Prep(assets);

        var voices = responses.Select(shared.GetSpeakerVoices).ToList();

        for (var i = 0; i < responses.Count; i++)
        {
            var alone = new VoiceTypeAssetLookup();
            alone.Prep(assets);
            Describe(voices[i]).ShouldBe(Describe(alone.GetSpeakerVoices(responses[i])), $"{responses[i].EditorID}");
        }
        voices.Distinct(ReferenceEqualityComparer.Instance).Count().ShouldBeLessThan(responses.Count);
    }

    private static string Describe(VoiceContainer? voices) =>
        voices is null ? "none" : $"{string.Join(";", voices.Voices.Select(v => $"{v.Key}={string.Join(",", v.Value)}"))}{(voices.IsDefault ? " default" : "")}";
}
