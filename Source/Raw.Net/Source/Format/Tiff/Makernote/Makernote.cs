﻿namespace RawNet
{
    class Makernote : IFD
    {
        public Makernote() { }

        public Makernote(byte[] data, uint offset, Endianness endian, int depth, uint parentOffset)
        {
            TIFFBinaryReader file;

            if (endian == Endianness.little)
            {
                file = new TIFFBinaryReader(data);
            }
            else if (endian == Endianness.big)
            {
                file = new TIFFBinaryReaderRE(data);
            }
            else
            {
                throw new RawDecoderException("Endianess not correct " + endian);
            }
            file.BaseStream.Position = offset;
            relativeOffset = -(int)parentOffset;
            Parse(file);
            file.Dispose();
        }
    }
}
