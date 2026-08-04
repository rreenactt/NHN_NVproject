using NV.Shared.Contracts.Messages;

namespace NV.Realtime.Simulation.Bots
{
    /// 봇의 다음 입력을 만든다. **`InputFrame` 만 돌려준다.**
    ///
    /// 그것이 이 파일의 전부이고 규칙이다. 봇이 위치나 소지 열쇠를 직접 만지면 서버
    /// 판정을 우회하게 되고, 그러면 "봇으로 확인했다" 가 사람에게 아무것도 보증하지
    /// 않는다. 여기서 나온 프레임은 사람의 프레임과 같은 경로를 지난다 —
    /// `InputValidator.Sanitize` → 이동 잠금 → `PlayerMovement.Step` → 목표물·전투 판정.
    ///
    /// `Shared` 가 아니라 모듈에 있다. `structure.md` 8문 표의 1번 — 클라이언트는 봇의
    /// 다음 입력을 예측할 필요가 없고 스냅샷으로 결과만 받는다.
    ///
    /// 지금은 서 있는 것 하나다. 배회와 목표 수행이 붙는 자리이며, 그때 시야(자기 상태·
    /// 역할·목표물·다른 몸들)와 난수가 인자로 들어온다. 쓸 것이 없는 동안 그 인자를
    /// 미리 받아 두지 않는다 — 채워 넣는 코드가 함께 생기고, 그것이 곧 죽은 코드다.
    internal static class BotBrain
    {
        /// 서 있는다. 이동 성분만 비우고 시선은 유지한다.
        ///
        /// 시뮬레이션 자체는 계속 돌므로 중력을 받아 바닥에 내려앉고, 총에 맞고, 문간에
        /// 서 있으면 탈출까지 한다. 서 있는 몸 하나로 검증되는 것이 그만큼 넓다.
        public static InputFrame Think(in InputFrame lastInput)
        {
            return InputValidator.Neutral(lastInput);
        }
    }
}
