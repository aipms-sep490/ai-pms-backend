# CÂU HỎI CẦN BỘ MÔN / HỘI ĐỒNG HỌC VỤ QUYẾT — ngày 30/10/2026

**Người nhận:** Trưởng bộ môn · Hội đồng học vụ · (mục `Q-T4` cần thêm bộ phận pháp chế)
**Người gửi:** nhóm phát triển AI-PMS

---

## Xin đọc hai mục đầu trước

Hệ thống đang chuẩn bị để **chấm điểm thật cho một kỳ**, không còn là bản chạy thử. Hai quyết định dưới đây **chặn lập trình viên viết dòng code đầu tiên** của phần rubric:

| | Câu hỏi | Chặn |
|---|---|---|
| **`Q-A10`** | Thang chất lượng dùng **4 mức hay 5 mức** | Thiết kế cơ sở dữ liệu. Làm trước rồi đổi sau là phải chuyển đổi dữ liệu |
| **`Q-N02`** | **Ai sở hữu và ai được phê duyệt** một bộ tiêu chí | Mô hình phân quyền. Hiện **một người tự công bố được** bộ tiêu chí dùng để chấm điểm thật |

Sáu mục còn lại cần trả lời trong 2–3 tuần tới, không chặn ngay.

**Một điều xin nói rõ về nhãn DEMO.** Hệ thống hiện gắn nhãn *DEMO* lên mọi màn hình có điểm, và điều đó là **đúng theo thiết kế**: phần mềm chặn cứng không cho coi điểm là chính thức khi chưa có **số văn bản phê duyệt**. Khi bộ môn ra văn bản, việc cần làm chỉ là **nhập số văn bản vào hệ thống** — một thao tác dữ liệu, không phải sửa phần mềm. Nhóm phát triển **sẽ không điền số văn bản giả** để nhãn đó mất đi.

---

## `Q-A10` · 🔴 CHẶN · Thang chất lượng 4 mức hay 5 mức?

### Câu hỏi

Mỗi tiêu chí trong bộ tiêu chí sẽ có bao nhiêu mức chất lượng được mô tả bằng lời?

### Vì sao nó chặn

Số mức quyết định cấu trúc bảng dữ liệu và cấu trúc phiếu chấm in ra. Nếu làm 4 mức rồi bộ môn chốt 5, phải chuyển đổi toàn bộ dữ liệu đã nhập và sửa lại phiếu. Đây là lý do duy nhất khiến chúng tôi phải hỏi trước thay vì làm trước.

### Các lựa chọn

| | Mô tả | Ưu | Nhược |
|---|---|---|---|
| **A. 4 mức** | Xuất sắc · Tốt · Cơ bản · Chưa đạt | Theo chuẩn AAC&U VALUE (bộ rubric được dùng rộng nhất trong giáo dục đại học Hoa Kỳ). Người chấm quyết nhanh hơn, ít do dự giữa các mức | Khoảng cách giữa "Tốt" và "Cơ bản" khá rộng |
| **B. 5 mức** | thêm một mức giữa | Phân biệt mịn hơn | Thực tế người chấm hay dồn vào mức giữa, làm mất tác dụng phân loại. Phiếu dài hơn 25% |

### Đề xuất của nhóm phát triển: **A — 4 mức**

Căn cứ: bộ rubric AAC&U Integrative Learning VALUE dùng 4 mức. Và phần mềm được thiết kế để **số mức là dữ liệu, không phải cấu trúc** — nếu sau một kỳ bộ môn thấy cần 5 mức thì đổi được bằng một dòng cấu hình, không phải sửa phần mềm. Nên chọn 4 bây giờ không phải là khóa cửa.

### Ai cần ký

Trưởng bộ môn (hoặc hội đồng học vụ nếu quyết định này áp cho nhiều bộ môn).

### Nếu trả lời muộn

BE vẫn làm trước được với mặc định 4 mức và **không merge**; FE cũng vậy. Nhưng cả hai bên không thể hoàn thành phần rubric, và bộ môn **không thể bắt đầu soạn nội dung 9 bộ tiêu chí** — việc này mất nhiều buổi, nên nó là phần dài nhất của cả lộ trình.

---

## `Q-N02` · 🔴 CHẶN · Ai sở hữu và ai phê duyệt một bộ tiêu chí?

### Câu hỏi

Ba câu hỏi nhỏ:

1. Bộ tiêu chí **phần CHUNG** của đồ án liên ngành (áp cho cả 3 ngành) thuộc bộ môn nào?
2. Người **soạn** và người **phê duyệt** có được là cùng một người không?
3. `ADMIN` (quản trị hệ thống) có được phê duyệt bộ tiêu chí không?

### Vì sao nó chặn — và đây là vấn đề thật, không phải thủ tục

Hiện tại hệ thống đi **trực tiếp** từ *bản nháp* sang *đã công bố*, bằng **một lời gọi của một người**. Nghĩa là:

> **Một người có thể tự soạn rồi tự công bố bộ tiêu chí dùng để chấm điểm thật cho sinh viên.** Không có người thứ hai xem lại, và nếu sinh viên khiếu nại thì **không có vết ai đã phê duyệt**.

Ở bản chạy thử thì tạm được. Khi chấm điểm thật thì không.

### Các lựa chọn

**Về câu 1 — bộ tiêu chí phần CHUNG:**

| | Mô tả | Nhược |
|---|---|---|
| **A. Bộ môn chủ trì sở hữu**, các bộ môn tham gia góp ý | Có một người chịu trách nhiệm cuối | Bộ môn tham gia có thể thấy bị áp đặt |
| **B. Phải cả ba bộ môn đồng ý** | Công bằng | Một bộ môn chậm là cả kỳ đứng |

**Về câu 2 — nguyên tắc bốn mắt:**

| | Mô tả |
|---|---|
| **A. Người phê duyệt phải khác người gửi** | Phần mềm sẽ từ chối nếu cùng một người |
| **B. Cho phép cùng người** | Giữ như hiện tại |

**Về câu 3 — quản trị hệ thống:**

| | Mô tả |
|---|---|
| **A. Không** | `ADMIN` quản tài khoản và hệ thống, không quyết nội dung học thuật |
| **B. Có** | `ADMIN` làm được mọi thứ |

### Đề xuất của nhóm phát triển

**1-A · 2-A · 3-A.**

Căn cứ cho **1-A**: phần mềm không cho phép hai mốc Review chạy song song, nên đồ án liên ngành buộc phải có một bộ môn điều phối. Cho mỗi bộ môn quyền phủ quyết sẽ làm cả kỳ dừng khi một bên chậm. Các bộ môn tham gia vẫn sở hữu trọn bộ tiêu chí **của ngành mình**.

Căn cứ cho **2-A**: đây là nguyên tắc bốn mắt thông thường với văn bản quyết định điểm. Nó cũng tạo ra vết phê duyệt để bảo vệ bộ môn khi có khiếu nại.

Căn cứ cho **3-A**: hệ thống đã có tiền lệ — quyền **công bố kết quả** đã được siết để quản trị viên đơn thuần không làm được. Làm khác đi ở chỗ này sẽ không nhất quán.

### Ai cần ký

Hội đồng học vụ (vì câu 1 liên quan nhiều bộ môn).

### Nếu trả lời muộn

Lập trình viên làm trước được nhưng không merge. Và trong thời gian đó, **rủi ro "một người tự công bố" vẫn còn nguyên** trên hệ thống.

---

## `Q-A9` · Ngưỡng đạt là bao nhiêu?

### Câu hỏi

Sinh viên phải đạt bao nhiêu điểm thì được coi là **qua** đồ án? Và có ngưỡng riêng cho từng thành phần không (ví dụ: phần bảo vệ phải ≥ 5 bất kể điểm hồ sơ cao đến đâu)?

### Vì sao cần

Phần mềm có chỗ khai ngưỡng đạt, hiện để trống. Không có ngưỡng thì hệ thống tính ra điểm nhưng **không kết luận được đạt hay không đạt**, và người làm học vụ phải tự đối chiếu bằng tay.

### Đề xuất của nhóm phát triển

**Chưa đề xuất.** Đây là quy định của trường, chúng tôi không có căn cứ nào để đề xuất một con số. Xin bộ môn cho biết quy định hiện hành.

Chỉ xin lưu ý một điều: nếu đặt **ngưỡng riêng cho từng thành phần**, xin nói rõ chuyện gì xảy ra khi tổng đạt mà một thành phần không đạt — đó là một quy tắc phần mềm phải làm đúng, không được đoán.

### Ai cần ký

Hội đồng học vụ / theo quy định trường.

---

## `Q-D13` · Giữ nguyên bộ tiêu chí Marketing 25/20/25/20/10?

### Câu hỏi

Bộ tiêu chí chấm hồ sơ ngành Marketing hiện có 5 tiêu chí với trọng số:

| Tiêu chí | Trọng số |
|---|---|
| Nghiên cứu thị trường & khách hàng | 25% |
| Phân khúc – mục tiêu – định vị | 20% |
| Chiến lược & triển khai | 25% |
| KPI & kiểm chứng | 20% |
| Liêm chính nghiên cứu & chất lượng hồ sơ | 10% |

Bộ này **kế thừa từ tài liệu v5.0 của dự án**, và ở tài liệu đó nó đang ở trạng thái *đề xuất*, chưa được bộ môn Marketing phê duyệt. Giữ nguyên, hay sửa?

### Đề xuất của nhóm phát triển

**Giữ nguyên.** Để bộ môn Marketing đối chiếu dễ với tài liệu đã đọc trước đó. Nếu bộ môn muốn sửa thì đây là lúc thuận lợi nhất — sau khi đã nhập vào hệ thống và dùng chấm thì sửa phải tạo phiên bản mới.

### Ai cần ký

Bộ môn Marketing.

---

## `Q-N09` · Bộ tiêu chí ngành Công nghệ thông tin và Thiết kế đồ họa

### Câu hỏi

Tài liệu v5.0 của dự án **không có** bộ tiêu chí cho hai ngành này. Nhóm phát triển đã đề xuất mới, dựa trên chuẩn ngoài:

- **Công nghệ thông tin:** neo vào ABET CAC Criterion 3 (bản 2025–2026), chuẩn kiểm định chương trình máy tính.
- **Thiết kế đồ họa:** neo vào NASAD BFA Communication Design (bản 2022), chuẩn kiểm định ngành thiết kế.

Chi tiết ở `RUBRIC_DESIGN_2026-10-30.md` §6.6.

Hai bộ môn có chấp nhận hướng này, và có muốn sửa nội dung tiêu chí không?

### Vì sao cần

Đây là **nội dung học thuật**, nhóm phát triển không có thẩm quyền chốt. Chúng tôi chỉ đề xuất một bản để bộ môn có cái sửa thay vì phải viết từ đầu.

### Đề xuất của nhóm phát triển

Trình bộ môn Công nghệ thông tin và bộ môn Thiết kế xem và sửa. Chúng tôi **không đề nghị giữ nguyên** — bộ môn hiểu ngành mình hơn chúng tôi.

### Ai cần ký

Bộ môn Công nghệ thông tin · Bộ môn Thiết kế.

---

## `Q-N11` · Ba tiêu chí mới do nhóm phát triển đề xuất thêm

### Câu hỏi

Khi soát lại bộ tiêu chí kế thừa từ v5.0, chúng tôi thấy nó đánh giá **sản phẩm** rất kỹ nhưng gần như không đánh giá ba năng lực mà chương trình liên ngành vốn phải dạy. Chúng tôi đề xuất thêm ba tiêu chí:

| Mã | Tiêu chí | Ở bộ nào | Trọng số | Căn cứ |
|---|---|---|---|---|
| `CMNC-COLL-06` | Phối hợp & giao tiếp liên ngành | Hồ sơ chung | 12% | Borrego & Newswander (2010) — *teamwork* và *communication* là 2 trong 5 nhóm kết quả học tập liên ngành, rút ra từ 129 đề xuất NSF IGERT. Và ABET outcome (3) và (5) |
| `INDD-CRIT-04` | Nhận thức giới hạn ngành mình | Vấn đáp cá nhân | 15% | Borrego & Newswander — *critical awareness* |
| `SED-PROF-03` | Trách nhiệm nghề nghiệp: pháp lý, đạo đức, giấy phép, dữ liệu cá nhân | Bảo vệ chuyên môn CNTT | 30% | ABET CAC Criterion 3 outcome **(4)** |

Thêm ba tiêu chí này nghĩa là **chia lại trọng số** các tiêu chí còn lại trong cùng bộ — bảng chia lại đã ghi rõ trong `RUBRIC_DESIGN_2026-10-30.md`.

### Vì sao cần

Nếu không có ba tiêu chí này thì một nhóm có thể đạt điểm cao mà **chưa từng chứng minh được họ phối hợp liên ngành thật** — ba sinh viên ba ngành làm ba phần rời rồi đóng gói chung cũng ra sản phẩm tốt. Đó chính là thứ một đồ án liên ngành cần phân biệt được.

### Đề xuất của nhóm phát triển

**Chấp nhận cả ba.** Nhưng xin bộ môn soát lại **trọng số** — con số chúng tôi đặt là để tổng vẫn bằng 100, không phải vì chúng tôi biết ba năng lực đó nặng bao nhiêu trong chương trình của trường.

### Ai cần ký

Hội đồng học vụ (vì `CMNC-COLL-06` và `INDD-CRIT-04` áp cho cả ba ngành).

---

## `Q-T4` · 🔴 PHÁP LÝ · Bảo vệ trực tuyến: ghi hình và lưu trữ

### Câu hỏi

Trường có tổ chức bảo vệ đồ án **trực tuyến** không? Nếu có thì:

1. Lấy sự đồng thuận của hội đồng và của sinh viên bằng hình thức nào, lưu ở đâu?
2. Bản ghi hình ghi âm lưu **bao lâu**, ai được truy cập?

### Vì sao cần — đây là tuân thủ pháp luật, không phải tính năng

**Thông tư 08/2021/TT-BGDĐT, Điều 9 khoản 1** quy định với hình thức bảo vệ trực tuyến:

- Hội đồng phải có **ít nhất 3 thành viên**;
- Phải có **sự đồng thuận của tất cả thành viên hội đồng và của sinh viên**;
- Phải **ghi hình, ghi âm đầy đủ và lưu trữ**.

**Hệ thống hiện không có chức năng ghi hình và lưu trữ bản ghi.** Nghĩa là nếu trường tổ chức bảo vệ trực tuyến trên hệ thống này ở trạng thái hiện tại thì **buổi bảo vệ đó không đáp ứng quy định**.

### Lựa chọn

| | Mô tả |
|---|---|
| **A. Không tổ chức bảo vệ trực tuyến kỳ này** | An toàn, không cần làm gì thêm |
| **B. Có tổ chức** | Phải làm chức năng ghi hình + lưu trữ + thu đồng thuận **trước** buổi bảo vệ đầu tiên. Cần quyết định thời hạn lưu và quyền truy cập, vì đó là dữ liệu cá nhân |

### Đề xuất của nhóm phát triển

Nếu chưa có quyết định về thời hạn lưu trữ và quyền truy cập thì chọn **A** cho kỳ này. Chúng tôi không đề xuất tổ chức trực tuyến khi chưa có chức năng ghi hình — rủi ro là buổi bảo vệ bị coi là không hợp lệ.

### Ai cần ký

Hội đồng học vụ **cùng bộ phận pháp chế** (vì liên quan dữ liệu cá nhân).

---

## `Q-APPR` · Số văn bản phê duyệt để bỏ nhãn DEMO

### Câu hỏi

Khi bộ tiêu chí và cấu trúc điểm được phê duyệt, xin cho nhóm phát triển **số hiệu văn bản** (ví dụ: *Quyết định số 123/QĐ-ĐHX ngày …*).

### Vì sao cần

Hệ thống chặn cứng: không có số văn bản thì mọi màn hình có điểm **vẫn gắn nhãn DEMO**, và điểm không được công bố là chính thức. Đây là thiết kế có chủ ý, để không ai vô tình công bố một bảng điểm chưa được phê duyệt.

### Việc phải làm khi có văn bản

Nhập số văn bản vào hệ thống. **Đó là tất cả.** Không cần sửa phần mềm, không cần cập nhật phiên bản, không cần nhập lại điểm. Nhãn DEMO tự mất.

### Ai cần ký

Người có thẩm quyền ra văn bản.

---

## Tóm tắt

| Mục | Chặn? | Ai ký | Đề xuất của nhóm phát triển |
|---|---|---|---|
| `Q-A10` 4 hay 5 mức | 🔴 **có** | Trưởng bộ môn | **4 mức** |
| `Q-N02` sở hữu & phê duyệt rubric | 🔴 **có** | Hội đồng học vụ | bộ môn chủ trì sở hữu phần CHUNG · bốn mắt · ADMIN không duyệt |
| `Q-A9` ngưỡng đạt | không | theo quy định trường | **chưa đề xuất** |
| `Q-D13` rubric Marketing | không | BM Marketing | giữ nguyên |
| `Q-N09` rubric CNTT & Thiết kế | không | BM CNTT · BM Thiết kế | trình hai bộ môn sửa |
| `Q-N11` ba tiêu chí mới | không | Hội đồng học vụ | chấp nhận, soát lại trọng số |
| `Q-T4` ghi hình bảo vệ trực tuyến | 🔴 **pháp lý** | Hội đồng + pháp chế | không tổ chức trực tuyến nếu chưa đủ điều kiện |
| `Q-APPR` số văn bản phê duyệt | không | người có thẩm quyền | nhập khi có |

---

**Tài liệu đọc kèm:** `RUBRIC_DESIGN_2026-10-30.md` — thiết kế đầy đủ bộ tiêu chí, trọng số, phân công giảng viên và trình tự chấm. Trạng thái: **đề xuất, chờ phê duyệt**.
