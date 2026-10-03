using System.Text;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Skyrim.Records.Assets.VoiceType;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records.Skyrim.Assets;

/// <summary>
/// A voice-type lookup prepared in parts, its plugins read on several threads at once and in any order, is the lookup
/// prepared on one: every speaker's voice types, each voice type's speakers, and the speakers of responses conditioned
/// on each kind of condition; on random load orders of NPCs with templates (of NPCs and leveled NPCs), overrides
/// changing them, and talking activators.
/// </summary>
public class VoiceTypeLookupPreparationTests
{
    public static TheoryData<int> Seeds() => [.. Enumerable.Range(1, 30)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Prepared_in_parts_on_several_threads_it_is_prepared_on_one(int seed)
    {
        var (linkCache, responses, speakers) = LoadOrder(new Random(seed));
        var assets = linkCache.CreateImmutableAssetLinkCache();

        var serial = new VoiceTypeAssetLookup();
        serial.Prep(assets);

        var parts = new VoiceTypeAssetLookup();
        var preparation = parts.Prepare(assets);
        Parallel.For(0, preparation.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var part = preparation.Count - 1 - i;
            preparation.Read(part, part + 1);
        });
        preparation.Finish();

        var expected = Describe(serial, responses, speakers);
        // Some responses are spoken by only some speakers of a voice type: what NPCs and their templates give is in it.
        expected.ShouldContain("=0");
        Describe(parts, responses, speakers).ShouldBe(expected);
    }

    /// <summary>What a lookup says about every speaker and voice type, and about each response's speakers, in order.</summary>
    internal static string Describe(VoiceTypeAssetLookup lookup, IReadOnlyList<IDialogResponsesGetter> responses, IReadOnlyList<FormKey> speakers)
    {
        var text = new StringBuilder();
        foreach (var speaker in speakers)
        {
            text.AppendLine($"{speaker}: {string.Join(",", lookup.GetVoiceTypesOfSpeaker(speaker))}");
        }
        foreach (var voiceType in lookup.VoiceTypes)
        {
            text.AppendLine($"{voiceType}: {string.Join(",", lookup.GetSpeakersOfVoiceType(voiceType))}");
        }
        foreach (var response in responses)
        {
            var voices = lookup.GetSpeakerVoices(response);
            text.Append($"{response.EditorID}: ");
            if (voices is null) text.AppendLine("none");
            else text.AppendLine(string.Join(";", voices.Voices.Select(v => $"{v.Key}={string.Join(",", v.Value)}")) + (voices.IsDefault ? " default" : ""));
        }
        return text.ToString();
    }

    internal static (ILinkCache LinkCache, IReadOnlyList<IDialogResponsesGetter> Responses, IReadOnlyList<FormKey> Speakers) LoadOrder(Random random,
        int conditionPool = 0, int responseCount = 24)
    {
        var mods = Enumerable.Range(0, 4).Select(i => new SkyrimMod(ModKey.FromFileName($"Mod{i}.esp"), SkyrimRelease.SkyrimSE)).ToArray();
        var voiceTypes = new List<VoiceType>();
        var factions = new List<Faction>();
        var classes = new List<Class>();
        var races = new List<Race>();
        var npcs = new List<Npc>();
        var leveled = new List<LeveledNpc>();
        var activators = new List<TalkingActivator>();
        var made = new Dictionary<FormKey, int>();
        T Pick<T>(List<T> of) => of[random.Next(of.Count)];

        foreach (var mod in mods)
        {
            for (var i = 0; i < 3; i++)
            {
                var voiceType = mod.VoiceTypes.AddNew($"{mod.ModKey.Name}Voice{i}");
                if (random.Next(2) == 0) voiceType.Flags = VoiceType.Flag.AllowDefaultDialog;
                voiceTypes.Add(voiceType);
            }
            for (var i = 0; i < 2; i++)
            {
                factions.Add(mod.Factions.AddNew($"{mod.ModKey.Name}Faction{i}"));
                classes.Add(mod.Classes.AddNew($"{mod.ModKey.Name}Class{i}"));
                var race = mod.Races.AddNew($"{mod.ModKey.Name}Race{i}");
                if (random.Next(3) == 0) race.Flags = Race.Flag.Child;
                races.Add(race);
            }

            // NPCs, each templated on one made before it (never on itself, so what a template gives is the same whichever
            // NPC reaches it first), leveled NPCs of them, and overrides changing earlier plugins' NPCs.
            for (var i = 0; i < 12; i++)
            {
                var npc = mod.Npcs.AddNew($"{mod.ModKey.Name}Npc{i}");
                made[npc.FormKey] = made.Count;
                Shape(npc);
                npcs.Add(npc);
                if (random.Next(4) == 0)
                {
                    var list = mod.LeveledNpcs.AddNew($"{mod.ModKey.Name}List{i}");
                    list.Entries = [.. Enumerable.Range(0, random.Next(1, 4)).Select(_ => new LeveledNpcEntry
                    {
                        Data = new LeveledNpcEntryData { Reference = random.Next(3) == 0 && leveled.Count > 0 ? Pick(leveled).ToLink<INpcSpawnGetter>() : Pick(npcs).ToLink<INpcSpawnGetter>() },
                    })];
                    made[list.FormKey] = made.Count;
                    leveled.Add(list);
                }
            }
            for (var i = 0; i < 4 && npcs.Count > 12; i++)
            {
                var overridden = Pick(npcs);
                if (overridden.FormKey.ModKey == mod.ModKey) continue;
                var copy = (Npc)overridden.DeepCopy();
                Shape(copy);
                mod.Npcs.Set(copy);
            }
            var activator = mod.TalkingActivators.AddNew($"{mod.ModKey.Name}Activator");
            activator.Voice.SetTo(Pick(voiceTypes));
            activators.Add(activator);

            void Shape(Npc npc)
            {
                npc.Voice.SetTo(random.Next(4) == 0 ? null : Pick(voiceTypes));
                npc.Race.SetTo(Pick(races));
                npc.Class.SetTo(random.Next(4) == 0 ? null : Pick(classes));
                npc.Factions.Clear();
                foreach (var faction in factions.Where(_ => random.Next(4) == 0)) npc.Factions.Add(new RankPlacement { Faction = faction.ToLink() });
                npc.Configuration.Flags = random.Next(2) == 0 ? NpcConfiguration.Flag.Female : 0;
                // Only on what was made before the NPC, as a leveled NPC is only of what was made before it: no template
                // reaches itself.
                var earlier = made.Where(m => m.Value < made[npc.FormKey]).Select(m => m.Key).ToList();
                if (random.Next(3) != 0 && earlier.Count > 0)
                {
                    npc.Template.SetTo(earlier[random.Next(earlier.Count)]);
                    npc.Configuration.TemplateFlags = (NpcConfiguration.TemplateFlag)random.Next(0, 0x4000);
                }
                else
                {
                    npc.Template.Clear();
                    npc.Configuration.TemplateFlags = 0;
                }
            }
        }

        // Responses conditioned on each kind of condition, in the last plugin's quest.
        var last = mods[^1];
        var quest = last.Quests.AddNew("Quest");
        var topic = last.DialogTopics.AddNew("Topic");
        topic.Quest.SetTo(quest);
        var responses = new List<IDialogResponsesGetter>();
        ConditionData[] Conditions() =>
        [
            new GetInFactionConditionData { RunOnType = Condition.RunOnType.Subject }.Also(d => d.Faction.Link.SetTo(Pick(factions))),
            new GetIsClassConditionData { RunOnType = Condition.RunOnType.Subject }.Also(d => d.Class.Link.SetTo(Pick(classes))),
            new GetIsRaceConditionData { RunOnType = Condition.RunOnType.Subject }.Also(d => d.Race.Link.SetTo(Pick(races))),
            new GetIsSexConditionData { RunOnType = Condition.RunOnType.Subject, MaleFemaleGender = random.Next(2) == 0 ? MaleFemaleGender.Male : MaleFemaleGender.Female },
            new IsChildConditionData { RunOnType = Condition.RunOnType.Subject },
            new GetIsIDConditionData { RunOnType = Condition.RunOnType.Subject }.Also(d => d.Object.Link.SetTo(Pick(npcs))),
            new GetIsVoiceTypeConditionData { RunOnType = Condition.RunOnType.Subject }.Also(d => d.VoiceTypeOrList.Link.SetTo(Pick(voiceTypes))),
        ];
        List<ConditionFloat> SomeConditions() =>
        [
            .. Conditions().Where(_ => random.Next(3) == 0).Select(data => new ConditionFloat
            {
                Data = data,
                CompareOperator = random.Next(4) == 0 ? CompareOperator.NotEqualTo : CompareOperator.EqualTo,
                ComparisonValue = 1,
                Flags = random.Next(4) == 0 ? Condition.Flag.OR : 0,
            }),
        ];
        // With a pool, responses take their conditions from it (copies of them), and some have a speaker: many are alike.
        var pool = Enumerable.Range(0, conditionPool).Select(_ => SomeConditions()).ToList();
        // Each with a twin whose conditions are OR'd where its aren't, and not where they are: alike but for that.
        pool.AddRange([.. pool.Select(conditions => conditions.Select(c => (ConditionFloat)c.DeepCopy()).Select(c =>
        {
            c.Flags ^= Condition.Flag.OR;
            return c;
        }).ToList())]);
        var topics = new[] { topic, last.DialogTopics.AddNew("SceneTopic") };
        topics[1].Quest.SetTo(quest);
        topics[1].Category = DialogTopic.CategoryEnum.Scene;
        quest.Aliases.Add(new QuestAlias { ID = 3, UniqueActor = Pick(npcs).ToNullableLink() });
        var scene = last.Scenes.AddNew("Scene");
        scene.Actions.Add(new SceneAction { Type = SceneAction.TypeEnum.Dialog, Topic = topics[1].ToNullableLink<IDialogTopicGetter>(), ActorID = 3 });
        for (var i = 0; i < responseCount; i++)
        {
            var response = new DialogResponses(last) { EditorID = $"Response{i}" };
            response.Responses.Add(new DialogResponse());
            var conditions = pool.Count > 0 ? pool[random.Next(pool.Count)].Select(c => (ConditionFloat)c.DeepCopy()) : SomeConditions();
            response.Conditions.AddRange(conditions);
            if (pool.Count > 0 && random.Next(6) == 0) response.Speaker.SetTo(Pick(npcs));
            topics[pool.Count > 0 ? random.Next(2) : 0].Responses.Add(response);
            responses.Add(response);
        }

        var linkCache = new LoadOrder<IModListing<ISkyrimModGetter>>(mods.Select(m => new ModListing<ISkyrimModGetter>(m))).ToImmutableLinkCache();
        IReadOnlyList<FormKey> speakers = [.. npcs.Select(n => n.FormKey), .. leveled.Select(l => l.FormKey), .. activators.Select(a => a.FormKey)];
        return (linkCache, responses, speakers);
    }
}

file static class Fluent
{
    public static T Also<T>(this T value, Action<T> act)
    {
        act(value);
        return value;
    }
}
