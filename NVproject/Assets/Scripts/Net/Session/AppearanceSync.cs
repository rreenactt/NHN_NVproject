using NV.Game;
using UnityEngine;

namespace NV.Client.Net.Session
{
    /// 명단에 실려 온 캐릭터를 몸에 입힌다. 게임 씬에 하나 둔다.
    ///
    /// 로비에서 고른 캐릭터가 매치까지 따라오게 하는 것이 전부다. 그 전에는 id 가 와이어로는
    /// 오는데(`RoomPlayerEntry.CharacterId`) 읽는 쪽이 대기방밖에 없어서, 여덟 명이 각자 다른
    /// 캐릭터를 고르고 문을 열면 전원이 같은 흰 블록으로 들어갔다.
    ///
    /// **명단은 매치 중에도 계속 온다.** 서버의 `Room.Broadcast` 가 단계를 보기 전에
    /// `BroadcastRoomState` 를 부르므로(`Phase != Playing` 반환은 스냅샷 앞에 있다) 이 컴포넌트는
    /// 로비 전용 정보를 붙잡아 두지 않아도 된다 — 지금 오는 것을 읽으면 된다.
    ///
    /// 규칙이 아니라 겉모습이므로 `MatchSync` 와 갈라 둔다. 저쪽이 옮기는 넷(시작·Seeker·배치·종료)은
    /// 틀리면 게임이 갈리고, 이쪽은 틀려도 옷이 틀릴 뿐이다. 같은 컴포넌트에 넣으면 그 차이가
    /// 사라진다.
    [DefaultExecutionOrder(-70)]
    public sealed class AppearanceSync : MonoBehaviour
    {
        private NetSession _session;
        private NetworkClient _client;
        private NetworkBootstrap _bootstrap;

        private void Update()
        {
            if (!Bind())
            {
                return;
            }

            // 폴링이다. 명단 변경 이벤트만 듣지 않는 이유는 **몸이 명단보다 늦게 오기 때문**이다 —
            // 원격 몸은 첫 스냅샷이 와야 만들어지므로, 변경 시점에 칠하면 그때 없던 몸은 영원히
            // 흰색으로 남는다. 8명 × 프레임당 조회 하나이고, 옷이 이미 맞으면 `Apply` 가 즉시
            // 돌아온다.
            var count = _client.RosterCount;

            for (var index = 0; index < count; index++)
            {
                var entry = _client.RosterEntry(index);
                var rig = RigFor(entry.PlayerId);

                if (rig == null)
                {
                    continue;
                }

                var appearance = rig.GetComponent<CharacterAppearance>();

                if (appearance == null)
                {
                    appearance = rig.gameObject.AddComponent<CharacterAppearance>();
                }

                appearance.Apply(entry.CharacterId);
            }
        }

        /// 이 플레이어의 몸. 로컬은 자기 자신, 나머지는 원격 몸이다.
        ///
        /// 매치 레이어를 거치지 않는다. 겉모습은 규칙과 무관하고, 규칙 레이어가 없는 씬
        /// (`MultiplayerTest`)에서도 캐릭터는 보여야 한다.
        private BlockRig RigFor(byte playerId)
        {
            if (_client.HasWelcome && playerId == _client.LocalPlayerId)
            {
                return _bootstrap != null && _bootstrap.LocalPlayer != null
                    ? _bootstrap.LocalPlayer.GetComponent<BlockRig>()
                    : null;
            }

            return _bootstrap != null && _bootstrap.TryGetPuppet(playerId, out var puppet)
                ? puppet.GetComponent<BlockRig>()
                : null;
        }

        /// 세션과 클라이언트가 준비됐는가. 세션은 씬보다 오래 살고 부트스트랩은 이 씬에서
        /// 만들어지므로 순서로 보장하지 않고 매 프레임 확인한다.
        private bool Bind()
        {
            if (!NetSession.Exists)
            {
                return false;
            }

            if (_session == null)
            {
                _session = NetSession.Current;
                _client = _session.Client;
            }

            if (_bootstrap == null)
            {
                _bootstrap = FindFirstObjectByType<NetworkBootstrap>();
            }

            return _client != null && _client.RosterCount > 0 && _bootstrap != null;
        }
    }
}
