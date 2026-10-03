using System.Collections.Concurrent;
using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Assets;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
namespace Mutagen.Bethesda.Skyrim.Records.Assets.VoiceType;

public class VoiceTypeAssetLookup : IAssetCacheComponent
{
    private ILinkCache _formLinkCache = null!;

    //Databases
    private readonly Dictionary<ModKey, HashSet<string>> _defaultVoiceTypes = new();
    private readonly Dictionary<FormKey, HashSet<string>> _speakerVoices = new();
    // Each voice type's speakers, in the order of _speakerVoices: made at the end of Prep, and only read after.
    private readonly Dictionary<string, List<FormKey>> _speakersByVoiceType = new();
    private readonly Dictionary<FormKey, HashSet<FormKey>> _factionNPCs = new();
    private readonly Dictionary<FormKey, HashSet<FormKey>> _classNPCs = new();
    private readonly Dictionary<FormKey, HashSet<FormKey>> _raceNPCs = new();
    private readonly Dictionary<MaleFemaleGender, HashSet<FormKey>> _genderNPCs = new();
    private HashSet<FormKey> _childNPCs = null!;
    private readonly Dictionary<FormKey, int> _dialogueSceneAliasIndex = new();
    private readonly Dictionary<FormKey, HashSet<FormKey>> _sharedInfoUsages = new();

    //Caches
    // Filled as asked, from several threads at once: a container is worked out without a lock (two threads asking at once
    // both work it out, and one is kept), and only read once it's in.
    private readonly ConcurrentDictionary<ModKey, VoiceContainer> _defaultSpeakerVoices = new();
    private readonly Made<(FormKey Quest, ModKey Mod)> _questCache = new();

    /// <summary>
    /// Voices made once for each key, and shared: a thread asking for one another is making waits for it rather than
    /// making it too, as every thread asks for the same few at first (the conditions most responses have).
    /// </summary>
    private sealed class Made<TKey>
        where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, Lazy<VoiceContainer>> _made = new();

        public bool TryGet(TKey key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out VoiceContainer voices)
        {
            voices = _made.TryGetValue(key, out var made) && made.IsValueCreated ? made.Value : null;
            return voices is not null;
        }

        public VoiceContainer Get(TKey key, Func<VoiceContainer> make) =>
            (_made.TryGetValue(key, out var made) ? made : _made.GetOrAdd(key, new Lazy<VoiceContainer>(make, LazyThreadSafetyMode.ExecutionAndPublication))).Value;
    }

    // While Prep runs: what each template or leveled NPC, by FormKey, gives its NPCs, worked out from the winning versions
    // once rather than again for every NPC version reaching it. Unused after Prep.
    private Memo<string>? _spawnVoiceTypes;
    private Memo<FormKey>? _spawnFactions;
    private Memo<FormKey>? _spawnClasses;

    private static HashSet<T> Remembered<T>(Memo<T>? memo, FormKey key, Func<HashSet<T>> make) => memo is null ? make() : memo.Get(key, make);

    /// <summary>
    /// What templates give, worked out by several threads at once: one asked while a thread works it out gives nothing
    /// more on that thread (a template reaching itself), and two threads working one out at once both do, and one is kept.
    /// </summary>
    private sealed class Memo<T> : IDisposable
    {
        private readonly ConcurrentDictionary<FormKey, HashSet<T>> _made = new();
        private readonly ThreadLocal<HashSet<FormKey>> _making = new(static () => []);

        public HashSet<T> Get(FormKey key, Func<HashSet<T>> make)
        {
            if (_made.TryGetValue(key, out var known)) return known;
            var making = _making.Value!;
            if (!making.Add(key)) return [];
            try
            {
                return _made.GetOrAdd(key, make());
            }
            finally
            {
                making.Remove(key);
            }
        }

        public void Dispose() => _making.Dispose();
    }

    public void Prep(IAssetLinkCache linkCache)
    {
        var preparation = Prepare(linkCache);
        preparation.Read(0, preparation.Count);
        preparation.Finish();
    }

    /// <summary>
    /// <see cref="Prep"/> in parts: each plugin's records are listed, with what they give their speakers, read through the
    /// load order's winners (<see cref="Preparation.Read"/>, on any threads), then put together in the load order's order
    /// as Prep does (<see cref="Preparation.Finish"/>, on one).
    /// </summary>
    public Preparation Prepare(IAssetLinkCache linkCache) => new(this, linkCache);

    public sealed class Preparation
    {
        private readonly VoiceTypeAssetLookup _lookup;
        private readonly IModGetter[] _mods;
        private readonly ModRecords[] _read;

        internal Preparation(VoiceTypeAssetLookup lookup, IAssetLinkCache linkCache)
        {
            _lookup = lookup;
            _lookup._formLinkCache = linkCache.FormLinkCache;
            _lookup._spawnVoiceTypes = new();
            _lookup._spawnFactions = new();
            _lookup._spawnClasses = new();
            _mods = [.. linkCache.FormLinkCache.PriorityOrder];
            _read = new ModRecords[_mods.Length];
        }

        /// <summary>The parts: the load order's plugins.</summary>
        public int Count => _mods.Length;

        /// <summary>Reads the plugins from <paramref name="start"/> up to <paramref name="end"/>.</summary>
        public void Read(int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                _read[i] = ModRecords.Of(_lookup, _mods[i]);
            }
        }

        /// <summary>Puts the plugins' records together, once all are read.</summary>
        public void Finish()
        {
            try
            {
                _lookup.PrepLoadOrder(_mods, _read);
            }
            finally
            {
                _lookup._spawnVoiceTypes?.Dispose();
                _lookup._spawnFactions?.Dispose();
                _lookup._spawnClasses?.Dispose();
                _lookup._spawnVoiceTypes = null;
                _lookup._spawnFactions = null;
                _lookup._spawnClasses = null;
            }
        }
    }

    /// <summary>
    /// What an NPC version gives the lookup: its voice types (used when it's the winner), the factions and classes it has
    /// or its template gives, its gender and race unless its template gives them.
    /// </summary>
    private readonly record struct NpcRecord(
        FormKey FormKey,
        HashSet<string> VoiceTypes,
        HashSet<FormKey> Factions,
        HashSet<FormKey> Classes,
        MaleFemaleGender? Gender,
        FormKey? Race);

    /// <summary>A plugin's records Prep reads, in the order it enumerates them, and what they give speakers.</summary>
    private sealed record ModRecords(
        (FormKey UniqueActor, FormKey Faction)[] AliasFactions,
        (FormKey LeveledNpc, HashSet<string> VoiceTypes)[] LeveledNpcs,
        NpcRecord[] Npcs,
        (FormKey Responses, FormKey SharedInfo)[] SharedInfos,
        (FormKey TalkingActivator, HashSet<string> VoiceTypes)[] TalkingActivators,
        FormKey[] ChildRaces,
        (FormKey Topic, int Actor)[] SceneTopics,
        string[] DefaultVoiceTypes)
    {
        public static ModRecords Of(VoiceTypeAssetLookup lookup, IModGetter mod)
        {
            var aliasFactions = new List<(FormKey, FormKey)>();
            foreach (var quest in mod.EnumerateMajorRecords<IQuestGetter>())
            {
                foreach (var alias in quest.Aliases)
                {
                    var uniqueActor = alias.UniqueActor.FormKey;
                    if (uniqueActor.IsNull) continue;

                    foreach (var faction in alias.Factions)
                    {
                        if (!faction.IsNull) aliasFactions.Add((uniqueActor, faction.FormKey));
                    }
                }
            }

            var leveledNpcs = new List<(FormKey, HashSet<string>)>();
            foreach (var leveledNpc in mod.EnumerateMajorRecords<ILeveledNpcGetter>())
            {
                if (leveledNpc.Entries is null) continue;
                leveledNpcs.Add((leveledNpc.FormKey, leveledNpc.Entries.Select(x => x.Data?.Reference).WhereNotNull().SelectMany(lookup.GetVoiceTypes).ToHashSet()));
            }

            var npcs = new List<NpcRecord>();
            foreach (var npc in mod.EnumerateMajorRecords<INpcGetter>())
            {
                var genders = lookup.GetGenders(npc);
                var races = lookup.GetRaces(npc);
                npcs.Add(new NpcRecord(
                    npc.FormKey,
                    lookup.GetVoiceTypes(npc),
                    lookup.GetFactions(npc),
                    lookup.GetClasses(npc),
                    genders.Count == 0 ? null : genders.Single(),
                    races.Count == 0 ? null : races.Single()));
            }

            var sharedInfos = new List<(FormKey, FormKey)>();
            foreach (var response in mod.EnumerateMajorRecords<IDialogResponsesGetter>())
            {
                if (!response.ResponseData.IsNull) sharedInfos.Add((response.FormKey, response.ResponseData.FormKey));
            }

            var sceneTopics = new List<(FormKey, int)>();
            foreach (var scene in mod.EnumerateMajorRecords<ISceneGetter>())
            {
                foreach (var action in scene.Actions)
                {
                    if (action.Type == SceneAction.TypeEnum.Dialog && !action.Topic.IsNull && action.ActorID != null)
                    {
                        sceneTopics.Add((action.Topic.FormKey, action.ActorID.Value));
                    }
                }
            }

            return new ModRecords(
                [.. aliasFactions],
                [.. leveledNpcs],
                [.. npcs],
                [.. sharedInfos],
                [.. mod.EnumerateMajorRecords<ITalkingActivatorGetter>().Select(t => (t.FormKey, lookup.GetVoiceTypes(t)))],
                [.. mod.EnumerateMajorRecords<IRaceGetter>().Where(race => (race.Flags & Race.Flag.Child) != 0).Select(race => race.FormKey)],
                [.. sceneTopics],
                [.. mod.EnumerateMajorRecords<IVoiceTypeGetter>()
                    .Where(voiceType => voiceType.EditorID != null && (voiceType.Flags & Skyrim.VoiceType.Flag.AllowDefaultDialog) != 0)
                    .Select(voiceType => voiceType.EditorID!)]);
        }
    }

    private void PrepLoadOrder(IModGetter[] mods, ModRecords[] read)
    {
        var childRaces = new HashSet<FormKey>();
        for (var m = 0; m < mods.Length; m++)
        {
            var mod = mods[m];
            var records = read[m];
            foreach (var (uniqueActor, faction) in records.AliasFactions)
            {
                _factionNPCs
                    .GetOrAdd(faction)
                    .Add(uniqueActor);
            }

            foreach (var (leveledNpc, voiceTypes) in records.LeveledNpcs)
            {
                _speakerVoices
                    .GetOrAdd(leveledNpc)
                    .Add(voiceTypes);
            }

            foreach (var npc in records.Npcs)
            {
                _speakerVoices.TryAdd(npc.FormKey, npc.VoiceTypes);

                foreach (var factionKey in npc.Factions)
                {
                    _factionNPCs
                        .GetOrAdd(factionKey)
                        .Add(npc.FormKey);
                }

                foreach (var classKey in npc.Classes)
                {
                    _classNPCs
                        .GetOrAdd(classKey)
                        .Add(npc.FormKey);
                }

                if (npc.Gender is { } gender)
                {
                    _genderNPCs
                        .GetOrAdd(gender)
                        .Add(npc.FormKey);
                }

                if (npc.Race is { } raceKey)
                {
                    _raceNPCs
                        .GetOrAdd(raceKey)
                        .Add(npc.FormKey);
                }
            }

            foreach (var (responses, sharedInfo) in records.SharedInfos)
            {
                _sharedInfoUsages
                    .GetOrAdd(sharedInfo)
                    .Add(responses);
            }

            foreach (var (talkingActivator, voiceTypes) in records.TalkingActivators)
            {
                _speakerVoices.TryAdd(talkingActivator, voiceTypes);
            }

            childRaces.UnionWith(records.ChildRaces);

            foreach (var (topic, actor) in records.SceneTopics)
            {
                _dialogueSceneAliasIndex.TryAdd(topic, actor);
            }

            _defaultVoiceTypes.Add(mod.ModKey, [.. records.DefaultVoiceTypes]);
        }

        //Build child cache
        _childNPCs = childRaces
            .SelectWhere(r => _raceNPCs.TryGetValue(r, out var raceNpcFormKeys) ? TryGet<HashSet<FormKey>>.Succeed(raceNpcFormKeys) : TryGet<HashSet<FormKey>>.Failure)
            .SelectMany(x => x)
            .ToHashSet();

        foreach (var (speaker, voiceTypes) in _speakerVoices)
        {
            foreach (var voiceType in voiceTypes)
            {
                _speakersByVoiceType.GetOrAdd(voiceType).Add(speaker);
            }
        }

        //Add master voice types
        var defaultVoicesCopy = new Dictionary<ModKey, HashSet<string>>(_defaultVoiceTypes);
        foreach (var mod in mods)
        {
            foreach (var master in mod.MasterReferences)
            {
                if (!defaultVoicesCopy.TryGetValue(master.Master, out var defaultVoiceTypes)) continue;

                foreach (var voiceType in defaultVoiceTypes)
                {
                    _defaultVoiceTypes[mod.ModKey].Add(voiceType);
                }
            }
        }
    }

    /// <summary>
    /// Used for testing mainly
    /// </summary>
    /// <param name="topic"></param>
    /// <param name="response"></param>
    /// <returns></returns>
    public VoiceContainer? GetVoicesWithQuest(IDialogTopicGetter topic, IDialogResponsesGetter response)
    {
        var quest = topic.Quest.TryResolve(_formLinkCache);
        if (quest == null) return null;

        //When the quest doesn't allow export return no voices  
        if ((quest.Flags & Quest.Flag.ExcludeFromDialogExport) != 0) return new VoiceContainer();

        //When all responses use a sound override, return no voices
        if (response.Responses.All(r => !r.Sound.IsNull)) return new VoiceContainer();

        //If this is a shared info and it's not used, return no voices
        if (topic.Subtype == DialogTopic.SubtypeEnum.SharedInfo && !_sharedInfoUsages.ContainsKey(response.FormKey)) return new VoiceContainer();

        //Get quest voices
        var questVoices = GetQuestVoices(topic, quest);

        //If we have selected default voices, make sure the quest voices are being checked first - they might not be part of default voices
        var voices = GetVoices(topic, response, quest);
        voices.IntersectWith(questVoices);

        LimitVoicesToSharedInfoUsages(voices, topic, response);

        return voices;
    }

    public IEnumerable<DataRelativePath> GetVoiceLineFilePaths(IDialogTopicGetter topic)
    {
        var quest = topic.Quest.TryResolve(_formLinkCache);
        if (quest == null) yield break;

        //Get quest voices
        var questVoices = GetQuestVoices(topic, quest);

        var (questString, topicString) = GetQuestAndTopicStrings(topic, quest);
        foreach (var responses in topic.Responses)
        {
            foreach (var path in GetVoiceLineFilePaths(topic, responses, quest, questVoices, questString, topicString))
            {
                yield return path;
            }
        }
    }

    public IEnumerable<DataRelativePath> GetVoiceLineFilePaths(IDialogResponsesGetter responses)
    {
        var responsesContext = _formLinkCache.ResolveSimpleContext<IDialogResponsesGetter>(responses.FormKey);
        if (!responsesContext.TryGetParent<IDialogTopicGetter>(out var topic)) yield break;

        var quest = topic.Quest.TryResolve(_formLinkCache);
        if (quest == null) yield break;

        //Get quest voices
        var questVoices = GetQuestVoices(topic, quest);

        var (questString, topicString) = GetQuestAndTopicStrings(topic, quest);
        foreach (var path in GetVoiceLineFilePaths(topic, responses, quest, questVoices, questString, topicString))
        {
            yield return path;
        }
    }

    /// <summary>
    /// Get all NPCs for a given dialog that can speak it based on the conditions of the dialog.
    /// </summary>
    /// <param name="responses">Dialog responses to get speakers for</param>
    /// <returns>List of NPC speakers, including npcs or talking activators</returns>
    public IEnumerable<IFormLinkGetter<IHasVoiceTypeGetter>> GetSpeakers(IDialogResponsesGetter responses)
    {
        if (GetSpeakerVoices(responses) is not { } voiceContainer) yield break;

        foreach (var formKey in voiceContainer.Voices.SelectMany(x =>
                 {
                     if (x.Value.Count > 0) return x.Value;

                     // Get speakers with voice type when the whole voice type is used (there are no speakers)
                     return GetSpeakersOfVoiceType(x.Key);
                 }))
        {
            yield return new FormLink<IHasVoiceTypeGetter>(formKey);
        }
    }

    /// <summary>
    /// Who can speak dialog responses, as <see cref="GetSpeakers"/> lists them, before they're listed: each voice type
    /// with the speakers of it who can, or with none for all of them (<see cref="GetSpeakersOfVoiceType"/>). Cheaper
    /// than listing them where most are a whole voice type, as a response without conditions is (every speaker).
    /// </summary>
    /// <param name="responses">Dialog responses to get speakers for</param>
    /// <returns>The voices, frozen and shared by the responses alike; null where the responses have no topic or quest</returns>
    public VoiceContainer? GetSpeakerVoices(IDialogResponsesGetter responses)
    {
        var responsesContext = _formLinkCache.ResolveSimpleContext<IDialogResponsesGetter>(responses.FormKey);
        if (!responsesContext.TryGetParent<IDialogTopicGetter>(out var topic)) return null;

        var quest = topic.Quest.TryResolve(_formLinkCache);
        if (quest == null) return null;

        // Many responses have the same speaker, scene and conditions, in the same quest: their voices are worked out once.
        var key = ResponseKeyOf(topic, responses, quest);
        if (_responseCache.TryGet(key, out var known)) return known;
        return _responseCache.Get(key, () =>
        {
            //Get quest voices
            var questVoices = GetQuestVoices(topic, quest);

            //If we have selected default voices, make sure the quest voices are being checked first - they might not be part of default voices
            var voiceContainer = GetVoices(topic, responses, quest);
            voiceContainer.IntersectWith(questVoices);

            return voiceContainer.IsDefault ? GetAllDefaultVoices() : voiceContainer.Freeze();
        });
    }

    /// <summary>
    /// What a response's voices (<see cref="GetSpeakerVoices"/>) depend on, besides the load order: the plugin of its
    /// topic, its quest, and its speaker; or, without one, its scene's alias and its conditions (each by what its voices
    /// depend on, <see cref="ConditionKey"/>, or none for one that doesn't limit them, and whether it's OR'd with the next).
    /// </summary>
    private sealed class ResponseKey(ModKey mod, FormKey quest, FormKey speaker, int? sceneAlias, (ConditionKey? Key, bool Or)[] conditions)
        : IEquatable<ResponseKey>
    {
        private readonly int _hash = Hash(mod, quest, speaker, sceneAlias, conditions);

        public bool Equals(ResponseKey? other) =>
            other is not null && _hash == other._hash && mod == other.Mod && quest == other.Quest && speaker == other.Speaker
            && sceneAlias == other.SceneAlias && conditions.AsSpan().SequenceEqual(other.Conditions);

        public override bool Equals(object? obj) => Equals(obj as ResponseKey);

        public override int GetHashCode() => _hash;

        private ModKey Mod => mod;

        private FormKey Quest => quest;

        private FormKey Speaker => speaker;

        private int? SceneAlias => sceneAlias;

        private (ConditionKey? Key, bool Or)[] Conditions => conditions;

        private static int Hash(ModKey mod, FormKey quest, FormKey speaker, int? sceneAlias, (ConditionKey? Key, bool Or)[] conditions)
        {
            var hash = new HashCode();
            hash.Add(mod);
            hash.Add(quest);
            hash.Add(speaker);
            hash.Add(sceneAlias);
            foreach (var condition in conditions) hash.Add(condition);
            return hash.ToHashCode();
        }
    }

    private readonly Made<ResponseKey> _responseCache = new();

    private ResponseKey ResponseKeyOf(IDialogTopicGetter topic, IDialogResponsesGetter response, IQuestGetter quest)
    {
        var mod = topic.FormKey.ModKey;
        // As GetVoices(topic, response, quest) works them out.
        if (!response.Speaker.IsNull) return new ResponseKey(mod, quest.FormKey, response.Speaker.FormKey, null, []);
        int? sceneAlias = topic.Category == DialogTopic.CategoryEnum.Scene && _dialogueSceneAliasIndex.TryGetValue(topic.FormKey, out var aliasIndex) ? aliasIndex : null;
        var conditions = response.Conditions;
        var keys = new (ConditionKey?, bool)[conditions.Count];
        for (var i = 0; i < keys.Length; i++)
        {
            var condition = conditions[i];
            keys[i] = (TryKey(condition, quest, mod, out var key, out _) ? key : null, (condition.Flags & Condition.Flag.OR) != 0);
        }
        return new ResponseKey(mod, quest.FormKey, FormKey.Null, sceneAlias, keys);
    }

    /// <summary>Every voice type a speaker has.</summary>
    public IEnumerable<string> VoiceTypes => _speakersByVoiceType.Keys;

    /// <summary>The speakers (NPCs and talking activators) with a voice type.</summary>
    public IReadOnlyCollection<FormKey> GetSpeakersOfVoiceType(string voiceType) =>
        _speakersByVoiceType.TryGetValue(voiceType, out var speakers) ? speakers : Array.Empty<FormKey>();

    /// <summary>A speaker's voice types: none for a FormKey that isn't a speaker.</summary>
    public IReadOnlyCollection<string> GetVoiceTypesOfSpeaker(FormKey speaker) =>
        _speakerVoices.TryGetValue(speaker, out var voiceTypes) ? voiceTypes : Array.Empty<string>();

    private IEnumerable<DataRelativePath> GetVoiceLineFilePaths(
        IDialogTopicGetter topic,
        IDialogResponsesGetter responses,
        IQuestGetter quest,
        VoiceContainer questVoices,
        string questString,
        string topicString)
    {
        var voices = GetDialogVoiceContainer(topic, responses, quest, questVoices);

        var responseFormID = responses.FormKey.ID.ToString("X8");

        foreach (var response in responses.Responses)
        {
            // Skip responses with sound override
            if (!response.Sound.IsNull) continue;

            var responseNumber = response.ResponseNumber;
            foreach (var voiceType in voices.GetVoiceTypes(_defaultVoiceTypes[topic.FormKey.ModKey]))
            {
                yield return Path.Combine
                (
                    "Sound",
                    "Voice",
                    topic.FormKey.ModKey.FileName,
                    voiceType,
                    $"{questString}_{topicString}_{responseFormID}_{responseNumber}.fuz"
                );
            }
        }
    }

    private VoiceContainer GetDialogVoiceContainer(
        IDialogTopicGetter topic,
        IDialogResponsesGetter responses,
        IQuestGetter quest,
        VoiceContainer questVoices)
    {
        //Don't process responses with response data
        if (!responses.ResponseData.IsNull)
        {
            return new VoiceContainer();
        }

        //If we have selected default voices, make sure the quest voices are being checked first - they might not be part of default voices
        var voices = GetVoices(topic, responses, quest);
        voices.IntersectWith(questVoices);

        LimitVoicesToSharedInfoUsages(voices, topic, responses);

        return voices;
    }

    private void LimitVoicesToSharedInfoUsages(VoiceContainer voices, IDialogTopicGetter topic, IDialogResponsesGetter responses) {
        if (topic.Subtype == DialogTopic.SubtypeEnum.SharedInfo && _sharedInfoUsages.TryGetValue(responses.FormKey, out var responseFormKeys))
        {
            var userConditions = responseFormKeys
                .Select(responseKey =>
                {
                    var responseContext = _formLinkCache.ResolveSimpleContext<IDialogResponsesGetter>(responseKey);
                    if (responseContext is not { Parent.Record: not null }) return null;

                    if (!responseContext.TryGetParent<IDialogTopicGetter>(out var currentTopic)) return null;
                    var currentQuest = currentTopic.Quest.TryResolve(_formLinkCache);
                    if (currentQuest == null) return null;

                    return GetVoices(responseContext.Record.Conditions, currentQuest, topic.FormKey.ModKey);
                })
                .WhereNotNull()
                .MergeInsert(true);

            voices.IntersectWith(userConditions);
        }
    }

    private VoiceContainer GetQuestVoices(IDialogTopicGetter topic, IQuestGetter quest)
    {
        // A quest's voices depend on the plugin of the topic asked about (its default voice types), so they're kept for
        // each quest and plugin.
        var key = (quest.FormKey, topic.FormKey.ModKey);
        if (_questCache.TryGet(key, out var questVoices)) return questVoices;
        return _questCache.Get(key, () => GetVoices(quest, topic.FormKey.ModKey).Freeze());
    }

    private static (string questString, string topicString) GetQuestAndTopicStrings(IDialogTopicGetter topic, IQuestGetter quest)
    {
        //Voice line variables
        var questID = quest.EditorID ?? "";
        var topicID = topic.EditorID ?? "";

        //Evaluate string length
        var questLength = questID.Length;
        var topicLength = topicID.Length;
        if (questLength + topicLength > 25)
        {
            if (questLength > 10)
            {
                questLength = 10;
                topicLength = 15;
            }
            else
            {
                topicLength = 25 - questLength;
            }
        }

        var questString = questID[..questLength];
        var topicString = topicID.Length > topicLength ? topicID[..topicLength] : topicID;
        return (questString, topicString);
    }

    private VoiceContainer GetVoices(IDialogTopicGetter topic, IDialogResponsesGetter response, IQuestGetter quest)
    {
        var voices = new VoiceContainer(true);

        //Use speaker only if we have one
        if (!response.Speaker.IsNull) return GetVoices(response.Speaker.FormKey);

        //Check scene
        if (topic.Category == DialogTopic.CategoryEnum.Scene && _dialogueSceneAliasIndex.TryGetValue(topic.FormKey, out var aliasIndex))
        {
            voices.IntersectWith(GetVoices(quest, aliasIndex, topic.FormKey.ModKey).Freeze());
        }

        //Search conditions
        if (response.Conditions.Any())
        {
            voices.IntersectWith(GetVoices(response.Conditions, quest, topic.FormKey.ModKey).Freeze());
        }

        return voices;
    }

    private VoiceContainer GetVoices(IEnumerable<IConditionGetter> conditions, IQuestGetter quest, ModKey currentMod)
    {
        var voiceTypesOrBlock = new List<VoiceContainer>();
        var currentConditions = new List<IConditionGetter>();

        //Calculate OR blocks
        var conditionsList = conditions.ToList();
        for (var i = 0; i < conditionsList.Count; i++)
        {
            var condition = conditionsList[i];
            currentConditions.Add(condition);

            //At every new AND or at the end of the conditions, finish the current block
            if ((condition.Flags & Condition.Flag.OR) == 0 || i == conditionsList.Count - 1)
            {
                var voices = GetVoiceTypesOrBlock(currentConditions, quest, currentMod);
                if (!voices.IsDefault) voiceTypesOrBlock.Add(voices);

                currentConditions.Clear();
            }
        }

        //Merge OR blocks
        return voiceTypesOrBlock.Any() ? voiceTypesOrBlock.MergeIntersect() : new VoiceContainer(true);
    }

    private VoiceContainer GetVoiceTypesOrBlock(IEnumerable<IConditionGetter> conditions, IQuestGetter quest, ModKey currentMod)
    {
        return conditions
            .Select(condition =>
            {
                var conditionVoices = GetVoices(condition, quest, currentMod);
                if (conditionVoices.IsDefault) return null;

                return conditionVoices;
            })
            .WhereNotNull()
            .MergeInsert(true);
    }

    /// <summary>What a condition's voices depend on, besides the load order: a condition's voices are made once for each.</summary>
    private readonly record struct ConditionKey(Condition.Function Function, FormKey Link, int Number, bool Valid, FormKey Quest, ModKey Mod);

    private readonly Made<ConditionKey> _conditionCache = new();

    /// <summary>A condition's voices, frozen: made once for all the conditions alike, and shared.</summary>
    private VoiceContainer GetVoices(IConditionGetter condition, IQuestGetter quest, ModKey currentMod)
    {
        if (!TryKey(condition, quest, currentMod, out var key, out var valid)) return new VoiceContainer(true);
        if (_conditionCache.TryGet(key, out var made)) return made;
        return _conditionCache.Get(key, () => MakeVoices(condition, quest, currentMod, valid).Freeze());
    }

    /// <summary>What a condition's voices depend on; false for a condition that doesn't limit them (its voices are the default).</summary>
    private bool TryKey(IConditionGetter condition, IQuestGetter quest, ModKey currentMod, out ConditionKey key, out bool valid)
    {
        var data = condition.Data;
        key = default;
        valid = false;

        if (data.RunOnType != Condition.RunOnType.Subject) return false;

        valid = IsConditionValid(condition);
        switch (data)
        {
            case IGetIsIDConditionDataGetter getIsId:
                key = new(data.Function, getIsId.Object.UsesLink() ? getIsId.Object.Link.FormKey : FormKey.Null, 0, valid, FormKey.Null, ModKey.Null);
                break;
            case IGetIsVoiceTypeConditionDataGetter isVoiceType:
                // Inverted, its voices are the default voices of the plugin asked about, less these.
                key = new(data.Function, isVoiceType.VoiceTypeOrList.UsesLink() ? isVoiceType.VoiceTypeOrList.Link.FormKey : FormKey.Null, 0, valid, FormKey.Null, valid ? ModKey.Null : currentMod);
                break;
            case IGetIsAliasRefConditionDataGetter aliasRef:
                key = new(data.Function, FormKey.Null, aliasRef.ReferenceAliasIndex, valid, quest.FormKey, currentMod);
                break;
            case IGetInFactionConditionDataGetter getInFaction:
                key = new(data.Function, getInFaction.Faction.UsesLink() ? getInFaction.Faction.Link.FormKey : FormKey.Null, 0, valid, FormKey.Null, ModKey.Null);
                break;
            case IGetFactionRankConditionDataGetter getFactionRank:
                key = new(data.Function, getFactionRank.Faction.UsesLink() ? getFactionRank.Faction.Link.FormKey : FormKey.Null, 0, valid, FormKey.Null, ModKey.Null);
                break;
            case IGetIsClassConditionDataGetter getIsClass:
                key = new(data.Function, getIsClass.Class.UsesLink() ? getIsClass.Class.Link.FormKey : FormKey.Null, 0, valid, FormKey.Null, ModKey.Null);
                break;
            case IGetIsRaceConditionDataGetter getIsRace:
                key = new(data.Function, getIsRace.Race.UsesLink() ? getIsRace.Race.Link.FormKey : FormKey.Null, 0, valid, FormKey.Null, ModKey.Null);
                break;
            case IGetIsSexConditionDataGetter sex:
                key = new(data.Function, FormKey.Null, (int)sex.MaleFemaleGender, valid, FormKey.Null, ModKey.Null);
                break;
            case IIsInListConditionDataGetter isInList:
                key = new(data.Function, isInList.FormList.UsesLink() ? isInList.FormList.Link.FormKey : FormKey.Null, 0, valid, FormKey.Null, ModKey.Null);
                break;
            case IIsChildConditionDataGetter:
                key = new(data.Function, FormKey.Null, 0, valid, FormKey.Null, ModKey.Null);
                break;
            default:
                return false;
        }
        return true;
    }

    private VoiceContainer MakeVoices(IConditionGetter condition, IQuestGetter quest, ModKey currentMod, bool valid)
    {
        var voices = new VoiceContainer();

        var data = condition.Data;

        switch (data)
        {
            case IGetIsIDConditionDataGetter getIsId:
                if (getIsId.Object.UsesLink())
                {
                    var getIsIdFormKey = getIsId.Object.Link.FormKey;
                    if (_speakerVoices.TryGetValue(getIsIdFormKey, out var idVoices))
                    {
                        voices = new VoiceContainer(getIsIdFormKey, idVoices);
                    }
                }

                break;
            case IGetIsVoiceTypeConditionDataGetter isVoiceType:
                if (isVoiceType.VoiceTypeOrList.UsesLink() && isVoiceType.VoiceTypeOrList.Link.TryResolve(_formLinkCache, out var voiceTypeRecord))
                {
                    switch (voiceTypeRecord)
                    {
                        case IVoiceTypeGetter voiceType when voiceType.EditorID != null:
                            voices = new VoiceContainer(voiceType.EditorID);
                            break;
                        case IFormListGetter formList:
                            voices = new VoiceContainer(formList.Items.SelectWhere(link =>
                            {
                                _formLinkCache.TryResolveIdentifier<IVoiceTypeGetter>(link.FormKey, out var linkVoiceTypeEditorId);
                                return linkVoiceTypeEditorId == null ? TryGet<string>.Failure : TryGet<string>.Succeed(linkVoiceTypeEditorId);
                            }).ToHashSet());
                            break;
                    }
                }

                break;
            case IGetIsAliasRefConditionDataGetter aliasRef:
                voices = GetVoices(quest, aliasRef.ReferenceAliasIndex, currentMod);

                break;
            case IGetInFactionConditionDataGetter getInFaction:
                if (getInFaction.Faction.UsesLink() && _factionNPCs.TryGetValue(getInFaction.Faction.Link.FormKey, out var factionNpcFormKeys))
                {
                    voices = new VoiceContainer(factionNpcFormKeys.ToDictionary(npc => npc, GetVoiceTypes));
                }

                break;
            case IGetFactionRankConditionDataGetter getFactionRank:
                // Assume the actor can be in any rank as long they are in the faction - they might shift ranks later on
                if (getFactionRank.Faction.UsesLink() && _factionNPCs.TryGetValue(getFactionRank.Faction.Link.FormKey, out var factionNpcFormKeys2))
                {
                    voices = new VoiceContainer(factionNpcFormKeys2.ToDictionary(npc => npc, GetVoiceTypes));
                }

                break;
            case IGetIsClassConditionDataGetter getIsClass:
                if (getIsClass.Class.UsesLink() && _classNPCs.TryGetValue(getIsClass.Class.Link.FormKey, out var classNpcFormKeys))
                {
                    voices = new VoiceContainer(classNpcFormKeys.ToDictionary(npc => npc, GetVoiceTypes));
                }

                break;
            case IGetIsRaceConditionDataGetter getIsRace:
                if (getIsRace.Race.UsesLink() && _raceNPCs.TryGetValue(getIsRace.Race.Link.FormKey, out var raceNpcFormKeys))
                {
                    voices = new VoiceContainer(raceNpcFormKeys.ToDictionary(npc => npc, GetVoiceTypes));
                }

                break;
            case IGetIsSexConditionDataGetter sexConditionDataGetter:
                if (_genderNPCs.TryGetValue(sexConditionDataGetter.MaleFemaleGender, out var genderNpcFormKeys))
                {
                    voices = new VoiceContainer(genderNpcFormKeys.ToDictionary(npc => npc, GetVoiceTypes));
                }

                break;
            case IIsInListConditionDataGetter isInList:
                if (isInList.FormList.UsesLink())
                {
                    var formList = isInList.FormList.Link.TryResolve(_formLinkCache);
                    //Only look at speakers in the form list
                    if (formList != null) voices = formList.Items.Select(link => GetVoices(link.FormKey)).MergeInsert(false);
                }

                break;
            case IIsChildConditionDataGetter isChild:
                voices = new VoiceContainer(_childNPCs.ToDictionary(npc => npc, GetVoiceTypes));

                break;
            default:
                voices = new VoiceContainer(true);
                break;
        }

        if (!voices.IsDefault && !valid)
        {
            //Can't invert alias according to CK calculation
            if (data.Function == Condition.Function.GetIsAliasRef)
            {
                return new VoiceContainer(true);
            }

            voices = Invert(voices, data.Function == Condition.Function.GetIsVoiceType, currentMod);
        }

        return voices;
    }

    private bool IsConditionValid(IConditionGetter condition)
    {
        const double floatTolerance = 0.001;

        bool FloatEquals(float value, int expected) => Math.Abs(value - expected) < floatTolerance;

        bool GlobalEquals(ILink<IGlobalGetter> global, int expected)
        {
            var globalValue = global.TryResolve(_formLinkCache);
            if (globalValue == null) return false;

            return globalValue switch
            {
                IGlobalFloatGetter globalFloat => globalFloat.Data != null && Math.Abs(globalFloat.Data.Value - expected) < floatTolerance,
                IGlobalIntGetter globalInt => globalInt.Data != null && globalInt.Data.Value == expected,
                IGlobalShortGetter globalShort => globalShort.Data != null && globalShort.Data.Value == expected,
                _ => false
            };
        }

        switch (condition.CompareOperator)
        {
            case CompareOperator.EqualTo:
                return condition switch
                {
                    IConditionFloatGetter floatCondition => FloatEquals(floatCondition.ComparisonValue, 1),
                    IConditionGlobalGetter globalCondition => GlobalEquals(globalCondition.ComparisonValue, 1),
                    _ => false
                };
            case CompareOperator.NotEqualTo:
                return condition switch
                {
                    IConditionFloatGetter floatCondition => FloatEquals(floatCondition.ComparisonValue, 0),
                    IConditionGlobalGetter globalCondition => GlobalEquals(globalCondition.ComparisonValue, 0),
                    _ => false
                };
            case CompareOperator.GreaterThan:
            case CompareOperator.GreaterThanOrEqualTo:
            case CompareOperator.LessThan:
            case CompareOperator.LessThanOrEqualTo:
            default:
                return false;
        }
    }

    private VoiceContainer GetVoices(IQuestGetter quest, int aliasIndex, ModKey currentMod)
    {
        var alias = quest.Aliases.FirstOrDefault(a => a.ID == aliasIndex);
        return alias == null ? new VoiceContainer(true) : GetVoices(alias, quest, currentMod);
    }

    private VoiceContainer GetVoices(IQuestAliasGetter alias, IQuestGetter quest, ModKey currentMod)
    {
        //External Alias
        if (alias.External != null)
        {
            var externalQuest = alias.External.Quest.TryResolve(_formLinkCache);
            var aliasIndex = alias.External.AliasID;
            if (externalQuest != null && aliasIndex != null)
            {
                return GetVoices(externalQuest, aliasIndex.Value, currentMod);
            }
        }

        //Additional voice types
        VoiceContainer? additionalVoices = null;
        if (!alias.VoiceTypes.IsNull)
        {
            var additionalVoiceTypes = alias.VoiceTypes.TryResolve(_formLinkCache);
            if (additionalVoiceTypes != null)
            {
                additionalVoices = additionalVoiceTypes switch
                {
                    INpcGetter npc => GetVoices(npc),
                    IFormListGetter formList => GetVoices(formList),
                    _ => new VoiceContainer(true)
                };
            }
        }

        //Forced Ref
        if (!alias.ForcedReference.IsNull)
        {
            var placedNPC = alias.ForcedReference.TryResolve<IPlacedNpcGetter>(_formLinkCache);
            if (placedNPC != null)
            {
                var voices = GetVoices(placedNPC.Base.FormKey);
                if (additionalVoices != null) voices.Insert(additionalVoices);
                return voices;
            }

            var placedObject = alias.ForcedReference.TryResolve<IPlacedObjectGetter>(_formLinkCache);
            if (placedObject != null)
            {
                var voices = GetVoices(placedObject.Base.FormKey);
                if (additionalVoices != null) voices.Insert(additionalVoices);
                return voices;
            }
        }

        //Created object
        if (alias.CreateReferenceToObject != null)
        {
            var voices = GetVoices(alias.CreateReferenceToObject.Object.FormKey);
            if (additionalVoices != null) voices.Insert(additionalVoices);
            return voices;
        }

        //Conditions
        if (alias.Conditions.Any())
        {
            var voices = GetVoices(alias.Conditions, quest, currentMod);
            if (additionalVoices != null) voices.Insert(additionalVoices);
            return voices;
        }

        //Additional voices overwrites location alias and unique npc
        if (additionalVoices != null)
        {
            return additionalVoices;
        }

        //Location alias
        if (alias.Location is { AliasID: {} })
        {
            var locationAlias = quest.Aliases.FirstOrDefault(a => a.ID == alias.Location.AliasID.Value);
            if (locationAlias != null)
            {
                if (!locationAlias.SpecificLocation.IsNull)
                {
                    var location = locationAlias.SpecificLocation.TryResolve(_formLinkCache);
                    if (location is not null)
                    {
                        foreach (var locRef in location.LocationRefTypesReferences().Where(r => r.LocationRefType.FormKey == alias.Location.RefType.FormKey))
                        {
                            var linkedRef = locRef.Ref.TryResolve(_formLinkCache);
                            if (linkedRef == null) continue;

                            return linkedRef switch
                            {
                                IPlacedNpcGetter placedNpc => GetVoices(placedNpc.Base.FormKey),
                                IPlacedObjectGetter placedObject => GetVoices(placedObject.Base.FormKey),
                                _ => new VoiceContainer(true)
                            };
                        }
                    }
                }
            }
        }

        //Unique NPC
        if (!alias.UniqueActor.IsNull) return new VoiceContainer(alias.UniqueActor.FormKey, GetVoiceTypes(alias.UniqueActor.FormKey));

        //Find matching from event => default voices
        if (alias.FindMatchingRefFromEvent != null || alias.FindMatchingRefNearAlias != null)
        {
            return new VoiceContainer(true);
        }

        //Nothing is valid => no voices for this alias
        return new VoiceContainer();
    }

    private VoiceContainer GetVoices(FormKey speaker) => new(speaker, GetVoiceTypes(speaker));
    private VoiceContainer GetVoices(INpcGetter npc) => new(npc.FormKey, GetVoiceTypes(npc.FormKey));

    private VoiceContainer GetVoices(IFormListGetter formList)
    {
        var voices = new List<VoiceContainer>();

        foreach (var item in formList.Items)
        {
            if (_formLinkCache.TryResolveIdentifier<IVoiceTypeGetter>(item.FormKey, out var voiceTypeEditorId))
            {
                //FormList entry is VoiceType
                voices.Add(new VoiceContainer(voiceTypeEditorId!));
            }
            else if (_speakerVoices.ContainsKey(item.FormKey))
            {
                //FormList entry is Npc
                voices.Add(GetVoices(item.FormKey));
            }
        }

        return voices.MergeInsert(false);
    }

    private VoiceContainer GetVoices(IQuestGetter quest, ModKey currentMod) => GetVoices(quest.DialogConditions, quest, currentMod);

    // Made by the first to ask, once Prep is done; others asking meanwhile wait for it.
    private readonly Made<bool> _allDefaultVoices = new();

    private VoiceContainer GetAllDefaultVoices() =>
        _allDefaultVoices.TryGet(true, out var made) ? made : _allDefaultVoices.Get(true, () => MakeAllDefaultVoices().Freeze());

    private VoiceContainer MakeAllDefaultVoices()
    {
        var allDefaultVoices = new VoiceContainer();

        foreach (var modKey in _formLinkCache.PriorityOrder.Select(x => x.ModKey))
        {
            allDefaultVoices.Insert(GetDefaultVoices(modKey));
        }

        return allDefaultVoices;
    }

    private VoiceContainer GetDefaultVoices(ModKey mod)
    {
        if (_defaultSpeakerVoices.TryGetValue(mod, out var defaultVoiceTypes)) return defaultVoiceTypes;
        // Each default voice type standing for all its speakers, rather than listing them (they're the same).
        var keep = _defaultVoiceTypes[mod];
        var vc = new VoiceContainer(_speakersByVoiceType.Keys.Where(keep.Contains));
        return _defaultSpeakerVoices.GetOrAdd(mod, vc.Freeze());
    }

    private VoiceContainer Invert(VoiceContainer voiceContainer, bool invertDefaultVoices, ModKey currentMod)
    {
        // Every voice type standing for all its speakers (rather than a copy of every speaker), made the speakers it
        // stands for only where some are taken out.
        VoiceContainer baseVoices;
        if (invertDefaultVoices)
        {
            baseVoices = (VoiceContainer)GetDefaultVoices(currentMod).Clone();
        }
        else
        {
            baseVoices = new VoiceContainer(_speakersByVoiceType.Keys);
        }

        baseVoices.Remove(voiceContainer, voiceType => _speakersByVoiceType.TryGetValue(voiceType, out var speakers) ? speakers : []);
        return baseVoices;
    }

    private IEnumerable<string> GetVoiceTypes(FormKey speaker)
    {
        return _speakerVoices.TryGetValue(speaker, out var speakerVoiceTypes) ? speakerVoiceTypes : [];
    }

    #region Voice Parser
    private HashSet<string> GetVoiceTypes(INpcGetter npc)
    {
        if (_speakerVoices.TryGetValue(npc.FormKey, out var speakerVoiceTypes)) return speakerVoiceTypes;

        //Check voice type
        if (!npc.Voice.IsNull)
        {
            var voiceType = npc.Voice.TryResolve(_formLinkCache);
            if (voiceType is { EditorID: {} })
            {
                return new HashSet<string> { voiceType.EditorID };
            }
        }

        //Check template
        if (!npc.Template.IsNull && (npc.Configuration.TemplateFlags & NpcConfiguration.TemplateFlag.Traits) != 0)
        {
            return GetVoiceTypes(npc.Template).ToHashSet();
        }

        return new HashSet<string>();
    }

    private HashSet<string> GetVoiceTypes(IFormLinkGetter<INpcSpawnGetter> npcSpawn) =>
        npcSpawn.IsNull ? new HashSet<string>() : Remembered(_spawnVoiceTypes, npcSpawn.FormKey, () => SpawnVoiceTypes(npcSpawn));

    private HashSet<string> SpawnVoiceTypes(IFormLinkGetter<INpcSpawnGetter> npcSpawn)
    {

        //NPC
        var npc = npcSpawn.TryResolve<INpcGetter>(_formLinkCache);
        if (npc != null)
        {
            return GetVoiceTypes(npc);
        }

        //Levelled NPC
        var leveledNpc = npcSpawn.TryResolve<ILeveledNpcGetter>(_formLinkCache);
        if (leveledNpc is { Entries: {} })
        {
            return leveledNpc.Entries
                .Select(entry => entry.Data?.Reference).NotNull()
                .SelectMany(GetVoiceTypes).ToHashSet();
        }

        return new HashSet<string>();
    }

    private HashSet<string> GetVoiceTypes(ITalkingActivatorGetter talkingActivator)
    {
        if (_speakerVoices.TryGetValue(talkingActivator.FormKey, out var speakerVoiceTypes)) return speakerVoiceTypes;

        if (!talkingActivator.Voice.IsNull)
        {
            var voiceTypeGetter = talkingActivator.Voice.TryResolve(_formLinkCache);
            if (voiceTypeGetter is { EditorID: not null })
            {
                return new HashSet<string> { voiceTypeGetter.EditorID };
            }
        }

        return new HashSet<string>();
    }
    #endregion

    #region Faction Parser
    private HashSet<FormKey> GetFactions(INpcGetter npc)
    {
        if ((npc.Configuration.TemplateFlags & NpcConfiguration.TemplateFlag.Factions) == 0)
        {
            return npc.Factions.Where(f => !f.Faction.IsNull).Select(f => f.Faction.FormKey).ToHashSet();
        }

        return npc.Template.IsNull ? new HashSet<FormKey>() : GetFactions(npc.Template);

    }

    private HashSet<FormKey> GetFactions(IFormLinkGetter<INpcSpawnGetter> npcTemplate) =>
        npcTemplate.IsNull ? new HashSet<FormKey>() : Remembered(_spawnFactions, npcTemplate.FormKey, () => SpawnFactions(npcTemplate));

    private HashSet<FormKey> SpawnFactions(IFormLinkGetter<INpcSpawnGetter> npcTemplate)
    {

        //NPC
        var npc = npcTemplate.TryResolve<INpcGetter>(_formLinkCache);
        if (npc != null) return GetFactions(npc);

        //Levelled NPC
        var leveledNpc = npcTemplate.TryResolve<ILeveledNpcGetter>(_formLinkCache);
        if (leveledNpc is { Entries: {} })
        {
            return leveledNpc.Entries
                .Select(entry => entry.Data?.Reference).NotNull()
                .SelectMany(GetFactions).ToHashSet();
        }

        return new HashSet<FormKey>();
    }
    #endregion

    #region Class Parser
    private HashSet<FormKey> GetClasses(INpcGetter npc)
    {
        if ((npc.Configuration.TemplateFlags & NpcConfiguration.TemplateFlag.Stats) == 0 && !npc.Class.IsNull)
        {
            return new HashSet<FormKey> { npc.Class.FormKey };
        }

        return npc.Template.IsNull ? new HashSet<FormKey>() : GetClasses(npc.Template);

    }

    private HashSet<FormKey> GetClasses(IFormLinkGetter<INpcSpawnGetter> npcTemplate) =>
        npcTemplate.IsNull ? new HashSet<FormKey>() : Remembered(_spawnClasses, npcTemplate.FormKey, () => SpawnClasses(npcTemplate));

    private HashSet<FormKey> SpawnClasses(IFormLinkGetter<INpcSpawnGetter> npcTemplate)
    {

        //NPC
        var npc = npcTemplate.TryResolve<INpcGetter>(_formLinkCache);
        if (npc != null) return GetClasses(npc);

        //Levelled NPC
        var leveledNpc = npcTemplate.TryResolve<ILeveledNpcGetter>(_formLinkCache);
        if (leveledNpc is { Entries: {} })
        {
            return leveledNpc.Entries
                .Select(entry => entry.Data?.Reference).NotNull()
                .SelectMany(GetClasses).ToHashSet();
        }

        return new HashSet<FormKey>();
    }
    #endregion

    #region Gender Parser
    private HashSet<MaleFemaleGender> GetGenders(INpcGetter npc)
    {
        if ((npc.Configuration.TemplateFlags & NpcConfiguration.TemplateFlag.Traits) == 0)
        {
            return [(npc.Configuration.Flags & NpcConfiguration.Flag.Female) != 0 ? MaleFemaleGender.Female : MaleFemaleGender.Male];
        }

        return [];
    }
    #endregion

    #region Race Parser
    private HashSet<FormKey> GetRaces(INpcGetter npc)
    {
        if ((npc.Configuration.TemplateFlags & NpcConfiguration.TemplateFlag.Traits) == 0 && !npc.Race.IsNull)
        {
            return new HashSet<FormKey> { npc.Race.FormKey };
        }

        return [];
    }
    #endregion
}