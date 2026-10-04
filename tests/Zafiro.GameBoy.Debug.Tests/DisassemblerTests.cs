using Zafiro.GameBoy.Debug.Core;

namespace Zafiro.GameBoy.Debug.Tests;

public sealed class DisassemblerTests
{
    [Fact]
    public void Nop_decodes_as_single_byte_instruction()
    {
        var instruction = DisassembleOne(0x0100, [0x00]);

        Assert.Equal("0x0100", instruction.Address);
        Assert.Equal("00", instruction.Bytes);
        Assert.Equal("NOP", instruction.Text);
        Assert.Null(instruction.Symbol);
    }

    [Fact]
    public void Sixteen_bit_immediate_operands_are_little_endian()
    {
        var instruction = DisassembleOne(0x0100, [0x21, 0x34, 0x12]);

        Assert.Equal("21 34 12", instruction.Bytes);
        Assert.Equal("LD HL, 0x1234", instruction.Text);
    }

    [Fact]
    public void Eight_bit_immediate_operands_are_formatted_as_hex_bytes()
    {
        var instruction = DisassembleOne(0x0100, [0x3E, 0x3F]);

        Assert.Equal("3E 3F", instruction.Bytes);
        Assert.Equal("LD A, 0x3F", instruction.Text);
    }

    [Fact]
    public void Absolute_jumps_render_their_target_address()
    {
        var instruction = DisassembleOne(0x0100, [0xC3, 0x50, 0x01]);

        Assert.Equal("C3 50 01", instruction.Bytes);
        Assert.Equal("JP 0x0150", instruction.Text);
    }

    [Fact]
    public void Calls_render_their_target_address()
    {
        var instruction = DisassembleOne(0x0100, [0xCD, 0x34, 0x12]);

        Assert.Equal("CD 34 12", instruction.Bytes);
        Assert.Equal("CALL 0x1234", instruction.Text);
    }

    [Theory]
    [InlineData(0x0100, 0x05, "JR 0x0107")]
    [InlineData(0x0100, 0xFE, "JR 0x0100")]
    public void Relative_jumps_render_the_resolved_target(ushort address, byte offset, string expectedText)
    {
        var instruction = DisassembleOne(address, [0x18, offset]);

        Assert.Equal("18 " + offset.ToString("X2"), instruction.Bytes);
        Assert.Equal(expectedText, instruction.Text);
    }

    [Fact]
    public void High_ram_immediate_addresses_are_resolved_to_ff00_page()
    {
        var instruction = DisassembleOne(0x0100, [0xE0, 0x47]);

        Assert.Equal("E0 47", instruction.Bytes);
        Assert.Equal("LDH (0xFF47), A", instruction.Text);
    }

    [Fact]
    public void Cb_prefixed_opcodes_decode_bit_operations()
    {
        var instruction = DisassembleOne(0x0100, [0xCB, 0x7C]);

        Assert.Equal("CB 7C", instruction.Bytes);
        Assert.Equal("BIT 7, H", instruction.Text);
    }

    [Fact]
    public void Restart_vectors_are_rendered_as_hex_bytes()
    {
        var instruction = DisassembleOne(0x0100, [0xFF]);

        Assert.Equal("FF", instruction.Bytes);
        Assert.Equal("RST 0x38", instruction.Text);
    }

    [Fact]
    public void Disassemble_advances_by_instruction_length_and_resolves_symbols()
    {
        byte[] bytes = [0x00, 0x21, 0x34, 0x12, 0xCB, 0x7C];

        var instructions = Disassembler.Disassemble(
            0x0100,
            3,
            ReadFrom(0x0100, bytes),
            address => address == 0x0101 ? "LoadHl" : null);

        Assert.Collection(
            instructions,
            first =>
            {
                Assert.Equal("0x0100", first.Address);
                Assert.Equal("00", first.Bytes);
                Assert.Equal("NOP", first.Text);
                Assert.Null(first.Symbol);
            },
            second =>
            {
                Assert.Equal("0x0101", second.Address);
                Assert.Equal("21 34 12", second.Bytes);
                Assert.Equal("LD HL, 0x1234", second.Text);
                Assert.Equal("LoadHl", second.Symbol);
            },
            third =>
            {
                Assert.Equal("0x0104", third.Address);
                Assert.Equal("CB 7C", third.Bytes);
                Assert.Equal("BIT 7, H", third.Text);
                Assert.Null(third.Symbol);
            });
    }

    [Fact]
    public void Illegal_opcodes_are_rendered_as_data_bytes()
    {
        var instruction = DisassembleOne(0x0100, [0xD3, 0x00, 0x00]);

        Assert.Equal("D3", instruction.Bytes);
        Assert.Equal("DB 0xD3", instruction.Text);
    }

    private static DisassembledInstruction DisassembleOne(ushort address, byte[] bytes)
    {
        return Disassembler.DisassembleOne(address, ReadFrom(address, bytes));
    }

    private static Func<ushort, byte> ReadFrom(ushort startAddress, byte[] bytes)
    {
        return address => bytes[(ushort)(address - startAddress)];
    }
}
