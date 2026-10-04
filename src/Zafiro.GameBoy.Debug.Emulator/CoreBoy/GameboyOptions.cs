using System;
using System.Collections.Generic;
using System.IO;

namespace CoreBoy
{
    public class GameboyOptions
    {
        public FileInfo? RomFile => string.IsNullOrWhiteSpace(Rom) ? null : new FileInfo(Rom);

        public string Rom { get; set; }

        public bool ForceDmg { get; set; }

        public bool ForceCgb { get; set; }

        public bool UseBootstrap { get; set; }

        public bool DisableBatterySaves { get; set; }

        public bool Debug { get; set; }

        public bool Headless { get; set; }

        public bool Interactive { get; set; }

        public bool ShowUi => !Headless;

        public bool IsSupportBatterySaves() => !DisableBatterySaves;

        public bool RomSpecified => !string.IsNullOrWhiteSpace(Rom);

        public GameboyOptions()
        {
        }

        public GameboyOptions(FileInfo romFile) : this(romFile, new string[0], new string[0])
        {
        }

        public GameboyOptions(FileInfo romFile, ICollection<string> longParameters, ICollection<string> shortParams)
        {
            Rom = romFile.FullName;
            ForceDmg = longParameters.Contains("force-dmg") || shortParams.Contains("d");
            ForceCgb = longParameters.Contains("force-cgb") || shortParams.Contains("c");


            UseBootstrap = longParameters.Contains("use-bootstrap") || shortParams.Contains("b");
            DisableBatterySaves = longParameters.Contains("disable-battery-saves") || shortParams.Contains("db");
            Debug = longParameters.Contains("debug");
            Headless = longParameters.Contains("headless");

            Verify();
        }

        public void Verify()
        {
            if (ForceDmg && ForceCgb)
            {
                throw new ArgumentException("force-dmg and force-cgb options are can't be used together");
            }
        }
        
        public static void PrintUsage(TextWriter stream)
        {
            stream.WriteLine("Usage:");
            stream.WriteLine("coreboy.cli.exe my-totally-not-pirate-rom-file.gb");
            stream.WriteLine();
            stream.WriteLine("Available options:");
            stream.WriteLine("  -d  --force-dmg                Emulate classic GB (DMG) for universal ROMs");
            stream.WriteLine("  -c  --force-cgb                Emulate color GB (CGB) for all ROMs");
            stream.WriteLine("  -b  --use-bootstrap            Start with the GB bootstrap");
            stream.WriteLine("      --disable-battery-saves    Disable battery saves");
            stream.WriteLine("      --debug                    Enable debug console");
            stream.WriteLine("      --headless                 Start in the headless mode");
            stream.WriteLine("      --interactive              Play on the console!");
            stream.Flush();
        }

        public static GameboyOptions Parse(string[] args)
        {
            var longParameters = new List<string>();
            var shortParameters = new List<string>();
            FileInfo romFile = null;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == "-r" || arg == "--rom")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.WriteLine("Missing ROM path.");
                        return null;
                    }

                    romFile = new FileInfo(args[++i]);
                }
                else if (arg.StartsWith("--"))
                {
                    longParameters.Add(arg.Substring(2));
                }
                else if (arg.StartsWith("-"))
                {
                    shortParameters.Add(arg.Substring(1));
                }
                else if (romFile == null)
                {
                    romFile = new FileInfo(arg);
                }
            }

            if (romFile != null)
            {
                return new GameboyOptions(romFile, longParameters, shortParameters);
            }

            var options = new GameboyOptions();
            options.ForceDmg = longParameters.Contains("force-dmg") || shortParameters.Contains("d");
            options.ForceCgb = longParameters.Contains("force-cgb") || shortParameters.Contains("c");
            options.UseBootstrap = longParameters.Contains("use-bootstrap") || shortParameters.Contains("b");
            options.DisableBatterySaves = longParameters.Contains("disable-battery-saves") || shortParameters.Contains("db");
            options.Debug = longParameters.Contains("debug");
            options.Headless = longParameters.Contains("headless");
            options.Interactive = longParameters.Contains("interactive");
            options.Verify();
            return options;
        }
    }
}
    