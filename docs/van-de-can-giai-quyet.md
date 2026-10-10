# Sổ vấn đề cần giải quyết

**Tài liệu nguồn:** [Đặc tả chức năng](../dac-ta-he-thong-gan-nhan-cong-dong.md) · [Kiến trúc backend](kien-truc-backend.md)

Danh sách mọi rủi ro kỹ thuật, bảo mật, pháp lý và sản phẩm đã nhận diện. Dùng làm checklist khi code và làm chương "Rủi ro và giải pháp" trong báo cáo.

**Mức độ:** `CHẶN` = không xử lý thì không được lên production · `CAO` = phải có trước khi bảo vệ · `TB` = nên có

**Thống kê:** **110 vấn đề** (mục A–L, N) — 32 CHẶN · 58 CAO · 20 TB · kèm **29 hướng nâng cao** (mục M)

---

## A. Tiền và sổ cái — `VD-M`

| Mã | Vấn đề | Hậu quả | Hướng giải | Mức | Phase |
|---|---|---|---|---|---|
| M-01 | Double-spend escrow: publish 2 dự án song song, cả hai cùng đọc số dư trước khi ai ghi | Escrow vượt số dư thật | `UPDATE ... SET balance = balance - :amt WHERE balance >= :amt`, kiểm `rowcount` | CHẶN | P1 |
| M-02 | Trả trùng cho cùng một nhãn (event at-least-once, hoặc duyệt → hủy → duyệt lại) | Labeler nhận tiền 2 lần | Hai lớp: `processed_events(event_id)` **và** `UNIQUE(annotation_id, entry_type)` | CHẶN | P1 |
| M-03 | Chi vượt số redundancy đã ký quỹ (do mất lease, 5 người làm task chỉ ký quỹ cho 3) | Escrow âm | Đếm payout theo `task_id` **trong cùng transaction** của ledger | CHẶN | P1 |
| M-04 | Sổ cái bị `UPDATE`/`DELETE` | Mất toàn bộ khả năng kiểm toán | `REVOKE UPDATE, DELETE` — chỉ `INSERT`; sửa sai bằng bút toán đảo; chuỗi băm phát hiện can thiệp | CHẶN | P1 |
| M-05 | Số tiền âm ở **biên API** biến ghi nợ thành ghi có | Rút tiền trái phép | Hai tầng khác nhau, đừng lẫn: **biên API** validate `amount > 0` (số tiền người dùng xin luôn dương); **`journal_lines`** dùng số **có dấu** với `CHECK (amount <> 0)` và bất biến `SUM = 0` theo từng `journal_entry` (dòng nguồn âm, dòng đích dương). Đặt `CHECK (amount > 0)` lên `journal_lines` sẽ phá chính phép ghi kép | CHẶN | P1 |
| M-06 | Đua giữa job giải phóng hold và mở tranh chấp | Tiền được rút trước khi có phán quyết | `AND NOT EXISTS (SELECT 1 FROM disputes WHERE hold_id=:id AND state='open')` trong câu UPDATE | CAO | P1 |
| M-07 | Lệch tiền do làm tròn khi chia thù lao / trừ phí | `SUM(journal_entry) ≠ 0` — sổ cái vỡ | Tiền là **số nguyên đồng**; phần dư gán tường minh bằng phương pháp phần dư lớn nhất | CAO | P1 |
| M-08 | Nghẽn dòng nóng: 1.000 lượt vượt link/giây cùng ghi nợ một tài khoản escrow | Toàn bộ ledger tắc | Gộp lô CPM trong Redis, 60s ghi một bút toán tổng | CAO | P2 |
| M-09 | Gọi VNPay bên trong transaction DB | Cạn connection pool, khóa dòng 30s. Chết giữa chừng thì DB rollback nhưng **VNPay không rollback** — hai hệ lệch nhau vĩnh viễn | Ba pha: ghi ý định → **commit** → gọi ngoài (không transaction nào mở) → transaction mới ghi kết quả. **Job đối soát quét giao dịch `pending` quá 15 phút, hỏi ngược cổng về trạng thái rồi chốt** | CAO | P1 |
| M-10 | Hoàn tiền khi hủy dự án tính sai phần đã dùng | Tranh chấp với doanh nghiệp | Tính từ journal thực tế, không từ cột đếm rời | CAO | P1 |
| M-11 | Khấu trừ 10% TNCN với chi trả cá nhân từ 2 triệu | Sai luật thuế | Ngưỡng tính theo từng lần chi; xuất chứng từ khấu trừ | CAO | P1 |
| M-12 | Escrow kẹt vĩnh viễn khi doanh nghiệp bỏ dự án giữa chừng | Tiền treo không ai lấy được | Timeout dự án → tự động kết thúc và hoàn phần chưa dùng | CAO | P1 |
| M-13 | Đối soát cuối ngày chạy khi còn giao dịch đang bay | Báo lệch giả, mất công điều tra | Đối soát theo số thứ tự bút toán trên snapshot nhất quán, không theo giờ treo tường | TB | P1 |
| M-14 | Xuất hóa đơn VAT cho doanh nghiệp | Doanh nghiệp không hạch toán được chi phí | Tích hợp hóa đơn điện tử, hoặc export dữ liệu cho kế toán | TB | P6 |
| M-15 | **Mô hình phí nền tảng chưa chốt**: cộng thêm vào ký quỹ (`× (1 + phí)` theo §2.11) hay trừ vào thù lao labeler? | Ký quỹ tính sai ngay từ lúc publish; `SUM = 0` không cân; doanh nghiệp và labeler hiểu khác nhau về "đơn giá 200đ" | Chốt theo §2.11: phí **cộng thêm**. Đơn giá 200.000 + phí 30% → ký quỹ **260.000/nhãn**; chi `−260.000 escrow → +200.000 labeler +60.000 platform`. Hiển thị rõ cả hai con số cho cả hai phía | CHẶN | P1 |

---

## B. Dữ liệu phân tán và đồng thời — `VD-D`

| Mã | Vấn đề | Hậu quả | Hướng giải | Mức | Phase |
|---|---|---|---|---|---|
| D-01 | Service chết giữa `SaveChanges()` và `Publish()` | Nhãn đã duyệt nhưng ledger không bao giờ biết → không trả tiền, **không ai phát hiện** | Outbox: ghi event vào bảng cùng transaction nghiệp vụ; dispatcher riêng đẩy sang bus | CHẶN | P0 |
| D-02 | RabbitMQ at-least-once, event đến nhiều lần | Ghi trùng ở mọi consumer | `processed_events(event_id PK, handler)` bắt buộc ở **mọi** consumer | CHẶN | P0 |
| D-03 | Không quan sát được luồng xuyên 17 service | Không debug được, không đo được | `correlationId` sinh ở gateway, xuyên mọi hop và mọi event; OTel → Jaeger **từ P0** | CHẶN | P0 |
| D-04 | Bản sao read-only trễ (`labeler_cache`) | Cấp task cho người vừa bị khóa | Chấp nhận trễ ở bước lọc; kiểm tra lại ở bước quan trọng; luôn fail-safe về phía từ chối | CAO | P0 |
| D-05 | Event đến sai thứ tự (`reputation.changed` cũ ghi đè bản mới) | Dữ liệu lùi về quá khứ | So sánh `occurredAt`/version trước khi ghi; bỏ qua bản cũ hơn | CAO | P0 |
| D-06 | Poison message làm nghẽn hàng đợi ledger | **Mọi thanh toán trong hệ thống dừng** | DLQ + retry có giới hạn + cảnh báo; không bao giờ drop im lặng | CAO | P1 |
| D-07 | Saga treo giữa chừng (chờ escrow mãi không tới) | Dự án kẹt trạng thái trung gian | Timeout cho mọi bước saga + bù trừ tự động | CAO | P1 |
| D-08 | Đổi schema event làm vỡ consumer | Service khác crash hàng loạt | `version` trong envelope; phát song song 2 bản; xóa bản cũ khi consumer cuối đã chuyển | CAO | P1 |
| D-09 | Lệch đồng hồ giữa các service | Hold hết hạn sai thời điểm | Dùng thời gian của DB (`now()`), không dùng thời gian ứng dụng | TB | P1 |
| D-10 | Không có transaction xuyên service | Dữ liệu mồ côi khi một bên lỗi | Saga + bù trừ; chấp nhận eventual consistency có chủ đích | TB | P0 |
| D-11 | **Chuyển trạng thái không có optimistic concurrency**: đọc lại trạng thái → kiểm hợp lệ → ghi. Dưới READ COMMITTED, hai người cùng đọc được trạng thái cũ trước khi ai commit. Đọc lại chỉ làm cửa sổ **hẹp hơn**, không làm nó biến mất | Hook chạy **hai lần**: publish dự án phát `project.publish_requested` 2 lần → ký quỹ 2 lần; duyệt lệnh rút chi 2 lần | Điều kiện phải nằm **trong** câu UPDATE: `UPDATE ... SET state=:new WHERE id=:id AND state=:expected`, kiểm `rowcount = 0`. Hook chạy trong **cùng transaction** hoặc qua outbox, không chạy sau khi đã commit | CAO | P0 |
| D-12 | **Hợp đồng payload nghiêm ngặt** (`UnmappedMemberHandling = Disallow`): thêm một trường vào payload là breaking change với mọi consumer chưa deploy lại. Đã kiểm chứng bằng thực nghiệm — consumer cũ ném `EventContractException` ngay khi gặp trường mới | Deploy cuốn chiếu và canary (`NC-A-04`) không dùng được — canary nghĩa là bản cũ và bản mới chạy **song song**, bản cũ sẽ đẩy toàn bộ event vào DLQ. Dòng tiền đứng mà mọi health check vẫn xanh | **Quyết định: GIỮ nghiêm ngặt.** Ở quy mô đồ án mọi service deploy cùng lúc, nên cái giá gần bằng 0 còn lợi ích bắt trôi dạt hợp đồng là thật. **Điều kiện bắt buộc đi kèm: cảnh báo độ sâu DLQ** — `Disallow` tạo ra lỗi chứ không làm ai biết về nó. Xét lại khi làm `NC-A-04`: hoặc chuyển payload sang tolerant reader (`[JsonUnmappedMemberHandling]` chỉ gắn lên envelope), hoặc canary chỉ áp cho service không consume event | CAO | P1 |

---

## C. Phân phối task — `VD-T`

| Mã | Vấn đề | Hậu quả | Hướng giải | Mức | Phase |
|---|---|---|---|---|---|
| T-01 | Submit sau khi lease hết hạn, task đã sang người khác | Ghi đè nhãn của người khác | Lua script atomic: `GET lease == labelerId ? DEL : reject` | CAO | P0 |
| T-02 | Redis restart mất sạch lease | 2 người cùng làm 1 task | Reaper dựng lại từ Postgres; ledger chặn chi vượt (M-03) | CAO | P0 |
| T-03 | Đói task: labeler mới không đủ uy tín nên không bao giờ nhận được việc | Người mới bỏ nền tảng, không có nguồn cung | Dành hạn ngạch task cho người mới; bài test đầu vào cấp uy tín khởi điểm | CAO | P1 |
| T-04 | Cùng một người nhận lại task đã làm qua tài khoản khác | Phá redundancy, đồng thuận giả | Fingerprint thiết bị + cụm hóa (xem Q-02) | CAO | P3 |
| T-05 | Task khó bị bỏ qua liên tục, tồn đọng mãi | Dự án không bao giờ hoàn thành | Tăng đơn giá theo số lần bị bỏ; sau N lần đẩy lên reviewer cấp cao | TB | P3 |
| T-06 | Nhiều labeler tranh cùng một task | Tranh chấp khóa, cấp task chậm | `FOR UPDATE SKIP LOCKED` + offset ngẫu nhiên trong tập ứng viên | TB | P0 |
| T-07 | Hàng đợi hàng triệu task làm chậm truy vấn lọc eligibility | Cấp task chậm dần theo thời gian | Index phủ + partition theo `project_id` | TB | P2 |

---

## D. Chất lượng nhãn — `VD-Q`

| Mã | Vấn đề | Hậu quả | Hướng giải | Mức | Phase |
|---|---|---|---|---|---|
| Q-01 | **Rò đáp án câu vàng** ra client (trong JSON, JS bundle, hoặc thông báo lỗi chi tiết) | Sụp đổ toàn bộ mô hình chống bot — bot giải đúng 100%, CPM không về 0 | Chấm điểm hoàn toàn server-side; response chỉ `pass`/`fail`, **không nói câu nào sai** | CHẶN | P2 |
| Q-02 | **Sybil phá majority vote**: một người 5 tài khoản, cùng gán một nhãn sai | Đồng thuận tuyệt đối trên rác; doanh nghiệp trả tiền cho rác; uy tín cả 5 tài khoản đều tăng | Cụm hóa theo **phương thức rút tiền** (nút thắt khó ngụy trang nhất) + fingerprint + private pool cho dự án giá trị cao | CHẶN | P3 |
| Q-03 | Câu vàng bị nhận diện do khác biệt kỹ thuật (đường dẫn ảnh, thứ tự, kích thước) | Vô hiệu hóa toàn bộ cơ chế giám sát | Câu vàng lấy từ chính dataset, phục vụ qua cùng đường, trộn ngẫu nhiên vị trí và tỉ lệ k/m | CHẶN | P2 |
| Q-04 | Cold start điểm uy tín cho người mới | Không nhận được dự án tốt, không tích lũy được uy tín | Bài test đầu vào cấp uy tín khởi điểm (FB-16) | CAO | P1 |
| Q-05 | Guideline mơ hồ → nhãn kém → đổ lỗi cho labeler | Tranh chấp hàng loạt, mất niềm tin hai phía | LLM kiểm guideline (FA-05) + bắt buộc ví dụ đúng/sai trước khi publish | CAO | P4 |
| Q-06 | Dùng chung một chỉ số đồng thuận cho mọi loại bài toán | Kết luận chất lượng sai | Kappa cho phân loại, IoU matching cho bbox, STAPLE cho segmentation — tách rõ | CAO | P3 |
| Q-07 | Dawid–Skene cần đủ dữ liệu mới hội tụ | Ước lượng độ tin cậy sai ở giai đoạn đầu | Dùng câu vàng làm mồi; chỉ bật DS khi mỗi labeler đủ n mẫu | CAO | P3 |
| Q-08 | Redundancy thích ứng tăng vô hạn với mẫu thật sự mơ hồ | Đốt hết ngân sách vào vài mẫu | Trần redundancy; vượt trần thì đánh dấu "không quyết được" và trả về doanh nghiệp | CAO | P3 |
| Q-09 | Nhãn chất lượng thấp từ cổng link trộn vào tập chính | Dữ liệu bẩn, doanh nghiệp mất niềm tin | Thuộc tính `source` trên mọi nhãn; doanh nghiệp tự chọn chấp nhận nguồn nào | TB | P2 |

---

## E. Cổng link rút gọn — `VD-L`

| Mã | Vấn đề | Hậu quả | Hướng giải | Mức | Phase |
|---|---|---|---|---|---|
| L-01 | Link đích chứa mã độc / cờ bạc / phishing / vi phạm bản quyền | **Trách nhiệm pháp lý của nền tảng** — rủi ro lớn nhất của mô hình này ở Việt Nam | Quét Safe Browsing + VirusTotal khi tạo; blacklist domain; nút báo cáo; hàng đợi kiểm duyệt; tự vô hiệu hóa và giữ doanh thu khi xác nhận | CHẶN | P2 |
| L-02 | Replay token vượt link | Doanh thu giả không giới hạn | `jti` dùng một lần trong Redis, TTL 3 phút, gắn `linkId` + domain | CHẶN | P2 |
| L-03 | Cạn escrow dự án giữa lúc traffic cao | Chi vượt ngân sách doanh nghiệp | Kiểm tra escrow **trước khi** phục vụ câu hỏi; cache trạng thái dự án; dừng phục vụ khi cạn | CAO | P2 |
| L-04 | Tự vượt link của chính mình (đăng xuất, ẩn danh, đổi IP) | Sharer rút tiền từ chính ngân sách nền tảng | Fingerprint thiết bị + tương quan thời gian (link vừa tạo đã có lượt vượt) + chặn `userId`/IP | CAO | P2 |
| L-05 | Vòng lặp referral bằng tài khoản clone tự mời nhau | Mất 10% doanh thu vào hư không | Chỉ trả hoa hồng sau khi người được mời tạo doanh thu thật vượt ngưỡng; cụm hóa hai bên | CAO | P2 |
| L-06 | Bot rẻ tiền tiêu tốn câu hỏi thật trước khi bị loại | Cạn tập câu thật, tốn tài nguyên | Turnstile đặt **trước** lớp gán nhãn — bot chết trước khi chạm task nào | CAO | P2 |
| L-07 | Traffic giả: spike đột ngột, referrer giả, dải IP tập trung | Doanh thu ảo, mất tiền nền tảng | Phát hiện bất thường + giữ doanh thu chờ xác minh thủ công | CAO | P2 |
| L-08 | Khách vãng lai không có danh tính, không truy trách nhiệm được | Không thể trừng phạt hành vi xấu | Chỉ dùng kênh này cho nhãn giá rẻ; mọi nhãn luôn qua sàng lọc câu vàng | TB | P2 |

---

## F. Cộng tác realtime — `VD-R`

| Mã | Vấn đề | Hậu quả | Hướng giải | Mức | Phase |
|---|---|---|---|---|---|
| R-01 | **Bias neo**: thấy nhau làm mất tính độc lập | Không dùng majority vote được nữa — người vào sau bắt chước người vào trước | Tách hoàn toàn khỏi luồng đồng thuận; chỉ dùng cho mẫu nhiều đối tượng (vệ tinh, y tế, segmentation phức tạp) | CHẶN | P5 |
| R-02 | Đo đóng góp bị gian lận (tạo/xóa object liên tục để tăng số đếm) | Chia tiền sai, người làm thật thiệt | Chỉ tính object **hợp lệ còn lại cuối phiên**; chống spam thao tác; kết hợp thời gian thao tác thực | CAO | P5 |
| R-03 | Leader lạm quyền điều chỉnh ±20% | Bất công, không ai muốn làm member | Bắt buộc nhập lý do; ghi log mọi điều chỉnh; cho phép khiếu nại | CAO | P5 |
| R-04 | Hai người sửa cùng một object đồng thời | Mất dữ liệu | Yjs CRDT + khóa mềm object đang được chọn | CAO | P5 |
| R-05 | WebSocket cần sticky session khi scale nhiều instance | Mất phòng, mất trạng thái | y-redis adapter chia sẻ trạng thái giữa các instance | CAO | P5 |
| R-06 | Hàng đợi offline đồng bộ ngược thứ tự khi có mạng lại | Nhãn sai lệch | CRDT tự hòa giải; snapshot định kỳ + rollback phiên bản khi cần | TB | P5 |

---

## G. AI hỗ trợ — `VD-A`

| Mã | Vấn đề | Hậu quả | Hướng giải | Mức | Phase |
|---|---|---|---|---|---|
| A-01 | **Automation bias**: labeler bấm chấp nhận gợi ý hàng loạt không xem | Nhãn thu được = output của model → **vô giá trị**, doanh nghiệp trả tiền cho chính model của mình | Trộn câu vàng vào phần có pre-label; đo tỉ lệ sửa của từng labeler; tỉ lệ sửa ≈ 0 là cờ đỏ | CHẶN | P4 |
| A-02 | Model train từ chính nhãn nó sinh ra | Trôi dạt tích lũy, sai lệch tự khuếch đại | Chỉ train từ nhãn **đã được người duyệt**, không bao giờ từ pre-label chưa xác nhận | CHẶN | P4 |
| A-03 | Active learning tạo tập huấn luyện lệch phân phối | Model cuối của doanh nghiệp bị lệch | Giữ một phần mẫu chọn ngẫu nhiên làm đối chứng | CAO | P4 |
| A-04 | `ml-svc` sập hoặc GPU không sẵn | Không được phép chặn luồng gán nhãn chính | Gợi ý là tùy chọn; degrade im lặng; ranh giới cứng: ML không bao giờ ghi nhãn cuối | CAO | P4 |
| A-05 | Chi phí GPU vượt khả năng đồ án | Không chạy được | Model nhỏ, chạy CPU khi cần; SAM để sau cùng, cắt được nếu thiếu thời gian | TB | P4 |

---

## H. Xác thực, phân quyền, bảo mật — `VD-S`

| Mã | Vấn đề | Hậu quả | Hướng giải | Mức | Phase |
|---|---|---|---|---|---|
| S-01 | **Ghi tiền dựa trên Return URL** của cổng thanh toán | Ai cũng nạp tiền vô hạn bằng cách gõ một URL | Chỉ ghi qua **IPN** (server-to-server). Tuyệt đối không từ Return URL | CHẶN | P1 |
| S-02 | Webhook thanh toán giả mạo hoặc bị sửa số tiền | Nạp tiền khống | Verify checksum bằng secret + **gọi ngược API truy vấn để xác nhận số tiền** + `UNIQUE(provider, provider_txn_id)` + đối chiếu với `payment_intent`. Kiểm "đã xử lý chưa" cũng phải atomic: `UPDATE ... SET state='completed' WHERE id=:id AND state='pending'`, không phải `if (state != 'pending')` | CHẶN | P1 |
| S-03 | IDOR: lấy `userId` từ đường dẫn thay vì từ token | Đọc ví / nhãn / hồ sơ của bất kỳ ai | Service sở hữu **luôn** lấy danh tính từ JWT `sub`; path chỉ để so khớp, lệch thì 403 | CHẶN | P0 |
| S-04 | BFF gọi service bằng service account đặc quyền | Một lỗi logic ở BFF = lộ toàn bộ dữ liệu tiền của mọi người | BFF chuyển tiếp nguyên token người dùng; BFF không có đặc quyền gì | CHẶN | P1 |
| S-05 | JWT: alg confusion, thiếu kiểm `aud`/`iss`, TTL quá dài | Chiếm phiên | Cố định thuật toán; kiểm `aud`/`iss`; access 15 phút + refresh rotation | CHẶN | P0 |
| S-06 | Service nội bộ phơi ra Internet, bỏ qua gateway | Nộp nhãn giả, đọc dữ liệu trực tiếp | Mạng private; chỉ gateway public; mTLS giữa các service nếu làm được | CHẶN | P0 |
| S-15 | Mọi container nằm chung một Docker network, nên datastore của service này vẫn phân giải tên và mở cổng được từ container của service khác. Cô lập hiện tại là **bằng credential, không phải bằng mạng** — đã kiểm chứng bằng thực nghiệm | Rò một connection string là chạm được thẳng vào database của service khác, không cần đi qua service đó | Chấp nhận ở môi trường phát triển (chuẩn mực công nghiệp; mật khẩu chỉ nằm trong biến môi trường của đúng service sở hữu). Khi lên Kubernetes ở `NC-A-01`: dùng **NetworkPolicy** để mỗi datastore chỉ nhận kết nối từ đúng service của nó. Docker Compose không làm được việc này một cách gọn gàng | TB | P6 |
| S-07 | Signed URL TTL dài hoặc bucket public chứa KYB/CCCD | Lộ dữ liệu định danh | Bucket private tuyệt đối; signed URL TTL ≤ 5 phút, gắn `userId`; mọi truy cập ghi audit | CHẶN | P0 |
| S-08 | Secret nằm trong compose / commit vào repo | Lộ credential toàn hệ thống | Secret manager; `.env` không commit; đổi toàn bộ mật khẩu dev trước khi lên thật | CHẶN | P0 |
| S-09 | Chiếm tài khoản → đổi số tài khoản ngân hàng → rút sạch | Mất tiền người dùng | Khóa rút 48h sau khi đổi phương thức rút; bắt buộc 2FA; cảnh báo về email cũ | CAO | P1 |
| S-10 | Lạm quyền admin (cấu hình phí + xử tranh chấp + duyệt rút) | Rút tiền khỏi hệ thống một cách hợp lệ | Dual control cho lệnh rút vượt ngưỡng; audit log append-only mà admin không xóa được; bắt buộc nhập lý do | CAO | P1 |
| S-11 | Không có cơ chế thu hồi token | Khóa tài khoản nhưng token cũ vẫn dùng được 15 phút | Danh sách thu hồi trong Redis, TTL bằng TTL access token | CAO | P1 |
| S-12 | Thiếu rate limit ở endpoint tiền và OTP | Brute force, dò tài khoản | Giới hạn theo user + IP + endpoint, chặt nhất ở nhóm tiền | CAO | P1 |
| S-13 | Phân biệt 404 và 403 làm lộ sự tồn tại tài nguyên | Dò danh sách người dùng, dự án | Trả về cùng một mã cho "không tồn tại" và "không có quyền" | TB | P1 |
| S-14 | **BOLA — thiếu phân quyền chiều ngang.** `S-03` chỉ đảm bảo lấy đúng danh tính từ JWT; nó **không** trả lời được câu hỏi *user này có quyền đụng bản ghi này không*. RBAC thuần chỉ giải quyết chiều dọc ("được làm LOẠI việc này?"), không giải quyết chiều ngang ("được đụng BẢN GHI này?") | Labeler A đọc được nhãn, task, ảnh của dự án mà A không tham gia. **OWASP API Security Top 10 hạng 1** (API1:2023). Phá luôn FP-05 private pool, FB-23 chặn labeler, FL-04, FB-21, FP-06 | Bảng `project_members(project_id, user_id, role, state)` ở `project_db`; bản sao read-only ở `task_db` / `annotation_db` / `media_db` qua event. **Một predicate DÙNG CHUNG cho cả kiểm-một-bản-ghi lẫn lọc-danh-sách** — viết hai bản luật riêng thì chúng sẽ lệch nhau (danh sách hiện ra item mà bấm vào báo 403, hoặc ngược lại) | CHẶN | P0 |

---

## I. Riêng tư và bảo vệ dữ liệu — `VD-P`

| Mã | Vấn đề | Hậu quả | Hướng giải | Mức | Phase |
|---|---|---|---|---|---|
| P-01 | Rò ảnh gốc của doanh nghiệp ra ngoài | Vi phạm NDA, mất khách hàng lớn | Stream qua proxy + watermark chìm mang `labelerId`; chặn tải trực tiếp; client không bao giờ biết key trên object storage | CAO | P6 |
| P-02 | PII trong dataset: khuôn mặt, biển số, số CCCD | Vi phạm quy định dữ liệu cá nhân | Blur tự động lúc ingest, sinh bản derived; bản gốc không ai chạm | CAO | P6 |
| P-03 | Không truy được ai đã xem mẫu nào | Không điều tra được khi có rò rỉ | Audit log append-only: ai, mẫu nào, lúc nào, bao lâu (FP-06) | CAO | P6 |
| P-04 | Nội dung nhạy cảm hiển thị cho tài khoản chưa xác minh tuổi | Rủi ro pháp lý và đạo đức | Xác minh 18+; chế độ mờ mặc định; người dùng chủ động bỏ mờ | CAO | P6 |
| P-05 | Phơi nhiễm nội dung độc hại kéo dài | Tổn hại sức khỏe tâm lý labeler | Cảnh báo trước + đồng ý rõ ràng; giới hạn thời gian liên tục; ép nghỉ giữa phiên; cho rời bất kỳ lúc nào không bị phạt | CAO | P6 |
| P-06 | NDA điện tử có giá trị pháp lý đến đâu | Tranh chấp không xử được | Ký điện tử + lưu bằng chứng thời điểm và nội dung phiên bản đã ký | TB | P6 |

---

## J. Vận hành — `VD-O`

| Mã | Vấn đề | Hậu quả | Hướng giải | Mức | Phase |
|---|---|---|---|---|---|
| O-01 | Không quan sát được hệ 17 service | Không debug, không đo, không bảo vệ được trước hội đồng | OTel + Jaeger từ P0; `correlationId` bắt buộc; log JSON tập trung | CHẶN | P0 |
| O-02 | ~24 container hạ tầng + ~25 container ứng dụng trên một máy cá nhân | Máy không chạy nổi, không dev được | Compose profile theo phase; chỉ bật datastore của phase đang làm | CAO | P0 |
| O-03 | Migration 15 datastore không đồng bộ | Lỗi lúc chạy, khó lần ra | Mỗi service tự migrate lúc khởi động; kiểm version schema trước khi phục vụ | CAO | P0 |
| O-04 | Backup 15 datastore riêng biệt | Mất dữ liệu không khôi phục được | Script backup theo service; `ledger_db` ưu tiên cao nhất, tần suất dày nhất | CAO | P1 |
| O-05 | Log phân tán ở 17 nơi | Không tra cứu được sự cố | Log tập trung, format JSON, luôn kèm `correlationId` | CAO | P1 |
| O-06 | Chưa từng chạy full profile trước ngày bảo vệ | Vỡ trận lúc demo | Ít nhất một lần chạy đầy đủ + kịch bản demo có sẵn dữ liệu mẫu | CAO | P6 |
| O-07 | Khởi động chậm do phụ thuộc chéo | Dev khó chịu, CI chậm | Healthcheck + `depends_on: service_healthy` + retry kết nối có backoff | TB | P0 |

---

## K. Pháp lý và tuân thủ — `VD-G`

| Mã | Vấn đề | Hậu quả | Hướng giải | Mức | Phase |
|---|---|---|---|---|---|
| G-01 | Trách nhiệm của nền tảng với link đích vi phạm | Rủi ro pháp lý lớn nhất của mô hình rút gọn link tại Việt Nam | Quét tự động + kiểm duyệt thủ công + gỡ nhanh + lưu bằng chứng đã xử lý | CHẶN | P2 |
| G-02 | Khấu trừ và kê khai thuế TNCN | Vi phạm luật thuế | Khấu trừ 10% với chi trả từ 2 triệu; xuất chứng từ khấu trừ | CAO | P1 |
| G-03 | Dữ liệu cá nhân người dùng (Nghị định 13/2023/NĐ-CP) | Vi phạm quy định bảo vệ dữ liệu cá nhân | Thu thập tối thiểu; ghi rõ mục đích; có cơ chế xóa theo yêu cầu | CAO | P6 |
| G-04 | Quan hệ pháp lý với labeler: người lao động hay đối tác? | Rủi ro về nghĩa vụ bảo hiểm, hợp đồng lao động | Điều khoản dịch vụ rõ ràng; không cam kết giờ làm, không quản lý theo ca | TB | P6 |
| G-05 | Hóa đơn VAT đầu ra cho doanh nghiệp | Doanh nghiệp không hạch toán được | Hóa đơn điện tử hoặc export dữ liệu chuẩn cho kế toán | TB | P6 |

---

## L. Rủi ro sản phẩm — `VD-X`

| Mã | Vấn đề | Hậu quả | Hướng giải | Mức | Phase |
|---|---|---|---|---|---|
| X-01 | **Cold start hai chiều**: không có labeler thì không doanh nghiệp nào đăng dự án, và ngược lại | Nền tảng chết ngay từ đầu | Seed bằng dự án nội bộ; ưu tiên cổng link vì tạo sản lượng sớm mà không cần tuyển labeler | CAO | — |
| X-02 | Doanh nghiệp từ chối nhãn hàng loạt để không phải trả tiền | Labeler bị quỵt, bỏ nền tảng | Tỉ lệ từ chối bất thường → cảnh báo admin; bắt buộc nhập lý do từng nhãn; labeler khiếu nại được | CAO | P1 |
| X-03 | Doanh nghiệp không nghiệm thu, để tiền treo vô thời hạn | Labeler chờ mãi không được trả | Tự động duyệt sau N ngày không phản hồi | CAO | P1 |
| X-04 | Đơn giá quá thấp, labeler không đủ sống | Không có nguồn cung lao động | Đơn giá tối thiểu do admin cấu hình (FM-04) | CAO | P1 |
| X-05 | Ba kênh thu thập có chất lượng rất khác nhau bị trộn lẫn | Doanh nghiệp mất niềm tin vào toàn bộ dữ liệu | Thuộc tính `source` bắt buộc; doanh nghiệp chọn kênh chấp nhận ngay từ lúc tạo dự án | TB | P2 |

---

## M. Hướng nâng cao — `NC`

> Mục này dùng **cột khác** với phần trên: đây không phải rủi ro cần vá mà là hướng mở rộng cần chọn.
>
> **Luật duy nhất:** mỗi hạng mục phải đẻ ra **một artifact đo được**. *"Tôi đã dựng Kubernetes"* không phải kết quả. *"Canary tự rollback khi p99 vượt ngưỡng, đây là log lần rollback thật"* mới là kết quả.
>
> **Lõi bắt buộc giữ nguyên:** P0→P3 (gán nhãn + tiền + cổng link + chất lượng). Mọi hạng mục dưới đây là lớp phủ. Cháy tiến độ thì cắt lớp phủ, không cắt lõi.

### M.1. Nhánh A — DevOps · `NC-A`

| Mã | Hạng mục | Công cụ | Artifact phải tạo ra | Giá trị | Công sức |
|---|---|---|---|---|---|
| A-01 | Orchestration | k3s hoặc kind + Helm | 17 chart, một lệnh dựng cả hệ thống | ⭐⭐ | TB |
| A-02 | GitOps | ArgoCD | Commit → cluster tự đồng bộ; ảnh chụp drift detection | ⭐⭐ | Thấp |
| A-03 | CI/CD | GitHub Actions | Đo lead time từ commit tới production | ⭐⭐ | TB |
| A-04 | **Progressive delivery** | Argo Rollouts hoặc Flagger | **Canary 10% tự rollback khi p99 vượt ngưỡng — video lần rollback thật** | ⭐⭐⭐ | TB |
| A-05 | Service mesh | **Linkerd** (không Istio — phức tạp gấp 5, lợi ích thêm ~0 ở quy mô này) | mTLS tự động giữa 17 service, golden metrics, retry budget | ⭐⭐ | TB |
| A-06 | Observability | Prometheus + Grafana + Loki + Tempo | Dashboard RED/USE từng service; trace xuyên 17 hop | ⭐⭐⭐ | TB |
| A-07 | **SLO & error budget** | Sloth | "SLO 99,5% cho `POST /tasks/next`, error budget tháng còn 63%" | ⭐⭐⭐ | Thấp |
| A-08 | Secrets | External Secrets + Vault (hoặc Sealed Secrets) | Không còn secret nào trong repo — **đóng `VD-S-08`** | ⭐⭐ | TB |
| A-09 | Policy as code | Kyverno | Chặn deploy container chạy root / thiếu resource limit / image không chữ ký | ⭐⭐ | Thấp |
| A-10 | **Supply chain security** | Syft (SBOM) + Trivy + **Cosign** + SLSA provenance | Mọi image có SBOM và chữ ký; cluster từ chối image không ký | ⭐⭐⭐ | TB |
| A-11 | Chaos tự động | Chaos Mesh | Kịch bản giết pod / phân mạng chạy trong CI | ⭐⭐⭐ | TB |
| A-12 | Load test trong cluster | k6 Operator | Biểu đồ throughput / p99 đẩy thẳng vào Grafana | ⭐⭐⭐ | Thấp |
| A-13 | eBPF observability | Cilium Hubble | Bản đồ lưu lượng 17 service ở tầng kernel, không sửa code | ⭐⭐ | TB |

`A-04`, `A-07`, `A-10` là ba thứ tạo khác biệt — đúng những gì ngành đang tuyển và rất ít đồ án chạm tới.

### M.2. Nhánh B — Tính đúng đắn và độ tin cậy · `NC-B`

| Mã | Hạng mục | Công cụ | Artifact phải tạo ra | Giá trị | Công sức |
|---|---|---|---|---|---|
| B-01 | **Đặc tả hình thức cho bất biến tiền** | TLA+ / TLC | Spec ~200 dòng mô hình hóa publish / duyệt nhãn / release hold / rút tiền chạy xen kẽ. Báo cáo số trạng thái đã duyệt, **và trace vi phạm nếu tìm được** | ⭐⭐⭐ | Cao |
| B-02 | Bản nhẹ của B-01 | FsCheck / Hypothesis | Property-based test với harness chạy đồng thời — phương án dự phòng nếu TLA+ trễ | ⭐⭐ | Thấp |
| B-03 | **Kiểm thử kiểu Jepsen cho lease manager** | Toxiproxy + Elle/Knossos | So Redis lease vs etcd lease: **số lần cấp trùng lease khi phân mạng**, kèm chi phí độ trễ. Một chương CAP có số liệu từ hệ thống của chính mình | ⭐⭐⭐ | Cao |
| B-04 | **Deterministic simulation testing** | Tự cài, **chỉ cho `ledger-svc`** | Thời gian/mạng/đĩa/RNG mô phỏng có seed → bug tái hiện 100% theo seed. Thứ cao cấp nhất danh sách này | ⭐⭐⭐ | Rất cao |
| B-05 | Chaos có kịch bản | Toxiproxy / Chaos Mesh | Bảng 5 kịch bản (giết ledger giữa saga, tắt RabbitMQ 60s, `FLUSHALL` redis-task, ngắt mạng annotation↔task, giết payment giữa payout) + chứng minh bất biến giữ nguyên | ⭐⭐⭐ | TB |
| B-06 | **Thí nghiệm hiệu năng có giả thuyết** | k6 | Đường cong A (ghi CPM trực tiếp) vs B (gộp lô 60s): điểm bão hòa, p99, lock wait. **Chứng minh `VD-M-08` bằng số**. **Đã làm** ([báo cáo](thi-nghiem/nc-b-06-hieu-nang.md)): perClick ~30/s không tăng theo instance, batched ~70/s → ~170/s với 3 instance; kèm đo lấy task (0 cấp trùng / 46.804 lượt, sửa câu lấy task tăng thông lượng ~2 lần) | ⭐⭐⭐ | Thấp |
| B-07 | Chính thức hóa ledger thành Event Sourcing | — | Số dư là projection có snapshot; truy vấn số dư tại thời điểm T. Kiến trúc đã đi 80%, chỉ cần đặt tên và bổ sung | ⭐⭐ | Thấp |

### M.3. Nhánh C — Blockchain, phần không phải gimmick · `NC-C`

> **Không làm:** đưa sổ cái chính lên chain (cần transaction đa dòng, truy vấn phức tạp, và không bên nào cần tin nhau tới mức đó — chưa kể VND không lên chain hợp pháp được), phát hành token, gamification bằng NFT.

| Mã | Hạng mục | Công cụ | Artifact phải tạo ra | Giá trị | Công sức |
|---|---|---|---|---|---|
| C-01 | **Neo Merkle root ra chuỗi công khai** | OpenTimestamps → Bitcoin (miễn phí, không cần ví) | Công bố root audit log mỗi giờ. **Mô hình đe dọa nó giải: `VD-S-10`** — chủ nền tảng hoặc admin cao nhất sửa sổ cái rồi sửa luôn audit log sẽ bị phát hiện chắc chắn. Dùng blockchain đúng chỗ nó mạnh: **chứng minh thời điểm tồn tại**, không phải lưu trữ | ⭐⭐⭐ | Thấp |
| C-02 | **Danh tính và uy tín khả chuyển** | W3C DID + Verifiable Credentials | Uy tín cấp dưới dạng VC ký bởi nền tảng, labeler tự giữ và mang sang nơi khác chứng minh được mà không cần hỏi lại. Giải bài data portability thật: uy tín 3 năm đang bị khóa trong `identity_db` | ⭐⭐⭐ | Cao |
| C-03 | Escrow smart contract — **nghiên cứu so sánh**, không thay thế | Solidity trên testnet | Bảng đối chiếu độ trễ xác nhận / chi phí gas thật / thông lượng / mô hình tin cậy. **Kết luận dự kiến: smart contract bỏ được niềm tin vào việc *giữ tiền* nhưng không bỏ được niềm tin vào việc *đánh giá nhãn* (vấn đề oracle) — nên lợi ích thực tế hạn chế.** Kết luận phủ định có bằng chứng là kết quả nghiên cứu hợp lệ | ⭐⭐ | Cao |

### M.4. Nhánh D — Đóng góp nghiên cứu · `NC-D`

Chọn **một** làm điểm nhấn. Đây là phần biến đồ án từ "xây được hệ thống" thành "có đóng góp".

| Mã | Hạng mục | Artifact phải tạo ra | Giá trị | Công sức |
|---|---|---|---|---|
| D-01 | **Redundancy thích ứng như bài toán dừng tối ưu** ⬅ khuyến nghị | Thay quy tắc thô của FQ-03 bằng quyết định tuần tự: *sau k nhãn, dựa trên hậu nghiệm Dawid–Skene, giá trị thông tin kỳ vọng của nhãn thứ k+1 có vượt chi phí biên 200đ không?* Chạy trên benchmark truth-inference công khai. **Kết quả cần có: đường cong "độ chính xác ↔ số nhãn phải mua" so với redundancy cố định n=3/5/7, quy ra % tiết kiệm chi phí**. **Đã làm** ([báo cáo](thi-nghiem/nc-d-01-redundancy-thich-ung.md)): 5 benchmark + mô phỏng, cùng độ chính xác cần ít hơn 29–76% nhãn so với cố định n; chính sách `posterior` / `voi` chọn bằng setting `quality.redundancy_policy` | ⭐⭐⭐ | Cao |
| D-02 | Peer prediction / Bayesian Truth Serum | Cơ chế trả thưởng khiến khai báo trung thực là chiến lược tối ưu, **không cần câu hỏi vàng** — tấn công đúng điểm yếu `VD-Q-01`. Mechanism design thật, nhưng khó đo hơn D-01 | ⭐⭐⭐ | Rất cao |
| D-03 | Cài Dawid–Skene + MACE từ đầu và đánh giá | So với majority vote trên benchmark công khai, đối chiếu kết quả đã công bố. Ít mới nhất nhưng chắc chắn ra kết quả | ⭐⭐ | TB |

### M.5. Nhánh E — Hạ tầng dữ liệu · `NC-E`

| Mã | Hạng mục | Artifact phải tạo ra | Giá trị | Công sức |
|---|---|---|---|---|
| E-01 | **TigerBeetle cho `ledger_db`** | Database chuyên dụng cho sổ cái ghi kép (hàng triệu transfer/giây, bản thân nó được kiểm thử bằng DST như `NC-B-04`). **Benchmark đối đầu Postgres trên chính kịch bản CPM tải cao** — lựa chọn công nghệ có lý do kỹ thuật thật, nối thẳng vào `VD-M-08` | ⭐⭐⭐ | Cao |
| E-02 | CDC thay outbox polling | Debezium đọc WAL Postgres → Kafka. **Chỉ đáng làm nếu có chương so sánh độ trễ hai cách cài đặt outbox** — thay công nghệ suông thì không | ⭐⭐ | Cao |
| E-03 | Audit log dạng cây Merkle | Nâng chuỗi băm thành cây Merkle; chứng minh "bút toán này có trong sổ tại thời điểm T" mà không lộ toàn bộ sổ. Là nền cho `NC-C-01` | ⭐⭐ | Thấp |

### M.6. Lộ trình gợi ý (~8–9 tháng)

| Tháng | Trọng tâm | Giao được |
|---|---|---|
| 1–2 | Lõi P0–P1: gán nhãn end-to-end + vòng tiền | Demo chạy được |
| 3 | P2–P3: cổng link + chất lượng | Ba kênh thu thập hoạt động |
| 4 | `NC-A` phần nền: A-01→A-03, A-06 | Một lệnh dựng cả hệ thống |
| 5 | `NC-B-01` + `NC-B-05` + `NC-B-06` | Chương "Tính đúng đắn" + biểu đồ hiệu năng |
| 6 | `NC-D-01` | Chương "Đóng góp" có số liệu |
| 7 | `NC-A` phần nâng cao: A-04, A-05, A-07, A-10 | Chương "Vận hành" + video canary rollback |
| 8 | `NC-B-03` + `NC-C-01` (+ `NC-E-03`) | Chương "CAP có thực nghiệm" + sổ cái chống sửa |
| 9 | Chọn **một**: `NC-B-04` · `NC-C-02` · `NC-C-03` · `NC-E-01` | Chương mở rộng |
| Cuối | P4–P6 còn lại, viết báo cáo, tập demo | |

Tháng 9 chọn **một**, không phải bốn. Đó là chỗ dễ tham nhất.

---

## N. Rủi ro của chính việc mở rộng — `VD-N`

| Mã | Vấn đề | Hậu quả | Hướng giải | Mức | Phase |
|---|---|---|---|---|---|
| N-01 | **Rộng mà không sâu**: dựng mười thứ, không đo được thứ nào | Hội đồng đào một mục là vỡ; công sức lớn nhưng không quy ra điểm | Mỗi hạng mục `NC` phải có artifact đo được. Thà ba chương có số liệu còn hơn mười mục "đã cài đặt X" | CHẶN | — |
| N-02 | Giảng viên hướng dẫn chưa duyệt phạm vi mở rộng | Làm xong nhưng không được tính vào đánh giá | Thống nhất phạm vi và tiêu chí đánh giá **trước tháng 3**, đặc biệt với `NC-D` vì nó đổi bản chất đồ án | CHẶN | — |
| N-03 | Hệ 17 service trên K8s vỡ đúng ngày bảo vệ | Mất điểm nặng ở phần khó cứu nhất | Kịch bản demo có dữ liệu mẫu sẵn; chạy thử toàn bộ ≥ 3 lần; **video dự phòng** cho phần rủi ro (canary rollback, chaos) | CHẶN | — |
| N-04 | Nhánh A hút hết thời gian, lõi nghiệp vụ chưa xong | Không có gì để demo — DevOps không có ứng dụng để vận hành | Không chạm K8s trước khi P0–P3 chạy được trên Docker Compose | CHẶN | — |
| N-05 | Chi phí cloud vượt khả năng sinh viên | Phải tắt cluster giữa chừng, mất dữ liệu thí nghiệm | k3s trên máy cá nhân hoặc một VPS rẻ; chỉ bật cloud vào đợt đo và đợt demo | CAO | — |
| N-06 | `NC-B-04` đòi trừu tượng hóa toàn bộ I/O sau interface | Phải viết lại service đã xong | Quyết định sớm: nếu định làm DST thì thiết kế `ledger-svc` với I/O sau interface **ngay từ đầu**; chỉ làm cho một service | CAO | — |
| N-07 | Quản lý khóa ký: Cosign, DID issuer, ví testnet | Lộ khóa = chữ ký mất giá trị, toàn bộ `NC-A-10` và `NC-C` vô nghĩa | Khóa trong Vault/KMS, không bao giờ commit; ví testnet không giữ tiền thật; ghi rõ quy trình xoay khóa | CAO | — |
| N-08 | **Benchmark tự chạy rất dễ ra số đẹp giả** | Kết luận sai, bị hỏi là sụp | Ghi rõ cấu hình phần cứng; chạy nhiều lần, **báo cáo phương sai không chỉ trung bình**; warm-up trước khi đo; công bố script để tái lập | CAO | — |
| N-09 | Học TLA+ mất 1–2 tuần trước khi ra kết quả đầu tiên | Trễ tiến độ tháng 5 | Bắt đầu học từ tháng 4, song song nhánh A; có `NC-B-02` làm phương án dự phòng | TB | — |
| N-10 | Service mesh + eBPF ngốn tài nguyên trên máy yếu | Không dev nổi | Linkerd thay Istio; bật mesh chỉ trong đợt đo, tắt lúc dev thường ngày | TB | — |

---

## Ba việc quan trọng nhất

Nếu chỉ làm được ba, làm ba việc này:

1. **`VD-M-01` + `VD-M-03`** — mọi ràng buộc escrow phải nằm **trong transaction của `ledger-svc`**. Đây là nơi chặn cuối cho mọi lỗi ở tầng trên.
2. **`VD-S-01` + `VD-S-02`** — nạp tiền chỉ ghi qua IPN có verify ngược số tiền. Sai chỗ này là ai cũng nạp tiền vô hạn.
3. **`VD-Q-01`** — đáp án câu vàng không bao giờ rời server. Toàn bộ mô hình kinh tế chống bot đứng trên đúng giả định này.

Và nếu chỉ làm được **ba hạng mục nâng cao**: `NC-B-06` (thí nghiệm hiệu năng có giả thuyết — rẻ nhất, thuyết phục nhất), `NC-B-01` (TLA+ — chứng minh tính đúng đắn), `NC-D-01` (redundancy thích ứng — đóng góp riêng có số liệu). Ba cái đó chứng minh ba thứ khác nhau mà hội đồng quan tâm: *hệ thống chạy được*, *hệ thống đúng*, *bạn có đóng góp*.
