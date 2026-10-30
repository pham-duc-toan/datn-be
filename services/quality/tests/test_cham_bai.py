"""So suat = so nguoi duoc thuong: n bai DAT nop som nhat (danh sach bai da xep theo luc nop)."""

from app import cham


def _bai(ma: str, dat: bool) -> cham.KetQuaBai:
    return cham.KetQuaBai(ma, "l-" + ma, 10, 9 if dat else 3, 90 if dat else 30, 0, 0, dat, 10)


def test_n_bai_dat_nop_som_nhat_duoc_thuong():
    bai = [_bai("a", True), _bai("b", False), _bai("c", True), _bai("d", True), _bai("e", True)]
    assert cham.chon_duoc_thuong(bai, 3) == {"a", "c", "d"}


def test_it_bai_dat_hon_so_suat_thi_moi_bai_dat_deu_duoc_thuong():
    bai = [_bai("a", False), _bai("b", True)]
    assert cham.chon_duoc_thuong(bai, 3) == {"b"}
    assert cham.chon_duoc_thuong(bai, 0) == set()


def _bai_pt(ma: str, phan_tram: int, dat: bool = False, vang_dung: int = 2, vang_tong: int = 2) -> cham.KetQuaBai:
    return cham.KetQuaBai(ma, "l-" + ma, 10, phan_tram // 10, phan_tram, vang_tong, vang_dung, dat, 10)


def test_khong_bat_lap_suat_thi_bai_chua_dat_khong_duoc_thuong():
    bai = [_bai_pt("a", 70), _bai_pt("b", 75)]
    assert cham.chon_duoc_thuong(bai, 2) == set()


def test_lap_suat_trong_theo_phan_tram_roi_nop_som():
    # a dat; con 2 suat: d (75%) truoc, b va c cung 70% → b nop som hon.
    bai = [_bai_pt("a", 90, dat=True), _bai_pt("b", 70), _bai_pt("c", 70), _bai_pt("d", 75)]
    assert cham.chon_duoc_thuong(bai, 3, 0) == {"a", "d", "b"}


def test_lap_suat_bo_bai_sai_cau_vang_va_duoi_san():
    bai = [_bai_pt("a", 79, vang_dung=1), _bai_pt("b", 40), _bai_pt("c", 60)]
    assert cham.chon_duoc_thuong(bai, 3, 50) == {"c"}


def test_du_bai_dat_thi_khong_lap():
    bai = [_bai_pt("a", 90, dat=True), _bai_pt("b", 89), _bai_pt("c", 95, dat=True)]
    assert cham.chon_duoc_thuong(bai, 2, 0) == {"a", "c"}
