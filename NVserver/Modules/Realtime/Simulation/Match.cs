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

        /// 열쇠 두 개를 연달아 넣는 사이의 간격(틱). 0.6초 × 30Hz = 18.
        ///
        /// 룸의 삽입 판정이 쓴다. 초가 아니라 틱으로 재는 이유는 시계와 같다 — 실수 누적은
        /// 재적용에서 같은 결과를 주지 않는다.
        public const int InsertIntervalTicks = (int)(MatchConstants.KeyInsertInterval * SimConstants.TickRate);

        /// 열린 문간에 머물러야 탈출로 인정되는 시간(틱). 0.8초 × 30Hz = 24.
        ///
        /// 즉시 탈출이 아닌 이유는 목표의 마지막 한 걸음을 Seeker 가 끊을 수 있는 순간으로
        /// 만들기 위해서다(`MatchConstants.EscapeHoldTime`).
        public const int EscapeHoldTicks = (int)(MatchConstants.EscapeHoldTime * SimConstants.TickRate);

        /// 연사 간격(틱). **0.15초 × 30Hz = 4.5 이므로 올려서 5 다(0.167초).**
        ///
        /// 내리면(4틱, 0.133초) 서버가 클라이언트보다 빠른 연사를 허용한다 — 오차가 "클라이언트가
        /// 보내지도 않은 발사를 서버가 받아 준다" 는 방향으로 생기고, 그것은 신뢰 경계를 넓히는
        /// 쪽이다. 올리면 반대로 서버가 조금 엄격해지고, 증상은 연사가 미세하게 느린 것뿐이다.
        ///
        /// **틱으로 나누어지지 않는 간격을 상수로 두면 이 선택이 코드에서 사라진다.** 그래서
        /// 나눗셈이 아니라 값으로 적었다.
        public const int FireIntervalTicks = 5;

        /// 총알 수명(틱). 3초 × 30Hz = 90.
        public const int BulletLifetimeTicks = (int)(MatchConstants.BulletLifetime * SimConstants.TickRate);

        /// 무적 창(틱). **0.75초 × 30Hz = 22.5 이므로 올려서 23 이다(0.767초).**
        ///
        /// `FireIntervalTicks` 와 같은 올림이지만 이유가 다르다. 그쪽은 서버가 관대해지지 않게
        /// 하려는 것이고, 이쪽은 **무적 창이 짧아지는 것이 곧 규칙이 깨지는 것**이기 때문이다 —
        /// 이 창의 존재 이유가 3연사가 순간이동을 관통해 죽이는 것을 막는 것이므로, 반올림으로
        /// 한 틱을 잃으면 막으려던 경우가 다시 열린다.
        public const int HitImmunityTicks = 23;

        private MatchPhase _phase = MatchPhase.Lobby;
        private int _freezeTicksRemaining;
        private int _revealTicksRemaining;
        private int _matchTicksRemaining;

        public MatchPhase Phase => _phase;

        /// 문에 들어간 열쇠 수. 기획서 §3 의 목표 진행도다.
        ///
        /// **Seeker 는 이 값을 몰라야 한다.** 전문을 인코딩할 때 코덱이 Seeker 사본에서
        /// 0 으로 만든다(`MessageCodec.WriteMatchState`) — 여기서 숨기지 않는 이유는 이것이
        /// 판정에 쓰이는 실제 값이고, 필터는 나가는 길목에 한 번만 있어야 하기 때문이다.
        public int KeysInserted { get; private set; }

        /// 문이 열렸는가. **삽입 수에서 유도한다.**
        ///
        /// 따로 필드를 두면 "열쇠는 10개인데 문은 닫혀 있다" 가 표현 가능한 상태가 되고,
        /// 그 상태에 빠지는 경로를 찾는 일이 남는다.
        public bool DoorOpen => KeysInserted >= MatchConstants.KeysRequired;

        /// 탈출한 Runner 수.
        ///
        /// **이것은 Seeker 도 안다** — 자기가 막아야 하는 수이고, 코덱이 거르지 않는다
        /// (`MatchParticipant.Escapes` 아니라 `MatchStateHeader.Escapes`). 열쇠 진행도와 다른
        /// 취급인 이유는 기획서 §2.1 이 숨기는 것이 **목표의 위치와 진행도**이고, 몇 명이
        /// 빠져나갔는지는 술래가 세고 있어야 하는 값이기 때문이다.
        public int Escapes { get; private set; }

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
        ///
        /// **전체 정지 장치도 여기로 들어온다**(기획서 §5.1). 잠그는 이유가 다를 뿐 잠그는
        /// 방식은 같아야 한다 — 갈라 두면 "리빌 중에는 멈추는데 정지 장치에는 걸어다닌다"
        /// 같은 반쪽 규칙이 생긴다. 체인이 같은 이유로 같은 자리를 쓰고 있다(`Room.ApplyFrame`).
        public bool MovementLocked =>
            _phase == MatchPhase.RoleReveal || _phase == MatchPhase.Ended || DevicesFrozen;

        /// 전체 정지 장치가 도는 중인가.
        public bool DevicesFrozen => _freezeTicksRemaining > 0;

        /// 전체 정지를 건다(기획서 §5.1). 이미 돌고 있으면 **다시 채운다** — 두 대를 잇달아
        /// 쓰면 겹치는 것이 아니라 이어지는 것이 맞다.
        public void FreezeDevices(int ticks)
        {
            if (ticks > _freezeTicksRemaining)
            {
                _freezeTicksRemaining = ticks;
            }
        }

        /// 시계에 더한다(기획서 §5.1 "시간 증가").
        ///
        /// **`Playing` 에서만 더한다.** 리빌 중에 더하면 아직 시작하지 않은 매치의 시계가
        /// 늘어나고, 끝난 뒤에 더하면 0 이어야 할 시계가 되살아나 결과 화면이 카운트다운을 그린다.
        public void AddTicks(int ticks)
        {
            if (_phase != MatchPhase.Playing || ticks <= 0)
            {
                return;
            }

            _matchTicksRemaining += ticks;
        }

        /// 매치를 시작한다. 역할 공개부터다.
        ///
        /// 시계를 여기서 채운다. `Playing` 에 들어갈 때 채우면 리빌 동안 남은 시간이
        /// 0 으로 보이고, 그 값이 전문에 실려 클라이언트 HUD 가 "시간 종료" 를 그린다.
        public void Begin()
        {
            _phase = MatchPhase.RoleReveal;
            _revealTicksRemaining = RevealTicks;
            _matchTicksRemaining = MatchTicks;
            _freezeTicksRemaining = 0;
            KeysInserted = 0;
            Escapes = 0;
        }

        /// 열쇠 하나가 문에 들어갔다. **이 삽입으로 문이 열렸으면 true.**
        ///
        /// 이미 열린 문에는 넣을 수 없다. 룸이 먼저 확인하지만 여기서도 막는다 — 열린 뒤에도
        /// 세면 `KeysInserted` 가 10을 넘고, 그 값이 HUD 의 "10/10" 을 "13/10" 으로 만든다.
        ///
        /// 자격(역할·소지·거리·간격)은 룸이 판단한다. 여기는 진행도만 센다.
        public bool InsertKey()
        {
            if (DoorOpen)
            {
                return false;
            }

            KeysInserted++;
            return DoorOpen;
        }

        /// Runner 한 명이 빠져나갔다.
        ///
        /// **승리 판정은 여기서 하지 않는다.** 세는 것과 이기는 것이 갈라져 있고, 이기는 쪽은
        /// 아직 방장이 판정한다(`MatchManager.EvaluateWinConditions`).
        ///
        /// 그것을 막고 있던 질문 둘은 이제 답이 있다 — 전멸은 술래 승리이고(OQ-2), 탈출 목표는
        /// 시작 인원을 넘지 않는다(OQ-6, `MatchConstants.EscapesToWinWith`). 그래서 IG-007 은
        /// 더 이상 막혀 있지 않고, 이 값을 읽어 결과 코드를 채우는 일만 남았다.
        public void RegisterEscape()
        {
            Escapes++;
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
                    // 정지 장치는 매치 시계를 멈추지 않는다. 멈추면 정지가 곧 시간 연장이 되어
                    // 기획서에 없는 두 번째 효과가 붙는다.
                    if (_freezeTicksRemaining > 0)
                    {
                        _freezeTicksRemaining--;
                    }

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
            _freezeTicksRemaining = 0;
        }

        public void Reset()
        {
            _phase = MatchPhase.Lobby;
            _revealTicksRemaining = 0;
            _matchTicksRemaining = 0;
            _freezeTicksRemaining = 0;
            KeysInserted = 0;
            Escapes = 0;
        }
    }
}
