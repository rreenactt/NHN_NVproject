using System.Collections;
using System.Collections.Generic;
using NV.Client.Lobby.Events;
using NV.Client.Lobby.Models;
using NV.Client.Net.Session;
using UnityEngine;

namespace NV.Client.Lobby.Services
{
    /// 방 목록 조회와, 방에 들어가는 행위를 세션으로 넘기는 일.
    ///
    /// 방을 만들고 참가하는 로직 자체는 여기 없다 — `NetSession` 이 이미 그것이고,
    /// 실패 분류·재시도·코드 정규화가 전부 그 아래 붙어 있다. 이 서비스는 목록이라는
    /// 새 접속면 하나를 더하고, 그 목록으로 후보를 고르는 일만 한다.
    public sealed class RoomService
    {
        /// 수동 새로고침 쿨다운(초).
        ///
        /// `GET /rooms` 에는 서버 레이트리밋이 없다. 막는 쪽이 없으므로 연타를 여기서
        /// 막는다.
        public const float RefreshCooldownSeconds = 3f;

        /// 빠른 참가가 시도할 후보 수 상한.
        ///
        /// 한 번의 시도가 `GET /rooms/{code}` 를 쓰고, 그 경로는 **`/ws` 와 분당 60회
        /// 양동이를 공유한다.** 후보를 무제한으로 훑으면 정작 접속할 예산을 자기가 쓴다.
        private const int QuickJoinCandidates = 3;

        private readonly MonoBehaviour _runner;
        private readonly LobbyModel _model;
        private readonly LobbyEvents _events;

        private Coroutine _pending;
        private float _lastRefreshRequestAt = -999f;

        public RoomService(MonoBehaviour runner, LobbyModel model, LobbyEvents events)
        {
            _runner = runner;
            _model = model;
            _events = events;
        }

        public bool IsRefreshing { get; private set; }

        /// 쿨다운이 남아 있으면 남은 초, 아니면 0.
        public float CooldownRemaining =>
            Mathf.Max(0f, RefreshCooldownSeconds - (Time.unscaledTime - _lastRefreshRequestAt));

        public bool CanRefresh => !IsRefreshing && CooldownRemaining <= 0f;

        /// 목록을 다시 받는다.
        ///
        /// <param name="force">쿨다운을 무시한다. 화면 진입 시의 첫 조회에만 쓴다.</param>
        public void Refresh(bool force = false)
        {
            if (IsRefreshing || (!force && CooldownRemaining > 0f))
            {
                return;
            }

            _lastRefreshRequestAt = Time.unscaledTime;
            IsRefreshing = true;
            _events.RaiseRoomListChanged();

            _pending = _runner.StartCoroutine(RefreshRoutine());
        }

        private IEnumerator RefreshRoutine()
        {
            var api = new RoomApi(NetSession.Current.Host, NetSession.Current.Secure);
            var result = default(RoomListResult);

            yield return api.List(value => result = value);

            var now = Time.unscaledTime;

            if (result.Unavailable)
            {
                _model.SetListUnavailable(now);
            }
            else if (result.Ok)
            {
                _model.SetRooms(Sorted(result.Rooms), now);
            }
            else
            {
                _model.SetListFailed(result.Failure, now);
            }

            IsRefreshing = false;
            _pending = null;

            _events.RaiseRoomListChanged();
            _events.RaiseConnectionChanged();
        }

        public void Stop()
        {
            if (_pending != null)
            {
                _runner.StopCoroutine(_pending);
                _pending = null;
            }

            IsRefreshing = false;
        }

        // ==================================================== 참가

        /// 목록에서 고른 방에 들어간다.
        ///
        /// `JoinByCode` 가 아니라 `JoinRoomId` 다. 목록의 id 는 서버가 준 것이라 오타일 수
        /// 없고, 정적 개발 룸(`test`)은 4자여서 초대 코드 형식 검사에 걸린다 — 그 문으로
        /// 보내면 목록에 보이는 방을 눌러도 `InvalidCode` 로 거부된다.
        public void Join(RoomInfo room)
        {
            NetSession.Current.JoinRoomId(room.Code);
        }

        /// <param name="isPublic">공개 방으로 만들어 목록에 실을 것인가.</param>
        public void Create(string mapId, bool isPublic)
        {
            NetSession.Current.CreateAndJoin(mapId, isPublic);
        }

        public void JoinByCode(string rawCode)
        {
            NetSession.Current.JoinByCode(rawCode);
        }

        /// 빠른 참가를 지금 시도할 수 있는가. 없으면 이유를 돌려준다.
        ///
        /// 버튼을 이유 없이 비활성으로 두지 않기 위한 것이다. 이유 없이 꺼진 버튼은
        /// 고장으로 읽힌다.
        public bool CanQuickJoin(out string reason)
        {
            switch (_model.ListStatus)
            {
                case RoomListStatus.Unavailable:
                    reason = "이 서버는 공개 방 목록을 지원하지 않는다. 초대 코드로 참가한다.";
                    return false;

                case RoomListStatus.Failed:
                    reason = "방 목록을 받지 못했다. 새로고침한 뒤 다시 시도한다.";
                    return false;

                case RoomListStatus.Unknown:
                    reason = "방 목록을 아직 받지 못했다.";
                    return false;
            }

            if (!_model.HasJoinableRoom)
            {
                // 공개 방 중에 없다는 뜻이다. 비공개 방은 목록에 없으므로 빠른 참가가
                // 닿을 수 없고, 그것을 "방이 없다" 로 쓰면 코드를 받은 사람이 코드 참가
                // 대신 방을 만들게 된다.
                reason = "지금 들어갈 수 있는 공개 방이 없다. 코드로 참가하거나 방을 만든다.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public bool IsQuickJoining { get; private set; }

        /// 빠른 참가.
        ///
        /// 매치메이킹이 아니다. 서버에 그런 모듈이 없고, 이것은 목록 위에 얹은 선택
        /// 규칙일 뿐이다 — **가장 많이 찬 방부터** 고른다. 빨리 시작될 방이 좋은 방이고,
        /// 비어 있는 방을 골라 주면 혼자 앉아 최소 인원을 기다리게 된다.
        ///
        /// 조회와 참가 사이에 방이 차거나 시작될 수 있다. 그것은 실패가 아니라 정상이므로
        /// `RoomFull`·`RoomInProgress` 만 다음 후보로 넘어가고, 나머지 실패(버전 불일치,
        /// 서버 미도달, 레이트리밋)는 후보를 더 써도 결과가 같으므로 즉시 멈춘다.
        ///
        /// 후보는 <see cref="QuickJoinCandidates"/> 개까지다. 한 번의 시도가
        /// `GET /rooms/{code}` 를 쓰고 그 경로는 `/ws` 와 분당 60회 양동이를 공유하므로,
        /// 무제한으로 훑으면 정작 접속할 예산을 자기가 쓴다.
        public void QuickJoin()
        {
            if (IsQuickJoining || !CanQuickJoin(out _))
            {
                return;
            }

            IsQuickJoining = true;
            _runner.StartCoroutine(QuickJoinRoutine());
        }

        private IEnumerator QuickJoinRoutine()
        {
            var candidates = Candidates();
            var session = NetSession.Current;

            for (var index = 0; index < candidates.Count; index++)
            {
                var room = candidates[index];

                _events.Toast($"{InviteCodeText.ToDisplay(room.Code)} 에 참가하는 중… ({index + 1}/{candidates.Count})");

                // 목록에서 온 id 다. 초대 코드 형식 검사를 거치지 않는다 — `Join` 참고.
                session.JoinRoomId(room.Code);

                // 세션이 결론을 낼 때까지 기다린다. 대기 단계가 아닌 상태가 나오면 끝난 것이다.
                while (IsSettling(session.State))
                {
                    yield return null;
                }

                if (session.State != SessionState.Failed)
                {
                    IsQuickJoining = false;
                    yield break;
                }

                var kind = session.Failure.Kind;

                if (kind != SessionFailureKind.RoomFull && kind != SessionFailureKind.RoomInProgress)
                {
                    // 후보를 더 써도 같은 결과다. 세션이 분류한 실패를 화면이 그대로 쓴다.
                    IsQuickJoining = false;
                    yield break;
                }
            }

            IsQuickJoining = false;

            _events.Toast(
                candidates.Count == 0
                    ? "지금 들어갈 수 있는 방이 없다."
                    : $"후보 {candidates.Count}곳이 모두 찼거나 시작됐다. 방을 만들거나 새로고침한다.",
                true);

            // 목록이 낡았다는 뜻이다. 다음 새로고침을 막지 않는다.
            _lastRefreshRequestAt = -999f;
        }

        /// 참가 시도가 아직 결론에 이르지 않은 단계인가.
        private static bool IsSettling(SessionState state)
        {
            return state == SessionState.Resolving
                || state == SessionState.Connecting
                || state == SessionState.Handshaking;
        }

        /// 들어갈 수 있는 방을 많이 찬 순서로, 상한까지.
        private List<RoomInfo> Candidates()
        {
            var candidates = new List<RoomInfo>(QuickJoinCandidates);

            // 목록은 이미 참가 가능 → 인원 많은 순으로 정렬되어 있다.
            for (var index = 0; index < _model.Rooms.Count && candidates.Count < QuickJoinCandidates; index++)
            {
                if (LobbyModel.IsJoinable(_model.Rooms[index]))
                {
                    candidates.Add(_model.Rooms[index]);
                }
            }

            return candidates;
        }

        /// 들어갈 수 있는 방을 앞으로, 그 안에서는 많이 찬 순서로.
        ///
        /// 정렬을 서비스에서 한다. 뷰가 정렬하면 같은 목록이 화면마다 다른 순서로
        /// 보이고, 어느 것이 맞는지 판단할 근거가 없어진다.
        private static List<RoomInfo> Sorted(IReadOnlyList<RoomInfo> rooms)
        {
            var sorted = new List<RoomInfo>(rooms.Count);

            for (var index = 0; index < rooms.Count; index++)
            {
                sorted.Add(rooms[index]);
            }

            sorted.Sort(Compare);
            return sorted;
        }

        private static int Compare(RoomInfo left, RoomInfo right)
        {
            var leftJoinable = LobbyModel.IsJoinable(left);
            var rightJoinable = LobbyModel.IsJoinable(right);

            if (leftJoinable != rightJoinable)
            {
                return leftJoinable ? -1 : 1;
            }

            if (left.PlayerCount != right.PlayerCount)
            {
                return right.PlayerCount.CompareTo(left.PlayerCount);
            }

            return string.CompareOrdinal(left.Code, right.Code);
        }
    }
}
