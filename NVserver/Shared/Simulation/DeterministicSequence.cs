namespace NV.Shared.Simulation
{
    /// 순서가 있는 결정적 난수. 상태를 명시적으로 들고 다닌다.
    ///
    /// `DeterministicRandom` 으로는 이것을 만들 수 없다. 그쪽은 (틱, 엔티티, salt) →
    /// 값의 **무상태 해시**여서, 같은 틱에 여러 번 뽑으면 같은 값이 나온다. 목표물
    /// 배치처럼 "열쇠 10개를 차례로" 뽑는 일에는 수열이 필요하다.
    ///
    /// `new Random()` 은 쓰지 않는다. 구현이 런타임 버전에 묶여 있어 Unity(IL2CPP)와
    /// .NET 이 같은 씨드에서 다른 수열을 낼 수 있고, 그러면 클라이언트와 서버의 배치가
    /// 갈린다. `architecture.md` 의 기본값 대체표가 금지하는 항목이다.
    ///
    /// **초대 코드와 방장 토큰에는 절대 쓰지 않는다.** 그쪽은 예측 불가능해야 하므로
    /// `RandomNumberGenerator` 다(`conventions.md`). 이것은 재현 가능한 것이 목적이라
    /// 정확히 반대의 성질을 갖는다.
    ///
    /// xorshift32 다. 상태가 32비트 하나뿐이라 값으로 복사해도 싸고, 배치를 재현하려면
    /// 씨드만 남겨 두면 된다. 구조체로 둔 이유가 그것이다 — 호출자가 `ref` 로 넘겨
    /// 수열을 이어 가거나, 복사해 분기를 만들 수 있다.
    public struct DeterministicSequence
    {
        /// 0 을 대신할 값. xorshift 는 0 이 고정점이어서 한 번 빠지면 계속 0 만 낸다.
        private const uint NonZeroFallback = 2463534242u;

        private uint _state;

        public DeterministicSequence(int seed)
        {
            _state = seed == 0 ? NonZeroFallback : unchecked((uint)seed);
        }

        public DeterministicSequence(uint seed)
        {
            _state = seed == 0u ? NonZeroFallback : seed;
        }

        /// 지금 상태. 같은 값으로 다시 만들면 같은 수열이 이어진다.
        public uint State => _state;

        public uint NextUInt()
        {
            // default(DeterministicSequence) 로 만들어진 경우를 여기서 건진다.
            // 그냥 두면 0 만 돌려주고, 증상은 "목표물이 전부 한 자리에 겹침" 이다.
            if (_state == 0u)
            {
                _state = NonZeroFallback;
            }

            var x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;

            return x;
        }

        /// [0, 1). 상위 24비트만 쓴다 — float 의 유효 자릿수가 거기까지다.
        /// `DeterministicRandom.NextUnitFloat` 와 같은 변환을 쓴다.
        public float NextUnitFloat()
        {
            return (NextUInt() >> 8) / 16777216f;
        }

        /// [0, exclusiveMax). 나머지 연산의 편향을 거부 표집으로 없앤다.
        ///
        /// 편향을 남기면 열쇠가 격자의 앞쪽 셀에 몰린다. 2^32 는 대개 범위의 배수가
        /// 아니므로 앞쪽 값들이 한 번 더 뽑힐 기회를 갖고, 후보 셀이 수천 개일 때 그
        /// 치우침은 눈에 보인다. `InviteCode` 가 31 심볼 알파벳에 같은 이유로 같은
        /// 방법을 쓴다.
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 1)
            {
                return 0;
            }

            var bound = (uint)exclusiveMax;

            // 범위의 배수를 넘는 꼬리를 버린다. 기대 재시도는 1회 미만이다.
            var limit = uint.MaxValue - (uint.MaxValue % bound);

            uint value;
            do
            {
                value = NextUInt();
            }
            while (value >= limit);

            return (int)(value % bound);
        }

        /// [min, exclusiveMax).
        public int NextInt(int min, int exclusiveMax)
        {
            return exclusiveMax <= min ? min : min + NextInt(exclusiveMax - min);
        }
    }
}
