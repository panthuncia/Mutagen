namespace Mutagen.Bethesda.Skyrim.Records.Assets.VoiceType;

public static class VoiceContainerExtension
{
    public static VoiceContainer MergeInsert(this IEnumerable<VoiceContainer> voiceContainers, bool isDefaultIfEmpty)
    {
        // Enumerated once: the containers are often worked out as they're enumerated.
        VoiceContainer? output = null;
        foreach (var voiceContainer in voiceContainers)
        {
            (output ??= new VoiceContainer()).Insert(voiceContainer);
        }

        return output ?? new VoiceContainer(isDefaultIfEmpty);
    }

    public static VoiceContainer MergeIntersect(this IEnumerable<VoiceContainer> voiceContainers)
    {
        var voiceContainerList = voiceContainers.ToList();

        switch (voiceContainerList) {
            case []: return new VoiceContainer();
            case [var voiceContainer]: return voiceContainer;
            default:
                var output = new VoiceContainer(true);
                foreach (var voiceContainer in voiceContainerList)
                {
                    output.IntersectWith(voiceContainer);
                }

                return output;
        }
    }
}
