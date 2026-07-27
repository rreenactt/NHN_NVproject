using System;

namespace NV.Shared.Serialization
{
    /// 비트 단위 기록기. 비트는 각 바이트의 LSB 부터 채운다.
    /// 바이트 정렬 상태에서 16·32비트를 쓰면 리틀엔디언이 된다.
    public ref struct BitWriter
    {
        private readonly Span<byte> _buffer;
        private int _bitPosition;

        public BitWriter(Span<byte> buffer)
        {
            _buffer = buffer;
            _bitPosition = 0;
        }

        public int BitPosition => _bitPosition;

        public int BytesWritten => (_bitPosition + 7) / 8;

        public void WriteBits(uint value, int bitCount)
        {
            if (bitCount <= 0 || bitCount > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(bitCount), bitCount, "1..32 만 허용한다.");
            }

            if (_bitPosition + bitCount > _buffer.Length * 8)
            {
                throw new InvalidOperationException("버퍼가 부족하다.");
            }

            for (var offset = 0; offset < bitCount; offset++)
            {
                var position = _bitPosition + offset;
                var byteIndex = position >> 3;
                var bitIndex = position & 7;
                var mask = (byte)(1 << bitIndex);

                if (((value >> offset) & 1u) != 0u)
                {
                    _buffer[byteIndex] |= mask;
                }
                else
                {
                    _buffer[byteIndex] &= (byte)~mask;
                }
            }

            _bitPosition += bitCount;
        }

        public void WriteBool(bool value)
        {
            WriteBits(value ? 1u : 0u, 1);
        }

        public void WriteByte(byte value)
        {
            WriteBits(value, 8);
        }

        public void WriteSByte(sbyte value)
        {
            WriteBits(unchecked((byte)value), 8);
        }

        public void WriteUInt16(ushort value)
        {
            WriteBits(value, 16);
        }

        public void WriteInt16(short value)
        {
            WriteBits(unchecked((ushort)value), 16);
        }

        public void WriteUInt32(uint value)
        {
            WriteBits(value, 32);
        }

        /// 다음 바이트 경계까지 0 으로 채운다.
        public void AlignToByte()
        {
            var remainder = _bitPosition & 7;
            if (remainder != 0)
            {
                WriteBits(0u, 8 - remainder);
            }
        }
    }
}
