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
        public const string DatasetManifestFileMaxRows = "dataset.manifest_file_max_rows";
        public const string DatasetTextMaxChars = "dataset.text_max_chars";
        public const string DatasetImageMaxBytes = "dataset.image_max_bytes";
        public const string DatasetEventBatchSize = "dataset.event_batch_size";
        public const string DatasetWorkerIdle = "dataset.worker_idle";
        public const string DatasetNameMaxLength = "dataset.name_max_length";
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
        public const string AnnotationHistoryRetention = "annotation.history_retention";
        public const string AnnotationHistoryCleanupInterval = "annotation.history_cleanup_interval";

        // ---- Chat luong ----
        public const string QualityDsInterval = "quality.ds_interval";
        public const string QualityFraudEnabled = "quality.fraud_enabled";
        public const string QualityFraudMinSeconds = "quality.fraud_min_seconds";
        public const string QualityFraudFastRatio = "quality.fraud_fast_ratio";
        public const string QualityFraudFastWindow = "quality.fraud_fast_window";
        public const string QualityFraudSameLabelStreak = "quality.fraud_same_label_streak";
        public const string QualityFraudSharedAccounts = "quality.fraud_shared_accounts";
        public const string QualityFraudSharedWindow = "quality.fraud_shared_window";
        public const string QualityFraudIgnorePrivateIps = "quality.fraud_ignore_private_ips";
        public const string QualityFraudReopenCooldown = "quality.fraud_reopen_cooldown";
        public const string QualityFraudSubmissionRetention = "quality.fraud_submission_retention";
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

        // ---- Admin: nhat ky audit (FP-06 / VD-S-10), thong ke (FM-09) ----
        public const string AdminAuditExcludedTypes = "admin.audit_excluded_types";
        public const string AdminAuditRetentionDays = "admin.audit_retention_days";

        // ---- Van hanh: canh bao do sau DLQ (VD-D-06, VD-D-12) ----
        public const string OpsDlqAlertThreshold = "ops.dlq_alert_threshold";
        public const string OpsDlqCheckInterval = "ops.dlq_check_interval";
        public const string OpsAlertRepeat = "ops.alert_repeat";
        public const string OpsAlertEmail = "ops.alert_email";

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
        private const string NhomKyThuat = "Kỹ thuật";
        private const string NhomBaoVeDuLieu = "Bảo vệ dữ liệu";

        /// <summary>
        /// Event tan suat cao (moi nhan / moi luot vuot link / moi lan dong bo mot event): khong luu chi tiet
        /// vao nhat ky audit. Nhat ky giu THAO TAC CUA NGUOI VA QUAN TRI (dang ky, KYB, khoa, du an, tien nap /
        /// rut, tranh chap, setting...). Lich su tung nhan da co o annotation_history, tien o so cai.
        /// </summary>
        public const string AuditLoaiTruMacDinh =
            "gate.solved,click.validated,settings.snapshot,settings.snapshot_requested,dataset.ingested,"
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
            ds.Add(Text(SettingKeys.PaymentEnabledProviders, NhomTien, "sandbox,vnpay,momo", 100,
                "Cổng nạp tiền đang mở (sandbox, vnpay, momo — ngăn cách dấu phẩy). Cổng phải CÓ cấu hình khoá mới dùng được; tắt một cổng khi cổng đó sự cố."));
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
                "Thuế suất GTGT trên phí nền tảng (FB-05). Phí đã thu là giá ĐÃ GỒM thuế; hoá đơn tách ngược ra phần chưa thuế + thuế. Áp cho hoá đơn xuất sau khi đổi."));
            ds.Add(Text(SettingKeys.InvoiceSellerName, NhomTien, "CONG TY TNHH NEN TANG GAN NHAN DATN", 200,
                "Tên đơn vị bán hàng in trên hoá đơn."));
            ds.Add(Text(SettingKeys.InvoiceSellerTaxCode, NhomTien, "0109999999", 20,
                "Mã số thuế đơn vị bán hàng."));
            ds.Add(Text(SettingKeys.InvoiceSellerAddress, NhomTien, "So 1 Dai Co Viet, Hai Ba Trung, Ha Noi", 300,
                "Địa chỉ đơn vị bán hàng."));
            ds.Add(Text(SettingKeys.InvoiceSerialTemplate, NhomTien, "1C{yy}TDN", 20,
                "Ký hiệu hoá đơn; {yy} = 2 số cuối năm. 1 = hoá đơn GTGT, C = có mã của cơ quan thuế, T = điện tử, DN = mã riêng."));
            ds.Add(Bool(SettingKeys.InvoiceAutoIssue, NhomTien, true,
                "Bật: đầu mỗi tháng tự xuất hoá đơn tháng trước cho mọi doanh nghiệp có phát sinh phí nền tảng (đã xác minh KYB)."));
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
                "Số email tối đa một người được mời vào dự án trong 24 giờ (VD-S-16) — chặn dùng ô mời để dò danh sách email. Vượt → 429."));
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
                "Tiền thưởng tối thiểu cho MỘT người làm đạt cả dự án (FM-04). 0 = không giới hạn."));
            ds.Add(Seconds(SettingKeys.AnnotationHistoryRetention, NhomGanNhan, 365 * Ngay, 0, 3650 * Ngay,
                "Hạn giữ nhật ký vòng đời nhãn (annotation_history): dòng cũ hơn bị xoá, trừ dòng mới nhất của mỗi nhãn. 0 = không bao giờ xoá."));
            ds.Add(Seconds(SettingKeys.AnnotationHistoryCleanupInterval, NhomKyThuat, 3600, 10, Ngay,
                "Chu kỳ worker dọn annotation_history."));
            ds.Add(Seconds(SettingKeys.ProjectClosedDataRetention, NhomDuAn, 90 * Ngay, 0, 3650 * Ngay,
                "Dự án đã đóng (hoàn thành / huỷ) bao lâu thì xoá dữ liệu VẬN HÀNH của nó: task, lượt giữ, báo lỗi (task-svc); nhãn thô và vòng đồng thuận (quality-svc). Trước khi xoá, tiến độ và báo cáo chất lượng được chụp lại nên vẫn xem được. Nhãn đã nộp / đã duyệt, kết quả, sổ cái, mẫu dữ liệu và uy tín labeler KHÔNG bị xoá. 0 = không bao giờ xoá."));
            ds.Add(Int(SettingKeys.ProjectEntranceQuestionDefault, NhomDuAn, 10, 1, 500, "câu",
                "Số câu bài test đầu vào mặc định của dự án mới tạo."));
            ds.Add(Int(SettingKeys.ProjectEntrancePassPercentDefault, NhomDuAn, 80, 1, 100, "%",
                "Ngưỡng đậu bài test đầu vào mặc định của dự án mới tạo."));
            ds.Add(Int(SettingKeys.ProjectEntranceQuestionMax, NhomDuAn, 50, 1, 500, "câu",
                "Số câu bài test đầu vào tối đa."));
            ds.Add(Int(SettingKeys.ProjectGoldItemsMax, NhomDuAn, 2000, 1, 100000, "câu",
                "Số câu hỏi vàng tối đa mỗi dự án."));
            ds.Add(Int(SettingKeys.ProjectNameMaxLength, NhomDuAn, 200, 10, 200, "ký tự",
                "Độ dài tên dự án (cột DB tối đa 200)."));
            ds.Add(Int(SettingKeys.ProjectDescriptionMaxLength, NhomDuAn, 5000, 100, 5000, "ký tự",
                "Độ dài mô tả dự án (cột DB tối đa 5000)."));
            ds.Add(Int(SettingKeys.GuidelineMarkdownMaxLength, NhomDuAn, 20000, 100, 200000, "ký tự",
                "Độ dài hướng dẫn (markdown)."));
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
                "Kích thước tối đa file ZIP ảnh upload qua API (gateway và project-svc cùng áp)."));
            ds.Add(Int(SettingKeys.DatasetZipMaxEntries, NhomDuLieu, 10000, 1, 1000000, "file",
                "Số file tối đa trong một ZIP."));
            ds.Add(Long(SettingKeys.DatasetZipImageMaxBytes, NhomDuLieu, 20 * MB, 1024, 1024 * MB, "byte",
                "Kích thước tối đa mỗi ảnh trong ZIP (sau giải nén)."));
            ds.Add(Long(SettingKeys.DatasetZipTotalMaxBytes, NhomDuLieu, 2048 * MB, MB, 100L * 1024 * MB, "byte",
                "Tổng dung lượng sau giải nén tối đa của một ZIP (chống bom ZIP)."));
            ds.Add(Int(SettingKeys.DatasetManifestInlineMaxRows, NhomDuLieu, 1000, 1, 100000, "dòng",
                "Số dòng manifest tối đa gửi kèm request."));
            ds.Add(Int(SettingKeys.DatasetManifestFileMaxRows, NhomDuLieu, 50000, 1, 10000000, "dòng",
                "Số dòng tối đa của file manifest .jsonl."));
            ds.Add(Int(SettingKeys.DatasetTextMaxChars, NhomDuLieu, 100000, 100, 10000000, "ký tự",
                "Độ dài tối đa một mẫu văn bản."));
            ds.Add(Long(SettingKeys.DatasetImageMaxBytes, NhomDuLieu, 50 * MB, 1024, 2048 * MB, "byte",
                "Kích thước tối đa một ảnh nạp qua manifest."));
            ds.Add(Int(SettingKeys.DatasetEventBatchSize, NhomKyThuat, 500, 1, 10000, "mẫu",
                "Số mẫu mỗi event dataset.ingested (chia lô khi nạp nhiều)."));
            ds.Add(Int(SettingKeys.DatasetImportMaxFiles, NhomDuLieu, 500, 1, 100000, "file",
                "Nhập từ nguồn ngoài (S3 / Google Drive / danh sách URL, FB-11): số file tối đa mỗi lô."));
            ds.Add(Long(SettingKeys.DatasetImportFileMaxBytes, NhomDuLieu, 500 * MB, MB, 10240 * MB, "byte",
                "Nhập từ nguồn ngoài: kích thước tối đa mỗi file âm thanh / video (ảnh theo dataset.image_max_bytes)."));
            ds.Add(Seconds(SettingKeys.DatasetImportTimeout, NhomKyThuat, 120, 5, 3600,
                "Nhập từ nguồn ngoài: thời gian tối đa tải MỘT file."));
            ds.Add(Seconds(SettingKeys.DatasetWorkerIdle, NhomKyThuat, 2, 0.1, 600,
                "Worker nạp dữ liệu nghỉ bao lâu khi hết việc."));
            ds.Add(Int(SettingKeys.DatasetNameMaxLength, NhomDuLieu, 200, 10, 200, "ký tự",
                "Độ dài tên lô dữ liệu (cột DB tối đa 200). Tên lô do hệ thống tự sinh từ tên dự án + ngày giờ nạp."));
            ds.Add(Int(SettingKeys.DatasetErrorSummaryMaxLength, NhomDuLieu, 4000, 100, 4000, "ký tự",
                "Độ dài tóm tắt lỗi của lô (cột DB tối đa 4000)."));
            ds.Add(Int(SettingKeys.DatasetErrorSampleCount, NhomDuLieu, 20, 1, 1000, "dòng",
                "Số dòng lỗi đầu tiên ghi lại khi nạp một lô dữ liệu."));
            ds.Add(Int(SettingKeys.SampleNameMaxLength, NhomDuLieu, 500, 10, 500, "ký tự",
                "Độ dài tên mẫu (cột DB tối đa 500)."));
            ds.Add(Int(SettingKeys.UploadMaxFiles, NhomDuLieu, 100, 1, 10000, "file",
                "Số file tối đa mỗi lần xin link upload."));
            ds.Add(Long(SettingKeys.UploadMaxFileBytes, NhomDuLieu, 5L * 1024 * MB, MB, 5L * 1024 * MB, "byte",
                "Kích thước tối đa mỗi file upload thẳng (giới hạn của một lệnh PUT S3 là 5 GB)."));
            ds.Add(Seconds(SettingKeys.UploadLinkTtl, NhomDuLieu, 3600, 60, 7 * Ngay,
                "Link upload ký sẵn sống bao lâu."));
            ds.Add(Seconds(SettingKeys.StorageViewLinkTtl, NhomDuLieu, 300, 10, 7 * Ngay,
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
                "Bao nhiêu labeler khác nhau báo lỗi cùng một mẫu thì tự loại mẫu đó khỏi dự án (FL-07)."));
            ds.Add(Int(SettingKeys.TaskReaperBatchSize, NhomKyThuat, 200, 1, 10000, "lượt giữ",
                "Số lượt giữ quá hạn reaper khoá và trả về pool mỗi lô."));
            ds.Add(Int(SettingKeys.AnnotationImbalanceThresholdPercent, NhomGanNhan, 80, 50, 99, "%",
                "Cảnh báo lệch phân bố (FB-24) khi một lớp chiếm trên bấy nhiêu phần trăm số nhãn của công cụ."));
            ds.Add(Int(SettingKeys.AnnotationImbalanceMinLabels, NhomGanNhan, 20, 1, 100000, "nhãn",
                "Công cụ cần ít nhất bấy nhiêu nhãn mới bắt đầu cảnh báo lệch phân bố."));

            // ================= CHAT LUONG =================
            ds.Add(Seconds(SettingKeys.QualityDsInterval, NhomChatLuong, 900, 10, 7 * Ngay,
                "Chu kỳ chạy lô Dawid-Skene."));

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
            ds.Add(Bool(SettingKeys.QualityFraudIgnorePrivateIps, NhomChatLuong, true,
                "Bỏ qua IP nội bộ / loopback khi xét chung IP (môi trường dev, mạng công ty dùng chung NAT)."));
            ds.Add(Seconds(SettingKeys.QualityFraudReopenCooldown, NhomChatLuong, 7 * Ngay, 0, 365 * Ngay,
                "Hồ sơ đã xử lý (bỏ qua / xác nhận) thì không mở lại cùng khoá trong khoảng này."));
            ds.Add(Seconds(SettingKeys.QualityFraudSubmissionRetention, NhomChatLuong, 7 * Ngay, Ngay, 365 * Ngay,
                "Giữ dữ liệu lượt nộp dùng để xét gian lận (tốc độ, IP, thiết bị) bao lâu. Thuật toán chỉ cần vài chục lượt gần nhất và cửa sổ chung IP — không cần giữ mãi."));
            ds.Add(Int(SettingKeys.QualityDsMinLabels, NhomChatLuong, 5, 1, 10000, "nhãn",
                "Labeler cần tối thiểu bao nhiêu nhãn trong dự án thì mới chấm độ tin cậy Dawid-Skene."));
            ds.Add(Double(SettingKeys.QualityReputationGoldWeight, NhomChatLuong, 0.6, 0, 1, "",
                "Trọng số câu vàng trong điểm uy tín (phần còn lại là đồng thuận / Dawid-Skene)."));
            ds.Add(Double(SettingKeys.QualityReputationPrior, NhomChatLuong, 0.7, 0, 1, "",
                "Mức tiên nghiệm của điểm uy tín (người mới bắt đầu quanh mức này)."));
            ds.Add(Double(SettingKeys.QualityReputationPriorStrength, NhomChatLuong, 2, 0, 1000, "nhãn",
                "Sức nặng tiên nghiệm: số bằng chứng cần để điểm rời xa mức tiên nghiệm."));
            ds.Add(Double(SettingKeys.LabelingThresholdBbox, NhomChatLuong, 0.5, 0, 1, "IoU",
                "Ngưỡng chấm mặc định cho khung chữ nhật (công cụ không tự đặt matchThreshold)."));
            ds.Add(Double(SettingKeys.LabelingThresholdPolygon, NhomChatLuong, 0.5, 0, 1, "IoU",
                "Ngưỡng chấm mặc định cho đa giác."));
            ds.Add(Double(SettingKeys.LabelingThresholdSpan, NhomChatLuong, 1.0, 0, 1, "F1",
                "Ngưỡng chấm mặc định cho đoạn văn bản."));
            ds.Add(Double(SettingKeys.LabelingThresholdTranscription, NhomChatLuong, 0.1, 0, 1, "CER",
                "Tỉ lệ lỗi ký tự tối đa cho bản chép (nhỏ hơn = khắt khe hơn)."));
            ds.Add(Double(SettingKeys.LabelingThresholdTemporalSegment, NhomChatLuong, 0.5, 0, 1, "IoU",
                "Ngưỡng chấm mặc định cho đoạn thời gian."));
            ds.Add(Double(SettingKeys.LabelingThresholdKeypoint, NhomChatLuong, 0.98, 0, 1, "độ giống",
                "Ngưỡng mặc định cho điểm mốc: 1 − (độ lệch / đường chéo ảnh). 0,98 = lệch tối đa 2% đường chéo."));
            ds.Add(Double(SettingKeys.LabelingThresholdPolyline, NhomChatLuong, 0.97, 0, 1, "độ giống",
                "Ngưỡng mặc định cho đường gấp khúc: 1 − (độ lệch trung bình / đường chéo ảnh)."));
            ds.Add(Double(SettingKeys.LabelingThresholdMask, NhomChatLuong, 0.5, 0, 1, "IoU",
                "Ngưỡng mặc định cho mặt nạ phân vùng."));
            ds.Add(Double(SettingKeys.LabelingThresholdTrack, NhomChatLuong, 0.5, 0, 1, "IoU",
                "Ngưỡng mặc định cho chuỗi khung theo dõi trong video (IoU không-thời gian)."));
            ds.Add(Double(SettingKeys.LabelingThresholdRelation, NhomChatLuong, 1.0, 0, 1, "F1",
                "Ngưỡng chấm mặc định cho quan hệ giữa các đoạn văn bản."));
            ds.Add(Double(SettingKeys.LabelingThresholdRanking, NhomChatLuong, 0.8, 0, 1, "Kendall",
                "Ngưỡng mặc định cho xếp hạng: tỉ lệ cặp mục được xếp cùng thứ tự."));
            ds.Add(Double(SettingKeys.QualityConsensusMinF1, NhomChatLuong, 0.8, 0, 1, "F1",
                "Gộp nhãn công cụ dạng danh sách (khung, đa giác, đoạn...): một người tính là KHỚP kết quả gộp khi F1 giữa các đối tượng của họ và các đối tượng đã gộp đạt mức này. Không quá một nửa số người khớp thì mẫu là tranh chấp."));
            ds.Add(Double(SettingKeys.QualityFreeTextSimilarity, NhomChatLuong, 0.6, 0.3, 0.99, "cosine",
                "Câu trả lời mở (freeText) chấm THEO NGHĨA (ml-svc nhúng câu): một người giống đa số khi độ giống cosine giữa câu của họ và câu “ở giữa” cụm (medoid) đạt mức này. Câu cùng nghĩa khác chữ thường 0,63 – 0,9; câu lạc đề gần 0."));
            ds.Add(Double(SettingKeys.QualityFreeTextLexicalSimilarity, NhomChatLuong, 0.38, 0.1, 0.99, "cosine",
                "Ngưỡng khi PHẢI chấm câu trả lời mở bằng so CHỮ (n-gram ký tự) vì ml-svc không phân tích kịp. So chữ yếu hơn so nghĩa: câu cùng nghĩa khác chữ chỉ 0,4 – 0,5."));
            ds.Add(Seconds(SettingKeys.QualityFreeTextAnalysisTimeout, NhomChatLuong, 3600, 10, 7 * Ngay,
                "Sau khi dự án đóng thu bài, chờ ml-svc phân tích hết câu trả lời mở tối đa bao lâu; quá hạn thì chấm bằng so chữ để không giữ tiền của labeler."));
            ds.Add(Seconds(SettingKeys.QualityFreeTextAnalysisInterval, NhomKyThuat, 5, 1, 3600,
                "Chu kỳ quality-svc gửi câu trả lời mở mới sang ml-svc và thử chấm lại dự án đang chờ phân tích."));

            // ================= TAI KHOAN =================
            ds.Add(Seconds(SettingKeys.IdentityAccessTokenLifetime, NhomTaiKhoan, 15 * 60, 60, Ngay,
                "Access token sống bao lâu."));
            ds.Add(Seconds(SettingKeys.IdentityRefreshTokenLifetime, NhomTaiKhoan, 14 * Ngay, 3600, 365 * Ngay,
                "Refresh token sống bao lâu (đăng nhập được giữ bao lâu)."));
            ds.Add(Int(SettingKeys.IdentityPasswordMinLength, NhomTaiKhoan, 8, 6, 128, "ký tự",
                "Độ dài mật khẩu tối thiểu."));
            ds.Add(Int(SettingKeys.IdentityPasswordMaxLength, NhomTaiKhoan, 128, 16, 1024, "ký tự",
                "Độ dài mật khẩu tối đa."));
            ds.Add(Long(SettingKeys.IdentityKybFileMaxBytes, NhomTaiKhoan, 10 * MB, 100 * 1024, 50 * MB, "byte",
                "Kích thước tối đa file giấy phép kinh doanh (KYB)."));
            ds.Add(Seconds(SettingKeys.IdentityRefreshTokenCleanupInterval, NhomKyThuat, 120, 60, 7 * Ngay,
                "Chu kỳ xoá refresh token đã hết hạn."));
            ds.Add(Text(SettingKeys.IdentityFrontendUrl, NhomTaiKhoan, "http://localhost:5173", 300,
                "Địa chỉ frontend — dùng để dựng link trong email xác thực / đặt lại mật khẩu."));
            ds.Add(Seconds(SettingKeys.IdentityEmailVerifyTtl, NhomTaiKhoan, Ngay, 600, 30 * Ngay,
                "Link xác thực email sống bao lâu."));
            ds.Add(Seconds(SettingKeys.IdentityPasswordResetTtl, NhomTaiKhoan, 30 * 60, 300, Ngay,
                "Link đặt lại mật khẩu sống bao lâu."));
            ds.Add(Long(SettingKeys.IdentityAvatarMaxBytes, NhomTaiKhoan, 2 * MB, 50 * 1024, 20 * MB, "byte",
                "Kích thước tối đa ảnh đại diện."));
            ds.Add(Text(SettingKeys.IdentityLevelRules, NhomTaiKhoan, "2=50/50;3=200/65;4=1000/75;5=5000/85", 200,
                "Điều kiện lên cấp: cấp=số nhãn được duyệt/uy tín tối thiểu, ngăn cách bằng ';'. Sai định dạng thì dùng mặc định."));
            ds.Add(Text(SettingKeys.IdentityTwoFactorIssuer, NhomTaiKhoan, "DATN Crowd", 50,
                "Tên hiện trong ứng dụng xác thực (Google Authenticator...) khi người dùng bật 2FA (FC-04)."));
            ds.Add(Seconds(SettingKeys.IdentityTwoFactorChallengeTtl, NhomTaiKhoan, 5 * 60, 60, 30 * 60,
                "Đăng nhập đúng mật khẩu của tài khoản bật 2FA: có bao lâu để nhập mã 6 số."));
            ds.Add(Int(SettingKeys.IdentityTwoFactorMaxAttempts, NhomTaiKhoan, 5, 1, 20, "lần",
                "Số lần nhập sai mã 2FA tối đa cho MỘT lần đăng nhập; hết lượt phải đăng nhập lại bằng mật khẩu (chống dò mã 6 số)."));
            ds.Add(Int(SettingKeys.IdentityAdultAge, NhomTaiKhoan, 18, 16, 21, "tuổi",
                "Tuổi tối thiểu để xem dự án nội dung nhạy cảm (mục 2.9, VD-P-04). Tính từ ngày sinh trên giấy tờ đã được admin xác minh."));

            // ================= ADMIN =================
            ds.Add(Text(SettingKeys.AdminAuditExcludedTypes, NhomKyThuat, AuditLoaiTruMacDinh, 1000,
                "Loại event KHÔNG lưu chi tiết vào nhật ký audit (tần suất cao, do hệ thống / luồng gán nhãn sinh ra — vẫn được ĐẾM cho báo cáo tổng quan), ngăn cách bằng dấu phẩy."));
            ds.Add(Int(SettingKeys.AdminAuditRetentionDays, NhomKyThuat, 365, 7, 3650, "ngày",
                "Giữ nhật ký audit bao nhiêu ngày."));
            ds.Add(Int(SettingKeys.OpsDlqAlertThreshold, NhomKyThuat, 1, 1, 1000000, "message",
                "Một hàng đợi DLQ có từ bấy nhiêu message trở lên thì cảnh báo (message vào DLQ = tiền / trạng thái có thể đang đứng)."));
            ds.Add(Seconds(SettingKeys.OpsDlqCheckInterval, NhomKyThuat, 60, 10, 3600,
                "Chu kỳ admin-svc đọc độ sâu các DLQ qua RabbitMQ management API."));
            ds.Add(Seconds(SettingKeys.OpsAlertRepeat, NhomKyThuat, 3600, 60, 86400,
                "DLQ vẫn còn message sau khoảng này thì cảnh báo lại (tránh spam mỗi chu kỳ)."));
            ds.Add(Text(SettingKeys.OpsAlertEmail, NhomKyThuat, "", 200,
                "Email nhận cảnh báo vận hành (DLQ). Trống = chỉ ghi log và hiện trên màn hình admin."));

            // ================= MEDIA (bao ve du lieu) =================
            ds.Add(Bool(SettingKeys.MediaWatermarkEnabled, NhomBaoVeDuLieu, true,
                "Gắn watermark chìm (nhúng thẳng người xem) vào ảnh / âm thanh / video phát cho labeler / reviewer / khách cổng link."));
            ds.Add(Int(SettingKeys.MediaWatermarkStrength, NhomBaoVeDuLieu, 24, 6, 80, "bước QIM",
                "Độ mạnh watermark chìm: lớn thì bền hơn với nén JPEG nhưng dễ lộ hơn. Đổi giá trị thì ảnh đã phát trước đó VẪN giải mã được (bộ giải thử mọi bước 6–80)."));

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
            ds.Add(Seconds(SettingKeys.SettingsReloadInterval, NhomKyThuat, 5, 1, 3600,
                "Mỗi service đọc lại bản sao setting từ DB của nó sau mỗi khoảng này."));
            ds.Add(Seconds(SettingKeys.SettingsSnapshotInterval, NhomKyThuat, 300, 10, Ngay,
                "admin-svc phát lại toàn bộ setting định kỳ (để service lỡ mất event vẫn đồng bộ)."));

            // ================= CONG LINK (P2) =================
            ds.Add(Bool(SettingKeys.GateEnabled, NhomCongLink, true,
                "Tắt = trang vượt link chỉ chuyển thẳng tới đích, không phát câu hỏi, không trả tiền sharer."));
            ds.Add(Text(SettingKeys.GateAllowedTools, NhomCongLink, "classification,pairwise,number,bbox,keypoint,span", 200,
                "Loại công cụ khách vãng lai làm được ở trang vượt link (vài giây, không uy tín). Dự án bật cổng link mà có công cụ ngoài danh sách thì KHÔNG publish được."));
            ds.Add(Seconds(SettingKeys.GateCountdown, NhomCongLink, 8, 0, 120,
                "Đếm ngược tối thiểu trước khi khách nộp bài (server kiểm, không chỉ là giao diện)."));
            ds.Add(Int(SettingKeys.GateGoldPerSession, NhomCongLink, 1, 1, 5, "câu",
                "Số câu vàng (đã biết đáp án) mỗi lượt vượt link."));
            ds.Add(Int(SettingKeys.GateRealPerSession, NhomCongLink, 2, 1, 10, "câu",
                "Số câu thật (cần thu thập nhãn) mỗi lượt vượt link."));
            ds.Add(Seconds(SettingKeys.GateSessionTtl, NhomCongLink, 600, 60, 3600,
                "Bộ câu hỏi đã phát sống bao lâu."));
            ds.Add(Seconds(SettingKeys.GateTokenTtl, NhomCongLink, 180, 30, 900,
                "Token mở link đích dùng một lần, sống bao lâu (VD-L-02)."));
            ds.Add(Seconds(SettingKeys.GateIpDedupeWindow, NhomCongLink, Ngay, 0, 30 * Ngay,
                "Mỗi IP chỉ tính một lượt có tiền cho mỗi link trong khoảng này (đặc tả: 24 giờ)."));
            ds.Add(Int(SettingKeys.GateMaxLabelsPerSample, NhomCongLink, 5, 1, 1000, "nhãn",
                "Mỗi mẫu nhận tối đa bấy nhiêu nhãn từ cổng link."));
            ds.Add(Seconds(SettingKeys.GateCacheRefreshInterval, NhomCongLink, 60, 5, 3600,
                "Gate nạp lại bộ nhớ đệm dự án / mẫu / link từ bản sao sau mỗi khoảng này."));
            ds.Add(Seconds(SettingKeys.GateEventFlushInterval, NhomKyThuat, 1, 0.2, 60,
                "Gate chuyển lượt vượt từ Redis Stream sang outbox + ClickHouse sau mỗi khoảng này."));
            ds.Add(Int(SettingKeys.GateEventBatchSize, NhomKyThuat, 200, 1, 5000, "lượt",
                "Số lượt vượt gate chuyển mỗi lô."));
            ds.Add(Int(SettingKeys.LinkCodeLength, NhomCongLink, 7, 5, 12, "ký tự",
                "Độ dài mã link ngẫu nhiên."));
            ds.Add(Int(SettingKeys.LinkBulkMax, NhomCongLink, 100, 1, 1000, "link",
                "Rút gọn hàng loạt tối đa bấy nhiêu link mỗi lần (FS-03)."));
            ds.Add(Int(SettingKeys.LinkReportReviewThreshold, NhomCongLink, 3, 1, 1000, "báo cáo",
                "Link bị báo cáo từ bấy nhiêu IP khác nhau thì vào hàng đợi kiểm duyệt."));
            ds.Add(Seconds(SettingKeys.LinkScanInterval, NhomKyThuat, 2, 1, 3600,
                "Worker quét link chờ kiểm duyệt (Safe Browsing) sau mỗi khoảng này."));
            ds.Add(Int(SettingKeys.LinkReferralPercent, NhomCongLink, 10, 0, 50, "%",
                "Người giới thiệu hưởng phần trăm này trên doanh thu cổng link của người được mời (FS-08). Lấy từ phần của nền tảng."));
            ds.Add(Int(SettingKeys.LinkReferralMaxPerIp, NhomCongLink, 2, 1, 1000, "tài khoản",
                "Một người giới thiệu đã có bấy nhiêu người nhập mã từ CÙNG một IP trong 24 giờ thì người tiếp theo bị đánh dấu nghi lạm dụng (chưa tính hoa hồng, chờ admin xét)."));
            ds.Add(Int(SettingKeys.LinkReferralBurstPerHour, NhomCongLink, 10, 1, 100000, "tài khoản",
                "Một người giới thiệu đã có bấy nhiêu người nhập mã trong 1 giờ thì người tiếp theo bị đánh dấu nghi lạm dụng (đột biến)."));
            ds.Add(Long(SettingKeys.LinkReferralMinEarningsVnd, NhomCongLink, 50000, 0, 1e12, "đồng",
                "Chỉ trả hoa hồng sau khi người được mời đã tự kiếm được tổng bấy nhiêu (chống tài khoản clone tự mời nhau — VD-L-05)."));
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

        private static SettingDefinition Text(string key, string group, string def, int maxLength, string description)
        {
            return new SettingDefinition(key, group, SettingType.Chuoi, JsonValue.Create(def), null, maxLength, "ký tự", SettingEffect.NewOperations, description);
        }
    }
}
