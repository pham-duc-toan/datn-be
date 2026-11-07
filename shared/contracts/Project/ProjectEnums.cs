namespace Crowd.Contracts.Project
{
    /// <summary>Vai tro cua mot nguoi TRONG MOT du an (khac vai tro tai khoan).</summary>
    public enum ProjectMemberRole
    {
        /// <summary>Doanh nghiep so huu du an.</summary>
        Owner,

        /// <summary>Nguoi gan nhan duoc phep nhan task cua du an.</summary>
        Labeler,

        /// <summary>Nguoi duyet nhan thay mat doanh nghiep.</summary>
        Reviewer,
    }
}
