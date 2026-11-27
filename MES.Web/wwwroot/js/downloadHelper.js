// Helper để download file từ base64 bytes (dùng cho export Excel từ Blazor)
window.downloadFileFromBytes = function (fileName, contentType, base64) {
    const link = document.createElement('a');
    link.href = `data:${contentType};base64,${base64}`;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

// [RPT-TAB-MOI 2026-11-27] Đổi địa chỉ đang hiện (không tải lại trang, không thêm bước Back) — WTS Report ghi kỳ / người đang xem lên địa chỉ để F5 / gửi
//   địa chỉ mở lại đúng màn. Giữ history.state của Blazor (không đè null). Nguồn: HÙNG 27/11 06:33 + 06:36 "bạn sửa đi".
window.mesReplaceUrl = function (url) {
    try { history.replaceState(history.state, '', url); } catch (e) { }
};
