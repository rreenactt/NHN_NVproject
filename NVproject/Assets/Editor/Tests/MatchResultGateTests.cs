using NUnit.Framework;
using NV.Client.Net.Session;

namespace NV.Client.EditorTests
{
    /// 결과 화면의 관문. **이 화면을 네 번 고쳤고, 세 번은 이 래치의 상태 전이가 원인이었다.**
    ///
    /// 유니티에 의존하지 않는 순수 로직이므로 검사할 수 있다. 실제로 새던 자리를 그대로
    /// 밟는다 — 라우터는 `Raise` 를 **매 프레임** 부르고, `Rearm` 은 매치가 도는 동안에만
    /// 불린다는 두 사실이 이 검사들의 전제다.
    public class MatchResultGateTests
    {
        [SetUp]
        public void Reset() => MatchResultGate.Rearm();

        [Test]
        public void 매치가_도는_동안에는_붙잡지_않는다()
        {
            Assert.IsFalse(MatchResultGate.Standing);
        }

        [Test]
        public void 결과가_생기면_붙잡는다()
        {
            MatchResultGate.Raise();

            Assert.IsTrue(MatchResultGate.Standing);
        }

        /// **이것이 "나가기를 눌러도 안 나가진다" 였다.**
        ///
        /// 라우터는 방이 끝난 상태인 동안 매 프레임 `Raise` 를 부르고, 방은 방장이 대기방에
        /// 도착할 때까지 끝난 상태로 남는다 — 즉 나간 뒤에야 풀린다. `Raise` 가 멱등하지
        /// 않으면 다음 프레임이 `Dismiss` 를 곧바로 되돌리고 아무도 나갈 수 없다.
        [Test]
        public void 닫은_뒤에는_다시_올라오지_않는다()
        {
            MatchResultGate.Raise();
            MatchResultGate.Dismiss();

            // 방은 아직 끝난 상태다. 라우터가 계속 부른다.
            MatchResultGate.Raise();
            MatchResultGate.Raise();

            Assert.IsFalse(MatchResultGate.Standing);
        }

        /// **이것이 "한 사람이 누르면 전원이 나간다" 였다.**
        ///
        /// 관문이 방의 단계를 보고 있어서, 방장이 대기방에서 방을 되돌리면 아직 읽고 있던
        /// 사람들까지 함께 풀렸다. 래치는 남이 무엇을 하든 움직이지 않는다.
        [Test]
        public void 닫지_않으면_계속_붙잡는다()
        {
            MatchResultGate.Raise();

            for (var frame = 0; frame < 10; frame++)
            {
                Assert.IsTrue(MatchResultGate.Standing, "닫지 않았는데 풀렸다.");
            }
        }

        /// **이것이 "두 번째 판만 결과가 안 뜬다" 였다.**
        ///
        /// 되감지 않으면 지난 매치에서 누른 나가기가 그대로 남아, 다음 매치의 결과가 아예
        /// 올라오지 않는다.
        [Test]
        public void 다음_매치는_다시_붙잡을_수_있다()
        {
            MatchResultGate.Raise();
            MatchResultGate.Dismiss();

            MatchResultGate.Rearm();
            MatchResultGate.Raise();

            Assert.IsTrue(MatchResultGate.Standing);
        }
    }
}
