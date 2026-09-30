using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Testing;
using Shouldly;
using Xunit;

namespace Mutagen.Bethesda.UnitTests.Plugins.Records;

/// <summary>A quest's topics are nested in it, and their responses in them: each has its own parent.</summary>
public class Fallout4RecordBatchesTests
{
    [Fact]
    public void Fallout4RecordsNestedTwiceHaveTheirOwnParent()
    {
        var mod = new Fallout4Mod(TestConstants.PluginModKey, Fallout4Release.Fallout4);
        var quest = mod.Quests.AddNew("Quest");
        var topic = new DialogTopic(mod, "Topic");
        var response = new DialogResponses(mod);
        topic.Responses.Add(response);
        quest.DialogTopics.Add(topic);
        var branch = new DialogBranch(mod, "Branch");
        quest.DialogBranches.Add(branch);
        var scene = new Scene(mod, "Scene");
        quest.Scenes.Add(scene);

        var entries = mod.EnumerateMajorRecordBatches().SelectMany(b => b).ToList();
        entries.Select(e => e.Record.FormKey).Order().ShouldBe(mod.EnumerateMajorRecords().Select(r => r.FormKey).Order());
        var parents = entries.ToDictionary(e => e.Record.FormKey, e => e.Parent?.FormKey);
        parents[quest.FormKey].ShouldBeNull();
        parents[topic.FormKey].ShouldBe(quest.FormKey);
        parents[branch.FormKey].ShouldBe(quest.FormKey);
        parents[scene.FormKey].ShouldBe(quest.FormKey);
        parents[response.FormKey].ShouldBe(topic.FormKey);
    }
}