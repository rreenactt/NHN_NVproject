using NV.Shared.Simulation;
using Xunit;

namespace NV.Modules.Tests.Simulation
{
    /// 배치용 난수. 요구는 둘이다 — 같은 씨드가 같은 수열을 내고, 치우치지 않는다.
    ///
    /// 클라이언트와 서버가 같은 씨드로 같은 목표물 배치를 계산해야 하므로 첫 번째가
    /// 필수이고, 두 번째를 놓치면 열쇠가 격자 앞쪽에 몰린다.
    public class DeterministicSequenceTests
    {
        [Fact]
        public void 같은_씨드는_같은_수열을_낸다()
        {
            var a = new DeterministicSequence(12345);
            var b = new DeterministicSequence(12345);

            for (var index = 0; index < 256; index++)
            {
                Assert.Equal(a.NextUInt(), b.NextUInt());
            }
        }

        [Fact]
        public void 다른_씨드는_다른_수열을_낸다()
        {
            var a = new DeterministicSequence(1);
            var b = new DeterministicSequence(2);

            var same = 0;
            for (var index = 0; index < 64; index++)
            {
                if (a.NextUInt() == b.NextUInt())
                {
                    same++;
                }
            }

            // 우연히 몇 개 겹칠 수는 있으나 수열이 같아서는 안 된다.
            Assert.True(same < 8, $"두 씨드의 수열이 {same}/64 만큼 겹쳤다.");
        }

        /// 씨드 0 은 실수로 들어오기 쉬운 값이고, xorshift 의 고정점이다.
        /// 걸러 내지 않으면 0 만 계속 나오고 목표물이 전부 한 자리에 겹친다.
        [Fact]
        public void 씨드가_0_이어도_0_만_내지_않는다()
        {
            var sequence = new DeterministicSequence(0);

            var nonZero = 0;
            for (var index = 0; index < 32; index++)
            {
                if (sequence.NextUInt() != 0u)
                {
                    nonZero++;
                }
            }

            Assert.Equal(32, nonZero);
        }

        /// 구조체라 `default` 로도 만들어진다. 그 상태의 내부값이 0 이므로
        /// 같은 고정점 문제를 갖는다.
        [Fact]
        public void default_로_만들어도_수열이_돌아간다()
        {
            var sequence = default(DeterministicSequence);

            var first = sequence.NextUInt();
            var second = sequence.NextUInt();

            Assert.NotEqual(0u, first);
            Assert.NotEqual(first, second);
        }

        [Fact]
        public void 단위_실수는_0_이상_1_미만이다()
        {
            var sequence = new DeterministicSequence(777);

            for (var index = 0; index < 4096; index++)
            {
                var value = sequence.NextUnitFloat();
                Assert.InRange(value, 0f, 0.99999994f);
            }
        }

        [Theory]
        [InlineData(2)]
        [InlineData(7)]
        [InlineData(31)]
        [InlineData(2450)]
        public void 정수는_범위_안에_있다(int exclusiveMax)
        {
            var sequence = new DeterministicSequence(99);

            for (var index = 0; index < 4096; index++)
            {
                var value = sequence.NextInt(exclusiveMax);
                Assert.InRange(value, 0, exclusiveMax - 1);
            }
        }

        [Fact]
        public void 상한이_1_이하면_0_을_낸다()
        {
            var sequence = new DeterministicSequence(5);

            Assert.Equal(0, sequence.NextInt(1));
            Assert.Equal(0, sequence.NextInt(0));
            Assert.Equal(0, sequence.NextInt(-3));
        }

        [Fact]
        public void 최소값이_있는_정수는_그_범위_안에_있다()
        {
            var sequence = new DeterministicSequence(4242);

            for (var index = 0; index < 1024; index++)
            {
                Assert.InRange(sequence.NextInt(10, 20), 10, 19);
            }

            // 범위가 비면 최소값을 낸다.
            Assert.Equal(10, sequence.NextInt(10, 10));
            Assert.Equal(10, sequence.NextInt(10, 3));
        }

        /// 거부 표집이 실제로 편향을 없앴는지 본다. 나머지 연산만 쓰면 앞쪽 값이
        /// 한 번 더 뽑힐 기회를 갖고, 후보가 수천 개일 때 그 치우침이 배치에 보인다.
        [Fact]
        public void 정수_분포가_한쪽으로_치우치지_않는다()
        {
            const int buckets = 16;
            const int draws = 160_000;
            const int expected = draws / buckets;

            var counts = new int[buckets];
            var sequence = new DeterministicSequence(20260804);

            for (var index = 0; index < draws; index++)
            {
                counts[sequence.NextInt(buckets)]++;
            }

            for (var bucket = 0; bucket < buckets; bucket++)
            {
                // 기대값의 ±5%. 10,000 표본이면 우연한 편차는 이보다 훨씬 작다.
                Assert.InRange(counts[bucket], (int)(expected * 0.95), (int)(expected * 1.05));
            }
        }

        /// 상태를 남겨 두면 같은 자리에서 수열을 이어 갈 수 있다. 배치를 재현하거나
        /// 중간부터 다시 돌리는 데 쓴다.
        [Fact]
        public void 상태를_옮기면_수열이_이어진다()
        {
            var original = new DeterministicSequence(31337);

            for (var index = 0; index < 10; index++)
            {
                original.NextUInt();
            }

            var resumed = new DeterministicSequence(original.State);

            for (var index = 0; index < 32; index++)
            {
                Assert.Equal(original.NextUInt(), resumed.NextUInt());
            }
        }
    }
}
