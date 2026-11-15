using MapVote.Runtime;

namespace MapVote.Tests;

public class SlotActionTokensTests
{
    [Fact]
    public void FirstUse_DoesNotThrow()
    {
        SlotActionTokens tokens = new();

        int token = tokens.Next(0);

        Assert.Equal(1, token);
        Assert.True(tokens.IsCurrent(0, token));
    }

    [Fact]
    public void NewerAction_SupersedesOlderToken()
    {
        SlotActionTokens tokens = new();
        int first = tokens.Next(4);
        int second = tokens.Next(4);

        Assert.Equal(first + 1, second);
        Assert.False(tokens.IsCurrent(4, first));
        Assert.True(tokens.IsCurrent(4, second));
    }

    [Fact]
    public void Slots_AreIndependent()
    {
        SlotActionTokens tokens = new();
        int slotOne = tokens.Next(1);

        int slotTwo = tokens.Next(2);

        Assert.Equal(1, slotTwo);
        Assert.True(tokens.IsCurrent(1, slotOne));
        Assert.True(tokens.IsCurrent(2, slotTwo));
    }

    [Fact]
    public void Clear_InvalidatesOnlyThatSlot()
    {
        SlotActionTokens tokens = new();
        int first = tokens.Next(0);
        int second = tokens.Next(1);

        tokens.Clear(0);

        Assert.False(tokens.IsCurrent(0, first));
        Assert.True(tokens.IsCurrent(1, second));
        Assert.Equal(1, tokens.Next(0));
    }

    [Fact]
    public void ClearAll_InvalidatesEverything()
    {
        SlotActionTokens tokens = new();
        int first = tokens.Next(0);
        int second = tokens.Next(1);

        tokens.ClearAll();

        Assert.False(tokens.IsCurrent(0, first));
        Assert.False(tokens.IsCurrent(1, second));
        Assert.Equal(1, tokens.Next(0));
    }

    [Fact]
    public void UnknownSlotOrToken_IsNotCurrent()
    {
        SlotActionTokens tokens = new();
        int token = tokens.Next(3);

        Assert.False(tokens.IsCurrent(2, token));
        Assert.False(tokens.IsCurrent(3, token + 1));
    }
}
