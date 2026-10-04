using System.Runtime.InteropServices;

namespace Zafiro.GameBoy.Debug.SameBoy;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRegisters
{
    public ushort Af;
    public ushort Bc;
    public ushort De;
    public ushort Hl;
    public ushort Sp;
    public ushort Pc;
    public byte A;
    public byte F;
    public byte B;
    public byte C;
    public byte D;
    public byte E;
    public byte H;
    public byte L;
    [MarshalAs(UnmanagedType.I1)]
    public bool Ime;
    [MarshalAs(UnmanagedType.I1)]
    public bool Halted;
}
