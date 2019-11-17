using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PCSGU250_saver
{
    class Program
    {
        [DllImport("PCSGU250.dll")]
        public static extern void ReadCh1(ref int Buffer);

        [DllImport("PCSGU250.dll")]
        public static extern void ReadCh2(ref int Buffer);

        [DllImport("PCSGU250.dll")]
        public static extern bool DataReady();

        [DllImport("PCSGU250.dll")]
        public static extern int Start_PCSGU250();

        const int iBuf_Freq_Hz = 0;
        const int iBuf_Volt_mV = 1;
        const int iBuf_GndLevel_ADCcounts = 2;
        const int nBuf_Samples = 4096;
        const int iBuf_SamplesBeg = 3;
        const int iBuf_SamplesEnd = iBuf_SamplesBeg + nBuf_Samples;
        const int iBuf_TrigPoint = 1018;

        class Channel
        {
            int ch;
            int[] dataBuf = new int[5000];
            public Channel(int ch) { this.ch = ch; }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("START saving data from PCSGU250.");
            Console.WriteLine("Press [Esc] to stop and exit.");

            var dataBuf = new int[5000];
            var bytes = new byte[nBuf_Samples];
            int prevFreq = 0;
            var sw = new Stopwatch();
            int nCounts = 0;
            sw.Start();
            int prevPulse = -1;

            while (true)
            {
                if (Console.KeyAvailable && Console.ReadKey().Key == ConsoleKey.Escape)
                    break;
                if (DataReady())
                {
                    ReadCh1(ref dataBuf[0]);
                    int currFreq = dataBuf[iBuf_Freq_Hz];
                    if (prevFreq != currFreq)
                    {
                        sw.Stop(); sw.Start();
                        // todo: flush data acquired at previos frequency
                        Console.WriteLine($"Max Freq. = {currFreq} Hz");
                        // todo: create new file
                        nCounts = 0;
                        prevFreq = currFreq;
                    }
                    bool withData = false;
                    for (int i = iBuf_SamplesBeg; i < iBuf_SamplesEnd; i++)
                    {
                        if (dataBuf[i] < 255)
                            withData = true;
                        bytes[i - iBuf_SamplesBeg] = (byte)dataBuf[i];
                    }
                    if (withData)
                    {
                        nCounts++;
                        using (var fs = new FileStream("data_log.bin", FileMode.Append, FileAccess.Write, FileShare.Read))
                            fs.Write(bytes, 0, nBuf_Samples);
                        int w = Console.BufferWidth - 1;
                        int d = nBuf_Samples / w;
                        if (d == 0) d = 1;
                        for (int j = 0; j < w; j++)
                        {
                            int i0 = j * d;
                            int i1 = i0 + d - 1;
                            byte min = 255, max = 0;
                            int sum = 0;
                            for (int i = i0; i < i1; i++)
                            {
                                var b = bytes[i];
                                if (b < min) min = b;
                                if (b > max) max = b;
                                sum += b;
                            }
                            byte avg = (byte)(sum / d);
                            //PixelConsole.Write(Color.FromArgb(avg, min, max));
                            PixelConsole.WriteHeat(sum / (255f * d));
                        }
                        Console.ResetColor();
                        Console.WriteLine();
                    }
                }
                else System.Threading.Thread.Sleep(1);

                int currPulse = (int)(sw.ElapsedMilliseconds >> 12);
                if (currPulse != prevPulse)
                {
                    if (nCounts == 0)
                        Console.WriteLine("Press [Run] in PCSGU250 GUI to start sampling...");
                    //else
                    //    Console.WriteLine($"Buffers readed: {nCounts}");
                    nCounts = 0;
                    prevPulse = currPulse;
                }
            }
            Console.WriteLine("STOP saving data.");
        }
    }

    static class PixelConsole
    {
        static readonly Color[] ColorsOfMap = new Color[] {
            Color.FromArgb(0, 0, 0) ,//Black
            Color.FromArgb(0, 0, 0xFF) ,//Blue
            Color.FromArgb(0, 0xFF, 0xFF) ,//Cyan
            Color.FromArgb(0, 0xFF, 0) ,//Green
            Color.FromArgb(0xFF, 0xFF, 0) ,//Yellow
            Color.FromArgb(0xFF, 0, 0) ,//Red
            Color.FromArgb(0xFF, 0xFF, 0xFF) // White
        };

        static Color GetHeatColor(float fraction)
        {
            double colorPerc = 1d / (ColorsOfMap.Length - 1);// % of each block of color. the last is the "100% Color"
            double blockOfColor = fraction / colorPerc;// the integer part repersents how many block to skip
            int blockIdx = (int)Math.Truncate(blockOfColor);// Idx of 
            double valPercResidual = fraction - (blockIdx * colorPerc);//remove the part represented of block 
            double percOfColor = valPercResidual / colorPerc;// % of color of this block that will be filled

            Color cTarget = ColorsOfMap[blockIdx];
            Color cNext = cNext = ColorsOfMap[blockIdx + 1];

            var deltaR = cNext.R - cTarget.R;
            var deltaG = cNext.G - cTarget.G;
            var deltaB = cNext.B - cTarget.B;

            var R = cTarget.R + (deltaR * percOfColor);
            var G = cTarget.G + (deltaG * percOfColor);
            var B = cTarget.B + (deltaB * percOfColor);

            var c = ColorsOfMap[0];
            try { c = Color.FromArgb((byte)R, (byte)G, (byte)B); }
            catch { }
            return c;
        }

        public static void WriteHeat(float fraction) => Write(GetHeatColor(fraction));

        static readonly int[] cColors = { 0x000000, 0x000080, 0x008000, 0x008080, 0x800000, 0x800080, 0x808000, 0xC0C0C0, 0x808080, 0x0000FF, 0x00FF00, 0x00FFFF, 0xFF0000, 0xFF00FF, 0xFFFF00, 0xFFFFFF };
        static readonly Color[] cTable = cColors.Select(x => Color.FromArgb(x)).ToArray();
        static readonly char[] rList = new char[] { (char)9617, (char)9618, (char)9619, (char)9608 }; // 1/4, 2/4, 3/4, 4/4

        public static void Write(Color cValue)
        {
            int[] bestHit = new int[] { 0, 0, 4, int.MaxValue }; //ForeColor, BackColor, Symbol, Score

            for (int rChar = rList.Length; rChar > 0; rChar--)
            {
                for (int cFore = 0; cFore < cTable.Length; cFore++)
                {
                    for (int cBack = 0; cBack < cTable.Length; cBack++)
                    {
                        int R = (cTable[cFore].R * rChar + cTable[cBack].R * (rList.Length - rChar)) / rList.Length;
                        int G = (cTable[cFore].G * rChar + cTable[cBack].G * (rList.Length - rChar)) / rList.Length;
                        int B = (cTable[cFore].B * rChar + cTable[cBack].B * (rList.Length - rChar)) / rList.Length;
                        int iScore = (cValue.R - R) * (cValue.R - R) + (cValue.G - G) * (cValue.G - G) + (cValue.B - B) * (cValue.B - B);
                        if (!(rChar > 1 && rChar < 4 && iScore > 50000)) // rule out too weird combinations
                        {
                            if (iScore < bestHit[3])
                            {
                                bestHit[3] = iScore; //Score
                                bestHit[0] = cFore;  //ForeColor
                                bestHit[1] = cBack;  //BackColor
                                bestHit[2] = rChar;  //Symbol
                            }
                        }
                    }
                }
            }
            Console.ForegroundColor = (ConsoleColor)bestHit[0];
            Console.BackgroundColor = (ConsoleColor)bestHit[1];
            Console.Write(rList[bestHit[2] - 1]);
        }
    }
}
