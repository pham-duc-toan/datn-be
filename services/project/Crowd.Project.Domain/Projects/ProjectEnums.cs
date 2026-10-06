namespace Crowd.Project.Domain.Projects
{
    /// <summary>
    /// Vong doi du an — muc 2.2 dac ta + saga ky quy muc 3.4 docs.
    ///
    ///   Draft ──publish──► PendingEscrow ──escrow.reserved──► PendingApproval ──duyet──► Running ⇄ Paused
    ///     ▲                     │                                  │                       │       │
    ///     └────escrow.rejected──┘                        tu choi / qua 72h                 ▼       ▼
    ///                                                              └──────► Cancelled ◄── Completed
    ///
    /// Luu thanh CHUOI trong database, khong phai so thu tu, nen them trang
    /// thai moi vao giua khong lam lech du lieu cu.
    /// </summary>
    public enum ProjectStatus
    {
        /// <summary>Nhap: doanh nghiep dang cau hinh. Chi o day moi sua duoc.</summary>
        Draft,

        /// <summary>Da bam publish, cho ledger giu tien ky quy.</summary>
        PendingEscrow,

        /// <summary>Da ky quy, cho admin duyet (FM-02). Qua 72h thi tu huy.</summary>
        PendingApproval,

        /// <summary>Dang chay: labeler tham gia va nhan task.</summary>
        Running,

        /// <summary>Tam dung: ngung cap task, van giu ky quy.</summary>
        Paused,

        Completed,

        Cancelled,
    }

    public enum ProjectVisibility
    {
        /// <summary>Moi labeler du dieu kien deu thay va tham gia duoc.</summary>
        Public,

        /// <summary>Private pool (FP-05): chi nguoi duoc chu du an moi.</summary>
        Private,
    }
}
