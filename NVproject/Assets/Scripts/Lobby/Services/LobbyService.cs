using System.Collections;
using NV.Client.Config;
using NV.Client.Lobby.Events;
using NV.Client.Lobby.Models;
using NV.Client.Net.Session;
using UnityEngine;

namespace NV.Client.Lobby.Services
{
    /// 서버 생존 확인과 플레이어 프로필.
    ///
    /// 로비가 "지금 붙을 수 있는가" 를 말하기 위한 최소한의 것만 한다. 방과 관련된
    /// 것은 `RoomService` 가 갖는다.
    ///
    /// 서버는 상태를 알려 주는 엔드포인트를 `GET /health` 하나만 가지고 있고 그 응답은
    /// 문자열 `ok` 다. 버전·가동 시간·룸 수·맵 목록 어느 것도 없다. 그래서 여기서
    /// 만들 수 있는 것은 "닿는다/안 닿는다" 뿐이며, 그 이상을 표시하면 지어낸 것이 된다.
    public sealed class LobbyService
    {
        /// 생존 확인 주기(초).
        ///
        /// 이 호출은 레이트리밋 양동이를 쓰지 않으므로 주기를 둬도 안전하다. 방 목록은
        /// 사정이 다르다 — 그쪽은 자동 폴링을 붙이지 않는다.
        private const float HealthIntervalSeconds = 5f;

        private readonly MonoBehaviour _runner;
        private readonly LobbyModel _model;
        private readonly LobbyEvents _events;

        private Coroutine _loop;

        public LobbyService(MonoBehaviour runner, LobbyModel model, LobbyEvents events)
        {
            _runner = runner;
            _model = model;
            _events = events;
        }

        /// 지금 화면이 쓰는 서버 주소. 설정에서 바뀌면 다음 순회부터 반영된다.
        public string Host => NetSession.Current.Host;

        public bool Secure => NetSession.Current.Secure;

        public void StartWatching()
        {
            StopWatching();
            _loop = _runner.StartCoroutine(HealthLoop());
        }

        public void StopWatching()
        {
            if (_loop != null)
            {
                _runner.StopCoroutine(_loop);
                _loop = null;
            }
        }

        private IEnumerator HealthLoop()
        {
            var wait = new WaitForSecondsRealtime(HealthIntervalSeconds);

            while (true)
            {
                if (_model.Server == ServerStatus.Unknown)
                {
                    _model.SetServer(ServerStatus.Checking);
                    _events.RaiseConnectionChanged();
                }

                // 주소가 바뀔 수 있으므로 매 순회마다 새로 만든다. 한 번 만든 것을
                // 들고 있으면 설정에서 서버를 바꿔도 옛 주소를 계속 확인한다.
                var api = new RoomApi(Host, Secure);
                var alive = false;

                yield return api.Health(result => alive = result);

                var next = alive ? ServerStatus.Online : ServerStatus.Offline;

                if (_model.Server != next)
                {
                    _model.SetServer(next);
                    _events.RaiseConnectionChanged();
                }

                yield return wait;
            }
        }

        // ==================================================== 프로필

        /// 저장된 프로필을 세션에 적는다. 로비가 뜰 때 한 번.
        public void ApplyStoredProfile()
        {
            NetSession.Current.Configure(
                PlayerProfile.Host,
                PlayerProfile.Secure,
                PlayerProfile.DisplayName);
        }

        /// 설정 화면의 값을 저장하고 세션에 반영한다.
        ///
        /// 세션이 거부하면(접속 중) 저장도 하지 않는다. 저장만 되고 반영되지 않으면
        /// 다음 실행에서야 적용되는데, 화면은 이미 바뀐 값을 보여 주고 있다.
        public bool SaveProfile(string displayName, string host, bool secure)
        {
            var cleanName = PlayerProfile.Sanitize(displayName);
            // 비워서 저장하면 이 환경이 정한 주소로 되돌아간다. 예전에는 상수 하나로
            // 되돌아갔고, 그래서 배포 빌드에서 주소를 지우면 localhost 가 들어갔다.
            var cleanHost = string.IsNullOrWhiteSpace(host)
                ? NVEnvironment.Active.Host
                : host.Trim();

            if (!NetSession.Current.Configure(cleanHost, secure, cleanName))
            {
                return false;
            }

            PlayerProfile.DisplayName = cleanName;
            PlayerProfile.Host = cleanHost;
            PlayerProfile.Secure = secure;
            PlayerProfile.Save();

            // 주소가 바뀌었으면 다음 확인까지 기다리지 않고 다시 본다.
            _model.SetServer(ServerStatus.Unknown);

            _events.RaiseProfileChanged();
            _events.RaiseConnectionChanged();

            return true;
        }
    }
}
