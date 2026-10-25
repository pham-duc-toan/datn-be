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
