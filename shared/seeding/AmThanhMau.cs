using System;
using System.IO;
using System.Text;

namespace Crowd.Seeding
{
    /// <summary>
    /// Sinh file WAV mot tan so (song sin) ngay trong code — du an audio seed co
    /// file nghe duoc that ma repo khong phai chua file am thanh.
    ///
    /// WAV PCM toi gian: "RIFF" | "WAVE" | khoi "fmt " (dinh dang) | khoi "data" (mau).
    /// 16 kHz, mono, 16 bit — 1 giay = 32 KB. Cung tham so ra cung tung byte.
    /// </summary>
    public static class AmThanhMau
    {
        public const string ContentType = "audio/wav";

        private const int TanSoLayMau = 16000;
        private const short SoBit = 16;
        private const short SoKenh = 1;

        public static byte[] TaoWav(double tanSoHz, double soGiay)
        {
            if (soGiay <= 0 || soGiay > 600)
            {
                throw new ArgumentOutOfRangeException(nameof(soGiay), soGiay, "Do dai 0-600 giay.");
            }

            int soMau = (int)(TanSoLayMau * soGiay);
            int byteDuLieu = soMau * SoKenh * (SoBit / 8);

            using (MemoryStream ra = new MemoryStream())
            using (BinaryWriter w = new BinaryWriter(ra, Encoding.ASCII))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + byteDuLieu);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));

                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);                                     // do dai khoi fmt
                w.Write((short)1);                               // PCM
                w.Write(SoKenh);
                w.Write(TanSoLayMau);
                w.Write(TanSoLayMau * SoKenh * (SoBit / 8));     // byte / giay
                w.Write((short)(SoKenh * (SoBit / 8)));          // byte / mau
                w.Write(SoBit);

                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(byteDuLieu);

                for (int i = 0; i < soMau; i++)
                {
                    double t = (double)i / TanSoLayMau;
                    // Bien do 30% de nghe khong choi tai.
                    short giaTri = (short)(Math.Sin(2 * Math.PI * tanSoHz * t) * short.MaxValue * 0.3);
                    w.Write(giaTri);
                }

                w.Flush();
                return ra.ToArray();
            }
        }
    }
}
