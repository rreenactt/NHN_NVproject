using NUnit.Framework;
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
    /// What that boundary *does* cost: `Assembly-CSharp-Editor` is a separate assembly, so
    /// **`internal` members are not visible here** and neither are `[SerializeField] private` fields.
    /// That is what limits how much of the apply path this file can reach today — see the notes on
    /// the individual gaps in `docs/LOOP_PROGRESS.md` (IG-018).
    /// </summary>
    public sealed class ClientApplyTests
    {
        private GameObject _matchObject;
        private GameObject _agentObject;
        private MatchManager _match;
        private PlayerAgent _agent;

        [SetUp]
        public void SetUp()
        {
            _matchObject = new GameObject("Match (test)");
            _match = _matchObject.AddComponent<MatchManager>();

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
    }
}
