namespace NV.Shared.Contracts.Messages
{
    /// 접속 직후 서버가 한 번 보낸다. 13B.
    /// MapHash 가 다르면 클라이언트가 다른 맵으로 예측하고 있다는 뜻이다.
    public readonly struct WelcomeMessage
    {
        public const int WireSize = 13;

        public WelcomeMessage(ushort protocolVersion, byte playerId, uint serverTick, uint mapHash, byte tickRate)
        {
            ProtocolVersion = protocolVersion;
            PlayerId = playerId;
            ServerTick = serverTick;
            MapHash = mapHash;
            TickRate = tickRate;
        }

        public ushort ProtocolVersion { get; }

        public byte PlayerId { get; }

        public uint ServerTick { get; }

        public uint MapHash { get; }

        public byte TickRate { get; }
    }
}
