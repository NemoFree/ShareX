﻿#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (C) 2008-2014 ShareX Developers

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using HelpersLib;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;

namespace ScreenCaptureLib
{
    public class ScreenRecorder : IDisposable
    {
        public bool IsRecording { get; private set; }
        public bool WriteCompressed { get; set; }

        public int FPS
        {
            get
            {
                return fps;
            }
            set
            {
                if (!IsRecording)
                {
                    fps = value;
                    UpdateInfo();
                }
            }
        }

        public float DurationSeconds
        {
            get
            {
                return durationSeconds;
            }
            set
            {
                if (!IsRecording)
                {
                    durationSeconds = value;
                    UpdateInfo();
                }
            }
        }

        public Rectangle CaptureRectangle
        {
            get
            {
                return captureRectangle;
            }
            private set
            {
                if (!IsRecording)
                {
                    captureRectangle = value;
                }
            }
        }

        public string CachePath { get; private set; }

        public ScreenRecordOutput OutputType { get; private set; }

        public AVIOptions Options { get; set; }

        public delegate void ProgressEventHandler(int progress);

        public event ProgressEventHandler EncodingProgressChanged;

        private int fps, delay, frameCount;
        private float durationSeconds;
        private Rectangle captureRectangle;
        private HardDiskCache hdCache;
        private ImageRecorder imageRecorder;
        private bool stopRequest;

        public ScreenRecorder(int fps, float durationSeconds, Rectangle captureRectangle, string cachePath, ScreenRecordOutput outputType, AVICOMPRESSOPTIONS compressOptions)
        {
            if (string.IsNullOrEmpty(cachePath))
            {
                throw new Exception("Screen recorder cache path is empty.");
            }

            FPS = fps;
            DurationSeconds = durationSeconds;
            CaptureRectangle = captureRectangle;
            CachePath = cachePath;
            OutputType = outputType;

            switch (OutputType)
            {
                case ScreenRecordOutput.AVI:
                case ScreenRecordOutput.FFmpeg:
                    Options = new AVIOptions
                    {
                        CompressOptions = compressOptions,
                        FPS = FPS,
                        OutputPath = CachePath,
                        Size = CaptureRectangle.Size
                    };
                    break;
                case ScreenRecordOutput.GIF:
                    hdCache = new HardDiskCache(CachePath);
                    break;
                default:
                    throw new Exception("Not all possible ScreenRecordOutput types are handled.");
            }

            if (Options != null)
            {
                if (OutputType == ScreenRecordOutput.AVI)
                {
                    imageRecorder = new AVICache(Options);
                }
                else if (OutputType == ScreenRecordOutput.FFmpeg)
                {
                    imageRecorder = new FFmpegRecorder(Options);
                }
            }
        }

        private void UpdateInfo()
        {
            delay = 1000 / fps;
            frameCount = (int)(fps * durationSeconds);
        }

        public void StartRecording()
        {
            if (!IsRecording)
            {
                IsRecording = true;
                stopRequest = false;

                for (int i = 0; !stopRequest && (frameCount == 0 || i < frameCount); i++)
                {
                    Stopwatch timer = Stopwatch.StartNew();

                    Image img = Screenshot.CaptureRectangle(CaptureRectangle);
                    //DebugHelper.WriteLine("Screen capture: " + (int)timer.ElapsedMilliseconds);

                    if (OutputType == ScreenRecordOutput.AVI || OutputType == ScreenRecordOutput.FFmpeg)
                    {
                        imageRecorder.AddImageAsync(img);
                    }
                    else if (OutputType == ScreenRecordOutput.GIF)
                    {
                        hdCache.AddImageAsync(img);
                    }

                    if (!stopRequest && (frameCount == 0 || i + 1 < frameCount))
                    {
                        int sleepTime = delay - (int)timer.ElapsedMilliseconds;

                        if (sleepTime > 0)
                        {
                            Thread.Sleep(sleepTime);
                        }
                        else if (sleepTime < 0)
                        {
                            //DebugHelper.WriteLine("FPS drop: " + -sleepTime);
                        }
                    }
                }

                switch (OutputType)
                {
                    case ScreenRecordOutput.AVI:
                    case ScreenRecordOutput.FFmpeg:
                        imageRecorder.Finish();
                        break;
                    case ScreenRecordOutput.GIF:
                        hdCache.Finish();
                        break;
                    default:
                        throw new Exception("Not all possible ScreenRecordOutput types are handled.");
                }
            }

            IsRecording = false;
        }

        public void StopRecording()
        {
            stopRequest = true;
        }

        public void SaveAsGIF(string path, GIFQuality quality)
        {
            if (!IsRecording)
            {
                using (GifCreator gifEncoder = new GifCreator(delay))
                {
                    int i = 0;
                    int count = hdCache.Count;

                    foreach (Image img in hdCache.GetImageEnumerator())
                    {
                        i++;
                        OnEncodingProgressChanged((int)((float)i / count * 100));

                        using (img)
                        {
                            gifEncoder.AddFrame(img, quality);
                        }
                    }

                    gifEncoder.Finish();
                    gifEncoder.Save(path);
                }
            }
        }

        public void EncodeUsingCommandLine(VideoEncoder encoder, string targetFilePath)
        {
            if (!string.IsNullOrEmpty(CachePath) && File.Exists(CachePath))
            {
                OnEncodingProgressChanged(-1);
                encoder.Encode(CachePath, targetFilePath);
                OnEncodingProgressChanged(100);
            }
        }

        protected void OnEncodingProgressChanged(int progress)
        {
            if (EncodingProgressChanged != null)
            {
                EncodingProgressChanged(progress);
            }
        }

        public void Dispose()
        {
            if (hdCache != null)
            {
                hdCache.Dispose();
            }

            if (imageRecorder != null)
            {
                imageRecorder.Dispose();
            }
        }
    }
}