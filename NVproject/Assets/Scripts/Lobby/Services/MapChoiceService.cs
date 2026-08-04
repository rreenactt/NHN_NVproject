using System;
using System.Collections;
using System.Collections.Generic;
using NV.Client.Config;
using NV.Client.Lobby.Models;
using NV.Client.Map;
using NV.Client.Net.Session;
using UnityEngine;

namespace NV.Client.Lobby.Services
{
    /// 서버의 맵 목록을 받아 이 빌드의 카탈로그와 합쳐 둔다.
    ///
    /// **한 세션에 한 번만 받는다.** 맵은 서버가 다시 뜨기 전까지 변하지 않으므로(서버도 그
    /// 전제로 응답을 기동 때 만들어 ETag 를 붙인다) 팝업을 열 때마다 부를 값이 아니다.
    /// 목록을 못 받았을 때만 다음에 다시 시도한다.
    public sealed class MapChoiceService
    {
        private readonly MonoBehaviour _runner;

        private List<MapChoice> _choices;
        private Coroutine _pending;

        /// 목록을 받아 온 서버. 환경을 바꿔 다른 서버를 보게 되면 캐시를 버려야 한다 —
        /// 서버가 다르면 있는 맵도 다르다.
        private string _fetchedFrom;

        public MapChoiceService(MonoBehaviour runner)
        {
            _runner = runner;
        }

        /// 지금 화면에 보여 줄 목록. 아직 받지 못했으면 비어 있다.
        public IReadOnlyList<MapChoice> Choices => _choices ?? (IReadOnlyList<MapChoice>)Array.Empty<MapChoice>();

        public bool IsLoading { get; private set; }

        /// 서버가 목록을 주지 않았다(옛 서버이거나 미도달). 이 빌드가 아는 맵만 보인다.
        public bool ServerListUnavailable { get; private set; }

        public bool HasChoices => _choices != null && _choices.Count > 0;

        /// 목록을 가져온다. 이미 있으면 아무것도 하지 않는다.
        ///
        /// <param name="onChanged">목록이 바뀌었을 때. 로딩이 시작될 때도 한 번 온다.</param>
        public void Ensure(Action onChanged)
        {
            if (IsLoading)
            {
                return;
            }

            var host = NetSession.Current == null ? string.Empty : NetSession.Current.Host;

            if (_choices != null && string.Equals(_fetchedFrom, host, StringComparison.Ordinal))
            {
                onChanged?.Invoke();
                return;
            }

            IsLoading = true;
            onChanged?.Invoke();

            _pending = _runner.StartCoroutine(FetchRoutine(host, onChanged));
        }

        /// 다음 `Ensure` 가 다시 받도록 한다. 환경을 바꾼 뒤에 부른다.
        public void Invalidate()
        {
            _choices = null;
            _fetchedFrom = null;
        }

        public void Stop()
        {
            if (_pending != null)
            {
                _runner.StopCoroutine(_pending);
                _pending = null;
            }

            IsLoading = false;
        }

        private IEnumerator FetchRoutine(string host, Action onChanged)
        {
            var api = new RoomApi(host, NetSession.Current != null && NetSession.Current.Secure);
            var result = default(MapListResult);

            yield return api.Maps(value => result = value);

            // **서버가 답했는가를 따로 넘긴다.** 목록을 못 받은 것과 "서버에 맵이 없다" 는
            // 다르고, 구분하지 않으면 서버가 꺼져 있을 때 이 빌드의 모든 맵이 "서버에 없다"
            // 로 뜬다.
            ServerListUnavailable = !result.Succeeded;

            _choices = MapChoices.Merge(result.Maps, result.Succeeded, MapCatalog.Load());
            _fetchedFrom = host;

            IsLoading = false;
            _pending = null;

            onChanged?.Invoke();
        }

        // ==================================================== 마지막 선택

        /// 마지막으로 고른 맵을 기억하는 키.
        ///
        /// **환경별로 나뉜다.** `PlayerPrefs` 는 재설치를 넘어 살아남으므로 키가 하나면
        /// `localhost` 를 보던 기계가 실서버용 빌드를 깔고도 그 선택을 들고 있게 된다.
        /// 서버가 다르면 있는 맵도 다르다 — 로비의 호스트 키가 같은 규칙을 쓴다.
        private static string PreferenceKey => "nv." + NVEnvironment.Active.Id + ".lobby.map";

        public static string LastChosen => PlayerPrefs.GetString(PreferenceKey, string.Empty);

        public static void Remember(string mapId)
        {
            if (string.IsNullOrEmpty(mapId))
            {
                return;
            }

            PlayerPrefs.SetString(PreferenceKey, mapId);
            PlayerPrefs.Save();
        }

        /// 처음 선택할 줄의 인덱스.
        ///
        /// 기억한 맵이 지금 목록에 없으면(서버가 바뀌었거나 맵이 사라졌다) 조용히 기본 맵으로
        /// 되돌린다. 고를 수 없는 줄을 골라 두면 만들기 버튼이 이유 없이 꺼져 보인다.
        public static int PreferredIndex(IReadOnlyList<MapChoice> choices)
        {
            if (choices == null || choices.Count == 0)
            {
                return -1;
            }

            var remembered = LastChosen;

            if (!string.IsNullOrEmpty(remembered))
            {
                for (var index = 0; index < choices.Count; index++)
                {
                    if (choices[index].CanCreate
                        && string.Equals(choices[index].MapId, remembered, StringComparison.Ordinal))
                    {
                        return index;
                    }
                }
            }

            for (var index = 0; index < choices.Count; index++)
            {
                if (choices[index].CanCreate && choices[index].IsDefault)
                {
                    return index;
                }
            }

            for (var index = 0; index < choices.Count; index++)
            {
                if (choices[index].CanCreate)
                {
                    return index;
                }
            }

            // 고를 수 있는 것이 하나도 없다. 그래도 첫 줄을 선택해 둔다 — 그 줄의 이유가
            // 화면에 보이는 것이 아무것도 선택되지 않은 것보다 낫다.
            return 0;
        }
    }
}
