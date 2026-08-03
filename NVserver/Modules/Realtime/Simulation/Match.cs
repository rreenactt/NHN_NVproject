using NV.Shared.Contracts.Enums;
using NV.Shared.Simulation;

namespace NV.Realtime.Simulation
{
    /// 매치 하나의 단계와 시계. 룸이 소유하고 틱 루프만 만진다.
    ///
    /// **여기가 매치 진행의 유일한 판정자다.** 지금까지 이 계산은 클라이언트 전원에서
    /// 각자 한 벌씩 돌았고(`MatchManager.Update`), 서버는 시작 틱만 넘겼다. 그래서 두
    /// 클라이언트의 시계가 프레임레이트에 따라 갈렸고 리빌이 끝나는 순간도 서로 달랐다.
    ///
    /// **시계를 틱으로 센다.** `Time.deltaTime` 같은 실제 경과 시간을 쓰지 않는 이유는
    /// 결정성이다 — 초를 실수로 누적하면 같은 입력을 재적용해도 같은 결과가 나오지
    /// 않는다. 초는 표시할 때만 만든다.
    ///
    /// 승리 조건은 여기 없다. 시간이 0 이 되면 `Ended` 로 옮기지만 **결과 코드는 채우지
    /// 않는다** — 기획서 §8 과 구현이 어긋나는 지점이 남아 있어(전멸 승리 유무, 2인
    /// 매치에서 Runner 승리 불가) 추측하지 않는다. 그 판정은 IG-007 이 붙인다.
    internal sealed class Match
    {
        /// 매치 길이(틱). 480초 × 30Hz = 14400.
        ///
        /// `const` 로 두면 컴파일 시점에 접히므로 런타임 계산이 없다. 두 상수 모두
        /// 30 의 배수라 나머지가 생기지 않는다.
        private const int MatchTicks = (int)(MatchConstants.MatchDuration * SimConstants.TickRate);

        /// 역할 공개 길이(틱). 4초 × 30Hz = 120.
        private const int RevealTicks = (int)(MatchConstants.RoleRevealDuration * SimConstants.TickRate);

        private MatchPhase _phase = MatchPhase.Lobby;
        private int _revealTicksRemaining;
        private int _matchTicksRemaining;

        public MatchPhase Phase => _phase;

        /// 매치 시계의 남은 틱. `Playing` 이전에는 전체 길이이고, `Ended` 에서는 0 이다.
        public int MatchTicksRemaining => _matchTicksRemaining;

        /// 역할 공개의 남은 틱. `RoleReveal` 이 아니면 0 이다.
        public int RevealTicksRemaining => _phase == MatchPhase.RoleReveal ? _revealTicksRemaining : 0;

        /// 표시용 초. 와이어에는 0.1초 단위로 실린다(IG-008).
        public float MatchSecondsRemaining => _matchTicksRemaining * SimConstants.TickDelta;

        public float RevealSecondsRemaining => RevealTicksRemaining * SimConstants.TickDelta;

        /// 이동을 잠글 것인가.
        ///
        /// **단계 전이가 아니라 입력 무력화로 구현해야 한다.** 시뮬레이션을 멈추면 그
        /// 구간에 중력도 멈춰 공중에 있던 몸이 떠 있고, 시선까지 잠그면 커서가 풀려
        /// 게임이 포커스를 잃은 것처럼 보인다. 이동 성분만 0 으로 만들고 시선은 남긴다
        /// — 클라이언트의 `MatchManager.ApplyMovementLocks` 도 같은 의도였다.
        ///
        /// `Ended` 도 잠근다. 룸이 `Ended` 로 가면 시뮬레이션 자체가 멈추므로 실질적으로는
        /// 닿지 않는 경로지만, 두 단계가 한 틱 어긋나는 순간에도 결과 화면에서 걸어다니지
        /// 않게 한다.
        public bool MovementLocked => _phase == MatchPhase.RoleReveal || _phase == MatchPhase.Ended;

        /// 매치를 시작한다. 역할 공개부터다.
        ///
        /// 시계를 여기서 채운다. `Playing` 에 들어갈 때 채우면 리빌 동안 남은 시간이
        /// 0 으로 보이고, 그 값이 전문에 실려 클라이언트 HUD 가 "시간 종료" 를 그린다.
        public void Begin()
        {
            _phase = MatchPhase.RoleReveal;
            _revealTicksRemaining = RevealTicks;
            _matchTicksRemaining = MatchTicks;
        }

        /// 한 틱 진행한다. 이 틱에 매치가 끝났으면 `true` 다.
        ///
        /// 룸이 그 반환값을 보고 자기 단계를 `Ended` 로 옮긴다. 매치가 룸을 직접 바꾸지
        /// 않는 이유는 소유 관계다 — 룸 단계는 룸의 것이고, 매치는 자기 진행만 안다.
        public bool Advance()
        {
            switch (_phase)
            {
                case MatchPhase.RoleReveal:
                    _revealTicksRemaining--;

                    if (_revealTicksRemaining <= 0)
                    {
                        _revealTicksRemaining = 0;
                        _phase = MatchPhase.Playing;
                    }

                    return false;

                case MatchPhase.Playing:
                    _matchTicksRemaining--;

                    if (_matchTicksRemaining <= 0)
                    {
                        // 기획서 §8 — 시간 종료. 결과 코드는 IG-007 이 정한다.
                        _matchTicksRemaining = 0;
                        _phase = MatchPhase.Ended;
                        return true;
                    }

                    return false;

                default:
                    return false;
            }
        }

        /// 결과와 무관하게 매치를 끝낸다. 방장의 종료 보고가 이 경로로 온다.
        ///
        /// 규칙이 아직 클라이언트에 있는 동안의 한시적 경로이며(`ControlKind.EndMatch`),
        /// 서버가 결과를 정하게 되면 사라진다.
        public void ForceEnd()
        {
            _phase = MatchPhase.Ended;
            _matchTicksRemaining = 0;
            _revealTicksRemaining = 0;
        }

        public void Reset()
        {
            _phase = MatchPhase.Lobby;
            _revealTicksRemaining = 0;
            _matchTicksRemaining = 0;
        }
    }
}
