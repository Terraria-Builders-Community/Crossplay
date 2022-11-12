using Auxiliary.Configuration;
using Auxiliary.Packets;
using Terraria;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI.Hooks;

namespace Crossplay
{
    [ApiVersion(2, 1)]
    public class Crossplay : TerrariaPlugin
    {
        private readonly List<int> _allowedVersions = new() 
        { 
            269, 
            270, 
            271,
            272,
            273,
            274,
            275,
            276,
            277,
            278
        };

        private readonly int[] _clientVersions = new int[Main.maxPlayers];

        private static int _serverVersion;

        public override string Name 
            => "Crossplay";

        public override string Author 
            => "TBC Developers";

        public override string Description 
            => "Enables crossplay between mobile and PC clients";

        public override Version Version 
            => new(1, 0);

        public Crossplay(Main game)
            : base(game)
        {
            Order = -1;
        }

        public override void Initialize()
        {
            Configuration<CrossplaySettings>.Load("Crossplay");

            GeneralHooks.ReloadEvent += (x) =>
            {
                Configuration<CrossplaySettings>.Load("Crossplay");
                x.Player.SendSuccessMessage("[Crossplay] Reloaded configuration.");

                if (Configuration<CrossplaySettings>.Settings.UseFakeVersion)
                    _serverVersion = Configuration<CrossplaySettings>.Settings.FakeVersion;
                else
                    _serverVersion = Main.curRelease;
            };

            if (Configuration<CrossplaySettings>.Settings.UseFakeVersion)
                _serverVersion = Configuration<CrossplaySettings>.Settings.FakeVersion;
            else
                _serverVersion = Main.curRelease;

            ServerApi.Hooks.NetGetData.Register(this, OnGetData, int.MaxValue);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
        }

        private void OnGetData(GetDataEventArgs args)
        {
            MemoryStream stream = new(args.Msg.readBuffer, args.Index, args.Length);

            int index = args.Msg.whoAmI;
            using BinaryReader reader = new(stream);

            switch (args.MsgID)
            {
                case PacketTypes.ConnectRequest:
                    {
                        string clientVersion = reader.ReadString();

                        if (!int.TryParse(clientVersion.AsSpan(clientVersion.Length - 3), out int versionNum))
                            return;

                        if (versionNum == _serverVersion)
                            return;

                        if (!_allowedVersions.Contains(versionNum) && !Configuration<CrossplaySettings>.Settings.UseFakeVersion)
                            return;

                        _clientVersions[index] = versionNum;
                        NetMessage.SendData(9, args.Msg.whoAmI, -1, NetworkText.FromLiteral("Different version detected. Patching..."), 1);

                        byte[] connectRequest = new PacketFactory()
                            .SetType(1)
                            .PackString($"Terraria{_serverVersion}")
                            .GetByteData();

                        Log($"[Crossplay] Changing version of index {args.Msg.whoAmI} from {ParseVersion(versionNum)} => {ParseVersion(_serverVersion)}", ConsoleColor.Magenta);

                        Buffer.BlockCopy(connectRequest, 0, args.Msg.readBuffer, args.Index - 3, connectRequest.Length);
                    }
                    break;
                case PacketTypes.PlayerInfo:
                    {
                        var length = args.Length - 1;
                        var bitsbyte = (BitsByte)args.Msg.readBuffer[length];

                        if (Main.GameMode == 3 && !bitsbyte[3])
                        {
                            bitsbyte[0] = false;
                            bitsbyte[1] = false;

                            bitsbyte[3] = true;

                            args.Msg.readBuffer[length] = bitsbyte;

                            Log($"[Crossplay] {(bitsbyte[3] ? "Enabled" : "Disabled")} journeymode for index {args.Msg.whoAmI}.", ConsoleColor.Magenta);
                        }
                    }
                    break;
            }
        }

        private void OnLeave(LeaveEventArgs args)
            => _clientVersions[args.Who] = 0;

        private static void Log(string message, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        private static string ParseVersion(int version)
            => version switch
            {
                269 => "v1.4.4",
                270 => "v1.4.4.1",
                271 => "v1.4.4.2",
                272 => "v1.4.4.3",
                273 => "v1.4.4.4",
                274 => "v1.4.4.5",
                275 => "v1.4.4.6",
                276 => "v1.4.4.7",
                277 => "v1.4.4.8",
                278 => "v1.4.4.8.1",
                _ => $"Unknown{version}",
            };
    }
}