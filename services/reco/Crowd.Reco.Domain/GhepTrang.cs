using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Crowd.Reco.Domain
{
    /// <summary>Mot muc trong danh sach da xep: id du an + diem luc xep (de so voi du an moi chen vao).</summary>
    public sealed class MucDanhSach
    {
        public required Guid ProjectId { get; init; }

        public required float Diem { get; init; }

        public required bool KhamPha { get; init; }
    }

    /// <summary>
    /// Danh sach da xep cho MOT luot luot (Redis feed:{userId}:{phien}, song 15 phut). KHONG BAO GIO SUA sau khi tao —
    /// con tro chi di toi, du an moi tron vao luc ghep trang chu khong chen vao day, nen khong lap va khong mat the.
    /// Ma hoa nhi phan: 8 byte thoi diem tao + moi muc (16 byte id + 4 byte diem + 1 byte co kham pha).
    /// </summary>
    public sealed class DanhSachLuot
    {
        private const int MotMuc = 21;

        public DanhSachLuot(DateTimeOffset taoLuc, IReadOnlyList<MucDanhSach> muc)
        {
            if (muc == null)
            {
                throw new ArgumentNullException(nameof(muc));
            }

            TaoLuc = taoLuc;
            Muc = muc;
        }

        public DateTimeOffset TaoLuc { get; }

        public IReadOnlyList<MucDanhSach> Muc { get; }

        public byte[] MaHoa()
        {
            byte[] b = new byte[8 + Muc.Count * MotMuc];
            BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(0, 8), TaoLuc.ToUnixTimeMilliseconds());
            for (int i = 0; i < Muc.Count; i++)
            {
                int o = 8 + i * MotMuc;
                Muc[i].ProjectId.TryWriteBytes(b.AsSpan(o, 16));
                BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(o + 16, 4), Muc[i].Diem);
                b[o + 20] = Muc[i].KhamPha ? (byte)1 : (byte)0;
            }

            return b;
        }

        public static DanhSachLuot? GiaiMa(byte[]? b)
        {
            if (b == null || b.Length < 8 || (b.Length - 8) % MotMuc != 0)
            {
                return null;
            }

            DateTimeOffset luc = DateTimeOffset.FromUnixTimeMilliseconds(BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(0, 8)));
            int n = (b.Length - 8) / MotMuc;
            List<MucDanhSach> ds = new List<MucDanhSach>(n);
            for (int i = 0; i < n; i++)
            {
                int o = 8 + i * MotMuc;
                ds.Add(new MucDanhSach
                {
                    ProjectId = new Guid(b.AsSpan(o, 16)),
                    Diem = BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(o + 16, 4)),
                    KhamPha = b[o + 20] == 1,
                });
            }

            return new DanhSachLuot(luc, ds);
        }

        public static DanhSachLuot Tu(IReadOnlyList<DuAnDaCham> daXep, DateTimeOffset luc)
        {
            if (daXep == null)
            {
                throw new ArgumentNullException(nameof(daXep));
            }

            return new DanhSachLuot(luc, daXep.Select(x => new MucDanhSach
            {
                ProjectId = x.DuAn.Id,
                Diem = (float)x.Diem,
                KhamPha = x.KhamPha,
            }).ToList());
        }
    }

    /// <summary>Mot the da chon cho trang (kem du an + co dong la du an moi chen vao).</summary>
    public sealed class TheTrongTrang
    {
        public required DuAnGoiY DuAn { get; init; }

        public required int ViTri { get; init; }

        public required bool KhamPha { get; init; }

        /// <summary>Du an mo SAU luc tao danh sach, duoc tron vao trang nay.</summary>
        public required bool ChenMoi { get; init; }
    }

    /// <summary>Ket qua ghep mot trang.</summary>
    public sealed class KetQuaTrang
    {
        public required List<TheTrongTrang> The { get; init; }

        /// <summary>Con tro moi (vi tri trong danh sach) sau trang nay.</summary>
        public required int ConTro { get; init; }

        /// <summary>Da di het danh sach ma trang chua du — noi goi xep lai (tru cac id da tra) va ghep tiep.</summary>
        public required bool HetDanhSach { get; init; }
    }

    /// <summary>
    /// Ghep MOT trang tu danh sach da xep (muc 5.6 ke hoach):
    ///   1. lay id tiep theo tu con tro, bo id da tra trong phien / du an da dong / khong con lam duoc;
    ///   2. du an MO SAU luc tao danh sach ma nguoi nay lam duoc va chua tra: cham diem, chen toi da N du an len dau trang
    ///      (diem cao hon the cuoi cua trang, hoac con trong be du an moi);
    ///   3. con tro tang dung so muc da DOC tu danh sach (ke ca muc bi bo).
    /// </summary>
    public static class GhepTrang
    {
        public static KetQuaTrang Ghep(
            DanhSachLuot danhSach,
            int conTro,
            int kichThuoc,
            IReadOnlySet<Guid> daTra,
            Func<Guid, DuAnGoiY?> timDuAn,
            Func<DuAnGoiY, bool> lamDuoc,
            IReadOnlyList<DuAnDaCham> duAnMoiDaCham,
            ThongSoGoiY thongSo,
            int viTriDau)
        {
            if (danhSach == null)
            {
                throw new ArgumentNullException(nameof(danhSach));
            }

            if (daTra == null || timDuAn == null || lamDuoc == null || duAnMoiDaCham == null || thongSo == null)
            {
                throw new ArgumentNullException(nameof(daTra));
            }

            int n = Math.Max(1, kichThuoc);
            int i = Math.Max(0, conTro);
            HashSet<Guid> trongTrang = new HashSet<Guid>();
            List<KeyValuePair<DuAnGoiY, MucDanhSach>> tuDanhSach = new List<KeyValuePair<DuAnGoiY, MucDanhSach>>();

            // Chua cho cho du an moi: lay toi da n - so cho chen moi tu danh sach truoc.
            List<DuAnDaCham> moi = duAnMoiDaCham
                .Where(x => !daTra.Contains(x.DuAn.Id))
                .OrderByDescending(x => x.Diem)
                .ToList();

            while (i < danhSach.Muc.Count && tuDanhSach.Count < n)
            {
                MucDanhSach m = danhSach.Muc[i];
                i++;
                if (daTra.Contains(m.ProjectId) || trongTrang.Contains(m.ProjectId))
                {
                    continue;
                }

                DuAnGoiY? p = timDuAn(m.ProjectId);
                if (p == null || !lamDuoc(p))
                {
                    continue;
                }

                trongTrang.Add(m.ProjectId);
                tuDanhSach.Add(new KeyValuePair<DuAnGoiY, MucDanhSach>(p, m));
            }

            // Du an moi: chen len dau neu diem cao hon the cuoi cua trang hoac con trong be du an moi.
            float diemCuoi = tuDanhSach.Count == 0 ? float.MinValue : tuDanhSach.Min(x => x.Value.Diem);
            List<DuAnDaCham> chen = new List<DuAnDaCham>();
            foreach (DuAnDaCham x in moi)
            {
                if (chen.Count >= thongSo.ChenMoiMoiTrang)
                {
                    break;
                }

                if (trongTrang.Contains(x.DuAn.Id))
                {
                    continue;
                }

                bool trongBe = x.DuAn.SeenCount < thongSo.NguongDuAnMoi;
                if (trongBe || x.Diem > diemCuoi || tuDanhSach.Count < n)
                {
                    chen.Add(x);
                    trongTrang.Add(x.DuAn.Id);
                }
            }

            // Trang du n the: du an moi dung dau, bot the CUOI cua phan lay tu danh sach — tra con tro ve de khong mat the.
            int bot = Math.Max(0, chen.Count + tuDanhSach.Count - n);
            for (int k = 0; k < bot; k++)
            {
                KeyValuePair<DuAnGoiY, MucDanhSach> cuoi = tuDanhSach[tuDanhSach.Count - 1];
                tuDanhSach.RemoveAt(tuDanhSach.Count - 1);
                int viTriTrongDs = IndexOf(danhSach, cuoi.Value.ProjectId, conTro, i);
                if (viTriTrongDs >= 0 && viTriTrongDs < i)
                {
                    i = viTriTrongDs;
                }
            }

            List<TheTrongTrang> the = new List<TheTrongTrang>();
            int viTri = viTriDau;
            foreach (DuAnDaCham x in chen)
            {
                the.Add(new TheTrongTrang { DuAn = x.DuAn, ViTri = viTri, KhamPha = false, ChenMoi = true });
                viTri++;
            }

            foreach (KeyValuePair<DuAnGoiY, MucDanhSach> x in tuDanhSach)
            {
                the.Add(new TheTrongTrang { DuAn = x.Key, ViTri = viTri, KhamPha = x.Value.KhamPha, ChenMoi = false });
                viTri++;
            }

            return new KetQuaTrang
            {
                The = the,
                ConTro = i,
                HetDanhSach = i >= danhSach.Muc.Count && the.Count < n,
            };
        }

        private static int IndexOf(DanhSachLuot ds, Guid id, int tu, int den)
        {
            for (int k = Math.Max(0, tu); k < den && k < ds.Muc.Count; k++)
            {
                if (ds.Muc[k].ProjectId == id)
                {
                    return k;
                }
            }

            return -1;
        }
    }
}
