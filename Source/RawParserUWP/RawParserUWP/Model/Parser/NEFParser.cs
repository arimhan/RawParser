﻿using RawParser.Model.Format;
using RawParser.Model.Format.Nikon;
using RawParser.Model.ImageDisplay;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

namespace RawParser.Model.Parser
{
    class NEFParser : Parser
    {
        protected IFD ifd;
        protected Header header;
        protected IFD subifd0;
        protected IFD subifd1;
        protected IFD exif;
        protected NikonMakerNote makerNote;

        protected BitArray rawData;
        protected byte[] previewData;

        public RawImage parse(Stream file)
        {
            //Open a binary stream on the file
            BinaryReader fileStream = new BinaryReader(file);

            //read the first bit to get the endianness of the file           
            if (fileStream.ReadUInt16() == 0x4D4D)
            {
                //File is in reverse bit order
                fileStream = new BinaryReaderBE(file);
            }

            //read the header
            header = new Header(fileStream, 0); // OK

            //Read the IFD
            ifd = new IFD(fileStream, header.TIFFoffset, true); // OK

            Tag subifdoffsetTag;
            Tag exifoffsetTag;
            if (!ifd.tags.TryGetValue(0x14A, out subifdoffsetTag)) throw new FormatException("File not correct");
            if (!ifd.tags.TryGetValue(0x8769, out exifoffsetTag)) throw new FormatException("File not correct");

            subifd0 = new IFD(fileStream, (uint)subifdoffsetTag.data[0], true);
            subifd1 = new IFD(fileStream, (uint)subifdoffsetTag.data[1], true);
            //todo third IFD
            exif = new IFD(fileStream, (uint)exifoffsetTag.data[0], true); //OK

            //optimize (stop ifd from loaoding the makernote

            Tag makerNoteOffsetTag;
            if (!exif.tags.TryGetValue(0x927C, out makerNoteOffsetTag)) throw new FormatException("File not correct");

            makerNote = new NikonMakerNote(fileStream, makerNoteOffsetTag.dataOffset, true);

            //Get image data
            Tag imagepreviewOffsetTags, imagepreviewX, imagepreviewY, imagepreviewSize;
            if (!subifd0.tags.TryGetValue(0x201, out imagepreviewOffsetTags)) throw new FormatException("File not correct");
            if (!subifd0.tags.TryGetValue(0x11A, out imagepreviewX)) throw new FormatException("File not correct");
            if (!subifd0.tags.TryGetValue(0x11B, out imagepreviewY)) throw new FormatException("File not correct");
            if (!subifd0.tags.TryGetValue(0x202, out imagepreviewSize)) throw new FormatException("File not correct");

            Tag imageRAWOffsetTags, imageRAWWidth, imageRAWHeight, imageRAWSize, imageRAWCompressed, imageRAWDepth;
            if (!subifd1.tags.TryGetValue(0x0111, out imageRAWOffsetTags)) throw new FormatException("File not correct");
            if (!subifd1.tags.TryGetValue(0x0100, out imageRAWWidth)) throw new FormatException("File not correct");
            if (!subifd1.tags.TryGetValue(0x0101, out imageRAWHeight)) throw new FormatException("File not correct");
            if (!subifd1.tags.TryGetValue(0x0102, out imageRAWDepth)) throw new FormatException("File not correct");
            if (!subifd1.tags.TryGetValue(0x0117, out imageRAWSize)) throw new FormatException("File not correct");
            if (!subifd1.tags.TryGetValue(0x0117, out imageRAWCompressed)) throw new FormatException("File not correct");

            //get the preview data ( faster than rezising )
            fileStream.BaseStream.Position = (uint)imagepreviewOffsetTags.data[0];
            previewData = fileStream.ReadBytes(Convert.ToInt32(imagepreviewSize.data[0]));

            //get Raw Data            
            fileStream.BaseStream.Position = (uint)imageRAWOffsetTags.data[0];
            rawData = new BitArray(fileStream.ReadBytes(Convert.ToInt32(imageRAWSize.data[0])));

            //Check if uncompressed
            if ((uint)imageRAWCompressed.data[0] == 34713)
            {
                Tag compressionType;
                if (!makerNote.ifd.tags.TryGetValue(0x0093, out compressionType)) throw new FormatException("File not correct");
                //uncompress the image
                rawData = uncompressed(new BitArray(rawData), (int)imageRAWHeight.data[0], (int)imageRAWWidth.data[0], (ushort)compressionType.data[0], (ushort)imageRAWDepth.data[0]);
            }
            //parse to RawImage
            Dictionary<ushort, Tag> exifTag = parseToStandardExifTag();
            RawImage rawImage = new RawImage(exifTag, rawData, previewData, (uint)imageRAWHeight.data[0], (uint)imageRAWWidth.data[0], (ushort)imageRAWDepth.data[0]);

            return rawImage;
        }

        /*
         * Only lossless for the moment
         * 
         */
        private BitArray uncompressed(BitArray rawData, int height, int width, ushort compressionType, ushort colordepth)
        {
            byte[] uncompressedData = new byte[height * width]; //add pixel*
            //decompress the linearisationtable
            Tag lineTag = new Tag();
            makerNote.ifd.tags.TryGetValue(0x0096, out lineTag);
            LinearisationTable line = new LinearisationTable(lineTag.data, compressionType, colordepth);

            ushort[] huff;

            int tree = 0, row, col, len, shl, diff;

            if (line.version0 == 0x46) tree = 2;
            if (colordepth == 14) tree += 3;


            while (line.curve[line.max - 2] == line.curve[line.max - 1]) line.max--;
            huff = line.makeDecoder(tree);
            //fseek(ifp, data_offset, SEEK_SET);
            //getbits(-1);

            int i = 0;
            for (line.min = row = 0; row < height; row++)
            {
                if (line.splitValue > 1 && row == line.splitValue)
                {
                    huff = line.makeDecoder(tree + 1);
                    line.max += (line.min = 16) << 1;
                }
                for (col = 0; col < width; col++)
                {
                    i = (int)line.gethuff(huff);
                    len = i & 15;
                    shl = i >> 4;
                    diff = (int)((line.getbithuff(len - shl, null) << 1) + 1) << shl >> 1;
                    if ((diff & (1 << (len - 1))) == 0)
                        diff -= (1 << len) - ((shl != 0) ? 1 : 1);
                    if (col < 2)
                    {
                        line.hpred[col] = (ushort)((line.vpreds[row & 1][col]) + (short)diff);
                    }
                    else
                    {
                        line.hpred[col & 1] += (ushort)diff;
                    }
                    if ((ushort)(line.hpred[col & 1] + line.min) >= line.max) throw new Exception("Error during deflate");

                    //TODO
                    //RAW(row, col) = line.curve[LIM((short)line.hpred[col & 1], 0, 0x3fff)];
                }
            }
            return rawData;
        }

        public Dictionary<ushort, Tag> parseToStandardExifTag()
        {
            Dictionary<ushort, Tag> temp = new Dictionary<ushort, Tag>();
            Dictionary<ushort, ushort> nikonToStandard = new DictionnaryFromFileUShort(@"Assets\Dic\NikonToStandard.dic");
            Dictionary<ushort, string> standardExifName = new DictionnaryFromFileString(@"Assets\\Dic\StandardExif.dic");
            foreach (ushort exifTag in standardExifName.Keys)
            {
                Tag tempTag;
                ushort nikonTagId;
                if (nikonToStandard.TryGetValue(exifTag, out nikonTagId))
                {
                    ifd.tags.TryGetValue(nikonTagId, out tempTag);
                    makerNote.ifd.tags.TryGetValue(nikonTagId, out tempTag);
                    subifd0.tags.TryGetValue(nikonTagId, out tempTag);
                    subifd1.tags.TryGetValue(nikonTagId, out tempTag);
                    if (tempTag == null)
                    {
                        tempTag = new Tag();
                        tempTag.dataType = 2;
                        tempTag.data[0] = "";
                    }
                    string t = "";
                    standardExifName.TryGetValue(exifTag, out t);
                    tempTag.displayName = t;

                    temp.Add(nikonTagId, tempTag);
                }
            }
            return temp;
        }
    }
}
