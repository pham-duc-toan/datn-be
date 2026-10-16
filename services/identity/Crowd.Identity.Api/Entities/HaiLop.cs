using System;
using System.Collections.Generic;

namespace Crowd.Identity.Api.Entities
{
    /// <summary>
    /// Xac thuc hai lop cua mot tai khoan (FC-04, tuy chon). Mot dong moi user, chi co khi nguoi
    /// dung bat dau cai dat.
    ///
    ///   (khong co dong) ──setup──► Cho xac nhan (co bi mat, EnabledAt = null)
    ///                                   │ nhap dung ma tu ung dung
    ///                                   ▼
    ///                               Dang bat (EnabledAt co gia tri) ──tat / admin reset──► xoa dong
    ///
    /// Bi mat TOTP luu dang MA HOA (KhoaDuLieu): server can doc lai de tinh ma, nen khong bam mot
    /// chieu duoc nhu mat khau. Ma du phong chi luu ban bam co khoa, dung xong la gach khoi danh sach.
    /// </summary>
    public sealed class HaiLop
    {
        private List<string> _recoveryHashes;

        private HaiLop()
        {
            SecretCipher = string.Empty;
            _recoveryHashes = new List<string>();
        }

        public Guid UserId { get; private set; }

        /// <summary>Bi mat TOTP 20 byte, ma hoa AES-GCM (base64).</summary>
        public string SecretCipher { get; private set; }

        public DateTimeOffset CreatedAt { get; private set; }

        /// <summary>null = moi cai dat, chua xac nhan bang mot ma dung → dang nhap CHUA hoi ma.</summary>
        public DateTimeOffset? EnabledAt { get; private set; }

        /// <summary>
        /// Buoc thoi gian (30 giay) cua ma da dung gan nhat. Ma cua buoc nay tro ve truoc bi tu choi:
        /// ke dung trom ma nguoi dung vua go khong dung lai duoc trong cung 30 giay.
        /// </summary>
        public long LastUsedStep { get; private set; }

        /// <summary>Ban bam co khoa cua ma du phong CON DUNG DUOC (dung mot lan).</summary>
        public IReadOnlyList<string> RecoveryHashes
        {
            get { return _recoveryHashes; }
        }

        public bool DangBat
        {
            get { return EnabledAt.HasValue; }
        }

        public static HaiLop BatDauCaiDat(Guid userId, string secretCipher, DateTimeOffset luc)
        {
            HaiLop h = new HaiLop();
            h.UserId = userId;
            h.SecretCipher = secretCipher;
            h.CreatedAt = luc;
            return h;
        }

        /// <summary>Cai dat lai (chua bat): doi bi mat moi — ma QR cu het tac dung.</summary>
        public void DoiBiMat(string secretCipher, DateTimeOffset luc)
        {
            if (DangBat)
            {
                throw new InvalidOperationException("2FA dang bat — tat truoc khi cai dat lai.");
            }

            SecretCipher = secretCipher;
            CreatedAt = luc;
            LastUsedStep = 0;
        }

        public void Bat(long buoc, IEnumerable<string> recoveryHashes, DateTimeOffset luc)
        {
            EnabledAt = luc;
            LastUsedStep = buoc;
            _recoveryHashes = new List<string>(recoveryHashes);
        }

        public void GhiBuocDaDung(long buoc)
        {
            if (buoc > LastUsedStep)
            {
                LastUsedStep = buoc;
            }
        }

        /// <summary>Dung mot ma du phong: co trong danh sach thi gach di va tra true.</summary>
        public bool DungMaDuPhong(string hash)
        {
            int viTri = _recoveryHashes.IndexOf(hash);
            if (viTri < 0)
            {
                return false;
            }

            // Gan danh sach MOI (khong sua tai cho) de EF Core thay doi va ghi lai cot text[].
            List<string> conLai = new List<string>(_recoveryHashes);
            conLai.RemoveAt(viTri);
            _recoveryHashes = conLai;
            return true;
        }

        public void DatMaDuPhong(IEnumerable<string> recoveryHashes)
        {
            _recoveryHashes = new List<string>(recoveryHashes);
        }
    }
}
