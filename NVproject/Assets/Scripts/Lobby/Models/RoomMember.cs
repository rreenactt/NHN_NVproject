using NV.Client.Net;
using NV.Client.Net.Session;
using NV.Shared.Contracts.Messages;

namespace NV.Client.Lobby.Models
{
    /// 명단 한 줄을 화면이 쓰는 형태로 옮긴 것.
    ///
    /// **사본이 아니다.** 그릴 때마다 서버가 보낸 전문에서 만들고 어디에도 보관하지 않는다.
    /// 보관하면 그것이 곧 두 번째 명단이 되고, 서버가 보낸 것과 어긋날 수 있다.
    ///
    /// 이 타입이 있는 이유는 한 줄이 무엇인지 답하는 계산이 **세 값의 조합**이라는 것이다 —
    /// 명단 항목(`RoomPlayerEntry`), 전문 헤더의 방장 바이트, 그리고 이 클라이언트 자신의
    /// PlayerId. 그 조합을 그리는 곳마다 다시 하면 3D 스탠드와 HUD 명단이 서로 다른 답을
    /// 내는 순간이 생긴다 — 특히 "누가 나인가" 는 화면마다 다르게 틀릴 수 있다.
    public readonly struct RoomMember
    {
        public RoomMember(in RoomPlayerEntry entry, byte hostPlayerId, byte localPlayerId, bool hostKnown)
        {
            PlayerId = entry.PlayerId;
            Name = entry.Name;
            CharacterId = entry.CharacterId;
            IsReady = entry.IsReady;
            IsBot = entry.IsBot;
            IsHost = hostKnown && hostPlayerId == entry.PlayerId;
            IsSelf = localPlayerId == entry.PlayerId;
        }

        public byte PlayerId { get; }

        /// 서버가 보낸 이름. **비어 있을 수 있다** — 와이어가 길이 0 을 허용한다.
        public string Name { get; }

        public byte CharacterId { get; }

        public bool IsReady { get; }

        public bool IsBot { get; }

        public bool IsHost { get; }

        public bool IsSelf { get; }

        /// 화면에 쓸 이름. 비어 있으면 슬롯 번호로 대신한다.
        public string DisplayName => string.IsNullOrEmpty(Name) ? "플레이어 " + PlayerId : Name;

        /// 지금 명단을 훑는다. 아무것도 보관하지 않는다.
        ///
        /// `hostKnown` 이 거짓인 동안(전문이 아직 오지 않았다) 아무도 방장이 아니다 —
        /// 모르는 것을 "방장이 없다" 로 그리면 접속 직후 한순간 잘못된 화면이 뜬다.
        public static int Collect(NetSession session, RoomMember[] destination)
        {
            var client = session != null ? session.Client : null;

            if (client == null || destination == null)
            {
                return 0;
            }

            var hostKnown = client.HasRoomState;
            var hostPlayerId = hostKnown ? client.RoomState.HostPlayerId : RoomStateHeader.NoPlayer;
            var localPlayerId = client.HasWelcome ? client.LocalPlayerId : RoomStateHeader.NoPlayer;

            var count = 0;

            for (var index = 0; index < client.RosterCount && count < destination.Length; index++)
            {
                destination[count++] = new RoomMember(
                    client.RosterEntry(index),
                    hostPlayerId,
                    localPlayerId,
                    hostKnown);
            }

            return count;
        }
    }
}
