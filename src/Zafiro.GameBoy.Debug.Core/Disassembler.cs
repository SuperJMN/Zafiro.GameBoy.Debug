using System.Globalization;

namespace Zafiro.GameBoy.Debug.Core;

public static class Disassembler
{
    public static DisassembledInstruction DisassembleOne(
        ushort address,
        Func<ushort, byte> readByte,
        Func<ushort, string?>? resolveSymbol = null)
    {
        ArgumentNullException.ThrowIfNull(readByte);

        var decoded = Decode(address, readByte);
        var bytes = Enumerable.Range(0, decoded.Length).Select(offset => readByte(Add(address, offset)));

        return new DisassembledInstruction(
            Hex.FormatWord(address),
            Hex.FormatBytes(bytes),
            decoded.Text,
            resolveSymbol?.Invoke(address));
    }

    public static IReadOnlyList<DisassembledInstruction> Disassemble(
        ushort address,
        int count,
        Func<ushort, byte> readByte,
        Func<ushort, string?>? resolveSymbol = null)
    {
        ArgumentNullException.ThrowIfNull(readByte);

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Instruction count cannot be negative.");
        }

        var instructions = new List<DisassembledInstruction>(count);
        var currentAddress = address;

        for (var i = 0; i < count; i++)
        {
            var decoded = Decode(currentAddress, readByte);
            var bytes = Enumerable.Range(0, decoded.Length).Select(offset => readByte(Add(currentAddress, offset)));

            instructions.Add(new DisassembledInstruction(
                Hex.FormatWord(currentAddress),
                Hex.FormatBytes(bytes),
                decoded.Text,
                resolveSymbol?.Invoke(currentAddress)));

            currentAddress = Add(currentAddress, decoded.Length);
        }

        return instructions;
    }

    private static DecodedInstruction Decode(ushort address, Func<ushort, byte> readByte)
    {
        var opcode = readByte(address);

        if (opcode == 0xCB)
        {
            return new DecodedInstruction(DecodeCb(readByte(Add(address, 1))), 2);
        }

        if (IsIllegalOpcode(opcode))
        {
            return new DecodedInstruction($"DB {Hex.FormatByte(opcode)}", 1);
        }

        var x = opcode >> 6;
        var y = (opcode >> 3) & 0b111;
        var z = opcode & 0b111;
        var p = y >> 1;
        var q = y & 1;

        return x switch
        {
            0 => DecodeBlock0(address, readByte, y, z, p, q),
            1 => DecodeBlock1(y, z, opcode),
            2 => new DecodedInstruction(FormatAlu(y, RegisterName(z)), 1),
            3 => DecodeBlock3(address, readByte, y, z, p, q),
            _ => throw new InvalidOperationException($"Invalid opcode block {x}."),
        };
    }

    private static DecodedInstruction DecodeBlock0(
        ushort address,
        Func<ushort, byte> readByte,
        int y,
        int z,
        int p,
        int q)
    {
        return z switch
        {
            0 => DecodeBlock0Z0(address, readByte, y),
            1 => q == 0
                ? new DecodedInstruction($"LD {RegisterPair(p)}, {FormatImmediateWord(address, readByte)}", 3)
                : new DecodedInstruction($"ADD HL, {RegisterPair(p)}", 1),
            2 => new DecodedInstruction(DecodeIndirectLoad(y), 1),
            3 => new DecodedInstruction($"{(q == 0 ? "INC" : "DEC")} {RegisterPair(p)}", 1),
            4 => new DecodedInstruction($"INC {RegisterName(y)}", 1),
            5 => new DecodedInstruction($"DEC {RegisterName(y)}", 1),
            6 => new DecodedInstruction($"LD {RegisterName(y)}, {FormatImmediateByte(address, readByte)}", 2),
            7 => new DecodedInstruction(AccumulatorOperation(y), 1),
            _ => throw new InvalidOperationException($"Invalid opcode field z={z}."),
        };
    }

    private static DecodedInstruction DecodeBlock0Z0(ushort address, Func<ushort, byte> readByte, int y)
    {
        return y switch
        {
            0 => new DecodedInstruction("NOP", 1),
            1 => new DecodedInstruction($"LD ({FormatImmediateWord(address, readByte)}), SP", 3),
            2 => new DecodedInstruction($"STOP {FormatImmediateByte(address, readByte)}", 2),
            3 => new DecodedInstruction($"JR {FormatRelativeTarget(address, readByte)}", 2),
            4 => new DecodedInstruction($"JR NZ, {FormatRelativeTarget(address, readByte)}", 2),
            5 => new DecodedInstruction($"JR Z, {FormatRelativeTarget(address, readByte)}", 2),
            6 => new DecodedInstruction($"JR NC, {FormatRelativeTarget(address, readByte)}", 2),
            7 => new DecodedInstruction($"JR C, {FormatRelativeTarget(address, readByte)}", 2),
            _ => throw new InvalidOperationException($"Invalid opcode field y={y}."),
        };
    }

    private static DecodedInstruction DecodeBlock1(int y, int z, byte opcode)
    {
        return opcode == 0x76
            ? new DecodedInstruction("HALT", 1)
            : new DecodedInstruction($"LD {RegisterName(y)}, {RegisterName(z)}", 1);
    }

    private static DecodedInstruction DecodeBlock3(
        ushort address,
        Func<ushort, byte> readByte,
        int y,
        int z,
        int p,
        int q)
    {
        return z switch
        {
            0 => DecodeBlock3Z0(address, readByte, y),
            1 => q == 0
                ? new DecodedInstruction($"POP {StackRegisterPair(p)}", 1)
                : DecodeBlock3Z1(p),
            2 => DecodeBlock3Z2(address, readByte, y),
            3 => DecodeBlock3Z3(address, readByte, y),
            4 => new DecodedInstruction($"CALL {Condition(y)}, {FormatImmediateWord(address, readByte)}", 3),
            5 => q == 0
                ? new DecodedInstruction($"PUSH {StackRegisterPair(p)}", 1)
                : new DecodedInstruction($"CALL {FormatImmediateWord(address, readByte)}", 3),
            6 => new DecodedInstruction(FormatAlu(y, FormatImmediateByte(address, readByte)), 2),
            7 => new DecodedInstruction($"RST {Hex.FormatByte((byte)(y * 8))}", 1),
            _ => throw new InvalidOperationException($"Invalid opcode field z={z}."),
        };
    }

    private static DecodedInstruction DecodeBlock3Z0(ushort address, Func<ushort, byte> readByte, int y)
    {
        return y switch
        {
            <= 3 => new DecodedInstruction($"RET {Condition(y)}", 1),
            4 => new DecodedInstruction($"LDH ({FormatHighRamAddress(readByte(Add(address, 1)))}), A", 2),
            5 => new DecodedInstruction($"ADD SP, {FormatSignedImmediate(readByte(Add(address, 1)))}", 2),
            6 => new DecodedInstruction($"LDH A, ({FormatHighRamAddress(readByte(Add(address, 1)))})", 2),
            7 => new DecodedInstruction($"LD HL, SP{FormatSignedOffset(readByte(Add(address, 1)))}", 2),
            _ => throw new InvalidOperationException($"Invalid opcode field y={y}."),
        };
    }

    private static DecodedInstruction DecodeBlock3Z1(int p)
    {
        return p switch
        {
            0 => new DecodedInstruction("RET", 1),
            1 => new DecodedInstruction("RETI", 1),
            2 => new DecodedInstruction("JP HL", 1),
            3 => new DecodedInstruction("LD SP, HL", 1),
            _ => throw new InvalidOperationException($"Invalid opcode field p={p}."),
        };
    }

    private static DecodedInstruction DecodeBlock3Z2(ushort address, Func<ushort, byte> readByte, int y)
    {
        return y switch
        {
            <= 3 => new DecodedInstruction($"JP {Condition(y)}, {FormatImmediateWord(address, readByte)}", 3),
            4 => new DecodedInstruction("LD (0xFF00+C), A", 1),
            5 => new DecodedInstruction($"LD ({FormatImmediateWord(address, readByte)}), A", 3),
            6 => new DecodedInstruction("LD A, (0xFF00+C)", 1),
            7 => new DecodedInstruction($"LD A, ({FormatImmediateWord(address, readByte)})", 3),
            _ => throw new InvalidOperationException($"Invalid opcode field y={y}."),
        };
    }

    private static DecodedInstruction DecodeBlock3Z3(ushort address, Func<ushort, byte> readByte, int y)
    {
        return y switch
        {
            0 => new DecodedInstruction($"JP {FormatImmediateWord(address, readByte)}", 3),
            1 => new DecodedInstruction(DecodeCb(readByte(Add(address, 1))), 2),
            6 => new DecodedInstruction("DI", 1),
            7 => new DecodedInstruction("EI", 1),
            _ => new DecodedInstruction($"DB {Hex.FormatByte(readByte(address))}", 1),
        };
    }

    private static string DecodeCb(byte opcode)
    {
        var x = opcode >> 6;
        var y = (opcode >> 3) & 0b111;
        var z = opcode & 0b111;
        var register = RegisterName(z);

        return x switch
        {
            0 => $"{RotateOperation(y)} {register}",
            1 => $"BIT {y}, {register}",
            2 => $"RES {y}, {register}",
            3 => $"SET {y}, {register}",
            _ => throw new InvalidOperationException($"Invalid CB opcode block {x}."),
        };
    }

    private static string DecodeIndirectLoad(int y)
    {
        return y switch
        {
            0 => "LD (BC), A",
            1 => "LD A, (BC)",
            2 => "LD (DE), A",
            3 => "LD A, (DE)",
            4 => "LD (HL+), A",
            5 => "LD A, (HL+)",
            6 => "LD (HL-), A",
            7 => "LD A, (HL-)",
            _ => throw new InvalidOperationException($"Invalid opcode field y={y}."),
        };
    }

    private static string RegisterName(int index)
    {
        return index switch
        {
            0 => "B",
            1 => "C",
            2 => "D",
            3 => "E",
            4 => "H",
            5 => "L",
            6 => "(HL)",
            7 => "A",
            _ => throw new InvalidOperationException($"Invalid register index {index}."),
        };
    }

    private static string RegisterPair(int index)
    {
        return index switch
        {
            0 => "BC",
            1 => "DE",
            2 => "HL",
            3 => "SP",
            _ => throw new InvalidOperationException($"Invalid register pair index {index}."),
        };
    }

    private static string StackRegisterPair(int index)
    {
        return index switch
        {
            0 => "BC",
            1 => "DE",
            2 => "HL",
            3 => "AF",
            _ => throw new InvalidOperationException($"Invalid stack register pair index {index}."),
        };
    }

    private static string Condition(int index)
    {
        return index switch
        {
            0 => "NZ",
            1 => "Z",
            2 => "NC",
            3 => "C",
            _ => throw new InvalidOperationException($"Invalid condition index {index}."),
        };
    }

    private static string AccumulatorOperation(int index)
    {
        return index switch
        {
            0 => "RLCA",
            1 => "RRCA",
            2 => "RLA",
            3 => "RRA",
            4 => "DAA",
            5 => "CPL",
            6 => "SCF",
            7 => "CCF",
            _ => throw new InvalidOperationException($"Invalid accumulator operation index {index}."),
        };
    }

    private static string AluOperation(int index)
    {
        return index switch
        {
            0 => "ADD A,",
            1 => "ADC A,",
            2 => "SUB",
            3 => "SBC A,",
            4 => "AND",
            5 => "XOR",
            6 => "OR",
            7 => "CP",
            _ => throw new InvalidOperationException($"Invalid ALU operation index {index}."),
        };
    }

    private static string RotateOperation(int index)
    {
        return index switch
        {
            0 => "RLC",
            1 => "RRC",
            2 => "RL",
            3 => "RR",
            4 => "SLA",
            5 => "SRA",
            6 => "SWAP",
            7 => "SRL",
            _ => throw new InvalidOperationException($"Invalid rotate operation index {index}."),
        };
    }

    private static string FormatAlu(int index, string operand)
    {
        return $"{AluOperation(index)} {operand}";
    }

    private static string FormatImmediateByte(ushort address, Func<ushort, byte> readByte)
    {
        return Hex.FormatByte(readByte(Add(address, 1)));
    }

    private static string FormatImmediateWord(ushort address, Func<ushort, byte> readByte)
    {
        return Hex.FormatWord(ReadWord(address, readByte));
    }

    private static ushort ReadWord(ushort address, Func<ushort, byte> readByte)
    {
        var low = readByte(Add(address, 1));
        var high = readByte(Add(address, 2));

        return (ushort)(low | high << 8);
    }

    private static string FormatRelativeTarget(ushort address, Func<ushort, byte> readByte)
    {
        var offset = unchecked((sbyte)readByte(Add(address, 1)));
        return Hex.FormatWord((ushort)(address + 2 + offset));
    }

    private static string FormatHighRamAddress(byte offset)
    {
        return Hex.FormatWord((ushort)(0xFF00 + offset));
    }

    private static string FormatSignedImmediate(byte value)
    {
        return unchecked((sbyte)value).ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatSignedOffset(byte value)
    {
        var signedValue = unchecked((sbyte)value);
        return signedValue >= 0
            ? "+" + signedValue.ToString(CultureInfo.InvariantCulture)
            : signedValue.ToString(CultureInfo.InvariantCulture);
    }

    private static bool IsIllegalOpcode(byte opcode)
    {
        return opcode is 0xD3 or 0xDB or 0xDD or 0xE3 or 0xE4 or 0xEB or 0xEC or 0xED or 0xF4 or 0xFC or 0xFD;
    }

    private static ushort Add(ushort address, int offset)
    {
        return (ushort)(address + offset);
    }

    private sealed record DecodedInstruction(string Text, int Length);
}
