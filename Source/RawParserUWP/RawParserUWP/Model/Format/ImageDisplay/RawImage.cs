﻿using RawParser.Model.Format;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace RawParser.Model.ImageDisplay
{

    class RawImage
    {
        private string fileName { get; set; }
        public Dictionary<ushort, Tag> exif { get; set; }
        public byte[] imageData { set; get; }
        public byte[] imagePreviewData { get; set; }
        public uint height;
        public uint width;

        public RawImage(Dictionary<ushort, Tag> e, byte[] d, byte[] p, uint h, uint w)
        {
            exif = e;
            imageData = d;
            imagePreviewData = p;
            height = h;
            width = w;
        }

        public SoftwareBitmap getImageAsBitmap(bool raw)
        {
            MemoryStream ms = new MemoryStream();
            if (raw)
            {
                ms.Write(imageData, 0, imageData.Length);
            }
            else
            {
                ms.Write(imagePreviewData, 0, imagePreviewData.Length);
            }
            ms.Position = 0; //reset the stream after populate
            var decoder = BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());

            Task t = Task.Run(() =>
            {
                while (decoder.Status == AsyncStatus.Started) { }
            });
            t.Wait();
            if (decoder.Status == AsyncStatus.Error)
            {
                throw decoder.ErrorCode;
            }

            var bitmapasync = decoder.GetResults().GetSoftwareBitmapAsync();
            t = Task.Run(() =>
                    {
                        while (bitmapasync.Status == AsyncStatus.Started) { }
                    });
            t.Wait();
            if (bitmapasync.Status == AsyncStatus.Error)
            {
                throw bitmapasync.ErrorCode;
            }

            return bitmapasync.GetResults(); ;
        }
    }
}
