using WindowsComputerUse.Core;
using System.Text.Json;
using System.Runtime.InteropServices;
using FlaUI.Core.Exceptions;
using Xunit;

namespace WindowsComputerUse.Tests;

public class ContractsTests
{
    [Theory]
    [InlineData("0x1", true)]
    [InlineData("ABCD", true)]
    [InlineData("0x0", false)]
    [InlineData("0xBAD!", false)]
    [InlineData("0xFFFFFFFFFFFFFFFF", false)]
    public void HandlesAreCanonicalHex(string input, bool valid) =>
        Assert.Equal(valid, WindowHandle.TryParse(input, out _));

    [Fact]
    public void HandleRoundTrip()
    {
        Assert.True(WindowHandle.TryParse("0xABCD", out var handle));
        Assert.Equal("0xABCD", WindowHandle.Format(handle));
    }

    [Fact]
    public void TreeAndPathAreBounded()
    {
        Assert.True(InputLimits.ValidTree(0, 1));
        Assert.True(InputLimits.ValidTree(InputLimits.MaxDepth, InputLimits.MaxNodes));
        Assert.False(InputLimits.ValidTree(-1, 1));
        Assert.False(InputLimits.ValidTree(2, InputLimits.MaxNodes + 1));
        Assert.True(InputLimits.ValidPath([]));
        Assert.False(InputLimits.ValidPath(null));
        Assert.False(InputLimits.ValidPath([-1]));
        Assert.False(InputLimits.ValidPath(Enumerable.Repeat(0, InputLimits.MaxPath + 1).ToArray()));
    }

    [Theory]
    [InlineData("hello", null, true)]
    [InlineData(null, (ushort)13, true)]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("a", (ushort)13, false)]
    public void KeysAreExclusive(string? text, ushort? vk, bool valid) =>
        Assert.Equal(valid, InputLimits.ValidKeys(new KeysRequest(text, vk)));

    [Fact]
    public void BearerRequiresExactToken()
    {
        Assert.True(TokenStore.Matches("Bearer secret", "secret"));
        Assert.False(TokenStore.Matches("bearer secret", "secret"));
        Assert.False(TokenStore.Matches("Bearer secrex", "secret"));
    }

    [Fact]
    public void UnsupportedPropertyDoesNotAbortTreeNode()
    {
        var truncated = false;
        var name = UiTreeReader.Read<string?>(
            () => throw new PropertyNotSupportedException((FlaUI.Core.Identifiers.PropertyId)null!),
            null, () => truncated = true);
        Assert.True(truncated);
        Assert.Null(name);
        var element = new ElementDto([0], name, "Unknown", null, null, false, false, []);
        Assert.Contains("\"bounds\":null", JsonSerializer.Serialize(element, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void ProviderFailuresAreLocalButProgrammingErrorsAreNotHidden()
    {
        var failures = 0;
        foreach (var error in new Exception[] { new COMException(), new FlaUIException(),
                     new PatternNotSupportedException(), new InvalidOperationException() })
            Assert.Equal("unavailable", UiTreeReader.Read<string>(() => throw error,
                "unavailable", () => failures++));
        Assert.Equal(4, failures);
        Assert.Throws<FormatException>(() => UiTreeReader.Read<string>(
            () => throw new FormatException(), "unavailable", () => failures++));
        Assert.Equal(4, failures);
    }
}
