using System;
using System.Collections.Generic;
using System.Globalization;
using Crowd.Labeling;

namespace Crowd.Seeding
{
    /// <summary>
    /// KICH BAN SEED — nguon su that duy nhat cua du lieu mau.
    ///
    /// Cau chuyen:
    ///   - Doanh nghiep 1 (biz1) nap 2.000.000d, co 4 du an o 4 buoc khac nhau
    ///     cua vong doi: P1 dang chay (co nhan da nop/duyet/khieu nai), P2 cho
    ///     admin duyet, P3 nhap san sang publish, P4 dang chay va bat buoc test
    ///     dau vao.
    ///   - Doanh nghiep 2 (biz2) chua co tien, co mot lenh nap 1.000.000d chua
    ///     thanh toan — de test trang thanh toan sandbox va nhanh "thieu tien".
    ///   - lab1 da duoc duyet 3 nhan tu 4 ngay truoc → 60.000d RUT DUOC NGAY.
    ///   - lab2 co nhan vua duoc duyet (tien dang treo), nhan bi tu choi (con
    ///     khieu nai duoc) va nhan dang khieu nai (cho admin).
    ///   - lab3 chua tham gia du an nao — de test tham gia / bai test dau vao.
    ///   - rev1 la reviewer cua P1.
    ///
    /// Anh mau la anh PNG mot mau (do / xanh la / xanh duong / vang), nen dap an
    /// dung nhin la thay — tien cho viec test duyet nhan bang mat.
    /// </summary>
    public static class KichBanSeed
    {
        /// <summary>Mat khau chung cho MOI tai khoan seed. Chi dung o Development.</summary>
        public const string MatKhauChung = "Matkhau@123";

        /// <summary>Phi nen tang chot cho moi du an seed — khop FeeOptions.PlatformFeePercent cua dev.</summary>
        /// <summary>Ti le cau vang kiem tra tron vao task — khop LabelingProject.GoldCheckPercentMacDinh.</summary>
        public const int TiLeCauVangKiemTra = 10;

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

        /// <summary>Moi mau la MOT task — TaskId tinh tu SampleId de task-svc va annotation-svc khop nhau.</summary>
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
            return ds;
        }

        private const string HuongDan =
            "# Huong dan\n\n"
            + "Moi anh la MOT mau. Chon dung mot lop:\n\n"
            + "- `do`\n- `xanh_la`\n- `xanh_duong`\n- `vang`\n\n"
            + "Anh toi hay sang deu chon theo sac mau chinh.";

        /// <summary>
        /// P1 — DANG CHAY, du lieu day du nhat: 8 anh, redundancy 2, co nhan cho
        /// duyet / da duyet / bi tu choi / dang khieu nai, mot cau vang kiem tra.
        ///
        /// Ky quy 500.000d ≥ 8 anh x 2 nguoi x (20.000 + 6.000 phi) = 416.000d.
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
            Guid rev1 = User("rev1").Id;

            List<SeedSubmission> nop = new List<SeedSubmission>
            {
                // lab1: ba nhan duoc duyet tu 4 ngay truoc → khoan treo da het han → RUT DUOC.
                Nop(k, id, mau[0], "lab1", "do", SeedReview.Approved, biz1, null, null,
                    NgayGio(4, 2), NgayGio(4, 0), null),
                Nop(k, id, mau[1], "lab1", "do", SeedReview.Approved, biz1, null, null,
                    NgayGio(4, 1), NgayGio(4, 0), null),
                Nop(k, id, mau[2], "lab1", "xanh_la", SeedReview.Approved, rev1, null, null,
                    NgayGio(4, 0), NgayGio(3, 22), null),

                // lab1: hai nhan dang cho duyet — de test duyet / tu choi.
                Nop(k, id, mau[3], "lab1", "xanh_la", SeedReview.PendingReview, null, null, null,
                    TimeSpan.FromMinutes(30), null, null),
                Nop(k, id, mau[5], "lab1", "xanh_duong", SeedReview.PendingReview, null, null, null,
                    TimeSpan.FromMinutes(20), null, null),

                // lab2: vua duoc duyet → tien dang TREO (dev treo 2 phut roi tu giai phong).
                Nop(k, id, mau[0], "lab2", "do", SeedReview.Approved, rev1, null, null,
                    TimeSpan.FromHours(2), TimeSpan.Zero, null),

                // lab2: bi tu choi 2 gio truoc → con khieu nai duoc (han 7 ngay).
                Nop(k, id, mau[1], "lab2", "vang", SeedReview.Rejected, rev1,
                    "Anh mau do, khong phai vang.", null,
                    TimeSpan.FromHours(3), TimeSpan.FromHours(2), null),

                // lab2: bi tu choi roi khieu nai → nam trong hang doi cua admin.
                Nop(k, id, mau[4], "lab2", "xanh_la", SeedReview.Appealed, biz1,
                    "Anh mau xanh duong.", "Toi thay anh nghieng ve xanh la, de nghi xem lai.",
                    NgayGio(1, 0), TimeSpan.FromHours(20), TimeSpan.FromHours(10)),
            };

            return new SeedProject
            {
                Key = k,
                Id = id,
                OwnerId = biz1,
                DatasetId = SeedIds.Tu("dataset:" + k),
                Name = "[Seed] Phan loai mau anh - dang chay",
                Description = "Du an mau dang chay: co nhan cho duyet, da duyet, bi tu choi va dang khieu nai.",
                GuidelineMarkdown = HuongDan,
                Stage = SeedStage.Running,
                IsPrivate = false,
                Modality = Modalities.Image,
                LabelSchemaJson = TapNhanMau,
                UnitPriceVnd = 20000,
                Redundancy = 2,
                BudgetVnd = 500000,
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
                    new SeedMember { UserId = rev1, Role = SeedMemberRole.Reviewer },
                },
                // Anh 8 la cau vang kiem tra chat luong → task cua no bi loai khoi pool.
                Gold = new SeedGold[] { Vang(k, mau[7], false) },
                Submissions = nop,
            };
        }

        /// <summary>
        /// P2 — CHO ADMIN DUYET: da ky quy 100.000d (≥ 4 x 1 x 13.000 = 52.000d).
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
                UnitPriceVnd = 10000,
                Redundancy = 1,
                BudgetVnd = 100000,
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
                Submissions = Array.Empty<SeedSubmission>(),
            };
        }

        /// <summary>
        /// P3 — NHAP, SAN SANG PUBLISH: de test saga ky quy tu dau.
        /// Can ky quy toi thieu 4 x 2 x (15.000 + 4.500) = 156.000d; ngan sach 200.000d.
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
                UnitPriceVnd = 15000,
                Redundancy = 2,
                BudgetVnd = 200000,
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
                Submissions = Array.Empty<SeedSubmission>(),
            };
        }

        /// <summary>
        /// P4 — DANG CHAY, BAT BUOC TEST DAU VAO: 3 cau, dau khi dung ≥ 60% (2/3).
        /// 4 anh thuong + 3 anh lam cau test. Ky quy 300.000d ≥ 7 x 1 x 39.000 = 273.000d.
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
                Description = "Du an dang chay, labeler phai dau bai test 3 cau truoc khi nhan task.",
                GuidelineMarkdown = HuongDan,
                Stage = SeedStage.Running,
                IsPrivate = false,
                Modality = Modalities.Image,
                LabelSchemaJson = TapNhanMau,
                UnitPriceVnd = 30000,
                Redundancy = 1,
                BudgetVnd = 300000,
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
                Submissions = Array.Empty<SeedSubmission>(),
            };
        }

        // =====================================================================
        // TIEN NAP
        // =====================================================================

        private static List<SeedDeposit> TaoDeposits()
        {
            List<SeedDeposit> ds = new List<SeedDeposit>();

            // biz1: da thanh toan → ledger co 2.000.000d. Tru ky quy P1 + P2 + P4
            // (500k + 100k + 300k) con 1.100.000d kha dung — du publish P3.
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
        /// va gan the thuc the (NER, span). 4 cau, redundancy 1.
        /// Ky quy 50.000d ≥ 4 x 1 x (5.000 + 1.500) = 26.000d.
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

            List<SeedSubmission> nop = new List<SeedSubmission>
            {
                NopJson(k, id, mau[0], "lab1",
                    "{\"cam_xuc\":{\"labelIds\":[\"tich_cuc\"]},\"thuc_the\":["
                    + Doan(cau[0], "Minh", "TEN_NGUOI") + "," + Doan(cau[0], "Viettel", "TO_CHUC") + "," + Doan(cau[0], "Ha Noi", "DIA_DIEM") + "]}",
                    SeedReview.PendingReview, null, TimeSpan.FromMinutes(40), null),
                NopJson(k, id, mau[1], "lab2",
                    "{\"cam_xuc\":{\"labelIds\":[\"tieu_cuc\"]},\"thuc_the\":[" + Doan(cau[1], "Lan", "TEN_NGUOI") + "]}",
                    SeedReview.Approved, biz1, NgayGio(1, 2), NgayGio(1, 0)),
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
                UnitPriceVnd = 5000,
                Redundancy = 1,
                BudgetVnd = 50000,
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
                Submissions = nop,
            };
        }

        /// <summary>
        /// P6 — AM THANH, DANG CHAY, CAT DOAN 10 GIAY: file 25 giay thanh 3 mau
        /// (0-10, 10-20, 20-25), file 8 giay giu nguyen. Cong cu: chep loi + danh
        /// dau doan thoi gian. Ky quy 50.000d ≥ 4 x 1 x (8.000 + 2.400) = 41.600d.
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
                UnitPriceVnd = 8000,
                Redundancy = 1,
                BudgetVnd = 50000,
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
                Submissions = new SeedSubmission[]
                {
                    NopJson(k, id, mau[0], "lab1",
                        "{\"loi_noi\":{\"text\":\"(chi co tieng nhac nen)\"},\"doan\":[{\"labelId\":\"nhac\",\"start\":0,\"end\":10}]}",
                        SeedReview.PendingReview, null, TimeSpan.FromMinutes(25), null),
                },
            };
        }

        /// <summary>
        /// P7 — CAP CAU TRA LOI (RLHF), DANG CHAY: chon cau tra loi tot hon va
        /// danh gia an toan. Redundancy 2 — hai nguoi cung chon a → gop da so ra "a".
        /// Bat redundancy thich ung: tran 3 nguoi — cap tranh chap duoc xin them nguoi thu ba.
        /// Ky quy tinh theo tran: 50.000d ≥ 3 x 3 x (3.000 + 900) = 35.100d.
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
                Stage = SeedStage.Running,
                IsPrivate = false,
                UnitPriceVnd = 3000,
                Redundancy = 2,
                MaxRedundancy = 3,
                BudgetVnd = 50000,
                DeadlineInDays = 30,
                RequireEntranceTest = false,
                EntranceQuestionCount = 10,
                EntrancePassPercent = 80,
                CreatedAgo = NgayGio(3, 0),
                EscrowedAgo = NgayGio(2, 23),
                PublishedAgo = NgayGio(2, 22),
                Samples = mau,
                Members = new SeedMember[]
                {
                    new SeedMember { UserId = User("lab1").Id, Role = SeedMemberRole.Labeler },
                    new SeedMember { UserId = User("lab2").Id, Role = SeedMemberRole.Labeler },
                },
                Gold = Array.Empty<SeedGold>(),
                Submissions = new SeedSubmission[]
                {
                    NopJson(k, id, mau[0], "lab1", chonA, SeedReview.Approved, biz1, NgayGio(2, 10), NgayGio(2, 8)),
                    NopJson(k, id, mau[0], "lab2", chonA, SeedReview.Approved, biz1, NgayGio(2, 9), NgayGio(2, 8)),
                    NopJson(k, id, mau[1], "lab2", chonA, SeedReview.PendingReview, null, TimeSpan.FromMinutes(15), null),
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

        /// <summary>Luot nop PHAN LOAI cho P1-P4.</summary>
        private static SeedSubmission Nop(
            string projectKey,
            Guid projectId,
            SeedSample mau,
            string labelerKey,
            string nhan,
            SeedReview ketQua,
            Guid? reviewerId,
            string? lyDoTuChoi,
            string? noiDungKhieuNai,
            TimeSpan nopCach,
            TimeSpan? duyetCach,
            TimeSpan? khieuNaiCach)
        {
            return TaoLuotNop(projectKey, projectId, mau, labelerKey, PhanLoai(nhan), ketQua, reviewerId,
                lyDoTuChoi, noiDungKhieuNai, nopCach, duyetCach, khieuNaiCach);
        }

        /// <summary>Luot nop voi nhan JSON tuy y (text, audio, pair...).</summary>
        private static SeedSubmission NopJson(
            string projectKey,
            Guid projectId,
            SeedSample mau,
            string labelerKey,
            string payloadJson,
            SeedReview ketQua,
            Guid? reviewerId,
            TimeSpan nopCach,
            TimeSpan? duyetCach)
        {
            return TaoLuotNop(projectKey, projectId, mau, labelerKey, payloadJson, ketQua, reviewerId, null, null, nopCach, duyetCach, null);
        }

        private static SeedSubmission TaoLuotNop(
            string projectKey,
            Guid projectId,
            SeedSample mau,
            string labelerKey,
            string payloadJson,
            SeedReview ketQua,
            Guid? reviewerId,
            string? lyDoTuChoi,
            string? noiDungKhieuNai,
            TimeSpan nopCach,
            TimeSpan? duyetCach,
            TimeSpan? khieuNaiCach)
        {
            string ten = projectKey + ":" + mau.Index.ToString(CultureInfo.InvariantCulture) + ":" + labelerKey;

            return new SeedSubmission
            {
                AssignmentId = SeedIds.Tu("assignment:" + ten),
                TaskId = TaskIdCua(mau.Id),
                AnnotationId = SeedIds.Tu("annotation:" + ten),
                ProjectId = projectId,
                SampleId = mau.Id,
                StorageKey = mau.StorageKey,
                LabelerId = User(labelerKey).Id,
                PayloadJson = payloadJson,
                Review = ketQua,
                ReviewerId = reviewerId,
                RejectReason = lyDoTuChoi,
                AppealMessage = noiDungKhieuNai,
                SubmittedAgo = nopCach,
                ReviewedAgo = duyetCach,
                AppealedAgo = khieuNaiCach,
            };
        }

        private static TimeSpan NgayGio(int ngay, int gio)
        {
            return TimeSpan.FromDays(ngay) + TimeSpan.FromHours(gio);
        }
    }
}
