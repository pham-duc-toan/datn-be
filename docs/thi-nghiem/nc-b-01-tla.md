# NC-B-01 — Đặc tả hình thức bất biến tiền bằng TLA+

> Thí nghiệm cho mục **NC-B-01** ([sổ vấn đề](../van-de-can-giai-quyet.md)). Đặc tả: [spec/tla/TienVaDongDuAn.tla](../../spec/tla/TienVaDongDuAn.tla). Chạy: `python spec/tla/chay_tlc.py` (cần Java 11+, tự tải `tla2tools.jar` v1.7.4). CI chạy lại mỗi lần push (job `tla`).

## 1. Câu hỏi

Tiền đi qua năm service (project, task, annotation, ledger, payment) bằng event bất đồng bộ. Mỗi service đã có test riêng, nhưng **lỗi tiền thường nằm ở chỗ các bước của nhiều service xen kẽ nhau**, trong khi hạ tầng giao event lặp lại và sai thứ tự. Test E2E chỉ đi qua vài thứ tự cụ thể. Câu hỏi đặt ra: với **mọi** thứ tự xen kẽ trong một mô hình nhỏ, các bất biến tiền có luôn đúng không?

## 2. Mô hình

**Phạm vi.** Một dự án, các luồng:

- nạp tiền → publish → ký quỹ (đủ / thiếu tiền) → chạy;
- cấp lease → nộp → nhãn;
- duyệt / từ chối → khiếu nại (trong hạn) → phân xử;
- chi tiền nhãn (treo) → giải phóng → rút (tự duyệt / admin duyệt / từ chối; cổng thanh toán thành công / thất bại);
- tạm dừng / chạy tiếp → **đóng dự án** (hỏi task-svc + đóng sổ annotation-svc) → hoàn ký quỹ.

**Quy ước:**

- **Mỗi action là một transaction** trong code. Khoá dòng, `FOR SHARE` / `FOR UPDATE` và advisory lock là những thứ làm cho nó nguyên tử.
- **Hạ tầng được mô hình như hệ thống thật phải chịu.** Event ghi outbox cùng transaction. `msgs` là một **tập**: consumer lấy phần tử bất kỳ, nên thứ tự giữa các queue là tuỳ ý, và có thể lấy lại phần tử đã xử lý, tương ứng với giao lặp do service chết sau commit trước ACK hoặc do dispatcher gửi lại. Consumer có `processed_events`. Handler ném lỗi vĩnh viễn thì message vào DLQ.
- **Kích thước:** 2 labeler (đối xứng), 1 task, trần redundancy 2, đơn giá 1, nạp 1 hoặc 2 (thiếu / đủ ký quỹ), tạm dừng tối đa 2 lần, publish tối đa 2 lần.

**Bất biến** (TLC kiểm ở **mọi** trạng thái đến được):

| Bất biến | Ý nghĩa |
|---|---|
| `BaoToanTien` | Tổng mọi tài khoản = 0: sổ cái ghi kép, tiền không tự sinh ra hay mất đi |
| `KhongSoDuAm` | Không tài khoản doanh nghiệp, ký quỹ, treo, khả dụng hay đang chuyển nào âm |
| `ChiMotLan` | Mỗi nhãn được trả **đúng một lần**, dù event `approved` bị giao lặp (VD-M-02) |
| `KhongVuotRedundancy` | Không task nào được chi quá trần (VD-M-03) |
| `KhongDLQ` | Không event nào làm đổi tiền rơi vào DLQ |
| `KhongNoLabeler` | Ký quỹ đã trả về doanh nghiệp thì **mọi nhãn đã duyệt đều đã được trả** |
| `KhongTuocQuyenKhieuNai` | Đã đóng sổ thì không nhãn bị từ chối nào còn trong hạn khiếu nại |
| `KhongBoSotLuotNop` | Dự án đã kết thúc thì mọi lượt nộp đều đã thành nhãn |
| `DongKyQuySauKhiXong` | Ký quỹ chỉ đóng sau khi dự án Xong |

**Biến thể lỗi.** Mỗi cờ `BUG_*` tắt **một** cơ chế bảo vệ. Mỗi biến thể **phải** bị TLC bắt; nếu không, mô hình hoặc bất biến đã quá lỏng.

## 3. Kết quả

| Biến thể | Kết quả | Bất biến vi phạm | Trạng thái (sinh / phân biệt) | Trace ngắn nhất |
|---|---|---|---|---|
| `dung`: thiết kế sau khi sửa | **không vi phạm** | — | 1.449.643 / 293.889, độ sâu 43 | — |
| `hien_tai`: **code trước NC-B-01** | **vi phạm** | `KhongNoLabeler` | 6.203 / 1.851 | 15 bước |
| `loi_khong_idempotent`: ledger bỏ kiểm "nhãn đã chi" | vi phạm | `ChiMotLan` | 1.005 / 399 | 12 bước |
| `loi_dong_khong_dem_nhan`: đóng không so số nhãn với số lượt nộp | vi phạm | `KhongBoSotLuotNop` | 1.908 / 675 | 13 bước |
| `loi_dong_khong_han_kn`: đóng không chờ hết hạn khiếu nại | vi phạm | `KhongTuocQuyenKhieuNai` | 1.901 / 665 | 13 bước |
| `loi_dong_khi_con_lease`: đóng không cần task-svc đã tạm dừng | vi phạm | `KhongBoSotLuotNop` | 1.539 / 585 | 12 bước |

Trace của biến thể lỗi là trace **ngắn nhất** (TLC duyệt theo chiều rộng, một luồng) và tái lập được.

## 4. Hai lỗi thật TLC tìm ra trong code

### 4.1 Hoàn ký quỹ trước khi chi xong nhãn đã duyệt (`hien_tai`, 15 bước)

```
 10 Giao (task)        taskTT = "TamDung"
 11 Giao (annotation)  nhãn l1: "Cho"
 12 Duyet              nhãn l1: "Duyet"   → event approved vào queue annotation→ledger
 13 GoiDongSo          annotation đóng sổ (không còn nhãn chờ duyệt — l1 đã DUYỆT)
 14 ChotHoanThanh      dự án "Xong"      → event completed vào queue project→ledger
 15 Giao (ledger)      xử lý completed TRƯỚC approved: hoàn toàn bộ ký quỹ, kyQuyTT = "Dong"
                       ⇒ approved tới sau gặp ky_quy_da_dong → DLQ; l1 đã được duyệt nhưng không bao giờ được trả
```

Luồng đóng dự án (phương án A) chỉ kiểm trạng thái **phía annotation-svc**, mà ở đó nhãn đã duyệt rồi nên điều kiện đạt. Nhưng tiền của nhãn đó vẫn **đang trên đường** tới ledger, qua một queue khác với `project.completed`. E2E không bắt được vì thông thường `approved` tới trước vài mili giây.

**Sửa:**

- annotation-svc trả số nhãn đã duyệt khi đóng sổ;
- `project.completed` / `project.cancelled` mang theo `ApprovedAnnotationCount`;
- ledger chưa chi đủ thì chuyển ký quỹ sang **`Closing`**: vẫn chi cho nhãn đã duyệt, không chi cho cổng link nữa, chi đủ thì tự hoàn phần dư và đóng.

Hợp đồng event đổi ngay trên v1 (ngoại lệ thứ tư, ghi trong [CATALOG](../../contracts/events/CATALOG.md)).

**Kiểm chứng trên hệ thống thật** ([tests/e2e/e2e_dong_cho_chi.py](../../tests/e2e/e2e_dong_cho_chi.py)):

1. **Dừng container ledger**, duyệt nhãn, tạm dừng rồi hoàn thành dự án. Hai event nằm chờ trong hai queue.
2. **Bật ledger lại**: thứ tự xử lý giữa hai queue là tuỳ ý.
3. Lặp nhiều vòng. Vòng nào cũng phải có: nhãn được trả, ký quỹ đóng, 7.000 hoàn về, DLQ trống.

Kết quả 3 vòng: **13/13 bước đạt**. Log ledger cho thấy **2/3 vòng ledger nhận `completed` trước `approved`** ("kết thúc nhưng mới chi 0/1 nhãn đã duyệt — giữ ký quỹ cho tới khi chi đủ"), tức đúng kịch bản TLC tìm ra, và vẫn trả đủ. Với code cũ, 2 vòng đó sẽ để labeler không được trả. Bài này nằm trong bộ hồi quy của CI (`chay_hoi_quy.py`).

### 4.2 Nhãn nộp sau khi chạy tiếp bị đẩy vào DLQ (TLC tìm ra khi chạy bản `dung` đầu tiên)

Trình tự: tạm dừng → đóng sổ thành công → **project-svc lưu Completed thất bại** (ví dụ lỗi DB) → doanh nghiệp chạy tiếp → task-svc nhận `resumed` trước annotation-svc → labeler nộp → `assignment.submitted` tới annotation-svc **khi sổ vẫn đang đóng** → code ném lỗi → giao lại 5 lần trong vài mili giây → DLQ. Lượt nộp bị mất.

**Sửa:** đóng sổ chỉ cấm các thao tác làm **đổi tiền** (duyệt, khiếu nại, phân xử). annotation-svc **vẫn ghi nhận** nhãn nộp tới, trừ khi dự án đã **kết thúc hẳn** (nhận `project.completed` / `cancelled`). Nhãn mới ở trạng thái chờ duyệt sẽ tự chặn lần đóng tiếp theo. Bất biến `KhongBoSotLuotNop` được phát biểu lại cho đúng: chỉ ràng buộc khi dự án đã kết thúc. Trong lúc đóng sổ chưa chốt, nhãn đang trên đường là bình thường.

## 5. Diễn giải

1. **Mô hình bắt được lỗi, không phải "đúng vì quá lỏng".** Cả 5 biến thể lỗi đều bị bắt bằng trace 12–15 bước. Trong đó 3 biến thể là lỗi đã từng mắc khi thiết kế (idempotency theo nhãn, đếm nhãn, hạn khiếu nại).
2. **Giá trị lớn nhất là hai lỗi chưa từng thấy.** 456 bước E2E và phép thử ép race đều không bắt được chúng, vì cần một thứ tự event hiếm: `completed` tới ledger trước `approved`, hoặc `submitted` tới annotation trước `resumed`. TLC tìm ra chỉ trong vài giây.
3. **Không gian trạng thái nhỏ nhưng đủ.** 2 labeler và 1 task đã chứa mọi cặp xen kẽ giữa hai nhãn của cùng một task, cùng đủ thứ tự giữa 4 queue. Các lỗi kiểu này thường lộ ra ở mô hình nhỏ (giả thuyết "small scope").

## 6. Giới hạn

- **Mô hình là trừu tượng của code.** Mỗi action được viết tay theo một handler. Lệch giữa mô hình và code (ví dụ handler thật làm thêm một bước) TLC không thấy được. Giảm thiểu: chú thích trong đặc tả trỏ tới từng hàm, và E2E kiểm chứng các kịch bản TLC tìm ra.
- **Chỉ kiểm an toàn (safety), chưa kiểm liveness.** "Mọi nhãn đã duyệt rốt cuộc được trả" được bắt gián tiếp qua `KhongDLQ` và `KhongNoLabeler`, chưa phải công thức `◇` có giả thiết công bằng (fairness).
- **Thu nhỏ:** đơn giá 1, phí nền tảng 0, mỗi labeler một lệnh rút, không mô hình cổng link (sharer, hoa hồng) và redundancy thích ứng. Đó là các luồng tiền khác cần mô hình riêng.
- **Thời gian trừu tượng:** hạn khiếu nại và giải phóng treo là các action "thời gian trôi qua" có thể xảy ra bất kỳ lúc nào.
