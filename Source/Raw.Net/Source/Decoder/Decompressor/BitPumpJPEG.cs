using System;
using System.IO;

namespace RawNet
{
    // Note: Allocated buffer MUST be at least size+sizeof(UInt32) large.
    internal class BitPumpJPEG
    {
        int BITS_PER_LONG = (8 * sizeof(UInt32));
        int MIN_GET_BITS;   /* max value for long getBuffer */
        byte[] buffer;
        byte[] current_buffer = new byte[24];
        UInt32 size = 0;            // This if the end of buffer.
        int mLeft = 0;
        UInt32 off;                  // Offset in bytes
        int stuffed = 0;              // How many bytes has been stuffed?

        /*** Used for entropy encoded sections ***/
        public BitPumpJPEG(ref TIFFBinaryReader s) : this(ref s, (uint)s.Position, (uint)s.BaseStream.Length) { }

        /*** Used for entropy encoded sections ***/
        public BitPumpJPEG(ref TIFFBinaryReader s, uint offset, uint count)
        {
            MIN_GET_BITS = (BITS_PER_LONG - 7);
            size = (uint)(s.GetRemainSize() + sizeof(UInt32));
            buffer = new byte[size];
            s.BaseStream.Position = offset;
            s.Read(buffer, 0, s.GetRemainSize());
            Init();
        }

        public BitPumpJPEG(byte[] _buffer, UInt32 _size)
        {
            buffer = _buffer;
            size = _size + sizeof(UInt32);
            Init();
        }

        public void Init()
        {
            Fill();
        }

        public void FillNoCheck()
        {
            // Fill in 96 bits
            //int[] b = Common.convertByteToInt(current_buffer);
            if ((off + 12) >= size)
            {
                while (mLeft <= 64 && off < size)
                {
                    for (int i = (mLeft >> 3); i >= 0; i--)
                        current_buffer[i + 1] = current_buffer[i];
                    byte val = buffer[off++];
                    if (val == 0xff)
                    {
                        if (buffer[off] == 0)
                            off++;
                        else
                        {
                            // We hit another marker - don't forward bitpump anymore
                            val = 0;
                            off--;
                            stuffed++;
                        }
                    }
                    current_buffer[0] = val;
                    mLeft += 8;
                }
                while (mLeft < 64)
                {
                    current_buffer[11] = current_buffer[7];
                    current_buffer[10] = current_buffer[6];
                    current_buffer[9] = current_buffer[5];
                    current_buffer[8] = current_buffer[4];

                    current_buffer[7] = current_buffer[3];
                    current_buffer[6] = current_buffer[2];
                    current_buffer[5] = current_buffer[1];
                    current_buffer[4] = current_buffer[0];

                    current_buffer[3] = 0;
                    current_buffer[2] = 0;
                    current_buffer[1] = 0;
                    current_buffer[0] = 0;
                    mLeft += 32;
                    stuffed += 4;  //We are adding to mLeft without incrementing offset
                }
                return;
            }
            current_buffer[15] = current_buffer[3];
            current_buffer[14] = current_buffer[2];
            current_buffer[13] = current_buffer[1];
            current_buffer[12] = current_buffer[0];

            for (int i = 0; i < 12; i++)
            {
                byte val = buffer[off++];
                if (val == 0xff)
                {
                    if (buffer[off] == 0)
                        off++;
                    else
                    {
                        val = 0;
                        off--;
                        stuffed++;
                    }
                }
                current_buffer[11 - i] = val;
            }
            mLeft += 96;
        }

        public UInt32 GetOffset()
        {
            return (uint)(off - (mLeft >> 3) + stuffed);
        }

        public void CheckPos()
        {
            if (off >= size || stuffed > (mLeft >> 3))
            {
                throw new IOException("Out of buffer read");
            }
        }        // Check if we have a valid position

        // Fill the buffer with at least 24 bits
        public void Fill()
        {
            if (mLeft < 25) FillNoCheck();
        }

        public UInt32 PeekBitsNoFill(UInt32 nbits)
        {
            int shift = (int)(mLeft - nbits);
            UInt32 ret = current_buffer[shift >> 3] | (uint)current_buffer[(shift >> 3) + 1] << 8 | (uint)current_buffer[(shift >> 3) + 2] << 16 | (uint)current_buffer[(shift >> 3) + 3] << 24;
            ret >>= shift & 7;
            return (uint)(ret & ((1 << (int)nbits) - 1));
        }

        public UInt32 GetBit()
        {
            if (mLeft == 0) FillNoCheck();
            mLeft--;
            UInt32 _byte = (uint)(mLeft >> 3);
            return (uint)(current_buffer[_byte] >> (mLeft & 0x7)) & 1;
        }

        public UInt32 GetBitsNoFill(UInt32 nbits)
        {
            UInt32 ret = PeekBitsNoFill(nbits);
            mLeft -= (int)nbits;
            return ret;
        }

        public UInt32 GetBits(UInt32 nbits)
        {
            Fill();
            return GetBitsNoFill(nbits);
        }

        public UInt32 PeekBit()
        {
            if (mLeft == 0) FillNoCheck();
            return (uint)(current_buffer[(mLeft - 1) >> 3] >> ((mLeft - 1) & 0x7)) & 1;
        }

        public UInt32 GetBitNoFill()
        {
            mLeft--;
            UInt32 ret = (uint)(current_buffer[mLeft >> 3] >> (mLeft & 0x7)) & 1;
            return ret;
        }

        public UInt32 PeekByteNoFill()
        {
            int shift = mLeft - 8;
            UInt32 ret = current_buffer[shift >> 3] | (uint)current_buffer[(shift >> 3) + 1] << 8 | (uint)current_buffer[(shift >> 3) + 2] << 16 | (uint)current_buffer[(shift >> 3) + 3] << 24;
            ret >>= shift & 7;
            return ret & 0xff;
        }

        public UInt32 PeekBits(UInt32 nbits)
        {
            Fill();
            return PeekBitsNoFill(nbits);
        }

        public UInt32 PeekByte()
        {
            Fill();
            if (off > size)
                throw new IOException("Out of buffer read");

            return PeekByteNoFill();
        }

        public void SkipBits(UInt32 nbits)
        {
            int skipn = (int)nbits;
            while (skipn != 0)
            {
                Fill();
                CheckPos();
                int n = Math.Min(skipn, mLeft);
                mLeft -= n;
                skipn -= n;
            }
        }

        public void SkipBitsNoFill(uint nbits)
        {
            mLeft -= (int)nbits;
        }

        public byte GetByte()
        {
            Fill();
            mLeft -= 8;
            int shift = mLeft;
            UInt32 ret = current_buffer[shift >> 3];
            ret >>= shift & 7;
            return (byte)(ret & 0xff);
        }

        public UInt32 GetBitSafe()
        {
            Fill();
            CheckPos();
            return GetBitNoFill();
        }

        public UInt32 GetBitsSafe(uint nbits)
        {
            if (nbits > MIN_GET_BITS)
                throw new IOException("Too many bits requested");

            Fill();
            CheckPos();
            return GetBitsNoFill(nbits);
        }

        public byte GetByteSafe()
        {
            Fill();
            CheckPos();
            return (byte)GetBitsNoFill(8);
        }

        public void SetAbsoluteOffset(uint offset)
        {
            if (offset >= size)
                throw new IOException("Offset set out of buffer");

            mLeft = 0;
            off = offset;
            FillNoCheck();
        }
    }
}




