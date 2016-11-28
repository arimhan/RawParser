#ifndef DNG_DECODER_SLICES_H
#define DNG_DECODER_SLICES_H

#include "RawDecoder.h"
#include <queue>
#include "LJpegPlain.h"
/* 
    RawSpeed - RAW file decoder.

    Copyright (C) 2009-2014 Klaus Post

    This library is free software; you can redistribute it and/or
    modify it under the terms of the GNU Lesser General Public
    License as published by the Free Software Foundation; either
    version 2 of the License, or (at your option) any later version.

    This library is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
    Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public
    License along with this library; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA

    http://www.klauspost.com
*/

namespace RawSpeed {

class DngSliceElement
{
public:
  DngSliceElement(UInt32 off, UInt32 count, UInt32 offsetX, UInt32 offsetY) : 
      byteOffset(off), byteCount(count), offX(offsetX), offY(offsetY), mUseBigtable(false) {};
  ~DngSliceElement(void) {};
  UInt32 byteOffset;
  UInt32 byteCount;
  UInt32 offX;
  UInt32 offY;
  bool mUseBigtable;
};
class DngDecoderSlices;

class DngDecoderThread
{
public:
  DngDecoderThread(void) {}
  ~DngDecoderThread(void) {}
#ifndef NO_PTHREAD
  pthread_t threadid;
#endif
  queue<DngSliceElement> slices;
  DngDecoderSlices* parent;
};


class DngDecoderSlices
{
public:
  DngDecoderSlices(FileMap* file, RawImage img, int compression );
  ~DngDecoderSlices(void);
  void addSlice(DngSliceElement slice);
  void startDecoding();
  void decodeSlice(DngDecoderThread* t);
  int size();
  queue<DngSliceElement> slices;
  vector<DngDecoderThread*> threads;
  FileMap *mFile; 
  RawImage mRaw;
  bool mFixLjpeg;
  UInt32 nThreads;
  int compression;
};

} // namespace RawSpeed

#endif
#include "StdAfx.h"
#include "DngDecoderSlices.h"
/*
    RawSpeed - RAW file decoder.

    Copyright (C) 2009-2014 Klaus Post

    This library is free software; you can redistribute it and/or
    modify it under the terms of the GNU Lesser General Public
    License as published by the Free Software Foundation; either
    version 2 of the License, or (at your option) any later version.

    This library is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
    Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public
    License along with this library; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA

    http://www.klauspost.com
*/

namespace RawSpeed {

void *DecodeThread(void *_this) {
  DngDecoderThread* me = (DngDecoderThread*)_this;
  DngDecoderSlices* parent = me.parent;
  try {
    parent.decodeSlice(me);
  } catch (...) {
    parent.mRaw.setError("DNGDEcodeThread: Caught exception.");
  }
  return null;
}


DngDecoderSlices::DngDecoderSlices(FileMap* file, RawImage img, int _compression) :
    mFile(file), mRaw(img) {
  mFixLjpeg = false;
  compression = _compression;
}

DngDecoderSlices::~DngDecoderSlices(void) {
}

void DngDecoderSlices::addSlice(DngSliceElement slice) {
  slices.push(slice);
}

void DngDecoderSlices::startDecoding() {
#ifdef NO_PTHREAD
  DngDecoderThread t;
  while (!slices.empty()) {
    t.slices.push(slices.front());
    slices.pop();
  }
  t.parent = this;
  DecodeThread(&t);
#else
  // Create threads

  nThreads = getThreadCount();
  int slicesPerThread = ((int)slices.size() + nThreads - 1) / nThreads;
//  decodedSlices = 0;
  pthread_attr_t attr;
  /* Initialize and set thread detached attribute */
  pthread_attr_init(&attr);
  pthread_attr_setdetachstate(&attr, PTHREAD_CREATE_JOINABLE);

  for (UInt32 i = 0; i < nThreads; i++) {
    DngDecoderThread* t = new DngDecoderThread();
    for (int j = 0; j < slicesPerThread ; j++) {
      if (!slices.empty()) {
        t.slices.push(slices.front());
        slices.pop();
      }
    }
    t.parent = this;
    pthread_create(&t.threadid, &attr, DecodeThread, t);
    threads.push_back(t);
  }
  pthread_attr_destroy(&attr);

  void *status;
  for (UInt32 i = 0; i < nThreads; i++) {
    pthread_join(threads[i].threadid, &status);
    delete(threads[i]);
  }
#endif
}

#if JPEG_LIB_VERSION < 80

#define JPEG_MEMSRC(A,B,C) jpeg_mem_src_int(A,B,C)
/* Read JPEG image from a memory segment */

static void init_source (j_decompress_ptr cinfo) {}
static boolean fill_input_buffer (j_decompress_ptr cinfo)
{
  struct jpeg_source_mgr* src = (struct jpeg_source_mgr*) cinfo.src;
  return !!src.bytes_in_buffer;
}
static void skip_input_data (j_decompress_ptr cinfo, long num_bytes)
{
  struct jpeg_source_mgr* src = (struct jpeg_source_mgr*) cinfo.src;

  if (num_bytes > (int)src.bytes_in_buffer)
    ThrowIOE("JPEG Decoder - read out of buffer");
  if (num_bytes > 0) {
    src.next_input_byte += (size_t) num_bytes;
    src.bytes_in_buffer -= (size_t) num_bytes;
  }
}
static void term_source (j_decompress_ptr cinfo) {}
static void jpeg_mem_src_int (j_decompress_ptr cinfo, unsigned stringbuffer, long nbytes)
{
  struct jpeg_source_mgr* src;

  if (cinfo.src == null) {   /* first time for this JPEG object? */
    cinfo.src = (struct jpeg_source_mgr *)
      (*cinfo.mem.alloc_small) ((j_common_ptr) cinfo, JPOOL_PERMANENT,
      sizeof(struct jpeg_source_mgr));
  }

  src = (struct jpeg_source_mgr*) cinfo.src;
  src.init_source = init_source;
  src.fill_input_buffer = fill_input_buffer;
  src.skip_input_data = skip_input_data;
  src.resync_to_restart = jpeg_resync_to_restart; /* use default method */
  src.term_source = term_source;
  src.bytes_in_buffer = nbytes;
  src.next_input_byte = (JOCTET*)buffer;
}
#else
#define JPEG_MEMSRC(A,B,C) jpeg_mem_src(A,B,C)
#endif

METHODDEF(void)
my_error_throw (j_common_ptr cinfo)
{
  ThrowRDE("JPEG decoder error!");
} 


void DngDecoderSlices::decodeSlice(DngDecoderThread* t) {
  if (compression == 7) {
    while (!t.slices.empty()) {
      LJpegPlain l(mFile, mRaw);
      l.mDNGCompatible = mFixLjpeg;
      DngSliceElement e = t.slices.front();
      l.mUseBigtable = e.mUseBigtable;
      t.slices.pop();
      try {
        l.startDecoder(e.byteOffset, e.byteCount, e.offX, e.offY);
      } catch (RawDecoderException &err) {
        mRaw.setError(err.what());
      } catch (IOException &err) {
        mRaw.setError(err.what());
      }
    }
    /* Lossy DNG */
  } else if (compression == 0x884c) {
    /* Each slice is a JPEG image */
    struct jpeg_decompress_struct dinfo;
    struct jpeg_error_mgr jerr;
    while (!t.slices.empty()) {
      DngSliceElement e = t.slices.front();
      t.slices.pop();
      byte *complete_buffer = null;
      JSAMPARRAY buffer = (JSAMPARRAY)malloc(sizeof(JSAMPROW));

      try {
        jpeg_create_decompress(&dinfo);
        dinfo.err = jpeg_std_error(&jerr);
        jerr.error_exit = my_error_throw;
        JPEG_MEMSRC(&dinfo, (unsigned char*)mFile.getData(e.byteOffset, e.byteCount), e.byteCount);

        if (JPEG_HEADER_OK != jpeg_read_header(&dinfo, true))
          ThrowRDE("DngDecoderSlices: Unable to read JPEG header");

        jpeg_start_decompress(&dinfo);
        if (dinfo.output_components != (int)mRaw.getCpp())
          ThrowRDE("DngDecoderSlices: Component count doesn't match");
        int row_stride = dinfo.output_width * dinfo.output_components;
        int pic_size = dinfo.output_height * row_stride;
        complete_buffer = (byte8*)_aligned_malloc(pic_size, 16);
        while (dinfo.output_scanline < dinfo.output_height) {
          buffer[0] = (JSAMPROW)(&complete_buffer[dinfo.output_scanline*row_stride]);
          if (0 == jpeg_read_scanlines(&dinfo, buffer, 1))
            ThrowRDE("DngDecoderSlices: JPEG Error while decompressing image.");
        }
        jpeg_finish_decompress(&dinfo);

        // Now the image is decoded, and we copy the image data
        int copy_w = Math.Math.Min(((mRaw.dim.x-e.offX, dinfo.output_width);
        int copy_h = Math.Math.Min(((mRaw.dim.y-e.offY, dinfo.output_height);
        for (int y = 0; y < copy_h; y++) {
          byte[] src = &complete_buffer[row_stride*y];
          UInt16* dst = (UInt16*)mRaw.getData(e.offX, y+e.offY);
          for (int x = 0; x < copy_w; x++) {
            for (int c=0; c < dinfo.output_components; c++)
              *dst++ = (*src++);
          }
        }
      } catch (RawDecoderException &err) {
        mRaw.setError(err.what());
      } catch (IOException &err) {
        mRaw.setError(err.what());
      }
      free(buffer);
      if (complete_buffer)
        _aligned_free(complete_buffer);
      jpeg_destroy_decompress(&dinfo);
    }
  }
  else
    mRaw.setError("DngDecoderSlices: Unknown compression");
}

int DngDecoderSlices::size() {
  return (int)slices.size();
}


} // namespace RawSpeed