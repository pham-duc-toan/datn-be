using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Crowd.Seeding
{
    /// <summary>
    /// ID TAT DINH cho du lieu seed: cung mot ten → luon cung mot Guid, o moi
    /// service, moi lan chay.
    ///
    /// Vi sao can: moi service seed database RIENG va KHONG noi chuyen voi nhau
    /// luc seed. project-svc tao du an P1, task-svc tao task cho P1, ledger-svc
    /// giu ky quy cho P1 — ca ba phai dung chung mot ProjectId. Guid.NewGuid()
    /// moi noi mot kieu thi du lieu khong khop nhau.
    /// </summary>
    public static class SeedIds
    {
        /// <summary>
        /// "Khong gian ten" rieng cua kich ban seed. Doi chuoi nay = doi TOAN BO
        /// ID seed — chi doi khi muon seed lai tu dau tren database trong.
        /// </summary>
        private const string KhongGianTen = "crowd-seed-v1:";

        /// <summary>
        /// Bam SHA-256 cua ten, lay 16 byte dau lam Guid. Dat bit version (5) va
        /// variant (RFC 4122) de Guid trong giong Guid chuan khi doc trong psql.
        /// </summary>
        public static Guid Tu(string ten)
        {
            if (string.IsNullOrWhiteSpace(ten))
            {
                throw new ArgumentException("ten khong duoc rong", nameof(ten));
            }

            byte[] bam = SHA256.HashData(Encoding.UTF8.GetBytes(KhongGianTen + ten));

            byte[] g = new byte[16];
            Array.Copy(bam, g, 16);

            g[7] = (byte)((g[7] & 0x0F) | 0x50);
            g[8] = (byte)((g[8] & 0x3F) | 0x80);

            return new Guid(g);
        }

        /// <summary>
        /// Ghi de ID cua mot entity vua tao bang factory cua domain.
        ///
        /// Factory cua domain (LabelingProject.Tao, Sample.Tao...) tu sinh ID
        /// ngau nhien va setter la private — dung nhu thiet ke, vi code nghiep vu
        /// khong bao gio duoc tu chon ID. Seed la ngoai le duy nhat: can ID tat
        /// dinh de cac service khop nhau. Nen seed van goi DUNG factory (de moi
        /// luat nghiep vu van chay), roi chi thay ID bang reflection — truoc khi
        /// entity duoc Add vao DbContext.
        /// </summary>
        public static void GanId(object entity, Guid id)
        {
            GanThuocTinh(entity, "Id", id);
        }

        /// <summary>Ghi mot thuoc tinh co setter private. CHI dung trong seed.</summary>
        public static void GanThuocTinh(object entity, string tenThuocTinh, object? giaTri)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            PropertyInfo? p = entity.GetType().GetProperty(
                tenThuocTinh,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            // SetMethod tra ve ca setter private; null nghia la thuoc tinh chi co get.
            if (p == null || p.SetMethod == null)
            {
                throw new InvalidOperationException(
                    entity.GetType().Name + " khong co thuoc tinh ghi duoc ten '" + tenThuocTinh + "'.");
            }

            p.SetValue(entity, giaTri);
        }
    }
}
