using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Crowd.BuildingBlocks.Settings
{
    /// <summary>Khoa cua moi setting. Dung hang so de code khong go sai chuoi.</summary>
    public static class SettingKeys
    {
        // ---- Phi va tien ----
        public const string FeePlatformPercent = "fee.platform_percent";
        public const string PaymentDepositMinVnd = "payment.deposit_min_vnd";
        public const string PaymentDepositMaxVnd = "payment.deposit_max_vnd";
        public const string PaymentManualTransferEnabled = "payment.manual_transfer_enabled";
        public const string PaymentManualTransferAutoApproveMaxVnd = "payment.manual_transfer_auto_approve_max_vnd";
        public const string PaymentManualTransferBankInfo = "payment.manual_transfer_bank_info";
        public const string PaymentPayoutInterval = "payment.payout_interval";
        public const string PaymentPayoutLookupAfter = "payment.payout_lookup_after";
        public const string LedgerHoldDuration = "ledger.hold_duration";
        public const string LedgerHoldReleaseInterval = "ledger.hold_release_interval";
        public const string LedgerHoldReleaseBatchSize = "ledger.hold_release_batch_size";
        public const string LedgerGatePayoutMode = "ledger.gate_payout_mode";
        public const string LedgerGateBatchInterval = "ledger.gate_batch_interval";
        public const string LedgerGateBatchSize = "ledger.gate_batch_size";
        public const string LedgerWithdrawMinVnd = "ledger.withdraw_min_vnd";
        public const string LedgerWithdrawTaxThresholdVnd = "ledger.withdraw_tax_threshold_vnd";
        public const string LedgerWithdrawTaxPercent = "ledger.withdraw_tax_percent";
        public const string LedgerWithdrawAutoApprove = "ledger.withdraw_auto_approve";
        public const string LedgerWithdrawAutoApproveMaxVnd = "ledger.withdraw_auto_approve_max_vnd";

        // ---- Du an ----
        public const string ProjectAutoApprove = "project.auto_approve";
        public const string ProjectApprovalTimeout = "project.approval_timeout";
        public const string ProjectApprovalScanInterval = "project.approval_scan_interval";
        public const string ProjectRedundancyMax = "project.redundancy_max";
        public const string ProjectGoldCheckPercentDefault = "project.gold_check_percent_default";
        public const string ProjectGoldCheckPercentMax = "project.gold_check_percent_max";
        public const string ProjectEntranceQuestionDefault = "project.entrance_question_default";
        public const string ProjectEntrancePassPercentDefault = "project.entrance_pass_percent_default";
        public const string ProjectEntranceQuestionMax = "project.entrance_question_max";
        public const string ProjectGoldItemsMax = "project.gold_items_max";
        public const string ProjectNameMaxLength = "project.name_max_length";
        public const string ProjectDescriptionMaxLength = "project.description_max_length";
        public const string GuidelineMarkdownMaxLength = "guideline.markdown_max_length";
        public const string GuidelineExamplesMax = "guideline.examples_max";
        public const string GuidelineExplanationMaxLength = "guideline.explanation_max_length";
        public const string EntranceMaxAttempts = "entrance.max_attempts";
        public const string EntranceDuration = "entrance.duration";

        // ---- Du lieu va file ----
        public const string DatasetZipMaxBytes = "dataset.zip_max_bytes";
        public const string DatasetZipMaxEntries = "dataset.zip_max_entries";
        public const string DatasetZipImageMaxBytes = "dataset.zip_image_max_bytes";
        public const string DatasetZipTotalMaxBytes = "dataset.zip_total_max_bytes";
        public const string DatasetManifestInlineMaxRows = "dataset.manifest_inline_max_rows";
        public const string DatasetManifestFileMaxRows = "dataset.manifest_file_max_rows";
        public const string DatasetTextMaxChars = "dataset.text_max_chars";
        public const string DatasetImageMaxBytes = "dataset.image_max_bytes";
        public const string DatasetEventBatchSize = "dataset.event_batch_size";
        public const string DatasetWorkerIdle = "dataset.worker_idle";
        public const string DatasetNameMaxLength = "dataset.name_max_length";
        public const string DatasetErrorSummaryMaxLength = "dataset.error_summary_max_length";
        public const string DatasetErrorSampleCount = "dataset.error_sample_count";
        public const string SampleNameMaxLength = "sample.name_max_length";
        public const string UploadMaxFiles = "upload.max_files";
        public const string UploadMaxFileBytes = "upload.max_file_bytes";
        public const string UploadLinkTtl = "upload.link_ttl";
        public const string StorageViewLinkTtl = "storage.view_link_ttl";
        public const string MediaFfprobeTimeout = "media.ffprobe_timeout";

        // ---- Gan nhan, duyet ----
        public const string TaskLeaseDuration = "task.lease_duration";
        public const string TaskReaperInterval = "task.reaper_interval";
        public const string TaskReaperBatchSize = "task.reaper_batch_size";
        public const string TaskLeaseRetryCount = "task.lease_retry_count";
        public const string TaskLeaseRetryDelay = "task.lease_retry_delay";
        public const string TaskLeaseCandidateWindow = "task.lease_candidate_window";
        public const string AnnotationAppealWindow = "annotation.appeal_window";
        public const string AnnotationReasonMaxLength = "annotation.reason_max_length";
        public const string AnnotationBulkApproveMax = "annotation.bulk_approve_max";
        public const string AnnotationAutoApproveAgreed = "annotation.auto_approve_agreed";

        // ---- Chat luong ----
        public const string QualityDsInterval = "quality.ds_interval";
        public const string QualityDsMinLabels = "quality.ds_min_labels";
        public const string QualityReputationGoldWeight = "quality.reputation_gold_weight";
        public const string QualityReputationPrior = "quality.reputation_prior";
        public const string QualityReputationPriorStrength = "quality.reputation_prior_strength";
        public const string QualityRedundancyPolicy = "quality.redundancy_policy";
        public const string QualityPosteriorTarget = "quality.posterior_target";
        public const string QualityVoiValueRatio = "quality.voi_value_ratio";
        public const string LabelingThresholdBbox = "labeling.threshold.bbox";
        public const string LabelingThresholdPolygon = "labeling.threshold.polygon";
        public const string LabelingThresholdSpan = "labeling.threshold.span";
        public const string LabelingThresholdTranscription = "labeling.threshold.transcription";
        public const string LabelingThresholdTemporalSegment = "labeling.threshold.temporal_segment";

        // ---- Tai khoan ----
        public const string IdentityAccessTokenLifetime = "identity.access_token_lifetime";
        public const string IdentityRefreshTokenLifetime = "identity.refresh_token_lifetime";
        public const string IdentityPasswordMinLength = "identity.password_min_length";
        public const string IdentityPasswordMaxLength = "identity.password_max_length";

        // ---- Cong link (P2) ----
        public const string GateEnabled = "gate.enabled";
        public const string GateCountdown = "gate.countdown";
        public const string GateGoldPerSession = "gate.gold_per_session";
        public const string GateRealPerSession = "gate.real_per_session";
        public const string GateSessionTtl = "gate.session_ttl";
        public const string GateTokenTtl = "gate.token_ttl";
        public const string GateIpDedupeWindow = "gate.ip_dedupe_window";
        public const string GateMaxLabelsPerSample = "gate.max_labels_per_sample";
        public const string GateCacheRefreshInterval = "gate.cache_refresh_interval";
        public const string GateEventFlushInterval = "gate.event_flush_interval";
        public const string GateEventBatchSize = "gate.event_batch_size";
        public const string LinkCodeLength = "link.code_length";
        public const string LinkBulkMax = "link.bulk_max";
        public const string LinkReportReviewThreshold = "link.report_review_threshold";
        public const string LinkScanInterval = "link.scan_interval";
        public const string LinkReferralPercent = "link.referral_percent";
        public const string LinkReferralMinEarningsVnd = "link.referral_min_earnings_vnd";
        public const string LinkReferralClaimWindow = "link.referral_claim_window";

        // ---- Ky thuat ----
        public const string OutboxPollInterval = "outbox.poll_interval";
        public const string OutboxBatchSize = "outbox.batch_size";
        public const string OutboxPublishTimeout = "outbox.publish_timeout";
        public const string OutboxRetryMaxDelay = "outbox.retry_max_delay";
        public const string ConsumersReconnectDelay = "consumers.reconnect_delay";
        public const string ConsumersPrefetchCount = "consumers.prefetch_count";
        public const string SettingsReloadInterval = "settings.reload_interval";
        public const string SettingsSnapshotInterval = "settings.snapshot_interval";
    }

    /// <summary>
    /// Danh muc MOI setting cua he thong. admin-svc nap gia tri khoi tao tu day lan
    /// dau; sau do gia tri that do admin sua trong bang settings.
    ///
    /// Them setting moi = them mot dong o day + mot hang so trong SettingKeys, roi
    /// cap nhat anh chup shared/settings/catalog.json (test SettingCatalogSnapshotTests).
    /// </summary>
    public static class SettingCatalog
    {
        private const string NhomTien = "Phi va tien";
        private const string NhomDuyet = "Tu dong duyet";
        private const string NhomDuAn = "Du an";
        private const string NhomDuLieu = "Du lieu va file";
        private const string NhomGanNhan = "Gan nhan va duyet nhan";
        private const string NhomChatLuong = "Chat luong";
        private const string NhomTaiKhoan = "Tai khoan";
        private const string NhomCongLink = "Cong link";

        private static readonly string[] ChinhSachRedundancy = new string[] { "majority", "posterior", "voi" };

        private static readonly string[] CheDoChiCongLink = new string[] { "perClick", "batched" };
        private const string NhomKyThuat = "Ky thuat";

        private const double Ngay = 86400;
        private const double MB = 1024 * 1024;

        public static readonly IReadOnlyList<SettingDefinition> TatCa = Tao();

        private static readonly Dictionary<string, SettingDefinition> TheoKhoa = TaoTra();

        public static SettingDefinition? Tim(string key)
        {
            SettingDefinition? d;
            return key != null && TheoKhoa.TryGetValue(key, out d) ? d : null;
        }

        public static SettingDefinition Lay(string key)
        {
            SettingDefinition? d = Tim(key);
            if (d == null)
            {
                throw new KeyNotFoundException("Khong co setting '" + key + "' trong danh muc.");
            }

            return d;
        }

        private static Dictionary<string, SettingDefinition> TaoTra()
        {
            Dictionary<string, SettingDefinition> d = new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);
            foreach (SettingDefinition s in TatCa)
            {
                if (d.ContainsKey(s.Key))
                {
                    throw new InvalidOperationException("Trung khoa setting '" + s.Key + "'.");
                }

                d[s.Key] = s;
            }

            return d;
        }

        private static List<SettingDefinition> Tao()
        {
            List<SettingDefinition> ds = new List<SettingDefinition>();

            // ================= PHI VA TIEN =================
            ds.Add(Int(SettingKeys.FeePlatformPercent, NhomTien, 30, 0, 90, "%",
                "Phi nen tang cong them tren don gia moi nhan. Chot vao du an luc publish — du an dang chay giu muc cu."));
            ds.Add(Long(SettingKeys.PaymentDepositMinVnd, NhomTien, 10000, 1000, 1e12, "dong", "So tien nap toi thieu mot lan."));
            ds.Add(Long(SettingKeys.PaymentDepositMaxVnd, NhomTien, 500000000, 1000, 1e13, "dong", "So tien nap toi da mot lan."));
            ds.Add(Bool(SettingKeys.PaymentManualTransferEnabled, NhomTien, true,
                "Cho phep nap bang chuyen khoan thu cong (doanh nghiep bao da chuyen, admin xac nhan)."));
            ds.Add(Text(SettingKeys.PaymentManualTransferBankInfo, NhomTien,
                "Ngan hang DATN Bank - STK 0123456789 - CONG TY DATN", 500,
                "Thong tin tai khoan nhan tien hien cho doanh nghiep khi nap chuyen khoan."));
            ds.Add(Seconds(SettingKeys.PaymentPayoutInterval, NhomKyThuat, 5, 1, 3600,
                "Chu ky worker gui lenh chi tien ra cong thanh toan."));
            ds.Add(Seconds(SettingKeys.PaymentPayoutLookupAfter, NhomKyThuat, 15 * 60, 60, Ngay,
                "Lenh chi treo qua lau thi hoi nguoc cong ve trang thai (doi soat)."));
            ds.Add(Seconds(SettingKeys.LedgerHoldDuration, NhomTien, 3 * Ngay, 0, 90 * Ngay,
                "Tien cong vua duyet bi TREO bao lau truoc khi labeler rut duoc (de xu ly khieu nai, gian lan)."));
            ds.Add(Seconds(SettingKeys.LedgerHoldReleaseInterval, NhomKyThuat, 60, 5, 3600,
                "Chu ky worker quet khoan treo den han."));
            ds.Add(Int(SettingKeys.LedgerHoldReleaseBatchSize, NhomKyThuat, 100, 1, 10000, "khoan", "So khoan treo den han giai phong moi lo."));
            ds.Add(Choice(SettingKeys.LedgerGatePayoutMode, NhomKyThuat, "perClick", CheDoChiCongLink,
                "Chi tien luot vuot link: perClick = moi luot mot but toan ngay (tien vao vi sau vai giay, nghen khi tai cao); "
                + "batched = gom theo du an moi ledger.gate_batch_interval roi viet mot but toan (VD-M-08)."));
            ds.Add(Seconds(SettingKeys.LedgerGateBatchInterval, NhomKyThuat, 60, 1, 3600, "Che do batched: gom luot vuot link bao lau mot lan."));
            ds.Add(Int(SettingKeys.LedgerGateBatchSize, NhomKyThuat, 5000, 1, 100000, "luot", "Che do batched: toi da bay nhieu luot moi lo."));
            ds.Add(Long(SettingKeys.LedgerWithdrawMinVnd, NhomTien, 50000, 0, 1e12, "dong", "So tien rut toi thieu mot lan."));
            ds.Add(Long(SettingKeys.LedgerWithdrawTaxThresholdVnd, NhomTien, 2000000, 0, 1e13, "dong",
                "Rut tu muc nay tro len thi khau tru thue TNCN."));
            ds.Add(Int(SettingKeys.LedgerWithdrawTaxPercent, NhomTien, 10, 0, 50, "%", "Thue suat khau tru khi rut tien."));

            // ================= TU DONG DUYET =================
            ds.Add(Bool(SettingKeys.ProjectAutoApprove, NhomDuyet, false,
                "Bat: du an ky quy xong chay ngay, khong cho admin duyet."));
            ds.Add(Bool(SettingKeys.LedgerWithdrawAutoApprove, NhomDuyet, true,
                "Bat: lenh rut khong vuot nguong ben duoi duoc chuyen tien ngay. Tat: moi lenh rut cho admin duyet."));
            ds.Add(Long(SettingKeys.LedgerWithdrawAutoApproveMaxVnd, NhomDuyet, 2000000, 0, 1e13, "dong",
                "Nguong tu duyet rut tien: lenh lon hon so nay luon cho admin duyet."));
            ds.Add(Long(SettingKeys.PaymentManualTransferAutoApproveMaxVnd, NhomDuyet, 0, 0, 1e12, "dong",
                "Lenh nap chuyen khoan thu cong KHONG vuot so nay duoc tu xac nhan khi doanh nghiep bao da chuyen. "
                + "0 = luon cho admin. CANH BAO: tu xac nhan la cong tien ma chua ai doi chieu sao ke."));
            ds.Add(Bool(SettingKeys.AnnotationAutoApproveAgreed, NhomDuyet, false,
                "Bat: nhan khop ket qua dong thuan duoc tu duyet va tra tien; nhan lech van cho nguoi duyet."));

            // ================= DU AN =================
            ds.Add(Seconds(SettingKeys.ProjectApprovalTimeout, NhomDuAn, 72 * 3600, 3600, 30 * Ngay,
                "Du an cho admin duyet qua lau thi tu huy va hoan ky quy."));
            ds.Add(Seconds(SettingKeys.ProjectApprovalScanInterval, NhomKyThuat, 10 * 60, 10, Ngay,
                "Chu ky quet du an cho duyet qua han."));
            ds.Add(Int(SettingKeys.ProjectRedundancyMax, NhomDuAn, 10, 1, 50, "nguoi",
                "So nguoi gan / tran redundancy toi da doanh nghiep duoc dat cho moi mau."));
            ds.Add(Int(SettingKeys.ProjectGoldCheckPercentDefault, NhomDuAn, 10, 0, 100, "%",
                "Ti le cau vang kiem tra mac dinh cua du an moi tao."));
            ds.Add(Int(SettingKeys.ProjectGoldCheckPercentMax, NhomDuAn, 50, 0, 100, "%",
                "Ti le cau vang kiem tra toi da doanh nghiep duoc dat."));
            ds.Add(Int(SettingKeys.ProjectEntranceQuestionDefault, NhomDuAn, 10, 1, 500, "cau",
                "So cau bai test dau vao mac dinh cua du an moi tao."));
            ds.Add(Int(SettingKeys.ProjectEntrancePassPercentDefault, NhomDuAn, 80, 1, 100, "%",
                "Nguong dau bai test dau vao mac dinh cua du an moi tao."));
            ds.Add(Int(SettingKeys.ProjectEntranceQuestionMax, NhomDuAn, 50, 1, 500, "cau", "So cau bai test dau vao toi da."));
            ds.Add(Int(SettingKeys.ProjectGoldItemsMax, NhomDuAn, 2000, 1, 100000, "cau", "So cau hoi vang toi da moi du an."));
            ds.Add(Int(SettingKeys.ProjectNameMaxLength, NhomDuAn, 200, 10, 200, "ky tu", "Do dai ten du an (cot DB toi da 200)."));
            ds.Add(Int(SettingKeys.ProjectDescriptionMaxLength, NhomDuAn, 5000, 100, 5000, "ky tu",
                "Do dai mo ta du an (cot DB toi da 5000)."));
            ds.Add(Int(SettingKeys.GuidelineMarkdownMaxLength, NhomDuAn, 20000, 100, 200000, "ky tu", "Do dai huong dan (markdown)."));
            ds.Add(Int(SettingKeys.GuidelineExamplesMax, NhomDuAn, 50, 0, 500, "vi du", "So vi du toi da trong huong dan."));
            ds.Add(Int(SettingKeys.GuidelineExplanationMaxLength, NhomDuAn, 1000, 10, 10000, "ky tu", "Do dai giai thich moi vi du."));
            ds.Add(Int(SettingKeys.EntranceMaxAttempts, NhomDuAn, 3, 1, 100, "lan", "So lan lam bai test dau vao toi da moi du an."));
            ds.Add(Seconds(SettingKeys.EntranceDuration, NhomDuAn, 30 * 60, 60, Ngay, "Thoi gian lam mot bai test dau vao."));

            // ================= DU LIEU VA FILE =================
            ds.Add(Long(SettingKeys.DatasetZipMaxBytes, NhomDuLieu, 200 * MB, MB, 2048 * MB, "byte",
                "Kich thuoc toi da file ZIP anh upload qua API (gateway va project-svc cung ap)."));
            ds.Add(Int(SettingKeys.DatasetZipMaxEntries, NhomDuLieu, 10000, 1, 1000000, "file", "So file toi da trong mot ZIP."));
            ds.Add(Long(SettingKeys.DatasetZipImageMaxBytes, NhomDuLieu, 20 * MB, 1024, 1024 * MB, "byte",
                "Kich thuoc toi da moi anh trong ZIP (sau giai nen)."));
            ds.Add(Long(SettingKeys.DatasetZipTotalMaxBytes, NhomDuLieu, 2048 * MB, MB, 100L * 1024 * MB, "byte",
                "Tong dung luong sau giai nen toi da cua mot ZIP (chong bom ZIP)."));
            ds.Add(Int(SettingKeys.DatasetManifestInlineMaxRows, NhomDuLieu, 1000, 1, 100000, "dong",
                "So dong manifest toi da gui kem request."));
            ds.Add(Int(SettingKeys.DatasetManifestFileMaxRows, NhomDuLieu, 50000, 1, 10000000, "dong",
                "So dong toi da cua file manifest .jsonl."));
            ds.Add(Int(SettingKeys.DatasetTextMaxChars, NhomDuLieu, 100000, 100, 10000000, "ky tu", "Do dai toi da mot mau van ban."));
            ds.Add(Long(SettingKeys.DatasetImageMaxBytes, NhomDuLieu, 50 * MB, 1024, 2048 * MB, "byte",
                "Kich thuoc toi da mot anh nap qua manifest."));
            ds.Add(Int(SettingKeys.DatasetEventBatchSize, NhomKyThuat, 500, 1, 10000, "mau",
                "So mau moi event dataset.ingested (chia lo khi nap nhieu)."));
            ds.Add(Seconds(SettingKeys.DatasetWorkerIdle, NhomKyThuat, 2, 0.1, 600, "Worker nap du lieu nghi bao lau khi het viec."));
            ds.Add(Int(SettingKeys.DatasetNameMaxLength, NhomDuLieu, 200, 10, 200, "ky tu", "Do dai ten lo du lieu (cot DB toi da 200)."));
            ds.Add(Int(SettingKeys.DatasetErrorSummaryMaxLength, NhomDuLieu, 4000, 100, 4000, "ky tu",
                "Do dai tom tat loi cua lo (cot DB toi da 4000)."));
            ds.Add(Int(SettingKeys.DatasetErrorSampleCount, NhomDuLieu, 20, 1, 1000, "dong", "So dong loi dau tien ghi lai khi nap mot lo du lieu."));
            ds.Add(Int(SettingKeys.SampleNameMaxLength, NhomDuLieu, 500, 10, 500, "ky tu", "Do dai ten mau (cot DB toi da 500)."));
            ds.Add(Int(SettingKeys.UploadMaxFiles, NhomDuLieu, 100, 1, 10000, "file", "So file toi da moi lan xin link upload."));
            ds.Add(Long(SettingKeys.UploadMaxFileBytes, NhomDuLieu, 5L * 1024 * MB, MB, 5L * 1024 * MB, "byte",
                "Kich thuoc toi da moi file upload thang (gioi han cua mot lenh PUT S3 la 5 GB)."));
            ds.Add(Seconds(SettingKeys.UploadLinkTtl, NhomDuLieu, 3600, 60, 7 * Ngay, "Link upload ky san song bao lau."));
            ds.Add(Seconds(SettingKeys.StorageViewLinkTtl, NhomDuLieu, 300, 10, 7 * Ngay,
                "Link xem file ky san (anh, am thanh, video) song bao lau."));
            ds.Add(Seconds(SettingKeys.MediaFfprobeTimeout, NhomKyThuat, 60, 1, 3600, "Thoi gian toi da cho mot lan ffprobe do file."));

            // ================= GAN NHAN VA DUYET =================
            ds.Add(Seconds(SettingKeys.TaskLeaseDuration, NhomGanNhan, 15 * 60, 30, Ngay,
                "Labeler giu mot task bao lau; qua han task ve hang doi cho nguoi khac."));
            ds.Add(Seconds(SettingKeys.TaskReaperInterval, NhomKyThuat, 30, 1, 3600, "Chu ky quet lease qua han."));
            ds.Add(Int(SettingKeys.TaskReaperBatchSize, NhomKyThuat, 200, 1, 10000, "lease", "So lease qua han reaper khoa va tra ve pool moi lo."));
            ds.Add(Int(SettingKeys.TaskLeaseRetryCount, NhomKyThuat, 5, 0, 100, "lan",
                "So lan thu lai khi khong khoa duoc task nao trong luc van con ung vien."));
            ds.Add(Seconds(SettingKeys.TaskLeaseRetryDelay, NhomKyThuat, 0.04, 0, 5, "Cho giua hai lan thu khoa task."));
            ds.Add(Int(SettingKeys.TaskLeaseCandidateWindow, NhomGanNhan, 32, 1, 1000, "task",
                "Cap task NGAU NHIEN trong N task con cho cu nhat (1 = dung thu tu, task cu truoc). "
                + "Labeler bam cung luc it roi vao cung mot task; chi phi lay task tang theo N."));
            ds.Add(Seconds(SettingKeys.AnnotationAppealWindow, NhomGanNhan, 7 * Ngay, 0, 365 * Ngay,
                "Labeler duoc khieu nai trong bao lau ke tu khi nhan bi tu choi."));
            ds.Add(Int(SettingKeys.AnnotationReasonMaxLength, NhomGanNhan, 1000, 10, 1000, "ky tu",
                "Do dai ly do tu choi / khieu nai (cot DB toi da 1000)."));
            ds.Add(Int(SettingKeys.AnnotationBulkApproveMax, NhomGanNhan, 500, 1, 100000, "nhan",
                "So nhan toi da moi lan duyet hang loat."));

            // ================= CHAT LUONG =================
            ds.Add(Seconds(SettingKeys.QualityDsInterval, NhomChatLuong, 900, 10, 7 * Ngay, "Chu ky chay lo Dawid-Skene."));
            ds.Add(Int(SettingKeys.QualityDsMinLabels, NhomChatLuong, 5, 1, 10000, "nhan",
                "Labeler can toi thieu bao nhieu nhan trong du an thi moi cham do tin cay Dawid-Skene."));
            ds.Add(Double(SettingKeys.QualityReputationGoldWeight, NhomChatLuong, 0.6, 0, 1, "",
                "Trong so cau vang trong diem uy tin (phan con lai la dong thuan / Dawid-Skene)."));
            ds.Add(Double(SettingKeys.QualityReputationPrior, NhomChatLuong, 0.7, 0, 1, "",
                "Muc tien nghiem cua diem uy tin (nguoi moi bat dau quanh muc nay)."));
            ds.Add(Double(SettingKeys.QualityReputationPriorStrength, NhomChatLuong, 2, 0, 1000, "nhan",
                "Suc nang tien nghiem: so bang chung can de diem roi xa muc tien nghiem."));
            ds.Add(Choice(SettingKeys.QualityRedundancyPolicy, NhomChatLuong, "majority", ChinhSachRedundancy,
                "Khi nao xin them nguoi gan cho mot mau (NC-D-01): majority = tranh chap (khong lua chon nao qua ban) thi xin them; "
                + "posterior = dung khi xac suat hau nghiem cua dap an dan dau >= quality.posterior_target (co trong so theo do chinh xac labeler); "
                + "voi = dung toi uu mot buoc: chi mua them nhan khi gia tri thong tin ky vong vuot chi phi (quality.voi_value_ratio)."));
            ds.Add(Double(SettingKeys.QualityPosteriorTarget, NhomChatLuong, 0.95, 0.5, 0.999, "xac suat",
                "Chinh sach posterior: du tin cay de dung khi hau nghiem cua dap an dan dau dat muc nay."));
            ds.Add(Double(SettingKeys.QualityVoiValueRatio, NhomChatLuong, 20, 1, 10000, "lan",
                "Chinh sach voi: mot nhan CUOI dung gia tri bang bay nhieu lan chi phi mot nhan mua them. Cao = chiu mua them nhieu hon de tang do chinh xac."));
            ds.Add(Double(SettingKeys.LabelingThresholdBbox, NhomChatLuong, 0.5, 0, 1, "IoU",
                "Nguong cham cau vang mac dinh cho khung (cong cu khong tu dat matchThreshold)."));
            ds.Add(Double(SettingKeys.LabelingThresholdPolygon, NhomChatLuong, 0.5, 0, 1, "IoU", "Nguong cham mac dinh cho da giac."));
            ds.Add(Double(SettingKeys.LabelingThresholdSpan, NhomChatLuong, 1.0, 0, 1, "F1", "Nguong cham mac dinh cho doan van ban."));
            ds.Add(Double(SettingKeys.LabelingThresholdTranscription, NhomChatLuong, 0.1, 0, 1, "CER",
                "Ti le loi ky tu toi da cho ban chep (nho hon = khat khe hon)."));
            ds.Add(Double(SettingKeys.LabelingThresholdTemporalSegment, NhomChatLuong, 0.5, 0, 1, "IoU",
                "Nguong cham mac dinh cho doan thoi gian."));

            // ================= TAI KHOAN =================
            ds.Add(Seconds(SettingKeys.IdentityAccessTokenLifetime, NhomTaiKhoan, 15 * 60, 60, Ngay, "Access token song bao lau."));
            ds.Add(Seconds(SettingKeys.IdentityRefreshTokenLifetime, NhomTaiKhoan, 14 * Ngay, 3600, 365 * Ngay,
                "Refresh token song bao lau (dang nhap duoc giu bao lau)."));
            ds.Add(Int(SettingKeys.IdentityPasswordMinLength, NhomTaiKhoan, 8, 6, 128, "ky tu", "Do dai mat khau toi thieu."));
            ds.Add(Int(SettingKeys.IdentityPasswordMaxLength, NhomTaiKhoan, 128, 16, 1024, "ky tu", "Do dai mat khau toi da."));

            // ================= KY THUAT =================
            ds.Add(Seconds(SettingKeys.OutboxPollInterval, NhomKyThuat, 0.5, 0.05, 60, "Outbox nghi bao lau khi khong co event cho gui."));
            ds.Add(Int(SettingKeys.OutboxBatchSize, NhomKyThuat, 50, 1, 5000, "event", "So event moi lo outbox."));
            ds.Add(Seconds(SettingKeys.OutboxPublishTimeout, NhomKyThuat, 10, 1, 300, "Cho broker xac nhan mot event toi da bao lau."));
            ds.Add(Seconds(SettingKeys.OutboxRetryMaxDelay, NhomKyThuat, 300, 1, Ngay,
                "Gui event that bai thi thu lai sau 2, 4, 8... giay — khong lui qua muc nay."));
            ds.Add(Seconds(SettingKeys.ConsumersReconnectDelay, NhomKyThuat, 5, 1, 300, "Consumer chua noi duoc RabbitMQ thi thu lai sau bao lau."));
            ds.Add(new SettingDefinition(SettingKeys.ConsumersPrefetchCount, NhomKyThuat, SettingType.SoNguyen, JsonValue.Create(10), 1, 1000,
                "message", SettingEffect.Restart, "So message broker day toi moi consumer truoc khi ack. Co tac dung khi service khoi dong lai."));
            ds.Add(Seconds(SettingKeys.SettingsReloadInterval, NhomKyThuat, 5, 1, 3600,
                "Moi service doc lai ban sao setting tu DB cua no sau moi khoang nay."));
            ds.Add(Seconds(SettingKeys.SettingsSnapshotInterval, NhomKyThuat, 300, 10, Ngay,
                "admin-svc phat lai toan bo setting dinh ky (de service lo mat event van dong bo)."));

            // ================= CONG LINK (P2) =================
            ds.Add(Bool(SettingKeys.GateEnabled, NhomCongLink, true, "Tat = trang vuot link chi chuyen thang toi dich, khong phat cau hoi, khong tra tien sharer."));
            ds.Add(Seconds(SettingKeys.GateCountdown, NhomCongLink, 8, 0, 120, "Dem nguoc toi thieu truoc khi khach nop bai (server kiem, khong chi la giao dien)."));
            ds.Add(Int(SettingKeys.GateGoldPerSession, NhomCongLink, 1, 1, 5, "cau", "So cau vang (da biet dap an) moi luot vuot link."));
            ds.Add(Int(SettingKeys.GateRealPerSession, NhomCongLink, 2, 1, 10, "cau", "So cau that (can thu thap nhan) moi luot vuot link."));
            ds.Add(Seconds(SettingKeys.GateSessionTtl, NhomCongLink, 600, 60, 3600, "Bo cau hoi da phat song bao lau."));
            ds.Add(Seconds(SettingKeys.GateTokenTtl, NhomCongLink, 180, 30, 900, "Token mo link dich dung mot lan, song bao lau (VD-L-02)."));
            ds.Add(Seconds(SettingKeys.GateIpDedupeWindow, NhomCongLink, Ngay, 0, 30 * Ngay,
                "Moi IP chi tinh mot luot co tien cho moi link trong khoang nay (dac ta: 24 gio)."));
            ds.Add(Int(SettingKeys.GateMaxLabelsPerSample, NhomCongLink, 5, 1, 1000, "nhan", "Moi mau nhan toi da bay nhieu nhan tu cong link."));
            ds.Add(Seconds(SettingKeys.GateCacheRefreshInterval, NhomCongLink, 60, 5, 3600, "Gate nap lai bo nho dem du an / mau / link tu ban sao sau moi khoang nay."));
            ds.Add(Seconds(SettingKeys.GateEventFlushInterval, NhomKyThuat, 1, 0.2, 60, "Gate chuyen luot vuot tu Redis Stream sang outbox + ClickHouse sau moi khoang nay."));
            ds.Add(Int(SettingKeys.GateEventBatchSize, NhomKyThuat, 200, 1, 5000, "luot", "So luot vuot gate chuyen moi lo."));
            ds.Add(Int(SettingKeys.LinkCodeLength, NhomCongLink, 7, 5, 12, "ky tu", "Do dai ma link ngau nhien."));
            ds.Add(Int(SettingKeys.LinkBulkMax, NhomCongLink, 100, 1, 1000, "link", "Rut gon hang loat toi da bay nhieu link moi lan (FS-03)."));
            ds.Add(Int(SettingKeys.LinkReportReviewThreshold, NhomCongLink, 3, 1, 1000, "bao cao",
                "Link bi bao cao tu bay nhieu IP khac nhau thi vao hang doi kiem duyet."));
            ds.Add(Seconds(SettingKeys.LinkScanInterval, NhomKyThuat, 2, 1, 3600, "Worker quet link cho kiem duyet (Safe Browsing) sau moi khoang nay."));
            ds.Add(Int(SettingKeys.LinkReferralPercent, NhomCongLink, 10, 0, 50, "%", "Nguoi gioi thieu huong phan tram nay tren doanh thu cong link cua nguoi duoc moi (FS-08). Lay tu phan cua nen tang."));
            ds.Add(Long(SettingKeys.LinkReferralMinEarningsVnd, NhomCongLink, 50000, 0, 1e12, "dong",
                "Chi tra hoa hong sau khi nguoi duoc moi da tu kiem duoc tong bay nhieu (chong tai khoan clone tu moi nhau — VD-L-05)."));
            ds.Add(Seconds(SettingKeys.LinkReferralClaimWindow, NhomCongLink, 7 * Ngay, 0, 365 * Ngay,
                "Tai khoan moi tao trong khoang nay moi nhap duoc ma gioi thieu."));

            return ds;
        }

        private static SettingDefinition Bool(string key, string group, bool def, string description)
        {
            return new SettingDefinition(key, group, SettingType.DungSai, JsonValue.Create(def), null, null, "", SettingEffect.NewOperations, description);
        }

        private static SettingDefinition Int(string key, string group, int def, double min, double max, string unit, string description)
        {
            return new SettingDefinition(key, group, SettingType.SoNguyen, JsonValue.Create(def), min, max, unit, SettingEffect.NewOperations, description);
        }

        private static SettingDefinition Long(string key, string group, double def, double min, double max, string unit, string description)
        {
            return new SettingDefinition(key, group, SettingType.SoLon, JsonValue.Create((long)def), min, max, unit, SettingEffect.NewOperations, description);
        }

        private static SettingDefinition Double(string key, string group, double def, double min, double max, string unit, string description)
        {
            return new SettingDefinition(key, group, SettingType.SoThuc, JsonValue.Create(def), min, max, unit, SettingEffect.NewOperations, description);
        }

        private static SettingDefinition Seconds(string key, string group, double def, double min, double max, string description)
        {
            return new SettingDefinition(key, group, SettingType.ThoiGian, JsonValue.Create(def), min, max, "giay", SettingEffect.NewOperations, description);
        }

        private static SettingDefinition Choice(string key, string group, string def, string[] choices, string description)
        {
            return new SettingDefinition(key, group, SettingType.Chuoi, JsonValue.Create(def), null, 50, "", SettingEffect.NewOperations, description, choices);
        }

        private static SettingDefinition Text(string key, string group, string def, int maxLength, string description)
        {
            return new SettingDefinition(key, group, SettingType.Chuoi, JsonValue.Create(def), null, maxLength, "ky tu", SettingEffect.NewOperations, description);
        }
    }
}
