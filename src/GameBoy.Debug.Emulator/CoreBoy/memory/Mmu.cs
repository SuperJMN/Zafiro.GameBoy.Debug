using System.Collections.Generic;

namespace CoreBoy.memory
{
    public class Mmu : IAddressSpace
    {
        private static readonly IAddressSpace Void = new VoidAddressSpace();
        private readonly List<IAddressSpace> _spaces = new List<IAddressSpace>();

        public void AddAddressSpace(IAddressSpace space) => _spaces.Add(space);
        public bool Accepts(int address) => true;

        // Added for gameboy-debug-mcp: observe every write (address, value) to support
        // find_last_writer / trace_until_write debugging tools.
        public System.Action<int, int> WriteObserver { get; set; }

        // Added for gameboy-debug-mcp: capture authoritative state immediately before
        // selected video writes, paired with WriteObserver's post-write callback.
        public System.Action<int, int> BeforeWriteObserver { get; set; }

        // Added for gameboy-debug-mcp: observe reads to support memory watchpoints.
        public System.Action<int> ReadObserver { get; set; }

        public void SetByte(int address, int value)
        {
            BeforeWriteObserver?.Invoke(address, value);
            GetSpace(address).SetByte(address, value);
            WriteObserver?.Invoke(address, value);
        }

        public int GetByte(int address)
        {
            var value = GetSpace(address).GetByte(address);
            ReadObserver?.Invoke(address);
            return value;
        }

        private IAddressSpace GetSpace(int address)
        {
            foreach (var s in _spaces)
            {
                if (s.Accepts(address))
                {
                    return s;
                }
            }

            return Void;
        }

    }
}
