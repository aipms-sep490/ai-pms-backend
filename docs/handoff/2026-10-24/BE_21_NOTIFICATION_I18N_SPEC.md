# BẢNG ĐẶC TẢ KHÓA I18N CHO NOTIFICATION (BE-21 / UX-03)

Tài liệu này xác định hợp đồng dữ liệu giữa Backend và Frontend đối với toàn bộ các loại thông báo (`notificationType` -> `titleKey` + `params`).

## 1. Cấu trúc Payload NotificationDto

```json
{
  "id": 123,
  "notificationType": "TASK_DEADLINE_REMINDER",
  "titleKey": "notification.task.deadlineReminder",
  "params": {
    "entityType": "TASK",
    "entityId": 456,
    "rawContent": "Task 'Finalize Architecture' is due in 24 hours."
  },
  "title": "A task is due soon",
  "content": "Task 'Finalize Architecture' is due in 24 hours.",
  "relatedEntityType": "TASK",
  "relatedEntityId": 456,
  "createdAt": "2026-10-24T08:00:00Z",
  "isRead": false,
  "readAt": null
}
```

## 2. Bảng Tra Cứu Notification Type -> Title Key

| STT | NotificationType | TitleKey | Mô tả / Ngữ cảnh |
|---|---|---|---|
| 1 | `TEAM_INVITATION_SENT` | `notification.teamInvitation.sent` | Lời mời gia nhập nhóm đồ án gửi tới sinh viên |
| 2 | `TEAM_INVITATION_ACCEPTED` | `notification.teamInvitation.accepted` | Sinh viên đã đồng ý vào nhóm |
| 3 | `TEAM_INVITATION_REJECTED` | `notification.teamInvitation.rejected` | Sinh viên từ chối lời mời vào nhóm |
| 4 | `TEAM_INVITATION_CANCELLED` | `notification.teamInvitation.cancelled` | Lời mời nhóm đã bị hủy |
| 5 | `SUPERVISOR_REQUEST_SENT` | `notification.supervisorRequest.sent` | Nhóm gửi yêu cầu mời GVHD/Mentor |
| 6 | `SUPERVISOR_REQUEST_ACCEPTED` | `notification.supervisorRequest.accepted` | GVHD/Mentor chấp thuận hướng dẫn |
| 7 | `SUPERVISOR_REQUEST_REJECTED` | `notification.supervisorRequest.rejected` | GVHD/Mentor từ chối hướng dẫn |
| 8 | `SUPERVISOR_REQUEST_CANCELLED` | `notification.supervisorRequest.cancelled` | Yêu cầu mời GVHD đã bị hủy |
| 9 | `SUPERVISOR_REPLACED` | `notification.supervisor.replaced` | Thay đổi / phân công lại GVHD |
| 10 | `TEAM_LEADER_CHANGE_REQUESTED` | `notification.teamLeaderChange.requested` | Yêu cầu chuyển giao quyền trưởng nhóm |
| 11 | `TEAM_LEADER_CHANGE_APPROVED` | `notification.teamLeaderChange.approved` | Yêu cầu đổi trưởng nhóm được duyệt |
| 12 | `TEAM_LEADER_CHANGE_REJECTED` | `notification.teamLeaderChange.rejected` | Yêu cầu đổi trưởng nhóm bị từ chối |
| 13 | `MEETING_SCHEDULED` | `notification.meeting.scheduled` | Lịch họp mới được thiết lập |
| 14 | `PROGRESS_REPORT_SUBMITTED` | `notification.progressReport.submitted` | Báo cáo tiến độ chu kỳ đã nộp |
| 15 | `STUDENT_RESULT_PUBLISHED` | `notification.studentResult.published` | Kết quả điểm cá nhân đã công bố |
| 16 | `PROJECT_RESULT_PUBLISHED` | `notification.projectResult.published` | Kết quả điểm đồ án chung đã công bố |
| 17 | `PROJECT_APPROVED` | `notification.project.approved` | Đề cương đồ án được phê duyệt |
| 18 | `PROJECT_REJECTED` | `notification.project.rejected` | Đề cương đồ án bị từ chối |
| 19 | `PROJECT_REVISION_REQUESTED` | `notification.project.revisionRequested` | Đề cương đồ án cần chỉnh sửa |
| 20 | `SUPERVISOR_FEEDBACK_ADDED` | `notification.supervisorFeedback.added` | GVHD thêm phản hồi/nhận xét |
| 21 | `EVALUATION_FINALIZED` | `notification.evaluation.finalized` | Điểm đánh giá hội đồng đã chốt |
| 22 | `EVALUATOR_ASSIGNED` | `notification.evaluator.assigned` | Được phân công chấm đồ án |
| 23 | `FINAL_SUBMISSION_LOCKED` | `notification.finalSubmission.locked` | Gói bàn giao cuối cùng đã khóa |
| 24 | `APPEAL_SUBMITTED` | `notification.appeal.submitted` | Đơn phúc khảo được nộp |
| 25 | `APPEAL_DECIDED` | `notification.appeal.decided` | Quyết định phúc khảo đã ban hành |
| 26 | `APPEAL_STATUS_CHANGED` | `notification.appeal.statusChanged` | Trạng thái phúc khảo thay đổi |
| 27 | `REVIEW_GATE_DECISION` | `notification.reviewGate.decision` | Quyết định cổng thẩm định bộ môn |
| 28 | `INTERDISCIPLINARY_REVIEW_DECISION` | `notification.interdisciplinaryReview.decision` | Quyết định thẩm định liên ngành |
| 29 | `DEFENSE_READINESS_CHANGED` | `notification.defenseReadiness.changed` | Cập nhật điều kiện tham gia bảo vệ |
| 30 | `TASK_DEADLINE_REMINDER` | `notification.task.deadlineReminder` | Nhắc nhở hạn chót công việc |
| 31 | `DELIVERABLE_DEADLINE_REMINDER` | `notification.deliverable.deadlineReminder` | Nhắc nhở hạn nộp tài liệu/bàn giao |
| 32 | `DEADLINE_REMINDER` | `notification.deadline.reminder` | Nhắc nhở hạn chót chung |
