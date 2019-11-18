using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
//using System.Linq;
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
            public int Ch { get; private set; }

            int[] dataBuf = new int[5000];
            int[] prevCfg = new int[iBuf_SamplesBeg];
            public string CfgStr { get; private set; }

            public Channel(int ch) { this.Ch = ch; }

            public bool FetchData()
            {
                switch (Ch)
                {
                    case 1: ReadCh1(ref dataBuf[0]); break;
                    case 2: ReadCh2(ref dataBuf[0]); break;
                    default: return false;
                }

                for (int i = iBuf_SamplesBeg; i < iBuf_SamplesEnd; i++)
                    if (dataBuf[i] != 255)
                        return true;
                return false;
            }

            public void Stop()
            {
                for (int i = 0; i < prevCfg.Length; i++) prevCfg[i] = 0;
                FileName = null;
            }

            public bool CfgChanged()
            {
                bool changed = false;
                for (int i = 0; i < prevCfg.Length; i++)
                    if (prevCfg[i] != dataBuf[i])
                    {
                        prevCfg[i] = dataBuf[i];
                        changed = true;
                    }
                return changed;
            }

            static string MinLenStr(params string[] strs)
            {
                int iMin = 0;
                for (int i = 1; i < strs.Length; i++)
                    if (strs[iMin].Length > strs[i].Length)
                        iMin = i;
                return strs[iMin];
            }

            public void UpdateFileName(DateTime firstTime, string dir)
            {
                int F = dataBuf[iBuf_Freq_Hz];
                int V = dataBuf[iBuf_Volt_mV];
                int G = dataBuf[iBuf_GndLevel_ADCcounts];
                var sF = MinLenStr($"{F}Hz", $"{F * 0.001}kHz", $"{F * 0.000001}MHz");
                var sV = MinLenStr($"{V}mV", $"{V * 0.001}V");
                CfgStr = (G > 0)
                    ? $"#{Ch}_F={sF}_V={sV}_nGND={G}"
                    : $"#{Ch}_F={sF}_V={sV}";
                FileName = Path.Combine(dir, $"{firstTime:yyyyMMdd_HHmm}_{CfgStr}.tsv.txt");
                firstRow = true;
            }

            public string FileName { get; private set; }
            bool firstRow;
            StringBuilder sb = new StringBuilder(4096 * 5);

            public void SaveDataAsText(DateTime dataTime)
            {
                sb.Append(dataTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                sb.Append('\t');
                for (int i = iBuf_SamplesBeg; i < iBuf_SamplesEnd; i++)
                { sb.Append('\t'); sb.Append(dataBuf[i]); }
                sb.AppendLine();
                try { File.AppendAllText(FileName, sb.ToString(), Encoding.ASCII); }
                catch { Console.WriteLine($"#{Ch}: ERROR SAVING TO FILE: {FileName}"); }
                firstRow = false;
            }

            public void PrintPixels(int w)
            {
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
                        var b = (byte)dataBuf[iBuf_SamplesBeg + i];
                        if (b < min) min = b;
                        if (b > max) max = b;
                        sum += b;
                    }
                    byte avg = (byte)Math.Round(1f * sum / d);
                    PixelConsole.WriteHeatByte(avg);
                }
            }
        }

        const string DirCH1 = "CH1";
        const string DirCH2 = "CH2";

        static void Main(string[] args)
        {
            Console.WriteLine("START receiving data from PCSGU250.");
            const string HelpMsg = "Press [Esc] to stop and exit, [Space] to start/pause recording.";
            Console.WriteLine(HelpMsg);

            var sw = new Stopwatch();
            int nCounts = 0;
            sw.Start();
            int prevPulse = -1;
            bool REC = false;

            var ch1 = new Channel(1); Directory.CreateDirectory(DirCH1);
            var ch2 = new Channel(2); Directory.CreateDirectory(DirCH2);

            while (true)
            {
                if (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(true).Key;
                    if (k == ConsoleKey.Escape)
                        break;
                    else if (k == ConsoleKey.Spacebar)
                    {
                        REC = !REC;
                        Console.WriteLine(REC ? "Recording started!" : "Recording paused...");
                        if (REC) { ch1.Stop(); ch2.Stop(); }
                    }
                    else Console.WriteLine(HelpMsg);
                }
                if (DataReady())
                {
                    nCounts++;
                    var time = DateTime.Now;
                    var with1 = ch1.FetchData();
                    var with2 = ch2.FetchData();
                    bool changed = false;
                    if (with1 && ch1.CfgChanged())
                        changed = true;
                    if (with2 && ch2.CfgChanged())
                        changed = true;
                    if (changed)
                    {
                        int left = "REC 00:00:00.000 ".Length;
                        Console.CursorLeft = left;
                        if (with1)
                        {
                            Console.Write(ch1.CfgStr);
                            Console.CursorLeft = left + (Console.BufferWidth - 1 - left) / 2;
                        }
                        if (with2)
                            Console.Write(ch2.CfgStr);
                        Console.WriteLine();
                    }
                    if (REC)
                    {
                        Console.BackgroundColor = ConsoleColor.DarkRed;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                    }
                    Console.Write("REC");
                    Console.ResetColor();
                    Console.Write($" {time:HH:mm:ss.fff} ");
                    int w = Console.BufferWidth - Console.CursorLeft - 1;
                    if (with1 && with2)
                        w /= 2;
                    if (with1)
                    {
                        ch1.PrintPixels(w);
                        Console.ResetColor();
                        if (REC)
                        {
                            if (ch1.FileName == null) ch1.UpdateFileName(time, DirCH1);
                            ch1.SaveDataAsText(time);
                        }
                    }
                    if (with2)
                    {
                        ch2.PrintPixels(w);
                        Console.ResetColor();
                        if (REC)
                        {
                            if (ch2.FileName == null) ch2.UpdateFileName(time, DirCH2);
                            ch2.SaveDataAsText(time);
                        }
                    }
                    Console.WriteLine();
                }
                else System.Threading.Thread.Sleep(1);

                int currPulse = (int)(sw.ElapsedMilliseconds >> 14);
                if (currPulse != prevPulse)
                {
                    if (nCounts == 0)
                        Console.WriteLine("Press [Run] in PCSGU250 GUI to start sampling...");
                    nCounts = 0;
                    prevPulse = currPulse;
                }
            }
            Console.WriteLine("STOP receiving data from PCSGU250.");
        }
    }

    static class PixelConsole
    {
        #region Code sourced from https://stackoverflow.com/questions/33538527/display-a-image-in-a-console-application authored by https://stackoverflow.com/users/4959221/anton%c3%adn-lejsek

        static readonly int[] cColors = { 0x000000, 0x000080, 0x008000, 0x008080, 0x800000, 0x800080, 0x808000, 0xC0C0C0, 0x808080, 0x0000FF, 0x00FF00, 0x00FFFF, 0xFF0000, 0xFF00FF, 0xFFFF00, 0xFFFFFF };
        static readonly Color[] cTable = cColors.Select(x => Color.FromArgb(x)).ToArray();
        static readonly char[] rList = new char[] { (char)9617, (char)9618, (char)9619, (char)9608 }; // 1/4, 2/4, 3/4, 4/4

        public struct ScoreHit
        {
            public int ForeColor, BackColor, Symbol, Score;
            public void Write()
            {
                Console.ForegroundColor = (ConsoleColor)ForeColor;
                Console.BackgroundColor = (ConsoleColor)BackColor;
                Console.Write(rList[Symbol - 1]);
            }
        }

        public static ScoreHit GetScoreHit(Color cValue)
        {
            var bestHit = new ScoreHit() { Symbol = 4, Score = int.MaxValue };

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
                            if (iScore < bestHit.Score)
                            {
                                bestHit.Score = iScore;
                                bestHit.ForeColor = cFore;
                                bestHit.BackColor = cBack;
                                bestHit.Symbol = rChar;
                            }
                        }
                    }
                }
            }
            return bestHit;
        }

        public static void Write(Color cValue) => GetScoreHit(cValue).Write();
        #endregion

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
            if (fraction >= 1f)
                return ColorsOfMap[ColorsOfMap.Length - 1];

            double colorPerc = 1d / (ColorsOfMap.Length - 1);// % of each block of color. the last is the "100% Color"
            double blockOfColor = fraction / colorPerc;// the integer part repersents how many block to skip
            int blockIdx = (int)Math.Truncate(blockOfColor);// Idx of 
            double valPercResidual = fraction - (blockIdx * colorPerc);//remove the part represented of block 
            double percOfColor = valPercResidual / colorPerc;// % of color of this block that will be filled

            Color cTarget = ColorsOfMap[blockIdx];
            Color cNext = ColorsOfMap[blockIdx + 1];

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

        static readonly ScoreHit[] HeatPixelMap;

        public static void WriteHeatByte(byte fraction) => HeatPixelMap[fraction].Write();


        static PixelConsole()
        {
            const float coeff = 1f / 255;

            HeatPixelMap = new ScoreHit[256];
            for (int i = 0; i < HeatPixelMap.Length; i++)
                HeatPixelMap[i] = GetScoreHit(GetHeatColor(i * coeff));
        }
    }
}
