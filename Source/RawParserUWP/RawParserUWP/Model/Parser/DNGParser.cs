﻿using System;
using System.Collections.Generic;
using System.IO;
using RawParserUWP.Model.Format.Image;
using RawParserUWP.Model.Format.Reader;

namespace RawParserUWP.Model.Parser
{
    class DNGParser : Parser
    {
        private TIFFBinaryReader fileStream;
        private Header header;

        public override RawImage parse(Stream s)
        {
            throw new NotImplementedException();
        }

        public override Dictionary<ushort, Tag> parseExif()
        {
            throw new NotImplementedException();
        }

        public override byte[] parsePreview()
        {
            throw new NotImplementedException();
        }

        public override ushort[] parseRAWImage()
        {
            throw new NotImplementedException();
        }

        public override byte[] parseThumbnail()
        {
            IFD ifd = new IFD(fileStream, header.TIFFoffset, false, false);
            return null;
        }

        public override void setStream(Stream s)
        {
            fileStream = new TIFFBinaryReader(s);

            Header header = new Header(fileStream, 0);
            if (header.byteOrder == 0x4D4D)
            {
                //File is in reverse bit order
                fileStream = new TIFFBinaryReaderRE(s, System.Text.Encoding.BigEndianUnicode);
            }
            header = new Header(fileStream, 0);
        }
    }
}
