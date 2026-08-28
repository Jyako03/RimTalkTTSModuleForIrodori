using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RimTalk.TTS.Service.IrodoriService
{
    /// <summary>Joins complete PCM/IEEE-float WAV chunks emitted by Irodori SSE into one valid WAV.</summary>
    public static class WavConcatUtil
    {
        private sealed class WaveParts
        {
            public byte[] Fmt;
            public byte[] Data;
        }

        public static byte[] Concatenate(IReadOnlyList<byte[]> waves)
        {
            if (waves == null || waves.Count == 0) return null;
            if (waves.Count == 1) return waves[0];

            var parsed = waves.Select(Parse).ToList();
            byte[] fmt = parsed[0].Fmt;
            if (parsed.Any(x => !fmt.SequenceEqual(x.Fmt)))
                throw new InvalidDataException("Irodori SSE WAV chunks use different audio formats.");

            int dataLength = parsed.Sum(x => x.Data.Length);
            using var ms = new MemoryStream(12 + 8 + fmt.Length + 8 + dataLength + 8);
            using var bw = new BinaryWriter(ms, Encoding.ASCII, true);
            bw.Write(Encoding.ASCII.GetBytes("RIFF"));
            bw.Write(4 + (8 + fmt.Length) + (8 + dataLength));
            bw.Write(Encoding.ASCII.GetBytes("WAVE"));
            bw.Write(Encoding.ASCII.GetBytes("fmt "));
            bw.Write(fmt.Length);
            bw.Write(fmt);
            if ((fmt.Length & 1) != 0) bw.Write((byte)0);
            bw.Write(Encoding.ASCII.GetBytes("data"));
            bw.Write(dataLength);
            foreach (var item in parsed) bw.Write(item.Data);
            if ((dataLength & 1) != 0) bw.Write((byte)0);
            bw.Flush();
            return ms.ToArray();
        }

        private static WaveParts Parse(byte[] wav)
        {
            if (wav == null || wav.Length < 12) throw new InvalidDataException("Invalid WAV chunk.");
            if (Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
                throw new InvalidDataException("SSE chunk is not RIFF/WAVE.");

            byte[] fmt = null;
            byte[] data = null;
            int p = 12;
            while (p + 8 <= wav.Length)
            {
                string id = Encoding.ASCII.GetString(wav, p, 4);
                int len = BitConverter.ToInt32(wav, p + 4);
                p += 8;
                if (len < 0 || p + len > wav.Length) throw new InvalidDataException("Corrupt WAV chunk.");
                if (id == "fmt ")
                {
                    fmt = new byte[len];
                    Buffer.BlockCopy(wav, p, fmt, 0, len);
                }
                else if (id == "data")
                {
                    data = new byte[len];
                    Buffer.BlockCopy(wav, p, data, 0, len);
                }
                p += len + (len & 1);
            }
            if (fmt == null || data == null) throw new InvalidDataException("WAV chunk lacks fmt/data.");
            return new WaveParts { Fmt = fmt, Data = data };
        }
    }
}
