using Auxiliary.Configuration;
using Auxiliary.Packets;
using System.Runtime.CompilerServices;
using Terraria;
using Terraria.Localization;
using Terraria.Net;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using static Org.BouncyCastle.Math.EC.ECCurve;

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
            278,
            279
        };

        private readonly Dictionary<int, int> _maxItems = new()
        {
            { 269, 5453 },
            { 270, 5453 },
            { 271, 5453 },
            { 272, 5453 },
            { 273, 5453 },
            { 274, 5456 },
            { 275, 5456 },
            { 276, 5456 },
            { 277, 5456 },
            { 278, 5456 },
            { 279, 5456 },
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

            On.Terraria.Net.NetManager.Broadcast_NetPacket_int += OnBroadcast;
            On.Terraria.Net.NetManager.SendToClient += OnSendToClient;

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
                        if (!Configuration<CrossplaySettings>.Settings.EnableClassicSupport)
                            return;

                        ref byte gameModeFlags = ref args.Msg.readBuffer[args.Length - 1];
                        if (Main.GameModeInfo.IsJourneyMode)
                        {
                            if ((gameModeFlags & 8) != 8)
                            {
                                Log($"[Crossplay] Enabled journey mode for index {args.Msg.whoAmI}", color: ConsoleColor.Green);
                                gameModeFlags |= 8;
                                if (Main.ServerSideCharacter)
                                {
                                    NetMessage.SendData(4, args.Msg.whoAmI, -1, null, args.Msg.whoAmI);
                                }
                            }
                            return;
                        }
                        if (TShock.Config.Settings.SoftcoreOnly && (gameModeFlags & 3) != 0)
                        {
                            return;
                        }
                        if ((gameModeFlags & 8) == 8)
                        {
                            Log($"[Crossplay] Disabled journey mode for index {args.Msg.whoAmI}", color: ConsoleColor.Green);
                            gameModeFlags &= 247;
                        }
                    }
                    break;
            }
        }

        private void OnBroadcast(On.Terraria.Net.NetManager.orig_Broadcast_NetPacket_int orig, NetManager self, NetPacket packet, int ignoreClient)
        {
            for (int i = 0; i <= Main.maxPlayers; i++)
            {
                if (i != ignoreClient && Netplay.Clients[i].IsConnected() && !InvalidNetPacket(packet, i))
                {
                    self.SendData(Netplay.Clients[i].Socket, packet);
                }
            }
        }

        private void OnSendToClient(On.Terraria.Net.NetManager.orig_SendToClient orig, NetManager self, NetPacket packet, int playerId)
        {
            if (!InvalidNetPacket(packet, playerId))
            {
                orig(self, packet, playerId);
            }
        }

        private bool InvalidNetPacket(NetPacket packet, int playerId)
        {
            switch (packet.Id)
            {
                case 5:
                    {
                        var itemNetID = Unsafe.As<byte, short>(ref packet.Buffer.Data[3]); // https://unsafe.as/

                        if (itemNetID > _maxItems[_clientVersions[playerId]])
                        {
                            return true;
                        }
                    }
                    break;
            }
            return false;
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
                279 => "v1.4.4.9",
                _ => $"Unknown{version}",
            };
    }
}