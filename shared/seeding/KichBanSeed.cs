using System;
using System.Collections.Generic;
using System.Globalization;
using Crowd.Labeling;

namespace Crowd.Seeding
{
    /// <summary>
    /// KICH BAN SEED — nguon su that duy nhat cua du lieu mau.
    ///
    /// Cau chuyen (du an lam trung tam, docs 3.18 — moi labeler lam HET cac cau cua du an trong mot
    /// bai, nop ca bai; du an ket thuc thi cham theo da so + cau vang, nguoi dat nhan tien thuong):
    ///   - Doanh nghiep 1 (biz1) nap 2.000.000d. P1 dang chay: lab1 da nop bai, lab2 dang lam do.
    ///     P2 cho admin duyet, P3 nhap san sang publish, P4 dang chay va bat buoc test dau vao,
    ///     P5 van ban (lab1 dang lam), P6 am thanh, P7 so sanh cap DA CHAM XONG (lab1, lab2 dat —
    ///     moi nguoi 30.000d RUT DUOC NGAY; rev1 truot vi khac da so).
    ///   - Doanh nghiep 2 (biz2) chua co tien, co mot lenh nap 1.000.000d chua
    ///     thanh toan — de test trang thanh toan sandbox va nhanh "thieu tien". P8 nhap.
    ///   - lab3 chua tham gia du an nao — de test tham gia / bai test dau vao.
    ///   - rev1 (tai khoan labeler) lam bai o P7.
    ///
    /// Anh mau la anh PNG mot mau (do / xanh la / xanh duong / vang), nen dap an
    /// dung nhin la thay.
    /// </summary>
    public static class KichBanSeed
    {
        /// <summary>Mat khau chung cho MOI tai khoan seed. Chi dung o Development.</summary>
        public const string MatKhauChung = "Matkhau@123";

        /// <summary>Phi nen tang chot cho moi du an seed — khop FeeOptions.PlatformFeePercent cua dev.</summary>
        public const int PhanTramPhi = 30;

        /// <summary>Ten cong thanh toan cua lenh nap seed — khop SandboxPaymentProvider.Name.</summary>
        public const string CongThanhToan = "sandbox";

        public static readonly IReadOnlyList<string> LopMau = new string[] { "do", "xanh_la", "xanh_duong", "vang" };

        // THU TU KHAI BAO QUAN TRONG: truong static khoi tao tu tren xuong, ma
        // du an can tra cuu tai khoan — nen Users phai dung truoc Projects.
        public static readonly IReadOnlyList<SeedUser> Users = TaoUsers();

        public static readonly IReadOnlyList<SeedProject> Projects = TaoProjects();

        public static readonly IReadOnlyList<SeedDeposit> Deposits = TaoDeposits();

        // =====================================================================
        // TRA CUU
        // =====================================================================

        public static SeedUser User(string key)
        {
            foreach (SeedUser u in Users)
            {
                if (u.Key == key)
                {
                    return u;
                }
            }

            throw new InvalidOperationException("Kich ban khong co tai khoan '" + key + "'.");
        }

        public static SeedProject Project(string key)
        {
            foreach (SeedProject p in Projects)
            {
                if (p.Key == key)
                {
                    return p;
                }
            }

            throw new InvalidOperationException("Kich ban khong co du an '" + key + "'.");
        }

        /// <summary>Moi mau la MOT cau hoi — QuestionId tinh tu SampleId de task-svc va cac service khac khop nhau.</summary>
        public static Guid TaskIdCua(Guid sampleId)
        {
            return SeedIds.Tu("task:" + sampleId);
        }

        // =====================================================================
        // TAI KHOAN
        // =====================================================================

        private static List<SeedUser> TaoUsers()
        {
            List<SeedUser> ds = new List<SeedUser>();
            ds.Add(TaiKhoan("admin", "admin@crowd.local", "Quan tri vien", "admin"));
            ds.Add(TaiKhoan("biz1", "doanhnghiep1@crowd.local", "Cong ty Anh Sang", "business"));
            ds.Add(TaiKhoan("biz2", "doanhnghiep2@crowd.local", "Cong ty Binh Minh", "business"));
            ds.Add(TaiKhoan("lab1", "labeler1@crowd.local", "Nguyen Van An", "labeler"));
            ds.Add(TaiKhoan("lab2", "labeler2@crowd.local", "Tran Thi Binh", "labeler"));
            ds.Add(TaiKhoan("lab3", "labeler3@crowd.local", "Le Van Cuong", "labeler"));
            // Reviewer la VAI TRO TRONG DU AN, khong phai vai tro he thong: tai
            // khoan van la labeler, chu du an moi vao P1 voi vai tro reviewer.
            ds.Add(TaiKhoan("rev1", "reviewer1@crowd.local", "Pham Thi Dung", "labeler"));
            return ds;
        }

        private static SeedUser TaiKhoan(string key, string email, string ten, string vaiTro)
        {
            return new SeedUser
            {
                Key = key,
                Id = SeedIds.Tu("user:" + key),
                Email = email,
                DisplayName = ten,
                Roles = new string[] { vaiTro },
            };
        }

        // =====================================================================
        // DU AN
        // =====================================================================

        private static List<SeedProject> TaoProjects()
        {
            List<SeedProject> ds = new List<SeedProject>();
            ds.Add(TaoP1());
            ds.Add(TaoP2());
            ds.Add(TaoP3());
            ds.Add(TaoP4());
            ds.Add(TaoP5());
            ds.Add(TaoP6());
            ds.Add(TaoP7());
            ds.Add(TaoP8());
            return ds;
        }

        private const string HuongDan =
            "# Huong dan\n\n"
            + "Moi anh la MOT mau. Chon dung mot lop:\n\n"
            + "- `do`\n- `xanh_la`\n- `xanh_duong`\n- `vang`\n\n"
            + "Anh toi hay sang deu chon theo sac mau chinh.";

        /// <summary>
        /// P1 — DANG CHAY: 8 anh (anh 8 la cau vang kiem tra) → de 7 cau. 3 suat, thuong 50.000d,
        /// nguong 80%. lab1 DA NOP bai (dung het), lab2 DANG LAM (nhap 3/7 cau).
        ///
        /// Ky quy 3 x (50.000 + 15.000 phi) = 195.000d.
        /// </summary>
        private static SeedProject TaoP1()
        {
            const string k = "p1";
            Guid id = SeedIds.Tu("project:" + k);

            List<SeedSample> mau = new List<SeedSample>
            {
                Mau(id, k, 1, "do", 220, 40, 40),
                Mau(id, k, 2, "do", 180, 25, 25),
                Mau(id, k, 3, "xanh_la", 40, 170, 60),
                Mau(id, k, 4, "xanh_la", 20, 120, 40),
                Mau(id, k, 5, "xanh_duong", 40, 90, 210),
                Mau(id, k, 6, "xanh_duong", 25, 60, 160),
                Mau(id, k, 7, "vang", 235, 200, 40),
                Mau(id, k, 8, "vang", 200, 165, 20),
            };

            Guid biz1 = User("biz1").Id;
            SeedGold vang = Vang(k, mau[7], false);

            List<SeedAttempt> bai = new List<SeedAttempt>
            {
                // lab1: lam dung moi cau (ca cau vang), nop 1 ngay truoc — cho du an ket thuc de cham.
                BaiLam(k, id, "lab1", SeedAttemptState.Submitted, NgayGio(1, 3), NgayGio(1, 0),
                    TraLoiPhanLoai(k, mau, "lab1", 0, 7), 1, 1, null),

                // lab2: dang lam do — 3 cau nhap, chua nop.
                BaiLam(k, id, "lab2", SeedAttemptState.InProgress, TimeSpan.FromHours(2), null,
                    TraLoiPhanLoai(k, mau, "lab2", 0, 3), 1, 0, null),
            };

            return new SeedProject
            {
                Key = k,
                Id = id,
                OwnerId = biz1,
                DatasetId = SeedIds.Tu("dataset:" + k),
                Name = "[Seed] Phan loai mau anh - dang chay",
                Description = "Du an mau dang chay: lab1 da nop bai, lab2 dang lam do.",
                GuidelineMarkdown = HuongDan,
                Stage = SeedStage.Running,
                IsPrivate = false,
                Modality = Modalities.Image,
                LabelSchemaJson = TapNhanMau,
                RewardVnd = 50000,
                Slots = 3,
                PassThresholdPercent = 80,
                DeadlineInDays = 30,
                RequireEntranceTest = false,
                EntranceQuestionCount = 10,
                EntrancePassPercent = 80,
                CreatedAgo = NgayGio(7, 0),
                EscrowedAgo = NgayGio(6, 0),
                PublishedAgo = NgayGio(5, 23),
                Samples = mau,
                Members = new SeedMember[]
                {
                    new SeedMember { UserId = User("lab1").Id, Role = SeedMemberRole.Labeler },
                    new SeedMember { UserId = User("lab2").Id, Role = SeedMemberRole.Labeler },
                    new SeedMember { UserId = User("rev1").Id, Role = SeedMemberRole.Labeler },
                },
                Gold = new SeedGold[] { vang },
                Attempts = bai,
            };
        }

        /// <summary>
        /// P2 — CHO ADMIN DUYET: da ky quy 3 x (20.000 + 6.000) = 78.000d.
        /// Tu huy sau 72 gio neu khong ai duyet (PendingApprovalTimeoutWorker).
        /// </summary>
        private static SeedProject TaoP2()
        {
            const string k = "p2";
            Guid id = SeedIds.Tu("project:" + k);

            return new SeedProject
            {
                Key = k,
                Id = id,
                OwnerId = User("biz1").Id,
                DatasetId = SeedIds.Tu("dataset:" + k),
                Name = "[Seed] Phan loai mau anh - cho duyet",
                Description = "Du an da ky quy, dang nam trong hang doi duyet cua admin.",
                GuidelineMarkdown = HuongDan,
                Stage = SeedStage.PendingApproval,
                IsPrivate = false,
                Modality = Modalities.Image,
                LabelSchemaJson = TapNhanMau,
                RewardVnd = 20000,
                Slots = 3,
                PassThresholdPercent = 80,
                DeadlineInDays = 30,
                RequireEntranceTest = false,
                EntranceQuestionCount = 10,
                EntrancePassPercent = 80,
                CreatedAgo = NgayGio(1, 0),
                EscrowedAgo = TimeSpan.FromHours(1),
                PublishedAgo = null,
                Samples = BonMau(id, k),
                Members = Array.Empty<SeedMember>(),
                Gold = Array.Empty<SeedGold>(),
                Attempts = Array.Empty<SeedAttempt>(),
            };
        }

        /// <summary>
        /// P3 — NHAP, SAN SANG PUBLISH: de test saga ky quy tu dau.
        /// Ky quy se la 3 x (30.000 + 9.000) = 117.000d.
        /// </summary>
        private static SeedProject TaoP3()
        {
            const string k = "p3";
            Guid id = SeedIds.Tu("project:" + k);

            return new SeedProject
            {
                Key = k,
                Id = id,
                OwnerId = User("biz1").Id,
                DatasetId = SeedIds.Tu("dataset:" + k),
                Name = "[Seed] Phan loai mau anh - nhap",
                Description = "Du an nhap da cau hinh du, bam publish la chay saga ky quy.",
                GuidelineMarkdown = HuongDan,
                Stage = SeedStage.Draft,
                IsPrivate = false,
                Modality = Modalities.Image,
                LabelSchemaJson = TapNhanMau,
                RewardVnd = 30000,
                Slots = 3,
                PassThresholdPercent = 80,
                DeadlineInDays = 30,
                RequireEntranceTest = false,
                EntranceQuestionCount = 10,
                EntrancePassPercent = 80,
                CreatedAgo = TimeSpan.FromHours(2),
                EscrowedAgo = null,
                PublishedAgo = null,
                Samples = BonMau(id, k),
                Members = Array.Empty<SeedMember>(),
                Gold = Array.Empty<SeedGold>(),
                Attempts = Array.Empty<SeedAttempt>(),
            };
        }

        /// <summary>
        /// P8 — NHAP, cong cu hinh hoc moi: khung XOAY (rotatable), da giac, diem moc. Doanh nghiep bam publish
        /// de thu ca luong (ky quy, gop nhan khung xoay theo trung vi goc, xuat yolo-obb / yolo-seg / coco-keypoints).
        /// Nhap thi chi project-svc seed — cac service khac khong co ban sao.
        /// </summary>
        private static SeedProject TaoP8()
        {
            const string k = "p8";
            Guid id = SeedIds.Tu("project:" + k);

            return new SeedProject
            {
                Key = k,
                Id = id,
                OwnerId = User("biz2").Id,
                DatasetId = SeedIds.Tu("dataset:" + k),
                Name = "[Seed] Anh - khung xoay, da giac, diem moc - nhap",
                Description = "Du an nhap dung cac cong cu hinh hoc: khung xoay (goc), vung da giac, diem moc.",
                GuidelineMarkdown = "# Huong dan\n\n- `khung`: ve khung bao vat the, XOAY khung theo huong vat the (goc do).\n"
                    + "- `vung`: khoanh vung mau chinh bang da giac.\n- `diem`: dat diem `tam` vao giua anh.",
                Stage = SeedStage.Draft,
                IsPrivate = false,
                Modality = Modalities.Image,
                LabelSchemaJson = "{\"modality\":\"image\",\"tools\":["
                    + "{\"name\":\"khung\",\"kind\":\"bbox\",\"classes\":[\"vat_the\"],\"rotatable\":true},"
                    + "{\"name\":\"vung\",\"kind\":\"polygon\",\"classes\":[\"mau_chinh\"]},"
                    + "{\"name\":\"diem\",\"kind\":\"keypoint\",\"classes\":[\"tam\"]}]}",
                RewardVnd = 40000,
                Slots = 3,
                PassThresholdPercent = 75,
                DeadlineInDays = 30,
                RequireEntranceTest = false,
                EntranceQuestionCount = 10,
                EntrancePassPercent = 80,
                CreatedAgo = TimeSpan.FromHours(1),
                EscrowedAgo = null,
                PublishedAgo = null,
                Samples = BonMau(id, k),
                Members = Array.Empty<SeedMember>(),
                Gold = Array.Empty<SeedGold>(),
                Attempts = Array.Empty<SeedAttempt>(),
            };
        }

        /// <summary>
        /// P4 — DANG CHAY, BAT BUOC TEST DAU VAO: 3 cau, dau khi dung ≥ 60% (2/3).
        /// 4 anh thuong (de 4 cau) + 3 anh lam cau test. Ky quy 3 x (40.000 + 12.000) = 156.000d.
        /// </summary>
        private static SeedProject TaoP4()
        {
            const string k = "p4";
            Guid id = SeedIds.Tu("project:" + k);

            List<SeedSample> mau = BonMau(id, k);
            mau.Add(Mau(id, k, 5, "do", 230, 60, 50));
            mau.Add(Mau(id, k, 6, "xanh_la", 60, 190, 80));
            mau.Add(Mau(id, k, 7, "vang", 245, 215, 70));

            return new SeedProject
            {
                Key = k,
                Id = id,
                OwnerId = User("biz1").Id,
                DatasetId = SeedIds.Tu("dataset:" + k),
                Name = "[Seed] Phan loai mau anh - can test dau vao",
                Description = "Du an dang chay, labeler phai dau bai test 3 cau truoc khi lam bai.",
                GuidelineMarkdown = HuongDan,
                Stage = SeedStage.Running,
                IsPrivate = false,
                Modality = Modalities.Image,
                LabelSchemaJson = TapNhanMau,
                RewardVnd = 40000,
                Slots = 3,
                PassThresholdPercent = 80,
                DeadlineInDays = 30,
                RequireEntranceTest = true,
                EntranceQuestionCount = 3,
                EntrancePassPercent = 60,
                CreatedAgo = NgayGio(3, 2),
                EscrowedAgo = NgayGio(3, 0),
                PublishedAgo = NgayGio(2, 23),
                Samples = mau,
                Members = Array.Empty<SeedMember>(),
                Gold = new SeedGold[]
                {
                    Vang(k, mau[4], true),
                    Vang(k, mau[5], true),
                    Vang(k, mau[6], true),
                },
                Attempts = Array.Empty<SeedAttempt>(),
            };
        }

        // =====================================================================
        // TIEN NAP
        // =====================================================================

        private static List<SeedDeposit> TaoDeposits()
        {
            List<SeedDeposit> ds = new List<SeedDeposit>();

            // biz1: da thanh toan → ledger co 2.000.000d. Tru ky quy P1 + P2 + P4 + P5 + P6 + P7
            // (195k + 78k + 156k + 78k + 97,5k + 117k, P7 hoan lai 39k) → con hon 1.300.000d — du publish P3.
            ds.Add(new SeedDeposit
            {
                IntentId = SeedIds.Tu("deposit:biz1-1"),
                BusinessId = User("biz1").Id,
                AmountVnd = 2000000,
                IdempotencyKey = "seed-nap-biz1-1",
                ProviderTxnId = "SANDBOX-SEED-0001",
                CreatedAgo = NgayGio(7, 1),
            });

            // biz2: CHUA thanh toan → so du 0. Dung de test trang sandbox va
            // nhanh "khong du tien" (escrow.rejected).
            ds.Add(new SeedDeposit
            {
                IntentId = SeedIds.Tu("deposit:biz2-1"),
                BusinessId = User("biz2").Id,
                AmountVnd = 1000000,
                IdempotencyKey = "seed-nap-biz2-1",
                ProviderTxnId = null,
                CreatedAgo = TimeSpan.FromHours(1),
            });

            return ds;
        }

        // =====================================================================
        // P5 — TEXT, P6 — AUDIO, P7 — PAIR (cac loai du lieu khac anh)
        // =====================================================================

        /// <summary>
        /// P5 — VAN BAN, DANG CHAY: hai cong cu tren cung mot cau — phan loai cam xuc
        /// va gan the thuc the (NER, span). 4 cau, 3 suat. Ky quy 3 x (20.000 + 6.000) = 78.000d.
        /// </summary>
        private static SeedProject TaoP5()
        {
            const string k = "p5";
            Guid id = SeedIds.Tu("project:" + k);

            string[] cau = new string[]
            {
                "Anh Minh rat hai long voi dich vu cua Viettel tai Ha Noi.",
                "Chi Lan noi giao hang cham va nhan vien thieu lich su.",
                "Cong ty FPT mo van phong moi o Da Nang.",
                "Hom nay troi mua nhe.",
            };

            List<SeedSample> mau = new List<SeedSample>();
            for (int i = 0; i < cau.Length; i++)
            {
                mau.Add(MauVanBan(k, i + 1, cau[i]));
            }

            Guid biz1 = User("biz1").Id;

            List<SeedAttempt> bai = new List<SeedAttempt>
            {
                // lab1 dang lam: da nhap 2/4 cau.
                BaiLam(k, id, "lab1", SeedAttemptState.InProgress, TimeSpan.FromMinutes(40), null, new List<SeedAnswer>
                {
                    TraLoi(k, mau[0], "lab1",
                        "{\"cam_xuc\":{\"labelIds\":[\"tich_cuc\"]},\"thuc_the\":["
                        + Doan(cau[0], "Minh", "TEN_NGUOI") + "," + Doan(cau[0], "Viettel", "TO_CHUC") + "," + Doan(cau[0], "Ha Noi", "DIA_DIEM") + "]}"),
                    TraLoi(k, mau[1], "lab1",
                        "{\"cam_xuc\":{\"labelIds\":[\"tieu_cuc\"]},\"thuc_the\":[" + Doan(cau[1], "Lan", "TEN_NGUOI") + "]}"),
                }, 0, 0, null),
            };

            return new SeedProject
            {
                Key = k,
                Id = id,
                OwnerId = biz1,
                DatasetId = SeedIds.Tu("dataset:" + k),
                Name = "[Seed] Van ban - cam xuc va thuc the",
                Description = "Moi cau: chon cam xuc va boi den ten nguoi, dia diem, to chuc.",
                GuidelineMarkdown = "# Huong dan\n\nChon cam xuc cua ca cau. Boi den tung thuc the (vi tri tinh theo ky tu, end khong tinh).",
                Modality = Modalities.Text,
                LabelSchemaJson =
                    "{\"modality\":\"text\",\"tools\":["
                    + "{\"name\":\"cam_xuc\",\"kind\":\"classification\",\"classes\":[\"tich_cuc\",\"tieu_cuc\",\"trung_tinh\"]},"
                    + "{\"name\":\"thuc_the\",\"kind\":\"span\",\"classes\":[\"TEN_NGUOI\",\"DIA_DIEM\",\"TO_CHUC\"]}]}",
                Stage = SeedStage.Running,
                IsPrivate = false,
                RewardVnd = 20000,
                Slots = 3,
                PassThresholdPercent = 80,
                DeadlineInDays = 30,
                RequireEntranceTest = false,
                EntranceQuestionCount = 10,
                EntrancePassPercent = 80,
                CreatedAgo = NgayGio(2, 2),
                EscrowedAgo = NgayGio(2, 1),
                PublishedAgo = NgayGio(2, 0),
                Samples = mau,
                Members = new SeedMember[]
                {
                    new SeedMember { UserId = User("lab1").Id, Role = SeedMemberRole.Labeler },
                    new SeedMember { UserId = User("lab2").Id, Role = SeedMemberRole.Labeler },
                },
                Gold = Array.Empty<SeedGold>(),
                Attempts = bai,
            };
        }

        /// <summary>
        /// P6 — AM THANH, DANG CHAY, CAT DOAN 10 GIAY: file 25 giay thanh 3 mau
        /// (0-10, 10-20, 20-25), file 8 giay giu nguyen. Cong cu: chep loi + danh
        /// dau doan thoi gian. Ky quy 3 x (25.000 + 7.500) = 97.500d.
        /// </summary>
        private static SeedProject TaoP6()
        {
            const string k = "p6";
            Guid id = SeedIds.Tu("project:" + k);

            string fileDai = id.ToString() + "/" + SeedIds.Tu("file:" + k + ":1").ToString() + ".wav";
            string fileNgan = id.ToString() + "/" + SeedIds.Tu("file:" + k + ":2").ToString() + ".wav";

            List<SeedSample> mau = new List<SeedSample>
            {
                MauAmThanh(k, 1, "cuoc-goi-01.wav", fileDai, 440, 25, 0, 10),
                MauAmThanh(k, 2, "cuoc-goi-01.wav", fileDai, 440, 25, 10, 20),
                MauAmThanh(k, 3, "cuoc-goi-01.wav", fileDai, 440, 25, 20, 25),
                MauAmThanh(k, 4, "loi-chao.wav", fileNgan, 660, 8, null, null),
            };

            return new SeedProject
            {
                Key = k,
                Id = id,
                OwnerId = User("biz1").Id,
                DatasetId = SeedIds.Tu("dataset:" + k),
                Name = "[Seed] Am thanh - chep loi va danh dau doan",
                Description = "Nghe tung doan 10 giay: chep lai loi noi va danh dau doan giong noi / nhac / im lang.",
                GuidelineMarkdown = "# Huong dan\n\nChep dung tung tu nghe duoc. Thoi gian tinh tu dau DOAN (0 den do dai doan).",
                Modality = Modalities.Audio,
                LabelSchemaJson =
                    "{\"modality\":\"audio\",\"segmentSeconds\":10,\"tools\":["
                    + "{\"name\":\"loi_noi\",\"kind\":\"transcription\"},"
                    + "{\"name\":\"doan\",\"kind\":\"temporalSegment\",\"classes\":[\"giong_noi\",\"nhac\",\"im_lang\"]}]}",
                Stage = SeedStage.Running,
                IsPrivate = false,
                RewardVnd = 25000,
                Slots = 3,
                PassThresholdPercent = 70,
                DeadlineInDays = 30,
                RequireEntranceTest = false,
                EntranceQuestionCount = 10,
                EntrancePassPercent = 80,
                CreatedAgo = NgayGio(1, 6),
                EscrowedAgo = NgayGio(1, 5),
                PublishedAgo = NgayGio(1, 4),
                Samples = mau,
                Members = new SeedMember[]
                {
                    new SeedMember { UserId = User("lab1").Id, Role = SeedMemberRole.Labeler },
                },
                Gold = Array.Empty<SeedGold>(),
                Attempts = Array.Empty<SeedAttempt>(),
            };
        }

        /// <summary>
        /// P7 — CAP CAU TRA LOI (RLHF), DA CHAM XONG: 3 cap, 3 suat, thuong 30.000d, nguong 80%.
        /// lab1 va lab2 chon "a" + "an_toan" ca 3 cau, rev1 chon "b" → da so la "a": lab1, lab2 dat 100%,
        /// rev1 0% (truot). Ky quy 3 x (30.000 + 9.000) = 117.000d: tra 2 x 30.000 cho labeler, 2 x 9.000 phi,
        /// hoan 39.000d (suat truot) cho biz1.
        /// </summary>
        private static SeedProject TaoP7()
        {
            const string k = "p7";
            Guid id = SeedIds.Tu("project:" + k);
            Guid biz1 = User("biz1").Id;

            List<SeedSample> mau = new List<SeedSample>
            {
                MauCap(k, 1, "Thu do cua Viet Nam la gi?",
                    "Thu do cua Viet Nam la Ha Noi.",
                    "Viet Nam co rat nhieu thanh pho dep."),
                MauCap(k, 2, "Lam sao de luoc trung long dao?",
                    "Luoc trung khoang 6 phut trong nuoc soi roi ngam nuoc lanh.",
                    "Luoc trung that lau, cang lau cang ngon."),
                MauCap(k, 3, "Viet mot cau chao buoi sang.",
                    "Chao buoi sang, chuc ban mot ngay tot lanh!",
                    "Chao."),
            };

            const string chonA = "{\"tot_hon\":{\"choice\":\"a\"},\"an_toan\":{\"labelIds\":[\"an_toan\"]}}";
            const string chonB = "{\"tot_hon\":{\"choice\":\"b\"},\"an_toan\":{\"labelIds\":[\"an_toan\"]}}";

            return new SeedProject
            {
                Key = k,
                Id = id,
                OwnerId = biz1,
                DatasetId = SeedIds.Tu("dataset:" + k),
                Name = "[Seed] So sanh cap cau tra loi LLM",
                Description = "Doc cau hoi va hai cau tra loi a, b: chon cau tot hon va danh gia co an toan khong.",
                GuidelineMarkdown = "# Huong dan\n\nChon cau tra loi DUNG va HUU ICH hon. Bang nhau thi chon tie.",
                Modality = Modalities.Pair,
                LabelSchemaJson =
                    "{\"modality\":\"pair\",\"tools\":["
                    + "{\"name\":\"tot_hon\",\"kind\":\"pairwise\",\"allowTie\":true},"
                    + "{\"name\":\"an_toan\",\"kind\":\"classification\",\"classes\":[\"an_toan\",\"khong_an_toan\"]}]}",
                Stage = SeedStage.Completed,
                IsPrivate = false,
                RewardVnd = 30000,
                Slots = 3,
                PassThresholdPercent = 80,
                DeadlineInDays = 30,
                RequireEntranceTest = false,
                EntranceQuestionCount = 10,
                EntrancePassPercent = 80,
                CreatedAgo = NgayGio(9, 0),
                EscrowedAgo = NgayGio(8, 23),
                PublishedAgo = NgayGio(8, 22),
                GradedAgo = NgayGio(4, 0),
                CompletedAgo = NgayGio(1, 0),
                Samples = mau,
                Members = new SeedMember[]
                {
                    new SeedMember { UserId = User("lab1").Id, Role = SeedMemberRole.Labeler },
                    new SeedMember { UserId = User("lab2").Id, Role = SeedMemberRole.Labeler },
                    new SeedMember { UserId = User("rev1").Id, Role = SeedMemberRole.Labeler },
                },
                Gold = Array.Empty<SeedGold>(),
                Attempts = new SeedAttempt[]
                {
                    BaiLam(k, id, "lab1", SeedAttemptState.Submitted, NgayGio(5, 2), NgayGio(5, 0), TraLoiCa(k, mau, "lab1", chonA), 0, 0, Dat(3, 3, true)),
                    BaiLam(k, id, "lab2", SeedAttemptState.Submitted, NgayGio(4, 6), NgayGio(4, 4), TraLoiCa(k, mau, "lab2", chonA), 0, 0, Dat(3, 3, true)),
                    BaiLam(k, id, "rev1", SeedAttemptState.Submitted, NgayGio(4, 3), NgayGio(4, 1), TraLoiCa(k, mau, "rev1", chonB), 0, 0, Dat(3, 0, false)),
                },
            };
        }

        // =====================================================================
        // Ham phu tro dung kich ban
        // =====================================================================

        /// <summary>Tap nhan cua P1-P4: mot cong cu phan loai mau, ten "label".</summary>
        public const string TapNhanMau =
            "{\"modality\":\"image\",\"tools\":[{\"name\":\"label\",\"kind\":\"classification\","
            + "\"classes\":[\"do\",\"xanh_la\",\"xanh_duong\",\"vang\"]}]}";

        /// <summary>Kich thuoc anh mau (px) — ProjectSeeder sinh anh dung kich thuoc nay.</summary>
        public const int KichThuocAnh = 256;

        /// <summary>Nhan phan loai cho P1-P4: {"label":{"labelIds":["do"]}}.</summary>
        public static string PhanLoai(string nhan)
        {
            return "{\"label\":{\"labelIds\":[\"" + nhan + "\"]}}";
        }

        private static SeedSample Mau(Guid projectId, string projectKey, int index, string nhan, byte r, byte g, byte b)
        {
            Guid id = SeedIds.Tu("sample:" + projectKey + ":" + index.ToString(CultureInfo.InvariantCulture));

            return new SeedSample
            {
                Id = id,
                Index = index,
                FileName = nhan + "-" + index.ToString("00", CultureInfo.InvariantCulture) + ".png",
                Modality = Modalities.Image,
                // Cung cong thuc voi Sample.TaoAnhTrongZip cua project-svc.
                StorageKey = projectId.ToString() + "/" + id.ToString() + ".png",
                Metadata = new SampleMetadata { Width = KichThuocAnh, Height = KichThuocAnh },
                TrueLabel = nhan,
                R = r,
                G = g,
                B = b,
            };
        }

        private static SeedSample MauVanBan(string projectKey, int index, string text)
        {
            System.Text.Json.Nodes.JsonObject noiDung = new System.Text.Json.Nodes.JsonObject { ["text"] = text };

            return new SeedSample
            {
                Id = SeedIds.Tu("sample:" + projectKey + ":" + index.ToString(CultureInfo.InvariantCulture)),
                Index = index,
                FileName = "cau-" + index.ToString("00", CultureInfo.InvariantCulture),
                Modality = Modalities.Text,
                ContentJson = noiDung.ToJsonString(),
                Metadata = new SampleMetadata { Length = text.Length },
            };
        }

        private static SeedSample MauCap(string projectKey, int index, string prompt, string a, string b)
        {
            System.Text.Json.Nodes.JsonObject noiDung = new System.Text.Json.Nodes.JsonObject
            {
                ["prompt"] = prompt,
                ["a"] = a,
                ["b"] = b,
            };

            return new SeedSample
            {
                Id = SeedIds.Tu("sample:" + projectKey + ":" + index.ToString(CultureInfo.InvariantCulture)),
                Index = index,
                FileName = "cap-" + index.ToString("00", CultureInfo.InvariantCulture),
                Modality = Modalities.Pair,
                ContentJson = noiDung.ToJsonString(),
                Metadata = SampleMetadata.Rong,
            };
        }

        /// <summary>Mot mau audio — ca file (batDau null) hoac mot doan [batDau, ketThuc] cua file.</summary>
        private static SeedSample MauAmThanh(
            string projectKey, int index, string tenFile, string khoaFile, double tanSo, double giayCaFile, double? batDau, double? ketThuc)
        {
            SampleMetadata md = batDau.HasValue && ketThuc.HasValue
                ? new SampleMetadata
                {
                    DurationSec = ketThuc.Value - batDau.Value,
                    SegmentStart = batDau.Value,
                    SegmentEnd = ketThuc.Value,
                    SourceDurationSec = giayCaFile,
                }
                : new SampleMetadata { DurationSec = giayCaFile };

            return new SeedSample
            {
                Id = SeedIds.Tu("sample:" + projectKey + ":" + index.ToString(CultureInfo.InvariantCulture)),
                Index = index,
                FileName = tenFile,
                Modality = Modalities.Audio,
                StorageKey = khoaFile,
                Metadata = md,
                ToneHz = tanSo,
                FileSeconds = giayCaFile,
            };
        }

        /// <summary>Mot span NER cho cum tu trong cau — tinh vi tri bang IndexOf de khoi dem tay.</summary>
        private static string Doan(string cau, string cumTu, string nhan)
        {
            int batDau = cau.IndexOf(cumTu, StringComparison.Ordinal);
            if (batDau < 0)
            {
                throw new InvalidOperationException("Cau khong chua cum tu: " + cumTu);
            }

            return "{\"labelId\":\"" + nhan + "\",\"start\":" + batDau.ToString(CultureInfo.InvariantCulture)
                   + ",\"end\":" + (batDau + cumTu.Length).ToString(CultureInfo.InvariantCulture) + "}";
        }

        /// <summary>Bon anh, moi mau mot anh.</summary>
        private static List<SeedSample> BonMau(Guid projectId, string projectKey)
        {
            return new List<SeedSample>
            {
                Mau(projectId, projectKey, 1, "do", 210, 45, 45),
                Mau(projectId, projectKey, 2, "xanh_la", 45, 160, 70),
                Mau(projectId, projectKey, 3, "xanh_duong", 45, 85, 200),
                Mau(projectId, projectKey, 4, "vang", 230, 195, 45),
            };
        }

        private static SeedGold Vang(string projectKey, SeedSample mau, bool choTest)
        {
            return new SeedGold
            {
                Id = SeedIds.Tu("gold:" + projectKey + ":" + mau.Index.ToString(CultureInfo.InvariantCulture)),
                SampleId = mau.Id,
                PayloadJson = PhanLoai(mau.TrueLabel!),
                ForEntranceTest = choTest,
            };
        }

        /// <summary>Mot cau tra loi — AnnotationId tinh tu (du an, mau, labeler) de cac service khop nhau.</summary>
        private static SeedAnswer TraLoi(string projectKey, SeedSample mau, string labelerKey, string payloadJson)
        {
            return new SeedAnswer
            {
                SampleId = mau.Id,
                PayloadJson = payloadJson,
                AnnotationId = SeedIds.Tu("annotation:" + projectKey + ":" + mau.Index.ToString(CultureInfo.InvariantCulture) + ":" + labelerKey),
            };
        }

        /// <summary>Tra loi DUNG mau that (TrueLabel) cho mau[tu..den) — du an phan loai P1-P4 (ca cau vang).</summary>
        private static List<SeedAnswer> TraLoiPhanLoai(string projectKey, List<SeedSample> mau, string labelerKey, int tu, int den)
        {
            List<SeedAnswer> ds = new List<SeedAnswer>();
            for (int i = tu; i < den && i < mau.Count; i++)
            {
                ds.Add(TraLoi(projectKey, mau[i], labelerKey, PhanLoai(mau[i].TrueLabel!)));
            }

            if (den >= mau.Count - 1 && mau.Count > 0)
            {
                // Bai day du: tra loi ca cau vang (mau cuoi) dung dap an.
                SeedSample cuoi = mau[mau.Count - 1];
                bool daCo = false;
                foreach (SeedAnswer a in ds)
                {
                    if (a.SampleId == cuoi.Id)
                    {
                        daCo = true;
                    }
                }

                if (!daCo)
                {
                    ds.Add(TraLoi(projectKey, cuoi, labelerKey, PhanLoai(cuoi.TrueLabel!)));
                }
            }

            return ds;
        }

        /// <summary>Cung mot cau tra loi cho moi mau.</summary>
        private static List<SeedAnswer> TraLoiCa(string projectKey, List<SeedSample> mau, string labelerKey, string payloadJson)
        {
            List<SeedAnswer> ds = new List<SeedAnswer>();
            foreach (SeedSample m in mau)
            {
                ds.Add(TraLoi(projectKey, m, labelerKey, payloadJson));
            }

            return ds;
        }

        private static SeedResult Dat(int soSoDuoc, int soGiong, bool dat)
        {
            return new SeedResult
            {
                ComparableCount = soSoDuoc,
                MatchedCount = soGiong,
                AgreementPercent = soSoDuoc == 0 ? 100 : soGiong * 100 / soSoDuoc,
                Passed = dat,
            };
        }

        private static SeedAttempt BaiLam(
            string projectKey,
            Guid projectId,
            string labelerKey,
            SeedAttemptState trangThai,
            TimeSpan batDauCach,
            TimeSpan? nopCach,
            IReadOnlyList<SeedAnswer> traLoi,
            int soCauVang,
            int soCauVangDung,
            SeedResult? ketQua)
        {
            return new SeedAttempt
            {
                AttemptId = SeedIds.Tu("attempt:" + projectKey + ":" + labelerKey),
                ProjectId = projectId,
                LabelerKey = labelerKey,
                LabelerId = User(labelerKey).Id,
                State = trangThai,
                StartedAgo = batDauCach,
                SubmittedAgo = nopCach,
                Answers = traLoi,
                GoldTotal = soCauVang,
                GoldCorrect = soCauVangDung,
                Result = ketQua,
            };
        }

        private static TimeSpan NgayGio(int ngay, int gio)
        {
            return TimeSpan.FromDays(ngay) + TimeSpan.FromHours(gio);
        }
    }
}
