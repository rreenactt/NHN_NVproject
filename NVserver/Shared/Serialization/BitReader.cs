using System;

namespace NV.Shared.Serialization
{
    /// BitWriter 와 짝을 이루는 판독기. 비트 순서와 엔디언 규칙이 같다.
    public ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _buffer;
        private int _bitPosition;

        public BitReader(ReadOnlySpan<byte> buffer)
        {
            _buffer = buffer;
            _bitPosition = 0;
        }

        public int BitPosition => _bitPosition;

        public int BytesRead => (_bitPosition + 7) / 8;

        public uint ReadBits(int bitCount)
        {
            if (bitCount <= 0 || bitCount > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(bitCount), bitCount, "1..32 만 허용한다.");
            }

            if (_bitPosition + bitCount > _buffer.Length * 8)
            {
                throw new InvalidOperationException("읽을 비트가 남아 있지 않다.");
            }

            var value = 0u;
            for (var offset = 0; offset < bitCount; offset++)
            {
                var position = _bitPosition + offset;
                var byteIndex = position >> 3;
                var bitIndex = position & 7;

                if ((_buffer[byteIndex] & (1 << bitIndex)) != 0)
                {
                    value |= 1u << offset;
                }
            }

            _bitPosition += bitCount;
            return value;
        }

        public bool ReadBool()
        {
            return ReadBits(1) != 0u;
        }

        public byte ReadByte()
        {
            return (byte)ReadBits(8);
        }

        public sbyte ReadSByte()
        {
            return unchecked((sbyte)(byte)ReadBits(8));
        }

        public ushort ReadUInt16()
        {
            return (ushort)ReadBits(16);
        }

        public short ReadInt16()
        {
            return unchecked((short)(ushort)ReadBits(16));
        }

        public uint ReadUInt32()
        {
            return ReadBits(32);
        }

        public void AlignToByte()
        {
            var remainder = _bitPosition & 7;
            if (remainder != 0)
            {
                ReadBits(8 - remainder);
            }
        }
    }
}
