namespace NV.Shared.Contracts.Messages
{
    /// 스냅샷 헤더. opcode 를 포함해 10B.
    /// 8인 기준 스냅샷은 10 + 8 * 13 = 114B 다.
    public readonly struct SnapshotHeader
    {
        /// opcode(1) + tick(4) + ackedInputTick(4) + entityCount(1)
        public const int WireSize = 10;

        public SnapshotHeader(uint tick, uint ackedInputTick, byte entityCount)
        {
            Tick = tick;
            AckedInputTick = ackedInputTick;
            EntityCount = entityCount;
        }

        public uint Tick { get; }

        /// 이 수신자의 입력 중 서버가 마지막으로 적용한 틱.
        /// 클라이언트가 리컨실리에이션 시 버퍼를 어디까지 버릴지 판단하는 기준이다.
        /// 수신자마다 다르므로 스냅샷 본문은 같아도 헤더는 세션별로 다르다.
        public uint AckedInputTick { get; }

        public byte EntityCount { get; }
    }
}
