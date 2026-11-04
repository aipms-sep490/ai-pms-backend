namespace AIPMS.Infrastructure.Services.Projects;

internal static class WorkflowAssistanceLabels
{
    internal static (string Title, string Owner, string Target)? Action(string code) => code switch
    {
        "submit_project" => ("Nộp đề xuất đồ án", "Trưởng nhóm", "overview"),
        "resubmit_project" => ("Nộp lại đề xuất sau chỉnh sửa", "Trưởng nhóm", "overview"),
        "edit_project_draft" => ("Bổ sung nội dung đề xuất", "Trưởng nhóm", "overview"),
        "send_supervisor_request" => ("Mời giảng viên hướng dẫn", "Trưởng nhóm", "supervisor"),
        "create_task" => ("Tạo công việc", "Trưởng nhóm hoặc GVHD chính", "tasks"),
        "create_milestone" => ("Tạo mốc tiến độ", "Trưởng nhóm hoặc GVHD chính", "milestones"),
        "create_progress_report" => ("Viết báo cáo tiến độ", "Thành viên nhóm", "reports"),
        "schedule_meeting" => ("Lên lịch trao đổi", "Trưởng nhóm hoặc GVHD chính", "meetings"),
        "manage_evaluation_scheme" => ("Cấu hình phương án chấm", "Khoa chủ trì", "evaluation-schemes"),
        "manage_evaluators" => ("Phân công người chấm", "Khoa phụ trách cấu phần", "evaluators"),
        "publish_project_result" => ("Công bố kết quả", "Khoa chủ trì", "result"),
        _ => null
    };

    // Numeric suffixes are internal references, never user-facing labels.
    internal static string Reason(string code) => code.Split(':')[0] switch
    {
        "PROJECT_NOT_ACTIVE" => "Đồ án không ở giai đoạn đang thực hiện. Kiểm tra trạng thái với GVHD/Khoa.",
        "PROJECT_NOT_FINAL_SUBMISSION" => "Đồ án chưa ở giai đoạn bàn giao cuối kỳ.",
        "PROJECT_MANAGER_REQUIRED" => "Thao tác cần trưởng nhóm hoặc GVHD chính thực hiện.",
        "ACTIVE_TEAM_MEMBER_REQUIRED" or "NOT_PROJECT_MEMBER" => "Cần là thành viên đang hoạt động của nhóm.",
        "LEADER_REQUIRED" => "Chỉ trưởng nhóm hiện tại được nộp gói bàn giao.",
        "TEAM_LEADER_REQUIRED" => "Thao tác cần trưởng nhóm hiện tại thực hiện.",
        "INVALID_PROJECT_STATE" or "PROJECT_NOT_EDITABLE" => "Giai đoạn hiện tại không cho phép thao tác này. Kiểm tra trạng thái tại tổng quan.",
        "PROPOSAL_FIELDS_REQUIRED" => "Cần bổ sung tên đề tài, vấn đề, mục tiêu và sản phẩm dự kiến.",
        "PROPOSAL_TAGS_REQUIRED" => "Cần chọn lĩnh vực, công nghệ và từ khóa cho đề xuất.",
        "PROJECT_MAJORS_REQUIRED" or "PROJECT_MAJOR_SCOPE_MISMATCH" => "Kiểm tra ngành của đề xuất phải khớp phạm vi ngành đã đăng ký của nhóm.",
        "SUPERVISOR_NOT_CONFIRMED" => "Chưa có GVHD chính xác nhận. Trưởng nhóm kiểm tra lời mời và giảng viên phản hồi.",
        "MENTOR_NOT_CONFIRMED" => "Có ngành chưa được mentor xác nhận. Xem ngành còn thiếu trong trang giảng viên hướng dẫn.",
        "SUPERVISOR_SELECTION_STATE_INVALID" => "Hiện không có vị trí giảng viên còn mở phù hợp trạng thái đồ án.",
        "SUPERVISOR_SELECTION_POLICY_UNAVAILABLE" => "Chính sách mời giảng viên chưa đủ cấu hình. Liên hệ Khoa.",
        "WORKING_AGREEMENT_MISSING" => "Chưa có cam kết nhóm đang áp dụng; trưởng nhóm/GVHD cần chuẩn bị và công bố.",
        "WORKING_AGREEMENT_ROSTER_CHANGED" => "Thành viên nhóm đã thay đổi; cần cập nhật cam kết cho danh sách hiện tại.",
        "WORKING_AGREEMENT_INCOMPLETE" => "Chưa đủ thành viên hiện tại xác nhận cam kết; từng thành viên cần đăng nhập để đồng ý.",
        "WORKING_AGREEMENT_POLICY_NOT_LOCKED" => "Chính sách cam kết chưa khóa; Khoa cần kiểm tra cấu hình kỳ.",
        "LEAD_DEPARTMENT_REQUIRED" or "LEAD_DEPARTMENT_REQUIRED_FOR_PUBLICATION" => "Khoa chủ trì cần thực hiện thao tác này.",
        "ACADEMIC_COMPONENT_SCOPE_REQUIRED" => "Cần quyền quản lý cấu phần thuộc khoa phụ trách.",
        "ACADEMIC_SCOPE_UNKNOWN_OR_INACTIVE" or "ACADEMIC_SCOPE_INACTIVE" => "Phạm vi học vụ chưa hợp lệ hoặc đã ngừng hoạt động. Liên hệ Khoa.",
        "FINAL_PACKAGE_REQUIRED" => "Cần gói bàn giao cuối kỳ đã khóa trước khi tiếp tục.",
        "PUBLISHED_SCHEME_REQUIRED" => "Cần phương án chấm đã công bố đúng kỳ.",
        "EVALUATION_WINDOW_CLOSED" => "Chưa có một đợt chấm đang mở phù hợp. Khoa cần kiểm tra lịch.",
        "FINAL_SUBMISSION_PERIOD_REQUIRED" => "Chọn đúng đợt bàn giao cuối kỳ trong bản nháp.",
        "WINDOW_NOT_STARTED" => "Đợt bàn giao chưa đến thời gian mở.",
        "WINDOW_CLOSED" => "Đợt bàn giao đã hết hạn. Liên hệ Khoa để được hướng dẫn.",
        "SEMESTER_CLOSED" or "PERIOD_INACTIVE" => "Kỳ học hoặc đợt áp dụng không còn hoạt động.",
        "AMBIGUOUS_FINAL_SUBMISSION_WINDOW" => "Có nhiều đợt bàn giao cùng mở; Khoa cần kiểm tra cấu hình.",
        "DRAFT_REQUIRED" => "Tạo bản nháp gói bàn giao và chọn phiên bản hạng mục trước.",
        "EMPTY_PACKAGE" => "Gói bàn giao chưa chọn phiên bản hạng mục nào.",
        "REQUIREMENTS_NOT_CONFIGURED" => "Khoa chưa cấu hình danh sách hạng mục bắt buộc.",
        "REQUIRED_DELIVERABLE_MISSING" => "Có hạng mục bắt buộc chưa đủ phiên bản hoặc tệp hợp lệ; xem danh sách bên dưới.",
        "VERSION_INELIGIBLE" => "Có phiên bản chưa nộp/được chấp nhận hoặc tệp không hợp lệ. Kiểm tra ở gói bàn giao.",
        "FILE_CONTENT_INVALID" => "Nội dung tệp không khớp metadata lưu trữ; kiểm tra và tải lại tệp ở hạng mục.",
        "VERSION_NOT_IN_PROJECT" => "Có phiên bản không thuộc đồ án hiện tại; chọn lại trong gói bàn giao.",
        "DUPLICATE_DELIVERABLE_VERSION" => "Một hạng mục đang được chọn nhiều phiên bản; giữ đúng một phiên bản.",
        "ALREADY_SUBMITTED" => "Đồ án đã nộp gói bàn giao; xem gói đã khóa.",
        "DEMO_SCHEME_NOT_OFFICIAL" => "Phương án DEMO chưa được dùng để công bố điểm chính thức.",
        "MISSING_EVALUATOR" => "Có cấu phần chưa được phân công người chấm; Khoa kiểm tra phương án chấm.",
        "RESULT_NOT_READY" => "Kết quả chưa sẵn sàng; Khoa kiểm tra phương án và các lượt chấm.",
        "RESULT_PUBLICATION_FORBIDDEN" => "Tài khoản hiện tại chưa có quyền công bố kết quả.",
        _ => "Còn điều kiện nghiệp vụ chưa đáp ứng. Mở trang thao tác để xem chi tiết hiện hành hoặc liên hệ người phụ trách."
    };

    internal static string Stage(string value) => value switch
    {
        "ACTIVE" => "Đang thực hiện", "FINAL_SUBMISSION" => "Bàn giao cuối kỳ",
        "DRAFT" => "Đang soạn đề xuất", "SUBMITTED" => "Đã nộp đề xuất", "UNDER_REVIEW" => "Đang được Khoa xét duyệt",
        "REVISION_REQUIRED" => "Cần chỉnh sửa đề xuất", "APPROVED" => "Đề xuất đã được duyệt", "REJECTED" => "Đề xuất bị từ chối",
        "COMPLETED" => "Đã hoàn thành", "ARCHIVED" => "Đã lưu trữ", "CANCELLED" => "Đã hủy",
        _ => "Cần kiểm tra giai đoạn tại trang tổng quan"
    };
}
