using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim.Records.Assets.VoiceType;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim.Assets;

/// <summary>
/// Voice containers sharing a frozen container's sets rather than copying them hold what they held when they always
/// copied (<see cref="ReferenceVoiceContainer"/>): on random sequences of every operation, between containers frozen or
/// not, and a frozen container, or one it shares sets with, never changes.
/// </summary>
public class VoiceContainerSharingTests
{
    public static TheoryData<int> Seeds() => [.. Enumerable.Range(1, 60)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Containers_hold_what_they_held_when_they_copied(int seed)
    {
        var random = new Random(seed);
        var voiceTypes = Enumerable.Range(0, 6).Select(i => $"Voice{i}").ToArray();
        var npcs = Enumerable.Range(0, 12).Select(i => new FormKey(ModKey.FromFileName("Npcs.esp"), (uint)(0x800 + i))).ToArray();
        IEnumerable<FormKey> SpeakersOf(string voiceType) => npcs.Where((_, i) => i % voiceTypes.Length == Array.IndexOf(voiceTypes, voiceType) || i % 5 == 0);
        string[] SomeVoiceTypes() => [.. voiceTypes.Where(_ => random.Next(3) == 0)];

        var pool = new List<(VoiceContainer Shared, ReferenceVoiceContainer Copied)>();

        void Make()
        {
            switch (random.Next(6))
            {
                case 0:
                    var isDefault = random.Next(4) == 0;
                    pool.Add((new VoiceContainer(isDefault), new ReferenceVoiceContainer(isDefault)));
                    break;
                case 1:
                    var npc = npcs[random.Next(npcs.Length)];
                    var ofNpc = SomeVoiceTypes();
                    pool.Add((new VoiceContainer(npc, ofNpc), new ReferenceVoiceContainer(npc, ofNpc)));
                    break;
                case 2:
                    var npcVoices = npcs.Where(_ => random.Next(3) == 0).ToDictionary(n => n, _ => SomeVoiceTypes().ToHashSet());
                    pool.Add((new VoiceContainer(npcVoices), new ReferenceVoiceContainer(npcVoices)));
                    break;
                case 3:
                    var voiceType = voiceTypes[random.Next(voiceTypes.Length)];
                    pool.Add((new VoiceContainer(voiceType), new ReferenceVoiceContainer(voiceType)));
                    break;
                default:
                    var whole = SomeVoiceTypes();
                    pool.Add((new VoiceContainer(whole), new ReferenceVoiceContainer(whole)));
                    break;
            }
        }

        for (var i = 0; i < 6; i++) Make();
        for (var step = 0; step < 120; step++)
        {
            var (a, ra) = pool[random.Next(pool.Count)];
            var (b, rb) = pool[random.Next(pool.Count)];
            var operation = random.Next(9);
            if (operation >= 7)
            {
                if (operation == 7) Make();
                else pool.Add(((VoiceContainer)a.Clone(), (ReferenceVoiceContainer)ra.Clone()));
                continue;
            }
            if (operation == 6)
            {
                a.Freeze();
                continue;
            }
            if (ReferenceEquals(a, b)) continue;

            var keep = SomeVoiceTypes().ToHashSet();
            Action change = operation switch
            {
                0 => () => a.IntersectWith(b),
                1 => () => a.Insert(b),
                2 => () => a.Remove(b),
                3 => () => a.Remove(b, SpeakersOf),
                4 => () => a.InvertVoiceTypes(keep),
                _ => () => a.IntersectWith(b),
            };
            if (a.IsFrozen)
            {
                Should.Throw<InvalidOperationException>(change);
                continue;
            }
            change();
            switch (operation)
            {
                case 0 or 5: ra.IntersectWith(rb); break;
                case 1: ra.Insert(rb); break;
                case 2: ra.Remove(rb); break;
                case 3: ra.Remove(rb, SpeakersOf); break;
                case 4: ra.InvertVoiceTypes(keep); break;
            }

            foreach (var (shared, copied) in pool)
            {
                shared.IsDefault.ShouldBe(copied.IsDefault, $"step {step}");
                shared.Voices.Keys.OrderBy(k => k).ShouldBe(copied.Voices.Keys.OrderBy(k => k), $"step {step}");
                foreach (var (voiceType, held) in copied.Voices)
                {
                    shared.Voices[voiceType].OrderBy(n => n.ID).ShouldBe(held.OrderBy(n => n.ID), $"step {step}, {voiceType}");
                }
            }
        }
    }
}
