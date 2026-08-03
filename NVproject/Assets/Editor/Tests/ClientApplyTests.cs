using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NV.Game.Tests
{
    /// <summary>
    /// EditMode tests for the client's **apply path** — the code that takes what the server decided
    /// and puts it on the local objects (`Accept*` on <see cref="MatchManager"/>).
    ///
    /// **This file is the whole point of IG-018.** Those methods were written across six iterations
    /// with no automated test at all: they are client-only, so `dotnet test` never saw them, and a
    /// bug in them compiles cleanly. The rule they most easily break is idempotence — the bulletins
    /// repeat at 2 Hz and the apply methods are polled every frame, so anything that *accumulates*
    /// or restarts a side effect goes wrong quietly.
    ///
    /// **No assembly definition was needed.** The predefined `Assembly-CSharp-Editor` already
    /// references `nunit.framework`, both TestRunner assemblies, and `Assembly-CSharp` itself — so a
    /// test placed under `Assets/Editor/` sees the client code with nothing else set up. Three
    /// earlier iterations recorded the opposite ("asmdef cannot reference Assembly-CSharp, so the
    /// scripts must move first") without checking the generated project files.
    ///
    /// **The production code was not touched to make any of this testable** (IG-029). Two things
    /// looked like they would need it and neither did:
    /// - **`internal` members.** `Assembly-CSharp-Editor` is a separate assembly so they are hidden,
    ///   and the plan was to open them the way the server opens its own to `Modules.Tests`. It turned
    ///   out no test here needs one: every apply method is public and every value it writes is
    ///   readable through a public property. Driving the state through `AcceptCombatState` rather
    ///   than pre-setting it with `SetBleeding` is also the better test — it exercises the real path.
    /// - **`[SerializeField] private config`.** Injected with `SerializedObject`, the same API the
    ///   Inspector uses. A test-only setter on `MatchManager` would have been wider.
    /// </summary>
    public sealed class ClientApplyTests
    {
        private GameObject _matchObject;
        private GameObject _agentObject;
        private MatchManager _match;
        private PlayerAgent _agent;
        private GameConfig _config;

        [SetUp]
        public void SetUp()
        {
            _matchObject = new GameObject("Match (test)");
            _match = _matchObject.AddComponent<MatchManager>();

            // `config` 는 `[SerializeField] private` 이라 다른 어셈블리에서 보이지 않는다.
            // 인스펙터가 쓰는 것과 같은 API 로 넣는다 — 프로덕션에 테스트용 setter 를 만드는 것보다
            // 좁고, `MatchManager` 는 자기 필드가 어떻게 채워졌는지 알 필요가 없다.
            _config = ScriptableObject.CreateInstance<GameConfig>();

            var serialized = new SerializedObject(_match);
            serialized.FindProperty("config").objectReferenceValue = _config;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            _agentObject = new GameObject("Agent (test)");
            _agent = _agentObject.AddComponent<PlayerAgent>();
        }

        [TearDown]
        public void TearDown()
        {
            // EditMode 에서는 `Destroy` 가 지연되지 않는 경로가 필요하다 — 남겨 두면 다음 테스트가
            // 이전 테스트의 `MatchManager.Instance` 를 집는다.
            Object.DestroyImmediate(_agentObject);
            Object.DestroyImmediate(_matchObject);
            Object.DestroyImmediate(_config);
        }

        /// **서버의 소지 열쇠 수는 더하는 값이 아니라 덮어쓰는 값이다.**
        ///
        /// 전문은 2Hz 로 같은 값을 다시 보내고 적용은 매 프레임 폴링된다 — 더하면 초당 두 개씩
        /// 늘어난다. `PlayerAgent` 에 `AddKeys` 와 `SetCarriedKeys` 가 둘 다 있는 이유이고,
        /// 오프라인 경로만 `AddKeys` 를 쓴다.
        [Test]
        public void 소지_열쇠는_반복_적용해도_늘지_않는다()
        {
            _match.AcceptCarriedKeys(_agent, 3);
            Assert.AreEqual(3, _agent.CarriedKeys);

            // 같은 전문이 열 번 더 온 것과 같다.
            for (var repeat = 0; repeat < 10; repeat++)
            {
                _match.AcceptCarriedKeys(_agent, 3);
            }

            Assert.AreEqual(3, _agent.CarriedKeys, "폴링이 값을 누적했다 — 더하지 말고 덮어써야 한다.");
        }

        /// 줄어드는 방향도 따라가야 한다. 열쇠를 문에 넣으면 서버의 소지 수가 내려간다.
        [Test]
        public void 소지_열쇠는_줄어드는_방향도_따라간다()
        {
            _match.AcceptCarriedKeys(_agent, 3);
            _match.AcceptCarriedKeys(_agent, 1);

            Assert.AreEqual(1, _agent.CarriedKeys);
        }

        /// 음수가 오면 0 으로 막는다. 와이어는 바이트라 음수가 올 수 없지만, 그 사실에 의존하는
        /// 대신 경계를 코드에 둔다.
        [Test]
        public void 소지_열쇠는_음수가_되지_않는다()
        {
            _match.AcceptCarriedKeys(_agent, -5);

            Assert.AreEqual(0, _agent.CarriedKeys);
        }

        /// 대상이 없으면 조용히 지나간다. 원격 몸은 첫 스냅샷이 와야 생기므로 **전문이 몸보다
        /// 먼저 도착하는 것이 정상**이고, 그때 예외가 나면 매치 시작 직후 2Hz 로 로그가 찬다.
        [Test]
        public void 몸이_없는_참가자는_조용히_지나간다()
        {
            Assert.DoesNotThrow(() => _match.AcceptCarriedKeys(null, 2));
        }

        /// **`KeysChanged` 는 값이 바뀔 때만 올라간다.** 전문이 2Hz 로 같은 값을 다시 보내므로,
        /// 매번 올리면 HUD 가 열쇠 슬롯을 초당 두 번 다시 만든다.
        [Test]
        public void 열쇠_진행도는_바뀔_때만_이벤트를_올린다()
        {
            var raised = 0;
            _match.KeysChanged += (inserted, required) => raised++;

            _match.AcceptObjectiveProgress(3, doorOpen: false);
            Assert.AreEqual(1, raised);
            Assert.AreEqual(3, _match.KeysInserted);

            // 같은 전문이 다섯 번 더 왔다.
            for (var repeat = 0; repeat < 5; repeat++)
            {
                _match.AcceptObjectiveProgress(3, doorOpen: false);
            }

            Assert.AreEqual(1, raised, "같은 값에도 이벤트가 올라갔다 — 폴링이 HUD 를 매번 다시 만든다.");
        }

        /// 서버가 보내는 삽입 수는 `keysRequired` 를 넘을 수 없지만, 그 사실에 의존하지 않고
        /// 경계를 코드에 둔다 — 넘으면 HUD 가 "13/10" 을 그린다.
        [Test]
        public void 열쇠_진행도는_필요_수를_넘지_않는다()
        {
            _match.AcceptObjectiveProgress(_config.keysRequired + 5, doorOpen: false);

            Assert.AreEqual(_config.keysRequired, _match.KeysInserted);
        }

        /// **탈출 수도 바뀔 때만 올라간다.** 이유는 열쇠와 같다.
        [Test]
        public void 탈출_수는_바뀔_때만_이벤트를_올린다()
        {
            var raised = 0;
            _match.EscapesChanged += (escaped, needed) => raised++;

            _match.AcceptEscapes(1);
            _match.AcceptEscapes(1);
            _match.AcceptEscapes(1);

            Assert.AreEqual(1, raised);
            Assert.AreEqual(1, _match.Escapes);
        }

        /// **출혈은 값이 바뀔 때만 적용해야 한다** — `SetBleeding` 이 `BloodTrail` 을 시작하므로
        /// 매 프레임 부르면 흔적이 매 프레임 다시 시작해 **아무것도 남지 않는다.** 이 검사는
        /// 그 규칙이 지켜지는지를 `Bleeding` 상태가 흔들리지 않는 것으로 확인한다.
        [Test]
        public void 출혈은_반복_적용해도_유지된다()
        {

            _match.AcceptCombatState(_agent, hits: 1, bleeding: true, downed: false);
            Assert.IsTrue(_agent.Bleeding);

            for (var repeat = 0; repeat < 10; repeat++)
            {
                _match.AcceptCombatState(_agent, hits: 1, bleeding: true, downed: false);
            }

            Assert.IsTrue(_agent.Bleeding);
            Assert.AreEqual(1, _agent.Hits);
        }

        /// 쓰러짐은 한 번만 적용된다. 반복하면 `Kill` 이 매번 돌아 알림이 매 프레임 뜬다.
        [Test]
        public void 쓰러짐은_한_번만_적용된다()
        {

            var notices = 0;
            _match.Notified += message => notices++;

            _match.AcceptCombatState(_agent, hits: 2, bleeding: false, downed: true);
            Assert.IsFalse(_agent.Alive);

            var afterFirst = notices;

            for (var repeat = 0; repeat < 5; repeat++)
            {
                _match.AcceptCombatState(_agent, hits: 2, bleeding: false, downed: true);
            }

            Assert.AreEqual(afterFirst, notices, "쓰러짐 알림이 반복됐다 — 전이에서만 올라가야 한다.");
        }
    }
}
