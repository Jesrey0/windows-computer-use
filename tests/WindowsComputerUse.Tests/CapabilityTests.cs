using System.Text.Json;
using WindowsComputerUse.Core;
using Xunit;

namespace WindowsComputerUse.Tests;

public class CapabilityTests
{
    [Fact]
    public void KeySequencesRejectMalformedAndUnboundedRequests()
    {
        Assert.False(InputLimits.ValidSequence(null));
        Assert.False(InputLimits.ValidSequence(new([])));
        Assert.False(InputLimits.ValidSequence(new([new(0, [])])));
        Assert.False(InputLimits.ValidSequence(new([new(255, [])])));
        Assert.False(InputLimits.ValidSequence(new([new(65, null)])));
        Assert.False(InputLimits.ValidSequence(new([new(65, [0x11, 0x11])])));
        Assert.False(InputLimits.ValidSequence(new([new(65, [0x5B])])));
        Assert.False(InputLimits.ValidSequence(new([new(0x10, [0x10])])));
        Assert.False(InputLimits.ValidSequence(new(Enumerable.Repeat(new KeyChord(65, []),
            InputLimits.MaxKeySequence + 1).ToArray())));
        Assert.True(InputLimits.ValidSequence(new([new(65, [0x12, 0x11, 0x10])])));
    }

    [Fact]
    public void ModifierOrderIsCanonicalAndReleasesInReverse()
    {
        var chord = new KeyChord(65, [0x12, 0x10, 0x11]);
        Assert.Equal(new (ushort, bool)[] { (0x11, true), (0x10, true), (0x12, true),
            (65, true), (65, false), (0x12, false), (0x10, false), (0x11, false) },
            NativeInput.KeyOrder(chord));
    }

    [Fact]
    public void PointerAndFindLimitsAreValidated()
    {
        Assert.True(InputLimits.ValidPointerClick(new(0, 0, "right", 2)));
        Assert.False(InputLimits.ValidPointerClick(new(0, 0, "middle", 1)));
        Assert.False(InputLimits.ValidPointerClick(new(0, 0, "left", 3)));
        Assert.True(InputLimits.ValidScroll(new(-InputLimits.MaxWheelDelta, 0, 0)));
        Assert.False(InputLimits.ValidScroll(new(0)));
        Assert.False(InputLimits.ValidScroll(new(InputLimits.MaxWheelDelta + 1)));
        Assert.False(InputLimits.ValidScroll(new(120, 0)));
        Assert.True(InputLimits.ValidFind(new("0x1", new("id"))));
        Assert.False(InputLimits.ValidFind(new("0x1", new("id"), MaxResults: 0)));
        Assert.False(InputLimits.ValidFind(new("0x1", new("id"), MaxNodes: 0)));
        Assert.False(InputLimits.ValidFind(new("0x1", new("id"), MaxDepth: -1)));
    }

    [Fact]
    public void CapabilitiesAreCompactAndAdvertiseActualLimits()
    {
        var data = Capabilities.Current;
        Assert.Equal("v1", data.ApiVersion);
        Assert.Contains("find-elements", data.SemanticActions);
        Assert.Contains("close", data.WindowActions);
        Assert.Equal(InputLimits.MaxFindResults, data.Limits.MaxFindResults);
        Assert.Equal(InputLimits.MaxKeySequence, data.Limits.MaxKeySequence);
        Assert.Contains("drag", data.InputActions);
        Assert.Equal(InputLimits.MaxDragPoints, data.Limits.MaxDragPoints);
        Assert.Equal(InputLimits.MaxDragDurationMs, data.Limits.MaxDragDurationMs);
        Assert.Equal(InputLimits.MaxDragSteps, data.Limits.MaxDragSteps);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"locatorFields\":[\"automationId\",\"name\",\"controlType\"]", json);
        Assert.Contains("\"maxDragPoints\":128", json);
        Assert.Contains("\"maxDragDurationMs\":5000", json);
        Assert.Contains("\"maxDragSteps\":512", json);
    }
}
