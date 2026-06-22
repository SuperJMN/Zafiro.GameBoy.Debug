using GameBoy.Debug.Core;

namespace GameBoy.Debug.Tests;

public sealed class BreakpointConditionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_condition_always_breaks(string? expression)
    {
        var parsed = BreakpointCondition.TryParse(expression, out var condition, out var errorMessage);

        Assert.True(parsed, errorMessage);
        Assert.Null(condition);
    }

    [Theory]
    [InlineData("A == 0x10", true)]
    [InlineData("B != 6", true)]
    [InlineData("C < 7", true)]
    [InlineData("D <= 7", true)]
    [InlineData("E > 7", true)]
    [InlineData("HL >= 0xC000", true)]
    [InlineData("SP < 0xB000", false)]
    [InlineData("PC == 0x0150", true)]
    public void Register_conditions_compare_current_cpu_state(string expression, bool expected)
    {
        var condition = Parse(expression);
        var context = new TestConditionContext(Registers);

        var result = condition.Evaluate(context);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("LY >= 0x90", true)]
    [InlineData("STAT == 0x85", true)]
    [InlineData("SCX == 4", true)]
    [InlineData("OBP0 != 0", true)]
    public void Ppu_alias_conditions_read_matching_io_registers(string expression, bool expected)
    {
        var condition = Parse(expression);
        var context = new TestConditionContext(
            Registers,
            new Dictionary<ushort, byte>
            {
                [0xFF41] = 0x85,
                [0xFF43] = 4,
                [0xFF44] = 0x90,
                [0xFF48] = 0xFC,
            });

        var result = condition.Evaluate(context);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData("[0xFF80] == 1", true)]
    [InlineData("[65408] == 1", true)]
    [InlineData("[HL] < 4", true)]
    [InlineData("[SP] >= 0x10", true)]
    public void Memory_conditions_read_one_byte_from_constant_or_register_address(string expression, bool expected)
    {
        var condition = Parse(expression);
        var context = new TestConditionContext(
            Registers,
            new Dictionary<ushort, byte>
            {
                [0xFF80] = 1,
                [0xC000] = 3,
                [0xBFFE] = 0x10,
            });

        var result = condition.Evaluate(context);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Parser_is_case_insensitive_and_allows_extra_whitespace()
    {
        var condition = Parse("  a   ==   0x10  ");
        var context = new TestConditionContext(Registers);

        var result = condition.Evaluate(context);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value);
    }

    [Theory]
    [InlineData("A = 1")]
    [InlineData("Q == 1")]
    [InlineData("[A] == 1")]
    [InlineData("[0x10000] == 1")]
    [InlineData("A == -1")]
    [InlineData("A && 1")]
    [InlineData("[HL == 1")]
    [InlineData("A == 0xGG")]
    [InlineData("A ==")]
    public void Invalid_conditions_are_rejected(string expression)
    {
        var parsed = BreakpointCondition.TryParse(expression, out var condition, out var errorMessage);

        Assert.False(parsed);
        Assert.Null(condition);
        Assert.False(string.IsNullOrWhiteSpace(errorMessage));
    }

    [Fact]
    public void Memory_read_failures_are_returned_as_evaluation_errors()
    {
        var condition = Parse("[0xC000] == 1");
        var context = new TestConditionContext(Registers);

        var result = condition.Evaluate(context);

        Assert.False(result.IsSuccess);
        Assert.Equal("read_memory_failed", result.Error?.Code);
    }

    private static BreakpointCondition Parse(string expression)
    {
        var parsed = BreakpointCondition.TryParse(expression, out var condition, out var errorMessage);
        Assert.True(parsed, errorMessage);
        return Assert.IsType<BreakpointCondition>(condition);
    }

    private static readonly CpuRegisters Registers = new(
        "0x10B0",
        "0x0506",
        "0x0708",
        "0xC000",
        "0xBFFE",
        "0x0150",
        "0x10",
        "0xB0",
        "0x05",
        "0x06",
        "0x07",
        "0x08",
        "0xC0",
        "0x00",
        false,
        false);

    private sealed class TestConditionContext(
        CpuRegisters registers,
        IReadOnlyDictionary<ushort, byte>? memory = null) : IBreakpointConditionContext
    {
        public CpuRegisters Registers { get; } = registers;

        public DebugResult<byte> ReadByte(ushort address)
        {
            return memory is not null && memory.TryGetValue(address, out var value)
                ? DebugResult<byte>.Success(value)
                : DebugResult<byte>.Failure("read_memory_failed", $"No byte at {Hex.FormatWord(address)}.");
        }
    }
}
