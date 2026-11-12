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
        public const string PaymentEnabledProviders = "payment.enabled_providers";
        public const string PaymentDepositLookupAfter = "payment.deposit_lookup_after";
        public const string PaymentDepositExpireAfter = "payment.deposit_expire_after";
        public const string PaymentDepositReconcileInterval = "payment.deposit_reconcile_interval";
        public const string PaymentDepositReconcileBatch = "payment.deposit_reconcile_batch";
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
        public const string InvoiceVatPercent = "invoice.vat_percent";
        public const string InvoiceSellerName = "invoice.seller_name";
        public const string InvoiceSellerTaxCode = "invoice.seller_tax_code";
        public const string InvoiceSellerAddress = "invoice.seller_address";
        public const string InvoiceSerialTemplate = "invoice.serial_template";
        public const string InvoiceAutoIssue = "invoice.auto_issue";
        public const string InvoiceAutoIssueInterval = "invoice.auto_issue_interval";
        public const string InvoiceAllowOpenPeriod = "invoice.allow_open_period";

        // ---- Du an ----
        public const string ProjectAutoApprove = "project.auto_approve";
        public const string ProjectApprovalTimeout = "project.approval_timeout";
        public const string ProjectApprovalScanInterval = "project.approval_scan_interval";
        public const string ProjectClosedDataRetention = "project.closed_data_retention";
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
        public const string ProjectMemberBatchMax = "project.member_batch_max";
        public const string ProjectInviteCodeMaxTtl = "project.invite_code_max_ttl";
        public const string ProjectJoinRequestCooldown = "project.join_request_cooldown";
        public const string ProjectJoinRequestTtl = "project.join_request_ttl";
        public const string ProjectInvitationTtl = "project.invitation_ttl";
        public const string ProjectEmailInviteDailyMax = "project.email_invite_daily_max";
        public const string ProjectHousekeepingInterval = "project.housekeeping_interval";

        // ---- Du an lam trung tam: cham ca bai khi du an ket thuc ----
        public const string ProjectPassThresholdMin = "project.pass_threshold_min";
        public const string ProjectPassThresholdMax = "project.pass_threshold_max";
        public const string ProjectMaxQuestions = "project.max_questions";
        public const string ProjectMinSlots = "project.min_slots";
        public const string ProjectMaxSlots = "project.max_slots";
        public const string ProjectParticipantMultiplier = "project.participant_multiplier";
        public const string ProjectMinRewardVnd = "project.min_reward_vnd";
        public const string TaskAttemptDuration = "task.attempt_duration";
        public const string TaskAttemptMaxBytes = "task.attempt_max_bytes";
        public const string TaskAppealWindow = "task.appeal_window";
        public const string LedgerRewardHold = "ledger.reward_hold";

        // ---- Du lieu va file ----
        public const string DatasetZipMaxBytes = "dataset.zip_max_bytes";
        public const string DatasetZipMaxEntries = "dataset.zip_max_entries";
        public const string DatasetZipImageMaxBytes = "dataset.zip_image_max_bytes";
        public const string DatasetZipTotalMaxBytes = "dataset.zip_total_max_bytes";
        public const string DatasetManifestInlineMaxRows = "dataset.manifest_inline_max_rows";
        public const string DatasetTextMaxChars = "dataset.text_max_chars";
        public const string DatasetImageMaxBytes = "dataset.image_max_bytes";
        public const string DatasetWorkerIdle = "dataset.worker_idle";
        public const string DatasetImportMaxFiles = "dataset.import_max_files";
        public const string DatasetImportFileMaxBytes = "dataset.import_file_max_bytes";
        public const string DatasetImportTimeout = "dataset.import_timeout";
        public const string DatasetErrorSummaryMaxLength = "dataset.error_summary_max_length";
        public const string DatasetErrorSampleCount = "dataset.error_sample_count";
        public const string SampleNameMaxLength = "sample.name_max_length";
        public const string UploadMaxFiles = "upload.max_files";
        public const string UploadMaxFileBytes = "upload.max_file_bytes";
        public const string UploadLinkTtl = "upload.link_ttl";
        public const string StorageViewLinkTtl = "storage.view_link_ttl";
        public const string MediaFfprobeTimeout = "media.ffprobe_timeout";

        // ---- Gan nhan, duyet ----
        public const string TaskReaperInterval = "task.reaper_interval";
        public const string TaskReportAutoExclude = "task.report_auto_exclude";
        public const string TaskReaperBatchSize = "task.reaper_batch_size";
        public const string AnnotationImbalanceThresholdPercent = "annotation.imbalance_threshold_percent";
        public const string AnnotationImbalanceMinLabels = "annotation.imbalance_min_labels";
        // annotation.history_retention / annotation.history_cleanup_interval: da bo (11/2026, kien truc 3.31) — lich su bai
        // dung lai tu attempts, khong con bang attempt_history de don.

        // ---- Chat luong ----
        public const string QualityFraudEnabled = "quality.fraud_enabled";
        public const string QualityFraudMinSeconds = "quality.fraud_min_seconds";
        public const string QualityFraudFastRatio = "quality.fraud_fast_ratio";
        public const string QualityFraudFastWindow = "quality.fraud_fast_window";
        public const string QualityFraudSameLabelStreak = "quality.fraud_same_label_streak";
        public const string QualityFraudSharedAccounts = "quality.fraud_shared_accounts";
        public const string QualityFraudSharedWindow = "quality.fraud_shared_window";
        public const string QualityFraudIgnorePrivateIps = "quality.fraud_ignore_private_ips";
        public const string QualityFraudReopenCooldown = "quality.fraud_reopen_cooldown";
        public const string QualityDsMinLabels = "quality.ds_min_labels";
        public const string QualityReputationGoldWeight = "quality.reputation_gold_weight";
        public const string QualityReputationPrior = "quality.reputation_prior";
        public const string QualityReputationPriorStrength = "quality.reputation_prior_strength";
        public const string LabelingThresholdBbox = "labeling.threshold.bbox";
        public const string LabelingThresholdPolygon = "labeling.threshold.polygon";
        public const string LabelingThresholdSpan = "labeling.threshold.span";
        public const string LabelingThresholdTranscription = "labeling.threshold.transcription";
        public const string LabelingThresholdTemporalSegment = "labeling.threshold.temporal_segment";
        public const string LabelingThresholdKeypoint = "labeling.threshold.keypoint";
        public const string LabelingThresholdPolyline = "labeling.threshold.polyline";
        public const string LabelingThresholdMask = "labeling.threshold.mask";
        public const string LabelingThresholdTrack = "labeling.threshold.track";
        public const string LabelingThresholdRelation = "labeling.threshold.relation";
        public const string LabelingThresholdRanking = "labeling.threshold.ranking";
        public const string QualityConsensusMinF1 = "quality.consensus_min_f1";
        public const string QualityFreeTextSimilarity = "quality.freetext_similarity";
        public const string QualityFreeTextLexicalSimilarity = "quality.freetext_lexical_similarity";
        public const string QualityFreeTextAnalysisTimeout = "quality.freetext_analysis_timeout";
        public const string QualityFreeTextAnalysisInterval = "quality.freetext_analysis_interval";
        public const string QualityPendingWorkInterval = "quality.pending_work_interval";

        // ---- Tai khoan ----
        public const string IdentityAccessTokenLifetime = "identity.access_token_lifetime";
        public const string IdentityRefreshTokenLifetime = "identity.refresh_token_lifetime";
        public const string IdentityPasswordMinLength = "identity.password_min_length";
        public const string IdentityPasswordMaxLength = "identity.password_max_length";
        public const string IdentityKybFileMaxBytes = "identity.kyb_file_max_bytes";
        public const string IdentityRefreshTokenCleanupInterval = "identity.refresh_token_cleanup_interval";
        public const string IdentityFrontendUrl = "identity.frontend_url";
        public const string IdentityEmailVerifyTtl = "identity.email_verify_ttl";
        public const string IdentityPasswordResetTtl = "identity.password_reset_ttl";
        public const string IdentityAvatarMaxBytes = "identity.avatar_max_bytes";
        public const string IdentityLevelRules = "identity.level_rules";
        public const string IdentityTwoFactorIssuer = "identity.two_factor_issuer";
        public const string IdentityTwoFactorChallengeTtl = "identity.two_factor_challenge_ttl";
        public const string IdentityTwoFactorMaxAttempts = "identity.two_factor_max_attempts";
        public const string IdentityAdultAge = "identity.adult_age";
        public const string IdentityLevelRecalcInterval = "identity.level_recalc_interval";
        public const string IdentityStatsCacheTtl = "identity.stats_cache_ttl";

        // ---- Admin: nhat ky audit (FP-06 / VD-S-10), thong ke (FM-09) ----
        public const string AdminAuditExcludedTypes = "admin.audit_excluded_types";
        public const string AdminAuditRetentionDays = "admin.audit_retention_days";


        // ---- Bao ve du lieu: media-svc (FP-02, FP-03) ----
        public const string MediaWatermarkEnabled = "media.watermark_enabled";
        public const string MediaWatermarkStrength = "media.watermark_strength";

        // ---- Gateway: gioi han tan suat (VD-S-12) ----
        public const string GatewayRateLimitPerMinute = "gateway.rate_limit_per_minute";
        public const string GatewayRateLimitAuthPerMinute = "gateway.rate_limit_auth_per_minute";
        public const string GatewayRateLimitMoneyPerMinute = "gateway.rate_limit_money_per_minute";

        // ---- Thong bao (notification-svc, FC-06) ----
        public const string NotificationEmailEnabled = "notification.email_enabled";
        public const string NotificationEmailFrom = "notification.email_from";
        public const string NotificationEmailInterval = "notification.email_interval";
        public const string NotificationEmailBatchSize = "notification.email_batch_size";
        public const string NotificationEmailMaxAttempts = "notification.email_max_attempts";
        public const string NotificationRetentionDays = "notification.retention_days";
        public const string NotificationCleanupInterval = "notification.cleanup_interval";
        public const string NotificationRealtimeTicketTtl = "notification.realtime_ticket_ttl";

        // ---- Cong link (P2) ----
        public const string GateEnabled = "gate.enabled";
        public const string GateAllowedTools = "gate.allowed_tools";
        public const string GateCountdown = "gate.countdown";
        public const string GateGoldPerSession = "gate.gold_per_session";
        public const string GateRealPerSession = "gate.real_per_session";
        public const string GateSessionTtl = "gate.session_ttl";
        public const string GateTokenTtl = "gate.token_ttl";
        public const string GateIpDedupeWindow = "gate.ip_dedupe_window";
        public const string GateMaxLabelsPerSample = "gate.max_labels_per_sample";
        public const string GateUnitPriceVnd = "gate.unit_price_vnd";
        public const string GateMaxFailsBeforeBlock = "gate.max_fails_before_block";
        public const string GateBlockDuration = "gate.block_duration";
        public const string GateCacheRefreshInterval = "gate.cache_refresh_interval";
        public const string GateEventFlushInterval = "gate.event_flush_interval";
        public const string GateEventBatchSize = "gate.event_batch_size";
        public const string GateContentLoadInterval = "gate.content_load_interval";
        public const string LinkCodeLength = "link.code_length";
        public const string LinkBulkMax = "link.bulk_max";
        public const string LinkReportReviewThreshold = "link.report_review_threshold";
        public const string LinkScanInterval = "link.scan_interval";
        public const string LinkReferralPercent = "link.referral_percent";
        public const string LinkReferralMinEarningsVnd = "link.referral_min_earnings_vnd";
        public const string LinkReferralClaimWindow = "link.referral_claim_window";
        public const string LinkReferralMaxPerIp = "link.referral_max_per_ip";
        public const string LinkReferralBurstPerHour = "link.referral_burst_per_hour";

        // ---- Ky thuat ----
        public const string OutboxPollInterval = "outbox.poll_interval";
        public const string OutboxBatchSize = "outbox.batch_size";
        public const string OutboxPublishTimeout = "outbox.publish_timeout";
        public const string OutboxRetryMaxDelay = "outbox.retry_max_delay";
        public const string OutboxRetention = "outbox.retention";
        public const string OutboxCleanupInterval = "outbox.cleanup_interval";
        public const string ConsumersProcessedRetention = "consumers.processed_retention";
        public const string ConsumersReconnectDelay = "consumers.reconnect_delay";
        public const string ConsumersPrefetchCount = "consumers.prefetch_count";
        public const string ConsumersRetryMaxDelay = "consumers.retry_max_delay";

        // ---- Loi he thong (loai 3, kien truc 3.29): theo doi DLQ, bao doi ky thuat ----
        public const string OpsDlqAlertThreshold = "ops.dlq_alert_threshold";
        public const string OpsDlqCheckInterval = "ops.dlq_check_interval";
        public const string OpsAlertRepeat = "ops.alert_repeat";
        public const string OpsAlertEmail = "ops.alert_email";
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
        private const string NhomTien = "Phí và tiền";
        private const string NhomDuyet = "Tự động duyệt";
        private const string NhomDuAn = "Dự án";
        private const string NhomDuLieu = "Dữ liệu và file";
        private const string NhomGanNhan = "Gán nhãn và duyệt nhãn";
        private const string NhomChatLuong = "Chất lượng";
        private const string NhomTaiKhoan = "Tài khoản";
        private const string NhomThongBao = "Thông báo";
        private const string NhomCongLink = "Cổng link";


        private static readonly string[] CheDoChiCongLink = new string[] { "perClick", "batched" };

        /// <summary>Cong nap tien co adapter (payment-svc PaymentProviders).</summary>
        private static readonly string[] CongNap = new string[] { "sandbox", "vnpay", "momo" };

        /// <summary>Moi loai cong cu gan nhan — giu khop Crowd.Labeling.ToolKinds.</summary>
        private static readonly string[] CongCuGanNhan = new string[]
        {
            "classification", "freeText", "number", "bbox", "polygon", "keypoint", "polyline", "mask",
            "span", "relation", "transcription", "temporalSegment", "track", "pairwise", "ranking",
        };
        private const string NhomKyThuat = "Kỹ thuật";
        private const string NhomBaoVeDuLieu = "Bảo vệ dữ liệu";

        /// <summary>
        /// Event tan suat cao (moi nhan / moi luot vuot link / moi lan dong bo mot event): khong luu chi tiet
        /// vao nhat ky audit. Nhat ky giu THAO TAC CUA NGUOI VA QUAN TRI (dang ky, KYB, khoa, du an, tien nap /
        /// rut, tranh chap, setting...). Lich su tung nhan da co o annotation_history, tien o so cai.
        /// </summary>
        public const string AuditLoaiTruMacDinh =
            "gate.solved,click.validated,settings.snapshot,settings.snapshot_requested,samples.ingested,"
            + "attempt.submitted,reputation.changed,funds.held,hold.expired,gate.budget_changed";

        /// <summary>Mac dinh tu 2026-10-13 toi truoc du an lam trung tam (3.18) — migration admin nang len mac dinh moi neu admin chua sua.</summary>
        public const string AuditLoaiTruMacDinh1013 =
            "gate.solved,click.validated,settings.snapshot,settings.snapshot_requested,dataset.ingested,"
            + "assignment.submitted,annotation.submitted,annotation.approved,annotation.rejected,"
            + "task.redundancy_reached,task.redundancy_changed,gold.answered,consensus.reached,"
            + "redundancy.increase_requested,reputation.changed,funds.held,hold.expired,gate.budget_changed";

        /// <summary>Mac dinh truoc 2026-10-13 — migration admin nang gia tri nay len mac dinh moi neu admin chua sua.</summary>
        public const string AuditLoaiTruMacDinhCu = "gate.solved,click.validated,settings.snapshot";

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
                "Phí nền tảng cộng thêm trên tiền thưởng mỗi bài đạt. Chốt vào dự án lúc publish — dự án đang chạy giữ mức cũ."));
            ds.Add(Long(SettingKeys.PaymentDepositMinVnd, NhomTien, 10000, 1000, 1e12, "đồng",
                "Số tiền nạp tối thiểu một lần."));
            ds.Add(Long(SettingKeys.PaymentDepositMaxVnd, NhomTien, 500000000, 1000, 1e13, "đồng",
                "Số tiền nạp tối đa một lần."));
            ds.Add(Bool(SettingKeys.PaymentManualTransferEnabled, NhomTien, true,
                "Cho phép nạp bằng chuyển khoản thủ công (doanh nghiệp báo đã chuyển, admin xác nhận)."));
            ds.Add(Text(SettingKeys.PaymentManualTransferBankInfo, NhomTien,
                "Ngan hang DATN Bank - STK 0123456789 - CONG TY DATN", 500,
                "Thông tin tài khoản nhận tiền hiện cho doanh nghiệp khi nạp chuyển khoản."));
            ds.Add(Seconds(SettingKeys.PaymentPayoutInterval, NhomKyThuat, 5, 1, 3600,
                "Chu kỳ worker gửi lệnh chi tiền ra cổng thanh toán."));
            ds.Add(Seconds(SettingKeys.PaymentPayoutLookupAfter, NhomKyThuat, 15 * 60, 60, Ngay,
                "Lệnh chi treo quá lâu thì hỏi ngược cổng về trạng thái (đối soát)."));
            ds.Add(Many(SettingKeys.PaymentEnabledProviders, NhomTien, "sandbox,vnpay,momo", 100, CongNap,
                "Cổng nạp tiền đang mở. Cổng phải được đội kỹ thuật kết nối trước mới dùng được; tắt một cổng khi cổng đó gặp sự cố."));
            ds.Add(Seconds(SettingKeys.PaymentDepositLookupAfter, NhomKyThuat, 15 * 60, 60, Ngay,
                "Lệnh nạp qua cổng chưa có kết quả sau bấy nhiêu thì hỏi ngược cổng (đối soát, VD-M-09) — phòng khi IPN bị lạc."));
            ds.Add(Seconds(SettingKeys.PaymentDepositExpireAfter, NhomTien, Ngay, 15 * 60, 30 * Ngay,
                "Lệnh nạp qua cổng quá thời gian này mà cổng vẫn chưa báo đã thu tiền thì đánh dấu hết hạn (thất bại)."));
            ds.Add(Seconds(SettingKeys.PaymentDepositReconcileInterval, NhomKyThuat, 60, 5, 3600,
                "Chu kỳ worker đối soát lệnh nạp treo."));
            ds.Add(Int(SettingKeys.PaymentDepositReconcileBatch, NhomKyThuat, 50, 1, 1000, "lệnh",
                "Số lệnh nạp treo hỏi ngược cổng mỗi lần đối soát."));
            ds.Add(Seconds(SettingKeys.LedgerRewardHold, NhomTien, 0, 0, 90 * Ngay,
                "Tiền thưởng dự án của labeler đạt treo bao lâu trước khi rút được. 0 = khả dụng ngay khi dự án chấm xong."));
            ds.Add(Seconds(SettingKeys.LedgerHoldDuration, NhomTien, 3 * Ngay, 0, 90 * Ngay,
                "Tiền công vừa duyệt bị TREO bao lâu trước khi labeler rút được (để xử lý khiếu nại, gian lận)."));
            ds.Add(Seconds(SettingKeys.LedgerHoldReleaseInterval, NhomKyThuat, 60, 5, 3600,
                "Chu kỳ worker quét khoản treo đến hạn."));
            ds.Add(Int(SettingKeys.LedgerHoldReleaseBatchSize, NhomKyThuat, 100, 1, 10000, "khoản",
                "Số khoản treo đến hạn giải phóng mỗi lô."));
            ds.Add(Choice(SettingKeys.LedgerGatePayoutMode, NhomKyThuat, "perClick", CheDoChiCongLink,
                "Chi tiền lượt vượt link: perClick = mỗi lượt một bút toán ngay (tiền vào ví sau vài giây, nghẽn khi tải cao); batched = gom theo dự án mỗi ledger.gate_batch_interval rồi viết một bút toán (VD-M-08)."));
            ds.Add(Seconds(SettingKeys.LedgerGateBatchInterval, NhomKyThuat, 60, 1, 3600,
                "Chế độ batched: gom lượt vượt link bao lâu một lần."));
            ds.Add(Int(SettingKeys.LedgerGateBatchSize, NhomKyThuat, 5000, 1, 100000, "lượt",
                "Chế độ batched: tối đa bấy nhiêu lượt mỗi lô."));
            ds.Add(Long(SettingKeys.LedgerWithdrawMinVnd, NhomTien, 50000, 0, 1e12, "đồng",
                "Số tiền rút tối thiểu một lần."));
            ds.Add(Long(SettingKeys.LedgerWithdrawTaxThresholdVnd, NhomTien, 2000000, 0, 1e13, "đồng",
                "Rút từ mức này trở lên thì khấu trừ thuế TNCN."));
            ds.Add(Int(SettingKeys.LedgerWithdrawTaxPercent, NhomTien, 10, 0, 50, "%",
                "Thuế suất khấu trừ khi rút tiền."));
            ds.Add(Int(SettingKeys.InvoiceVatPercent, NhomTien, 10, 0, 20, "%",
                "Thuế suất GTGT trên phí nền tảng. Phí đã thu là giá ĐÃ GỒM thuế; hoá đơn tách ngược ra phần chưa thuế + thuế. Áp cho hoá đơn xuất sau khi đổi."));
            ds.Add(Text(SettingKeys.InvoiceSellerName, NhomTien, "CONG TY TNHH NEN TANG GAN NHAN DATN", 200,
                "Tên đơn vị bán hàng in trên hoá đơn."));
            ds.Add(Text(SettingKeys.InvoiceSellerTaxCode, NhomTien, "0109999999", 20,
                "Mã số thuế đơn vị bán hàng."));
            ds.Add(Text(SettingKeys.InvoiceSellerAddress, NhomTien, "So 1 Dai Co Viet, Hai Ba Trung, Ha Noi", 300,
                "Địa chỉ đơn vị bán hàng."));
            ds.Add(Text(SettingKeys.InvoiceSerialTemplate, NhomTien, "1C{yy}TDN", 20,
                "Ký hiệu hoá đơn; {yy} = 2 số cuối năm. 1 = hoá đơn GTGT, C = có mã của cơ quan thuế, T = điện tử, DN = mã riêng."));
            ds.Add(Bool(SettingKeys.InvoiceAutoIssue, NhomTien, true,
                "Bật: đầu mỗi tháng tự xuất hoá đơn tháng trước cho mọi doanh nghiệp có phát sinh phí nền tảng (đã xác minh doanh nghiệp)."));
            ds.Add(Seconds(SettingKeys.InvoiceAutoIssueInterval, NhomKyThuat, 3600, 60, Ngay,
                "Chu kỳ worker kiểm tra và tự xuất hoá đơn tháng trước."));
            ds.Add(Bool(SettingKeys.InvoiceAllowOpenPeriod, NhomKyThuat, false,
                "CHỈ ĐỂ KIỂM THỬ: cho xuất hoá đơn cho tháng CHƯA kết thúc. Bật trên môi trường thật sẽ chiếm mất kỳ của tháng (mỗi tháng một hoá đơn)."));

            // ================= TU DONG DUYET =================
            ds.Add(Bool(SettingKeys.ProjectAutoApprove, NhomDuyet, false,
                "Bật: dự án ký quỹ xong chạy ngay, không chờ admin duyệt."));
            ds.Add(Bool(SettingKeys.LedgerWithdrawAutoApprove, NhomDuyet, true,
                "Bật: lệnh rút không vượt ngưỡng bên dưới được chuyển tiền ngay. Tắt: mọi lệnh rút chờ admin duyệt."));
            ds.Add(Long(SettingKeys.LedgerWithdrawAutoApproveMaxVnd, NhomDuyet, 2000000, 0, 1e13, "đồng",
                "Ngưỡng tự duyệt rút tiền: lệnh lớn hơn số này luôn chờ admin duyệt."));
            ds.Add(Long(SettingKeys.PaymentManualTransferAutoApproveMaxVnd, NhomDuyet, 0, 0, 1e12, "đồng",
                "Lệnh nạp chuyển khoản thủ công KHÔNG vượt số này được tự xác nhận khi doanh nghiệp báo đã chuyển. 0 = luôn chờ admin. CẢNH BÁO: tự xác nhận là cộng tiền mà chưa ai đối chiếu sao kê."));

            // ================= DU AN =================
            ds.Add(Seconds(SettingKeys.ProjectApprovalTimeout, NhomDuAn, 72 * 3600, 3600, 30 * Ngay,
                "Dự án chờ admin duyệt quá lâu thì tự huỷ và hoàn ký quỹ."));
            ds.Add(Seconds(SettingKeys.ProjectApprovalScanInterval, NhomKyThuat, 10 * 60, 10, Ngay,
                "Chu kỳ quét dự án chờ duyệt quá hạn."));
            ds.Add(Seconds(SettingKeys.ProjectJoinRequestTtl, NhomDuAn, 14 * Ngay, Ngay, 180 * Ngay,
                "Đơn xin tham gia chủ dự án không duyệt quá thời gian này thì tự hết hạn (labeler gửi lại được ngay)."));
            ds.Add(Seconds(SettingKeys.ProjectInvitationTtl, NhomDuAn, 14 * Ngay, Ngay, 180 * Ngay,
                "Lời mời chờ (email chưa có tài khoản, hoặc dự án cần labeler tự đồng ý điều khoản) sống bao lâu."));
            ds.Add(Int(SettingKeys.ProjectEmailInviteDailyMax, NhomDuAn, 200, 1, 100000, "lời mời",
                "Số email tối đa một người được mời vào dự án trong 24 giờ — chặn dùng ô mời để dò danh sách email. Vượt thì phải chờ hôm sau."));
            ds.Add(Seconds(SettingKeys.ProjectHousekeepingInterval, NhomKyThuat, 10 * 60, 10, Ngay,
                "Chu kỳ worker dọn đơn xin tham gia / lời mời quá hạn."));
            ds.Add(Int(SettingKeys.ProjectPassThresholdMin, NhomDuAn, 60, 1, 100, "%",
                "x — NGƯỠNG DƯỚI doanh nghiệp được chọn cho tỉ lệ câu giống đa số (t). Labeler chỉ được thưởng khi tỉ lệ câu trả lời giống đa số ≥ t và không sai câu vàng nào."));
            ds.Add(Int(SettingKeys.ProjectPassThresholdMax, NhomDuAn, 95, 1, 100, "%",
                "y — NGƯỠNG TRÊN doanh nghiệp được chọn cho t (x ≤ t ≤ y)."));
            ds.Add(Int(SettingKeys.ProjectMaxQuestions, NhomDuAn, 500, 1, 10000, "câu",
                "Số câu (mẫu dữ liệu, không tính câu vàng) tối đa của một dự án — mỗi labeler làm HẾT các câu trong một bài. Dữ liệu lớn hơn thì tách thành nhiều dự án."));
            ds.Add(Int(SettingKeys.ProjectMinSlots, NhomDuAn, 3, 1, 100, "người",
                "Số suất thưởng (số người được thưởng) tối thiểu doanh nghiệp được đặt cho một dự án. Cần ≥ 3 để “đa số” có nghĩa."));
            ds.Add(Int(SettingKeys.ProjectMaxSlots, NhomDuAn, 50, 1, 1000, "người",
                "Số suất thưởng tối đa doanh nghiệp được đặt cho một dự án."));
            ds.Add(Int(SettingKeys.ProjectParticipantMultiplier, NhomDuAn, 2, 1, 20, "lần",
                "Trần số người làm bài = số suất thưởng × hệ số này. Cho nhiều người làm hơn số suất để “đa số” chắc hơn; khi chấm, n suất thưởng trao cho n bài ĐẠT nộp sớm nhất, bài đạt sau đó không có tiền. Đủ trần thì dự án đóng thu bài. Chụp vào dự án lúc publish."));
            ds.Add(Long(SettingKeys.ProjectMinRewardVnd, NhomDuAn, 10000, 0, 1e10, "đồng",
                "Tiền thưởng tối thiểu cho MỘT người làm đạt cả dự án. 0 = không giới hạn."));
            ds.Add(Seconds(SettingKeys.ProjectClosedDataRetention, NhomDuAn, 90 * Ngay, 0, 3650 * Ngay,
                "Dự án đã đóng (hoàn thành / huỷ) bao lâu thì xoá dữ liệu VẬN HÀNH của nó: bài làm dở còn sót, báo lỗi mẫu, nhãn thô và các vòng tính kết quả chung. Trước khi xoá, tiến độ và báo cáo chất lượng được chụp lại nên vẫn xem được. Nhãn đã nộp / đã duyệt, kết quả, sổ cái, mẫu dữ liệu và uy tín labeler KHÔNG bị xoá. 0 = không bao giờ xoá."));
            ds.Add(Int(SettingKeys.ProjectEntranceQuestionDefault, NhomDuAn, 10, 1, 500, "câu",
                "Số câu bài test đầu vào mặc định của dự án mới tạo."));
            ds.Add(Int(SettingKeys.ProjectEntrancePassPercentDefault, NhomDuAn, 80, 1, 100, "%",
                "Ngưỡng đậu bài test đầu vào mặc định của dự án mới tạo."));
            ds.Add(Int(SettingKeys.ProjectEntranceQuestionMax, NhomDuAn, 50, 1, 500, "câu",
                "Số câu bài test đầu vào tối đa."));
            ds.Add(Int(SettingKeys.ProjectGoldItemsMax, NhomDuAn, 2000, 1, 100000, "câu",
                "Số câu hỏi vàng tối đa mỗi dự án."));
            ds.Add(Int(SettingKeys.ProjectNameMaxLength, NhomDuAn, 200, 10, 200, "ký tự",
                "Độ dài tên dự án (tối đa 200)."));
            ds.Add(Int(SettingKeys.ProjectDescriptionMaxLength, NhomDuAn, 5000, 100, 5000, "ký tự",
                "Độ dài mô tả dự án (tối đa 5000)."));
            ds.Add(Int(SettingKeys.GuidelineMarkdownMaxLength, NhomDuAn, 20000, 100, 200000, "ký tự",
                "Độ dài tối đa phần hướng dẫn làm bài."));
            ds.Add(Int(SettingKeys.GuidelineExamplesMax, NhomDuAn, 50, 0, 500, "ví dụ",
                "Số ví dụ tối đa trong hướng dẫn."));
            ds.Add(Int(SettingKeys.GuidelineExplanationMaxLength, NhomDuAn, 1000, 10, 10000, "ký tự",
                "Độ dài giải thích mỗi ví dụ."));
            ds.Add(Int(SettingKeys.EntranceMaxAttempts, NhomDuAn, 3, 1, 100, "lần",
                "Số lần làm bài test đầu vào tối đa mỗi dự án."));
            ds.Add(Seconds(SettingKeys.EntranceDuration, NhomDuAn, 30 * 60, 60, Ngay,
                "Thời gian làm một bài test đầu vào."));
            ds.Add(Int(SettingKeys.ProjectMemberBatchMax, NhomDuAn, 100, 1, 1000, "người",
                "Số người tối đa trong một lần chủ dự án mời nhiều người (theo email / username)."));
            ds.Add(Seconds(SettingKeys.ProjectInviteCodeMaxTtl, NhomDuAn, 30 * Ngay, 3600, 365 * Ngay,
                "Hạn dùng tối đa của một mã mời tham gia dự án (chủ dự án không đặt hạn thì lấy 7 ngày, không vượt mức này)."));
            ds.Add(Seconds(SettingKeys.ProjectJoinRequestCooldown, NhomDuAn, Ngay, 0, 30 * Ngay,
                "Đơn xin tham gia bị từ chối: labeler phải chờ bao lâu mới được gửi đơn mới vào cùng dự án. 0 = gửi lại ngay."));

            // ================= DU LIEU VA FILE =================
            ds.Add(Long(SettingKeys.DatasetZipMaxBytes, NhomDuLieu, 200 * MB, MB, 2048 * MB, "byte",
                "Dung lượng tối đa một file ZIP ảnh doanh nghiệp tải lên."));
            ds.Add(Int(SettingKeys.DatasetZipMaxEntries, NhomDuLieu, 10000, 1, 1000000, "file",
                "Số file tối đa trong một ZIP."));
            ds.Add(Long(SettingKeys.DatasetZipImageMaxBytes, NhomDuLieu, 20 * MB, 1024, 1024 * MB, "byte",
                "Kích thước tối đa mỗi ảnh trong ZIP (sau giải nén)."));
            ds.Add(Long(SettingKeys.DatasetZipTotalMaxBytes, NhomDuLieu, 2048 * MB, MB, 100L * 1024 * MB, "byte",
                "Tổng dung lượng tối đa của một ZIP sau khi giải nén (chặn file nén độc hại)."));
            ds.Add(Int(SettingKeys.DatasetManifestInlineMaxRows, NhomDuLieu, 1000, 1, 100000, "dòng",
                "Số mẫu (văn bản, cặp, danh sách...) tối đa mỗi lần thêm. Nhiều hơn thì chia thành nhiều lần."));
            ds.Add(Int(SettingKeys.DatasetTextMaxChars, NhomDuLieu, 100000, 100, 10000000, "ký tự",
                "Độ dài tối đa một mẫu văn bản."));
            ds.Add(Long(SettingKeys.DatasetImageMaxBytes, NhomDuLieu, 50 * MB, 1024, 2048 * MB, "byte",
                "Kích thước tối đa một ảnh upload thẳng hoặc nhập từ nguồn ngoài."));
            ds.Add(Int(SettingKeys.DatasetImportMaxFiles, NhomDuLieu, 500, 1, 100000, "file",
                "Nhập từ nguồn ngoài (S3 / Google Drive / danh sách đường link): số file tối đa mỗi lần nhập."));
            ds.Add(Long(SettingKeys.DatasetImportFileMaxBytes, NhomDuLieu, 500 * MB, MB, 10240 * MB, "byte",
                "Nhập từ nguồn ngoài: dung lượng tối đa mỗi file âm thanh / video (ảnh theo mục “Dung lượng mỗi ảnh”)."));
            ds.Add(Seconds(SettingKeys.DatasetImportTimeout, NhomKyThuat, 120, 5, 3600,
                "Nhập từ nguồn ngoài: thời gian tối đa tải MỘT file."));
            ds.Add(Seconds(SettingKeys.DatasetWorkerIdle, NhomKyThuat, 2, 0.1, 600,
                "Worker xử lý mẫu (tải / đọc file) nghỉ bao lâu khi hết việc."));
            ds.Add(Int(SettingKeys.DatasetErrorSummaryMaxLength, NhomKyThuat, 4000, 100, 4000, "ký tự",
                "Độ dài lý do lỗi của một mẫu nạp không được (cột DB tối đa 4000)."));
            ds.Add(Int(SettingKeys.DatasetErrorSampleCount, NhomKyThuat, 20, 1, 1000, "dòng",
                "Số dòng lỗi đầu tiên trả về khi gửi một lần dữ liệu."));
            ds.Add(Int(SettingKeys.SampleNameMaxLength, NhomKyThuat, 500, 10, 500, "ký tự",
                "Độ dài tên mẫu (cột DB tối đa 500)."));
            ds.Add(Int(SettingKeys.UploadMaxFiles, NhomDuLieu, 100, 1, 10000, "file",
                "Số file tối đa mỗi lần tải lên."));
            ds.Add(Long(SettingKeys.UploadMaxFileBytes, NhomDuLieu, 5L * 1024 * MB, MB, 5L * 1024 * MB, "byte",
                "Dung lượng tối đa mỗi file tải lên (không quá 5 GB)."));
            ds.Add(Seconds(SettingKeys.UploadLinkTtl, NhomKyThuat, 3600, 60, 7 * Ngay,
                "Link upload ký sẵn sống bao lâu."));
            ds.Add(Seconds(SettingKeys.StorageViewLinkTtl, NhomKyThuat, 300, 10, 7 * Ngay,
                "Link xem file ký sẵn (ảnh, âm thanh, video) sống bao lâu."));
            ds.Add(Seconds(SettingKeys.MediaFfprobeTimeout, NhomKyThuat, 60, 1, 3600,
                "Thời gian tối đa cho một lần ffprobe đo file."));

            // ================= GAN NHAN VA DUYET =================
            ds.Add(Seconds(SettingKeys.TaskAttemptDuration, NhomGanNhan, 48 * 3600, 600, 30 * Ngay,
                "Labeler giữ một lượt làm bài bao lâu kể từ lúc bắt đầu (không quá hạn chót dự án). Hết hạn chưa nộp thì mất lượt, người khác vào làm."));
            ds.Add(Long(SettingKeys.TaskAttemptMaxBytes, NhomKyThuat, 8 * 1024 * 1024, 64 * 1024, 64 * 1024 * 1024, "byte",
                "Kích thước tối đa toàn bộ câu trả lời của một bài làm (mỗi câu vẫn giới hạn 256 KB)."));
            ds.Add(Seconds(SettingKeys.TaskAppealWindow, NhomGanNhan, 3 * Ngay, 0, 90 * Ngay,
                "Sau khi chấm, labeler TRƯỢT được khiếu nại kết quả cả bài trong bao lâu. Ký quỹ còn lại chỉ hoàn doanh nghiệp khi hết hạn này và không còn khiếu nại mở."));
            ds.Add(Seconds(SettingKeys.TaskReaperInterval, NhomKyThuat, 30, 1, 3600,
                "Chu kỳ quét lượt giữ quá hạn."));
            ds.Add(Int(SettingKeys.TaskReportAutoExclude, NhomDuAn, 3, 1, 100, "người",
                "Bao nhiêu labeler khác nhau báo lỗi cùng một mẫu thì tự loại mẫu đó khỏi dự án."));
            ds.Add(Int(SettingKeys.TaskReaperBatchSize, NhomKyThuat, 200, 1, 10000, "lượt giữ",
                "Số lượt giữ quá hạn reaper khoá và trả về pool mỗi lô."));
            ds.Add(Int(SettingKeys.AnnotationImbalanceThresholdPercent, NhomGanNhan, 80, 50, 99, "%",
                "Cảnh báo lệch phân bố khi một lớp chiếm trên bấy nhiêu phần trăm số nhãn của công cụ."));
            ds.Add(Int(SettingKeys.AnnotationImbalanceMinLabels, NhomGanNhan, 20, 1, 100000, "nhãn",
                "Công cụ cần ít nhất bấy nhiêu nhãn mới bắt đầu cảnh báo lệch phân bố."));

            // ================= CHAT LUONG =================
            // Dawid-Skene khong co chu ky rieng: chay mot lan sau khi cham, trong worker viec cho
            // (quality.pending_work_interval).

            // Chong gian lan (FM-07) — quality-svc mo ho so nghi van, admin quyet dinh.
            ds.Add(Bool(SettingKeys.QualityFraudEnabled, NhomChatLuong, true,
                "Bật phát hiện gian lận (tắt thì không mở hồ sơ mới)."));
            ds.Add(Double(SettingKeys.QualityFraudMinSeconds, NhomChatLuong, 2, 0, 600, "giây",
                "Trung bình mỗi câu dưới bấy nhiêu giây luôn bị coi là làm quá nhanh."));
            ds.Add(Double(SettingKeys.QualityFraudFastRatio, NhomChatLuong, 0.25, 0, 1, "lần",
                "Hoặc nhanh hơn tỉ lệ này nhân với số giây mỗi câu trung vị của các bài khác trong dự án."));
            ds.Add(Int(SettingKeys.QualityFraudFastWindow, NhomChatLuong, 20, 3, 1000, "câu",
                "Bài nộp có ít nhất bấy nhiêu câu đã trả lời mới xét làm quá nhanh (ít câu quá thì số giây không đáng tin)."));
            ds.Add(Int(SettingKeys.QualityFraudSameLabelStreak, NhomChatLuong, 30, 3, 10000, "câu",
                "Trong một bài, chọn cùng một lớp bấy nhiêu câu liên tiếp (trong khi người khác chọn đa dạng) thì mở hồ sơ."));
            ds.Add(Int(SettingKeys.QualityFraudSharedAccounts, NhomChatLuong, 3, 2, 1000, "tài khoản",
                "Bấy nhiêu tài khoản khác nhau nộp từ cùng IP / thiết bị trong khoảng thời gian dưới thì mở hồ sơ."));
            ds.Add(Seconds(SettingKeys.QualityFraudSharedWindow, NhomChatLuong, Ngay, 60, 30 * Ngay,
                "Khoảng thời gian xét chung IP / thiết bị."));
            ds.Add(Bool(SettingKeys.QualityFraudIgnorePrivateIps, NhomKyThuat, true,
                "Bỏ qua IP nội bộ / loopback khi xét chung IP (môi trường dev, mạng công ty dùng chung NAT)."));
            ds.Add(Seconds(SettingKeys.QualityFraudReopenCooldown, NhomChatLuong, 7 * Ngay, 0, 365 * Ngay,
                "Hồ sơ đã xử lý (bỏ qua / xác nhận) thì không mở lại cùng khoá trong khoảng này."));
            ds.Add(Int(SettingKeys.QualityDsMinLabels, NhomChatLuong, 5, 1, 10000, "nhãn",
                "Người làm cần tối thiểu bấy nhiêu nhãn trong dự án thì mới được chấm độ tin cậy."));
            ds.Add(Double(SettingKeys.QualityReputationGoldWeight, NhomChatLuong, 0.6, 0, 1, "",
                "Câu vàng chiếm bao nhiêu phần trong điểm uy tín (0–1); phần còn lại tính theo mức trả lời giống đa số."));
            ds.Add(Double(SettingKeys.QualityReputationPrior, NhomChatLuong, 0.7, 0, 1, "",
                "Điểm uy tín (0–1) của người mới; điểm dần thay đổi theo kết quả làm bài."));
            ds.Add(Double(SettingKeys.QualityReputationPriorStrength, NhomChatLuong, 2, 0, 1000, "nhãn",
                "Cần khoảng bấy nhiêu nhãn thì điểm uy tín của người mới mới rời xa điểm khởi đầu. Lớn = điểm thay đổi chậm hơn."));
            ds.Add(Double(SettingKeys.LabelingThresholdBbox, NhomChatLuong, 0.5, 0, 1, "độ chồng (0–1)",
                "Hai khung chữ nhật coi là khớp khi phần chồng lên nhau đạt mức này (1 = trùng hoàn toàn). Áp khi dự án không tự đặt ngưỡng."));
            ds.Add(Double(SettingKeys.LabelingThresholdPolygon, NhomChatLuong, 0.5, 0, 1, "độ chồng (0–1)",
                "Hai đa giác coi là khớp khi phần chồng lên nhau đạt mức này."));
            ds.Add(Double(SettingKeys.LabelingThresholdSpan, NhomChatLuong, 1.0, 0, 1, "độ khớp (0–1)",
                "Các đoạn văn bản được đánh dấu coi là khớp khi trùng nhau đạt mức này (1 = phải trùng hoàn toàn)."));
            ds.Add(Double(SettingKeys.LabelingThresholdTranscription, NhomChatLuong, 0.1, 0, 1, "tỉ lệ lỗi (0–1)",
                "Bản chép lời coi là khớp khi số ký tự sai không quá tỉ lệ này (0,1 = sai tối đa 10%; nhỏ hơn = khắt khe hơn)."));
            ds.Add(Double(SettingKeys.LabelingThresholdTemporalSegment, NhomChatLuong, 0.5, 0, 1, "độ chồng (0–1)",
                "Hai đoạn thời gian (âm thanh / video) coi là khớp khi phần trùng nhau đạt mức này."));
            ds.Add(Double(SettingKeys.LabelingThresholdKeypoint, NhomChatLuong, 0.98, 0, 1, "độ giống",
                "Hai điểm mốc coi là khớp khi lệch nhau không quá (1 − mức này) đường chéo ảnh. 0,98 = lệch tối đa 2% đường chéo."));
            ds.Add(Double(SettingKeys.LabelingThresholdPolyline, NhomChatLuong, 0.97, 0, 1, "độ giống",
                "Hai đường gấp khúc coi là khớp khi lệch trung bình không quá (1 − mức này) đường chéo ảnh."));
            ds.Add(Double(SettingKeys.LabelingThresholdMask, NhomChatLuong, 0.5, 0, 1, "độ chồng (0–1)",
                "Hai vùng tô (mặt nạ) coi là khớp khi phần chồng lên nhau đạt mức này."));
            ds.Add(Double(SettingKeys.LabelingThresholdTrack, NhomChatLuong, 0.5, 0, 1, "độ chồng (0–1)",
                "Hai chuỗi khung theo dõi vật thể trong video coi là khớp khi chồng lên nhau (theo cả vị trí và thời gian) đạt mức này."));
            ds.Add(Double(SettingKeys.LabelingThresholdRelation, NhomChatLuong, 1.0, 0, 1, "độ khớp (0–1)",
                "Các quan hệ giữa đoạn văn bản coi là khớp khi trùng nhau đạt mức này."));
            ds.Add(Double(SettingKeys.LabelingThresholdRanking, NhomChatLuong, 0.8, 0, 1, "tỉ lệ (0–1)",
                "Hai bảng xếp hạng coi là khớp khi tỉ lệ cặp mục được xếp cùng thứ tự đạt mức này."));
            ds.Add(Double(SettingKeys.QualityConsensusMinF1, NhomChatLuong, 0.8, 0, 1, "độ khớp (0–1)",
                "Công cụ có nhiều đối tượng mỗi mẫu (khung, đa giác, đoạn...): một người tính là KHỚP kết quả chung khi các đối tượng của họ trùng với kết quả chung ở mức này. Không quá một nửa số người khớp thì mẫu là tranh chấp."));
            ds.Add(Double(SettingKeys.QualityFreeTextSimilarity, NhomChatLuong, 0.6, 0.3, 0.99, "độ giống (0–1)",
                "Câu trả lời mở được chấm THEO NGHĨA: một người tính là giống đa số khi câu của họ giống nghĩa với câu tiêu biểu nhất của nhóm đạt mức này. Câu cùng nghĩa khác chữ thường 0,63 – 0,9; câu lạc đề gần 0."));
            ds.Add(Double(SettingKeys.QualityFreeTextLexicalSimilarity, NhomChatLuong, 0.38, 0.1, 0.99, "độ giống (0–1)",
                "Ngưỡng khi PHẢI chấm câu trả lời mở bằng so CHỮ vì hệ thống phân tích nghĩa không kịp. So chữ yếu hơn so nghĩa: câu cùng nghĩa khác chữ chỉ 0,4 – 0,5."));
            ds.Add(Seconds(SettingKeys.QualityFreeTextAnalysisTimeout, NhomChatLuong, 3600, 10, 7 * Ngay,
                "Sau khi dự án đóng thu bài, chờ phân tích nghĩa các câu trả lời mở tối đa bao lâu; quá hạn thì chấm bằng so chữ để người làm không phải chờ tiền."));
            ds.Add(Seconds(SettingKeys.QualityFreeTextAnalysisInterval, NhomKyThuat, 5, 1, 3600,
                "Chu kỳ quality-svc gửi câu trả lời mở mới sang ml-svc để nhúng."));
            ds.Add(Seconds(SettingKeys.QualityPendingWorkInterval, NhomKyThuat, 5, 1, 3600,
                "Chu kỳ quality-svc xét gian lận bài vừa nộp, chấm lại dự án chưa chấm xong (project-svc tạm không trả lời qua gRPC, hoặc đang chờ phân tích câu trả lời mở) và tính lại uy tín labeler sau khi project-svc ghi xong kết quả chấm."));

            // ================= TAI KHOAN =================
            ds.Add(Seconds(SettingKeys.IdentityAccessTokenLifetime, NhomKyThuat, 15 * 60, 60, Ngay,
                "Access token sống bao lâu."));
            ds.Add(Seconds(SettingKeys.IdentityRefreshTokenLifetime, NhomTaiKhoan, 14 * Ngay, 3600, 365 * Ngay,
                "Người dùng giữ đăng nhập bao lâu thì phải đăng nhập lại."));
            ds.Add(Int(SettingKeys.IdentityPasswordMinLength, NhomTaiKhoan, 8, 6, 128, "ký tự",
                "Độ dài mật khẩu tối thiểu."));
            ds.Add(Int(SettingKeys.IdentityPasswordMaxLength, NhomTaiKhoan, 128, 16, 1024, "ký tự",
                "Độ dài mật khẩu tối đa."));
            ds.Add(Long(SettingKeys.IdentityKybFileMaxBytes, NhomTaiKhoan, 10 * MB, 100 * 1024, 50 * MB, "byte",
                "Dung lượng tối đa file giấy phép kinh doanh doanh nghiệp nộp để xác minh."));
            ds.Add(Seconds(SettingKeys.IdentityRefreshTokenCleanupInterval, NhomKyThuat, 120, 60, 7 * Ngay,
                "Chu kỳ xoá refresh token đã hết hạn."));
            ds.Add(Text(SettingKeys.IdentityFrontendUrl, NhomKyThuat, "http://localhost:5173", 300,
                "Địa chỉ frontend — dùng để dựng link trong email xác thực / đặt lại mật khẩu."));
            ds.Add(Seconds(SettingKeys.IdentityEmailVerifyTtl, NhomTaiKhoan, Ngay, 600, 30 * Ngay,
                "Link xác thực email sống bao lâu."));
            ds.Add(Seconds(SettingKeys.IdentityPasswordResetTtl, NhomTaiKhoan, 30 * 60, 300, Ngay,
                "Link đặt lại mật khẩu sống bao lâu."));
            ds.Add(Long(SettingKeys.IdentityAvatarMaxBytes, NhomTaiKhoan, 2 * MB, 50 * 1024, 20 * MB, "byte",
                "Kích thước tối đa ảnh đại diện."));
            ds.Add(Text(SettingKeys.IdentityLevelRules, NhomTaiKhoan, "2=50/50;3=200/65;4=1000/75;5=5000/85", 200,
                "Điều kiện lên cấp, dạng cấp=số nhãn được duyệt/uy tín tối thiểu, ngăn cách bằng dấu ';' (vd 2=50/50: lên cấp 2 cần 50 nhãn và uy tín 50). Sai định dạng thì dùng mặc định."));
            ds.Add(Text(SettingKeys.IdentityTwoFactorIssuer, NhomTaiKhoan, "DATN Crowd", 50,
                "Tên hiện trong ứng dụng xác thực (Google Authenticator...) khi người dùng bật xác thực 2 lớp."));
            ds.Add(Seconds(SettingKeys.IdentityTwoFactorChallengeTtl, NhomTaiKhoan, 5 * 60, 60, 30 * 60,
                "Tài khoản bật xác thực 2 lớp: sau khi nhập đúng mật khẩu có bao lâu để nhập mã 6 số."));
            ds.Add(Int(SettingKeys.IdentityTwoFactorMaxAttempts, NhomTaiKhoan, 5, 1, 20, "lần",
                "Số lần nhập sai mã xác thực 2 lớp tối đa cho MỘT lần đăng nhập; hết lượt phải đăng nhập lại bằng mật khẩu (chống dò mã)."));
            ds.Add(Int(SettingKeys.IdentityAdultAge, NhomTaiKhoan, 18, 16, 21, "tuổi",
                "Tuổi tối thiểu để xem dự án nội dung nhạy cảm. Tính từ ngày sinh trên giấy tờ đã được admin xác minh."));
            ds.Add(Seconds(SettingKeys.IdentityLevelRecalcInterval, NhomKyThuat, 5, 1, 3600,
                "Identity tính lại cấp / huy hiệu cho labeler vừa có bài được chấp nhận hoặc vừa đổi uy tín sau mỗi khoảng này (số câu được chấp nhận đọc qua gRPC từ project-svc; project-svc không trả lời thì vòng sau thử lại)."));
            ds.Add(Seconds(SettingKeys.IdentityStatsCacheTtl, NhomKyThuat, 60, 0, 3600,
                "Bảng xếp hạng và số câu được chấp nhận (đọc qua gRPC từ project-svc) được giữ trong bộ nhớ của identity bấy lâu. 0 = luôn hỏi lại."));

            // ================= ADMIN =================
            // Danh sach loai event nam o Crowd.Contracts (building-blocks khong tham chieu) — admin-svc dien luc hien thi.
            ds.Add(Many(SettingKeys.AdminAuditExcludedTypes, NhomKyThuat, AuditLoaiTruMacDinh, 1000, null,
                "Loại event KHÔNG lưu chi tiết vào nhật ký audit (tần suất cao, do hệ thống / luồng gán nhãn sinh ra — vẫn được ĐẾM cho báo cáo tổng quan)."));
            ds.Add(Int(SettingKeys.AdminAuditRetentionDays, NhomKyThuat, 365, 7, 3650, "ngày",
                "Giữ nhật ký audit bao nhiêu ngày."));
            ds.Add(Bool(SettingKeys.MediaWatermarkEnabled, NhomBaoVeDuLieu, true,
                "Gắn dấu chìm (không nhìn thấy, mang danh tính người xem) vào ảnh / âm thanh / video phát cho người làm, người duyệt và khách cổng link — để truy ra ai làm lộ dữ liệu."));
            ds.Add(Int(SettingKeys.MediaWatermarkStrength, NhomBaoVeDuLieu, 24, 6, 80, "mức",
                "Độ mạnh dấu chìm: lớn thì khó bị mất khi file bị nén lại nhưng dễ nhìn thấy hơn. Đổi giá trị thì file đã phát trước đó VẪN truy được người xem."));

            // ================= GATEWAY =================
            ds.Add(Int(SettingKeys.GatewayRateLimitPerMinute, NhomKyThuat, 600, 10, 1000000, "request/phút",
                "Mỗi người dùng (hoặc mỗi IP nếu chưa đăng nhập) được gọi tối đa bấy nhiêu request mỗi phút."));
            ds.Add(Int(SettingKeys.GatewayRateLimitAuthPerMinute, NhomKyThuat, 20, 1, 1000000, "request/phút",
                "Mỗi IP: đăng nhập / đăng ký / quên mật khẩu / đặt lại mật khẩu / xác thực email tối đa bấy nhiêu lần mỗi phút."));
            ds.Add(Int(SettingKeys.GatewayRateLimitMoneyPerMinute, NhomKyThuat, 30, 1, 1000000, "request/phút",
                "Mỗi người dùng: tạo lệnh nạp tiền / rút tiền tối đa bấy nhiêu lần mỗi phút."));

            // ================= THONG BAO =================
            ds.Add(Bool(SettingKeys.NotificationEmailEnabled, NhomThongBao, true,
                "Gửi email thông báo (tắt thì chỉ còn thông báo trong ứng dụng)."));
            ds.Add(Text(SettingKeys.NotificationEmailFrom, NhomThongBao, "Crowd Label <no-reply@crowd.local>", 200,
                "Địa chỉ người gửi email thông báo."));
            ds.Add(Seconds(SettingKeys.NotificationEmailInterval, NhomKyThuat, 5, 1, 3600,
                "Chu kỳ worker gửi email thông báo."));
            ds.Add(Int(SettingKeys.NotificationEmailBatchSize, NhomKyThuat, 50, 1, 1000, "email",
                "Số email mỗi lô gửi."));
            ds.Add(Int(SettingKeys.NotificationRetentionDays, NhomThongBao, 180, 7, 3650, "ngày",
                "Giữ thông báo / email giao dịch bao nhiêu ngày (dòng còn email chờ gửi không bị xoá)."));
            ds.Add(Seconds(SettingKeys.NotificationCleanupInterval, NhomKyThuat, 3600, 60, 86400,
                "Chu kỳ job xoá thông báo quá hạn giữ và vé realtime hết hạn."));
            ds.Add(Seconds(SettingKeys.NotificationRealtimeTicketTtl, NhomKyThuat, 30, 5, 300,
                "Vé mở WebSocket thông báo realtime sống bao lâu (dùng một lần)."));
            ds.Add(Int(SettingKeys.NotificationEmailMaxAttempts, NhomKyThuat, 5, 1, 50, "lần",
                "Gửi email lỗi bao nhiêu lần thì bỏ (thông báo trong ứng dụng vẫn còn)."));

            // ================= KY THUAT =================
            ds.Add(Seconds(SettingKeys.OutboxPollInterval, NhomKyThuat, 0.5, 0.05, 60,
                "Outbox nghỉ bao lâu khi không có event chờ gửi."));
            ds.Add(Int(SettingKeys.OutboxBatchSize, NhomKyThuat, 50, 1, 5000, "event",
                "Số event mỗi lô outbox."));
            ds.Add(Seconds(SettingKeys.OutboxPublishTimeout, NhomKyThuat, 10, 1, 300,
                "Chờ broker xác nhận một event tối đa bao lâu."));
            ds.Add(Seconds(SettingKeys.OutboxRetryMaxDelay, NhomKyThuat, 300, 1, Ngay,
                "Gửi event thất bại thì thử lại sau 2, 4, 8... giây — không lùi quá mức này."));
            ds.Add(Seconds(SettingKeys.ConsumersReconnectDelay, NhomKyThuat, 5, 1, 300,
                "Consumer chưa nối được RabbitMQ thì thử lại sau bao lâu."));
            ds.Add(Seconds(SettingKeys.OutboxRetention, NhomKyThuat, 7 * Ngay, 3600, 365 * Ngay,
                "Event ĐÃ GỬI giữ trong bảng outbox bao lâu rồi xoá (để tra soát sự cố). Event chưa gửi không bao giờ bị xoá."));
            ds.Add(Seconds(SettingKeys.OutboxCleanupInterval, NhomKyThuat, 300, 10, Ngay,
                "Chu kỳ dọn bảng outbox (event đã gửi) và processed_events (dấu vết chống xử lý trùng) quá hạn."));
            ds.Add(Seconds(SettingKeys.ConsumersProcessedRetention, NhomKyThuat, 14 * Ngay, Ngay, 365 * Ngay,
                "Dấu vết “đã xử lý event này” giữ bao lâu. Phải DÀI hơn thời gian một message có thể bị giao lại (kể cả đưa lại từ DLQ) — quá hạn mà message cũ quay lại thì bị xử lý lần hai."));
            ds.Add(new SettingDefinition(SettingKeys.ConsumersPrefetchCount, NhomKyThuat, SettingType.SoNguyen, JsonValue.Create(10), 1, 1000,
                "message", SettingEffect.Restart,
                "Số message broker đẩy tới mỗi consumer trước khi ack. Có tác dụng khi service khởi động lại."));
            ds.Add(Seconds(SettingKeys.ConsumersRetryMaxDelay, NhomKyThuat, 30, 0, 600,
                "Event xử lý lỗi (lỗi kỹ thuật) được thử lại sau 1, 3, 9, 27... giây — không chờ quá mức này. Trong lúc chờ, hàng đợi đó tạm dừng nhận event khác. 0 = thử lại ngay."));
            ds.Add(Int(SettingKeys.OpsDlqAlertThreshold, NhomKyThuat, 1, 1, 1000000, "message",
                "Một hàng lỗi (DLQ) có từ bấy nhiêu message trở lên thì báo đội kỹ thuật. Chỉ lỗi kỹ thuật mới vào DLQ; ca nghiệp vụ cần người xử lý nằm ở hàng đợi duyệt."));
            ds.Add(Seconds(SettingKeys.OpsDlqCheckInterval, NhomKyThuat, 60, 10, 3600,
                "Chu kỳ admin-svc đọc số message trong các DLQ (RabbitMQ management API) — cũng là độ trễ của thẻ “Tình trạng hệ thống”."));
            ds.Add(Seconds(SettingKeys.OpsAlertRepeat, NhomKyThuat, 3600, 60, 86400,
                "DLQ vẫn còn message sau khoảng này thì gửi lại email cảnh báo (tránh gửi mỗi chu kỳ)."));
            ds.Add(Text(SettingKeys.OpsAlertEmail, NhomKyThuat, "", 200,
                "Email đội kỹ thuật nhận cảnh báo lỗi hệ thống (DLQ có message). Trống = chỉ ghi log và hiện trên thẻ “Tình trạng hệ thống”."));
            ds.Add(Seconds(SettingKeys.SettingsReloadInterval, NhomKyThuat, 5, 1, 3600,
                "Mỗi service đọc lại bản sao setting từ DB của nó sau mỗi khoảng này."));
            ds.Add(Seconds(SettingKeys.SettingsSnapshotInterval, NhomKyThuat, 300, 10, Ngay,
                "admin-svc phát lại toàn bộ setting định kỳ (để service lỡ mất event vẫn đồng bộ)."));

            // ================= CONG LINK (P2) =================
            ds.Add(Bool(SettingKeys.GateEnabled, NhomCongLink, true,
                "Tắt = trang vượt link chỉ chuyển thẳng tới đích, không phát câu hỏi, không trả tiền người chia sẻ link."));
            ds.Add(Many(SettingKeys.GateAllowedTools, NhomCongLink, "classification,pairwise,number,bbox,keypoint,span", 200, CongCuGanNhan,
                "Loại công cụ khách vãng lai làm được ở trang vượt link (vài giây, không uy tín). Dự án bật cổng link mà có công cụ ngoài danh sách thì KHÔNG publish được."));
            ds.Add(Seconds(SettingKeys.GateCountdown, NhomCongLink, 8, 0, 120,
                "Đếm ngược tối thiểu trước khi khách nộp bài (hệ thống kiểm tra thật, không bỏ qua được)."));
            ds.Add(Int(SettingKeys.GateGoldPerSession, NhomCongLink, 1, 1, 5, "câu",
                "Số câu vàng (đã biết đáp án) mỗi lượt vượt link."));
            ds.Add(Int(SettingKeys.GateRealPerSession, NhomCongLink, 2, 1, 10, "câu",
                "Số câu thật (cần thu thập nhãn) mỗi lượt vượt link."));
            ds.Add(Seconds(SettingKeys.GateSessionTtl, NhomCongLink, 600, 60, 3600,
                "Bộ câu hỏi đã phát sống bao lâu."));
            ds.Add(Seconds(SettingKeys.GateTokenTtl, NhomKyThuat, 180, 30, 900,
                "Token mở link đích dùng một lần, sống bao lâu (VD-L-02)."));
            ds.Add(Seconds(SettingKeys.GateIpDedupeWindow, NhomCongLink, Ngay, 0, 30 * Ngay,
                "Mỗi địa chỉ IP chỉ tính một lượt có tiền cho mỗi link trong khoảng này."));
            ds.Add(Int(SettingKeys.GateMaxLabelsPerSample, NhomCongLink, 5, 1, 1000, "nhãn",
                "Mỗi mẫu nhận tối đa bấy nhiêu nhãn từ cổng link."));
            ds.Add(Long(SettingKeys.GateUnitPriceVnd, NhomCongLink, 1000, 1, 10000000, "đ/nhãn",
                "Đơn giá MỖI nhãn khách vãng lai gán ở cổng link (chốt vào dự án lúc publish). Chỉ admin đặt; doanh nghiệp chỉ được thông báo mức này và tự đặt ngân sách cổng."));
            ds.Add(Int(SettingKeys.GateMaxFailsBeforeBlock, NhomCongLink, 3, 1, 100, "lần",
                "Khách vãng lai trượt câu kiểm tra đủ bấy nhiêu lần (tính theo địa chỉ IP, trong khoảng “Thời gian chặn IP trượt cổng”) thì bị chặn IP."));
            ds.Add(Seconds(SettingKeys.GateBlockDuration, NhomCongLink, Ngay, 60, 30 * Ngay,
                "Chặn IP bao lâu khi trượt quá số lần cho phép. Cũng là khoảng đếm số lần trượt và thời gian khoá một link với IP đã trượt link đó."));
            ds.Add(Seconds(SettingKeys.GateCacheRefreshInterval, NhomKyThuat, 60, 5, 3600,
                "Gate nạp lại bộ nhớ đệm dự án / mẫu / link từ bản sao sau mỗi khoảng này."));
            ds.Add(Seconds(SettingKeys.GateEventFlushInterval, NhomKyThuat, 1, 0.2, 60,
                "Gate chuyển lượt vượt từ Redis Stream sang outbox + ClickHouse sau mỗi khoảng này."));
            ds.Add(Int(SettingKeys.GateEventBatchSize, NhomKyThuat, 200, 1, 5000, "lượt",
                "Số lượt vượt gate chuyển mỗi lô."));
            ds.Add(Seconds(SettingKeys.GateContentLoadInterval, NhomKyThuat, 5, 1, 3600,
                "Gate kéo mẫu + câu vàng của dự án vừa publish bật cổng link từ project-svc (gRPC) sau mỗi khoảng này; project-svc không trả lời thì vòng sau thử lại."));
            ds.Add(Int(SettingKeys.LinkCodeLength, NhomCongLink, 7, 5, 12, "ký tự",
                "Độ dài mã link ngẫu nhiên."));
            ds.Add(Int(SettingKeys.LinkBulkMax, NhomCongLink, 100, 1, 1000, "link",
                "Rút gọn hàng loạt tối đa bấy nhiêu link mỗi lần."));
            ds.Add(Int(SettingKeys.LinkReportReviewThreshold, NhomCongLink, 3, 1, 1000, "báo cáo",
                "Link bị báo cáo từ bấy nhiêu IP khác nhau thì vào hàng đợi kiểm duyệt."));
            ds.Add(Seconds(SettingKeys.LinkScanInterval, NhomKyThuat, 2, 1, 3600,
                "Worker quét link chờ kiểm duyệt (Safe Browsing) sau mỗi khoảng này."));
            ds.Add(Int(SettingKeys.LinkReferralPercent, NhomCongLink, 10, 0, 50, "%",
                "Người giới thiệu hưởng phần trăm này trên doanh thu cổng link của người được mời. Lấy từ phần của nền tảng."));
            ds.Add(Int(SettingKeys.LinkReferralMaxPerIp, NhomCongLink, 2, 1, 1000, "tài khoản",
                "Một người giới thiệu đã có bấy nhiêu người nhập mã từ CÙNG một IP trong 24 giờ thì người tiếp theo bị đánh dấu nghi lạm dụng (chưa tính hoa hồng, chờ admin xét)."));
            ds.Add(Int(SettingKeys.LinkReferralBurstPerHour, NhomCongLink, 10, 1, 100000, "tài khoản",
                "Một người giới thiệu đã có bấy nhiêu người nhập mã trong 1 giờ thì người tiếp theo bị đánh dấu nghi lạm dụng (đột biến)."));
            ds.Add(Long(SettingKeys.LinkReferralMinEarningsVnd, NhomCongLink, 50000, 0, 1e12, "đồng",
                "Chỉ trả hoa hồng sau khi người được mời đã tự kiếm được tổng bấy nhiêu (chống tạo tài khoản ảo để tự mời nhau)."));
            ds.Add(Seconds(SettingKeys.LinkReferralClaimWindow, NhomCongLink, 7 * Ngay, 0, 365 * Ngay,
                "Tài khoản mới tạo trong khoảng này mới nhập được mã giới thiệu."));

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
            return new SettingDefinition(key, group, SettingType.ThoiGian, JsonValue.Create(def), min, max, "giây", SettingEffect.NewOperations, description);
        }

        private static SettingDefinition Choice(string key, string group, string def, string[] choices, string description)
        {
            return new SettingDefinition(key, group, SettingType.Chuoi, JsonValue.Create(def), null, 50, "", SettingEffect.NewOperations, description, choices);
        }

        /// <summary>Setting "chon nhieu": chuoi "a,b,c", moi phan tu thuoc choices (null = khong kiem tung phan tu).</summary>
        private static SettingDefinition Many(string key, string group, string def, int maxLength, string[]? choices, string description)
        {
            return new SettingDefinition(key, group, SettingType.Chuoi, JsonValue.Create(def), null, maxLength, "ký tự", SettingEffect.NewOperations, description, choices, true);
        }

        private static SettingDefinition Text(string key, string group, string def, int maxLength, string description)
        {
            return new SettingDefinition(key, group, SettingType.Chuoi, JsonValue.Create(def), null, maxLength, "ký tự", SettingEffect.NewOperations, description);
        }
    }
}
